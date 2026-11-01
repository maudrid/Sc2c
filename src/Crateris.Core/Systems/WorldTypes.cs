// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Values from The Ur-Quan Masters 0.8.0 (planets/plandata.h, planets.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// World type numbers (PLANET_DESC.data_index) and orbit constants.
/// A world's data_index is a planet type (0..58, the order of
/// data/planet-types.json), optionally with <see cref="Shielded"/> set,
/// or one of the two special worlds.
/// </summary>
public static class WorldTypes
{
    public const byte Shielded = 0x80;              // PLANET_SHIELDED, WORLD_TYPE_SPECIAL
    public const byte HierarchyStarbase = 127 | 0x80;
    public const byte SaMatra = 126 | 0x80;
    public const int NumberOfPlanetTypes = 59;
    public const int FirstLargeRockyWorld = 29;     // FLUORESCENT_WORLD
    public const int FirstGasGiant = 50;            // BLU_GAS_GIANT

    // The planet types the special generators name.
    public const byte MetalWorld = 6;
    public const byte RadioactiveWorld = 7;
    public const byte CyanicWorld = 9;
    public const byte AlkaliWorld = 11;
    public const byte HalideWorld = 12;
    public const byte PellucidWorld = 22;
    public const byte DustWorld = 23;
    public const byte SelenicWorld = 27;
    public const byte RainbowWorld = 32;
    public const byte ShatteredWorld = 33;
    public const byte SapphireWorld = 34;
    public const byte ReduxWorld = 37;
    public const byte PrimordialWorld = 38;
    public const byte WaterWorld = 42;
    public const byte TelluricWorld = 43;
    public const byte VinylogousWorld = 46;
    public const byte BluGasGiant = 50;
    public const byte GrnGasGiant = 52;
    public const byte OraGasGiant = 54;
    public const byte RedGasGiant = 56;

    // Orbits (planets.h). Radii are in SCALE_RADIUS units (64 per display unit).
    public const int EarthRadius = 8 << 6;           // SCALE_RADIUS (8)
    public const int MinPlanetRadius = 4 << 6;
    public const int MaxPlanetRadius = 124 << 6;
    public const int MinMoonRadius = 35;
    public const int MoonDelta = 20;
    public const int MaxPlanets = 16;
    public const int MaxMoons = 4;

    /// <summary>False for the starbase and the Sa-Matra, which have no planet data; true for every planet type.</summary>
    public static bool HasPlanetData(byte dataIndex) => (dataIndex & ~Shielded) < NumberOfPlanetTypes;

    /// <summary>A planet with a shield over it (PLANET_SHIELDED on a planet type).</summary>
    public static bool IsShielded(byte dataIndex) => HasPlanetData(dataIndex) && (dataIndex & Shielded) != 0;

    /// <summary>The planet type of a world (data_index without the shield bit).</summary>
    public static int PlanetType(byte dataIndex) => dataIndex & ~Shielded;
}
