// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The calendar's engine and the events with no mission in the world
/// (docs/plans/stage-3c.md, item 1); what each event does on its own is
/// checked against UQM's gameev.c in ClockEventsTests. Here: the events set
/// aside before 3c run once, in date order, from where the clock is; the
/// location gates hold an event while any player is in its race's system
/// (question 1); the events run on time while their race is met (question
/// 2); every player is told of an event that changes a race; the
/// Kohr-Ah's ship at the Zoq-Fot-Pik's world in their distress; the
/// Arilou's portal counter; the Yehat's rebellion check; --start-date; and
/// what an event changes taken back whole.
/// </summary>
[Trait("Topic", "story")]
public class CalendarEventsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;

    private static StoryEvent At(string evt, int y, int m, int d) => new(evt, y, m, d);

    private static GameWorld BareWorld(int y = 2155, int m = 2, int d = 17)
    {
        var clock = new GameClock(y, m, d);
        return new GameWorld(new GameSettings(), clock) { Story = StoryState.NewGame(Rules, clock) };
    }

    private static GameWorld NewGame(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.BattleKit = WorldBattleTests.Kit();
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        return world;
    }

    private static int Star(GameWorld world, string special) =>
        world.Systems!.Map.Stars.Single(s => s.Special == special).Number;

    private static void Frames(GameWorld world, int frames, ShipInput input = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, input);
            world.Step();
        }
    }

    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 600 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    // A group of a race brought to the player in a system of no groups of its own, and met.
    private static Meeting MeetInSystem(GameWorld world, string id, string race, int ships = 1)
    {
        world.PutInSystem(id, GroupStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[GroupStar].Groups.Clear();
        world.AddEncounterNear(id, world.Story!.Rules.RaceIndex(race), ships);
        return Meet(world, id);
    }

    private static void Retreat(GameWorld world, string id)
    {
        for (int i = 0; i < 600 && world.Find(id)!.Ship.Battle is not null; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? ShipInput.TakeOff : ShipInput.None);
            world.Step();
        }
        Assert.Null(world.Find(id)!.Ship.Battle);
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The next day, as the world's clock passes it.
    private static List<StoryState.DueEvent> NextDay(GameWorld world)
    {
        world.Clock.NextDay();
        return world.RunDayEvents();
    }

    // ---- the events set aside before stage 3c

    [Fact]
    public void TheEventsSetAsideRunOnceInDateOrderFromWhereTheClockIs()
    {
        // A game whose clock stands on 1 September 2155, with the events an
        // earlier stage's code set aside on their days: the Arilou's
        // entrance, the Zoq-Fot-Pik's distress, the Pkunk's mission (run
        // since stage 3c item 3), the Kohr-Ah's genocide (still set aside:
        // item 4's), the Shofixti's return.
        var world = BareWorld(2155, 9, 1);
        var story = world.Story!;
        var queue = new[]
        {
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 1), At("SLYLANDRO_RAMP_UP", 2156, 2, 16), At("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
        };
        var genocide = At("KOHR_AH_GENOCIDE_EVENT", 2155, 4, 3);
        story.Events.Restore(queue, new[]
        {
            At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17), At("ZOQFOT_DISTRESS_EVENT", 2155, 4, 1), At("ADVANCE_PKUNK_MISSION", 2155, 4, 2),
            genocide, At("SHOFIXTI_RETURN_EVENT", 2155, 4, 5),
        });
        var done = world.RunDayEvents();
        // Each run once, in date order, before today's queued event; the Kohr-Ah's left aside.
        Assert.Equal(new[]
        {
            new StoryState.DueEvent(At("ARILOU_ENTRANCE_EVENT", 2155, 3, 17), false),
            new StoryState.DueEvent(At("ZOQFOT_DISTRESS_EVENT", 2155, 4, 1), false, true),
            new StoryState.DueEvent(At("ADVANCE_PKUNK_MISSION", 2155, 4, 2), false, true),
            new StoryState.DueEvent(At("SHOFIXTI_RETURN_EVENT", 2155, 4, 5), false, true),
            new StoryState.DueEvent(At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 1), false),
        }, done);
        Assert.Equal(new[] { genocide }, story.Events.SetAside);
        // What they queue counted from today: the portal's exit 3 days on, the death six months on; the
        // Pkunk set off today, half a year from the Yehat (a day of it gone with today's fleets).
        Assert.Equal(new[]
        {
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 2), At("ARILOU_EXIT_EVENT", 2155, 9, 4), At("SLYLANDRO_RAMP_UP", 2156, 2, 16),
            At("ZOQFOT_DEATH_EVENT", 2156, 3, 1), At("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
        }, story.Events);
        Assert.Equal((StoryState.ArilouOpening, 1, Allied.Good), (story.Get("ARILOU_SPACE"), story.Get("ZOQFOT_DISTRESS"), story.Alliance("SHOFIXTI_SHIP")));
        Assert.Equal((1, 181, "ADVANCE_PKUNK_MISSION"), (story.Get("PKUNK_MISSION"), story.Fleet("PKUNK_SHIP").DaysLeft, story.Fleet("PKUNK_SHIP").Arrival));
        // None runs twice: nothing more today, and the Kohr-Ah's stays aside.
        Assert.Empty(world.RunDayEvents());
        Assert.Equal(new[] { genocide }, story.Events.SetAside);
        Assert.Equal(1, story.Get("PKUNK_MISSION"));
        Assert.Equal(1, story.CrewCost);   // 3, less 2 once
    }

    [Fact]
    public void ASetAsideEventRunsBeforeAQueuedOneOfItsDayAndAfterAnEarlierOne()
    {
        // Reading (c): a set-aside event fell due before a queued one of its
        // day (it was taken off the queue first), so it runs first; a queued
        // one of an earlier day (a save's from before 3b-1) runs before it.
        var world = BareWorld(2155, 9, 1);
        var story = world.Story!;
        story.Events.Restore(new[]
        {
            At("ARILOU_UMGAH_CHECK", 2155, 8, 15), At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 1), At("ARILOU_ENTRANCE_EVENT", 2155, 9, 1),
        }, new[] { At("SHOFIXTI_RETURN_EVENT", 2155, 8, 20), At("ZOQFOT_DISTRESS_EVENT", 2155, 9, 1) });
        Assert.Equal(new[]
        {
            At("ARILOU_UMGAH_CHECK", 2155, 8, 15), At("SHOFIXTI_RETURN_EVENT", 2155, 8, 20), At("ZOQFOT_DISTRESS_EVENT", 2155, 9, 1),
            At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 1), At("ARILOU_ENTRANCE_EVENT", 2155, 9, 1),
        }, world.RunDayEvents().Select(e => e.Event));
        Assert.Empty(story.Events.SetAside);
    }

    [Fact]
    public void ASetAsideEventItsGateHoldsIsQueuedAgainSevenDaysFromToday()
    {
        var world = BareWorld(2155, 9, 1);
        var story = world.Story!;
        story.Events.Restore(new[] { At("HYPERSPACE_ENCOUNTER_EVENT", 2155, 9, 1) },
            new[] { At("SPATHI_SHIELD_EVENT", 2155, 5, 1) });
        world.InSystemForTests = special => special == "SPATHI_DEFINED";
        var done = world.RunDayEvents();
        Assert.Equal(new StoryState.DueEvent(At("SPATHI_SHIELD_EVENT", 2155, 5, 1), false, false, true), done[0]);
        Assert.Empty(story.Events.SetAside);
        Assert.Contains(At("SPATHI_SHIELD_EVENT", 2155, 9, 8), story.Events);
        Assert.Equal(0, story.Get("SPATHI_SHIELDED_SELVES"));
        // Once nobody's there, on its day.
        world.InSystemForTests = _ => false;
        for (int i = 0; i < 7; ++i)
            NextDay(world);
        Assert.Equal(1, story.Get("SPATHI_SHIELDED_SELVES"));
        Assert.Equal(0, story.Fleet("SPATHI_SHIP").ActualStrength);
    }

    [ContentFact]
    public void ASaveFromBeforeStage3cRunsItsSetAsideEventsOnLoading()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        world.MoveClockDays(60);   // to 18 April 2155
        // As 3b-2's code saved it: the Arilou's entrance and the Shofixti's return set aside on their days, the
        // Arilou's cycle not begun.
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        var story = node["story"]!;
        story["fields"]!.AsObject().Remove("ARILOU_SPACE");
        story["events"] = new JsonArray(story["events"]!.AsArray().Where(e => !e!["event"]!.GetValue<string>().StartsWith("ARILOU", StringComparison.Ordinal))
            .Select(e => e!.DeepClone()).ToArray());
        story["set_aside"] = JsonNode.Parse("""
            [{"event":"ARILOU_ENTRANCE_EVENT","year":2155,"month":3,"day":17},{"event":"SHOFIXTI_RETURN_EVENT","year":2155,"month":4,"day":2}]
            """);
        var loaded = HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes);
        var s = loaded.Story!;
        Assert.Empty(s.Events.SetAside);
        Assert.Equal(StoryState.ArilouOpening, s.Get("ARILOU_SPACE"));
        Assert.Contains(At("ARILOU_EXIT_EVENT", 2155, 4, 21), s.Events);
        Assert.Equal((Allied.Good, 1), (s.Alliance("SHOFIXTI_SHIP"), s.CrewCost));
        // Nobody is in the game yet as it loads, so nobody is told (as a player away isn't).
        Assert.Empty(loaded.TakeNotices());
        // Saved again, nothing runs twice.
        var again = HostSave.FromJson(HostSave.ToJson(loaded), systems, TestContent.Shapes);
        Assert.Equal(s.Events, again.Story!.Events);
        Assert.Equal(1, again.Story.CrewCost);
        Assert.Empty(again.TakeNotices());
    }

    [ContentFact]
    public void ASavesEventsHeldAsItLoadsByASavedPlayerInTheRacesSystem()
    {
        // Nobody is in the game as a save loads: the gates ask where every
        // saved player's ship is (a guest's too, though away).
        var world = NewGame("g");
        var story = world.Story!;
        int zoq = Star(world, "ZOQFOT_DEFINED");
        Assert.True(story.Events.Add(true, 0, 1, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        world.PutInSystem("g", zoq);
        Frames(world, 1);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["clock"]!["day"] = world.Clock.Day + 3;   // the distress's day passed
        var loaded = HostSave.FromJson(node.ToJsonString(), world.Systems!, TestContent.Shapes);
        Assert.Equal(0, loaded.Story!.Get("ZOQFOT_DISTRESS"));
        var again = Assert.Single(loaded.Story.Events, e => e.Event == "ZOQFOT_DISTRESS_EVENT");
        Assert.Equal((loaded.Clock.Month, loaded.Clock.Day + 7), (again.Month, again.Day));
        // A saved wreck there doesn't hold it, as a wreck in the game doesn't.
        world.Find("g")!.Ship.Destroyed = true;
        node = JsonNode.Parse(HostSave.ToJson(world))!;
        Assert.True(node["players"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "g")!["wrecked"]!.GetValue<bool>());
        node["clock"]!["day"] = world.Clock.Day + 3;
        var fromWreck = HostSave.FromJson(node.ToJsonString(), world.Systems!, TestContent.Shapes);
        Assert.True(fromWreck.Find("g")!.Ship.Destroyed);
        Assert.Equal(1, fromWreck.Story!.Get("ZOQFOT_DISTRESS"));
        world.Find("g")!.Ship.Destroyed = false;
        // The guest elsewhere: it runs as the save loads.
        world.PutInHyperspace("g");
        Frames(world, 1);
        node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["clock"]!["day"] = world.Clock.Day + 3;
        Assert.Equal(1, HostSave.FromJson(node.ToJsonString(), world.Systems!, TestContent.Shapes).Story!.Get("ZOQFOT_DISTRESS"));
    }

    // ---- the location gates (question 1)

    [ContentFact]
    public void AnEventWaitsWhileAnyPlayerIsInItsRacesSystem()
    {
        var world = NewGame("g", "x");
        var story = world.Story!;
        int zoq = Star(world, "ZOQFOT_DEFINED");
        // The distress due tomorrow; the host flying in the Zoq-Fot-Pik's system, the guests in hyperspace.
        Assert.True(story.Events.Add(true, 0, 1, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        world.PutInSystem("h", zoq);
        Frames(world, 1);
        Assert.True(world.PlayerInSystem("ZOQFOT_DEFINED"));
        Assert.False(world.PlayerInSystem("SPATHI_DEFINED"));
        var done = NextDay(world);
        Assert.Contains(done, e => e.Event.Event == "ZOQFOT_DISTRESS_EVENT" && !e.News);
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
        var again = Assert.Single(story.Events, e => e.Event == "ZOQFOT_DISTRESS_EVENT");
        // Held 7 days at a time while any player is there: a guest in orbit of another world there while the host
        // is gone; a guest in the Zoq-Fot-Pik's talk at their world.
        world.PutInHyperspace("h");
        world.PutInOrbit("g", zoq, 1);
        Assert.Equal(Activity.Orbit, world.Find("g")!.Ship.Activity);
        for (int i = 0; i < 7; ++i)
            NextDay(world);
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
        world.PutInHyperspace("g");
        Frames(world, 1);
        world.PutInOrbit("x", zoq, 0);
        Assert.Equal(Activity.Talk, world.Find("x")!.Ship.Activity);
        for (int i = 0; i < 7; ++i)
            NextDay(world);
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
        // A player away doesn't hold it (their ship stays where it was), nor does a wreck.
        world.Leave("x");
        Assert.False(world.PlayerInSystem("ZOQFOT_DEFINED"));
        world.PutInSystem("g", zoq);
        world.Find("g")!.Ship.Destroyed = true;
        Assert.False(world.PlayerInSystem("ZOQFOT_DEFINED"));
        for (int i = 0; i < 7; ++i)
            NextDay(world);
        Assert.Equal(1, story.Get("ZOQFOT_DISTRESS"));
        Assert.Contains(story.Events, e => e.Event == "ZOQFOT_DEATH_EVENT");
        Assert.NotEqual(again, Assert.Single(story.Events, e => e.Event == "ZOQFOT_DEATH_EVENT"));
    }

    [ContentFact]
    public void ALandedPlayerHoldsTheEventToo()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int spathi = Star(world, "SPATHI_DEFINED");
        story.SetRaceAllied("SPATHI_SHIP", true);
        Assert.True(story.Events.Add(true, 0, 1, 0, "SPATHI_SHIELD_EVENT", world.Clock));
        // Spathiwa itself (the moon's talk is the Spathi's): its lander down on the planet.
        world.PutInOrbit("g", spathi, 0);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("g", 500, 150));
        Frames(world, 60);
        Assert.Equal(Activity.Planetside, world.Find("g")!.Ship.Activity);
        NextDay(world);
        Assert.Equal((0, Allied.Good), (story.Get("SPATHI_SHIELDED_SELVES"), story.Alliance("SPATHI_SHIP")));
        Assert.Contains(story.Events, e => e.Event == "SPATHI_SHIELD_EVENT");
    }

    // ---- notices

    [ContentFact]
    public void EveryPlayerPresentIsToldOfAnEventThatChangesARace()
    {
        var world = NewGame("g", "away");
        var story = world.Story!;
        world.Leave("away");
        world.TakeNotices();
        story.SetRaceAllied("SPATHI_SHIP", true);
        Assert.True(story.Events.Add(true, 0, 1, 0, "SPATHI_SHIELD_EVENT", world.Clock));
        Assert.True(story.Events.Add(true, 0, 1, 0, "ARILOU_UMGAH_CHECK", world.Clock));
        NextDay(world);
        // The shield, told to the host and the guest (not the player away); the Arilou's look changes no race.
        var notices = world.TakeNotices();
        Assert.Equal(new[] { "g", "h" }, notices.Select(n => n.PlayerId).OrderBy(p => p));
        Assert.All(notices, n => Assert.Equal((NoticeEvent.Shielded, "Spathi"), (n.Event, n.Race)));
        Assert.Equal("The Spathi have shielded their world: they're no longer allies, and their ships are gone.", notices[0].Text);
        // The Zoq-Fot-Pik's distress and death.
        Assert.True(story.Events.Add(true, 0, 1, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        NextDay(world);
        Assert.All(world.TakeNotices(), n => Assert.Equal(NoticeEvent.Distress, n.Event));
        // The Shofixti's return, with the crew's price.
        Assert.True(story.Events.Add(true, 0, 1, 0, "SHOFIXTI_RETURN_EVENT", world.Clock));
        NextDay(world);
        var back = Assert.Single(world.TakeNotices(), n => n.PlayerId == "h");
        Assert.Equal("The Shofixti are back and allied: their ships are for sale, and crew cost 1 RU each at the starbase now.", back.Text);
        Assert.Equal("The Zoq-Fot-Pik are in distress: the Kohr-Ah are attacking their world.",
            new WorldNotice("h", NoticeEvent.Distress, "Someone", "Zoq-Fot-Pik").Text);
        world.MoveClockDays(200);
        var death = world.TakeNotices().Where(n => n.Event == NoticeEvent.Extinct).ToList();
        Assert.Equal(2, death.Count);
        Assert.Equal("The Zoq-Fot-Pik have been destroyed.", death[0].Text);
        Assert.Equal((Allied.Dead, 2), (story.Alliance("ZOQFOTPIK_SHIP"), story.Get("ZOQFOT_DISTRESS")));
        // An event that changes nothing (the Spathi gone already) tells nothing.
        Assert.True(story.Events.Add(true, 0, 1, 0, "SPATHI_SHIELD_EVENT", world.Clock));
        NextDay(world);
        Assert.Empty(world.TakeNotices());
    }

    // ---- events while their race is met (question 2)

    [ContentFact]
    public void AMeetingWithARaceTheCalendarChangesGoesOnAsItBegan()
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.SetRaceAllied("SPATHI_SHIP", true);
        var m = MeetInSystem(world, "g", "SPATHI_SHIP", ships: 2);
        // The shield runs on time, the guest's meeting with the Spathi elsewhere going on.
        Assert.True(story.Events.Add(true, 0, 1, 0, "SPATHI_SHIELD_EVENT", world.Clock));
        NextDay(world);
        Assert.Equal((1, 0, Allied.Bad), (story.Get("SPATHI_SHIELDED_SELVES"), story.Fleet("SPATHI_SHIP").ActualStrength, story.Alliance("SPATHI_SHIP")));
        Assert.Same(m, world.MeetingOf("g"));
        // Its battle fought as begun, against the group's ships.
        Assert.True(world.EncounterChoice("g", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.All(m.Battle!.EnemyFleet.Ships, s => Assert.Equal("spathi", s.Ship));
        Retreat(world, "g");
        Assert.Null(world.MeetingOf("g"));
    }

    // ---- the Zoq-Fot-Pik in distress (genzoq.c)

    private static GameWorld InDistress(out int star)
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("MET_ZOQFOT", 1);
        story.SetRaceAllied("ZOQFOTPIK_SHIP", true);
        Assert.True(story.Events.Add(true, 0, 1, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        NextDay(world);
        Assert.Equal(1, story.Get("ZOQFOT_DISTRESS"));
        world.TakeNotices();
        star = Star(world, "ZOQFOT_DEFINED");
        return world;
    }

    [ContentFact]
    public void InDistressTheKohrAhsShipAtTheirWorldComesFirstAndTheirThanksFollowItsDefeat()
    {
        var world = InDistress(out int star);
        var story = world.Story!;
        // No groups in their system meanwhile (GenerateZoqFotPik_initNpcs).
        world.PutInSystem("g", star);
        Frames(world, 1);
        Assert.Empty(world.SystemGroups!.Systems[star].Groups);
        // Orbiting their world: the Kohr-Ah's one ship, hostile, the Zoq-Fot-Pik's sphere tracked.
        world.PutInOrbit("g", star, 0);
        var m = world.MeetingOf("g")!;
        Assert.Equal(story.Rules.RaceIndex("BLACK_URQUAN_SHIP"), m.Race);
        Assert.IsType<Comm.Scripts.BlackUrquanScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.Single(m.Ships);
        Assert.False(m.Infinite);
        Assert.NotEqual(0, story.Fleet("ZOQFOTPIK_SHIP").KnownStrength);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.Equal("g", world.DialogueLocks!.Holder("BLACKURQ_CONVERSATION"));
        Assert.True(world.EncounterChoice("g", EncounterAction.Attack));
        WorldTalkTests.WinTheBattle(world, m);
        for (int i = 0; i < 10 && world.MeetingOf("g") is null or { Step: MeetingStep.Battle }; ++i)
            Frames(world, 1);
        // Destroyed: the Zoq-Fot-Pik's talk follows at once, the ship still by the world, their thanks and ships.
        var zfp = world.MeetingOf("g")!;
        Assert.NotSame(m, zfp);
        Assert.Equal(story.Rules.RaceIndex("ZOQFOTPIK_SHIP"), zfp.Race);
        Assert.True(Said(zfp.Talk!, "THANKS_FOR_RESCUE1"));
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
        Assert.True(world.Fleet.CountOf("ZOQFOTPIK_SHIP") > 0);
        // Their death, still queued, does nothing now.
        world.PutInHyperspace("g");
        world.MoveClockDays(200);
        Assert.Equal((Allied.Good, 0), (story.Alliance("ZOQFOTPIK_SHIP"), story.Get("ZOQFOT_DISTRESS")));
        Assert.NotEqual(0, story.Fleet("ZOQFOTPIK_SHIP").ActualStrength);
    }

    [ContentFact]
    public void RetreatingFromTheKohrAhsShipLeavesTheShipByTheWorldAndTheDistressOn()
    {
        var world = InDistress(out int star);
        var story = world.Story!;
        world.PutInOrbit("g", star, 0);
        var m = world.MeetingOf("g")!;
        Assert.True(world.EncounterChoice("g", EncounterAction.Attack));
        Retreat(world, "g");
        Frames(world, 2);
        Assert.Null(world.MeetingOf("g"));
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Equal(1, story.Get("ZOQFOT_DISTRESS"));
        // Orbited again: the Kohr-Ah's ship again.
        world.PutInOrbit("g", star, 0);
        Assert.Equal(story.Rules.RaceIndex("BLACK_URQUAN_SHIP"), world.MeetingOf("g")!.Race);
    }

    [ContentFact]
    public void TheKohrAhsShipDestroyedByAGuestAfterTheHostLeftStillBringsTheRescue()
    {
        // The host meets the Kohr-Ah's ship and retreats; the guest, let in,
        // destroys it. No thanks talk follows (the host flies elsewhere), but
        // the ship is recorded gone: the next orbit of their world, by
        // anyone, is the Zoq-Fot-Pik's rescue, with no new Kohr-Ah ship.
        var world = InDistress(out int star);
        var story = world.Story!;
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        Assert.Equal(story.Rules.RaceIndex("BLACK_URQUAN_SHIP"), m.Race);
        world.PutInSystem("g", star);
        var gip = world.Find("g")!.Ship.Ip!;
        gip.PlaceInOrbit(0, null);
        gip.ReturnFromOrbit();   // the guest by the same world
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.True(world.LetIntoBattle("g"));
        Frames(world, 3);
        var wb = m.Battle!;
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            if (wb.Enemy.RaceDesc is { } enemy)
                enemy.EnergyLevel = 0;
            world.Step();
        }
        Assert.Null(world.Host.Ship.Battle);
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 2);
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(world.MeetingOf("g"));
        Assert.Equal(1, story.Get("ZOQFOT_DISTRESS"));
        Assert.Equal(new[] { "ZOQFOT_KOHR_AH" }, story.GuardsGone);
        // Saved with the game.
        var loaded = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Equal(new[] { "ZOQFOT_KOHR_AH" }, loaded.Story!.GuardsGone);
        // Their death's day comes, both players flown off: spared (their attacker gone), the distress on.
        world.PutInHyperspace("g");
        world.PutInHyperspace("h");
        Frames(world, 1);
        Assert.False(world.PlayerInSystem("ZOQFOT_DEFINED"));
        Assert.Single(story.Events, e => e.Event == "ZOQFOT_DEATH_EVENT");
        world.MoveClockDays(200);
        Assert.DoesNotContain(story.Events, e => e.Event == "ZOQFOT_DEATH_EVENT");
        Assert.Equal((Allied.Good, 1), (story.Alliance("ZOQFOTPIK_SHIP"), story.Get("ZOQFOT_DISTRESS")));
        Assert.NotEqual(0, story.Fleet("ZOQFOTPIK_SHIP").ActualStrength);
        Assert.Equal(new[] { "ZOQFOT_KOHR_AH" }, story.GuardsGone);
        // The guest's orbit: the Zoq-Fot-Pik at once, their thanks; the record goes with the distress, at once.
        world.PutInOrbit("g", star, 0);
        var zfp = world.MeetingOf("g")!;
        Assert.Equal(story.Rules.RaceIndex("ZOQFOTPIK_SHIP"), zfp.Race);
        Assert.True(Said(zfp.Talk!, "THANKS_FOR_RESCUE1"));
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
        Assert.Empty(story.GuardsGone);
    }

    [Fact]
    public void TheKohrAhsRecordStandsOnlyWhileTheDistressIsOnAndASaveRefusesAnUnknownOne()
    {
        var world = BareWorld();
        var story = world.Story!;
        // Not recorded with no distress.
        story.MarkGuardGone("ZOQFOT_KOHR_AH");
        Assert.Empty(story.GuardsGone);
        Assert.Throws<KeyNotFoundException>(() => story.MarkGuardGone("NO_SUCH_GUARD"));
        story.Set("ZOQFOT_DISTRESS", 1);
        story.MarkGuardGone("ZOQFOT_KOHR_AH");
        Assert.Single(story.GuardsGone);
        // The death event, its day come: spared, the record standing.
        Assert.True(story.Events.Add(true, 0, 1, 0, "ZOQFOT_DEATH_EVENT", world.Clock));
        var done = world.MoveClockDays(1);
        Assert.Contains(done, e => e.Event.Event == "ZOQFOT_DEATH_EVENT" && !e.News && !e.Held);
        Assert.Equal((1, Allied.Bad), (story.Get("ZOQFOT_DISTRESS"), story.Alliance("ZOQFOTPIK_SHIP")));
        Assert.Single(story.GuardsGone);
        // Gone as soon as the distress isn't 1, whatever sets it: dead (2) as a death event or --start-state sets it.
        story.Set("ZOQFOT_DISTRESS", 2);
        Assert.Empty(story.GuardsGone);
        // Saved and restored with the story; a restored record whose distress is over goes; an unknown one is refused.
        story.Set("ZOQFOT_DISTRESS", 1);
        story.MarkGuardGone("ZOQFOT_KOHR_AH");
        var saved = story.Save();
        Assert.Equal(new[] { "ZOQFOT_KOHR_AH" }, saved.GuardsGone);
        Assert.Equal(new[] { "ZOQFOT_KOHR_AH" }, StoryState.Restore(Rules, saved).GuardsGone);
        var over = new Dictionary<string, int>(saved.Fields) { ["ZOQFOT_DISTRESS"] = 0 };
        Assert.Empty(StoryState.Restore(Rules, saved with { Fields = over }).GuardsGone);
        Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, saved with { GuardsGone = new() { "NO_SUCH_GUARD" } }));
    }

    [ContentFact]
    public void AHostSaveRefusesAnUnknownGuardGone()
    {
        var world = NewGame();
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["story"]!["guards_gone"] = new JsonArray("NO_SUCH_GUARD");
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), world.Systems!, TestContent.Shapes));
        node["story"]!["guards_gone"] = new JsonArray("ZOQFOT_KOHR_AH");   // known, but no distress: dropped
        Assert.Empty(HostSave.FromJson(node.ToJsonString(), world.Systems!, TestContent.Shapes).Story!.GuardsGone);
    }

    [ContentFact]
    public void TheKohrAhsShipDestroyedWhileTheirLockIsHeldBringsTheRescueAtTheNextOrbit()
    {
        var world = InDistress(out int star);
        var story = world.Story!;
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        // The guest talking to the Zoq-Fot-Pik elsewhere meanwhile: their lock held.
        Assert.True(world.DialogueLocks!.TryTake("ZOQFOTPIK_CONVERSATION", "g", out _));
        world.TakeNotices();
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 2);
        // No thanks now: the host told who's talking to them, the ship by the world.
        Assert.Null(world.MeetingOf("h"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.Talking);
        Assert.Equal(1, story.Get("ZOQFOT_DISTRESS"));
        Assert.Contains("ZOQFOT_KOHR_AH", story.GuardsGone);
        // The lock let go: the host's next orbit is the rescue.
        world.DialogueLocks.Release("ZOQFOTPIK_CONVERSATION", "g");
        world.PutInOrbit("h", star, 0);
        var zfp = world.MeetingOf("h")!;
        Assert.Equal(story.Rules.RaceIndex("ZOQFOTPIK_SHIP"), zfp.Race);
        Assert.True(Said(zfp.Talk!, "THANKS_FOR_RESCUE1"));
        Assert.Equal(0, story.Get("ZOQFOT_DISTRESS"));
    }

    // ---- the Arilou's portal (starcon.c's checkArilouGate)

    [Fact]
    public void TheArilouPortalsCounterStepsOnceAWorldFrameAsItOpensAndCloses()
    {
        var world = BareWorld();
        var story = world.Story!;
        world.Clock.FramesIntoDay = -100000;   // no new day meanwhile
        story.Set("ARILOU_SPACE", StoryState.ArilouOpening);
        var counts = new List<int>();
        for (int i = 0; i < 12; ++i)
        {
            world.Step();
            counts.Add(story.Get("ARILOU_SPACE_COUNTER"));
        }
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 9 }, counts);
        // Held by a pause, as every frame is.
        Assert.True(world.SetHostPause(true));
        story.Set("ARILOU_SPACE", StoryState.ArilouClosing);
        world.Step();
        Assert.Equal(9, story.Get("ARILOU_SPACE_COUNTER"));
        Assert.True(world.SetHostPause(false));
        counts.Clear();
        for (int i = 0; i < 11; ++i)
        {
            world.Step();
            counts.Add(story.Get("ARILOU_SPACE_COUNTER"));
        }
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1, 0, 0, 0 }, counts);
    }

    // ---- the Yehat's rebellion check (row 72)

    [ContentFact]
    public void AfterABattleOrBeforeTheCivilWarTheYehatGroupsStay()
    {
        var world = NewGame("g");
        var story = world.Story!;
        // Before the civil war: a peace (an ally's group) leaves the group as touching it left it.
        story.SetRaceAllied("YEHAT_SHIP", true);
        var m = MeetInSystem(world, "g", "YEHAT_SHIP");
        var group = world.SystemGroups!.Systems[GroupStar].Groups.Single();
        if (m.Step == MeetingStep.Encounter)
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        while (world.MeetingOf("g") is { Step: not MeetingStep.Closing } now && now.Talk is { Ended: false } t && t.Replies.Count > 0)
            Assert.True(world.ChooseReply("g", t.Replies.Count - 1));
        if (world.MeetingOf("g") is { Step: MeetingStep.Closing })
            MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Null(world.MeetingOf("g"));
        Assert.NotEqual(IpGroup.Flee, group.Task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup));
        // In the civil war, after a battle: the encounter's race turns on the players instead (DoMissions).
        story.SetRaceAllied("YEHAT_SHIP", false);
        story.Set("YEHAT_CIVIL_WAR", 1);
        m = MeetInSystem(world, "g", "YEHAT_SHIP");
        group = world.SystemGroups.Systems[GroupStar].Groups.Single();
        Assert.True(world.EncounterChoice("g", EncounterAction.Attack));
        Retreat(world, "g");
        Assert.NotEqual(IpGroup.Flee, group.Task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup));
    }

    [Fact]
    public void TheRebellionCheckSendsOffOnlyTheYehatGroupsThatStoodDown()
    {
        // check_yehat_rebellion (gendefault.c:338) after a meeting with no
        // battle, the civil war begun: a Yehat group that stood down from the
        // touch (IGNORE_FLAGSHIP), heading for a planet, flees, keeping only
        // REFORM_GROUP of its task, heading nowhere; a Yehat group that
        // didn't stand down, and another race's that did, stay as they were.
        var rules = Rules;
        int yehat = rules.RaceIndex("YEHAT_SHIP"), spathi = rules.RaceIndex("SPATHI_SHIP");
        (SystemGroups, SystemDraw, IpGroup[]) Setup(int civilWar, out StoryState story)
        {
            story = StoryState.NewGame(rules, new GameClock(2155, 2, 17));
            story.Set("YEHAT_CIVIL_WAR", civilWar);
            var sg = new SystemGroups(rules) { Ported = rules.Races.ToHashSet() };
            var d = new SystemDraw(GroupStar) { Random = new HyperspaceRandom(9) { Places = new() } };
            sg.Systems[GroupStar] = d;
            IpGroup G(int race, byte task, byte dest) => new() { Race = race, Task = task, DestLoc = dest, InSystem = true };
            var groups = new[]
            {
                G(yehat, (byte)(IpGroup.OnStation | IpGroup.IgnoreFlagship | IpGroup.ReformGroup), 3),   // the one met
                G(yehat, (byte)(IpGroup.Explore | IpGroup.IgnoreFlagship), 2),
                G(yehat, IpGroup.OnStation, 4),
                G(spathi, (byte)(IpGroup.OnStation | IpGroup.IgnoreFlagship), 5),
            };
            d.Groups.AddRange(groups);
            return (sg, d, groups);
        }
        var (sg, d, g) = Setup(1, out var story);
        sg.EndMeeting(d, g[0], null, -1, story);
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship | IpGroup.ReformGroup, (byte)0), (g[0].Task, g[0].DestLoc));
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship, (byte)0), (g[1].Task, g[1].DestLoc));
        Assert.Equal((IpGroup.OnStation, (byte)4), (g[2].Task, g[2].DestLoc));
        Assert.Equal(((byte)(IpGroup.OnStation | IpGroup.IgnoreFlagship), (byte)5), (g[3].Task, g[3].DestLoc));
        // After a battle (its race to turn on the players), or before the war: nobody flees.
        foreach (var (war, battle) in new[] { (1, yehat), (0, -1) })
        {
            (sg, d, g) = Setup(war, out story);
            sg.EndMeeting(d, g[0], null, battle, story);
            Assert.Equal(((byte)(IpGroup.OnStation | IpGroup.IgnoreFlagship | IpGroup.ReformGroup), (byte)3), (g[0].Task, g[0].DestLoc));
            Assert.Equal(((byte)(IpGroup.Explore | IpGroup.IgnoreFlagship), (byte)2), (g[1].Task, g[1].DestLoc));
        }
    }

    // ---- --start-date

    [ContentFact]
    public void StartDateMovesTheClockOnRunningEachDaysEventsWithoutRolls()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host", systems,
            TestContent.Shapes);
        var world = host.World;
        var story = world.Story!;
        Assert.True(story.Events.Add(true, 0, 30, 0, "SHOFIXTI_RETURN_EVENT", world.Clock));
        Assert.Equal("Starting on 2155-03-18: 29 days passed, 1 event run (ARILOU_ENTRANCE_EVENT)", host.StartDate("2155-03-18"));
        Assert.Equal((2155, 3, 18), (world.Clock.Year, world.Clock.Month, world.Clock.Day));
        Assert.Equal(StoryState.ArilouOpening, story.Get("ARILOU_SPACE"));
        Assert.Equal(0, world.HyperspaceEncounterDays);
        Assert.Equal("Starting on 2155-03-21: 3 days passed, 2 events run (SHOFIXTI_RETURN_EVENT, ARILOU_EXIT_EVENT)",
            host.StartDate("2155-03-21"));
        Assert.Equal(Allied.Good, story.Alliance("SHOFIXTI_SHIP"));
        Assert.Equal("Starting on 2155-03-21: 0 days passed, 0 events run", host.StartDate("2155-03-21"));
        // Those a location gate held, and those set aside, told apart from those run.
        Assert.True(story.Events.Add(true, 0, 1, 0, "SPATHI_SHIELD_EVENT", world.Clock));
        Assert.True(story.Events.Add(true, 0, 2, 0, "KOHR_AH_GENOCIDE_EVENT", world.Clock));
        world.PutInSystem("host", Star(world, "SPATHI_DEFINED"));
        Assert.Equal("Starting on 2155-03-30: 9 days passed, 0 events run, 2 events held by a player in their race's system "
                     + "(SPATHI_SHIELD_EVENT, SPATHI_SHIELD_EVENT), 1 event set aside (KOHR_AH_GENOCIDE_EVENT)", host.StartDate("2155-03-30"));
        // Not backwards, and only real dates.
        foreach (var bad in new[] { "2155-03-20", "2155-02-30", "2155-3", "tomorrow", "2155-13-01" })
            Assert.Throws<ArgumentException>(() => host.StartDate(bad));
        Assert.Equal((2155, 3, 30), (world.Clock.Year, world.Clock.Month, world.Clock.Day));
    }

    // ---- what an event changes, taken back

    [Fact]
    public void WhatAnEventChangedIsTakenBackWhole()
    {
        var world = BareWorld();
        var story = world.Story!;
        story.SetCrewCost(5);
        story.Set("CREW_PURCHASED0", 7);
        Assert.True(story.Events.Add(true, 0, 3, 0, "ZOQFOT_DISTRESS_EVENT", world.Clock));
        var before = story.Save();
        // The Yehat's civil war run at once, and the Shofixti's return.
        story.AddEvent(true, 0, 0, 0, "YEHAT_REBEL_EVENT", world.Clock);
        story.RunEvent("SHOFIXTI_RETURN_EVENT", world.Clock);
        world.MoveClockDays(3);
        var changes = StoryState.Changes.Between(before, story.Save());
        Assert.Equal(5, changes.CrewCost);
        Assert.Equal(new[] { "SHOFIXTI_SHIP" }, changes.Alliances.Keys);
        Assert.Equal(new[] { "YEHAT_REBEL_SHIP", "YEHAT_SHIP" }, changes.Fleets.Keys.OrderBy(k => k));
        Assert.Contains("ZOQFOT_DISTRESS", changes.Fields.Keys);
        changes.TakeBackFrom(story);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(story.Save()));
    }
}
