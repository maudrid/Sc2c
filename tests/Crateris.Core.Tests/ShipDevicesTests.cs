// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Devices held by ships (the stage 3c plan's item 5, part 1; the owner's
/// answer to its question 5): each ship's devices, the talks' reads and
/// writes of them, the reads outside talks, the hand-over, the leavers'
/// containers, the save and the wire, and the markers to other players,
/// wrecks and containers (question 12).
/// </summary>
[Trait("Topic", "story")]
public class ShipDevicesTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static SystemCatalog Systems => SystemGroupTests.Systems.Value;

    // Device numbers (globdata.h's).
    private const int Spawner = 0, Pet = 1, Bomb = 2, Sun = 3, Rosy = 4, Ultron0 = 7, Ultron2 = 9, Maidens = 11,
        Taalo = 17, Artifact2 = 25, LunarBase = 27;

    private static GameWorld NewGame(int guests = 1)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems,
            TestContent.Shapes);
        for (int i = 1; i <= guests; ++i)
        {
            string id = guests == 1 ? "guest" : $"g{i}";
            Assert.Equal(JoinResult.Joined, world.Join(id, guests == 1 ? "Guest" : $"Guest {i}"));
            world.UndockShown(id);
        }
        return world;
    }

    private static int Star(GameWorld world, string special) =>
        world.Systems!.Map.Stars.Single(s => s.Special == special).Number;

    // ---------------------------------------------------------------- the talks

    // A recorded start of a script's, its state replaced (UQM's one ship's fields, the TestShip's devices and
    // the story's record from them), the talk started.
    private static Talk Started(ContentPackage package, string script, params (string Field, int Value)[] state)
    {
        var start = JsonNode.Parse(TalkTests.Recorded(script)[0].GetRawText())!.AsObject();
        var fields = new JsonObject();
        foreach (var (f, v) in state)
            fields[f] = v;
        start["state"] = fields;
        return TalkTests.Start(package, script, JsonDocument.Parse(start.ToJsonString()).RootElement);
    }

    [ContentFact]
    public void ATalkReadsTheAnsweringShipsDevicesAndTheStorysRecordWhereItsTheStorysCheck()
    {
        TestContent.With(package =>
        {
            // The Supox: the Ultron found but aboard another ship is read as UQM's once given back (5), so they
            // neither give a second nor ask after its repairs; aboard the answerer's, its repair level.
            var supox = Started(package, "supox", ("ULTRON_CONDITION", 1));
            var ship = (TalkTests.TestShip)supox.Ship;
            Assert.Equal(1, supox.Get("ULTRON_CONDITION"));
            ship.HeldDevices = new SortedSet<int> { Ultron2 };
            Assert.Equal(3, supox.Get("ULTRON_CONDITION"));
            ship.HeldDevices = new SortedSet<int>();
            Assert.Equal(Talk.UltronElsewhere, supox.Get("ULTRON_CONDITION"));
            // The Utwig read the answerer's alone (utwigc.c:800-806): no Ultron, none, whoever else has it.
            var utwig = Started(package, "utwig", ("ULTRON_CONDITION", 1));
            ((TalkTests.TestShip)utwig.Ship).HeldDevices = new SortedSet<int>();
            Assert.Equal(0, utwig.Get("ULTRON_CONDITION"));
            // Given back (5, no device), every talk reads it so.
            var back = Started(package, "utwig", ("ULTRON_CONDITION", 5));
            Assert.Empty(((TalkTests.TestShip)back.Ship).HeldDevices);
            Assert.Equal(5, back.Get("ULTRON_CONDITION"));
            // The Druuge's Trident: sold once (their only check), whoever holds it; the starbase asks the answerer's.
            var druuge = Started(package, "druuge", ("ARTIFACT_2_ON_SHIP", 1));
            ((TalkTests.TestShip)druuge.Ship).HeldDevices = new SortedSet<int>();
            Assert.Equal(1, druuge.Get("ARTIFACT_2_ON_SHIP"));
            var starbase = Started(package, "starbas", ("ARTIFACT_2_ON_SHIP", 1), ("STARBASE_AVAILABLE", 1));
            ((TalkTests.TestShip)starbase.Ship).HeldDevices = new SortedSet<int>();
            Assert.Equal(0, starbase.Get("ARTIFACT_2_ON_SHIP"));
            // Every other device: the answerer's alone, with no shared field to read.
            Assert.Equal(0, starbase.Get("MAIDENS_ON_SHIP"));
            ((TalkTests.TestShip)starbase.Ship).HeldDevices = new SortedSet<int> { Maidens };
            Assert.Equal(1, starbase.Get("MAIDENS_ON_SHIP"));
            // The Chmmr see no bomb aboard a guest's ship (question 5: only the host's can be the bomb ship).
            var chmmr = Started(package, "chmmr", ("UTWIG_BOMB_ON_SHIP", 1), ("TALKING_PET_ON_SHIP", 1));
            var chmmrShip = (TalkTests.TestShip)chmmr.Ship;
            Assert.Equal((1, 1), (chmmr.Get("UTWIG_BOMB_ON_SHIP"), chmmr.Get("TALKING_PET_ON_SHIP")));
            chmmrShip.IsHost = false;
            Assert.Equal((0, 1), (chmmr.Get("UTWIG_BOMB_ON_SHIP"), chmmr.Get("TALKING_PET_ON_SHIP")));
        });
    }

    [ContentFact]
    public void ATalksWritesGoToTheAnsweringShipAndARecordedOnesToTheStoryToo()
    {
        TestContent.With(package =>
        {
            var talk = Started(package, "supox");
            var ship = (TalkTests.TestShip)talk.Ship;
            talk.Set("ULTRON_CONDITION", 1);
            Assert.Equal(new[] { Ultron0 }, ship.HeldDevices);
            Assert.Equal(1, talk.Story.Get("ULTRON_CONDITION"));
            talk.Set("ULTRON_CONDITION", 5);   // given back: no device, the record 5
            Assert.Empty(ship.HeldDevices);
            Assert.Equal(5, talk.Story.Get("ULTRON_CONDITION"));
            talk.Set("MAIDENS_ON_SHIP", 1);
            Assert.Equal(new[] { Maidens }, ship.HeldDevices);
            Assert.Throws<InvalidOperationException>(() => talk.Story.Get("MAIDENS_ON_SHIP"));   // no shared field
            Assert.Throws<InvalidOperationException>(() => talk.Set("MAIDENS_ON_SHIP", 2));      // past its bit
            // The spawner and the Lunar base stay the story's.
            talk.Set("PORTAL_SPAWNER_ON_SHIP", 1);
            Assert.Equal(1, talk.Story.Get("PORTAL_SPAWNER_ON_SHIP"));
            Assert.Equal(new[] { Maidens }, ship.HeldDevices);
            // Each write is recorded as UQM's (the recordings stand).
            Assert.Contains(talk.Events, e => e is StateSet { Field: "MAIDENS_ON_SHIP", Value: 1 });
        });
    }

    [ContentFact]
    public void TheUltronIsOneShipsAndItsRecordTheStorys()
    {
        // The set-up's (and a talk's) Ultron: one ship's device, by its repair level, the story's record with it
        // (the Utwig's and the Pkunk's meetings with it are UtwigSupoxTests' and OrzPkunkTests').
        var world = NewGame();
        world.SetDevice("guest", "ULTRON_CONDITION", 4);
        Assert.Equal((4, 0), (world.Device("guest", "ULTRON_CONDITION"), world.Device("host", "ULTRON_CONDITION")));
        Assert.Equal(new[] { 10 }, world.Find("guest")!.Ship.Hold.Devices);
        Assert.Empty(world.Host.Ship.Hold.Devices);
        Assert.Equal(4, world.Story!.Get("ULTRON_CONDITION"));   // the record
    }

    // ---------------------------------------------------------------- reads outside talks

    [ContentFact]
    public void ProcyonsGuardAndTanakasBrotherReadAnyPlayersShip()
    {
        // genchmmr.c:108 and genshof.c:58, 119: any player's ship holding the Sun Device or the Maidens.
        var world = NewGame();
        var procyon = GameWorld.TalkWorlds.Single(w => w.Special == "CHMMR_DEFINED" && w.Race == "ILWRATH_SHIP");
        Assert.False(world.Story!.HeldByAnyShip("SUN_DEVICE_ON_SHIP"));
        Assert.False(procyon.Guarded(world.Story, () => true));
        world.SetDevice("guest", "SUN_DEVICE_ON_SHIP");
        Assert.True(world.Story.HeldByAnyShip("SUN_DEVICE_ON_SHIP"));
        Assert.True(procyon.Guarded(world.Story, () => true));
        Assert.True(world.AnyShipHolds("SUN_DEVICE_ON_SHIP"));
        Assert.False(world.AnyShipHolds("MAIDENS_ON_SHIP"));
        Assert.Throws<ArgumentException>(() => world.Story.HeldByAnyShip("MOONBASE_ON_SHIP"));   // shared: the story's own
        // A leaver's container isn't a ship.
        world.Leave("guest");
        Assert.False(world.AnyShipHolds("SUN_DEVICE_ON_SHIP"));
    }

    // ---------------------------------------------------------------- the hand-over

    [ContentFact]
    public void ADeviceIsHandedOverCloseByAsFuelIs()
    {
        var world = NewGame();
        world.SetDevice("host", "TALKING_PET_ON_SHIP");
        world.SetDevice("host", "ROSY_SPHERE_ON_SHIP");
        world.Host.Ship.Hold.ResourceUnits = 100;
        Gift Device(int device, string from = "host", string to = "guest", uint units = 0) =>
            new(from, to, units, new int[ShipHold.Categories], 0, Device: device);
        // Not held, or not one a ship holds (the spawner and the Lunar base are everyone's), or no such device.
        Assert.Equal(TradeRefusal.NotEnough, world.Give(Device(Taalo)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(Device(Spawner)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(Device(LunarBase)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(Device(14)));   // a data plate: never listed
        Assert.Equal(TradeRefusal.Nothing, world.Give(Device(60)));
        // Close by (both in hyperspace, within a screen: the guest undocked from the host): it goes, with RU too.
        Assert.Equal(TradeRefusal.None, world.Give(Device(Pet, units: 10)));
        Assert.Equal(new[] { Rosy }, world.Host.Ship.Hold.Devices);
        Assert.Equal(new[] { Pet }, world.Find("guest")!.Ship.Hold.Devices);
        Assert.Contains(Device(Pet, units: 10), world.TakeGifts());
        // The bomb can be carried and handed over by anyone (only used aboard the host's: question 5).
        world.SetDevice("guest", "UTWIG_BOMB_ON_SHIP");
        Assert.Equal(TradeRefusal.None, world.Give(Device(Bomb, "guest", "host")));
        Assert.Equal(1, world.Device("host", "UTWIG_BOMB_ON_SHIP"));
        // One of a kind the receiver holds: refused (the Ultron in another state).
        world.SetDevice("host", "ULTRON_CONDITION", 1);
        PlayerTalkShip.SetDeviceOf(world, world.Find("guest")!, "ULTRON_CONDITION", 3);
        Assert.Equal(TradeRefusal.HasOne, world.Give(Device(Ultron0)));
        // Too far: a screen away in hyperspace, or another system.
        world.Find("guest")!.Ship.Flight.LogX += 100000;
        Assert.Equal(TradeRefusal.DeviceTooFar, world.Give(Device(Rosy)));
        Assert.Equal(TradeRefusal.None, world.Give(Device(0, units: 5) with { Device = Gift.NoDevice }));   // RU go anywhere
        Assert.Equal(new[] { Rosy, Ultron0, Bomb }.OrderBy(d => d), world.Host.Ship.Hold.Devices);
    }

    [Fact]
    public void AGiftCarriesItsDeviceOnTheWire()
    {
        var gift = new Gift("a", "b", 3, new int[ShipHold.Categories], 0, Device: Pet);
        Assert.Equal(gift, Gift.Decode(gift.Encode()));
        var none = gift with { Device = Gift.NoDevice };
        Assert.Equal(none, Gift.Decode(none.Encode()));
        Assert.Throws<InvalidDataException>(() => Gift.Decode((gift with { Device = 9999 }).Encode()));
        Assert.Equal("the Talking Pet, 3 RU", gift.Describe(d => d == Pet ? "Talking Pet" : null));
        Assert.Equal("a device", (gift with { ResourceUnits = 0 }).Describe());
    }

    [ContentFact]
    public void TheGivingScreensDeviceRowChoosesOneTheShipHoldsAndGivesIt()
    {
        var world = NewGame();
        world.SetDevice("host", "TALKING_PET_ON_SHIP");
        world.SetDevice("host", "MAIDENS_ON_SHIP");
        var snapshot = world.Snapshot();
        var screen = new TradeScreen();
        var shown = screen.Follow(snapshot, "host");
        Assert.Equal((2L, 0, (int?)null), (shown.Have[TradeScreen.DeviceRow], shown.Give[TradeScreen.DeviceRow], shown.Device));
        Assert.True(shown.Near);
        Assert.Null(screen.Press(MenuKey.Up, snapshot, "host"));   // the receiver's row
        Assert.Null(screen.Press(MenuKey.Up, snapshot, "host"));   // round to the devices'
        Assert.Equal(TradeScreen.DeviceRow, screen.Row);
        Assert.Null(screen.Press(MenuKey.Right, snapshot, "host"));
        Assert.Null(screen.Press(MenuKey.Right, snapshot, "host"));
        Assert.Equal(Maidens, screen.Follow(snapshot, "host").Device);
        Assert.Null(screen.Press(MenuKey.Right, snapshot, "host"));   // no more than it holds
        var gift = screen.Press(MenuKey.Select, snapshot, "host")!;
        Assert.Equal(("host", "guest", Maidens, 0u), (gift.From, gift.To, gift.Device, gift.ResourceUnits));
        Assert.Equal(TradeRefusal.None, world.Give(gift));
        Assert.Equal(new[] { Maidens }, world.Find("guest")!.Ship.Hold.Devices);
        Assert.Equal(0, screen.Follow(world.Snapshot(), "host").Give[TradeScreen.DeviceRow]);
    }

    // ---------------------------------------------------------------- containers

    // A ship flown out of a place's reach in hyperspace and back, a frame each.
    private static void FlyOutAndBack(GameWorld world, string id)
    {
        var flight = world.Find(id)!.Ship.Flight;
        flight.LogX += 2000;
        Run(world, 1);
        flight.LogX -= 2000;
        Run(world, 1);
    }

    [ContentFact]
    public void ALeaversDevicesAreLeftInAContainerWhereTheirShipWasForAnotherToTake()
    {
        var world = NewGame();
        var guest = world.Find("guest")!.Ship;
        world.SetDevice("guest", "TALKING_PET_ON_SHIP");
        world.SetDevice("guest", "ROSY_SPHERE_ON_SHIP");
        guest.Flight.LogX += 400;   // a little way from the host, within its screen
        Run(world, 1);
        world.TakeNotices();
        int x = guest.Flight.LogX, y = guest.Flight.LogY;
        world.Leave("guest");
        var c = Assert.Single(world.Containers);
        Assert.Equal(("guest", "Guest", new BattlePlace(null, null, x, y)), (c.Owner, c.OwnerName, c.Place));
        Assert.Equal(new[] { Pet, Rosy }, c.Devices);
        Assert.Empty(guest.Hold.Devices);
        Assert.Equal("Guest left the game: their 2 devices are in a container where their ship was.",
            Assert.Single(world.TakeNotices()).Text);
        // Sent to everyone.
        Assert.Equal(new ContainerSnapshot(c.Id, "Guest", null, null, x, y, 2), Assert.Single(world.Snapshot().Containers!));
        // The host, flying into it from outside its reach, takes what's in it.
        world.DeviceNames = d => d == Pet ? "Talking Pet" : d == Rosy ? "Rosy Sphere" : null;
        world.Host.Ship.Flight.LogX = x - 2000;
        world.Host.Ship.Flight.LogY = y;
        Run(world, 1);
        Assert.Single(world.Containers);
        world.Host.Ship.Flight.LogX = x;
        Run(world, 1);
        Assert.Empty(world.Containers);
        Assert.Equal(new[] { Pet, Rosy }, world.Host.Ship.Hold.Devices);
        Assert.Equal("You took Guest's container: the Talking Pet, the Rosy Sphere.", Assert.Single(world.TakeNotices()).Text);
        Assert.Empty(world.Snapshot().Containers!);
    }

    [ContentFact]
    public void ARejoiningLeaverGetsTheirContainerBackIfItsStillThere()
    {
        var world = NewGame(guests: 2);
        world.SetDevice("g1", "MAIDENS_ON_SHIP");
        world.SetDevice("g2", "SUN_DEVICE_ON_SHIP");
        world.Leave("g1");
        world.Leave("g2");
        Assert.Equal(2, world.Containers.Count);
        // Both lie where the two undocked, by the host: first seen inside their reach, flown out and back, it takes both.
        Run(world, 1);
        Assert.Equal(2, world.Containers.Count);
        FlyOutAndBack(world, "host");
        Assert.Empty(world.Containers);
        Assert.Equal(new[] { Sun, Maidens }, world.Host.Ship.Hold.Devices);
        // Its leaver back while it's there: theirs again.
        var again = NewGame(guests: 2);
        again.SetDevice("g1", "MAIDENS_ON_SHIP");
        again.Leave("g1");
        Assert.Single(again.Containers);
        Assert.Equal(JoinResult.Rejoined, again.Join("g1", "Guest 1"));
        Assert.Empty(again.Containers);
        Assert.Equal(new[] { Maidens }, again.Find("g1")!.Ship.Hold.Devices);
        // Gone meanwhile: nothing comes back.
        again.Leave("g1");
        Run(again, 1);
        FlyOutAndBack(again, "g2");
        Assert.Equal(new[] { Maidens }, again.Find("g2")!.Ship.Hold.Devices);
        Assert.Equal(JoinResult.Rejoined, again.Join("g1", "Guest 1"));
        Assert.Empty(again.Find("g1")!.Ship.Hold.Devices);
    }

    [ContentFact]
    public void TheContainersPlaceIsWhereTheShipWaitsInOrbitAWreckAndTheHostLeavesNone()
    {
        // In orbit: beside the world, in its planet's inner view where the ship is.
        var world = NewGame(guests: 2);
        int sol = Star(world, "SOL_DEFINED");
        world.PutInOrbit("g1", sol, 2, 1);
        world.SetDevice("g1", "VUX_BEAST_ON_SHIP");
        var ip = world.Find("g1")!.Ship.Ip!;
        world.Leave("g1");
        var c = Assert.Single(world.Containers);
        Assert.Equal(new BattlePlace(sol, 2, ip.Location.X, ip.Location.Y), c.Place);
        // A wreck keeps its devices while its player is here; leaving, they're left where it lies.
        var g2 = world.Find("g2")!.Ship;
        world.SetDevice("g2", "AQUA_HELIX_ON_SHIP");
        g2.Destroyed = true;
        g2.Hold.SetCrew(0);
        Assert.True(g2.Wreck);
        Assert.Equal(new[] { 5 }, g2.Hold.Devices);
        world.Leave("g2");
        Assert.Equal(new BattlePlace(null, null, g2.Flight.LogX, g2.Flight.LogY), world.Containers[1].Place);
        Assert.Contains(world.Snapshot().Ships, s => s.Id == "g2" && s.Wrecked);   // the wreck stays
        // The host leaving ends the game: its devices stay aboard.
        world.SetDevice("host", "TAALO_PROTECTOR_ON_SHIP");
        world.Leave("host");
        Assert.Equal(2, world.Containers.Count);
        Assert.Equal(new[] { Taalo }, world.Host.Ship.Hold.Devices);
    }

    [ContentFact]
    public void DevicesAndContainersAreSavedAndAGuestsAreLeftInAContainerAsTheSaveLoads()
    {
        var world = NewGame(guests: 2);
        world.SetDevice("host", "ULTRON_CONDITION", 3);
        world.SetDevice("g1", "MAIDENS_ON_SHIP");
        world.SetDevice("g2", "TAALO_PROTECTOR_ON_SHIP");
        world.Leave("g2");
        string json = HostSave.ToJson(world);
        var node = JsonNode.Parse(json)!;
        Assert.Equal(15, node["version"]!.GetValue<int>());
        Assert.Equal("[9]", node["players"]![0]!["hold"]!["devices"]!.ToJsonString());
        Assert.Equal(3, node["story"]!["fields"]!["ULTRON_CONDITION"]!.GetValue<int>());   // the record
        Assert.Null(node["story"]!["fields"]!["MAIDENS_ON_SHIP"]);
        Assert.Single(node["containers"]!.AsArray());
        var loaded = HostSave.FromJson(json, Systems, TestContent.Shapes);
        Assert.Equal(new[] { Ultron2 }, loaded.Host.Ship.Hold.Devices);
        Assert.Equal(3, loaded.Story!.Get("ULTRON_CONDITION"));
        // g1 is away as the save loads: their Maidens wait in a container where their ship is, g2's as it was.
        Assert.Equal(new[] { ("g1", Maidens), ("g2", Taalo) },
            loaded.Containers.Select(c => (c.Owner, c.Devices.Single())).OrderBy(c => c.Owner));
        Assert.Empty(loaded.Find("g1")!.Ship.Hold.Devices);
        Assert.Equal(JoinResult.Rejoined, loaded.Join("g1", "Guest 1"));
        Assert.Equal(new[] { Maidens }, loaded.Find("g1")!.Ship.Hold.Devices);

        // Refused: a device twice in the game, one a ship can't hold, two of one kind, a container of no player's.
        void Refused(Action<JsonNode> change, string says)
        {
            var bad = JsonNode.Parse(json)!;
            change(bad);
            Assert.Contains(says, Assert.Throws<InvalidDataException>(
                () => HostSave.FromJson(bad.ToJsonString(), Systems, TestContent.Shapes)).Message);
        }
        Refused(n => n["players"]![1]!["hold"]!["devices"] = new JsonArray(Ultron2), "held twice");
        Refused(n => n["players"]![1]!["hold"]!["devices"] = new JsonArray(Spawner), "isn't one a ship holds");
        Refused(n => n["players"]![1]!["hold"]!["devices"] = new JsonArray(Ultron0, 8), "two devices of ULTRON_CONDITION");
        Refused(n => n["containers"]![0]!["owner"] = "nobody", "isn't a player");
        Refused(n => n["containers"]![0]!["devices"] = new JsonArray(), "has no device");
        Refused(n => n["containers"]![0]!["star"] = 9999, "isn't in a place");
        // An older save can't have them.
        Refused(n => n["version"] = 14, "can't have devices held by ships");
    }

    [ContentFact]
    public void AnOlderSavesGroupDevicesGoToTheHostsShip()
    {
        // A save from before stage 3c item 5: its devices were story fields, the group's: the host's ship's now,
        // the recorded ones' fields kept as their record, the spawner and the Lunar base shared as before.
        var world = NewGame();
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 14;
        var fields = node["story"]!["fields"]!.AsObject();
        foreach (var (f, v) in new[] { ("MAIDENS_ON_SHIP", 1), ("ULTRON_CONDITION", 2), ("ARTIFACT_2_ON_SHIP", 1), ("PORTAL_SPAWNER_ON_SHIP", 1) })
            fields[f] = v;
        var loaded = HostSave.FromJson(node.ToJsonString(), Systems, TestContent.Shapes);
        Assert.Equal(new[] { 8, Maidens, Artifact2 }, loaded.Host.Ship.Hold.Devices);
        Assert.Equal((2, 1, 1), (loaded.Story!.Get("ULTRON_CONDITION"), loaded.Story.Get("ARTIFACT_2_ON_SHIP"),
            loaded.Story.Get("PORTAL_SPAWNER_ON_SHIP")));
        Assert.Empty(loaded.Find("guest")!.Ship.Hold.Devices);
        Assert.Empty(loaded.Containers);
        // The Ultron given back (5): no device.
        fields["ULTRON_CONDITION"] = 5;
        loaded = HostSave.FromJson(node.ToJsonString(), Systems, TestContent.Shapes);
        Assert.Equal(new[] { Maidens, Artifact2 }, loaded.Host.Ship.Hold.Devices);
    }

    // ---------------------------------------------------------------- the wire and the client

    [ContentFact]
    public void HoldsDevicesAndContainersGoOnTheWire()
    {
        var world = NewGame(guests: 2);
        world.SetDevice("g1", "TALKING_PET_ON_SHIP");
        world.SetDevice("g1", "EGG_CASE1_ON_SHIP");
        world.SetDevice("g2", "MAIDENS_ON_SHIP");
        world.Leave("g2");
        Run(world, 1);
        var first = world.Snapshot();
        var w = new WireWriter();
        SnapshotCodec.Encode(w, first);
        var full = SnapshotCodec.Decode(w.Written, _ => null, out _)!;
        Assert.Equal(new[] { Pet, 19 }, full.Ships.Single(s => s.Id == "g1").Hold!.HeldDevices);
        Assert.Equal(first.Containers, full.Containers);
        // A delta: the hand-over changes the holds, the container taken goes.
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("g1", "host", 0, new int[ShipHold.Categories], 0, Device: Pet)));
        Run(world, 1);
        FlyOutAndBack(world, "host");
        var next = world.Snapshot();
        Assert.Empty(next.Containers!);
        SnapshotCodec.Encode(w, next, first);
        var delta = SnapshotCodec.Decode(w.Written, f => f == first.Frame ? full : null, out _)!;
        Assert.Empty(delta.Containers!);
        Assert.Equal(new[] { Pet, Maidens }, delta.Ships.Single(s => s.IsHost).Hold!.HeldDevices);
        Assert.Equal(new[] { 19 }, delta.Ships.Single(s => s.Id == "g1").Hold!.HeldDevices);
    }

    [ContentFact]
    public void EachPlayersStatusPanelListsTheContainers()
    {
        var world = NewGame(guests: 2);
        world.SetDevice("g2", "MAIDENS_ON_SHIP");
        world.Leave("g2");
        var names = TestContent.With(p => Names.Load(p));
        var me = new PlayerSession(Systems, names, "g1", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        var lines = me.StatusLines();
        int at = lines.ToList().IndexOf("Containers (1):");
        Assert.True(at > 0);
        Assert.StartsWith("  Guest 2's container (1 device)  hyperspace ", lines[at + 1]);
    }

    // ---------------------------------------------------------------- the markers (question 12)

    [Fact]
    public void AMarkerIsAnArrowOnTheEdgeOrALabelForOneOnTheScreenNotDrawn()
    {
        const double w = 242, h = 227;
        Assert.Null(EdgeMarkers.Place(new MarkerTarget(MarkerKind.Player, "a", 100, 100, true), w, h, 4));
        var label = EdgeMarkers.Place(new MarkerTarget(MarkerKind.Player, "a", 100, 100, false), w, h, 4)!;
        Assert.Equal((false, 100.0, 100.0), (label.AtEdge, label.X, label.Y));
        // Far to the right: on the right edge, at the middle's height, pointing right.
        var right = EdgeMarkers.Place(new MarkerTarget(MarkerKind.Wreck, "b", 5000, h / 2, true), w, h, 4)!;
        Assert.Equal((true, w - 4, h / 2, 1.0, 0.0), (right.AtEdge, right.X, right.Y, right.DirX, right.DirY));
        // Far up and left: on the edge it leaves the screen by.
        var up = EdgeMarkers.Place(new MarkerTarget(MarkerKind.Container, "c", w / 2 - 10, -5000, false), w, h, 4)!;
        Assert.Equal(4, up.Y, 3);
        Assert.True(up.DirY < 0 && up.DirX < 0 && up.X < w / 2);
    }

    [ContentFact]
    public void InHyperspaceOthersAreMarkedWhereTheyAreOrByTheirSystemsStar()
    {
        var world = NewGame(guests: 2);
        var host = world.Host.Ship.Flight;
        var snapshot = world.Snapshot();
        // Both guests beside the host: drawn, no marker.
        var targets = EdgeMarkers.InHyperspace(snapshot, "host", host.LogX, host.LogY, Systems.Map, s => s.Number.ToString());
        Assert.All(targets, t => Assert.Null(EdgeMarkers.Place(t, IpGeometry.ScreenWidth, IpGeometry.ScreenHeight, 4)));
        // One far east, the other in Sol's system, its container left far west.
        world.Find("g1")!.Ship.Flight.PlaceAtUniverse((short)(host.UniverseX + 500), host.UniverseY);
        int sol = Star(world, "SOL_DEFINED");
        world.PutInSystem("g2", sol);
        Run(world, 1);
        snapshot = world.Snapshot();
        targets = EdgeMarkers.InHyperspace(snapshot, "host", host.LogX, host.LogY, Systems.Map, s => $"star {s.Number}");
        var g1 = EdgeMarkers.Place(targets.Single(t => t.Label.StartsWith("Guest 1")), IpGeometry.ScreenWidth, IpGeometry.ScreenHeight, 4)!;
        Assert.True(g1.AtEdge && g1.DirX > 0.99);
        var g2 = targets.Single(t => t.Label.StartsWith("Guest 2"));
        Assert.StartsWith($"Guest 2: star {sol} ", g2.Label);
        var (sx, sy) = HyperScreen.Star(Systems.Map.Stars[sol], host.LogX, host.LogY);
        Assert.True(Math.Abs(sx - g2.X) <= 1 && Math.Abs(sy - g2.Y) <= 1, $"{g2} at ({sx}, {sy})");   // Sol's star, on the screen
        Assert.False(EdgeMarkers.Place(g2, IpGeometry.ScreenWidth, IpGeometry.ScreenHeight, 4)!.AtEdge);
        // In a system: another player in hyperspace pointed at by the way they lie from the star.
        world.PutInSystem("host", sol);
        Run(world, 1);
        snapshot = world.Snapshot();
        var system = Systems[sol];
        targets = EdgeMarkers.InSystem(snapshot, "host", system, null, world.Host.Ship.Ip!.ZoomRadius, Systems.Map,
            s => "Sol", p => $"planet {p}");
        var east = EdgeMarkers.Place(targets.Single(t => t.Label.StartsWith("Guest 1")), IpGeometry.ScreenWidth, IpGeometry.ScreenHeight, 4)!;
        Assert.True(east.AtEdge && east.DirX > 0.9);
        Assert.Contains(": hyperspace ", east.Label);
    }
}
