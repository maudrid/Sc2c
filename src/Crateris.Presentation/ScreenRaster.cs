// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/libs/graphics: frame.c's and
// font.c's primitives, each clipped to its context; sdl/canvas.c's
// rectangles, images, filled images and font characters on their
// backing), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using System.Text;

namespace Crateris.Presentation;

/// <summary>
/// The screen (320 by 240) that UQM's drawing calls (<see cref="UqmDraw"/>)
/// are painted on, as UQM paints them: each only inside its clip
/// rectangle; a rectangle, an outline, a point or a cleared context in its
/// colour; a stamp's picture at its hot spot, with its transparency and
/// alpha; a filled stamp in the colour, in the picture's shape; text
/// through its font's characters (font_DrawText's TextRect and
/// _text_blt), each character's alpha laid over the colour or over the
/// font effect's frame (of graphics.fontgradient, or the call's effect
/// sheet: the context's font backing, its corner at each character's).
/// <para>
/// UQM draws a screen a change at a time over what is there, and so does
/// the port (<see cref="StarbaseScreens"/>): each step's calls are painted
/// over the picture the last ones left. The client paints the port's
/// calls with it; the tests paint UQM's and the port's on the real
/// content, which checks that it paints them all.
/// </para>
/// </summary>
public sealed class ScreenRaster
{
    public const int Width = UqmCanvas.ScreenWidth, Height = UqmCanvas.ScreenHeight;

    private readonly Func<string, int, SpriteImage> _frame;
    private readonly Func<string, UqmFont> _font;
    private readonly Func<string, string> _text;

    /// <param name="frame">A sheet's frame as drawn (<see cref="SpriteSheet.Render"/>, in the colour maps as they stand).</param>
    /// <param name="font">A font by the name a call gives it (starcon, tiny, micro).</param>
    /// <param name="text">The words a call's text stands for (the game's strings put in); as given if null.</param>
    public ScreenRaster(Func<string, int, SpriteImage> frame, Func<string, UqmFont> font, Func<string, string>? text = null)
    {
        _frame = frame;
        _font = font;
        _text = text ?? (t => t);
    }

    /// <summary>The picture, row by row, four bytes (RGBA, A always 255) a pixel; black at first.</summary>
    public byte[] Rgba { get; } = NewScreen();

    private static byte[] NewScreen()
    {
        var b = new byte[Width * Height * 4];
        for (int i = 3; i < b.Length; i += 4)
            b[i] = 255;
        return b;
    }

    public void Paint(IEnumerable<UqmDraw> calls)
    {
        foreach (var d in calls)
            Paint(d);
    }

    public void Paint(UqmDraw d)
    {
        // The clip rectangle, within the screen.
        int cx0 = Math.Max(0, d.Clip.X), cy0 = Math.Max(0, d.Clip.Y);
        int cx1 = Math.Min(Width, d.Clip.X + d.Clip.W), cy1 = Math.Min(Height, d.Clip.Y + d.Clip.H);
        if (cx0 >= cx1 || cy0 >= cy1)
            return;
        var clip = (cx0, cy0, cx1, cy1);
        switch (d.Kind)
        {
            case UqmDrawKind.Rect:
            case UqmDrawKind.Clear:
                Fill(clip, d.X, d.Y, d.Width, d.Height, d.Color);
                break;
            case UqmDrawKind.Outline:
                if (d.Width <= 0 || d.Height <= 0)
                    break;
                Fill(clip, d.X, d.Y, d.Width, 1, d.Color);
                Fill(clip, d.X, d.Y + d.Height - 1, d.Width, 1, d.Color);
                Fill(clip, d.X, d.Y, 1, d.Height, d.Color);
                Fill(clip, d.X + d.Width - 1, d.Y, 1, d.Height, d.Color);
                break;
            case UqmDrawKind.Point:
                Fill(clip, d.X, d.Y, 1, 1, d.Color);
                break;
            case UqmDrawKind.Line:
                Line(clip, d.X, d.Y, d.Width, d.Height, d.Color);
                break;
            case UqmDrawKind.Stamp:
            case UqmDrawKind.FilledStamp:
            {
                var img = _frame(d.Sheet, d.Frame);
                bool filled = d.Kind == UqmDrawKind.FilledStamp;
                // TFB_DrawCanvas_Rescale_Nearest's picture of a scaled stamp: its size and hot spot scaled down,
                // each pixel taken from the source's, stepping from a quarter-and-a-bit in to spread the jumps.
                int w = img.Width, h = img.Height, hx = img.HotSpotX, hy = img.HotSpotY;
                int fsx = 0, fsy = 0;
                bool scaled = d.Scale > 0 && d.Scale != GraphicScaleIdentity;
                if (scaled)
                {
                    w = img.Width * d.Scale / GraphicScaleIdentity;
                    h = img.Height * d.Scale / GraphicScaleIdentity;
                    hx = img.HotSpotX * d.Scale / GraphicScaleIdentity;
                    hy = img.HotSpotY * d.Scale / GraphicScaleIdentity;
                    if (w == 0 && img.Width > 0)
                        w = 1;
                    if (h == 0 && img.Height > 0)
                        h = 1;
                    if (w > 1)
                        fsx = ((img.Width - 1) << 16) / (w - 1);
                    if (h > 1)
                        fsy = ((img.Height - 1) << 16) / (h - 1);
                }
                int left = d.X - hx, top = d.Y - hy;
                for (int y = 0, sy = 0x6000, srcY = 0; y < h; ++y)
                {
                    if (scaled)
                    {
                        srcY += sy >> 16;
                        sy = (sy & 0xffff) + fsy;
                    }
                    else
                        srcY = y;
                    for (int x = 0, sx = 0x6000, srcX = 0; x < w; ++x)
                    {
                        if (scaled)
                        {
                            srcX += sx >> 16;
                            sx = (sx & 0xffff) + fsx;
                        }
                        else
                            srcX = x;
                        int o = (srcY * img.Width + srcX) * 4;
                        byte a = img.Rgba[o + 3];
                        if (a == 0)
                            continue;
                        if (filled)
                            Blend(clip, left + x, top + y, d.Color.R, d.Color.G, d.Color.B, a);
                        else
                            Blend(clip, left + x, top + y, img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2], a);
                    }
                }
                break;
            }
            case UqmDrawKind.Text:
                Text(clip, d);
                break;
        }
    }

    private void Text(in (int X0, int Y0, int X1, int Y1) clip, UqmDraw d)
    {
        var font = _font(d.Font);
        string text = _text(d.Text);
        if (d.Count >= 0)
        {
            // TEXT.CharCount: so many characters, each a code point as UQM's getCharFromString takes them.
            var cut = new StringBuilder();
            foreach (var rune in text.EnumerateRunes().Take(d.Count))
                cut.Append(rune.ToString());
            text = cut.ToString();
        }
        var layout = font.Layout(text, d.X, d.Y, d.Align);
        if (!layout.Drawn)
            return;
        // The context's font backing (FixContextFontEffect): an image the font's widest character by its leading,
        // filled with the colour, or with the effect's frame drawn at minus its hot spot over black. Each
        // character's alpha takes the backing's from its corner (TFB_DrawCanvas_FontChar); a character bigger than
        // the backing isn't drawn.
        int bw = font.LeadingWidth, bh = font.Leading;
        var backing = new byte[bw * bh * 3];
        if (d.Effect >= 0)
        {
            var e = _frame(d.EffectSheet is "" ? StarbaseScreens.FontGradientSheet : d.EffectSheet, d.Effect);
            for (int y = 0; y < e.Height; ++y)
                for (int x = 0; x < e.Width; ++x)
                {
                    int bx = x - e.HotSpotX, by = y - e.HotSpotY;
                    if (bx < 0 || by < 0 || bx >= bw || by >= bh)
                        continue;
                    int o = (y * e.Width + x) * 4, t = (by * bw + bx) * 3;
                    (backing[t], backing[t + 1], backing[t + 2]) = (e.Rgba[o], e.Rgba[o + 1], e.Rgba[o + 2]);
                }
        }
        else
            for (int t = 0; t < backing.Length; t += 3)
                (backing[t], backing[t + 1], backing[t + 2]) = (d.Color.R, d.Color.G, d.Color.B);
        foreach (var p in layout.Glyphs)
        {
            var g = p.Glyph;
            if (g.Width > bw || g.Height > bh)
                continue;
            for (int y = 0; y < g.Height; ++y)
                for (int x = 0; x < g.Width; ++x)
                {
                    byte a = g.Alpha[y * g.Width + x];
                    if (a == 0)
                        continue;
                    int t = (y * bw + x) * 3;
                    Blend(clip, p.Left + x, p.Top + y, backing[t], backing[t + 1], backing[t + 2], a);
                }
        }
    }

    private void Fill(in (int X0, int Y0, int X1, int Y1) clip, int x, int y, int w, int h, Crateris.Core.Systems.Rgb c)
    {
        int x0 = Math.Max(clip.X0, x), y0 = Math.Max(clip.Y0, y);
        int x1 = Math.Min(clip.X1, x + w), y1 = Math.Min(clip.Y1, y + h);
        for (int py = y0; py < y1; ++py)
            for (int px = x0; px < x1; ++px)
            {
                int o = (py * Width + px) * 4;
                (Rgba[o], Rgba[o + 1], Rgba[o + 2]) = (c.R, c.G, c.B);
            }
    }

    /// <summary>GSCALE_IDENTITY: a graphic scale drawing a stamp at its own size.</summary>
    public const int GraphicScaleIdentity = 256;

    // line_prim's Bresenham line, both ends drawn; UQM clips the line to the
    // rectangle first (clip_line), this tests each pixel, which draws the same
    // pixels for a line starting inside it.
    private void Line(in (int X0, int Y0, int X1, int Y1) clip, int x1, int y1, int x2, int y2, Crateris.Core.Systems.Rgb c)
    {
        int dx = x2 - x1, ax = Math.Abs(dx) << 1, sx = dx < 0 ? -1 : 1;
        int dy = y2 - y1, ay = Math.Abs(dy) << 1, sy = dy < 0 ? -1 : 1;
        int x = x1, y = y1, d;
        if (ax > ay)
        {
            d = ay - (ax >> 1);
            for (;;)
            {
                Blend(clip, x, y, c.R, c.G, c.B, 255);
                if (x == x2)
                    return;
                if (d >= 0)
                {
                    y += sy;
                    d -= ax;
                }
                x += sx;
                d += ay;
            }
        }
        d = ax - (ay >> 1);
        for (;;)
        {
            Blend(clip, x, y, c.R, c.G, c.B, 255);
            if (y == y2)
                return;
            if (d >= 0)
            {
                x += sx;
                d -= ay;
            }
            y += sy;
            d += ax;
        }
    }

    private void Blend(in (int X0, int Y0, int X1, int Y1) clip, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < clip.X0 || x >= clip.X1 || y < clip.Y0 || y >= clip.Y1)
            return;
        int o = (y * Width + x) * 4;
        if (a == 255)
        {
            (Rgba[o], Rgba[o + 1], Rgba[o + 2]) = (r, g, b);
            return;
        }
        Rgba[o] = (byte)((r * a + Rgba[o] * (255 - a) + 127) / 255);
        Rgba[o + 1] = (byte)((g * a + Rgba[o + 1] * (255 - a) + 127) / 255);
        Rgba[o + 2] = (byte)((b * a + Rgba[o + 2] * (255 - a) + 127) / 255);
    }

    /// <summary>The pixels where this picture and <paramref name="other"/> differ, at most <paramref name="max"/>.</summary>
    public List<(int X, int Y)> Differences(ScreenRaster other, int max = 20)
    {
        var diff = new List<(int, int)>();
        for (int i = 0; i < Rgba.Length && diff.Count < max; i += 4)
            if (Rgba[i] != other.Rgba[i] || Rgba[i + 1] != other.Rgba[i + 1] || Rgba[i + 2] != other.Rgba[i + 2])
                diff.Add((i / 4 % Width, i / 4 / Width));
        return diff;
    }
}
