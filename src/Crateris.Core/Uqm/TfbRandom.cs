// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0, src/libs/math/random.c
// (TFB_Random, TFB_SeedRandom), Copyright Paul Reiche, Fred Ford and the
// UQM contributors, GPL v2 or later.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Uqm;

/// <summary>
/// UQM's general random generator, TFB_Random: the "minimal standard"
/// generator from the CACM article (Park and Miller), as UQM writes it with
/// an unsigned 32-bit seed. Where the signed version adds M to a negative
/// step, UQM's wraps round 2^32 and then takes M off, so its numbers differ
/// from the textbook's; this keeps UQM's. Everything random on a planet's
/// surface that isn't generated from the world's seed draws on it: where
/// the lander comes down, the way it faces, hazards, creatures and hits.
/// </summary>
public sealed class TfbRandom
{
    private const long A = 16807, Q = 127773, R = 2836;
    private const uint M = 2147483647;

    /// <summary>The seed, as TFB_SeedRandom sets it: 0 becomes 1, above M has M taken off.</summary>
    public TfbRandom(uint seed) => Seed = seed == 0 ? 1 : seed > M ? seed - M : seed;

    /// <summary>The seed now: the last number drawn (or the one seeded).</summary>
    public uint Seed { get; private set; }

    /// <summary>The next number, 1 to 2^32 - 1 as UQM's arithmetic gives it (TFB_Random).</summary>
    public uint Next()
    {
        uint seed = unchecked((uint)(A * (Seed % Q) - R * (Seed / Q)));
        if (seed > M)
            seed -= M;
        else if (seed == 0)
            seed = 1;
        Seed = seed;
        return seed;
    }
}
