// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>Ships in systems are drawn moving evenly (stage 2, decision 8).</summary>
public class SmoothedMotionTests
{
    [Fact]
    public void SteadyFlightIsDrawnAtASteadySpeed()
    {
        // A ship moving 3 pixels a system frame. The host runs 1, 1, 1, 2,
        // ... system frames a world frame, so world-frame positions jump
        // unevenly; drawn positions must not.
        var motion = new SmoothedMotion();
        var drawn = new List<double>();
        long systemFrames = 0;
        for (long f = 1; f <= 40; ++f)
        {
            systemFrames += GameWorld.SystemFramesIn(f);
            Assert.Equal(systemFrames, SmoothedMotion.SystemFramesBy(f));
            motion.Add(f, 3 * systemFrames, 0, "sol");
            if (f > 4)
            {
                for (int step = 0; step < 4; ++step)
                    drawn.Add(motion.At(step / 4.0).X);
            }
        }
        var speeds = drawn.Zip(drawn.Skip(1), (a, b) => b - a).ToList();
        Assert.All(speeds, s => Assert.Equal(3 * 1.25 / 4, s, 6));
    }

    [Fact]
    public void ANewViewIsntBlendedWithTheOld()
    {
        var motion = new SmoothedMotion();
        motion.Add(1, 100, 100, "outer");
        motion.Add(2, 110, 100, "outer");
        motion.Add(3, 10, 20, "inner");
        Assert.Equal((10.0, 20.0), motion.At(0.5));
        motion.Add(3, 999, 999, "inner");   // a repeat of frame 3 is ignored
        Assert.Equal((10.0, 20.0), motion.At(0));
    }
}
