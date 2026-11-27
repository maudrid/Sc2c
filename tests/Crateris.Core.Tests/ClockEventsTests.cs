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
/// The clock's events (docs/plans/stage-3b.md, item 1; docs/plans/stage-3c.md,
/// item 1): each day the events due run as UQM's processClockDayEvents runs
/// them, the hyperspace encounters, the Slylandro ramps and since stage 3c
/// item 1 the events with no mission (each recorded from UQM's gameev.c,
/// the location gates with them), since item 3 the missions, each over
/// its whole course, and since item 4 the Kohr-Ah's victory and genocide
/// to the game over; a save's events whose day has passed, and those it
/// set aside (decision 9) until the port ran them, run on loading.
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

    // The story fields the missions and the Kohr-Ah read and set, as golden/clock.c prints them (its
    // MISSION_FIELDS, then its KOHR_AH_FIELDS).
    private static readonly string[] MissionFields =
    {
        "PKUNK_MISSION", "PKUNK_ON_THE_MOVE", "PKUNK_SWITCH", "YEHAT_ABSORBED_PKUNK", "THRADD_MISSION",
        "ILWRATH_FIGHT_THRADDASH", "HELIX_UNPROTECTED", "THRADD_VISITS", "MYCON_KNOW_AMBUSH", "UTWIG_SUPOX_MISSION",
        "UTWIG_VISITS", "UTWIG_INFO", "SUPOX_VISITS", "SUPOX_INFO", "UTWIG_WAR_NEWS", "SUPOX_WAR_NEWS",
        "KOHR_AH_FRENZY", "KOHR_AH_VISITS", "KOHR_AH_REASONS", "KOHR_AH_PLEAD", "KOHR_AH_INFO", "URQUAN_VISITS",
        "KOHR_AH_KILLED_ALL",
    };

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
            int rollsBefore = 0;
            string? inSystem = null;   // the special system UQM's flagship is in (the location gates)
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
                // Those run at once from within another (the Thraddash home
                // running the Ilwrath's mission) and a fleet's arrival (which
                // UQM's recording doesn't list) are seen in what they did.
                var ran = done.Where(e => !e.Arrival).Select(e => (e.Event.Event, (e.Event.Year, e.Event.Month, e.Event.Day))).ToList();
                Assert.True(uqmRan.Where(r => r.Depth == 0).Select(r => (r.Event, r.Date)).SequenceEqual(ran),
                    $"{where}: ran {string.Join(", ", ran)}");
                // Since stage 3c item 4 the port runs every event: none is set aside.
                Assert.Empty(story.Events.SetAside);
                // check_hyperspace_encounter: called as each encounter event
                // runs, a draw if in hyperspace (the world's, for each player
                // there: item 3).
                int asked = world.HyperspaceEncounterDays - rollsBefore;
                rollsBefore = world.HyperspaceEncounterDays;
                Assert.Equal(ran.Count(e => e.Event == "HYPERSPACE_ENCOUNTER_EVENT"), asked);
                Assert.Equal(hyper ? asked : 0, step[2].GetInt32());
                Assert.True(step[3].GetInt32() == story.Get("SLYLANDRO_MULTIPLIER"), where);
                // The queue: UQM's.
                var queue = step[4].EnumerateArray().Select(Event).ToList();
                Assert.True(queue.SequenceEqual(story.Events), $"{where}: queued {string.Join(", ", story.Events)}");
                // The fleets: every race's standing and FLEET_INFO as UQM's.
                foreach (var f in step[5].EnumerateArray().Select(f => f.Ints()))
                    uqmFleets[f[0]] = ((Allied)f[1], new RaceFleet(f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11],
                        f[12] == byte.MaxValue ? null : names[f[12]], f[13], f[14]));
                for (int r = 0; r < Rules.Races.Count; ++r)
                    Assert.True(uqmFleets[r] == (story.Alliances[r], story.Fleets[r]),
                        $"{where}: {Rules.Races[r]} is {story.Alliances[r]} {story.Fleets[r]}, UQM's {uqmFleets[r]}");
                // The story the events set: the Arilou's portal and look at the Umgah, the Zoq-Fot-Pik's
                // distress, the Spathi's shield, the crew bought and its price, the missions' fields and
                // the Kohr-Ah's; and the game over (CrewEnlisted ~0), the world's KohrAhVictorious.
                Assert.True(step[6].Ints().SequenceEqual(new[]
                {
                    story.Get("ARILOU_SPACE"), story.Get("ARILOU_CHECKED_UMGAH"), story.Get("ZOQFOT_DISTRESS"),
                    story.Get("SPATHI_SHIELDED_SELVES"), story.Get("CREW_PURCHASED0"), story.Get("CREW_PURCHASED1"), story.CrewCost,
                }.Concat(MissionFields.Select(story.Get)).Append(world.KohrAhVictorious ? 1 : 0)),
                    $"{where}: the story's {string.Join(",", step[6].Ints())}");
                // StartSphereTracking's result, UQM's race number or 0.
                Assert.Equal(step.GetArrayLength() > 7, tracked is not null);
                if (tracked is { } t)
                    Assert.True(t == (step[7].GetInt32() != 0), where);
            }

            foreach (string command in scenario.GetProperty("commands").EnumerateArray().Select(c => c.GetString()!))
            {
                var words = command.Split(' ');
                int n = words.Length > 1 && words[0] != "state" ? int.Parse(words[1]) : 0;
                int[] a = words[0] == "state" ? Array.Empty<int>() : words.Skip(2).Select(int.Parse).ToArray();
                switch (words[0])
                {
                    case "new":
                        world = NewWorld();
                        world.InSystemForTests = special => special == inSystem;
                        // AddInitialGameEvents: every event is UQM's, the ramp-up run at once (by AddEvent).
                        Assert.Equal(steps[at][4].EnumerateArray().Select(Event), world.Story!.Events);
                        Check(new() { new(At("SLYLANDRO_RAMP_UP", 2155, 2, 17)) });
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
                        inSystem = null;
                        break;
                    case "system":
                        // UQM's flagship in a special system (gendef.h's number): the location gates hold its race's events.
                        hyper = false;
                        inSystem = n switch
                        {
                            0 => null, 5 => "SPATHI_DEFINED", 6 => "ZOQFOT_DEFINED", 29 => "SAMATRA_DEFINED",
                            _ => throw new InvalidDataException($"system {n}"),
                        };
                        break;
                    case "add":
                    {
                        // AddEvent, as a talk calls it: queued, or run at once.
                        var story = world!.Story!;
                        string evt = names[a[3]];
                        bool atOnce = n != 0 && a[0] == 0 && a[1] == 0 && a[2] == 0;
                        story.AddEvent(n != 0, a[0], a[1], a[2], evt, world.Clock, null, special => world.PlayerInSystem(special));
                        Check(atOnce ? new() { new(At(evt, world.Clock.Year, world.Clock.Month, world.Clock.Day)) } : new());
                        break;
                    }
                    case "strength":
                    {
                        var story = world!.Story!;
                        story.SetFleet(Rules.Races[n], story.Fleets[n] with { ActualStrength = a[0] });
                        break;
                    }
                    case "ally":
                        world!.Story!.SetRaceAllied(Rules.Races[n], a[0] != 0);
                        break;
                    case "crew":
                    {
                        // The crew bought and its price, as the starbase's trade leaves them.
                        var story = world!.Story!;
                        story.Set("CREW_PURCHASED0", n & 0xFF);
                        story.Set("CREW_PURCHASED1", n >> 8);
                        story.SetCrewCost(a[0]);
                        break;
                    }
                    case "distress":
                        // The Zoq-Fot-Pik's thanks (zoqfotc.c), rescued.
                        world!.Story!.Set("ZOQFOT_DISTRESS", n);
                        break;
                    case "state":
                        // A mission's field or the Kohr-Ah's, as a talk or --start-state sets it.
                        Assert.Contains(words[1], MissionFields);
                        world!.Story!.Set(words[1], int.Parse(words[2]));
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
                        Check(new() { new(At("SLYLANDRO_RAMP_DOWN", world.Clock.Year, world.Clock.Month, world.Clock.Day)) });
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

    // ---- the events the port runs

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
    public void ARelativeEventDueNowRunsAtOnceTheKohrAhsToo()
    {
        var world = NewWorld();
        var story = world.Story!;
        world.MoveClockDays(10);
        Assert.False(story.AddEvent(true, 0, 0, 0, "SLYLANDRO_RAMP_UP", world.Clock));
        Assert.Equal(2, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Contains(At("SLYLANDRO_RAMP_UP", 2155, 8, 28), story.Events);   // 182 days from 27 February
        // The genocide at once (stage 3c item 4): the frenzy, the Kohr-Ah off to the Druuge first, to run it again there.
        Assert.False(story.AddEvent(true, 0, 0, 0, "KOHR_AH_GENOCIDE_EVENT", world.Clock));
        Assert.Equal(1, story.Get("KOHR_AH_FRENZY"));
        var kohrAh = story.Fleet("BLACK_URQUAN_SHIP");
        var druuge = story.Fleet("DRUUGE_SHIP");
        Assert.Equal((druuge.LocX, druuge.LocY, "KOHR_AH_GENOCIDE_EVENT"), (kohrAh.DestLocX, kohrAh.DestLocY, kohrAh.Arrival));
        Assert.Empty(story.Events.SetAside);
        Assert.Throws<KeyNotFoundException>(() => story.AddEvent(true, 0, 0, 0, "NO_SUCH_EVENT", world.Clock));
        Assert.Throws<KeyNotFoundException>(() => story.RunEvent("NO_SUCH_EVENT", world.Clock));
        // A mission's runs at once since stage 3c item 3 (the Pkunk set off).
        Assert.False(story.AddEvent(true, 0, 0, 0, "ADVANCE_PKUNK_MISSION", world.Clock));
        Assert.Equal(1, story.Get("PKUNK_MISSION"));
        Assert.Equal(StoryState.EventsRun.ToHashSet(), Rules.EventIds.Keys.ToHashSet());   // every event gameev.h has
    }

    // ---- the events a save set aside (decision 9): run from where the clock is

    // A new game's story as saved, with these events set aside (as a save
    // from before stage 3c item 4 holds the Kohr-Ah's).
    private static StoryState.Saved SavedWithSetAside(GameWorld world, params StoryEvent[] aside) =>
        world.Story!.Save() with { SetAside = aside.ToList() };

    [Fact]
    public void ASavesEventsSetAsideRunOnceFromWhereTheClockIsInDateOrder()
    {
        var world = NewWorld();
        world.MoveClockDays(30);   // to 19 March, the Arilou's portal open
        // A save's Kohr-Ah events set aside, two on one day, before the queue's events of the day.
        var saved = SavedWithSetAside(world, At("KOHR_AH_GENOCIDE_EVENT", 2155, 3, 1), At("KOHR_AH_VICTORIOUS_EVENT", 2155, 3, 1),
            At("KOHR_AH_GENOCIDE_EVENT", 2155, 3, 5));
        // Kept, with their dates, through a save.
        var story = StoryState.Restore(Rules, JsonSerializer.Deserialize<StoryState.Saved>(JsonSerializer.Serialize(saved))!);
        Assert.Equal(saved.SetAside, story.Events.SetAside);
        Assert.Equal(saved.Events, story.Events.ToList());
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
        Assert.Empty(StoryState.Restore(Rules, saved with { SetAside = null }).Events.SetAside);

        // Run on the clock's day, each once, in date order, before the queue's
        // events of today (they fell due first): the first genocide begins the
        // frenzy, heading for the Druuge; the victory falls through to the
        // genocide again (from where the Kohr-Ah are now: still the Druuge);
        // the third the same. Their days don't come again.
        world.Story = story;
        world.Clock.NextDay();
        var done = world.RunDayEvents();
        Assert.Equal(new[] { At("KOHR_AH_GENOCIDE_EVENT", 2155, 3, 1), At("KOHR_AH_VICTORIOUS_EVENT", 2155, 3, 1), At("KOHR_AH_GENOCIDE_EVENT", 2155, 3, 5) },
            done.Take(3).Select(e => e.Event));
        Assert.Equal(new[] { "ARILOU_EXIT_EVENT", "HYPERSPACE_ENCOUNTER_EVENT" }, done.Skip(3).Select(e => e.Event.Event));
        Assert.All(done.Take(3), e => Assert.True(e.News && !e.Held && !e.Arrival));   // each a new course, told
        Assert.Empty(story.Events.SetAside);
        Assert.Equal(1, story.Get("KOHR_AH_FRENZY"));
        var kohrAh = story.Fleet("BLACK_URQUAN_SHIP");
        Assert.Equal((9500, 2792), (kohrAh.DestLocX, kohrAh.DestLocY));
        world.Clock.NextDay();
        Assert.DoesNotContain(world.RunDayEvents(), e => e.Event.Event.StartsWith("KOHR_AH", StringComparison.Ordinal));
    }

    // ---- saves

    [ContentFact]
    public void AHostSavesEventsSetAsideRunAsItLoadsAndOlderOnesAreRefusedThem()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        world.MoveClockDays(40);
        // A save made before stage 3c item 4, its genocide set aside on 1 March.
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["story"]!["set_aside"] = JsonNode.Parse("""[{"event":"KOHR_AH_GENOCIDE_EVENT","year":2155,"month":3,"day":1}]""");
        var loaded = HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes);
        // Run as it loads, once, from where the clock is: the frenzy, the Kohr-Ah heading for the Druuge.
        Assert.Empty(loaded.Story!.Events.SetAside);
        Assert.Equal(1, loaded.Story.Get("KOHR_AH_FRENZY"));
        Assert.Equal("KOHR_AH_GENOCIDE_EVENT", loaded.Story.Fleet("BLACK_URQUAN_SHIP").Arrival);
        Assert.Equal(Date(world.Clock), Date(loaded.Clock));
        Assert.Null(JsonNode.Parse(HostSave.ToJson(loaded))!["story"]!["set_aside"]?.AsArray().FirstOrDefault());
        // Saves before 13 had none.
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
        // for the next day, the Arilou's portal opened (its exit queued 3
        // days from the day of loading), the ramp-up run (2) and queued 182
        // days from the day of loading.
        Assert.Empty(story.Events.SetAside);
        Assert.Equal(2, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Equal(StoryState.ArilouOpening, story.Get("ARILOU_SPACE"));
        Assert.Equal(new[]
        {
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 2), At("ARILOU_EXIT_EVENT", 2155, 9, 4), At("SLYLANDRO_RAMP_UP", 2156, 3, 1),
            At("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
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
        Assert.Empty(world.Story.Events.SetAside);
        Assert.Equal(StoryState.ArilouClosing, world.Story.Get("ARILOU_SPACE"));   // opened on the 17th, closed on the 20th
        Assert.Contains(At("ARILOU_ENTRANCE_EVENT", 2155, 4, 17), world.Story.Events);
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
