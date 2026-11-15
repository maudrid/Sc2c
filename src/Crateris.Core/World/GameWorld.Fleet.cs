// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;
using Crateris.Core.Uqm;

namespace Crateris.Core.World;

// The host's fleet (stage 4b item 6), its shipyard's ship of no crew and its roster (item 7).
public sealed partial class GameWorld
{
    private TfbRandom? _fleetNames;

    /// <summary>
    /// The host's fleet (stage 4b, decision 3: only the host has one), which
    /// talks give ships to whoever answers, and saves keep.
    /// </summary>
    public HostFleet Fleet { get; } = new();

    /// <summary>
    /// Ships of a race join the host's fleet outside a talk (the testing
    /// starts'): AddEscortShips, the captains'
    /// names drawn from a generator of the world's own, seeded as it's
    /// first needed from the frame (UQM's TFB_Random, seeded from the
    /// clock; a talk's ships take the talk's generator). Needs the story's
    /// rules (<see cref="InvalidOperationException"/> without them).
    /// </summary>
    public int AddFleetShips(string race, int count) =>
        Fleet.Add(FleetRules, race, count, FleetRandom);

    /// <summary>The rules fleets are made by: the story's (each race's ship).</summary>
    internal StoryRules FleetRules => Story?.Rules ?? throw new InvalidOperationException("a fleet needs the story's rules");

    /// <summary>
    /// A fleet ship of no crew stays only while the host's shipyard edits its
    /// crew (shipyard.c's DMS_EditCrewMode scraps it as the editing ends,
    /// and nothing else leaves one): any other is scrapped, as UQM's would
    /// be on leaving its crew editing. Each frame (<see cref="Step"/>), so the
    /// shipyard left any way scraps it: its keys (select, cancel), or the
    /// host's visit ending under it. A save leaves it out
    /// (<see cref="HostSave"/>).
    /// </summary>
    internal void ScrapUnmannedFleetShips()
    {
        if (!Fleet.Ships.Any(s => s.Crew == 0))
            return;
        int? editing = Host.Ship.Visit?.Menu?.Yard?.EditingSlot;
        foreach (var ship in Fleet.Ships.Where(s => s.Crew == 0 && s.Slot != editing).ToList())
            Fleet.Scrap(ship.Slot);
    }

    /// <summary>
    /// The roster's crew move (planets/roster.c's DoModifyRoster and
    /// DeltaSupportCrew), the host's only (stage 4b, decision 3): a crew
    /// member from the flagship to the fleet ship at <paramref name="index"/>
    /// in the fleet's queue (<paramref name="delta"/> 1: the flagship must
    /// have one, and the ship stay within its race's starting crew, the
    /// template's crew_level, as a Syreen's 12 of its most 42), or back
    /// (-1: the flagship must have room, and the ship keep one). In flight
    /// (hyperspace, a system, orbit), not docked, in a battle, a talk, or
    /// with the lander down; not while the game waits. False, changing
    /// nothing, if refused. A ship with more than its starting crew (a
    /// Syreen's after a battle) may still send crew back, as UQM's.
    /// </summary>
    public bool MoveRosterCrew(string id, int index, int delta)
    {
        if (StoryPaused || Held || Story is null
                || Find(id) is not { Present: true, IsHost: true, Ship: { Visit: null, Battle: null, Lander: null, Destroyed: false } } host)
            return false;
        var hold = host.Ship.Hold;
        if (!Fleet.MoveRosterCrew(FleetRules, index, delta, hold.Crew, hold.CrewCapacity))
            return false;
        hold.SetCrew(hold.Crew - delta);
        return true;
    }

    /// <summary>The roster's view of the host's fleet, for the host's own orbit menu (null for a guest: they have no fleet).</summary>
    public Systems.IRosterFleet? RosterFor(string id) => Find(id) is { IsHost: true } ? new Roster(this, id) : null;

    private sealed class Roster : Systems.IRosterFleet
    {
        private readonly GameWorld _world;
        private readonly string _id;

        public Roster(GameWorld world, string id) => (_world, _id) = (world, id);

        public IReadOnlyList<FleetShip> Ships => _world.Fleet.Ships;

        public bool MoveCrew(int index, int delta) => _world.MoveRosterCrew(_id, index, delta);

        public int FlagshipCrew => _world.Find(_id)?.Ship.Hold.Crew ?? 0;
    }

    /// <summary>The world's own generator for captains' names (testing starts draw from it too).</summary>
    internal uint FleetRandomForTesting() => FleetRandom();

    private uint FleetRandom() => (_fleetNames ??= new TfbRandom(unchecked((uint)Frame * 2654435761u + 0x5EED))).Next();
}
