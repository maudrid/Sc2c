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
/// flagship's crew gauge as crew moves. It draws the roster's steps as its
/// keys took them (<see cref="Apply"/>: <see cref="RosterEvent"/>, recorded
/// by <see cref="RosterMenu"/>), each with the crews it left (review rounds
/// 3 and 4), and the clock's passes (<see cref="Tick"/>).
/// </summary>
public sealed class RosterDrawing
{
    /// <summary>ONE_SECOND / 15 in UQM's ticks: flashSupportShip's step.</summary>
    public const long FlashTicks = 840 / 15;

    private static readonly Rgb Modify = new(255, 255, 255);   // ROSTER_MODIFY_SHIP_COLOR: WHITE_COLOR

    private readonly UqmCanvas _c;
    private readonly StarbaseScreens _screens;
    private readonly Func<int, YardRace?> _races;
    private readonly RosterFlash _flash;
    private (int X, int Y) _lit;
    private string? _litIcons;
    private bool _modifying;

    /// <param name="screens">The status area's drawing (the message, the gauges), on the same canvas, with the ship and fleet as they stand.</param>
    /// <param name="flash">flashSupportShip's statics, kept from one opening to the next as UQM's are (none: afresh, as a new game's).</param>
    public RosterDrawing(UqmCanvas canvas, StarbaseScreens screens, Func<int, YardRace?> races, RosterFlash? flash = null)
    {
        _c = canvas;
        _screens = screens;
        _races = races;
        _flash = flash ?? new RosterFlash();
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
    private void CrewMessage(int? crewThen = null)
    {
        if (ShipAt(_lit) is not { } at)
            return;
        var ship = crewThen is int c ? at with { Crew = c } : at;
        int most = _races(ship.Race)?.Crew ?? 0;
        StarbaseScreens.StatusMessage(_c, ship.Crew >= most ? ship.Crew.ToString(CultureInfo.InvariantCulture) : $"{ship.Crew}/{most}");
    }

    // selectSupportShip: the point lit, its icon, its crew in the message (DeltaSupportCrew (0): no gauge, no flash).
    private void Select((int X, int Y) point, int? crewThen = null)
    {
        _lit = point;
        _litIcons = ShipAt(point) is { } ship ? _races(ship.Race)?.Icons : null;
        CrewMessage(crewThen);
    }

    /// <summary>RosterMenu's opening (in StatusContext): the cursor's ship lit, its crew in the message; in the crew mode already (drawn afresh mid-edit), white and the message flashing.</summary>
    public void Open(RosterMenu menu)
    {
        var old = _c.SetContext(_c.Status);
        Flash = null;
        Select(menu.Point);
        _modifying = menu.ModifyingCrew;
        if (_modifying)
            ModeOn();
        _c.SetContext(old);
    }

    // drawModifiedSupportShip, flashSupportShipCrew.
    private void ModeOn()
    {
        _c.SetForeground(Modify);
        DrawLit(filled: true);
        Flash = StarbaseScreens.StatusToScreen(StarbaseScreens.StatusMessageRect);
    }

    /// <summary>
    /// What DoModifyRoster drew for one of its steps, at <paramref name="now"/>
    /// (UQM's ticks): the cursor's (the old ship unlit, the new lit with
    /// its crew, then flashSupportShip's turn), the crew mode on (the ship
    /// white, the message flashing) or off, a crew member moved (the
    /// message, the flagship's crew gauge, the message flashing), or the
    /// roster left (the ship unlit).
    /// </summary>
    public void Apply(RosterEvent e, long now)
    {
        var old = _c.SetContext(_c.Status);
        switch (e.Step)
        {
            case RosterStep.Left:
                DrawLit(filled: false);   // RosterMenu's tail: the last ship unlit
                break;
            case RosterStep.ModeOn:
                _modifying = true;
                ModeOn();
                break;
            case RosterStep.ModeOff:
                _modifying = false;
                Flash = null;
                break;
            case RosterStep.CrewMoved:
                // The ship's crew and the flagship's as the move left them (the gauge draws the flagship's as it stands).
                CrewMessage(e.Crew);
                _screens.Ship = _screens.Ship with { Crew = e.FlagshipCrew };
                _screens.Apply(new ScreenOp(ScreenOpKind.Gauges, -e.Delta));
                Flash = StarbaseScreens.StatusToScreen(StarbaseScreens.StatusMessageRect);
                break;
            case RosterStep.Navigated:
                if (e.To != e.From)
                {
                    DrawLit(filled: false);
                    Select(e.To, e.Ship >= 0 ? e.Crew : null);   // its crew as the key found it
                }
                FlashLit(now);
                break;
        }
        _c.SetContext(old);
    }

    /// <summary>DoModifyRoster's pass with no key at <paramref name="now"/>: the lit ship's flash, while the crew isn't being changed.</summary>
    public void Tick(long now)
    {
        if (_modifying)
            return;
        var old = _c.SetContext(_c.Status);
        FlashLit(now);
        _c.SetContext(old);
    }

    // flashSupportShip: every fifteenth of a second the lit ship filled in the next colour, red whitening to pink,
    // then red again (8-bit steps, as UQM's 24-bit code makes them).
    private void FlashLit(long now)
    {
        if (now < _flash.Next)
            return;
        _flash.Next = now + FlashTicks;
        const int five = (0x19 << 3) | (0x19 >> 2), step = (0x02 << 3) | (0x02 >> 2);   // CC5TO8 (0x19), CC5TO8 (0x02)
        var c = _flash.Color;
        _flash.Color = c.G >= five ? new Rgb(0xFF, 0x00, 0x00) : new Rgb(c.R, (byte)(c.G + step), (byte)(c.B + step));
        _c.SetForeground(_flash.Color);
        DrawLit(filled: true);
    }
}

/// <summary>flashSupportShip's static colour and the time it next steps (roster.c), which outlive a roster's opening.</summary>
public sealed class RosterFlash
{
    public Rgb Color = new(0xFF, 0x00, 0x00);
    public long Next;
}

/// <summary>
/// The roster as the host's client follows it (stage 4b item 10; the game's
/// roster panel's logic, here to be tested). The roster is the host's own
/// and the host's world is local, so it's drawn from the same live source
/// the roster acts on (review round 4: <see cref="IRosterFleet"/>, the
/// fleet's ships and crews and the flagship's crew as they are, not the
/// snapshot's, which comes a world step later): each frame the open
/// roster's steps since the last (<see cref="RosterMenu.TakeEvents"/>,
/// recorded as each key was taken, each with the crews it left) are drawn
/// in order (<see cref="RosterDrawing.Apply"/>), then the clock's pass. As
/// the roster opens (afresh too: a talk changing the fleet's ships reopens
/// it) or the fleet's ships change, the status area is drawn afresh as
/// things are, and the steps not drawn yet are dropped: the fresh drawing
/// shows what they did, and they mustn't be drawn against another fleet.
/// </summary>
public sealed class RosterFollower
{
    private readonly Func<(UqmCanvas Canvas, StarbaseScreens Screens)?> _newArea;
    private readonly Func<int, YardRace?> _races;
    private readonly RosterFlash _flash = new();
    private RosterMenu? _menu;
    private readonly List<(int Slot, string Race, byte Captain)> _ships = new();
    private StarbaseScreens? _screens;
    private RosterDrawing? _drawing;

    private readonly Func<string, int> _raceIndex;

    /// <param name="newArea">A status area of its own to draw on: a canvas and the screens' drawing on it.</param>
    /// <param name="raceIndex">A race's number in avail_race_q, by its name (the fleet's ships' races).</param>
    public RosterFollower(Func<(UqmCanvas, StarbaseScreens)?> newArea, Func<int, YardRace?> races, Func<string, int> raceIndex)
    {
        _newArea = newArea;
        _races = races;
        _raceIndex = raceIndex;
    }

    /// <summary>Whether a roster is shown.</summary>
    public bool Open => _menu is not null;

    /// <summary>
    /// The host's ship as the status area draws it: from the snapshot what
    /// the roster doesn't change (its names as <see cref="Crateris.Core.World.ShipNames"/>
    /// gives them, UQM's for a flagship never renamed; its modules, fuel,
    /// landers and hold), and from <paramref name="fleet"/> the live crews:
    /// the flagship's and each fleet ship's.
    /// </summary>
    public static SisState Sis(Crateris.Core.World.ShipSnapshot host, IRosterFleet fleet, Crateris.Core.Content.GameStrings strings,
        Func<string, int> raceIndex)
    {
        var modules = Crateris.Core.World.ShipModules.Restore(host.Modules!);
        var hold = host.Hold!;
        const Crateris.Core.World.LanderUpgrades shields = Crateris.Core.World.LanderUpgrades.BioShield
            | Crateris.Core.World.LanderUpgrades.QuakeShield | Crateris.Core.World.LanderUpgrades.LightningShield
            | Crateris.Core.World.LanderUpgrades.LavaShield;
        return new SisState(modules.ModuleSlots.ToArray(), modules.DriveSlots.ToArray(), modules.JetSlots.ToArray(), fleet.FlagshipCrew,
            host.Fuel, hold.Landers, hold.ResourceUnits, hold.MineralMass, Crateris.Core.World.ShipNames.Ship(host, strings),
            Crateris.Core.World.ShipNames.Captain(host, strings), 0, (int)(hold.Upgrades & shields), 0, 0, _ => 0, 50,
            Live(fleet, raceIndex));
    }

    /// <summary>The fleet's ships with their crews as they are, as the status area draws them.</summary>
    public static HangarShip[] Live(IRosterFleet fleet, Func<string, int> raceIndex) =>
        fleet.Ships.Select(s => new HangarShip(s.Slot, raceIndex(s.Race), s.Crew)).ToArray();

    /// <summary>Whether the last update drew the status area afresh (on a new canvas: paint it on a clean picture).</summary>
    public bool Fresh { get; private set; }

    /// <summary>The canvas the roster draws on (its calls taken by whoever paints them).</summary>
    public UqmCanvas? Canvas { get; private set; }

    /// <summary>Where the highlight flashes, in the screen's pixels.</summary>
    public URect? Flash => _menu is null ? null : _drawing?.Flash;

    // Whether the fleet's ships (each one's slot, race and captain, which tell them apart) are those drawn.
    private bool SameShips(IRosterFleet fleet)
    {
        var ships = fleet.Ships;
        if (ships.Count != _ships.Count)
            return false;
        for (int i = 0; i < ships.Count; ++i)
            if (_ships[i] != (ships[i].Slot, ships[i].Race, ships[i].Captain))
                return false;
        return true;
    }

    /// <summary>
    /// A frame: the roster as it stands (null when closed) over the host's
    /// ship (<paramref name="sis"/>: what the snapshot gives, made only when
    /// the area is drawn afresh; its crews and fleet taken from the live
    /// fleet), the fleet as it is
    /// (<paramref name="fleet"/>, which the roster acts on), at
    /// <paramref name="now"/>.
    /// </summary>
    public void Update(RosterMenu? menu, IRosterFleet? fleet, Func<SisState?> sis, long now)
    {
        Fresh = false;
        if (menu is null || menu.Left || fleet is null)
        {
            _menu = null;
            return;
        }
        if (!ReferenceEquals(menu, _menu) || !SameShips(fleet) || _drawing is null)
        {
            if (sis() is not { } ship || _newArea() is not { } area)
                return;   // nothing to draw on yet: the steps wait
            menu.TakeEvents();   // what they did is drawn afresh below, against the fleet as it is
            (Canvas, _screens) = area;
            _drawing = new RosterDrawing(Canvas, _screens, _races, _flash);
            _screens.Ship = ship with { Crew = fleet.FlagshipCrew, Fleet = Live(fleet, _raceIndex) };   // the crews as they are
            // The status area as the roster opens over it: the flagship and the fleet's ships (DeltaSISGauges).
            _screens.Apply(new ScreenOp(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined));
            _drawing.Open(menu);
            _menu = menu;
            _ships.Clear();
            foreach (var s in fleet.Ships)
                _ships.Add((s.Slot, s.Race, s.Captain));
            Fresh = true;
            return;
        }
        foreach (var e in menu.TakeEvents())
            _drawing.Apply(e, now);
        _drawing.Tick(now);
    }
}

/// <summary>
/// The battle's looks a client takes from the snapshot and the content
/// (stage 4b item 10, review round 1): a ship's look in the status panel
/// (<see cref="StatusShip"/>) and the pick screen's (<see cref="IPickLook"/>).
/// A player's flagship goes by its ship's and captain's names as
/// <see cref="Crateris.Core.World.ShipNames"/> gives them, UQM's own for a
/// flagship never renamed (GLOBAL_SIS's ShipName and CommanderName, which
/// status.c's panel and pickship.c show), as the starbase's screens do; a
/// race ship by its race's strings.
/// </summary>
public sealed class BattleLooks : IPickLook
{
    private readonly Crateris.Core.Combat.ShipDescriptions _ships;
    private readonly Crateris.Core.Content.GameStrings _strings;
    private readonly Func<Crateris.Core.Combat.ShipDescription, int, string> _raceString;

    /// <param name="raceString">A race's string by its index (its race strings' table).</param>
    public BattleLooks(Crateris.Core.Combat.ShipDescriptions ships, Crateris.Core.Content.GameStrings strings,
        Func<Crateris.Core.Combat.ShipDescription, int, string> raceString)
    {
        _ships = ships;
        _strings = strings;
        _raceString = raceString;
    }

    /// <summary>The snapshot the players' ships are read from (the last received).</summary>
    public Crateris.Core.World.WorldSnapshot? Snapshot { get; set; }

    private Crateris.Core.World.ShipSnapshot? ShipOf(string? player) =>
        player is null ? null : Snapshot?.Ships.FirstOrDefault(s => s.Id == player);

    /// <summary>
    /// A ship's look in the status panel: a player's flagship (captain 0:
    /// status.c's SIS branch) by its ship's name and its captain's, any
    /// other by its race's name and its captain's.
    /// </summary>
    public StatusShip StatusShip(Crateris.Core.World.BattleShipStatus s, string? player)
    {
        var d = _ships[s.Ship];
        bool sis = player is not null && s.Captain == 0;
        var ship = ShipOf(player);
        return new StatusShip(d.Graphics.Icons, d.Graphics.Captain, s.MaxCrew, s.MaxEnergy,
            (d.Flags & Crateris.Core.Combat.ShipFlags.CrewImmune) != 0, BattleScreen.PlayerCaptain(s.Ship, d.Flags), sis,
            sis ? ShipName(player!) : _raceString(d, 1),
            sis ? CaptainName(player!) : _raceString(d, s.Captain));
    }

    private string CaptainName(string player) =>
        ShipOf(player) is { } s ? Crateris.Core.World.ShipNames.Captain(s, _strings) : "";

    /// <inheritdoc/>
    public string ShipName(string player) => ShipOf(player) is { } s ? Crateris.Core.World.ShipNames.Ship(s, _strings) : "";

    /// <inheritdoc/>
    public PickShipLit Flagship(string player)
    {
        var s = ShipOf(player);
        int room = s?.Modules is { } m ? Crateris.Core.World.ShipModules.Restore(m).CrewCapacity : 0;
        return new PickShipLit(CaptainName(player), s?.Hold?.Crew ?? 0, room);
    }

    /// <inheritdoc/>
    public string? Icons(string ship) => _ships[ship].Graphics.Icons;

    /// <inheritdoc/>
    public string Captain(string ship, int captain) => _raceString(_ships[ship], captain);
}
