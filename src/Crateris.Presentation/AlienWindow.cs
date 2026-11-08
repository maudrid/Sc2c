// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: InitCommunication's
// AnimContext, AlienTalkSegue's first drawing, UpdateAnimations;
// commanim.c: ProcessCommAnimations' drawing, DrawAlienFrame), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Content;
using Crateris.Core.Data;

namespace Crateris.Presentation;

/// <summary>
/// The alien's window in the communication screen: SIS_SCREEN_WIDTH wide
/// and as tall as the race's main frame, drawn as UQM draws it. At the
/// start the race's colour table's first entry is set and the main frame
/// drawn; then every <see cref="AlienAnimation.FrameTicks"/> the colour
/// fades step (a change redraws the whole picture), the animations step,
/// colour animations start their fades, and the frames that changed are
/// stamped over what was there. <see cref="Pixels"/> is the window's RGBA.
/// </summary>
public sealed class AlienWindow
{
    /// <summary>SIS_SCREEN_WIDTH.</summary>
    public const int Width = 242;

    private readonly SpriteSheet _frames;
    private readonly BinaryTable _colors;
    private readonly ColorFades _fades;
    private readonly Dictionary<int, SpriteImage> _rendered = new();
    private long _next;
    private bool _clear;

    /// <param name="seed">The client's own seed for the animations (decision 12).</param>
    /// <param name="now">The client's clock (ticks, ONE_SECOND 840) at the start.</param>
    public AlienWindow(AnimationSheets sheets, ContentPackage package, CommDescription race, uint seed, long now)
    {
        _frames = SpriteSheet.Load(sheets, race.Frames);
        _colors = BinaryTable.Load(package, race.ColorMap);
        var maps = new ColorMaps();
        maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
        maps.Set(_colors[0]);
        _fades = new ColorFades(maps);
        Animation = new AlienAnimation(race.Ambient, race.Talk, race.Transition, seed);
        Height = _frames.Render(0, maps).Height;   // GetFrameRect (CommData.AlienFrame)
        Pixels = new byte[Width * Height * 4];
        _next = now;
    }

    /// <summary>The window's height: the main frame's.</summary>
    public int Height { get; }

    /// <summary>The window's pixels, RGBA, row by row.</summary>
    public byte[] Pixels { get; }

    /// <summary>The race's animations (the talk starts and stops them).</summary>
    public AlienAnimation Animation { get; }

    /// <summary>The colour maps as they stand (fading).</summary>
    public ColorMaps Maps => _fades.Maps;

    /// <summary>The subtitles were cleared: the next frame redraws the whole picture (clear_subtitles).</summary>
    public void Clear() => _clear = true;

    /// <summary>
    /// UpdateAnimations at <paramref name="now"/>: each frame due steps
    /// (COMM_ANIM_RATE apart, as UQM's NextTime keeps them); whether the
    /// picture changed. <paramref name="paused"/> (seeking through the
    /// lines) steps the colour fades and nothing else.
    /// </summary>
    public bool Update(long now, bool paused = false)
    {
        if (now < _next)
            return false;
        _next = now + AlienAnimation.FrameTicks;
        if (paused)
        {
            if (_fades.Step(now))
                _rendered.Clear();
            return false;
        }
        bool colors = _fades.Step(now);
        if (colors)
            _rendered.Clear();
        bool full = colors || _clear;
        _clear = false;
        // The first frame draws the whole picture (AlienTalkSegue's DrawAlienFrame).
        var (frames, fades) = Animation.Step(AlienAnimation.FrameTicks, full);
        foreach (var fade in fades)
            _fades.Start(_colors[fade.Map].ToArray(), fade.Time, now);
        foreach (int frame in frames)
            Stamp(frame);
        return full || frames.Count > 0;
    }

    private void Stamp(int frame)
    {
        int index = frame == AlienAnimation.MainFrame ? 0 : frame;
        if (!_rendered.TryGetValue(index, out var image))
            _rendered[index] = image = _frames.Render(index, _fades.Maps);
        // s.origin (-SAFE_X, 0), less the hot spot; clipped to the window.
        int ox = -image.HotSpotX, oy = -image.HotSpotY;
        for (int y = 0; y < image.Height; ++y)
        {
            int ty = oy + y;
            if (ty < 0 || ty >= Height)
                continue;
            for (int x = 0; x < image.Width; ++x)
            {
                int tx = ox + x;
                if (tx < 0 || tx >= Width)
                    continue;
                int s = (y * image.Width + x) * 4, t = (ty * Width + tx) * 4;
                int a = image.Rgba[s + 3];
                if (a == 0)
                    continue;
                for (int c = 0; c < 3; ++c)
                    Pixels[t + c] = a == 255 ? image.Rgba[s + c]
                        : (byte)((image.Rgba[s + c] * a + Pixels[t + c] * (255 - a)) / 255);
                Pixels[t + 3] = 255;
            }
        }
    }
}
