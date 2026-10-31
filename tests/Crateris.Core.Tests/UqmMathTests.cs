// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>The C# math must match UQM's (tests/golden/uqm-math.json).</summary>
public class UqmMathTests
{
    private static readonly System.Text.Json.JsonElement G = Golden.Load("uqm-math");

    [Fact]
    public void Constants_match_uqm()
    {
        var c = G.GetProperty("constants");
        Assert.Equal(Units.LogSpaceWidth, c.GetProperty("LOG_SPACE_WIDTH").GetInt32());
        Assert.Equal(Units.LogSpaceHeight, c.GetProperty("LOG_SPACE_HEIGHT").GetInt32());
        Assert.Equal(Units.MaxXLogical, c.GetProperty("MAX_X_LOGICAL").GetInt32());
        Assert.Equal(Units.MaxYLogical, c.GetProperty("MAX_Y_LOGICAL").GetInt32());
        Assert.Equal(Units.LogUnitsX, c.GetProperty("LOG_UNITS_X").GetInt32());
        Assert.Equal(Units.LogUnitsY, c.GetProperty("LOG_UNITS_Y").GetInt32());
        Assert.Equal(Units.UniverseUnitsX, c.GetProperty("UNIVERSE_UNITS_X").GetInt32());
        Assert.Equal(Units.UniverseUnitsY, c.GetProperty("UNIVERSE_UNITS_Y").GetInt32());
    }

    [Fact]
    public void Sine_table_matches()
    {
        var table = G.GetProperty("sinetab").Ints();
        for (int a = 0; a < table.Length; a++)
            Assert.Equal(table[a], UqmMath.SinVal(a));
    }

    [Fact]
    public void ArcTan_matches()
    {
        foreach (var row in G.GetProperty("arctan").EnumerateArray())
        {
            var v = row.Ints();
            Assert.Equal(v[2], UqmMath.ArcTan((short)v[0], (short)v[1]));
        }
    }

    [Fact]
    public void SquareRoot_matches()
    {
        foreach (var row in G.GetProperty("square_root").EnumerateArray())
        {
            var v = row.Longs();
            Assert.Equal(v[1], UqmMath.SquareRoot((uint)v[0]));
        }
    }

    [Fact]
    public void Universe_and_log_conversions_match()
    {
        foreach (var row in G.GetProperty("universe_to_log").EnumerateArray())
        {
            var v = row.Ints();
            Assert.Equal(v[1], Units.UniverseToLogX((short)v[0]));
            Assert.Equal(v[2], Units.UniverseToLogY((short)v[0]));
        }
        foreach (var row in G.GetProperty("log_to_universe").EnumerateArray())
        {
            var v = row.Ints();
            Assert.Equal(v[1], Units.LogXToUniverse(v[0]));
            Assert.Equal(v[2], Units.LogYToUniverse(v[0]));
        }
    }

    [Fact]
    public void Velocity_operations_match()
    {
        int checkedCases = 0;
        foreach (var c in G.GetProperty("velocity").EnumerateArray())
        {
            var v = new Velocity();
            if (c.TryGetProperty("set_components", out var sc))
            {
                var a = sc.Ints();
                v.SetComponents((short)a[0], (short)a[1]);
            }
            else if (c.TryGetProperty("set_vector", out var sv))
            {
                var a = sv.Ints();
                v.SetVector(a[0], a[1]);
            }
            else
            {
                var a = c.GetProperty("set_then_delta").Ints();
                v.SetComponents((short)a[0], (short)a[1]);
                v.Delta(a[2], a[3]);
            }
            var after = c.GetProperty("after").Ints();
            Assert.Equal(after, new int[] { v.TravelAngle, v.VectorWidth, v.VectorHeight, v.FractWidth,
                v.FractHeight, v.ErrorWidth, v.ErrorHeight, v.IncrWidth, v.IncrHeight });
            var cur = c.GetProperty("current").Ints();
            Assert.Equal((cur[0], cur[1]), ((int)v.Current().dx, (int)v.Current().dy));
            foreach (var step in c.GetProperty("steps").EnumerateArray())
            {
                var s = step.Ints();
                var (dx, dy) = v.Next((ushort)s[0]);
                Assert.Equal(new[] { s[1], s[2], s[3], s[4] }, new int[] { dx, dy, v.ErrorWidth, v.ErrorHeight });
            }
            checkedCases++;
        }
        Assert.Equal(200, checkedCases);
    }
}
