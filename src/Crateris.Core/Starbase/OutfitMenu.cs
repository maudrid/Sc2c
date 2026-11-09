// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/outfit.c's DoOutfit and
// DoInstallModule; menu.c's DoMenuChooser for PM_FUEL), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Starbase;

/// <summary>What a key on the outfitting screen did.</summary>
public enum OutfitOutcome
{
    /// <summary>The cursor moved, or nothing happened.</summary>
    None,

    /// <summary>UQM refuses this here (MENU_SOUND_FAILURE): too dear, no room, needed, or not an escort's.</summary>
    Refused,

    /// <summary>An action was applied (bought or sold).</summary>
    Acted,

    /// <summary>Save and load: the host's own (only the host has them).</summary>
    SaveLoad,

    /// <summary>Outfitting is left: back to the starbase's menu.</summary>
    Left,
}

/// <summary>
/// The outfitting screen's cursor, as UQM's DoOutfit and DoInstallModule
/// move it (stage 3a, item 8), with UQM's own numbers for where it is
/// (<see cref="State"/>, <see cref="Item"/>). The menu: Fuel, Modules,
/// Save/Load and Exit; in Fuel, up and down buy and sell a unit, page up
/// and down a bar. In Modules, up and down choose the row (the drives,
/// the jets, the modules, the landers) and left and right the slot; on an
/// empty slot, select opens the modules that may go there (left and right
/// to choose, only those for sale, weapons only at the front and back),
/// and select buys one; on a full one, select sells it.
/// <para>
/// It runs on the host, from the keys the player's client sends (each
/// docked player has their own: <see cref="StarbaseMenu"/>); each purchase
/// or sale is an <see cref="OutfitAction"/> given to the rules
/// (<see cref="Outfitting"/>), which may refuse it.
/// </para>
/// </summary>
public sealed class OutfitMenu
{
    /// <summary>The menu's entries (OUTFIT_FUEL ... OUTFIT_EXIT) and the fuel gauge's (OUTFIT_DOFUEL).</summary>
    public const byte Fuel = 0, Modules = 1, SaveLoad = 2, Exit = 3, DoFuel = 4;

    private readonly Outfitting _rules;
    private readonly Func<OutfitAction, bool> _act;
    private readonly List<ScreenOp> _ops = new();
    private bool _installing, _initialized;

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
    /// Outfitting as it stands, drawn afresh (when drawing was lost): as
    /// it opens with the fuel aboard now, the status area drawn afresh (the
    /// opening doesn't draw its gauges, which the lost drawing may have
    /// changed), then the menu on its entry, or in Modules the radar
    /// cleared and what the cursor is on.
    /// </summary>
    public List<ScreenOp> Redraw()
    {
        var ops = new List<ScreenOp> { new(ScreenOpKind.OutfitOpened, (int)_rules.Ship.Fuel), new(ScreenOpKind.SisDisplay) };
        if (_installing)
        {
            ops.Add(new(ScreenOpKind.ClearRadar));
            ops.Add(new(ScreenOpKind.ModuleStrings, Choosing ? State : _rules.Piece(Row, Item)));
        }
        else if (State is not (Fuel or DoFuel))
            ops.Add(new(ScreenOpKind.MenuStrings, ScreenOp.PmFuel, State));
        return ops;
    }

    /// <summary>The ship or the captain renamed while outfitting: onNamingDone's DrawFlagshipName, and the captain's name.</summary>
    public void Renamed(bool ship, bool captain)
    {
        if (ship)
            Draw(ScreenOpKind.FlagshipName);
        if (captain)
            Draw(ScreenOpKind.CaptainsName);
    }

    /// <param name="rules">The player's ship and the prices, as this side sees them.</param>
    /// <param name="act">Does an action (sends it, and applies it here); false if refused.</param>
    public OutfitMenu(Outfitting rules, Func<OutfitAction, bool> act)
    {
        _rules = rules;
        _act = act;
    }

    /// <summary>CurState: a menu entry; in Modules, a row's empty mark (EMPTY_SLOT + 0 to 3) or, choosing a module, the module's number.</summary>
    public byte State { get; private set; }

    /// <summary>delta_item: the slot in the row.</summary>
    public int Item { get; private set; }

    /// <summary>Whether the cursor is in Modules (DoInstallModule).</summary>
    public bool Installing => _installing;

    /// <summary>The row the cursor is on in Modules, from <see cref="State"/>.</summary>
    public SlotKind Row => RowOf(State);

    /// <summary>The row a cursor's CurState in Modules is on.</summary>
    public static SlotKind RowOf(byte state) => state switch
    {
        ShipModules.PlanetLander or ShipModules.EmptyLander => SlotKind.Lander,
        ShipModules.FusionThruster or ShipModules.EmptyDrive => SlotKind.Drive,
        ShipModules.TurningJets or ShipModules.EmptyJet => SlotKind.Jet,
        _ => SlotKind.Module,
    };

    /// <summary>How many slots a row has: MAX_LANDERS, NUM_DRIVE_SLOTS, NUM_JET_SLOTS, NUM_MODULE_SLOTS.</summary>
    public static int SlotCount(SlotKind row) => row switch
    {
        SlotKind.Lander => ShipHold.MaxLanders,
        SlotKind.Drive => ShipModules.DriveSlotCount,
        SlotKind.Jet => ShipModules.JetSlotCount,
        _ => ShipModules.ModuleSlotCount,
    };

    /// <summary>Choosing a module to buy (CurState below EMPTY_SLOT in Modules).</summary>
    public bool Choosing => _installing && State < ShipModules.EmptySlot;

    /// <summary>DoOutfit, as it opens: the fuel rounded, the cursor on Fuel.</summary>
    public void Open()
    {
        _installing = false;
        State = Fuel;
        Item = 0;
        Draw(ScreenOpKind.OutfitOpened, (int)_rules.Ship.Fuel);   // drawn before the fuel is rounded
        _act(new OpenOutfit());
    }

    /// <summary>
    /// A key, as DoOutfit (or, in Modules, DoInstallModule) takes it.
    /// Save/Load is refused, drawing nothing, where
    /// <paramref name="canSave"/> is false (a guest: only the host saves).
    /// </summary>
    public OutfitOutcome Key(MenuKey key, bool canSave = true)
    {
        if (_installing)
            return InstallModule(key);

        if (key == MenuKey.Cancel || (key == MenuKey.Select && State == Exit))
        {
            if (State == DoFuel)
            {
                State = Fuel;
                return OutfitOutcome.None;
            }
            return OutfitOutcome.Left;
        }
        if (key == MenuKey.Select)
        {
            switch (State)
            {
                case Fuel:
                    State = DoFuel;
                    break;
                case DoFuel:
                    State = Fuel;
                    break;
                case Modules:
                    State = ShipModules.EmptyModule;
                    Item = _rules.BombInstalled ? ShipModules.BombModules : 0;
                    _initialized = false;
                    _installing = true;
                    return InstallModule(null);
                case SaveLoad:
                    if (!canSave)
                        return OutfitOutcome.Refused;
                    // GameOptions, then the menu drawn again.
                    Draw(ScreenOpKind.MenuStrings, ScreenOp.PmFuel, SaveLoad);
                    return OutfitOutcome.SaveLoad;
            }
            return OutfitOutcome.None;
        }
        if (State == DoFuel)
        {
            int incr = key switch
            {
                MenuKey.Up => (int)Hyperspace.Fuel.Scale,
                MenuKey.Down => -(int)Hyperspace.Fuel.Scale,
                MenuKey.PageUp => Outfitting.FuelBar,
                MenuKey.PageDown => -Outfitting.FuelBar,
                _ => 0,
            };
            if (incr == 0)
                return OutfitOutcome.None;
            var (fuel, ru) = (_rules.Ship.Fuel, _rules.Ship.ResourceUnits);
            if (!_act(new ChangeFuel(incr)))
                return OutfitOutcome.Refused;
            // ChangeFuelQuantity: DeltaSISGauges (0, incr, -cost), then RedistributeFuel.
            Draw(ScreenOpKind.Gauges, 0, (int)(_rules.Ship.Fuel - (long)fuel), (int)(_rules.Ship.ResourceUnits - (long)ru));
            Draw(ScreenOpKind.RedistributeFuel);
            return OutfitOutcome.Acted;
        }
        // DoMenuChooser (PM_FUEL): four entries, wrapping round, the menu drawn on any arrow.
        if (key is MenuKey.Left or MenuKey.Up)
            State = (byte)(State == Fuel ? Exit : State - 1);
        else if (key is MenuKey.Right or MenuKey.Down)
            State = (byte)(State == Exit ? Fuel : State + 1);
        else
            return OutfitOutcome.None;
        Draw(ScreenOpKind.MenuStrings, ScreenOp.PmFuel, State);
        return OutfitOutcome.None;
    }

    // DoInstallModule; key null as it starts.
    private OutfitOutcome InstallModule(MenuKey? key)
    {
        bool select = key == MenuKey.Select, cancel = key == MenuKey.Cancel;
        bool up = key == MenuKey.Up, down = key == MenuKey.Down, left = key == MenuKey.Left, right = key == MenuKey.Right;
        bool motion = up || down || left || right;
        var outcome = OutfitOutcome.None;

        int firstItem = 0, lastItem;
        byte newState = State;
        var row = Row;
        byte oldPiece = _rules.Piece(row, Item);
        lastItem = Outfitting.Slots(row) - 1;
        if (row == SlotKind.Module && _rules.BombInstalled)
            firstItem = ShipModules.BombModules;
        if (newState < ShipModules.CrewPod)
            firstItem = lastItem = newState;
        else if (newState < ShipModules.EmptySlot)
            (firstItem, lastItem) = (ShipModules.CrewPod, ShipModules.PurchaseModules - 1);

        if (!_initialized)
        {
            // ClearSISRect (CLEAR_SIS_RADAR), then InitFlash's DrawModuleStrings of the slot's piece.
            _initialized = true;
            Draw(ScreenOpKind.ClearRadar);
            Draw(ScreenOpKind.ModuleStrings, oldPiece);
            return outcome;
        }
        if (select || cancel)
        {
            byte newPiece = State;
            if (select)
            {
                uint ru = _rules.Ship.ResourceUnits;
                bool acted = false;
                if (newPiece < ShipModules.EmptySlot)
                {
                    // Buying: in a module's own row, at the slot the cursor was on.
                    if (!_act(new Install(row, Item, newPiece)))
                        return OutfitOutcome.Refused;
                    acted = true;
                }
                else if (oldPiece < ShipModules.EmptySlot)
                {
                    // Selling.
                    if (!_act(new Remove(row, Item)))
                        return OutfitOutcome.Refused;
                    acted = true;
                }
                else
                {
                    // An empty slot: the modules that may go there.
                    newPiece = (byte)(newPiece - (ShipModules.EmptySlot - 1));
                    if (newPiece > ShipModules.CrewPod)
                        newPiece = ShipModules.PlanetLander;
                }
                if (acted)
                {
                    // The gauges, then the landers, or the piece, the fuel and the stats.
                    outcome = OutfitOutcome.Acted;
                    Draw(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined,
                        (int)(_rules.Ship.ResourceUnits - (long)ru));
                    if (row == SlotKind.Lander)
                        Draw(ScreenOpKind.DisplayLanders);
                    else
                    {
                        Draw(ScreenOpKind.ShipPiece, newPiece, Item);
                        if (newPiece > ShipModules.TurningJets && oldPiece > ShipModules.TurningJets)
                            Draw(ScreenOpKind.RedistributeFuel);
                        Draw(ScreenOpKind.FlagshipStats);
                    }
                }
                cancel = false;
            }

            if (State < ShipModules.EmptySlot)
            {
                // Back from choosing to the row, showing what was bought (or, cancelled, nothing).
                int s = State + ShipModules.EmptySlot - 1;
                State = (byte)(s < ShipModules.EmptySlot ? ShipModules.EmptyLander
                    : s > ShipModules.EmptyModule ? ShipModules.EmptyModule : s);
                Draw(ScreenOpKind.ModuleStrings, cancel ? State : newPiece);
            }
            else if (!cancel)
            {
                State = newPiece;
                Draw(ScreenOpKind.ModuleStrings, newPiece);
            }
            else
            {
                State = Modules;
                _installing = false;
                Draw(ScreenOpKind.MenuStrings, ScreenOp.PmFuel, Modules);
                Draw(ScreenOpKind.SisDisplay);
            }
            return outcome;
        }
        if (!motion)
            return outcome;

        int newItem = newState < ShipModules.EmptySlot ? State : Item;
        // A row whose modules are all off sale would turn forever in UQM:
        // here it stops after a turn.
        for (int turns = 0; ; ++turns)
        {
            if (newState >= ShipModules.EmptySlot && (up || down))
            {
                if (up)
                {
                    if (newState-- == ShipModules.EmptyDrive)
                        newState = ShipModules.EmptyLander;
                }
                else if (newState++ == ShipModules.EmptyLander)
                    newState = ShipModules.EmptyDrive;
                newItem = 0;
                if (_rules.BombInstalled)
                {
                    if (newState == ShipModules.EmptyLander)
                        newState = up ? ShipModules.EmptyModule : ShipModules.EmptyDrive;
                    if (newState == ShipModules.EmptyModule)
                        newItem = ShipModules.BombModules;
                }
                Item = newItem;
            }
            else if (left || up)
            {
                if (newItem-- == firstItem)
                    newItem = lastItem;
            }
            else
            {
                if (newItem++ == lastItem)
                    newItem = firstItem;
            }
            bool skip = newState < ShipModules.EmptySlot
                && (_rules.Cost((byte)newItem) == 0
                    || (newItem >= ShipModules.GunWeapon && newItem <= ShipModules.CannonWeapon && Item > 0 && Item < 13));
            if (!skip || turns > ShipModules.Modules)
                break;
        }

        if (newState < ShipModules.EmptySlot)
        {
            if (newItem != State)
            {
                State = (byte)newItem;
                Draw(ScreenOpKind.ModuleStrings, State);
            }
        }
        else if (newItem != Item || newState != State)
        {
            if (newState == State)
                Item = newItem;
            else
                State = newState;
            Draw(ScreenOpKind.ModuleStrings, _rules.Piece(Row, Item));
        }
        return outcome;
    }
}
