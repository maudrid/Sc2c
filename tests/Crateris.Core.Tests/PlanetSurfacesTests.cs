// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using Crateris.Core.Content;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// Coloured surface maps against UQM's own RenderTopography for every world
/// (tests/golden/surface-colors.json), binary tables against UQM's
/// _GetBinaryTableData (binary-tables.json), and Earth's own surface.
/// </summary>
public class PlanetSurfacesTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static byte[] RgbBytes(Rgb[] colors)
    {
        var b = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; ++i)
        {
            b[i * 3] = colors[i].R;
            b[i * 3 + 1] = colors[i].G;
            b[i * 3 + 2] = colors[i].B;
        }
        return b;
    }

    [ContentFact]
    public void EveryBinaryTableIsReadAsUqmReadsIt()
    {
        int tables = 0;
        TestContent.With(package =>
        {
            foreach (var t in Golden.Load("binary-tables").GetProperty("tables").EnumerateObject())
            {
                var table = BinaryTable.Load(package, t.Name);
                var want = t.Value.GetProperty("entries").EnumerateArray().ToList();
                Assert.True(want.Count == table.Count, $"{t.Name}: {table.Count} entries, UQM {want.Count}");
                for (int i = 0; i < want.Count; ++i)
                {
                    var e = table[i].ToArray();
                    Assert.True(want[i][0].GetInt32() == e.Length && want[i][1].GetString() == Hash(e), $"{t.Name} entry {i} differs from UQM's");
                }
                tables++;
            }
            Assert.Equal(package.Resources.Values.Count(r => r.Type == "BINTAB"), tables);
        });
    }

    [ContentFact]
    public void EveryWorldsColouredMapIsTheOneUqmDraws()
    {
        var golden = Golden.Load("surface-colors").GetProperty("worlds").EnumerateArray()
            .Where(w => w[3].ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(w => (Star: w[0].GetInt32(), Planet: w[1].GetInt32(), Moon: w[2].GetInt32(), Want: w[3].GetString()!))
            .ToList();
        Assert.True(golden.Count > 3700, $"only {golden.Count} maps");
        // Systems and orbits are worked out first, one at a time; the maps then on every core.
        foreach (var w in golden)
            Systems[w.Star].EnterOrbit(w.Planet, w.Moon < 0 ? null : w.Moon);
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(golden, w =>
        {
            var s = surfaces.Generated(Systems[w.Star], w.Planet, w.Moon < 0 ? null : w.Moon);
            if (Hash(RgbBytes(s.Colors)) != w.Want)
                wrong.Add($"star {w.Star} planet {w.Planet} moon {w.Moon}");
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} coloured maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [ContentFact]
    public void EarthHasItsOwnSurface()
    {
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var earth = surfaces.Generated(sol, 2, null);
        Assert.Equal(SurfaceMap.Width * SurfaceMap.Height, earth.Colors.Length);
        Assert.True(earth.Heights.Distinct().Count() > 20, "Earth's elevations are flat");
        // Mostly sea: more blue than red over the whole map.
        Assert.True(earth.Colors.Sum(c => c.B) > earth.Colors.Sum(c => c.R), "Earth isn't mostly blue");
        Assert.Same(earth, surfaces.Generated(sol, 2, null));
    }

    [ContentFact]
    public void AShieldedWorldsMapHasTheShieldsRedAdded()
    {
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        Assert.True(WorldTypes.IsShielded(sol.Planets[2].DataIndex), "Earth is shielded in a new game");
        var plain = surfaces.Generated(sol, 2, null);
        var shown = surfaces.For(sol, 2, null);
        Assert.All(shown.Colors, c => Assert.Equal(255, c.R));
        Assert.Equal(plain.Colors.Select(c => (c.G, c.B)), shown.Colors.Select(c => (c.G, c.B)));
        // An unshielded world is shown as made.
        Assert.Equal(surfaces.Generated(sol, 3, null).Colors, surfaces.For(sol, 3, null).Colors);
    }

    [Fact]
    public void BlendsAreUqmsAdditiveDrawing()
    {
        // renderpixel_additive at full strength: a clipped sum.
        Assert.Equal(new Rgb(255, 20, 255), PlanetSurfaces.AddClipped(new Rgb(200, 20, 250), new Rgb(100, 0, 10)));
        // modulated_sum at 127: dc + ((sc * 127) >> 8), clipped; 255 * 127 >> 8 is 126, not 127.
        Assert.Equal(new Rgb(126, 136, 255), PlanetSurfaces.AddModulated(new Rgb(0, 10, 200), new Rgb(255, 255, 255), 127));
        Assert.Equal(new Rgb(10, 10, 10), PlanetSurfaces.AddModulated(new Rgb(10, 10, 10), new Rgb(1, 1, 1), 127));
    }
}
