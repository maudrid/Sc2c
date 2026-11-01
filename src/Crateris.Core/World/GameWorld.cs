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

public enum JoinResult
{
    /// <summary>First time in this game: a new escort undocks from the host.</summary>
    Joined,
    /// <summary>Back again: the player's ship returns where they left it.</summary>
    Rejoined,
    /// <summary>Already connected.</summary>
    AlreadyPresent,
    /// <summary>The game already has the most players allowed.</summary>
    Full,
}

public sealed record ShipSnapshot(
    string Id, string Name, bool IsHost, ShipKind Kind,
    int LogX, int LogY, int UniverseX, int UniverseY,
    int Facing, int VelocityX, int VelocityY,
    uint Fuel, uint FuelCapacity, bool Thrusting, bool UndockPending);

public sealed record WorldSnapshot(long Frame, int Year, int Month, int Day, IReadOnlyList<ShipSnapshot> Ships);

/// <summary>
/// The host's authoritative game: world state (clock) and every player's
/// state, present or away. Stage 1 has one activity, hyperspace.
/// Not thread-safe; the server calls it from one thread.
/// </summary>
public sealed class GameWorld
{
    private readonly List<Player> _players = new();
    private readonly Dictionary<string, InputBuffer> _inputs = new();

    public GameSettings Settings { get; }
    public GameClock Clock { get; }
    public long Frame { get; private set; }

    /// <summary>Planets and moons: taken nodes, shared by every player.</summary>
    public PlanetWorldState Planets { get; }

    /// <summary>The star map and its systems, when the world has them (always in the game; optional in tests).</summary>
    public SystemCatalog? Systems { get; }

    public IReadOnlyList<Player> Players => _players;

    public Player Host => _players.Single(p => p.IsHost);

    public GameWorld(GameSettings settings, GameClock clock, long frame = 0, SystemCatalog? systems = null)
    {
        Settings = settings;
        Clock = clock;
        Frame = frame;
        Systems = systems;
        Planets = new PlanetWorldState(systems);
    }

    /// <summary>A new game with the host in the flagship, in hyperspace at the start position.</summary>
    public static GameWorld NewGame(GameSettings settings, NewGameData data, string hostId, string hostName,
        SystemCatalog? systems = null)
    {
        var world = new GameWorld(settings, new GameClock(data.Year, data.Month, data.Day), 0, systems);
        var ship = new PlayerShip(ShipKind.Flagship, data.Flagship);
        ship.Flight.PlaceAtUniverse(data.StartUniverseX, data.StartUniverseY);
        ship.Flight.ResetFuelCounters();
        ship.Flight.StarJustLeft = data.StartStar;
        world.AddPlayer(new Player(hostId, hostName, isHost: true, ship) { Present = true });
        return world;
    }

    /// <summary>Restore a player from a save (not present until they rejoin).</summary>
    public void AddPlayer(Player player)
    {
        if (_players.Any(p => p.Id == player.Id))
            throw new ArgumentException($"Player {player.Id} is already in the game.");
        if (player.IsHost && _players.Any(p => p.IsHost))
            throw new ArgumentException("The game already has a host.");
        _players.Add(player);
        _inputs[player.Id] = new InputBuffer();
    }

    public Player? Find(string id) => _players.FirstOrDefault(p => p.Id == id);

    public JoinResult Join(string id, string name)
    {
        var existing = Find(id);
        if (existing is { Present: true })
            return JoinResult.AlreadyPresent;
        if (_players.Count(p => p.Present) >= Settings.MaxPlayers)
            return JoinResult.Full;

        if (existing != null)
        {
            existing.Name = name;
            existing.Present = true;
            _inputs[id].Clear();
            return JoinResult.Rejoined;
        }

        // First join: a new escort at the host's position, shown undocking
        // from the flagship to this player only (docs/differences.md row 14).
        var host = Host.Ship.Flight;
        var ship = new PlayerShip(ShipKind.Escort, Settings.EscortLoadout);
        ship.Flight.LogX = host.LogX;
        ship.Flight.LogY = host.LogY;
        ship.Flight.Facing = host.Facing;
        ship.Flight.StarJustLeft = host.StarJustLeft;   // undocking on a star doesn't drop into it
        ship.Flight.ResetFuelCounters();
        AddPlayer(new Player(id, name, isHost: false, ship) { Present = true, UndockPending = true });
        return JoinResult.Joined;
    }

    /// <summary>
    /// Leave or disconnect: the ship vanishes and waits, stopped, in the save
    /// until the player rejoins.
    /// </summary>
    public void Leave(string id)
    {
        var player = Find(id);
        if (player == null)
            return;
        player.Present = false;
        _inputs[id].Clear();
        player.Ship.Flight.Stop();
    }

    /// <summary>The client has shown the undocking scene.</summary>
    public void UndockShown(string id)
    {
        if (Find(id) is { } p)
            p.UndockPending = false;
    }

    public void SubmitInput(string id, long frame, ShipInput input)
    {
        if (Find(id) is { Present: true })
            _inputs[id].Submit(frame, input);
    }

    /// <summary>Advance the whole game one frame.</summary>
    public void Step()
    {
        Frame++;
        foreach (var p in _players)
        {
            if (!p.Present)
                continue;
            p.Ship.Flight.Step(_inputs[p.Id].Take(Frame));
        }
        Clock.Tick(Settings.SecondsPerGameDay);
    }

    public WorldSnapshot Snapshot() => new(Frame, Clock.Year, Clock.Month, Clock.Day,
        _players.Where(p => p.Present).Select(p =>
        {
            var f = p.Ship.Flight;
            var (vx, vy) = f.Velocity.Current();
            return new ShipSnapshot(p.Id, p.Name, p.IsHost, p.Ship.Kind, f.LogX, f.LogY, f.UniverseX, f.UniverseY,
                f.Facing, vx, vy, f.Fuel, f.FuelCapacity, (f.Status & HyperFlight.StatusThrust) != 0,
                p.UndockPending);
        }).ToList());
}
