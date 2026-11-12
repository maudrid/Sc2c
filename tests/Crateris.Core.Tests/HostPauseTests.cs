// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Xunit;

namespace Crateris.Core.Tests;

/// <summary>
/// The host's pause (docs/plans/stage-4.md, item 10, decision 15): the
/// world, its battles and the clock stop; nothing changes the world
/// meanwhile but players coming and going; every player is shown it.
/// </summary>
[Trait("Topic", "world")]
public class HostPauseTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    [ContentFact]
    public void ThePauseStopsTheWorldItsBattlesAndTheClockTillItLifts()
    {
        var world = WorldBattleTests.NewWorld(3);
        var wb = WorldBattleTests.Started(world);
        var guest = world.Find("g2")!;
        // The clock and the battle run first.
        WorldBattleTests.RunDisarmed(world, wb, 5);
        var before = world.Snapshot();
        Assert.True(world.SetHostPause(true));
        long frame = world.Frame;
        var clock = (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay);
        uint seed = wb.Battle.Random.Seed;
        var ship = wb.FighterOf("host")!.Starship.Ship!;
        var at = (ship.Current.X, ship.Current.Y, ship.Velocity.TravelAngle);
        // Input held through the pause is dropped, a late one stamped for its frames too.
        WorldBattleTests.Run(world, 48, _ => ShipInput.Thrust | ShipInput.Fire);
        Assert.Equal(frame + 48, world.Frame);   // frames go on (snapshots are sent), nothing in them moves
        Assert.Equal(clock, (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay));
        Assert.Equal(seed, wb.Battle.Random.Seed);
        Assert.Equal(at, (ship.Current.X, ship.Current.Y, ship.Velocity.TravelAngle));
        Assert.Null(wb.Outcome);
        var paused = world.Snapshot();
        Assert.True(paused.HostPaused);
        Assert.False(before.HostPaused);
        Assert.Equal(before.Ships.Select(s => (s.LogX, s.LogY, s.Facing, s.Fuel)), paused.Ships.Select(s => (s.LogX, s.LogY, s.Facing, s.Fuel)));
        world.SubmitInput("host", world.Frame, ShipInput.Fire);   // stamped for a paused frame, arriving late
        // Lifted: the world goes on.
        Assert.True(world.SetHostPause(false));
        WorldBattleTests.RunDisarmed(world, wb, 12, _ => ShipInput.Thrust);
        Assert.NotEqual(at, (ship.Current.X, ship.Current.Y, ship.Velocity.TravelAngle));
        Assert.False(world.Snapshot().HostPaused);
        Assert.Same(guest, world.Find("g2"));
    }

    [ContentFact]
    public void NothingChangesTheWorldWhilePausedButPlayersComingAndGoing()
    {
        var world = WorldBattleTests.NewWorld(2);
        world.Host.Ship.Hold.ResourceUnits = 100;
        Assert.True(world.SetHostPause(true));
        Assert.Equal(TradeRefusal.HostPaused, world.Give(new Gift("host", "g1", 10, new int[ShipHold.Categories], 0)));
        Assert.Equal(DispatchRefusal.HostPaused, world.Dispatch("host", 10, 10));
        Assert.Equal(RescueRefusal.HostPaused, world.Revive("host", "g1", 1));
        Assert.Equal(GameWorld.CommanderTalk.Paused, world.TalkToCommander("host", out _));
        Assert.False(world.ChooseReply("host", 0));
        Assert.False(world.DiscardCargo("host", 0));
        Assert.False(world.Rename("host", "Paused", null));
        Assert.False(world.Depart("host"));
        Assert.Equal(Starbase.OutfitOutcome.None, world.StarbaseKey("host", MenuKey.Select, out _));
        Assert.Equal(100u, world.Host.Ship.Hold.ResourceUnits);
        // Players still come and go.
        Assert.Equal(JoinResult.Joined, world.Join("g2", "Guest 2"));
        world.Leave("g1");
        Assert.False(world.Find("g1")!.Present);
        // Lifted, a gift goes.
        Assert.True(world.SetHostPause(false));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "g2", 10, new int[ShipHold.Categories], 0)));
    }

    [ContentFact]
    public void TheHostsSessionTogglesItAndEveryPlayerIsShownIt()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host", systems,
            TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, host.World.Join("guest", "Guest"));
        Assert.True(host.TogglePause());
        Assert.True(host.World.HostPaused);
        Assert.True(host.TogglePause());
        Assert.False(host.World.HostPaused);

        host.TogglePause();
        var names = TestContent.With(p => Names.Load(p));
        var me = new PlayerSession(systems, names, "guest", TestContent.Shapes.HyperFlagship.Count);
        var w = new WireWriter(16);
        SnapshotCodec.Encode(w, host.World.Snapshot(), null, "guest");
        me.Observe(SnapshotCodec.Decode(w.Written));
        Assert.True(me.HostPaused);
        Assert.Contains("PAUSED by the host", me.StatusLines());
        Assert.Equal(ShipInput.None, me.NextInput(ShipInput.Thrust));
        var hostView = new PlayerSession(systems, names, "host", TestContent.Shapes.HyperFlagship.Count);
        hostView.Observe(host.World.Snapshot());
        Assert.Contains("PAUSED - P or Pause: go on", hostView.StatusLines());
        // Its script's pause key (--script's pause step) is the session's to take.
        var scripted = new PlayerSession(systems, names, "host", TestContent.Shapes.HyperFlagship.Count,
            InputScript.Parse("none:2,pause:1,none:1"));
        var pressed = Enumerable.Range(0, 4).Select(_ => { scripted.NextInput(ShipInput.None); return scripted.TakePauseKey(); }).ToList();
        Assert.Equal(new[] { false, false, true, false }, pressed);
    }
}
