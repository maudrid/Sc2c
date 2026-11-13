// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/process.c:
// CALC_ZOOM_STUFF and CalcDisplayCoord), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under the
// GNU GPL 2 or later.

namespace Crateris.Core.Combat;

/// <summary>
/// The camera a battle is drawn from in one frame: UQM's view state (0
/// stable, 1 scrolled, 2 zoomed), its scroll (CalcView's dx and dy, which
/// moved every element and moves the stars), its zoom (zoom_out) and the
/// view's corner (SpaceOrg), and whether the zoom is UQM's step zoom
/// rather than its smooth one; the battle's frames so far and every
/// frame's scroll added up (<see cref="Battle.Frames"/>,
/// <see cref="Battle.ScrolledX"/>), so a screen sees a frame it missed and
/// places the stars afresh.
/// </summary>
public readonly record struct BattleCamera(int ViewState, int ScrollX, int ScrollY, int ZoomOut, int OrgX, int OrgY, bool Step,
    int Frame = 0, int ScrolledX = 0, int ScrolledY = 0)
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

