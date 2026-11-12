// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/sis.h's slots and modules,
// sis.c's GetCrewPodCapacity, GetStorageBayCapacity, GetFuelTankCapacity
// and CountSISPieces), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.World;

/// <summary>
/// A ship's slots, as UQM's flagship has them (SIS_STATE's DriveSlots,
/// JetSlots and ModuleSlots): 11 drive slots, each a fusion thruster or
/// empty; 8 jet slots, each turning jets or empty; and 16 module slots.
/// Each holds a module's number (sis.h's: PLANET_LANDER 0 to BOMB_MODULE_5
/// 19), or <see cref="EmptyDrive"/>, <see cref="EmptyJet"/> or
/// <see cref="EmptyModule"/>. What the ship holds follows from them: its
/// thrusters and jets, its crew pods (50 each), storage bays (500 each)
/// and fuel tanks, as sis.c counts them.
/// <para>
/// An escort's kit holds crew of its own beside its modules (stage 3,
/// decision 10: <see cref="KitCrew"/>, 10); a flagship's none.
/// </para>
/// </summary>
public sealed class ShipModules
{
    /// <summary>NUM_DRIVE_SLOTS, NUM_JET_SLOTS and NUM_MODULE_SLOTS (sis.h).</summary>
    public const int DriveSlotCount = 11, JetSlotCount = 8, ModuleSlotCount = 16;

    /// <summary>sis.h's module numbers.</summary>
    public const byte PlanetLander = 0, FusionThruster = 1, TurningJets = 2, CrewPod = 3, StorageBay = 4,
        FuelTank = 5, HighEfficiencyFuelSystem = 6, DynamoUnit = 7, ShivaFurnace = 8, GunWeapon = 9,
        BlasterWeapon = 10, CannonWeapon = 11, TrackingSystem = 12, AntimissileDefense = 13,
        PurchaseModules = 14, BombModule0 = 14, Modules = 20;

    /// <summary>EMPTY_SLOT and the empty marks of each kind of slot (EMPTY_SLOT + 0, 1, 2; + 3 a lander's).</summary>
    public const byte EmptySlot = Modules, EmptyDrive = EmptySlot + 0, EmptyJet = EmptySlot + 1,
        EmptyModule = EmptySlot + 2, EmptyLander = EmptySlot + 3;

    /// <summary>NUM_BOMB_MODULES: the module slots the Chmmr's bomb takes (CHMMR_BOMB_STATE 3).</summary>
    public const int BombModules = 10;

    /// <summary>Each module's name, by number (data/modules.json's order).</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "PLANET_LANDER", "FUSION_THRUSTER", "TURNING_JETS", "CREW_POD", "STORAGE_BAY", "FUEL_TANK",
        "HIGHEFF_FUELSYS", "DYNAMO_UNIT", "SHIVA_FURNACE", "GUN_WEAPON", "BLASTER_WEAPON", "CANNON_WEAPON",
        "TRACKING_SYSTEM", "ANTIMISSILE_DEFENSE", "BOMB_MODULE_0", "BOMB_MODULE_1", "BOMB_MODULE_2",
        "BOMB_MODULE_3", "BOMB_MODULE_4", "BOMB_MODULE_5",
    };

    private readonly byte[] _drives = new byte[DriveSlotCount];
    private readonly byte[] _jets = new byte[JetSlotCount];
    private readonly byte[] _modules = new byte[ModuleSlotCount];

    /// <summary>Slots as given (each its module's number or its kind's empty mark); a bad one is an ArgumentException.</summary>
    public ShipModules(IReadOnlyList<byte> drives, IReadOnlyList<byte> jets, IReadOnlyList<byte> modules, int kitCrew = 0)
    {
        if (drives.Count != DriveSlotCount || jets.Count != JetSlotCount || modules.Count != ModuleSlotCount)
            throw new ArgumentException($"{DriveSlotCount} drive, {JetSlotCount} jet and {ModuleSlotCount} module slots");
        if (drives.Any(d => d != FusionThruster && d != EmptyDrive))
            throw new ArgumentException("a drive slot holds a fusion thruster or nothing", nameof(drives));
        if (jets.Any(j => j != TurningJets && j != EmptyJet))
            throw new ArgumentException("a jet slot holds turning jets or nothing", nameof(jets));
        if (modules.Any(m => m != EmptyModule && (m < CrewPod || m >= Modules)))
            throw new ArgumentException("a module slot holds a module (CREW_POD and after) or nothing", nameof(modules));
        if (kitCrew < 0)
            throw new ArgumentOutOfRangeException(nameof(kitCrew));
        for (int i = 0; i < DriveSlotCount; ++i)
            _drives[i] = drives[i];
        for (int i = 0; i < JetSlotCount; ++i)
            _jets[i] = jets[i];
        for (int i = 0; i < ModuleSlotCount; ++i)
            _modules[i] = modules[i];
        KitCrew = kitCrew;
    }

    /// <summary>
    /// The slots a loadout says a ship has (a flagship's crew in crew pods
    /// only: its capacity a multiple of theirs). A new game's flagship has them
    /// in InitGameStructures' places (data/new-game.json: thrusters in
    /// drive slots 5 and 6, jets in 0 and 6, a fuel tank in module slot 1,
    /// a crew pod in 2, a storage bay in 8 and the gun in 15); more of a
    /// kind go in the empty slots after. An escort's kit holds its crew
    /// itself, and has its own guns (<see cref="ShipLoadout.Guns"/>, stage
    /// 4, decision 3), put first, in the weapon slots in the order UQM
    /// fires from them (<see cref="WeaponSlots"/>: the first in the nose);
    /// a flagship's loadout has none (its gun is UQM's own, in 15).
    /// </summary>
    public static ShipModules FromLoadout(ShipKind kind, ShipLoadout loadout)
    {
        var drives = Enumerable.Repeat(EmptyDrive, DriveSlotCount).ToArray();
        var jets = Enumerable.Repeat(EmptyJet, JetSlotCount).ToArray();
        var modules = Enumerable.Repeat(EmptyModule, ModuleSlotCount).ToArray();
        if (kind == ShipKind.Flagship && loadout.Guns != 0)
            throw new ArgumentException($"a flagship's gun is UQM's own, not {loadout.Guns} of its loadout's");
        if (loadout.Guns < 0 || PutGuns(modules, loadout.Guns) != loadout.Guns)
            throw new ArgumentException($"no room for {loadout.Guns} guns in the {WeaponSlots.Count} weapon slots");

        static void Put(byte[] slots, byte empty, byte piece, int count, int first)
        {
            for (int i = first; count > 0 && i < slots.Length + first; ++i)
                if (slots[i % slots.Length] == empty)
                {
                    slots[i % slots.Length] = piece;
                    --count;
                }
            if (count > 0)
                throw new ArgumentException($"no room for {Names[piece]}");
        }

        Put(drives, EmptyDrive, FusionThruster, loadout.Thrusters, 5);
        Put(jets, EmptyJet, TurningJets, Math.Min(loadout.Jets, 1), 0);
        Put(jets, EmptyJet, TurningJets, loadout.Jets - Math.Min(loadout.Jets, 1), 6);
        int pods = 0, kitCrew = loadout.CrewCapacity;
        if (kind == ShipKind.Flagship)
        {
            // A flagship holds crew in crew pods only, as UQM's does.
            if (loadout.CrewCapacity % ShipLoadout.CrewPodCapacity != 0)
                throw new ArgumentException($"a flagship's crew is held in crew pods of {ShipLoadout.CrewPodCapacity}, "
                                            + $"not {loadout.CrewCapacity}");
            Put(modules, EmptyModule, GunWeapon, 1, 15);
            pods = loadout.CrewCapacity / ShipLoadout.CrewPodCapacity;
            kitCrew = 0;
        }
        Put(modules, EmptyModule, FuelTank, loadout.FuelTanks, 1);
        Put(modules, EmptyModule, HighEfficiencyFuelSystem, loadout.HighEfficiencyTanks, 1);
        Put(modules, EmptyModule, CrewPod, pods, 2);
        Put(modules, EmptyModule, StorageBay, loadout.StorageBays, 8);
        return new ShipModules(drives, jets, modules, kitCrew);
    }

    /// <summary>
    /// The module slots UQM's flagship fires weapons from, in the order
    /// InitWeaponSlots reads them (sis_ship.c): 15 the nose, 14 a spread
    /// pair, 13 a pair to both sides, 0 the tail. Outfitting puts guns,
    /// blasters and cannons only there (DoInstallModule).
    /// </summary>
    public static readonly IReadOnlyList<int> WeaponSlots = new[] { ModuleSlotCount - 1, ModuleSlotCount - 2, ModuleSlotCount - 3, 0 };

    /// <summary>A gun, blaster or cannon (GUN_WEAPON to CANNON_WEAPON).</summary>
    public static bool IsWeapon(byte piece) => piece is >= GunWeapon and <= CannonWeapon;

    /// <summary>Whether a weapon is in a slot it fires from (<see cref="WeaponSlots"/>): what the ship fires in battle.</summary>
    public bool HasWeapon => WeaponSlots.Any(s => IsWeapon(_modules[s]));

    // Guns in the free weapon slots in firing order, as many as fit of count: how many went in.
    private static int PutGuns(byte[] modules, int count)
    {
        int put = 0;
        foreach (int slot in WeaponSlots)
            if (put < count && modules[slot] == EmptyModule)
            {
                modules[slot] = GunWeapon;
                ++put;
            }
        return put;
    }

    /// <summary>
    /// Gives an escort saved before it had guns its kit's (stage 4a item 8):
    /// unless it has a weapon (<see cref="HasWeapon"/>), up to
    /// <paramref name="count"/> guns in its free weapon slots, in the order
    /// UQM fires from them (15, 14, 13, then 0); none where every weapon
    /// slot is full. How many it was given.
    /// </summary>
    internal int GiveKitGuns(int count)
    {
        if (HasWeapon || count <= 0)
            return 0;
        int given = PutGuns(_modules, count);
        if (given > 0)
        {
            ++Version;
            _saved = null;
        }
        return given;
    }

    public IReadOnlyList<byte> DriveSlots => _drives;

    public IReadOnlyList<byte> JetSlots => _jets;

    public IReadOnlyList<byte> ModuleSlots => _modules;

    /// <summary>Crew the ship holds beside its crew pods (an escort's kit's).</summary>
    public int KitCrew { get; }

    /// <summary>Changed each time a slot does.</summary>
    public int Version { get; private set; }

    public int Thrusters => _drives.Count(d => d == FusionThruster);

    public int Jets => _jets.Count(j => j == TurningJets);

    /// <summary>CountSISPieces for a module.</summary>
    public int Count(byte module) => _modules.Count(m => m == module);

    /// <summary>GetCrewPodCapacity: 50 a crew pod, and the kit's own.</summary>
    public int CrewCapacity => Count(CrewPod) * ShipLoadout.CrewPodCapacity + KitCrew;

    /// <summary>GetStorageBayCapacity: 500 a storage bay.</summary>
    public int StorageCapacity => Count(StorageBay) * ShipHold.StorageBayCapacity;

    /// <summary>GetFuelTankCapacity: FUEL_RESERVE, and each tank's.</summary>
    public uint FuelCapacity => Hyperspace.Fuel.TankCapacity(Count(FuelTank), Count(HighEfficiencyFuelSystem));

    /// <summary>The module slots by name (CREW_POD and so on), null if empty.</summary>
    public IReadOnlyList<string?> ModuleNames => _modules.Select(m => m == EmptyModule ? null : Names[m]).ToList();

    /// <summary>Sets a drive slot (a fusion thruster or <see cref="EmptyDrive"/>).</summary>
    internal void SetDrive(int slot, byte piece)
    {
        if (piece != FusionThruster && piece != EmptyDrive)
            throw new ArgumentException($"a drive slot can't hold {piece}");
        _drives[slot] = piece;
        ++Version;
        _saved = null;
    }

    /// <summary>Sets a jet slot (turning jets or <see cref="EmptyJet"/>).</summary>
    internal void SetJet(int slot, byte piece)
    {
        if (piece != TurningJets && piece != EmptyJet)
            throw new ArgumentException($"a jet slot can't hold {piece}");
        _jets[slot] = piece;
        ++Version;
        _saved = null;
    }

    /// <summary>Sets a module slot (CREW_POD and after, or <see cref="EmptyModule"/>).</summary>
    internal void SetModule(int slot, byte piece)
    {
        if (piece != EmptyModule && (piece < CrewPod || piece >= Modules))
            throw new ArgumentException($"a module slot can't hold {piece}");
        _modules[slot] = piece;
        ++Version;
        _saved = null;
    }

    /// <summary>The slots as saved and sent (made again only when they change).</summary>
    public ModulesState Save() => _saved ??= new(_drives.ToArray(), _jets.ToArray(), _modules.ToArray(), KitCrew);

    private ModulesState? _saved;

    /// <summary>Saved slots; a bad one is an <see cref="InvalidDataException"/>.</summary>
    public static ShipModules Restore(ModulesState s)
    {
        try
        {
            return new ShipModules(s.Drives ?? throw new ArgumentException("no drive slots"),
                s.Jets ?? throw new ArgumentException("no jet slots"),
                s.Modules ?? throw new ArgumentException("no module slots"), s.KitCrew);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"bad ship slots: {e.Message}", e);
        }
    }
}

/// <summary>A ship's slots as saved and sent (<see cref="ShipModules"/>).</summary>
public sealed record ModulesState(byte[]? Drives, byte[]? Jets, byte[]? Modules, int KitCrew)
{
    public bool Equals(ModulesState? other) =>
        other is not null && KitCrew == other.KitCrew
        && (Drives ?? Array.Empty<byte>()).SequenceEqual(other.Drives ?? Array.Empty<byte>())
        && (Jets ?? Array.Empty<byte>()).SequenceEqual(other.Jets ?? Array.Empty<byte>())
        && (Modules ?? Array.Empty<byte>()).SequenceEqual(other.Modules ?? Array.Empty<byte>());

    public override int GetHashCode() => HashCode.Combine(KitCrew, Drives?.Length, Modules?.Length);
}
