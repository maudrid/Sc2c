// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/sis.h, sis.c:
// GetFuelTankCapacity), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Hyperspace;

/// <summary>Fuel amounts are in 1/100 units (FUEL_TANK_SCALE).</summary>
public static class Fuel
{
    public const uint Scale = 100;                 // FUEL_TANK_SCALE
    public const uint Reserve = 10 * Scale;        // FUEL_RESERVE: always available
    public const uint TankSize = 50 * Scale;       // FUEL_TANK_CAPACITY
    public const uint HighEfficiencyTankSize = 100 * Scale; // HEFUEL_TANK_CAPACITY

    /// <summary>GetFuelTankCapacity for a ship with these tanks.</summary>
    public static uint TankCapacity(int tanks, int highEfficiencyTanks = 0) =>
        Reserve + (uint)tanks * TankSize + (uint)highEfficiencyTanks * HighEfficiencyTankSize;
}
