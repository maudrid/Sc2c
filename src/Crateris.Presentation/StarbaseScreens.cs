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
// DoInstallModule's drawing; border.c: DrawSISFrame; sis.c: ClearSISRect,
// DrawSISTitle, DrawSISMessage, DrawStatusMessage, DrawCaptainsName,
// DrawFlagshipName, DrawFlagshipStats, DrawLanders, DrawStorageBays,
// DrawPC_SIS, DrawThrusters, DrawTurningJets, DrawModules,
// DeltaSISGauges, GetFTankCapacity; menu.c: DrawMenuStateStrings,
// DrawPCMenu, DrawPCMenuFrame; colors.h), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under
// the GNU GPL 2 or later.

using System.Globalization;
using Crateris.Core.Content;
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
/// The allied starbase's screens as UQM's PC version draws them, a whole
/// screen at a time (UQM draws each change over what was there; drawing
/// the whole screen from where its menus stand gives the same picture):
/// the menu (the starbase turning, the entries) and outfitting (the SIS
/// frame and status area, the flagship's pieces and fuel, the landers,
/// the menu or the module chosen). The game's strings come from
/// <see cref="GameString"/>.
/// </summary>
public sealed class StarbaseScreens
{
    public const string StarbaseSheet = "graphics.starbase", ModulesMenuSheet = "graphics.modulesmenu",
        OutfitSheet = "graphics.outfit", ModuleSheet = "graphics.outfitmodules", FlagStatSheet = "graphics.flagshipstatus",
        PlayMenuSheet = "graphics.playmenu", FontGradientSheet = "graphics.fontgradient";

    /// <summary>The sheets these screens stamp (to load their pictures).</summary>
    public static readonly string[] Sheets = { StarbaseSheet, ModulesMenuSheet, OutfitSheet, ModuleSheet, FlagStatSheet, PlayMenuSheet };

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
    public const int PmFuel = 21, PmExitOutfit = 24, PmCrew = 25, PmExitShipyard = 27;

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

    public StarbaseScreens(UqmCanvas canvas, Func<int, string> gameString)
    {
        _c = canvas;
        GameString = gameString;
    }

    public Func<int, string> GameString { get; }

    public List<UqmDraw> Drawn => _c.Drawn;

    // ------------------------------------------------------------ the menu

    /// <summary>
    /// DoStarBase's screen on <paramref name="state"/> (TALK_COMMANDER to
    /// DEPART_BASE), the starbase turned to <paramref name="frame"/>
    /// (rotateStarbase: 0 at first, then 1 on and round, past 0).
    /// </summary>
    public void Menu(int state, int frame)
    {
        _c.SetContext(_c.Screen);
        _c.SetBackground(Black);
        _c.Clear();
        _c.Stamp(StarbaseSheet, 0, UqmCanvas.SafeX, UqmCanvas.SafeY + 4);
        if (frame != 0)
            _c.Stamp(StarbaseSheet, frame, UqmCanvas.SafeX, UqmCanvas.SafeY + 4);
        BaseStateStrings(-1, state);
    }

    /// <summary>rotateStarbase: the frame after <paramref name="frame"/>, round past 0 (which isn't drawn again).</summary>
    public static int NextStarbaseFrame(int frame, int count)
    {
        frame = (frame + 1) % count;
        return frame == 0 ? 1 % count : frame;
    }

    /// <summary>rotateStarbase's pace: ONE_SECOND / 20.</summary>
    public const int StarbaseFrameTicks = 840 / 20;

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

    /// <summary>
    /// DoOutfit's screen for <paramref name="ship"/> with its menus where
    /// <paramref name="state"/> and <paramref name="item"/> (UQM's CurState
    /// and delta_item) leave them: the outfitting menu (Fuel, Modules,
    /// Save, Exit, or the fuel being bought), or with
    /// <paramref name="installing"/> the module being chosen or the slot.
    /// </summary>
    public void Outfit(SisState ship, int state, int item, bool installing)
    {
        _s = ship;
        // DoOutfit's first drawing.
        SisFrame();
        SisMessage(GameString(GameStrings.StarbaseBase + 2));
        SisTitle(GameString(GameStrings.StarbaseBase));
        _c.SetContext(_c.Space);
        _c.Stamp(OutfitSheet, 0, 0, 0);
        for (int i = 0; i < NumDriveSlots; ++i)
            if (_s.Drives[i] < EmptySlot)
                ShipPiece(_s.Drives[i], i, false);
        for (int i = 0; i < NumJetSlots; ++i)
            if (_s.Jets[i] < EmptySlot)
                ShipPiece(_s.Jets[i], i, false);
        for (int i = 0; i < NumModuleSlots; ++i)
            if (_s.Modules[i] < EmptySlot)
                ShipPiece(_s.Modules[i], i, false);
        RedistributeFuel();
        DisplayLanders();
        if (_s.BombState < 3)
        {
            int f = _c.FrameCount(ModuleSheet) - 5;
            for (int d = 0; d < 4; ++d, ++f)
                if ((_s.LanderShields & (1 << d)) != 0)
                    _c.Stamp(ModuleSheet, f, 0, 0);
        }
        MenuStateStrings(PmFuel, installing ? 1 : state == 4 ? 0 : state);
        FlagshipName(false);
        FlagshipStats();
        if (!installing)
            return;
        // DoInstallModule: the menu's area cleared, then the module shown.
        ClearSisRect(clearRadar: true);
        int shown;
        if (state < EmptySlot)
            shown = state;
        else
            shown = state switch
            {
                ShipModules.PlanetLander or EmptySlot + 3 => item < _s.Landers ? ShipModules.PlanetLander : EmptySlot + 3,
                ShipModules.FusionThruster or EmptySlot + 0 => _s.Drives[item],
                ShipModules.TurningJets or EmptySlot + 1 => _s.Jets[item],
                _ => _s.Modules[item],
            };
        ModuleStrings(shown);
    }

    // starbase.c's DrawShipPiece (not as a blueprint).
    private void ShipPiece(int piece, int slot, bool bluePrint)
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
        if (repair < 0)
        {
            sideFrame = _c.Frame(ModuleSheet, (Modules - 1) + (6 - 2) + (Modules + 6) - (repair + 1));
            _c.Stamp(ModuleSheet, sideFrame, sideX, sideY);
        }
        else if (repair != 0)
        {
            _c.SetForeground(Black);
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
            _c.FilledStamp(ModuleSheet, piece - 1, sideX, sideY);
            return;
        }
        topX += slot * ShipPieceOffset;
        if (repair < 0)
            _c.Stamp(ModuleSheet, RelFrame(sideFrame, -((Modules - 1) + 6)), topX, topY);
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
        int top = _c.Frame(ModuleSheet, piece);
        _c.Stamp(ModuleSheet, top, topX, topY);
        _c.Stamp(ModuleSheet, RelFrame(top, (Modules - 1) + 6), sideX, sideY);
        if (slot == 1 && piece == EmptySlot + 2)
        {
            int f = _c.Frame(ModuleSheet, Modules + 5);
            _c.Stamp(ModuleSheet, f, topX - ShipPieceOffset, topY);
            _c.Stamp(ModuleSheet, RelFrame(f, (Modules - 1) + 6), sideX - ShipPieceOffset, sideY);
        }
        if (repair != 0)
        {
            topX += ShipPieceOffset;
            sideX += ShipPieceOffset;
            piece = _s.Modules[++slot];
            if (piece == EmptySlot + 2 && slot >= NumModuleSlots - 3)
                ++piece;
            top = _c.Frame(ModuleSheet, piece);
            _c.Stamp(ModuleSheet, top, topX, topY);
            _c.Stamp(ModuleSheet, RelFrame(top, (Modules - 1) + 6), sideX, sideY);
        }
    }

    // SetRelFrameIndex: round the sheet's frames either way.
    private int RelFrame(int frame, int offset)
    {
        int n = _c.FrameCount(ModuleSheet);
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
            SisGauges();
        else
            MenuStateStrings(0xFF, 1);
        _c.SetForeground(oldColor);
        _c.SetContext(old);
    }

    // sis.c's DeltaSISGauges (UNDEFINED_DELTA, UNDEFINED_DELTA, UNDEFINED_DELTA).
    private void SisGauges()
    {
        var old = _c.SetContext(_c.Status);
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
        _c.SetFont("tiny");
        var gauge = GaugeRect(crew: true);
        _c.SetForeground(Black);
        _c.FillRect(gauge);
        _c.SetForeground(C(0x00, 0x0E, 0x00));
        _c.Text(UqmCanvas.StatusWidth >> 1, gauge.Y + gauge.H, TextAlign.Center, _s.Crew.ToString(CultureInfo.InvariantCulture));
        gauge = GaugeRect(crew: false);
        _c.SetForeground(Black);
        _c.FillRect(gauge);
        _c.SetForeground(C(0x13, 0x00, 0x00));
        _c.Text(UqmCanvas.StatusWidth >> 1, gauge.Y + gauge.H, TextAlign.Center,
            (_s.Fuel / FuelTankScale).ToString(CultureInfo.InvariantCulture));
        FlagshipName(true);
        CaptainsName();
        Landers();
        StorageBays();
        _c.SetForeground(C(0x00, 0x08, 0x00));
        _c.FillRect(new URect(2, 130, StatusMessageWidth, StatusMessageHeight));
        StatusMessage();
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
