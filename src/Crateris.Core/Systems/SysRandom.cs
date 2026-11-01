// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (libs/math/random2.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// UQM's RandomContext (libs/math/random2.c): a Park–Miller generator
/// whose state is one seed. Solar systems and planets are generated with
/// it (UQM's SysGenRNG), so it has to match UQM exactly.
/// </summary>
public sealed class SysRandom
{
    private const uint A = 16807;
    private const uint M = 2147483647;
    private const uint Q = 127773;
    private const uint R = 2836;

    /// <summary>RandomContext_New: seed 12345.</summary>
    public SysRandom()
    {
        Seed = 12345;
    }

    public SysRandom(uint seed)
    {
        Seed = 12345;
        SeedRandom(seed);
    }

    /// <summary>RandomContext_GetSeed.</summary>
    public uint Seed { get; private set; }

    /// <summary>RandomContext_Random.</summary>
    public uint Random()
    {
        // DWORD arithmetic, as in the C code.
        uint seed = unchecked(A * (Seed % Q) - R * (Seed / Q));
        if (seed > M)
            seed -= M;
        else if (seed == 0)
            seed = 1;
        Seed = seed;
        return seed;
    }

    /// <summary>LOWORD (Random ()).</summary>
    public ushort RandomWord() => (ushort)Random();

    /// <summary>RandomContext_SeedRandom: 0 becomes 1, above M subtracts M. Returns the old seed.</summary>
    public uint SeedRandom(uint newSeed)
    {
        if (newSeed == 0)
            newSeed = 1;
        else if (newSeed > M)
            newSeed -= M;
        uint old = Seed;
        Seed = newSeed;
        return old;
    }
}
