// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/orbits.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>FillOrbits from orbits.c: places planets around a star, or moons around a planet.</summary>
public static class Orbits
{
    // Which planet types each star colour can have, one character per type
    // in PlanData order: 'A' PLANET_ALWAYS (255), '.' PLANET_NEVER (0).
    // Generated from the tables in orbits.c.
    private static readonly string[] Distributions =
    {
        "AAAAAAAAAAAAAAAAAAA........AAAAA..AAAAA.AAAA......AAAAAAAAA", // blue
        "..AAAAAAAAAAAAAAAAAAAAA....AAAAA...AAAAAAAAAAA....AAAAAAAAA", // green
        ".....A.A..AAAAAAAAAAAAAAAAAA............AAAAAAA.AAAAAAAAAAA", // orange
        ".....AA.........AAAAAAAAAAAA.............AAAAAAAAAAAAAAAAAA", // red
        "AAAAAAAAAAAAA..............AAAAA..AAAAAA......AA..AAAAAAAAA", // white
        "....AAAAAAAAAAAAAAAAAAAAAAAAA......AAAA.AAAAAAA.AAAAAAAAAAA", // yellow
    };

    private const int MaxGeneratedPlanets = 9;

    // Minimum orbit radius for rocky worlds and gas giants, per star size.
    private static readonly (int Rocky, int GasGiant)[] MinDistance =
    {
        (WorldTypes.MinPlanetRadius, 12 << 6),   // dwarf
        (8 << 6, 13 << 6),                       // giant
        (16 << 6, 33 << 6),                      // super giant
    };

    private static byte Chance(StarColor color, int planetType) =>
        Distributions[(int)color][planetType] == 'A' ? (byte)255 : (byte)0;

    /// <summary>
    /// FillOrbits. Fills <paramref name="count"/> worlds of
    /// <paramref name="worlds"/> from <paramref name="start"/>, or a random
    /// number (1 to 9) when <paramref name="count"/> is null, and returns how
    /// many. With <paramref name="typesDefined"/> the worlds keep the types
    /// already set. Sorts the filled worlds by orbit radius.
    /// </summary>
    public static int Fill(SysRandom rng, Star star, WorldDesc[] worlds, int start, int? count,
        bool typesDefined, bool moons)
    {
        int numPlanets;
        if (count is int n)
            numPlanets = n;
        else
        {
            // UQM spins until the result is non-zero instead of calling the
            // RNG once; kept to preserve the universe.
            do
                numPlanets = rng.RandomWord() % (MaxGeneratedPlanets + 1);
            while (numPlanets == 0);
        }

        int maxPlanet = moons ? WorldTypes.FirstLargeRockyWorld : WorldTypes.NumberOfPlanetTypes;
        var min = MinDistance[(int)star.Size];

        for (int p = start; p < start + numPlanets; ++p)
        {
            uint randVal;
            byte chance;
            do
            {
                randVal = rng.Random();
                if (typesDefined)
                    randVal = 0;
                else
                    worlds[p].DataIndex = (byte)((byte)((ushort)randVal >> 8) % maxPlanet);
                chance = Chance(star.Color, worlds[p].DataIndex);
            } while ((byte)randVal >= chance);

            int minRadius = worlds[p].DataIndex < WorldTypes.FirstGasGiant ? min.Rocky : min.GasGiant;

            bool relocate;
            do
            {
                relocate = false;
                randVal = rng.Random();
                if (moons)
                {
                    worlds[p].Radius = (short)(WorldTypes.MinMoonRadius
                        + ((ushort)randVal % WorldTypes.MaxMoons) * WorldTypes.MoonDelta);
                    for (int q = p - 1; q >= start && !relocate; --q)
                        relocate = worlds[p].Radius == worlds[q].Radius;
                    worlds[p].NumPlanets = 0;
                }
                else
                {
                    worlds[p].Radius = (short)(((ushort)randVal % (WorldTypes.MaxPlanetRadius - minRadius)) + minRadius);
                    for (int q = p - 1; q >= start && !relocate; --q)
                    {
                        int deltaR = (worlds[q].Radius >> 6) / 5 - (worlds[p].Radius >> 6) / 5;
                        if (deltaR < 0)
                            deltaR = -deltaR;
                        relocate = deltaR <= 1;
                    }
                }
            } while (relocate);

            randVal = rng.Random();
            int angle = UqmMath.NormalizeAngle((ushort)randVal);
            worlds[p].X = UqmMath.Cosine(angle, worlds[p].Radius);
            worlds[p].Y = UqmMath.Sine(angle, worlds[p].Radius);
            worlds[p].RandSeed = MakeDword(worlds[p].X, worlds[p].Y);
        }

        // UQM's sort by orbit radius, swap for swap.
        for (int i = 0; i < numPlanets; ++i)
        {
            for (int j = numPlanets - 1; j > i; --j)
            {
                if (worlds[start + i].Radius > worlds[start + j].Radius)
                    (worlds[start + i], worlds[start + j]) = (worlds[start + j], worlds[start + i]);
            }
        }

        return numPlanets;
    }

    /// <summary>MAKE_DWORD (lo, hi).</summary>
    public static uint MakeDword(short lo, short hi) => ((uint)(ushort)hi << 16) | (ushort)lo;
}
