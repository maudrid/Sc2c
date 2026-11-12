// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Wrecks, revival and game over (stage 4a item 7): a ship destroyed in a
/// battle is a wreck at the battle's place once it's over (decision 7),
/// doing nothing; a friend flying into it gives it crew, keeping at least
/// 1 (decision 8); wrecks are saved, among them a ship destroyed in a
/// battle still running (save version 10, decision 14); the flagship's
/// death ends the game for everyone (decision 12). Most run in a world of
/// hyperspace only, on sheets of our own; those in Earth's system need the
/// content.
/// </summary>
[Trait("Topic", "combat")]
public class WreckTests
{
    private static readonly int[] NoMinerals = new int[ShipHold.Categories];

    // A battle started by the host with every other player in it, where g1
    // (and any of <paramref name="lost"/>) is destroyed and the battle won:
    // g1 a wreck. Before it, g1 carries 3 units of minerals and 40 RU.
    private static WorldBattle Wrecked(GameWorld world, params string[] lost)
    {
        var g1 = world.Find("g1")!.Ship;
        g1.Hold.MoveCargo(new[] { 3, 0, 0, 0, 0, 0, 0, 0 }, 0, +1);
        g1.Hold.ResourceUnits = 40;
        var wb = Started(world);
        foreach (var id in lost.Prepend("g1"))
            Kill(wb.Battle, Fighter(wb, id).Starship);
        Run(world, 1);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        return wb;
    }

    // The rescuer flies out of the wreck's reach and back into it.
    private static void FlyOutAndBack(GameWorld world, string id)
    {
        var flight = world.Find(id)!.Ship.Flight;
        flight.LogX += 1000;
        Run(world, 1);
        flight.LogX -= 1000;
        Run(world, 1);
    }

    [Fact]
    public void ADestroyedShipIsAWreckAtTheBattlesPlaceDoingNothing()
    {
        var world = NewWorld(3);
        var g1 = world.Find("g1")!.Ship;
        g1.Hold.MoveCargo(new[] { 3, 0, 0, 0, 0, 0, 0, 0 }, 0, +1);
        g1.Hold.ResourceUnits = 40;
        // Joined a little way from the marker, within its reach: it waits there.
        var wb = world.StartBattle("host", Ilwrath);
        g1.Flight.LogX += 12;
        FlyIn(world, "g1", "g2");
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        uint fuel = g1.Flight.Fuel;
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        // Mid-battle it explodes, its player watching on; not a wreck yet.
        Assert.Equal((Activity.Battle, true), (g1.Activity, Fighter(wb, "g1").Exit == BattleExit.Destroyed));
        Assert.Empty(world.Wrecks);
        Assert.False(world.Snapshot().Ships.Single(s => s.Id == "g1").Wrecked);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);

        // Once it's over: at the battle's place, with no crew, its fuel and cargo kept.
        Assert.Equal(new[] { "g1" }, world.Wrecks.Select(p => p.Id));
        Assert.Equal((wb.Place.X, wb.Place.Y), (g1.Flight.LogX, g1.Flight.LogY));
        Assert.Equal((true, 0, fuel, Activity.Hyperspace), (g1.Destroyed, g1.Hold.Crew, g1.Flight.Fuel, g1.Activity));
        var shown = world.Snapshot().Ships;
        Assert.Equal(new[] { false, true, false }, shown.Select(s => s.Wrecked));

        // It doesn't fly, give or take gifts, drop cargo, or join battles.
        Run(world, 10, _ => ShipInput.Thrust | ShipInput.Left | ShipInput.Fire);
        Assert.Equal((wb.Place.X, wb.Place.Y), (g1.Flight.LogX, g1.Flight.LogY));
        Assert.Equal(TradeRefusal.Wrecked, world.Give(new Gift("g1", "host", 1, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.Wrecked, world.Give(new Gift("host", "g1", 1, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "g2", 1, NoMinerals, 0)));
        Assert.False(world.DiscardCargo("g1", 0));
        Assert.Equal((3, 40u), (g1.Hold.Minerals[0], g1.Hold.ResourceUnits));
        Assert.Throws<InvalidOperationException>(() => world.StartBattle("g1", Ilwrath));
        Assert.False(world.GameOver);
        Assert.False(world.FlagshipLost);
    }

    [Theory]
    [InlineData(1, 1, RescueRefusal.NotEnough)]   // a rescuer of 1 can't give any
    [InlineData(2, 1, RescueRefusal.None)]        // nor more than 1 of 2
    [InlineData(2, 2, RescueRefusal.NotEnough)]
    [InlineData(50, 10, RescueRefusal.None)]      // the escort's kit holds 10
    [InlineData(50, 11, RescueRefusal.NoRoom)]
    [InlineData(50, 0, RescueRefusal.Nothing)]
    public void RevivalKeepsTheRescuerAtLeastOne(int rescuerCrew, int given, RescueRefusal expected)
    {
        var world = NewWorld(3);
        Wrecked(world);
        var host = world.Host.Ship;
        var g1 = world.Find("g1")!.Ship;
        host.Hold.SetCrew(rescuerCrew);
        // Back in flight in the wreck's reach: not offered it until it flies out and back.
        Run(world, 3);
        Assert.Empty(world.TakeRescueOffers());
        host.Flight.Velocity.SetComponents(4, 0);
        FlyOutAndBack(world, "host");
        if (rescuerCrew < 2)
        {
            // One who can't rescue is neither offered it nor stopped.
            Assert.Empty(world.TakeRescueOffers());
            Assert.Empty(world.RescuesFor("host"));
        }
        else
        {
            Assert.Equal(new[] { ("host", "g1") }, world.TakeRescueOffers());
            Assert.True(host.Flight.Velocity.IsZero, "stopped at the wreck");
            Assert.Equal(new[] { "g1" }, world.RescuesFor("host").Select(p => p.Id));
        }
        host.Flight.Stop();
        Assert.Equal(expected, world.Revive("host", "g1", given));
        if (expected != RescueRefusal.None)
        {
            Assert.Equal((rescuerCrew, 0, true), (host.Hold.Crew, g1.Hold.Crew, g1.Destroyed));
            return;
        }
        Assert.Equal((rescuerCrew - given, given, false), (host.Hold.Crew, g1.Hold.Crew, g1.Destroyed));
        Assert.True(host.Hold.Crew >= 1);
        Assert.Equal(new[] { new Revival("host", "g1", given) }, world.TakeRevivals());
        Assert.Empty(world.Wrecks);
        Assert.False(world.Snapshot().Ships.Single(s => s.Id == "g1").Wrecked);
        // It flies on, with its cargo, and gives and joins battles again.
        var at = (g1.Flight.LogX, g1.Flight.LogY);
        Run(world, 10, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        Assert.NotEqual(at, (g1.Flight.LogX, g1.Flight.LogY));
        Assert.Equal((3, 40u), (g1.Hold.Minerals[0], g1.Hold.ResourceUnits));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("g1", "g2", 1, NoMinerals, 0)));
        Assert.Equal(RescueRefusal.NotAWreck, world.Revive("g2", "g1", 1));
    }

    [Fact]
    public void TwoRescuersAtOnceOnlyTheFirstGives()
    {
        var world = NewWorld(4);
        Wrecked(world);
        FlyOutAndBack(world, "host");
        FlyOutAndBack(world, "g2");
        var offers = world.TakeRescueOffers();
        Assert.Contains(("host", "g1"), offers);
        Assert.Contains(("g2", "g1"), offers);
        int g2Crew = world.Find("g2")!.Ship.Hold.Crew;
        Assert.Equal(RescueRefusal.None, world.Revive("g2", "g1", 4));
        Assert.Equal(RescueRefusal.NotAWreck, world.Revive("host", "g1", 4));
        Assert.Equal((g2Crew - 4, 50, 4), (world.Find("g2")!.Ship.Hold.Crew, world.Host.Ship.Hold.Crew, world.Find("g1")!.Ship.Hold.Crew));
    }

    [Fact]
    public void TwoWrecksAreRevivedOneByOne()
    {
        var world = NewWorld(4);
        Wrecked(world, "g2");
        Assert.Equal(new[] { "g1", "g2" }, world.Wrecks.Select(p => p.Id));
        // Both at the battle's place: the rescuer is offered one at a time (the first), and may revive either.
        FlyOutAndBack(world, "host");
        Assert.Equal(new[] { ("host", "g1") }, world.TakeRescueOffers());
        Run(world, 2);
        Assert.Empty(world.TakeRescueOffers());   // nor the other on the frames after
        Assert.Equal(new[] { "g1", "g2" }, world.RescuesFor("host").Select(p => p.Id));
        Assert.Equal(RescueRefusal.None, world.Revive("host", "g2", 2));
        Assert.Equal(RescueRefusal.None, world.Revive("host", "g1", 2));
        Assert.Equal(46, world.Host.Ship.Hold.Crew);
        Assert.Empty(world.Wrecks);
    }

    [Fact]
    public void RescuesAreRefusedWithAReason()
    {
        var world = NewWorld(4);
        Wrecked(world, "g2");
        Assert.Equal(RescueRefusal.NoOne, world.Revive("host", "host", 1));
        Assert.Equal(RescueRefusal.NoOne, world.Revive("host", "nobody", 1));
        Assert.Equal(RescueRefusal.NotAWreck, world.Revive("host", "g3", 1));
        // A wreck can't rescue another.
        Assert.Equal(RescueRefusal.Busy, world.Revive("g2", "g1", 1));
        Assert.Empty(world.RescuesFor("g2"));
        // Out of its reach, or in a battle: not here, busy.
        var g3 = world.Find("g3")!.Ship;
        g3.Flight.LogX += 1000;
        Assert.Equal(RescueRefusal.NotHere, world.Revive("g3", "g1", 1));
        Assert.Empty(world.RescuesFor("g3"));
        g3.Flight.LogX -= 1000;
        Assert.Equal(new[] { "g1", "g2" }, world.RescuesFor("g3").Select(p => p.Id));
        Assert.Equal(RescueRefusal.None, world.Revive("g3", "g2", 1));
        var wb = world.StartBattle("g3", Ilwrath);
        Assert.Equal(RescueRefusal.Busy, world.Revive("g3", "g1", 1));
        Assert.Empty(world.RescuesFor("g3"));
        // A wreck still in its battle (destroyed, the battle running) isn't one yet.
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "g3").Starship);
        Run(world, 1);
        Assert.Equal(RescueRefusal.NotAWreck, world.Revive("host", "g3", 1));
        Assert.Equal(RescueRefusal.None, world.Revive("host", "g1", 1));
    }

    [Fact]
    public void AWrecksPlayerLeavesAndRejoinsAWreck()
    {
        var world = NewWorld(3);
        var wb = Wrecked(world);
        var g1 = world.Find("g1")!.Ship;
        world.Leave("g1");
        // Away, it waits in the save, out of space: not revived meanwhile.
        Assert.Empty(world.Wrecks);
        Assert.Equal(RescueRefusal.NoOne, world.Revive("host", "g1", 1));
        Run(world, 5);
        Assert.Equal(JoinResult.Rejoined, world.Join("g1", "Guest 1"));
        Assert.Equal((true, 0, wb.Place.X, wb.Place.Y), (g1.Destroyed, g1.Hold.Crew, g1.Flight.LogX, g1.Flight.LogY));
        Assert.True(world.Snapshot().Ships.Single(s => s.Id == "g1").Wrecked);
        Assert.Equal(RescueRefusal.None, world.Revive("host", "g1", 5));
    }

    [Fact]
    public void APlayerLeavingAfterTheirShipIsDestroyedComesBackToTheirWreck()
    {
        // Destroyed in a battle still running, away as it ends: a wreck at its place all the same.
        var world = NewWorld(3);
        var wb = Started(world);
        var g1 = world.Find("g1")!.Ship;
        g1.Flight.LogX += 8;   // where it waits
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        world.Leave("g1");
        Assert.Equal(BattleExit.Destroyed, Fighter(wb, "g1").Exit);
        Assert.Equal(Activity.Battle, g1.Activity);
        Assert.Equal(JoinResult.Rejoined, world.Join("g1", "Guest 1"));   // back watching
        world.Leave("g1");
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((true, Activity.Hyperspace, wb.Place.X), (g1.Destroyed, g1.Activity, g1.Flight.LogX));
        Assert.Equal(JoinResult.Rejoined, world.Join("g1", "Guest 1"));
        Assert.Equal(new[] { "g1" }, world.Wrecks.Select(p => p.Id));
    }

    [Fact]
    public void AWreckInALaterBattlesMarkerIsntInIt()
    {
        var world = NewWorld(3);
        var first = Wrecked(world);
        // Another battle where the first was (the host is back there): the wreck doesn't join, nor is it offered.
        var next = world.StartBattle("host", Ilwrath);
        Assert.Equal((first.Place.X, first.Place.Y), (next.Place.X, next.Place.Y));
        Assert.Empty(world.MarkersFor("g1"));
        FlyIn(world, "g1", "g2");
        Assert.Null(next.FighterOf("g1"));
        Assert.NotNull(next.FighterOf("g2"));   // joined the battle, not offered the wreck
        Assert.Empty(world.TakeRescueOffers());
        Assert.Equal(RescueRefusal.Busy, world.Revive("g2", "g1", 1));
        for (int i = 0; i < 60 && !Warped(next.Battle); ++i)
            Run(world, 1);
        Kill(next.Battle, next.Enemy);
        RunOut(world, next);
        // Out of it, by the wreck: flying out and back offers it.
        Assert.Equal(new[] { "g1" }, world.Wrecks.Select(p => p.Id));
        Run(world, 2);
        Assert.Empty(world.TakeRescueOffers());
        FlyOutAndBack(world, "g2");
        Assert.Equal(new[] { ("g2", "g1") }, world.TakeRescueOffers());
    }

    [Fact]
    public void AWreckIsSavedAndLoaded()
    {
        var world = NewWorld(3);
        var wb = Wrecked(world);
        var loaded = HostSave.FromJson(HostSave.ToJson(world));
        var ship = loaded.Find("g1")!.Ship;
        Assert.Equal((true, 0, wb.Place.X, wb.Place.Y), (ship.Destroyed, ship.Hold.Crew, ship.Flight.LogX, ship.Flight.LogY));
        Assert.Equal((3, 40u), (ship.Hold.Minerals[0], ship.Hold.ResourceUnits));
        Assert.False(loaded.Find("g2")!.Ship.Destroyed);
        Assert.Equal(JoinResult.Rejoined, loaded.Join("g1", "Guest 1"));
        Assert.Equal(JoinResult.Rejoined, loaded.Join("g2", "Guest 2"));
        Assert.Equal(new[] { "g1" }, loaded.Wrecks.Select(p => p.Id));
        Run(loaded, 5, _ => ShipInput.Thrust);
        Assert.Equal(wb.Place.X, ship.Flight.LogX);
        Assert.Equal(RescueRefusal.None, loaded.Revive("g2", "g1", 3));
        Assert.False(HostSave.FromJson(HostSave.ToJson(loaded)).Find("g1")!.Ship.Destroyed);
    }

    [Fact]
    public void AShipDestroyedInABattleStillRunningIsSavedAsAWreck()
    {
        var world = NewWorld(3);
        var g1 = world.Find("g1")!.Ship;
        g1.Hold.MoveCargo(new[] { 0, 2, 0, 0, 0, 0, 0, 0 }, 0, +1);
        var wb = Started(world);
        g1.Flight.LogX += 8;   // it waits a little way from the marker
        uint fuel = g1.Flight.Fuel;
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        // Still exploding, and once its explosion is over, the battle running on.
        foreach (int frames in new[] { 0, 60 })
        {
            Run(world, frames);
            Assert.True(wb.Battle.InBattle);
            var loaded = HostSave.FromJson(HostSave.ToJson(world));
            var ship = loaded.Find("g1")!.Ship;
            Assert.Equal((true, 0, wb.Place.X, wb.Place.Y, fuel), (ship.Destroyed, ship.Hold.Crew, ship.Flight.LogX, ship.Flight.LogY, ship.Flight.Fuel));
            Assert.Equal(2, ship.Hold.Minerals[1]);
            Assert.Equal((false, 50), (loaded.Host.Ship.Destroyed, loaded.Host.Ship.Hold.Crew));
            Assert.Empty(loaded.Battles);
        }
    }

    [Fact]
    public void SaveVersionTenKeepsWrecksAndOlderSavesLoad()
    {
        var world = NewWorld(2);
        Wrecked(world);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        Assert.Equal(10, (int)node["version"]!);
        JsonNode G1(JsonNode n) => n["players"]!.AsArray().Single(p => (string)p!["id"]! == "g1")!;
        Assert.True((bool)G1(node)["wrecked"]!);

        // An older save with a wreck: refused; without one (none had them), it loads.
        var old = JsonNode.Parse(node.ToJsonString())!;
        old["version"] = 9;
        Assert.Contains("wrecks", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(old.ToJsonString())).Message);
        G1(old).AsObject().Remove("wrecked");
        Assert.False(HostSave.FromJson(old.ToJsonString()).Find("g1")!.Ship.Destroyed);

        // A wreck with crew, or the flagship a wreck: refused.
        var crewed = JsonNode.Parse(node.ToJsonString())!;
        G1(crewed)["hold"]!["crew"] = 3;
        Assert.Contains("crew", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(crewed.ToJsonString())).Message);
        var flagship = JsonNode.Parse(node.ToJsonString())!;
        var host = flagship["players"]!.AsArray().Single(p => (string)p!["id"]! == "host")!;
        host["wrecked"] = true;
        host["hold"]!["crew"] = 0;
        Assert.Contains("flagship", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(flagship.ToJsonString())).Message);
    }

    [Fact]
    public void TheFlagshipsDeathEndsTheGameAsItsExplosionEnds()
    {
        var world = NewWorld(3);
        var wb = Started(world);
        world.Find("g2")!.Ship.Hold.ResourceUnits = 10;
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        Run(world, 1);
        // Its crew run out: no save from now (decision 12), but the game goes on while it explodes.
        Assert.Equal((true, false), (world.FlagshipLost, world.GameOver));
        Assert.Throws<InvalidOperationException>(() => HostSave.ToJson(world));
        int frames = 1;
        while (!world.GameOver && frames < 300)
        {
            Assert.True(wb.Battle.InBattle);
            Run(world, 1);
            ++frames;
        }
        // UQM's: the explosion's 36 frames and the enemy's victory ditty's
        // 72, then new_ship finds no ship left to pick (CrewEnlisted ~0).
        Assert.Equal(36 + 72 + 2, frames);
        Assert.True(world.GameOver);
        Assert.Null(wb.Outcome);   // the battle stops with the others still fighting in it
        Assert.Equal((true, 0), (world.Host.Ship.Destroyed, world.Host.Ship.Hold.Crew));
        Assert.Null(Fighter(wb, "g1").Exit);

        // The world stops: nothing steps, joins or changes.
        long frame = world.Frame;
        var elements = string.Join(" ", wb.Battle.List.Elements.Select(e => $"{e.Current.X},{e.Current.Y}"));
        Run(world, 5, _ => ShipInput.Thrust | ShipInput.Fire);
        Assert.Equal((frame, elements), (world.Frame, string.Join(" ", wb.Battle.List.Elements.Select(e => $"{e.Current.X},{e.Current.Y}"))));
        Assert.Equal(JoinResult.GameOver, world.Join("g9", "Late"));
        Assert.Null(world.Find("g9"));
        // A guest leaving now only goes: not out of the stopped battle, no fuel paid.
        var g2 = world.Find("g2")!.Ship;
        uint g2Fuel = g2.Flight.Fuel;
        world.Leave("g2");
        Assert.Equal((false, (BattleExit?)null, wb, g2Fuel), (world.Find("g2")!.Present, Fighter(wb, "g2").Exit, g2.Battle, g2.Flight.Fuel));
        Assert.Equal(JoinResult.GameOver, world.Join("g2", "Guest 2"));
        Assert.Equal(TradeRefusal.GameOver, world.Give(new Gift("g1", "host", 1, NoMinerals, 0)));
        Assert.Equal(RescueRefusal.GameOver, world.Revive("g1", "host", 1));
        Assert.False(world.DiscardCargo("g1", 0));
        Assert.Throws<InvalidOperationException>(() => world.StartBattle("g1", Ilwrath));
        Assert.Throws<InvalidOperationException>(() => HostSave.ToJson(world));
    }

    [Fact]
    public void TheWreckLiesOnTheStarItsBattlesStarterHadJustLeft()
    {
        // Off Sol, the starter just out of another star (7) as it starts the battle; the guest's own isn't 7.
        var world = NewWorld(2);
        foreach (var p in world.Players)
            p.Ship.Flight.LogX += 20000;
        world.Host.Ship.Flight.StarJustLeft = 7;
        var wb = world.StartBattle("host", Ilwrath);
        FlyIn(world, "g1");
        var g1 = world.Find("g1")!.Ship;
        Assert.NotEqual(7, g1.Flight.StarJustLeft);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        // Saved mid-battle, and once the battle is won: a wreck on the star.
        Assert.Equal(7, HostSave.FromJson(HostSave.ToJson(world)).Find("g1")!.Ship.Flight.StarJustLeft);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((true, 7), (g1.Wreck, g1.Flight.StarJustLeft));
    }

    [Fact]
    public void TheGameOverStopsEveryBattleAtOnce()
    {
        // The host's battle, and the guest's own far off, started after it.
        var world = NewWorld(2);
        world.Find("g1")!.Ship.Flight.LogX += 20000;
        var first = world.StartBattle("host", Ilwrath);
        var other = world.StartBattle("g1", Ilwrath);
        for (int i = 0; i < 60 && !(Warped(first.Battle) && Warped(other.Battle)); ++i)
            Run(world, 1);
        string State(Battle b) => b.Random.Seed + " " + string.Join(" ", b.List.Elements.Select(e => $"{e.Current.X},{e.Current.Y},{e.LifeSpan}"));
        Kill(first.Battle, Fighter(first, "host").Starship);
        for (int i = 0; i < 300 && !world.GameOver; ++i)
        {
            string before = State(other.Battle);
            Run(world, 1);
            if (world.GameOver)
                Assert.Equal(before, State(other.Battle));   // not stepped in the frame the game ended
            else
                Assert.NotEqual(before, State(other.Battle));
        }
        Assert.True(world.GameOver);
        Assert.Null(other.Outcome);
    }

    [Fact]
    public void TheFlagshipDyingAtItsBattlesEndIsTheGameOverToo()
    {
        // Both sides' last ships die together: the battle ends, lost, and the game with it.
        var world = NewWorld(1);
        var wb = Started(world);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Equal((true, true), (world.GameOver, world.FlagshipLost));
        Assert.Empty(world.Wrecks);   // the flagship is never a wreck
    }

    [Fact]
    public void AGuestsDeathIsntTheGameOver()
    {
        var world = NewWorld(2);
        var wb = Started(world);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 80);
        Assert.Equal((false, false, true), (world.FlagshipLost, world.GameOver, wb.Battle.InBattle));
        Assert.NotNull(HostSave.ToJson(world));
    }

    // ------------------------------------------------------------ with the content

    [ContentFact]
    public void InASystemTheWreckIsAtTheMarkerAndFliesOnWithTheEscapeCounter()
    {
        var world = InSol(3);
        var guest = world.Find("g1")!.Ship;
        guest.Ip!.EscapeCounter = 250;
        Run(world, 12, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        var wb = world.StartBattle("host", Ilwrath);
        var place = world.Host.Ship.Ip!.Save();
        int back = (guest.Ip!.Facing + 8) % 16;
        for (int i = 0; i < 300 && guest.Ip!.Facing != back; ++i)
            Run(world, 1, id => id == "g1" ? ShipInput.Left : ShipInput.None);
        for (int i = 0; i < 400 && guest.Battle is null; ++i)
            Run(world, 1, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        Assert.Equal(Activity.Battle, guest.Activity);
        Assert.NotEqual((wb.Place.X, wb.Place.Y), ((int)guest.Ip!.Location.X, (int)guest.Ip.Location.Y));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);

        // Saved mid-battle as a wreck at the marker, in Earth's inner system.
        var midBattle = HostSave.FromJson(HostSave.ToJson(world), WorldBattleTests.Systems.Value, TestContent.Shapes).Find("g1")!.Ship;
        Assert.Equal((true, Activity.InnerSystem, wb.Place.X, wb.Place.Y),
            (midBattle.Destroyed, midBattle.Activity, (int)midBattle.Ip!.Location.X, (int)midBattle.Ip.Location.Y));

        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((true, Activity.InnerSystem), (guest.Destroyed, guest.Activity));
        Assert.Equal((wb.Place.X, wb.Place.Y), ((int)guest.Ip!.Location.X, (int)guest.Ip.Location.Y));
        Assert.Equal((place.ScreenX, place.ScreenY, place.ZoomRadius), ((int)guest.Ip.Screen.X, (int)guest.Ip.Screen.Y, (int)guest.Ip.ZoomRadius));
        var json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, WorldBattleTests.Systems.Value, TestContent.Shapes).Find("g1")!.Ship;
        Assert.Equal((true, wb.Place.X, wb.Place.Y), (loaded.Destroyed, (int)loaded.Ip!.Location.X, (int)loaded.Ip.Location.Y));
        // A save with the wreck in orbit round Luna: refused.
        var node = JsonNode.Parse(json)!;
        var system = node["players"]!.AsArray().Single(p => (string)p!["id"]! == "g1")!["system"]!;
        Assert.Equal(Earth, (int)system["inner_planet"]!);
        system["orbit_planet"] = Earth;
        system["orbit_moon"] = Luna;
        Assert.Contains("orbit", Assert.Throws<InvalidDataException>(
            () => HostSave.FromJson(node.ToJsonString(), WorldBattleTests.Systems.Value, TestContent.Shapes)).Message);
        // Not put in orbit, nor flying.
        Assert.Throws<InvalidOperationException>(() => world.PutInOrbit("g1", Sol, Earth, Luna));
        Run(world, 10, _ => ShipInput.Thrust);
        Assert.Equal((wb.Place.X, wb.Place.Y), ((int)guest.Ip!.Location.X, (int)guest.Ip.Location.Y));

        // The host, back in flight at the marker, revives it; it flies on with the escape counter.
        var host = world.Host.Ship;
        Assert.Equal(new[] { "g1" }, world.RescuesFor("host").Select(p => p.Id));
        Assert.Equal(RescueRefusal.None, world.Revive("host", "g1", 6));
        Assert.Equal((false, 6, IpFlight.EscapeFrames), (guest.Destroyed, guest.Hold.Crew, guest.Ip.EscapeCounter));
        Run(world, 5, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        Assert.Equal(Activity.InnerSystem, guest.Activity);
        Assert.NotEqual((wb.Place.X, wb.Place.Y), ((int)guest.Ip!.Location.X, (int)guest.Ip.Location.Y));
        Assert.Equal(44, host.Hold.Crew);
    }

    [ContentFact]
    public void ARevivalWaitsForAStoryTalk()
    {
        // g1's own battle, lost: a wreck where it undocked, by the host.
        var world = InSol(3);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        var wb = world.StartBattle("g1", Ilwrath);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        RunOut(world, wb);
        Assert.Equal(new[] { "g1" }, world.Wrecks.Select(p => p.Id));
        Assert.NotEmpty(world.RescuesFor("host"));
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Find("g2")!);
        Assert.True(world.StoryPaused);
        Assert.Empty(world.RescuesFor("host"));
        Assert.Equal(RescueRefusal.Paused, world.Revive("host", "g1", 1));
    }

    [ContentFact]
    public void NoStoryTalkStartsAsTheFlagshipExplodes()
    {
        // A guest with the Lunar base aboard reaching the starbase then would pause the game, and the game over with it.
        var world = InSol(2);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        var wb = world.StartBattle("host", Ilwrath);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        Run(world, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Find("g1")!);
        Assert.Equal((false, (StarbaseVisit?)null), (world.StoryPaused, world.Find("g1")!.Ship.Visit));
        for (int i = 0; i < 300 && !world.GameOver; ++i)
            Run(world, 1);
        Assert.True(world.GameOver);
    }

    [ContentFact]
    public void AStoryTalkStartedAsTheFlagshipsCrewRunsOutDoesntHoldOffTheGameOver()
    {
        // g1, with the Lunar base aboard, flies into the starbase in the very
        // frame the flagship's crew runs out in the host's battle (the
        // flights go first, then the battles: the story talk starts before
        // the battle sees the death). g2 fights a battle of its own there.
        (GameWorld World, WorldBattle Host, WorldBattle Other) Make()
        {
            var world = InSol(3);
            world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
            world.Story!.Set("STARBASE_AVAILABLE", 1);
            world.Story.Set("MOONBASE_ON_SHIP", 1);
            // g2's first, so a dying guest's battle would be found before the host's.
            var other = world.StartBattle("g2", Ilwrath);
            return (world, world.StartBattle("host", Ilwrath), other);
        }
        // Steering g1 at the starbase (Earth's first moon), as OpeningTests does.
        static void Fly(GameWorld world)
        {
            var ip = world.Find("g1")!.Ship.Ip!;
            var target = ip.MoonScreen[0];
            int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)(target.X - ip.Screen.X), (short)(target.Y - ip.Screen.Y)));
            int turn = (want - ip.Facing) & (Uqm.UqmMath.Facings - 1);
            var keys = turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right;
            Run(world, 1, id => id == "g1" ? keys : ShipInput.None);
        }
        // How many frames g1 takes to touch it.
        var (dry, _, _) = Make();
        int touch = 0;
        while (touch < 24 * 60 && !dry.StoryPaused)
        {
            Fly(dry);
            ++touch;
        }
        Assert.True(dry.StoryPaused);

        var (world, wb, other) = Make();
        for (int i = 0; i < touch - 1; ++i)
            Fly(world);
        Assert.False(world.StoryPaused);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        Kill(other.Battle, Fighter(other, "g2").Starship);   // a guest dying then: its battle still waits
        Fly(world);
        Assert.Equal((true, true, BattleExit.Destroyed), (world.StoryPaused, world.FlagshipLost, Fighter(wb, "host").Exit));
        Assert.Throws<InvalidOperationException>(() => HostSave.ToJson(world));
        // The flagship's battle runs on through the pause to the game over; g2's waits, as the pause has it.
        string State(Battle b) => b.Random.Seed + " " + string.Join(" ", b.List.Elements.Select(e => $"{e.Current.X},{e.Current.Y},{e.LifeSpan}"));
        Assert.Equal((true, (BattleOutcome?)null), (other.Battle.InBattle, other.Outcome));
        string otherAt = State(other.Battle);
        int frames = 1;
        for (; frames < 300 && !world.GameOver; ++frames)
            Run(world, 1);
        Assert.Equal((true, true), (world.GameOver, world.StoryPaused));
        Assert.Equal(36 + 72 + 2, frames);
        Assert.Equal(otherAt, State(other.Battle));
    }

    [ContentFact]
    public void AfterTheGameOverNothingChanges()
    {
        // g1 talking to the commander, g3 docked at the starbase's menu, g2 in orbit round Luna.
        var world = InSol(4);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        world.DialogueLocks = Comm.DialogueLocks.Load(DataDir);
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);
        world.CommanderMet = true;
        world.VisitStarbase(world.Find("g1")!);
        world.VisitStarbase(world.Find("g3")!);
        Assert.Equal(GameWorld.CommanderTalk.Started, world.TalkToCommander("g1", out _));
        Assert.NotEmpty(world.TalkFor("g1")!.Replies);
        var g3 = world.Find("g3")!;
        Assert.Equal(Activity.Starbase, g3.Ship.Activity);
        world.PutInOrbit("g2", Sol, Earth, Luna);
        var wb = world.StartBattle("host", Ilwrath);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        for (int i = 0; i < 300 && !world.GameOver; ++i)
            Run(world, 1);
        Assert.True(world.GameOver);

        Assert.Equal(DispatchRefusal.NotInOrbit, world.Dispatch("g2", 100, 100));
        Assert.Null(world.Find("g2")!.Ship.Lander);
        Assert.False(world.ChooseReply("g1", 0));
        Assert.False(world.Rename("g3", "Renamed", "Someone"));
        Assert.NotEqual("Renamed", g3.Ship.Name);
        byte state = g3.Ship.Visit!.Menu!.State;
        Assert.Equal(Starbase.OutfitOutcome.None, world.StarbaseKey("g3", MenuKey.Down, out _));
        Assert.Equal(state, g3.Ship.Visit.Menu.State);
        Assert.Equal(GameWorld.CommanderTalk.NotAtMenu, world.TalkToCommander("g3", out _));
        Assert.False(world.Depart("g3"));
        Assert.Equal(Activity.Starbase, g3.Ship.Activity);
        Assert.Throws<InvalidOperationException>(() => world.PutInOrbit("g1", Sol, Earth, Luna));
    }

    [ContentFact]
    public void TheLastGuestLeavingAsTheFlagshipExplodesEndsTheGameAndTheHostSaysSo()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        host.StartBattle(Ilwrath);
        host.Hello(1, "g1", "Guest 1", Protocol.Version);
        // g2 stays by the marker (there as it started: outside the battle), taking snapshots.
        host.Hello(2, "g2", "Guest 2", Protocol.Version);
        var wb = JoinedInSol(host, "g1");
        Assert.Null(wb.FighterOf("g2"));
        var g2 = new SnapshotReceiver();
        WorldSnapshot? Send()
        {
            var shown = g2.Receive(host.SnapshotFor(2), out _);
            host.GuestInput(2, host.World.Frame + 1, 0, shown!.Frame);   // it has this one
            return shown;
        }
        Kill(wb.Battle, wb.FighterOf("host")!.Starship);
        var before = host.Step(ShipInput.None);
        Assert.Equal(before.Frame, Send()!.Frame);
        Assert.Equal(3, before.Ships.Count);
        host.GuestLeft(1);
        Assert.True(host.World.GameOver);   // the battle ended with no one left in it, the flagship destroyed
        // The next step's snapshot is the world as it stopped, after the last one sent; g2 takes it as a delta.
        var after = host.Step(ShipInput.None);
        Assert.Equal(before.Frame + 1, after.Frame);
        Assert.Equal(new[] { "host", "g2" }, after.Ships.Select(s => s.Id));
        Assert.NotEqual(Activity.Battle, after.Ships[0].Activity);
        var shownAfter = g2.Receive(host.SnapshotFor(2), out bool full);
        Assert.False(full);
        Assert.Equal((after.Frame, "host g2"), (shownAfter!.Frame, string.Join(" ", shownAfter.Ships.Select(s => s.Id))));
        Assert.Same(after, host.Step(ShipInput.None));
    }

    [ContentFact]
    public void TheHostsSaveIsKeptFromTheFlagshipsDeathAndLateGuestsAreRefused()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        var log = new List<string>();
        host.Logged += log.Add;
        host.StartBattle(Ilwrath);
        Assert.True(Protocol.Accepted(host.Hello(1, "g1", "Guest 1", Protocol.Version).Result));
        var wb = host.World.Battles.Single();
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            host.Step(ShipInput.None);
        Assert.NotNull(host.SaveJson());
        Kill(wb.Battle, wb.FighterOf("host")!.Starship);
        host.Step(ShipInput.None);
        // Between the death and the game over (the autosave, the window closing): none.
        Assert.False(host.World.GameOver);
        Assert.Null(host.SaveJson());
        for (int i = 0; i < 300 && !host.World.GameOver; ++i)
            host.Step(ShipInput.None);
        Assert.True(host.World.GameOver);
        Assert.Null(host.SaveJson());
        host.Step(ShipInput.None);
        Assert.Single(log, l => l == "Game over: the flagship was lost");
        var late = host.Hello(2, "g2", "Guest 2", Protocol.Version);
        Assert.Equal(JoinResult.GameOver, late.Result);
        Assert.False(Protocol.Accepted(late.Result));
        Assert.False(host.InGame(2));
    }

    // The host's battle by Earth, the guests (there as it started, at the
    // host's place) flying off and back into its marker, everyone warped in.
    private static WorldBattle JoinedInSol(HostSession host, params string[] guests)
    {
        var world = host.World;
        var wb = world.Battles.Single();
        foreach (var id in guests)
            world.Find(id)!.Ship.Ip!.EscapeCounter = 250;
        void Fly(Func<string, ShipInput> keys)
        {
            foreach (var id in guests)
                world.SubmitInput(id, world.Frame + 1, keys(id));
            host.Step(ShipInput.None);
        }
        for (int i = 0; i < 12; ++i)
            Fly(_ => ShipInput.Thrust);
        int back = (world.Find(guests[0])!.Ship.Ip!.Facing + 8) % 16;
        for (int i = 0; i < 300 && world.Find(guests[0])!.Ship.Ip!.Facing != back; ++i)
            Fly(_ => ShipInput.Left);
        for (int i = 0; i < 400 && guests.Any(id => world.Find(id)!.Ship.Battle is null); ++i)
            Fly(id => world.Find(id)!.Ship.Battle is null ? ShipInput.Thrust : ShipInput.None);
        Assert.All(guests, id => Assert.NotNull(wb.FighterOf(id)));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            host.Step(ShipInput.None);
        return wb;
    }

    [ContentFact]
    public void TheHostLogsRescuesUntilTheWireSendsThem()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        var log = new List<string>();
        host.Logged += log.Add;
        host.StartBattle(Ilwrath);
        host.Hello(1, "g1", "Guest 1", Protocol.Version);
        host.Hello(2, "g2", "Guest 2", Protocol.Version);
        var world = host.World;
        var wb = JoinedInSol(host, "g1", "g2");
        // g1 is destroyed and the battle won.
        Kill(wb.Battle, wb.FighterOf("g1")!.Starship);
        host.Step(ShipInput.None);
        Kill(wb.Battle, wb.Enemy);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            host.Step(ShipInput.None);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Equal(RescueRefusal.NoOne, host.GuestRevive(9, "g1", 1));
        Assert.Equal(RescueRefusal.None, host.GuestRevive(2, "g1", 2));
        Assert.Contains("g2 revived g1 with 2 crew", log);
        Assert.Equal(RescueRefusal.NotAWreck, host.Revive("host", "g1", 1));
        Assert.Contains(log, l => l.StartsWith("host's rescue of g1 refused: NotAWreck"));
        host.Step(ShipInput.None);
        Assert.Empty(world.TakeRevivals());
        Assert.Empty(world.TakeRescueOffers());
    }
}
