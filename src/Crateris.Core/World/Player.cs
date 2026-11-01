// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

public enum ShipKind
{
    Flagship,
    Escort,
}

/// <summary>What a player is doing (each runs on the host at its own frame rate).</summary>
public enum Activity
{
    /// <summary>Flying in hyperspace, 24 frames a second.</summary>
    Hyperspace,
    /// <summary>Flying in a system's outer view, 30 frames a second.</summary>
    OuterSystem,
    /// <summary>Flying in a planet's inner system, 30 frames a second.</summary>
    InnerSystem,
    /// <summary>In orbit around a planet or moon; the ship doesn't move.</summary>
    Orbit,
}

/// <summary>A player's ship. The host flies the flagship; guests captain escorts.</summary>
public sealed class PlayerShip
{
    public ShipKind Kind { get; }
    public ShipLoadout Loadout { get; }
    /// <summary>
    /// Flight in hyperspace. While in a system it keeps the hyperspace
    /// position the ship returns to: the star's (ExploreSolarSys).
    /// </summary>
    public HyperFlight Flight { get; }

    /// <summary>The star (map number) whose system the ship is in, or null in hyperspace.</summary>
    public int? SystemStar { get; internal set; }

    /// <summary>Flight inside that system, or null in hyperspace.</summary>
    public IpFlight? Ip { get; internal set; }

    /// <summary>In orbit: the world's report, scan seeds and nodes (from <see cref="IpFlight.OrbitData"/>).</summary>
    public OrbitData? Orbit => Ip?.OrbitData;

    public Activity Activity => Ip switch
    {
        null => Activity.Hyperspace,
        { Orbit: not null } => Activity.Orbit,
        { InnerPlanet: not null } => Activity.InnerSystem,
        _ => Activity.OuterSystem,
    };

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
