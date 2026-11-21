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
using Crateris.Core.Hyperspace;
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
    /// defaults in a save without them, as the escorts' caps,
    /// Settings.EscortCaps, do since the escorts' upgrades: no new version). 10 added wrecks
    /// (PlayerState.Wrecked, stage 4a item 7): a ship destroyed in a
    /// battle, over or still running, is saved as a wreck at the battle's
    /// place (decision 14) with no crew, its hold and fuel otherwise as it
    /// joined the battle; it loads as a wreck. An older save has no wrecks
    /// (a version 9 ship destroyed loads flying with no crew, as it was
    /// saved), and an older save with one is refused, as is a wreck that's
    /// the flagship, has crew or is in orbit. No save is made once the
    /// flagship is lost or has surrendered (GameWorld.FlagshipLost, decision 12). 11 gave
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
    /// keeps none. A save during the Ilwrath's fight in the opening (stage
    /// 4a item 9) is of the game without the visit, as during its talks:
    /// the starter's hold and fuel as before the visit (no crew if it was
    /// destroyed in the fight: a wreck, as the visit taken back keeps it),
    /// the others as they joined it; a starter that disconnected from the
    /// fight was settled as it left (StarbaseVisit.SettleStarter: its hold
    /// as before the visit with the crew it left with, its fuel as before
    /// it less the retreat's 5 units) and is saved as it is from then on.
    /// 12 has the host's fleet (stage 4b item 6: each ship's race, crew,
    /// captain and hangar slot); an older save loads with none, and one
    /// with a fleet is refused. During a visit taken back if unfinished (the
    /// opening's), the fleet is saved without the ships its talks gave it
    /// (what else changed it meanwhile, a battle's deaths and crews, kept;
    /// StarbaseVisit.FleetWithout). A ship of no crew
    /// (the shipyard's while its crew is edited) isn't saved: it would be
    /// scrapped as the editing ends (stage 4b item 7), and the save loads
    /// out of the shipyard; one in a save loads, and is scrapped in the
    /// world's first frame (<see cref="GameWorld.ScrapUnmannedFleetShips"/>).
    /// 13 runs the clock's events (stage 3b item 1) and has the events set
    /// aside for stage 3c (StoryState.Saved.SetAside, decision 9); an older
    /// save has none set aside, and one with any is refused. On loading any
    /// save, the events whose day has passed run at once, in date order
    /// (those of 3c set aside): an older save's, which ran none, as the
    /// encounters' event waiting since the second day. During a visit's
    /// opening, the two weeks' events are taken back with the two weeks.
    /// Version 13 also has the races' fleets (StoryState.Saved.Fleets,
    /// stage 3b item 2: each race's sphere, known sphere, growth and
    /// heading), an optional part rather than a version of its own: a
    /// save without them (an older one, or a version 13 from before item
    /// 2) has a new game's, and an older save with them is refused.
    /// During a visit's opening they're saved without what its talks and
    /// its two weeks did to them. Version 13 also has the hyperspace
    /// encounters (stage 3b item 3: each group's race, flags, ships' crews,
    /// sphere, place and vortex, whose roll made it and whom it's kept
    /// for, as UQM saves encounter_q, without its element), an optional
    /// part as the fleets are: a save without them has none, and an older
    /// save with them is refused. During a visit's opening they're saved
    /// without what its two weeks rolled. Loaded, the players who were in
    /// hyperspace keep the groups in their span until they're back.
    /// </summary>
    public const int Version = 13;
    public const int OldestReadableVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public sealed record SaveFile(
        int Version, GameSettings Settings, ClockState Clock, long Frame, List<PlayerState> Players,
        List<WorldState>? Worlds = null, StoryState.Saved? Story = null, bool CommanderMet = false,
        List<FleetShip>? Fleet = null, List<EncounterState>? Encounters = null,
        SystemGroupsState? SystemGroups = null);

    /// <summary>
    /// The groups kept with the systems (stage 3b-1 item 4): the special
    /// systems' own groups, by the system's special name (UQM's
    /// *_GRPOFFS0 groups), and each system's draw: its random groups
    /// (null: none kept), the last day it's good for, the players who were
    /// in it, the special system whose own groups fly (null: the random
    /// draw's), the groups flying while someone's in it (ip_group_q, as
    /// grpinfo.c's GROUP_LIST keeps them, without their elements) and the
    /// race of a battle fought there whose groups are yet to turn on the
    /// players (-1 for none).
    /// </summary>
    public sealed record SystemGroupsState(List<DefinedState>? Defined, List<SystemDrawState>? Systems);

    public sealed record DefinedState(string Special, List<GroupShipsState> Groups);

    public sealed record GroupShipsState(int Id, string Race, List<int> Ships, List<int>? Captains = null);

    public sealed record SystemDrawState(
        int Star, string? Defined, List<GroupShipsState>? Random, int[]? Expires, List<string> Visitors,
        List<IpGroupState> Groups, int EncounterRace = -1);

    public sealed record IpGroupState(
        int Id, string Race, int Counter, int SysLoc, int Task, int DestLoc, int OrbitPos, bool InSystem, int X, int Y);

    /// <summary>
    /// A hyperspace encounter (UQM's ENCOUNTER, as save.c keeps it): race
    /// (races.h's number), flags (ONE_SHOT_ENCOUNTER, ENCOUNTER_REFORMING),
    /// its ships' crews, its sphere's centre and radius, where it is
    /// (universe and logical units), its vortex's state, whose roll made
    /// it, and the players out of hyperspace it's kept for, with where
    /// they left.
    /// </summary>
    public sealed record EncounterState(
        int Race, int Flags, List<int> Ships, int OriginX, int OriginY, int Radius, int LocX, int LocY,
        int LogX, int LogY, int TransitionState, string Roller, List<KeptState>? KeptFor = null);

    public sealed record KeptState(string Player, int X, int Y);

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
    /// once the flagship is lost or has surrendered
    /// (<see cref="GameWorld.FlagshipLost"/>):
    /// the host's save is kept as it was (decision 12).
    /// </summary>
    public static string ToJson(GameWorld world, Action<string>? log = null)
    {
        if (world.FlagshipLost)
            throw new InvalidOperationException("the flagship is lost: the game isn't saved (its save is kept as it was)");
        if (world.FlagshipSurrendering)
            throw new InvalidOperationException("the flagship surrenders: the game isn't saved (its save is kept as it was)");
        string json = Write(world);
        // A meeting waiting at its player's closing lines is saved with its
        // talk's end made, as leaving then makes it (the owner, 2026-11-21,
        // question 13): on a copy loaded from the save, the live game's
        // meeting waiting on. A flagship surrendering so isn't saved (the
        // game is over once its lines are heard).
        var ends = world.ClosingEndsForSave();
        if (ends.Count == 0)
            return json;
        if (world.Systems is null || world.Shapes is null)
        {
            log?.Invoke("The save: no systems to load its copy with; the meetings at their closing lines are saved without their ends");
            return json;
        }
        GameWorld copy;
        try
        {
            copy = Load(json, world.Systems, world.Shapes);
        }
        catch (InvalidDataException e)
        {
            log?.Invoke($"The save: its copy couldn't be loaded ({e.Message}); the meetings at their closing lines are saved without their ends");
            return json;
        }
        copy.MakeClosingEnds(ends, log);
        return Write(copy);
    }

    private static string Write(GameWorld world)
    {
        // A visit going on is saved as not begun.
        var visit = world.Visit;
        var players = world.Players.Select(p =>
        {
            var f = p.Ship.Flight;
            // The visit's starter as before it, unless settled (it left the Ilwrath's fight): then as it is, less what later talks changed.
            bool starter = visit?.Starter == p && !visit.StarterSettled;
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
            uint fuel = starter ? visit!.FuelBefore : joined?.JoinedFuel ?? f.Fuel;
            // Any other ship the visit's talks used (its answerers'): saved
            // without what the talks changed on it. (A ship in a battle is
            // saved as it joined: this assumes the talks changed it before it
            // joined, as they do now: the opening's one battle, the Ilwrath's,
            // starts after the talks before it and ends before the talks
            // after it. Encounters during a talk, 3b's, must keep the two
            // apart.)
            if (visit is not null && !starter)
                (hold, fuel) = visit.WithoutTalks(p, hold, fuel);
            return new PlayerState(p.Id, p.Name, p.IsHost, p.Ship.Kind, p.Ship.Loadout, p.UndockPending,
                new FlightState(inHyperspace ? wb!.Place.X : f.LogX, inHyperspace ? wb!.Place.Y : f.LogY, f.Facing,
                    fuel, f.Status, f.TurnWait, f.ThrustWait,
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
            visit?.MetBefore ?? world.CommanderMet,
            // A fleet ship of no crew (the shipyard's, its crew all sent away and its cost given back) is saved
            // scrapped, as leaving the crew editing would scrap it (stage 4b item 7): the save loads out of the shipyard.
            // In a battle, the fleet ships are as they joined it, those destroyed gone (item 8: FleetForSave); during
            // an opening visit, without the ships its talks gave (StarbaseVisit.FleetWithout).
            (visit?.FleetWithout(world.FleetForSave()) ?? world.FleetForSave()).Where(s => s.Crew > 0).ToList(),
            // During an opening, without what its two weeks rolled; none, left out; kept for a ship spinning into a star
            // (or into a group it met: EncountersForSave).
            world.EncountersForSave(visit?.EncountersWithout() ?? world.Encounters?.List) is { Count: > 0 } list ? list.Select(e => new EncounterState(
                e.Race, e.Flags, e.Ships.ToList(), e.OriginX, e.OriginY, e.Radius, e.LocX, e.LocY, e.LogX, e.LogY,
                e.TransitionState, e.Roller, e.KeptFor.Select(k => new KeptState(k.Key, k.Value.X, k.Value.Y)).ToList()))
                .ToList() : null,
            SystemGroupsForSave(world));
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
        if (save.Version < 12 && save.Fleet is { Count: > 0 })
            throw new InvalidDataException($"A version {save.Version} save can't have a fleet.");
        if (save.Version < 13 && save.Story?.SetAside is { Count: > 0 })
            throw new InvalidDataException($"A version {save.Version} save can't have events set aside.");
        if (save.Version < 13 && save.Story?.Fleets is not null)
            throw new InvalidDataException($"A version {save.Version} save can't have the races' fleets.");
        if (save.Version < 13 && save.Encounters is not null)
            throw new InvalidDataException($"A version {save.Version} save can't have hyperspace encounters.");
        if (save.Version < 13 && save.SystemGroups is not null)
            throw new InvalidDataException($"A version {save.Version} save can't have system groups.");
        if ((save.Fleet ?? new()).Any(s => s is null || s.Race is null))
            throw new InvalidDataException("The save has a fleet ship that is null or of no race.");
        // A setting a save leaves out is the default, and so is the ship's
        // name or the escort's kit given as null; a number given as null
        // isn't a number, and the save is refused (above).
        var settings = save.Settings ?? throw new InvalidDataException("The save has no settings.");
        if (settings.EscortShipName is null)
            settings = settings with { EscortShipName = new GameSettings().EscortShipName };
        if (settings.EscortLoadout is null)
            settings = settings with { EscortLoadout = new GameSettings().EscortLoadout };
        if (settings.EscortCaps is null)
            settings = settings with { EscortCaps = new GameSettings().EscortCaps };
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
        try
        {
            settings.CheckEncounters();
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException($"Bad settings: encounters: {e.Message}", e);
        }
        try
        {
            settings.EscortCaps.Check();
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException($"Bad settings: escort caps: {e.Message}", e);
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
        if (save.Fleet is { Count: > 0 } fleet)
        {
            if (systems?.StoryRules is not { } fleetRules)
                throw new InvalidDataException("The save has a fleet, but no data was given to load it with.");
            try
            {
                world.Fleet.Restore(fleetRules, fleet);
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException($"Bad fleet in save: {e.Message}", e);
            }
        }
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
        if (save.Encounters is { } encounters)
            RestoreEncounters(world, encounters);
        world.LoadedInHyperspace();
        if (save.SystemGroups is { } systemGroups)
            RestoreSystemGroups(world, systemGroups);
        world.LoadedInSystems();
        world.CommanderMet = save.CommanderMet;
        // The events whose day has passed run now, in date order (an older
        // save's: it ran none); those of 3c are set aside (decision 9).
        world.RunDayEvents();
        return world;
    }
    // The system groups as they are: left out when there are none.
    private static SystemGroupsState? SystemGroupsForSave(GameWorld world)
    {
        if (world.SystemGroups is not { } sg || (sg.Defined.Count == 0 && sg.Systems.Count == 0))
            return null;
        var races = world.Story!.Rules.Races;
        GroupShipsState Ships(GroupShips g) => new(g.Id, races[g.Race], g.Ships.ToList(), g.Captains.ToList());
        return new SystemGroupsState(
            sg.Defined.OrderBy(d => d.Key, StringComparer.Ordinal)
                .Select(d => new DefinedState(d.Key, d.Value.Select(Ships).ToList())).ToList(),
            sg.Systems.Values.OrderBy(d => d.Star).Select(d => new SystemDrawState(
                d.Star, d.Defined, d.RandomGroups?.Select(Ships).ToList(),
                d.Expires is { } e ? new[] { e.Year, e.Month, e.Day } : null,
                d.Visitors.OrderBy(v => v, StringComparer.Ordinal).ToList(),
                d.Groups.Select(g => new IpGroupState(g.GroupId, races[g.Race], g.GroupCounter, g.SysLoc, g.Task,
                    g.DestLoc, g.OrbitPos, g.InSystem, g.LocX, g.LocY)).ToList(),
                d.EncounterRace)).ToList());
    }

    // The specials whose systems have groups of their own.
    private static readonly HashSet<string> OwnGroupSpecials = new()
    {
        "SOL_DEFINED", "START_COLONY_DEFINED", "SHOFIXTI_DEFINED", "ZOQ_SCOUT_DEFINED",
        "MELNORME0_DEFINED", "MELNORME1_DEFINED", "MELNORME2_DEFINED", "MELNORME3_DEFINED", "MELNORME4_DEFINED",
        "MELNORME5_DEFINED", "MELNORME6_DEFINED", "MELNORME7_DEFINED", "MELNORME8_DEFINED",
    };

    // The system groups: each checked as UQM's types and files hold them,
    // its stars, races, specials and players the game's.
    private static void RestoreSystemGroups(GameWorld world, SystemGroupsState saved)
    {
        if (world.SystemGroups is not { } sg || world.Systems is null)
            throw new InvalidDataException("The save has system groups, but no data was given to load them with.");
        var rules = world.Story!.Rules;
        int RaceOf(string? race) => race is not null && rules.Races.Contains(race)
            ? rules.RaceIndex(race) : throw new InvalidDataException($"Bad system group in save: race {race} isn't a race.");
        List<GroupShips> Groups(List<GroupShipsState>? list, string where)
        {
            var groups = new List<GroupShips>();
            foreach (var g in list ?? throw new InvalidDataException($"Bad system groups in save: {where} has no list."))
            {
                if (g is null || g.Id < 1 || g.Id > 64 || g.Ships is null || g.Ships.Count > 255
                    || g.Ships.Any(c => c < 0 || c > byte.MaxValue) || groups.Any(o => o.Id == g.Id)
                    || (g.Captains is { } cs && (cs.Count != g.Ships.Count || cs.Any(c => c < 0 || c > byte.MaxValue))))
                    throw new InvalidDataException($"Bad system group in save: {where} has a bad group.");
                groups.Add(new GroupShips(g.Id, RaceOf(g.Race), g.Ships, g.Captains));
            }
            return groups;
        }
        foreach (var d in saved.Defined ?? new())
        {
            if (d is null || d.Special is null || !OwnGroupSpecials.Contains(d.Special) || sg.Defined.ContainsKey(d.Special))
                throw new InvalidDataException($"Bad system groups in save: {d?.Special} has no groups of its own, or is there twice.");
            sg.Defined[d.Special] = Groups(d.Groups, d.Special);
        }
        int stars = world.Systems.Map.Stars.Count;
        foreach (var s in saved.Systems ?? new())
        {
            if (s is null || s.Star < 0 || s.Star >= stars || sg.Systems.ContainsKey(s.Star))
                throw new InvalidDataException($"Bad system groups in save: star {s?.Star} isn't a star, or is there twice.");
            var system = world.Systems[s.Star];
            if (s.Defined is not null && (s.Defined != system.Star.Special || !sg.Defined.ContainsKey(s.Defined)))
                throw new InvalidDataException($"Bad system groups in save: star {s.Star}'s own groups aren't {s.Defined}'s.");
            if (s.Expires is { } e && (e.Length != 3 || e[1] is < 1 or > 12 || e[2] < 1 || e[2] > GameClock.DaysInMonth(e[1], e[0])))
                throw new InvalidDataException($"Bad system groups in save: star {s.Star}'s date isn't a date.");
            if (s.Visitors is null || s.Visitors.Any(v => v is null || world.Find(v) is null) || s.Visitors.Distinct().Count() != s.Visitors.Count)
                throw new InvalidDataException($"Bad system groups in save: star {s.Star} has a visitor who isn't a player.");
            if (s.EncounterRace < -1 || s.EncounterRace >= rules.Races.Count)
                throw new InvalidDataException($"Bad system groups in save: star {s.Star}'s battle race isn't a race.");
            var draw = new SystemDraw(s.Star)
            {
                Defined = s.Defined,
                RandomGroups = s.Random is null ? null : Groups(s.Random, $"star {s.Star}"),
                Expires = s.Expires is { } x ? new GameDate(x[0], x[1], x[2]) : null,
                EncounterRace = s.EncounterRace,
            };
            foreach (var v in s.Visitors)
                draw.Visitors.Add(v);
            int planets = system.Planets.Count;
            foreach (var g in s.Groups ?? throw new InvalidDataException($"Bad system groups in save: star {s.Star} has no groups' list."))
            {
                string? bad = g is null ? "a group is null"
                    : g.Id < 1 || g.Id > 64 ? $"group {g.Id} isn't a group's number"
                    : g.Counter < 0 || g.Counter > ushort.MaxValue ? "a counter isn't a UWORD"
                    : g.SysLoc < 0 || g.SysLoc > planets ? $"a place {g.SysLoc} isn't one of its views"
                    : g.Task < 0 || g.Task > 15 || (g.Task & 3) > IpGroup.OnStation ? $"task {g.Task} isn't a task"
                    : g.DestLoc < 0 || g.DestLoc > planets ? $"destination {g.DestLoc} isn't one of its worlds"
                    : g.OrbitPos < 0 || g.OrbitPos > 15 ? $"orbit place {g.OrbitPos} isn't a facing"
                    : g.X < short.MinValue || g.X > short.MaxValue || g.Y < short.MinValue || g.Y > short.MaxValue ? "a place isn't a COORD"
                    : null;
                if (bad is not null)
                    throw new InvalidDataException($"Bad system group in save: star {s.Star}: {bad}.");
                draw.Groups.Add(new IpGroup
                {
                    GroupId = g!.Id, Race = RaceOf(g.Race), GroupCounter = (ushort)g.Counter, SysLoc = (byte)g.SysLoc,
                    Task = (byte)g.Task, DestLoc = (byte)g.DestLoc, OrbitPos = (byte)g.OrbitPos, InSystem = g.InSystem,
                    LocX = (short)g.X, LocY = (short)g.Y,
                });
            }
            sg.Systems[s.Star] = draw;
        }
    }

    // The hyperspace encounters: each checked as UQM's types hold them,
    // its roller and those it's kept for players of the game.
    private static void RestoreEncounters(GameWorld world, List<EncounterState> saved)
    {
        if (world.Encounters is not { } encounters)
            throw new InvalidDataException("The save has hyperspace encounters, but no data was given to load them with.");
        if (saved.Count > world.Settings.MaxHyperspaceEncounters)
            throw new InvalidDataException(
                $"The save has {saved.Count} hyperspace encounters, more than its {world.Settings.MaxHyperspaceEncounters}.");
        var races = world.Story!.Rules.Races;
        foreach (var e in saved)
        {
            if (e is null)
                throw new InvalidDataException("The save has a hyperspace encounter that is null.");
            string? bad =
                e.Race < 0 || e.Race >= races.Count ? $"race {e.Race} isn't a race"
                : (e.Flags & ~(HyperEncounter.OneShot | HyperEncounter.Reforming)) != 0 ? $"flags {e.Flags} aren't an encounter's"
                : e.Ships is null || e.Ships.Count > HyperEncounter.MaxShips ? $"it has more than {HyperEncounter.MaxShips} ships, or none listed"
                : e.Ships.Any(c => c < 0 || c > ushort.MaxValue) ? "a ship's crew isn't a COUNT"
                : new[] { e.OriginX, e.OriginY, e.LocX, e.LocY, e.TransitionState }.Any(v => v < short.MinValue || v > short.MaxValue)
                    ? "a place or its vortex isn't a COORD"
                : e.Radius < 0 || e.Radius > ushort.MaxValue ? $"radius {e.Radius} isn't a COUNT"
                : e.Roller is null || world.Find(e.Roller) is null ? $"its roller {e.Roller} isn't a player"
                : (e.KeptFor ?? new()).Any(k => k is null || k.Player is null || world.Find(k.Player) is null)
                    ? "it's kept for someone who isn't a player"
                : (e.KeptFor ?? new()).Select(k => k.Player).Distinct().Count() != (e.KeptFor ?? new()).Count
                    ? "it's kept for a player twice"
                : (e.KeptFor ?? new()).Any(k => k.X < short.MinValue || k.X > short.MaxValue || k.Y < short.MinValue || k.Y > short.MaxValue)
                    ? "a place it's kept for isn't a COORD"
                : null;
            if (bad is not null)
                throw new InvalidDataException($"Bad hyperspace encounter in save: {bad}.");
            var enc = new HyperEncounter
            {
                Race = e.Race, Flags = (byte)e.Flags, OriginX = (short)e.OriginX, OriginY = (short)e.OriginY,
                Radius = (ushort)e.Radius, LocX = (short)e.LocX, LocY = (short)e.LocY, LogX = e.LogX, LogY = e.LogY,
                TransitionState = (short)e.TransitionState, Roller = e.Roller!,
            };
            enc.Ships.AddRange(e.Ships!);
            foreach (var k in e.KeptFor ?? new())
                enc.KeptFor[k.Player] = ((short)k.X, (short)k.Y);
            encounters.List.Add(enc);
        }
    }
}
