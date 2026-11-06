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
/// their crew, fuel, landers, resource units and minerals.
/// </summary>
internal sealed class PlayerTalkShip : ITalkShip
{
    private readonly Player _player;
    private readonly IList<int> _minerals;

    public PlayerTalkShip(Player player, GameStrings strings)
    {
        _player = player;
        CaptainName = ShipNames.Captain(player, strings);
        ShipName = ShipNames.Ship(player, strings);
        // The hold's minerals, read only until a script sells them (item 8).
        _minerals = new System.Collections.ObjectModel.ReadOnlyCollection<int>((IList<int>)player.Ship.Hold.Minerals);
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
}
