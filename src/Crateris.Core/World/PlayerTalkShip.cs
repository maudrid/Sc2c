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
/// allied commander buys), and its escort fleet; but the flagship's
/// modules, as the allied commander grades them (decision 4: the
/// flagship carries the mission and its upgrades). Ships have
/// no escort fleets of UQM's kind until combat (stage 4) brings ships to
/// buy and win: until then a fleet is worth nothing and has no room, so
/// the Arilou's gift of ships waits (docs/differences.md).
/// </summary>
internal sealed class PlayerTalkShip : ITalkShip
{
    private readonly Player _player;
    private readonly PlayerShip _flagship;
    private readonly IList<int> _minerals;

    /// <param name="flagship">The flagship, whose modules the talk reads.</param>
    public PlayerTalkShip(Player player, GameStrings strings, PlayerShip flagship)
    {
        _player = player;
        _flagship = flagship;
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

    public int EscortsWorth => 0;

    public int EscortRoom => 0;

    public int AddEscorts(int race, int count) => 0;

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
