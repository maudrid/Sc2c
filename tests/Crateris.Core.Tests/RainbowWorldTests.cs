// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The rainbow worlds marked on orbit (docs/plans/stage-3b-2.md, item 3 and
/// question 2; genrain.c's GenerateRainbowWorld_generateOrbital): orbiting
/// the first planet of a RAINBOW_DEFINED star, however the ship gets there
/// (flying in, put there, a game loaded in orbit), sets the star's bit in
/// RAINBOW_WORLD0/1, the bit its place among the rainbow stars in the map's
/// order; the group's one story field, whoever orbits it (question 6).
/// </summary>
[Trait("Topic", "world")]
public class RainbowWorldTests
{
    private static int Mask(StoryState story) => story.Get("RAINBOW_WORLD0") | (story.Get("RAINBOW_WORLD1") << 8);

    private static List<int> RainbowStars(GameWorld world) =>
        world.Systems!.Map.Stars.Where(s => s.Special == StoryWorlds.RainbowSpecial).Select(s => s.Number).ToList();

    private static GameWorld TwoPlayers()
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("g", "Guest"));
        world.UndockShown("g");
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        return world;
    }

    [ContentFact]
    public void EachRainbowStarHasItsBitInTheMapsOrder()
    {
        var world = SystemGroupTests.NewWorld();
        var stars = RainbowStars(world);
        Assert.Equal(10, stars.Count);   // RAINBOW_WORLD0's 8 bits and RAINBOW_WORLD1's 2
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (int?)i), stars.Select(s => StoryWorlds.RainbowIndex(world.Systems!.Map, s)));
        Assert.Null(StoryWorlds.RainbowIndex(world.Systems!.Map, 82));   // Sol
        // Each one's first planet is the rainbow world (GenerateRainbowWorld_generatePlanets).
        Assert.All(stars, s => Assert.Equal(WorldTypes.RainbowWorld, world.Systems![s].Planets[0].DataIndex));
    }

    [ContentFact]
    public void OrbitingARainbowWorldMarksItForTheGroup()
    {
        var world = TwoPlayers();
        var story = world.Story!;
        var stars = RainbowStars(world);
        Assert.Equal(0, Mask(story));
        // Another world of its system, or another system's first planet: nothing.
        world.PutInOrbit("h", stars[0], 1);
        world.PutInOrbit("h", 82, 0);
        Assert.Equal(0, Mask(story));
        // The first rainbow world, by the host; the ninth and tenth, in the second field, by the guest: one mask for all.
        world.PutInOrbit("h", stars[0], 0);
        Assert.Equal(1, Mask(story));
        world.PutInOrbit("g", stars[8], 0);
        world.PutInOrbit("g", stars[9], 0);
        Assert.Equal((1, 3), (story.Get("RAINBOW_WORLD0"), story.Get("RAINBOW_WORLD1")));
        // Again: no change; the others are kept.
        world.PutInOrbit("h", stars[0], 0);
        world.PutInOrbit("h", stars[3], 0);
        Assert.Equal(1 | 1 << 3 | 1 << 8 | 1 << 9, Mask(story));
        // Sent to everyone with the story.
        Assert.Equal(1 | 1 << 3, world.Snapshot().Story!.Get(story.Rules.Table, "RAINBOW_WORLD0"));
    }

    [ContentFact]
    public void FlyingIntoARainbowWorldsOrbitMarksIt()
    {
        var world = TwoPlayers();
        int star = RainbowStars(world)[4];
        var ship = world.Host.Ship;
        // In the rainbow world's inner system, a little way off it: some facing thrusts the ship into its orbit.
        for (int facing = 0; facing < Uqm.UqmMath.Facings; ++facing)
        {
            world.PutInSystem("h", star);
            var flight = SystemGroupTests.FlightAt(world.Systems![star], 0, 1200, inner: 0);
            ship.Ip = IpFlight.Restore(world.Systems[star], flight.Save() with { Facing = facing }, 2, 2, TestContent.Shapes!);
            for (int i = 0; i < 300 && ship.Ip.Orbit is null && ship.Ip.InnerPlanet is not null; ++i)
            {
                world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
                world.Step();
            }
            if (ship.Ip.Orbit is not null)
            {
                Assert.Equal(((int, int?))(0, null), ship.Ip.Orbit.Value);
                Assert.Equal(1 << 4, Mask(world.Story!));
                return;
            }
            Assert.Equal(0, Mask(world.Story!));
        }
        Assert.Fail("no facing took the ship into the rainbow world's orbit");
    }

    [ContentFact]
    public void AGameLoadedInOrbitMarksItAsUqmsDoes()
    {
        var world = TwoPlayers();
        int star = RainbowStars(world)[2];
        world.PutInOrbit("g", star, 0);
        // A save from before the marking (as an older one is): its story without the bit.
        world.Story!.Set("RAINBOW_WORLD0", 0);
        var loaded = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(1 << 2, Mask(loaded.Story!));
        // Out of orbit, nothing is marked.
        world.Find("g")!.Ship.Ip!.ReturnFromOrbit();
        world.Story.Set("RAINBOW_WORLD0", 0);
        loaded = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(0, Mask(loaded.Story!));
    }
}
