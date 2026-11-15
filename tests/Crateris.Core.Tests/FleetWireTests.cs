// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.FleetBattleTests;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The host's fleet on the wire (stage 4b item 9), on the real path: the
/// host's world, its snapshot, encoded for a guest as the host sends it,
/// and decoded as the guest's client reads it. The host's fleet in the
/// host's ship; each battle marker's race; the host's fleet ships and pick
/// screen in the battle a guest fights in; the captain's windows as the
/// ships' code changes them (the Shofixti's Glory Device, the VUX's
/// limpets). In a world of hyperspace only, on sheets of our own
/// (WorldBattleTests'), but for the session's test.
/// </summary>
[Trait("Topic", "fleet")]
public class FleetWireTests
{
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private const string Spathi = "SPATHI_SHIP", Arilou = "ARILOU_SHIP", Vux = "VUX_SHIP", Shofixti = "SHOFIXTI_SHIP";

    // The world's snapshot as the host sends it to a viewer (a delta from a base it has, or in full), as the viewer decodes it.
    private static WorldSnapshot Sent(WorldSnapshot snapshot, string viewer, WorldSnapshot? sentBase = null, WorldSnapshot? decodedBase = null)
    {
        var w = new WireWriter();
        SnapshotCodec.Encode(w, snapshot, sentBase, viewer);
        return SnapshotCodec.Decode(w.Written, decodedBase);
    }

    private static BattleArmadaSnapshot HostsArmada(GameWorld world, string viewer) =>
        Sent(world.Snapshot(), viewer).Battles!.Single().FighterOf("host")!.Armada!;

    [Fact]
    public void TheHostsFleetGoesToEveryoneInTheHostsShip()
    {
        var world = WithFleet(2, (Spathi, 20), (Arilou, 4));
        var first = world.Snapshot();
        var seen = Sent(first, "g1");
        var host = seen.Ships.Single(s => s.Id == "host");
        Assert.Equal(world.Fleet.Ships, host.Fleet!.Ships);
        Assert.Equal(new[] { (Spathi, 20, 0), (Arilou, 4, 1) }, host.Fleet.Ships.Select(s => (s.Race, s.Crew, s.Slot)));
        Assert.All(host.Fleet.Ships, s => Assert.InRange(s.Captain, HostFleet.CaptainNameOffset, HostFleet.CaptainNameOffset + 15));
        Assert.Null(seen.Ships.Single(s => s.Id == "g1").Fleet);   // only the host has a fleet (decision 3)
        // The host's own: the same, unencoded.
        Assert.Equal(world.Fleet.Snapshot(), first.Ships.Single(s => s.Id == "host").Fleet);

        // The shipyard and the roster change it: a delta carries it.
        world.Fleet.SetCrew(Rules, 1, 2);
        Run(world, 1);
        var crewed = world.Snapshot();
        var delta = Sent(crewed, "g1", first, seen);
        Assert.Equal(2, delta.Ships.Single(s => s.Id == "host").Fleet!.InSlot(1)!.Crew);
        world.Fleet.Scrap(0);
        Run(world, 1);
        var scrapped = Sent(world.Snapshot(), "g1", crewed, delta);
        Assert.Equal(new[] { 1 }, scrapped.Ships.Single(s => s.Id == "host").Fleet!.Ships.Select(s => s.Slot));
        // No fleet ships left: an empty fleet.
        world.Fleet.Scrap(1);
        Run(world, 1);
        Assert.Empty(Sent(world.Snapshot(), "g1").Ships.Single(s => s.Id == "host").Fleet!.Ships);
    }

    [ContentFact]
    public void AGuestsSessionSeesTheHostsFleetAsTheHostSendsIt()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.AddFleetShips(Arilou, 3);
        host.Hello(1, "g1", "Guest 1", Protocol.Version);
        host.Step(ShipInput.None);
        var guest = new PlayerSession(WorldBattleTests.Systems.Value, null, "g1", TestContent.Shapes.HyperFlagship.Count);
        guest.Observe(SnapshotCodec.Decode(host.SnapshotFor(1)!));
        var fleet = guest.Snapshot!.Ships.Single(s => s.IsHost).Fleet!;
        Assert.Equal(host.World.Fleet.Ships, fleet.Ships);
        Assert.Equal(new[] { 0, 1, 2 }, fleet.Ships.Select(s => s.Slot));
    }

    [Fact]
    public void EachMarkerCarriesTheRaceOfItsEnemyFleetsFirstShip()
    {
        var world = WithFleet(2);
        var wb = world.StartBattle("host", new EnemyFleet(new[] { new EnemyFleetShip("spathi", 1), new EnemyFleetShip(Ilwrath) }));
        var marker = Assert.Single(Sent(world.Snapshot(), "g1").Markers!);
        Assert.Equal((wb.Id, "spathi"), (marker.Id, marker.EnemyShip));
        // The Spathi dead and the Ilwrath come in: still the encounter race's (EncounterRace), the battle's name.
        WarpIn(world, wb);
        Kill(wb.Battle, wb.Enemy);
        for (int i = 0; i < 200 && wb.Enemy.Code is not IlwrathShip; ++i)
            RunDisarmed(world, wb, 1);
        Assert.Equal(Ilwrath, wb.EnemyShipNow.Id);
        Assert.Equal("spathi", Assert.Single(Sent(world.Snapshot(), "g1").Markers!).EnemyShip);
        Assert.Equal(Ilwrath, Sent(world.Snapshot(), "host").Battles!.Single().Enemy.Ship);   // the status, the ship now
        // Another battle's marker its own race.
        world.Find("g1")!.Ship.Flight.LogX += 5000;
        Run(world, 1);
        var other = world.StartBattle("g1", "arilou");
        var markers = Sent(world.Snapshot(), "g1").Markers!;
        Assert.Equal(new[] { (wb.Id, "spathi"), (other.Id, "arilou") }, markers.Select(m => (m.Id, m.EnemyShip)));
    }

    [Fact]
    public void AGuestInTheBattleSeesTheHostsFleetShipsAndPickScreen()
    {
        var world = WithFleet(2, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        var host = Fighter(wb, "host");
        Assert.Null(Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("g1")!.Armada);   // a guest has none

        // Choosing the first while the guest fights (decision 2): every ship can be picked, the cursor on the flagship.
        var armada = HostsArmada(world, "g1");
        Assert.Equal(new BattlePickSnapshot(0, PickShipMenu.FlagshipColumn), armada.Pick);
        Assert.True(armada.Pick!.OnFlagship);
        var captains = host.FleetShips.Select(f => (int)f.Joined.Captain).ToArray();
        Assert.Equal(new[]
        {
            new BattleFleetShipSnapshot(0, "spathi", captains[0], 20, 30, null, false, true),
            new BattleFleetShipSnapshot(1, "arilou", captains[1], 4, 6, null, false, true),
        }, armada.Ships);
        // The cursor moves: the guest sees it, on the Arilou's slot.
        world.PickShipKey("host", MenuKey.Left);
        world.PickShipKey("host", MenuKey.Left);
        Assert.Equal(new BattlePickSnapshot(0, 1), HostsArmada(world, "g1").Pick);
        Assert.Equal(1, HostsArmada(world, "g1").Pick!.Slot);
        world.PickShipKey("host", MenuKey.Down);
        Assert.Equal(new BattlePickSnapshot(1, 1), HostsArmada(world, "g1").Pick);
        world.PickShipKey("host", MenuKey.Up);

        // The Arilou picked: the pick screen closed, it flies (no ship can be picked meanwhile), the status names it.
        Assert.Equal(PickOutcome.Chosen, world.PickShipKey("host", MenuKey.Select));
        var seen = Sent(world.Snapshot(), "g1").Battles!.Single();
        armada = seen.FighterOf("host")!.Armada!;
        Assert.Null(armada.Pick);
        Assert.Equal(new[] { (false, false), (true, false) }, armada.Ships.Select(s => (s.Flown, s.CanPick)));
        Assert.Equal("arilou", seen.FighterOf("host")!.Status.Ship);
        WarpIn(world, wb);
        // Its crew as it flies, its element's.
        host.FleetShips[1].Starship.RaceDesc!.CrewLevel = 3;
        Assert.Equal(3, HostsArmada(world, "g1").InSlot(1)!.Crew);

        // It retreats: out with its crew, the pick screen open again (the guest fights on), the Arilou not pickable.
        UntilPicking(world, wb, id => id == "host" ? ShipInput.TakeOff : ShipInput.None);
        armada = HostsArmada(world, "g1");
        Assert.Equal(new BattlePickSnapshot(0, PickShipMenu.FlagshipColumn), armada.Pick);   // afresh, on the flagship
        Assert.Equal(new BattleFleetShipSnapshot(1, "arilou", captains[1], 3, 6, BattleExit.Retreated, true, false), armada.InSlot(1));
        Assert.True(armada.InSlot(0)!.CanPick);
        // A talk takes the Spathi from the fleet meanwhile: it can't be picked; the host's fleet in their ship says so too.
        world.Fleet.Remove(Spathi);
        var now = Sent(world.Snapshot(), "g1");
        Assert.False(now.Battles!.Single().FighterOf("host")!.Armada!.InSlot(0)!.CanPick);
        Assert.Equal(new[] { Arilou }, now.Ships.Single(s => s.Id == "host").Fleet!.Ships.Select(s => s.Race));
        world.Fleet.Restore(Rules, world.Fleet.Ships.Prepend(new FleetShip(Spathi, 20, (byte)captains[0], 0)));
        Assert.True(HostsArmada(world, "g1").InSlot(0)!.CanPick);

        // The Spathi picked and destroyed: dead, of no crew, and gone from the host's fleet (decision 4).
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        Kill(wb.Battle, host.FleetShips[0].Starship);
        UntilPicking(world, wb);
        now = Sent(world.Snapshot(), "g1");
        armada = now.Battles!.Single().FighterOf("host")!.Armada!;
        Assert.Equal(new BattleFleetShipSnapshot(0, "spathi", captains[0], 0, 30, BattleExit.Destroyed, true, false), armada.InSlot(0));
        Assert.Equal(new[] { Arilou }, now.Ships.Single(s => s.Id == "host").Fleet!.Ships.Select(s => s.Race));
        // The client reads it from the battle it's sent: the host's ship there, with the armada.
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "g1", 1);
        session.Observe(now);
        Assert.Equal(armada, session.Battle!.FighterOf("host")!.Armada);
        // The flagship picked: the last choice; nothing more to pick.
        Assert.Equal(PickOutcome.Chosen, PickAt(world, null));
        Assert.All(HostsArmada(world, "g1").Ships, s => Assert.False(s.CanPick));
        Assert.Null(HostsArmada(world, "g1").Pick);
    }

    [Fact]
    public void TheHostChoosingAloneIsSentTheirOwnPickScreen()
    {
        var world = WithFleet(1, (Spathi, 20));
        world.StartBattle("host", Ilwrath);
        var armada = HostsArmada(world, "host");
        Assert.Equal(new BattlePickSnapshot(0, PickShipMenu.FlagshipColumn), armada.Pick);
        Assert.True(Assert.Single(armada.Ships).CanPick);
        // Without fleet ships the host has no armada.
        var alone = WithFleet(1);
        alone.StartBattle("host", Ilwrath);
        Assert.Null(Sent(alone.Snapshot(), "host").Battles!.Single().FighterOf("host")!.Armada);
    }

    [Fact]
    public void TheShofixtisGloryDeviceStepsOnItsCaptainsWindowOnTheWire()
    {
        var world = WithFleet(2, (Shofixti, 6));
        var wb = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        // DrawCaptainsWindow's special frame, 12, then one on each press and release of the special.
        Assert.Equal(12, Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status.CaptainSpecial);
        RunDisarmed(world, wb, 1, id => id == "host" ? ShipInput.Special : ShipInput.None);
        Assert.Equal(13, Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status.CaptainSpecial);
        RunDisarmed(world, wb, 1);
        Assert.Equal(14, Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status.CaptainSpecial);
        // Every other ship keeps DrawCaptainsWindow's: none of its own.
        Assert.Equal(0, Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("g1")!.Status.CaptainSpecial);
        Assert.Equal(0, Sent(world.Snapshot(), "g1").Battles!.Single().Enemy.CaptainSpecial);
    }

    [Fact]
    public void TheVuxsLimpetsOnTheEnemysCaptainsWindowGoOnTheWire()
    {
        var world = WithFleet(2, (Vux, 20));
        var wb = world.StartBattle("host", Ilwrath);
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        // The host's VUX drops its limpets (its special: they home) for the Ilwrath until one sticks.
        for (int i = 0; i < 600 && wb.Enemy.Silhouette.Count == 0; ++i)
            RunDisarmed(world, wb, 1, id => id == "host" ? ShipInput.Special : ShipInput.None);
        var mark = Assert.Single(wb.Enemy.Silhouette);
        Assert.False(mark.Erase);
        // A guest flying in sees the enemy's captain's window with the limpet on it, the sheet's name sent once.
        FlyIn(world, "g1");
        var enemy = Sent(world.Snapshot(), "g1").Battles!.Single().Enemy;
        Assert.Equal(new[] { new SilhouetteMarkSnapshot(mark.Frame.Sheet!.Id, mark.Frame.Index, mark.X, mark.Y, false) }, enemy.Marks);
        Assert.Equal(Ilwrath, enemy.Ship);
        // The host's own ship has none.
        Assert.Empty(Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status.Marks!);
        // A ship gone keeps its panel as it last was, the marks on it too.
        // An erasing mark (MODIFY_SWAP: a marine's death, the window drawn afresh there) is sent as one, in its order.
        var gone = new Starship(wb.Enemy.Code, 1, 3, 22);
        gone.Silhouette.Add(mark);
        gone.Silhouette.Add(mark with { Erase = true, X = (short)(mark.X + 1) });
        Assert.Equal(new[] { enemy.Marks![0], enemy.Marks[0] with { Erase = true, X = (short)(mark.X + 1) } },
            GameWorld.StatusSnapshot(Ilwrath, gone).Marks);
        Assert.True(GameWorld.StatusSnapshot(Ilwrath, gone).Gone);
    }
}
