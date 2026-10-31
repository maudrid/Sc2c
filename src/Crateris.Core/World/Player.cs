// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;

namespace Crateris.Core.World;

public enum ShipKind
{
    Flagship,
    Escort,
}

/// <summary>A player's ship. The host flies the flagship; guests captain escorts.</summary>
public sealed class PlayerShip
{
    public ShipKind Kind { get; }
    public ShipLoadout Loadout { get; }
    public HyperFlight Flight { get; }

    public PlayerShip(ShipKind kind, ShipLoadout loadout)
    {
        Kind = kind;
        Loadout = loadout;
        Flight = new HyperFlight(ShipDrive.ForHyperspace(loadout.Thrusters, loadout.Jets),
            Hyperspace.Fuel.TankCapacity(loadout.FuelTanks, loadout.HighEfficiencyTanks))
        {
            Fuel = loadout.Fuel,
        };
    }
}

/// <summary>
/// A player in the host's game, kept in the host's save while away.
/// The id is stable per player (the client keeps it), so a returning
/// player gets their own ship back.
/// </summary>
public sealed class Player
{
    public string Id { get; }
    public string Name { get; set; }
    public bool IsHost { get; }
    public PlayerShip Ship { get; }

    /// <summary>Connected and flying. Absent players' ships wait in the save, not in space.</summary>
    public bool Present { get; set; }

    /// <summary>Set on a guest's first join until their client has shown the undocking scene.</summary>
    public bool UndockPending { get; set; }

    public Player(string id, string name, bool isHost, PlayerShip ship)
    {
        Id = id;
        Name = name;
        IsHost = isHost;
        Ship = ship;
    }
}
