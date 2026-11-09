// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/outfit.c's DoOutfit,
// DoInstallModule and ChangeFuelQuantity; shipyard.c's DoShipyard and
// DoModifyShips; the drawing each does, and where), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors,
// under the GNU GPL 2 or later.

namespace Crateris.Core.Starbase;

/// <summary>Which of UQM's drawing routines a <see cref="ScreenOp"/> is.</summary>
public enum ScreenOpKind : byte
{
    /// <summary>DoOutfit's first drawing; <see cref="ScreenOp.A"/> the fuel aboard as it was drawn (before DoOutfit rounds it).</summary>
    OutfitOpened,

    /// <summary>DoShipyard's first drawing (the menu on Crew, the power lines in colortable.hangar's first map).</summary>
    YardOpened,

    /// <summary>DrawMenuStateStrings: <see cref="ScreenOp.A"/> the menu (PM_FUEL, PM_CREW), <see cref="ScreenOp.B"/> its entry.</summary>
    MenuStrings,

    /// <summary>ClearSISRect (CLEAR_SIS_RADAR): the menu's area cleared, as Modules opens.</summary>
    ClearRadar,

    /// <summary>ClearSISRect (DRAW_SIS_DISPLAY): the status area drawn afresh, as Modules is left.</summary>
    SisDisplay,

    /// <summary>DrawModuleStrings: <see cref="ScreenOp.A"/> the module shown (an empty mark: nothing).</summary>
    ModuleStrings,

    /// <summary>DrawShipPiece: <see cref="ScreenOp.A"/> the piece put in slot <see cref="ScreenOp.B"/> (its row by the piece).</summary>
    ShipPiece,

    /// <summary>RedistributeFuel: the fuel's bars in the tanks.</summary>
    RedistributeFuel,

    /// <summary>DisplayLanders: the landers aboard.</summary>
    DisplayLanders,

    /// <summary>DrawFlagshipStats.</summary>
    FlagshipStats,

    /// <summary>
    /// DeltaSISGauges: <see cref="ScreenOp.A"/> the crew's change,
    /// <see cref="ScreenOp.B"/> the fuel's (in hundredths) and
    /// <see cref="ScreenOp.C"/> the resource units', each
    /// <see cref="ScreenOp.Undefined"/> to draw it afresh.
    /// </summary>
    Gauges,

    /// <summary>
    /// A crew member's point in the blueprint: drawn (<see cref="ScreenOp.A"/>
    /// 1, the one just hired, DMS_HireFlagShipCrew) or erased (0, where
    /// the one just sent away was, DMS_DismissFlagShipCrew).
    /// </summary>
    CrewPoint,

    /// <summary>DrawFlagshipName (FALSE): the ship's name over the space window (renamed).</summary>
    FlagshipName,
}

/// <summary>
/// A piece of the drawing the starbase's outfitting and shipyard screens
/// do, at the point in the keys' handling where UQM's code does it: the
/// menus (<see cref="OutfitMenu"/>, <see cref="ShipyardMenu"/>) list them,
/// and the screen draws each, in order, with the ship as it stands after
/// the key, over what it drew before, as UQM draws a change over its
/// screen (docs/plans/stage-3.md, item 11).
/// </summary>
public readonly record struct ScreenOp(ScreenOpKind Kind, int A = 0, int B = 0, int C = 0)
{
    /// <summary>UNDEFINED_DELTA: a gauge drawn afresh.</summary>
    public const int Undefined = 0x7FFF;

    /// <summary>menustat.h's PM_FUEL (outfitting's menu: Fuel, Modules, Save/Load, Exit) and PM_CREW (the shipyard's).</summary>
    public const int PmFuel = 21, PmCrew = 25;
}
