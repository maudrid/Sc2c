// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/planets.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// A planet or moon as generated (the generated part of PLANET_DESC).
/// A value type, because UQM's generators copy, swap and shift these.
/// </summary>
public struct WorldDesc
{
    /// <summary>Seed for this world's own generation (planet data, moons, surface).</summary>
    public uint RandSeed;

    /// <summary>Planet type, maybe with the shield bit; see <see cref="WorldTypes"/>.</summary>
    public byte DataIndex;

    /// <summary>For a planet, how many moons it has.</summary>
    public byte NumPlanets;

    /// <summary>Orbit radius around the sun (planets) or the planet (moons).</summary>
    public short Radius;

    /// <summary>Position relative to what it orbits.</summary>
    public short X;
    public short Y;

    public override readonly string ToString() =>
        $"type {DataIndex} radius {Radius} at ({X}, {Y}) seed {RandSeed} moons {NumPlanets}";
}
