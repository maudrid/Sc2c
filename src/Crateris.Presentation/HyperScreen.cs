// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0: where the hyperspace view draws
// things (CalcReduction, CalcView, CalcDisplayCoord and PostProcessQueue in
// src/uqm/process.c with the default smooth zoom, AllocHyperElement and
// AddStars in src/uqm/hyper.c), Copyright Paul Reiche, Fred Ford and Toys
// for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Presentation;

/// <summary>
/// Places on UQM's hyperspace screen (the 242 x 227 SIS screen): elements
/// sit in a logical space of 8192 x 7680 around the ship, and the view
/// shows them at a quarter of a pixel per logical unit. The view's corner
/// is fixed (CalcReduction always gives 1x in hyperspace), so the ship is
/// drawn at (128, 120) when the view has caught up with it, a little right
/// of and below the screen's middle.
/// </summary>
public static class HyperScreen
{
    public const int ZoomShift = 8;
    public const int MaxZoomOut = 1 << (ZoomShift + Units.MaxReduction - 1);   // 1024

    /// <summary>CalcReduction's answer in hyperspace: 1x.</summary>
    public const int Reduction = 1 << ZoomShift;

    /// <summary>SpaceOrg (CalcView): the logical point at the screen's top left corner.</summary>
    public const int OriginX = ((Units.LogSpaceWidth >> 1) - Units.LogSpaceWidth * Reduction / (MaxZoomOut << 2)) & ~((1 << Units.OneShift) - 1);
    public const int OriginY = ((Units.LogSpaceHeight >> 1) - Units.LogSpaceHeight * Reduction / (MaxZoomOut << 2)) & ~((1 << Units.OneShift) - 1);

    /// <summary>How near a star must be to be drawn: the stars AddStars puts near a ship (<see cref="StarContact.ReachX"/>).</summary>
    public const int StarReachX = StarContact.ReachX, StarReachY = StarContact.ReachY;

    /// <summary>CalcDisplayCoord with the smooth zoom (C division, towards zero).</summary>
    public static int Display(int c, int origin) => ((c - origin) << ZoomShift) / (Reduction << Units.OneShift);

    /// <summary>WRAP_X and WRAP_Y: into the logical space, as UQM's COUNT.</summary>
    private static int Wrap(long v, int w) => (ushort)(v < 0 ? v + w : v >= w ? v - w : v);

    /// <summary>
    /// A ship drawn on the screen of the ship at (<paramref name="viewerLogX"/>,
    /// <paramref name="viewerLogY"/>): its own place in its view, moved by how
    /// far it is from the viewer. Only meaningful for a ship near the viewer
    /// (<see cref="ShowsShip"/>); the distance isn't wrapped, as hyperspace
    /// doesn't wrap at the map's scale.
    /// </summary>
    public static (int X, int Y) Ship(int logX, int logY, int viewX, int viewY, int viewerLogX, int viewerLogY) =>
        (Display(checked((int)((long)logX - viewerLogX + viewX)), OriginX),
         Display(checked((int)((long)logY - viewerLogY + viewY)), OriginY));

    /// <summary>
    /// A sprite at a place in hyperspace's logical units (where its ship's
    /// view puts it: <see cref="StarContact.SpritePosition"/>), drawn on the
    /// screen of the ship at (<paramref name="viewerLogX"/>, <paramref name="viewerLogY"/>).
    /// </summary>
    public static (int X, int Y) Sprite(long x, long y, int viewerLogX, int viewerLogY) =>
        (Display(checked((int)(x - viewerLogX + HyperFlight.CenterX)), OriginX),
         Display(checked((int)(y - viewerLogY + HyperFlight.CenterY)), OriginY));

    /// <summary>
    /// Whether another ship is near enough to the viewer to be drawn: within
    /// the same reach as the stars (<see cref="Shows"/>), which covers the
    /// whole screen.
    /// </summary>
    public static bool ShowsShip(int universeX, int universeY, int viewerUniverseX, int viewerUniverseY) =>
        Math.Abs(universeX - viewerUniverseX) <= StarReachX && Math.Abs(universeY - viewerUniverseY) <= StarReachY;

    /// <summary>AllocHyperElement's place for a star, drawn on the screen of the ship at (<paramref name="logX"/>, <paramref name="logY"/>).</summary>
    public static (int X, int Y) Star(Star star, int logX, int logY) =>
        (Display(Wrap(Units.UniverseToLogX(star.X) + (Units.LogSpaceWidth >> 1) - (long)logX, Units.LogSpaceWidth), OriginX),
         Display(Wrap(Units.UniverseToLogY(star.Y) + (Units.LogSpaceHeight >> 1) - (long)logY, Units.LogSpaceHeight), OriginY));

    /// <summary>Whether AddStars puts a star on the screen of a ship at a universe position.</summary>
    public static bool Shows(Star star, int universeX, int universeY) => StarContact.InReach(star, universeX, universeY);
}
