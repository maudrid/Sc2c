// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoStarBase,
// shipyard.c's DoShipyard and DoModifyShips for the flagship's crew),
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

    /// <summary>DoModifyShips navigating: the cursor on the flagship or one of the hangar's slots.</summary>
    Ships,

    /// <summary>DoModifyShips editing the flagship's crew: up hires, down sends away.</summary>
    Crew,
}

/// <summary>
/// The shipyard's screen, for the player's own ship, as UQM's keys move
/// round it: DoShipyard's menu (Crew, Save/Load, Exit); on Crew,
/// DoModifyShips with the cursor on the flagship, moved round the
/// hangar's twelve slots and back (DMS_MoveCursor); select on the
/// flagship edits its crew (up hires one, down sends one away,
/// DMS_ModifyCrew; select or cancel goes back to the cursor), and cancel
/// goes back to the menu. The escort fleet's ships come with combat
/// (stage 4): select on a slot is refused, where UQM would open the list
/// of ships to build (docs/differences.md row 44).
/// </summary>
public sealed class ShipyardMenu
{
    /// <summary>SHIPYARD_CREW, SHIPYARD_SAVELOAD and SHIPYARD_EXIT.</summary>
    public const byte Crew = 0, SaveLoad = 1, Exit = 2;

    /// <summary>HANGAR_SHIPS_ROW and HANGAR_ROWS: the PC hangar's 6 by 2 slots.</summary>
    public const int HangarShipsRow = 6, HangarRows = 2;

    /// <summary>DoModifyShips' CurState for the flagship: MAKE_BYTE (0, 0xF).</summary>
    public const byte Flagship = 0xF0;

    private readonly Outfitting _rules;
    private readonly Func<OutfitAction, bool> _act;
    private readonly List<ScreenOp> _ops = new();

    /// <summary>The shipyard opened (DoShipyard's first drawing).</summary>
    /// <param name="rules">The player's ship and the prices, as this side sees them.</param>
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

    public YardMode Mode { get; private set; } = YardMode.Menu;

    /// <summary>
    /// DoModifyShips' cursor: a hangar slot (0 to 11), or the flagship
    /// (the high nibble 0xF; the low one keeps the column it came from).
    /// </summary>
    public byte Cursor { get; private set; } = Flagship;

    /// <summary>Whether <paramref name="cursor"/> is on the flagship (HINIBBLE (CurState) != 0).</summary>
    public static bool OnFlagship(byte cursor) => cursor >> 4 != 0;

    /// <summary>Changing the crew (DoModifyShips' crew mode on the flagship).</summary>
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
                    case MenuKey.Up or MenuKey.Down:
                        return ModifyCrew(key == MenuKey.Up);
                    case MenuKey.Select or MenuKey.Cancel:
                        Mode = YardMode.Ships;
                        break;
                }
                return OutfitOutcome.None;
            case YardMode.Ships:
                switch (key)
                {
                    case MenuKey.Select when OnFlagship(Cursor):
                        Mode = YardMode.Crew;
                        return OutfitOutcome.None;
                    case MenuKey.Select:
                        return OutfitOutcome.Refused;   // building fleet ships: stage 4
                    case MenuKey.Cancel:
                        (Mode, State) = (YardMode.Menu, Crew);
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
            (Mode, Cursor) = (YardMode.Ships, Flagship);
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

    /// <summary>A key's drawing (none: nothing kept), with the ship as the key left it (null: the drawing doesn't read it).</summary>
    internal void Drew(StarbaseShip? ship, List<ScreenOp> ops)
    {
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
        Yard?.State ?? 0, Yard?.Mode ?? YardMode.Menu, Yard?.Cursor ?? ShipyardMenu.Flagship, Busy ?? "", Refusals);
}

/// <summary>
/// A docked player's starbase screen as their client is sent it (a
/// reliable message when it changes): the screen, the menu's cursor,
/// outfitting's cursor (<see cref="OutfitMenu.State"/> and
/// <see cref="OutfitMenu.Item"/>, UQM's numbers), the shipyard's (its
/// menu, its mode and DoModifyShips' cursor), who's talking to the
/// commander if Talk was refused, and the refusals so far.
/// </summary>
public sealed record StarbaseView(StarbaseScreen Screen, byte State, byte OutfitState, int OutfitItem, bool Installing,
    byte YardState, YardMode YardMode, byte YardCursor, string Busy, int Refusals)
{
    /// <summary>Changing the flagship's crew in the shipyard.</summary>
    public bool CrewMode => YardMode == YardMode.Crew;

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
        // Outfitting's slot within its row (the screen indexes the row's slots by it); off Modules, the menu's
        // entries; the shipyard's cursor a hangar slot or the flagship (its column kept in the low nibble).
        int slots = installing == 1 && outfitState <= World.ShipModules.EmptyLander
            ? OutfitMenu.SlotCount(OutfitMenu.RowOf((byte)outfitState)) : World.ShipModules.ModuleSlotCount;
        bool cursorOk = yardCursor >> 4 == 0 ? yardCursor < ShipyardMenu.HangarShipsRow * ShipyardMenu.HangarRows
            : yardCursor >> 4 == 0xF && (yardCursor & 0xF) < ShipyardMenu.HangarShipsRow;
        if (state > StarbaseMenu.DepartBase || outfitState > World.ShipModules.EmptyLander || outfitItem >= (ulong)slots
                || installing > 1 || (installing == 0 && outfitState > OutfitMenu.DoFuel) || yardState > ShipyardMenu.Exit
                || yardMode > (ulong)YardMode.Crew || !cursorOk || refusals > int.MaxValue)
            throw new InvalidDataException("Bad starbase screen state.");
        if (!r.AtEnd)
            throw new InvalidDataException("A starbase screen has bytes after its end.");
        return new StarbaseView((StarbaseScreen)(int)(screen - 1), (byte)state, (byte)outfitState, (int)outfitItem,
            installing == 1, (byte)yardState, (YardMode)(int)yardMode, (byte)yardCursor, busy, (int)refusals);
    }
}
