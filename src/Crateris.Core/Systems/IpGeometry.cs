// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/solarsys.c, planets.h,
// units.h), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>A point in display pixels or system location units.</summary>
public readonly record struct IpPoint(short X, short Y);

/// <summary>
/// The interplanetary screen: its size, the zoom radii, and UQM's
/// conversions between a system location and the screen
/// (locationToDisplay, displayToLocation). At UQM's default 320x240.
/// </summary>
public static class IpGeometry
{
    public const int ScreenWidth = Units.SpaceWidth - 14;    // SIS_SCREEN_WIDTH, 242
    public const int ScreenHeight = Units.SpaceHeight - 13;  // SIS_SCREEN_HEIGHT, 227
    public const int MaxZoomRadius = 128 << 6;               // MAX_ZOOM_RADIUS, 8192
    public const int MinZoomRadius = MaxZoomRadius >> 3;     // MIN_ZOOM_RADIUS, 1024
    public const int DisplayFactor = (ScreenWidth >> 1) - 8; // DISPLAY_FACTOR, 113
    private const int DisplayToLoc = DisplayFactor >> 1;

    public static IpPoint LocationToDisplay(IpPoint pt, int scaleRadius) => new(
        (short)((ScreenWidth >> 1) + (long)pt.X * DisplayToLoc / scaleRadius),
        (short)((ScreenHeight >> 1) + (long)pt.Y * DisplayToLoc / scaleRadius));

    public static IpPoint DisplayToLocation(IpPoint pt, int scaleRadius) => new(
        (short)(((long)pt.X - (ScreenWidth >> 1)) * scaleRadius / DisplayToLoc),
        (short)(((long)pt.Y - (ScreenHeight >> 1)) * scaleRadius / DisplayToLoc));
}
