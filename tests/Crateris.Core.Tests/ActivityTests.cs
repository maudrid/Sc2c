// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>The host's per-player activities: hyperspace, systems and orbit.</summary>
public class ActivityTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int SolNumber = 82;
    private static Star Sol => Systems.Map.Stars[SolNumber];

    private static GameWorld NewWorld() =>
        GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems);

    private static void Run(GameWorld world, string id, ShipInput input, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            world.SubmitInput(id, world.Frame + 1, input);
            world.Step();
        }
    }

    [Fact]
    public void SystemsGetThirtyFramesForEveryTwentyFourWorldFrames()
    {
        int total = 0;
        for (long f = 1; f <= 24 * 10; ++f)
        {
            int n = GameWorld.SystemFramesIn(f);
            Assert.InRange(n, 1, 2);
            total += n;
            if (f % 24 == 0)
                Assert.Equal(30 * (int)(f / 24), total);
        }
    }

    [Fact]
    public void FlyingIntoAStarEntersItsSystemWhereUqmArrives()
    {
        var world = NewWorld();
        var ship = world.Host.Ship;
        Star target = Systems.Map.Stars[0];
        ship.Flight.StarJustLeft = null;
        ship.Flight.PlaceAtUniverse(target.X, target.Y);
        ship.Flight.LogY += 40 * 32;   // 40 pixels below it, facing up

        for (int i = 0; i < 300 && ship.Activity == Activity.Hyperspace; ++i)
            Run(world, "host", ShipInput.Thrust, 1);

        Assert.Equal(Activity.OuterSystem, ship.Activity);
        Assert.Equal(target.Number, ship.SystemStar);
        var arrival = SystemTransition.Arrive(target);
        Assert.Equal(arrival.Location, ship.Ip!.Location);
        Assert.Equal((target.X, target.Y), (ship.Flight.UniverseX, ship.Flight.UniverseY));
    }

    [Fact]
    public void InASystemTheShipFliesThirtyFramesASecondExactlyAsIpFlight()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 4);   // Jupiter
        Run(world, "host", ShipInput.Action, 1);  // leave orbit
        var ship = world.Host.Ship;
        Assert.Equal(Activity.InnerSystem, ship.Activity);

        // The same flight on its own, stepped 30 times a second.
        var alone = IpFlight.Restore(Systems[SolNumber], ship.Ip!.Save(), ship.Loadout.Thrusters, ship.Loadout.Jets);
        long start = world.Frame;
        Run(world, "host", ShipInput.Thrust | ShipInput.Right, 48);
        int systemFrames = 0;
        for (long f = start + 1; f <= world.Frame; ++f)
            systemFrames += GameWorld.SystemFramesIn(f);
        Assert.Equal(60, systemFrames);
        for (int i = 0; i < systemFrames; ++i)
            alone.Step(ShipInput.Thrust | ShipInput.Right);

        Assert.Equal(alone.Save().Velocity, ship.Ip!.Save().Velocity);
        Assert.Equal(alone.Location, ship.Ip.Location);
        Assert.Equal(alone.Facing, ship.Ip.Facing);
    }

    [Fact]
    public void TheActionKeyLeavesOrbitOncePerPress()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 2, 1);   // the Moon
        var ship = world.Host.Ship;
        Assert.Equal(Activity.Orbit, ship.Activity);
        Assert.NotNull(ship.Orbit!.Nodes);

        Run(world, "host", ShipInput.None, 10);
        Assert.Equal(Activity.Orbit, ship.Activity);             // orbit holds without input

        Run(world, "host", ShipInput.Action, 1);
        Assert.Equal(Activity.InnerSystem, ship.Activity);        // left orbit, on the Moon
        Assert.Null(ship.Orbit);
        Assert.Equal(2, ship.Ip!.InnerPlanet);

        Run(world, "host", ShipInput.Action, 30);                 // still holding: nothing more
        Assert.Equal(Activity.InnerSystem, ship.Activity);
    }

    [Fact]
    public void LeavingTheSystemComesOutOnTheStarInHyperspace()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 8);   // Pluto, near the edge
        Run(world, "host", ShipInput.Action, 1);
        var ship = world.Host.Ship;

        // Fly straight out: inner system, then the outer view's zooms, then off the map.
        for (int i = 0; i < 24 * 120 && ship.Activity != Activity.Hyperspace; ++i)
            Run(world, "host", ShipInput.Thrust, 1);

        Assert.Equal(Activity.Hyperspace, ship.Activity);
        Assert.Null(ship.Ip);
        Assert.Equal((Sol.X, Sol.Y), (ship.Flight.UniverseX, ship.Flight.UniverseY));
        Assert.Equal(SolNumber, ship.Flight.StarJustLeft);
    }

    [Fact]
    public void AGuestJoiningWhileTheHostIsInOrbitUndocksNextToTheHostsWorld()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 2);   // Earth
        var host = world.Host.Ship;

        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        var guest = world.Find("guest")!;
        Assert.True(guest.UndockPending);
        Assert.Equal(Activity.InnerSystem, guest.Ship.Activity);  // flying, not in orbit
        Assert.Equal(SolNumber, guest.Ship.SystemStar);
        Assert.Equal(2, guest.Ship.Ip!.InnerPlanet);
        Assert.Equal(host.Ip!.Location, guest.Ship.Ip.Location);   // at the host, by Earth
        Assert.Equal((0, 0), guest.Ship.Ip.Velocity.Current());

        // The guest doesn't drop into Earth's orbit by sitting there.
        Run(world, "guest", ShipInput.None, 24);
        Assert.Equal(Activity.InnerSystem, guest.Ship.Activity);

        // Both are in the same system: each snapshot carries the other.
        var snap = world.Snapshot();
        Assert.All(snap.Ships, s => Assert.Equal(SolNumber, s.SystemStar));
        Assert.Contains(snap.Ships, s => s.Id == "host" && s.Activity == Activity.Orbit && s.OrbitPlanet == 2);
        Assert.Contains(snap.Ships, s => s.Id == "guest" && s.Activity == Activity.InnerSystem && s.InnerPlanet == 2);
    }

    [Fact]
    public void AGuestJoiningWhileTheHostFliesInASystemUndocksThere()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 5);
        Run(world, "host", ShipInput.Action, 1);
        // Out of Saturn's inner system into the outer view.
        for (int i = 0; i < 24 * 30 && world.Host.Ship.Activity != Activity.OuterSystem; ++i)
            Run(world, "host", ShipInput.Thrust, 1);
        Assert.Equal(Activity.OuterSystem, world.Host.Ship.Activity);

        world.Join("guest", "Guest");
        var guest = world.Find("guest")!.Ship;
        Assert.Equal(Activity.OuterSystem, guest.Activity);
        Assert.Equal(world.Host.Ship.Ip!.Location, guest.Ip!.Location);
    }

    [Fact]
    public void TheSnapshotCarriesWhatHasBeenTakenFromOrbitedWorlds()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 2, 1);
        world.Planets.SetRetrieved(new WorldId(SolNumber, 2, 1), ScanType.Biological, 3);
        var snap = world.Snapshot();
        var orbited = Assert.Single(snap.OrbitedWorlds!);
        Assert.Equal((SolNumber, 2, (int?)1), (orbited.Star, orbited.Planet, orbited.Moon));
        Assert.Equal(1u << 3, orbited.BiologicalTaken);
    }

    [Fact]
    public void TheSaveKeepsShipsInSystemsAndInOrbit()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 2, 1);
        world.Join("guest", "Guest");
        Run(world, "guest", ShipInput.Thrust | ShipInput.Left, 20);

        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, Systems);
        var host = loaded.Host.Ship;
        var guest = loaded.Find("guest")!.Ship;
        Assert.Equal(Activity.Orbit, host.Activity);
        Assert.NotNull(host.Orbit);
        Assert.Equal(world.Host.Ship.Ip!.Location, host.Ip!.Location);
        Assert.Equal(world.Find("guest")!.Ship.Ip!.Location, guest.Ip!.Location);
        Assert.Equal((0, 0), guest.Ip.Velocity.Current());   // away ships wait stopped
        Assert.Equal(world.Find("guest")!.Ship.Ip!.Facing, guest.Ip.Facing);
        Assert.Equal(world.Host.Ship.Ip.ZoomRadius, host.Ip.ZoomRadius);

        // Saving what was loaded gives the same save, once the guest has stopped.
        world.Leave("guest");
        Assert.Equal(HostSave.ToJson(world), HostSave.ToJson(loaded));

        // Without the star map a save with ships in systems can't be loaded.
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(json));
    }

    [Fact]
    public void OlderSavesLoadWithEveryShipInHyperspace()
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        node["version"] = 3;
        foreach (var p in node["players"]!.AsArray())
        {
            p!.AsObject().Remove("system_star");
            p.AsObject().Remove("system");
        }
        var loaded = HostSave.FromJson(node.ToJsonString(), Systems);
        Assert.Equal(Activity.Hyperspace, loaded.Host.Ship.Activity);
    }

    [Fact]
    public void ABadSystemInASaveIsRefused()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 2, 1);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["players"]![0]!["system"]!["orbit_moon"] = 3;     // Earth has 2 moons
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), Systems));

        node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["players"]![0]!["system"]!["zoom_radius"] = 1000;  // not a zoom level
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), Systems));
    }

    [Fact]
    public void WorldsThatCantBeOrbitedAreRefusedByPutInOrbitAndNothingChanges()
    {
        var world = NewWorld();
        string before = HostSave.ToJson(world);
        Assert.Throws<ArgumentException>(() => world.PutInOrbit("host", SolNumber, 2, 0));   // the starbase
        Assert.Throws<ArgumentException>(() => world.PutInOrbit("host", SolNumber, 9));      // Sol has 9 planets
        Assert.Throws<ArgumentException>(() => world.PutInOrbit("host", SolNumber, 0, 0));   // Mercury has no moon
        Assert.Equal(before, HostSave.ToJson(world));
        Assert.Equal(Activity.Hyperspace, world.Host.Ship.Activity);

        // From orbit elsewhere, a refused call leaves the ship in that orbit.
        world.PutInOrbit("host", SolNumber, 4);
        Assert.Throws<ArgumentException>(() => world.PutInOrbit("host", SolNumber, 2, 0));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(4, world.Host.Ship.Ip!.Orbit!.Value.Planet);
        Assert.NotNull(world.Host.Ship.Orbit);
    }

    [Fact]
    public void OrbitDataAlwaysFollowsTheOrbitedWorld()
    {
        var world = NewWorld();
        var ship = world.Host.Ship;
        Assert.Null(ship.Orbit);
        world.PutInOrbit("host", SolNumber, 2, 1);
        Assert.Equal(Activity.Orbit, ship.Activity);
        Assert.Single(ship.Orbit!.Nodes!.Energy);                 // the Moon base
        world.PutInOrbit("host", SolNumber, 4);
        Assert.Equal(PlanetInfo.GasGiantAtmosphere, ship.Orbit!.Report!.AtmoDensity);   // Jupiter's
        Run(world, "host", ShipInput.Action, 1);
        Assert.Null(ship.Orbit);
        Assert.Equal(Activity.InnerSystem, ship.Activity);
    }

    [Fact]
    public void SnapshotsKeepHyperspaceAndSystemVelocitySeparate()
    {
        var world = NewWorld();
        world.PutInOrbit("host", SolNumber, 8);
        Run(world, "host", ShipInput.Action, 1);
        Run(world, "host", ShipInput.Thrust, 24);
        var me = world.Snapshot().Ships.Single();
        Assert.Equal((0, 0), (me.VelocityX, me.VelocityY));                 // stopped in hyperspace
        Assert.NotEqual((0, 0), (me.IpVelocityX, me.IpVelocityY));          // flying in the system
    }
}
