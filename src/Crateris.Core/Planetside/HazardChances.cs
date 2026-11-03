// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0, src/uqm/planets/lander.c
// (GetHazardChance, GetThermalHazardRating, PlanetSide),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;

namespace Crateris.Core.Planetside;

/// <summary>
/// A world's chances, out of 256 each frame around each lander, of an
/// earthquake, a bolt of lightning and a lava spot starting
/// (PLANETSIDE_DESC's TectonicsChance, WeatherChance and FireChance).
/// </summary>
public readonly record struct HazardChances(int Tectonics, int Weather, int Fire)
{
    /// <summary>No hazards.</summary>
    public static readonly HazardChances None = default;

    private static readonly int[] TectonicsChanceTab = { 0 * 3, 0 * 3, 1 * 3, 2 * 3, 4 * 3, 8 * 3, 16 * 3, 32 * 3 };
    private static readonly int[] WeatherChanceTab = { 0 * 3, 0 * 3, 1 * 3, 2 * 3, 3 * 3, 6 * 3, 12 * 3, 24 * 3 };
    private static readonly int[] FireChanceTab = { 0 * 3, 0 * 3, 1 * 3, 2 * 3, 4 * 3, 12 * 3, 24 * 3, 48 * 3 };
    private static readonly int[] TempBreakpoints = { 50, 100, 150, 250, 350, 550, 800 };

    /// <summary>
    /// PlanetSide's chances from a world's report as orbit leaves it: its
    /// tectonics and weather ratings (0 to 7, one less than the scan's
    /// classes) and its surface temperature, rated by
    /// <see cref="ThermalHazardRating"/> (GetHazardChance).
    /// </summary>
    public static HazardChances For(int tectonics, int weather, int temperature) =>
        new(TectonicsChanceTab[tectonics], WeatherChanceTab[weather], FireChanceTab[ThermalHazardRating(temperature)]);

    /// <inheritdoc cref="For(int, int, int)"/>
    public static HazardChances For(PlanetInfo report) => For(report.Tectonics, report.Weather, report.SurfaceTemperature);

    /// <summary>GetThermalHazardRating: a temperature's rating, 0 (under 50°) to 7 (800° or more).</summary>
    public static int ThermalHazardRating(int temperature)
    {
        for (int i = 0; i < TempBreakpoints.Length; ++i)
        {
            if (temperature < TempBreakpoints[i])
                return i;
        }
        return TempBreakpoints.Length;
    }
}
