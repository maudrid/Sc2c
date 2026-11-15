// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/pickship.c:
// DrawArmadaPickShip, DoPickBattleShip's drawing; src/uqm/planets/roster.c:
// drawSupportShip, flashSupportShip, flashSupportShipCrew, DeltaSupportCrew's
// and DoModifyRoster's drawing; colors.h), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under the GNU
// GPL 2 or later.

using System.Globalization;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// A fleet ship on the pick screen: its hangar slot, its race's status
/// icons (null: none drawn), whether it's out of the battle (SpeciesID
/// NO_ID: destroyed or fled) and its crew; one out with crew ran away
/// (drawn blue), one out with none was destroyed (an X on it).
/// </summary>
public sealed record PickShip(int Slot, string? Icons, bool Out, int Crew);

/// <summary>
/// The ship the pick screen's cursor lights (DoPickBattleShip): its
/// captain's name (the flagship's commander's), its crew and most (the
/// flagship's crew enlisted and its crew pods' room).
/// </summary>
public sealed record PickShipLit(string Captain, int Crew, int MaxCrew);

/// <summary>
/// The pick screen as UQM draws it over the battle's space window (stage
/// 4b item 10; pickship.c): the pick frame in the middle with the
/// flagship's name and each fleet ship's icon in its hangar slot's place
/// (<see cref="Draw"/>, DrawArmadaPickShip), and as the cursor moves, the
/// lit ship's captain and crew (<see cref="Select"/>, DoPickBattleShip),
/// the flashing highlight on the cursor (<see cref="Flash"/>).
/// </summary>
public sealed class PickScreenDrawing
{
    /// <summary>SC2_PICK_PMAP_ANIM and STATUS_MASK_PMAP_ANIM (StatusFrame: the X on a destroyed ship).</summary>
    public const string PickSheet = "graphics.pickship", StatusSheet = "graphics.status";

    private const int IconWidth = 16, IconHeight = 16, FlagshipXOffs = 65, FlagshipYOffs = 4, FlagshipWidth = 22, FlagshipHeight = 48;
    private const int Columns = PickShipMenu.Columns;

    private static Rgb C(int r, int g, int b) => Rgb.From15((byte)r, (byte)g, (byte)b);

    private static readonly Rgb Black = C(0, 0, 0), NameColor = C(0x12, 0x12, 0x12), FledColor = C(0x00, 0x00, 0x14),
        CaptainColor = C(0x14, 0x0A, 0x00), CrewColor = C(0x00, 0x14, 0x00);

    /// <summary>The battle's space window (init.c's SpaceContext clip): where the pick screen is drawn.</summary>
    public static readonly URect SpaceWindow = new(UqmCanvas.SafeX, UqmCanvas.SafeY, UqmCanvas.SpaceWidth, UqmCanvas.SpaceHeight);

    private readonly UqmCanvas _c;

    public PickScreenDrawing(UqmCanvas canvas) => _c = canvas;

    /// <summary>The pick frame's place in the space window (pick_r: centred, its hot spot taken as its corner).</summary>
    public URect PickRect
    {
        get
        {
            var f = _c.FrameRect(PickSheet, 0);
            return new URect((SpaceWindow.W >> 1) - (f.W >> 1), (SpaceWindow.H >> 1) - (f.H >> 1), f.W, f.H);
        }
    }

    /// <summary>Where the flashing highlight is (DoPickBattleShip's flash_rect0), in the screen's pixels; null for none.</summary>
    public URect? Flash { get; set; }

    private UqmCanvas.Context Enter()
    {
        var old = _c.SetContext(_c.Space);
        _c.SetClipRect(SpaceWindow);
        return old;
    }

    /// <summary>DrawArmadaPickShip: the pick frame, the flagship's name, each fleet ship as it stands.</summary>
    public void Draw(string shipName, IEnumerable<PickShip> ships)
    {
        var old = Enter();
        var pick = PickRect;
        var own = _c.FrameRect(PickSheet, 0);
        _c.Stamp(PickSheet, 0, pick.X - own.X, pick.Y - own.Y);   // SetFrameHot (0, 0): its corner at pick_r's
        _c.SetForeground(NameColor);
        _c.SetFont("starcon");
        _c.Text(pick.X + (pick.W >> 1), pick.Y + pick.H - 5, TextAlign.Center, shipName);
        foreach (var ship in ships)
        {
            int col = ship.Slot % Columns;
            int x = pick.X + 5 + (IconWidth + 4) * col;
            if (col >= Columns >> 1)
                x += FlagshipWidth + 4;
            int y = pick.Y + 16 + (IconHeight + 4) * (ship.Slot / Columns);
            _c.SetForeground(Black);
            _c.FillRect(new URect(x, y, IconWidth, IconHeight));
            if (ship.Icons is not { } icons)
                continue;
            if (!ship.Out || ship.Crew == 0)
            {
                _c.Stamp(icons, 0, x, y);
                if (ship.Out)
                    _c.Stamp(StatusSheet, 3, x - 1, y);   // a dead ship marked with an X
            }
            else
            {
                _c.SetForeground(FledColor);   // a ship that ran away
                _c.FilledStamp(icons, 0, x, y);
            }
        }
        _c.SetContext(old);
    }

    /// <summary>
    /// DoPickBattleShip's ChangeSelection: the flash on the cursor at
    /// <paramref name="row"/> and <paramref name="column"/>, the lit ship's
    /// captain and crew (none: the two boxes black).
    /// </summary>
    public void Select(int row, int column, PickShipLit? lit)
    {
        var old = Enter();
        var pick = PickRect;
        URect flash;
        if (column == PickShipMenu.FlagshipColumn)
            flash = new URect(pick.X - 2 + FlagshipXOffs, pick.Y - 2 + FlagshipYOffs, FlagshipWidth + 4, FlagshipHeight + 4);
        else
        {
            int x = 5 + pick.X - 2 + (IconWidth + 4) * column;
            if (column > PickShipMenu.FlagshipColumn)
                x += FlagshipWidth - IconWidth;
            flash = new URect(x, 16 + pick.Y - 2 + (IconHeight + 4) * row, IconWidth + 4, IconHeight + 4);
        }
        var r = new URect(pick.X + 6, pick.Y + 5, (IconWidth + 4) * 3 - 4, 7);
        _c.SetForeground(Black);
        _c.FillRect(r);
        if (lit is not null)
        {
            _c.SetFont("tiny");
            _c.SetForeground(CaptainColor);
            _c.Text(r.X + (r.W >> 1), r.Y + (r.H - 1), TextAlign.Center, lit.Captain);
            _c.SetForeground(Black);
        }
        r.X += (IconWidth + 4) * ((Columns >> 1) + 1) + FlagshipWidth - IconWidth;
        _c.FillRect(r);
        if (lit is { Crew: > 0 })
        {
            string text = lit.Crew >= lit.MaxCrew ? lit.Crew.ToString(CultureInfo.InvariantCulture) : $"{lit.Crew}/{lit.MaxCrew}";
            _c.SetForeground(CrewColor);
            _c.Text(r.X + (r.W >> 1), r.Y + (r.H - 1), TextAlign.Center, text);
        }
        Flash = new URect(SpaceWindow.X + flash.X, SpaceWindow.Y + flash.Y, flash.W, flash.H);
        _c.SetContext(old);
    }
}

/// <summary>
/// The roster as UQM draws it in the status area (stage 4b item 10;
/// roster.c), over the status area with the fleet's ships round the
/// flagship (DeltaSISGauges' DrawSupportShips): the lit ship flashing on
/// the clock (flashSupportShip, a fifteenth of a second a step), its crew
/// in the status message, drawn white while its crew is changed, the
/// flagship's crew gauge as crew moves. Each call is what DoModifyRoster
/// draws for a key or a pass, given the roster's state.
/// </summary>
public sealed class RosterDrawing
{
    /// <summary>ONE_SECOND / 15 in UQM's ticks: flashSupportShip's step.</summary>
    public const long FlashTicks = 840 / 15;

    private static readonly Rgb Modify = new(255, 255, 255);   // ROSTER_MODIFY_SHIP_COLOR: WHITE_COLOR

    private readonly UqmCanvas _c;
    private readonly StarbaseScreens _screens;
    private readonly Func<int, YardRace?> _races;
    // flashSupportShip's statics: its colour and when it next steps.
    private Rgb _color = new(0xFF, 0x00, 0x00);
    private long _next;
    private (int X, int Y) _lit;
    private string? _litIcons;

    /// <param name="screens">The status area's drawing (the message, the gauges), on the same canvas, with the ship and fleet as they stand.</param>
    public RosterDrawing(UqmCanvas canvas, StarbaseScreens screens, Func<int, YardRace?> races)
    {
        _c = canvas;
        _screens = screens;
        _races = races;
    }

    /// <summary>Where the flashing highlight is (flashSupportShipCrew: the status message while the crew is changed), in the screen's pixels.</summary>
    public URect? Flash { get; private set; }

    private IReadOnlyList<HangarShip> Fleet => _screens.Ship.Fleet ?? Array.Empty<HangarShip>();

    // The fleet ship at the point (LockSupportShip: its place in the queue by SUPPORT_SHIP_PTS), or null.
    private HangarShip? ShipAt((int X, int Y) point)
    {
        int i = Array.IndexOf(StarbaseScreens.SupportShipPoints, point);
        return i >= 0 && i < Fleet.Count ? Fleet[i] : null;
    }

    // drawSupportShip: the lit ship's icon, plain or filled in the colour set.
    private void DrawLit(bool filled)
    {
        if (_litIcons is not { } icons)
            return;
        if (filled)
            _c.FilledStamp(icons, 0, _lit.X, _lit.Y);
        else
            _c.Stamp(icons, 0, _lit.X, _lit.Y);
    }

    // DeltaSupportCrew's message: the lit ship's crew, its race's starting crew if full, crew/starting if not.
    private void CrewMessage()
    {
        if (ShipAt(_lit) is not { } ship)
            return;
        int most = _races(ship.Race)?.Crew ?? 0;
        StarbaseScreens.StatusMessage(_c, ship.Crew >= most ? ship.Crew.ToString(CultureInfo.InvariantCulture) : $"{ship.Crew}/{most}");
    }

    // selectSupportShip: the point lit, its icon, its crew in the message (DeltaSupportCrew (0): no gauge, no flash).
    private void Select((int X, int Y) point)
    {
        _lit = point;
        _litIcons = ShipAt(point) is { } ship ? _races(ship.Race)?.Icons : null;
        CrewMessage();
    }

    /// <summary>RosterMenu's opening (in StatusContext): the cursor's ship lit, its crew in the message.</summary>
    public void Open(RosterMenu menu)
    {
        var old = _c.SetContext(_c.Status);
        Flash = null;
        Select(menu.Point);
        _c.SetContext(old);
    }

    /// <summary>
    /// What DoModifyRoster draws for a key: <paramref name="was"/> the
    /// cursor's place and crew mode before it, <paramref name="outcome"/>
    /// what the menu did (<see cref="RosterMenu.Press"/>), the ship and
    /// fleet as the key left them (the screens' <see cref="StarbaseScreens.Ship"/>),
    /// at <paramref name="now"/> (UQM's ticks).
    /// </summary>
    public void Key(RosterMenu menu, (int State, bool Modifying) was, MenuKey key, MenuOutcome outcome, int crewMoved, long now)
    {
        var old = _c.SetContext(_c.Status);
        if (menu.Left)
            DrawLit(filled: false);   // RosterMenu's tail: the last ship unlit
        else if (key is MenuKey.Select or MenuKey.Cancel)
        {
            if (!menu.ModifyingCrew)
                Flash = null;
            else
            {   // drawModifiedSupportShip, flashSupportShipCrew
                _c.SetForeground(Modify);
                DrawLit(filled: true);
                Flash = StarbaseScreens.StatusToScreen(StarbaseScreens.StatusMessageRect);
            }
        }
        else if (was.Modifying)
        {
            if (outcome == MenuOutcome.Moved && crewMoved != 0)
            {   // DeltaSupportCrew: the message, the flagship's crew gauge, the flash on the message
                CrewMessage();
                _screens.Apply(new ScreenOp(ScreenOpKind.Gauges, -crewMoved));
                Flash = StarbaseScreens.StatusToScreen(StarbaseScreens.StatusMessageRect);
            }
        }
        else
        {
            if (menu.State != was.State)
            {
                DrawLit(filled: false);
                Select(menu.Point);
            }
            FlashLit(now);
        }
        _c.SetContext(old);
    }

    /// <summary>DoModifyRoster's pass with no key at <paramref name="now"/>: the lit ship's flash, while the crew isn't being changed.</summary>
    public void Tick(RosterMenu menu, long now)
    {
        if (menu.Left || menu.ModifyingCrew)
            return;
        var old = _c.SetContext(_c.Status);
        FlashLit(now);
        _c.SetContext(old);
    }

    // flashSupportShip: every fifteenth of a second the lit ship filled in the next colour, red whitening to pink,
    // then red again (8-bit steps, as UQM's 24-bit code makes them).
    private void FlashLit(long now)
    {
        if (now < _next)
            return;
        _next = now + FlashTicks;
        const int five = (0x19 << 3) | (0x19 >> 2), step = (0x02 << 3) | (0x02 >> 2);   // CC5TO8 (0x19), CC5TO8 (0x02)
        _color = _color.G >= five ? new Rgb(0xFF, 0x00, 0x00) : new Rgb(_color.R, (byte)(_color.G + step), (byte)(_color.B + step));
        _c.SetForeground(_color);
        DrawLit(filled: true);
    }
}
