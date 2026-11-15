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
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The host's fleet at the shipyard and in the roster (stage 4b item 7)
/// against UQM's own DoModifyShips (shipyard.c) and DoModifyRoster
/// (planets/roster.c), with build.c's CloneShipFragment and NameCaptain,
/// key by key (tests/golden/outfit.json's shipyard): the cursor, the list
/// of ships to build, the prices and refunds, the crew bought and its
/// price, the slots, the captains drawn, and the roster's cursor and crew.
/// </summary>
[Trait("Topic", "fleet")]
public class ShipyardGoldenTests
{
    private static readonly JsonElement Recorded = Golden.Load("outfit").GetProperty("shipyard");
    private static StoryRules Rules => OutfitTests.Rules;

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    // The roster's fleet, as a test gives it: the fleet's rules, the test ship's crew.
    private sealed class TestRoster : IRosterFleet
    {
        private readonly HostFleet _fleet;
        private readonly OutfitTests.TestShip _ship;

        public TestRoster(HostFleet fleet, OutfitTests.TestShip ship) => (_fleet, _ship) = (fleet, ship);

        public IReadOnlyList<FleetShip> Ships => _fleet.Ships;

        public int FlagshipCrew => _ship.Crew;

        public bool MoveCrew(int index, int delta)
        {
            if (!_fleet.MoveRosterCrew(Rules, index, delta, _ship.Crew, _ship.Modules.CrewCapacity))
                return false;
            _ship.Crew -= delta;
            return true;
        }
    }

    /// <summary>A scenario's ship, story (its allies), fleet and TFB_Random, as golden/outfit.c is given them.</summary>
    internal static (OutfitTests.TestShip Ship, StoryState Story, HostFleet Fleet, TfbRandom Random) Start(JsonElement scenario)
    {
        var (ship, story) = OutfitTests.Start(scenario);
        var allied = scenario.GetProperty("allied").EnumerateArray().Select(a => a.GetString()!).ToHashSet();
        foreach (var race in Rules.Races)
            story.SetAlliance(race, allied.Contains(race) ? Allied.Good : Allied.Bad);
        var fleet = new HostFleet();
        fleet.Restore(Rules, scenario.GetProperty("fleet").EnumerateArray().Select(f =>
            new FleetShip(f[0].GetString()!, f[1].GetInt32(), (byte)f[2].GetInt32(), f[3].GetInt32())));
        return (ship, story, fleet, new TfbRandom(scenario.GetProperty("seed").GetUInt32()));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TheShipyardAndTheRosterAreUqms(string name)
    {
        var scenario = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var (ship, story, fleet, random) = Start(scenario);
        var rules = new Outfitting(ship, story, fleet, random.Next);
        var roster = new TestRoster(fleet, ship);
        ShipyardMenu? yard = null;
        RosterMenu? menu = null;
        int[][]? wantFleet = null;
        var steps = scenario.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
        var results = scenario.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(steps.Count, results.Count);
        for (int i = 0; i < steps.Count; ++i)
        {
            var want = results[i];
            string where = $"{name} step {i} ({steps[i]})";
            if (want.TryGetProperty("fleet", out var f))
                wantFleet = f.EnumerateArray().Select(s => s.Ints()).ToArray();
            bool refused = false, left = false;
            int state, item;
            switch (steps[i])
            {
                case "yard":
                    // DoShipyard's select on Crew: DoModifyShips, freshly.
                    yard = new ShipyardMenu(rules, rules.Apply);
                    Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Select));
                    menu = null;
                    break;
                case "roster":
                    // RosterMenu: none with no fleet.
                    yard = null;
                    menu = fleet.Count == 0 ? null : new RosterMenu(fleet.Count);
                    left = menu is null;
                    break;
                default:
                    var key = steps[i] switch
                    {
                        "u" => MenuKey.Up,
                        "d" => MenuKey.Down,
                        "l" => MenuKey.Left,
                        "r" => MenuKey.Right,
                        "s" => MenuKey.Select,
                        _ => MenuKey.Cancel,
                    };
                    if (yard is not null)
                    {
                        refused = yard.Key(key) == OutfitOutcome.Refused;
                        left = yard.Mode == YardMode.Menu;
                    }
                    else
                    {
                        Assert.True(menu is not null, $"{where}: a key with nothing open");
                        refused = menu!.Press(key, roster) == MenuOutcome.Refused;
                        left = menu.Left;
                    }
                    break;
            }
            int screen;
            if (yard is not null)
            {
                (state, item) = left ? (yard.State, yard.Item) : (yard.Cursor, yard.Item);
                screen = left ? 0 : 2;
                if (left)
                    yard = null;
            }
            else
            {
                (state, item) = menu is null ? (0, 0) : (menu.State, menu.ModifyingCrew ? 1 : 0);
                screen = left || menu is null ? 0 : 3;
                var point = menu?.Point ?? (0, 0);
                Assert.True(want.GetProperty("point").Ints().SequenceEqual(new[] { point.X, point.Y }),
                    $"{where}: point {point}, UQM {want.GetProperty("point")}");
                if (left)
                    menu = null;
            }
            Assert.True(want.GetProperty("refused").GetInt32() == 1 == refused, $"{where}: refused {refused}");
            Assert.True(want.GetProperty("left").GetInt32() == 1 == left, $"{where}: left {left}");
            Assert.True(want.GetProperty("screen").GetInt32() == screen, $"{where}: screen {screen}");
            Assert.True(want.GetProperty("state").GetInt32() == state, $"{where}: state {state}, UQM {want.GetProperty("state")}");
            Assert.True(want.GetProperty("item").GetInt32() == item, $"{where}: item {item}, UQM {want.GetProperty("item")}");
            var shipNow = new[] { ship.Crew, (int)ship.Fuel, ship.Landers, (int)ship.ResourceUnits };
            Assert.True(want.GetProperty("ship").Ints().SequenceEqual(shipNow),
                $"{where}: ship [{string.Join(", ", shipNow)}], UQM {want.GetProperty("ship")}");
            Assert.True(want.GetProperty("crew_cost").GetInt32() == story.CrewCost, $"{where}: crew cost {story.CrewCost}");
            Assert.True(want.GetProperty("crew_bought").GetInt32() == story.CrewPurchased, $"{where}: crew bought {story.CrewPurchased}");
            var fleetNow = fleet.Ships.Select(s => new[] { Rules.RaceIndex(s.Race), s.Crew, (int)s.Captain, s.Slot }).ToArray();
            Assert.True(wantFleet!.Length == fleetNow.Length && wantFleet.Zip(fleetNow).All(p => p.First.SequenceEqual(p.Second)),
                $"{where}: fleet {string.Join(" ", fleetNow.Select(s => $"[{string.Join(",", s)}]"))}, "
                + $"UQM {string.Join(" ", wantFleet.Select(s => $"[{string.Join(",", s)}]"))}");
        }
    }

    [Fact]
    public void TheRecordingCoversTheShipyardsAndTheRostersRules()
    {
        // The scenarios the plan asks for are there, and the walks reach each rule (a guard against a re-recording
        // that loses them).
        var names = Recorded.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToHashSet();
        foreach (var n in new[] { "build-slots", "build-too-poor", "hangar-full", "hire-to-most", "hire-syreen", "dismiss-to-none",
                     "buy-back", "buy-back-short", "scrap-on-select", "threshold", "threshold-shofixti", "roster-crew", "roster-12" })
            Assert.Contains(n, names);
        Assert.Equal(40, names.Count(n => n.StartsWith("walk-")));
        // Across the walks: ships built, crew hired, refused, scrapped; the roster's crew moved both ways.
        int built = 0, scrapped = 0, refusedSteps = 0;
        foreach (var sc in Recorded.EnumerateArray().Where(s => s.GetProperty("name").GetString()!.StartsWith("walk-")))
        {
            int last = sc.GetProperty("fleet").GetArrayLength();
            foreach (var r in sc.GetProperty("results").EnumerateArray())
            {
                refusedSteps += r.GetProperty("refused").GetInt32();
                if (!r.TryGetProperty("fleet", out var fl))
                    continue;
                built += fl.GetArrayLength() > last ? 1 : 0;
                scrapped += fl.GetArrayLength() < last ? 1 : 0;
                last = fl.GetArrayLength();
            }
        }
        Assert.True(built > 10 && scrapped > 3 && refusedSteps > 20, $"walks: {built} built, {scrapped} scrapped, {refusedSteps} refused");
    }
}
