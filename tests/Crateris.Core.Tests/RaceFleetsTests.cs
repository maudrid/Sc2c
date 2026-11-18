// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The races' fleets (docs/plans/stage-3b.md, item 2): races.h's tables
/// of them, each race's FLEET_INFO as a new game starts it, what
/// check_race_growth, SetRaceDest and StartSphereTracking do to it (UQM's
/// own run, day by day, is matched in <see cref="ClockEventsTests"/>), and
/// the fleets in saves.
/// </summary>
[Trait("Topic", "story")]
public class RaceFleetsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    private static GameWorld NewWorld()
    {
        var clock = new GameClock(2155, 2, 17);
        return new GameWorld(new GameSettings(), clock) { Story = StoryState.NewGame(Rules, clock) };
    }

    // ---- the tables

    [Fact]
    public void TheRulesHoldRacesHsFleetTables()
    {
        Assert.Equal(Rules.Races, Rules.Fleets.Select(f => f.Race));
        RaceFleetRules Of(string race) => Rules.Fleets[Rules.RaceIndex(race)];
        // races.h: RACE_HYPERSPACE_PERCENT 20, RACE_INTERPLANETARY_PERCENT 10,
        // MAKE_BYTE (1, 5), LOG_TO_HYPER (48), MAKE_RGB15_INIT (0x0C, 0x05, 0x00);
        // the sphere from spathi.c's RACE_DESC: 180 at (2549, 3600).
        Assert.Equal(new RaceFleetRules("SPATHI_SHIP", 180, 2549, 3600, 20, 10, 1, 5, (48 << 5) >> 1, new Rgba(0x63, 0x29, 0, 0xFF)),
            Of("SPATHI_SHIP"));
        Assert.Equal((70, 40, 1, 5, 480), (Of("URQUAN_SHIP").HyperspacePercent, Of("URQUAN_SHIP").InterplanetaryPercent,
            Of("URQUAN_SHIP").MakeupMin, Of("URQUAN_SHIP").MakeupMax, Of("URQUAN_SHIP").HyperSpeed));
        Assert.Equal((256, 48, 1700, 60, 60), (Of("ILWRATH_SHIP").Strength, Of("ILWRATH_SHIP").HomeX, Of("ILWRATH_SHIP").HomeY,
            Of("ILWRATH_SHIP").HyperspacePercent, Of("ILWRATH_SHIP").InterplanetaryPercent));
        // The races met anywhere, one ship a group.
        foreach (string race in new[] { "SLYLANDRO_SHIP", "MELNORME_SHIP" })
            Assert.Equal((Rules.InfiniteRadius, 1, 1), (Of(race).Strength, Of(race).MakeupMin, Of(race).MakeupMax));
        Assert.Equal((2, 5), (Of("SLYLANDRO_SHIP").HyperspacePercent, Of("SLYLANDRO_SHIP").InterplanetaryPercent));
        // The Yehat rebels' tables are their own, their ship the Yehat's; the
        // drone's tables stop after the percents (and its ship, the probe's, has no sphere).
        Assert.Equal((136, 4970, 40, new Rgba(165, 57, 255, 255)), (Of("YEHAT_REBEL_SHIP").Strength, Of("YEHAT_REBEL_SHIP").HomeX,
            Of("YEHAT_REBEL_SHIP").HomeY, Of("YEHAT_REBEL_SHIP").Color));
        Assert.Equal(new RaceFleetRules("URQUAN_DRONE_SHIP", 0, 0, 0, 0, 0, 0, 0, 0, null), Of("URQUAN_DRONE_SHIP"));
        Assert.Equal((11, 2794, 65535), (Rules.SphereRadiusIncrement, Rules.MaxFleetStrength, Rules.InfiniteRadius));
        // Each race's sphere is its ship's.
        var ships = ShipDescriptions.Load(DataDir);
        for (int i = 0; i < Rules.Races.Count; ++i)
        {
            var fleet = ships[Rules.RaceShips[i].Ship].Fleet;
            Assert.Equal((fleet.Strength, fleet.HomeX, fleet.HomeY), (Rules.Fleets[i].Strength, Rules.Fleets[i].HomeX, Rules.Fleets[i].HomeY));
        }
    }

    // ---- a new game

    [Fact]
    public void ANewGamesFleetsAreItsShipsSpheresUnknownWithNoGrowth()
    {
        var story = NewWorld().Story!;
        for (int i = 0; i < Rules.Races.Count; ++i)
        {
            var r = Rules.Fleets[i];
            int strength = r.Race == "YEHAT_REBEL_SHIP" ? 0 : r.Strength;
            Assert.Equal(new RaceFleet(strength, 0, r.HomeX, r.HomeY, r.HomeX, r.HomeY, 0, 0, 127, 0, null, 0, 0), story.Fleets[i]);
        }
        Assert.Equal(new RaceFleet(484, 0, 5750, 6000, 5750, 6000, 0, 0, 127, 0, null, 0, 0), story.Fleet("URQUAN_SHIP"));
        // InitGameStructures' "Rebel special case": the Yehat's place, no sphere.
        Assert.Equal((0, 4970, 40), (story.Fleet("YEHAT_REBEL_SHIP").ActualStrength, story.Fleet("YEHAT_REBEL_SHIP").LocX,
            story.Fleet("YEHAT_REBEL_SHIP").LocY));
        Assert.Equal(65535, story.Fleet("MELNORME_SHIP").ActualStrength);
        Assert.Equal(0, story.Fleet("CHMMR_SHIP").ActualStrength);
    }

    [Fact]
    public void InStage3b1TheFleetsStayAsANewGameStartsThem()
    {
        var world = NewWorld();
        var fleets = world.Story!.Fleets.ToList();
        world.MoveClockDays(400);
        Assert.Equal(fleets, world.Story.Fleets);
        Assert.Equal(400, world.HyperspaceEncounterDays);
    }

    // ---- check_race_growth's arrivals, SetRaceDest, StartSphereTracking

    [Fact]
    public void AFleetsArrivalRunsItsEventOr3csIsSetAside()
    {
        var world = NewWorld();
        var story = world.Story!;
        story.SetRaceDest("ILWRATH_SHIP", 2500, 8070, 3, "ADVANCE_ILWRATH_MISSION");
        story.SetRaceDest("URQUAN_SHIP", 5000, 5000, 2, "SLYLANDRO_RAMP_UP");
        story.SetRaceDest("SPATHI_SHIP", 0, 0, 2, null);
        var done = world.MoveClockDays(3);
        // The Ur-Quan's arrival on the second day ran the ramp-up (not one the clock took off its queue).
        Assert.Equal(2, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Contains(new StoryEvent("SLYLANDRO_RAMP_UP", 2155, 8, 18), story.Events);
        Assert.Contains(new StoryEvent("SLYLANDRO_RAMP_UP", 2155, 8, 20), story.Events);   // 182 days from 19 February
        Assert.All(done, e => Assert.Equal("HYPERSPACE_ENCOUNTER_EVENT", e.Event.Event));
        // The Ilwrath's on the third, one of 3c's: set aside on that day for 3c.
        Assert.Equal(new[] { new StoryEvent("ADVANCE_ILWRATH_MISSION", 2155, 2, 20) }, story.Events.SetAside);
        Assert.Equal((2500, 8070, 0), (story.Fleet("ILWRATH_SHIP").LocX, story.Fleet("ILWRATH_SHIP").LocY, story.Fleet("ILWRATH_SHIP").DaysLeft));
        // The Spathi there, with nothing to run.
        Assert.Equal((0, 0, 0), (story.Fleet("SPATHI_SHIP").LocX, story.Fleet("SPATHI_SHIP").LocY, story.Fleet("SPATHI_SHIP").DaysLeft));
        // Arrived, they stay.
        world.MoveClockDays(3);
        Assert.Equal((2500, 8070), (story.Fleet("ILWRATH_SHIP").LocX, story.Fleet("ILWRATH_SHIP").LocY));
        Assert.Single(story.Events.SetAside);
    }

    [Fact]
    public void AFleetShrinkingToNothingIsExtinctAndNoLongerTracked()
    {
        var world = NewWorld();
        var story = world.Story!;
        long version = story.Version;
        story.SetFleet("VUX_SHIP", story.Fleet("VUX_SHIP") with { Growth = unchecked((byte)-100) });
        Assert.True(story.Version > version);
        Assert.True(story.StartSphereTracking("VUX_SHIP"));
        Assert.Equal((1, 4412, 1558), (story.Fleet("VUX_SHIP").KnownStrength, story.Fleet("VUX_SHIP").KnownLocX, story.Fleet("VUX_SHIP").KnownLocY));
        world.MoveClockDays(1);
        Assert.Equal((62, Allied.Bad), (story.Fleet("VUX_SHIP").ActualStrength, story.Alliance("VUX_SHIP")));
        world.MoveClockDays(1);
        Assert.Equal((0, Allied.Dead), (story.Fleet("VUX_SHIP").ActualStrength, story.Alliance("VUX_SHIP")));
        Assert.False(story.StartSphereTracking("VUX_SHIP"));
        // A race with no sphere but alive: nothing to track, yet it lives.
        Assert.True(story.StartSphereTracking("CHMMR_SHIP"));
        Assert.Equal(0, story.Fleet("CHMMR_SHIP").KnownStrength);
        // One met anywhere: never shown.
        Assert.True(story.StartSphereTracking("SLYLANDRO_SHIP"));
        Assert.Equal(0, story.Fleet("SLYLANDRO_SHIP").KnownStrength);
        // UQM's race number for the Arilou is 0: false, though tracked.
        Assert.False(story.StartSphereTracking("ARILOU_SHIP"));
        Assert.Equal(1, story.Fleet("ARILOU_SHIP").KnownStrength);
        Assert.Throws<KeyNotFoundException>(() => story.StartSphereTracking("NO_SHIP"));
    }

    [Fact]
    public void AFleetUqmsFieldsCantHoldIsRefused()
    {
        var story = NewWorld().Story!;
        var spathi = story.Fleet("SPATHI_SHIP");
        foreach (var bad in BadFleets(spathi))
            Assert.Throws<ArgumentException>(() => story.SetFleet("SPATHI_SHIP", bad));
        Assert.Throws<ArgumentException>(() => story.SetRaceDest("SPATHI_SHIP", 0, 0, 256, null));
        Assert.Throws<ArgumentException>(() => story.SetRaceDest("SPATHI_SHIP", 40000, 0, 1, null));
        Assert.Throws<ArgumentException>(() => story.SetRaceDest("SPATHI_SHIP", 0, 0, 1, "NO_SUCH_EVENT"));
        Assert.Equal(spathi, story.Fleet("SPATHI_SHIP"));
        Assert.Throws<KeyNotFoundException>(() => story.SetRaceDest("NO_SHIP", 0, 0, 1, null));
    }

    private static IEnumerable<RaceFleet> BadFleets(RaceFleet f) => new[]
    {
        f with { ActualStrength = -1 }, f with { ActualStrength = 65536 }, f with { KnownStrength = -1 },
        f with { LocX = 32768 }, f with { LocY = -32769 }, f with { KnownLocX = 40000 }, f with { KnownLocY = -40000 },
        f with { Growth = 256 }, f with { Growth = -1 }, f with { GrowthFract = 300 }, f with { GrowthErrTerm = -2 },
        f with { DaysLeft = 256 }, f with { DestLocX = 32768 }, f with { DestLocY = -32769 }, f with { Arrival = "NO_SUCH_EVENT" },
    };

    // ---- saves

    [Fact]
    public void TheFleetsAreSavedAndANewGamesWithoutThem()
    {
        var world = NewWorld();
        var story = world.Story!;
        story.SetFleet("THRADDASH_SHIP", story.Fleet("THRADDASH_SHIP") with { Growth = 251, GrowthFract = 91 });
        story.SetRaceDest("THRADDASH_SHIP", 4879, 7201, 14, "ADVANCE_THRADD_MISSION");
        story.StartSphereTracking("ILWRATH_SHIP");
        world.MoveClockDays(3);
        var saved = story.Save();
        var json = JsonSerializer.Serialize(saved);
        var restored = StoryState.Restore(Rules, JsonSerializer.Deserialize<StoryState.Saved>(json)!);
        Assert.Equal(story.Fleets, restored.Fleets);
        Assert.Equal(story.Alliances, restored.Alliances);
        Assert.Equal(new RaceFleet(134, 0, 3036, 8112, 2535, 8358, 251, 91, 110, 11, "ADVANCE_THRADD_MISSION", 4879, 7201),
            restored.Fleet("THRADDASH_SHIP"));
        // They go on as they were: the same day after.
        var again = new GameWorld(new GameSettings(), new GameClock(world.Clock.Year, world.Clock.Month, world.Clock.Day)) { Story = restored };
        world.MoveClockDays(5);
        again.MoveClockDays(5);
        Assert.Equal(story.Fleets, again.Story!.Fleets);
        // A saved story without them has a new game's.
        Assert.Equal(StoryState.NewGame(Rules, new GameClock(2155, 2, 17)).Fleets,
            StoryState.Restore(Rules, saved with { Fleets = null }).Fleets);
    }

    [Fact]
    public void BadSavedFleetsAreRefused()
    {
        var saved = NewWorld().Story!.Save();
        void Refused(Action<Dictionary<string, RaceFleet>> change)
        {
            var fleets = new Dictionary<string, RaceFleet>(saved.Fleets!);
            change(fleets);
            Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, saved with { Fleets = fleets }));
        }
        Refused(f => f.Remove("SPATHI_SHIP"));
        Refused(f => f["NO_SHIP"] = f["SPATHI_SHIP"]);
        Refused(f => f["SPATHI_SHIP"] = null!);
        foreach (var bad in BadFleets(saved.Fleets!["SPATHI_SHIP"]))
            Refused(f => f["SPATHI_SHIP"] = bad);
        // In JSON too: a field missing is 0, an unknown one ignored; a strength past a COUNT refused.
        var node = JsonNode.Parse(JsonSerializer.Serialize(saved))!;
        node["Fleets"]!["SPATHI_SHIP"]!["ActualStrength"] = 70000;
        Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, node.Deserialize<StoryState.Saved>()!));
    }

    [ContentFact]
    public void AHostSaveKeepsTheFleetsAndAnOlderOneHasANewGamesOrIsRefusedThem()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        var story = world.Story!;
        story.SetFleet("SPATHI_SHIP", story.Fleet("SPATHI_SHIP") with { Growth = 206 });   // 50 a day lost
        story.SetRaceDest("URQUAN_SHIP", 5000, 5000, 20, null);
        story.StartSphereTracking("ILWRATH_SHIP");
        world.MoveClockDays(4);
        Assert.Equal(Allied.Dead, story.Alliance("SPATHI_SHIP"));
        string json = HostSave.ToJson(world);
        var node = JsonNode.Parse(json)!;
        Assert.Equal(13, node["version"]!.GetValue<int>());
        Assert.Equal(1, node["story"]!["fleets"]!["ILWRATH_SHIP"]!["known_strength"]!.GetValue<int>());
        var loaded = HostSave.FromJson(json, systems, TestContent.Shapes);
        Assert.Equal(story.Fleets, loaded.Story!.Fleets);
        Assert.Equal(Allied.Dead, loaded.Story.Alliance("SPATHI_SHIP"));
        // A version 13 save from before the fleets: a new game's.
        var newGame = StoryState.NewGame(Rules, new GameClock(2155, 2, 17)).Fleets;
        node["story"]!.AsObject().Remove("fleets");
        Assert.Equal(newGame, HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes).Story!.Fleets);
        // An older save has none (a new game's), and one with them is refused.
        node["version"] = 12;
        node["story"]!.AsObject().Remove("set_aside");
        node.AsObject().Remove("encounters");   // the days' rolls' groups: saves before 13 have none
        Assert.Equal(newGame, HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes).Story!.Fleets);
        node["story"]!["fleets"] = JsonNode.Parse(json)!["story"]!["fleets"]!.DeepClone();
        Assert.Contains("fleets", Assert.Throws<InvalidDataException>(
            () => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes)).Message);
        // A bad one is refused.
        var bad = JsonNode.Parse(json)!;
        bad["story"]!["fleets"]!["SPATHI_SHIP"]!["days_left"] = 300;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(bad.ToJsonString(), systems, TestContent.Shapes));
        bad = JsonNode.Parse(json)!;
        bad["story"]!["fleets"]!.AsObject().Remove("VUX_SHIP");
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(bad.ToJsonString(), systems, TestContent.Shapes));
    }
}
