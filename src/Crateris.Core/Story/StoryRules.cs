// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Story;

/// <summary>
/// An event a new game starts with (AddInitialGameEvents, gameev.c):
/// relative to the start date, or on a date of its own.
/// </summary>
public sealed record InitialEvent(string Event, bool Relative, int Months, int Days, int Years);

/// <summary>
/// A race's ship, as its FLEET_INFO in avail_race_q holds it
/// (InitGameStructures, globdata.c): its race (races.h's ARILOU_SHIP, ...),
/// its ship (data/ships.json's id; the Yehat rebels' is the Yehat's, the
/// Ur-Quan drone's the probe), what one costs (RACE_SHIP_COST, which the
/// shipyard charges and CalculateEscortsWorth adds; 0 for the drone, which
/// has none), and its crew as one is made and at most (crew_level,
/// max_crew).
/// </summary>
public sealed record RaceShip(string Race, string Ship, int Cost, int Crew, int MaxCrew);

/// <summary>
/// A race's fleet as the data gives it: its sphere as a new game starts it
/// (its ship's FLEET_STUFF in data/ships.json: the strength, and the
/// centre, known_loc, which InitGameStructures makes the fleet's loc and
/// known_loc; the Yehat rebels' is the Yehat's, the drone's the probe's),
/// and races.h's tables of it (data/races.json): the daily chance in 100
/// of a group in hyperspace inside the sphere (RACE_HYPERSPACE_PERCENT)
/// and in a system at its centre (RACE_INTERPLANETARY_PERCENT), how many
/// ships a group has (RACE_ENCOUNTER_MAKEUP: <see cref="MakeupMin"/>, then
/// each to <see cref="MakeupMax"/> at 50%), its groups' speed in
/// hyperspace (RACE_HYPER_SPEED, a velocity) and its sphere's colour on
/// the starmap (RACE_COLORS). The makeup, speed and colour aren't given
/// for the drone (UQM's tables stop before it): 0, 0, 0 and null.
/// </summary>
public sealed record RaceFleetRules(string Race, int Strength, int HomeX, int HomeY,
    int HyperspacePercent, int InterplanetaryPercent, int MakeupMin, int MakeupMax, int HyperSpeed, Data.Rgba? Color);

/// <summary>
/// What the story state is made from, all from data/: the game-state
/// fields (game-state.json), a new game's prices and events
/// (new-game.json), the races in avail_race_q's order (races.json), the
/// event ids (events.json), the modules (modules.json),
/// CREW_EXPENSE_THRESHOLD and the fleets' sphere constants (constants.json),
/// the devices (devices.json), and each race's ship and fleet (races.json
/// and ships.json: <see cref="RaceShips"/>, <see cref="Fleets"/>).
/// </summary>
public sealed class StoryRules
{
    private readonly Dictionary<string, int> _raceIndex;

    public GameStateTable Table { get; }

    public int CrewCost { get; }

    public int FuelCost { get; }

    /// <summary>MODULE_COST_SCALE: resource units a unit of <see cref="StoryState.ModuleCost"/>.</summary>
    public int ModuleCostScale { get; }

    /// <summary>A new game's module prices; modules not listed aren't for sale (0).</summary>
    public IReadOnlyDictionary<string, int> ModuleCost { get; }

    /// <summary>Every module's name, in UQM's order.</summary>
    public IReadOnlyList<string> Modules { get; }

    public IReadOnlyList<int> ElementWorth { get; }

    /// <summary>The races, by ship name, in avail_race_q's order.</summary>
    public IReadOnlyList<string> Races { get; }

    public IReadOnlyDictionary<string, int> EventIds { get; }

    public IReadOnlyList<InitialEvent> InitialEvents { get; }

    public int CrewExpenseThreshold { get; }

    /// <summary>The devices the group may carry (data/devices.json; none if the rules are made otherwise).</summary>
    public DeviceTable Devices { get; init; } = new(Array.Empty<Device>());

    /// <summary>
    /// Each race's ship, in <see cref="Races"/>' order (data/races.json and
    /// data/ships.json; none if the rules are made otherwise).
    /// </summary>
    public IReadOnlyList<RaceShip> RaceShips { get; init; } = Array.Empty<RaceShip>();

    /// <summary>A race's ship, by its race (ARILOU_SHIP, ...).</summary>
    public RaceShip RaceShip(string race) => RaceShips[RaceIndex(race)];

    /// <summary>
    /// Each race's fleet as the data gives it, in <see cref="Races"/>'
    /// order (data/races.json and data/ships.json; none if the rules are
    /// made otherwise, and then every race starts with no sphere).
    /// </summary>
    public IReadOnlyList<RaceFleetRules> Fleets { get; init; } = Array.Empty<RaceFleetRules>();

    /// <summary>SPHERE_RADIUS_INCREMENT: a sphere's radius in universe units for each step of a fleet's strength.</summary>
    public int SphereRadiusIncrement { get; init; } = 11;

    /// <summary>MAX_FLEET_STRENGTH: the most a fleet grows to (254 steps).</summary>
    public int MaxFleetStrength { get; init; } = 254 * 11;

    /// <summary>INFINITE_RADIUS: the strength of a race met anywhere (the Slylandro, the Melnorme), whose fleet doesn't grow.</summary>
    public int InfiniteRadius { get; init; } = ushort.MaxValue;

    /// <summary>
    /// The race a ship (data/ships.json's id) is of, a race that can have
    /// a fleet (not the Yehat rebels', whose ship is the Yehat's, nor the
    /// drone's), or null for a ship of none (the flagship, the Androsynth,
    /// Chenjesu, Mmrnmhrm, the Sa-Matra, the probe).
    /// </summary>
    public RaceShip? RaceOfShip(string ship) =>
        RaceShips.FirstOrDefault(r => r.Ship == ship && r.Race is not ("YEHAT_REBEL_SHIP" or "URQUAN_DRONE_SHIP"));

    public StoryRules(GameStateTable table, int crewCost, int fuelCost, int moduleCostScale,
        IReadOnlyDictionary<string, int> moduleCost, IReadOnlyList<string> modules, IReadOnlyList<int> elementWorth,
        IReadOnlyList<string> races, IReadOnlyDictionary<string, int> eventIds, IReadOnlyList<InitialEvent> initialEvents,
        int crewExpenseThreshold)
    {
        Table = table;
        CrewCost = crewCost;
        FuelCost = fuelCost;
        ModuleCostScale = moduleCostScale;
        ModuleCost = moduleCost;
        Modules = modules;
        ElementWorth = elementWorth;
        Races = races;
        EventIds = eventIds;
        InitialEvents = initialEvents;
        CrewExpenseThreshold = crewExpenseThreshold;
        _raceIndex = races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
        foreach (var m in moduleCost.Keys)
        {
            if (!modules.Contains(m))
                throw new InvalidDataException($"new-game.json: no module {m}");
        }
        foreach (var e in initialEvents)
        {
            if (!eventIds.ContainsKey(e.Event))
                throw new InvalidDataException($"new-game.json: no event {e.Event}");
        }
    }

    public int RaceIndex(string race) =>
        _raceIndex.TryGetValue(race, out int i) ? i : throw new KeyNotFoundException($"no race {race}");

    public static StoryRules Load(string dataDirectory)
    {
        JsonDocument Open(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, file)));
        using var newGame = Open("new-game.json");
        using var races = Open("races.json");
        using var events = Open("events.json");
        using var modules = Open("modules.json");
        using var constants = Open("constants.json");

        var prices = newGame.RootElement.GetProperty("prices");
        var worth = prices.GetProperty("element_worth");
        var elementWorth = Data.PlanetTypes.ElementCategoryNames.Select(c => worth.GetProperty(c).GetInt32()).ToList();
        var moduleCost = prices.GetProperty("module_cost").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var initial = newGame.RootElement.GetProperty("initial_events").EnumerateArray().Select(e =>
        {
            bool relative = e.GetProperty("kind").GetString() == "RELATIVE";
            string p(string rel, string abs) => relative ? rel : abs;
            return new InitialEvent(e.GetProperty("event").GetString()!, relative,
                e.GetProperty(p("months", "month")).GetInt32(), e.GetProperty(p("days", "day")).GetInt32(),
                e.GetProperty(p("years", "year")).GetInt32());
        }).ToList();

        var shipDescriptions = Combat.ShipDescriptions.Load(dataDirectory);
        var raceShips = LoadRaceShips(races.RootElement, shipDescriptions);
        int Constant(string name) => constants.RootElement.GetProperty("constants").GetProperty(name).GetProperty("value").GetInt32();
        return new StoryRules(
            GameStateTable.Load(dataDirectory),
            prices.GetProperty("crew").GetInt32(), prices.GetProperty("fuel").GetInt32(),
            prices.GetProperty("module_cost_scale").GetInt32(), moduleCost,
            ModuleNames(modules.RootElement), elementWorth,
            races.RootElement.GetProperty("races").EnumerateArray().Select(r => r.GetProperty("race").GetString()!).ToList(),
            events.RootElement.GetProperty("events").EnumerateArray()
                .ToDictionary(e => e.GetProperty("event").GetString()!, e => e.GetProperty("id").GetInt32()),
            initial,
            constants.RootElement.GetProperty("constants").GetProperty("CREW_EXPENSE_THRESHOLD").GetProperty("value").GetInt32())
        {
            Devices = DeviceTable.Load(dataDirectory),
            RaceShips = raceShips,
            Fleets = LoadFleets(races.RootElement, raceShips, shipDescriptions),
            SphereRadiusIncrement = Constant("SPHERE_RADIUS_INCREMENT"),
            MaxFleetStrength = Constant("MAX_FLEET_STRENGTH"),
            InfiniteRadius = Constant("INFINITE_RADIUS"),
        };
    }

    // Each race's fleet: its ship's sphere (RaceShips' ship, as InitGameStructures
    // copies it from the master ship list, or the probe's from load_ship) and races.h's tables.
    private static List<RaceFleetRules> LoadFleets(JsonElement races, IReadOnlyList<RaceShip> raceShips,
        Combat.ShipDescriptions ships)
    {
        var list = new List<RaceFleetRules>();
        int i = 0;
        foreach (var r in races.GetProperty("races").EnumerateArray())
        {
            string race = r.GetProperty("race").GetString()!;
            var fleet = ships[raceShips[i++].Ship].Fleet;
            int Int(string name) => r.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            var makeup = r.TryGetProperty("encounter_makeup", out var m) ? m : (JsonElement?)null;
            Data.Rgba? color = null;
            if (r.TryGetProperty("color", out var c))
            {
                var rgba = c.EnumerateArray().Select(x => x.GetByte()).ToArray();
                if (rgba.Length != 4)
                    throw new InvalidDataException($"races.json: {race}'s colour isn't [r, g, b, a]");
                color = new Data.Rgba(rgba[0], rgba[1], rgba[2], rgba[3]);
            }
            list.Add(new RaceFleetRules(race, fleet.Strength, fleet.HomeX, fleet.HomeY,
                Int("hyperspace_percent"), Int("interplanetary_percent"),
                makeup?.GetProperty("min").GetInt32() ?? 0, makeup?.GetProperty("max").GetInt32() ?? 0,
                Int("hyper_speed"), color));
        }
        return list;
    }

    // Each race's ship: the ship whose species is the race's (BattleKit.RaceOf), the Yehat rebels' the Yehat's,
    // the drone's the probe (InitGameStructures' YEHAT_ID and UR_QUAN_PROBE_ID).
    private static List<RaceShip> LoadRaceShips(JsonElement races, Combat.ShipDescriptions ships)
    {
        var list = new List<RaceShip>();
        foreach (var r in races.GetProperty("races").EnumerateArray())
        {
            string race = r.GetProperty("race").GetString()!;
            var ship = race switch
            {
                "YEHAT_REBEL_SHIP" => ships["yehat"],
                "URQUAN_DRONE_SHIP" => ships["probe"],
                _ => ships.All.SingleOrDefault(s => World.BattleKit.RaceOf(s) == race)
                    ?? throw new InvalidDataException($"races.json: no ship in ships.json is of {race}"),
            };
            int cost = r.TryGetProperty("ship_cost", out var c) ? c.GetInt32() : 0;
            list.Add(new RaceShip(race, ship.Id, cost, ship.Crew, ship.MaxCrew));
        }
        return list;
    }

    private static List<string> ModuleNames(JsonElement root)
    {
        var list = root.GetProperty("modules");
        return list.EnumerateArray().OrderBy(m => m.GetProperty("id").GetInt32()).Select(m => m.GetProperty("module").GetString()!).ToList();
    }
}
