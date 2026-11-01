// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>
/// The rotating planet against UQM's own code: every world's light map
/// (tests/golden/surface-heights.json, code only) and UQM's rendered frames
/// of a sample of worlds at several rotation steps (planet-spheres.json).
/// </summary>
public class PlanetSphereTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static PlanetType Type(WorldDesc w) => Systems.Generator.Types[WorldTypes.PlanetType(w.DataIndex)];

    [Fact]
    public void EveryWorldsLightMapIsTheOneUqmMakes()
    {
        var worlds = Golden.Load("surface-heights").GetProperty("worlds").EnumerateArray()
            .Where(w => w[6].ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(w => (Desc: SurfaceMapTests.World(w[0].GetInt32(), w[1].GetInt32(), w[2].GetInt32()),
                Where: $"star {w[0]} planet {w[1]} moon {w[2]}", Want: w[6].GetString()!))
            .ToList();
        Assert.True(worlds.Count > 3700, $"only {worlds.Count} light maps");
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(worlds, w =>
        {
            var type = Type(w.Desc);
            var light = PlanetSphere.LightMap(SurfaceMap.Generate(type, w.Desc.RandSeed), type.Algorithm == MapAlgorithm.GasGiant);
            if (Hash(SurfaceMapTests.Bytes(light)) != w.Want)
                wrong.Add(w.Where);
        });
        Assert.True(wrong.IsEmpty, $"{wrong.Count} light maps differ from UQM's, such as {wrong.FirstOrDefault()}");
    }

    [ContentFact]
    public void TheRotatingPlanetIsDrawnAsUqmDrawsIt()
    {
        var surfaces = TestContent.With(p => new PlanetSurfaces(p));
        var golden = Golden.Load("planet-spheres").GetProperty("worlds").EnumerateArray().ToList();
        Assert.True(golden.Count > 50, $"only {golden.Count} worlds");
        int frames = 0, shields = 0;
        foreach (var w in golden)
        {
            int star = w[0].GetInt32(), planet = w[1].GetInt32(), moon = w[2].GetInt32();
            string where = $"star {star} planet {planet} moon {moon}";
            var system = Systems[star];
            var desc = SurfaceMapTests.World(star, planet, moon);
            var report = system.EnterOrbit(planet, moon < 0 ? null : moon).Report!;
            var surface = surfaces.Generated(system, planet, moon < 0 ? null : moon, report);
            var sun = system.Planets[planet];   // a moon is lit from its planet's place
            var sphere = new PlanetSphere(surface.Colors,
                PlanetSphere.LightMap(surface.Heights, Type(desc).Algorithm == MapAlgorithm.GasGiant),
                sun.X, sun.Y, report.AxialTilt, WorldTypes.IsShielded(desc.DataIndex));
            foreach (var f in w[3].EnumerateArray())
            {
                int step = f[0].GetInt32();
                Assert.True(f[1].GetString() == Hash(sphere.Render(sphere.Offset(step))), $"{where} step {step}: the frame differs from UQM's");
                frames++;
            }
            if (w[4].ValueKind == System.Text.Json.JsonValueKind.String)
            {
                Assert.Equal(w[4].GetString(), Hash(PlanetSphere.ShieldHalo()));
                shields++;
            }
            else
                Assert.False(WorldTypes.IsShielded(desc.DataIndex), $"{where} is shielded, but UQM made no halo");
        }
        Assert.True(frames > 300 && shields > 0, $"{frames} frames, {shields} shields");
    }

    [Fact]
    public void ItTurnsAsUqmTurnsIt()
    {
        var colors = new Rgb[SurfaceMap.Width * SurfaceMap.Height];
        var flat = new sbyte[colors.Length];
        var east = new PlanetSphere(colors, flat, 100, 0, 4, false);
        var west = new PlanetSphere(colors, flat, 100, 0, -3, false);
        Assert.Equal((1, -1), (east.Direction, west.Direction));
        Assert.Equal(1, east.Offset(1));
        Assert.Equal(SurfaceMap.Width - 1, west.Offset(1));
        Assert.Equal(0, east.Offset(SurfaceMap.Width));
        Assert.Equal(41, PlanetSphere.TicksPerStep);   // 12 s a turn, 242 steps
        Assert.Equal((121, 69), (PlanetSphere.CenterX, PlanetSphere.CenterY));
    }
}
