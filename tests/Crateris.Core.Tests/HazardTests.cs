// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Hazards on the surface (stage 2b, item 8) beyond the recorded landings
/// (LanderTests replays lander-hazards.json): every world's chances
/// against UQM's, the shields' bits, and the rules for several landers
/// (decision 3), which UQM, with its one lander, has no recording of.
/// </summary>
public class HazardTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public void EveryWorldsChancesAreUqms()
    {
        // From each world's report as our own orbit gives it.
        var chances = Golden.Load("lander-hazards").GetProperty("chances").EnumerateArray().ToList();
        Assert.True(chances.Count >= 3800, $"only {chances.Count} worlds");
        foreach (var c in chances)
        {
            int star = c[0].GetInt32(), planet = c[1].GetInt32(), moon = c[2].GetInt32();
            var report = Systems[star].EnterOrbit(planet, moon < 0 ? null : moon).Report!;
            var want = c[3].Ints();
            Assert.True(new HazardChances(want[0], want[1], want[2]) == HazardChances.For(report),
                $"star {star} planet {planet} moon {moon}");
        }
        // Every rating and temperature band is reached somewhere.
        Assert.Equal(8, chances.Select(c => c[3][0].GetInt32()).Distinct().Count() + 1);   // ratings 0 and 1 share 0
        Assert.Equal(7, chances.Select(c => c[3][2].GetInt32()).Distinct().Count());
    }

    [Fact]
    public void TheShieldsAreUqmsBits()
    {
        // LANDER_SHIELDS has a bit a disaster, 1 << its number (planets.h).
        foreach (var (shield, disaster) in new[]
                 {
                     (LanderUpgrades.BioShield, LanderDisaster.Biological),
                     (LanderUpgrades.QuakeShield, LanderDisaster.Earthquake),
                     (LanderUpgrades.LightningShield, LanderDisaster.Lightning),
                     (LanderUpgrades.LavaShield, LanderDisaster.Lavaspot),
                 })
            Assert.Equal(1 << (int)disaster, (int)shield);
    }

    [Fact]
    public void AHazardSurfaceNeedsTheShapes()
    {
        Assert.Throws<ArgumentException>(() => new PlanetsideSurface(new TfbRandom(1), hazards: new HazardChances(3, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetsideSurface(new TfbRandom(1), shapes: TestContent.Shapes, hazards: new HazardChances(0, -1, 0)));
    }

    private static Lander SendDown(PlanetsideSurface surface, int crew, int x, int y)
    {
        var hold = new ShipHold(new ShipLoadout(1, 1, 0, 0, crew, 0));
        hold.LanderLeaves();
        return surface.Land(hold, x, y);
    }

    // Wraps a difference east and west the nearer way round.
    private static int Across(int dx) => Math.Abs(dx) > PlanetsideMap.Width / 2 ? PlanetsideMap.Width - Math.Abs(dx) : Math.Abs(dx);

    [ContentFact]
    public void EachLanderHasItsOwnHazardsAroundIt()
    {
        // Decision 3: each lander draws its own roll a frame, and what
        // starts, starts round it; harmful lightning keeps to its own lander.
        var surface = new PlanetsideSurface(new TfbRandom(11), shapes: TestContent.Shapes,
            hazards: new HazardChances(40, 40, 40));
        var a = SendDown(surface, 50, 200, 150);
        var b = SendDown(surface, 50, 700, 150);
        var seen = new HashSet<SurfaceElement>();
        int lightning = 0;
        for (int frame = 0; frame < 120 && a.Crew > 0 && b.Crew > 0; ++frame)
        {
            surface.Step(_ => LanderInput.None);
            // Each starts within 90 units east or west of its lander (spread
            // lava 4 units on from its spot), and the landers are 500 apart.
            foreach (var e in surface.Elements.Where(e => e.Hazard && seen.Add(e)))
                Assert.Same(Across(e.X - a.X) <= Across(e.X - b.X) ? a : b, e.Owner);
            foreach (var e in surface.Elements.Where(e => e.Kind == SurfaceElementKind.Lightning
                         && e.MassPoints == (int)LanderDisaster.Lightning))
            {
                lightning++;
                Assert.Equal((e.Owner!.X, e.Owner.Y), (e.X, e.Y));
            }
        }
        Assert.True(lightning > 0, "no harmful lightning");
        Assert.Contains(seen, e => e.Owner == a);
        Assert.Contains(seen, e => e.Owner == b);
    }

    [ContentFact]
    public void TwoLandersDrawARollEachAFrame()
    {
        // With every chance 0 the hazards draw nothing but their rolls: one
        // a lander a frame, landing and taking off too.
        var surface = new PlanetsideSurface(new TfbRandom(5), shapes: TestContent.Shapes);
        SendDown(surface, 50, 200, 150);
        SendDown(surface, 50, 700, 150);
        var want = new TfbRandom(5);
        for (int i = 0; i < 2 * 3; ++i)
            want.Next();   // each landing's facing and place
        for (int frame = 0; frame < 60; ++frame)
        {
            surface.Step(_ => LanderInput.None);
            want.Next();
            want.Next();
        }
        Assert.Equal(want.Seed, surface.Random.Seed);
    }

    [ContentFact]
    public void LightningRoundALanderThatLeftStaysWhereItIs()
    {
        // A lander takes off while its lightning lasts: the lightning stays
        // where it was, no longer keeping to a lander.
        var surface = new PlanetsideSurface(new TfbRandom(3), shapes: TestContent.Shapes,
            hazards: new HazardChances(0, 255, 0));
        var leaving = SendDown(surface, 50, 200, 150);
        var staying = SendDown(surface, 50, 210, 150);
        while (leaving.Phase != LanderPhase.Ground)
            surface.Step(_ => LanderInput.None);
        while (leaving.Phase != LanderPhase.Gone)
            surface.Step(l => l == leaving ? LanderInput.TakeOff : LanderInput.None);
        var left = surface.Elements.Where(e => e.Kind == SurfaceElementKind.Lightning && e.Owner == leaving
                && e.MassPoints == (int)LanderDisaster.Lightning).ToList();
        Assert.NotEmpty(left);
        var places = left.ToDictionary(e => e, e => (e.X, e.Y));
        for (int frame = 0; frame < 3; ++frame)
        {
            surface.Step(_ => LanderInput.None);
            foreach (var e in left.Where(surface.Elements.Contains))
                Assert.Equal(places[e], (e.X, e.Y));
        }
        Assert.DoesNotContain(surface.Landers, l => l == leaving);
        Assert.Contains(staying, surface.Landers);
    }
}
