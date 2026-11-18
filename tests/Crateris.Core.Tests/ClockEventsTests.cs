// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Data;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The clock's events (docs/plans/stage-3b.md, item 1): each day the
/// events due run as UQM's processClockDayEvents runs them, the hyperspace
/// encounters and the Slylandro ramps; stage 3c's are set aside on their
/// day (decision 9); a save's events whose day has passed run on loading.
/// </summary>
[Trait("Topic", "story")]
public class ClockEventsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    private static GameWorld NewWorld(GameSettings? settings = null)
    {
        var clock = new GameClock(2155, 2, 17);
        return new GameWorld(settings ?? new GameSettings(), clock) { Story = StoryState.NewGame(Rules, clock) };
    }

    private static StoryEvent At(string evt, int y, int m, int d) => new(evt, y, m, d);

    private static (int, int, int) Date(GameClock c) => (c.Year, c.Month, c.Day);

    // ---- against UQM's own clock

    // tests/golden/clock-events.json: UQM's clock.c and gameev.c, day by
    // day, with the races' fleets (stage 3b item 2) after each step.
    [Fact]
    public void TheClockRunsItsEventsOnUqmsDays()
    {
        var golden = Golden.Load("clock-events");
        var names = Rules.EventIds.ToDictionary(e => e.Value, e => e.Key);
        StoryEvent Event(JsonElement e) => new(names[e[0].GetInt32()], e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32());
        foreach (var scenario in golden.GetProperty("scenarios").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var steps = scenario.GetProperty("steps").EnumerateArray().ToList();
            int at = 0;
            GameWorld? world = null;
            bool hyper = false;
            int setAsideBefore = 0, rollsBefore = 0;
            // UQM's fleets as the steps so far left them: each step has those that changed.
            var uqmFleets = new (Allied, RaceFleet)[Rules.Races.Count];

            void Check(List<StoryState.DueEvent> done, bool? tracked = null)
            {
                var step = steps[at++];
                string where = $"{name}, step {at - 1}";
                var story = world!.Story!;
                var d = step[0].Ints();
                Assert.True((d[0], d[1], d[2]) == Date(world.Clock), where);
                // The events UQM ran (at the queue's head, or due at once)
                // that the port runs: the same, in the same order, on the same days.
                var uqmRan = step[1].EnumerateArray()
                    .Select(r => (Event: names[r[0].GetInt32()], Depth: r[1].GetInt32(), Date: (r[2].GetInt32(), r[3].GetInt32(), r[4].GetInt32())))
                    .ToList();
                Assert.True(uqmRan.All(r => r.Depth == 0), where);   // nothing these scenarios run runs another at once
                var ran = done.Where(e => !e.SetAside).Select(e => (e.Event.Event, (e.Event.Year, e.Event.Month, e.Event.Day))).ToList();
                Assert.True(uqmRan.Where(r => StoryState.EventsRun.Contains(r.Event)).Select(r => (r.Event, r.Date)).SequenceEqual(ran),
                    $"{where}: ran {string.Join(", ", ran)}");
                // Those set aside are 3c's, each run by UQM on its day.
                var aside = done.Where(e => e.SetAside).Select(e => e.Event).ToList();
                Assert.Equal(aside, story.Events.SetAside.Skip(setAsideBefore));
                setAsideBefore = story.Events.SetAside.Count;
                foreach (var e in aside)
                {
                    Assert.DoesNotContain(e.Event, StoryState.EventsRun);
                    Assert.Contains((e.Event, (e.Year, e.Month, e.Day)), uqmRan.Select(r => (r.Event, r.Date)));
                }
                // check_hyperspace_encounter: called as each encounter event
                // runs, a draw if in hyperspace (the world's, for each player
                // there: item 3).
                int asked = world.HyperspaceEncounterDays - rollsBefore;
                rollsBefore = world.HyperspaceEncounterDays;
                Assert.Equal(ran.Count(e => e.Event == "HYPERSPACE_ENCOUNTER_EVENT"), asked);
                Assert.Equal(hyper ? asked : 0, step[2].GetInt32());
                Assert.True(step[3].GetInt32() == story.Get("SLYLANDRO_MULTIPLIER"), where);
                // The queue: the events the port runs as UQM's; 3c's still
                // queued as in UQM's (UQM's then queues what they run: the
                // Arilou's exits and returns, which the port's has none of).
                var queue = step[4].EnumerateArray().Select(Event).ToList();
                Assert.True(queue.Where(e => StoryState.EventsRun.Contains(e.Event))
                        .SequenceEqual(story.Events.Where(e => StoryState.EventsRun.Contains(e.Event))),
                    $"{where}: queued {string.Join(", ", story.Events)}");
                Assert.All(story.Events.Where(e => !StoryState.EventsRun.Contains(e.Event)), e => Assert.Contains(e, queue));
                // The fleets: every race's standing and FLEET_INFO as UQM's.
                foreach (var f in step[5].EnumerateArray().Select(f => f.Ints()))
                    uqmFleets[f[0]] = ((Allied)f[1], new RaceFleet(f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11],
                        f[12] == byte.MaxValue ? null : names[f[12]], f[13], f[14]));
                for (int r = 0; r < Rules.Races.Count; ++r)
                    Assert.True(uqmFleets[r] == (story.Alliances[r], story.Fleets[r]),
                        $"{where}: {Rules.Races[r]} is {story.Alliances[r]} {story.Fleets[r]}, UQM's {uqmFleets[r]}");
                // StartSphereTracking's result, UQM's race number or 0.
                Assert.Equal(step.GetArrayLength() > 6, tracked is not null);
                if (tracked is { } t)
                    Assert.True(t == (step[6].GetInt32() != 0), where);
            }

            foreach (string command in scenario.GetProperty("commands").EnumerateArray().Select(c => c.GetString()!))
            {
                var words = command.Split(' ');
                int n = words.Length > 1 ? int.Parse(words[1]) : 0;
                int[] a = words.Skip(2).Select(int.Parse).ToArray();
                switch (words[0])
                {
                    case "new":
                        world = NewWorld();
                        // AddInitialGameEvents: every event is UQM's, the ramp-up run at once (by AddEvent).
                        Assert.Equal(steps[at][4].EnumerateArray().Select(Event), world.Story!.Events);
                        Check(new() { new(At("SLYLANDRO_RAMP_UP", 2155, 2, 17), false) });
                        break;
                    case "day":
                        for (int i = 0; i < n; ++i)
                        {
                            world!.Clock.NextDay();
                            Check(world.RunDayEvents());
                        }
                        break;
                    case "move":
                        Check(world!.MoveClockDays(n));
                        break;
                    case "hyper":
                        hyper = n != 0;
                        break;
                    case "rampdown":
                    {
                        // slyhome.c, told of the self-destruct code.
                        var story = world!.Story!;
                        story.Set("DESTRUCT_CODE_ON_SHIP", 1);
                        story.Set("SLYLANDRO_MULTIPLIER", story.Get("SLYLANDRO_MULTIPLIER") + 1);
                        var before = story.Events.ToList();
                        Assert.False(story.AddEvent(true, 0, 0, 0, "SLYLANDRO_RAMP_DOWN", world.Clock));
                        // It ran at once: the port's ran list is what it queued.
                        Check(new() { new(At("SLYLANDRO_RAMP_DOWN", world.Clock.Year, world.Clock.Month, world.Clock.Day), false) });
                        Assert.Equal(before.Count + 1, story.Events.Count);
                        break;
                    }
                    case "dest":
                        // SetRaceDest, as 3c's events call it.
                        world!.Story!.SetRaceDest(Rules.Races[n], a[0], a[1], a[2], a[3] == byte.MaxValue ? null : names[a[3]]);
                        break;
                    case "growth":
                    {
                        // A fleet's growth, as 3c's events set it.
                        var story = world!.Story!;
                        story.SetFleet(Rules.Races[n], story.Fleets[n] with { Growth = a[0], GrowthFract = a[1], GrowthErrTerm = a[2] });
                        break;
                    }
                    case "track":
                        Check(new(), world!.Story!.StartSphereTracking(Rules.Races[n]));
                        break;
                    default:
                        throw new InvalidDataException($"no command {command}");
                }
            }
            Assert.Equal(steps.Count, at);
        }
    }

    // ---- the events stage 3b-1 runs

    [Fact]
    public void TheEncounterEventRunsEachDayAndQueuesItselfForTheNext()
    {
        var world = NewWorld();
        for (int i = 0; i < 40; ++i)
        {
            world.Clock.NextDay();
            var done = world.RunDayEvents();
            Assert.Contains(new StoryState.DueEvent(At("HYPERSPACE_ENCOUNTER_EVENT", world.Clock.Year, world.Clock.Month, world.Clock.Day), false), done);
            var next = new DateTime(world.Clock.Year, world.Clock.Month, world.Clock.Day).AddDays(1);
            Assert.Single(world.Story!.Events, At("HYPERSPACE_ENCOUNTER_EVENT", next.Year, next.Month, next.Day));
        }
        Assert.Equal(40, world.HyperspaceEncounterDays);
        // A day with nothing due runs nothing; the draws are asked for once a day.
        Assert.Empty(world.RunDayEvents());
        Assert.Equal(40, world.HyperspaceEncounterDays);
    }

    [Fact]
    public void TheSlylandroMultiplierRampsUpEvery182DaysToFour()
    {
        var world = NewWorld();
        var story = world.Story!;
        var rampedOn = new List<(int, int, int, int)>();
        for (int day = 0; day < 800; ++day)
        {
            world.Clock.NextDay();
            if (world.RunDayEvents().Any(e => e.Event.Event == "SLYLANDRO_RAMP_UP"))
                rampedOn.Add((world.Clock.Year, world.Clock.Month, world.Clock.Day, story.Get("SLYLANDRO_MULTIPLIER")));
        }
        // 1 at the start (at once); then 182 days apart, the fifth finding 4 and queuing no more.
        Assert.Equal(new[] { (2155, 8, 18, 2), (2156, 2, 16, 3), (2156, 8, 16, 4), (2157, 2, 14, 4) }, rampedOn);
        Assert.DoesNotContain(story.Events, e => e.Event == "SLYLANDRO_RAMP_UP");
    }

    [Fact]
    public void ARelativeEventDueNowRunsAtOnceAnd3csIsSetAsideToday()
    {
        var world = NewWorld();
        var story = world.Story!;
        world.MoveClockDays(10);
        Assert.False(story.AddEvent(true, 0, 0, 0, "SLYLANDRO_RAMP_UP", world.Clock));
        Assert.Equal(2, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Contains(At("SLYLANDRO_RAMP_UP", 2155, 8, 28), story.Events);   // 182 days from 27 February
        Assert.False(story.AddEvent(true, 0, 0, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        Assert.Equal(new[] { At("ZOQFOT_DISTRESS_EVENT", 2155, 2, 27) }, story.Events.SetAside);
        Assert.DoesNotContain(story.Events, e => e.Event == "ZOQFOT_DISTRESS_EVENT");
        Assert.Throws<KeyNotFoundException>(() => story.AddEvent(true, 0, 0, 0, "NO_SUCH_EVENT", world.Clock));
        // Run directly, one of 3c's is refused as before.
        Assert.Throws<NotSupportedException>(() => story.RunEvent("ZOQFOT_DISTRESS_EVENT", world.Clock));
    }

    // ---- 3c's events (decision 9)

    [Fact]
    public void ThreeCsEventsAreSetAsideOnTheirDayWithoutHoldingUpTheRest()
    {
        var world = NewWorld();
        var story = world.Story!;
        // Two of 3c's on one day, before the Arilou's, with an encounter event behind them.
        Assert.True(story.Events.Add(false, 3, 1, 2155, "ADVANCE_PKUNK_MISSION", world.Clock));
        Assert.True(story.Events.Add(false, 3, 1, 2155, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        var done = world.MoveClockDays(30);   // to 19 March
        Assert.Equal(new[]
        {
            At("ADVANCE_PKUNK_MISSION", 2155, 3, 1), At("ZOQFOT_DISTRESS_EVENT", 2155, 3, 1), At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17),
        }, story.Events.SetAside);
        Assert.Equal(story.Events.SetAside, done.Where(e => e.SetAside).Select(e => e.Event));
        // The encounters went on every day, those days too.
        Assert.Equal(30, done.Count(e => e.Event.Event == "HYPERSPACE_ENCOUNTER_EVENT" && !e.SetAside));
        Assert.Equal(30, world.HyperspaceEncounterDays);
        Assert.Equal(new[]
        {
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 3, 20), At("SLYLANDRO_RAMP_UP", 2155, 8, 18), At("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
        }, story.Events);

        // Kept, with their dates, through a save.
        var saved = story.Save();
        Assert.Equal(story.Events.SetAside, saved.SetAside);
        var restored = StoryState.Restore(Rules, JsonSerializer.Deserialize<StoryState.Saved>(JsonSerializer.Serialize(saved))!);
        Assert.Equal(story.Events.SetAside, restored.Events.SetAside);
        Assert.Equal(story.Events, restored.Events);
        // In date order, of events that exist, on days that exist.
        void Refused(Action<List<StoryEvent>> change)
        {
            var aside = saved.SetAside!.ToList();
            change(aside);
            Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, saved with { SetAside = aside }));
        }
        Refused(a => a.Reverse());
        Refused(a => a.Add(At("NO_SUCH_EVENT", 2156, 1, 1)));
        Refused(a => a.Add(At("ARILOU_EXIT_EVENT", 2155, 2, 30)));
        // None set aside in a save without them.
        Assert.Empty(StoryState.Restore(Rules, saved with { SetAside = null }).Events.SetAside);
    }

    // ---- saves

    [ContentFact]
    public void AHostSaveKeepsTheEventsSetAsideAndOlderOnesAreRefusedThem()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        world.MoveClockDays(40);   // past the Arilou's day
        Assert.Equal(new[] { At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17) }, world.Story!.Events.SetAside);
        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, systems, TestContent.Shapes);
        Assert.Equal(world.Story.Events.SetAside, loaded.Story!.Events.SetAside);
        Assert.Equal(world.Story.Events, loaded.Story.Events);
        Assert.Equal(Date(world.Clock), Date(loaded.Clock));
        var node = JsonNode.Parse(json)!;
        node["version"] = 12;
        node["story"]!.AsObject().Remove("fleets");   // saves before 13 have no races' fleets
        node.AsObject().Remove("encounters");   // nor hyperspace encounters (the days' rolls')
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes));
        node["story"]!.AsObject().Remove("set_aside");
        Assert.Empty(HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes).Story!.Events.SetAside);
    }

    [ContentFact]
    public void ASavesEventsWhoseDayHasPassedRunOnLoadingInDateOrder()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        // A version 12 save, which ran no events: its clock months past
        // the encounter event's day, the Arilou's and the ramp-up's.
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 12;
        node["story"]!.AsObject().Remove("set_aside");
        node["story"]!.AsObject().Remove("fleets");
        node["clock"]!["year"] = 2155;
        node["clock"]!["month"] = 9;
        node["clock"]!["day"] = 1;
        var loaded = HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes);
        var story = loaded.Story!;
        // Each ran once, at once, in date order: the encounter event queued
        // for the next day, the Arilou's set aside, the ramp-up run (2) and
        // queued 182 days from the day of loading.
        Assert.Equal(new[] { At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17) }, story.Events.SetAside);
        Assert.Equal(2, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Equal(new[]
        {
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 2), At("SLYLANDRO_RAMP_UP", 2156, 3, 1), At("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
        }, story.Events);
        Assert.Equal(1, loaded.HyperspaceEncounterDays);
        Assert.Equal((2155, 9, 1), Date(loaded.Clock));
        // A save made today has nothing due: nothing runs on loading it.
        var again = HostSave.FromJson(HostSave.ToJson(loaded), systems, TestContent.Shapes);
        Assert.Equal(story.Events, again.Story!.Events);
        Assert.Equal(0, again.HyperspaceEncounterDays);
    }

    // ---- the world's days

    [Fact]
    public void ADayPassingInTheWorldRunsItsEventsAndAPauseHoldsThem()
    {
        // A day a frame.
        var world = NewWorld(new GameSettings { SecondsPerGameDay = 1.0 / GameClock.FramesPerSecond });
        world.Step();
        Assert.Equal((2155, 2, 18), Date(world.Clock));
        Assert.Equal(1, world.HyperspaceEncounterDays);
        Assert.Equal(At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 2, 19), world.Story!.Events[0]);
        Assert.True(world.SetHostPause(true));
        for (int i = 0; i < 5; ++i)
            world.Step();
        Assert.Equal((2155, 2, 18), Date(world.Clock));
        Assert.Equal(1, world.HyperspaceEncounterDays);
        Assert.True(world.SetHostPause(false));
        for (int i = 0; i < 30; ++i)
            world.Step();
        Assert.Equal((2155, 3, 20), Date(world.Clock));
        Assert.Equal(31, world.HyperspaceEncounterDays);
        Assert.Equal(new[] { At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17) }, world.Story.Events.SetAside);
    }

    [Fact]
    public void SkippedDaysRunEachDaysEventsAndTheNextDayStartsAfresh()
    {
        var world = NewWorld();
        world.Clock.FramesIntoDay = 50;
        var done = world.MoveClockDays(StarbaseVisit.LostDays);
        Assert.Equal((2155, 3, 3), Date(world.Clock));
        Assert.Equal(0, world.Clock.FramesIntoDay);
        Assert.Equal(StarbaseVisit.LostDays, done.Count);
        Assert.All(done, e => Assert.Equal("HYPERSPACE_ENCOUNTER_EVENT", e.Event.Event));
        Assert.Equal(Enumerable.Range(18, 11).Select(d => (2155, 2, d)).Concat(Enumerable.Range(1, 3).Select(d => (2155, 3, d))),
            done.Select(e => (e.Event.Year, e.Event.Month, e.Event.Day)));
        Assert.Empty(world.MoveClockDays(0));
        // A world without a story has no events to run.
        var bare = new GameWorld(new GameSettings(), new GameClock(2155, 2, 17));
        Assert.Empty(bare.MoveClockDays(3));
        Assert.Equal((2155, 2, 20), Date(bare.Clock));
    }
}
