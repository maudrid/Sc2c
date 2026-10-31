// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.World;

/// <summary>
/// Host settings. Every number the design leaves open is a setting here,
/// with a placeholder default to be tuned by playtesting.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Most players in one game, the host included.</summary>
    public int MaxPlayers { get; init; } = 4;

    /// <summary>
    /// Constant clock rate (docs/differences.md row 1). Placeholder: UQM's
    /// hyperspace rate.
    /// </summary>
    public double SecondsPerGameDay { get; init; } = 5;

    /// <summary>
    /// Frames between a client pressing a key and the host applying it, like
    /// UQM's netplay input delay (default 2 frames, about 83 ms).
    /// </summary>
    public int InputDelayFrames { get; init; } = 2;

    /// <summary>
    /// A new guest's escort. Placeholder until the escort module set is
    /// decided: the flagship's starting drives, one fuel tank, 10 fuel.
    /// </summary>
    public ShipLoadout EscortLoadout { get; init; } = new(Thrusters: 2, Jets: 2, FuelTanks: 1,
        HighEfficiencyTanks: 0, Crew: 10, Fuel: 1000);
}
