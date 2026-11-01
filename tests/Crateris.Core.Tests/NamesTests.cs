// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using Crateris.Core.Content;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// Names against what UQM's own code gives for every star, planet and moon
/// (tests/golden/names.json, as hashes), and a few in plain words.
/// </summary>
public class NamesTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [ContentFact]
    public void EveryNameIsTheNameUqmGives()
    {
        var names = TestContent.With(Names.Load);
        var stars = Golden.Load("names").GetProperty("stars").EnumerateArray().ToList();
        Assert.Equal(Systems.Map.Stars.Count, stars.Count);
        int worlds = 0;
        for (int s = 0; s < stars.Count; ++s)
        {
            var system = Systems[s];
            var star = system.Star;
            Assert.True(stars[s].GetProperty("cluster").GetString() == Hash(names.Cluster(star)), $"star {s}: {names.Cluster(star)}");
            var planets = stars[s].GetProperty("planets").EnumerateArray().ToList();
            Assert.Equal(system.Planets.Count, planets.Count);
            for (int p = 0; p < planets.Count; ++p)
            {
                string where = $"star {s} planet {p}";
                Assert.True(planets[p].GetProperty("name").GetString() == Hash(names.PlanetName(star, p)), $"{where}: name {names.PlanetName(star, p)}");
                Assert.True(planets[p].GetProperty("title").GetString() == Hash(names.Title(system, p, null)), $"{where}: title {names.Title(system, p, null)}");
                Assert.True(planets[p].GetProperty("planet_or_moon").GetString() == Hash(names.PlanetOrMoon(star, p, null)), $"{where}: {names.PlanetOrMoon(star, p, null)}");
                var moons = planets[p].GetProperty("moons").EnumerateArray().ToList();
                Assert.Equal(system.Moons(p).Length, moons.Count);
                for (int m = 0; m < moons.Count; ++m)
                {
                    Assert.True(moons[m][0].GetString() == Hash(names.Title(system, p, m)), $"{where} moon {m}: title {names.Title(system, p, m)}");
                    Assert.True(moons[m][1].GetString() == Hash(names.PlanetOrMoon(star, p, m)), $"{where} moon {m}: {names.PlanetOrMoon(star, p, m)}");
                    worlds++;
                }
                worlds++;
            }
        }
        Assert.True(worlds > 3000, $"only {worlds} worlds");
    }

    [ContentFact]
    public void SolIsNamedAsInTheGame()
    {
        var names = TestContent.With(Names.Load);
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        Assert.Equal("Sol", names.Cluster(sol.Star));
        Assert.Equal("Earth", names.PlanetName(sol.Star, 2));
        Assert.Equal("Earth", names.Title(sol, 2, null));
        Assert.Equal("Luna", names.Title(sol, 2, 1));
        Assert.Equal("Luna", names.PlanetOrMoon(sol.Star, 2, 1));
        Assert.Equal("Titan", names.Title(sol, 5, 0));
    }

    [ContentFact]
    public void OtherWorldsAreCalledByNumberAndType()
    {
        var names = TestContent.With(Names.Load);
        var system = Systems[0];
        int p = Enumerable.Range(0, system.Planets.Count).First(i => system.Moons(i).Length > 0);
        string planet = names.PlanetName(system.Star, p);
        Assert.StartsWith("Planet ", planet);
        Assert.Equal(planet + "-B", names.PlanetOrMoon(system.Star, p, 1));
        Assert.Matches("^.+ (World|Giant)$", names.Title(system, p, null));
        Assert.Matches(@"^\w+ \w+$", names.Cluster(system.Star));
    }
}
