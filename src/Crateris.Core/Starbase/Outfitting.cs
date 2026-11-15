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
// DMS_HireFlagShipCrew, DMS_DismissFlagShipCrew, CrewTransaction, and for
// the host's fleet DMS_TryAddEscortShip, DMS_HireEscortShipCrew,
// DMS_DismissEscortShipCrew, DMS_ScrapEscortShip and
// GetAvailableRaceFromIndex; sis.c's DeltaSISGauges), Copyright Paul Reiche, Fred Ford and Toys for
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

/// <summary>A fleet ship of a race (ARILOU_SHIP, ...) built into an empty hangar slot (the shipyard, the host's only).</summary>
public sealed record BuildShip(int Slot, string Race) : OutfitAction;

/// <summary>A crew member hired for the fleet ship in a hangar slot (the host's only).</summary>
public sealed record HireShipCrew(int Slot) : OutfitAction;

/// <summary>A crew member sent away from the fleet ship in a hangar slot (the host's only).</summary>
public sealed record DismissShipCrew(int Slot) : OutfitAction;

/// <summary>The fleet ship of no crew in a hangar slot scrapped, as its crew editing ends (the host's only).</summary>
public sealed record ScrapShip(int Slot) : OutfitAction;

/// <summary>
/// The starbase's outfitting and shipyard crew, by UQM's rules (stage 3a,
/// item 8): what a player may buy and sell, at the story's prices
/// (ModuleCost by MODULE_COST_SCALE, FuelCost, CrewCost), from their own
/// wallet. A refused action changes nothing, as UQM refuses with
/// MENU_SOUND_FAILURE.
/// <para>
/// Escorts (decision 10) buy fuel, crew and landers within their kit, and
/// storage bays; and (stage 4, decision 3) the combat modules: guns,
/// blasters and cannons (in the weapon slots, as the flagship's), tracking,
/// point defence, dynamos and furnaces, at the flagship's prices, and sell
/// them back as the flagship does (<see cref="EscortMayHold"/>). The rest
/// of their modules are their kit's (its thrusters, jets, fuel tank and
/// crew); the flagship keeps its unique ones: crew pods, fuel tanks and
/// high-efficiency fuel systems, thrusters and jets, and the bomb's parts
/// (which outfitting never sells). The crew bought counts for the whole
/// group (decision 5): it raises the crew price for everyone.
/// </para>
/// </summary>
public sealed class Outfitting
{
    /// <summary>FUEL_VOLUME_PER_ROW: a bar of the fuel gauge (HEFUEL_TANK_CAPACITY / MAX_FUEL_BARS).</summary>
    public const int FuelBar = (int)(Hyperspace.Fuel.HighEfficiencyTankSize / 10);

    /// <param name="ship">The player's own ship.</param>
    /// <param name="story">The prices, the alliances.</param>
    /// <param name="fleet">The host's fleet, for the host (stage 4b, decision 3); null for a guest, who has none.</param>
    /// <param name="random">TFB_Random, for the captains' names of the ships built; needed with a fleet.</param>
    public Outfitting(IOutfitShip ship, StoryState story, HostFleet? fleet = null, Func<uint>? random = null)
    {
        Ship = ship;
        Story = story;
        Fleet = fleet;
        _random = random;
    }

    private readonly Func<uint>? _random;

    public IOutfitShip Ship { get; }

    public StoryState Story { get; }

    /// <summary>The host's fleet the shipyard builds, crews and scraps (the host's own); null for a guest.</summary>
    public HostFleet? Fleet { get; }

    /// <summary>
    /// The ships the shipyard builds (GetAvailableRaceCount and
    /// GetAvailableRaceFromIndex): the allied races' (GOOD_GUY), in
    /// avail_race_q's order; none without a fleet (a guest). The Ur-Quan
    /// drone, which UQM never allies, isn't one even if allied.
    /// </summary>
    public IReadOnlyList<string> BuildList => Fleet is null ? Array.Empty<string>()
        : Story.Rules.Races.Where(r => Story.Alliance(r) == Allied.Good && HostFleet.CanHave(Story.Rules, r)).ToList();

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
        BuildShip b => Build(b.Slot, b.Race),
        HireShipCrew h => HireFleetCrew(h.Slot),
        DismissShipCrew d => DismissFleetCrew(d.Slot),
        ScrapShip s => Scrap(s.Slot),
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
    /// What an escort may buy and sell (stage 3, decision 10; stage 4,
    /// decision 3): landers, storage bays, and the combat modules (dynamos,
    /// furnaces, guns, blasters, cannons, tracking, point defence). Not the
    /// flagship's unique ones: crew pods, fuel tanks, high-efficiency fuel
    /// systems, thrusters and jets (an escort's are its kit's), nor the
    /// bomb's parts.
    /// </summary>
    public static bool EscortMayHold(byte piece) => piece is ShipModules.PlanetLander or ShipModules.StorageBay
        or (>= ShipModules.DynamoUnit and <= ShipModules.AntimissileDefense);

    /// <summary>
    /// Whether a module may go in a slot, as DoInstallModule's choices
    /// allow: a lander, a thruster or jets in their own rows; a module
    /// (CREW_POD to ANTIMISSILE_DEFENSE) in a module slot, weapons only in
    /// the front and back slots (0 and 13 to 15); one that's for sale; not
    /// in the bomb's slots, and no landers, once the bomb is in. An
    /// escort's only those it may hold (<see cref="EscortMayHold"/>).
    /// </summary>
    public bool CanHold(SlotKind kind, int slot, byte piece)
    {
        if (slot < 0 || slot >= Slots(kind) || piece >= ShipModules.EmptySlot || Cost(piece) == 0)
            return false;
        if (Ship.Kind == ShipKind.Escort && !EscortMayHold(piece))
            return false;
        return kind switch
        {
            SlotKind.Lander => piece == ShipModules.PlanetLander && !BombInstalled,
            SlotKind.Drive => piece == ShipModules.FusionThruster,
            SlotKind.Jet => piece == ShipModules.TurningJets,
            _ => piece >= ShipModules.CrewPod && piece < ShipModules.PurchaseModules
                && !(ShipModules.IsWeapon(piece) && slot > 0 && slot < 13)
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
    /// escort sells only those it may hold (<see cref="EscortMayHold"/>).
    /// </summary>
    public bool CanRemove(SlotKind kind, int slot)
    {
        if (slot < 0 || slot >= Slots(kind))
            return false;
        byte old = Piece(kind, slot);
        if (old >= ShipModules.EmptySlot)
            return false;
        if (Ship.Kind == ShipKind.Escort && !EscortMayHold(old))
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

    /// <summary>
    /// DMS_TryAddEscortShip: a ship of an allied race built into an empty
    /// hangar slot, if the wallet has its cost (RACE_SHIP_COST): one crew
    /// aboard, its captain named (CloneShipFragment), the cost paid. The
    /// crew bought doesn't count it (no CrewTransaction). The host's only.
    /// </summary>
    private bool Build(int slot, string race)
    {
        if (Fleet is null || slot is < 0 or >= HostFleet.MaxShips || Fleet.InSlot(slot) is not null || !BuildList.Contains(race))
            return false;
        int cost = Story.Rules.RaceShip(race).Cost;
        if (Ship.ResourceUnits < (uint)cost)
            return false;
        Fleet.Build(Story.Rules, race, slot, 1, _random ?? throw new InvalidOperationException("building needs TFB_Random"));
        DeltaResourceUnits(-cost);
        return true;
    }

    /// <summary>
    /// DMS_HireEscortShipCrew: a crew member for a fleet ship, if the
    /// wallet has the crew price and the ship is below its most and its
    /// race's starting crew (the template's crew_level: a Syreen takes 12
    /// here, not its 42). Hiring for a ship of no crew buys it back at its
    /// cost instead of the crew price (the wallet still checked against the
    /// crew price, and emptied if short, as DeltaSISGauges does); either way
    /// it counts as crew bought.
    /// </summary>
    private bool HireFleetCrew(int slot)
    {
        if (Fleet?.InSlot(slot) is not { } ship)
            return false;
        var race = Story.Rules.RaceShip(ship.Race);
        if (Ship.ResourceUnits < (uint)Story.CrewCost || ship.Crew >= race.MaxCrew || ship.Crew >= race.Crew)
            return false;
        DeltaResourceUnits(-(ship.Crew > 0 ? Story.CrewCost : race.Cost));
        Fleet.SetCrew(Story.Rules, slot, ship.Crew + 1);
        Story.CrewTransaction(1);
        return true;
    }

    /// <summary>
    /// DMS_DismissEscortShipCrew: a crew member sent away from a fleet
    /// ship, the crew price paid back (2 less as the count stands on the
    /// threshold); the last one gives back the ship's cost instead, leaving
    /// it in its slot with no crew (scrapped as its crew editing ends). It
    /// counts as crew sent away.
    /// </summary>
    private bool DismissFleetCrew(int slot)
    {
        if (Fleet?.InSlot(slot) is not { Crew: > 0 } ship)
            return false;
        DeltaResourceUnits(ship.Crew > 1
            ? Story.CrewCost - (Story.CrewPurchased == Story.Rules.CrewExpenseThreshold ? 2 : 0)
            : Story.Rules.RaceShip(ship.Race).Cost);
        Fleet.SetCrew(Story.Rules, slot, ship.Crew - 1);
        Story.CrewTransaction(-1);
        return true;
    }

    /// <summary>DMS_ScrapEscortShip: a fleet ship of no crew leaves the fleet (its cost was given back with its last crew).</summary>
    private bool Scrap(int slot)
    {
        if (Fleet?.InSlot(slot) is not { Crew: 0 })
            return false;
        Fleet.Scrap(slot);
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
