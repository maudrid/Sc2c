// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

public enum ShipKind
{
    Flagship,
    Escort,
}

/// <summary>What a player is doing (each runs on the host at its own frame rate).</summary>
public enum Activity
{
    /// <summary>Flying in hyperspace, 24 frames a second.</summary>
    Hyperspace,
    /// <summary>Flying in a system's outer view, 30 frames a second.</summary>
    OuterSystem,
    /// <summary>Flying in a planet's inner system, 30 frames a second.</summary>
    InnerSystem,
    /// <summary>In orbit around a planet or moon; the ship doesn't move.</summary>
    Orbit,
    /// <summary>In orbit, with its lander on the world's surface, 35 frames a second.</summary>
    Planetside,
    /// <summary>
    /// In a talk (or between the talks of a visit, as time passes): the
    /// ship waits in its system where it touched the world.
    /// </summary>
    Talk,
    /// <summary>Docked at the allied starbase, at its menu (outfitting and the shipyard are the player's own screens there).</summary>
    Starbase,
    /// <summary>
    /// In a battle (stage 4a item 6), 24 frames a second: fighting, running
    /// away, or, its ship destroyed, watching it to its end. The ship waits
    /// stopped where it joined, in hyperspace or its system (the battle's
    /// marker), and is back there when it leaves.
    /// </summary>
    Battle,
}

/// <summary>A player's ship. The host flies the flagship; guests captain escorts.</summary>
public sealed class PlayerShip
{
    public ShipKind Kind { get; }
    public ShipLoadout Loadout { get; }

    /// <summary>Crew, landers, minerals and bio data aboard, and the lander's upgrades.</summary>
    public ShipHold Hold { get; }

    /// <summary>
    /// The ship's name (ShipName), or null for the flagship's UQM name
    /// (<see cref="ShipNames"/>). An escort's is set when it's made.
    /// </summary>
    public string? Name { get; internal set; }

    /// <summary>
    /// Flight in hyperspace. While in a system it keeps the hyperspace
    /// position the ship returns to: the star's (ExploreSolarSys).
    /// </summary>
    public HyperFlight Flight { get; }

    /// <summary>The star (map number) whose system the ship is in, or null in hyperspace.</summary>
    public int? SystemStar { get; internal set; }

    /// <summary>Flight inside that system, or null in hyperspace.</summary>
    public IpFlight? Ip { get; internal set; }

    /// <summary>
    /// While the ship's entry into a star plays (<see cref="StarEntry"/>):
    /// where and how it touched the star. Its system flight waits until
    /// the entry is over. Not saved: a game loaded mid-entry starts in the
    /// system.
    /// </summary>
    public StarEntryState? Entry { get; internal set; }

    /// <summary>
    /// The last world frame whose input is ignored after an entry into a
    /// star: input for frames up to the entry's end plus the input delay
    /// was read while the entry was still playing on its player's screen.
    /// </summary>
    public long InputHeldUntil { get; internal set; } = -1;

    /// <summary>In orbit: the world's report, scan seeds and nodes (from <see cref="IpFlight.OrbitData"/>).</summary>
    public OrbitData? Orbit => Ip?.OrbitData;

    /// <summary>The world orbited, or null when not in orbit.</summary>
    public WorldId? OrbitedWorld => Ip?.Orbit is { } o ? new WorldId(SystemStar!.Value, o.Planet, o.Moon) : null;

    /// <summary>The ship's lander on the orbited world's surface, while it's there (not saved: a save keeps it in orbit).</summary>
    public Planetside.Lander? Lander { get; internal set; }

    /// <summary>The visit to Earth's starbase this ship is on, while it's on one (not saved).</summary>
    public StarbaseVisit? Visit { get; internal set; }

    /// <summary>The battle this ship is in, fighting or watching (not saved: decision 14).</summary>
    public WorldBattle? Battle { get; internal set; }

    /// <summary>
    /// Destroyed in a battle: its crew ran out. While the battle goes on
    /// its player watches it (<see cref="Battle"/> still set); once it's
    /// over the ship is a wreck at the battle's place (stage 4a item 7,
    /// decision 7) with no crew, which doesn't fly, land, talk, join
    /// battles, give or take gifts or drop cargo, and keeps its cargo, fuel
    /// and modules until a friend revives it (<see cref="GameWorld.Revive"/>).
    /// Saved as a wreck (<see cref="HostSave"/>, version 10), as is a ship
    /// destroyed in a battle still running. Its activity is its flight's
    /// (the snapshot says it's a wreck, <see cref="ShipSnapshot.Wrecked"/>).
    /// The flagship destroyed is the game over (<see cref="GameWorld.GameOver"/>).
    /// </summary>
    public bool Destroyed { get; internal set; }

    /// <summary>
    /// A wreck (stage 4a item 7): destroyed in a battle that's over. Never
    /// the flagship, whose loss is the game over.
    /// </summary>
    public bool Wreck => Destroyed && Battle is null && Kind != ShipKind.Flagship;

    public Activity Activity => Battle is not null ? Activity.Battle : Visit is { AtMenu: true } ? Activity.Starbase : Visit is not null ? Activity.Talk : Ip switch
    {
        null => Activity.Hyperspace,
        { Orbit: not null } when Lander is not null => Activity.Planetside,
        { Orbit: not null } => Activity.Orbit,
        { InnerPlanet: not null } => Activity.InnerSystem,
        _ => Activity.OuterSystem,
    };

    /// <summary>
    /// The ship's slots (sis.h's drive, jet and module slots), which
    /// outfitting changes: its drive, its crew, storage and fuel capacities
    /// follow them.
    /// </summary>
    public ShipModules Modules { get; }

    /// <param name="modules">Its slots; the loadout's (<see cref="ShipModules.FromLoadout"/>) if not given.</param>
    public PlayerShip(ShipKind kind, ShipLoadout loadout, ShipModules? modules = null)
    {
        Kind = kind;
        Loadout = loadout;
        Modules = modules ?? ShipModules.FromLoadout(kind, loadout);
        // A flagship holds crew in its crew pods only, as UQM's does (its
        // battle description counts nothing else, SisLoadout.From).
        if (kind == ShipKind.Flagship && Modules.KitCrew != 0)
            throw new ArgumentException($"a flagship holds crew in crew pods only, not {Modules.KitCrew} beside them");
        Hold = new ShipHold(loadout, Modules);
        Flight = new HyperFlight(ShipDrive.ForHyperspace(Modules.Thrusters, Modules.Jets), Modules.FuelCapacity)
        {
            Fuel = loadout.Fuel,
        };
    }

    /// <summary>The slots changed (outfitting): the drive in hyperspace and in a system, and the capacities, follow.</summary>
    internal void Refit()
    {
        Flight.Drive = ShipDrive.ForHyperspace(Modules.Thrusters, Modules.Jets);
        Flight.FuelCapacity = Modules.FuelCapacity;
        Ip?.Refit(Modules.Thrusters, Modules.Jets);
        Hold.Refitted();
    }
}

/// <summary>
/// A player in the host's game, kept in the host's save while away.
/// The id is stable per player (the client keeps it), so a returning
/// player gets their own ship back.
/// </summary>
public sealed class Player
{
    public string Id { get; }
    public string Name { get; set; }
    public bool IsHost { get; }
    public PlayerShip Ship { get; }

    /// <summary>Connected and flying. Absent players' ships wait in the save, not in space.</summary>
    public bool Present { get; set; }

    /// <summary>
    /// The captain's name (CommanderName), or null for the default: the
    /// flagship's UQM name, a guest's the name they join with (<see cref="ShipNames"/>).
    /// </summary>
    public string? CaptainName { get; internal set; }

    /// <summary>Set on a guest's first join until their client has shown the undocking scene.</summary>
    public bool UndockPending { get; set; }

    public Player(string id, string name, bool isHost, PlayerShip ship)
    {
        Id = id;
        Name = name;
        IsHost = isHost;
        Ship = ship;
    }
}

/// <summary>
/// A ship's contact with a star, as the entry starts from it: the world
/// frame of the contact, the ship's hyperspace position and its place in
/// its view then (so its sprite was at LogX + ViewX - CenterX), its
/// facing, the star, and how many frames the entry plays after the contact
/// frame (<see cref="StarEntry.Animate"/>'s steps). The system's flight
/// starts on the frame after the last. An escort that joined during its
/// host's entry has the host's, <paramref name="Following"/> the host: it
/// waits with it and arrives with it, but rides in with the flagship rather
/// than being drawn entering itself.
/// </summary>
public sealed record StarEntryState(long ContactFrame, int LogX, int LogY, short ViewX, short ViewY, int Facing,
    int Star, int Steps, string? Following = null)
{
    /// <summary>Where the ship's sprite was at contact, in hyperspace logical units.</summary>
    public (long X, long Y) Sprite => HyperFlight.SpriteAt(LogX, LogY, ViewX, ViewY);

    /// <summary>Whether the entry is still playing at a world frame (the contact frame and the steps after it).</summary>
    public bool PlayingAt(long frame) => frame >= ContactFrame && frame <= ContactFrame + Steps;
}
