// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Entries into stars: the host keeps the ship's system waiting and its
/// input ignored while one plays, and clients play it from the contact the
/// host sends, at the right step even if they missed snapshots.
/// </summary>
public class StarEntryTrackerTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private static Star Sol => Systems.Map.Stars[82];

    private static ShipSnapshot Ship(string id, StarEntryState? entry) =>
        new(id, id, true, ShipKind.Flagship, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, Activity.OuterSystem, Sol.Number,
            Entry: entry);

    private static WorldSnapshot At(long frame, params ShipSnapshot[] ships) => new(frame, 2155, 2, 17, ships);

    [Fact]
    public void TheEntryPlaysFromTheContactOneStepAFrame()
    {
        int lx = Crateris.Core.Uqm.Units.UniverseToLogX(Sol.X) - 70, ly = Crateris.Core.Uqm.Units.UniverseToLogY(Sol.Y) + 90;
        var steps = StarEntry.Animate(lx, ly, 3, Sol, 34);
        var entry = new StarEntryState(100, lx, ly, HyperFlight.CenterX, HyperFlight.CenterY, 3, Sol.Number, steps.Count);
        var tracker = new StarEntryTracker(Systems.Map, 34);

        // On the contact frame: where and as it touched the star.
        tracker.Observe(At(100, Ship("a", entry)));
        var at = tracker.Of("a")!;
        Assert.Equal((3, (long)lx, (long)ly), (at.Frame, at.X, at.Y));
        // Then one step a frame.
        tracker.Observe(At(101, Ship("a", entry)));
        Assert.Equal(steps[0], new StarEntry.Step(tracker.Of("a")!.Frame, tracker.Of("a")!.X, tracker.Of("a")!.Y));
        // A client that missed snapshots picks it up at the right step.
        tracker.Observe(At(110, Ship("a", entry)));
        Assert.Equal(steps[9].Frame, tracker.Of("a")!.Frame);
        tracker.Observe(At(100 + steps.Count, Ship("a", entry)));
        Assert.Equal(steps[^1].Frame, tracker.Of("a")!.Frame);
        Assert.True(tracker.Playing("a"));
        // Over: the host stops sending it, and it would be ignored anyway.
        tracker.Observe(At(101 + steps.Count, Ship("a", entry)));
        Assert.False(tracker.Playing("a"));
        tracker.Observe(At(102 + steps.Count, Ship("a", null)));
        Assert.Empty(tracker.Current);
    }

    [ContentFact]
    public void WhileTheEntryPlaysTheSystemWaitsAndInputIsIgnored()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "h", "Host", Systems, TestContent.Shapes);
        var flight = world.Host.Ship.Flight;
        flight.PlaceAtUniverse(1752, 1430);
        flight.Stop();
        flight.StarJustLeft = null;
        for (int i = 0; i < 200 && world.Host.Ship.Activity == Activity.Hyperspace; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        var ship = world.Host.Ship;
        var entry = ship.Entry!;
        Assert.Equal(world.Frame, entry.ContactFrame);
        Assert.Equal(StarEntry.Animate(entry.Sprite.X, entry.Sprite.Y, entry.Facing, Sol, TestContent.Shapes.HyperFlagship.Count).Count, entry.Steps);
        Assert.Equal(entry, world.Snapshot().Ships[0].Entry);
        var arrival = (ship.Ip!.Location, ship.Ip.Facing);
        for (int i = 0; i < entry.Steps; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust | ShipInput.Right);
            world.Step();
            Assert.Equal(arrival, (ship.Ip!.Location, ship.Ip.Facing));   // waiting, input ignored
        }
        world.Step();
        Assert.Null(ship.Entry);
        Assert.Null(world.Snapshot().Ships[0].Entry);
        // Input read during it is ignored too (to the end plus the input delay); then it flies.
        for (int i = 0; i < world.Settings.InputDelayFrames + 20; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Right);
            world.Step();
        }
        Assert.NotEqual(arrival.Facing, ship.Ip!.Facing);   // flying now
    }

    [ContentFact]
    public void StartingInOrbitPlaysNoEntry()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "h", "Host", Systems, TestContent.Shapes);
        world.PutInOrbit("h", 82, 2);
        Assert.Null(world.Host.Ship.Entry);
    }

    [ContentFact]
    public void PutInOrbitDuringAnEntryHoldsNoInput()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "h", "Host", Systems, TestContent.Shapes);
        var flight = world.Host.Ship.Flight;
        flight.PlaceAtUniverse(1752, 1430);
        flight.Stop();
        flight.StarJustLeft = null;
        for (int i = 0; i < 200 && world.Host.Ship.Activity == Activity.Hyperspace; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        Assert.NotNull(world.Host.Ship.Entry);
        world.PutInOrbit("h", Sol.Number, 2);
        Assert.Null(world.Host.Ship.Entry);
        // The first press of the action key leaves orbit.
        world.SubmitInput("h", world.Frame + 1, ShipInput.Action);
        world.Step();
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void InputReadWhileTheEntryPlayedIsIgnoredHoweverLateItArrives()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "h", "Host", Systems, TestContent.Shapes);
        var flight = world.Host.Ship.Flight;
        flight.PlaceAtUniverse(1752, 1430);
        flight.Stop();
        flight.StarJustLeft = null;
        for (int i = 0; i < 200 && world.Host.Ship.Activity == Activity.Hyperspace; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        var entry = world.Host.Ship.Entry!;
        int delay = world.Settings.InputDelayFrames;
        long lastHeld = entry.ContactFrame + entry.Steps + delay;
        Assert.Equal(lastHeld, world.Host.Ship.InputHeldUntil);
        // A turn stamped for every frame up to lastHeld, sent in time: none
        // of them turns the ship.
        var ship = world.Host.Ship;
        while (world.Frame < lastHeld)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Right);
            world.Step();
        }
        var facing = ship.Ip!.Facing;
        Assert.Null(ship.Entry);
        Assert.Equal(SystemTransition.Arrive(Sol).Facing, facing);   // as it arrived
        // A lagging guest's turns, read during the entry, arrive after the
        // hold: dropped, and the last one isn't repeated on frames with no
        // input (a lost packet).
        for (int i = 0; i < 20; ++i)
        {
            world.SubmitInput("h", lastHeld - 5 + i % 6, ShipInput.Right);
            world.Step();
        }
        Assert.Equal(facing, ship.Ip!.Facing);
        // The first input read after it does.
        for (int i = 0; i < 20; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Right);
            world.Step();
        }
        Assert.NotEqual(facing, ship.Ip!.Facing);
    }
}
