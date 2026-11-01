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

/// <summary>
/// One ship as clients see it. Hyperspace fields (LogX .. Facing) are where
/// the ship is in hyperspace, or the star it will come out at while in a
/// system; VelocityX/Y is always hyperspace velocity. System fields are set
/// while in a system: the star, the inner system's planet, the ship's place
/// in the system (IpX/IpY), on the screen (Screen*), its facing and velocity
/// there (IpFacing, IpVelocityX/Y, in UQM's interplanetary units), the outer
/// view's zoom, and the world orbited. Thrusting is the hyperspace flame,
/// or in a system whether the ship thrust last frame.
/// Clients generate systems themselves from the star number.
/// </summary>
public sealed record ShipSnapshot(
    string Id, string Name, bool IsHost, ShipKind Kind,
    int LogX, int LogY, int UniverseX, int UniverseY,
    int Facing, int VelocityX, int VelocityY,
    uint Fuel, uint FuelCapacity, bool Thrusting, bool UndockPending,
    Activity Activity = Activity.Hyperspace,
    int? SystemStar = null, int? InnerPlanet = null,
    int IpX = 0, int IpY = 0, int ScreenX = 0, int ScreenY = 0, int IpFacing = 0, int ZoomRadius = 0,
    int? OrbitPlanet = null, int? OrbitMoon = null, int IpVelocityX = 0, int IpVelocityY = 0);

/// <summary>A world someone is orbiting, with its taken nodes (the masks of <see cref="PlanetWorldState"/>).</summary>
public sealed record OrbitedWorldSnapshot(int Star, int Planet, int? Moon, uint MineralTaken, uint EnergyTaken, uint BiologicalTaken);

public sealed record WorldSnapshot(long Frame, int Year, int Month, int Day, IReadOnlyList<ShipSnapshot> Ships,
    IReadOnlyList<OrbitedWorldSnapshot>? OrbitedWorlds = null);

/// <summary>
/// The host's authoritative game: world state (clock, planets) and every
/// player's state, present or away. Each player is in one activity
/// (<see cref="World.Activity"/>). The world steps at 24 frames a second,
/// hyperspace's rate; players in systems take 30 system frames a second,
/// one or two per world frame (five every four), so both match UQM's
/// rates frame for frame. Not thread-safe; the server calls it from one thread.
/// </summary>
public sealed class GameWorld
{
    private readonly List<Player> _players = new();
    private readonly Dictionary<string, InputBuffer> _inputs = new();
    private readonly HashSet<string> _actionHeld = new();   // the action key was down last frame

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
        // In a system the escort undocks there; with the host in orbit, in
        // the inner system next to the host's world (stage 2, decision 6).
        var hostShip = Host.Ship;
        var host = hostShip.Flight;
        var ship = new PlayerShip(ShipKind.Escort, Settings.EscortLoadout);
        ship.Flight.LogX = host.LogX;
        ship.Flight.LogY = host.LogY;
        ship.Flight.Facing = host.Facing;
        ship.Flight.StarJustLeft = host.StarJustLeft;   // undocking on a star doesn't drop into it
        ship.Flight.ResetFuelCounters();
        if (hostShip.Ip is { } hostIp)
        {
            ship.SystemStar = hostShip.SystemStar;
            ship.Ip = hostIp.JoinAt(ship.Loadout.Thrusters, ship.Loadout.Jets);
        }
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
        _actionHeld.Remove(id);
        player.Ship.Flight.Stop();
        player.Ship.Ip?.Stop();   // in orbit it stays in orbit
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

    /// <summary>System frames (30 a second) to run in world frame <paramref name="frame"/> (24 a second).</summary>
    public static int SystemFramesIn(long frame) =>
        (int)(frame * IpFlight.FrameRate / GameClock.FramesPerSecond
              - (frame - 1) * IpFlight.FrameRate / GameClock.FramesPerSecond);

    /// <summary>Advance the whole game one world frame.</summary>
    public void Step()
    {
        Frame++;
        int systemFrames = SystemFramesIn(Frame);
        foreach (var p in _players)
        {
            if (!p.Present)
                continue;
            ShipInput input = _inputs[p.Id].Take(Frame);
            // The action key acts when pressed, not while held.
            bool actionPressed = (input & ShipInput.Action) != 0 && !_actionHeld.Contains(p.Id);
            if ((input & ShipInput.Action) != 0)
                _actionHeld.Add(p.Id);
            else
                _actionHeld.Remove(p.Id);
            switch (p.Ship.Activity)
            {
                case Activity.Hyperspace:
                    StepHyperspace(p.Ship, input);
                    break;
                case Activity.Orbit:
                    if (actionPressed)
                        LeaveOrbit(p.Ship);
                    break;
                default:
                    for (int i = 0; i < systemFrames && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem; ++i)
                        StepSystem(p.Ship, input);
                    break;
            }
        }
        Clock.Tick(Settings.SecondsPerGameDay);
    }

    private void StepHyperspace(PlayerShip ship, ShipInput input)
    {
        ship.Flight.Step(input & ~ShipInput.Action);
        if (Systems is null)
            return;
        if (StarContact.Check(ship.Flight, Systems.Map) is { } star)
            EnterSystem(ship, star);
    }

    private void StepSystem(PlayerShip ship, ShipInput input)
    {
        IpFlight ip = ship.Ip!;
        switch (ip.Step(input & ~ShipInput.Action))
        {
            case IpEvent.EnteredOrbit:
                if (!ip.OrbitData!.Entered)
                    ip.ReturnFromOrbit();   // UQM starts a conversation here; none exist yet (stage 3)
                break;
            case IpEvent.LeftSystem:
                SystemTransition.Leave(ship.Flight, Systems!.Map.Stars[ship.SystemStar!.Value], ip.Facing);
                ship.Ip = null;
                ship.SystemStar = null;
                break;
        }
    }

    private void EnterSystem(PlayerShip ship, Star star)
    {
        // ExploreSolarSys: the hyperspace position becomes the star's, so the
        // ship comes back out on it.
        ship.Flight.PlaceAtUniverse(star.X, star.Y);
        ship.Flight.Stop();
        ship.SystemStar = star.Number;
        ship.Ip = new IpFlight(Systems![star.Number], SystemTransition.Arrive(star),
            ship.Loadout.Thrusters, ship.Loadout.Jets);
    }

    private static void LeaveOrbit(PlayerShip ship) => ship.Ip!.ReturnFromOrbit();

    /// <summary>
    /// Puts a player straight into orbit around a world, as if they had
    /// flown there (for testing and debugging: --start-orbit). Checks the
    /// world first and changes nothing if it can't be orbited.
    /// </summary>
    public void PutInOrbit(string id, int star, int planet, int? moon = null)
    {
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        if (Systems is null)
            throw new InvalidOperationException("this world has no systems");
        if (!Systems.HasWorld(star, planet, moon))
            throw new ArgumentException($"star {star} has no planet {planet}" + (moon is int m ? $" moon {m}" : ""));
        if (!Systems[star].EnterOrbit(planet, moon).Entered)
            throw new ArgumentException($"star {star} planet {planet} moon {moon}: that world can't be orbited");

        PlayerShip ship = player.Ship;
        if (ship.SystemStar != star)
        {
            if (ship.Ip is not null)
                ship.Ip = null;   // out of the other system, straight into this one
            EnterSystem(ship, Systems.Map.Stars[star]);
        }
        if (ship.Ip!.Orbit is not null)
            LeaveOrbit(ship);
        ship.Ip.PlaceInOrbit(planet, moon);
    }

    public WorldSnapshot Snapshot()
    {
        var present = _players.Where(p => p.Present).ToList();
        var ships = present.Select(p =>
        {
            var f = p.Ship.Flight;
            var ip = p.Ship.Ip;
            var (vx, vy) = f.Velocity.Current();
            var (ivx, ivy) = ip?.Velocity.Current() ?? ((short)0, (short)0);
            return new ShipSnapshot(p.Id, p.Name, p.IsHost, p.Ship.Kind, f.LogX, f.LogY, f.UniverseX, f.UniverseY,
                f.Facing, vx, vy, f.Fuel, f.FuelCapacity,
                ip is null ? (f.Status & HyperFlight.StatusThrust) != 0 : ip.Orbit is null && ip.Thrusting,
                p.UndockPending, p.Ship.Activity, p.Ship.SystemStar, ip?.InnerPlanet,
                ip?.Location.X ?? 0, ip?.Location.Y ?? 0, ip?.Screen.X ?? 0, ip?.Screen.Y ?? 0,
                ip?.Facing ?? 0, ip?.ZoomRadius ?? 0, ip?.Orbit?.Planet, ip?.Orbit?.Moon, ivx, ivy);
        }).ToList();

        // Worlds being orbited, with what has been taken from them, so an
        // orbiting player's scan shows what's left right now.
        var orbited = present
            .Where(p => p.Ship.Ip?.Orbit is not null)
            .Select(p => new WorldId(p.Ship.SystemStar!.Value, p.Ship.Ip!.Orbit!.Value.Planet, p.Ship.Ip.Orbit.Value.Moon))
            .Distinct()
            .Select(w => new OrbitedWorldSnapshot(w.Star, w.Planet, w.Moon,
                Planets.RetrieveMask(w, ScanType.Mineral), Planets.RetrieveMask(w, ScanType.Energy),
                Planets.RetrieveMask(w, ScanType.Biological)))
            .ToList();

        return new WorldSnapshot(Frame, Clock.Year, Clock.Month, Clock.Day, ships, orbited);
    }
}
