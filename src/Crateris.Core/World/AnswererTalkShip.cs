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
    private readonly GameWorld _world;
    private readonly PlayerShip _flagship;
    private readonly Dictionary<Player, TalkChanges> _changes = new();

    /// <param name="answerer">Who answers now.</param>
    /// <param name="world">The world: its flagship, whose modules the talks read, and the host's fleet.</param>
    public AnswererTalkShip(Func<Player> answerer, GameStrings strings, GameWorld world)
    {
        _answerer = answerer;
        _strings = strings;
        _world = world;
        _flagship = world.Host.Ship;
    }

    /// <summary>What the talks changed on each player's ship, by player.</summary>
    public IReadOnlyDictionary<Player, TalkChanges> Changes => _changes;

    /// <summary>Forgets what the talks changed on a player's ship (its part put back otherwise).</summary>
    public void Forget(Player player) => _changes.Remove(player);

    private Player Answerer => _answerer();

    private PlayerTalkShip Ship => new(Answerer, _strings, _world);

    private TalkChanges Of(Player p)
    {
        if (!_changes.TryGetValue(p, out var c))
            _changes[p] = c = new TalkChanges();
        return c;
    }

    public string CaptainName => Ship.CaptainName;

    public (int X, int Y) LogPlace => Ship.LogPlace;

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

    public int BioData
    {
        get => Answerer.Ship.Hold.BioData;
        set
        {
            var p = Answerer;
            int before = p.Ship.Hold.BioData;
            p.Ship.Hold.SetBioData(value);
            Of(p).BioData += p.Ship.Hold.BioData - before;
        }
    }

    public int StorageCapacity => Answerer.Ship.Hold.StorageCapacity;

    public int Thrusters => _flagship.Modules.Thrusters;

    public int Jets => _flagship.Modules.Jets;

    public IReadOnlyList<string?> ModuleSlots => _flagship.Modules.ModuleNames;

    // The host's fleet (stage 4b item 6): its worth for the host's talks, none for a guest's (a guest has no fleet,
    // decision 3); its room and the ships given whoever answers (decision 3: they join the host's fleet).
    public int EscortsWorth => Answerer.IsHost && _world.Story is { } story ? _world.Fleet.Worth(story.Rules) : 0;

    public int EscortRoom => _world.Fleet.Room;

    // The host's fleet's ships, whoever answers (the stage 3b-2 plan's question 4).
    public IReadOnlyList<int> EscortRaces => PlayerTalkShip.EscortRacesOf(_world);

    // Taken from the host's fleet, whoever answers (question 4). A ship the
    // talks gave and now take back is no longer among those an opening
    // taken back takes from the fleet (EscortsAdded): it's gone already.
    // One the talks didn't give isn't put back by an opening taken back:
    // that can't arise, as no opening talk takes ships (the commander's
    // and the Ilwrath's at Earth; the Orz's RemoveEscortShips is a meeting
    // talk's, and the allied commander's from the menu, which is no
    // opening, never calls it).
    public int RemoveEscorts(int race, int count)
    {
        if (_world.Story is not { } story || race < 0 || race >= story.Rules.Races.Count)
            return 0;
        var before = _world.Fleet.Ships.ToList();
        int removed = count - _world.Fleet.RemoveSome(story.Rules.Races[race], count);
        var gone = before.Except(_world.Fleet.Ships).ToList();
        _escortsAdded.RemoveAll(gone.Contains);
        return removed;
    }

    public int AddEscorts(int race, int count, Func<uint> random)
    {
        if (_world.Story is not { } story || race < 0 || race >= story.Rules.Races.Count)
            return 0;
        var before = _world.Fleet.Ships.ToList();
        int added = _world.Fleet.Add(story.Rules, story.Rules.Races[race], count, random);
        _escortsAdded.AddRange(_world.Fleet.Ships.Except(before));
        return added;
    }

    private readonly List<FleetShip> _escortsAdded = new();

    // A ship the talks gave stays told by its slot, race and captain once its captain is set.
    public void SetEscortCrewComplement(int race, int crew, int captain)
    {
        if (_world.Story is not { } story || race < 0 || race >= story.Rules.Races.Count
            || _world.Fleet.SetCrewComplement(story.Rules, story.Rules.Races[race], crew, (byte)captain) is not { } changed)
            return;
        int i = _escortsAdded.IndexOf(changed.Was);
        if (i >= 0)
            _escortsAdded[i] = changed.Is;
    }

    /// <summary>
    /// The ships the talks gave the host's fleet, as they joined it (each
    /// told by its slot, race and captain: its crew may change since, in a
    /// battle or the roster): what an opening taken back takes from the
    /// fleet, and a save during it leaves out; those the talks took back
    /// again aren't among them.
    /// </summary>
    public IReadOnlyList<FleetShip> EscortsAdded => _escortsAdded;

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
    public int Crew, Landers, BioData;
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
        if (BioData != 0)
            hold.SetBioData(Math.Clamp(hold.BioData - BioData, 0, ushort.MaxValue));
    }

    /// <summary>A hold and fuel without these changes (each no lower than 0, the crew and landers no more than the ship holds).</summary>
    /// <param name="landersAtLeast">The fewest landers to leave: 1 for a ship whose lander is out and saved as back aboard
    /// (as <see cref="TakeBackFrom"/> keeps it), else 0.</param>
    /// <param name="crewOut">The crew of a lander that is out, counted in the hold: the talks' crew is taken from those
    /// aboard only, as <see cref="TakeBackFrom"/> takes it.</param>
    public (HoldState Hold, uint Fuel) Without(HoldState hold, uint fuel, int crewCapacity, int landersAtLeast = 0, int crewOut = 0) =>
        (hold with
        {
            Crew = Crew == 0 ? hold.Crew
                : Math.Clamp(hold.Crew - crewOut - Crew, 0, Math.Max(crewCapacity, hold.Crew - crewOut)) + crewOut,
            Landers = Math.Clamp(hold.Landers - Landers, Math.Min(landersAtLeast, hold.Landers), ShipHold.MaxLanders),
            ResourceUnits = (uint)Math.Clamp(hold.ResourceUnits - ResourceUnits, 0, uint.MaxValue),
            Minerals = hold.Minerals?.Select((m, i) => Math.Max(0, m - Minerals[i])).ToArray(),
            BioData = Math.Clamp(hold.BioData - BioData, 0, ushort.MaxValue),
        }, (uint)Math.Clamp(fuel - Fuel, 0, uint.MaxValue));
}
