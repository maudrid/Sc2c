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
/// one per disaster as in lander.h's order). None to start with; they come
/// from the Melnorme and the Chmmr in stage 3.
/// </summary>
[Flags]
public enum LanderUpgrades
{
    None = 0,
    /// <summary>Shield against earthquakes (1 &lt;&lt; EARTHQUAKE_DISASTER).</summary>
    QuakeShield = 1 << 0,
    /// <summary>Shield against creatures (1 &lt;&lt; BIOLOGICAL_DISASTER).</summary>
    BioShield = 1 << 1,
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
/// What a ship carries for landing and mining (SIS_STATE in sis.h): its
/// crew, its landers, the minerals in its storage bays by category
/// (ElementAmounts, TotalElementMass) and its bio data (TotalBioMass), and
/// its lander upgrades. Each player's own.
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

    public ShipHold(ShipLoadout loadout)
    {
        if (loadout.Landers < 0 || loadout.Landers > MaxLanders)
            throw new ArgumentOutOfRangeException(nameof(loadout), $"{loadout.Landers} landers (0 to {MaxLanders})");
        if (loadout.StorageBays < 0 || loadout.Crew < 0)
            throw new ArgumentOutOfRangeException(nameof(loadout), "negative storage bays or crew");
        Crew = loadout.Crew;
        Landers = loadout.Landers;
        StorageCapacity = loadout.StorageBays * StorageBayCapacity;
    }

    public int Crew { get; private set; }
    public int Landers { get; private set; }
    public LanderUpgrades Upgrades { get; set; }

    /// <summary>The minerals the storage bays hold (GetStorageBayCapacity).</summary>
    public int StorageCapacity { get; }

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
        Math.Min(Upgrades.HasFlag(LanderUpgrades.Cargo) ? MaxScrounged << 1 : MaxScrounged, FreeStorage);

    /// <summary>The crew a lander takes now: up to 12, as many as are aboard (AnimateLanderWarmup).</summary>
    public int CrewForLander => Math.Min(LanderCrew, Crew);

    /// <summary>A lander leaves with its crew.</summary>
    public void LanderLeaves(int crew)
    {
        if (Landers == 0)
            throw new InvalidOperationException("no lander aboard");
        if (crew < 0 || crew > CrewForLander)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew (0 to {CrewForLander})");
        Crew -= crew;
    }

    /// <summary>
    /// A lander comes back (PlanetSide): its crew go back aboard and what it
    /// carries goes into the hold. A lander whose crew all died is lost, and
    /// what it carried with it.
    /// </summary>
    public void LanderReturns(int crew, IReadOnlyList<int> minerals, int bioData)
    {
        if (crew < 0 || bioData < 0 || minerals.Count != Categories || minerals.Any(m => m < 0))
            throw new ArgumentOutOfRangeException(nameof(minerals), "bad lander cargo");
        if (crew == 0)
        {
            Landers--;
            return;
        }
        if (minerals.Sum() > FreeStorage)
            throw new InvalidOperationException($"{minerals.Sum()} minerals don't fit in {FreeStorage}");
        Crew += crew;
        for (int i = 0; i < Categories; ++i)
            _minerals[i] += minerals[i];
        BioData += bioData;
    }

    /// <summary>Puts a saved hold back (the loadout gives the storage).</summary>
    internal void Restore(int crew, int landers, IReadOnlyList<int> minerals, int bioData, LanderUpgrades upgrades)
    {
        if (crew < 0 || landers < 0 || landers > MaxLanders || bioData < 0)
            throw new InvalidDataException($"bad hold: {crew} crew, {landers} landers, {bioData} bio data");
        if (minerals.Count != Categories || minerals.Any(m => m < 0))
            throw new InvalidDataException($"bad hold: minerals need {Categories} amounts of at least 0");
        if (minerals.Sum() > StorageCapacity)
            throw new InvalidDataException($"bad hold: {minerals.Sum()} minerals in {StorageCapacity} of storage");
        if ((upgrades & ~LanderUpgrades.All) != 0)
            throw new InvalidDataException($"bad hold: lander upgrades {(int)upgrades}");
        Crew = crew;
        Landers = landers;
        for (int i = 0; i < Categories; ++i)
            _minerals[i] = minerals[i];
        BioData = bioData;
        Upgrades = upgrades;
    }
}
