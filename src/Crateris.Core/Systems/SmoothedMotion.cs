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
    /// <summary>
    /// A longer gap between positions than this (world frames) isn't
    /// blended across: the ship was elsewhere (in orbit, say) or many
    /// snapshots were lost.
    /// </summary>
    public const int MaxGap = 4;

    private readonly List<(long WorldFrame, double X, double Y)> _samples = new();
    private object? _view;

    /// <summary>The newest world frame added, or -1.</summary>
    public long Newest => _samples.Count > 0 ? _samples[^1].WorldFrame : -1;

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
        if (_samples.Count > 0 && worldFrame - _samples[^1].WorldFrame > MaxGap)
            _samples.Clear();
        _samples.Add((worldFrame, x, y));
        if (_samples.Count > 4)
            _samples.RemoveAt(0);
    }

    /// <summary>
    /// The position to draw at a (fractional) world frame time, normally
    /// from a <see cref="PlaybackClock"/>: at most the newest frame, about a
    /// frame behind it.
    /// </summary>
    public (double X, double Y) AtTime(double worldTime)
    {
        if (_samples.Count == 0)
            return (0, 0);
        var newest = _samples[^1];
        // A system frame behind the steady time: world frame n reached
        // system frame floor(1.25 n), never less than 1.25 n - 1, so the
        // newest sample is far enough ahead while the time is at most n.
        double t = SystemFramesTime(Math.Min(worldTime, newest.WorldFrame)) - 1;
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

/// <summary>
/// The time ships in systems are drawn at, in world frames: it runs on the
/// local clock at 24 frames a second, about a frame behind the newest
/// snapshot, so the drawing stays even when snapshots arrive unevenly over
/// the network. It never runs past the newest frame, and jumps back into
/// step if it falls too far behind (or a game is joined).
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>How far behind the newest frame the clock aims to run.</summary>
    public const double Delay = 1.5;

    /// <summary>Further behind than this, it jumps to <see cref="Delay"/>.</summary>
    public const double MaxLag = 3;

    private long _newest = -1;

    /// <summary>The time to draw at (world frames).</summary>
    public double Time { get; private set; }

    /// <summary>A snapshot of world frame <paramref name="frame"/> has arrived.</summary>
    public void Observe(long frame)
    {
        if (frame <= _newest)
            return;
        _newest = frame;
        if (Time < frame - MaxLag || Time > frame)
            Time = frame - Delay;
    }

    /// <summary>Local time passes.</summary>
    public void Advance(double seconds)
    {
        if (_newest < 0)
            return;
        // A little faster when behind, a little slower when ahead, so the
        // delay settles at Delay without visible jumps.
        double lag = _newest - Time;
        double rate = 1 + Math.Clamp((lag - Delay) * 0.1, -0.1, 0.1);
        Time = Math.Min(_newest, Time + seconds * GameClock.FramesPerSecond * rate);
    }
}
