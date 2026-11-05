// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>Surface height maps against UQM's own GeneratePlanetSurface for every world (tests/golden/surface-heights.json).</summary>
[Trait("Topic", "planet-maps")]
public class SurfaceMapTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    internal static WorldDesc World(int star, int planet, int moon) =>
        moon < 0 ? Systems[star].Planets[planet] : Systems[star].Moons(planet)[moon];

    internal static byte[] Bytes(sbyte[] map) => (byte[])(Array)map;

    // Every world's height map, made once for the planet-maps tests that
    // need it (it's checked against UQM's here; the light maps, colours and
    // planetside maps are made from it). Don't change the arrays.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Type, uint Seed), Lazy<sbyte[]>> HeightCache = new();

    /// <summary>A world's height map (<see cref="SurfaceMap.Generate"/>), made once for every test.</summary>
    internal static sbyte[] HeightMap(Data.PlanetType type, uint seed) =>
        HeightCache.GetOrAdd((type.Index, seed), _ => new Lazy<sbyte[]>(() => SurfaceMap.Generate(type, seed))).Value;

    [Fact]
    public void EveryWorldsHeightMapIsTheOneUqmMakes()
    {
        var golden = Golden.Load("surface-heights");
        var maps = new List<(string Where, WorldDesc Desc, string Want)>();
        foreach (var w in golden.GetProperty("worlds").EnumerateArray())
        {
            int star = w[0].GetInt32(), planet = w[1].GetInt32(), moon = w[2].GetInt32();
            bool entered = w[3].GetInt32() == 1, defined = w[4].GetInt32() == 1;
            string where = $"star {star} planet {planet} moon {moon}";
            Assert.True(entered == Systems[star].EnterOrbit(planet, moon < 0 ? null : moon).Entered, $"{where}: entered");
            if (entered && !defined)
                maps.Add((where, World(star, planet, moon), w[5].GetString()!));
        }
        Assert.True(maps.Count > 3700, $"only {maps.Count} maps");

        // The maps are independent, so they are made on every core.
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(maps.Sample(), m =>
        {
            int type = WorldTypes.PlanetType(m.Desc.DataIndex);
            var map = HeightMap(Systems.Generator.Types[type], m.Desc.RandSeed);
            if (m.Want != Convert.ToHexString(SHA256.HashData(Bytes(map))).ToLowerInvariant())
                wrong.Add($"{m.Where} (type {type})");
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} height maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [Fact]
    public void AFewWholeMapsMatchPointForPoint()
    {
        foreach (var f in Golden.Load("surface-heights").GetProperty("full").EnumerateArray())
        {
            int star = f.GetProperty("world")[0].GetInt32(), planet = f.GetProperty("world")[1].GetInt32(),
                moon = f.GetProperty("world")[2].GetInt32();
            var desc = World(star, planet, moon);
            var map = Bytes(SurfaceMap.Generate(Systems.Generator.Types[WorldTypes.PlanetType(desc.DataIndex)], desc.RandSeed));
            var want = Convert.FromHexString(f.GetProperty("heights").GetString()!);
            int first = Enumerable.Range(0, want.Length).FirstOrDefault(i => want[i] != map[i], -1);
            Assert.True(first < 0, $"star {star} planet {planet}: first difference at x {first % SurfaceMap.Width}, y {first / SurfaceMap.Width}: {(sbyte)map[Math.Max(first, 0)]}, UQM {(sbyte)want[Math.Max(first, 0)]}");
        }
    }
}
