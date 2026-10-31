// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;

namespace Crateris.Core.Tests;

/// <summary>
/// Hyperspace flight must match UQM frame by frame
/// (tests/golden/hyperspace-flight.json, recorded from UQM's own code).
/// </summary>
public class HyperFlightTests
{
    private static readonly System.Text.Json.JsonElement G = Golden.Load("hyperspace-flight");

    public static IEnumerable<object[]> Scenarios() =>
        G.GetProperty("scenarios").EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Flight_matches_uqm_frame_by_frame(string name)
    {
        var s = G.GetProperty("scenarios").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        int thrusters = s.GetProperty("thrusters").GetInt32();
        int jets = s.GetProperty("jets").GetInt32();
        int tanks = s.GetProperty("tanks").GetInt32();

        var drive = ShipDrive.ForHyperspace(thrusters, jets);
        var ship = s.GetProperty("ship");
        Assert.Equal(ship.GetProperty("max_thrust").GetInt32(), drive.MaxThrust);
        Assert.Equal(ship.GetProperty("thrust_increment").GetInt32(), drive.ThrustIncrement);
        Assert.Equal(ship.GetProperty("thrust_wait").GetInt32(), drive.ThrustWait);
        Assert.Equal(ship.GetProperty("turn_wait").GetInt32(), drive.TurnWait);

        var flight = new HyperFlight(drive, Fuel.TankCapacity(tanks))
        {
            Fuel = (uint)s.GetProperty("fuel").GetInt64(),
        };
        flight.PlaceAtUniverse((short)s.GetProperty("start_x").GetInt32(), (short)s.GetProperty("start_y").GetInt32());
        flight.ResetFuelCounters();

        var frames = s.GetProperty("frames").EnumerateArray().ToList();
        int f = 0;
        foreach (var step in s.GetProperty("input").EnumerateArray())
        {
            var st = step.Ints();
            for (int i = 0; i < st[0]; i++, f++)
            {
                flight.Step((ShipInput)st[1]);
                var e = frames[f].Longs();
                var (vdx, vdy) = flight.Velocity.Current();
                long[] actual =
                {
                    flight.Facing, vdx, vdy, flight.LogX, flight.LogY, flight.Fuel,
                    flight.Status & 0x187, flight.TurnWait, flight.ThrustWait,
                };
                Assert.True(e.SequenceEqual(actual),
                    $"{name}: frame {f} differs.\n UQM:  [{string.Join(", ", e)}]\n ours: [{string.Join(", ", actual)}]");
            }
        }
        Assert.Equal(frames.Count, f);
    }

    [Fact]
    public void Stops_at_the_edge_of_the_universe()
    {
        var flight = new HyperFlight(ShipDrive.ForHyperspace(11, 8), Fuel.TankCapacity(1)) { Fuel = 5000 };
        flight.PlaceAtUniverse(5, 5000);
        flight.Facing = 12; // west
        for (int i = 0; i < 200; i++)
            flight.Step(ShipInput.Thrust);
        Assert.Equal(0, flight.LogX);
        Assert.Equal(0, flight.UniverseX);
    }
}
