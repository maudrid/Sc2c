// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The group inventory (docs/plans/stage-3.md, item 10): the devices the
/// group carries, and the Manifest's Devices list, against UQM's own
/// planets/devices.c (tests/golden/devices.json; its drawing is
/// DevicesDisplayTests').
/// </summary>
[Trait("Topic", "story")]
public class DevicesTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly JsonElement Recorded = Golden.Load("devices").GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TheListAndItsCursorAreUqms(string name)
    {
        var sc = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        // UQM's one ship's fields; and the same devices as the port holds them (stage 3c item 5): the shared ones in
        // the story, the rest aboard the ship.
        var state = sc.GetProperty("state").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32());
        var list = Rules.Devices.Inventory(f => state.GetValueOrDefault(f));
        Assert.Equal(sc.GetProperty("list").Ints(), list);
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        var held = new SortedSet<int>();
        foreach (var (f, v) in state)
        {
            if (DeviceTable.HeldByShips(f))
                held = Rules.Devices.WithValue(held, f, v);
            else
                story.Set(f, v);
        }
        Assert.Equal(list, Rules.Devices.Inventory(story.Get, held));
        var steps = sc.GetProperty("steps").EnumerateArray().ToList();
        if (list.Count == 0)
        {
            Assert.Empty(steps);   // DevicesMenu opens nothing
            return;
        }
        var menu = new DevicesMenu(list);
        var keys = sc.GetProperty("keys").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < steps.Count; ++i)
        {
            var want = steps[i];
            if (i > 0)
                menu.Press(keys[i - 1] switch
                {
                    "u" => MenuKey.Up,
                    "d" => MenuKey.Down,
                    "l" => MenuKey.Left,
                    "r" => MenuKey.Right,
                    "pu" => MenuKey.PageUp,
                    "pd" => MenuKey.PageDown,
                    _ => MenuKey.Cancel,
                });
            string where = $"{name} step {i}";
            Assert.True(want.GetProperty("state").GetInt32() == menu.Cursor, $"{where}: cursor {menu.Cursor}");
            Assert.True(want.GetProperty("top").GetInt32() == menu.Top, $"{where}: top {menu.Top}");
            Assert.Equal(want.GetProperty("left").GetInt32() == 1, menu.Left);
        }
    }

    [Fact]
    public void DevicesAreHeldByShipsButTheSpawnerAndTheLunarBase()
    {
        Assert.Equal(28, Rules.Devices.All.Count);
        foreach (var field in Rules.Devices.Fields)
            Assert.True(Rules.Table.Contains(field), field);
        // Stage 3c item 5 (the owner's answer to question 5): every listed device's field is held by ships, but the
        // spawner's and the Lunar base's, shared; the held ones are kept out of the shared story, but the three
        // whose field stays as the story's record of found, sold or given.
        Assert.Equal(Rules.Devices.Fields.Except(DeviceTable.SharedFields).OrderBy(f => f), DeviceTable.HeldFields.OrderBy(f => f));
        Assert.Equal(new[] { "MOONBASE_ON_SHIP", "PORTAL_SPAWNER_ON_SHIP" }, DeviceTable.SharedFields.OrderBy(f => f));
        Assert.All(DeviceTable.SharedFields.Concat(DeviceTable.RecordedFields), f => Assert.False(StoryState.NotShared.ContainsKey(f)));
        Assert.All(DeviceTable.HeldFields.Except(DeviceTable.RecordedFields), f =>
        {
            Assert.True(StoryState.NotShared.ContainsKey(f));
            Assert.Contains(f, StoryState.MovedToPlayers);
        });
        Assert.Equal(new[] { 0, 27 }, Rules.Devices.All.Where(d => d.Listed && !Rules.Devices.IsHeld(d.Id)).Select(d => d.Id));
        // A held field's value on a ship: the device's (the Ultron's repairs), set and taken as a talk sets it.
        var held = Rules.Devices.WithValue(new[] { 4 }, "ULTRON_CONDITION", 3);
        Assert.Equal(new[] { 4, 9 }, held);
        Assert.Equal(3, Rules.Devices.ValueOf(held, "ULTRON_CONDITION"));
        Assert.Equal(new[] { 4, 10 }, Rules.Devices.WithValue(held, "ULTRON_CONDITION", 4));
        Assert.Equal(new[] { 4 }, Rules.Devices.WithValue(held, "ULTRON_CONDITION", 5));   // given back: no device
        Assert.Equal(new[] { 9 }, Rules.Devices.WithValue(held, "ROSY_SPHERE_ON_SHIP", 0));
        Assert.Equal(new[] { 14, 15, 16, 23 }, Rules.Devices.All.Where(d => !d.Listed).Select(d => d.Id));
        Assert.Throws<ArgumentException>(() => new DevicesMenu(Array.Empty<int>()));
        Assert.Equal(MenuOutcome.NotYet, new DevicesMenu(new[] { 3 }).Press(MenuKey.Select));   // using them is 3c
    }

    [ContentFact]
    public void EachPlayersManifestListsTheSharedDevicesAndTheirShips()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host",
            systems, TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        world.PutInOrbit("guest", 82, 2, null);
        var names = TestContent.With(p => Names.Load(p));
        var me = new PlayerSession(systems, names, "guest", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.NextInput(Hyperspace.ShipInput.None);
        // Manifest, then Devices: with nothing aboard, nothing opens.
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);
        Assert.Null(me.OrbitMenu!.DeviceList);
        // The host's lander brought up the Lunar base: everyone's; the Rosy Sphere aboard the guest's ship, the Talking
        // Pet aboard the host's (stage 3c item 5): the guest's list has the base and their sphere.
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        world.SetDevice("guest", "ROSY_SPHERE_ON_SHIP");
        world.SetDevice("host", "TALKING_PET_ON_SHIP");
        world.Step();
        me.Observe(world.Snapshot());
        Assert.Equal(new[] { 4, 27 }, me.Devices());
        Assert.Equal(new[] { 4 }, me.Me!.Hold!.HeldDevices);
        Assert.Equal(new[] { 1 }, world.Snapshot().Ships.Single(s => s.IsHost).Hold!.HeldDevices);
        me.PressMenu(MenuKey.Select);
        Assert.Equal(new[] { 4, 27 }, me.OrbitMenu.DeviceList);
        // Drawn in the status area (DevicesDisplay), which hides the status panel: its lines don't list them.
        Assert.DoesNotContain(me.StatusLines(), l => l.Contains("Rosy Sphere") || l.Contains("Moon Base"));
        me.PressMenu(MenuKey.Select);
        Assert.Contains("later stage", me.OrbitMessage);
        me.PressMenu(MenuKey.Cancel);
        Assert.Null(me.OrbitMenu.DeviceList);
        // The game unable to draw it closes it as Esc does, not by a key: back at the Manifest, the message gone.
        me.PressMenu(MenuKey.Select);
        me.PressMenu(MenuKey.Select);
        Assert.Contains("later stage", me.OrbitMessage);
        me.CloseDevices();
        Assert.Null(me.OrbitMenu.DeviceList);
        Assert.Equal(OrbitMenu.MenuPage.Manifest, me.OrbitMenu.Page);
        Assert.Equal("", me.OrbitMessage);
    }

    [ContentFact]
    public void StartDevicesPutThemAboardTheHostsShip()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host", systems,
            TestContent.Shapes);
        var story = host.World.Story!;
        // --start-devices (testing: the Manifest's Devices list): by name, in any case, or number; the Ultron by its
        // state. Since stage 3c item 5 the shared ones (the Lunar base) in the story, the rest aboard the host's ship,
        // the Ultron's shared record set with it.
        Assert.Contains("4 devices", host.StartWithDevices(new[] { "ROSY_SPHERE_DEVICE", "egg_casing1_device", "27", "ULTRON_2_DEVICE" }));
        List<int> Listed() => Rules.Devices.Inventory(story.Get, host.World.Host.Ship.Hold.Devices);
        Assert.Equal(new[] { 4, 9, 19, 27 }, Listed());
        Assert.Equal(new[] { 4, 9, 19 }, host.World.Host.Ship.Hold.Devices);
        Assert.Equal((1, 3), (story.Get("MOONBASE_ON_SHIP"), story.Get("ULTRON_CONDITION")));
        Assert.Throws<ArgumentException>(() => host.StartWithDevices(new[] { "DESTRUCT_CODE_DEVICE" }));   // never listed
        Assert.Throws<ArgumentException>(() => host.StartWithDevices(new[] { "NO_SUCH_DEVICE" }));
        Assert.Throws<ArgumentException>(() => host.StartWithDevices(new[] { "ULTRON_0_DEVICE", "ULTRON_3_DEVICE" }));
        Assert.Equal(new[] { 4, 9, 19, 27 }, Listed());   // a refusal changes nothing
        // --start-state's device fields: the host's ship's too, a recorded one's record with it.
        Assert.Contains("2 story fields", host.StartWithState(new[] { "TALKING_PET_ON_SHIP=1", "ARTIFACT_2_ON_SHIP=1" }));
        Assert.Equal(new[] { 1, 4, 9, 19, 25 }, host.World.Host.Ship.Hold.Devices);
        Assert.Equal(1, story.Get("ARTIFACT_2_ON_SHIP"));
        Assert.Throws<InvalidOperationException>(() => story.Get("TALKING_PET_ON_SHIP"));   // no shared field
    }

    [ContentFact]
    public void StartStateSetsSharedStoryFieldsAndRefusesTheRest()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host", systems,
            TestContent.Shapes);
        var story = host.World.Story!;
        // --start-state NAME=VALUE (testing, the stage 3b-2 plan's item 1): shared fields, each to what its bits hold.
        Assert.Equal("Starting with 3 story fields set (THRADDASH_BODY_COUNT=31, USED_BROADCASTER=1, MYCON_FELL_FOR_AMBUSH=1)",
            host.StartWithState(new[] { "THRADDASH_BODY_COUNT=31", "USED_BROADCASTER=0", "USED_BROADCASTER=1", " MYCON_FELL_FOR_AMBUSH = 1 " }));
        // USED_BROADCASTER is each player's own since stage 3b-2 item 4: the host's is set.
        Assert.Equal((31, true, 1), (story.Get("THRADDASH_BODY_COUNT"), host.World.Host.Ship.UsedBroadcaster, story.Get("MYCON_FELL_FOR_AMBUSH")));
        var bits = story.Bits.ToArray();
        void Refused(string assignment, string says)
        {
            var e = Assert.Throws<ArgumentException>(() => host.StartWithState(new[] { "SUN_DEVICE_ON_SHIP=1", assignment }));
            Assert.Contains(says, e.Message);
        }
        Refused("NO_SUCH_FIELD=1", "isn't a story field");
        Refused("THRADDASH_BODY_COUNT=32", "holds 0 to 31 (5 bits)");
        Refused("USED_BROADCASTER=2", "holds 0 to 1 (1 bit)");
        Refused("USED_BROADCASTER=-1", "holds 0 to 1");
        Refused("USED_BROADCASTER=yes", "holds 0 to 1");
        Refused("USED_BROADCASTER", "NAME=VALUE");
        Refused("GLOBAL_FLAGS_AND_DATA=128", "isn't shared story state");
        Refused("MELNORME_CREDIT0=5", "Melnorme credits");
        Refused("MELNORME_RESCUE_COUNT=1", "isn't shared story state");
        Refused("ESCAPE_COUNTER=1", "isn't shared story state");
        Assert.Equal(bits, story.Bits.ToArray());   // a refusal changes nothing, the good field before it either
    }
}
