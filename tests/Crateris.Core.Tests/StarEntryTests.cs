// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>The flagship's entry into a star, as UQM's init_transition and unhyper_transition play it.</summary>
[Trait("Topic", "hyperspace")]
public class StarEntryTests
{
    private static readonly Star Sol = new(82, 1752, 1450, StarSize.Dwarf, StarColor.Yellow, "SOL_DEFINED");

    [Fact]
    public void TheShipTurnsToFacingZeroThenPlaysTheEntryFrames()
    {
        long sx = Units.UniverseToLogX(Sol.X), sy = Units.UniverseToLogY(Sol.Y);
        // Facing 4 (right): 5 to 15, 0, then frames 16 to 33 of the 34; 30 frames in all.
        var steps = StarEntry.Animate(sx - 90, sy + 60, 4, Sol, 34);
        Assert.Equal(Enumerable.Range(5, 11).Append(0).Concat(Enumerable.Range(16, 18)), steps.Select(s => s.Frame));
        Assert.Equal(34 - 16 + 12, steps.Count);   // num_turns
        // Facing 0: straight to the entry frames.
        Assert.Equal(Enumerable.Range(16, 18), StarEntry.Animate(sx, sy, 0, Sol, 34).Select(s => s.Frame));
    }

    [Fact]
    public void TheShipDriftsOntoTheStar()
    {
        long sx = Units.UniverseToLogX(Sol.X), sy = Units.UniverseToLogY(Sol.Y);
        var steps = StarEntry.Animate(sx - 90, sy + 60, 4, Sol, 34);
        // A steady velocity: dx / num_turns in velocity units (1/32 of a logical unit).
        Assert.InRange(Math.Abs(steps[^1].X - sx), 0, 3);
        Assert.InRange(Math.Abs(steps[^1].Y - sy), 0, 3);
        // Closer every frame.
        for (int i = 1; i < steps.Count; ++i)
            Assert.True(Math.Abs(steps[i].X - sx) <= Math.Abs(steps[i - 1].X - sx));
        Assert.Throws<ArgumentException>(() => StarEntry.Animate(sx, sy, 0, Sol, 16));
    }
}
