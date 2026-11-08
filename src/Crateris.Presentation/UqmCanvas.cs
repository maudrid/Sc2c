// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/libs/graphics: the contexts'
// clip rectangles and colours, the drawing primitives; src/uqm/setup.c
// InitContexts, border.c InitSISContexts), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under
// the GNU GPL 2 or later.

using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>What one of UQM's drawing calls is.</summary>
public enum UqmDrawKind
{
    /// <summary>DrawStamp: <see cref="UqmDraw.Sheet"/>'s frame, its hot spot at (X, Y).</summary>
    Stamp,

    /// <summary>DrawFilledStamp: the frame's shape in the colour.</summary>
    FilledStamp,

    /// <summary>DrawFilledRectangle.</summary>
    Rect,

    /// <summary>DrawRectangle: its one-pixel outline.</summary>
    Outline,

    /// <summary>DrawPoint.</summary>
    Point,

    /// <summary>ClearDrawable: the context's clip rectangle in its background colour.</summary>
    Clear,

    /// <summary>font_DrawText: <see cref="UqmDraw.Text"/> in <see cref="UqmDraw.Font"/> from its baseline, in the colour or the font effect's frame.</summary>
    Text,
}

/// <summary>
/// One of UQM's drawing calls in the screen's pixels (320 by 240): the
/// shape of golden/screens.c's recording. A stamp's frame is its index in
/// the sheet (a GFXRES resource); text names its font (starcon, tiny,
/// micro), its alignment, how many characters (-1 all) and its effect
/// (a frame of graphics.fontgradient the characters take their colours
/// from, or -1).
/// </summary>
public readonly record struct UqmDraw(UqmDrawKind Kind, int X, int Y, int Width = 0, int Height = 0, Rgb Color = default,
    string Sheet = "", int Frame = 0, string Font = "", TextAlign Align = TextAlign.Left, int Count = -1, string Text = "",
    int Effect = -1);

/// <summary>UQM's RECT.</summary>
public struct URect
{
    public int X, Y, W, H;

    public URect(int x, int y, int w, int h) => (X, Y, W, H) = (x, y, w, h);
}

/// <summary>
/// UQM's drawing contexts and primitives, recording each call in the
/// screen's coordinates (a context's clip rectangle's corner added) as
/// golden/screens.c records UQM's: what the starbase's screens are ported
/// onto, so they read as UQM's code does. The contexts are setup.c's and
/// border.c's: the screen, the status area, the SIS window and the
/// off-screen one (clipped as each use sets it).
/// </summary>
public sealed class UqmCanvas
{
    public const int ScreenWidth = 320, ScreenHeight = 240;
    public const int SafeX = 0, SafeY = 0;
    public const int StatusWidth = 64, StatusHeight = ScreenHeight - SafeY * 2;
    public const int SpaceWidth = ScreenWidth - StatusWidth - SafeX * 2, SpaceHeight = ScreenHeight - SafeY * 2;
    public const int SisOrgX = 7 + SafeX, SisOrgY = 10 + SafeY;
    public const int SisScreenWidth = SpaceWidth - 14, SisScreenHeight = SpaceHeight - 13;
    public const int RadarX = 4 + (ScreenWidth - StatusWidth - SafeX), RadarWidth = StatusWidth - 8, RadarHeight = 53;
    public const int RadarY = SisOrgY + SisScreenHeight - RadarHeight;

    public sealed class Context
    {
        public URect? Clip;
        public Rgb Fg, Bg;
        public string Font = "";
        public int Effect = -1;
    }

    public readonly Context Screen = new(), Status = new(), Space = new(), OffScreen = new();
    private Context _cur;
    private readonly Func<string, int> _frameCount;
    private readonly Func<string, int, URect> _frameRect;
    private readonly IReadOnlyDictionary<string, int> _leading;

    /// <param name="frameCount">A sheet's number of frames (SetAbsFrameIndex wraps at it).</param>
    /// <param name="frameRect">A frame's GetFrameRect: its corner minus the hot spot, and its size.</param>
    /// <param name="leading">Each font's leading, by name.</param>
    public UqmCanvas(Func<string, int> frameCount, Func<string, int, URect> frameRect, IReadOnlyDictionary<string, int> leading)
    {
        _frameCount = frameCount;
        _frameRect = frameRect;
        _leading = leading;
        _cur = Screen;
        Status.Clip = new URect(SpaceWidth + SafeX, SafeY, StatusWidth, StatusHeight);
        Space.Clip = new URect(SisOrgX, SisOrgY, SisScreenWidth, SisScreenHeight);
    }

    /// <summary>What was drawn, in order.</summary>
    public List<UqmDraw> Drawn { get; } = new();

    public Context SetContext(Context c)
    {
        var old = _cur;
        _cur = c;
        return old;
    }

    public Rgb SetForeground(Rgb c)
    {
        var old = _cur.Fg;
        _cur.Fg = c;
        return old;
    }

    public Rgb SetBackground(Rgb c)
    {
        var old = _cur.Bg;
        _cur.Bg = c;
        return old;
    }

    public string SetFont(string f)
    {
        var old = _cur.Font;
        _cur.Font = f;
        return old;
    }

    /// <summary>SetContextFontEffect: a frame of graphics.fontgradient, -1 none.</summary>
    public int SetFontEffect(int frame)
    {
        int old = _cur.Effect;
        _cur.Effect = frame;
        return old;
    }

    public void SetClipRect(URect? r) => _cur.Clip = r;

    /// <summary>GetContextClipRect: the screen when not clipped.</summary>
    public URect ClipRect => _cur.Clip ?? new URect(0, 0, ScreenWidth, ScreenHeight);

    public int Leading => _leading.GetValueOrDefault(_cur.Font);

    /// <summary>SetAbsFrameIndex: the index wrapped at the sheet's size.</summary>
    public int Frame(string sheet, int index) => index % _frameCount(sheet);

    public int FrameCount(string sheet) => _frameCount(sheet);

    public URect FrameRect(string sheet, int index) => _frameRect(sheet, Frame(sheet, index));

    private int Ox => _cur.Clip?.X ?? 0;
    private int Oy => _cur.Clip?.Y ?? 0;

    public void FillRect(URect r) => Drawn.Add(new UqmDraw(UqmDrawKind.Rect, Ox + r.X, Oy + r.Y, r.W, r.H, _cur.Fg));

    public void Outline(URect r) => Drawn.Add(new UqmDraw(UqmDrawKind.Outline, Ox + r.X, Oy + r.Y, r.W, r.H, _cur.Fg));

    public void Point(int x, int y) => Drawn.Add(new UqmDraw(UqmDrawKind.Point, Ox + x, Oy + y, Color: _cur.Fg));

    public void Clear()
    {
        var r = ClipRect;
        Drawn.Add(new UqmDraw(UqmDrawKind.Clear, Ox, Oy, r.W, r.H, _cur.Bg));
    }

    public void Stamp(string sheet, int frame, int x, int y) =>
        Drawn.Add(new UqmDraw(UqmDrawKind.Stamp, Ox + x, Oy + y, Sheet: sheet, Frame: Frame(sheet, frame)));

    public void FilledStamp(string sheet, int frame, int x, int y) =>
        Drawn.Add(new UqmDraw(UqmDrawKind.FilledStamp, Ox + x, Oy + y, Color: _cur.Fg, Sheet: sheet, Frame: Frame(sheet, frame)));

    public void Text(int x, int y, TextAlign align, string text, int count = -1) =>
        Drawn.Add(new UqmDraw(UqmDrawKind.Text, Ox + x, Oy + y, Color: _cur.Fg, Font: _cur.Font, Align: align, Count: count,
            Text: text, Effect: _cur.Effect));

    /// <summary>DrawStarConBox (util.c).</summary>
    public void StarConBox(URect r, int border, Rgb topLeft, Rgb bottomRight, bool fill, Rgb interior)
    {
        if (border == 0)
            border = 2;
        else
        {
            SetForeground(topLeft);
            var l = new URect(r.X, r.Y, r.W, 1);
            FillRect(l);
            if (border == 2)
            {
                ++l.X;
                ++l.Y;
                l.W -= 2;
                FillRect(l);
            }
            l = new URect(r.X, r.Y, 1, r.H);
            FillRect(l);
            if (border == 2)
            {
                ++l.X;
                ++l.Y;
                l.H -= 2;
                FillRect(l);
            }
            SetForeground(bottomRight);
            l.X = r.X + r.W - 1;
            l.Y = r.Y + 1;
            l.H = r.H - 1;
            FillRect(l);
            if (border == 2)
            {
                --l.X;
                ++l.Y;
                l.H -= 2;
                FillRect(l);
            }
            l.X = r.X;
            l.W = r.W;
            l.Y = r.Y + r.H - 1;
            l.H = 1;
            FillRect(l);
            if (border == 2)
            {
                ++l.X;
                --l.Y;
                l.W -= 2;
                FillRect(l);
            }
        }
        if (fill)
        {
            SetForeground(interior);
            FillRect(new URect(r.X + border, r.Y + border, r.W - (border << 1), r.H - (border << 1)));
        }
    }
}
