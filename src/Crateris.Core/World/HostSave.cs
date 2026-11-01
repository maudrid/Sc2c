// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Serialization;
using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// The host's save: world state and every player's state, including
/// players who are away, in readable JSON. Players are loaded as away,
/// their ships stopped where they were, and come back when they rejoin.
/// </summary>
public static class HostSave
{
    /// <summary>
    /// 2 added the star each ship has just left (FlightState.StarJustLeft);
    /// 3 added planet world state (Worlds); 4 added ships in systems and in
    /// orbit (PlayerState.SystemStar, PlayerState.System). Older saves
    /// still load: their ships are in hyperspace and not on a star, and no
    /// world has been touched.
    /// </summary>
    public const int Version = 4;
    public const int OldestReadableVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public sealed record SaveFile(
        int Version, GameSettings Settings, ClockState Clock, long Frame, List<PlayerState> Players,
        List<WorldState>? Worlds = null);

    /// <summary>
    /// One planet or moon with stored state: moon null for the planet itself;
    /// taken nodes as UQM's three retrieve masks.
    /// </summary>
    public sealed record WorldState(
        int Star, int Planet, int? Moon, uint MineralTaken, uint EnergyTaken, uint BiologicalTaken);

    public sealed record ClockState(int Year, int Month, int Day, int FramesIntoDay);

    public sealed record FlightState(
        int LogX, int LogY, int Facing, uint Fuel, int Status, int TurnWait, int ThrustWait,
        int ViewX, int ViewY, int FuelDistX, int FuelDistY, int FuelDistExtra, int FuelTicks,
        int[] Velocity, int? StarJustLeft = null);

    public sealed record PlayerState(
        string Id, string Name, bool IsHost, ShipKind Kind, ShipLoadout Loadout, bool UndockPending,
        FlightState Flight, int? SystemStar = null, IpFlightState? System = null);

    public static string ToJson(GameWorld world)
    {
        var players = world.Players.Select(p =>
        {
            var f = p.Ship.Flight;
            var v = f.Velocity;
            return new PlayerState(p.Id, p.Name, p.IsHost, p.Ship.Kind, p.Ship.Loadout, p.UndockPending,
                new FlightState(f.LogX, f.LogY, f.Facing, f.Fuel, f.Status, f.TurnWait, f.ThrustWait,
                    f.ViewX, f.ViewY, f.FuelDistX, f.FuelDistY, f.FuelDistExtra, f.FuelTicks,
                    new int[] { v.TravelAngle, v.VectorWidth, v.VectorHeight, v.FractWidth, v.FractHeight,
                        v.ErrorWidth, v.ErrorHeight, v.IncrWidth, v.IncrHeight },
                    f.StarJustLeft),
                p.Ship.SystemStar, p.Ship.Ip?.Save());
        }).ToList();
        var c = world.Clock;
        var worlds = world.Planets.Worlds
            .OrderBy(w => w.Key.Star).ThenBy(w => w.Key.Planet).ThenBy(w => w.Key.Moon ?? -1)
            .Select(w => new WorldState(w.Key.Star, w.Key.Planet, w.Key.Moon,
                w.Value[ScanType.Mineral], w.Value[ScanType.Energy], w.Value[ScanType.Biological]))
            .ToList();
        var save = new SaveFile(Version, world.Settings, new ClockState(c.Year, c.Month, c.Day, c.FramesIntoDay),
            world.Frame, players, worlds);
        return JsonSerializer.Serialize(save, Options);
    }

    /// <summary>Loads a save. With a catalog, saved worlds must exist in their systems.</summary>
    public static GameWorld FromJson(string json, SystemCatalog? systems = null)
    {
        var save = JsonSerializer.Deserialize<SaveFile>(json, Options)
            ?? throw new InvalidDataException("Empty save file.");
        if (save.Version < OldestReadableVersion || save.Version > Version)
            throw new InvalidDataException(
                $"Save version {save.Version} is not supported (expected {OldestReadableVersion} to {Version}).");

        var clock = new GameClock(save.Clock.Year, save.Clock.Month, save.Clock.Day)
        {
            FramesIntoDay = save.Clock.FramesIntoDay,
        };
        var world = new GameWorld(save.Settings, clock, save.Frame, systems);
        foreach (var ps in save.Players)
        {
            var ship = new PlayerShip(ps.Kind, ps.Loadout);
            var f = ship.Flight;
            var s = ps.Flight;
            f.LogX = s.LogX;
            f.LogY = s.LogY;
            f.Facing = (ushort)s.Facing;
            f.Fuel = s.Fuel;
            f.Status = (ushort)s.Status;
            f.TurnWait = (byte)s.TurnWait;
            f.ThrustWait = (byte)s.ThrustWait;
            f.ViewX = (short)s.ViewX;
            f.ViewY = (short)s.ViewY;
            f.FuelDistX = (ushort)s.FuelDistX;
            f.FuelDistY = (ushort)s.FuelDistY;
            f.FuelDistExtra = (ushort)s.FuelDistExtra;
            f.FuelTicks = (byte)s.FuelTicks;
            if (s.StarJustLeft is int star && (star < 0 || star >= StarMap.SolarSystemCount))
                throw new InvalidDataException($"Player {ps.Id}: star_just_left {star} is not a star.");
            f.StarJustLeft = s.StarJustLeft;
            var v = s.Velocity;
            if (v.Length != 9)
                throw new InvalidDataException($"Player {ps.Id}: velocity needs 9 values.");
            f.Velocity = new Uqm.Velocity
            {
                TravelAngle = (ushort)v[0], VectorWidth = (short)v[1], VectorHeight = (short)v[2],
                FractWidth = (short)v[3], FractHeight = (short)v[4], ErrorWidth = (short)v[5],
                ErrorHeight = (short)v[6], IncrWidth = (short)v[7], IncrHeight = (short)v[8],
            };
            // Everyone is away after loading, and away ships wait stopped.
            f.Stop();
            if (ps.SystemStar is int systemStar)
            {
                if (systems is null)
                    throw new InvalidDataException($"Player {ps.Id} is in a system, but no star map was given to load it.");
                if (systemStar < 0 || systemStar >= StarMap.SolarSystemCount || ps.System is null)
                    throw new InvalidDataException($"Player {ps.Id}: bad system {systemStar}.");
                ship.SystemStar = systemStar;
                ship.Ip = IpFlight.Restore(systems[systemStar], ps.System, ps.Loadout.Thrusters, ps.Loadout.Jets);
                ship.Ip.Stop();
                if (ship.Ip.OrbitData is { Entered: false })
                    throw new InvalidDataException($"Player {ps.Id}: in orbit around a world that can't be orbited.");
            }
            else if (ps.System is not null)
                throw new InvalidDataException($"Player {ps.Id}: system flight without a system.");
            world.AddPlayer(new Player(ps.Id, ps.Name, ps.IsHost, ship)
            {
                Present = false,
                UndockPending = ps.UndockPending,
            });
        }
        var seen = new HashSet<WorldId>();
        foreach (var w in save.Worlds ?? new List<WorldState>())
        {
            try
            {
                var id = new WorldId(w.Star, w.Planet, w.Moon);
                if (!seen.Add(id))
                    throw new InvalidDataException($"World {id} is in the save twice.");
                world.Planets.Restore(id, w.MineralTaken, w.EnergyTaken, w.BiologicalTaken);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new InvalidDataException($"Bad world in save: {e.Message}", e);
            }
        }
        return world;
    }
}
