// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/outfit.c's DoOutfit,
// DoInstallModule and ChangeFuelQuantity; shipyard.c's
// DMS_HireFlagShipCrew, DMS_DismissFlagShipCrew and CrewTransaction;
// sis.c's DeltaSISGauges), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or
// later.

using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Starbase;

/// <summary>A ship as outfitting and the shipyard change it.</summary>
public interface IOutfitShip
{
    ShipKind Kind { get; }

    ShipModules Modules { get; }

    /// <summary>Crew aboard (CrewEnlisted).</summary>
    int Crew { get; set; }

    /// <summary>Fuel, in 1/100 units (FuelOnBoard).</summary>
    uint Fuel { get; set; }

    /// <summary>Landers aboard (NumLanders).</summary>
    int Landers { get; set; }

    /// <summary>The player's resource units (ResUnits).</summary>
    uint ResourceUnits { get; set; }

    /// <summary>Minerals in the hold (TotalElementMass).</summary>
    int MineralMass { get; }
}

/// <summary>The kinds of slot outfitting fills (DoInstallModule's rows): the landers, the drives, the jets and the modules.</summary>
public enum SlotKind : byte
{
    Lander,
    Drive,
    Jet,
    Module,
}

/// <summary>What a player asks of the starbase's outfitting and shipyard.</summary>
public abstract record OutfitAction;

/// <summary>Outfitting opens: the fuel is rounded to whole units (DoOutfit).</summary>
public sealed record OpenOutfit : OutfitAction;

/// <summary>Fuel bought (or, negative, sold), in 1/100 units: a unit or a bar of the gauge at a time (ChangeFuelQuantity).</summary>
public sealed record ChangeFuel(int Points) : OutfitAction;

/// <summary>A module (a lander, a thruster, jets, or a module) bought into an empty slot.</summary>
public sealed record Install(SlotKind Kind, int Slot, byte Piece) : OutfitAction;

/// <summary>A slot's module sold back.</summary>
public sealed record Remove(SlotKind Kind, int Slot) : OutfitAction;

/// <summary>A crew member hired (the shipyard).</summary>
public sealed record HireCrew : OutfitAction;

/// <summary>A crew member sent away (the shipyard).</summary>
public sealed record DismissCrew : OutfitAction;

/// <summary>
/// The starbase's outfitting and shipyard crew, by UQM's rules (stage 3a,
/// item 8): what a player may buy and sell, at the story's prices
/// (ModuleCost by MODULE_COST_SCALE, FuelCost, CrewCost), from their own
/// wallet. A refused action changes nothing, as UQM refuses with
/// MENU_SOUND_FAILURE.
/// <para>
/// Escorts (decision 10) buy fuel, crew and landers within their kit, and
/// storage bays: the rest of their modules are their kit's, until the
/// escort module set is decided. The crew bought counts for the whole
/// group (decision 5): it raises the crew price for everyone.
/// </para>
/// </summary>
public sealed class Outfitting
{
    /// <summary>FUEL_VOLUME_PER_ROW: a bar of the fuel gauge (HEFUEL_TANK_CAPACITY / MAX_FUEL_BARS).</summary>
    public const int FuelBar = (int)(Hyperspace.Fuel.HighEfficiencyTankSize / 10);

    public Outfitting(IOutfitShip ship, StoryState story)
    {
        Ship = ship;
        Story = story;
    }

    public IOutfitShip Ship { get; }

    public StoryState Story { get; }

    /// <summary>A module's price in resource units (GLOBAL (ModuleCost[piece]) * MODULE_COST_SCALE).</summary>
    public int Price(byte piece) => Cost(piece) * Story.Rules.ModuleCostScale;

    /// <summary>A module's cost as the story keeps it (0: not for sale).</summary>
    public int Cost(byte piece) => Story.ModuleCost(ShipModules.Names[piece]);

    /// <summary>The Chmmr's bomb is in the flagship (CHMMR_BOMB_STATE 3): its module slots and no landers.</summary>
    public bool BombInstalled => Story.Get("CHMMR_BOMB_STATE") == 3;

    /// <summary>What's in a slot now (old_slot_piece): a lander slot holds one if it's among the landers aboard.</summary>
    public byte Piece(SlotKind kind, int slot) => kind switch
    {
        SlotKind.Lander => slot < Ship.Landers ? ShipModules.PlanetLander : ShipModules.EmptyLander,
        SlotKind.Drive => Ship.Modules.DriveSlots[slot],
        SlotKind.Jet => Ship.Modules.JetSlots[slot],
        _ => Ship.Modules.ModuleSlots[slot],
    };

    /// <summary>How many slots of a kind there are (a lander slot each of MAX_LANDERS).</summary>
    public static int Slots(SlotKind kind) => kind switch
    {
        SlotKind.Lander => ShipHold.MaxLanders,
        SlotKind.Drive => ShipModules.DriveSlotCount,
        SlotKind.Jet => ShipModules.JetSlotCount,
        _ => ShipModules.ModuleSlotCount,
    };

    /// <summary>Applies an action; false if refused, changing nothing.</summary>
    public bool Apply(OutfitAction action) => action switch
    {
        OpenOutfit => Open(),
        ChangeFuel f => FuelChange(f.Points),
        Install i => Buy(i.Kind, i.Slot, i.Piece),
        Remove r => Sell(r.Kind, r.Slot),
        HireCrew => Hire(),
        DismissCrew => Dismiss(),
        _ => false,
    };

    // DoOutfit as it opens: the fuel to the nearest whole unit.
    private bool Open()
    {
        uint scale = Hyperspace.Fuel.Scale;
        Ship.Fuel = (Ship.Fuel + (scale >> 1)) / scale * scale;
        return true;
    }

    /// <summary>
    /// ChangeFuelQuantity: up to what the tanks hold, the wallet affords
    /// (whole units) and the ship has; refused if that's nothing. Paid by
    /// the whole units in it.
    /// </summary>
    private bool FuelChange(int incr)
    {
        int scale = (int)Hyperspace.Fuel.Scale;
        if (incr != scale && incr != -scale && incr != FuelBar && incr != -FuelBar)
            return false;
        int maxFit = (int)Ship.Modules.FuelCapacity - (int)Ship.Fuel;
        int maxAfford = (int)(Ship.ResourceUnits / (uint)Story.FuelCost);
        int minFit = -(int)Ship.Fuel;
        if (incr > maxFit)
            incr = maxFit;
        if (incr > maxAfford * scale)
            incr = maxAfford * scale;
        if (incr < minFit)
            incr = minFit;
        if (incr == 0)
            return false;
        int cost = incr / scale * Story.FuelCost;
        DeltaFuel(incr);
        DeltaResourceUnits(-cost);
        return true;
    }

    /// <summary>
    /// Whether a module may go in a slot, as DoInstallModule's choices
    /// allow: a lander, a thruster or jets in their own rows; a module
    /// (CREW_POD to ANTIMISSILE_DEFENSE) in a module slot, weapons only in
    /// the front and back slots (0 and 13 to 15); one that's for sale; not
    /// in the bomb's slots, and no landers, once the bomb is in. An
    /// escort's only landers and storage bays (decision 10).
    /// </summary>
    public bool CanHold(SlotKind kind, int slot, byte piece)
    {
        if (slot < 0 || slot >= Slots(kind) || piece >= ShipModules.EmptySlot || Cost(piece) == 0)
            return false;
        if (Ship.Kind == ShipKind.Escort && piece != ShipModules.PlanetLander && piece != ShipModules.StorageBay)
            return false;
        return kind switch
        {
            SlotKind.Lander => piece == ShipModules.PlanetLander && !BombInstalled,
            SlotKind.Drive => piece == ShipModules.FusionThruster,
            SlotKind.Jet => piece == ShipModules.TurningJets,
            _ => piece >= ShipModules.CrewPod && piece < ShipModules.PurchaseModules
                && !(piece >= ShipModules.GunWeapon && piece <= ShipModules.CannonWeapon && slot > 0 && slot < 13)
                && !(BombInstalled && slot < ShipModules.BombModules),
        };
    }

    /// <summary>A module bought into an empty slot (DoInstallModule's select on a piece), if it may go there and the wallet has its price.</summary>
    private bool Buy(SlotKind kind, int slot, byte piece)
    {
        if (!CanHold(kind, slot, piece) || Piece(kind, slot) < ShipModules.EmptySlot)
            return false;
        if (Ship.ResourceUnits < (uint)Price(piece))
            return false;
        switch (kind)
        {
            case SlotKind.Lander:
                Ship.Landers++;
                break;
            case SlotKind.Drive:
                Ship.Modules.SetDrive(slot, piece);
                break;
            case SlotKind.Jet:
                Ship.Modules.SetJet(slot, piece);
                break;
            default:
                Ship.Modules.SetModule(slot, piece);
                break;
        }
        DeltaResourceUnits(-Price(piece));
        return true;
    }

    /// <summary>
    /// Whether a slot's module may be sold: one is there; not a bomb's slot;
    /// a crew pod not needed for the crew, a fuel tank not for the fuel
    /// (beyond the reserve) and a storage bay not for the minerals. An
    /// escort sells only landers and storage bays.
    /// </summary>
    public bool CanRemove(SlotKind kind, int slot)
    {
        if (slot < 0 || slot >= Slots(kind))
            return false;
        byte old = Piece(kind, slot);
        if (old >= ShipModules.EmptySlot)
            return false;
        if (Ship.Kind == ShipKind.Escort && old != ShipModules.PlanetLander && old != ShipModules.StorageBay)
            return false;
        if (kind == SlotKind.Lander && BombInstalled)
            return false;
        if (kind != SlotKind.Module)
            return true;
        if (BombInstalled && slot < ShipModules.BombModules)
            return false;
        var m = Ship.Modules;
        switch (old)
        {
            case ShipModules.CrewPod:
                return Ship.Crew <= Data.ShipLoadout.CrewPodCapacity * (m.Count(ShipModules.CrewPod) - 1) + m.KitCrew;
            case ShipModules.FuelTank:
            case ShipModules.HighEfficiencyFuelSystem:
                uint volume = (uint)m.Count(ShipModules.FuelTank) * Hyperspace.Fuel.TankSize
                    + (uint)m.Count(ShipModules.HighEfficiencyFuelSystem) * Hyperspace.Fuel.HighEfficiencyTankSize
                    - (old == ShipModules.FuelTank ? Hyperspace.Fuel.TankSize : Hyperspace.Fuel.HighEfficiencyTankSize);
                return Ship.Fuel <= volume + Hyperspace.Fuel.Reserve;
            case ShipModules.StorageBay:
                return Ship.MineralMass <= ShipHold.StorageBayCapacity * (m.Count(ShipModules.StorageBay) - 1);
            default:
                return true;
        }
    }

    /// <summary>A slot's module sold back at its price now (DoInstallModule's select on a full slot).</summary>
    private bool Sell(SlotKind kind, int slot)
    {
        if (!CanRemove(kind, slot))
            return false;
        byte old = Piece(kind, slot);
        switch (kind)
        {
            case SlotKind.Lander:
                Ship.Landers--;
                break;
            case SlotKind.Drive:
                Ship.Modules.SetDrive(slot, ShipModules.EmptyDrive);
                break;
            case SlotKind.Jet:
                Ship.Modules.SetJet(slot, ShipModules.EmptyJet);
                break;
            default:
                Ship.Modules.SetModule(slot, ShipModules.EmptyModule);
                break;
        }
        DeltaResourceUnits(Price(old));
        return true;
    }

    /// <summary>DMS_HireFlagShipCrew: a crew member, if there's room and the wallet has the crew price.</summary>
    private bool Hire()
    {
        if (Ship.Modules.CrewCapacity <= Ship.Crew || Ship.ResourceUnits < (uint)Story.CrewCost)
            return false;
        int cost = Story.CrewCost;
        DeltaCrew(1);
        DeltaResourceUnits(-cost);
        Story.CrewTransaction(1);
        return true;
    }

    /// <summary>DMS_DismissFlagShipCrew: a crew member sent away, the crew price paid back (2 less as the count stands on the threshold).</summary>
    private bool Dismiss()
    {
        if (Ship.Crew == 0)
            return false;
        int refund = Story.CrewCost - (Story.CrewPurchased == Story.Rules.CrewExpenseThreshold ? 2 : 0);
        DeltaCrew(-1);
        DeltaResourceUnits(refund);
        Story.CrewTransaction(-1);
        return true;
    }

    // DeltaSISGauges' changes.
    private void DeltaCrew(int delta)
    {
        if (delta < 0 && Ship.Crew <= -delta)
            Ship.Crew = 0;
        else
            Ship.Crew = Math.Min((ushort)(Ship.Crew + delta), Ship.Modules.CrewCapacity);
    }

    private void DeltaFuel(int delta)
    {
        if (delta < 0 && Ship.Fuel <= (uint)-delta)
            Ship.Fuel = 0;
        else
            Ship.Fuel = Math.Min((uint)(Ship.Fuel + delta), Ship.Modules.FuelCapacity);
    }

    private void DeltaResourceUnits(int delta)
    {
        if (delta < 0 && Ship.ResourceUnits <= (uint)-delta)
            Ship.ResourceUnits = 0;
        else
            Ship.ResourceUnits = (uint)(Ship.ResourceUnits + delta);
    }
}
