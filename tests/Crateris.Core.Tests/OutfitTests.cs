// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Outfitting and the shipyard's crew (docs/plans/stage-3.md, item 8)
/// against UQM's own outfit.c and shipyard.c, key by key
/// (tests/golden/outfit.json): the menu's cursor (<see cref="OutfitMenu"/>)
/// and the rules (<see cref="Outfitting"/>) together.
/// </summary>
[Trait("Topic", "starbase")]
public class OutfitTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly JsonElement Recorded = Golden.Load("outfit").GetProperty("scenarios");

    /// <summary>A ship for outfitting, as the tests give it.</summary>
    internal sealed class TestShip : IOutfitShip
    {
        public ShipKind Kind { get; init; } = ShipKind.Flagship;
        public ShipModules Modules { get; init; } = null!;
        public int Crew { get; set; }
        public uint Fuel { get; set; }
        public int Landers { get; set; }
        public uint ResourceUnits { get; set; }
        public int MineralMass { get; init; }
    }

    private static readonly Dictionary<string, byte> ModuleNumbers = new()
    {
        ["PLANET_LANDER"] = 0, ["FUSION_THRUSTER"] = 1, ["TURNING_JETS"] = 2, ["CREW_POD"] = 3, ["STORAGE_BAY"] = 4,
        ["FUEL_TANK"] = 5, ["HIGHEFF_FUELSYS"] = 6, ["DYNAMO_UNIT"] = 7, ["SHIVA_FURNACE"] = 8, ["GUN_WEAPON"] = 9,
        ["BLASTER_WEAPON"] = 10, ["CANNON_WEAPON"] = 11, ["TRACKING_SYSTEM"] = 12, ["ANTIMISSILE_DEFENSE"] = 13,
        ["EMPTY"] = ShipModules.EmptyModule,
    };

    private static (TestShip, StoryState) Start(JsonElement scenario)
    {
        var story = StoryState.NewGame(Rules, new GameClock(2155, 3, 4));
        foreach (var f in scenario.GetProperty("state").EnumerateObject())
            story.Set(f.Name, f.Value.GetInt32());
        foreach (var c in scenario.GetProperty("costs").EnumerateObject())
            story.SetModuleCost(c.Name, c.Value.GetInt32());
        story.SetFuelCost(scenario.GetProperty("fuel_cost").GetInt32());
        story.SetCrewCost(scenario.GetProperty("crew_cost").GetInt32());
        if (scenario.GetProperty("shofixti").GetBoolean())
            story.SetAlliance("SHOFIXTI_SHIP", Allied.Good);
        var s = scenario.GetProperty("ship");
        var drives = s.GetProperty("drives").Ints();
        var jets = s.GetProperty("jets").Ints();
        var modules = new ShipModules(
            Enumerable.Range(0, 11).Select(i => drives.Contains(i) ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, 8).Select(i => jets.Contains(i) ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            s.GetProperty("modules").EnumerateArray().Select(m => m.GetString()!.StartsWith("BOMB_MODULE_")
                ? (byte)(ShipModules.BombModule0 + m.GetString()![^1] - '0') : ModuleNumbers[m.GetString()!]).ToArray());
        var ship = new TestShip
        {
            Modules = modules,
            Crew = s.GetProperty("crew").GetInt32(),
            Fuel = s.GetProperty("fuel").GetUInt32(),
            Landers = s.GetProperty("landers").GetInt32(),
            ResourceUnits = s.GetProperty("ru").GetUInt32(),
            MineralMass = s.GetProperty("elements").Ints().Sum(),
        };
        return (ship, story);
    }

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void OutfittingIsUqms(string name)
    {
        var scenario = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var (ship, story) = Start(scenario);
        var rules = new Outfitting(ship, story);
        var menu = new OutfitMenu(rules, rules.Apply);
        bool open = false;
        int[]? modules = null, drives = null, jets = null;
        var steps = scenario.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
        var results = scenario.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(steps.Count, results.Count);
        for (int i = 0; i < steps.Count; ++i)
        {
            var want = results[i];
            modules = want.TryGetProperty("modules", out var m) ? m.Ints() : modules;
            drives = want.TryGetProperty("drives", out var d) ? d.Ints() : drives;
            jets = want.TryGetProperty("jets", out var j) ? j.Ints() : jets;
            bool refused = false, left = false;
            switch (steps[i])
            {
                case "outfit":
                    menu.Open();
                    open = true;
                    break;
                case "hire":
                    refused = !rules.Apply(new HireCrew());
                    break;
                case "dismiss":
                    refused = !rules.Apply(new DismissCrew());
                    break;
                default:
                    Assert.True(open, $"{name} step {i}: a key with outfitting closed");
                    var outcome = menu.Key(steps[i] switch
                    {
                        "u" => MenuKey.Up,
                        "d" => MenuKey.Down,
                        "l" => MenuKey.Left,
                        "r" => MenuKey.Right,
                        "s" => MenuKey.Select,
                        "c" => MenuKey.Cancel,
                        "pu" => MenuKey.PageUp,
                        _ => MenuKey.PageDown,
                    });
                    refused = outcome == OutfitOutcome.Refused;
                    left = outcome == OutfitOutcome.Left;
                    open &= !left;
                    break;
            }
            string where = $"{name} step {i} ({steps[i]})";
            Assert.True(want.GetProperty("refused").GetInt32() == 1 == refused, $"{where}: refused {refused}");
            Assert.True(want.GetProperty("left").GetInt32() == 1 == left, $"{where}: left {left}");
            if (steps[i] is not ("hire" or "dismiss"))
            {
                Assert.True(want.GetProperty("state").GetInt32() == menu.State, $"{where}: state {menu.State}, UQM {want.GetProperty("state")}");
                Assert.True(want.GetProperty("item").GetInt32() == menu.Item, $"{where}: item {menu.Item}, UQM {want.GetProperty("item")}");
            }
            var shipNow = new[] { ship.Crew, (int)ship.Fuel, ship.Landers, (int)ship.ResourceUnits };
            Assert.True(want.GetProperty("ship").Ints().SequenceEqual(shipNow),
                $"{where}: ship [{string.Join(", ", shipNow)}], UQM {want.GetProperty("ship")}");
            Assert.Equal(modules, ship.Modules.ModuleSlots.Select(x => (int)x).ToArray());
            Assert.Equal(drives, ship.Modules.DriveSlots.Select(x => (int)x).ToArray());
            Assert.Equal(jets, ship.Modules.JetSlots.Select(x => (int)x).ToArray());
            Assert.True(want.GetProperty("crew_cost").GetInt32() == story.CrewCost, $"{where}: crew cost {story.CrewCost}");
            Assert.True(want.GetProperty("crew_bought").GetInt32() == story.CrewPurchased, $"{where}: crew bought {story.CrewPurchased}");
        }
    }
}
