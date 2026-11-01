// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// The system views' layout against tests/golden/system-layout.json, what
/// UQM's own DrawSystem would draw for every star: orbit ellipses and
/// planets at each zoom, and every planet's inner view with its moons.
/// </summary>
public class SystemLayoutTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void EverySystemIsLaidOutAsUqmDrawsIt()
    {
        var catalog = SystemCatalog.Load(DataDir);
        var failures = new StringBuilder();
        int checkedWorlds = 0;
        foreach (var sys in Golden.Load("system-layout").GetProperty("systems").EnumerateArray())
        {
            int star = sys.GetProperty("star").GetInt32();
            SolarSystem system = catalog[star];
            foreach (var row in sys.GetProperty("outer").EnumerateArray())
            {
                int[] v = row.Ints();
                int zoom = v[0], p = v[1];
                WorldDesc planet = system.Planets[p];
                var rect = SystemLayout.OuterOrbit(planet, zoom);
                var at = SystemLayout.OuterPlanet(planet, zoom);
                int size = SystemLayout.ImageSize(system.Generator, planet.DataIndex, SystemLayout.OuterSizeAdd(zoom));
                var got = new[] { zoom, p, rect.X, rect.Y, rect.Width, rect.Height, at.X, at.Y, size };
                if (!got.SequenceEqual(v))
                    failures.AppendLine($"star {star} planet {p} zoom {zoom}: [{string.Join(", ", got)}], UQM [{string.Join(", ", v)}]");
                checkedWorlds++;
            }
            foreach (var inner in sys.GetProperty("inner").EnumerateArray())
            {
                int p = inner.GetProperty("planet").GetInt32();
                WorldDesc planet = system.Planets[p];
                var arc = SystemLayout.InnerPlanetOrbit(planet);
                int[] wantArc = inner.GetProperty("arc").Ints();
                if (!new[] { arc.X, arc.Y, arc.Width, arc.Height }.SequenceEqual(wantArc))
                    failures.AppendLine($"star {star} planet {p}: arc {arc}, UQM [{string.Join(", ", wantArc)}]");
                int size = SystemLayout.ImageSize(system.Generator, planet.DataIndex, SystemLayout.InnerPlanetSizeAdd);
                if (size != inner.GetProperty("size").GetInt32())
                    failures.AppendLine($"star {star} planet {p}: inner size {size}");
                var moons = system.Moons(p);
                var want = inner.GetProperty("moons").EnumerateArray().Select(m => m.Ints()).ToList();
                if (moons.Length != want.Count)
                {
                    failures.AppendLine($"star {star} planet {p}: {moons.Length} moons, UQM {want.Count}");
                    continue;
                }
                for (int m = 0; m < moons.Length; ++m)
                {
                    var r = SystemLayout.InnerMoonOrbit(moons[m]);
                    var at = SystemLayout.InnerMoon(moons[m]);
                    int ms = SystemLayout.ImageSize(system.Generator, moons[m].DataIndex, SystemLayout.MoonSizeAdd);
                    var got = new[] { r.X, r.Y, r.Width, r.Height, at.X, at.Y, ms };
                    if (!got.SequenceEqual(want[m]))
                        failures.AppendLine($"star {star} planet {p} moon {m}: [{string.Join(", ", got)}], UQM [{string.Join(", ", want[m])}]");
                    checkedWorlds++;
                }
            }
        }
        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(checkedWorlds > 10000, $"only {checkedWorlds} checked");
    }

    [Fact]
    public void SunGrowsAsTheViewZoomsIn()
    {
        Assert.Equal(0, SystemLayout.SunSize(IpGeometry.MaxZoomRadius));
        Assert.Equal(1, SystemLayout.SunSize(IpGeometry.MaxZoomRadius >> 1));
        Assert.Equal(2, SystemLayout.SunSize(IpGeometry.MaxZoomRadius >> 2));
        Assert.Equal(2, SystemLayout.SunSize(IpGeometry.MinZoomRadius));
    }

    [Fact]
    public void OrbitColoursAreUqms()
    {
        // MAKE_RGB15 (0x00, 0x0B, 0x00): 5-bit 0x0B widens to 0x5A.
        Assert.Equal(new Rgb(0, 0x5A, 0), SystemLayout.OrbitColors[2]);
        Assert.Equal(new Rgb(0x7B, 0x42, 0), SystemLayout.OrbitColors[4]);
    }
}
