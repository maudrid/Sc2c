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
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// Entering orbit and surface nodes against tests/golden/world-surfaces.json,
/// recorded by running UQM 0.8.0's own generateOrbital and node generators
/// for every world.
/// </summary>
public class SurfaceTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly JsonElement Reference = Golden.Load("world-surfaces");

    [Fact]
    public void MapSizeMatchesUqm()
    {
        Assert.Equal(Surface.MapWidth, Reference.GetProperty("map_width").GetInt32());
        Assert.Equal(Surface.MapHeight, Reference.GetProperty("map_height").GetInt32());
    }

    [Fact]
    public void EveryWorldsOrbitAndNodesMatchUqm()
    {
        var map = StarMap.Load(DataDir);
        var gen = new SystemGenerator(PlanetTypes.Load(DataDir));
        var systems = Reference.GetProperty("systems").EnumerateArray().ToArray();
        Assert.Equal(map.Stars.Count, systems.Length);

        var failures = new StringBuilder();
        int worlds = 0, nodes = 0;
        for (int s = 0; s < systems.Length; ++s)
        {
            Star star = map.Stars[s];
            SolarSystem sys = gen.Generate(star);
            var planets = systems[s].EnumerateArray().ToArray();
            for (int p = 0; p < planets.Length; ++p)
            {
                var group = planets[p].EnumerateArray().ToArray();
                for (int w = 0; w < group.Length; ++w)
                {
                    int? moon = w == 0 ? null : w - 1;
                    string name = $"star {s} {star.Special ?? "default"} planet {p}" + (moon is int m ? $" moon {m}" : "");
                    nodes += CheckOrbit(failures, name, group[w], sys.EnterOrbit(p, moon));
                    ++worlds;
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(worlds > 3800, $"only {worlds} worlds");
        Assert.True(nodes > 30000, $"only {nodes} nodes");
    }

    [Fact]
    public void OrbitFollowsTheGeneratorsStoryState()
    {
        var map = StarMap.Load(DataDir);
        var types = PlanetTypes.Load(DataDir);
        Star chmmr = map.Stars.Single(s => s.Special == "CHMMR_DEFINED");

        var newGame = new SystemGenerator(types).Generate(chmmr);
        Assert.True(newGame.EnterOrbit(1).Entered);
        Assert.Null(newGame.EnterOrbit(1).Nodes);               // shielded

        var freed = new SystemGenerator(types, new SystemStoryState(ChmmrUnleashed: true)).Generate(chmmr);
        Assert.Equal(0, freed.Planets[1].DataIndex & WorldTypes.Shielded);
        Assert.False(freed.EnterOrbit(1).Entered);              // a conversation instead

        Star spathi = map.Stars.Single(s => s.Special == "SPATHI_DEFINED");
        var shielded = new SystemGenerator(types, new SystemStoryState(SpathiShieldedSelves: true)).Generate(spathi);
        Assert.NotEqual(0, shielded.Planets[0].DataIndex & WorldTypes.Shielded);
        Assert.Null(shielded.EnterOrbit(0).Nodes);
    }

    [Fact]
    public void MoonsAreGeneratedOnceAndCopiedOut()
    {
        var map = StarMap.Load(DataDir);
        var sol = new SystemGenerator(PlanetTypes.Load(DataDir)).Generate(map.At(1752, 1450)!);
        var a = sol.Moons(4);
        a[0].DataIndex = 0;                                      // the caller's copy
        Assert.NotEqual(0, sol.Moons(4)[0].DataIndex);
    }

    [Fact]
    public void SolHasItsStoryNodes()
    {
        var map = StarMap.Load(DataDir);
        var sol = new SystemGenerator(PlanetTypes.Load(DataDir)).Generate(map.At(1752, 1450)!);

        var earth = sol.EnterOrbit(2);
        Assert.True(earth.Entered);
        Assert.Equal(22, earth.Report!.SurfaceTemperature);
        Assert.Null(earth.Nodes);                       // shielded

        var luna = sol.EnterOrbit(2, 1).Nodes!;
        Assert.Single(luna.Energy);                     // the Moon base
        Assert.Equal(10, luna.Life.Count);              // its Brainbox Bulldozers
        Assert.All(luna.Life, n => Assert.Equal(24, n.Type));

        Assert.Single(sol.EnterOrbit(8).Nodes!.Energy); // the Spathi on Pluto (SPAPLUTO)
        Assert.False(sol.EnterOrbit(2, 0).Entered);     // the starbase
    }

    private static int CheckOrbit(StringBuilder failures, string name, JsonElement e, OrbitData got)
    {
        var v = e.EnumerateArray().ToArray();
        bool entered = v[1].GetInt32() != 0;
        if (!Check(failures, $"{name} entered", entered, got.Entered) || !entered)
            return 0;

        uint[] seeds = v[2].EnumerateArray().Select(x => x.GetUInt32()).ToArray();
        Check(failures, $"{name} mineral seed", seeds[0], got.MineralSeed);
        Check(failures, $"{name} energy seed", seeds[1], got.EnergySeed);
        Check(failures, $"{name} bio seed", seeds[2], got.BiologicalSeed);

        int[] report = v[3].Ints();
        PlanetInfo r = got.Report!;
        int[] mine =
        {
            r.AxialTilt, r.Tectonics, r.Weather, r.PlanetDensity, r.PlanetRadius, r.SurfaceGravity,
            r.SurfaceTemperature, r.RotationPeriod, r.AtmoDensity, r.LifeChance, r.PlanetToSunDist,
        };
        string[] names =
        {
            "tilt", "tectonics", "weather", "density", "radius", "gravity", "temperature",
            "rotation", "atmosphere", "life chance", "sun distance",
        };
        for (int i = 0; i < mine.Length; ++i)
            Check(failures, $"{name} report {names[i]}", report[i], mine[i]);

        // The orbit screen's numbers, against the text UQM's PrintCoarseScanPC printed.
        string[] scan = v[5].EnumerateArray().Select(x => x.GetString()!).ToArray();
        string[] mineScan = ScanText(got.Scan!);
        for (int i = 0; i < scan.Length; ++i)
            Check(failures, $"{name} scan text {i}", scan[i], mineScan[i]);

        if (v[4].ValueKind == JsonValueKind.Null)
        {
            Check(failures, $"{name} shielded (no nodes)", true, got.Nodes is null);
            return 0;
        }
        if (!Check(failures, $"{name} has nodes", true, got.Nodes is not null))
            return 0;

        var kinds = v[4].EnumerateArray().ToArray();
        IReadOnlyList<SurfaceNode>[] lists = { got.Nodes!.Minerals, got.Nodes.Energy, got.Nodes.Life };
        string[] kindNames = { "mineral", "energy", "life" };
        int count = 0;
        for (int k = 0; k < 3; ++k)
        {
            var expected = kinds[k].EnumerateArray().Select(n => n.Ints()).ToArray();
            if (!Check(failures, $"{name} {kindNames[k]} count", expected.Length, lists[k].Count))
                continue;
            for (int n = 0; n < expected.Length; ++n)
            {
                SurfaceNode node = lists[k][n];
                int[] actual = { node.X, node.Y, node.Type, node.Density };
                Check(failures, $"{name} {kindNames[k]} {n}", string.Join(",", expected[n]), string.Join(",", actual));
            }
            count += expected.Length;
        }
        return count;
    }

    /// <summary>
    /// PrintCoarseScanPC's text with the content's words left out (they are
    /// empty in the recording): "" for a value that is only a word.
    /// </summary>
    internal static string[] ScanText(PlanetReport r) => new[]
    {
        PlanetReport.ScanValue(r.OrbitAu100),
        r.Atmosphere == AtmosphereReading.Value ? PlanetReport.ScanValue(r.Atmospheres100) : "",
        $"{r.TemperatureC}\u00b0 c",
        r.WeatherClass is int w ? $" {w}" : "",
        r.TectonicsClass is int t ? $" {t}" : "",
        PlanetReport.ScanValue(r.EarthMasses100),
        PlanetReport.ScanValue(r.EarthRadii100),
        PlanetReport.ScanValue(r.Gravity100),
        PlanetReport.ScanValue(r.Days100),
        $"{r.TiltDegrees}\u00b0",
    };

    [Fact]
    public void EarthsReportReadsAsInUqm()
    {
        var map = StarMap.Load(DataDir);
        var sol = new SystemGenerator(PlanetTypes.Load(DataDir)).Generate(map.At(1752, 1450)!);
        var earth = sol.EnterOrbit(2).Scan!;
        Assert.Equal(new[] { "1.00", "1.00", "22\u00b0 c", " 2", " 2", "1.00", "1.00", "1.00", "1.00", "23\u00b0" },
            ScanText(earth));
        var jupiter = sol.EnterOrbit(4).Scan!;
        Assert.Equal(AtmosphereReading.SuperThick, jupiter.Atmosphere);
        Assert.Null(jupiter.TectonicsClass);
        Assert.Equal("337.1", PlanetReport.ScanValue(jupiter.EarthMasses100));
    }

    private static bool Check<T>(StringBuilder failures, string what, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            return true;
        if (failures.Length < 4000)
            failures.AppendLine($"{what}: expected {expected}, got {actual}");
        return false;
    }
}
