// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// Interplanetary flight against tests/golden/ip-flight.json: frame-by-frame
/// flights recorded by running UQM's own flight code, replayed here with the
/// same input.
/// </summary>
public class IpFlightTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly JsonElement Reference = Golden.Load("ip-flight");

    [Fact]
    public void ContactShapeIsTheOneTheTracesUse()
    {
        var shape = IpContactShape.Provisional;
        Assert.Equal(shape.ShipRadius, Reference.GetProperty("ship_radius").GetInt32());
        Assert.Equal(shape.PlanetRadius, Reference.GetProperty("planet_radius").Ints());
        Assert.Equal(shape.JunkRadius, Reference.GetProperty("junk_radius").GetInt32());
    }

    public static IEnumerable<object[]> Scenarios() =>
        Reference.GetProperty("scenarios").EnumerateArray()
            .Select(s => new object[] { s.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void FlightMatchesUqmFrameByFrame(string name)
    {
        var sc = Reference.GetProperty("scenarios").EnumerateArray()
            .First(s => s.GetProperty("name").GetString() == name);
        var map = StarMap.Load(DataDir);
        var gen = new SystemGenerator(PlanetTypes.Load(DataDir));
        Star star = map.Stars[sc.GetProperty("star").GetInt32()];
        var flight = new IpFlight(gen.Generate(star), SystemTransition.Arrive(star),
            sc.GetProperty("thrusters").GetInt32(), sc.GetProperty("jets").GetInt32());

        int[] start = sc.GetProperty("start").Ints();
        Assert.Equal(start, new int[]
        {
            flight.Facing, flight.Location.X, flight.Location.Y, flight.Screen.X, flight.Screen.Y, flight.ZoomRadius,
        });

        var failures = new StringBuilder();
        int frame = 0;
        var events = new Dictionary<IpEvent, int>();
        foreach (var f in sc.GetProperty("frames").EnumerateArray())
        {
            int[] e = f.Ints();
            IpEvent ev = flight.Step((ShipInput)e[0]);
            if (ev == IpEvent.EnteredOrbit)
                flight.ReturnFromOrbit();
            events[ev] = events.GetValueOrDefault(ev) + 1;

            var (vdx, vdy) = flight.Velocity.Current();
            int[] got =
            {
                e[0], flight.Facing, vdx, vdy, flight.Location.X, flight.Location.Y, flight.Screen.X,
                flight.Screen.Y, flight.ZoomRadius, flight.InnerPlanet ?? -1, flight.WaitIntersect, (int)ev,
            };
            if (!e.SequenceEqual(got))
            {
                failures.AppendLine($"frame {frame}: expected [{string.Join(", ", e)}]");
                failures.AppendLine($"frame {frame}:      got [{string.Join(", ", got)}]");
                break;   // everything after the first difference differs too
            }
            ++frame;
        }

        Assert.True(failures.Length == 0, $"{name}\n{failures}");
        Assert.True(frame > 500, $"only {frame} frames");
        // Every recorded flight ends by leaving the system, after at least
        // one zoom or inner system on the way.
        Assert.Equal((int)IpEvent.LeftSystem, sc.GetProperty("frames").EnumerateArray().Last()[11].GetInt32());
        Assert.True(events.ContainsKey(IpEvent.Zoom) || events.ContainsKey(IpEvent.EnteredInnerSystem),
            $"{name} never zoomed or entered an inner system");
    }

    [Fact]
    public void TheTracesCoverEveryKindOfEvent()
    {
        var seen = new HashSet<int>();
        foreach (var sc in Reference.GetProperty("scenarios").EnumerateArray())
            foreach (var f in sc.GetProperty("frames").EnumerateArray())
                seen.Add(f[11].GetInt32());
        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4, 5 }, seen);
    }
}
