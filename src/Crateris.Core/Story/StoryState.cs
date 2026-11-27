// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (globdata.c getGameState, setGameState,
// getGameState32, setGameState32 and InitGameStructures; shipyard.c
// CrewTransaction; races.h allied_state and FLEET_INFO; clock.c AddEvent and
// processClockDayEvents; gameev.c EventHandler, arilou_entrance_event,
// arilou_exit_event, SetRaceDest and check_race_growth; starcon.c
// checkArilouGate; build.c StartSphereTracking),
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
/// worths, each race's standing (allied_state in avail_race_q) and fleet
/// (the rest of its FLEET_INFO: <see cref="RaceFleet"/>), and the clock's
/// events. One instance per game, on the host; clients are sent it
/// (<see cref="StorySnapshot"/>: the fleets and events stay on the host).
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
        ["ESCAPE_COUNTER"] = "the escape counter after a battle or an alien's talk: each ship's own (IpFlight.EscapeCounter)",
        ["BATTLE_PLANET"] = "the planet a battle is fought by: each battle's own",
        ["IMPROVED_LANDER_SPEED"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["IMPROVED_LANDER_CARGO"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["IMPROVED_LANDER_SHOT"] = "a lander upgrade: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["LANDER_SHIELDS"] = "the lander's shields: each ship's own (ShipHold.Upgrades, stage 2b)",
        ["MELNORME_CREDIT0"] = "Melnorme credits: each player's own (docs/differences.md row 19)",
        ["MELNORME_CREDIT1"] = "Melnorme credits: each player's own (docs/differences.md row 19)",
        ["MELNORME_RESCUE_REFUSED"] = "whether a player refused the Melnorme's rescue: each player's own (PlayerShip.MelnormeRescueRefused, the stage 3b-2 plan's question 7)",
        ["MELNORME_RESCUE_COUNT"] = "how many times the Melnorme rescued a player: each player's own (PlayerShip.MelnormeRescueCount, question 7)",
        ["USED_BROADCASTER"] = "a player used the broadcaster since their last roll: each player's own (PlayerShip.UsedBroadcaster, stage 3b-2 item 4)",
        ["ARILOU_SPACE_SIDE"] = "whether a ship is in hyperspace or QuasiSpace: each ship's own",
        ["PLANETARY_LANDING"] = "whether a lander is down: each ship's own (PlayerShip.Lander, stage 2b)",
        ["PLANETARY_CHANGE"] = "whether a visit changed its world (for UQM's star info file): taken nodes are saved at once (PlanetWorldState)",
    };

    /// <summary>
    /// The fields shared until stage 3b-2 item 4 made them each player's own
    /// (the Melnorme's rescue fields, the broadcaster's use): a save from
    /// before has them in its story, which loads them as the host's own
    /// (<see cref="LoadedPlayerFields"/>) rather than refusing the save.
    /// </summary>
    public static readonly IReadOnlySet<string> MovedToPlayers = new HashSet<string>
    {
        "MELNORME_RESCUE_REFUSED", "MELNORME_RESCUE_COUNT", "USED_BROADCASTER",
    };

    /// <summary>The <see cref="MovedToPlayers"/> fields a restored story had, for the host's own.</summary>
    public Dictionary<string, int> LoadedPlayerFields { get; } = new();

    private readonly byte[] _bits;
    private readonly Dictionary<string, int> _moduleCost;
    private readonly Allied[] _alliances;
    private readonly RaceFleet[] _fleets;

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
        _fleets = NewFleets(rules);
        CrewCost = rules.CrewCost;
        FuelCost = rules.FuelCost;
        Events = new EventQueue(rules, () => ++Version);
    }

    /// <summary>
    /// A new game's story state, as InitGameStructures and
    /// AddInitialGameEvents leave it on <paramref name="clock"/>'s date:
    /// every field 0, every race but the humans not allied, each race's
    /// fleet as its ship starts it (<see cref="RaceFleet.NewGame"/>), UQM's
    /// prices, and the first events, of which the one due at once
    /// (SLYLANDRO_RAMP_UP) has run.
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
    /// Sets a race's standing, whatever it was (the scripts' call is
    /// <see cref="SetRaceAllied"/>, which leaves a dead race dead).
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

    /// <summary>
    /// SetRaceAllied (build.c:210), the scripts' call: a race's alliance
    /// made (<paramref name="flag"/>: GOOD_GUY) or ended (BAD_GUY), which
    /// puts its ships on sale at the shipyard or takes them off; a dead race
    /// stays dead ("strange request, silently ignore it"). Returns whether
    /// the standing changed.
    /// </summary>
    public bool SetRaceAllied(string race, bool flag)
    {
        if (Alliance(race) == Allied.Dead || Alliance(race) == (flag ? Allied.Good : Allied.Bad))
            return false;
        SetAlliance(race, flag ? Allied.Good : Allied.Bad);
        return true;
    }

    // ---------------------------------------------------------------- the fleets

    // InitGameStructures' avail_race_q: every race's fleet as its ship
    // starts it, or none for rules made without the fleets.
    private static RaceFleet[] NewFleets(StoryRules rules) =>
        rules.Fleets.Count == rules.Races.Count
            ? rules.Fleets.Select(RaceFleet.NewGame).ToArray()
            : rules.Races.Select(r => new RaceFleet(0, 0, 0, 0, 0, 0, 0, 0, 255 >> 1, 0, null, 0, 0)).ToArray();

    /// <summary>A race's fleet (its FLEET_INFO but the standing), by its ship name (ARILOU_SHIP and so on).</summary>
    public RaceFleet Fleet(string race) => _fleets[Rules.RaceIndex(race)];

    /// <summary>Every race's fleet, in data/races.json's order (avail_race_q's).</summary>
    public IReadOnlyList<RaceFleet> Fleets => _fleets;

    /// <summary>
    /// Sets a race's fleet, as stage 3c's events set its fields (the
    /// Thraddash's and Ilwrath's missions their growth, the genocide its
    /// strength). One a FLEET_INFO can't hold is refused.
    /// </summary>
    public void SetFleet(string race, RaceFleet fleet)
    {
        int i = Rules.RaceIndex(race);
        if (fleet.Invalid(Rules) is { } why)
            throw new ArgumentException($"{race}'s fleet: {why}", nameof(fleet));
        SetFleet(i, fleet);
    }

    private void SetFleet(int i, RaceFleet fleet)
    {
        if (_fleets[i] != fleet)
        {
            _fleets[i] = fleet;
            ++Version;
        }
    }

    /// <summary>
    /// SetRaceDest (gameev.c): a race's fleet heads for (x, y), arriving in
    /// <paramref name="daysLeft"/> days (check_race_growth moves it), and
    /// runs <paramref name="arrival"/> then (null for nothing). Stage 3c's
    /// events call it.
    /// </summary>
    public void SetRaceDest(string race, int x, int y, int daysLeft, string? arrival) =>
        SetFleet(race, Fleet(race) with { DestLocX = x, DestLocY = y, DaysLeft = daysLeft, Arrival = arrival });

    /// <summary>
    /// StartSphereTracking (build.c): the race's sphere is to be shown on
    /// the starmap (its strength known as 1 and its centre as now, until
    /// the starmap looks again), unless it's known already, has none or is
    /// met anywhere. False if the race is extinct, as UQM's 0; UQM returns
    /// the race's number otherwise, which for the Arilou (0) is false too,
    /// though their sphere is tracked: so is it here (no caller asks it of
    /// them). InitCommunication calls it for every talk's race; the worlds
    /// that start a talk ask it whether the race lives.
    /// </summary>
    public bool StartSphereTracking(string race)
    {
        int i = Rules.RaceIndex(race);
        var f = _fleets[i];
        if (f.ActualStrength == 0)
        {
            if (_alliances[i] == Allied.Dead)
                return false;
        }
        else if (f.KnownStrength == 0 && f.ActualStrength != Rules.InfiniteRadius)
            SetFleet(i, f with { KnownStrength = 1, KnownLocX = f.LocX, KnownLocY = f.LocY });
        return i != 0;
    }

    /// <summary>
    /// What <see cref="StartSphereTracking"/> would answer, without
    /// tracking the sphere: false if the race is extinct (or the Arilou).
    /// </summary>
    public bool WouldTrackSphere(string race)
    {
        int i = Rules.RaceIndex(race);
        return !(_fleets[i].ActualStrength == 0 && _alliances[i] == Allied.Dead) && i != 0;
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
    /// The events the port runs (EventHandler's cases): the daily hyperspace
    /// encounters and the Slylandro ramps (stage 3b-1), and since stage 3c
    /// item 1 the events with no mission: the Arilou's portal opening and
    /// closing, their look at the Umgah, the Shofixti's return, the
    /// Spathi's shield, the Zoq-Fot-Pik's distress and death, and the Yehat
    /// rebels. The others (the races' missions, the Kohr-Ah's victory and
    /// genocide: stage 3c items 3 and 4) are still set aside on their day
    /// (<see cref="EventQueue.SetAside"/>, stage 3b decision 9).
    /// </summary>
    public static readonly IReadOnlySet<string> EventsRun = new HashSet<string>
    {
        "HYPERSPACE_ENCOUNTER_EVENT", "SLYLANDRO_RAMP_UP", "SLYLANDRO_RAMP_DOWN",
        "ARILOU_ENTRANCE_EVENT", "ARILOU_EXIT_EVENT", "ARILOU_UMGAH_CHECK", "SHOFIXTI_RETURN_EVENT",
        "SPATHI_SHIELD_EVENT", "ZOQFOT_DISTRESS_EVENT", "ZOQFOT_DEATH_EVENT", "YEHAT_REBEL_EVENT",
    };

    /// <summary>ARILOU_SPACE's values (gameev.h): the portal closing or closed (CLOSING, 0), opening or open (OPENING, 1).</summary>
    public const int ArilouClosing = 0, ArilouOpening = 1;

    /// <summary>What ARILOU_SPACE_COUNTER counts up to as the portal opens (checkArilouGate, starcon.c).</summary>
    public const int ArilouCounterOpen = 9;

    /// <summary>
    /// AddEvent (clock.c): queues an event on its date, or runs it at once
    /// if it's relative and due now (all of months, days and years 0); one
    /// that the port doesn't run yet (<see cref="EventsRun"/>) is set aside
    /// then, dated today, as on its day in the queue. A date already past
    /// isn't queued. False if it wasn't queued. <paramref name="playerInSystem"/>
    /// is the location gates' question (<see cref="RunEvent"/>).
    /// </summary>
    public bool AddEvent(bool relative, int months, int days, int years, string evt, GameClock clock,
        Action? hyperspaceRoll = null, Func<string, bool>? playerInSystem = null)
    {
        if (relative && months == 0 && days == 0 && years == 0)
        {
            if (EventsRun.Contains(evt) || !Rules.EventIds.ContainsKey(evt))
                RunEvent(evt, clock, hyperspaceRoll, playerInSystem);
            else
                Events.PutAside(new StoryEvent(evt, clock.Year, clock.Month, clock.Day));
            return false;
        }
        return Events.Add(relative, months, days, years, evt, clock);
    }

    /// <summary>
    /// An event the clock took off the queue (or off the events set aside):
    /// run, or set aside until the port runs it (decision 9); <see cref="News"/>
    /// if running it changed a race, for the players to be told (stage 3b
    /// decision 11; the owner, 2026-11-21).
    /// </summary>
    public sealed record DueEvent(StoryEvent Event, bool SetAside, bool News = false);

    /// <summary>
    /// processClockDayEvents (clock.c), on <paramref name="clock"/>'s day:
    /// takes the events at the queue's head off while they're due and runs
    /// each (<see cref="RunEvent"/>), or sets it aside if the port doesn't
    /// run it yet (decision 9), so it doesn't hold up the rest. UQM
    /// takes the head while its day is today; every event's day comes as
    /// the clock passes it, so taking those due today or before is the
    /// same, and also runs at once, in date order, the events whose day
    /// passed without them: a save's from before stage 3b-1, which ran
    /// none. The events set aside by earlier code that the port now runs
    /// (stage 3c item 1's: the Arilou's, the Shofixti's return, ...) are
    /// run with them, in date order (each before a queued one of its day,
    /// having fallen due first), once, from where the clock is
    /// (docs/plans/stage-3.md's "Story events" row): what they queue is
    /// counted from today (a distress run late gives the Zoq-Fot-Pik six
    /// months from now). <paramref name="hyperspaceRoll"/> is the world's
    /// check_hyperspace_encounter (each player in hyperspace, decision 3),
    /// called as each day's HYPERSPACE_ENCOUNTER_EVENT runs;
    /// <paramref name="playerInSystem"/> the location gates' question
    /// (<see cref="RunEvent"/>). What was taken off, in order.
    /// </summary>
    public List<DueEvent> RunDueEvents(GameClock clock, Action? hyperspaceRoll = null, Func<string, bool>? playerInSystem = null)
    {
        var done = new List<DueEvent>();
        while (true)
        {
            int aside = Events.FirstSetAside(e => EventsRun.Contains(e.Event));
            bool due = Events.HeadDue(clock);
            if (aside >= 0 && (!due || !EventQueue.Before(Events[0], Events.SetAside[aside])))
            {
                var late = Events.TakeSetAside(aside);
                done.Add(new DueEvent(late, false, RunEvent(late.Event, clock, hyperspaceRoll, playerInSystem)));
                continue;
            }
            if (!due)
                break;
            var e = Events.TakeHead();
            if (EventsRun.Contains(e.Event))
                done.Add(new DueEvent(e, false, RunEvent(e.Event, clock, hyperspaceRoll, playerInSystem)));
            else
            {
                done.Add(new DueEvent(e, true));
                Events.PutAside(e);
            }
        }
        return done;
    }

    /// <summary>
    /// EventHandler (gameev.c), for the events the port runs
    /// (<see cref="EventsRun"/>). The others (the races' missions, the
    /// Kohr-Ah's victory and genocide) come with stage 3c's items 3 and 4;
    /// until then the clock sets them aside (<see cref="RunDueEvents"/>),
    /// and running one here throws. True if it changed a race (an
    /// alliance, a death, a shield, a fleet), news for the players.
    /// <para>
    /// The location gates (gameev.c:76-105, 132-135): the Spathi's shield
    /// and the Zoq-Fot-Pik's distress and death wait 7 days at a time
    /// (queued again, 7 days on) while "the player" is in that race's
    /// system, UQM's IN_INTERPLANETARY there; here while any player is,
    /// flying, orbiting, landed or in a meeting, talk or battle there (the
    /// stage 3c plan's question 1): <paramref name="playerInSystem"/>,
    /// asked with the system's gendef.h name (SPATHI_DEFINED,
    /// ZOQFOT_DEFINED), says so (null: nobody is).
    /// </para>
    /// </summary>
    public bool RunEvent(string evt, GameClock clock, Action? hyperspaceRoll = null, Func<string, bool>? playerInSystem = null)
    {
        bool Waits(string special)
        {
            if (playerInSystem?.Invoke(special) != true)
                return false;
            AddEvent(true, 0, 7, 0, evt, clock, hyperspaceRoll, playerInSystem);
            return true;
        }

        switch (evt)
        {
            case "ARILOU_ENTRANCE_EVENT":
                // arilou_entrance_event: the portal opens, for three days.
                Set("ARILOU_SPACE", ArilouOpening);
                AddEvent(true, 0, 3, 0, "ARILOU_EXIT_EVENT", clock, hyperspaceRoll, playerInSystem);
                return false;
            case "ARILOU_EXIT_EVENT":
            {
                // arilou_exit_event: it closes, to open again on the 17th of next month.
                int year = clock.Year, month = clock.Month % 12;
                if (month == 0)
                    ++year;
                ++month;
                Set("ARILOU_SPACE", ArilouClosing);
                AddEvent(false, month, 17, year, "ARILOU_ENTRANCE_EVENT", clock, hyperspaceRoll, playerInSystem);
                return false;
            }
            case "HYPERSPACE_ENCOUNTER_EVENT":
                CheckRaceGrowth(clock, hyperspaceRoll, playerInSystem);
                // if (inHyperSpace ()) check_hyperspace_encounter (): the
                // world's, for each player in hyperspace (stage 3b item 3).
                hyperspaceRoll?.Invoke();
                AddEvent(true, 0, 1, 0, "HYPERSPACE_ENCOUNTER_EVENT", clock);
                return false;
            case "ZOQFOT_DISTRESS_EVENT":
                if (Waits("ZOQFOT_DEFINED"))
                    return false;
                Set("ZOQFOT_DISTRESS", 1);
                AddEvent(true, 6, 0, 0, "ZOQFOT_DEATH_EVENT", clock, hyperspaceRoll, playerInSystem);
                return true;
            case "ZOQFOT_DEATH_EVENT":
            {
                if (Waits("ZOQFOT_DEFINED") || Get("ZOQFOT_DISTRESS") == 0)
                    return false;
                // Not rescued: the Zoq-Fot-Pik die, their fleet gone.
                int i = Rules.RaceIndex("ZOQFOTPIK_SHIP");
                SetFleet(i, _fleets[i] with { ActualStrength = 0 });
                SetAlliance("ZOQFOTPIK_SHIP", Allied.Dead);
                Set("ZOQFOT_DISTRESS", 2);
                return true;
            }
            case "SHOFIXTI_RETURN_EVENT":
                // The Shofixti back and allied, and crew no longer an issue:
                // its price 2 lower (a BYTE, as UQM's), the crew bought counted afresh.
                SetRaceAllied("SHOFIXTI_SHIP", true);
                SetCrewCost((byte)(CrewCost - 2));
                Set("CREW_PURCHASED0", 0);
                Set("CREW_PURCHASED1", 0);
                return true;
            case "SPATHI_SHIELD_EVENT":
            {
                if (Waits("SPATHI_DEFINED"))
                    return false;
                int i = Rules.RaceIndex("SPATHI_SHIP");
                if (_fleets[i].ActualStrength == 0)
                    return false;
                SetRaceAllied("SPATHI_SHIP", false);
                Set("SPATHI_SHIELDED_SELVES", 1);
                SetFleet(i, _fleets[i] with { ActualStrength = 0 });
                return true;
            }
            case "ARILOU_UMGAH_CHECK":
                Set("ARILOU_CHECKED_UMGAH", 2);
                return false;
            case "YEHAT_REBEL_EVENT":
            {
                // The royalists cut to two thirds, the rebels as many, their
                // sphere centred at (5150, 0) and tracked.
                int royalists = Rules.RaceIndex("YEHAT_SHIP"), rebels = Rules.RaceIndex("YEHAT_REBEL_SHIP");
                int strength = (ushort)(_fleets[royalists].ActualStrength * 2 / 3);
                SetFleet(royalists, _fleets[royalists] with { ActualStrength = strength });
                SetFleet(rebels, _fleets[rebels] with { ActualStrength = strength, LocX = 5150, LocY = 0 });
                StartSphereTracking("YEHAT_REBEL_SHIP");
                return true;
            }
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
                return false;
            case "SLYLANDRO_RAMP_DOWN":
            {
                int ramp = (byte)(Get("SLYLANDRO_MULTIPLIER") - 1);
                if (ramp != 0)
                    AddEvent(true, 0, 23, 0, "SLYLANDRO_RAMP_DOWN", clock);
                Set("SLYLANDRO_MULTIPLIER", ramp);
                return false;
            }
            default:
                if (!Rules.EventIds.ContainsKey(evt))
                    throw new KeyNotFoundException($"no event {evt}");
                throw new NotSupportedException($"{evt} runs from stage 3c's items 3 and 4 (docs/plans/stage-3c.md)");
        }
    }

    /// <summary>
    /// checkArilouGate (starcon.c), once a world frame (UQM's, once a
    /// hyperspace frame; shared, so stepped once for everyone): the
    /// portal's counter (ARILOU_SPACE_COUNTER) up by one to
    /// <see cref="ArilouCounterOpen"/> while it's opening, down by one to 0
    /// while it's closing.
    /// </summary>
    public void StepArilouGate()
    {
        int counter = Get("ARILOU_SPACE_COUNTER");
        if (Get("ARILOU_SPACE") == ArilouOpening)
        {
            if (counter < ArilouCounterOpen)
                ++counter;
        }
        else if (counter > 0)
            --counter;
        Set("ARILOU_SPACE_COUNTER", counter);
    }

    /// <summary>
    /// check_race_growth (gameev.c): each race's fleet with a sphere (not
    /// none, not met anywhere) grows or shrinks by its growth, a signed
    /// byte, and one more (or less, for no growth or less) each time its
    /// error term, falling by the growth's fraction each day, comes to the
    /// fraction or below; a fleet down to 0 is extinct (DEAD_GUY), and none
    /// grows past MAX_FLEET_STRENGTH. One still alive and heading somewhere
    /// moves a day's share of the way (its days left divide what's left,
    /// rounding towards 0), and on the last day runs its arrival event
    /// (EventHandler): one the port doesn't run yet is set aside dated
    /// today, as the clock's are (decision 9). Every race starts with no
    /// growth and no heading (InitGameStructures), and only the missions'
    /// events give them any (<see cref="SetRaceDest"/>, <see cref="SetFleet"/>:
    /// stage 3c item 3), so until then nothing changes. UQM's sums are kept:
    /// the strength's in a SIZE, the places' in COORDs.
    /// </summary>
    private void CheckRaceGrowth(GameClock clock, Action? hyperspaceRoll, Func<string, bool>? playerInSystem)
    {
        for (int i = 0; i < _fleets.Length; ++i)
        {
            var f = _fleets[i];
            if (f.ActualStrength == 0 || f.ActualStrength == Rules.InfiniteRadius)
                continue;
            int delta = (sbyte)f.Growth;
            if (f.GrowthErrTerm <= f.GrowthFract)
                delta += delta <= 0 ? -1 : 1;
            int errTerm = (byte)(f.GrowthErrTerm - f.GrowthFract);
            delta = (short)(delta + f.ActualStrength);
            if (delta <= 0)
            {
                delta = 0;
                SetAlliance(Rules.Races[i], Allied.Dead);
            }
            else if (delta > Rules.MaxFleetStrength)
                delta = Rules.MaxFleetStrength;
            f = f with { ActualStrength = delta, GrowthErrTerm = errTerm };
            string? arrival = null;
            if (f.ActualStrength != 0 && f.DaysLeft != 0)
            {
                f = f with
                {
                    LocX = (short)(f.LocX + (f.DestLocX - f.LocX) / f.DaysLeft),
                    LocY = (short)(f.LocY + (f.DestLocY - f.LocY) / f.DaysLeft),
                    DaysLeft = f.DaysLeft - 1,
                };
                if (f.DaysLeft == 0)
                    arrival = f.Arrival;
            }
            SetFleet(i, f);
            if (arrival is not null)
            {
                // (What an arrival's event changes isn't news yet: none the port runs is one before stage 3c item 3.)
                if (EventsRun.Contains(arrival))
                    RunEvent(arrival, clock, hyperspaceRoll, playerInSystem);
                else
                    Events.PutAside(new StoryEvent(arrival, clock.Year, clock.Month, clock.Day));
            }
        }
    }

    /// <summary>
    /// What changed in the story between two saves of it, kept to be taken
    /// back (<see cref="TakeBackFrom"/>): the fields that changed, each as
    /// it was; the crew's price, if it changed; the clock's events (queued
    /// and set aside) as they were; and the races' fleets and standings
    /// that changed, as they were. An opening taken back takes back so the
    /// starbase's two weeks' events and its talks' events run at once
    /// (StarbaseVisit).
    /// </summary>
    public sealed record Changes(
        Dictionary<string, int> Fields, int? CrewCost, List<StoryEvent> Events, List<StoryEvent> SetAside,
        Dictionary<string, RaceFleet> Fleets, Dictionary<string, Allied> Alliances)
    {
        /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/>.</summary>
        public static Changes Between(Saved before, Saved after) => new(
            before.Fields.Keys.Union(after.Fields.Keys)
                .Where(f => before.Fields.GetValueOrDefault(f) != after.Fields.GetValueOrDefault(f))
                .ToDictionary(f => f, f => before.Fields.GetValueOrDefault(f)),
            before.CrewCost != after.CrewCost ? before.CrewCost : null,
            before.Events, before.SetAside ?? new(),
            before.Fleets!.Where(f => f.Value != after.Fleets![f.Key]).ToDictionary(f => f.Key, f => f.Value),
            before.Alliances.Where(a => a.Value != after.Alliances[a.Key]).ToDictionary(a => a.Key, a => a.Value));

        /// <summary>Puts what changed back in <paramref name="story"/> as it was.</summary>
        public void TakeBackFrom(StoryState story)
        {
            foreach (var (field, value) in Fields)
                story.Set(field, value);
            if (CrewCost is int cost)
                story.SetCrewCost(cost);
            story.Events.Restore(Events, SetAside);
            foreach (var (race, fleet) in Fleets)
                story.SetFleet(race, fleet);
            foreach (var (race, standing) in Alliances)
                story.SetAlliance(race, standing);
        }
    }

    // ---------------------------------------------------------------- saving

    /// <summary>
    /// The story state in a save: the fields that aren't 0, by name, and
    /// everything beside them: the clock's events queued, and those set
    /// aside until the port runs them (since save version 13; none before), and each
    /// race's fleet (since stage 3b item 2, in version 13; without them, a
    /// new game's).
    /// </summary>
    public sealed record Saved(
        Dictionary<string, int> Fields, int CrewCost, int FuelCost, Dictionary<string, int> ModuleCost,
        Dictionary<string, Allied> Alliances, List<StoryEvent> Events, List<StoryEvent>? SetAside = null,
        Dictionary<string, RaceFleet>? Fleets = null);

    public Saved Save() => new(
        Table.Fields.Where(f => !NotShared.ContainsKey(f.Name) && Get(f.Name) != 0).ToDictionary(f => f.Name, f => Get(f.Name)),
        CrewCost, FuelCost,
        Rules.Modules.Where(m => _moduleCost.GetValueOrDefault(m) != 0).ToDictionary(m => m, m => _moduleCost[m]),
        Rules.Races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => _alliances[x.i]),
        Events.ToList(), Events.SetAside.ToList(),
        Rules.Races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => _fleets[x.i]));

    /// <summary>A saved story state. A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static StoryState Restore(StoryRules rules, Saved saved)
    {
        var story = new StoryState(rules);
        foreach (var (name, value) in saved.Fields ?? new())
        {
            if (!rules.Table.Contains(name))
                throw new InvalidDataException($"Story state: no field {name}.");
            var f = rules.Table[name];
            if (value < 0 || value > f.Max)
                throw new InvalidDataException($"Story state: {name} is {value}, more than its {f.Bits} bits hold.");
            if (MovedToPlayers.Contains(name))
            {
                // Shared before stage 3b-2 item 4: kept for the host's own (HostSave).
                story.LoadedPlayerFields[name] = value;
                continue;
            }
            if (NotShared.ContainsKey(name))
                throw new InvalidDataException($"Story state: {name} isn't shared story state.");
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
        if (saved.Fleets is { } fleets)
        {
            if (fleets.Count != rules.Races.Count || rules.Races.Any(r => !fleets.ContainsKey(r)))
                throw new InvalidDataException("Story state: every race's fleet is needed, once.");
            for (int i = 0; i < rules.Races.Count; ++i)
            {
                var f = fleets[rules.Races[i]]
                    ?? throw new InvalidDataException($"Story state: {rules.Races[i]}'s fleet is missing.");
                if (f.Invalid(rules) is { } why)
                    throw new InvalidDataException($"Story state: {rules.Races[i]}'s fleet: {why}.");
                story._fleets[i] = f;
            }
        }
        story.Events.Restore(saved.Events ?? new(), saved.SetAside);
        story.Version = 0;
        return story;

        static int Byte(int value, string what) => value is >= 0 and <= byte.MaxValue
            ? value : throw new InvalidDataException($"Story state: the {what} is {value}, not a byte.");
    }
}
