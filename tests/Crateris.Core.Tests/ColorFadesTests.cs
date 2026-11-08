// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The communication screen's colour fades (docs/plans/stage-3.md, item 11)
/// against UQM's own cmap.c (tests/golden/cmap.json).
/// </summary>
[Trait("Topic", "drawing")]
public class ColorFadesTests
{
    private static readonly JsonElement Recorded = Golden.Load("cmap").GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    // golden/cmap.c's entries: byte k after the map numbers is (SEED * 131 + k * 71 + (k * k) % 251) & 255.
    private static byte[] Entry(int first, int last, int seed)
    {
        var e = new byte[2 + (last - first + 1) * 768];
        e[0] = (byte)first;
        e[1] = (byte)last;
        for (int k = 0; k < e.Length - 2; ++k)
            e[2 + k] = (byte)((seed * 131 + k * 71 + (k * k) % 251) & 255);
        return e;
    }

    private static string Hash(Core.Systems.Rgb[] map)
    {
        uint h = 2166136261;
        foreach (var c in map)
        {
            h = (h ^ c.R) * 16777619;
            h = (h ^ c.G) * 16777619;
            h = (h ^ c.B) * 16777619;
        }
        return h.ToString("x8");
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void FadesAreUqms(string name)
    {
        var sc = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var steps = sc.GetProperty("steps").EnumerateArray().ToList();
        int step = 0;
        // A fade starts at the clock as the last step left it.
        var fades = new ColorFades(new ColorMaps());
        long now = 0;
        foreach (var command in sc.GetProperty("commands").EnumerateArray().Select(c => c.GetString()!))
        {
            var v = command.Split(' ');
            int Arg(int i) => int.Parse(v[i]);
            if (v[0] == "set")
                fades.Maps.Set(Entry(Arg(1), Arg(2), Arg(3)));
            else if (v[0] == "xform")
                fades.Start(Entry(Arg(1), Arg(2), Arg(3)), Arg(4), now);
            else
            {
                now = Arg(1);
                bool changed = fades.Step(now);
                var want = steps[step];
                string where = $"{name}, step {step}";
                Assert.True((want[0].GetInt32() == 1) == changed, $"{where}: changed {changed}");
                var maps = Enumerable.Range(0, ColorMaps.Count).Where(m => fades.Maps[m] != null)
                    .Select(m => $"{m}:{Hash(fades.Maps[m]!)}");
                var wantMaps = want[1].EnumerateArray().Select(m => $"{m[0].GetInt32()}:{m[1].GetString()}");
                Assert.Equal(wantMaps, maps);
                ++step;
            }
        }
        Assert.Equal(steps.Count, step);
    }
}
