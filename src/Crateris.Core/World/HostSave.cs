// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Serialization;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// The host's save: world state and every player's state, including
/// players who are away, in readable JSON. Players are loaded as away,
/// their ships stopped where they were, and come back when they rejoin.
/// </summary>
public static class HostSave
{
    /// <summary>
    /// 2 added the star each ship has just left (FlightState.StarJustLeft);
    /// 3 added planet world state (Worlds); 4 added ships in systems and in
    /// orbit (PlayerState.SystemStar, PlayerState.System). Older saves
    /// still load: their ships are in hyperspace and not on a star, and no
    /// world has been touched. 5 added each ship's hold (PlayerState.Hold:
    /// crew, landers, minerals, bio data, lander upgrades) and its loadout's
    /// landers and storage bays; a ship from an older save has its loadout's
    /// crew, one lander and one storage bay, and nothing in its hold. 6
    /// added the story state (Story: the game-state fields that aren't 0,
    /// the prices, the races' standing and the clock's events); an older
    /// save has a new game's story state, its events counted from the
    /// save's own day, so those already past aren't queued, as UQM's
    /// AddEvent queues none in the past. 7 added the ship's and captain's
    /// names (PlayerState.ShipName, CaptainName: none for the flagship's
    /// UQM names and a guest's join name); an escort from an older save is
    /// named from the settings, and an older save with names is refused. 8
    /// added each ship's resource units (HoldState.ResourceUnits), the crew
    /// its loadout holds (ShipLoadout.CrewCapacity: an older save's flagship
    /// holds its crew pod's 50, an escort the kit's 10) and whether the
    /// group has met the starbase's commander (CommanderMet); an older save
    /// with either is refused. A save made during a visit to the starbase
    /// is of the game without the visit: the story fields its talks wrote,
    /// the starter's hold and fuel and the date as before it (UQM can't
    /// save during one); what other players did meanwhile is kept. 9 added
    /// each ship's slots (PlayerState.Modules: the drive, jet and module
    /// slots outfitting changes, and the crew its kit holds); a ship from
    /// an older save has its loadout's (ShipModules.FromLoadout), and an
    /// older save with slots is refused. Battles aren't saved (stage 4,
    /// decision 14): a ship in one is saved as it joined it, where it
    /// joined it, and a loaded game has none (stage 4a item 6; the battle
    /// settings, Settings.BattleArenaScale and the like, take their
    /// defaults in a save without them). 10 added wrecks
    /// (PlayerState.Wrecked, stage 4a item 7): a ship destroyed in a
    /// battle, over or still running, is saved as a wreck at the battle's
    /// place (decision 14) with no crew, its hold and fuel otherwise as it
    /// joined the battle; it loads as a wreck. An older save has no wrecks
    /// (a version 9 ship destroyed loads flying with no crew, as it was
    /// saved), and an older save with one is refused, as is a wreck that's
    /// the flagship, has crew or is in orbit. No save is made once the
    /// flagship is lost (GameWorld.FlagshipLost, decision 12). 11 gave
    /// escorts guns (stage 4a item 8: the escort's kit's,
    /// ShipLoadout.Guns, and the combat modules outfitting sells them): an
    /// escort from an older save, a wreck too, is given the default kit's
    /// guns on load (ShipModules.GiveKitGuns), unless it has a weapon in a
    /// slot it fires from: as many as its own free weapon slots hold, in
    /// the order UQM fires from them (15, 14, 13, then 0), even where its
    /// kit had no room for them (it may have sold bays since). An older
    /// save's kit, for new guests, takes the default's guns too, as many as
    /// its slots have room for. Each ship's loadout stays the one it was
    /// made from, without guns. Guns in an older save's
    /// loadouts, which it can't have had, are taken as none. Keyed on the
    /// version, not on an escort having no weapon: one that sold its guns
    /// keeps none.
    /// </summary>
    public const int Version = 11;
    public const int OldestReadableVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public sealed record SaveFile(
        int Version, GameSettings Settings, ClockState Clock, long Frame, List<PlayerState> Players,
        List<WorldState>? Worlds = null, StoryState.Saved? Story = null, bool CommanderMet = false);

    /// <summary>
    /// One planet or moon with stored state: moon null for the planet itself;
    /// taken nodes as UQM's three retrieve masks.
    /// </summary>
    public sealed record WorldState(
        int Star, int Planet, int? Moon, uint MineralTaken, uint EnergyTaken, uint BiologicalTaken);

    public sealed record ClockState(int Year, int Month, int Day, int FramesIntoDay);

    public sealed record FlightState(
        int LogX, int LogY, int Facing, uint Fuel, int Status, int TurnWait, int ThrustWait,
        int ViewX, int ViewY, int FuelDistX, int FuelDistY, int FuelDistExtra, int FuelTicks,
        int[] Velocity, int? StarJustLeft = null);

    public sealed record PlayerState(
        string Id, string Name, bool IsHost, ShipKind Kind, ShipLoadout Loadout, bool UndockPending,
        FlightState Flight, int? SystemStar = null, IpFlightState? System = null, HoldState? Hold = null,
        string? ShipName = null, string? CaptainName = null, ModulesState? Modules = null, bool Wrecked = false);

    /// <summary>
    /// The world as a save. Refused (<see cref="InvalidOperationException"/>)
    /// once the flagship is lost (<see cref="GameWorld.FlagshipLost"/>):
    /// the host's save is kept as it was (decision 12).
    /// </summary>
    public static string ToJson(GameWorld world)
    {
        if (world.FlagshipLost)
            throw new InvalidOperationException("the flagship is lost: the game isn't saved (its save is kept as it was)");
        // A visit going on is saved as not begun.
        var visit = world.Visit;
        var players = world.Players.Select(p =>
        {
            var f = p.Ship.Flight;
            bool starter = visit?.Starter == p;
            // A battle isn't saved (stage 4, decision 14): a ship in one is
            // saved as it joined it, where it joined it (it waits there).
            var wb = p.Ship.Battle;
            var joined = wb?.FighterOf(p.Id);
            // A ship destroyed in a battle still running is saved as the
            // wreck it will be (decision 14): at the battle's place.
            bool wreck = p.Ship.Destroyed || joined?.Exit == BattleExit.Destroyed;
            bool inHyperspace = joined?.Exit == BattleExit.Destroyed && wb!.Place.InHyperspace;
            bool inSystem = joined?.Exit == BattleExit.Destroyed && !wb!.Place.InHyperspace;
            var v = f.Velocity;
            var hold =
                starter ? visit!.HoldBefore
                : joined is not null ? joined.JoinedHold
                // A lander on a surface is saved as back aboard with its crew and cargo.
                : p.Ship.Lander is { } l ? p.Ship.Hold.SaveWithLanderBack(l.Crew, l.Minerals, l.BioData) : p.Ship.Hold.Save();
            return new PlayerState(p.Id, p.Name, p.IsHost, p.Ship.Kind, p.Ship.Loadout, p.UndockPending,
                new FlightState(inHyperspace ? wb!.Place.X : f.LogX, inHyperspace ? wb!.Place.Y : f.LogY, f.Facing,
                    starter ? visit!.FuelBefore : joined?.JoinedFuel ?? f.Fuel, f.Status, f.TurnWait, f.ThrustWait,
                    f.ViewX, f.ViewY, f.FuelDistX, f.FuelDistY, f.FuelDistExtra, f.FuelTicks,
                    new int[] { v.TravelAngle, v.VectorWidth, v.VectorHeight, v.FractWidth, v.FractHeight,
                        v.ErrorWidth, v.ErrorHeight, v.IncrWidth, v.IncrHeight },
                    inHyperspace ? wb!.PlaceStarJustLeft : f.StarJustLeft),
                p.Ship.SystemStar, inSystem ? wb!.PlaceIp : p.Ship.Ip?.Save(),
                wreck ? hold with { Crew = 0 } : hold,
                p.Ship.Name, p.CaptainName, p.Ship.Modules.Save(), wreck);
        }).ToList();
        var c = world.Clock;
        var worlds = world.Planets.Worlds
            .OrderBy(w => w.Key.Star).ThenBy(w => w.Key.Planet).ThenBy(w => w.Key.Moon ?? -1)
            .Select(w => new WorldState(w.Key.Star, w.Key.Planet, w.Key.Moon,
                w.Value[ScanType.Mineral], w.Value[ScanType.Energy], w.Value[ScanType.Biological]))
            .ToList();
        var (year, month, day) = visit?.DateWithout() ?? (c.Year, c.Month, c.Day);
        var save = new SaveFile(Version, world.Settings, new ClockState(year, month, day, c.FramesIntoDay),
            world.Frame, players, worlds, visit is null ? (world.Story is null ? null : world.StoryForSave()) : visit.StoryWithout(),
            visit?.MetBefore ?? world.CommanderMet);
        return JsonSerializer.Serialize(save, Options);
    }

    // An older save's kit's guns: the default kit's, fewer if its other
    // modules leave less room, down to none (a kit that doesn't fit even
    // without guns is refused with the settings, as before).
    private static int KitGuns(ShipLoadout kit)
    {
        for (int guns = new GameSettings().EscortLoadout.Guns; guns > 0; --guns)
        {
            try
            {
                _ = ShipModules.FromLoadout(ShipKind.Escort, kit with { Guns = guns });
                return guns;
            }
            catch (ArgumentException)
            {
            }
        }
        return 0;
    }

    /// <summary>Loads a save without systems (tests). A bad save is an <see cref="InvalidDataException"/>.</summary>
    public static GameWorld FromJson(string json) => Load(json, null, null);

    /// <summary>
    /// Loads a save with its systems and the shapes that decide contact:
    /// saved worlds must exist in their systems. A bad save is an
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static GameWorld FromJson(string json, SystemCatalog systems, ContactShapes shapes) =>
        Load(json, systems ?? throw new ArgumentNullException(nameof(systems)),
            shapes ?? throw new ArgumentNullException(nameof(shapes)));

    private static GameWorld Load(string json, SystemCatalog? systems, ContactShapes? shapes)
    {
        SaveFile save;
        try
        {
            save = JsonSerializer.Deserialize<SaveFile>(json, Options)
                   ?? throw new InvalidDataException("Empty save file.");
        }
        catch (JsonException e)
        {
            // Not JSON, or a value of the wrong kind (a number setting given as null).
            throw new InvalidDataException($"The save isn't readable: {e.Message}", e);
        }
        if (save.Version < OldestReadableVersion || save.Version > Version)
            throw new InvalidDataException(
                $"Save version {save.Version} is not supported (expected {OldestReadableVersion} to {Version}).");
        // Parts that can't be missing: refused, not a NullReferenceException.
        if ((save.Players ?? new()).Any(p => p is null))
            throw new InvalidDataException("The save has a player that is null.");
        foreach (var p in save.Players ?? new())
        {
            string? lacking = p.Id is null ? "id" : p.Name is null ? "name" : p.Loadout is null ? "loadout"
                : p.Flight is null ? "flight" : p.Flight.Velocity is null ? "velocity" : null;
            if (lacking is not null)
                throw new InvalidDataException($"Player {p.Id ?? "(no id)"} has no {lacking}.");
        }
        if (save.Clock is null)
            throw new InvalidDataException("The save has no clock.");
        if (save.Version < 7 && (save.Players ?? new()).Any(p => p.ShipName is not null || p.CaptainName is not null))
            throw new InvalidDataException($"A version {save.Version} save can't have ship or captain names.");
        if (save.Version < 8 && (save.CommanderMet || (save.Players ?? new()).Any(p => p.Hold?.ResourceUnits > 0)))
            throw new InvalidDataException($"A version {save.Version} save can't have resource units or have met the commander.");
        if (save.Version < 9 && (save.Players ?? new()).Any(p => p.Modules is not null))
            throw new InvalidDataException($"A version {save.Version} save can't have ship slots.");
        if (save.Version < 10 && (save.Players ?? new()).Any(p => p.Wrecked))
            throw new InvalidDataException($"A version {save.Version} save can't have wrecks.");
        // A setting a save leaves out is the default, and so is the ship's
        // name or the escort's kit given as null; a number given as null
        // isn't a number, and the save is refused (above).
        var settings = save.Settings ?? throw new InvalidDataException("The save has no settings.");
        if (settings.EscortShipName is null)
            settings = settings with { EscortShipName = new GameSettings().EscortShipName };
        if (settings.EscortLoadout is null)
            settings = settings with { EscortLoadout = new GameSettings().EscortLoadout };
        if (save.Version < 8 && settings.EscortLoadout is { } kit)
            settings = settings with { EscortLoadout = kit with { CrewCapacity = new GameSettings().EscortLoadout.CrewCapacity } };
        // Before version 11 the kit had no guns: it has the default's, as
        // many as its slots have room for (a kit that filled them has none).
        if (save.Version < 11)
            settings = settings with { EscortLoadout = settings.EscortLoadout with { Guns = KitGuns(settings.EscortLoadout) } };

        var clock = new GameClock(save.Clock.Year, save.Clock.Month, save.Clock.Day)
        {
            FramesIntoDay = save.Clock.FramesIntoDay,
        };
        try
        {
            settings.CheckBattle();
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException($"Bad settings: battle: {e.Message}", e);
        }
        GameWorld world;
        try
        {
            world = new GameWorld(settings, clock, save.Frame, systems, shapes, loading: true);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException($"Bad settings: escort loadout: {e.Message}", e);
        }
        foreach (var ps in save.Players ?? throw new InvalidDataException("The save has no players."))
        {
            PlayerShip ship;
            try
            {
                // Before version 8 the crew a ship holds wasn't saved: an
                // escort's kit holds 10, a flagship its crew pod's 50.
                var loadout = save.Version < 8 && ps.Kind == ShipKind.Escort
                    ? ps.Loadout with { CrewCapacity = new GameSettings().EscortLoadout.CrewCapacity }
                    : ps.Loadout;
                // Before version 11 no loadout had guns; an escort is given the
                // default kit's, as many as its own weapon slots have room for.
                if (save.Version < 11)
                    loadout = loadout with { Guns = 0 };
                var modules = ps.Modules is { } m ? ShipModules.Restore(m) : ShipModules.FromLoadout(ps.Kind, loadout);
                if (save.Version < 11 && ps.Kind == ShipKind.Escort)
                    modules.GiveKitGuns(new GameSettings().EscortLoadout.Guns);
                ship = new PlayerShip(ps.Kind, loadout, modules);
                if (ps.Hold is { } h)
                    ship.Hold.Restore(h);
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException($"Player {ps.Id}: {e.Message}", e);
            }
            var f = ship.Flight;
            var s = ps.Flight;
            f.LogX = s.LogX;
            f.LogY = s.LogY;
            f.Facing = (ushort)s.Facing;
            f.Fuel = s.Fuel;
            f.Status = (ushort)s.Status;
            f.TurnWait = (byte)s.TurnWait;
            f.ThrustWait = (byte)s.ThrustWait;
            f.ViewX = (short)s.ViewX;
            f.ViewY = (short)s.ViewY;
            f.FuelDistX = (ushort)s.FuelDistX;
            f.FuelDistY = (ushort)s.FuelDistY;
            f.FuelDistExtra = (ushort)s.FuelDistExtra;
            f.FuelTicks = (byte)s.FuelTicks;
            if (s.StarJustLeft is int star && (star < 0 || star >= StarMap.SolarSystemCount))
                throw new InvalidDataException($"Player {ps.Id}: star_just_left {star} is not a star.");
            f.StarJustLeft = s.StarJustLeft;
            var v = s.Velocity;
            if (v.Length != 9)
                throw new InvalidDataException($"Player {ps.Id}: velocity needs 9 values.");
            f.Velocity = new Uqm.Velocity
            {
                TravelAngle = (ushort)v[0], VectorWidth = (short)v[1], VectorHeight = (short)v[2],
                FractWidth = (short)v[3], FractHeight = (short)v[4], ErrorWidth = (short)v[5],
                ErrorHeight = (short)v[6], IncrWidth = (short)v[7], IncrHeight = (short)v[8],
            };
            // Everyone is away after loading, and away ships wait stopped.
            f.Stop();
            if (ps.SystemStar is int systemStar)
            {
                if (systems is null)
                    throw new InvalidDataException($"Player {ps.Id} is in a system, but no star map was given to load it.");
                if (systemStar < 0 || systemStar >= StarMap.SolarSystemCount || ps.System is null)
                    throw new InvalidDataException($"Player {ps.Id}: bad system {systemStar}.");
                ship.SystemStar = systemStar;
                ship.Ip = IpFlight.Restore(systems[systemStar], ps.System, ship.Modules.Thrusters, ship.Modules.Jets, shapes!);
                ship.Ip.Stop();
                if (ship.Ip.OrbitData is { Entered: false })
                    throw new InvalidDataException($"Player {ps.Id}: in orbit around a world that can't be orbited.");
            }
            else if (ps.System is not null)
                throw new InvalidDataException($"Player {ps.Id}: system flight without a system.");
            if (ps.ShipName is { } shipName && ShipNames.Fit(shipName) != shipName
                    || ps.CaptainName is { } captain && ShipNames.Fit(captain) != captain)
                throw new InvalidDataException($"Player {ps.Id}: a name isn't one UQM could hold (at most {ShipNames.MaxBytes} bytes, printable).");
            // An escort from before names were kept gets the settings' name.
            ship.Name = ps.ShipName ?? (ps.Kind == ShipKind.Escort ? ShipNames.Fit(settings.EscortShipName) : null);
            if (ps.Wrecked)
            {
                // A wreck (stage 4a item 7): an escort with no crew, out of orbit.
                if (ps.Hold is null)
                    throw new InvalidDataException($"Player {ps.Id}: a wreck without its hold.");
                if (ps.Kind == ShipKind.Flagship)
                    throw new InvalidDataException($"Player {ps.Id}: the flagship can't be a wreck (its loss is the game over).");
                if (ship.Hold.Crew != 0)
                    throw new InvalidDataException($"Player {ps.Id}: a wreck with {ship.Hold.Crew} crew.");
                if (ship.Ip?.Orbit is not null)
                    throw new InvalidDataException($"Player {ps.Id}: a wreck in orbit.");
                ship.Destroyed = true;
            }
            world.AddPlayer(new Player(ps.Id, ps.Name, ps.IsHost, ship)
            {
                Present = false,
                UndockPending = ps.UndockPending,
                CaptainName = ps.CaptainName,
            });
        }
        if (save.Version < 6 && save.Story is not null)
            throw new InvalidDataException($"A version {save.Version} save can't have a story state.");
        if (systems?.StoryRules is { } rules)
        {
            if (save.Version >= 6 && save.Story is null)
                throw new InvalidDataException("The save has no story state.");
            world.Story = save.Story is { } story
                ? StoryState.Restore(rules, story)
                : StoryState.NewGame(rules, clock);
            // The systems are made from the story state (Procyon's starbase, Spathiwa's
            // shield). Nothing in stage 3a changes what they read; the save must agree.
            if (world.Story.SystemStory != systems.Generator.Story)
                throw new InvalidDataException(
                    $"The save's story ({world.Story.SystemStory}) makes different systems from the ones loaded ({systems.Generator.Story}).");
        }
        else if (save.Story is not null)
            throw new InvalidDataException("The save has a story state, but no data was given to load it with.");
        var seen = new HashSet<WorldId>();
        foreach (var w in save.Worlds ?? new List<WorldState>())
        {
            try
            {
                var id = new WorldId(w.Star, w.Planet, w.Moon);
                if (!seen.Add(id))
                    throw new InvalidDataException($"World {id} is in the save twice.");
                world.Planets.Restore(id, w.MineralTaken, w.EnergyTaken, w.BiologicalTaken);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new InvalidDataException($"Bad world in save: {e.Message}", e);
            }
        }
        world.CommanderMet = save.CommanderMet;
        return world;
    }
}
