// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/ships/sis_ship/sis_ship.c:
// init_sis, InitDriveSlots, InitJetSlots), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Hyperspace;

/// <summary>
/// Flight characteristics of a player ship in hyperspace, derived from its
/// thrusters and turning jets the way init_sis does for the flagship.
/// Escorts use the same rules (their module set is an open item).
/// </summary>
public readonly record struct ShipDrive(int MaxThrust, int ThrustIncrement, int ThrustWait, int TurnWait)
{
    // sis_ship.c base values.
    private const int BaseMaxThrust = 10;
    private const int BaseThrustIncrement = 4;
    private const int BaseThrustWait = 6;
    private const int BaseTurnWait = 17;
    private const int HyperspaceThrustPenalty = 4;

    public const int MaxThrusters = 11;  // NUM_DRIVE_SLOTS
    public const int MaxJets = 8;        // NUM_JET_SLOTS

    /// <summary>init_sis in hyperspace, for a ship with this many thrusters and jets.</summary>
    public static ShipDrive ForHyperspace(int thrusters, int jets)
    {
        if (thrusters < 0 || thrusters > MaxThrusters)
            throw new ArgumentOutOfRangeException(nameof(thrusters));
        if (jets < 0 || jets > MaxJets)
            throw new ArgumentOutOfRangeException(nameof(jets));

        // inHQSpace: max_thrust -= 4, before the drive slots are counted.
        int maxThrust = BaseMaxThrust - HyperspaceThrustPenalty;
        maxThrust += 2 * thrusters;
        int thrustWait = (byte)(BaseThrustWait - (thrusters >> 1));
        maxThrust = (maxThrust / BaseThrustIncrement + 1) * BaseThrustIncrement;
        int turnWait = (byte)(BaseTurnWait - 2 * jets);
        return new ShipDrive(maxThrust, BaseThrustIncrement, thrustWait, turnWait);
    }
}
