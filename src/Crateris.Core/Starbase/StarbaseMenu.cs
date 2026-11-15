// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoStarBase,
// shipyard.c's DoShipyard and DoModifyShips with its DMS_ functions),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Net;
using Crateris.Core.Systems;

namespace Crateris.Core.Starbase;

/// <summary>Which of the starbase's screens a docked player is on.</summary>
public enum StarbaseScreen : byte
{
    /// <summary>DoStarBase's menu: Talk, Outfit, Shipyard, Leave.</summary>
    Menu,

    /// <summary>Talking to the commander (the talk is sent as a <see cref="Comm.TalkView"/>).</summary>
    Talk,

    /// <summary>Outfitting (<see cref="OutfitMenu"/>).</summary>
    Outfit,

    /// <summary>The shipyard (<see cref="ShipyardMenu"/>).</summary>
    Shipyard,
}

/// <summary>Where the shipyard's keys go (DoShipyard, then DoModifyShips' modes).</summary>
public enum YardMode : byte
{
    /// <summary>DoShipyard's menu: Crew, Save/Load, Exit.</summary>
    Menu,

    /// <summary>DoModifyShips navigating (DMS_Mode_navigate): the cursor on the flagship or one of the hangar's slots.</summary>
    Ships,

    /// <summary>
    /// DoModifyShips editing a ship's crew (DMS_Mode_editCrew): the
    /// flagship's, or the host's fleet ship's under the cursor; up hires,
    /// down sends away.
    /// </summary>
    Crew,

    /// <summary>
    /// DoModifyShips choosing a ship to build into the empty slot under the
    /// cursor (DMS_Mode_addEscort): the allied races' ships, the host's only.
    /// Guests never have it; the wire carries it with the race lit
    /// (<see cref="StarbaseView.YardBuildRace"/>) since stage 4b item 9, so
    /// a client of the host's state can draw the host's shipyard.
    /// </summary>
    Build,
}

/// <summary>
/// The shipyard's screen, as UQM's keys move round it: DoShipyard's menu
/// (Crew, Save/Load, Exit); on Crew, DoModifyShips with the cursor on the
/// flagship, moved round the hangar's twelve slots and back
/// (DMS_MoveCursor); select on the flagship edits its crew (up hires one,
/// down sends one away, DMS_ModifyCrew; select or cancel goes back to the
/// cursor), and cancel goes back to the menu. For the host, whose fleet
/// it is (stage 4b, decision 3), select on an empty slot opens the list of
/// the allied races' ships to build (DMS_AddEscortShip: the arrows go
/// round it, select builds, cancel goes back), and select on a fleet ship
/// (or building one) edits its crew (DMS_EditCrewMode), a ship left at no
/// crew scrapped as it ends (DMS_ScrapEscortShip). Whether a slot is being
/// built into or crewed follows the fleet, as UQM's does
/// (GetEscortByStarShipIndex at each key: a ship a talk puts in the slot
/// meanwhile is crewed). A guest has no fleet: select on a slot is refused.
/// </summary>
public sealed class ShipyardMenu
{
    /// <summary>SHIPYARD_CREW, SHIPYARD_SAVELOAD and SHIPYARD_EXIT.</summary>
    public const byte Crew = 0, SaveLoad = 1, Exit = 2;

    /// <summary>HANGAR_SHIPS_ROW and HANGAR_ROWS: the PC hangar's 6 by 2 slots.</summary>
    public const int HangarShipsRow = 6, HangarRows = 2;

    /// <summary>DoModifyShips' CurState for the flagship: MAKE_BYTE (0, 0xF).</summary>
    public const byte Flagship = 0xF0;

    /// <summary>MODIFY_CREW_FLAG: delta_item's flag for building or crewing.</summary>
    public const int ModifyCrewFlag = 1 << 8;

    private readonly Outfitting _rules;
    private readonly Func<OutfitAction, bool> _act;
    private readonly List<ScreenOp> _ops = new();
    private bool _inMenu = true;
    private bool _modify;

    /// <summary>The shipyard opened (DoShipyard's first drawing).</summary>
    /// <param name="rules">The player's ship, the host's fleet for the host, and the prices, as this side sees them.</param>
    /// <param name="act">Does an action (sends it, and applies it here); false if refused.</param>
    public ShipyardMenu(Outfitting rules, Func<OutfitAction, bool> act)
    {
        _rules = rules;
        _act = act;
        Draw(ScreenOpKind.YardOpened);
    }

    /// <summary>What the screen draws for the keys so far, in order, at UQM's points (taken by <see cref="TakeOps"/>).</summary>
    public IReadOnlyList<ScreenOp> Ops => _ops;

    /// <summary>The drawing since the last take.</summary>
    public List<ScreenOp> TakeOps()
    {
        var ops = new List<ScreenOp>(_ops);
        _ops.Clear();
        return ops;
    }

    private void Draw(ScreenOpKind kind, int a = 0, int b = 0, int c = 0) => _ops.Add(new ScreenOp(kind, a, b, c));

    /// <summary>
    /// The shipyard as it stands, drawn afresh (when drawing was lost): as
    /// it opens, the status area drawn afresh (the opening doesn't draw its
    /// gauges), then the menu on its entry.
    /// </summary>
    public List<ScreenOp> Redraw() => new()
    {
        new(ScreenOpKind.YardOpened), new(ScreenOpKind.SisDisplay), new(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, State),
    };

    /// <summary>The menu's cursor.</summary>
    public byte State { get; private set; } = Crew;

    /// <summary>
    /// Where the keys go: the menu; or DoModifyShips' mode, which, as
    /// UQM's, follows MODIFY_CREW_FLAG and the slot under the cursor (with
    /// the flag, an empty slot is built into, a ship crewed).
    /// </summary>
    public YardMode Mode => _inMenu ? YardMode.Menu
        : !_modify ? YardMode.Ships
        : OnFlagship(Cursor) || _rules.Fleet?.InSlot(Cursor) is not null ? YardMode.Crew
        : YardMode.Build;

    /// <summary>
    /// DoModifyShips' cursor: a hangar slot (0 to 11), or the flagship
    /// (the high nibble 0xF; the low one keeps the column it came from).
    /// </summary>
    public byte Cursor { get; private set; } = Flagship;

    /// <summary>
    /// The ship lit in the list to build (delta_item's low byte, an index
    /// into <see cref="Outfitting.BuildList"/>; kept, as UQM keeps it, until
    /// the list opens again, the hangar opens from the menu, or a fleet
    /// ship's crew is changed, refused or not).
    /// </summary>
    public byte RaceItem { get; private set; }

    /// <summary>The race whose ship <see cref="RaceItem"/> lights (null past the list's end).</summary>
    public string? BuildRace => _rules.BuildList is var list && RaceItem < list.Count ? list[RaceItem] : null;

    /// <summary>UQM's delta_item: <see cref="RaceItem"/> with <see cref="ModifyCrewFlag"/> while building or crewing.</summary>
    public int Item => (_modify ? ModifyCrewFlag : 0) | RaceItem;

    /// <summary>The hangar slot whose fleet ship's crew is being changed; null if none.</summary>
    public int? EditingSlot => Mode == YardMode.Crew && !OnFlagship(Cursor) ? Cursor : null;

    /// <summary>Whether <paramref name="cursor"/> is on the flagship (HINIBBLE (CurState) != 0).</summary>
    public static bool OnFlagship(byte cursor) => cursor >> 4 != 0;

    /// <summary>Changing a ship's crew (DoModifyShips' crew mode).</summary>
    public bool CrewMode => Mode == YardMode.Crew;

    /// <summary>
    /// A key, as DoShipyard or DoModifyShips takes it. Save/Load is
    /// refused, drawing nothing, where <paramref name="canSave"/> is false
    /// (a guest: only the host saves).
    /// </summary>
    public OutfitOutcome Key(MenuKey key, bool canSave = true)
    {
        switch (Mode)
        {
            case YardMode.Crew:
                switch (key)
                {
                    case MenuKey.Up or MenuKey.Down when OnFlagship(Cursor):
                        return ModifyCrew(key == MenuKey.Up);
                    case MenuKey.Up or MenuKey.Down:
                        return ModifyShipCrew(key == MenuKey.Up);
                    case MenuKey.Select or MenuKey.Cancel:
                        // DMS_EditCrewMode: a fleet ship left with no crew is scrapped as the editing ends (DMS_ScrapEscortShip:
                        // its hangar's doors closing on it, then the SIS display refreshed).
                        if (!OnFlagship(Cursor) && _rules.Fleet?.InSlot(Cursor) is { Crew: 0 } scrapped && _act(new ScrapShip(Cursor)))
                        {
                            Draw(ScreenOpKind.ShipScrapped, Cursor, RaceNumber(scrapped.Race));
                            Draw(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined, ScreenOp.Undefined);
                        }
                        _modify = false;
                        break;
                }
                return OutfitOutcome.None;
            case YardMode.Build:
                return AddShip(key);
            case YardMode.Ships:
                switch (key)
                {
                    case MenuKey.Select when OnFlagship(Cursor):
                        _modify = true;
                        return OutfitOutcome.None;
                    case MenuKey.Select when _rules.Fleet is null:
                        return OutfitOutcome.Refused;   // a guest has no fleet (stage 4b, decision 3)
                    case MenuKey.Select when _rules.Fleet.InSlot(Cursor) is not null:
                        _modify = true;   // the fleet ship's crew (delta_item |= MODIFY_CREW_FLAG)
                        return OutfitOutcome.None;
                    case MenuKey.Select:
                        if (_rules.BuildList.Count == 0)
                            return OutfitOutcome.Refused;   // no allied race (UQM's always has the Earthlings)
                        (_modify, RaceItem) = (true, 0);   // the list of ships to build, on its first
                        Draw(ScreenOpKind.RaceStrings, RaceNumber(_rules.BuildList[0]));
                        return OutfitOutcome.None;
                    case MenuKey.Cancel:
                        (_inMenu, State) = (true, Crew);
                        Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew);
                        return OutfitOutcome.None;
                    case MenuKey.Left:
                        Cursor = MoveCursor(Cursor, -1, 0);
                        break;
                    case MenuKey.Right:
                        Cursor = MoveCursor(Cursor, 1, 0);
                        break;
                    case MenuKey.Up:
                        Cursor = MoveCursor(Cursor, 0, -1);
                        break;
                    case MenuKey.Down:
                        Cursor = MoveCursor(Cursor, 0, 1);
                        break;
                }
                return OutfitOutcome.None;
        }
        if (key == MenuKey.Cancel || (key == MenuKey.Select && State == Exit))
            return OutfitOutcome.Left;
        if (key == MenuKey.Select)
        {
            if (State == SaveLoad)
            {
                if (!canSave)
                    return OutfitOutcome.Refused;
                // GameOptions, then the menu drawn again.
                Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, SaveLoad);
                return OutfitOutcome.SaveLoad;
            }
            // DoModifyShips, freshly: the cursor on the flagship, delta_item 0.
            (_inMenu, _modify, Cursor, RaceItem) = (false, false, Flagship, 0);
            return OutfitOutcome.None;
        }
        // DoMenuChooser (PM_CREW): three entries, wrapping round, the menu drawn on any arrow.
        if (key is MenuKey.Left or MenuKey.Up)
            State = (byte)(State == Crew ? Exit : State - 1);
        else if (key is MenuKey.Right or MenuKey.Down)
            State = (byte)(State == Exit ? Crew : State + 1);
        else
            return OutfitOutcome.None;
        Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, State);
        return OutfitOutcome.None;
    }

    // DMS_ModifyCrew for the flagship: a crew member hired (its point drawn, then the gauges) or sent away (the gauges,
    // then its point erased), and CrewTransaction's menu drawn again if the price changed.
    private OutfitOutcome ModifyCrew(bool hire)
    {
        var ship = _rules.Ship;
        var (crew, ru, cost) = (ship.Crew, ship.ResourceUnits, _rules.Story.CrewCost);
        if (!_act(hire ? new HireCrew() : new DismissCrew()))
            return OutfitOutcome.Refused;
        int ruDelta = (int)(ship.ResourceUnits - (long)ru);
        if (hire)
            Draw(ScreenOpKind.CrewPoint, 1, crew);   // the seat of the one hired
        Draw(ScreenOpKind.Gauges, ship.Crew - crew, 0, ruDelta);
        if (!hire)
            Draw(ScreenOpKind.CrewPoint, 0, ship.Crew);   // the seat of the one sent away
        if (_rules.Story.CrewCost != cost)
            Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew);
        return OutfitOutcome.Acted;
    }

    // DMS_ModifyCrew for a fleet ship: a crew member hired or sent away (the resource units' gauge, then the ship's
    // crew count, ShowShipCrew, drawn again for a dismissal refused too; and CrewTransaction's menu drawn again if the
    // price changed), and delta_item's ship to build forgotten, refused or not.
    private OutfitOutcome ModifyShipCrew(bool hire)
    {
        var (ru, cost) = (_rules.Ship.ResourceUnits, _rules.Story.CrewCost);
        bool done = _act(hire ? new HireShipCrew(Cursor) : new DismissShipCrew(Cursor));
        RaceItem = 0;
        if (!done)
        {
            if (!hire && _rules.Fleet?.InSlot(Cursor) is not null)
                Draw(ScreenOpKind.ShipCrew, Cursor);   // DMS_DismissEscortShipCrew shows the crew it couldn't send away
            return OutfitOutcome.Refused;
        }
        Draw(ScreenOpKind.Gauges, 0, 0, (int)(_rules.Ship.ResourceUnits - (long)ru));
        Draw(ScreenOpKind.ShipCrew, Cursor);
        if (_rules.Story.CrewCost != cost)
            Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew);
        return OutfitOutcome.Acted;
    }

    // DMS_AddEscortShip: cancel goes back to the cursor (the menu drawn where the list was); select builds the ship lit
    // (DMS_TryAddEscortShip: its hangar's doors opening on it, then its crew edited); the arrows go round the list (left
    // and up back, right and down on), DrawRaceStrings showing the ship lit when it changes.
    private OutfitOutcome AddShip(MenuKey key)
    {
        byte was = RaceItem;
        var outcome = AddShipKey(key);
        if (RaceItem != was && _modify && Mode == YardMode.Build && RaceItem < _rules.BuildList.Count)
            Draw(ScreenOpKind.RaceStrings, RaceNumber(_rules.BuildList[RaceItem]));
        return outcome;
    }

    private OutfitOutcome AddShipKey(MenuKey key)
    {
        var list = _rules.BuildList;
        switch (key)
        {
            case MenuKey.Cancel:
                _modify = false;
                Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew);
                return OutfitOutcome.None;
            case MenuKey.Select:
                long ru = _rules.Ship.ResourceUnits;
                if (RaceItem >= list.Count || !_act(new BuildShip(Cursor, list[RaceItem])))
                    return OutfitOutcome.Refused;
                Draw(ScreenOpKind.ShipBuilt, Cursor);
                Draw(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew);
                Draw(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined, (int)(_rules.Ship.ResourceUnits - ru));
                return OutfitOutcome.Acted;
            case MenuKey.Left or MenuKey.Up:
                RaceItem = (byte)(RaceItem == 0 ? list.Count - 1 : RaceItem - 1);
                return OutfitOutcome.None;
            case MenuKey.Right or MenuKey.Down:
                RaceItem = (byte)(RaceItem + 1 >= list.Count ? 0 : RaceItem + 1);
                return OutfitOutcome.None;
        }
        return OutfitOutcome.None;
    }

    /// <summary>
    /// Another player changed the ship in the slot under an open list of
    /// ships to build or a fleet ship's crew editing (stage 4b item 10,
    /// review round 1; a talk's gift into the empty slot, a talk taking the
    /// ship edited): the mode is left as cancel leaves it (the list: the menu
    /// drawn where it was; the crew editing: nothing drawn), back on the
    /// slot's cursor, rather than building into a slot now taken or crewing
    /// a ship nobody chose. UQM's one player can't meet this.
    /// </summary>
    internal List<ScreenOp> SlotChanged(HangarShip? was, HangarShip? now)
    {
        var ops = new List<ScreenOp>();
        if (_inMenu || !_modify || OnFlagship(Cursor) || was?.Race == now?.Race && (was is null) == (now is null))
            return ops;
        _modify = false;
        if (was is null)
            ops.Add(new(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Crew));   // the list was open: DMS_AddEscortShip's cancel
        return ops;
    }

    // A race's number in avail_race_q (races.h's order), as the drawing names it.
    private int RaceNumber(string race) => _rules.Story.Rules.RaceIndex(race);

    /// <summary>DMS_MoveCursor: the flagship is a row of its own below the hangar's; rows and columns wrap round.</summary>
    public static byte MoveCursor(byte cur, int dx, int dy)
    {
        int row = (cur & 0xF) / HangarShipsRow, col = (cur & 0xF) % HangarShipsRow;
        bool flagship = cur >> 4 != 0;
        if (dy != 0)
        {
            if (flagship)
                row = HangarRows;
            row = (row + (HangarRows + 1) + dy) % (HangarRows + 1);
            flagship = row == HangarRows;
            if (flagship)
                row = 0;
        }
        else if (dx != 0 && !flagship)
            col = (col + HangarShipsRow + dx) % HangarShipsRow;
        return (byte)((row * HangarShipsRow + col) | (flagship ? 0xF0 : 0));
    }
}

/// <summary>
/// A docked player's screens at the allied starbase (stage 3a, item 8),
/// run on the host from the keys their client sends, as talk replies are:
/// DoStarBase's menu (Talk, Outfit, Shipyard, Leave; up and left, down and
/// right move round it), outfitting and the shipyard. Each docked player
/// has their own at once; save and load are the host's alone.
/// </summary>
public sealed class StarbaseMenu
{
    /// <summary>TALK_COMMANDER, OUTFIT_STARSHIP, SHIPYARD and DEPART_BASE.</summary>
    public const byte TalkCommander = 0, OutfitStarship = 1, Shipyard = 2, DepartBase = 3;

    public StarbaseScreen Screen { get; internal set; } = StarbaseScreen.Menu;

    /// <summary>The menu's cursor.</summary>
    public byte State { get; internal set; } = TalkCommander;

    public OutfitMenu? Outfit { get; internal set; }

    public ShipyardMenu? Yard { get; internal set; }

    /// <summary>Who's talking to the commander, when Talk was refused for it (decision 8); null otherwise.</summary>
    public string? Busy { get; internal set; }

    /// <summary>How many times a key was refused (MENU_SOUND_FAILURE): the client sounds each new one.</summary>
    public int Refusals { get; internal set; }

    /// <summary>
    /// The most drawings kept for a player who isn't being sent them (no
    /// one takes them): past it they're dropped, and the next taken is the
    /// screen drawn afresh (<see cref="Redraw"/>).
    /// </summary>
    public const int MostKept = 256;

    private readonly List<StarbaseDrawing> _drawn = new();
    private bool _lost;
    private StarbaseShip? _shown;

    /// <summary>
    /// The story's version, the hold's units and minerals and the host's
    /// fleet's version when the host last looked for others' changes (null after the player's own key
    /// drew the ship: a change since can bring them back to what they were).
    /// </summary>
    internal (long Story, uint ResourceUnits, int MineralMass, long Fleet)? NoticedAt { get; set; }

    /// <summary>A key's drawing (none: nothing kept), with the ship as the key left it (null: the drawing doesn't read it).</summary>
    internal void Drew(StarbaseShip? ship, List<ScreenOp> ops)
    {
        if (ship is not null)
            (_shown, NoticedAt) = (ship, null);   // shown as the key left it: look again at the next take
        if (ops.Count == 0 || _lost)
            return;
        if (_drawn.Count >= MostKept)
        {
            _drawn.Clear();
            _lost = true;
            return;
        }
        _drawn.Add(new StarbaseDrawing(ship, ops));
    }

    /// <summary>
    /// The ship and the prices as they stand (<paramref name="now"/>),
    /// changed since last drawn by someone else (UQM has no such case:
    /// another player's gift, the crew price their purchases move, a
    /// module's price their talk sets, the Chmmr's bomb): what the screen
    /// shows of it drawn again. The bomb's state: the screen drawn afresh.
    /// Outfitting and the shipyard: the resource units' message, or
    /// with the minerals the status area, which shows the storage bays
    /// (in the shipyard, its blueprint too, which shows them as well); the
    /// shipyard's menu with the crew price; in Modules, the price of the
    /// module the radar shows. Called before the player's own keys, so a
    /// key's drawing goes over what others changed first.
    /// </summary>
    internal void Noticed(StarbaseShip now)
    {
        var was = _shown;
        _shown = now;
        if (was is null || Screen is not (StarbaseScreen.Outfit or StarbaseScreen.Shipyard))
            return;
        var ops = new List<ScreenOp>();
        bool minerals = now.MineralMass != was.MineralMass;
        var fleetWas = was.Fleet ?? Array.Empty<HangarShip>();
        var fleetNow = now.Fleet ?? Array.Empty<HangarShip>();
        bool fleet = !fleetWas.SequenceEqual(fleetNow);
        if (now.BombState != was.BombState)
            ops.AddRange(Redraw());   // the bomb shows in the landers' place, hides the shields, changes the status message
        else
        {
            // The host's fleet changed by another (stage 4b item 10: a talk's gift, the host at the shipyard): the
            // hangar's slots as they changed, a ship come or gone through its doors, its crew count drawn again.
            if (fleet && Yard is { } yard)
            {
                if (!ShipyardMenu.OnFlagship(yard.Cursor))
                {
                    int a = IndexOfSlot(fleetWas, yard.Cursor), b = IndexOfSlot(fleetNow, yard.Cursor);
                    ops.AddRange(yard.SlotChanged(a < 0 ? null : fleetWas[a], b < 0 ? null : fleetNow[b]));
                }
                ops.AddRange(HangarChanges(fleetWas, fleetNow));
            }
            if (minerals || fleet)
            {
                ops.Add(new(ScreenOpKind.SisDisplay));   // the status area shows the minerals and the fleet's ships
                if (minerals && Yard is not null)
                    ops.Add(new(ScreenOpKind.Blueprint));   // its storage bays show the minerals too
            }
            else if (now.ResourceUnits != was.ResourceUnits)
                ops.Add(new(ScreenOpKind.Gauges, 0, 0, ScreenOp.Undefined));
            if (Yard is not null && now.CrewCost != was.CrewCost)
                ops.Add(new(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, Yard.State));
            if (Outfit is { Installing: true } outfit && outfit.Shown is var piece && piece < World.ShipModules.EmptySlot
                    && now.ModuleCosts[piece] != was.ModuleCosts[piece])
                ops.Add(new(ScreenOpKind.ModuleStrings, piece));
        }
        Drew(now, ops);
    }

    /// <summary>
    /// The hangar's drawing for the fleet changing from <paramref name="was"/>
    /// to <paramref name="now"/>, slot by slot: a ship gone (or replaced by
    /// another race's) behind its doors closing, one come through them
    /// opening, a crew changed drawn again.
    /// </summary>
    public static IEnumerable<ScreenOp> HangarChanges(IReadOnlyList<HangarShip> was, IReadOnlyList<HangarShip> now)
    {
        for (int slot = 0; slot < HangarShip.MostShips; ++slot)
        {
            int a = IndexOfSlot(was, slot), b = IndexOfSlot(now, slot);
            if (a < 0 && b < 0 || a >= 0 && b >= 0 && was[a] == now[b])
                continue;
            if (a >= 0 && (b < 0 || now[b].Race != was[a].Race))
                yield return new(ScreenOpKind.ShipScrapped, slot, was[a].Race);
            if (b >= 0 && (a < 0 || now[b].Race != was[a].Race))
                yield return new(ScreenOpKind.ShipBuilt, slot);
            else if (b >= 0)
                yield return new(ScreenOpKind.ShipCrew, slot);
        }
    }

    private static int IndexOfSlot(IReadOnlyList<HangarShip> fleet, int slot)
    {
        for (int i = 0; i < fleet.Count; ++i)
            if (fleet[i].Slot == slot)
                return i;
        return -1;
    }

    /// <summary>The player's client lost its picture: the next drawing taken is the screen drawn afresh.</summary>
    internal void RedrawNext()
    {
        _drawn.Clear();
        _lost = true;
    }

    /// <summary>DoStarBase drawn afresh (docked, or back on the menu), on the menu's cursor.</summary>
    internal void Opened() => Drew(null, new List<ScreenOp> { new(ScreenOpKind.MenuOpened, State) });

    /// <summary>
    /// The drawing since the last take, in order, to send the player; the
    /// screen drawn afresh, with <paramref name="ship"/>, if some was
    /// dropped (<see cref="MostKept"/>).
    /// </summary>
    internal List<StarbaseDrawing> TakeDrawing(Func<StarbaseShip> ship)
    {
        if (_lost)
        {
            _lost = false;
            _drawn.Clear();
            var ops = Redraw();
            return ops.Count == 0 ? new() : new() { new StarbaseDrawing(ship(), ops) };
        }
        var drawn = new List<StarbaseDrawing>(_drawn);
        _drawn.Clear();
        return drawn;
    }

    /// <summary>The screen as it stands, drawn afresh (none for the commander's talk: the communication screen shows).</summary>
    public List<ScreenOp> Redraw() => Screen switch
    {
        StarbaseScreen.Menu => new() { new(ScreenOpKind.MenuOpened, State) },
        StarbaseScreen.Outfit when Outfit is not null => Outfit.Redraw(),
        StarbaseScreen.Shipyard when Yard is not null => Yard.Redraw(),
        _ => new(),
    };

    /// <summary>The screen as sent to its player.</summary>
    public StarbaseView View() => new(Screen, State, Outfit?.State ?? 0, Outfit?.Item ?? 0, Outfit?.Installing ?? false,
        Yard?.State ?? 0, Yard?.Mode ?? YardMode.Menu, Yard?.Cursor ?? ShipyardMenu.Flagship, Busy ?? "", Refusals)
    {
        YardBuildRace = Yard is { Mode: YardMode.Build } yard ? yard.BuildRace : null,
    };
}

/// <summary>
/// A docked player's starbase screen as their client is sent it (a
/// reliable message when it changes): the screen, the menu's cursor,
/// outfitting's cursor (<see cref="OutfitMenu.State"/> and
/// <see cref="OutfitMenu.Item"/>, UQM's numbers), the shipyard's (its
/// menu, its mode and DoModifyShips' cursor, and the race lit in the list
/// of ships to build), who's talking to the commander if Talk was refused,
/// and the refusals so far.
/// </summary>
public sealed record StarbaseView(StarbaseScreen Screen, byte State, byte OutfitState, int OutfitItem, bool Installing,
    byte YardState, YardMode YardMode, byte YardCursor, string Busy, int Refusals)
{
    /// <summary>Changing a ship's crew in the shipyard.</summary>
    public bool CrewMode => YardMode == YardMode.Crew;

    /// <summary>
    /// The race whose ship is lit in the shipyard's list to build
    /// (<see cref="YardMode.Build"/>: the host's own screen, as guests never
    /// build), or null (otherwise, or past the list's end). Encoded since
    /// stage 4b item 9, for a client of the host's state.
    /// </summary>
    public string? YardBuildRace { get; init; }

    public byte[] Encode()
    {
        var w = new WireWriter();
        w.UInt((ulong)Screen + 1);
        w.UInt(State);
        w.UInt(OutfitState);
        w.UInt((ulong)OutfitItem);
        w.UInt(Installing ? 1UL : 0);
        w.UInt(YardState);
        w.UInt((ulong)YardMode);
        w.UInt(YardCursor);
        w.String(Busy);
        w.UInt((ulong)Refusals);
        w.String(YardBuildRace);
        return w.Written.ToArray();
    }

    /// <summary>A view from the host; null for none (empty: not docked). A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static StarbaseView? Decode(ReadOnlySpan<byte> data)
    {
        if (Net.Protocol.IsNoView(data))
            return null;
        var r = new WireReader(data);
        ulong screen = r.UInt();
        if (screen == 0 || screen - 1 > (ulong)StarbaseScreen.Shipyard)
            throw new InvalidDataException("Bad starbase screen.");
        ulong state = r.UInt(), outfitState = r.UInt(), outfitItem = r.UInt(), installing = r.UInt();
        ulong yardState = r.UInt(), yardMode = r.UInt(), yardCursor = r.UInt();
        string busy = r.String() ?? "";
        ulong refusals = r.UInt();
        string? buildRace = r.String();
        // Outfitting's slot within its row (the screen indexes the row's slots by it); off Modules, the menu's
        // entries; the shipyard's cursor a hangar slot or the flagship (its column kept in the low nibble); building
        // into a hangar slot, the race lit (if any) a race's, and none lit otherwise.
        int slots = installing == 1 && outfitState <= World.ShipModules.EmptyLander
            ? OutfitMenu.SlotCount(OutfitMenu.RowOf((byte)outfitState)) : World.ShipModules.ModuleSlotCount;
        bool cursorOk = yardCursor >> 4 == 0 ? yardCursor < ShipyardMenu.HangarShipsRow * ShipyardMenu.HangarRows
            : yardCursor >> 4 == 0xF && (yardCursor & 0xF) < ShipyardMenu.HangarShipsRow;
        if (state > StarbaseMenu.DepartBase || outfitState > World.ShipModules.EmptyLander || outfitItem >= (ulong)slots
                || installing > 1 || (installing == 0 && outfitState > OutfitMenu.DoFuel) || yardState > ShipyardMenu.Exit
                || yardMode > (ulong)YardMode.Build || !cursorOk || refusals > int.MaxValue
                || (yardMode == (ulong)YardMode.Build ? ShipyardMenu.OnFlagship((byte)yardCursor) || buildRace is { Length: 0 }
                    : buildRace is not null))
            throw new InvalidDataException("Bad starbase screen state.");
        if (!r.AtEnd)
            throw new InvalidDataException("A starbase screen has bytes after its end.");
        return new StarbaseView((StarbaseScreen)(int)(screen - 1), (byte)state, (byte)outfitState, (int)outfitItem,
            installing == 1, (byte)yardState, (YardMode)(int)yardMode, (byte)yardCursor, busy, (int)refusals)
        {
            YardBuildRace = buildRace,
        };
    }
}
