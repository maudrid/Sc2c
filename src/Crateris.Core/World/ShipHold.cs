// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.World;

/// <summary>
/// Lander upgrades and shields, each ship's own (UQM's IMPROVED_LANDER_SPEED,
/// IMPROVED_LANDER_CARGO, IMPROVED_LANDER_SHOT and the LANDER_SHIELDS bits,
/// one per disaster in planets.h's order, as <see cref="Planetside.LanderDisaster"/> numbers them). None to start with; they come
/// from the Melnorme and the Chmmr in stage 3.
/// </summary>
[Flags]
public enum LanderUpgrades
{
    None = 0,
    /// <summary>Shield against creatures (1 &lt;&lt; BIOLOGICAL_DISASTER).</summary>
    BioShield = 1 << 0,
    /// <summary>Shield against earthquakes (1 &lt;&lt; EARTHQUAKE_DISASTER).</summary>
    QuakeShield = 1 << 1,
    /// <summary>Shield against lightning (1 &lt;&lt; LIGHTNING_DISASTER).</summary>
    LightningShield = 1 << 2,
    /// <summary>Shield against lava (1 &lt;&lt; LAVASPOT_DISASTER).</summary>
    LavaShield = 1 << 3,
    Speed = 1 << 4,
    Cargo = 1 << 5,
    Shot = 1 << 6,
    All = QuakeShield | BioShield | LightningShield | LavaShield | Speed | Cargo | Shot,
}

/// <summary>
/// A ship's hold as it's saved: crew aboard, landers aboard, minerals by
/// element category, bio data and lander upgrades. A lander that is out is
/// saved as back aboard with its crew (stage 2b, decision 5: the lander
/// takes off when its player leaves). Resource units are the player's
/// wallet (ResUnits), 0 in saves from before stage 3a.
/// </summary>
public sealed record HoldState(int Crew, int Landers, int[]? Minerals, int BioData,
    LanderUpgrades Upgrades = LanderUpgrades.None, uint ResourceUnits = 0);

/// <summary>
/// What a ship carries (<see cref="ShipHold"/>), as snapshots send it:
/// crew, landers, storage capacity, minerals by element category, bio data,
/// lander upgrades and resource units. Equal by value, so snapshots compare
/// by value.
/// </summary>
public sealed record HoldSnapshot(int Crew, int Landers, int StorageCapacity, IReadOnlyList<int> Minerals, int BioData,
    LanderUpgrades Upgrades = LanderUpgrades.None, uint ResourceUnits = 0)
{
    public int MineralMass => Minerals.Sum();

    public bool Equals(HoldSnapshot? other) =>
        other is not null && (Crew, Landers, StorageCapacity, BioData, Upgrades, ResourceUnits)
            == (other.Crew, other.Landers, other.StorageCapacity, other.BioData, other.Upgrades, other.ResourceUnits)
        && Minerals.SequenceEqual(other.Minerals);

    public override int GetHashCode() => HashCode.Combine(Crew, Landers, StorageCapacity, BioData, Upgrades, MineralMass);
}

/// <summary>
/// What a ship carries for landing and mining (SIS_STATE in sis.h): its
/// crew, its landers, the minerals in its storage bays by category
/// (ElementAmounts, TotalElementMass) and its bio data (TotalBioMass), and
/// its lander upgrades. Each player's own. A lander goes out with
/// <see cref="LanderLeaves"/> and comes back, or is lost, with
/// <see cref="LanderReturns"/>.
/// </summary>
public sealed class ShipHold
{
    /// <summary>MAX_LANDERS (sis.h).</summary>
    public const int MaxLanders = 10;
    /// <summary>STORAGE_BAY_CAPACITY (sis.h): minerals one storage bay holds.</summary>
    public const int StorageBayCapacity = 500;
    /// <summary>MAX_SCROUNGED (planets.h): minerals, or bio data, one lander holds.</summary>
    public const int MaxScrounged = 50;
    /// <summary>The crew a lander takes, at most (NUM_CREW_COLS * NUM_CREW_ROWS in lander.c).</summary>
    public const int LanderCrew = 12;
    /// <summary>NUM_ELEMENT_CATEGORIES (elemdata.h).</summary>
    public const int Categories = 8;

    private readonly int[] _minerals = new int[Categories];
    private HoldSnapshot? _snapshot;
    private LanderUpgrades _upgrades;

    /// <param name="modules">The ship's slots, which give the capacities as they change; else the loadout's.</param>
    public ShipHold(ShipLoadout loadout, ShipModules? modules = null)
    {
        Check(loadout);
        Crew = loadout.Crew;
        Landers = loadout.Landers;
        _modules = modules;
        _storageCapacity = loadout.StorageBays * StorageBayCapacity;
        _crewCapacity = loadout.CrewCapacity;
    }

    private readonly ShipModules? _modules;
    private readonly int _storageCapacity, _crewCapacity;

    /// <summary>The ship's slots changed: the snapshot is made again.</summary>
    internal void Refitted() => _snapshot = null;

    /// <summary>A loadout a hold can start from: 0 to 10 landers, no negative crew or storage bays (ArgumentException).</summary>
    public static void Check(ShipLoadout loadout)
    {
        if (loadout.Landers < 0 || loadout.Landers > MaxLanders)
            throw new ArgumentOutOfRangeException(nameof(loadout), $"{loadout.Landers} landers (0 to {MaxLanders})");
        if (loadout.StorageBays < 0)
            throw new ArgumentOutOfRangeException(nameof(loadout), $"{loadout.StorageBays} storage bays");
        if (loadout.Crew < 0)
            throw new ArgumentOutOfRangeException(nameof(loadout), $"{loadout.Crew} crew");
        if (loadout.CrewCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(loadout), $"room for {loadout.CrewCapacity} crew");
    }

    /// <summary>Crew aboard (not counting a lander's that is out).</summary>
    public int Crew { get; private set; }

    /// <summary>Landers aboard, and the one out if there is one.</summary>
    public int Landers { get; private set; }

    public LanderUpgrades Upgrades
    {
        get => _upgrades;
        set
        {
            if ((value & ~LanderUpgrades.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(value), $"lander upgrades {(int)value}");
            _upgrades = value;
            _snapshot = null;
        }
    }

    /// <summary>The minerals the storage bays hold (GetStorageBayCapacity).</summary>
    public int StorageCapacity => _modules?.StorageCapacity ?? _storageCapacity;

    /// <summary>The crew the ship holds (GetCrewPodCapacity).</summary>
    public int CrewCapacity => _modules?.CrewCapacity ?? _crewCapacity;

    /// <summary>The player's resource units (ResUnits): their wallet.</summary>
    public uint ResourceUnits
    {
        get => _resourceUnits;
        internal set
        {
            _resourceUnits = value;
            _snapshot = null;
        }
    }

    private uint _resourceUnits;

    /// <summary>A talk changes the crew aboard (the commander's lander with its crew).</summary>
    internal void SetCrew(int crew)
    {
        if (crew < 0)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew");
        Crew = crew;
        _snapshot = null;
    }

    /// <summary>A talk changes the landers aboard (the commander's new lander), up to <see cref="MaxLanders"/>.</summary>
    internal void SetLanders(int landers)
    {
        if (landers < 0 || landers > MaxLanders)
            throw new ArgumentOutOfRangeException(nameof(landers), $"{landers} landers (0 to {MaxLanders})");
        Landers = landers;
        _snapshot = null;
    }

    /// <summary>A talk sells minerals: a category's amount goes down (to 0, as the allied commander buys them all).</summary>
    internal void SetMineral(int category, int amount)
    {
        if (category < 0 || category >= Categories)
            throw new ArgumentOutOfRangeException(nameof(category), $"element category {category}");
        if (amount < 0 || amount > _minerals[category])
            throw new ArgumentOutOfRangeException(nameof(amount), $"{amount} of category {category} (0 to the {_minerals[category]} held)");
        _minerals[category] = amount;
        _snapshot = null;
    }

    /// <summary>Minerals held, by element category (ElementAmounts).</summary>
    public IReadOnlyList<int> Minerals => _minerals;

    /// <summary>TotalElementMass.</summary>
    public int MineralMass => _minerals.Sum();

    public int FreeStorage => StorageCapacity - MineralMass;

    /// <summary>TotalBioMass.</summary>
    public int BioData { get; private set; }

    /// <summary>
    /// The minerals a lander can bring up (PlanetSide's MaxElementLevel):
    /// MAX_SCROUNGED, twice that with the cargo upgrade, and no more than the
    /// storage bays have room for.
    /// </summary>
    public int LanderMineralCapacity =>
        Math.Min(_upgrades.HasFlag(LanderUpgrades.Cargo) ? MaxScrounged << 1 : MaxScrounged, FreeStorage);

    /// <summary>The crew a lander takes now: up to 12, as many as are aboard (AnimateLanderWarmup).</summary>
    public int CrewForLander => Math.Min(LanderCrew, Crew);

    /// <summary>The lander that is out: the crew it took and the minerals it has room for; null if none is.</summary>
    public (int Crew, int MineralCapacity)? LanderOut { get; private set; }

    /// <summary>
    /// How many times a lander has left since the hold was made (not saved):
    /// tells a lander's trip from a later one, after the hold is restored
    /// from a save while the first was still down.
    /// </summary>
    public int LanderTrips { get; private set; }

    /// <summary>
    /// A lander leaves, taking up to 12 crew (AnimateLanderWarmup). UQM
    /// dispatches one only with a lander and crew aboard (DoScan), one at a
    /// time. Returns the crew it took.
    /// </summary>
    public int LanderLeaves()
    {
        if (LanderOut is not null)
            throw new InvalidOperationException("a lander is already out");
        if (Landers == 0)
            throw new InvalidOperationException("no lander aboard");
        if (Crew == 0)
            throw new InvalidOperationException("no crew to take the lander down");
        int crew = CrewForLander;
        LanderOut = (crew, LanderMineralCapacity);
        LanderTrips++;
        Crew -= crew;
        _snapshot = null;
        return crew;
    }

    /// <summary>
    /// The lander that is out comes back (PlanetSide): the crew still alive
    /// go back aboard and what it carries goes into the hold, within what it
    /// could carry (its mineral room when it left, MAX_SCROUNGED bio data).
    /// A lander whose crew all died is lost, and what it carried with it.
    /// </summary>
    public void LanderReturns(int crew, IReadOnlyList<int> minerals, int bioData)
    {
        if (LanderOut is not { } lander)
            throw new InvalidOperationException("no lander is out");
        if (crew < 0 || crew > lander.Crew)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew (0 to the {lander.Crew} it took)");
        if (minerals.Count != Categories || minerals.Any(m => m < 0) || minerals.Sum() > lander.MineralCapacity)
            throw new ArgumentOutOfRangeException(nameof(minerals),
                $"minerals need {Categories} amounts of at least 0, {lander.MineralCapacity} in all at most");
        if (bioData < 0 || bioData > MaxScrounged)
            throw new ArgumentOutOfRangeException(nameof(bioData), $"{bioData} bio data (0 to {MaxScrounged})");
        LanderOut = null;
        _snapshot = null;
        if (crew == 0)
        {
            Landers--;
            return;
        }
        Crew += crew;
        for (int i = 0; i < Categories; ++i)
            _minerals[i] += minerals[i];
        BioData += bioData;
    }

    /// <summary>
    /// A unit of an element category goes overboard (the Manifest's cargo
    /// display, DoDiscardCargo); false, changing nothing, if there's none.
    /// </summary>
    public bool Discard(int category)
    {
        if (category < 0 || category >= Categories || _minerals[category] == 0)
            return false;
        _minerals[category]--;
        _snapshot = null;
        return true;
    }

    /// <summary>The hold for snapshots, made again only when it changes.</summary>
    public HoldSnapshot Snapshot() =>
        _snapshot ??= new HoldSnapshot(Crew, Landers, StorageCapacity, _minerals.ToArray(), BioData, _upgrades, _resourceUnits);

    /// <summary>The hold for the save; a lander that is out is saved as back aboard with its crew.</summary>
    public HoldState Save() => new(Crew + (LanderOut?.Crew ?? 0), Landers, _minerals.ToArray(), BioData, _upgrades, _resourceUnits);

    /// <summary>
    /// The hold for the save while its lander is on a surface (decision 5:
    /// saves keep players in orbit): as if the lander came back now with
    /// the crew and cargo it has (<see cref="LanderReturns"/>), lost if its
    /// crew have all died. The hold itself doesn't change.
    /// </summary>
    public HoldState SaveWithLanderBack(int crew, IReadOnlyList<int> minerals, int bioData)
    {
        if (LanderOut is null)
            return Save();
        if (crew == 0)
            return new(Crew, Landers - 1, _minerals.ToArray(), BioData, _upgrades, _resourceUnits);
        return new(Crew + crew, Landers, _minerals.Zip(minerals, (a, b) => a + b).ToArray(), BioData + bioData, _upgrades,
            _resourceUnits);
    }

    /// <summary>Puts a saved hold back (the loadout gives the storage). A bad one is an <see cref="InvalidDataException"/>.</summary>
    public void Restore(HoldState state)
    {
        if (state.Minerals is not { } minerals)
            throw new InvalidDataException("the hold has no minerals");
        if (state.Crew < 0 || state.Landers < 0 || state.Landers > MaxLanders || state.BioData < 0)
            throw new InvalidDataException(
                $"bad hold: {state.Crew} crew, {state.Landers} landers (0 to {MaxLanders}), {state.BioData} bio data");
        if (minerals.Length != Categories || minerals.Any(m => m < 0))
            throw new InvalidDataException($"bad hold: minerals need {Categories} amounts of at least 0");
        if (minerals.Sum() > StorageCapacity)
            throw new InvalidDataException($"bad hold: {minerals.Sum()} minerals in {StorageCapacity} of storage");
        if ((state.Upgrades & ~LanderUpgrades.All) != 0)
            throw new InvalidDataException($"bad hold: lander upgrades {(int)state.Upgrades}");
        Crew = state.Crew;
        Landers = state.Landers;
        minerals.CopyTo(_minerals, 0);
        BioData = state.BioData;
        _upgrades = state.Upgrades;
        _resourceUnits = state.ResourceUnits;
        LanderOut = null;
        _snapshot = null;
    }
}
