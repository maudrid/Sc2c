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
/// System generation against tests/golden/star-systems.json, recorded by
/// running UQM 0.8.0's own generators for every star.
/// </summary>
public class StarSystemTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly JsonElement Reference = Golden.Load("star-systems");

    [Fact]
    public void RandomContextMatchesUqm()
    {
        var rng = Reference.GetProperty("rng");
        Assert.Equal(rng.GetProperty("new_context_first").GetUInt32(), new SysRandom().Random());

        int sequences = 0;
        foreach (var seq in rng.GetProperty("sequences").EnumerateArray())
        {
            uint[] v = seq.EnumerateArray().Select(x => x.GetUInt32()).ToArray();
            var r = new SysRandom();
            r.SeedRandom(v[0]);
            Assert.Equal(v[1], r.Seed);
            for (int i = 2; i < v.Length; ++i)
                Assert.Equal(v[i], r.Random());
            ++sequences;
        }
        Assert.True(sequences > 40);
    }

    [Fact]
    public void EveryStarSystemMatchesUqm()
    {
        var map = StarMap.Load(DataDir);
        var gen = new SystemGenerator(PlanetTypes.Load(DataDir));
        var systems = Reference.GetProperty("systems").EnumerateArray().ToArray();
        Assert.Equal(map.Stars.Count, systems.Length);

        var failures = new StringBuilder();
        int worlds = 0;
        for (int s = 0; s < systems.Length; ++s)
        {
            var expected = systems[s];
            Star star = map.Stars[s];
            Assert.Equal(expected.GetProperty("x").GetInt32(), star.X);
            Assert.Equal(expected.GetProperty("y").GetInt32(), star.Y);

            SolarSystem sys = gen.Generate(star);
            string name = $"star {s} ({star.X}, {star.Y}) {star.Special ?? "default"}";
            Check(failures, name + " sun seed", expected.GetProperty("sun_seed").GetUInt32(), sys.SunSeed);
            Check(failures, name + " sun type", expected.GetProperty("sun_type").GetInt32(), sys.SunType);

            var planets = expected.GetProperty("planets").EnumerateArray().ToArray();
            var moons = expected.GetProperty("moons").EnumerateArray().ToArray();
            if (!Check(failures, name + " planet count", planets.Length, sys.Planets.Count))
                continue;

            for (int p = 0; p < planets.Length; ++p)
            {
                WorldDesc planet = sys.Planets[p];
                CheckWorld(failures, $"{name} planet {p}", planets[p], planet,
                    () => sys.OrbitTemperature(planet, planet.Radius), () => sys.PlanetInfo(p));
                ++worlds;

                var expectedMoons = moons[p].EnumerateArray().ToArray();
                WorldDesc[] got = sys.Moons(p);
                if (!Check(failures, $"{name} planet {p} moon count", expectedMoons.Length, got.Length))
                    continue;
                for (int m = 0; m < got.Length; ++m)
                {
                    WorldDesc moon = got[m];
                    CheckWorld(failures, $"{name} planet {p} moon {m}", expectedMoons[m], moon,
                        () => sys.OrbitTemperature(moon, planet.Radius), () => sys.MoonInfo(p, moon));
                    ++worlds;
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(worlds > 3000, $"only {worlds} worlds checked");
    }

    [Fact]
    public void SolHasItsPlanetsAndMoons()
    {
        var map = StarMap.Load(DataDir);
        var types = PlanetTypes.Load(DataDir);
        var sol = new SystemGenerator(types).Generate(map.At(1752, 1450)!);

        Assert.Equal("SOL_DEFINED", sol.Star.Special);
        Assert.Equal(9, sol.Planets.Count);
        Assert.Equal(types.IndexOf("WATER_WORLD") | WorldTypes.Shielded, sol.Planets[2].DataIndex);
        var earthMoons = sol.Moons(2);
        Assert.Equal(WorldTypes.HierarchyStarbase, earthMoons[0].DataIndex);
        Assert.Equal(types.IndexOf("SELENIC_WORLD"), earthMoons[1].DataIndex);
        Assert.Equal(4, sol.Moons(4).Length);
    }

    private static void CheckWorld(StringBuilder failures, string name, JsonElement e, WorldDesc w,
        Func<short> orbitTemperature, Func<PlanetInfo> info)
    {
        int[] v = e.EnumerateArray().Select(x => (int)x.GetInt64()).ToArray();
        long seed = e[4].GetInt64();
        int[] got = { w.DataIndex, w.Radius, w.X, w.Y, 0, w.NumPlanets };
        for (int i = 0; i < 6; ++i)
        {
            if (i != 4)
                Check(failures, $"{name} field {i}", v[i], got[i]);
        }
        Check(failures, $"{name} seed", (uint)seed, w.RandSeed);
        if (v.Length == 6)
        {
            Check(failures, $"{name} has no planet data", false, WorldTypes.HasPlanetData(w.DataIndex));
            return;
        }

        short plainTemp = orbitTemperature();
        Check(failures, $"{name} orbit temperature", v[6], plainTemp);
        Check(failures, $"{name} orbit colour", v[7], PlanetAnalysis.TemperatureColorIndex(plainTemp));

        PlanetInfo i2 = info();
        int[] infoGot =
        {
            i2.AxialTilt, i2.Tectonics, i2.Weather, i2.PlanetDensity, i2.PlanetRadius,
            i2.SurfaceGravity, i2.SurfaceTemperature, i2.RotationPeriod, i2.AtmoDensity,
            i2.LifeChance, i2.PlanetToSunDist,
        };
        string[] names =
        {
            "tilt", "tectonics", "weather", "density", "radius", "gravity", "temperature",
            "rotation", "atmosphere", "life chance", "sun distance",
        };
        for (int i = 0; i < infoGot.Length; ++i)
            Check(failures, $"{name} {names[i]}", v[8 + i], infoGot[i]);
        Check(failures, $"{name} seed after analysis", (uint)e[19].GetInt64(), i2.SeedAfter);
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
