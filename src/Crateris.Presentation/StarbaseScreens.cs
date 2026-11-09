// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c:
// DrawBaseStateStrings, rotateStarbase, DrawShipPiece; outfit.c:
// DrawModuleStrings, RedistributeFuel, DisplayLanders, DoOutfit's and
// DoInstallModule's drawing and flash rectangles; shipyard.c: DoShipyard's
// drawing, DrawBluePrint, animatePowerLines, DoModifyShips' flash
// rectangles; border.c: DrawSISFrame; sis.c: ClearSISRect, SetFlashRect,
// GetCPodCapacity, GetSBayCapacity, GetGaugeRect,
// DrawSISTitle, DrawSISMessage, DrawStatusMessage, DrawCaptainsName,
// DrawFlagshipName, DrawFlagshipStats, DrawLanders, DrawStorageBays,
// DrawPC_SIS, DrawThrusters, DrawTurningJets, DrawModules,
// DeltaSISGauges, GetFTankCapacity; menu.c: DrawMenuStateStrings,
// DrawPCMenu, DrawPCMenuFrame; colors.h), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under
// the GNU GPL 2 or later.

using System.Globalization;
using Crateris.Core.Content;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>
/// The flagship and the prices as the starbase's screens draw them
/// (GLOBAL_SIS and GLOBAL): the slots (sis.h's numbers), the crew, the
/// fuel in hundredths, the landers, the resource units, the minerals'
/// mass, the names, CHMMR_BOMB_STATE and LANDER_SHIELDS, and the prices
/// (a module's cost before MODULE_COST_SCALE).
/// </summary>
public sealed record SisState(byte[] Modules, byte[] Drives, byte[] Jets, int Crew, uint Fuel, int Landers, uint ResUnits,
    int MineralMass, string ShipName, string Captain, int BombState, int LanderShields, int FuelCost, int CrewCost,
    Func<int, int> ModuleCost, int ModuleCostScale = 50);

/// <summary>
/// The allied starbase's screens as UQM's PC version draws them: each
/// change drawn over what was there, as UQM draws it, so the picture is
/// UQM's, leftovers and all (a sold module's picture showing round the
/// empty slot's, the starbase's frames each over the last, the fuel as it
/// stood until the gauge is next drawn). The menu (the starbase turning
/// on the clock, the entries), outfitting and the shipyard: their menus
/// list what to draw (<see cref="ScreenOp"/>, at UQM's points in their
/// keys), drawn by <see cref="Apply(ScreenOp)"/> with the ship as it
/// stands (<see cref="Ship"/>). What is drawn piles up in
/// <see cref="Drawn"/>, to be painted over the last picture
/// (<see cref="ScreenRaster"/>). The game's strings come from
/// <see cref="GameString"/>.
/// </summary>
public sealed class StarbaseScreens
{
    public const string StarbaseSheet = "graphics.starbase", ModulesMenuSheet = "graphics.modulesmenu",
        OutfitSheet = "graphics.outfit", ModuleSheet = "graphics.outfitmodules", FlagStatSheet = "graphics.flagshipstatus",
        PlayMenuSheet = "graphics.playmenu", FontGradientSheet = "graphics.fontgradient", ShipyardSheet = "graphics.shipyard",
        BlueprintSheet = "graphics.blueprints";

    /// <summary>The sheets these screens stamp (to load their pictures).</summary>
    public static readonly string[] Sheets =
        { StarbaseSheet, ModulesMenuSheet, OutfitSheet, ModuleSheet, FlagStatSheet, PlayMenuSheet, ShipyardSheet, BlueprintSheet };

    /// <summary>The colour table the shipyard's power lines cycle through (HANGAR_COLOR_TAB).</summary>
    public const string HangarColorTable = "colortable.hangar";

    private const int Modules = ShipModules.Modules, EmptySlot = ShipModules.EmptySlot;
    private const int NumModuleSlots = ShipModules.ModuleSlotCount, NumDriveSlots = ShipModules.DriveSlotCount,
        NumJetSlots = ShipModules.JetSlotCount;
    private const int FuelTankScale = 100, FuelTankCapacity = 50 * FuelTankScale, HeFuelTankCapacity = 100 * FuelTankScale;
    private const int MaxFuelBars = 10, FuelVolumePerRow = HeFuelTankCapacity / MaxFuelBars, FuelReserve = FuelVolumePerRow;
    private const int MaxLanders = 10, StorageBayCapacity = 500;
    private const int ShipPieceOffset = 12;
    private const int DriveSideX = 31, DriveSideY = 56, DriveTopX = 33, DriveTopY = 65 + 21;
    private const int JetSideX = 71, JetSideY = 48, JetTopX = 70, JetTopY = 73 + 21;
    private const int ModuleSideX = 17, ModuleSideY = 14, ModuleTopX = 17, ModuleTopY = 96 + 21;
    private const int LanderX = 24, LanderY = 67, LanderWidth = 15;
    private const int SisTitleBoxWidth = 57, SisTitleWidth = SisTitleBoxWidth - 2, SisTitleHeight = 8, SisSpacerBoxWidth = 12;
    private const int SisMessageBoxWidth = UqmCanvas.SisScreenWidth - SisTitleBoxWidth - SisSpacerBoxWidth;
    private const int SisMessageWidth = SisMessageBoxWidth - 2, SisMessageHeight = SisTitleHeight;
    private const int StatusMessageWidth = UqmCanvas.StatusWidth - 4, StatusMessageHeight = 7;
    private const int ShipNameWidth = UqmCanvas.StatusWidth - 4, ShipNameHeight = 7;
    private const int PcMenuHeight = 8;

    /// <summary>menustat.h's PM_FUEL, PM_EXIT_OUTFIT, PM_CREW and PM_EXIT_SHIPYARD.</summary>
    public const int PmFuel = ScreenOp.PmFuel, PmExitOutfit = 24, PmCrew = ScreenOp.PmCrew, PmExitShipyard = 27;

    private static Rgb C(int r, int g, int b) => Rgb.From15((byte)r, (byte)g, (byte)b);

    private static readonly Rgb Black = C(0, 0, 0), MenuArea = C(0x0A, 0x0A, 0x0A);
    private static readonly Rgb SisMessageText = C(0x1B, 0x00, 0x1B), SisMessageBack = C(0x00, 0x00, 0x14);
    private static readonly Rgb StatusMessageText = C(0x00, 0x10, 0x00), StatusMessageBack = C(0x00, 0x08, 0x00);
    private static readonly Rgb CaptainNameText = C(0x16, 0x0B, 0x1F), CaptainNameBack = C(0x00, 0x00, 0x14);
    private static readonly Rgb PcCaptainText = C(0x02, 0x04, 0x1E), PcCaptainBack = C(0x00, 0x00, 0x14);
    private static readonly Rgb StorageFull = C(0x0A, 0x0A, 0x0A), StorageEmpty = C(0x06, 0x06, 0x06);
    private static readonly Rgb MenuBack = C(0x00, 0x00, 0x15), MenuTopLeft = C(0x0F, 0x0F, 0x0F), MenuBottomRight = C(0x06, 0x06, 0x06),
        MenuText = C(0x00, 0x15, 0x15), MenuSelText = C(0x0A, 0x1F, 0x1F), MenuSelBack = C(0x0A, 0x0A, 0x1F);
    private static readonly Rgb[] FuelColors =
    {
        C(0x0F, 0x00, 0x00), C(0x13, 0x00, 0x00), C(0x17, 0x00, 0x00), C(0x1B, 0x00, 0x00), C(0x1F, 0x03, 0x00),
        C(0x1F, 0x07, 0x00), C(0x1F, 0x0A, 0x00), C(0x1F, 0x0E, 0x00), C(0x1F, 0x11, 0x00), C(0x1F, 0x15, 0x00),
        C(0x1F, 0x18, 0x00),
    };

    private readonly UqmCanvas _c;
    private SisState _s = null!;

    /// <summary>The flagship and the prices as they stand, which the drawing reads (GLOBAL_SIS and GLOBAL).</summary>
    public SisState Ship
    {
        get => _s;
        set => _s = value;
    }

    public StarbaseScreens(UqmCanvas canvas, Func<int, string> gameString)
    {
        _c = canvas;
        GameString = gameString;
    }

    public Func<int, string> GameString { get; }

    public List<UqmDraw> Drawn => _c.Drawn;

    // ------------------------------------------------------------ the menu

    /// <summary>
    /// DoStarBase's first drawing, on <paramref name="state"/>
    /// (TALK_COMMANDER to DEPART_BASE): the screen cleared, the starbase's
    /// frame 0 (rotateStarbase), the entries.
    /// </summary>
    public void OpenMenu(int state)
    {
        _c.SetContext(_c.Screen);
        _c.SetBackground(Black);
        _c.Clear();
        Turn(0);
        BaseStateStrings(-1, state);
    }

    /// <summary>DoStarBase's cursor moved: the old entry drawn dark, the new lit.</summary>
    public void MoveMenu(int oldState, int newState)
    {
        _c.SetContext(_c.Screen);
        BaseStateStrings(oldState, newState);
    }

    /// <summary>rotateStarbase: the starbase's <paramref name="frame"/> over the last (<see cref="StarbaseTurning"/>).</summary>
    public void Turn(int frame)
    {
        _c.SetContext(_c.Screen);
        _c.Stamp(StarbaseSheet, frame, UqmCanvas.SafeX, UqmCanvas.SafeY + 4);
    }

    private void BaseStateStrings(int oldState, int newState)
    {
        _c.SetContext(_c.Screen);
        _c.SetFont("starcon");
        _c.SetForeground(Black);
        int x = 73 - 4 + UqmCanvas.SafeX, y;
        if (oldState == -1)
        {
            y = 106 + 28 + (UqmCanvas.SafeY + 4);
            for (oldState = 0; oldState < 3; ++oldState)
            {
                if (oldState != newState)
                    _c.Text(x, y, TextAlign.Center, GameString(GameStrings.StarbaseBase + 1 + oldState));
                y += 23 - 4;
            }
        }
        y = 106 + 28 + (UqmCanvas.SafeY + 4) + (23 - 4) * oldState;
        _c.Text(x, y, TextAlign.Center, GameString(GameStrings.StarbaseBase + 1 + oldState));
        _c.SetForeground(C(0x1F, 0x1F, 0x0A));
        y = 106 + 28 + (UqmCanvas.SafeY + 4) + (23 - 4) * newState;
        _c.Text(x, y, TextAlign.Center, GameString(GameStrings.StarbaseBase + 1 + newState));
    }

    // ------------------------------------------------------------ outfitting

    /// <summary>What a menu's <see cref="ScreenOp"/> draws, with <see cref="Ship"/> as it stands.</summary>
    public void Apply(ScreenOp op)
    {
        switch (op.Kind)
        {
            case ScreenOpKind.OutfitOpened:
                OpenOutfit((uint)op.A);
                break;
            case ScreenOpKind.YardOpened:
                OpenShipyard();
                break;
            case ScreenOpKind.MenuStrings:
                MenuStateStrings(op.A, op.B);
                break;
            case ScreenOpKind.ClearRadar:
                _c.SetContext(_c.Space);
                ClearSisRect(clearRadar: true);
                break;
            case ScreenOpKind.SisDisplay:
                ClearSisRect(clearRadar: false);
                break;
            case ScreenOpKind.ModuleStrings:
                ModuleStrings(op.A);
                break;
            case ScreenOpKind.ShipPiece:
                _c.SetContext(_c.Space);
                ShipPiece(ModuleSheet, op.A, op.B, false);
                break;
            case ScreenOpKind.RedistributeFuel:
                RedistributeFuel();
                break;
            case ScreenOpKind.DisplayLanders:
                _c.SetContext(_c.Space);
                DisplayLanders();
                break;
            case ScreenOpKind.FlagshipStats:
                FlagshipStats();
                break;
            case ScreenOpKind.Gauges:
                Gauges(op.A, op.B, op.C);
                break;
            case ScreenOpKind.CrewPoint:
            {
                // GetCPodCapacity's point for the crew member: the one just hired, or where the one sent away was.
                _c.SetContext(_c.Space);
                int x = 0, y = 0;
                CPodPoint(op.A == 1 ? _s.Crew - 1 : _s.Crew, ref x, ref y);
                if (op.A != 1)
                    _c.SetForeground(Black);
                _c.Point(x, y);
                break;
            }
            case ScreenOpKind.FlagshipName:
                FlagshipName(false);
                break;
        }
    }

    public void Apply(IEnumerable<ScreenOp> ops)
    {
        foreach (var op in ops)
            Apply(op);
    }

    // DoOutfit's first drawing, with the fuel as it stood before DoOutfit rounded it.
    private void OpenOutfit(uint fuel)
    {
        var ship = _s;
        _s = ship with { Fuel = fuel };
        SisFrame();
        SisMessage(GameString(GameStrings.StarbaseBase + 2));
        SisTitle(GameString(GameStrings.StarbaseBase));
        _c.SetContext(_c.Space);
        _c.Stamp(OutfitSheet, 0, 0, 0);
        for (int i = 0; i < NumDriveSlots; ++i)
            if (_s.Drives[i] < EmptySlot)
                ShipPiece(ModuleSheet, _s.Drives[i], i, false);
        for (int i = 0; i < NumJetSlots; ++i)
            if (_s.Jets[i] < EmptySlot)
                ShipPiece(ModuleSheet, _s.Jets[i], i, false);
        for (int i = 0; i < NumModuleSlots; ++i)
            if (_s.Modules[i] < EmptySlot)
                ShipPiece(ModuleSheet, _s.Modules[i], i, false);
        RedistributeFuel();
        DisplayLanders();
        if (_s.BombState < 3)
        {
            // The shields' frames in the order earthquake, biological, lightning, lava, each with its disaster's
            // bit (planets.h: BIOLOGICAL_DISASTER is 0, EARTHQUAKE_DISASTER 1).
            int f = _c.FrameCount(ModuleSheet) - 5;
            foreach (int disaster in new[] { 1, 0, 2, 3 })
            {
                if ((_s.LanderShields & (1 << disaster)) != 0)
                    _c.Stamp(ModuleSheet, f, 0, 0);
                ++f;
            }
        }
        MenuStateStrings(PmFuel, OutfitMenu.Fuel);
        FlagshipName(false);
        FlagshipStats();
        _s = ship;
    }

    /// <summary>
    /// Where the flashing highlight is (sis.c's SetFlashRect), in the
    /// screen's pixels, with outfitting's menus where <paramref name="state"/>,
    /// <paramref name="item"/> and <paramref name="installing"/> leave them
    /// (InitFlash's rectangles); null for none. The PC menus don't flash
    /// (SFR_MENU_3DO), except where UQM flashes the menu's area with them
    /// too (SFR_MENU_ANY: the radar while a module is chosen).
    /// </summary>
    public static URect? OutfitFlash(int state, int item, bool installing)
    {
        if (!installing)
            return state == OutfitMenu.DoFuel ? StatusToScreen(GaugeRect(crew: false)) : null;   // else SFR_MENU_3DO
        if (state < EmptySlot)
            return new URect(UqmCanvas.RadarX, UqmCanvas.RadarY, UqmCanvas.RadarWidth, UqmCanvas.RadarHeight);   // SFR_MENU_ANY
        URect r;
        int w = ShipPieceOffset;
        switch (state)
        {
            case EmptySlot + 3:
                r = new URect(LanderX - 1, LanderY - 1, 11 + 2, 13 + 2);
                w = LanderWidth;
                break;
            case EmptySlot + 0:
                r = new URect(DriveTopX - 1, DriveTopY - 1, 8, 6);
                break;
            case EmptySlot + 1:
                r = new URect(JetTopX - 1, JetTopY - 1, 9, 10);
                break;
            default:
                r = new URect(ModuleTopX - 1, ModuleTopY - 1, ShipPieceOffset + 2, 34);
                break;
        }
        return new URect(UqmCanvas.SisOrgX + r.X + w * item, UqmCanvas.SisOrgY + r.Y, r.W, r.H);
    }

    private static URect StatusToScreen(URect r) =>
        new(UqmCanvas.SpaceWidth + UqmCanvas.SafeX + r.X, UqmCanvas.SafeY + r.Y, r.W, r.H);

    // ------------------------------------------------------------ the shipyard

    /// <summary>shipyard.c's hangar_x_coords, HANGAR_Y and HANGAR_DY (the PC hangar), SHIP_WIN_WIDTH and SHIP_WIN_HEIGHT.</summary>
    private static readonly int[] HangarX = { 0, 38, 76, 131, 169, 207 };
    private const int HangarY = 88, HangarDy = 84, ShipWinWidth = 34, ShipWinHeight = ShipWinWidth + 6;
    private const int CrewPodCapacity = 50, CrewPerRow = 5, SbayMassPerRow = 50;

    private static readonly Rgb[] CrewColors =
    {
        C(0x0A, 0x1E, 0x09), C(0x00, 0x1E, 0x00), C(0x00, 0x1B, 0x00), C(0x00, 0x18, 0x00), C(0x00, 0x15, 0x00),
        C(0x00, 0x12, 0x00), C(0x00, 0x10, 0x00), C(0x00, 0x0D, 0x00), C(0x00, 0x0A, 0x00), C(0x00, 0x07, 0x00),
    };

    private static readonly Rgb[] StorageColors =
    {
        C(0x1F, 0x1F, 0x1F), C(0x1C, 0x1C, 0x1C), C(0x18, 0x18, 0x18), C(0x15, 0x15, 0x15), C(0x12, 0x12, 0x12),
        C(0x10, 0x10, 0x10), C(0x0D, 0x0D, 0x0D), C(0x0A, 0x0A, 0x0A), C(0x08, 0x08, 0x08), C(0x05, 0x05, 0x05),
    };

    /// <summary>The frame of graphics.shipyard the power lines are (animatePowerLines), drawn in colortable.hangar's maps.</summary>
    public const int PowerLinesFrame = 24;

    /// <summary>
    /// The colour map of <see cref="HangarColorTable"/> the last screen drew
    /// the power lines in (the shipyard), or -1.
    /// </summary>
    public int ColorMap { get; private set; } = -1;

    /// <summary>
    /// Where the flashing highlight is in the shipyard, in the screen's
    /// pixels, with DoModifyShips' <paramref name="mode"/> and
    /// <paramref name="cursor"/>: the flagship, a hangar slot, the crew
    /// gauge; on the menu, none (SFR_MENU_3DO).
    /// </summary>
    public static URect? YardFlash(YardMode mode, byte cursor) => mode switch
    {
        YardMode.Ships when ShipyardMenu.OnFlagship(cursor) =>
            new URect(UqmCanvas.SisOrgX, UqmCanvas.SisOrgY, UqmCanvas.SisScreenWidth, 61),   // DMS_FlashFlagShip
        YardMode.Ships => new URect(UqmCanvas.SisOrgX + HangarX[cursor % ShipyardMenu.HangarShipsRow],
            UqmCanvas.SisOrgY + HangarY + HangarDy * (cursor / ShipyardMenu.HangarShipsRow), ShipWinWidth, ShipWinHeight),
        YardMode.Crew => StatusToScreen(GaugeRect(crew: true)),   // DMS_FlashFlagShipCrewCount
        _ => null,
    };

    // DoShipyard's first drawing: the SIS frame and status area, the blueprint (DrawBluePrint: the pieces, the crew
    // pods lit, the crew, the minerals and the fuel), the menu on Crew, the hangar and its power lines in
    // colortable.hangar's first map. The flagship alone: fleet ships come with stage 4.
    private void OpenShipyard()
    {
        SisFrame();
        SisMessage(GameString(GameStrings.StarbaseBase + 3));
        SisTitle(GameString(GameStrings.StarbaseBase));
        _c.SetContext(_c.Space);
        BluePrint();
        MenuStateStrings(PmCrew, ShipyardMenu.Crew);
        _c.SetContext(_c.Space);
        // The PC ship dock overwrites the border: the clip rectangle a pixel wider each side, and one lower.
        var old = _c.ClipRect;
        _c.SetClipRect(new URect(old.X - 1, old.Y, old.W + 2, old.H + 1));
        _c.Stamp(ShipyardSheet, 0, 0, 0);
        _c.SetClipRect(old);
        PowerLines(0);
        _c.SetFont("tiny");
    }

    /// <summary>animatePowerLines: the power lines over the hangar in colortable.hangar's <paramref name="map"/> (<see cref="Presentation.PowerLines"/>).</summary>
    public void PowerLines(int map)
    {
        _c.SetContext(_c.Space);
        ColorMap = map;
        _c.Stamp(ShipyardSheet, PowerLinesFrame, 0, 0);
    }

    // shipyard.c's DrawBluePrint, with DoStarBase's menu on SHIPYARD (the crew pods lit).
    private void BluePrint()
    {
        _c.SetForeground(C(0x00, 0x00, 0x16));
        _c.FilledStamp(BlueprintSheet, _c.FrameCount(BlueprintSheet) - 1, 0, 0);   // DecFrameIndex: round to the last
        for (int i = 0; i < NumDriveSlots; ++i)
            ShipPiece(BlueprintSheet, _s.Drives[i], i, true);
        for (int i = 0; i < NumJetSlots; ++i)
            ShipPiece(BlueprintSheet, _s.Jets[i], i, true);
        for (int i = 0; i < NumModuleSlots; ++i)
            if (_s.Modules[i] != ShipModules.CrewPod)
                ShipPiece(BlueprintSheet, _s.Modules[i], i, true);
        _c.SetForeground(C(0x0A, 0x0A, 0x1F));
        for (int i = 0; i < NumModuleSlots; ++i)
            if (_s.Modules[i] == ShipModules.CrewPod)
                ShipPiece(BlueprintSheet, _s.Modules[i], i, true);
        // The crew, a point each (a point that doesn't fit stays where the last one was, as UQM's does).
        int px = 0, py = 0;
        for (int n = 0; n < _s.Crew; ++n)
        {
            CPodPoint(n, ref px, ref py);
            _c.Point(px, py);
        }
        // The minerals, a row of a bay for each SBAY_MASS_PER_ROW.
        var r = new URect(0, 0, 9, 1);
        for (int left = _s.MineralMass, mass = 0; left > 0;)
        {
            int m = Math.Min(left, SbayMassPerRow);
            mass += m;
            SBayPoint(mass, ref r.X, ref r.Y);
            _c.FillRect(r);
            left -= m;
        }
        // The fuel past the reserve, a bar a row of a tank.
        if (_s.Fuel > FuelReserve)
        {
            long volume = _s.Fuel - FuelReserve;
            int fuel = FuelReserve;
            var f = new URect(px, py, 3, 1);
            while (volume > 0)
            {
                FTankCapacity(fuel, ref f.X, ref f.Y);
                _c.Point(f.X, f.Y);
                f.X += f.W + 1;
                _c.Point(f.X, f.Y);
                f.X -= f.W;
                _c.SetForeground(_c.SetBackground(Black));
                _c.FillRect(f);
                int m = (int)Math.Min(volume, FuelVolumePerRow);
                fuel += m;
                volume -= m;
            }
        }
    }

    // sis.c's GetCPodCapacity for crew member `crew` (from 0): the point, and the colour set.
    private void CPodPoint(int crew, ref int x, ref int y)
    {
        for (int slot = NumModuleSlots - 1, capacity = 0; slot >= 0; --slot)
        {
            int c = _s.Modules[slot] == ShipModules.CrewPod ? CrewPodCapacity : 0;
            if (crew < capacity + c)
            {
                int seat = crew - capacity, row = seat / CrewPerRow, col = seat % CrewPerRow;
                _c.SetForeground(CrewColors[row]);
                x = 27 + slot * ShipPieceOffset - col * 2;
                y = 34 - row * 2;
                return;
            }
            capacity += c;
        }
    }

    // sis.c's GetSBayCapacity for the mass aboard `mass`: the point, and the colour set.
    private void SBayPoint(int mass, ref int x, ref int y)
    {
        for (int slot = NumModuleSlots - 1, capacity = 0; slot >= 0; --slot)
        {
            int c = _s.Modules[slot] == ShipModules.StorageBay ? StorageBayCapacity : 0;
            if (mass <= capacity + c)
            {
                int cell = mass - capacity, row = cell / SbayMassPerRow;
                if (row == 0)
                    _c.SetForeground(Black);
                else
                    _c.SetForeground(StorageColors[--row]);
                x = 19 + slot * ShipPieceOffset;
                y = 34 - row * 2;
                return;
            }
            capacity += c;
        }
    }

    // starbase.c's DrawShipPiece, from `sheet` (outfitting's modules, or the shipyard's blueprints).
    private void ShipPiece(string sheet, int piece, int slot, bool bluePrint)
    {
        int repair = 0;
        int sideX, sideY, topX, topY;
        switch (piece)
        {
            case ShipModules.FusionThruster or EmptySlot + 0:
                (sideX, sideY, topX, topY) = (DriveSideX, DriveSideY, DriveTopX, DriveTopY);
                break;
            case ShipModules.TurningJets or EmptySlot + 1:
                (sideX, sideY, topX, topY) = (JetSideX, JetSideY, JetTopX, JetTopY);
                break;
            default:
                if (piece < EmptySlot + 2)
                {
                    repair = 1;
                    if (piece < EmptySlot && (slot == 0 || _s.Modules[slot - 1] < EmptySlot))
                        ++repair;
                }
                else if (!bluePrint)
                {
                    if (slot == 0 || slot >= NumModuleSlots - 3)
                        ++piece;
                    if (slot < NumModuleSlots - 1 && _s.Modules[slot + 1] < EmptySlot)
                    {
                        repair = -1;
                        if (piece == EmptySlot + 3 || slot + 1 == NumModuleSlots - 3)
                            --repair;
                    }
                }
                (sideX, sideY, topX, topY) = (ModuleSideX, ModuleSideY, ModuleTopX, ModuleTopY);
                break;
        }
        sideX += slot * ShipPieceOffset;
        int sideFrame = -1;
        Rgb oldColor = default;
        if (repair < 0)
        {
            sideFrame = _c.Frame(sheet, (Modules - 1) + (6 - 2) + (Modules + 6) - (repair + 1));
            _c.Stamp(sheet, sideFrame, sideX, sideY);
        }
        else if (repair != 0)
        {
            oldColor = _c.SetForeground(Black);
            var r = new URect(sideX, sideY, ShipPieceOffset, 1);
            _c.FillRect(r);
            r.Y += 23 - 1;
            _c.FillRect(r);
            r.W = 1;
            r.H = 8;
            if (repair == 2)
            {
                r.X = sideX;
                r.Y = sideY;
                _c.FillRect(r);
                r.Y += 15;
                _c.FillRect(r);
            }
            if (slot < NumModuleSlots - 1)
            {
                r.X = sideX + ShipPieceOffset;
                r.Y = sideY;
                _c.FillRect(r);
                r.Y += 15;
                _c.FillRect(r);
            }
        }
        if (bluePrint)
        {
            if (repair != 0)
                _c.SetForeground(oldColor);
            _c.FilledStamp(sheet, piece - 1, sideX, sideY);
            return;
        }
        topX += slot * ShipPieceOffset;
        if (repair < 0)
            _c.Stamp(sheet, RelFrame(sheet, sideFrame, -((Modules - 1) + 6)), topX, topY);
        else if (repair != 0)
        {
            var r = new URect(topX, topY, ShipPieceOffset, 1);
            _c.FillRect(r);
            r.Y += 32 - 1;
            _c.FillRect(r);
            r.W = 1;
            r.H = 12;
            if (repair == 2)
            {
                r.X = topX;
                r.Y = topY;
                _c.FillRect(r);
                r.Y += 20;
                _c.FillRect(r);
            }
            repair = slot < NumModuleSlots - 1 ? 1 : 0;
            if (repair != 0)
            {
                r.X = topX + ShipPieceOffset;
                r.Y = topY;
                _c.FillRect(r);
                r.Y += 20;
                _c.FillRect(r);
            }
        }
        int top = _c.Frame(sheet, piece);
        _c.Stamp(sheet, top, topX, topY);
        _c.Stamp(sheet, RelFrame(sheet, top, (Modules - 1) + 6), sideX, sideY);
        if (slot == 1 && piece == EmptySlot + 2)
        {
            int f = _c.Frame(sheet, Modules + 5);
            _c.Stamp(sheet, f, topX - ShipPieceOffset, topY);
            _c.Stamp(sheet, RelFrame(sheet, f, (Modules - 1) + 6), sideX - ShipPieceOffset, sideY);
        }
        if (repair != 0)
        {
            topX += ShipPieceOffset;
            sideX += ShipPieceOffset;
            piece = _s.Modules[++slot];
            if (piece == EmptySlot + 2 && slot >= NumModuleSlots - 3)
                ++piece;
            top = _c.Frame(sheet, piece);
            _c.Stamp(sheet, top, topX, topY);
            _c.Stamp(sheet, RelFrame(sheet, top, (Modules - 1) + 6), sideX, sideY);
        }
    }

    // SetRelFrameIndex: round the sheet's frames either way.
    private int RelFrame(string sheet, int frame, int offset)
    {
        int n = _c.FrameCount(sheet);
        return ((frame + offset) % n + n) % n;
    }

    // outfit.c's DrawModuleStrings.
    private void ModuleStrings(int module)
    {
        var old = _c.SetContext(_c.Status);
        var clip = _c.ClipRect;
        int sx = UqmCanvas.RadarX - clip.X, sy = UqmCanvas.RadarY - clip.Y;
        _c.SetForeground(C(0x0A, 0x0A, 0x0A));
        _c.FillRect(new URect(sx - 1, sy - 11, UqmCanvas.RadarWidth + 2, 11));
        if (module >= EmptySlot)
        {
            _c.SetForeground(C(0, 0, 0));
            _c.FillRect(new URect(sx, sy, UqmCanvas.RadarWidth, UqmCanvas.RadarHeight));
        }
        else
        {
            _c.Stamp(ModulesMenuSheet, module, sx, sy);
            _c.SetFont("tiny");
            _c.SetForeground(C(0x00, 0x1F, 0x00));
            _c.Text(sx + UqmCanvas.RadarWidth - 2, sy + UqmCanvas.RadarHeight - 2, TextAlign.Right,
                (_s.ModuleCost(module) * _s.ModuleCostScale).ToString(CultureInfo.InvariantCulture));
        }
        _c.SetContext(old);
    }

    // sis.c's GetFuelTankCapacity, GetModuleFuelCapacity.
    private static int ModuleFuel(int m) => m == ShipModules.FuelTank ? FuelTankCapacity : m == ShipModules.HighEfficiencyFuelSystem ? HeFuelTankCapacity : 0;

    private int TankCapacity() => FuelReserve + _s.Modules.Sum(m => ModuleFuel(m));

    // sis.c's GetFTankCapacity for fuel unit `amount`: the point, and the colours set.
    private int FTankCapacity(int amount, ref int px, ref int py)
    {
        int capacity = TankCapacity();
        if (amount < FuelReserve)
            return capacity;
        int cap = FuelReserve, slot = -1, compartment = 0;
        for (int i = NumModuleSlots - 1; i >= 0; --i)
        {
            cap += ModuleFuel(_s.Modules[i]);
            if (amount < cap)
            {
                slot = i;
                compartment = cap - amount;
                break;
            }
        }
        if (slot < 0)
            return capacity;
        int volume = ModuleFuel(_s.Modules[slot]);
        int row = (volume - compartment) * MaxFuelBars / HeFuelTankCapacity;
        px = 21 + slot * ShipPieceOffset;
        py = volume == FuelTankCapacity ? 27 - row : 30 - row;
        _c.SetForeground(FuelColors[row]);
        _c.SetBackground(FuelColors[row + 1]);
        return capacity;
    }

    // outfit.c's RedistributeFuel.
    private void RedistributeFuel()
    {
        int volume = (int)_s.Fuel;
        var old = _c.SetContext(_c.Space);
        int x = 0, y = 0;
        for (int fuel = FuelReserve; fuel < FTankCapacity(fuel, ref x, ref y); fuel += FuelVolumePerRow)
        {
            int w;
            if (fuel < volume)
            {
                w = 3;
                _c.Point(x, y);
                x += w + 1;
                _c.Point(x, y);
                x -= w;
                _c.SetForeground(_c.SetBackground(Black));
            }
            else
            {
                w = 5;
                _c.SetForeground(C(0x0B, 0x00, 0x00));
            }
            _c.FillRect(new URect(x, y, w, 1));
        }
        _c.SetContext(old);
    }

    // outfit.c's DisplayLanders.
    private void DisplayLanders()
    {
        if (_s.BombState == 3)
        {
            _c.Stamp(ModuleSheet, (ShipModules.PlanetLander + _c.FrameCount(ModuleSheet) - 1) % _c.FrameCount(ModuleSheet), 0, 0);
            return;
        }
        int x = LanderX, i;
        for (i = 0; i < _s.Landers; ++i, x += LanderWidth)
            _c.Stamp(ModuleSheet, ShipModules.PlanetLander, x, LanderY);
        _c.SetForeground(Black);
        for (; i < MaxLanders; ++i, x += LanderWidth)
            _c.FilledStamp(ModuleSheet, ShipModules.PlanetLander, x, LanderY);
    }

    // ------------------------------------------------------------ the SIS frame and status area

    // border.c's DrawSISFrame.
    private void SisFrame()
    {
        const int ox = UqmCanvas.SisOrgX, oy = UqmCanvas.SisOrgY, sw = UqmCanvas.SisScreenWidth, sh = UqmCanvas.SisScreenHeight;
        const int W = UqmCanvas.ScreenWidth, H = UqmCanvas.ScreenHeight, spaceW = UqmCanvas.SpaceWidth;
        _c.SetContext(_c.Screen);
        _c.SetForeground(C(0x0A, 0x0A, 0x0A));
        var r = new URect(0, 0, ox + sw + 1, oy - 1);
        _c.FillRect(r);
        r = new URect(0, 0, ox - 1, oy + sh + 1);
        _c.FillRect(r);
        r.Y = r.H;
        r.X = 0;
        r.W = ox + sw + 1;
        r.H = H - oy + sh;
        _c.FillRect(r);
        r.X = ox + sw + 1;
        r.Y = 0;
        r.W = W - r.X;
        r.H = H;
        _c.FillRect(r);

        _c.StarConBox(new URect(ox - 1, oy - 1, sw + 2, sh + 2), 1, C(0x10, 0x10, 0x10), C(0x08, 0x08, 0x08), true, Black);
        _c.StarConBox(new URect(ox, 0, SisMessageBoxWidth, oy), 1, C(0x00, 0x00, 0x0E), C(0x00, 0x01, 0x1C), true, C(0x00, 0x00, 0x14));
        _c.StarConBox(new URect(ox + sw - SisTitleBoxWidth, 0, SisTitleBoxWidth, oy), 1, C(0x00, 0x00, 0x0E), C(0x00, 0x01, 0x1C),
            true, C(0x00, 0x00, 0x14));

        _c.SetForeground(Black);
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW - 1, 0, 1, H));
        _c.Point(UqmCanvas.SafeX + spaceW, UqmCanvas.SafeY + 139);
        _c.Point(W - 1, UqmCanvas.SafeY + 139);

        _c.SetForeground(C(0x10, 0x10, 0x10));
        r = new URect(ox - 1, 1, 1, UqmCanvas.SafeY + SisTitleHeight);
        _c.FillRect(r);
        r.X = ox + sw - SisTitleBoxWidth - 1;
        _c.FillRect(r);
        _c.FillRect(new URect(0, H - 1, UqmCanvas.SafeX + spaceW - 1, 1));
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW - 2, 0, 1, H - 1));
        _c.FillRect(new URect(W - 1, 0, 1, UqmCanvas.SafeY + 139));
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW, H - 1, W - (UqmCanvas.SafeX + spaceW), 1));
        _c.FillRect(new URect(W - 1, UqmCanvas.SafeY + 140, 1, (H - 1) - (UqmCanvas.SafeY + 140)));

        _c.SetForeground(C(0x08, 0x08, 0x08));
        r = new URect(ox + SisMessageBoxWidth, 1, 1, UqmCanvas.SafeY + SisMessageHeight);
        _c.FillRect(r);
        r.X = ox + sw;
        ++r.H;
        _c.FillRect(r);
        r.Y = 0;
        r.W = (UqmCanvas.SafeX + spaceW - 2) - r.X;
        r.H = 1;
        _c.FillRect(r);
        r.X = 0;
        r.W = ox - r.X;
        _c.FillRect(r);
        r.X = ox + SisMessageBoxWidth;
        r.W = SisSpacerBoxWidth;
        _c.FillRect(r);
        _c.FillRect(new URect(0, 1, 1, (H - 1) - 1));
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW, 0, 1, UqmCanvas.SafeY + 139));
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW + 1, UqmCanvas.SafeY + 139, UqmCanvas.StatusWidth - 2, 1));
        _c.FillRect(new URect(UqmCanvas.SafeX + spaceW, UqmCanvas.SafeY + 140, 1, H - (UqmCanvas.SafeY + 140)));

        // InitSISContexts, ClearSISRect (DRAW_SIS_DISPLAY).
        _c.SetContext(_c.Space);
        ClearSisRect(clearRadar: false);
    }

    // sis.c's ClearSISRect: DRAW_SIS_DISPLAY, or CLEAR_SIS_RADAR.
    private void ClearSisRect(bool clearRadar)
    {
        var old = _c.SetContext(_c.Status);
        var oldColor = _c.SetForeground(C(0x0A, 0x0A, 0x0A));
        if (!clearRadar)
            Gauges(ScreenOp.Undefined, ScreenOp.Undefined, ScreenOp.Undefined);
        else
            MenuStateStrings(0xFF, 1);
        _c.SetForeground(oldColor);
        _c.SetContext(old);
    }

    // sis.c's DeltaSISGauges: each gauge whose change isn't 0 drawn (with the ship as it stands after the change),
    // UNDEFINED_DELTA drawing it afresh (the crew's: the whole status area); the fuel's number only if its whole
    // units changed.
    private void Gauges(int crewDelta, int fuelDelta, int ruDelta)
    {
        const int U = ScreenOp.Undefined;
        if (crewDelta == 0 && fuelDelta == 0 && ruDelta == 0)
            return;
        var old = _c.SetContext(_c.Status);
        if (crewDelta == U)
        {
            _c.Stamp(FlagStatSheet, 0, 0, 0);
            PcSis();
            int y = 0;
            for (int i = 0; i < NumDriveSlots; ++i, y -= 3)
                if (_s.Drives[i] < EmptySlot)
                {
                    _c.Stamp(FlagStatSheet, _s.Drives[i] + 1, 1, y);
                    _c.Stamp(FlagStatSheet, _s.Drives[i] + 2, 1, y);
                }
            y = 0;
            for (int i = 0; i < NumJetSlots; ++i, y -= 3)
                if (_s.Jets[i] < EmptySlot)
                {
                    _c.Stamp(FlagStatSheet, _s.Jets[i] + 2, 1, y);
                    _c.Stamp(FlagStatSheet, _s.Jets[i] + 3, 1, y);
                }
            y = 1;
            for (int i = 0; i < NumModuleSlots; ++i, y -= 3)
                if (_s.Modules[i] < EmptySlot)
                    _c.Stamp(FlagStatSheet, _s.Modules[i] + 3, 1, y);
            // No escorts in the support ships' places (3b).
        }
        _c.SetFont("tiny");
        if (crewDelta != 0)
        {
            var gauge = GaugeRect(crew: true);
            _c.SetForeground(Black);
            _c.FillRect(gauge);
            _c.SetForeground(C(0x00, 0x0E, 0x00));
            _c.Text(UqmCanvas.StatusWidth >> 1, gauge.Y + gauge.H, TextAlign.Center, _s.Crew.ToString(CultureInfo.InvariantCulture));
        }
        if (fuelDelta != 0)
        {
            long oldCoarse = fuelDelta == U ? -1 : (_s.Fuel - (long)fuelDelta) / FuelTankScale;
            long coarse = _s.Fuel / FuelTankScale;
            if (coarse != oldCoarse)
            {
                var gauge = GaugeRect(crew: false);
                _c.SetForeground(Black);
                _c.FillRect(gauge);
                _c.SetForeground(C(0x13, 0x00, 0x00));
                _c.Text(UqmCanvas.StatusWidth >> 1, gauge.Y + gauge.H, TextAlign.Center, coarse.ToString(CultureInfo.InvariantCulture));
            }
        }
        if (crewDelta == U)
        {
            FlagshipName(true);
            CaptainsName();
            Landers();
            StorageBays();
        }
        if (ruDelta != 0)
        {
            if (ruDelta == U)
            {
                _c.SetForeground(C(0x00, 0x08, 0x00));
                _c.FillRect(new URect(2, 130, StatusMessageWidth, StatusMessageHeight));
            }
            StatusMessage();
        }
        _c.SetContext(old);
    }

    private static URect GaugeRect(bool crew) => new((UqmCanvas.StatusWidth >> 1) - (24 >> 1), crew ? 117 : 38, 24, 5);

    // sis.c's DrawPC_SIS.
    private void PcSis()
    {
        var r = GaugeRect(crew: false);
        int tx = UqmCanvas.StatusWidth >> 1, ty = r.Y - 1;
        _c.SetFont("tiny");
        _c.SetForeground(Black);
        r.Y -= 6;
        r.X--;
        r.W += 2;
        _c.FillRect(r);
        _c.SetFontEffect(1);
        _c.Text(tx, ty, TextAlign.Center, GameString(GameStrings.StatusBase + 3));
        r.Y += 79;
        ty += 79;
        _c.FillRect(r);
        _c.SetFontEffect(2);
        _c.Text(tx, ty, TextAlign.Center, GameString(GameStrings.StatusBase + 4));
        _c.SetFontEffect(-1);
        r = new URect(2 + 1, 3, 58, 7);
        _c.SetForeground(PcCaptainBack);
        _c.FillRect(r);
        _c.SetForeground(PcCaptainText);
        _c.Text(tx, r.Y + 6, TextAlign.Center, GameString(GameStrings.StatusBase + 5));
    }

    // sis.c's DrawFlagshipName.
    private void FlagshipName(bool inStatusArea)
    {
        URect r;
        string text;
        UqmCanvas.Context old;
        string oldFont;
        if (inStatusArea)
        {
            old = _c.SetContext(_c.Status);
            oldFont = _c.SetFont("starcon");
            r = new URect(2, 20, ShipNameWidth, ShipNameHeight);
            text = _s.ShipName;
        }
        else
        {
            old = _c.SetContext(_c.Space);
            oldFont = _c.SetFont("micro");
            r = new URect(0, 1, UqmCanvas.SisScreenWidth, ShipNameHeight);
            // strupr: ASCII letters only ("this will not work with UTF-8 strings").
            text = new string($"{GameString(GameStrings.NamingBase + 1)} {_s.ShipName}"
                .Select(ch => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch).ToArray());
        }
        int oldEffect = _c.SetFontEffect(-1);
        var oldColor = _c.SetForeground(Black);
        _c.FillRect(r);
        _c.SetFontEffect(inStatusArea ? 0 : 3);
        _c.Text(r.X + (r.W >> 1), r.Y + (ShipNameHeight - (inStatusArea ? 1 : 0)), TextAlign.Center, text);
        _c.SetFontEffect(oldEffect);
        _c.SetForeground(oldColor);
        _c.SetFont(oldFont);
        _c.SetContext(old);
    }

    // sis.c's DrawCaptainsName.
    private void CaptainsName()
    {
        var old = _c.SetContext(_c.Status);
        var oldFont = _c.SetFont("tiny");
        var oldColor = _c.SetForeground(CaptainNameBack);
        var r = new URect(2 + 1, 10, ShipNameWidth - 2, ShipNameHeight);
        _c.FillRect(r);
        _c.SetForeground(CaptainNameText);
        _c.Text((UqmCanvas.StatusWidth >> 1) - 1, r.Y + 6, TextAlign.Center, _s.Captain);
        _c.SetForeground(oldColor);
        _c.SetFont(oldFont);
        _c.SetContext(old);
    }

    // sis.c's DrawLanders.
    private void Landers()
    {
        var old = _c.SetContext(_c.Status);
        var r = _c.FrameRect(FlagStatSheet, 1);
        int i = _s.Landers;
        r.X = (UqmCanvas.StatusWidth >> 1) - r.X;
        int sx = r.X - (((r.W * i) + (2 * (i - 1))) >> 1), sy = 29;
        int width = r.W + 2;
        r.W = (r.W * MaxLanders) + (2 * (MaxLanders - 1)) + 2;
        r.X -= r.W >> 1;
        r.Y += sy;
        _c.SetForeground(Black);
        _c.FillRect(r);
        while (i-- > 0)
        {
            _c.Stamp(FlagStatSheet, 1, sx, sy);
            sx += width;
        }
        _c.SetContext(old);
    }

    // sis.c's DrawStorageBays (FALSE).
    private void StorageBays()
    {
        var old = _c.SetContext(_c.Status);
        var r = new URect(0, 123, 2, 4);
        int i = _s.Modules.Count(m => m == ShipModules.StorageBay);
        if (i > 0)
        {
            r.X = (UqmCanvas.StatusWidth >> 1) - ((i * (r.W + 1)) >> 1);
            _c.SetForeground(StorageFull);
            int j;
            for (j = _s.MineralMass; j >= StorageBayCapacity; j -= StorageBayCapacity)
            {
                _c.FillRect(r);
                r.X += r.W + 1;
                --i;
            }
            r.H = (4 * j + (StorageBayCapacity - 1)) / StorageBayCapacity;
            if (r.H != 0)
            {
                r.Y += 4 - r.H;
                _c.FillRect(r);
                r.H = 4 - r.H;
                if (r.H != 0)
                {
                    r.Y = 123;
                    _c.SetForeground(StorageEmpty);
                    _c.FillRect(r);
                }
                r.X += r.W + 1;
                --i;
            }
            r.H = 4;
            _c.SetForeground(StorageEmpty);
            while (i-- > 0)
            {
                _c.FillRect(r);
                r.X += r.W + 1;
            }
        }
        _c.SetContext(old);
    }

    // sis.c's DrawStatusMessage (NULL), in SMM_RES_UNITS mode as at the starbase.
    private void StatusMessage()
    {
        var old = _c.SetContext(_c.Status);
        var ctx = _c.ClipRect;
        _c.SetContext(_c.OffScreen);
        _c.SetClipRect(new URect(2 + ctx.X, 130 + ctx.Y, StatusMessageWidth, StatusMessageHeight));
        _c.SetBackground(StatusMessageBack);
        _c.Clear();
        string text = _s.BombState < 2
            ? $"{_s.ResUnits} {GameString(GameStrings.StatusBase + 1)}"
            : $"{GameString(GameStrings.StatusBase + 2)} {GameString(GameStrings.StatusBase + 1)}";
        _c.SetFont("tiny");
        _c.SetForeground(StatusMessageText);
        _c.Text(StatusMessageWidth >> 1, StatusMessageHeight - 1, TextAlign.Center, text);
        _c.SetClipRect(null);
        _c.SetContext(old);
    }

    // sis.c's DrawSISMessage (normal state).
    private void SisMessage(string text)
    {
        var old = _c.SetContext(_c.OffScreen);
        _c.SetClipRect(new URect(UqmCanvas.SisOrgX + 1, UqmCanvas.SisOrgY - SisMessageHeight, SisMessageWidth, SisMessageHeight - 1));
        _c.SetBackground(SisMessageBack);
        _c.SetForeground(SisMessageText);
        _c.SetFont("tiny");
        _c.Clear();
        _c.Text(SisMessageWidth >> 1, SisMessageHeight - 2, TextAlign.Center, text);
        _c.SetClipRect(null);
        _c.SetContext(old);
    }

    // sis.c's DrawSISTitle.
    private void SisTitle(string text)
    {
        var old = _c.SetContext(_c.OffScreen);
        _c.SetClipRect(new URect(UqmCanvas.SisOrgX + UqmCanvas.SisScreenWidth - SisTitleBoxWidth + 1, UqmCanvas.SisOrgY - SisTitleHeight,
            SisTitleWidth, SisTitleHeight - 1));
        _c.SetFont("tiny");
        _c.SetBackground(C(0x00, 0x00, 0x14));
        _c.Clear();
        _c.SetForeground(C(0x1B, 0x00, 0x1B));
        _c.Text(SisTitleWidth >> 1, SisTitleHeight - 2, TextAlign.Center, text);
        _c.SetClipRect(null);
        _c.SetContext(old);
    }

    // sis.c's DrawFlagshipStats.
    private void FlagshipStats()
    {
        int energyRegeneration = 1, energyWait = 10, maxThrust = 10, turnWait = 17;
        int fuel = 10 * FuelTankScale;
        foreach (var m in _s.Modules)
        {
            switch (m)
            {
                case ShipModules.FuelTank:
                    fuel += FuelTankCapacity;
                    break;
                case ShipModules.HighEfficiencyFuelSystem:
                    fuel += HeFuelTankCapacity;
                    break;
                case ShipModules.DynamoUnit:
                    energyWait -= 2;
                    if (energyWait < 4)
                        energyWait = 4;
                    break;
                case ShipModules.ShivaFurnace:
                    energyRegeneration++;
                    break;
            }
        }
        maxThrust += 2 * _s.Drives.Count(d => d == ShipModules.FusionThruster);
        turnWait -= 2 * _s.Jets.Count(j => j == ShipModules.TurningJets);

        var old = _c.SetContext(_c.Space);
        var oldFont = _c.SetFont("starcon");
        int oldEffect = _c.SetFontEffect(-1);
        int leading = _c.Leading;
        var r = new URect(0, UqmCanvas.SisScreenHeight - 4 * leading, UqmCanvas.SisScreenWidth, 4 * leading);
        var oldColor = _c.SetForeground(Black);
        _c.FillRect(r);
        leading -= 3;
        int x = UqmCanvas.SisScreenWidth / 6, y = r.Y + leading + 3;
        _c.SetFontEffect(4);
        for (int k = 0; k < 4; ++k, y += leading)
            _c.Text(x, y, TextAlign.Right, GameString(GameStrings.FlagshipBase + k));
        x += 5;
        y = r.Y + leading + 3;
        foreach (int slot in new[] { 15, 14, 13, 0 })
        {
            _c.Text(x, y, TextAlign.Left, Pad7(DescribeWeapon(_s.Modules[slot])));
            y += leading;
        }
        x = r.W - 25;
        y = r.Y + leading + 3;
        _c.SetFontEffect(5);
        for (int k = 4; k < 8; ++k, y += leading)
            _c.Text(x, y, TextAlign.Right, GameString(GameStrings.FlagshipBase + k));
        x = r.W - 2;
        y = r.Y + leading + 3;
        _c.Text(x, y, TextAlign.Right, $"{maxThrust * 4,4}");
        y += leading;
        _c.Text(x, y, TextAlign.Right, $"{1 + 17 - turnWait,4}");
        y += leading;
        // ONE_SECOND 840, BATTLE_FRAME_RATE ONE_SECOND / 24.
        int energyPer10 = ((100 * 840 * energyRegeneration) / ((1 + energyWait) * (840 / 24)) + 5) / 10;
        _c.Text(x, y, TextAlign.Right, $"{energyPer10 / 10,2}.{energyPer10 % 10}");
        y += leading;
        _c.Text(x, y, TextAlign.Right, $"{fuel / FuelTankScale,4}");
        _c.SetFontEffect(oldEffect);
        _c.SetForeground(oldColor);
        _c.SetFont(oldFont);
        _c.SetContext(old);
    }

    // "%-7.7s": padded to 7 bytes, cut at 7.
    private static string Pad7(string s) => s.Length >= 7 ? s[..7] : s.PadRight(7);

    private string DescribeWeapon(int m) => m switch
    {
        ShipModules.GunWeapon => GameString(GameStrings.FlagshipBase + 8),
        ShipModules.BlasterWeapon => GameString(GameStrings.FlagshipBase + 9),
        ShipModules.CannonWeapon => GameString(GameStrings.FlagshipBase + 10),
        >= ShipModules.BombModule0 and < ShipModules.BombModule0 + 6 => GameString(GameStrings.FlagshipBase + 11),
        _ => GameString(GameStrings.FlagshipBase + 12),
    };

    // ------------------------------------------------------------ the PC menu

    // menu.c's GetEndMenuState, for the starbase's menus.
    private static int EndMenuState(int beg) => beg switch
    {
        PmFuel => PmExitOutfit,
        PmCrew => PmExitShipyard,
        _ => beg,
    };

    /// <summary>
    /// menu.c's DrawMenuStateStrings with the PC menus, for the starbase's
    /// menus (PM_FUEL, PM_CREW; 0xFF draws only the menu's area, as
    /// ClearSISRect (CLEAR_SIS_RADAR) asks).
    /// </summary>
    private void MenuStateStrings(int beg, int newState)
    {
        bool hilite = true;
        if (newState < 0)
        {
            newState = -newState;
            hilite = false;
        }
        // GetAlternateMenu leaves these menus as they are.
        int end = EndMenuState(beg);
        bool frame = newState <= end - beg;   // s.frame: PlayFrame's beg + NewState
        var old = _c.SetContext(_c.Status);
        var clip = _c.ClipRect;
        int sx = UqmCanvas.RadarX - clip.X, sy = UqmCanvas.RadarY - clip.Y;
        var r = new URect(sx - 1, sy - 11, UqmCanvas.RadarWidth + 2, 0);
        _c.SetForeground(C(0x0A, 0x0A, 0x0A));
        if (frame)
        {
            r.H = UqmCanvas.RadarHeight + 11;
            PcMenu(beg, end, newState, hilite, r);
        }
        else
        {
            r.X -= 1;
            r.W += 1;
            r.H = UqmCanvas.RadarHeight + 11;
            _c.FillRect(r);
        }
        _c.SetContext(old);
    }

    // menu.c's DrawPCMenu and DrawPCMenuFrame.
    private void PcMenu(int beg, int end, int newState, bool hilite, URect r)
    {
        int pos = beg + newState;
        int items = 1 + end - beg;
        r.X -= 1;
        r.W += 1;
        _c.FillRect(r);
        if (items * PcMenuHeight <= r.H)
            r.Y += (r.H - items * PcMenuHeight) / 2;
        r.H = items * PcMenuHeight + 4;
        _c.SetForeground(MenuTopLeft);
        _c.Outline(r);
        r.X++;
        r.Y++;
        r.H--;
        r.W--;
        _c.SetForeground(MenuBottomRight);
        _c.Outline(r);
        r.H--;
        r.W--;
        _c.SetForeground(MenuBack);
        _c.FillRect(r);
        var oldFont = _c.SetFont("starcon");
        int tx = r.X + 2, ty = r.Y + PcMenuHeight - 1;
        r.X++;
        r.W -= 2;
        for (int i = beg; i <= end; i++)
        {
            string text = i == PmFuel ? $"{GameString(GameStrings.MainMenuBase + PmFuel)}({_s.FuelCost})"
                : i == PmCrew ? $"{GameString(GameStrings.MainMenuBase + PmCrew)}({_s.CrewCost})"
                : GameString(GameStrings.MainMenuBase + i);
            if (hilite && pos == i)
            {
                _c.SetForeground(MenuSelBack);
                r.Y = ty - PcMenuHeight + 2;
                r.H = PcMenuHeight - 1;
                _c.FillRect(r);
                _c.SetForeground(MenuSelText);
            }
            else
                _c.SetForeground(MenuText);
            _c.Text(tx, ty, TextAlign.Left, text);
            ty += PcMenuHeight;
        }
        _c.SetFont(oldFont);
    }
}

/// <summary>
/// shipyard.c's animatePowerLines on UQM's clock (840 ticks a second):
/// the shipyard drawn shows the power lines in colortable.hangar's first
/// map, and each input frame at least ONE_SECOND / HANGAR_ANIM_RATE after
/// the last redraw shows them in the next, round the table.
/// </summary>
public sealed class PowerLines
{
    /// <summary>ONE_SECOND / HANGAR_ANIM_RATE (15 a second).</summary>
    public const int Ticks = 840 / 15;

    private readonly int _count;
    private int _next;
    private long _nextTime;

    /// <param name="count">How many maps colortable.hangar has.</param>
    public PowerLines(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    /// <summary>The map the power lines are drawn in.</summary>
    public int Map { get; private set; } = -1;

    /// <summary>The shipyard drawn afresh at <paramref name="now"/> (animatePowerLines (pMS)).</summary>
    public void Open(long now)
    {
        _next = 0;
        Draw(now);
    }

    /// <summary>An input frame at <paramref name="now"/> (on_input_frame); whether the lines were redrawn.</summary>
    public bool Frame(long now)
    {
        if (now < _nextTime)
            return false;
        Draw(now);
        return true;
    }

    private void Draw(long now)
    {
        _nextTime = now + Ticks;
        Map = _next;
        _next = (_next + 1) % _count;
    }
}

/// <summary>
/// starbase.c's rotateStarbase on UQM's clock (840 ticks a second): the
/// menu drawn shows the starbase's frame 0, and each pass of DoStarBase
/// (with a key or none) at least ONE_SECOND / 20 after the last frame
/// shows the next, round the sheet past frame 0, which isn't drawn again.
/// </summary>
public sealed class StarbaseTurning
{
    /// <summary>ONE_SECOND / 20.</summary>
    public const int Ticks = 840 / 20;

    private readonly int _count;
    private int _next;
    private long _nextTime;

    /// <param name="count">How many frames graphics.starbase has.</param>
    public StarbaseTurning(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    /// <summary>The frame last drawn.</summary>
    public int Frame { get; private set; }

    /// <summary>The menu drawn afresh at <paramref name="now"/> (rotateStarbase with the sheet).</summary>
    public void Open(long now)
    {
        _next = 0;
        Draw(now);
    }

    /// <summary>A pass of DoStarBase at <paramref name="now"/>; whether a frame was drawn.</summary>
    public bool Pass(long now)
    {
        if (now < _nextTime)
            return false;
        Draw(now);
        return true;
    }

    private void Draw(long now)
    {
        _nextTime = now + Ticks;
        Frame = _next;
        _next = (_next + 1) % _count;
        if (_next == 0)
            _next = 1 % _count;
    }
}
