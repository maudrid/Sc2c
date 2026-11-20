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
/// nothing for a guest, and the ships talks give join it whoever answers.
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

    public int BioData => Hold.BioData;

    public int StorageCapacity => Hold.StorageCapacity;

    public int Thrusters => _flagship.Modules.Thrusters;

    public int Jets => _flagship.Modules.Jets;

    public IReadOnlyList<string?> ModuleSlots => _flagship.Modules.ModuleNames;

    // The host's fleet (stage 4b item 6): its worth for the host's talks, none for a guest's (a guest has no fleet,
    // decision 3); its room and the ships given whoever answers (decision 3: they join the host's fleet).
    public int EscortsWorth => _player.IsHost && _world.Story is { } story ? _world.Fleet.Worth(story.Rules) : 0;

    public int EscortRoom => _world.Fleet.Room;

    public int AddEscorts(int race, int count, Func<uint> random) =>
        _world.Story is { } story && race >= 0 && race < story.Rules.Races.Count
            ? _world.Fleet.Add(story.Rules, story.Rules.Races[race], count, random)
            : 0;

    public void SetEscortCrewComplement(int race, int crew, int captain)
    {
        if (_world.Story is { } story && race >= 0 && race < story.Rules.Races.Count)
            _world.Fleet.SetCrewComplement(story.Rules, story.Rules.Races[race], crew, (byte)captain);
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
