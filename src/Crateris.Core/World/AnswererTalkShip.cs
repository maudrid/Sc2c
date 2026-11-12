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
/// A visit's talks' ship: the ship of whoever answers (the owner's rule,
/// 2026-11-12: the group as a whole starts the story, and a talk is the
/// talker's). Its names, crew, fuel, landers, resource units and minerals
/// are the answerer's (<see cref="PlayerTalkShip"/>), and when the replies
/// pass to another player (stage 3, decision 7) the talk goes on with that
/// player's ship. What the talks change on each ship is kept
/// (<see cref="Changes"/>), so an opening taken back takes back just that
/// from each ship, whatever else the ship did meanwhile.
/// </summary>
internal sealed class AnswererTalkShip : ITalkShip
{
    private readonly Func<Player> _answerer;
    private readonly GameStrings _strings;
    private readonly PlayerShip _flagship;
    private readonly Dictionary<Player, TalkChanges> _changes = new();

    /// <param name="answerer">Who answers now.</param>
    /// <param name="flagship">The flagship, whose modules the talks read.</param>
    public AnswererTalkShip(Func<Player> answerer, GameStrings strings, PlayerShip flagship)
    {
        _answerer = answerer;
        _strings = strings;
        _flagship = flagship;
    }

    /// <summary>What the talks changed on each player's ship, by player.</summary>
    public IReadOnlyDictionary<Player, TalkChanges> Changes => _changes;

    /// <summary>Forgets what the talks changed on a player's ship (its part put back otherwise).</summary>
    public void Forget(Player player) => _changes.Remove(player);

    private Player Answerer => _answerer();

    private PlayerTalkShip Ship => new(Answerer, _strings, _flagship);

    private TalkChanges Of(Player p)
    {
        if (!_changes.TryGetValue(p, out var c))
            _changes[p] = c = new TalkChanges();
        return c;
    }

    public string CaptainName => Ship.CaptainName;

    public string ShipName => Ship.ShipName;

    public int Crew
    {
        get => Answerer.Ship.Hold.Crew;
        set
        {
            var p = Answerer;
            int before = p.Ship.Hold.Crew;
            p.Ship.Hold.SetCrew(value);
            Of(p).Crew += p.Ship.Hold.Crew - before;
        }
    }

    public int CrewCapacity => Answerer.Ship.Hold.CrewCapacity;

    public uint Fuel
    {
        get => Answerer.Ship.Flight.Fuel;
        set
        {
            var p = Answerer;
            long before = p.Ship.Flight.Fuel;
            p.Ship.Flight.Fuel = value;
            Of(p).Fuel += p.Ship.Flight.Fuel - before;
        }
    }

    public uint FuelCapacity => Answerer.Ship.Flight.FuelCapacity;

    public int Landers
    {
        get => Answerer.Ship.Hold.Landers;
        set
        {
            var p = Answerer;
            int before = p.Ship.Hold.Landers;
            // UQM offers a lander only to a ship short of them; a reply
            // offered before the answerer changed may find this ship full.
            p.Ship.Hold.SetLanders(Math.Clamp(value, 0, ShipHold.MaxLanders));
            Of(p).Landers += p.Ship.Hold.Landers - before;
        }
    }

    public uint ResourceUnits
    {
        get => Answerer.Ship.Hold.ResourceUnits;
        set
        {
            var p = Answerer;
            long before = p.Ship.Hold.ResourceUnits;
            p.Ship.Hold.ResourceUnits = value;
            Of(p).ResourceUnits += p.Ship.Hold.ResourceUnits - before;
        }
    }

    public IList<int> Minerals => new AnswererMinerals(this);

    public int BioData => Answerer.Ship.Hold.BioData;

    public int StorageCapacity => Answerer.Ship.Hold.StorageCapacity;

    public int Thrusters => _flagship.Modules.Thrusters;

    public int Jets => _flagship.Modules.Jets;

    public IReadOnlyList<string?> ModuleSlots => _flagship.Modules.ModuleNames;

    public int EscortsWorth => 0;

    public int EscortRoom => 0;

    public int AddEscorts(int race, int count) => 0;

    // The answerer's minerals, which a talk may sell (set to 0) but not add to.
    private sealed class AnswererMinerals : IList<int>
    {
        private readonly AnswererTalkShip _ship;

        public AnswererMinerals(AnswererTalkShip ship) => _ship = ship;

        private IReadOnlyList<int> Held => _ship.Answerer.Ship.Hold.Minerals;

        public int this[int index]
        {
            get => Held[index];
            set
            {
                var p = _ship.Answerer;
                int before = p.Ship.Hold.Minerals[index];
                p.Ship.Hold.SetMineral(index, value);
                _ship.Of(p).Minerals[index] += p.Ship.Hold.Minerals[index] - before;
            }
        }

        public int Count => Held.Count;

        public bool IsReadOnly => false;

        public IEnumerator<int> GetEnumerator() => Held.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public int IndexOf(int item) => Held.ToList().IndexOf(item);

        public bool Contains(int item) => Held.Contains(item);

        public void CopyTo(int[] array, int arrayIndex) => Held.ToArray().CopyTo(array, arrayIndex);

        public void Add(int item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public void Insert(int index, int item) => throw new NotSupportedException();

        public bool Remove(int item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}

/// <summary>What a visit's talks changed on one ship: each a net change, taken back by subtracting it.</summary>
internal sealed class TalkChanges
{
    public int Crew, Landers;
    public long Fuel, ResourceUnits;
    public int[] Minerals = new int[ShipHold.Categories];

    /// <summary>
    /// Takes these changes back from a ship, each that changed only (a
    /// wreck keeping no crew): from what's aboard, a lander that is out
    /// keeping its own crew and cargo.
    /// </summary>
    public void TakeBackFrom(PlayerShip ship)
    {
        var hold = ship.Hold;
        if (Crew != 0 || ship.Destroyed)
            hold.SetCrew(ship.Destroyed ? 0 : Math.Clamp(hold.Crew - Crew, 0, Math.Max(hold.CrewCapacity, hold.Crew)));
        // A lander that is out stays counted aboard till it's back or lost
        // (LanderReturns counts it off): a lander the talks gave and the ship
        // sent out stays the ship's.
        if (Landers != 0)
            hold.SetLanders(Math.Clamp(hold.Landers - Landers, hold.LanderOut is null ? 0 : 1, ShipHold.MaxLanders));
        if (ResourceUnits != 0)
            hold.ResourceUnits = (uint)Math.Clamp(hold.ResourceUnits - ResourceUnits, 0, uint.MaxValue);
        // Minerals sold come back aboard (MoveCargo adds; SetMineral only lowers).
        var back = new int[Minerals.Length];
        for (int i = 0; i < Minerals.Length; ++i)
        {
            if (Minerals[i] > 0)
                hold.SetMineral(i, Math.Max(0, hold.Minerals[i] - Minerals[i]));
            else
                back[i] = -Minerals[i];
        }
        if (back.Any(m => m > 0))
            hold.MoveCargo(back, 0, +1);
        if (Fuel != 0)
            ship.Flight.Fuel = (uint)Math.Clamp(ship.Flight.Fuel - Fuel, 0, uint.MaxValue);
    }

    /// <summary>A hold and fuel without these changes (each no lower than 0, the crew and landers no more than the ship holds).</summary>
    /// <param name="landersAtLeast">The fewest landers to leave: 1 for a ship whose lander is out and saved as back aboard
    /// (as <see cref="TakeBackFrom"/> keeps it), else 0.</param>
    public (HoldState Hold, uint Fuel) Without(HoldState hold, uint fuel, int crewCapacity, int landersAtLeast = 0) =>
        (hold with
        {
            Crew = Math.Clamp(hold.Crew - Crew, 0, Math.Max(crewCapacity, hold.Crew)),
            Landers = Math.Clamp(hold.Landers - Landers, Math.Min(landersAtLeast, hold.Landers), ShipHold.MaxLanders),
            ResourceUnits = (uint)Math.Clamp(hold.ResourceUnits - ResourceUnits, 0, uint.MaxValue),
            Minerals = hold.Minerals?.Select((m, i) => Math.Max(0, m - Minerals[i])).ToArray(),
        }, (uint)Math.Clamp(fuel - Fuel, 0, uint.MaxValue));
}
