// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Systems;

/// <summary>
/// Draws a ship in a system moving evenly although the host runs one or
/// two 30-a-second system frames in each 24-a-second world frame (stage 2,
/// decision 8). Each world frame's position is placed at the system frame
/// it was reached on, and the drawn position is read between them at a
/// steady system-frame time, a little over a world frame behind the newest.
/// Positions of different views (another system, inner system or zoom)
/// aren't blended: a new view starts afresh.
/// </summary>
public sealed class SmoothedMotion
{
    private readonly List<(long WorldFrame, double X, double Y)> _samples = new();
    private object? _view;

    /// <summary>System frames run by the end of a world frame (the sum of <see cref="GameWorld.SystemFramesIn"/>).</summary>
    public static double SystemFramesBy(double worldFrame) =>
        Math.Floor(worldFrame * IpFlight.FrameRate / GameClock.FramesPerSecond);

    /// <summary>
    /// A world frame's position. <paramref name="view"/> is anything that
    /// tells views apart; positions only blend within one view.
    /// </summary>
    public void Add(long worldFrame, double x, double y, object view)
    {
        if (!Equals(view, _view))
        {
            _samples.Clear();
            _view = view;
        }
        if (_samples.Count > 0 && worldFrame <= _samples[^1].WorldFrame)
            return;   // late or repeated
        _samples.Add((worldFrame, x, y));
        if (_samples.Count > 4)
            _samples.RemoveAt(0);
    }

    /// <summary>
    /// The position to draw. <paramref name="sinceNewest"/> is how far
    /// into the newest world frame drawing is, 0 to 1.
    /// </summary>
    public (double X, double Y) At(double sinceNewest)
    {
        if (_samples.Count == 0)
            return (0, 0);
        var newest = _samples[^1];
        // A world frame and a system frame behind the steady time: world
        // frame n reached system frame floor(1.25 n), never less than
        // 1.25 n - 1, so the newest sample is always far enough ahead.
        double t = SystemFramesTime(newest.WorldFrame - 1 + Math.Clamp(sinceNewest, 0, 1)) - 1;
        for (int i = _samples.Count - 1; i > 0; --i)
        {
            var a = _samples[i - 1];
            var b = _samples[i];
            double ta = SystemFramesBy(a.WorldFrame), tb = SystemFramesBy(b.WorldFrame);
            if (t < ta || tb <= ta)
                continue;
            double f = Math.Min(1, (t - ta) / (tb - ta));
            return (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        }
        var first = _samples[0];
        return t <= SystemFramesBy(first.WorldFrame) ? (first.X, first.Y) : (newest.X, newest.Y);
    }

    // The steady system-frame time at a (fractional) world frame.
    private static double SystemFramesTime(double worldFrame) =>
        worldFrame * IpFlight.FrameRate / GameClock.FramesPerSecond;
}
