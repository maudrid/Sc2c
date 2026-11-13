// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/galaxy.c: WrapStarBlock,
// CmpMovePoints and MoveGalaxy for a battle; src/uqm/process.c:
// CALC_ZOOM_STUFF, CalcDisplayCoord and PostProcessQueue's display
// coordinates), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Combat;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>
/// One primitive of a battle's space as UQM's DrawBatch draws it: its kind,
/// its place in the space window's pixels (a line's two ends), a stamp's
/// sheet (the size the zoom picked) and frame, and its colour (RGB15, as
/// the elements keep it; -1 never set).
/// </summary>
public readonly record struct BattlePrim(PrimType Type, int X, int Y, int X2, int Y2, string Sheet, int Frame, int Color);

/// <summary>
/// The camera a battle is drawn from in one frame: UQM's view state (0
/// stable, 1 scrolled, 2 zoomed), its scroll (CalcView's dx and dy, which
/// moved every element and moves the stars), its zoom (zoom_out) and the
/// view's corner (SpaceOrg), and whether the zoom is UQM's step zoom
/// rather than its smooth one.
/// </summary>
public readonly record struct BattleCamera(int ViewState, int ScrollX, int ScrollY, int ZoomOut, int OrgX, int OrgY, bool Step)
{
    public const int ZoomShift = 8, OneShift = 2;

    /// <summary>The reduction elements are drawn at (PostProcessQueue's).</summary>
    public int Reduction => Step ? ZoomOut + OneShift : ZoomOut << OneShift;

    /// <summary>CalcDisplayCoord: a world coordinate in the window's pixels.</summary>
    public int Display(int c, int org) => (short)(Step ? (c - org) >> Reduction : ((c - org) << ZoomShift) / Reduction);

    /// <summary>The farray size the zoom draws (CALC_ZOOM_STUFF's index; the step zoom's own).</summary>
    public int Size => Step ? ZoomOut : ZoomOut < 2 << ZoomShift ? 0 : ZoomOut < 4 << ZoomShift ? 1 : 2;

    /// <summary>The graphic scale the batch is drawn at (SetGraphicScale; 0 none, for the step zoom).</summary>
    public int Scale => Step ? 0 : (1 << (Size + ZoomShift + 8)) / ZoomOut;
}

/// <summary>
/// UQM's stars behind a battle (galaxy.c, outside HyperSpace): three planes
/// of InitGalaxy's 180 stars, the near ones moving with the camera's
/// scroll, the farther at half and a quarter its speed (WrapStarBlock),
/// drawn as stamps or points by the zoom; each plane's places recomputed
/// only while its first star moves (CmpMovePoints), as UQM's are. The
/// stars are 16-bit, as UQM's.
/// </summary>
public sealed class BattleGalaxy
{
    public const int BigStars = 30, MedStars = 60, SmallStars = 90, NumStars = BigStars + MedStars + SmallStars;
    public const string Stars = "graphics.stars";
    private const int LogSpaceWidth = Core.Uqm.Units.LogSpaceWidth, LogSpaceHeight = Core.Uqm.Units.LogSpaceHeight;
    private static readonly int[] StarCounts = { BigStars, MedStars, SmallStars };
    private static readonly int[] FirstOf = { 0, BigStars, BigStars + MedStars };

    private static int Rgb15(int r, int g, int b) => (r << 10) | (g << 5) | b;

    private readonly short[] _x = new short[NumStars], _y = new short[NumStars];
    // Each plane's star with the least y (min_star_index): where its wrap starts.
    private readonly int[] _min = new int[3];
    // The display list's star primitives: their kind, frame, colour and place.
    private readonly PrimType[] _type = new PrimType[NumStars];
    private readonly int[] _frame = new int[NumStars], _color = new int[NumStars];
    private readonly short[] _px = new short[NumStars], _py = new short[NumStars];

    /// <param name="stars">InitGalaxy's stars, each plane's block sorted by y (log_star_array).</param>
    public BattleGalaxy(IReadOnlyList<(short X, short Y)> stars)
    {
        if (stars.Count != NumStars)
            throw new ArgumentException($"{NumStars} stars, not {stars.Count}");
        for (int i = 0; i < NumStars; ++i)
        {
            (_x[i], _y[i]) = stars[i];
            bool big = i < BigStars + MedStars;
            _type[i] = big ? PrimType.Stamp : PrimType.Point;
            _color[i] = big ? Rgb15(0x0B, 0x0B, 0x1F) : Rgb15(0x15, 0x15, 0x15);
        }
    }

    /// <summary>MoveGalaxy: the frame's camera moves the stars and sets how they're drawn.</summary>
    public void Move(BattleCamera cam)
    {
        if (cam.ViewState == 0)
            return;
        int reduction = cam.ZoomOut;
        if (cam.ViewState == 2)
        {
            PrimType[] obj = new PrimType[2];
            int[] frame = { 1, 0 };   // IncFrameIndex (stars_in_space), stars_in_space
            if (cam.Step)
            {   // on PC, the closest stars are images when zoomed out
                obj[0] = PrimType.Stamp;
                if (reduction > 0)
                {
                    obj[1] = PrimType.Point;
                    frame[0] = frame[1];
                }
                else
                    obj[1] = PrimType.Stamp;
            }
            else
            {   // on 3DO, the closest stars are pixels when zoomed out
                obj[1] = PrimType.Point;
                obj[0] = reduction > (1 << BattleCamera.ZoomShift) ? PrimType.Point : PrimType.Stamp;
            }
            for (int plane = 0, p = 0; plane < 2; ++plane)
                for (int i = StarCounts[plane]; i > 0; --i, ++p)
                {
                    _type[p] = obj[plane];
                    _frame[p] = frame[plane];
                }
        }

        WrapStarBlock(2, (short)cam.ScrollX, (short)cam.ScrollY);
        WrapStarBlock(1, (short)cam.ScrollX, (short)cam.ScrollY);
        WrapStarBlock(0, (short)cam.ScrollX, (short)cam.ScrollY);

        short dx = (short)cam.OrgX, dy = (short)cam.OrgY;
        int red = (ushort)(cam.Step ? reduction + BattleCamera.OneShift : reduction << BattleCamera.OneShift);
        int wrapAround = LogSpaceWidth;
        for (int plane = 0, p = 0; plane < 3 && (cam.ViewState == 2 || Moved(p, dx, dy, red, cam.Step));
             ++plane, wrapAround <<= 1, dx = (short)(dx << 1), dy = (short)(dy << 1))
        {
            for (int i = StarCounts[plane]; i > 0; --i, ++p)
            {
                _x[p] = (short)(ushort)(_x[p] < 0 ? _x[p] + wrapAround : _x[p] >= wrapAround ? _x[p] - wrapAround : _x[p]);
                if (cam.Step)
                {
                    _px[p] = (short)((_x[p] - dx) >> red);
                    _py[p] = (short)((_y[p] - dy) >> red);
                }
                else
                {
                    _px[p] = (short)(((_x[p] - dx) << BattleCamera.ZoomShift) / red);
                    _py[p] = (short)(((_y[p] - dy) << BattleCamera.ZoomShift) / red);
                }
            }
            red = (ushort)(cam.Step ? red + 1 : red << 1);
        }
    }

    // CmpMovePoints: whether the plane's first star has moved on the screen.
    private bool Moved(int p, short dx, short dy, int red, bool step) => step
        ? _px[p] != (_x[p] - dx) >> red || _py[p] != (_y[p] - dy) >> red
        : _px[p] != ((_x[p] - dx) << BattleCamera.ZoomShift) / red || _py[p] != ((_y[p] - dy) << BattleCamera.ZoomShift) / red;

    // WrapStarBlock: a plane's stars moved by the scroll, those past the
    // plane's edge (its height times 2^plane) wrapped round, the block kept
    // sorted by y through its least star's index.
    private void WrapStarBlock(int plane, short dx, short dy)
    {
        int first0 = FirstOf[plane], n = StarCounts[plane];
        int i = _min[plane];
        int P(int k) => first0 + k;
        if (dy < 0)
        {
            int first = i;
            dy = (short)-dy;
            short offsY = (short)((LogSpaceHeight << plane) - dy);
            while (_y[P(i)] < dy)
            {
                _y[P(i)] = (short)(_y[P(i)] + offsY);
                _x[P(i)] = (short)(_x[P(i)] + dx);
                if (++i >= n)
                    i = 0;
                if (i == first)
                    return;
            }
            _min[plane] = i;
            if (first <= i)
            {
                for (int k = i; k < n; ++k)
                {
                    _y[P(k)] = (short)(_y[P(k)] - dy);
                    _x[P(k)] = (short)(_x[P(k)] + dx);
                }
                i = 0;
            }
            if (first > i)
                for (int k = i; k < first; ++k)
                {
                    _y[P(k)] = (short)(_y[P(k)] - dy);
                    _x[P(k)] = (short)(_x[P(k)] + dx);
                }
        }
        else
        {
            if (i-- == 0)
                i = n - 1;
            int last = i;
            if (dy > 0)
            {
                short offsY = (short)((LogSpaceHeight << plane) - dy);
                while (_y[P(i)] >= offsY)
                {
                    _y[P(i)] = (short)(_y[P(i)] - offsY);
                    _x[P(i)] = (short)(_x[P(i)] + dx);
                    if (i-- <= 0)
                        i = n - 1;
                    if (i == last)
                        return;
                }
                _min[plane] = i + 1 == n ? 0 : i + 1;
            }
            if (last >= i)
            {
                for (int k = i; k >= 0; --k)
                {
                    _y[P(k)] = (short)(_y[P(k)] + dy);
                    _x[P(k)] = (short)(_x[P(k)] + dx);
                }
                i = n - 1;
            }
            if (last < i)
                for (int k = i; k > last; --k)
                {
                    _y[P(k)] = (short)(_y[P(k)] + dy);
                    _x[P(k)] = (short)(_x[P(k)] + dx);
                }
        }
    }

    /// <summary>The stars' primitives as DrawBatch draws them: the display list's star links run from the last (InitGalaxy inserts each before the one before).</summary>
    public IEnumerable<BattlePrim> Prims => Enumerable.Range(0, NumStars).Reverse().Select(Prim);

    /// <summary>A star's primitive, by its index in the display list (log_star_array's order).</summary>
    public BattlePrim Prim(int i) =>
        new(_type[i], _px[i], _py[i], 0, 0, _type[i] == PrimType.Point ? "" : Stars, _type[i] == PrimType.Point ? -1 : _frame[i], _color[i]);
}

/// <summary>A battle's elements on the screen, as PostProcessQueue places them for DrawBatch.</summary>
public static class BattleSpace
{
    /// <summary>
    /// An element as a primitive: at its display place from <paramref name="cam"/>
    /// (a line's two ends), a stamp's frame from the zoom's size of its sheet
    /// (<paramref name="sizeOf"/>: a big sheet's id at a size; the one-size
    /// sheets their own).
    /// </summary>
    public static BattlePrim Of(BattleElementSnapshot e, BattleCamera cam, Func<string, int, string> sizeOf) => e.Prim switch
    {
        PrimType.Stamp or PrimType.StampFill => new BattlePrim(e.Prim, cam.Display(e.X, cam.OrgX), cam.Display(e.Y, cam.OrgY), 0, 0,
            sizeOf(e.Sheet, cam.Size), e.Frame, e.Color),
        PrimType.Line => new BattlePrim(e.Prim, cam.Display(e.X, cam.OrgX), cam.Display(e.Y, cam.OrgY),
            cam.Display(e.X2, cam.OrgX), cam.Display(e.Y2, cam.OrgY), "", -1, e.Color),
        _ => new BattlePrim(e.Prim, cam.Display(e.X, cam.OrgX), cam.Display(e.Y, cam.OrgY), 0, 0, "", -1, e.Color),
    };

    /// <summary>A big sheet's id at a size (0 big, 1 medium, 2 small), by the content's names (.large, .medium, .small).</summary>
    public static string SizeOf(string sheet, int size) =>
        sheet.EndsWith(".large", StringComparison.Ordinal) ? sheet[..^".large".Length] + (size switch { 0 => ".large", 1 => ".medium", _ => ".small" })
        : sheet;
}
