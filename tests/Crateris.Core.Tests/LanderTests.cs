// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The lander on the surface (stage 2b, item 5) against UQM's own lander.c,
/// frame by frame (tests/golden/lander.json): landing, driving, turning,
/// the poles and the wrap, firing, taking off, and TFB_Random's draws.
/// </summary>
public class LanderTests
{
    private static readonly JsonElement Recorded = Golden.Load("lander");

    public static IEnumerable<object[]> Landings() =>
        Recorded.GetProperty("landings").EnumerateArray().Select(l => new object[] { l.GetProperty("name").GetString()! });

    private static JsonElement Landing(string name) =>
        Recorded.GetProperty("landings").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name);

    private static LanderUpgrades Upgrades(int flags) =>
        ((flags & 1) != 0 ? LanderUpgrades.Speed : 0) | ((flags & 2) != 0 ? LanderUpgrades.Shot : 0);

    [Theory]
    [MemberData(nameof(Landings))]
    public void EveryFrameIsUqms(string name)
    {
        var landing = Landing(name);
        var input = landing.GetProperty("input");
        var surface = new PlanetsideSurface(new TfbRandom(landing.GetProperty("seed").GetUInt32()));
        var hold = Hold(input.GetProperty("crew").GetInt32(), Upgrades(landing.GetProperty("upgrades").GetInt32()));
        var site = input.GetProperty("site").Ints();
        var lander = surface.Land(hold, site[0], site[1]);

        // The steps' flags, one a ground frame; then take off.
        var flags = input.GetProperty("steps").EnumerateArray()
            .SelectMany(s => Enumerable.Repeat((LanderInput)s[1].GetInt32(), s[0].GetInt32()))
            .GetEnumerator();
        int frame = 0;
        foreach (var want in landing.GetProperty("frames").EnumerateArray())
        {
            var keys = LanderInput.None;
            if (lander.Phase == LanderPhase.Ground)
                keys = flags.MoveNext() ? flags.Current : LanderInput.TakeOff;
            surface.Step(_ => keys);
            string phase = lander.FramePhase switch
            {
                LanderPhase.Landing => "L",
                LanderPhase.Ground => "G",
                _ => "T",
            };
            var bolts = surface.Elements.Select(e => $"[{e.X}, {e.Y}, {e.LifeSpan}]");
            string got = $"{phase} {lander.X},{lander.Y} facing {lander.Facing} offset {-lander.Height} crew {lander.Crew} "
                + $"seed {surface.Random.Seed} [{string.Join(", ", bolts)}]";
            var wantBolts = want[7].EnumerateArray().Select(b => $"[{b[0]}, {b[1]}, {b[2]}]");
            string expected = $"{want[0].GetString()} {want[1]},{want[2]} facing {want[3]} offset {want[4]} crew {want[5]} "
                + $"seed {want[6]} [{string.Join(", ", wantBolts)}]";
            Assert.True(expected == got, $"{name}, frame {frame} ({keys}):\n want {expected}\n got  {got}");
            frame++;
        }
        Assert.Equal(LanderPhase.Gone, lander.Phase);
        Assert.Empty(surface.Landers);
        // The crew went aboard from the ship and came back (AnimateLanderWarmup, PlanetSide).
        Assert.Equal(landing.GetProperty("crew_left").GetInt32(), lander.Crew);
        Assert.Equal(landing.GetProperty("ship_crew").GetInt32(), hold.Crew);
        Assert.Equal(landing.GetProperty("landers").GetInt32(), hold.Landers);
        Assert.Null(hold.LanderOut);
    }

    private static ShipHold Hold(int crew, LanderUpgrades upgrades = LanderUpgrades.None) =>
        new(new ShipLoadout(1, 1, 0, 0, crew, 0)) { Upgrades = upgrades };

    [Fact]
    public void TheCrewGoAboardFromTheShipAndComeBack()
    {
        var hold = Hold(5);
        var surface = new PlanetsideSurface(new TfbRandom(3));
        var lander = surface.Land(hold, 100, 100);
        Assert.Equal((5, 0), (lander.Crew, hold.Crew));   // all five; the ship keeps none
        Assert.NotNull(hold.LanderOut);
        Assert.Throws<InvalidOperationException>(() => surface.Land(hold, 100, 100));   // one lander out at a time
        while (lander.Phase != LanderPhase.Gone)
            surface.Step(l => l.Phase == LanderPhase.Ground ? LanderInput.TakeOff : LanderInput.None);
        Assert.Equal(5, hold.Crew);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Land(hold, PlanetsideMap.Width, 0));
    }

    [Fact]
    public void TwoLandersShareTheSurfaceFrameWithoutDoublingIt()
    {
        // Each lander moves once a frame and draws its own hazard roll;
        // what's on the surface moves once, however many landers there are.
        var alone = new PlanetsideSurface(new TfbRandom(42));
        var a = alone.Land(Hold(50), 300, 150);
        var pair = new PlanetsideSurface(new TfbRandom(42));
        var b = pair.Land(Hold(50), 300, 150);
        var other = pair.Land(Hold(50), 600, 150);
        static LanderInput Keys(Lander l) => l.Phase == LanderPhase.Ground ? LanderInput.Fire : LanderInput.None;
        while (a.Phase != LanderPhase.Ground)
        {
            alone.Step(Keys);
            pair.Step(Keys);
        }
        alone.Step(Keys);
        pair.Step(Keys);   // one bolt each
        var bolt = alone.Elements.Single();
        var same = pair.Elements.Single(e => (e.LifeSpan, e.Y) == (bolt.LifeSpan, bolt.Y) && Math.Abs(e.X - b.X) < 20);
        for (int i = 0; i < 5; ++i)
        {
            alone.Step(Keys);
            pair.Step(Keys);
        }
        Assert.Equal(Lander.BoltLife - 6, bolt.LifeSpan);
        Assert.Equal((bolt.LifeSpan, bolt.X - a.X, bolt.Y - a.Y), (same.LifeSpan, same.X - b.X, same.Y - b.Y));
        Assert.Equal(2, pair.Landers.Count);
    }

    [Fact]
    public void TfbRandomIsUqms()
    {
        // TFB_Random from seed 1: the textbook generator's first numbers
        // match it, until a step goes negative.
        var r = new TfbRandom(1);
        Assert.Equal(16807u, r.Next());
        Assert.Equal(282475249u, r.Next());
        Assert.Equal(1622650073u, r.Next());
        Assert.Equal(1u, new TfbRandom(0).Seed);
        Assert.Equal(1u, new TfbRandom(2147483648u).Seed);
    }

    [Fact]
    public void TheLanderComesDownNearTheSiteAndOffThePoles()
    {
        for (uint seed = 1; seed < 500; ++seed)
        {
            var north = new PlanetsideSurface(new TfbRandom(seed)).Land(Hold(12), 0, 0);
            Assert.InRange(north.Y, 0, Lander.RandomMiss);
            Assert.True(north.X <= Lander.RandomMiss || north.X >= PlanetsideMap.Width - Lander.RandomMiss, $"x {north.X}");
            var south = new PlanetsideSurface(new TfbRandom(seed)).Land(Hold(12), 0, PlanetsideMap.Height - 1);
            Assert.InRange(south.Y, PlanetsideMap.Height - 1 - Lander.RandomMiss, PlanetsideMap.Height - 1);
        }
        Assert.Equal(new[] { 1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 66, 78, 91 }, Lander.Heights);
    }
}
