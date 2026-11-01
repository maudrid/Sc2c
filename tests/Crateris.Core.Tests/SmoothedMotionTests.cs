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
                    drawn.Add(motion.AtTime(f - 1 + step / 4.0).X);
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
        Assert.Equal((10.0, 20.0), motion.AtTime(2.5));
        motion.Add(3, 999, 999, "inner");   // a repeat of frame 3 is ignored
        Assert.Equal((10.0, 20.0), motion.AtTime(2));
    }

    [Fact]
    public void AGapIsntBlendedAcross()
    {
        // In a view, then in orbit for a while, then back in the same view:
        // the old position mustn't pull the ship back towards it.
        var motion = new SmoothedMotion();
        motion.Add(1, 0, 0, "earth");
        motion.Add(2, 10, 0, "earth");
        motion.Add(100, 50, 50, "earth");
        Assert.Equal((50.0, 50.0), motion.AtTime(99.5));
        // A snapshot or two lost is still blended across.
        motion.Add(102, 60, 50, "earth");
        Assert.Equal(1, motion.AtTime(101.5).X.CompareTo(50.0));
    }

    [Fact]
    public void PlaybackClockRunsEvenlyAFrameBehindUnevenSnapshots()
    {
        var clock = new PlaybackClock();
        clock.Observe(10);
        Assert.Equal(10 - PlaybackClock.Delay, clock.Time);
        // Snapshots arrive in bursts (two in one tick, then none), but the
        // clock moves on the local time, never past the newest frame.
        double last = clock.Time;
        long frame = 10;
        for (int tick = 0; tick < 48; ++tick)
        {
            if (tick % 2 == 0)
            {
                clock.Observe(++frame);
                clock.Observe(++frame);
            }
            clock.Advance(1.0 / 24);
            Assert.True(clock.Time > last, $"tick {tick}: the clock stood still");
            Assert.True(clock.Time <= frame);
            last = clock.Time;
        }
        Assert.True(frame - clock.Time is > 0 and <= 2.5, $"lag {frame - clock.Time}");

        // Far behind (a stall): it jumps back into step.
        clock.Observe(frame + 20);
        Assert.Equal(frame + 20 - PlaybackClock.Delay, clock.Time);
    }
}
