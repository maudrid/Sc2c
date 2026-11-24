// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Orz and the Pkunk in the world (the stage 3b-2 plan's item 7): the
/// Orz homeworld's talk and the alliance a guest makes there (on sale for
/// everyone, told to everyone); the break over the Androsynth, which takes
/// every Orz ship out of the host's fleet whoever answers; the Taalo
/// protector's moon with its 14 guards: at peace, their fight, and leave
/// to land for allies (the moon orbited from then on); the Pkunk
/// homeworld's Clear Spindle given, their friendship (no alliance) with 4
/// ships to the host's fleet, and the yearly gift by date and room; their
/// groups in space; the migration's event set aside until 3c. The
/// scripts' every path is TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class OrzPkunkTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;

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

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    private static void Frames(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
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
        world.AddEncounterNear(id, Race(world, race), ships);
        return Meet(world, id);
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Offered(Talk talk, string phrase) =>
        talk.Replies.Any(r => r.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    private static void Choose(GameWorld world, string id, string phrase) =>
        Assert.True(world.ChooseReply(id, Reply(world.MeetingOf(id)!.Talk!, phrase)));

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The player retreats from their meeting's battle.
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

    [ContentFact]
    public void AGuestMakesTheOrzAllianceAtTheirHomeworldAndEveryoneIsTold()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "ORZ_DEFINED");
        world.TakeNotices();
        world.PutInOrbit("g", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("g"));
        Assert.IsType<OrzScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.True(m.Infinite);
        Assert.Equal("g", world.DialogueLocks!.Holder("ORZ_CONVERSATION"));
        // Not allied (init_orz_comm): the encounter screen first.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        // The first meeting: the hello, the homeworld's greeting, and the computer's report without the talking animation.
        Assert.True(Said(m.Talk, "INIT_HELLO"));
        Assert.True(Said(m.Talk, "NEUTRAL_HOMEWORLD_HELLO_1"));
        Assert.Contains(m.Talk.Events, e => e is TalkingAnimation { Enabled: false });
        // On every client (differences.md row 83): the screen, first drawn at that segue, opens with the
        // mouth still for the report (the first line), and it moves again as the greeting (the second) starts.
        var screen = TalkView.Of(m)!.Screen!;
        Assert.False(screen.Talking);
        Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.TalkingOn) }, screen.Changes);
        Assert.Equal((1, 1), (story.Get("MET_ORZ_BEFORE"), story.Get("ORZ_HOME_VISITS")));
        // "We are ..." with the guest's names, then the alliance offered and made.
        var names = new PlayerTalkShip(world.Find("g")!, world.TalkLibrary!.Strings, world);
        Assert.Contains(names.CaptainName, m.Talk.Replies[Reply(m.Talk, "we_are_vindicator0")].Text);
        Assert.Contains(names.ShipName, m.Talk.Replies[Reply(m.Talk, "we_are_vindicator0")].Text);
        Choose(world, "g", "we_are_vindicator0");
        Assert.True(Said(m.Talk, "NICE_TO_MEET_YOU"));
        Choose(world, "g", "seem_like_nice_guys");
        Assert.True(Said(m.Talk, "ARE_NICE_WANT_ALLY"));
        Assert.False(world.Story!.Alliance("ORZ_SHIP") == Story.Allied.Good);
        Choose(world, "g", "yes_alliance");
        Assert.True(Said(m.Talk, "GREAT"));
        Assert.Equal(3, story.Get("ORZ_MANNER"));
        Assert.Equal(Story.Allied.Good, story.Alliance("ORZ_SHIP"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.AllianceMade);
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // genorz.c returns at the homeworld while its endless fleet survives: no orbit.
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        // The allies' homeworld greeting from then on, at peace (no encounter screen).
        world.PutInOrbit("h", star, 0);
        m = world.MeetingOf("h")!;
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "ALLIED_HOMEWORLD_HELLO_2"));   // ORZ_HOME_VISITS counted on
        Assert.False(Said(m.Talk!, "INIT_HELLO"));
    }

    [ContentFact]
    public void TheAlliancesBreakTakesEveryOrzShipOutOfTheHostsFleetWhoeverAnswers()
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("MET_ORZ_BEFORE", 1);
        story.Set("ORZ_MANNER", 3);
        story.SetRaceAllied("ORZ_SHIP", true);
        world.AddFleetShips("ORZ_SHIP", 2);
        world.AddFleetShips("SPATHI_SHIP", 1);
        world.AddFleetShips("ORZ_SHIP", 1);
        world.TakeNotices();
        // An Orz group met by the guest: allies, at peace.
        var m = MeetInSystem(world, "g", "ORZ_SHIP");
        Assert.IsType<OrzScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk, "ALLIED_SPACE_HELLO_1"));
        // Asked about the Androsynth three times: the third is too much.
        Choose(world, "g", "about_andro_1");
        Assert.True(Said(m.Talk, "FORGET_ANDRO_1"));
        Choose(world, "g", "about_andro_2");
        Assert.True(Said(m.Talk, "FORGET_ANDRO_2"));
        Choose(world, "g", "about_andro_3");
        Assert.True(Said(m.Talk, "BLEW_IT"));
        Assert.Equal(2, story.Get("ORZ_MANNER"));
        Assert.Equal(Story.Allied.Bad, story.Alliance("ORZ_SHIP"));
        // Every Orz ship leaves the host's fleet (the guest has none: question 4); the Spathi stays.
        Assert.Equal((0, 1), (world.Fleet.CountOf("ORZ_SHIP"), world.Fleet.CountOf("SPATHI_SHIP")));
        Assert.Contains(m.Talk.Events, e => e is ColorTransform { Map: 1 });   // their colours turn
        // On every client: the fade in the talk's view, at BLEW_IT's line (differences.md row 83).
        Assert.Contains(TalkView.Of(m)!.Screen!.Changes, c => c is { Kind: ScreenChangeKind.Fade, Value: 1, Time: Talk.OneSecond / 2 });
        var told = world.TakeNotices();
        Assert.Contains(told, n => n.PlayerId == "h" && n.Event == NoticeEvent.AllianceBroken);
        Assert.Contains(told, n => n.PlayerId == "h" && n.Event == NoticeEvent.ShipsTaken && n.Count == 3);
        // A fight.
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "g");
        Assert.Equal(2, story.Get("ORZ_MANNER"));   // post_orz_enc leaves their hatred as it is

        // From then on: their hostile greeting, in their angry colours, and a fight.
        m = MeetInSystem(world, "h", "ORZ_SHIP");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "HOSTILE_HELLO_1"));
        Assert.Equal(1, m.Talk!.ColorMap);
        Assert.Equal(1, TalkView.Of(m)!.Screen!.ColorMap);   // the client opens the screen in their angry colours
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
    }

    [ContentFact]
    public void TheTaaloMoonsFourteenGuardsTalkAndFight()
    {
        var world = NewGame();
        var story = world.Story!;
        story.Set("MET_ORZ_BEFORE", 1);
        int star = Star(world, "TAALO_PROTECTOR_DEFINED");
        world.PutInOrbit("h", star, 1, 2);
        var m = Assert.IsType<Meeting>(world.MeetingOf("h"));
        Assert.IsType<OrzScript>(m.Talk!.Script);
        Assert.Equal(1 << 6, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.False(m.Infinite);
        Assert.Equal(14, m.Ships.Count);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "FRIENDLY_ALLIED_TAALO_HELLO_1"));
        Choose(world, "h", "why_you_here");
        Assert.True(Said(m.Talk, "FRIENDLY_EXPLANATION"));
        // Not allies: landing only for allies.
        Choose(world, "h", "may_we_land");
        Assert.True(Said(m.Talk, "ALLIES_CAN_VISIT"));
        Choose(world, "h", "bye_taalo");
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        // The guards are there still: no orbit, TAALO_UNPROTECTED not set.
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(0, story.Get("TAALO_UNPROTECTED"));

        // Angry: "let us land" is a fight against the 14.
        story.Set("ORZ_MANNER", 1);
        world.PutInOrbit("h", star, 1, 2);
        m = world.MeetingOf("h")!;
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "ANGRY_TAALO_HELLO_2"));   // TAALO_VISITS 1 already (a fight would have counted them afresh)
        Choose(world, "h", "demand_to_land");
        Assert.True(Said(m.Talk!, "NO_DEMAND"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.False(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal(14, m.Battle.EnemyFleet.Ships.Count);
        Assert.All(m.Battle.EnemyFleet.Ships, s => Assert.Equal("orz", s.Ship));
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);   // survivors: out of orbit
        Assert.Equal(0, story.Get("TAALO_UNPROTECTED"));
    }

    [ContentFact]
    public void AlliesMayLandOnTheTaaloMoonWhichIsOrbitedFromThen()
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("MET_ORZ_BEFORE", 1);
        story.Set("ORZ_MANNER", 3);
        story.SetRaceAllied("ORZ_SHIP", true);
        int star = Star(world, "TAALO_PROTECTOR_DEFINED");
        world.PutInOrbit("g", star, 1, 2);
        var m = world.MeetingOf("g")!;
        // Allies: at peace, no encounter screen.
        Assert.Equal(MeetingStep.Talk, m.Step);
        Choose(world, "g", "may_we_land");
        Assert.True(Said(m.Talk!, "SURE_LAND"));
        Assert.Equal(1, story.Get("TAALO_UNPROTECTED"));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // genorz.c falls through to the orbit: the guest is put in orbit around the moon.
        var guest = world.Find("g")!.Ship;
        Assert.Equal(Activity.Orbit, guest.Activity);
        Assert.Equal(((int, int?)?)(1, 2), guest.Ip!.Orbit);
        // And from then on it's orbited, by anyone.
        world.PutInOrbit("h", star, 1, 2);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void ThePkunkGiveTheSpindleAndTheirFriendshipWithShipsToTheHostsFleet()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "PKUNK_DEFINED");
        world.TakeNotices();
        world.PutInOrbit("g", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("g"));
        Assert.IsType<PkunkScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.True(m.Infinite);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        // The Clear Spindle given on the first visit: the group's.
        Assert.True(Said(m.Talk, "GIVE_SPINDLE"));
        Assert.Equal((1, 1), (story.Get("CLEAR_SPINDLE"), story.Get("CLEAR_SPINDLE_ON_SHIP")));
        Assert.True(Said(m.Talk, "NEUTRAL_HOMEWORLD_HELLO_1"));
        // Their friendship, with 4 ships to the host's fleet; no alliance (their ships aren't for sale).
        Choose(world, "g", "we_are_vindicator0");
        Assert.True(Said(m.Talk, "WHY_YOU_HERE"));
        Choose(world, "g", "we_here_to_help");
        Assert.True(Said(m.Talk, "ALMOST_ALLIANCE"));
        Assert.True(Said(m.Talk, "INIT_SHIP_GIFT"));
        Assert.Equal(3, story.Get("PKUNK_MANNER"));
        Assert.Equal(4, world.Fleet.CountOf("PKUNK_SHIP"));
        Assert.Equal(Story.Allied.Bad, story.Alliance("PKUNK_SHIP"));
        var told = world.TakeNotices();
        Assert.Contains(told, n => n.PlayerId == "h" && n.Event == NoticeEvent.ShipsGiven && n.Count == 4);
        Assert.DoesNotContain(told, n => n.Event == NoticeEvent.AllianceMade);
        // The next ships a year on (2156-02-17); their migration's event queued six months on, for 3c.
        Assert.Equal((2, 17, 1), (story.Get("PKUNK_SHIP_MONTH"), story.Get("PKUNK_SHIP_DAY"), story.Get("PKUNK_SHIP_YEAR")));
        Assert.Contains(story.Events, e => e == new Story.StoryEvent("ADVANCE_PKUNK_MISSION", 2155, 8, 17));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);

        // Friends now: at peace. The first visit's greeting, then before the date their second.
        world.PutInOrbit("h", star, 0);
        m = world.MeetingOf("h")!;
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "FRIENDLY_HOMEWORLD_HELLO_1"));
        Assert.False(Offered(m.Talk!, "we_are_vindicator0"));
        Choose(world, "h", "bye_friendly");
        MeetingTests.HearClosing(world, "h");
        world.PutInOrbit("h", star, 0);
        m = world.MeetingOf("h")!;
        Assert.True(Said(m.Talk!, "FRIENDLY_HOMEWORLD_HELLO_2"));
        Choose(world, "h", "bye_friendly");
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(4, world.Fleet.CountOf("PKUNK_SHIP"));

        // A year on, the yearly gift: 4 more, the next date a year further.
        world.Clock.Year = 2156;
        world.PutInOrbit("g", star, 0);
        m = world.MeetingOf("g")!;
        Assert.True(Said(m.Talk!, "SHIP_GIFT"));
        Assert.Equal(8, world.Fleet.CountOf("PKUNK_SHIP"));
        Assert.Equal(2, story.Get("PKUNK_SHIP_YEAR"));
        Choose(world, "g", "bye_friendly");
        MeetingTests.HearClosing(world, "g");
        // With the host's fleet full the next year: no room, and no new date.
        world.AddFleetShips("SPATHI_SHIP", HostFleet.MaxShips - world.Fleet.Ships.Count);
        world.Clock.Year = 2157;
        world.PutInOrbit("g", star, 0);
        m = world.MeetingOf("g")!;
        Assert.True(Said(m.Talk!, "NO_ROOM"));
        Assert.Equal((8, 2), (world.Fleet.CountOf("PKUNK_SHIP"), story.Get("PKUNK_SHIP_YEAR")));
    }

    [ContentFact]
    public void PkunkGroupsInSpaceAndTheMigrationsEventSetAside()
    {
        var world = NewGame();
        var story = world.Story!;
        // Neutral: the encounter screen, their neutral greeting.
        var m = MeetInSystem(world, "h", "PKUNK_SHIP");
        Assert.IsType<PkunkScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "NEUTRAL_SPACE_HELLO_1"));
        Choose(world, "h", "form_alliance");
        Assert.True(Said(m.Talk, "GO_TO_HOMEWORLD"));
        Choose(world, "h", "neutral_bye_space");
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);

        // Migrating (PKUNK_MISSION odd, 3c's; --start-state reaches it): a good reason queues their
        // mission's event at once, set aside until 3c runs the calendar.
        story.Set("PKUNK_MANNER", 3);
        story.Set("PKUNK_MISSION", 1);
        m = MeetInSystem(world, "h", "PKUNK_SHIP");
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "MIGRATING_SPACE_1"));
        Choose(world, "h", "good_reason_1");
        Assert.True(Said(m.Talk!, "WE_GO_HOME_1"));
        Assert.Contains(story.Events.SetAside, e => e.Event == "ADVANCE_PKUNK_MISSION");
        Assert.Equal(1, story.Get("PKUNK_REASONS"));
    }
}
