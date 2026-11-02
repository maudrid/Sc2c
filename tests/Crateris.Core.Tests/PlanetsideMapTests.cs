// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The planetside map (stage 2b, item 2) against UQM's own TopoScale4x and
/// RenderTopography for every landable world, with frandom started afresh
/// for each (tests/golden/planetside-heights.json, planetside-colors.json).
/// </summary>
public class PlanetsideMapTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static List<(int Star, int Planet, int Moon, bool Landable, string? Want)> Worlds(string file, int wantAt) =>
        Golden.Load(file).GetProperty("worlds").EnumerateArray()
            .Select(w => (w[0].GetInt32(), w[1].GetInt32(), w[2].GetInt32(),
                file == "planetside-heights" ? w[3].GetInt32() == 1 : w[3].ValueKind == System.Text.Json.JsonValueKind.String,
                w[wantAt].ValueKind == System.Text.Json.JsonValueKind.String ? w[wantAt].GetString() : null))
            .ToList();

    [Fact]
    public void EveryLandableWorldsPlanetsideMapIsTheOneUqmMakes()
    {
        var worlds = Worlds("planetside-heights", 4);
        // Landable as UQM decides: every world UQM made a map for, and no other.
        var orbits = worlds.Select(w => Systems[w.Star].EnterOrbit(w.Planet, w.Moon < 0 ? null : w.Moon)).ToList();
        for (int i = 0; i < worlds.Count; ++i)
        {
            var w = worlds[i];
            bool landable = orbits[i].Entered
                && PlanetsideMap.Landable(SurfaceMapTests.World(w.Star, w.Planet, w.Moon).DataIndex, orbits[i].Report!);
            Assert.True(w.Landable == landable, $"star {w.Star} planet {w.Planet} moon {w.Moon}: landable");
            Assert.True(w.Landable == (w.Want != null), $"star {w.Star} planet {w.Planet} moon {w.Moon}: map");
        }
        var maps = worlds.Where(w => w.Landable).ToList();
        Assert.True(maps.Count > 2000, $"only {maps.Count} landable worlds");

        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(maps, w =>
        {
            var desc = SurfaceMapTests.World(w.Star, w.Planet, w.Moon);
            var type = Systems.Generator.Types[WorldTypes.PlanetType(desc.DataIndex)];
            var map = PlanetsideMap.Generate(SurfaceMap.Generate(type, desc.RandSeed), type);
            if (Hash(SurfaceMapTests.Bytes(map)) != w.Want)
                wrong.Add($"star {w.Star} planet {w.Planet} moon {w.Moon} (type {type.Name})");
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} planetside maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [ContentFact]
    public void EveryLandableWorldsPlanetsideMapIsColouredAsUqmColoursIt()
    {
        var worlds = Worlds("planetside-colors", 3).Where(w => w.Landable).ToList();
        Assert.True(worlds.Count > 2000, $"only {worlds.Count} landable worlds");
        var reports = worlds.Select(w => Systems[w.Star].EnterOrbit(w.Planet, w.Moon < 0 ? null : w.Moon).Report!).ToList();
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, worlds.Count, i =>
        {
            var w = worlds[i];
            var colors = surfaces.Planetside(Systems[w.Star], w.Planet, w.Moon < 0 ? null : w.Moon, reports[i]);
            if (colors is null || Hash(PlanetSurfacesTests.RgbBytes(colors)) != w.Want)
                wrong.Add($"star {w.Star} planet {w.Planet} moon {w.Moon}");
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} coloured planetside maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [ContentFact]
    public void NoPlanetsideMapForAShieldedWorldOrAGasGiant()
    {
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        Assert.Null(surfaces.Planetside(sol, 2, null, sol.EnterOrbit(2, null).Report!));   // Earth, shielded
        Assert.Null(surfaces.Planetside(sol, 4, null, sol.EnterOrbit(4, null).Report!));   // Jupiter
        var luna = surfaces.Planetside(sol, 2, 1, sol.EnterOrbit(2, 1).Report!);
        Assert.Equal(PlanetsideMap.Width * PlanetsideMap.Height, luna!.Length);
    }

    [Fact]
    public void ItsTheSameEveryTime()
    {
        // frandom starts afresh for each world: the same map, however many were made before.
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var luna = sol.Moons(2)[1];
        var type = Systems.Generator.Types[WorldTypes.PlanetType(luna.DataIndex)];
        var heights = SurfaceMap.Generate(type, luna.RandSeed);
        var first = PlanetsideMap.Generate(heights, type);
        var mars = sol.Planets[3];
        var marsType = Systems.Generator.Types[WorldTypes.PlanetType(mars.DataIndex)];
        PlanetsideMap.Generate(SurfaceMap.Generate(marsType, mars.RandSeed), marsType);
        Assert.Equal(first, PlanetsideMap.Generate(heights, type));
        Assert.Equal((968, 300), (PlanetsideMap.Width, PlanetsideMap.Height));
    }
}
