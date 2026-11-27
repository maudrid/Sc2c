// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The fleet missions (docs/plans/stage-3c.md, item 3): what each step of
/// each mission tells (each mission's course itself is checked day by day
/// against UQM's gameev.c in ClockEventsTests); a fleet's arrival run, not
/// set aside, and told to every player present; a race its losses run
/// out; and the spheres the players know, sent to the clients for the
/// status panel's fleet line (the plan's question 8).
/// </summary>
[Trait("Topic", "story")]
public class FleetMissionsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    private static (StoryState Story, GameClock Clock) NewStory()
    {
        var clock = new GameClock(2155, 2, 17);
        return (StoryState.NewGame(Rules, clock), clock);
    }

    // The days pass as the clock passes them, what the events told taken each day.
    private static List<(string Race, string? Also, NoticeEvent Event, string Detail)> Days(StoryState story, GameClock clock, int days)
    {
        var told = new List<(string, string?, NoticeEvent, string)>();
        for (int i = 0; i < days; ++i)
        {
            clock.NextDay();
            story.RunDueEvents(clock);
            told.AddRange(story.TakeNews().Select(n => (n.Race, n.Also, n.Event, n.Detail)));
        }
        return told;
    }

    // A mission's event run at once, as its talk runs it, and what it told.
    private static List<(string Race, string? Also, NoticeEvent Event, string Detail)> AtOnce(StoryState story, GameClock clock, string evt)
    {
        story.TakeNews();
        story.AddEvent(true, 0, 0, 0, evt, clock);
        return story.TakeNews().Select(n => (n.Race, n.Also, n.Event, n.Detail)).ToList();
    }

    private static (string, string?, NoticeEvent, string) Step(string race, string detail, string? also = null) =>
        (race, also, NoticeEvent.Mission, detail);

    // ---- what each mission's steps tell

    [Fact]
    public void ThePkunksMigrationTellsEachStepTheirAbsorptionTheLast()
    {
        var (story, clock) = NewStory();
        Assert.Equal(new[] { Step("PKUNK_SHIP", "have set off to join the Yehat") }, AtOnce(story, clock, "ADVANCE_PKUNK_MISSION"));
        Assert.Empty(Days(story, clock, 60));
        // A good reason, 60 days on: home as long as they've travelled.
        Assert.Equal(new[] { Step("PKUNK_SHIP", "have turned back for home") }, AtOnce(story, clock, "ADVANCE_PKUNK_MISSION"));
        Assert.Equal(60, story.Fleet("PKUNK_SHIP").DaysLeft);
        Assert.Equal(new[] { Step("PKUNK_SHIP", "are back home") }, Days(story, clock, 60));
        // Three months' wait, then off again, and absorbed on arriving.
        var told = Days(story, clock, 100);
        Assert.Equal(new[] { Step("PKUNK_SHIP", "have set off to join the Yehat") }, told);
        Assert.Equal(new[] { Step("PKUNK_SHIP", "have reached the Yehat and been absorbed by them: they're no more") },
            Days(story, clock, 200));
        Assert.Equal((Allied.Dead, 0, 1, 1), (story.Alliance("PKUNK_SHIP"), story.Fleet("PKUNK_SHIP").ActualStrength,
            story.Get("YEHAT_ABSORBED_PKUNK"), story.Fleet("YEHAT_SHIP").KnownStrength));
        // Gone: their event does nothing more, and tells nothing.
        Assert.Empty(AtOnce(story, clock, "ADVANCE_PKUNK_MISSION"));
    }

    [Fact]
    public void TheThraddashsMissionTellsEachStep()
    {
        var (story, clock) = NewStory();
        Assert.Equal(new[] { Step("THRADDASH_SHIP", "have set off to attack the Kohr-Ah") }, AtOnce(story, clock, "ADVANCE_THRADD_MISSION"));
        Assert.Equal(new[]
        {
            Step("THRADDASH_SHIP", "have reached the Kohr-Ah and are losing ships fighting them"),
            Step("THRADDASH_SHIP", "are heading home from the Kohr-Ah"),
            Step("THRADDASH_SHIP", "are back home"),
        }, Days(story, clock, 50));
        Assert.Equal((4, 75), (story.Get("THRADD_MISSION"), story.Fleet("THRADDASH_SHIP").ActualStrength));
    }

    [Fact]
    public void TheIlwrathsWarTellsTheWarTheRecallAndBothRacesEnd()
    {
        // The Ilwrath sent (their "gods"); the Thraddash, allied, sent at the Kohr-Ah 80 days on, so the
        // war calls them home; home, the Ilwrath's mission run at once from theirs sends them to the battle.
        var (story, clock) = NewStory();
        story.SetRaceAllied("THRADDASH_SHIP", true);
        Assert.Equal(new[] { Step("ILWRATH_SHIP", "have set off to make war on the Thraddash") }, AtOnce(story, clock, "ADVANCE_ILWRATH_MISSION"));
        Assert.Empty(Days(story, clock, 80));
        AtOnce(story, clock, "ADVANCE_THRADD_MISSION");
        Assert.Equal(new[]
        {
            Step("ILWRATH_SHIP", "are at war: both are losing ships, the Thraddash called home from the Kohr-Ah, and the Thraddash are no longer allies",
                "THRADDASH_SHIP"),
            Step("THRADDASH_SHIP", "are back home"),
            Step("THRADDASH_SHIP", "have set off to meet the Ilwrath in battle"),
        }, Days(story, clock, 20));
        Assert.Equal((1, 1, Allied.Bad), (story.Get("ILWRATH_FIGHT_THRADDASH"), story.Get("HELIX_UNPROTECTED"), story.Alliance("THRADDASH_SHIP")));
        Assert.Equal(new[] { Step("ILWRATH_SHIP", "have destroyed each other", "THRADDASH_SHIP") }, Days(story, clock, 150));
        Assert.Equal((Allied.Dead, Allied.Dead), (story.Alliance("ILWRATH_SHIP"), story.Alliance("THRADDASH_SHIP")));
    }

    [Fact]
    public void TheMyconsAndTheUtwigsAndSupoxsMissionsTellEachStep()
    {
        var (story, clock) = NewStory();
        Assert.Equal(new[] { Step("MYCON_SHIP", "have set off for Organon") }, AtOnce(story, clock, "ADVANCE_MYCON_MISSION"));
        Assert.Equal(new[]
        {
            Step("MYCON_SHIP", "have reached Organon and are losing ships there"),
            Step("MYCON_SHIP", "are heading back from Organon"),
        }, Days(story, clock, 80));
        Assert.Equal(1, story.Get("MYCON_KNOW_AMBUSH"));

        Assert.Equal(new[] { Step("UTWIG_SHIP", "have set off to attack the Kohr-Ah", "SUPOX_SHIP") },
            AtOnce(story, clock, "ADVANCE_UTWIG_SUPOX_MISSION"));
        // The losses for 160 days (the event 80 days into them tells nothing), then home.
        Assert.Equal(new[]
        {
            Step("UTWIG_SHIP", "have reached the Kohr-Ah and are losing ships fighting them", "SUPOX_SHIP"),
            Step("UTWIG_SHIP", "are heading home from the Kohr-Ah", "SUPOX_SHIP"),
            Step("UTWIG_SHIP", "are back home", "SUPOX_SHIP"),
        }, Days(story, clock, 210));
        Assert.Equal(6, story.Get("UTWIG_SUPOX_MISSION"));
    }

    [Fact]
    public void TwoArrivalsOnADayAreBothListedOneRunningAnotherMissionAtOnce()
    {
        // The Pkunk arriving home (their event re-queued three months on), then the Thraddash home from the
        // Kohr-Ah with the Ilwrath's war begun, whose arrival runs the Ilwrath's mission at once from within it.
        var (story, clock) = NewStory();
        story.Set("PKUNK_MISSION", 2);
        story.Set("PKUNK_ON_THE_MOVE", 1);
        story.SetFleet("PKUNK_SHIP", story.Fleet("PKUNK_SHIP") with { LocX = 600 });
        story.SetRaceDest("PKUNK_SHIP", 502, 401, 1, "ADVANCE_PKUNK_MISSION");
        story.Set("THRADD_MISSION", 3);
        story.Set("ILWRATH_FIGHT_THRADDASH", 1);
        story.SetRaceDest("THRADDASH_SHIP", 2535, 8358, 1, "ADVANCE_THRADD_MISSION");
        story.SetRaceDest("ILWRATH_SHIP", 2517, 8214, 50, "ADVANCE_ILWRATH_MISSION");
        clock.NextDay();
        var done = story.RunDueEvents(clock);
        Assert.Equal(new[] { "HYPERSPACE_ENCOUNTER_EVENT", "ADVANCE_PKUNK_MISSION", "ADVANCE_THRADD_MISSION" }, done.Select(e => e.Event.Event));
        Assert.Equal(new[] { false, true, true }, done.Select(e => e.Arrival));
        Assert.All(done.Skip(1), e => Assert.True(e.News));
        Assert.False(done[0].News);
        Assert.Equal(new[]
        {
            Step("PKUNK_SHIP", "are back home"), Step("THRADDASH_SHIP", "are back home"),
            Step("THRADDASH_SHIP", "have set off to meet the Ilwrath in battle"),
        }, story.TakeNews().Select(n => (n.Race, n.Also, n.Event, n.Detail)));
        // The Ilwrath's days left, and one: 50 as the Thraddash (earlier in avail_race_q) arrive, before the Ilwrath's day.
        Assert.Equal((4, 51, 49), (story.Get("THRADD_MISSION"), story.Fleet("THRADDASH_SHIP").DaysLeft, story.Fleet("ILWRATH_SHIP").DaysLeft));
        // RunEvent run outside the day's list keeps no arrival for a later day's.
        story.SetRaceDest("MYCON_SHIP", 6858, 577, 1, "ADVANCE_MYCON_MISSION");
        story.RunEvent("HYPERSPACE_ENCOUNTER_EVENT", clock);
        clock.NextDay();
        Assert.DoesNotContain(story.RunDueEvents(clock), e => e.Arrival);
    }

    [Fact]
    public void ARaceItsLossesRunOutIsTold()
    {
        var (story, clock) = NewStory();
        story.SetFleet("VUX_SHIP", story.Fleet("VUX_SHIP") with { Growth = unchecked((byte)-100) });
        var told = Days(story, clock, 5);
        Assert.Equal(new[] { ("VUX_SHIP", (string?)null, NoticeEvent.Extinct, "") }, told);
        Assert.Equal(Allied.Dead, story.Alliance("VUX_SHIP"));
    }

    // ---- in the world

    [ContentFact]
    public void AFleetsArrivalRunsItsMissionAndEveryPlayerPresentIsTold()
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("g", "g"));
        Assert.Equal(JoinResult.Joined, world.Join("away", "away"));
        world.Leave("away");
        var story = world.Story!;
        world.TakeNotices();
        // The Mycon's mission queued for tomorrow, as the clock runs it: told to the host and the guest.
        Assert.True(story.Events.Add(true, 0, 1, 0, "ADVANCE_MYCON_MISSION", world.Clock));
        world.MoveClockDays(1, rolls: false);
        var told = world.TakeNotices();
        Assert.Equal(new[] { "g", "h" }, told.Select(n => n.PlayerId).OrderBy(p => p));
        Assert.All(told, n => Assert.Equal("The Mycon have set off for Organon.", n.Text));
        // Their arrival at Organon 30 days on runs the mission, listed as an arrival
        // after its day's event, and is told.
        var done = world.MoveClockDays(30, rolls: false);
        var arrived = Assert.Single(done, e => e.Arrival);
        Assert.Equal(("ADVANCE_MYCON_MISSION", true), (arrived.Event.Event, arrived.News));
        Assert.Equal("HYPERSPACE_ENCOUNTER_EVENT", done[done.IndexOf(arrived) - 1].Event.Event);
        Assert.Empty(story.Events.SetAside);
        Assert.All(world.TakeNotices(), n => Assert.Equal("The Mycon have reached Organon and are losing ships there.", n.Text));
        Assert.Contains(story.Events, e => e.Event == "ADVANCE_MYCON_MISSION");   // the losses' end, 14 days on
    }

    // ---- the spheres the players know (question 8)

    [Fact]
    public void TheSpheresKnownAreTheTrackedOnesAtTheirStrengthAndPlaceNow()
    {
        var (story, clock) = NewStory();
        Assert.Empty(StorySnapshot.Of(story).Fleets);
        story.StartSphereTracking("THRADDASH_SHIP");
        story.StartSphereTracking("ILWRATH_SHIP");
        AtOnce(story, clock, "ADVANCE_THRADD_MISSION");
        Days(story, clock, 20);
        // As the star map would show them once opened: where the Thraddash are now, at their strength
        // now (not where tracking found them, at 1).
        var thradd = story.Fleet("THRADDASH_SHIP");
        Assert.Equal(1, thradd.KnownStrength);
        var known = StorySnapshot.Of(story).Fleets;
        Assert.Equal(new[]
        {
            new KnownFleet(Rules.RaceIndex("THRADDASH_SHIP"), thradd.ActualStrength, thradd.LocX, thradd.LocY),
            new KnownFleet(Rules.RaceIndex("ILWRATH_SHIP"), 256, 48, 1700),
        }, known);
        // A sphere shrunk to nothing isn't shown (the star map shrinks it away).
        story.SetFleet("ILWRATH_SHIP", story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        Assert.Single(StorySnapshot.Of(story).Fleets);
    }

    [Fact]
    public void TheSpheresKnownGoOnTheWireWhenTheyChangeAndBadOnesAreRefused()
    {
        var (story, clock) = NewStory();
        WorldSnapshot At(long frame) => new(frame, clock.Year, clock.Month, clock.Day, Array.Empty<ShipSnapshot>(), Story: StorySnapshot.Of(story));
        WorldSnapshot RoundTrip(WorldSnapshot s, WorldSnapshot? b, out byte[] data)
        {
            var w = new WireWriter();
            SnapshotCodec.Encode(w, s, b);
            data = w.Written.ToArray();
            return SnapshotCodec.Decode(data, b);
        }
        var none = RoundTrip(At(1), null, out _);
        Assert.Empty(none.Story!.Fleets);
        story.StartSphereTracking("MYCON_SHIP");
        story.StartSphereTracking("UTWIG_SHIP");
        var full = RoundTrip(At(2), null, out _);
        Assert.Equal(StorySnapshot.Of(story).Fleets, full.Story!.Fleets);
        // On the move: a delta carries them; with nothing changed, the base's are kept.
        AtOnce(story, clock, "ADVANCE_MYCON_MISSION");
        Days(story, clock, 3);
        var moved = RoundTrip(At(3), full, out _);
        Assert.Equal(StorySnapshot.Of(story).Fleets, moved.Story!.Fleets);
        Assert.NotEqual(full.Story.Fleets, moved.Story.Fleets);
        story.Set("MOONBASE_ON_SHIP", 1);
        var kept = RoundTrip(At(4), moved, out _);
        Assert.Equal(moved.Story.Fleets, kept.Story!.Fleets);
        Assert.True(kept.Story.SameAs(StorySnapshot.Of(story)));
        // A sphere of no strength, or out of race order, is refused.
        foreach (var bad in new[]
        {
            new[] { new KnownFleet(3, 0, 1, 1) },
            new[] { new KnownFleet(5, 10, 1, 1), new KnownFleet(4, 10, 1, 1) },
            new[] { new KnownFleet(5, 10, 40000, 1) },
        })
        {
            var s = StorySnapshot.Of(story);
            var spoilt = new StorySnapshot(s.Bits, s.CrewCost, s.FuelCost, s.ModuleCost.ToArray(), s.Alliances.ToArray(), bad);
            var w = new WireWriter();
            SnapshotCodec.Encode(w, At(5) with { Story = spoilt });
            Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(w.Written.ToArray()));
        }
    }

    [Fact]
    public void TheStatusPanelListsTheSpheresKnown()
    {
        var (story, clock) = NewStory();
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, "g1", 32);
        var ship = new ShipSnapshot("g1", "g1", false, ShipKind.Escort, 0, 0, 5000, 5000, 0, 0, 0, 100, 100, false, false, Activity.Hyperspace);
        me.Observe(new WorldSnapshot(1, 2155, 2, 17, new[] { ship }, Story: StorySnapshot.Of(story)));
        Assert.Null(me.FleetLine());
        Assert.DoesNotContain(me.StatusLines(), l => l.StartsWith("Spheres", StringComparison.Ordinal));
        story.StartSphereTracking("THRADDASH_SHIP");
        story.StartSphereTracking("ARILOU_SHIP");
        AtOnce(story, clock, "ADVANCE_THRADD_MISSION");
        Days(story, clock, 7);
        me.Observe(new WorldSnapshot(2, 2155, 2, 24, new[] { ship }, Story: StorySnapshot.Of(story)));
        var t = story.Fleet("THRADDASH_SHIP");
        string line = $"Spheres: Arilou 44 at 43.8 : 637.2; Thraddash 150 at {t.LocX / 10.0:0.0} : {t.LocY / 10.0:0.0}";
        Assert.Equal(line, me.FleetLine());
        Assert.Contains(line, me.StatusLines());
    }
}
