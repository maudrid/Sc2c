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

// The host's fleet (stage 4b item 6).
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
    /// starts', and the shipyard's to come): AddEscortShips, the captains'
    /// names drawn from a generator of the world's own, seeded as it's
    /// first needed from the frame (UQM's TFB_Random, seeded from the
    /// clock; a talk's ships take the talk's generator). Needs the story's
    /// rules (<see cref="InvalidOperationException"/> without them).
    /// </summary>
    public int AddFleetShips(string race, int count) =>
        Fleet.Add(FleetRules, race, count, FleetRandom);

    /// <summary>The rules fleets are made by: the story's (each race's ship).</summary>
    internal StoryRules FleetRules => Story?.Rules ?? throw new InvalidOperationException("a fleet needs the story's rules");

    /// <summary>The world's own generator for captains' names (testing starts draw from it too).</summary>
    internal uint FleetRandomForTesting() => FleetRandom();

    private uint FleetRandom() => (_fleetNames ??= new TfbRandom(unchecked((uint)Frame * 2654435761u + 0x5EED))).Next();
}
