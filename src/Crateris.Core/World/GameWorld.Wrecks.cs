// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>Why a revival was refused (none: the wreck flies on).</summary>
public enum RescueRefusal : byte
{
    None,

    /// <summary>No such player in the game now (or the rescuer themself).</summary>
    NoOne,

    /// <summary>The ship isn't a wreck: never destroyed, revived already, or still in the battle it was destroyed in.</summary>
    NotAWreck,

    /// <summary>The rescuer isn't flying free: in a battle, a talk, orbit or docked, entering a star, or a wreck itself.</summary>
    Busy,

    /// <summary>The rescuer isn't at the wreck: not in its place, or not within reach of it.</summary>
    NotHere,

    /// <summary>No crew to give (less than 1).</summary>
    Nothing,

    /// <summary>The rescuer must keep at least 1 crew (decision 8).</summary>
    NotEnough,

    /// <summary>More crew than the wreck holds.</summary>
    NoRoom,

    /// <summary>The game waits for a story talk.</summary>
    Paused,

    /// <summary>The game is over.</summary>
    GameOver,
}

/// <summary>A wreck revived: who gave it how much crew (<see cref="GameWorld.TakeRevivals"/>).</summary>
public sealed record Revival(string Rescuer, string Wreck, int Crew);

// Wrecks, revival and game over (stage 4a item 7).
public sealed partial class GameWorld
{
    private readonly List<(string Rescuer, string Wreck)> _rescueOffers = new();
    private readonly List<Revival> _revivals = new();
    // (rescuer, wreck): the rescuer was last seen flying in the wreck's place out of its reach.
    private readonly HashSet<(string Rescuer, string Wreck)> _seenOutOfWreck = new();

    /// <summary>
    /// The wrecks in space (decision 7): every present player's ship
    /// destroyed in a battle that's over, at that battle's place. A wreck
    /// doesn't fly, land, talk, join battles, give or take gifts or drop
    /// cargo; it keeps its cargo, fuel and modules, and is saved. An away
    /// player's wreck waits in the save, as any away ship does.
    /// </summary>
    public IReadOnlyList<Player> Wrecks => _players.Where(IsWreck).ToList();

    private static bool IsWreck(Player p) => p.Present && p.Ship.Wreck;

    /// <summary>
    /// The wrecks a player could revive now (<see cref="Revive"/>): their
    /// ship flying free with more than 1 crew, in the wreck's place and
    /// within reach of it (the battle markers' reach,
    /// <see cref="GameSettings.BattleMarkerReach"/>). Empty if none.
    /// </summary>
    public IReadOnlyList<Player> RescuesFor(string id)
    {
        if (GameOver || StoryPaused || Find(id) is not { } rescuer || !Flying(rescuer) || rescuer.Ship.Hold.Crew < 2)
            return Array.Empty<Player>();
        return _players.Where(w => w != rescuer && IsWreck(w) && AtWreck(rescuer.Ship, w.Ship)).ToList();
    }

    /// <summary>
    /// The rescuers who flew into a wreck since the last call (from outside
    /// its reach to inside, as a battle's marker is joined), each stopped
    /// there: item 11 tells them, and item 12 opens the rescue screen, from
    /// which the rescuer chooses how much crew to give (<see cref="Revive"/>).
    /// </summary>
    public List<(string Rescuer, string Wreck)> TakeRescueOffers()
    {
        var all = _rescueOffers.ToList();
        _rescueOffers.Clear();
        return all;
    }

    /// <summary>The wrecks revived since the last call, to tell the rescuer and the wreck's player.</summary>
    public List<Revival> TakeRevivals()
    {
        var all = _revivals.ToList();
        _revivals.Clear();
        return all;
    }

    /// <summary>
    /// A rescuer gives a wreck crew (decision 8): any player flying free (not
    /// in a battle, a talk, orbit, docked or entering a star, and not a
    /// wreck) at the wreck (<see cref="RescuesFor"/>), giving 1 crew or more,
    /// up to what the wreck holds (its crew pods or kit,
    /// <see cref="ShipHold.CrewCapacity"/>) and keeping at least 1 of their
    /// own. The wreck flies on with that crew, from its place, with its
    /// cargo, fuel and modules, and in a system the escape counter, as a
    /// ship leaving a battle has. Refused with a reason, changing nothing.
    /// </summary>
    public RescueRefusal Revive(string rescuerId, string wreckId, int crew)
    {
        if (GameOver)
            return RescueRefusal.GameOver;
        if (StoryPaused)
            return RescueRefusal.Paused;
        if (rescuerId == wreckId || Find(rescuerId) is not { Present: true } rescuer || Find(wreckId) is not { Present: true } wreck)
            return RescueRefusal.NoOne;
        if (!IsWreck(wreck))
            return RescueRefusal.NotAWreck;
        if (!Flying(rescuer))
            return RescueRefusal.Busy;
        if (!AtWreck(rescuer.Ship, wreck.Ship))
            return RescueRefusal.NotHere;
        if (crew < 1)
            return RescueRefusal.Nothing;
        if (crew > rescuer.Ship.Hold.Crew - 1)
            return RescueRefusal.NotEnough;
        if (crew > wreck.Ship.Hold.CrewCapacity)
            return RescueRefusal.NoRoom;
        rescuer.Ship.Hold.SetCrew(rescuer.Ship.Hold.Crew - crew);
        var ship = wreck.Ship;
        ship.Hold.SetCrew(crew);
        ship.Destroyed = false;
        ship.Flight.Stop();
        ship.Ip?.Stop();
        BackInFlight(ship);
        _seenOutOfWreck.RemoveWhere(t => t.Wreck == wreckId);
        _revivals.Add(new Revival(rescuerId, wreckId, crew));
        return RescueRefusal.None;
    }

    // A wreck's place: where it waits, as a battle's place is given.
    private static BattlePlace PlaceOf(PlayerShip wreck) => wreck.Ip is { } ip
        ? new BattlePlace(wreck.SystemStar, ip.InnerPlanet, ip.Location.X, ip.Location.Y)
        : new BattlePlace(null, null, wreck.Flight.LogX, wreck.Flight.LogY);

    private bool AtWreck(PlayerShip rescuer, PlayerShip wreck)
    {
        var place = PlaceOf(wreck);
        return InPlace(place, rescuer) && InReach(place, rescuer);
    }

    /// <summary>
    /// After every ship's flight and the battles' joining: a ship that may
    /// rescue (flying free, with more than 1 crew) flying into a wreck, from
    /// outside its reach to inside, stops there and is offered the rescue
    /// (<see cref="TakeRescueOffers"/>). As with a battle's marker, a ship
    /// first seen inside its reach (one back in flight there from a battle,
    /// or the wreck put by it) must fly out of it and back.
    /// </summary>
    private void FlyIntoWrecks()
    {
        var wrecks = _players.Where(IsWreck).ToList();
        if (wrecks.Count == 0)
        {
            _seenOutOfWreck.Clear();
            return;
        }
        foreach (var player in _players)
        {
            bool flying = Flying(player), offered = false;
            foreach (var wreck in wrecks)
            {
                if (wreck == player)
                    continue;
                var key = (player.Id, wreck.Id);
                var place = PlaceOf(wreck.Ship);
                if (!flying || !InPlace(place, player.Ship))
                {
                    _seenOutOfWreck.Remove(key);
                    continue;
                }
                if (!InReach(place, player.Ship))
                {
                    _seenOutOfWreck.Add(key);
                    continue;
                }
                // Into several at once (a battle's wrecks lie together): offered the first only.
                if (!_seenOutOfWreck.Remove(key) || offered || player.Ship.Hold.Crew < 2)
                    continue;
                player.Ship.Flight.Stop();
                player.Ship.Ip?.Stop();
                _rescueOffers.Add(key);
                offered = true;
            }
        }
        _seenOutOfWreck.RemoveWhere(t => !wrecks.Any(w => w.Id == t.Wreck));
    }

    // A ship destroyed in a battle, at the battle's end: a wreck at the
    // battle's place (decision 7), where the battle's starter was as it
    // started (its marker), at rest, as a guest is put at the host's place;
    // in hyperspace on the star it had just left there, if any.
    private void Wreck(WorldBattle wb, PlayerShip ship)
    {
        ship.Destroyed = true;
        ship.Hold.SetCrew(0);
        ship.Flight.Stop();
        if (wb.Place.InHyperspace)
        {
            ship.Flight.LogX = wb.Place.X;
            ship.Flight.LogY = wb.Place.Y;
            ship.Flight.StarJustLeft = wb.PlaceStarJustLeft;
        }
        else
            ship.Ip = IpFlight.Restore(Systems![wb.Place.Star!.Value], wb.PlaceIp!, ship.Modules.Thrusters, ship.Modules.Jets, Shapes!);
    }
}
