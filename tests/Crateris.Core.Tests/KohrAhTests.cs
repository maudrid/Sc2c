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
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Kohr-Ah (stage 3c item 4): their victory four years on (a year
/// later while the Utwig and the Supox fight them), the genocide race by
/// race with its news, the gate at the Sa-Matra before the frenzy, and the
/// game lost once every race is dead: no save, no visit or story pause,
/// the Black Ur-Quan's last talk shown to everyone on a story pause that
/// takes over from whatever story is shown, then the game over. UQM's own
/// course is <see cref="ClockEventsTests"/>' recording.
/// </summary>
[Trait("Topic", "story")]
public class KohrAhTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    private static StoryEvent At(string evt, int y, int m, int d) => new(evt, y, m, d);

    private static (int, int, int) Date(GameClock c) => (c.Year, c.Month, c.Day);

    // The eve of the end: every race the genocide kills gone but the
    // humans (who have no sphere), the frenzy begun, and the Kohr-Ah's
    // fleet `days` from Sol, to run the genocide there.
    private static void OnTheEve(StoryState story, int days = 1)
    {
        for (int i = 0; i < Rules.Races.Count; ++i)
        {
            string race = Rules.Races[i];
            if (race is "BLACK_URQUAN_SHIP" or "URQUAN_SHIP" or "HUMAN_SHIP" || story.Fleets[i].ActualStrength == Rules.InfiniteRadius)
                continue;
            story.SetFleet(race, story.Fleets[i] with { ActualStrength = 0 });
            story.SetAlliance(race, Allied.Dead);
        }
        story.Set("KOHR_AH_FRENZY", 1);
        story.SetRaceDest("BLACK_URQUAN_SHIP", 1752, 1450, days, "KOHR_AH_GENOCIDE_EVENT");
    }

    // ---- the events

    [Fact]
    public void TheVictoryBeginsTheGenocideWhichTellsItsCourseAndEachRaceItDestroys()
    {
        var clock = new GameClock(2155, 2, 17);
        var story = StoryState.NewGame(Rules, clock);
        var news = new List<(GameClock, StoryState.StoryNews)>();
        var dates = new List<(int, int, int)>();
        while (story.Get("KOHR_AH_KILLED_ALL") == 0)
        {
            clock.NextDay();
            story.RunDueEvents(clock);
            foreach (var n in story.TakeNews())
            {
                news.Add((clock, n));
                dates.Add(Date(clock));
            }
            Assert.True(clock.Year < 2161, "the genocide never ends");
        }
        // The victory on time, four years on: the frenzy, the Druuge first (the Mycon are nearer).
        Assert.Equal((2159, 2, 17), dates[0]);
        Assert.Equal(new StoryState.StoryNews(NoticeEvent.Mission, "BLACK_URQUAN_SHIP",
            Detail: "have won their war with the Ur-Quan and begun to wipe out every other race: they're heading for the Druuge"), news[0].Item2);
        Assert.Equal(new StoryState.StoryNews(NoticeEvent.Extinct, "DRUUGE_SHIP", Detail: "by the Kohr-Ah"), news[1].Item2);
        Assert.Equal(new StoryState.StoryNews(NoticeEvent.Mission, "BLACK_URQUAN_SHIP", Detail: "are heading for the Mycon"), news[2].Item2);
        Assert.Equal((2159, 3, 19), dates[1]);
        // Race by race (as UQM's recording has them), each told once; the
        // rebels and the humans, who have no sphere, killed untold.
        Assert.Equal(new[]
        {
            "DRUUGE_SHIP", "MYCON_SHIP", "VUX_SHIP", "ORZ_SHIP", "SPATHI_SHIP", "ZOQFOTPIK_SHIP", "UMGAH_SHIP", "ARILOU_SHIP",
            "THRADDASH_SHIP", "SUPOX_SHIP", "UTWIG_SHIP", "YEHAT_SHIP", "PKUNK_SHIP", "ILWRATH_SHIP",
        }, news.Where(n => n.Item2.Event == NoticeEvent.Extinct).Select(n => n.Item2.Race));
        Assert.All(news, n => Assert.True(n.Item2.Event is NoticeEvent.Extinct or NoticeEvent.Mission));
        // The last course, to Sol; there, every race dead: the game over (19 November 2159).
        Assert.Equal(new StoryState.StoryNews(NoticeEvent.Mission, "BLACK_URQUAN_SHIP",
            Detail: "have wiped out every other race: they're heading for Sol"), news[^1].Item2);
        Assert.Equal((2159, 11, 19), Date(clock));
        Assert.Equal((Allied.Dead, 0), (story.Alliance("HUMAN_SHIP"), story.Fleet("HUMAN_SHIP").ActualStrength));
        Assert.Equal(Allied.Dead, story.Alliance("YEHAT_REBEL_SHIP"));
        Assert.NotEqual(0, story.Fleet("URQUAN_SHIP").ActualStrength);
        Assert.NotEqual(Allied.Dead, story.Alliance("MELNORME_SHIP"));
        Assert.Equal((1752, 1450), (story.Fleet("BLACK_URQUAN_SHIP").LocX, story.Fleet("BLACK_URQUAN_SHIP").LocY));
        // In words: the race and how.
        Assert.Equal("The Druuge have been destroyed by the Kohr-Ah.",
            new WorldNotice("h", NoticeEvent.Extinct, "", "Druuge", Detail: "by the Kohr-Ah").Text);
        Assert.Equal("The Kohr-Ah are heading for the Mycon.", new WorldNotice("h", NoticeEvent.Mission, "", "Kohr-Ah", Detail: "are heading for the Mycon").Text);
    }

    [Fact]
    public void TheVictoryWaitsAYearOnceTheUtwigAndTheSupoxHaveGoneToWar()
    {
        var clock = new GameClock(2159, 2, 17);
        var story = StoryState.NewGame(Rules, clock);
        story.Set("UTWIG_SUPOX_MISSION", 6);   // home again: still set
        Assert.False(story.RunEvent("KOHR_AH_VICTORIOUS_EVENT", clock));
        Assert.Equal(0, story.Get("KOHR_AH_FRENZY"));
        Assert.Contains(At("KOHR_AH_GENOCIDE_EVENT", 2160, 2, 17), story.Events);
        Assert.Equal(0, story.Fleet("BLACK_URQUAN_SHIP").DaysLeft);
        Assert.Equal(0, story.NewsCount);
    }

    [Fact]
    public void ThePlayersAtTheSaMatraHoldTheVictoryAndTheGenocideUntilTheFrenzy()
    {
        var clock = new GameClock(2159, 2, 17);
        var story = StoryState.NewGame(Rules, clock);
        bool AtSaMatra(string special) => special == "SAMATRA_DEFINED";
        // The victory falls through to the genocide's gate, held: queued again as the genocide, 7 days on.
        Assert.False(story.RunEvent("KOHR_AH_VICTORIOUS_EVENT", clock, playerInSystem: AtSaMatra));
        Assert.Equal(0, story.Get("KOHR_AH_FRENZY"));
        Assert.Contains(At("KOHR_AH_GENOCIDE_EVENT", 2159, 2, 24), story.Events);
        Assert.Single(story.Events, e => e.Event == "KOHR_AH_VICTORIOUS_EVENT");   // the new game's own, not queued again
        // Listed as held, each time; another system holds nothing.
        for (int i = 0; i < 7; ++i)
            clock.NextDay();
        var held = story.RunDueEvents(clock, playerInSystem: AtSaMatra);
        Assert.Contains(new StoryState.DueEvent(At("KOHR_AH_GENOCIDE_EVENT", 2159, 2, 24), Held: true), held);
        for (int i = 0; i < 7; ++i)
            clock.NextDay();
        var ran = story.RunDueEvents(clock, playerInSystem: s => s == "SPATHI_DEFINED");
        Assert.Contains(new StoryState.DueEvent(At("KOHR_AH_GENOCIDE_EVENT", 2159, 3, 3), News: true), ran);
        Assert.Equal(1, story.Get("KOHR_AH_FRENZY"));
        // Once the frenzy has begun, the Sa-Matra holds nothing (the arrivals, or a genocide run again).
        Assert.True(story.RunEvent("KOHR_AH_GENOCIDE_EVENT", clock, playerInSystem: AtSaMatra));
        Assert.DoesNotContain(story.Events, e => e.Event == "KOHR_AH_GENOCIDE_EVENT");
    }

    [Fact]
    public void TheGenocidesSpeedIsADayFor158UnitsOfTheWayFromOneTo255()
    {
        var clock = new GameClock(2159, 2, 17);
        var story = StoryState.NewGame(Rules, clock);
        // The Druuge a unit away: the least, a day.
        var druuge = story.Fleet("DRUUGE_SHIP");
        var kohrAh = story.Fleet("BLACK_URQUAN_SHIP");
        story.SetFleet("BLACK_URQUAN_SHIP", kohrAh with { LocX = (short)(druuge.LocX - 1), LocY = druuge.LocY });
        story.RunEvent("KOHR_AH_GENOCIDE_EVENT", clock);
        Assert.Equal(1, story.Fleet("BLACK_URQUAN_SHIP").DaysLeft);
        // Across the map: square_root of 9999² + 9999² is 14140, 89 days.
        story.SetFleet("DRUUGE_SHIP", druuge with { LocX = 9999, LocY = 9999 });
        story.SetFleet("BLACK_URQUAN_SHIP", kohrAh with { LocX = 0, LocY = 0 });
        story.RunEvent("KOHR_AH_GENOCIDE_EVENT", clock);
        Assert.Equal((14140 / 158, 9999, 9999), (story.Fleet("BLACK_URQUAN_SHIP").DaysLeft,
            story.Fleet("BLACK_URQUAN_SHIP").DestLocX, story.Fleet("BLACK_URQUAN_SHIP").DestLocY));
        // The talks' counts start afresh with each step.
        story.Set("KOHR_AH_VISITS", 2);
        story.Set("URQUAN_VISITS", 3);
        story.RunEvent("KOHR_AH_GENOCIDE_EVENT", clock);
        Assert.Equal((0, 0), (story.Get("KOHR_AH_VISITS"), story.Get("URQUAN_VISITS")));
    }

    [Fact]
    public void TheKohrAhsTalkIsStoryOnceEveryRaceIsDead()
    {
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        Assert.False(TalkRules.IsStory("blackur", story));
        story.Set("KOHR_AH_KILLED_ALL", 1);
        Assert.True(TalkRules.IsStory("blackur", story));
    }

    [Fact]
    public void WithoutTheTalksTheGameIsOverAtOnce()
    {
        var clock = new GameClock(2159, 2, 17);
        var world = new GameWorld(new GameSettings(), clock) { Story = StoryState.NewGame(Rules, clock) };
        OnTheEve(world.Story!);
        world.MoveClockDays(1);
        Assert.True(world.KohrAhVictorious);
        Assert.True(world.GameLost);
        Assert.False(world.GameOver);
        world.Step();
        Assert.True(world.GameOver);
        Assert.Equal(GameLoss.KohrAhVictorious, world.Loss);
        Assert.False(world.FlagshipLost);
    }

    // ---- the game's end

    // The host and a guest (in hyperspace), with the talks, no day's roll meanwhile.
    private static GameWorld NewGame()
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("g", "Guest"));
        world.UndockShown("g");
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        var guest = world.Find("g")!;
        guest.Ship.Ip = null;
        guest.Ship.SystemStar = null;
        guest.Ship.Flight.PlaceAtUniverse(5800, 6100);
        guest.Ship.Flight.StarJustLeft = null;
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Clock.FramesIntoDay = -100000;   // the days only as a test passes them
        return world;
    }

    private static void Run(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    // The next frame passes a day.
    private static void DayNextFrame(GameWorld world) => world.Clock.FramesIntoDay = 1_000_000;

    // The game's end as it's shown: the Black Ur-Quan's last talk, its closing lines held, the host replying.
    private static StoryPause TheEnd(GameWorld world)
    {
        var end = Assert.IsType<StoryPause>(world.StoryShown);
        Assert.True(end.TakesOver && end.HoldsClosing);
        Assert.Same(world.Pause, end);
        Assert.Equal((VisitStep.StoryClosing, "h"), (end.Step, end.Replier.Id));
        var said = Assert.Single(end.Talks).Events.OfType<LineQueued>().Select(l => l.Phrase);
        Assert.Equal(new[] { Scripts["blackur"].PhraseNumber("GAME_OVER_DUDE") }, said.Distinct());
        foreach (var id in new[] { "h", "g" })
        {
            var view = world.TalkFor(id)!;
            Assert.Equal((VisitStep.StoryClosing, "blackur", "h", 0), (view.Step, view.Script, view.Replier, view.Replies.Count));
            Assert.NotEmpty(view.Lines);
        }
        return end;
    }

    [ContentFact]
    public void TheLastRaceKilledLosesTheGameAndTheBlackUrquansLastTalkEndsIt()
    {
        var world = NewGame();
        var story = world.Story!;
        OnTheEve(story);
        Assert.Equal(0, story.Fleet("BLACK_URQUAN_SHIP").KnownStrength);
        DayNextFrame(world);
        Run(world, 1);
        // The day's events killed the last race: the game is lost, and its end began in the same frame.
        Assert.Equal(1, story.Get("KOHR_AH_KILLED_ALL"));
        Assert.True(world.KohrAhVictorious && world.GameLost && world.StoryPaused);
        Assert.False(world.GameOver);
        var end = TheEnd(world);
        Assert.Equal(1, story.Fleet("BLACK_URQUAN_SHIP").KnownStrength);   // InitCommunication tracked their sphere
        // No save, no visit, no other story pause meanwhile.
        Assert.Throws<InvalidOperationException>(() => HostSave.ToJson(world));
        Assert.Null(world.StartStoryPause(world.Find("g")!, "another", 1));
        world.VisitStarbase(world.Host);
        Assert.Null(world.Visit);
        Assert.Null(world.Host.Ship.Visit);
        // The lines held for as long as the host takes.
        var date = Date(world.Clock);
        DayNextFrame(world);
        Run(world, 100);
        Assert.Same(end, world.StoryShown);
        Assert.Equal(date, Date(world.Clock));   // the clock stops under it
        // Only the replier, for this talk's round, goes through them.
        Assert.False(world.StoryClosingDone("g", end.Round));
        Assert.False(world.StoryClosingDone("h", end.Round + 1));
        Assert.True(world.StoryClosingDone("h", end.Round));
        Assert.False(world.GameOver);
        Run(world, 1);
        Assert.True(world.GameOver);
        Assert.Equal(GameLoss.KohrAhVictorious, world.Loss);
        Assert.False(world.Surrendered);
        Assert.Null(world.Pause);
        Assert.Equal(JoinResult.GameOver, world.Join("late", "Late"));
        Assert.Equal("The game is over: the Kohr-Ah have destroyed every race.", Net.Protocol.Describe(JoinResult.GameOver, world.Loss));
    }

    [ContentFact]
    public void TheEndTakesOverFromTheStarbasesTwoWeeks()
    {
        var world = NewGame();
        world.PutInOrbit("h", 82, 2, 1);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        var story = world.Story!;
        story.Set("MOONBASE_ON_SHIP", 1);
        OnTheEve(story, days: 5);
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;
        var date = new DateTime(world.Clock.Year, world.Clock.Month, world.Clock.Day).AddDays(StarbaseVisit.LostDays);
        Run(world, StarbaseVisit.TimePassingFrames);
        // The fifth day killed the last race: the rest of the days ran all the same, and the visit stopped
        // there (no commander), as UQM's VisitStarBase returns; the end shown instead.
        Assert.Equal((date.Year, date.Month, date.Day), Date(world.Clock));
        Assert.True(visit.Skip!.Skipped);
        Assert.Equal(VisitStep.TimePassing, visit.Step);
        Assert.Empty(visit.Talks);
        Assert.Same(visit, world.Visit);
        var end = TheEnd(world);
        Run(world, 20);
        Assert.Empty(visit.Talks);
        Assert.True(world.StoryClosingDone("h", end.Round));
        Run(world, 1);
        Assert.Equal((true, GameLoss.KohrAhVictorious), (world.GameOver, world.Loss));
        Assert.Empty(visit.Talks);
    }

    [ContentFact]
    public void TheEndTakesOverFromAStoryPausesDays()
    {
        var world = NewGame();
        OnTheEve(world.Story!, days: 2);
        int then = 0;
        var pause = world.StartStoryPause(world.Find("g")!, "a test's talk", 4, "comandr", new() { ["STARBASE_VISITED"] = 0 },
            then: _ => ++then)!;
        Run(world, TimeSkip.FadeFrames);
        // Its days all ran; its talk never comes, nor what was to follow it.
        Assert.True(pause.Skip!.Skipped);
        Assert.Empty(pause.Talks);
        Assert.NotSame(pause, world.Pause);
        var end = TheEnd(world);
        Assert.True(world.StoryClosingDone("h", end.Round));
        Run(world, 5);
        Assert.True(world.GameOver);
        Assert.Equal(0, then);
        Assert.Empty(pause.Talks);
    }

    [ContentFact]
    public void TheHostsPauseHoldsTheEndsEnd()
    {
        var world = NewGame();
        OnTheEve(world.Story!);
        DayNextFrame(world);
        Run(world, 1);
        var end = TheEnd(world);
        Assert.True(world.SetHostPause(true));
        Assert.True(world.StoryClosingDone("h", end.Round));
        Run(world, 10);
        Assert.False(world.GameOver);
        Assert.True(world.SetHostPause(false));
        Run(world, 1);
        Assert.True(world.GameOver);
        Assert.False(world.HostPaused);
    }

    [ContentFact]
    public void AGameThatStartsWithEveryRaceDeadEndsAsItStarts()
    {
        // --start-state's KOHR_AH_KILLED_ALL: the end at the first frame.
        var world = NewGame();
        world.Story!.Set("KOHR_AH_KILLED_ALL", 1);
        Assert.False(world.KohrAhVictorious);
        Run(world, 1);
        Assert.True(world.KohrAhVictorious);
        TheEnd(world);

        // A save whose events kill the last race as it loads (one made the day before, loaded a day late:
        // the Kohr-Ah's arrival due): lost as it loads, its end at the first frame.
        world = NewGame();
        OnTheEve(world.Story!);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        var date = new DateTime(world.Clock.Year, world.Clock.Month, world.Clock.Day).AddDays(1);
        node["clock"]!["day"] = date.Day;
        node["clock"]!["month"] = date.Month;
        var loaded = HostSave.FromJson(node.ToJsonString(), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.True(loaded.KohrAhVictorious);
        Assert.Throws<InvalidOperationException>(() => HostSave.ToJson(loaded));
        loaded.TalkLibrary = UrquanMeetingTests.Library.Value;
        loaded.Step();
        Assert.Equal((VisitStep.StoryClosing, "blackur"), (loaded.TalkFor("h")!.Step, loaded.TalkFor("h")!.Script));
    }

    [ContentFact]
    public void StartDateSaysTheGameIsLostAndItsEndComesAsTheGameStarts()
    {
        var systems = SystemGroupTests.Systems.Value;
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        host.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        OnTheEve(host.World.Story!, days: 3);
        Assert.Equal("Starting on 2155-02-21: 4 days passed, 1 event run (KOHR_AH_GENOCIDE_EVENT); the Kohr-Ah have destroyed every race: the game is lost",
            host.StartDate("2155-02-21"));
        Assert.Null(host.SaveJson());
        host.Step(ShipInput.None);
        Assert.Equal((VisitStep.StoryClosing, "blackur"), (host.World.TalkFor("h")!.Step, host.World.TalkFor("h")!.Script));
        Assert.True(host.StoryClosingDone("h", host.World.Pause!.Round));
        host.Step(ShipInput.None);
        Assert.True(host.World.GameOver);
        var notice = Assert.Single(host.TakeBattleNotices().Host, n => n.Kind == BattleNoticeKind.GameOver);
        Assert.Equal("Game over: the Kohr-Ah have destroyed every race.", notice.Describe());
    }
}
