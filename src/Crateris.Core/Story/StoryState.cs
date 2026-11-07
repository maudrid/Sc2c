// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (globdata.c getGameState, setGameState,
// getGameState32, setGameState32 and InitGameStructures; shipyard.c
// CrewTransaction; races.h allied_state),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Story;

/// <summary>A race's standing with the player (allied_state, races.h).</summary>
public enum Allied
{
    /// <summary>DEAD_GUY: the race is extinct.</summary>
    Dead = 0,

    /// <summary>GOOD_GUY: allied with the player.</summary>
    Good = 1,

    /// <summary>BAD_GUY: not allied with the player (every race at the start but the humans).</summary>
    Bad = 2,
}

/// <summary>
/// The story state the whole group shares (docs/plans/stage-3.md, item 1;
/// docs/differences.md row 10): UQM's game-state bits, packed as UQM packs
/// them and read and written by name, and the story state UQM keeps
/// beside them: the prices (CrewCost, FuelCost, ModuleCost), the element
/// worths, each race's standing (allied_state in avail_race_q) and the
/// clock's events. One instance per game, on the host; clients are sent
/// it (<see cref="StorySnapshot"/>).
///
/// A few fields hold one activity's working state, not the story's, and
/// are kept per player or per talk instead (decision 11; Melnorme credits
/// by row 19): <see cref="NotShared"/> lists them with where each lives,
/// and reading or writing one here throws.
/// </summary>
public sealed class StoryState
{
    /// <summary>
    /// The fields kept out of the shared story state, and why. Shared,
    /// one player's visit would change another's.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotShared = new Dictionary<string, string>
    {
        ["GLOBAL_FLAGS_AND_DATA"] = "where a talk is happening (at the starbase, a homeworld, a broadcaster): each talk's own",
        ["BATTLE_SEGUE"] = "how a talk ends: each talk's own",
        ["STARBASE_VISITED"] = "before the alliance, whether the group has met the commander (GameWorld.CommanderMet); at the allied starbase, whether he has greeted this player on this visit: each player's own",
        ["ESCAPE_COUNTER"] = "a battle's escape count: each battle's own",
        ["BATTLE_PLANET"] = "the planet a battle is fought by: each battle's own",
        ["IMPROVED_LANDER_SPEED"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["IMPROVED_LANDER_CARGO"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["IMPROVED_LANDER_SHOT"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["LANDER_SHIELDS"] = "the lander's shields: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["MELNORME_CREDIT0"] = "Melnorme credits: each player's own (docs/differences.md row 19)",
        ["MELNORME_CREDIT1"] = "Melnorme credits: each player's own (docs/differences.md row 19)",
        ["ARILOU_SPACE_SIDE"] = "whether a ship is in hyperspace or QuasiSpace: each ship's own",
        ["PLANETARY_LANDING"] = "whether a lander is down: each ship's own (PlayerShip.Lander, stage 2b)",
        ["PLANETARY_CHANGE"] = "whether a visit changed its world (for UQM's star info file): taken nodes are saved at once (PlanetWorldState)",
    };

    private readonly byte[] _bits;
    private readonly Dictionary<string, int> _moduleCost;
    private readonly Allied[] _alliances;

    public StoryRules Rules { get; }

    public GameStateTable Table => Rules.Table;

    /// <summary>Goes up by one at every change, so a snapshot is made again only when something changed.</summary>
    public long Version { get; private set; }

    /// <summary>The clock's events, in date order (the event queue of clock.c).</summary>
    public EventQueue Events { get; }

    private StoryState(StoryRules rules)
    {
        Rules = rules;
        _bits = new byte[rules.Table.Bytes];
        _moduleCost = new Dictionary<string, int>(rules.ModuleCost);
        _alliances = Enumerable.Repeat(Allied.Bad, rules.Races.Count).ToArray();
        // InitGameStructures: SetRaceAllied (HUMAN_SHIP, TRUE).
        _alliances[rules.RaceIndex("HUMAN_SHIP")] = Allied.Good;
        CrewCost = rules.CrewCost;
        FuelCost = rules.FuelCost;
        Events = new EventQueue(rules, () => ++Version);
    }

    /// <summary>
    /// A new game's story state, as InitGameStructures and
    /// AddInitialGameEvents leave it on <paramref name="clock"/>'s date:
    /// every field 0, every race but the humans not allied, UQM's prices, and the first
    /// events, of which the one due at once (SLYLANDRO_RAMP_UP) has run.
    /// </summary>
    public static StoryState NewGame(StoryRules rules, GameClock clock)
    {
        var story = new StoryState(rules);
        foreach (var e in rules.InitialEvents)
            story.AddEvent(e.Relative, e.Months, e.Days, e.Years, e.Event, clock);
        story.Version = 0;
        return story;
    }

    // ---------------------------------------------------------------- the bits

    /// <summary>GET_GAME_STATE.</summary>
    public int Get(string name)
    {
        var f = Shared(name);
        return GetBits(_bits, f.StartBit, f.EndBit);
    }

    /// <summary>
    /// SET_GAME_STATE. As in UQM the value is taken as a byte and isn't
    /// checked against the field's width: a value too big runs into the
    /// next field's bits in the same byte, as UQM's does.
    /// </summary>
    public void Set(string name, int value)
    {
        var f = Shared(name);
        if (SetBits(_bits, f.StartBit, f.EndBit, (byte)value))
            ++Version;
    }

    /// <summary>GET_GAME_STATE_32: the four 8-bit fields starting with <paramref name="first"/>, low byte first.</summary>
    public uint Get32(string first)
    {
        var f = Shared(first);
        uint v = 0;
        for (int shift = 0, bit = f.StartBit; shift < 32; shift += 8, bit += 8)
            v |= (uint)GetBits(_bits, bit, bit + 7) << shift;
        return v;
    }

    /// <summary>SET_GAME_STATE_32.</summary>
    public void Set32(string first, uint value)
    {
        var f = Shared(first);
        bool changed = false;
        for (int i = 0, bit = f.StartBit; i < 4; ++i, value >>= 8, bit += 8)
            changed |= SetBits(_bits, bit, bit + 7, (byte)value);
        if (changed)
            ++Version;
    }

    /// <summary>The packed bits, as UQM's GameState array holds them.</summary>
    public ReadOnlySpan<byte> Bits => _bits;

    private GameStateField Shared(string name)
    {
        if (NotShared.TryGetValue(name, out string? why))
            throw new InvalidOperationException($"{name} isn't shared story state: {why}");
        return Table[name];
    }

    /// <summary>getGameState: bits startBit to endBit (at most 8, at most two bytes).</summary>
    internal static int GetBits(ReadOnlySpan<byte> state, int startBit, int endBit) =>
        (byte)(((startBit >> 3) == (endBit >> 3)
                ? state[startBit >> 3] >> (startBit & 7)
                : (state[startBit >> 3] >> (startBit & 7))
                  | (state[endBit >> 3] << (endBit - startBit - (endBit & 7))))
            & ((1 << (endBit - startBit + 1)) - 1));

    /// <summary>setGameState, with its arithmetic. True if a byte changed.</summary>
    internal static bool SetBits(Span<byte> state, int startBit, int endBit, byte val)
    {
        byte first = state[startBit >> 3], last = state[endBit >> 3];
        state[startBit >> 3] = (byte)((state[startBit >> 3]
                & (byte)~(((1 << (endBit - startBit + 1)) - 1) << (startBit & 7)))
            | (byte)(val << (startBit & 7)));
        if ((startBit >> 3) < (endBit >> 3))
        {
            state[endBit >> 3] = (byte)((state[endBit >> 3]
                    & (byte)~((1 << ((endBit & 7) + 1)) - 1))
                | (byte)(val >> (endBit - startBit - (endBit & 7))));
        }
        return state[startBit >> 3] != first || state[endBit >> 3] != last;
    }

    /// <summary>
    /// What the system generator reads of the story (CHMMR_UNLEASHED for
    /// Procyon's starbase, SPATHI_SHIELDED_SELVES for Spathiwa's shield).
    /// </summary>
    public SystemStoryState SystemStory =>
        new(ChmmrUnleashed: Get("CHMMR_UNLEASHED") != 0, SpathiShieldedSelves: Get("SPATHI_SHIELDED_SELVES") != 0);

    // ---------------------------------------------------------------- beside the bits

    /// <summary>CrewCost: resource units a crew member.</summary>
    public int CrewCost { get; private set; }

    /// <summary>FuelCost: resource units a unit of fuel.</summary>
    public int FuelCost { get; private set; }

    /// <summary>ModuleCost: each module's price, in units of <see cref="StoryRules.ModuleCostScale"/> resource units (0: not for sale).</summary>
    public int ModuleCost(string module) =>
        Rules.Modules.Contains(module) ? _moduleCost.GetValueOrDefault(module) : throw new KeyNotFoundException($"no module {module}");

    /// <summary>Sets a module's price (the Melnorme's and the Chmmr's scripts do).</summary>
    public void SetModuleCost(string module, int cost)
    {
        if (!Rules.Modules.Contains(module))
            throw new KeyNotFoundException($"no module {module}");
        if (cost < 0 || cost > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cost), "a module's cost is a byte (ModuleCost[])");
        if (_moduleCost.GetValueOrDefault(module) != cost)
        {
            _moduleCost[module] = cost;
            ++Version;
        }
    }

    /// <summary>ElementWorth: each element category's worth a unit (set at the start, never changed in UQM).</summary>
    public IReadOnlyList<int> ElementWorth => Rules.ElementWorth;

    /// <summary>A race's standing (CheckAlliance), by its ship name (ARILOU_SHIP and so on, data/races.json).</summary>
    public Allied Alliance(string race) => _alliances[Rules.RaceIndex(race)];

    /// <summary>Every race's standing, in data/races.json's order.</summary>
    public IReadOnlyList<Allied> Alliances => _alliances;

    /// <summary>
    /// Sets a race's standing, whatever it was (SetRaceAllied, which leaves a
    /// dead race dead, is the scripts' call and comes with them).
    /// </summary>
    public void SetAlliance(string race, Allied state)
    {
        int i = Rules.RaceIndex(race);
        if (_alliances[i] != state)
        {
            _alliances[i] = state;
            ++Version;
        }
    }

    /// <summary>The crew bought at the starbase (CREW_PURCHASED0 and 1, low byte first).</summary>
    public int CrewPurchased => Get("CREW_PURCHASED0") | (Get("CREW_PURCHASED1") << 8);

    /// <summary>
    /// CrewTransaction (shipyard.c): crew bought (or, negative, sent away)
    /// at the starbase. The crew price rises by 2 once the crew bought
    /// reaches CREW_EXPENSE_THRESHOLD and falls by 2 below it again; with
    /// the Shofixti allied, the count no longer changes.
    /// </summary>
    public void CrewTransaction(int crewDelta)
    {
        if (crewDelta == 0)
            return;
        if (crewDelta < short.MinValue || crewDelta > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(crewDelta));
        int threshold = Rules.CrewExpenseThreshold;
        // SIZE is a signed 16-bit count in UQM.
        int bought = (short)(CrewPurchased + crewDelta);
        if (bought < 0)
            bought = crewDelta < 0 ? 0 : 0x7FFF;
        else if (crewDelta > 0)
        {
            if (bought >= threshold && bought - crewDelta < threshold)
                SetCrewCost((byte)(CrewCost + 2));   // CrewCost is a BYTE
        }
        else if (bought < threshold && bought - crewDelta >= threshold)
            SetCrewCost((byte)(CrewCost - 2));
        if (Alliance("SHOFIXTI_SHIP") != Allied.Good)
        {
            Set("CREW_PURCHASED0", bought & 0xFF);
            Set("CREW_PURCHASED1", (bought >> 8) & 0xFF);
        }
    }

    /// <summary>Sets the fuel price (FuelCost; UQM never changes it after the start).</summary>
    public void SetFuelCost(int cost)
    {
        if (cost < 1 || cost > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cost), "the fuel cost is a COUNT, and not 0 (ChangeFuelQuantity divides by it)");
        if (FuelCost != cost)
        {
            FuelCost = cost;
            ++Version;
        }
    }

    /// <summary>Sets the crew price (gameev.c's SHOFIXTI_RETURN_EVENT lowers it too).</summary>
    public void SetCrewCost(int cost)
    {
        if (cost < 0 || cost > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cost), "the crew cost is a byte (CrewCost)");
        if (CrewCost != cost)
        {
            CrewCost = cost;
            ++Version;
        }
    }

    // ---------------------------------------------------------------- events

    /// <summary>
    /// AddEvent (clock.c): queues an event on its date, or runs it at once
    /// if it's relative and due now (all of months, days and years 0).
    /// A date already past isn't queued. False if it wasn't queued.
    /// </summary>
    public bool AddEvent(bool relative, int months, int days, int years, string evt, GameClock clock)
    {
        if (relative && months == 0 && days == 0 && years == 0)
        {
            RunEvent(evt, clock);
            return false;
        }
        return Events.Add(relative, months, days, years, evt, clock);
    }

    /// <summary>
    /// EventHandler (gameev.c), for the events that only change the story
    /// state. The others (the races' missions, the Arilou, encounters) come
    /// with stage 3c; until then they wait in the queue.
    /// </summary>
    public void RunEvent(string evt, GameClock clock)
    {
        switch (evt)
        {
            case "SLYLANDRO_RAMP_UP":
                if (Get("DESTRUCT_CODE_ON_SHIP") == 0)
                {
                    int ramp = (byte)(Get("SLYLANDRO_MULTIPLIER") + 1);
                    if (ramp <= 4)
                    {
                        Set("SLYLANDRO_MULTIPLIER", ramp);
                        AddEvent(true, 0, 182, 0, "SLYLANDRO_RAMP_UP", clock);
                    }
                }
                break;
            case "SLYLANDRO_RAMP_DOWN":
            {
                int ramp = (byte)(Get("SLYLANDRO_MULTIPLIER") - 1);
                if (ramp != 0)
                    AddEvent(true, 0, 23, 0, "SLYLANDRO_RAMP_DOWN", clock);
                Set("SLYLANDRO_MULTIPLIER", ramp);
                break;
            }
            default:
                if (!Rules.EventIds.ContainsKey(evt))
                    throw new KeyNotFoundException($"no event {evt}");
                throw new NotSupportedException($"{evt} runs from stage 3c (docs/plans/stage-3.md)");
        }
    }

    // ---------------------------------------------------------------- saving

    /// <summary>The story state in a save: the fields that aren't 0, by name, and everything beside them.</summary>
    public sealed record Saved(
        Dictionary<string, int> Fields, int CrewCost, int FuelCost, Dictionary<string, int> ModuleCost,
        Dictionary<string, Allied> Alliances, List<StoryEvent> Events);

    public Saved Save() => new(
        Table.Fields.Where(f => !NotShared.ContainsKey(f.Name) && Get(f.Name) != 0).ToDictionary(f => f.Name, f => Get(f.Name)),
        CrewCost, FuelCost,
        Rules.Modules.Where(m => _moduleCost.GetValueOrDefault(m) != 0).ToDictionary(m => m, m => _moduleCost[m]),
        Rules.Races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => _alliances[x.i]),
        Events.ToList());

    /// <summary>A saved story state. A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static StoryState Restore(StoryRules rules, Saved saved)
    {
        var story = new StoryState(rules);
        foreach (var (name, value) in saved.Fields ?? new())
        {
            if (!rules.Table.Contains(name))
                throw new InvalidDataException($"Story state: no field {name}.");
            if (NotShared.ContainsKey(name))
                throw new InvalidDataException($"Story state: {name} isn't shared story state.");
            var f = rules.Table[name];
            if (value < 0 || value > f.Max)
                throw new InvalidDataException($"Story state: {name} is {value}, more than its {f.Bits} bits hold.");
            story.Set(name, value);
        }
        story.CrewCost = Byte(saved.CrewCost, "crew cost");
        story.FuelCost = Byte(saved.FuelCost, "fuel cost");
        story._moduleCost.Clear();
        foreach (var (module, cost) in saved.ModuleCost ?? new())
        {
            if (!rules.Modules.Contains(module))
                throw new InvalidDataException($"Story state: no module {module}.");
            story._moduleCost[module] = Byte(cost, module + "'s cost");
        }
        var alliances = saved.Alliances ?? new();
        if (alliances.Count != rules.Races.Count || rules.Races.Any(r => !alliances.ContainsKey(r)))
            throw new InvalidDataException("Story state: every race's standing is needed, once.");
        for (int i = 0; i < rules.Races.Count; ++i)
        {
            var a = alliances[rules.Races[i]];
            if (!Enum.IsDefined(a))
                throw new InvalidDataException($"Story state: {rules.Races[i]}'s standing is {(int)a}.");
            story._alliances[i] = a;
        }
        story.Events.Restore(saved.Events ?? new());
        story.Version = 0;
        return story;

        static int Byte(int value, string what) => value is >= 0 and <= byte.MaxValue
            ? value : throw new InvalidDataException($"Story state: the {what} is {value}, not a byte.");
    }
}
