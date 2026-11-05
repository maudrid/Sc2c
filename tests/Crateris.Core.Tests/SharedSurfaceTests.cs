// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The shared surface on the host (stage 2b, item 9): players orbiting a
/// world land on its one surface, step at 35 frames a second, take what
/// they touch from the world for everyone, and go back to orbit with their
/// cargo; leaving while landed and saving keep the crew and cargo.
/// </summary>
public class SharedSurfaceTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82, Earth = 2, Luna = 1;
    private static readonly WorldId Moon = new(Sol, Earth, Luna);

    // The host and a guest both orbiting Luna.
    private static GameWorld AtLuna()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        return world;
    }

    private static void Run(GameWorld world, int frames, Func<string, ShipInput>? keys = null)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, keys?.Invoke(p.Id) ?? ShipInput.None);
            world.Step();
        }
    }

    private static Lander LanderOf(GameWorld world, string id) => world.Find(id)!.Ship.Lander!;

    // Until both landers are on the ground.
    private static void Land(GameWorld world, params string[] ids)
    {
        for (int i = 0; i < 200 && ids.Any(id => LanderOf(world, id).Phase != LanderPhase.Ground); ++i)
            Run(world, 1);
        Assert.All(ids, id => Assert.Equal(LanderPhase.Ground, LanderOf(world, id).Phase));
    }

    [Fact]
    public void ThirtyFiveSurfaceFramesEveryTwentyFourWorldFrames()
    {
        int total = 0;
        for (long f = 1; f <= 24 * 10; ++f)
        {
            int n = GameWorld.SurfaceFramesIn(f);
            Assert.InRange(n, 1, 2);
            total += n;
        }
        Assert.Equal(35 * 10, total);
    }

    [ContentFact]
    public void PlayersOrbitingOneWorldLandOnItsOneSurface()
    {
        var world = AtLuna();
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 484, 148));
        Assert.Equal(DispatchRefusal.None, world.Dispatch("guest", 500, 150));
        Assert.Single(world.Surfaces);
        var surface = world.SurfaceOf(Moon)!;
        Assert.Equal(new[] { LanderOf(world, "host"), LanderOf(world, "guest") }, surface.Landers);
        Assert.All(world.Players, p => Assert.Equal(Activity.Planetside, p.Ship.Activity));
        // Luna's nodes: its 9 deposits and 10 Brainbox Bulldozers.
        Assert.Equal(9, surface.Elements.Count(e => e.Kind == SurfaceElementKind.Mineral));
        Assert.Equal(10, surface.Elements.Count(e => e.Kind == SurfaceElementKind.Creature));
        Assert.Equal(DispatchRefusal.LanderOut, world.Dispatch("host", 484, 148));

        var snapshot = world.Snapshot();
        var s = Assert.Single(snapshot.Surfaces!);
        Assert.Equal(Moon, s.World);
        Assert.Equal(new[] { "host", "guest" }, s.Landers.Select(l => l.PlayerId));
        Assert.DoesNotContain(s.Elements, e => e.Kind == SurfaceElementKind.Mineral);   // clients have the nodes
        Assert.Equal(10, s.Elements.Count);
        Assert.All(snapshot.Ships, sh => Assert.Equal(Activity.Planetside, sh.Activity));

        // 35 surface frames a second: the landing's 48 take under a second and a half.
        Run(world, 24);
        Assert.Equal(LanderPhase.Landing, LanderOf(world, "host").Phase);
        Land(world, "host", "guest");
    }

    [ContentFact]
    public void ADepositTakenOnTheSurfaceIsTakenForEveryoneAndComesBackInTheHold()
    {
        var world = AtLuna();
        world.Dispatch("host", 484, 148);
        world.Dispatch("guest", 100, 100);
        Land(world, "host", "guest");
        var surface = world.SurfaceOf(Moon)!;
        var deposit = surface.Elements.First(e => e.Kind == SurfaceElementKind.Mineral);
        // Both landers put on the deposit: one takes it.
        LanderOf(world, "host").PlaceForTest(deposit.X, deposit.Y);
        LanderOf(world, "guest").PlaceForTest(deposit.X, deposit.Y);
        Run(world, 1);
        Assert.True(world.Planets.IsRetrieved(Moon, ScanType.Mineral, deposit.Node));
        Assert.Equal(1u << deposit.Node, world.Snapshot().OrbitedWorlds!.Single().MineralTaken);
        int carried = LanderOf(world, "host").MineralMass;
        Assert.True(carried > 0);
        Assert.Equal(0, LanderOf(world, "guest").MineralMass);   // the earlier lander wins

        // The host takes off: back in orbit with the minerals; the guest stays.
        var hold = world.Host.Ship.Hold;
        Run(world, 2, id => id == "host" ? ShipInput.TakeOff : ShipInput.None);
        Run(world, 40);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Equal(carried, hold.MineralMass);
        Assert.Equal(50, hold.Crew);
        Assert.Equal(new[] { LanderOf(world, "guest") }, world.SurfaceOf(Moon)!.Landers);

        // The guest takes off too: the surface goes. A new one has the deposit gone.
        Run(world, 2, _ => ShipInput.TakeOff);
        Run(world, 40);
        Assert.Empty(world.Surfaces);
        world.Dispatch("guest", 484, 148);
        Assert.DoesNotContain(world.SurfaceOf(Moon)!.Elements, e => e.Kind == SurfaceElementKind.Mineral && e.Node == deposit.Node);
        Assert.Equal(8, world.SurfaceOf(Moon)!.Elements.Count(e => e.Kind == SurfaceElementKind.Mineral));
    }

    [ContentFact]
    public void TheLandersKeysAreTheShipsKeys()
    {
        var world = AtLuna();
        world.Dispatch("host", 484, 148);
        Land(world, "host");
        var lander = LanderOf(world, "host");
        int facing = lander.Facing;
        Run(world, 10, _ => ShipInput.Right);
        Assert.NotEqual(facing, lander.Facing);
        Run(world, 1, _ => ShipInput.Fire);
        Assert.Contains(world.SurfaceOf(Moon)!.Elements, e => e.Kind == SurfaceElementKind.Bolt);
        var (x, y) = (lander.X, lander.Y);
        Run(world, 10, _ => ShipInput.Thrust);
        Assert.NotEqual((x, y), (lander.X, lander.Y));
    }

    [ContentFact]
    public void LeavingWhileLandedTakesTheLanderOffAtOnceWithItsCrewAndCargo()
    {
        // Decision 5: like a retreat; nobody loses a lander by dropping out.
        var world = AtLuna();
        world.Dispatch("host", 484, 148);
        world.Dispatch("guest", 484, 148);
        Land(world, "host", "guest");
        var surface = world.SurfaceOf(Moon)!;
        var deposit = surface.Elements.First(e => e.Kind == SurfaceElementKind.Mineral);
        var lander = LanderOf(world, "guest");
        lander.PlaceForTest(deposit.X, deposit.Y);
        Run(world, 1);
        int carried = lander.MineralMass;
        Assert.True(carried > 0);
        var guest = world.Find("guest")!;
        world.Leave("guest");
        Assert.Null(guest.Ship.Lander);
        Assert.Equal(Activity.Orbit, guest.Ship.Activity);
        Assert.Equal((10, 1, carried), (guest.Ship.Hold.Crew, guest.Ship.Hold.Landers, guest.Ship.Hold.MineralMass));
        Assert.Equal(new[] { LanderOf(world, "host") }, surface.Landers);
        Run(world, 5);   // the surface goes on without it

        Assert.Equal(JoinResult.Rejoined, world.Join("guest", "Guest"));
        Assert.Equal(Activity.Orbit, guest.Ship.Activity);
        world.Leave("host");
        Assert.Empty(world.Surfaces);
    }

    [ContentFact]
    public void ASaveKeepsLandedPlayersInOrbitWithTheirCrewAndCargo()
    {
        var world = AtLuna();
        world.Dispatch("host", 484, 148);
        Land(world, "host");
        var deposit = world.SurfaceOf(Moon)!.Elements.First(e => e.Kind == SurfaceElementKind.Mineral);
        LanderOf(world, "host").PlaceForTest(deposit.X, deposit.Y);
        Run(world, 1);
        int carried = LanderOf(world, "host").MineralMass;
        Assert.True(carried > 0);
        string json = HostSave.ToJson(world);
        Assert.Equal(Activity.Planetside, world.Host.Ship.Activity);   // saving changes nothing

        var loaded = HostSave.FromJson(json, Systems, TestContent.Shapes);
        var ship = loaded.Host.Ship;
        Assert.Equal(Activity.Orbit, ship.Activity);
        Assert.Equal((50, 1, carried), (ship.Hold.Crew, ship.Hold.Landers, ship.Hold.MineralMass));
        Assert.True(loaded.Planets.IsRetrieved(Moon, ScanType.Mineral, deposit.Node));
    }

    [ContentFact]
    public void ALanderLostOnTheSurfaceIsLostToItsShip()
    {
        var world = AtLuna();
        world.Dispatch("guest", 484, 148);
        Land(world, "guest");
        LanderOf(world, "guest").KillForTest();
        Assert.Equal(1, world.Find("guest")!.Ship.Hold.Landers);
        string json = HostSave.ToJson(world);   // saved while it explodes: lost with its crew
        Run(world, 24 * 4);
        var ship = world.Find("guest")!.Ship;
        Assert.Equal(Activity.Orbit, ship.Activity);
        Assert.Equal((0, 0), (ship.Hold.Landers, ship.Hold.Crew));
        Assert.Empty(world.Surfaces);
        Assert.Equal(DispatchRefusal.NoLander, world.Dispatch("guest", 484, 148));
        var saved = HostSave.FromJson(json, Systems, TestContent.Shapes).Find("guest")!.Ship.Hold;
        Assert.Equal((0, 0), (saved.Landers, saved.Crew));
    }

    [ContentFact]
    public void ARequestFromAShipNotInOrbitIsRefused()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        Assert.Equal(DispatchRefusal.NotInOrbit, world.Dispatch("host", 484, 148));
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Dispatch("host", 484, 148);
        // Put somewhere else while landed (--start-orbit): the lander comes back first.
        world.PutInOrbit("host", Sol, Earth);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Empty(world.Surfaces);
        Assert.Equal(50, world.Host.Ship.Hold.Crew);
    }
    [ContentFact]
    public void EachPlayersHazardsStartRoundItsOwnLander()
    {
        // Decision 3, through the host: on the world with every hazard at
        // its worst, two landers far apart each have their own.
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        world.Join("guest", "Guest");
        world.PutInOrbit("host", 240, 1);
        world.PutInOrbit("guest", 240, 1);
        world.Dispatch("host", 200, 150);
        world.Dispatch("guest", 700, 150);
        var surface = world.SurfaceOf(new WorldId(240, 1))!;
        var host = LanderOf(world, "host");
        var guest = LanderOf(world, "guest");
        var seen = new HashSet<SurfaceElement>();
        for (int i = 0; i < 48 && world.Surfaces.Count > 0; ++i)
        {
            Run(world, 1);
            foreach (var e in surface.Elements.Where(e => e.Hazard && seen.Add(e)))
            {
                int toHost = Math.Abs(e.X - host.X), toGuest = Math.Abs(e.X - guest.X);
                Assert.Same(toHost < toGuest ? host : guest, e.Owner);
            }
        }
        Assert.Contains(seen, e => e.Owner == host);
        Assert.Contains(seen, e => e.Owner == guest);
    }

    [ContentFact]
    public void TheTakeOffKeyNeverLeavesOrbit()
    {
        // A take-off press that reaches the host after the lander is back
        // does nothing; only the action key (Navigate) leaves orbit.
        var world = AtLuna();
        Run(world, 3, _ => ShipInput.TakeOff);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Run(world, 1, _ => ShipInput.None);
        Run(world, 1, _ => ShipInput.Action);
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
    }
}

/// <summary>The client's side of a landing (stage 2b, item 9).</summary>
public class LandingSessionTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);

    [ContentFact]
    public void TheOrbitMenuAndItsScansLastThroughALanding()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        world.PutInOrbit("host", 82, 2, 1);
        var player = new Session.PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        void Step(ShipInput input = ShipInput.None)
        {
            world.SubmitInput("host", world.Frame + 1, input);
            world.Step();
            player.Observe(world.Snapshot());
        }
        Step();
        var menu = player.OrbitMenu!;
        player.PressMenu(MenuKey.Select);   // Scan: the cursor on Auto scan
        player.PressMenu(MenuKey.Down);     // Dispatch lander
        player.PressMenu(MenuKey.Select);   // picking the site
        player.PressMenu(MenuKey.Select);   // picked
        var site = player.TakeLandingRequest()!.Value;
        Assert.Null(player.TakeLandingRequest());   // once
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", site.X, site.Y));
        Step();
        Assert.Equal(Session.PlayerScreen.Planetside, player.Screen);
        Assert.Same(menu, player.OrbitMenu);
        Assert.NotNull(player.MyLander);
        var page = menu.Page;
        player.PressMenu(MenuKey.Cancel);   // the keys drive the lander, not the menu
        Assert.Equal(page, menu.Page);
        for (int i = 0; i < 24 * 4 && world.Host.Ship.Lander is { Phase: not LanderPhase.Ground }; ++i)
            Step();
        Step(ShipInput.TakeOff);
        for (int i = 0; i < 48 && world.Host.Ship.Lander is not null; ++i)
            Step();
        Assert.Equal(Session.PlayerScreen.Orbit, player.Screen);
        Assert.Same(menu, player.OrbitMenu);   // back to the same visit's scan screen
        player.LandingRefused(DispatchRefusal.LanderOut);
        Assert.Equal("The lander is already down.", player.OrbitMessage);
    }
}
