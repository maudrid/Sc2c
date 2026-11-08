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
/// planets/devices.c (tests/golden/devices.json).
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
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        foreach (var f in sc.GetProperty("state").EnumerateObject())
            story.Set(f.Name, f.Value.GetInt32());
        var list = Rules.Devices.Inventory(story.Get);
        Assert.Equal(sc.GetProperty("list").Ints(), list);
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
            var drawn = want.GetProperty("drawn").EnumerateArray().ToList();
            // A whole page drawn (with the title): the rows shown, each the
            // device's name and icon, as the port would show them.
            if (drawn.Count > 0 && drawn[0][0].GetString() == "title")
            {
                Assert.Equal(GameStrings.DeviceBase, drawn[0][1].GetInt32());
                var shown = list.Skip(menu.Top).Take(DeviceTable.VisibleRows).ToList();
                var icons = drawn.Where(d => d[0].GetString() == "icon").Select(d => d[1].GetInt32()).ToList();
                var names = drawn.Where(d => d[0].GetString() == "name" && d[3].GetInt32() == 0)
                    .Select(d => (d[1].GetInt32(), d[2].GetInt32())).ToList();
                Assert.Equal(shown.Select(d => DeviceTable.FirstIconFrame + d), icons);
                Assert.Equal(shown.Select((d, row) => (DeviceTable.NameString(d), row)), names);
            }
            // The row lit is the cursor's, with its name.
            if (drawn.LastOrDefault(d => d[0].GetString() == "name" && d[3].GetInt32() == 1) is { ValueKind: JsonValueKind.Array } lit)
                Assert.Equal((DeviceTable.NameString(list[menu.Cursor]), menu.Cursor - menu.Top), (lit[1].GetInt32(), lit[2].GetInt32()));
        }
    }

    [Fact]
    public void TheGroupInventoryIsStoryFieldsSharedByEveryone()
    {
        Assert.Equal(28, Rules.Devices.All.Count);
        foreach (var field in Rules.Devices.Fields)
            Assert.True(Rules.Table.Contains(field), field);
        Assert.DoesNotContain(Rules.Devices.Fields, f => StoryState.NotShared.ContainsKey(f));
        Assert.Equal(new[] { 14, 15, 16, 23 }, Rules.Devices.All.Where(d => !d.Listed).Select(d => d.Id));
        Assert.Throws<ArgumentException>(() => new DevicesMenu(Array.Empty<int>()));
        Assert.Equal(MenuOutcome.NotYet, new DevicesMenu(new[] { 3 }).Press(MenuKey.Select));   // using them is 3c
    }

    [ContentFact]
    public void EveryPlayersManifestListsTheGroupsDevicesByName()
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
        // The host's lander brought up the Lunar base: the group carries it.
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        world.Story.Set("ROSY_SPHERE_ON_SHIP", 1);
        world.Step();
        me.Observe(world.Snapshot());
        Assert.Equal(new[] { 4, 27 }, me.Devices());
        me.PressMenu(MenuKey.Select);
        Assert.Equal(new[] { 4, 27 }, me.OrbitMenu.DeviceList);
        string sphere = names.GameString(DeviceTable.NameString(4)).Replace('\n', ' ');
        Assert.Contains(me.StatusLines(), l => l == $"> {sphere}");
        me.PressMenu(MenuKey.Select);
        Assert.Contains("later stage", me.OrbitMessage);
        me.PressMenu(MenuKey.Cancel);
        Assert.Null(me.OrbitMenu.DeviceList);
    }
}
