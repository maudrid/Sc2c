// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The planetside map (stage 2b, item 2) against UQM's own TopoScale4x and
/// RenderTopography for every landable world, with frandom started afresh
/// for each (tests/golden/planetside-heights.json, planetside-colors.json).
/// </summary>
[Trait("Topic", "planet-maps")]
public class PlanetsideMapTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Hash(byte[] bytes) => Golden.Sha256(bytes);

    private static bool IsHash(System.Text.Json.JsonElement e) => e.ValueKind == System.Text.Json.JsonValueKind.String;

    /// <summary>A reference file's worlds: where each is, whether it's landable (by <paramref name="landable"/>), and the hash it should have (at <paramref name="wantAt"/>), or null.</summary>
    private static List<(int Star, int Planet, int Moon, bool Landable, string? Want)> Worlds(
        string file, Func<System.Text.Json.JsonElement, bool> landable, int wantAt) =>
        Golden.Load(file).GetProperty("worlds").EnumerateArray()
            .Select(w => (w[0].GetInt32(), w[1].GetInt32(), w[2].GetInt32(), landable(w),
                IsHash(w[wantAt]) ? w[wantAt].GetString() : null))
            .ToList();

    // planetside-heights.json says whether each world is landable; planetside-colors.json gives a map for each one that is.
    private static List<(int Star, int Planet, int Moon, bool Landable, string? Want)> Heights() =>
        Worlds("planetside-heights", w => w[3].GetInt32() == 1, 4);

    private static List<(int Star, int Planet, int Moon, bool Landable, string? Want)> Colors() =>
        Worlds("planetside-colors", w => IsHash(w[3]), 3);

    [Fact]
    public void EveryLandableWorldsPlanetsideMapIsTheOneUqmMakes()
    {
        var worlds = Heights();
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
        Parallel.ForEach(maps.Sample(), w =>
        {
            var desc = SurfaceMapTests.World(w.Star, w.Planet, w.Moon);
            var type = Systems.Generator.Types[WorldTypes.PlanetType(desc.DataIndex)];
            var map = PlanetsideMap.Generate(SurfaceMapTests.HeightMap(type, desc.RandSeed), type);
            if (Hash(SurfaceMapTests.Bytes(map)) != w.Want)
                wrong.Add($"star {w.Star} planet {w.Planet} moon {w.Moon} (type {type.Name})");
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} planetside maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [ContentFact]
    public void EveryLandableWorldsPlanetsideMapIsColouredAsUqmColoursIt()
    {
        var worlds = Colors().Where(w => w.Landable).ToList();
        Assert.True(worlds.Count > 2000, $"only {worlds.Count} landable worlds");
        worlds = worlds.Sample();
        var reports = worlds.Select(w => Systems[w.Star].EnterOrbit(w.Planet, w.Moon < 0 ? null : w.Moon).Report!).ToList();
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, worlds.Count, i =>
        {
            var w = worlds[i];
            // From the world's height map (made once for these tests), as the orbit screen gives it.
            var desc = SurfaceMapTests.World(w.Star, w.Planet, w.Moon);
            var type = Systems.Generator.Types[WorldTypes.PlanetType(desc.DataIndex)];
            var colors = surfaces.Planetside(Systems[w.Star], w.Planet, w.Moon < 0 ? null : w.Moon, reports[i],
                new PlanetSurface(SurfaceMapTests.HeightMap(type, desc.RandSeed), Array.Empty<Rgb>()));
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
        // frandom starts afresh for each world: Luna's map is UQM's first
        // map of a session, however many maps were made before it.
        var sol = Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number;
        string want = Heights().Single(w => (w.Star, w.Planet, w.Moon) == (sol, 2, 1)).Want!;
        sbyte[] Map(int planet, int? moon)
        {
            var desc = SurfaceMapTests.World(sol, planet, moon ?? -1);
            var type = Systems.Generator.Types[WorldTypes.PlanetType(desc.DataIndex)];
            return PlanetsideMap.Generate(SurfaceMap.Generate(type, desc.RandSeed), type);
        }
        Assert.Equal(want, Hash(SurfaceMapTests.Bytes(Map(2, 1))));
        Map(3, null);   // Mars
        Map(2, 1);
        Assert.Equal(want, Hash(SurfaceMapTests.Bytes(Map(2, 1))));
        Assert.Equal((968, 300), (PlanetsideMap.Width, PlanetsideMap.Height));
    }

    [Fact]
    public void NoPlanetsideMapForAWorldWithoutPlanetData()
    {
        // The starbase and the Sa-Matra have no planet type, whatever their report says.
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var rocky = sol.EnterOrbit(2, 1).Report!;   // Luna's
        Assert.True(PlanetsideMap.Landable(SurfaceMapTests.World(sol.Star.Number, 2, 1).DataIndex, rocky));
        for (int index = WorldTypes.NumberOfPlanetTypes; index < 256; ++index)
            if (!WorldTypes.HasPlanetData((byte)index))
                Assert.False(PlanetsideMap.Landable((byte)index, rocky), $"data index {index}");
    }
}
