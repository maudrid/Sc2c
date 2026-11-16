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
    public void AGoneShofixtisPanelKeepsItsGloryDevicesFrame()
    {
        var world = WithFleet(2, (Shofixti, 6), (Spathi, 20));
        var wb = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        // The special pressed and let go twice: frame 16 (12, then one a press and one a release).
        for (int i = 0; i < 2; ++i)
        {
            RunDisarmed(world, wb, 1, id => id == "host" ? ShipInput.Special : ShipInput.None);
            RunDisarmed(world, wb, 1);
        }
        Assert.Equal(16, Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status.CaptainSpecial);
        // It dies: the host chooses again, their status the Shofixti's, gone, its window as it last was.
        Kill(wb.Battle, Fighter(wb, "host").FleetShips[0].Starship);
        UntilPicking(world, wb);
        var status = Sent(world.Snapshot(), "g1").Battles!.Single().FighterOf("host")!.Status;
        Assert.Equal(("shofixti", true, 16), (status.Ship, status.Gone, status.CaptainSpecial));
    }

    // A sheet of our own with these frames' masks (each a width, a height, its hot spot and its solid pixels).
    private static Sheet Masks(string id, params (int W, int H, int HotX, int HotY, (int X, int Y)[] Solid)[] frames) => new(id, frames.Length,
        frames.Select(f =>
        {
            var mask = new byte[f.W * f.H];
            foreach (var (x, y) in f.Solid)
                mask[y * f.W + x] = 1;
            return new Uqm.IntersectFrame(f.W, f.H, f.HotX, f.HotY, mask);
        }).ToList());

    private static (int, int)[] Solid(int w, int h) => Enumerable.Range(0, w * h).Select(i => (i % w, i / w)).ToArray();

    [Fact]
    public void AMarkCoveredByALaterOneIsntShownAndTheShownDrawTheSamePicture()
    {
        // Frame 0 a 3x3 square, 1 its centre, 2 its centre and bottom right corner, 3 a 2x2 square (top left, by the hot
        // spot), each with its hot spot at (1, 1); and, the hot spots mixed as the VUX's slime's are, 4 a 5x4 square at
        // (2, 1), 5 a 5x5 square at (2, 2), 6 a pixel at (0, 0).
        var sheet = Masks("m", (3, 3, 1, 1, Solid(3, 3)), (3, 3, 1, 1, new[] { (1, 1) }), (3, 3, 1, 1, new[] { (2, 2), (1, 1) }),
            (2, 2, 1, 1, Solid(2, 2)), (5, 4, 2, 1, Solid(5, 4)), (5, 5, 2, 2, Solid(5, 5)), (1, 1, 0, 0, Solid(1, 1)));
        SilhouetteMark At(int frame, int x, int y, bool erase = false) => new(new Frame(sheet, (ushort)frame), (short)x, (short)y, erase);
        // A stamp at the same place covering its pixels: the same frame, or one whose solid pixels hold the other's.
        Assert.True(SilhouetteMarks.Covers(At(1, 5, 5), At(1, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(0, 5, 5), At(1, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(2, 5, 5), At(1, 5, 5)));
        Assert.False(SilhouetteMarks.Covers(At(1, 5, 5), At(2, 5, 5)));   // the corner shows
        Assert.False(SilhouetteMarks.Covers(At(0, 5, 6), At(1, 5, 5)));   // elsewhere
        Assert.True(SilhouetteMarks.Covers(At(3, 5, 5), At(1, 5, 5)));    // the 2x2's (1, 1), hot spots aligned, over the centre
        Assert.False(SilhouetteMarks.Covers(At(3, 5, 5), At(2, 5, 5)));   // not over the corner, which is outside it
        // Hot spots differing: the frames placed by them. The 5x5 square covers the 5x4 (one row short at the top) at
        // their place, not the other way; the 3x3's centre and the pixel each cover the other at one place, not the
        // whole 3x3.
        Assert.True(SilhouetteMarks.Covers(At(5, 5, 5), At(4, 5, 5)));
        Assert.False(SilhouetteMarks.Covers(At(4, 5, 5), At(5, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(1, 5, 5), At(6, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(6, 5, 5), At(1, 5, 5)));
        Assert.False(SilhouetteMarks.Covers(At(6, 5, 5), At(0, 5, 5)));
        // An erase covers what's within its rectangle, its corner the place less the hot spot: the 5x5's at (5, 5)
        // is (3, 3) to (7, 7), which holds the 5x4 at (5, 5) ((3, 4) to (7, 7)) and the pixel at (3, 3), but not the
        // 5x4 at (5, 6) ((3, 5) to (7, 8)) nor the pixel at (8, 8).
        Assert.True(SilhouetteMarks.Covers(At(5, 5, 5, true), At(4, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(5, 5, 5, true), At(6, 3, 3)));
        Assert.False(SilhouetteMarks.Covers(At(5, 5, 5, true), At(4, 5, 6)));
        Assert.False(SilhouetteMarks.Covers(At(5, 5, 5, true), At(6, 8, 8)));
        Assert.True(SilhouetteMarks.Covers(At(5, 5, 5, true), At(4, 5, 5, true)));
        // An erase covers what's within its rectangle, an erase too; a stamp never covers an erase.
        Assert.True(SilhouetteMarks.Covers(At(0, 5, 5, true), At(1, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(0, 5, 5, true), At(3, 5, 5)));
        Assert.True(SilhouetteMarks.Covers(At(0, 5, 5, true), At(3, 6, 6)));
        Assert.False(SilhouetteMarks.Covers(At(0, 5, 5, true), At(3, 7, 7)));
        Assert.True(SilhouetteMarks.Covers(At(0, 5, 5, true), At(3, 6, 6, true)));
        Assert.False(SilhouetteMarks.Covers(At(0, 5, 5), At(1, 5, 5, true)));
        // A frame of no known mask or size is never covered nor covers by its pixels.
        var bare = new Frame(new Sheet("bare", 2), 0);
        Assert.True(SilhouetteMarks.Covers(new SilhouetteMark(bare, 5, 5, false), new SilhouetteMark(bare, 5, 5, false)));   // the same frame
        Assert.False(SilhouetteMarks.Covers(At(0, 5, 5), new SilhouetteMark(bare.SetAbs(1), 5, 5, false)));
        Assert.False(SilhouetteMarks.Covers(At(0, 5, 5, true), new SilhouetteMark(bare, 5, 5, false)));

        // Random marks, as the Orz's marines and the VUX's limpets might make them: the shown draw every mark's picture,
        // fewer of them; cleared, none.
        var r = new Random(14);
        for (int n = 0; n < 500; ++n)
        {
            var marks = new SilhouetteMarks();
            for (int i = r.Next(40); i > 0; --i)
            {
                marks.Add(At(r.Next(7), 4 + r.Next(4), 4 + r.Next(4), r.Next(5) == 0));
                var full = RaceShipBattleTests.Picture(marks);
                var shown = RaceShipBattleTests.Picture(marks.Shown);
                Assert.True(full.Count == shown.Count && full.All(p => shown.TryGetValue(p.Key, out var q) && q == p.Value));
                // Some of the marks made, in their order: the last of them always.
                int j = 0;
                foreach (var m in marks)
                    if (j < marks.Shown.Count && marks.Shown[j] == m)
                        ++j;
                Assert.Equal(marks.Shown.Count, j);
                Assert.Equal(marks[^1], marks.Shown[^1]);
            }
            marks.Clear();
            Assert.Empty(marks);
            Assert.Empty(marks.Shown);
        }
        // The Orz's: the marine, then fights (the fight frame, the rest frame over it) at its place, then its death's erase.
        var orz = new SilhouetteMarks();
        orz.Add(At(1, 5, 5));
        for (int i = 0; i < 20; ++i)
        {
            orz.Add(At(2, 5, 5));
            orz.Add(At(0, 5, 5));
        }
        Assert.Equal(new[] { At(0, 5, 5) }, orz.Shown);
        orz.Add(At(0, 5, 5, true));
        Assert.Equal(new[] { At(0, 5, 5, true) }, orz.Shown);
        Assert.Equal(42, orz.Count);   // every mark made, as UQM made them
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
        Assert.Equal(new[] { enemy.Marks![0] with { Ordinal = 0 }, enemy.Marks[0] with { Erase = true, X = (short)(mark.X + 1), Ordinal = 1 } },
            GameWorld.StatusSnapshot(Ilwrath, gone).Marks);
        Assert.True(GameWorld.StatusSnapshot(Ilwrath, gone).Gone);
    }

    [Fact]
    public void FiftyLimpetsOnOneShipFitASnapshotsBudget()
    {
        // Item 9's note: the VUX's limpets are seldom covered, so a ship's marks grow with them, each sent in every
        // snapshot of its battle. The host's VUX drops limpets on the Ilwrath until 50 show on its captain's window.
        var world = WithFleet(2, (Vux, 20));
        var wb = world.StartBattle("host", Ilwrath);
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        FlyIn(world, "g1");
        for (int i = 0; i < 20000 && wb.Enemy.Silhouette.Shown.Count < 50; ++i)
            RunDisarmed(world, wb, 1, id => id == "host" ? ShipInput.Special : ShipInput.None);
        Assert.Null(wb.Outcome);
        Assert.True(wb.Enemy.Silhouette.Shown.Count >= 50, $"{wb.Enemy.Silhouette.Shown.Count} limpets showing");
        // What they add to the snapshot a player in the battle is sent (whole, as each battle frame is: BattleCodec).
        var snapshot = world.Snapshot();
        var battle = snapshot.Battles!.Single();
        // Every limpet showing is in it.
        Assert.Equal(wb.Enemy.Silhouette.Shown.Count, battle.Enemy.Marks!.Count);
        var bare = snapshot with { Battles = new[] { battle with { Enemy = battle.Enemy with { Marks = null } } } };
        var w = new WireWriter();
        SnapshotCodec.Encode(w, bare, null, "g1");
        int without = w.Length;
        SnapshotCodec.Encode(w, snapshot, null, "g1");
        int marks = w.Length - without;
        Assert.True(w.Length < 1500, $"the snapshot with {battle.Enemy.Marks.Count} limpets is {w.Length} bytes");
        // About 7 bytes a limpet: with the network test's busiest battle snapshot (633 bytes over the lossy relay),
        // well under its 1,500-byte budget.
        Assert.True(marks < 400, $"{wb.Enemy.Silhouette.Shown.Count} limpets cost {marks} bytes");
        Assert.Equal(battle.Enemy.Marks, Sent(snapshot, "g1").Battles!.Single().Enemy.Marks);
    }

    // A ship's code for a status alone (nothing loads it).
    private sealed class NoCode : IShipCode
    {
        public RaceDesc Load(Battle battle, Starship starship) => throw new InvalidOperationException();
    }

    [Fact]
    public void AMarkAtAPlaceUsedBeforeIsDrawnOnceThePrunedListLooksAsItDidBefore()
    {
        // An Orz marine boarding takes the lowest free place (count_marines), and its turret's fight frames share one
        // mask, so the marks still showing go [E] -> [E, S22] -> [E, S31] -> [E, S30] -> [E]: the last erase equals the
        // first by value. Told apart by their ordinals, the client draws it, and the last fight frame doesn't stay.
        var frames = Enumerable.Range(0, 32).Select(_ => (5, 5, 2, 2, Solid(5, 5))).ToArray();
        var sheet = Masks("turret", frames);
        var ship = new Starship(new NoCode(), 1, 10, 20);
        var canvas = new Crateris.Presentation.UqmCanvas(_ => 32, (_, _) => new Crateris.Presentation.URect(-2, -2, 5, 5),
            new Dictionary<string, int> { ["starcon"] = 11, ["tiny"] = 10 });
        var status = new Crateris.Presentation.BattleStatus(canvas);
        var look = new Crateris.Presentation.StatusShip("icons", "captain", 20, 10, false, false, false, "Orz", "captain");
        Crateris.Presentation.StatusState State() =>
            new(10, 5, StatusFlags.None, 10, 0, Marks: GameWorld.StatusSnapshot("orz", ship).Marks);
        void Mark(int frame, bool erase) => ship.Silhouette.Add(new SilhouetteMark(new Frame(sheet, (ushort)frame), 30, 33, erase));

        Mark(22, erase: true);
        status.Init(Crateris.Presentation.BattleStatus.EnemySlot, look, State());
        foreach (var (frame, erase) in new[] { (22, false), (31, false), (30, false), (22, true) })
        {
            Mark(frame, erase);
            canvas.Drawn.Clear();
            status.Update(Crateris.Presentation.BattleStatus.EnemySlot, State());
            if (erase)
                Assert.Contains(canvas.Drawn, d => d.Clip.W == 5 && d.Kind == Crateris.Presentation.UqmDrawKind.Rect);   // the window afresh there
            else
                Assert.Equal((frame, 1), (canvas.Drawn.Single(d => d.Sheet == "turret").Frame, canvas.Drawn.Count(d => d.Sheet == "turret")));
        }
        Assert.Single(ship.Silhouette.Shown);
        Assert.Equal(new[] { 4 }, ship.Silhouette.ShownOrdinals);
        // Sent with their ordinals, ascending; a list out of order is refused.
        var sent = GameWorld.StatusSnapshot("orz", ship).Marks!;
        Assert.Equal(4, sent.Single().Ordinal);
        var w = new WireWriter();
        var battle = new BattleSnapshot(1, 8192, 7680, new BattleShipStatus("orz", 10, 20, 5, 10, 5,
                Marks: new[] { sent[0] with { Ordinal = 7 }, sent[0] with { Ordinal = 9 } }), 0, 0,
            new[] { new BattleFighterSnapshot("p", new BattleShipStatus(ShipDescriptions.Flagship, 10, 20, 5, 10), 0, 0, true) },
            Array.Empty<BattleElementSnapshot>());
        SnapshotCodec.Encode(w, new WorldSnapshot(1, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { battle }), null, "p");
        Assert.Equal(new[] { 7, 9 }, SnapshotCodec.Decode(w.Written).Battles!.Single().Enemy.Marks!.Select(m => m.Ordinal));
        w = new WireWriter();
        SnapshotCodec.Encode(w, new WorldSnapshot(1, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { battle with
            { Enemy = battle.Enemy with { Marks = new[] { sent[0] with { Ordinal = 9 }, sent[0] with { Ordinal = 9 } } } } }), null, "p");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(w.Written));
    }

    [Fact]
    public void AnotherShipInTheSlotDrawsItsMarksFromItsFirstAndARebirthsGoOnFromItsLast()
    {
        var sheet = Masks("marks", Enumerable.Range(0, 4).Select(_ => (5, 5, 2, 2, Solid(5, 5))).ToArray());
        var canvas = new Crateris.Presentation.UqmCanvas(_ => 4, (_, _) => new Crateris.Presentation.URect(-2, -2, 5, 5),
            new Dictionary<string, int> { ["starcon"] = 11, ["tiny"] = 10 });
        var status = new Crateris.Presentation.BattleStatus(canvas);
        var look = new Crateris.Presentation.StatusShip("icons", "captain", 20, 10, false, false, false, "Ship", "captain");
        Crateris.Presentation.StatusState State(Starship ship) =>
            new(10, 5, StatusFlags.None, 10, 0, Marks: GameWorld.StatusSnapshot("vux", ship).Marks);
        int Stamps() => canvas.Drawn.Count(d => d.Sheet == "marks");
        // The first ship: three marks drawn.
        var first = new Starship(new NoCode(), 1, 10, 20);
        status.Init(Crateris.Presentation.BattleStatus.EnemySlot, look, State(first));
        for (int i = 0; i < 3; ++i)
            first.Silhouette.Add(new SilhouetteMark(new Frame(sheet, (ushort)i), (short)(20 + 6 * i), 30, false));
        status.Update(Crateris.Presentation.BattleStatus.EnemySlot, State(first));
        Assert.Equal(3, Stamps());
        // Another ship in the slot (the next enemy, the host's next pick), its marks counted from 0: drawn.
        var second = new Starship(new NoCode(), 1, 10, 20);
        second.Silhouette.Add(new SilhouetteMark(new Frame(sheet, 3), 40, 30, false));
        canvas.Drawn.Clear();
        status.Init(Crateris.Presentation.BattleStatus.EnemySlot, look, State(second));
        Assert.Equal(1, Stamps());
        second.Silhouette.Add(new SilhouetteMark(new Frame(sheet, 1), 44, 30, false));
        canvas.Drawn.Clear();
        status.Update(Crateris.Presentation.BattleStatus.EnemySlot, State(second));
        Assert.Equal(1, Stamps());
        // A ship reborn (its window drawn afresh, its marks cleared) whose death and rebirth the client missed, so its
        // slot isn't drawn afresh: its ordinals go on from its last, and its new mark is drawn.
        second.Silhouette.Clear();
        second.Silhouette.Add(new SilhouetteMark(new Frame(sheet, 2), 30, 30, false));
        Assert.Equal(new[] { 2 }, second.Silhouette.ShownOrdinals);
        canvas.Drawn.Clear();
        status.Update(Crateris.Presentation.BattleStatus.EnemySlot, State(second));
        Assert.Equal(1, Stamps());
    }
}
