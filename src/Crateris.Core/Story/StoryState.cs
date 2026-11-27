// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (globdata.c getGameState, setGameState,
// getGameState32, setGameState32 and InitGameStructures; shipyard.c
// CrewTransaction; races.h allied_state and FLEET_INFO; clock.c AddEvent and
// processClockDayEvents; gameev.c EventHandler, arilou_entrance_event,
// arilou_exit_event, SetRaceDest, check_race_growth and black_urquan_genocide;
// libs/math/sqrt.c square_root (UqmMath); starcon.c
// checkArilouGate; build.c StartSphereTracking),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.Uqm;
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
/// (<see cref="StorySnapshot"/>: the events and the fleets stay on the
/// host but for the spheres the players know, <see cref="StorySnapshot.Fleets"/>,
/// stage 3c item 3).
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
    public static readonly IReadOnlyDictionary<string, string> NotShared = WithHeldDevices(new Dictionary<string, string>
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
    });

    // The devices held by ships (stage 3c item 5), but for those whose field
    // stays as the story's record of found, sold or given (DeviceTable.RecordedFields).
    private static Dictionary<string, string> WithHeldDevices(Dictionary<string, string> fields)
    {
        foreach (string f in DeviceTable.HeldFields.Where(f => !DeviceTable.RecordedFields.Contains(f)))
            fields[f] = "a device held by one ship: each ship's own (ShipHold.Devices, stage 3c item 5)";
        return fields;
    }

    /// <summary>
    /// The fields shared until stage 3b-2 item 4 made them each player's own
    /// (the Melnorme's rescue fields, the broadcaster's use), and stage 3c
    /// item 5 each ship's (the devices held by ships but for the recorded
    /// three, <see cref="DeviceTable.RecordedFields"/>, which stay shared as
    /// their record and are read again for the host's ship by the save): a
    /// save from before has them in its story, which loads them as the
    /// host's own (<see cref="LoadedPlayerFields"/>) rather than refusing
    /// the save.
    /// </summary>
    public static readonly IReadOnlySet<string> MovedToPlayers = new HashSet<string>(
        new[] { "MELNORME_RESCUE_REFUSED", "MELNORME_RESCUE_COUNT", "USED_BROADCASTER" }
            .Concat(DeviceTable.HeldFields.Where(f => !DeviceTable.RecordedFields.Contains(f))));

    /// <summary>
    /// Whether any player's ship holds a device held by ships, by its field
    /// (stage 3c item 5): the game's (GameWorld.AnyShipHolds), for the
    /// story's reads outside talks; none without a game.
    /// </summary>
    public Func<string, bool>? AnyShipHolds { get; set; }

    /// <summary>Whether any player's ship holds a held device's field (<see cref="AnyShipHolds"/>).</summary>
    public bool HeldByAnyShip(string field) =>
        DeviceTable.HeldByShips(field) ? AnyShipHolds?.Invoke(field) ?? false
        : throw new ArgumentException($"{field} isn't a device held by ships", nameof(field));

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

    // ---------------------------------------------------------------- guards gone

    /// <summary>
    /// The world talks' guards destroyed that UQM keeps no story field for,
    /// each standing only while its field holds its value: the Kohr-Ah's
    /// ship at the Zoq-Fot-Pik's world (<c>ZOQFOT_KOHR_AH</c>, while
    /// ZOQFOT_DISTRESS is 1). UQM's one player always gets the Zoq-Fot-Pik's
    /// rescue at once after destroying it (genzoq.c:117-123); here it may
    /// not follow at once (the stage 3c plan's item 1, review rounds 1 and
    /// 2), so the record says it's gone until the rescue: while it stands,
    /// an orbit of their world starts their talk (GameWorld.WorldTalkAt),
    /// and their death event spares them (<see cref="RunEvent"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Field, int Value)> GuardRecords =
        new Dictionary<string, (string, int)> { ["ZOQFOT_KOHR_AH"] = ("ZOQFOT_DISTRESS", 1) };

    /// <summary>The guards recorded gone (<see cref="GuardRecords"/>), saved with the story.</summary>
    public IReadOnlySet<string> GuardsGone => _guardsGone;

    private readonly HashSet<string> _guardsGone = new();

    /// <summary>
    /// Records a guard gone (<see cref="GuardRecords"/>'s name), if its
    /// field holds its value (the distress on); else nothing.
    /// </summary>
    public void MarkGuardGone(string record)
    {
        var (field, value) = GuardRecords.TryGetValue(record, out var r) ? r : throw new KeyNotFoundException($"no guard record {record}");
        if (Get(field) == value && _guardsGone.Add(record))
            ++Version;
    }

    // A record whose field no longer holds its value goes (the distress
    // over: rescued, 0, or the Zoq-Fot-Pik dead, 2), whatever set it.
    private void DropGuardsGone()
    {
        if (_guardsGone.RemoveWhere(g => Get(GuardRecords[g].Field) != GuardRecords[g].Value) > 0)
            ++Version;
    }

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
        {
            ++Version;
            if (_guardsGone.Count > 0)
                DropGuardsGone();
        }
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
        {
            ++Version;
            if (_guardsGone.Count > 0)
                DropGuardsGone();
        }
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
    /// The events the port runs (EventHandler's cases): since stage 3c
    /// item 4, every one gameev.h has. The daily hyperspace encounters and
    /// the Slylandro ramps came with stage 3b-1, the events with no mission
    /// (the Arilou's portal opening and closing, their look at the Umgah,
    /// the Shofixti's return, the Spathi's shield, the Zoq-Fot-Pik's
    /// distress and death, and the Yehat rebels) with stage 3c item 1, the
    /// races' missions with item 3, and the Kohr-Ah's victory and genocide
    /// with item 4. Until then the clock set the others aside on their day
    /// (<see cref="EventQueue.SetAside"/>, stage 3b decision 9): a save
    /// that holds some has them run from where the clock is
    /// (<see cref="RunDueEvents"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> EventsRun = new HashSet<string>
    {
        "HYPERSPACE_ENCOUNTER_EVENT", "SLYLANDRO_RAMP_UP", "SLYLANDRO_RAMP_DOWN",
        "ARILOU_ENTRANCE_EVENT", "ARILOU_EXIT_EVENT", "ARILOU_UMGAH_CHECK", "SHOFIXTI_RETURN_EVENT",
        "SPATHI_SHIELD_EVENT", "ZOQFOT_DISTRESS_EVENT", "ZOQFOT_DEATH_EVENT", "YEHAT_REBEL_EVENT",
        "ADVANCE_PKUNK_MISSION", "ADVANCE_THRADD_MISSION", "ADVANCE_ILWRATH_MISSION", "ADVANCE_MYCON_MISSION",
        "ADVANCE_UTWIG_SUPOX_MISSION", "KOHR_AH_VICTORIOUS_EVENT", "KOHR_AH_GENOCIDE_EVENT",
    };

    /// <summary>The races' missions' events (stage 3c item 3), each also a fleet's arrival.</summary>
    public static readonly IReadOnlySet<string> MissionEvents = new HashSet<string>
    {
        "ADVANCE_PKUNK_MISSION", "ADVANCE_THRADD_MISSION", "ADVANCE_ILWRATH_MISSION", "ADVANCE_MYCON_MISSION",
        "ADVANCE_UTWIG_SUPOX_MISSION",
    };

    /// <summary>
    /// What an event did that the players are told (stage 3b decision 11;
    /// the owner, 2026-11-21: every event that changes a race is told to
    /// every player): the notice, the race it's about (a ship name) and a
    /// second one for an event about two (the Utwig and the Supox, the
    /// Ilwrath and the Thraddash), a number (the crew's new price for the
    /// Shofixti's return) and, for a mission's step, what the fleets are
    /// doing in words (<see cref="NoticeEvent.Mission"/>: "have set off to
    /// attack the Kohr-Ah").
    /// </summary>
    public sealed record StoryNews(NoticeEvent Event, string Race, string? Also = null, int Count = 0, string Detail = "");

    // What the events run have told, in order, until taken (TakeNews).
    private readonly List<StoryNews> _news = new();

    /// <summary>How many pieces of news the events have told that haven't been taken (<see cref="TakeNews"/>).</summary>
    public int NewsCount => _news.Count;

    /// <summary>
    /// The news the events run have told since the <paramref name="from"/>th
    /// not yet taken, in order, taken: the world tells the clock's to every
    /// player (GameWorld.RunDayEvents), a talk keeps what the events it ran
    /// at once told (Talk.AddEvent).
    /// </summary>
    public List<StoryNews> TakeNews(int from = 0)
    {
        var taken = _news.Skip(from).ToList();
        _news.RemoveRange(from, _news.Count - from);
        return taken;
    }

    private void Tell(NoticeEvent ev, string race, string? also = null, int count = 0, string detail = "") =>
        _news.Add(new StoryNews(ev, race, also, count, detail));

    // A mission's step, told in words.
    private void TellMission(string race, string detail, string? also = null) => Tell(NoticeEvent.Mission, race, also, 0, detail);

    /// <summary>ARILOU_SPACE's values (gameev.h): the portal closing or closed (CLOSING, 0), opening or open (OPENING, 1).</summary>
    public const int ArilouClosing = 0, ArilouOpening = 1;

    /// <summary>What ARILOU_SPACE_COUNTER counts up to as the portal opens (checkArilouGate, starcon.c).</summary>
    public const int ArilouCounterOpen = 9;

    /// <summary>
    /// AddEvent (clock.c): queues an event on its date, or runs it at once
    /// if it's relative and due now (all of months, days and years 0). A
    /// date already past isn't queued. False if it wasn't queued.
    /// <paramref name="playerInSystem"/> is the location gates' question
    /// (<see cref="RunEvent"/>).
    /// </summary>
    public bool AddEvent(bool relative, int months, int days, int years, string evt, GameClock clock,
        Action? hyperspaceRoll = null, Func<string, bool>? playerInSystem = null)
    {
        if (relative && months == 0 && days == 0 && years == 0)
        {
            Run(evt, clock, hyperspaceRoll, playerInSystem);
            return false;
        }
        return Events.Add(relative, months, days, years, evt, clock);
    }

    /// <summary>
    /// An event the clock took off the queue (or off the events a save set
    /// aside, decision 9) and ran; <see cref="News"/>
    /// if running it changed a race, for the players to be told (stage 3b
    /// decision 11; the owner, 2026-11-21: <see cref="TakeNews"/> has what);
    /// <see cref="Held"/> if its location gate held it, queued again 7 days
    /// on (<see cref="RunEvent"/>). <see cref="Arrival"/>: run by a fleet's
    /// arrival (check_race_growth's own EventHandler call, as the day's
    /// HYPERSPACE_ENCOUNTER_EVENT moved the fleet), not taken off the
    /// queue; listed after that day's event.
    /// </summary>
    public sealed record DueEvent(StoryEvent Event, bool News = false, bool Held = false, bool Arrival = false);

    // The arrivals check_race_growth ran while the day's event ran, for RunDueEvents' list.
    private readonly List<DueEvent> _arrivals = new();

    /// <summary>
    /// processClockDayEvents (clock.c), on <paramref name="clock"/>'s day:
    /// takes the events at the queue's head off while they're due and runs
    /// each (<see cref="RunEvent"/>). UQM
    /// takes the head while its day is today; every event's day comes as
    /// the clock passes it, so taking those due today or before is the
    /// same, and also runs at once, in date order, the events whose day
    /// passed without them: a save's from before stage 3b-1, which ran
    /// none. The events a save holds set aside (by the port before stage
    /// 3c item 4, which didn't run them yet: <see cref="EventQueue.SetAside"/>)
    /// are run with them, in date order (each before a queued one of its day,
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
            bool due = Events.HeadDue(clock);
            if (Events.SetAside.Count > 0 && (!due || !EventQueue.Before(Events[0], Events.SetAside[0])))
            {
                var late = Events.TakeSetAside();
                var (news, held) = Run(late.Event, clock, hyperspaceRoll, playerInSystem);
                done.Add(new DueEvent(late, news, held));
                done.AddRange(_arrivals);
                _arrivals.Clear();
                continue;
            }
            if (!due)
                break;
            var e = Events.TakeHead();
            var (ran, wasHeld) = Run(e.Event, clock, hyperspaceRoll, playerInSystem);
            done.Add(new DueEvent(e, ran, wasHeld));
            done.AddRange(_arrivals);
            _arrivals.Clear();
        }
        return done;
    }

    /// <summary>
    /// EventHandler (gameev.c), every event's (<see cref="EventsRun"/>).
    /// True if it changed a race (an alliance, a death, a shield, a
    /// fleet), news for the players, kept until taken (<see cref="TakeNews"/>;
    /// and <see cref="DueEvent.Held"/> says whether its gate held it).
    /// <para>
    /// The location gates (gameev.c:68-105, 132-135): the Spathi's shield
    /// and the Zoq-Fot-Pik's distress and death wait 7 days at a time
    /// (queued again, 7 days on) while "the player" is in that race's
    /// system, UQM's IN_INTERPLANETARY there, and the Kohr-Ah's genocide
    /// (their victory's too, which falls through to it) while the player
    /// is at the Sa-Matra before the frenzy has begun (queued again as the
    /// genocide); here while any player is,
    /// flying, orbiting, landed or in a meeting, talk or battle there (the
    /// stage 3c plan's question 1): <paramref name="playerInSystem"/>,
    /// asked with the system's gendef.h name (SPATHI_DEFINED,
    /// ZOQFOT_DEFINED, SAMATRA_DEFINED), says so (null: nobody is).
    /// </para>
    /// </summary>
    public bool RunEvent(string evt, GameClock clock, Action? hyperspaceRoll = null, Func<string, bool>? playerInSystem = null)
    {
        bool news = Run(evt, clock, hyperspaceRoll, playerInSystem).News;
        // The arrivals it ran are listed only by RunDueEvents: not kept for a later day's list.
        _arrivals.Clear();
        return news;
    }

    // RunEvent's work: whether the event changed a race (told news), and whether its location gate held it (queued again).
    private (bool News, bool Held) Run(string evt, GameClock clock, Action? hyperspaceRoll, Func<string, bool>? playerInSystem)
    {
        int told = _news.Count, byArrivals = _toldByArrivals;
        bool held = RunHandler(evt, clock, hyperspaceRoll, playerInSystem);
        // A fleet's arrival's news is its own (its DueEvent's), not the day's event's.
        return (_news.Count - told - (_toldByArrivals - byArrivals) > 0, held);
    }

    // How many pieces of news the fleets' arrivals have told (CheckRaceGrowth), for Run to tell its own apart.
    private int _toldByArrivals;

    // EventHandler's cases: true if the event's location gate held it (queued again).
    private bool RunHandler(string evt, GameClock clock, Action? hyperspaceRoll, Func<string, bool>? playerInSystem)
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
                    return true;
                Set("ZOQFOT_DISTRESS", 1);
                AddEvent(true, 6, 0, 0, "ZOQFOT_DEATH_EVENT", clock, hyperspaceRoll, playerInSystem);
                Tell(NoticeEvent.Distress, "ZOQFOTPIK_SHIP");
                return false;
            case "ZOQFOT_DEATH_EVENT":
            {
                if (Waits("ZOQFOT_DEFINED"))
                    return true;
                // Rescued; or their Kohr-Ah ship destroyed with the rescue to come at the next orbit (GuardsGone):
                // spared, the event spent, as UQM's rescue would have followed the ship's destruction at once.
                if (Get("ZOQFOT_DISTRESS") == 0 || _guardsGone.Contains("ZOQFOT_KOHR_AH"))
                    return false;
                // Not rescued: the Zoq-Fot-Pik die, their fleet gone.
                int i = Rules.RaceIndex("ZOQFOTPIK_SHIP");
                SetFleet(i, _fleets[i] with { ActualStrength = 0 });
                SetAlliance("ZOQFOTPIK_SHIP", Allied.Dead);
                Set("ZOQFOT_DISTRESS", 2);
                Tell(NoticeEvent.Extinct, "ZOQFOTPIK_SHIP");
                return false;
            }
            case "SHOFIXTI_RETURN_EVENT":
                // The Shofixti back and allied, and crew no longer an issue:
                // its price 2 lower (a BYTE, as UQM's), the crew bought counted afresh.
                SetRaceAllied("SHOFIXTI_SHIP", true);
                SetCrewCost((byte)(CrewCost - 2));
                Set("CREW_PURCHASED0", 0);
                Set("CREW_PURCHASED1", 0);
                Tell(NoticeEvent.Returned, "SHOFIXTI_SHIP", count: CrewCost);
                return false;
            case "SPATHI_SHIELD_EVENT":
            {
                if (Waits("SPATHI_DEFINED"))
                    return true;
                int i = Rules.RaceIndex("SPATHI_SHIP");
                if (_fleets[i].ActualStrength == 0)
                    return false;
                SetRaceAllied("SPATHI_SHIP", false);
                Set("SPATHI_SHIELDED_SELVES", 1);
                SetFleet(i, _fleets[i] with { ActualStrength = 0 });
                Tell(NoticeEvent.Shielded, "SPATHI_SHIP");
                return false;
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
                Tell(NoticeEvent.CivilWar, "YEHAT_SHIP");
                return false;
            }
            case "ADVANCE_PKUNK_MISSION":
                PkunkMission(clock);
                return false;
            case "ADVANCE_THRADD_MISSION":
                ThraddMission(clock, hyperspaceRoll, playerInSystem);
                return false;
            case "ADVANCE_ILWRATH_MISSION":
                IlwrathMission();
                return false;
            case "ADVANCE_MYCON_MISSION":
                MyconMission(clock);
                return false;
            case "ADVANCE_UTWIG_SUPOX_MISSION":
                UtwigSupoxMission(clock);
                return false;
            case "KOHR_AH_VICTORIOUS_EVENT":
                // The Kohr-Ah have won their war with the Ur-Quan: the
                // genocide begins now, or a year on while the Utwig and the
                // Supox fight them (UTWIG_SUPOX_MISSION, ever set: it's 6 once
                // they're home).
                if (Get("UTWIG_SUPOX_MISSION") != 0)
                {
                    AddEvent(true, 0, 0, 1, "KOHR_AH_GENOCIDE_EVENT", clock, hyperspaceRoll, playerInSystem);
                    return false;
                }
                goto case "KOHR_AH_GENOCIDE_EVENT";   // FALLTHROUGH, gate and all
            case "KOHR_AH_GENOCIDE_EVENT":
                // Before the frenzy, held while a player is at the Sa-Matra (queued again as the genocide).
                if (Get("KOHR_AH_FRENZY") == 0 && playerInSystem?.Invoke("SAMATRA_DEFINED") == true)
                {
                    AddEvent(true, 0, 7, 0, "KOHR_AH_GENOCIDE_EVENT", clock, hyperspaceRoll, playerInSystem);
                    return true;
                }
                BlackUrquanGenocide();
                return false;
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
                throw new KeyNotFoundException($"no event {evt}");
        }
    }

    // ---------------------------------------------------------------- the missions (stage 3c item 3)

    // Where the missions go (gameev.c): the Yehat's space and the Pkunk's
    // home; the Kohr-Ah and the Thraddash's home; where the Ilwrath meet the
    // Thraddash, and the midpoint they fight to (MADD_LENGTH days of
    // losses); Organon and where the Mycon go after; the Utwig's and the
    // Supox's fronts and homes.
    private const int YehatX = 4970, YehatY = 400, PkunkHomeX = 502, PkunkHomeY = 401;
    private const int KohrAhX = 4879, KohrAhY = 7201, ThraddHomeX = 2535, ThraddHomeY = 8358;
    private const int IlwrathFrontX = 2500, IlwrathFrontY = 8070, MaddLength = 128;
    private const int WarX = (IlwrathFrontX + ThraddHomeX) >> 1, WarY = (IlwrathFrontY + ThraddHomeY) >> 1;
    private const int OrganonX = 6858, OrganonY = 577;

    // A fleet's daily loss that takes away `loss` over `days` days (the
    // missions' growth: a BYTE of the negated whole part, and the
    // remainder in 256ths; strength_loss is a SIZE).
    private static (int Growth, int Fract) Losing(int loss, int days) =>
        ((byte)(-(short)loss / days), (byte)((((short)loss % days) << 8) / days));

    /// <summary>
    /// pkunk_mission (gameev.c:405-467): the Pkunk's migration to the
    /// Yehat (PKUNK_MISSION odd while they go), over half a year less what
    /// they've already travelled; turned home by a good reason given on the
    /// way (an odd state run before they arrive), taking as long as they've
    /// travelled; back home, three months' wait before they set off again;
    /// arrived at the Yehat's, absorbed (YEHAT_ABSORBED_PKUNK, the Pkunk
    /// extinct, the Yehat's sphere tracked). Nothing with no Pkunk fleet.
    /// </summary>
    private void PkunkMission(GameClock clock)
    {
        int i = Rules.RaceIndex("PKUNK_SHIP");
        var f = _fleets[i];
        if (f.ActualStrength == 0)
            return;
        int state = Get("PKUNK_MISSION");
        if (f.DaysLeft == 0 && state != 0)
        {
            if ((state & 1) != 0 || (f.LocX == YehatX && f.LocY == YehatY))
                f = f with { ActualStrength = 0 };   // made it to Yehat space
            else if (f.LocX == PkunkHomeX && f.LocY == PkunkHomeY && Get("PKUNK_ON_THE_MOVE") != 0)
            {
                Set("PKUNK_ON_THE_MOVE", 0);
                AddEvent(true, 3, 0, 0, "ADVANCE_PKUNK_MISSION", clock);
                TellMission("PKUNK_SHIP", "are back home");
                return;
            }
        }
        if (f.ActualStrength == 0)
        {
            Set("YEHAT_ABSORBED_PKUNK", 1);
            SetFleet(i, f);
            SetAlliance("PKUNK_SHIP", Allied.Dead);
            StartSphereTracking("YEHAT_SHIP");
            TellMission("PKUNK_SHIP", "have reached the Yehat and been absorbed by them: they're no more");
        }
        else
        {
            bool toYehat = (state & 1) == 0;
            Set("PKUNK_ON_THE_MOVE", 1);
            Set("PKUNK_SWITCH", 0);
            SetFleet(i, f with
            {
                DestLocX = toYehat ? YehatX : PkunkHomeX, DestLocY = toYehat ? YehatY : PkunkHomeY,
                DaysLeft = (byte)((365 >> 1) - f.DaysLeft), Arrival = "ADVANCE_PKUNK_MISSION",
            });
            TellMission("PKUNK_SHIP", toYehat ? "have set off to join the Yehat" : "have turned back for home");
        }
        Set("PKUNK_MISSION", state + 1);
    }

    /// <summary>
    /// thradd_mission (gameev.c:469-523): the Thraddash to the Kohr-Ah in 14
    /// days (THRADD_MISSION 0), there losing half their strength over 14
    /// days (1), then home in 14 (2), and home (3, then 4): with the
    /// Ilwrath's war begun, the Ilwrath's mission runs at once.
    /// </summary>
    private void ThraddMission(GameClock clock, Action? hyperspaceRoll, Func<string, bool>? playerInSystem)
    {
        int i = Rules.RaceIndex("THRADDASH_SHIP");
        var f = _fleets[i];
        int state = Get("THRADD_MISSION");
        if (f.ActualStrength != 0 && state < 3)
        {
            bool attacking = state < 2;
            if (state == 1)
            {
                // Arrived at the Kohr-Ah, engaging.
                var (growth, fract) = Losing(f.ActualStrength >> 1, 14);
                f = f with { Growth = growth, GrowthFract = fract, GrowthErrTerm = 255 >> 1 };
            }
            else if (state != 0)
                f = f with { Growth = 0, GrowthFract = 0 };   // stop the losses
            SetFleet(i, f with
            {
                DestLocX = attacking ? KohrAhX : ThraddHomeX, DestLocY = attacking ? KohrAhY : ThraddHomeY,
                DaysLeft = 14, Arrival = "ADVANCE_THRADD_MISSION",
            });
            TellMission("THRADDASH_SHIP", state switch
            {
                0 => "have set off to attack the Kohr-Ah",
                1 => "have reached the Kohr-Ah and are losing ships fighting them",
                _ => "are heading home from the Kohr-Ah",
            });
        }
        else if (f.ActualStrength != 0 && state == 3)
            TellMission("THRADDASH_SHIP", "are back home");
        state = (byte)(state + 1);
        Set("THRADD_MISSION", state);
        if (state == 4 && Get("ILWRATH_FIGHT_THRADDASH") != 0)
            AddEvent(true, 0, 0, 0, "ADVANCE_ILWRATH_MISSION", clock, hyperspaceRoll, playerInSystem);   // returned home: notify the Ilwrath
    }

    /// <summary>
    /// ilwrath_mission (gameev.c:525-600): the Ilwrath to the Thraddash's
    /// borders in 90 days; arrived, the war (ILWRATH_FIGHT_THRADDASH, the
    /// Aqua Helix unguarded, HELIX_UNPROTECTED): both races lose all their
    /// strength over MADD_LENGTH days as the Ilwrath close on the midpoint
    /// in one day less, the Thraddash no longer allies and their visits
    /// counted afresh; the Thraddash at home (or never gone) head for the
    /// midpoint too, those away at the Kohr-Ah recalled home in 10 days
    /// (THRADD_MISSION 3, so the Ilwrath's mission runs again as they
    /// arrive and sends them on); the Ilwrath at the midpoint, both races
    /// dead.
    /// </summary>
    private void IlwrathMission()
    {
        int il = Rules.RaceIndex("ILWRATH_SHIP"), th = Rules.RaceIndex("THRADDASH_SHIP");
        var ilwrath = _fleets[il];
        if (ilwrath.LocX == WarX && ilwrath.LocY == WarY)
        {
            SetFleet(il, ilwrath with { ActualStrength = 0 });
            SetFleet(th, _fleets[th] with { ActualStrength = 0 });
            SetAlliance("ILWRATH_SHIP", Allied.Dead);
            SetAlliance("THRADDASH_SHIP", Allied.Dead);
            TellMission("ILWRATH_SHIP", "have destroyed each other", "THRADDASH_SHIP");
            return;
        }
        if (ilwrath.ActualStrength == 0)
            return;
        if (Get("ILWRATH_FIGHT_THRADDASH") == 0 && (ilwrath.DestLocX != IlwrathFrontX || ilwrath.DestLocY != IlwrathFrontY))
        {
            SetRaceDest("ILWRATH_SHIP", IlwrathFrontX, IlwrathFrontY, 90, "ADVANCE_ILWRATH_MISSION");
            TellMission("ILWRATH_SHIP", "have set off to make war on the Thraddash");
            return;
        }
        bool war = ilwrath.DaysLeft == 0, unallied = false;
        if (war)
        {
            // Arrived for battle.
            Set("ILWRATH_FIGHT_THRADDASH", 1);
            Set("HELIX_UNPROTECTED", 1);
            var (growth, fract) = Losing(ilwrath.ActualStrength, MaddLength);
            ilwrath = ilwrath with
            {
                Growth = growth, GrowthFract = fract,
                DestLocX = WarX, DestLocY = WarY, DaysLeft = MaddLength - 1, Arrival = "ADVANCE_ILWRATH_MISSION",
            };
            SetFleet(il, ilwrath);
            (growth, fract) = Losing(_fleets[th].ActualStrength, MaddLength);
            SetFleet(th, _fleets[th] with { Growth = growth, GrowthFract = fract });
            Set("THRADD_VISITS", 0);
            if (Alliance("THRADDASH_SHIP") == Allied.Good)
                unallied = SetRaceAllied("THRADDASH_SHIP", false);
        }
        int thraddState = Get("THRADD_MISSION");
        bool toWar = thraddState == 0 || thraddState > 3, recalled = !toWar && thraddState < 3;
        if (toWar)
            SetRaceDest("THRADDASH_SHIP", WarX, WarY, (byte)(ilwrath.DaysLeft + 1), null);   // never went to the Kohr-Ah, or returned
        else if (recalled)
        {
            SetRaceDest("THRADDASH_SHIP", ThraddHomeX, ThraddHomeY, 10, "ADVANCE_THRADD_MISSION");   // recall on the double
            Set("THRADD_MISSION", 3);
        }
        if (war)
            TellMission("ILWRATH_SHIP", "are at war: both are losing ships"
                + (recalled ? ", the Thraddash called home from the Kohr-Ah" : "")
                + (unallied ? ", and the Thraddash are no longer allies" : ""), "THRADDASH_SHIP");
        else if (toWar && _fleets[th].ActualStrength != 0)
            TellMission("THRADDASH_SHIP", "have set off to meet the Ilwrath in battle");
        else if (recalled && _fleets[th].ActualStrength != 0)
            TellMission("THRADDASH_SHIP", "are called home from the Kohr-Ah to fight the Ilwrath");
    }

    /// <summary>
    /// mycon_mission (gameev.c:691-728): the Mycon to Organon in 30 days;
    /// there, half their strength lost over 14 days; then, knowing the
    /// ambush (MYCON_KNOW_AMBUSH), on to (6392, 2200) in 30 days, the losses
    /// stopped, with no event on arriving.
    /// </summary>
    private void MyconMission(GameClock clock)
    {
        int i = Rules.RaceIndex("MYCON_SHIP");
        var f = _fleets[i];
        if (f.ActualStrength == 0)
            return;
        if (f.Growth != 0)
        {
            // Head back.
            Set("MYCON_KNOW_AMBUSH", 1);
            SetFleet(i, f with { DestLocX = 6392, DestLocY = 2200, DaysLeft = 30, Arrival = null, Growth = 0, GrowthFract = 0 });
            TellMission("MYCON_SHIP", "are heading back from Organon");
        }
        else if (f.LocX != OrganonX || f.LocY != OrganonY)
        {
            SetRaceDest("MYCON_SHIP", OrganonX, OrganonY, 30, "ADVANCE_MYCON_MISSION");   // to Organon
            TellMission("MYCON_SHIP", "have set off for Organon");
        }
        else
        {
            // Endure losses at Organon.
            AddEvent(true, 0, 14, 0, "ADVANCE_MYCON_MISSION", clock);
            var (growth, fract) = Losing(f.ActualStrength >> 1, 14);
            SetFleet(i, f with { Growth = growth, GrowthFract = fract, GrowthErrTerm = 255 >> 1 });
            TellMission("MYCON_SHIP", "have reached Organon and are losing ships there");
        }
    }

    /// <summary>
    /// utwig_supox_mission (gameev.c:602-689): the Utwig and the Supox to
    /// the Kohr-Ah in 21 days (UTWIG_SUPOX_MISSION 0; the arrival is the
    /// Utwig's), their visits and news counted afresh; there, half of each
    /// one's strength lost over 160 days (1), the event again after 80 (2,
    /// on to 4) and 80 more; then home in 21 days, the losses stopped (4);
    /// home (5, then 6). Nothing but the count with no Utwig fleet.
    /// </summary>
    private void UtwigSupoxMission(GameClock clock)
    {
        int u = Rules.RaceIndex("UTWIG_SHIP"), s = Rules.RaceIndex("SUPOX_SHIP");
        int state = Get("UTWIG_SUPOX_MISSION");
        if (_fleets[u].ActualStrength != 0 && state < 5)
        {
            if (state == 1)
            {
                AddEvent(true, 0, 160 >> 1, 0, "ADVANCE_UTWIG_SUPOX_MISSION", clock);
                var (growth, fract) = Losing(_fleets[u].ActualStrength >> 1, 160);
                SetFleet(u, _fleets[u] with { Growth = growth, GrowthFract = fract, GrowthErrTerm = 255 >> 1 });
                int loss = (short)(_fleets[s].ActualStrength >> 1);
                if (loss != 0)
                {
                    (growth, fract) = Losing(loss, 160);
                    SetFleet(s, _fleets[s] with { Growth = growth, GrowthFract = fract, GrowthErrTerm = 255 >> 1 });
                }
                Set("UTWIG_WAR_NEWS", 0);
                Set("SUPOX_WAR_NEWS", 0);
                TellMission("UTWIG_SHIP", "have reached the Kohr-Ah and are losing ships fighting them", "SUPOX_SHIP");
            }
            else if (state == 2)
            {
                AddEvent(true, 0, 160 >> 1, 0, "ADVANCE_UTWIG_SUPOX_MISSION", clock);
                ++state;
            }
            else
            {
                bool setOff = state == 0;
                if (!setOff)
                {
                    SetFleet(u, _fleets[u] with { Growth = 0, GrowthFract = 0 });
                    SetFleet(s, _fleets[s] with { Growth = 0, GrowthFract = 0 });
                    Set("UTWIG_WAR_NEWS", 0);
                    Set("SUPOX_WAR_NEWS", 0);
                }
                Set("UTWIG_VISITS", 0);
                Set("UTWIG_INFO", 0);
                Set("SUPOX_VISITS", 0);
                Set("SUPOX_INFO", 0);
                SetRaceDest("UTWIG_SHIP", setOff ? 7208 : 8534, setOff ? 7000 : 8797, 21, "ADVANCE_UTWIG_SUPOX_MISSION");
                SetRaceDest("SUPOX_SHIP", setOff ? 6479 : 7468, setOff ? 7541 : 9246, 21, null);
                TellMission("UTWIG_SHIP", setOff ? "have set off to attack the Kohr-Ah" : "are heading home from the Kohr-Ah", "SUPOX_SHIP");
            }
        }
        else if (_fleets[u].ActualStrength != 0 && state == 5)
            TellMission("UTWIG_SHIP", "are back home", "SUPOX_SHIP");
        Set("UTWIG_SUPOX_MISSION", state + 1);
    }

    // ---------------------------------------------------------------- the Kohr-Ah (stage 3c item 4)

    // Sol, where the genocide ends (hyper.h's SOL_X, SOL_Y).
    private const int SolX = 1752, SolY = 1450;

    /// <summary>
    /// black_urquan_genocide (gameev.c:316-401), the Kohr-Ah's victory and
    /// each of their fleet's arrivals: every race (but the Kohr-Ah, the
    /// Ur-Quan and those met anywhere, INFINITE_RADIUS) whose fleet is
    /// where the Kohr-Ah's is (the race they came for, at home or away on
    /// a mission; any with no sphere there too) is killed (DEAD_GUY,
    /// strength 0); of the others still alive, the nearest is next, the
    /// first found of equals, but the Druuge first whenever they live (the
    /// scan stops at them). With none left, the Kohr-Ah head for Sol, and
    /// once there every race is dead: KOHR_AH_KILLED_ALL, the game over
    /// (UQM's CrewEnlisted = ~0; the world's, GameWorld.KohrAhVictorious).
    /// Otherwise KOHR_AH_FRENZY, the Kohr-Ah's and the Ur-Quan's talks
    /// counted afresh, and the Kohr-Ah's fleet heads there at a day for
    /// every 158 units of the way (square_root, at least 1, at most 255),
    /// to run this again on arriving. UQM's types are kept (the ways in
    /// SIZEs, the distances in longs, the days a BYTE). Each race killed
    /// that had a sphere is news (<see cref="NoticeEvent.Extinct"/>, by the
    /// Kohr-Ah), and so is each new course (<see cref="NoticeEvent.Mission"/>,
    /// the race they're heading for named, or Sol).
    /// </summary>
    private void BlackUrquanGenocide()
    {
        int kohrAh = Rules.RaceIndex("BLACK_URQUAN_SHIP"), urquan = Rules.RaceIndex("URQUAN_SHIP");
        int druuge = Rules.RaceIndex("DRUUGE_SHIP");
        var k = _fleets[kohrAh];
        long bestDist = -1;
        short bestDx = (short)(SolX - k.LocX), bestDy = (short)(SolY - k.LocY);
        int best = -1;
        for (int i = 0; i < _fleets.Length; ++i)
        {
            var f = _fleets[i];
            if (i == kohrAh || i == urquan || f.ActualStrength == Rules.InfiniteRadius)
                continue;
            short dx = (short)(f.LocX - k.LocX), dy = (short)(f.LocY - k.LocY);
            if (dx == 0 && dy == 0)
            {
                // Arrived at the victim's home world (or where its fleet is). Cleanse it.
                SetAlliance(Rules.Races[i], Allied.Dead);
                SetFleet(i, f with { ActualStrength = 0 });
                if (f.ActualStrength != 0)
                    Tell(NoticeEvent.Extinct, Rules.Races[i], detail: "by the Kohr-Ah");
            }
            else if (f.ActualStrength != 0)
            {
                long dist = (long)dx * dx + (long)dy * dy;
                if (bestDist < 0 || dist < bestDist || i == druuge)
                {
                    bestDist = dist;
                    bestDx = dx;
                    bestDy = dy;
                    best = i;
                    if (i == druuge)
                        break;   // hNextShip = 0: the Druuge first
                }
            }
        }
        if (bestDist < 0 && bestDx == 0 && bestDy == 0)
        {
            // All spheres of influence are gone: the game over.
            Set("KOHR_AH_KILLED_ALL", 1);
            return;
        }
        // Moving towards the next race to cleanse (or Sol).
        if (bestDist < 0)
            bestDist = (long)bestDx * bestDx + (long)bestDy * bestDy;
        int speed = UqmMath.SquareRoot((uint)bestDist) / 158;
        if (speed == 0)
            speed = 1;
        else if (speed > 255)
            speed = 255;
        bool frenzyBegins = Get("KOHR_AH_FRENZY") == 0;
        Set("KOHR_AH_FRENZY", 1);
        Set("KOHR_AH_VISITS", 0);
        Set("KOHR_AH_REASONS", 0);
        Set("KOHR_AH_PLEAD", 0);
        Set("KOHR_AH_INFO", 0);
        Set("URQUAN_VISITS", 0);
        SetRaceDest("BLACK_URQUAN_SHIP", (short)(k.LocX + bestDx), (short)(k.LocY + bestDy), (byte)speed, "KOHR_AH_GENOCIDE_EVENT");
        string toward = best < 0 ? "Sol" : "the " + Meeting.RaceName(Rules.Races[best]);
        TellMission("BLACK_URQUAN_SHIP", frenzyBegins ? $"have won their war with the Ur-Quan and begun to wipe out every other race: they're heading for {toward}"
            : best < 0 ? $"have wiped out every other race: they're heading for {toward}"
            : $"are heading for {toward}");
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
    /// (EventHandler, with no location gate: the missions' events since
    /// stage 3c item 3, the Kohr-Ah's genocide since item 4, each listed as
    /// an arrival after the day's event in <see cref="RunDueEvents"/>). A
    /// race its losses run out is news (the Ilwrath's
    /// war). Every race starts with no growth and no heading
    /// (InitGameStructures), and only the missions' events give them any
    /// (<see cref="SetRaceDest"/>, <see cref="SetFleet"/>). UQM's sums are kept:
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
                Tell(NoticeEvent.Extinct, Rules.Races[i]);   // its losses ran it out (the Ilwrath's war)
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
                // Run at once (gameev.c's own EventHandler call: no location gate), listed after the day's event.
                var arrived = new StoryEvent(arrival, clock.Year, clock.Month, clock.Day);
                int told = _news.Count;
                var (news, held) = Run(arrival, clock, hyperspaceRoll, playerInSystem);
                _toldByArrivals += _news.Count - told;
                _arrivals.Add(new DueEvent(arrived, news, held, Arrival: true));
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
    /// aside until the port ran them (since save version 13; none before;
    /// none made since stage 3c item 4, which runs every event), and each
    /// race's fleet (since stage 3b item 2, in version 13; without them, a
    /// new game's), and the guards recorded gone (<see cref="GuardsGone"/>,
    /// stage 3c item 1: an optional part, none without it).
    /// </summary>
    public sealed record Saved(
        Dictionary<string, int> Fields, int CrewCost, int FuelCost, Dictionary<string, int> ModuleCost,
        Dictionary<string, Allied> Alliances, List<StoryEvent> Events, List<StoryEvent>? SetAside = null,
        Dictionary<string, RaceFleet>? Fleets = null, List<string>? GuardsGone = null);

    public Saved Save() => new(
        Table.Fields.Where(f => !NotShared.ContainsKey(f.Name) && Get(f.Name) != 0).ToDictionary(f => f.Name, f => Get(f.Name)),
        CrewCost, FuelCost,
        Rules.Modules.Where(m => _moduleCost.GetValueOrDefault(m) != 0).ToDictionary(m => m, m => _moduleCost[m]),
        Rules.Races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => _alliances[x.i]),
        Events.ToList(), Events.SetAside.ToList(),
        Rules.Races.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => _fleets[x.i]),
        _guardsGone.Count == 0 ? null : _guardsGone.OrderBy(g => g, StringComparer.Ordinal).ToList());

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
        foreach (string gone in saved.GuardsGone ?? new())
        {
            if (!GuardRecords.ContainsKey(gone))
                throw new InvalidDataException($"Story state: no guard {gone} to be gone.");
            story.MarkGuardGone(gone);   // only while its field holds its value
        }
        story.Version = 0;
        return story;

        static int Byte(int value, string what) => value is >= 0 and <= byte.MaxValue
            ? value : throw new InvalidDataException($"Story state: the {what} is {value}, not a byte.");
    }
}
