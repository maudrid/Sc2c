// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/units.h), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Uqm;

/// <summary>
/// UQM's unit systems. Universe units (0..9999) are what the star map uses
/// (shown divided by 10). Logical units are the flagship's hyperspace
/// position (GLOBAL_SIS log_x/log_y): finer, with y increasing downward.
/// World units are combat and view units (4 per display pixel).
/// Values that depend on the screen use UQM's default 320x240.
/// </summary>
public static class Units
{
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 240;
    public const int StatusWidth = 64;
    public const int SpaceWidth = ScreenWidth - StatusWidth;
    public const int SpaceHeight = ScreenHeight;
    public const int OneShift = 2;
    public const int MaxReduction = 3;

    public const int MaxXUniverse = 9999;
    public const int MaxYUniverse = 9999;

    public const int LogSpaceWidth = (SpaceWidth << OneShift) << MaxReduction;   // 8192
    public const int LogSpaceHeight = (SpaceHeight << OneShift) << MaxReduction; // 7680

    public const int UniverseUnitsX = ((MaxXUniverse + 1) >> 4) * 10;            // 6250
    public const int UniverseUnitsY = (MaxYUniverse + 1) >> 4;                   // 625
    public const int LogUnitsX = (LogSpaceWidth >> 4) * 195;                      // 99840
    public const int LogUnitsY = (LogSpaceHeight >> 4) * 25;                      // 12000

    /// <summary>Per-frame scroll limit in hyperspace (ORG_JUMP_X/Y, process.c).</summary>
    public const int OrgJumpX = (LogSpaceWidth / 75) & ~((1 << OneShift) - 1);
    public const int OrgJumpY = (LogSpaceHeight / 75) & ~((1 << OneShift) - 1);

    public static int DisplayToWorld(int x) => x << OneShift;

    public static int WorldToDisplay(int x) => x >> OneShift;

    // 32-bit int arithmetic, as in UQM (SDWORD), including its wrap-around.
    public static short LogXToUniverse(int lx) =>
        unchecked((short)((lx * UniverseUnitsX + (LogUnitsX >> 1)) / LogUnitsX));

    public static short LogYToUniverse(int ly) =>
        unchecked((short)(MaxYUniverse - (ly * UniverseUnitsY + (LogUnitsY >> 1)) / LogUnitsY));

    public static int UniverseToLogX(short ux) =>
        unchecked((ux * LogUnitsX + (UniverseUnitsX >> 1)) / UniverseUnitsX);

    public static int UniverseToLogY(short uy) =>
        unchecked(((MaxYUniverse - uy) * LogUnitsY + (UniverseUnitsY >> 1)) / UniverseUnitsY);

    /// <summary>MAX_X_LOGICAL: largest log_x that still reads as below 1000.0.</summary>
    public static readonly int MaxXLogical =
        UniverseToLogX(MaxXUniverse + 1) - (UniverseToLogX(1) >> 1) - 1;

    /// <summary>MAX_Y_LOGICAL (the y axis is inverted).</summary>
    public static readonly int MaxYLogical =
        UniverseToLogY(-1) - (UniverseToLogY(MaxYUniverse - 1) >> 1) - 1;

    public static int WrapDeltaX(int dx) => dx < 0
        ? (-dx <= LogSpaceWidth >> 1 ? dx : LogSpaceWidth + dx)
        : (dx <= LogSpaceWidth >> 1 ? dx : dx - LogSpaceWidth);

    public static int WrapDeltaY(int dy) => dy < 0
        ? (-dy <= LogSpaceHeight >> 1 ? dy : LogSpaceHeight + dy)
        : (dy <= LogSpaceHeight >> 1 ? dy : dy - LogSpaceHeight);
}
