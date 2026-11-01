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

namespace Crateris.Core.World;

/// <summary>
/// The host's save: world state and every player's state, including
/// players who are away, in readable JSON. Players are loaded as away,
/// their ships stopped where they were, and come back when they rejoin.
/// </summary>
public static class HostSave
{
    /// <summary>
    /// 2 added the star each ship has just left (FlightState.StarJustLeft).
    /// Version 1 saves still load; their ships count as not on a star.
    /// </summary>
    public const int Version = 2;
    public const int OldestReadableVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public sealed record SaveFile(
        int Version, GameSettings Settings, ClockState Clock, long Frame, List<PlayerState> Players);

    public sealed record ClockState(int Year, int Month, int Day, int FramesIntoDay);

    public sealed record FlightState(
        int LogX, int LogY, int Facing, uint Fuel, int Status, int TurnWait, int ThrustWait,
        int ViewX, int ViewY, int FuelDistX, int FuelDistY, int FuelDistExtra, int FuelTicks,
        int[] Velocity, int? StarJustLeft = null);

    public sealed record PlayerState(
        string Id, string Name, bool IsHost, ShipKind Kind, ShipLoadout Loadout, bool UndockPending,
        FlightState Flight);

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
                    f.StarJustLeft));
        }).ToList();
        var c = world.Clock;
        var save = new SaveFile(Version, world.Settings, new ClockState(c.Year, c.Month, c.Day, c.FramesIntoDay),
            world.Frame, players);
        return JsonSerializer.Serialize(save, Options);
    }

    public static GameWorld FromJson(string json)
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
        var world = new GameWorld(save.Settings, clock, save.Frame);
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
            world.AddPlayer(new Player(ps.Id, ps.Name, ps.IsHost, ship)
            {
                Present = false,
                UndockPending = ps.UndockPending,
            });
        }
        return world;
    }
}
