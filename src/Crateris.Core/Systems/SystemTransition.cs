// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (hyper.c InterplanetaryTransition,
// planets/solarsys.c InitSolarSys, ExploreSolarSys and saveNonOrbitalLocation),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;

namespace Crateris.Core.Systems;

/// <summary>
/// Where a ship is when it arrives in a system's outer view: its place on
/// the screen (UQM's ShipStamp.origin), its system location (ip_location)
/// and its facing.
/// </summary>
public sealed record SystemArrival(Star Star, IpPoint ScreenPosition, IpPoint Location, ushort Facing);

/// <summary>Moving a ship between hyperspace and a star system, as UQM does.</summary>
public static class SystemTransition
{
    /// <summary>
    /// Arriving from hyperspace (InitSolarSys, not a reentry): always at the
    /// bottom middle of the outer view at its widest zoom, facing up, at rest,
    /// whichever way the ship came in.
    /// </summary>
    public static SystemArrival Arrive(Star star)
    {
        var origin = new IpPoint(IpGeometry.ScreenWidth >> 1, IpGeometry.ScreenHeight - 2);
        return new SystemArrival(star, origin, IpGeometry.DisplayToLocation(origin, IpGeometry.MaxZoomRadius), 0);
    }

    /// <summary>
    /// Leaving a system for hyperspace: the ship is put exactly on the star
    /// (ExploreSolarSys set the hyperspace position to the star on the way
    /// in), keeps the facing it had in the system, and is at rest. The star
    /// is remembered as the one just left, so the ship doesn't fall straight
    /// back in (see <see cref="StarContact"/>).
    /// </summary>
    public static void Leave(HyperFlight ship, Star star, ushort facing)
    {
        ship.PlaceAtUniverse(star.X, star.Y);
        ship.Stop();
        ship.Facing = (ushort)(facing & 15);
        ship.ResetFuelCounters();
        ship.StarJustLeft = star.Number;
    }
}
