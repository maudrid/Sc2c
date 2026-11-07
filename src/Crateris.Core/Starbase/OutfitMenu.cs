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

    /// <summary>An action went to the host (and was applied here).</summary>
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
/// This is the player's own screen: each purchase or sale is an
/// <see cref="OutfitAction"/> given to <see cref="Act"/>, which the client
/// sends to the host and the host checks by the same rules
/// (<see cref="Outfitting"/>).
/// </para>
/// </summary>
public sealed class OutfitMenu
{
    /// <summary>The menu's entries (OUTFIT_FUEL ... OUTFIT_EXIT) and the fuel gauge's (OUTFIT_DOFUEL).</summary>
    public const byte Fuel = 0, Modules = 1, SaveLoad = 2, Exit = 3, DoFuel = 4;

    private readonly Outfitting _rules;
    private readonly Func<OutfitAction, bool> _act;
    private bool _installing, _initialized;

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
    public SlotKind Row => State switch
    {
        ShipModules.PlanetLander or ShipModules.EmptyLander => SlotKind.Lander,
        ShipModules.FusionThruster or ShipModules.EmptyDrive => SlotKind.Drive,
        ShipModules.TurningJets or ShipModules.EmptyJet => SlotKind.Jet,
        _ => SlotKind.Module,
    };

    /// <summary>Choosing a module to buy (CurState below EMPTY_SLOT in Modules).</summary>
    public bool Choosing => _installing && State < ShipModules.EmptySlot;

    /// <summary>DoOutfit, as it opens: the fuel rounded, the cursor on Fuel.</summary>
    public void Open()
    {
        _installing = false;
        State = Fuel;
        Item = 0;
        _act(new OpenOutfit());
    }

    /// <summary>A key, as DoOutfit (or, in Modules, DoInstallModule) takes it.</summary>
    public OutfitOutcome Key(MenuKey key)
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
            return _act(new ChangeFuel(incr)) ? OutfitOutcome.Acted : OutfitOutcome.Refused;
        }
        // DoMenuChooser (PM_FUEL): four entries, wrapping round.
        if (key is MenuKey.Left or MenuKey.Up)
            State = (byte)(State == Fuel ? Exit : State - 1);
        else if (key is MenuKey.Right or MenuKey.Down)
            State = (byte)(State == Exit ? Fuel : State + 1);
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
            _initialized = true;
            return outcome;
        }
        if (select || cancel)
        {
            byte newPiece = State;
            if (select)
            {
                if (newPiece < ShipModules.EmptySlot)
                {
                    // Buying: in a module's own row, at the slot the cursor was on.
                    if (!_act(new Install(row, Item, newPiece)))
                        return OutfitOutcome.Refused;
                    outcome = OutfitOutcome.Acted;
                }
                else if (oldPiece < ShipModules.EmptySlot)
                {
                    // Selling.
                    if (!_act(new Remove(row, Item)))
                        return OutfitOutcome.Refused;
                    outcome = OutfitOutcome.Acted;
                }
                else
                {
                    // An empty slot: the modules that may go there.
                    newPiece = (byte)(newPiece - (ShipModules.EmptySlot - 1));
                    if (newPiece > ShipModules.CrewPod)
                        newPiece = ShipModules.PlanetLander;
                }
                cancel = false;
            }

            if (State < ShipModules.EmptySlot)
            {
                // Back from choosing to the row.
                int s = State + ShipModules.EmptySlot - 1;
                State = (byte)(s < ShipModules.EmptySlot ? ShipModules.EmptyLander
                    : s > ShipModules.EmptyModule ? ShipModules.EmptyModule : s);
            }
            else if (!cancel)
                State = newPiece;
            else
            {
                State = Modules;
                _installing = false;
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
            State = (byte)newItem;
        else if (newItem != Item || newState != State)
        {
            if (newState == State)
                Item = newItem;
            else
                State = newState;
        }
        return outcome;
    }
}
