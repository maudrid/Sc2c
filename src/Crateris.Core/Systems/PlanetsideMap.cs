// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0, src/uqm/planets/plangen.c
// (TopoScale4x, frandom, TopoVarianceFactor, TopoVarianceCalc),
// Copyright Paul Reiche, Fred Ford and the UQM contributors, GPL v2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Systems;

/// <summary>
/// The planetside map: the height map four times larger each way, which the
/// lander drives over (TopoScale4x, MAG_SHIFT). UQM makes it on entering the
/// orbit of a world that is neither shielded nor a gas giant, by joining
/// each pair of neighbouring heights with a line and adding a little random
/// variance along it. The variance comes from frandom, a generator whose
/// seed UQM starts at 0x12345678 when the program starts and never resets;
/// here it starts afresh for each world (docs/plans/stage-2b.md, decision 1),
/// so a world's terrain is UQM's for the first world orbited in a session,
/// and the same for every player on every visit. Nothing collides with the
/// terrain; it's only drawn.
/// </summary>
public static class PlanetsideMap
{
    /// <summary>MAG_SHIFT (lander.h): the planetside map is 4 times the height map each way.</summary>
    public const int MagShift = 2;
    public const int Width = SurfaceMap.Width << MagShift;
    public const int Height = SurfaceMap.Height << MagShift;

    /// <summary>frandom's first seed.</summary>
    public const uint FirstSeed = 0x12345678;

    private const int ScaleShift = 8;       // SCALE_SHIFT
    private const int AvgVariance = 250;    // AVG_VARIANCE
    private const int StepRange = 4 - 1;    // STEP_RANGE

    /// <summary>Whether a world has a planetside map: the lander can go down to it (not shielded, not a gas giant).</summary>
    public static bool Landable(byte dataIndex, PlanetInfo report) =>
        !WorldTypes.IsShielded(dataIndex) && report.AtmoDensity != PlanetInfo.GasGiantAtmosphere;

    /// <summary>The planetside map of a world of this type, from its height map, as GeneratePlanetSurface calls TopoScale4x.</summary>
    public static sbyte[] Generate(sbyte[] heights, PlanetType type) =>
        Scale4x(heights, type.NumFaults, type.FaultDepth * (type.Algorithm == MapAlgorithm.Cratered ? 2 : 1));

    // The four ways the inside of each 4 x 4 square is filled: lines
    // (x0, y0) to (x1, y1) by (dx, dy). fill_lines in TopoScale4x.
    private static readonly (int X0, int Y0, int X1, int Y1, int Dx, int Dy)[][] FillLines =
    {
        new[] { (0, 2, 2, 0, 1, -1), (0, 3, 3, 0, 1, -1), (0, 4, 4, 0, 1, -1), (1, 4, 4, 1, 1, -1), (2, 4, 4, 2, 1, -1) },
        new[] { (0, 2, 2, 4, 1, 1), (0, 1, 3, 4, 1, 1), (0, 0, 4, 4, 1, 1), (1, 0, 4, 3, 1, 1), (2, 0, 4, 2, 1, 1) },
        new[] { (0, 1, 4, 1, 1, 0), (0, 2, 4, 2, 1, 0), (0, 3, 4, 3, 1, 0) },
        new[] { (1, 0, 1, 4, 0, 1), (2, 0, 2, 4, 0, 1), (3, 0, 3, 4, 0, 1) },
    };

    /// <summary>TopoScale4x, with frandom started afresh.</summary>
    public static sbyte[] Scale4x(sbyte[] src, int numFaults, int faultVar)
    {
        const int w = SurfaceMap.Width, h = SurfaceMap.Height;
        const int spitch = w, dpitch = w * 4;
        if (src.Length != w * h)
            throw new ArgumentException($"the height map is {w} x {h}", nameof(src));
        var rng = new FRandom();
        var dst = new sbyte[dpitch * h * 4];
        var prevrow = new int[w * 4 + 1];
        var elev = new int[5, 5];
        int varAllow = (numFaults << ScaleShift) / AvgVariance;
        int varMin = faultVar << ScaleShift;

        int Factor(int step) => ((Math.Abs(step) * varAllow) >> ScaleShift) + varMin;
        int Variance(int factor) =>
            factor == 0 ? 0 : (int)(rng.Next() % unchecked((uint)factor)) - (factor >> 1);

        // The first row, in advance.
        int s = 0, prow = 0;
        prevrow[0] = src[0] << ScaleShift;
        for (int x = 0; x < w; ++x, ++s, prow += 4)
        {
            prevrow[prow + 4] = (x < w - 1 ? src[s + 1] : src[s + 1 - spitch]) << ScaleShift;
            int val = prevrow[prow];
            int step = (prevrow[prow + 4] - val) / StepRange;
            int rnd = Factor(step);
            val += step;
            for (int x2 = 1; x2 < 4; ++x2, val += step)
                prevrow[prow + x2] = val + Variance(rnd);
        }

        s = 0;
        int d = 0;
        for (int y = 0; y < h; ++y, d += dpitch * 3)
        {
            prow = 0;
            // The first interpolated column.
            elev[4, 0] = prevrow[prow];
            elev[4, 4] = y < h - 1 ? src[s + spitch] << ScaleShift : elev[4, 0];
            {
                int val = elev[4, 0];
                int step = (elev[4, 4] - val) / StepRange;
                int rnd = Factor(step);
                val += step;
                for (int y2 = 1; y2 < 4; ++y2, val += step)
                    elev[4, y2] = val + Variance(rnd);
            }

            for (int x = 0; x < w; ++x, ++s, d += 4, prow += 4)
            {
                for (int x2 = 0; x2 <= 4; ++x2)
                    elev[x2, 0] = prevrow[prow + x2];
                for (int y2 = 1; y2 <= 4; ++y2)
                    elev[0, y2] = elev[4, y2];

                if (y < h - 1)
                    elev[4, 4] = (x < w - 1 ? src[s + 1 + spitch] : src[s + 1]) << ScaleShift;
                else
                    elev[4, 4] = elev[4, 0];

                // The rest of the square's borders.
                {
                    int val = elev[0, 4];
                    int step = (elev[4, 4] - val) / StepRange;
                    int rnd = Factor(step);
                    val += step;
                    for (int x2 = 1; x2 < 4; ++x2, val += step)
                        elev[x2, 4] = val + Variance(rnd);
                }
                {
                    int val = elev[4, 0];
                    int step = (elev[4, 4] - val) / StepRange;
                    int rnd = Factor(step);
                    val += step;
                    for (int y2 = 1; y2 < 4; ++y2, val += step)
                        elev[4, y2] = val + Variance(rnd);
                }

                // The inside, by lines between opposite borders, one way picked at random.
                foreach (var line in FillLines[rng.Next() & 3])
                {
                    int x2 = line.X0, y2 = line.Y0;
                    int val = elev[x2, y2];
                    int steps = line.X1 - line.X0;
                    if (steps == 0)
                        steps = line.Y1 - line.Y0;
                    int step = (elev[line.X1, line.Y1] - val) / steps;
                    int rnd = Factor(step);
                    for (x2 += line.Dx, y2 += line.Dy, val += step;
                         x2 != line.X1 || y2 != line.Y1;
                         x2 += line.Dx, y2 += line.Dy, val += step)
                        elev[x2, y2] = val + Variance(rnd);
                }

                for (int y2 = 0; y2 < 4; ++y2)
                    for (int x2 = 0; x2 < 4; ++x2)
                        dst[d + y2 * dpitch + x2] = (sbyte)Math.Clamp(elev[x2, y2] >> ScaleShift, -128, 127);

                // The square's last row, for the next row of squares.
                for (int x2 = 0; x2 < 4; ++x2)
                    prevrow[prow + x2] = elev[x2, 4];
            }
            prevrow[prow] = elev[4, 4];
        }
        return dst;
    }

    /// <summary>frandom: UQM's small generator for the planetside map.</summary>
    private sealed class FRandom
    {
        private uint _seed = FirstSeed;

        public uint Next()
        {
            if (_seed == 0)
                _seed = 15807;
            _seed = unchecked((_seed >> 4) * 227);
            return _seed;
        }
    }
}
