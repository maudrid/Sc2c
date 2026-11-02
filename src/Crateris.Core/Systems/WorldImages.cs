// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/solarsys.c: ValidateOrbit's
// choice of image), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>The sprite sheets of a system's views (the numbers are the recorder's).</summary>
public enum SystemSheet
{
    /// <summary>graphics.flagship: the flagship, by facing.</summary>
    Flagship = 1,

    /// <summary>graphics.planets: worlds, (size &lt;&lt; 4) + facing towards the light.</summary>
    Worlds = 2,

    /// <summary>graphics.orbitbackground: the background stars, the starbase, the shield and the Sa-Matra.</summary>
    Junk = 3,

    /// <summary>graphics.truespacesun: the sun, by star size and zoom.</summary>
    Sun = 5,
}

/// <summary>
/// Which frame a world is in a system's views: the shape UQM tests for
/// contact (IpFlight) and draws (the system views).
/// </summary>
public static class WorldImages
{
    public const int StarbaseFrame = 16;
    public const int SaMatraFrame = 19;

    /// <summary>
    /// ValidateOrbit's image for a world: an orbital frame by size, lit from
    /// the sun (a moon by its planet's direction from the sun), or the
    /// starbase's or the Sa-Matra's.
    /// </summary>
    public static (SystemSheet Sheet, int Frame) Of(WorldDesc world, int size, WorldDesc lightFrom)
    {
        if (WorldTypes.HasPlanetData(world.DataIndex))
        {
            int angle = UqmMath.ArcTan(lightFrom.X, lightFrom.Y);
            return (SystemSheet.Worlds, (size << UqmMath.FacingShift) + UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle)));
        }
        return (SystemSheet.Junk, world.DataIndex == WorldTypes.SaMatra ? SaMatraFrame : StarbaseFrame);
    }
}
