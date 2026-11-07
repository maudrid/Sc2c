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
using Crateris.Core.Planetside;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.Session;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// UQM's opening at Earth (docs/plans/stage-3.md, item 5): the Lunar base
/// on Luna (gensol.c), and the visit to the starbase that runs the
/// commander, the Ilwrath and the alliance (starbase.c's VisitStarBase).
/// </summary>
[Trait("Topic", "opening")]
public class OpeningTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82, Earth = 2, Luna = 1;
    private static readonly WorldId Moon = new(Sol, Earth, Luna);

    // The Lunar base's place on the planetside map (GenerateSol_generateEnergy, at MAG_SHIFT).
    private const int BaseX = SurfaceMap.Width * 3 / 4 << PlanetsideMap.MagShift, BaseY = SurfaceMap.Height / 4 << PlanetsideMap.MagShift;

    private static GameWorld NewWorld(bool guest = false)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        if (guest)
        {
            Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
            world.UndockShown("guest");
        }
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

    // Lands and drives a player's lander onto the Lunar base (it comes down
    // a little off its site, as UQM's does), until its report shows.
    private static void DriveToTheBase(GameWorld world, string id)
    {
        var lander = world.Find(id)!.Ship.Lander!;
        for (int i = 0; i < 2000 && lander.Report is null; ++i)
        {
            ShipInput keys = ShipInput.None;
            if (lander.Phase == LanderPhase.Ground)
            {
                int dx = BaseX - lander.X, dy = BaseY - lander.Y;
                int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)dx, (short)dy));
                int turn = (want - lander.Facing) & (Uqm.UqmMath.Facings - 1);
                keys = turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right;
            }
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? keys : ShipInput.None);
            world.Step();
        }
    }

    [ContentFact]
    public void TheLunarBaseIsOnLunaUntilTaken()
    {
        var world = NewWorld();
        world.PutInOrbit("host", Sol, Earth, Luna);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 10, 10));
        var surface = world.SurfaceOf(Moon)!;
        var node = Assert.Single(surface.Elements, e => e.Kind == SurfaceElementKind.Energy);
        Assert.Equal((BaseX, BaseY, SurfaceSheet.Moonbase, ScanType.Energy, 0), (node.X, node.Y, node.Sheet, node.Scan, node.Node));
        // GeneratePlanetSide's order: the creatures, the base, the minerals.
        var kinds = surface.Elements.Select(e => e.Kind).ToList();
        Assert.True(kinds.LastIndexOf(SurfaceElementKind.Creature) < kinds.IndexOf(SurfaceElementKind.Energy)
            && kinds.IndexOf(SurfaceElementKind.Energy) < kinds.IndexOf(SurfaceElementKind.Mineral));
        // It animates through its sheet as a creature does, a frame every five.
        int before = node.Frame;
        Run(world, 10);
        Assert.NotEqual(before, node.Frame);
    }

    [ContentFact]
    public void TakingTheLunarBaseShowsItsReportAndSendsTheLanderUp()
    {
        var world = NewWorld();
        world.PutInOrbit("host", Sol, Earth, Luna);
        var hold = world.Host.Ship.Hold;
        int crew = hold.Crew;
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", BaseX, BaseY));
        var lander = world.Host.Ship.Lander!;
        DriveToTheBase(world, "host");
        Assert.Equal(new LanderReport(StoryWorlds.MoonbaseReport), lander.Report);
        Assert.Equal((1, 1), (world.Story!.Get("MOONBASE_DESTROYED"), world.Story.Get("MOONBASE_ON_SHIP")));
        Assert.True(world.Planets.IsRetrieved(Moon, ScanType.Energy, 0));
        // It goes up by itself and comes back aboard with its crew.
        for (int i = 0; i < 400 && world.Host.Ship.Lander is not null; ++i)
            Run(world, 1);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Equal(crew, hold.Crew);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        // The next landing finds no base.
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 10, 10));
        Assert.DoesNotContain(world.SurfaceOf(Moon)!.Elements, e => e.Kind == SurfaceElementKind.Energy);
    }

    [ContentFact]
    public void AnotherLanderOnLunaStaysAndIsTold()
    {
        var world = NewWorld(guest: true);
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("guest", 40, 40));
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", BaseX, BaseY));
        var host = world.Host.Ship.Lander!;
        var guest = world.Find("guest")!.Ship.Lander!;
        DriveToTheBase(world, "host");
        Assert.Equal(new LanderReport(StoryWorlds.MoonbaseReport, "host"), guest.Report);
        Run(world, 100);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Equal(LanderPhase.Ground, guest.Phase);
    }

    // ------------------------------------------------------------ the visit

    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));

    private static GameWorld WithTalks(bool guest = false)
    {
        var world = NewWorld(guest);
        world.TalkLibrary = Library.Value;
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Host.Ship.Ip!.ReturnFromOrbit();   // in Earth's inner system, by the starbase
        return world;
    }

    // The starter's reply by its phrase id.
    private static void Say(GameWorld world, string script, string phrase)
    {
        var talk = world.Visit!.Talk!;
        int n = Scripts[script].PhraseNumber(phrase);
        int i = talk.Replies.Select(r => r.Phrase).ToList().IndexOf(n);
        Assert.True(i >= 0, $"{phrase} isn't offered: {string.Join(", ", talk.Replies.Select(r => r.Phrase))}");
        Assert.True(world.ChooseReply(world.Visit.Replier.Id, i));
    }

    [ContentFact]
    public void TheFirstVisitIsTheCommandersTalkAndTheGroupHasMetHim()
    {
        var world = WithTalks();
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;
        Assert.Equal((VisitStep.Commander, Activity.Talk), (visit.Step, world.Host.Ship.Activity));
        Assert.False(world.CommanderMet);
        Say(world, "comandr", "yes_this_is_supply_ship");
        Say(world, "comandr", "i_lied");
        Say(world, "comandr", "ok_i_will_get_radios");
        Assert.Null(world.Visit);
        Assert.Null(world.Host.Ship.Visit);
        Assert.True(world.CommanderMet);   // the first visit's STARBASE_VISITED, the group's
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
        Assert.Equal(0, world.Story!.Get("RADIOACTIVES_PROVIDED"));
        // The next visit is "again": no first meeting.
        world.VisitStarbase(world.Host);
        Assert.Contains(world.Visit!.Talk!.Events.OfType<StateSet>(), e => e.Field == "BATTLE_SEGUE");
        Assert.DoesNotContain(world.Visit.Talk.Events.OfType<StateSet>(), e => e.Field == "STARBASE_VISITED");
    }

    [ContentFact]
    public void TheOpeningRunsFromTheBaseReportedToTheStarbaseReady()
    {
        var world = WithTalks(guest: true);
        var story = world.Story!;
        story.Set("RADIOACTIVES_PROVIDED", 1);
        story.Set("MOONBASE_DESTROYED", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        world.CommanderMet = true;
        var date = (world.Clock.Year, world.Clock.Month, world.Clock.Day);
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;

        Say(world, "comandr", "base_was_abandoned");
        Assert.Equal(VisitStep.Ilwrath, visit.Step);       // HERE_COMES_ILWRATH
        Say(world, "ilwrath", "surrender");
        Assert.Equal(Segue.Hostile, visit.Talks[1].Segue);  // its battle counts as won (decision 3)
        Assert.Equal(VisitStep.Alliance, visit.Step);
        Say(world, "comandr", "cook_their_butts");
        Say(world, "comandr", "name_2");
        Assert.Equal((1, 1, 0), (story.Get("STARBASE_AVAILABLE"), story.Get("NEW_ALLIANCE_NAME"), story.Get("PROBE_ILWRATH_ENCOUNTER")));

        // Two weeks pass, as the screen fades over two seconds.
        Assert.Equal(VisitStep.TimePassing, visit.Step);
        Run(world, StarbaseVisit.TimePassingFrames - 1);
        Assert.Equal(VisitStep.TimePassing, visit.Step);
        Run(world, 1);
        Assert.Equal(VisitStep.StarbaseReady, visit.Step);
        var now = new DateTime(date.Year, date.Month, date.Day).AddDays(StarbaseVisit.LostDays);
        Assert.Equal((now.Year, now.Month, now.Day), (world.Clock.Year, world.Clock.Month, world.Clock.Day));
        Assert.Equal((now.Month, now.Day), (story.Get("STARBASE_MONTH"), story.Get("STARBASE_DAY")));

        Say(world, "starbas", "goodbye_commander");
        Assert.Null(world.Visit);
        Assert.Equal(0, story.Get("MOONBASE_ON_SHIP"));     // post_starbase_enc
        // The 2,500 resource units, split between the two players (decision 5).
        Assert.Equal((1250u, 1250u), (world.Host.Ship.Hold.ResourceUnits, world.Find("guest")!.Ship.Hold.ResourceUnits));
        // The allied starbase's menu is item 8's: the next visit has nothing to tell.
        world.VisitStarbase(world.Host);
        Assert.Null(world.Visit);
    }

    [ContentFact]
    public void ASaveDuringAVisitIsOfTheGameBeforeItAndLeavingUndoesIt()
    {
        var world = WithTalks();
        var story = world.Story!;
        world.Host.Ship.Flight.Fuel = 150;
        world.VisitStarbase(world.Host);
        Say(world, "comandr", "yes_this_is_supply_ship");
        Say(world, "comandr", "i_lied");
        Say(world, "comandr", "need_fuel_mercury");      // 5 units given, GIVEN_FUEL_BEFORE set
        Assert.Equal(650u, world.Host.Ship.Flight.Fuel);
        Assert.Equal(1, story.Get("GIVEN_FUEL_BEFORE"));
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        Assert.Equal(150u, loaded.Host.Ship.Flight.Fuel);
        Assert.Equal(0, loaded.Story!.Get("GIVEN_FUEL_BEFORE"));
        Assert.False(loaded.CommanderMet);
        Assert.Null(loaded.Visit);

        world.Leave("host");
        Assert.Null(world.Visit);
        Assert.Equal(150u, world.Host.Ship.Flight.Fuel);
        Assert.Equal(0, world.Story!.Get("GIVEN_FUEL_BEFORE"));
        Assert.False(world.CommanderMet);
    }

    [ContentFact]
    public void OneStoryVisitAtATimeAndNoneWithoutTheTalks()
    {
        var world = WithTalks(guest: true);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        world.Find("guest")!.Ship.Ip!.ReturnFromOrbit();
        world.VisitStarbase(world.Host);
        world.VisitStarbase(world.Find("guest")!);
        Assert.Equal(world.Host, world.Visit!.Starter);
        Assert.Null(world.Find("guest")!.Ship.Visit);
        Assert.False(world.ChooseReply("guest", 0));
        Assert.False(world.ChooseReply("host", 9));

        var bare = NewWorld();
        bare.PutInOrbit("host", Sol, Earth, Luna);
        bare.VisitStarbase(bare.Host);
        Assert.Null(bare.Visit);
    }

    [ContentFact]
    public void TheCommandersGiftsGoToTheStartersShipWithinWhatItHolds()
    {
        var world = WithTalks();
        var hold = world.Host.Ship.Hold;
        // A new flagship's one crew pod holds 50; an escort's kit 10.
        Assert.Equal(50, hold.CrewCapacity);
        Assert.Equal(10, new GameSettings().EscortLoadout.CrewCapacity);
        hold.LanderLeaves();
        hold.LanderReturns(0, new int[8], 0);   // the lander lost, and its crew
        Assert.Equal(0, hold.Landers);
        world.VisitStarbase(world.Host);
        Say(world, "comandr", "yes_this_is_supply_ship");
        Say(world, "comandr", "i_lied");
        Say(world, "comandr", "i_lost_my_lander");
        Assert.Equal((1, 42), (hold.Landers, hold.Crew));   // 38 + 4
    }

    [ContentFact]
    public void FlyingIntoEarthsStarbaseStartsTheVisit()
    {
        var world = WithTalks();
        var ship = world.Host.Ship;
        // Off Luna, steer at the starbase (Earth's first moon) and thrust.
        for (int i = 0; i < 24 * 60 && ship.Visit is null; ++i)
        {
            var ip = ship.Ip!;
            var target = ip.MoonScreen[0];
            int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)(target.X - ip.Screen.X), (short)(target.Y - ip.Screen.Y)));
            int turn = (want - ip.Facing) & (Uqm.UqmMath.Facings - 1);
            var keys = turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right;
            world.SubmitInput("host", world.Frame + 1, keys);
            world.Step();
        }
        Assert.Equal(VisitStep.Commander, ship.Visit?.Step);
        Assert.Equal(Activity.Talk, ship.Activity);
        Assert.Null(ship.Ip!.Orbit);   // in flight by the starbase
    }

    [ContentFact]
    public void AGuestIsSentItsTalkAndItsReplyComesBack()
    {
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Zelnick",
            Systems, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.Hello(1, "guest", "Guest", Net.Protocol.Version);
        host.World.UndockShown("guest");
        host.World.PutInOrbit("guest", Sol, Earth, Luna);
        var guest = host.World.Find("guest")!;
        guest.Ship.Ip!.ReturnFromOrbit();
        Assert.Empty(host.TalkUpdates());
        host.World.VisitStarbase(guest);

        var (peer, data) = Assert.Single(host.TalkUpdates());
        Assert.Equal(1, peer);
        var view = TalkView.Decode(data)!;
        Assert.Equal((VisitStep.Commander, "comandr", 3), (view.Step, view.Script, view.Replies.Count));
        Assert.Single(view.Lines);                 // ARE_YOU_SUPPLY_SHIP
        Assert.Contains("Wayfarer", view.Replies[0]); // the escort's name, spliced in
        Assert.Empty(host.TalkUpdates());          // sent once
        Assert.Equal("guest", host.TalkFor("host")!.Replier);   // a story talk: the host watches it

        Assert.Equal(1, view.Round);
        host.GuestChooseReply(1, view.Round, 1);   // yes_this_is_supply_ship
        view = TalkView.Decode(Assert.Single(host.TalkUpdates()).Data)!;
        Assert.Equal((2, 2), (view.Round, view.Replies.Count));   // i_lied, plumb_out
        Assert.Single(view.Lines);                 // ABOUT_TIME: the lines since the reply
        host.GuestChooseReply(1, 1, 0);            // a reply for the set already answered: ignored
        host.GuestChooseReply(1, 2, view.Replies.Count);   // no such reply: ignored
        Assert.Empty(host.TalkUpdates());
        host.GuestChooseReply(1, 2, 0);
        view = TalkView.Decode(Assert.Single(host.TalkUpdates()).Data)!;
        host.GuestChooseReply(1, view.Round, IndexOf(host, "guest", "ok_i_will_get_radios"));
        // Over: its closing line (THANKS_FOR_HELPING) still goes, with no replies.
        view = TalkView.Decode(Assert.Single(host.TalkUpdates()).Data)!;
        Assert.Equal((VisitStep.Done, 1, 0), (view.Step, view.Lines.Count, view.Replies.Count));
        Assert.Null(host.World.Visit);
        Assert.Null(TalkView.Decode(Array.Empty<byte>()));
    }

    private static int IndexOf(Session.HostSession host, string id, string phrase)
    {
        int n = Scripts["comandr"].PhraseNumber(phrase);
        return host.World.Find(id)!.Ship.Visit!.Talk!.Replies.Select(r => r.Phrase).ToList().IndexOf(n);
    }

    [Fact]
    public void ATalkViewRoundTripsAndBadOnesAreRefused()
    {
        var view = new TalkView(VisitStep.Alliance, "comandr", new[] { "Hello", "Zoë!" }, new[] { "a", "b" }, 7);
        Assert.True(view.Same(TalkView.Decode(view.Encode())));
        Assert.False(view.Same(view with { Round = 8 }));
        Assert.Throws<InvalidDataException>(() => TalkView.Decode(new byte[] { 99 }));
        Assert.Throws<InvalidDataException>(() => TalkView.Decode(view.Encode().Append((byte)0).ToArray()));   // bytes after its end
        var big = new Net.WireWriter();
        big.UInt((1UL << 32) + 1);   // a step that only cutting it down would make Commander
        Assert.Throws<InvalidDataException>(() => TalkView.Decode(big.Written.ToArray()));
        var w = new Net.WireWriter();
        w.UInt(1);
        w.String("x");
        w.UInt(0);   // round
        w.UInt(0);
        w.UInt(9);   // more replies than a talk has
        Assert.Throws<InvalidDataException>(() => TalkView.Decode(w.Written.ToArray()));
    }

    [ContentFact]
    public void AStoryTalkPausesEverythingAndEveryoneWatches()
    {
        // A guest's lander is down on Luna and a third player flies when
        // the host starts the commander's talk: everything waits for it.
        var world = WithTalks(guest: true);
        Assert.Equal(JoinResult.Joined, world.Join("third", "Third"));
        world.UndockShown("third");
        world.PutInOrbit("guest", Sol, Earth, Luna);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("guest", 40, 40));
        Run(world, 60);
        var lander = world.Find("guest")!.Ship.Lander!;
        var third = world.Find("third")!.Ship.Ip!;   // undocked by the host, in Earth's inner system
        var date = (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay);
        world.VisitStarbase(world.Host);
        Assert.True(world.StoryPaused);
        Assert.Equal("host", world.Snapshot().StoryPause);
        var before = (lander.X, lander.Y, lander.Phase, third.Location.X, third.Location.Y);
        for (int i = 0; i < 100; ++i)
        {
            world.SubmitInput("guest", world.Frame + 1, ShipInput.Thrust | ShipInput.Fire);
            world.SubmitInput("third", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        Assert.Equal(before, (lander.X, lander.Y, lander.Phase, third.Location.X, third.Location.Y));
        Assert.Equal(date, (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay));
        Assert.Equal(DispatchRefusal.StoryPause, world.Dispatch("third", 10, 10));
        Assert.False(world.DiscardCargo("guest", 0));
        // Only the starter replies; everyone is shown the talk.
        Assert.False(world.ChooseReply("guest", 0));
        Assert.Same(world.Visit, world.VisitOf("guest"));
        Assert.Same(world.Visit, world.VisitOf("third"));
        Say(world, "comandr", "yes_this_is_supply_ship");
        Say(world, "comandr", "i_lied");
        Say(world, "comandr", "ok_i_will_get_radios");
        Assert.False(world.StoryPaused);
        Assert.Null(world.Snapshot().StoryPause);
        // And the game goes on: the third player's ship flies again.
        for (int i = 0; i < 24; ++i)
        {
            world.SubmitInput("third", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        Assert.NotEqual((before.Item4, before.Item5), (third.Location.X, third.Location.Y));
        // Its closing lines go to everyone who watched.
        Assert.Equal(VisitStep.Done, world.VisitOf("third")!.Step);
    }

    [ContentFact]
    public void TheRepliesPassToTheNextPlayerWhenTheirReplierLeaves()
    {
        var world = WithTalks(guest: true);
        Assert.Equal(JoinResult.Joined, world.Join("third", "Third"));
        world.UndockShown("third");
        var guest = world.Find("guest")!;
        world.PutInOrbit("guest", Sol, Earth, Luna);
        guest.Ship.Ip!.ReturnFromOrbit();
        world.VisitStarbase(guest);
        Assert.Equal(guest, world.Visit!.Replier);
        Say(world, "comandr", "yes_this_is_supply_ship");
        // The guest goes: the host, first in join order, replies now; the
        // talk is still the guest's (their ship, their names).
        world.Leave("guest");
        Assert.Equal(world.Host, world.Visit!.Replier);
        Assert.Equal(guest, world.Visit.Starter);
        Assert.False(world.ChooseReply("third", 0));
        Say(world, "comandr", "i_lied");
        world.Leave("host");
        Assert.Equal("third", world.Visit!.Replier.Id);
        Say(world, "comandr", "ok_i_will_get_radios");
        Assert.Null(world.Visit);
        Assert.True(world.CommanderMet);
        // A player who joins during a story talk watches it.
        Assert.Equal(JoinResult.Rejoined, world.Join("guest", "Guest"));
        world.VisitStarbase(world.Find("third")!);
        Assert.Equal(JoinResult.Rejoined, world.Join("host", "Host"));
        Assert.Same(world.Visit, world.VisitOf("host"));
        // With no one left to reply, the talk is undone.
        world.Leave("host");
        world.Leave("guest");
        world.Leave("third");
        Assert.Null(world.Visit);
    }

    [ContentFact]
    public void TimePassingIsShownToEveryone()
    {
        var world = WithTalks(guest: true);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Host);
        var view = TalkView.Of(world.VisitOf("guest"))!;
        Assert.Equal((VisitStep.TimePassing, "host"), (view.Step, view.Replier));
        Assert.True(world.StoryPaused);
    }

    [ContentFact]
    public void TheGiftIsSplitBetweenThePlayersInTheGameTheStarterKeepingTheRest()
    {
        var world = WithTalks(guest: true);
        Assert.Equal(JoinResult.Joined, world.Join("third", "Third"));
        world.UndockShown("third");
        Assert.Equal(JoinResult.Joined, world.Join("away", "Away"));
        world.Leave("away");
        var story = world.Story!;
        story.Set("STARBASE_AVAILABLE", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Host);
        Assert.Equal(VisitStep.TimePassing, world.Visit!.Step);
        world.Clock.FramesIntoDay = 50;
        Run(world, StarbaseVisit.TimePassingFrames);
        Assert.Equal(0, world.Clock.FramesIntoDay);   // MoveGameClockDays: the day starts afresh (and the clock waits for the story)
        Say(world, "starbas", "goodbye_commander");
        // 2,500 between three: 833 each, the starter keeping the one left.
        Assert.Equal((834u, 833u, 833u, 0u), (world.Host.Ship.Hold.ResourceUnits, world.Find("guest")!.Ship.Hold.ResourceUnits,
            world.Find("third")!.Ship.Hold.ResourceUnits, world.Find("away")!.Ship.Hold.ResourceUnits));
    }

    [ContentFact]
    public void LeavingAsTheStarbaseIsReadyTakesTheTwoWeeksBack()
    {
        var world = WithTalks();
        var story = world.Story!;
        story.Set("STARBASE_AVAILABLE", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        var date = (world.Clock.Year, world.Clock.Month, world.Clock.Day);
        world.VisitStarbase(world.Host);
        Run(world, StarbaseVisit.TimePassingFrames);
        Assert.Equal(VisitStep.StarbaseReady, world.Visit!.Step);
        Assert.Equal(2500u, world.Host.Ship.Hold.ResourceUnits);
        var saved = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        Assert.Equal(date, (saved.Clock.Year, saved.Clock.Month, saved.Clock.Day));
        Assert.Equal((0u, 0, 0), (saved.Host.Ship.Hold.ResourceUnits, saved.Story!.Get("STARBASE_MONTH"), saved.Story.Get("STARBASE_DAY")));
        world.Leave("host");
        Assert.Equal(date, (world.Clock.Year, world.Clock.Month, world.Clock.Day));
        Assert.Equal((0u, 1), (world.Host.Ship.Hold.ResourceUnits, story.Get("MOONBASE_ON_SHIP")));
    }

    [ContentFact]
    public void NoCargoGoesOverboardDuringAVisit()
    {
        var world = WithTalks();
        world.VisitStarbase(world.Host);
        Assert.False(world.DiscardCargo("host", 0));
    }

    [ContentFact]
    public void SavesKeepTheResourceUnitsAndTheMeetingAndOlderOnesAreRefusedThem()
    {
        var world = WithTalks();
        world.Host.Ship.Hold.ResourceUnits = 1234;
        world.CommanderMet = true;
        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, Systems, TestContent.Shapes);
        Assert.Equal((1234u, true), (loaded.Host.Ship.Hold.ResourceUnits, loaded.CommanderMet));
        Assert.Equal(50, loaded.Host.Ship.Hold.CrewCapacity);
        var old = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        old["version"] = 7;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes));
        old["commander_met"] = false;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes));
        old["players"]![0]!["hold"]!["resource_units"] = 0;
        Assert.Equal(0u, HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes).Host.Ship.Hold.ResourceUnits);
    }

    [ContentFact]
    public void AnOlderSavesEscortHoldsItsKitsTenCrew()
    {
        var world = WithTalks(guest: true);
        var old = System.Text.Json.Nodes.JsonNode.Parse(HostSave.ToJson(world))!;
        old["version"] = 7;
        foreach (var p in old["players"]!.AsArray())
            p!["loadout"]!.AsObject().Remove("crew_capacity");
        old["settings"]!["escort_loadout"]!.AsObject().Remove("crew_capacity");
        var loaded = HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes);
        Assert.Equal((50, 10), (loaded.Host.Ship.Hold.CrewCapacity, loaded.Find("guest")!.Ship.Hold.CrewCapacity));
        Assert.Equal(10, loaded.Settings.EscortLoadout.CrewCapacity);
    }

    [ContentFact]
    public void AWatchersKeysDoNothingAndTheRepliersChoose()
    {
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Zelnick",
            Systems, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.Hello(1, "guest", "Guest", Net.Protocol.Version);
        host.World.UndockShown("guest");
        host.World.PutInOrbit("host", Sol, Earth, Luna);
        host.World.Host.Ship.Ip!.ReturnFromOrbit();
        host.World.PutInOrbit("guest", Sol, Earth, Luna);   // in orbit: its menu waits too
        host.World.VisitStarbase(host.World.Host);
        var me = new Session.PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        var watcher = new Session.PlayerSession(Systems, null, "guest", TestContent.Shapes.HyperFlagship.Count);
        var snap = host.Step(ShipInput.None);
        me.Observe(snap);
        watcher.Observe(snap);
        me.ShowTalk(host.TalkFor("host"));
        watcher.ShowTalk(TalkView.Decode(host.TalkUpdates().Single().Data));
        Assert.True(me.StoryPaused && watcher.StoryPaused);
        Assert.True(me.Replying);
        Assert.False(watcher.Replying);
        Assert.Contains(watcher.StatusLines(), l => l.Contains("Zelnick replies"));
        Assert.Equal(ShipInput.None, watcher.NextInput(ShipInput.Thrust));
        watcher.PressMenu(MenuKey.Select);
        Assert.Null(watcher.TakeReply());
        var menu = watcher.OrbitMenu!;
        var scan = menu.Selected;
        watcher.PressMenu(MenuKey.Down);
        Assert.Equal(scan, menu.Selected);   // the orbit menu waits for the story
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);
        Assert.Equal((1, 1), me.TakeReply());
        me.PressMenu(MenuKey.Select);        // one reply a set
        Assert.Null(me.TakeReply());
    }
}
