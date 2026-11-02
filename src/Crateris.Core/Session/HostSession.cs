// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>
/// The host's side of a game, without Godot: the world, the guests by
/// their network peer, their input, saves, and the testing start options.
/// The Godot layer moves messages and files; this decides what they do.
/// The host's own player plays through the same input path, with the same
/// delay (<see cref="Step"/>).
/// </summary>
public sealed class HostSession
{
    private readonly Dictionary<long, string> _peers = new();

    private HostSession(GameWorld world) => World = world;

    public GameWorld World { get; }

    /// <summary>The host's player id (a loaded game's is the save's).</summary>
    public string HostId => World.Host.Id;

    /// <summary>Guests in the game, by peer.</summary>
    public int Guests => _peers.Count;

    public event Action<string>? Logged;

    /// <summary>A new game: the host in the flagship, in hyperspace at the start.</summary>
    public static HostSession New(GameSettings settings, NewGameData data, string hostId, string hostName,
        SystemCatalog systems, ContactShapes shapes) =>
        new(GameWorld.NewGame(settings, data, hostId, hostName, systems, shapes));

    /// <summary>A saved game; the host (the save's) joins it under <paramref name="hostName"/>.</summary>
    public static HostSession Load(string json, string hostName, SystemCatalog systems, ContactShapes shapes)
    {
        var world = HostSave.FromJson(json, systems, shapes);
        world.Join(world.Host.Id, hostName);
        return new HostSession(world);
    }

    public string SaveJson() => HostSave.ToJson(World);

    /// <summary>
    /// --start-at: the host's ship in hyperspace at universe coordinates, at
    /// rest. Only from hyperspace: a game loaded with the host in a system
    /// or in orbit is left where it is (InvalidOperationException).
    /// </summary>
    public string StartAt(short x, short y)
    {
        var host = World.Host.Ship;
        if (host.Activity != Activity.Hyperspace)
            throw new InvalidOperationException($"the host is in {host.Activity}, not hyperspace");
        host.Flight.PlaceAtUniverse(x, y);
        host.Flight.Stop();
        host.Flight.StarJustLeft = null;
        return $"Starting in hyperspace at {x}:{y}";
    }

    /// <summary>--start-orbit: the host's ship in orbit there (ArgumentException if there's no such world).</summary>
    public string StartInOrbit(int star, int planet, int? moon)
    {
        World.PutInOrbit(HostId, star, planet, moon);
        return $"Starting in orbit: star {star} planet {planet}" + (moon is int m ? $" moon {m}" : "");
    }

    /// <summary>
    /// A guest says hello: refused if its version differs, otherwise joined
    /// (or refused, if the game is full). The welcome goes back to it; an
    /// accepted guest's input and undocking are taken from its peer from now on.
    /// </summary>
    public Protocol.Welcome Hello(long peer, string playerId, string name, int version)
    {
        JoinResult result;
        string message;
        if (version != Protocol.Version)
        {
            result = JoinResult.Full;
            message = $"Version mismatch: host {Protocol.Version}, you {version}.";
        }
        else
        {
            result = World.Join(playerId, name);
            message = Protocol.Describe(result);
        }
        Logged?.Invoke($"{name} ({playerId}): {result}");
        if (Protocol.Accepted(result))
            _peers[peer] = playerId;
        return new Protocol.Welcome(result, playerId, World.Settings.InputDelayFrames, message);
    }

    /// <summary>A guest's input for a frame (only the four input bits count).</summary>
    public void GuestInput(long peer, long frame, int input)
    {
        if (_peers.TryGetValue(peer, out var id))
            World.SubmitInput(id, frame, (ShipInput)(input & 15));
    }

    public void GuestUndockShown(long peer)
    {
        if (_peers.TryGetValue(peer, out var id))
            World.UndockShown(id);
    }

    /// <summary>A guest's connection has gone: it leaves the game (its ship is kept for its return).</summary>
    public void GuestLeft(long peer)
    {
        if (_peers.Remove(peer, out var id))
        {
            World.Leave(id);
            Logged?.Invoke($"{id} left");
        }
    }

    /// <summary>One world frame: the host's input (with the game's input delay), the step, and the snapshot for everyone.</summary>
    public WorldSnapshot Step(ShipInput hostInput)
    {
        World.SubmitInput(HostId, World.Frame + World.Settings.InputDelayFrames, hostInput);
        World.Step();
        return World.Snapshot();
    }
}
