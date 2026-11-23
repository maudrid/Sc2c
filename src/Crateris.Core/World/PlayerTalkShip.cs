// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Content;

namespace Crateris.Core.World;

/// <summary>
/// A player's ship as a talk sees it (stage 3, decision 4): their names,
/// their crew, fuel, landers, resource units and minerals (which the
/// allied commander buys); but the flagship's modules, as the allied
/// commander grades them (decision 4: the flagship carries the mission
/// and its upgrades); and the escort fleet, the host's (stage 4b,
/// decision 3: only the host has one): worth what it is for the host,
/// nothing for a guest, and the ships talks give join it, are looked for
/// in it and are taken from it whoever answers (the stage 3b-2 plan's
/// question 4).
/// </summary>
internal sealed class PlayerTalkShip : ITalkShip
{
    private readonly Player _player;
    private readonly GameWorld _world;
    private readonly PlayerShip _flagship;
    private readonly IList<int> _minerals;

    /// <param name="world">The world: its flagship, whose modules the talk reads, and the host's fleet.</param>
    public PlayerTalkShip(Player player, GameStrings strings, GameWorld world)
    {
        _player = player;
        _world = world;
        _flagship = world.Host.Ship;
        CaptainName = ShipNames.Captain(player, strings);
        ShipName = ShipNames.Ship(player, strings);
        _minerals = new HoldMinerals(player.Ship.Hold);
    }

    public string CaptainName { get; }

    public string ShipName { get; }

    private ShipHold Hold => _player.Ship.Hold;

    public int Crew
    {
        get => Hold.Crew;
        set => Hold.SetCrew(value);
    }

    public int CrewCapacity => Hold.CrewCapacity;

    public uint Fuel
    {
        get => _player.Ship.Flight.Fuel;
        set => _player.Ship.Flight.Fuel = value;
    }

    public uint FuelCapacity => _player.Ship.Flight.FuelCapacity;

    public int Landers
    {
        get => Hold.Landers;
        set => Hold.SetLanders(value);
    }

    public uint ResourceUnits
    {
        get => Hold.ResourceUnits;
        set => Hold.ResourceUnits = value;
    }

    public IList<int> Minerals => _minerals;

    public int BioData
    {
        get => Hold.BioData;
        set => Hold.SetBioData(value);
    }

    public int StorageCapacity => Hold.StorageCapacity;

    // GLOBAL_SIS (log_x), log_y: in a system the star's (ExploreSolarSys), in hyperspace the ship's own.
    public (int X, int Y) LogPlace => _player.Ship.SystemStar is int star && _world.Systems?.Map.Stars[star] is { } s
        ? (Uqm.Units.UniverseToLogX(s.X), Uqm.Units.UniverseToLogY(s.Y))
        : (_player.Ship.Flight.LogX, _player.Ship.Flight.LogY);

    public int Thrusters => _flagship.Modules.Thrusters;

    public int Jets => _flagship.Modules.Jets;

    public IReadOnlyList<string?> ModuleSlots => _flagship.Modules.ModuleNames;

    // The host's fleet (stage 4b item 6): its worth for the host's talks, none for a guest's (a guest has no fleet,
    // decision 3); its room and the ships given whoever answers (decision 3: they join the host's fleet).
    public int EscortsWorth => _player.IsHost && _world.Story is { } story ? _world.Fleet.Worth(story.Rules) : 0;

    public int EscortRoom => _world.Fleet.Room;

    // The host's fleet's ships, whoever answers (the stage 3b-2 plan's question 4): a guest sees the host's escorts.
    public IReadOnlyList<int> EscortRaces => EscortRacesOf(_world);

    /// <summary>The host's fleet's ships' races (races.h's numbers), in its order (built_ship_q's: by slot).</summary>
    internal static IReadOnlyList<int> EscortRacesOf(GameWorld world) =>
        world.Story is { } story ? world.Fleet.Ships.Select(s => story.Rules.RaceIndex(s.Race)).ToList() : Array.Empty<int>();

    // Taken from the host's fleet, whoever answers (question 4), as gifts go to it.
    public int RemoveEscorts(int race, int count) =>
        _world.Story is { } story && race >= 0 && race < story.Rules.Races.Count
            ? count - _world.Fleet.RemoveSome(story.Rules.Races[race], count)
            : 0;

    public int AddEscorts(int race, int count, Func<uint> random) =>
        _world.Story is { } story && race >= 0 && race < story.Rules.Races.Count
            ? _world.Fleet.Add(story.Rules, story.Rules.Races[race], count, random)
            : 0;

    public void SetEscortCrewComplement(int race, int crew, int captain)
    {
        if (_world.Story is { } story && race >= 0 && race < story.Rules.Races.Count)
            _world.Fleet.SetCrewComplement(story.Rules, story.Rules.Races[race], crew, (byte)captain);
    }

    // ---- the Melnorme's (stage 3b-2 item 4): the answering ship's own

    // The answerer's credits, read and written live (a gift mid-talk counts).
    public uint Credits
    {
        get => Hold.Credits;
        set => Hold.Credits = value;
    }

    public LanderUpgrades Upgrades
    {
        get => Hold.Upgrades;
        set => Hold.Upgrades = value;
    }

    public int RescueRefused
    {
        get => _player.Ship.MelnormeRescueRefused;
        set => _player.Ship.MelnormeRescueRefused = value;
    }

    public int RescueCount
    {
        get => _player.Ship.MelnormeRescueCount;
        set => _player.Ship.MelnormeRescueCount = value;
    }

    // The answering ship's own slots (a guest's escort's: stage 4 item 8's note), the ship refitted as they change.
    public IList<byte> OwnDriveSlots => new Slots(_player.Ship, ShipModules.DriveSlotCount, m => m.DriveSlots, (m, i, v) => m.SetDrive(i, v));

    public IList<byte> OwnJetSlots => new Slots(_player.Ship, ShipModules.JetSlotCount, m => m.JetSlots, (m, i, v) => m.SetJet(i, v));

    public IList<byte> OwnModuleSlots =>
        new Slots(_player.Ship, ShipModules.ModuleSlotCount, m => m.ModuleSlots, (m, i, v) => m.SetModule(i, v));

    public int KitCrew => _player.Ship.Modules.KitCrew;

    // A ship's slots of a kind as a list a talk reads and changes: each change refits the ship.
    private sealed class Slots : IList<byte>
    {
        private readonly PlayerShip _ship;
        private readonly Func<ShipModules, IReadOnlyList<byte>> _get;
        private readonly Action<ShipModules, int, byte> _set;

        public Slots(PlayerShip ship, int count, Func<ShipModules, IReadOnlyList<byte>> get, Action<ShipModules, int, byte> set)
        {
            _ship = ship;
            Count = count;
            _get = get;
            _set = set;
        }

        public byte this[int index]
        {
            get => _get(_ship.Modules)[index];
            set
            {
                if (_get(_ship.Modules)[index] == value)
                    return;
                _set(_ship.Modules, index, value);
                _ship.Refit();
            }
        }

        public int Count { get; }

        public bool IsReadOnly => false;

        public IEnumerator<byte> GetEnumerator() => _get(_ship.Modules).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(byte item) => _get(_ship.Modules).ToList().IndexOf(item);

        public bool Contains(byte item) => _get(_ship.Modules).Contains(item);

        public void CopyTo(byte[] array, int arrayIndex) => _get(_ship.Modules).ToArray().CopyTo(array, arrayIndex);

        public void Add(byte item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public void Insert(int index, byte item) => throw new NotSupportedException();

        public bool Remove(byte item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    // The hold's minerals, which a talk may sell (set to 0) but not add to.
    private sealed class HoldMinerals : IList<int>
    {
        private readonly ShipHold _hold;

        public HoldMinerals(ShipHold hold) => _hold = hold;

        public int this[int index]
        {
            get => _hold.Minerals[index];
            set => _hold.SetMineral(index, value);
        }

        public int Count => _hold.Minerals.Count;

        public bool IsReadOnly => false;

        public IEnumerator<int> GetEnumerator() => _hold.Minerals.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(int item) => _hold.Minerals.ToList().IndexOf(item);

        public bool Contains(int item) => _hold.Minerals.Contains(item);

        public void CopyTo(int[] array, int arrayIndex) => _hold.Minerals.ToArray().CopyTo(array, arrayIndex);

        public void Add(int item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public void Insert(int index, int item) => throw new NotSupportedException();

        public bool Remove(int item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
