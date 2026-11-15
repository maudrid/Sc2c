// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/build.c: AddEscortShips,
// CalculateEscortsWorth, CountEscortShips, EscortFeasibilityStudy,
// RemoveSomeEscortShips, RemoveEscortShips, CloneShipFragment,
// NameCaptain; build.h: PickCaptainName; races.h: MAX_BUILT_SHIPS;
// shipyard.c's ShowCombatShip and DMS_ScrapEscortShip; planets/roster.c's
// DeltaSupportCrew),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>
/// One of the host's fleet ships (a SHIP_FRAGMENT of built_ship_q): its
/// race (races.h's ARILOU_SHIP, ...), its crew, its captain's name (an
/// index into its race's strings, from NAME_OFFSET's 5) and its hangar
/// slot (index, 0 to 11).
/// </summary>
public sealed record FleetShip(string Race, int Crew, byte Captain, int Slot);

/// <summary>
/// The host's fleet (stage 4b item 6; stage 4b, decision 3: only the host
/// has one): UQM's built_ship_q, up to MAX_BUILT_SHIPS' 12 ships of allied
/// races, each in a hangar slot, kept in the order of their slots, with
/// build.c's functions on it. Talks give ships to it whoever answers
/// (decision 3), as the Arilou's gift does.
/// </summary>
public sealed class HostFleet
{
    /// <summary>MAX_BUILT_SHIPS: the most ships the fleet holds, and its hangar's slots.</summary>
    public const int MaxShips = 12;

    /// <summary>build.h's NAME_OFFSET and NUM_CAPTAINS_NAMES: where a race's captains' names start in its strings, and how many.</summary>
    public const int CaptainNameOffset = 5, NumCaptainsNames = 16;

    private readonly List<FleetShip> _ships = new();

    /// <summary>The ships, by their slots (built_ship_q's order).</summary>
    public IReadOnlyList<FleetShip> Ships => _ships;

    /// <summary>The number of ships (CountLinks).</summary>
    public int Count => _ships.Count;

    /// <summary>
    /// EscortFeasibilityStudy: how many more ships the fleet has room for
    /// (0 for a race not in avail_race_q, which no caller asks for).
    /// </summary>
    public int Room => MaxShips - _ships.Count;

    /// <summary>CountEscortShips: how many ships of a race are in the fleet.</summary>
    public int CountOf(string race) => _ships.Count(s => s.Race == race);

    /// <summary>CalculateEscortsWorth: what the fleet's ships cost (RACE_SHIP_COST each).</summary>
    public int Worth(StoryRules rules) => _ships.Sum(s => rules.RaceShip(s.Race).Cost);

    /// <summary>
    /// AddEscortShips: ships of a race join the fleet, as many as it has
    /// room for, each made as CloneShipFragment makes it (its race's
    /// starting crew, its captain named by NameCaptain, drawing from
    /// <paramref name="random"/>: TFB_Random) and put in the first free
    /// slot, before the ship in the next (the queue kept by slot). Returns
    /// how many joined. As UQM's, the name is drawn before the queue is
    /// found full, so asking for more than the room draws once more. A
    /// race not in avail_race_q is an <see cref="ArgumentException"/>, as
    /// is a count below 1 (UQM asserts count > 0); the Ur-Quan drone, whose
    /// ship never fights, too.
    /// </summary>
    public int Add(StoryRules rules, string race, int count, Func<uint> random)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "AddEscortShips adds one ship at least");
        var raceShip = Fleetable(rules, race);
        int i;
        for (i = 0; i < count; ++i)
        {
            // CloneShipFragment: the name first (NameCaptain), then the ship (Build: none when the queue is full).
            byte captain = NameCaptain(rules, raceShip.Race, random);
            if (_ships.Count == MaxShips)
                break;
            // The first window free: the first place in the queue whose ship's slot is past its place.
            int at = 0;
            while (at < _ships.Count && _ships[at].Slot <= at)
                ++at;
            _ships.Insert(at, new FleetShip(raceShip.Race, raceShip.Crew, captain, at));
        }
        return i;
    }

    /// <summary>
    /// The shipyard's build (DMS_TryAddEscortShip): a ship of a race made
    /// as CloneShipFragment makes it with <paramref name="crew"/> crew (the
    /// shipyard's 1), its captain named by NameCaptain from
    /// <paramref name="random"/>, put in the empty <paramref name="slot"/>
    /// under the cursor (ShowCombatShip moves the queue's new tail before
    /// the first ship of a later slot and gives it the slot). UQM's Build
    /// fails with the fleet full (after the name is drawn), which the
    /// shipyard can't reach: an empty slot means room. A slot out of range
    /// or taken, a crew out of range (1 to the race's most), or a race that
    /// can't have a fleet ship is an <see cref="ArgumentException"/>,
    /// nothing drawn.
    /// </summary>
    public FleetShip Build(StoryRules rules, string race, int slot, int crew, Func<uint> random)
    {
        if (slot is < 0 or >= MaxShips)
            throw new ArgumentOutOfRangeException(nameof(slot), $"hangar slot {slot}: 0 to {MaxShips - 1}");
        if (InSlot(slot) is not null)
            throw new ArgumentException($"hangar slot {slot} has a ship", nameof(slot));
        var raceShip = Fleetable(rules, race);
        if (crew < 1 || crew > raceShip.MaxCrew)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew for a ship of {raceShip.MaxCrew}");
        byte captain = NameCaptain(rules, raceShip.Race, random);
        int at = 0;
        while (at < _ships.Count && _ships[at].Slot <= slot)
            ++at;
        var ship = new FleetShip(raceShip.Race, crew, captain, slot);
        _ships.Insert(at, ship);
        return ship;
    }

    /// <summary>
    /// The ship in a slot leaves the fleet (DMS_ScrapEscortShip: the
    /// shipyard's ship of no crew, scrapped as its crew editing ends). An
    /// <see cref="ArgumentException"/> for a slot with no ship.
    /// </summary>
    public void Scrap(int slot)
    {
        int at = _ships.FindIndex(s => s.Slot == slot);
        if (at < 0)
            throw new ArgumentException($"no fleet ship in slot {slot}", nameof(slot));
        _ships.RemoveAt(at);
    }

    /// <summary>
    /// NameCaptain: a captain's name for a new ship of a race,
    /// PickCaptainName drawn again while another ship of its species in the
    /// fleet has it (the Yehat rebels' ships are the Yehat's species).
    /// </summary>
    public byte NameCaptain(StoryRules rules, string race, Func<uint> random)
    {
        string ship = rules.RaceShip(race).Ship;
        byte name;
        do
            name = (byte)((random() & (NumCaptainsNames - 1)) + CaptainNameOffset);
        while (_ships.Any(s => rules.RaceShip(s.Race).Ship == ship && s.Captain == name));
        return name;
    }

    /// <summary>
    /// RemoveSomeEscortShips: up to <paramref name="count"/> ships of a race
    /// leave the fleet, the first by slot first. Returns, as UQM's does,
    /// the count it couldn't remove (0 when all went, though its comment
    /// says the number removed).
    /// </summary>
    public int RemoveSome(string race, int count)
    {
        if (count <= 0)
            return 0;
        for (int at = 0; at < _ships.Count && count > 0;)
        {
            if (_ships[at].Race == race)
            {
                _ships.RemoveAt(at);
                --count;
            }
            else
                ++at;
        }
        return count;
    }

    /// <summary>RemoveEscortShips: every ship of a race leaves the fleet.</summary>
    public void Remove(string race) => RemoveSome(race, int.MaxValue);

    /// <summary>
    /// A fleet ship's crew set (0 to its most): the shipyard's and the
    /// roster's, and a battle's. An <see cref="ArgumentException"/> for a
    /// slot with no ship or a crew out of range.
    /// </summary>
    public void SetCrew(StoryRules rules, int slot, int crew)
    {
        int at = _ships.FindIndex(s => s.Slot == slot);
        if (at < 0)
            throw new ArgumentException($"no fleet ship in slot {slot}", nameof(slot));
        int most = rules.RaceShip(_ships[at].Race).MaxCrew;
        if (crew < 0 || crew > most)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew for a ship of {most}");
        _ships[at] = _ships[at] with { Crew = crew };
    }

    /// <summary>
    /// The roster's crew move (planets/roster.c's DoModifyRoster and
    /// DeltaSupportCrew) for the ship at <paramref name="index"/> in the
    /// queue: <paramref name="delta"/> 1 takes a crew member from the
    /// flagship (which must have one, <paramref name="flagshipCrew"/>), -1
    /// gives one back (the flagship must have room: below
    /// <paramref name="flagshipRoom"/>, GetCrewPodCapacity). The ship keeps
    /// at least one, and takes none past its race's starting crew (the
    /// template's crew_level, not its most: a Syreen's 12 of 42); one over
    /// it (a Syreen's that picked up crew in a battle) may still give crew
    /// back. True if the ship's crew changed; the caller moves the
    /// flagship's by -<paramref name="delta"/> (DeltaSISGauges). A delta
    /// other than 1 or -1, or no ship there, is refused (false). UQM's
    /// ship of no crew giving one back (its crew_level wrapping round to
    /// 65535) can't happen here: no such ship is out of the shipyard.
    /// </summary>
    public bool MoveRosterCrew(StoryRules rules, int index, int delta, int flagshipCrew, int flagshipRoom)
    {
        if (delta is not (1 or -1) || index < 0 || index >= _ships.Count)
            return false;
        var ship = _ships[index];
        if (delta > 0 ? flagshipCrew == 0 || ship.Crew + 1 > rules.RaceShip(ship.Race).Crew
                : flagshipCrew >= flagshipRoom || ship.Crew <= 1)
            return false;
        _ships[index] = ship with { Crew = ship.Crew + delta };
        return true;
    }

    /// <summary>The fleet ship in a slot, or null.</summary>
    public FleetShip? InSlot(int slot) => _ships.FirstOrDefault(s => s.Slot == slot);

    /// <summary>
    /// The fleet as a save holds it, or as a test sets it: every ship's
    /// race one that can have a fleet, its crew from 0 to its most (UQM's
    /// shipyard holds one of none while its crew is edited, scrapping it
    /// after, and encount.c drops one after a battle: the shipyard and the
    /// battles that come do so too; a save may come between), its
    /// captain one of its race's 16 names, its slot 0 to 11 and its own;
    /// 12 ships at most. Anything else is an <see cref="ArgumentException"/>,
    /// and the fleet is left as it was.
    /// </summary>
    public void Restore(StoryRules rules, IEnumerable<FleetShip> ships)
    {
        var list = ships.OrderBy(s => s.Slot).ToList();
        if (list.Count > MaxShips)
            throw new ArgumentException($"{list.Count} fleet ships: {MaxShips} at most");
        foreach (var s in list)
        {
            var raceShip = Fleetable(rules, s.Race);
            if (s.Crew < 0 || s.Crew > raceShip.MaxCrew)
                throw new ArgumentException($"a {s.Race} of {s.Crew} crew: 0 to {raceShip.MaxCrew}");
            if (s.Captain is < CaptainNameOffset or >= CaptainNameOffset + NumCaptainsNames)
                throw new ArgumentException($"a {s.Race}'s captain {s.Captain} isn't one of its names");
            if (s.Slot is < 0 or >= MaxShips)
                throw new ArgumentException($"fleet slot {s.Slot}: 0 to {MaxShips - 1}");
        }
        if (list.Select(s => s.Slot).Distinct().Count() != list.Count)
            throw new ArgumentException("two fleet ships in one slot");
        _ships.Clear();
        _ships.AddRange(list);
    }

    /// <summary>Whether a race can have fleet ships: one of avail_race_q's but the Ur-Quan drone (whose ship never fights).</summary>
    public static bool CanHave(StoryRules rules, string race) => rules.Races.Contains(race) && race != "URQUAN_DRONE_SHIP";

    // A race that can have a fleet ship: one of avail_race_q's but the drone (whose ship never fights).
    private static RaceShip Fleetable(StoryRules rules, string race)
    {
        if (!rules.Races.Contains(race))
            throw new ArgumentException($"no race {race}", nameof(race));
        var r = rules.RaceShip(race);
        if (race == "URQUAN_DRONE_SHIP")
            throw new ArgumentException("the Ur-Quan drone has no fleet ships", nameof(race));
        return r;
    }
}
