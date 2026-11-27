// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Story pauses generalised (stage 3c item 2): a time skip any sequence
/// can take, a story talk shown on everyone's screen outside the starbase,
/// or both, everyone waiting; the skip's days run their events and roll no
/// hyperspace encounter (the plan's question 3). The starbase's two weeks
/// (the same skip) are <see cref="OpeningTests"/>'.
/// </summary>
[Trait("Topic", "story")]
public class StoryPauseTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // The host and a guest, the guest in hyperspace in the Ur-Quan's sphere with every race's groups kept.
    private static GameWorld NewGame(bool talks = false)
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("g", "Guest"));
        world.UndockShown("g");
        if (talks)
            world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var guest = world.Find("g")!;
        guest.Ship.Ip = null;
        guest.Ship.SystemStar = null;
        guest.Ship.Flight.PlaceAtUniverse(5800, 6100);
        guest.Ship.Flight.StarJustLeft = null;
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.Story.Set("STARBASE_AVAILABLE", 1);
        return world;
    }

    private static void Run(GameWorld world, int frames, ShipInput input = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, input);
            world.Step();
        }
    }

    private static (int, int, int) Date(GameWorld world) => (world.Clock.Year, world.Clock.Month, world.Clock.Day);

    [Fact]
    public void ASkipIsAStoryPauseForEveryoneItsDaysRunTheirEventsAndRollNoEncounters()
    {
        var world = NewGame();
        var story = world.Story!;
        Run(world, 1);
        var guest = world.Find("g")!;
        var at = (guest.Ship.Flight.UniverseX, guest.Ship.Flight.UniverseY);
        uint seed = world.HyperspaceRandomOf("g").Seed;
        int rolls = world.HyperspaceEncounterDays;
        var date = Date(world);
        // The Shofixti come back on the skip's second day.
        Assert.True(story.Events.Add(true, 0, 2, 0, "SHOFIXTI_RETURN_EVENT", world.Clock));
        world.Clock.FramesIntoDay = 50;
        int ended = 0;
        var pause = world.StartStoryPause(world.Host, "a test's skip", 3, then: p => ++ended)!;
        Assert.NotNull(pause);
        Assert.Same(pause, world.Pause);
        Assert.Same(pause, world.StoryShown);
        Assert.True(world.StoryPaused);
        Assert.Equal(VisitStep.TimePassing, pause.Step);
        // Everyone is shown it, the fade with no talk before it, its starter as its replier; the snapshot says so.
        foreach (var id in new[] { "h", "g" })
        {
            var view = world.TalkFor(id)!;
            Assert.Equal((VisitStep.TimePassing, "", "h", 0), (view.Step, view.Script, view.Replier, view.Lines.Count));
        }
        Assert.Equal("h", world.Snapshot().StoryPause);
        // One at a time.
        Assert.Null(world.StartStoryPause(guest, "another", 1));
        // The fade: two seconds of frames, everyone held (the guest pressing thrust doesn't move), the clock still.
        Run(world, TimeSkip.FadeFrames - 1, ShipInput.Thrust);
        Assert.Equal((date, 1, 50), (Date(world), pause.Skip!.FadeLeft, world.Clock.FramesIntoDay));
        Assert.Equal(at, (guest.Ship.Flight.UniverseX, guest.Ship.Flight.UniverseY));
        Assert.Equal(0, ended);
        // Then the days at once (MoveGameClockDays): each runs its events, the day after starts afresh, no roll.
        Run(world, 1);
        Assert.Equal(new DateTime(date.Item1, date.Item2, date.Item3).AddDays(3),
            new DateTime(world.Clock.Year, world.Clock.Month, world.Clock.Day));
        Assert.Equal(0, world.Clock.FramesIntoDay);
        Assert.Equal(Allied.Good, story.Alliance("SHOFIXTI_SHIP"));
        Assert.Empty(world.Encounters!.List);
        Assert.Equal((seed, rolls), (world.HyperspaceRandomOf("g").Seed, world.HyperspaceEncounterDays));
        // Over: the game goes on, the sequence that took it told once.
        Assert.True(pause.Done);
        Assert.False(world.StoryPaused);
        Assert.Null(world.Pause);
        Assert.Null(world.Snapshot().StoryPause);
        Assert.Equal(1, ended);
        Assert.Null(world.TalkFor("g"));   // no talk: no closing lines to show
        Run(world, 5, ShipInput.Thrust);
        Assert.True(world.Clock.FramesIntoDay > 0);   // the clock runs again
        Assert.Equal(1, ended);
    }

    [Fact]
    public void TheHostsPauseHoldsTheFadeAndASaveIsTheGameAsItStands()
    {
        var world = NewGame();
        Run(world, 1);
        var date = Date(world);
        var pause = world.StartStoryPause(world.Host, "a test's skip", 14)!;
        Run(world, 10);
        int left = pause.Skip!.FadeLeft;
        Assert.True(world.SetHostPause(true));
        Run(world, 100);
        Assert.Equal(left, pause.Skip.FadeLeft);
        // Saved during the fade: before the days (a story pause isn't saved).
        var saved = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(date, Date(saved));
        Assert.False(saved.StoryPaused);
        Assert.True(world.SetHostPause(false));
        Run(world, left);
        Assert.False(world.StoryPaused);
        // Saved after them: with them.
        saved = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(Date(world), Date(saved));
        Assert.NotEqual(date, Date(saved));
    }

    [Fact]
    public void APauseNeedsSomethingToDo()
    {
        var world = NewGame();
        Assert.Throws<ArgumentException>(() => world.StartStoryPause(world.Host, "nothing", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSkip(0));
        // A talk needs the talks' content.
        Assert.Throws<InvalidOperationException>(() => world.StartStoryPause(world.Host, "a talk", 0, "comandr"));
        Assert.Null(world.Pause);
        Assert.False(world.StoryPaused);
    }

    [ContentFact]
    public void AStoryTalkAfterTheDaysIsShownToEveryoneAndAnsweredByItsReplier()
    {
        var world = NewGame(talks: true);
        Assert.Equal(JoinResult.Joined, world.Join("t", "Third"));
        world.UndockShown("t");
        Run(world, 1);
        var guest = world.Find("g")!;
        int ended = 0;
        // A test's sequence: two days, then the commander's talk (story by its rule, decision 6) with the guest
        // replying, his screen fading in from the skip's black.
        var intro = new CommIntro(CommIntroMode.FadeInScreen, Talk.OneSecond * 2);
        var pause = world.StartStoryPause(guest, "a test's talk", 2, "comandr", new() { ["STARBASE_VISITED"] = 0 },
            intro: intro, then: p => ++ended)!;
        Assert.Null(pause.Talk);
        Run(world, TimeSkip.FadeFrames);
        Assert.Equal(VisitStep.Story, pause.Step);
        Assert.True(world.StoryPaused);
        var talk = pause.Talk!;
        Assert.True(talk.Replies.Count > 0);
        foreach (var id in new[] { "h", "g", "t" })
        {
            var view = world.TalkFor(id)!;
            Assert.Equal((VisitStep.Story, "comandr", "g"), (view.Step, view.Script, view.Replier));
            Assert.Equal(intro, view.Screen!.Intro);   // the view its screen is drawn in
        }
        Assert.Equal("g", world.Snapshot().StoryPause);
        // Only its replier answers it, and pages it for the others; a story talk takes no lock.
        int round = pause.Round, lines = world.TalkFor("h")!.LinesHash;
        Assert.False(world.ChooseReply("h", 0, round));
        Assert.False(world.PageTalk("h", round, lines, TalkPage.Skip));
        Assert.True(world.PageTalk("g", round, lines, TalkPage.Skip));
        Assert.Equal(new[] { TalkPage.Skip }, world.TalkFor("t")!.PagingSoFar);
        Assert.True(world.ChooseReply("g", 0, round));
        if (!talk.Ended)
            Assert.Null(world.TalkFor("t")!.Screen!.Intro);   // the views after the first: already in
        // The guest leaves: the replies pass to the next player, the host first.
        world.Leave("g");
        Assert.Same(world.Host, pause.Replier);
        for (int i = 0; i < 100 && !talk.Ended; ++i)
            Assert.True(world.ChooseReply("h", 0, pause.Round));
        Assert.True(talk.Ended);
        // Over: the game goes on, those who watched shown its closing lines, the sequence told once.
        Assert.False(world.StoryPaused);
        Assert.Null(world.Pause);
        Assert.Equal(1, ended);
        Assert.Equal(VisitStep.Done, world.TalkFor("t")!.Step);
        Assert.True(pause.WasStory);
    }

    [ContentFact]
    public void ATalkOnlyPauseStartsAtOnceAndHoldsOffTheStarbase()
    {
        var world = NewGame(talks: true);
        var date = Date(world);
        var pause = world.StartStoryPause(world.Host, "a test's talk", 0, "comandr", new() { ["STARBASE_VISITED"] = 0 })!;
        Assert.Equal(VisitStep.Story, pause.Step);
        Assert.Null(pause.Skip);
        Assert.Equal(new CommIntro(CommIntroMode.CrossfadeSpace, 0), world.TalkFor("g")!.Screen!.Intro);   // the default
        // The starbase's opening waits: no visit starts under a story pause.
        world.PutInOrbit("h", 82, 2, 1);   // Luna
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.Story!.Set("STARBASE_AVAILABLE", 0);
        world.VisitStarbase(world.Host);
        Assert.Null(world.Visit);
        Assert.Same(pause, world.StoryShown);
        Run(world, 100);
        Assert.Equal(date, Date(world));   // the clock waits for the story
        for (int i = 0; i < 100 && world.Pause is not null; ++i)
            Assert.True(world.ChooseReply("h", 0, pause.Round));
        Assert.Null(world.Pause);
    }

    [ContentFact]
    public void APausesTalkMustBeStory()
    {
        // The Spathi met in space are ordinary (decision 6): no story pause shows them.
        var world = NewGame(talks: true);
        Assert.Throws<InvalidOperationException>(() => world.StartStoryPause(world.Host, "not story", 0, "spathi"));
        Assert.Null(world.Pause);
        Assert.False(world.StoryPaused);
    }

    // ------------------------------------------------------------ review round 1

    // NewGame with the test's talks for meetings (a line, one reply, then the end, MeetingTests.TestTalk: peaceful
    // from init, so a meeting goes to its talk at once) and the dialogue locks, no day's roll meanwhile.
    private static GameWorld WithMeetings(bool talks = false)
    {
        var world = NewGame(talks);
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.Clock.FramesIntoDay = -100000;
        world.MeetingTalks = (_, _, seed) => MeetingTests.TestTalk(world.Story!, Segue.Peace, Segue.Peace, seed).Talk;
        return world;
    }

    // The guest in its meeting's talk with a Spathi group, its reply offered.
    private static Meeting InMeetingTalk(GameWorld world)
    {
        var f = world.Find("g")!.Ship.Flight;
        int race = world.Story!.Rules.RaceIndex("SPATHI_SHIP");
        var e = new Hyperspace.HyperEncounter
        {
            Race = race, Roller = "g", OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        e.Ships.Add(world.Story.Rules.RaceShips[race].Crew);
        world.Encounters!.List.Add(e);
        for (int i = 0; i < 300 && world.MeetingOf("g") is not { Step: MeetingStep.Talk, Talk.Replies.Count: > 0 }; ++i)
            Run(world, 1);
        var m = world.MeetingOf("g")!;
        Assert.Equal(MeetingStep.Talk, m.Step);
        return m;
    }

    [Fact]
    public void AReplyToTheRepliersMeetingDuringASkipIsHeldAndMadeAsThePauseEnds()
    {
        // Round 1: during a skip there's no story talk to answer, so a reply for the replier's own meeting goes
        // to it, held through the pause (it went to the pause, which has no talk, and was dropped).
        var world = WithMeetings();
        var m = InMeetingTalk(world);
        var pause = world.StartStoryPause(world.Find("g")!, "a test's skip", 2)!;
        Assert.Equal(pause.FirstRound, m.Round);   // the same number: the reply must still go to the meeting
        Assert.True(world.ChooseReply("g", 0, m.Round));
        Assert.Equal(new[] { ("g", 0, (int?)m.Round) }, world.HeldReplies);
        Assert.Empty(m.Talk!.Events.OfType<ReplyChosen>());
        Run(world, TimeSkip.FadeFrames + 1);
        Assert.Null(world.Pause);
        Assert.Single(m.Talk.Events.OfType<ReplyChosen>());
        Assert.True(m.Talk.Ended);
    }

    [ContentFact]
    public void AReplyToTheMeetingOfAVisitsReplierDuringTheTwoWeeksIsHeldToo()
    {
        // Round 1: the visit straight into the two weeks, the replies passed (the host gone) to the guest in its
        // meeting's talk: its reply goes to its meeting, held, and made once the story's over.
        var world = WithMeetings(talks: true);
        var m = InMeetingTalk(world);
        world.PutInOrbit("h", 82, 2, 1);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;
        Assert.Equal(VisitStep.TimePassing, visit.Step);
        world.Leave("h");
        Assert.Equal("g", visit.Replier.Id);
        Assert.True(world.ChooseReply("g", 0, m.Round));
        Assert.Single(world.HeldReplies);
        Run(world, StarbaseVisit.TimePassingFrames);
        Assert.Equal(VisitStep.StarbaseReady, visit.Step);
        Assert.Empty(m.Talk!.Events.OfType<ReplyChosen>());   // still held: the commander's talk is story
        var goodbye = Scripts["starbas"].PhraseNumber("goodbye_commander");
        for (int i = 0; i < 20 && world.Visit is not null; ++i)
        {
            var talk = visit.Talk!;
            int r = talk.Replies.Select(x => x.Phrase).ToList().IndexOf(goodbye);
            Assert.True(world.ChooseReply("g", Math.Max(r, 0), visit.Round));
        }
        Assert.Null(world.Visit);
        Run(world, 1);
        Assert.Single(m.Talk.Events.OfType<ReplyChosen>());
    }

    [Fact]
    public void TheGameOverDuringTheDaysEndsThePauseThere()
    {
        // Round 1: UQM's skip returns at once if its days end the game (starbase.c:494): no talk, nothing after.
        var world = NewGame();
        int then = 0;
        world.DayForTests = () =>
        {
            if (world.Clock.Day == 18)
                world.LoseTheGameForTests();
        };
        var pause = world.StartStoryPause(world.Host, "a test's skip", 3, then: _ => ++then)!;
        Run(world, TimeSkip.FadeFrames);
        Assert.True(world.GameOver);
        Assert.True(pause.Skip!.Skipped);
        Assert.Null(pause.Talk);
        Assert.Equal(0, then);
        Run(world, 10);
        Assert.Equal(0, then);
    }

    [ContentFact]
    public void TheGameOverDuringTheDaysStartsNoTalk()
    {
        // The same with a talk to come (the pause's) and the starbase's two weeks (the allied commander).
        var world = NewGame(talks: true);
        world.DayForTests = () => world.LoseTheGameForTests();
        var pause = world.StartStoryPause(world.Host, "a test's talk", 2, "comandr", new() { ["STARBASE_VISITED"] = 0 })!;
        Run(world, TimeSkip.FadeFrames);
        Assert.True(world.GameOver);
        Assert.Empty(pause.Talks);

        world = NewGame(talks: true);
        world.PutInOrbit("h", 82, 2, 1);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;
        world.DayForTests = () => world.LoseTheGameForTests();
        Run(world, StarbaseVisit.TimePassingFrames);
        Assert.True(world.GameOver);
        Assert.Empty(visit.Talks);
        Assert.Equal(VisitStep.TimePassing, visit.Step);
    }

    [ContentFact]
    public void APlayerInTheirMeetingsTalkIsShownThePausesClosingLinesBeforeItAgain()
    {
        // Round 1: as the visit's: the story's closing lines kept for the frame after its end, then the meeting's talk.
        var world = WithMeetings(talks: true);
        var m = InMeetingTalk(world);
        var pause = world.StartStoryPause(world.Host, "a test's talk", 0, "comandr", new() { ["STARBASE_VISITED"] = 0 })!;
        Assert.Equal(VisitStep.Story, world.TalkFor("g")!.Step);
        for (int i = 0; i < 100 && world.Pause is not null; ++i)
            Assert.True(world.ChooseReply("h", 0, pause.Round));
        Assert.Null(world.Pause);
        var shown = world.TalkFor("g")!;
        Assert.Equal((VisitStep.Done, "comandr"), (shown.Step, shown.Script));
        Run(world, 1);
        Assert.Equal((VisitStep.Done, "comandr"), (world.TalkFor("g")!.Step, world.TalkFor("g")!.Script));
        Run(world, 1);
        Assert.Equal(VisitStep.Meeting, world.TalkFor("g")!.Step);
        Assert.Same(m, world.MeetingOf("g"));
    }
}
