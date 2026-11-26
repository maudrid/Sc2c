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
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Arilou, the Syreen and the Chmmr in the world, and every race
/// talking (the stage 3b-2 plan's item 11): the Arilou met in hyperspace,
/// their talk keyed by the answering player's own ARILOU_SPACE_SIDE (a
/// player in QuasiSpace, as stage 3c will put one there, has the home
/// talk while another has the one in hyperspace), an attack angering
/// them for everyone, their Umgah check's event queued and set aside;
/// the Syreen's starbase entered and talked at (no ships, no battle,
/// never an orbit, under their lock), their alliance after the ambush
/// told to everyone; a Chmmr group emptied after its talk, with no
/// battle, in hyperspace and in a system, but left as it was by a player
/// leaving mid-talk, and the bomb branch by set-up rebuilding the
/// answering ship; Procyon's Ilwrath by --start-state and --start-devices;
/// every race's groups flying and each race with a talk in space talking,
/// the humans and the Syreen having none; a group whose talk can't be
/// made (no content) passing through, the stand-in meeting gone; and
/// --start-encounter's refusals. Each expectation is UQM's (arilouc.c,
/// syreenc.c, chmmrc.c, gensyr.c, genchmmr.c, comm.c's InitCommunication
/// and RACE_COMMUNICATION), cited by function; the scripts' every path is
/// TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class ArilouSyreenChmmrTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;
    private const int Sol = 82;

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

    // A group of a race by the player in hyperspace, far from the others (each its own place), met.
    private static (Meeting Meeting, HyperEncounter Group) MeetInHyperspace(GameWorld world, string id, string race,
        int ships = 1, short x = 5800)
    {
        world.PutInHyperspace(id);
        var f = world.Find(id)!.Ship.Flight;
        f.PlaceAtUniverse(x, 6100);
        f.Stop();
        f.StarJustLeft = null;
        int r = Race(world, race);
        var e = new HyperEncounter
        {
            Race = r, Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story!.Rules.RaceShips[r].Crew);
        world.Encounters!.List.Add(e);
        return (Meet(world, id), e);
    }

    // A group of a race brought to the player in a system of no groups of its own, and met; every other
    // player sent off to Sol first, out of its way.
    private static Meeting MeetInSystem(GameWorld world, string id, string race, int ships = 1)
    {
        foreach (var p in world.Players.Where(p => p.Present && p.Id != id && p.Ship.Activity != Activity.Starbase
                                                  && p.Ship.SystemStar == GroupStar))
            Away(world, p.Id);
        Leave(world, id);
        world.PutInSystem(id, GroupStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[GroupStar].Groups.Clear();
        world.AddEncounterNear(id, Race(world, race), ships);
        return Meet(world, id);
    }

    // A player's ship sent off to Sol, out of the way of the groups left where it met them and of another's meetings.
    private static void Away(GameWorld world, string id)
    {
        world.PutInSystem(id, Sol);
        Frames(world, 2);
        Assert.Null(world.MeetingOf(id));
    }

    // The player's ship out of any orbit, its last meeting's escape counter run out.
    private static void Leave(GameWorld world, string id)
    {
        if (world.Find(id)!.Ship.Ip is { } ip)
        {
            if (ip.Orbit is not null)
                ip.ReturnFromOrbit();
            ip.EscapeCounter = 0;
        }
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    // The replies offered, by name, in order.
    private static string[] Offers(Talk talk)
    {
        var names = Scripts[talk.Script.Phrases.Name];
        return talk.Replies.Select(r => names.Phrases[r.Phrase - 1]).ToArray();
    }

    private static void Choose(GameWorld world, string id, params string[] phrases)
    {
        foreach (var phrase in phrases)
            Assert.True(world.ChooseReply(id, Reply(world.MeetingOf(id)!.Talk!, phrase)));
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The player retreats from their meeting's battle, their ship kept whole meanwhile.
    private static void Retreat(GameWorld world, string id)
    {
        var ship = world.Find(id)!.Ship;
        for (int i = 0; i < 600 && ship.Battle is { } wb; ++i)
        {
            foreach (var f in wb.Fighters)
                if (f.Starship.RaceDesc is { } rd)
                    rd.CrewLevel = rd.MaxCrew;
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? ShipInput.TakeOff : ShipInput.None);
            world.Step();
        }
        Assert.Null(ship.Battle);
        Assert.False(ship.Destroyed);
    }

    // A host session with the talks, the battles and the locks, before anything starts.
    private static HostSession Session()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        host.World.BattleKit = WorldBattleTests.Kit();
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        host.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        return host;
    }

    // ---------------------------------------------------------------- the Arilou

    [ContentFact]
    public void TheArilouAreMetInHyperspaceAtTheEncounterScreenAndPartInPeace()
    {
        // arilouc.c's init: hostile unless ARILOU_SPACE_SIDE > 1 or ARILOU_MANNER 3, so the encounter screen
        // comes first; Intro's first meeting in hyperspace (INIT_HELLO, MET_ARILOU), FriendlySpaceArilou's
        // small talk, and every way out in peace (ExitConversation): the group goes, one-shot (comm.c).
        var world = NewGame("g");
        var story = world.Story!;
        world.TakeNotices();
        var (m, group) = MeetInHyperspace(world, "h", "ARILOU_SHIP", 2);
        Assert.IsType<ArilouScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(m.Talk.OffersAttack);
        Assert.Equal("h", world.DialogueLocks!.Holder("ARILOU_CONVERSATION"));
        Assert.Equal(0, m.Talk.OwnFields["ARILOU_SPACE_SIDE"]);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "INIT_HELLO"));
        Assert.Equal(1, story.Get("MET_ARILOU"));
        Assert.Equal(new[] { "confused_by_hello", "happy_by_hello", "miffed_by_hello" }, Offers(m.Talk));
        Choose(world, "h", "happy_by_hello", "give_me_info_1");
        Assert.True(Said(m.Talk, "ARILOU_HINTS_1"));
        Assert.Equal(1, story.Get("ARILOU_HINTS"));   // shared story
        Choose(world, "h", "bye_friendly_space");
        Assert.True(Said(m.Talk, "GOODBYE_FRIENDLY_SPACE"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal(HyperEncounter.OneShot, group.Flags & HyperEncounter.OneShot);
        Assert.Null(world.DialogueLocks.Holder("ARILOU_CONVERSATION"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "g" && n.Event == NoticeEvent.Peace);
        // Met again (the next group), by another player: their friendly greeting by visit.
        (m, _) = MeetInHyperspace(world, "g", "ARILOU_SHIP", x: 4000);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "FRIENDLY_SPACE_HELLO_1"));
        Assert.Equal(1, story.Get("ARILOU_VISITS"));
        // Their second hint (the shattered world known) is for the group's story.
        Choose(world, "g", "give_me_info_2");
        Assert.True(Said(m.Talk!, "ARILOU_HINTS_2"));
        Assert.Equal(2, story.Get("KNOW_ABOUT_SHATTERED"));
    }

    [ContentFact]
    public void TheArilouTalkIsKeyedByTheAnsweringPlayersOwnArilouSpaceSide()
    {
        // ARILOU_SPACE_SIDE is each ship's own (StoryState.NotShared): a player in QuasiSpace (2 or more:
        // stage 3c's portal, set here as 3c will) has the Arilou's homeworld talk, at peace from init
        // (init_arilou_comm), its first meeting FRDLY_HOMEWORLD_HELLO_1 and ArilouHome's questions, while
        // another player in hyperspace has theirs, at the encounter screen. The story they share stays one.
        var world = NewGame("g");
        var story = world.Story!;
        world.Find("g")!.Ship.ArilouSpaceSide = 2;
        var (m, _) = MeetInHyperspace(world, "g", "ARILOU_SHIP", x: 4000);
        Assert.Equal(2, m.Talk!.OwnFields["ARILOU_SPACE_SIDE"]);
        Assert.Equal(MeetingStep.Talk, m.Step);   // at peace: no encounter screen
        Assert.True(Said(m.Talk, "FRDLY_HOMEWORLD_HELLO_1"));
        Assert.Equal((1, 1), (story.Get("MET_ARILOU"), story.Get("ARILOU_HOME_VISITS")));
        Choose(world, "g", "confused_by_hello");
        Assert.Contains("what_give_me", Offers(m.Talk));   // ArilouHome's, not hyperspace's
        Assert.DoesNotContain("why_you_here", Offers(m.Talk));
        Choose(world, "g", "bye_friendly_homeworld");
        // post_arilou_enc: a talk at their homeworld with ARILOU_HOME_VISITS at most 1 makes the Umgah the
        // Talking Pet's zombies again and the Arilou the players' friends for good, for everyone.
        Assert.Equal((1, 3), (story.Get("UMGAH_ZOMBIE_BLOBBIES"), story.Get("ARILOU_MANNER")));
        MeetingTests.HearClosing(world, "g");
        // The host, in hyperspace, meets them as friends for good: at peace from init, their greeting
        // in hyperspace by visit (the side is the answerer's, the manner the group's).
        (m, _) = MeetInHyperspace(world, "h", "ARILOU_SHIP");
        Assert.Equal(0, m.Talk!.OwnFields["ARILOU_SPACE_SIDE"]);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk, "FRIENDLY_SPACE_HELLO_1"));
        Assert.Contains("why_you_here", Offers(m.Talk));
        Assert.Throws<InvalidOperationException>(() => story.Get("ARILOU_SPACE_SIDE"));
    }

    [ContentFact]
    public void AnAttackAngersTheArilouForEveryoneAndTheyWontFightAgain()
    {
        // The encounter screen's Attack: the battle against their ships; post_arilou_enc after a hostile
        // ending sets ARILOU_MANNER 1, the visits counted afresh. Then Intro's angry greeting in
        // hyperspace (ANGRY_SPACE_HELLO_1) and AngrySpaceArilou: "let's fight" is a goodbye in peace.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("MET_ARILOU", 1);
        story.Set("ARILOU_VISITS", 3);
        var (m, _) = MeetInHyperspace(world, "h", "ARILOU_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("arilou", m.Battle!.EnemyShip.Id);
        Assert.Equal((1, 0), (story.Get("ARILOU_MANNER"), story.Get("ARILOU_VISITS")));
        Retreat(world, "h");
        (m, _) = MeetInHyperspace(world, "g", "ARILOU_SHIP", x: 4000);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "ANGRY_SPACE_HELLO_1"));
        Choose(world, "g", "im_sorry");
        Assert.True(Said(m.Talk!, "APOLOGIZE_AT_HOMEWORLD"));
        Choose(world, "g", "lets_fight");
        Assert.True(Said(m.Talk!, "NO_FIGHT"));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Equal(1, story.Get("ARILOU_MANNER"));
    }

    [ContentFact]
    public void TheArilousUmgahCheckIsQueuedAndSetAside()
    {
        // ArilouHome's "the Umgah are acting weird" (QuasiSpace, stage 3c's; set up here): ARILOU_CHECKED_UMGAH
        // 1 and ARILOU_UMGAH_CHECK queued ten days on, which stage 3c's calendar would run
        // (ARILOU_CHECKED_UMGAH 2); until then it's set aside on its day (the plan's question 3) and the
        // check stays at 1: "what did you learn" has no news yet.
        var world = NewGame();
        var story = world.Story!;
        story.Set("MET_ARILOU", 1);
        story.Set("KNOW_UMGAH_ZOMBIES", 1);
        world.Host.Ship.ArilouSpaceSide = 2;
        var (m, _) = MeetInHyperspace(world, "h", "ARILOU_SHIP");
        Choose(world, "h", "umgah_acting_weird");
        Assert.True(Said(m.Talk!, "WELL_GO_CHECK"));
        Assert.Equal(1, story.Get("ARILOU_CHECKED_UMGAH"));
        var check = Assert.Single(story.Events, e => e.Event == "ARILOU_UMGAH_CHECK");
        Assert.Equal((2155, 2, 27), (check.Year, check.Month, check.Day));
        Choose(world, "h", "bye_friendly_homeworld");
        MeetingTests.HearClosing(world, "h");
        for (int day = 0; day < 10; ++day)
        {
            world.Clock.NextDay();
            world.RunDayEvents();
        }
        Assert.Contains(story.Events.SetAside, e => e.Event == "ARILOU_UMGAH_CHECK");
        Assert.DoesNotContain(story.Events, e => e.Event == "ARILOU_UMGAH_CHECK");
        Assert.Equal(1, story.Get("ARILOU_CHECKED_UMGAH"));
        // Asked at the next talk, they have no news yet (UQM's event would have made it 2: the compulsion).
        (m, _) = MeetInHyperspace(world, "h", "ARILOU_SHIP", x: 4000);
        Choose(world, "h", "learned_about_umgah");
        Assert.True(Said(m.Talk!, "NO_NEWS_YET"));
    }

    // ---------------------------------------------------------------- the Syreen

    [ContentFact]
    public void TheSyreensStarbaseIsEnteredAndTalkedAtUnderTheirLockWithNoBattle()
    {
        // gensyr.c:77: orbiting the starbase (the first planet's moon) starts the Syreen's conversation, with
        // no ships cloned and no GLOBAL_FLAGS_AND_DATA, and returns: never an orbit. At peace from init
        // (init_syreen_comm): no encounter screen and nothing to fight; no escape counter (no BuildBattle).
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "SYREEN_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        world.PutInOrbit("h", star, 0, 0);
        var m = world.MeetingOf("h")!;
        Assert.IsType<SyreenScript>(m.Talk!.Script);
        Assert.Equal(((int, int?)?)(0, 0), m.World);
        Assert.Empty(m.Ships);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.False(m.Talk.OwnFields.ContainsKey("GLOBAL_FLAGS_AND_DATA"));
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
        Assert.Equal("h", world.DialogueLocks!.Holder("SYREEN_CONVERSATION"));
        Assert.True(Said(m.Talk, "HELLO_BEFORE_AMBUSH_1"));
        Assert.Equal(1, story.Get("SYREEN_HOME_VISITS"));
        // The captain's introductions, with the answerer's names.
        Assert.Equal(new[] { "we_are_vice_squad", "we_are_the_one_for_you_baby", "we_are_vindicator0", "we_are_impressed" },
            Offers(m.Talk));
        // Another player at the starbase meanwhile: told who's talking, nothing starts.
        world.PutInOrbit("g", star, 0, 0);
        Assert.Null(world.MeetingOf("g"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "g" && n.Event == NoticeEvent.Talking);
        Choose(world, "h", "we_are_impressed", "we_here_to_help", "what_doing_here", "bye");
        Assert.True(Said(m.Talk, "OUR_NEW_WORLD"));
        Assert.Equal(1, story.Get("SYREEN_STACK0"));
        Assert.True(Said(m.Talk, "GOODBYE"));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal(MeetingStep.Done, m.Step);
        // The generator returns after the talk: the ship by the starbase, never in orbit.
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        Assert.Null(world.DialogueLocks.Holder("SYREEN_CONVERSATION"));
        // The next visit (by the guest): the second greeting and their questions at once (Intro's
        // NumVisits > 1), the stack where the host left it: shared story.
        Leave(world, "g");
        world.PutInOrbit("g", star, 0, 0);
        m = world.MeetingOf("g")!;
        Assert.True(Said(m.Talk!, "HELLO_BEFORE_AMBUSH_2"));
        Assert.Equal(new[] { "what_about_war", "what_about_history", "what_about_outfit", "bye" }, Offers(m.Talk!));
        Assert.Equal(2, story.Get("SYREEN_HOME_VISITS"));
    }

    [ContentFact]
    public void AfterTheAmbushTheSyreenAllyAtTheFirstGreetingAndEveryoneIsTold()
    {
        // syreenc.c's Intro after the ambush (MYCON_KNOW_AMBUSH: stage 3c's, set up here): at the first
        // greeting SetRaceAllied (SYREEN_SHIP): their ships for sale at the starbase, every other player
        // told; a guest answering.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("SYREEN_KNOW_ABOUT_MYCON", 1);
        story.Set("MYCON_AMBUSH", 1);
        story.Set("MYCON_KNOW_AMBUSH", 1);
        Assert.Equal(Allied.Bad, story.Alliance("SYREEN_SHIP"));
        world.TakeNotices();
        int star = Star(world, "SYREEN_DEFINED");
        world.PutInSystem("g", star);
        world.PutInOrbit("g", star, 0, 0);
        var m = world.MeetingOf("g")!;
        Assert.True(Said(m.Talk!, "HELLO_AFTER_AMBUSH_1"));
        Assert.Equal(Allied.Good, story.Alliance("SYREEN_SHIP"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.AllianceMade);
        Choose(world, "g", "bye_after_ambush");
        Assert.True(Said(m.Talk!, "GOODBYE_AFTER_AMBUSH"));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(1, story.Get("SYREEN_HOME_VISITS"));
    }

    // ---------------------------------------------------------------- the Chmmr

    [ContentFact]
    public void AChmmrGroupIsEmptiedAfterItsTalkWithNoBattle()
    {
        // comm.c:1539: after a talk with a Chmmr group, ReinitQueue (&npc_built_ship_q): in hyperspace the
        // group is written back with no ships (re-forming, one-shot: status 0) and goes. At peace from init
        // (init_chmmr_comm): no encounter screen. Shielded (CHMMR_EMERGING 0): the shielded colour map,
        // WHY_YOU_HERE_1 and ChmmrShielded.
        var world = NewGame("g");
        var story = world.Story!;
        world.TakeNotices();
        var (m, group) = MeetInHyperspace(world, "h", "CHMMR_SHIP", 3);
        Assert.IsType<ChmmrScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Equal("h", world.DialogueLocks!.Holder("CHMMR_CONVERSATION"));
        Assert.True(Said(m.Talk, "WHY_YOU_HERE_1"));
        Assert.Equal(1, m.Talk.ColorMap);
        Choose(world, "h", "need_help");
        Assert.Equal(1, story.Get("CHMMR_STACK"));
        Choose(world, "h", "bye_shielded");
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(new List<(int, int)>(), m.Left);
        Assert.Empty(group.Ships);
        Frames(world, 2);
        Assert.DoesNotContain(group, world.Encounters!.List);
        Assert.Null(world.DialogueLocks.Holder("CHMMR_CONVERSATION"));
        Assert.Equal(1, story.Get("CHMMR_HOME_VISITS"));
    }

    [ContentFact]
    public void AChmmrGroupInASystemIsGoneAfterItsTalk()
    {
        // In a system the same: the group written back with the ships left in the queue (grpinfo.c's
        // GROUP_LIST read after the encounter), none, so it's gone; no battle, so the Chmmr don't turn on
        // the players (no EncounterRace).
        var world = NewGame();
        var m = MeetInSystem(world, "h", "CHMMR_SHIP", 2);
        Assert.Equal(MeetingStep.Talk, m.Step);
        var d = world.SystemGroups!.Systems[GroupStar];
        Assert.Contains(m.Group!, d.Groups);
        Choose(world, "h", "bye_shielded");
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.DoesNotContain(m.Group!, d.Groups);
        Assert.DoesNotContain(d.Groups, g => g.Race == Race(world, "CHMMR_SHIP"));
        Assert.NotEqual(Race(world, "CHMMR_SHIP"), d.EncounterRace);
    }

    [ContentFact]
    public void AChmmrGroupLeftMidTalkIsAsItWas()
    {
        // A talk aborted (its player leaving: the owner's question 2) returns before comm.c's ReinitQueue:
        // the group goes on as it was, its ships all there.
        var world = NewGame("g");
        var (m, group) = MeetInHyperspace(world, "g", "CHMMR_SHIP", 2, x: 4000);
        Assert.Equal(MeetingStep.Talk, m.Step);
        world.Leave("g");
        Assert.Equal(2, group.Ships.Count);
        Assert.Contains(group, world.Encounters!.List);
        Assert.Null(world.DialogueLocks!.Holder("CHMMR_CONVERSATION"));
    }

    [ContentFact]
    public void TheChmmrsBombBranchBySetUpRebuildsTheAnsweringShip()
    {
        // chmmrc.c's ExitConversation "proceed" (the Chmmr unleashed, the Pet and the bomb aboard: stage 3c's,
        // set up here; the plan's question 8): their alliance (everyone told), the bomb's state, and the
        // answering ship (a guest's) rebuilt as chmmrc.c rebuilds the flagship: 1,000,000 RU, no landers,
        // with the starbase only the reserve's fuel, no crew, no minerals, the modules past the bomb's
        // cleared, thrusters and jets in every slot and the bomb's ten modules; the landers' and the storage
        // bays' prices 0 for everyone (shared). The move to the starbase and the 14 days are stage 3c's.
        var world = NewGame("g");
        var story = world.Story!;
        foreach (var (f, v) in new[] { ("CHMMR_UNLEASHED", 1), ("TALKING_PET_ON_SHIP", 1), ("UTWIG_BOMB_ON_SHIP", 1) })
            story.Set(f, v);
        world.TakeNotices();
        var guest = world.Find("g")!.Ship;
        var hostRu = world.Host.Ship.Hold.ResourceUnits;
        var (m, _) = MeetInHyperspace(world, "g", "CHMMR_SHIP", x: 4000);
        Assert.True(Said(m.Talk!, "YOU_ARE_READY"));
        Assert.True(Said(m.Talk!, "WE_WILL_IMPROVE_BOMB"));
        Choose(world, "g", "proceed");
        Assert.True(Said(m.Talk!, "TAKE_2_WEEKS"));
        Assert.Equal(Allied.Good, story.Alliance("CHMMR_SHIP"));
        Assert.Equal((2, 0, 0, 0), (story.Get("CHMMR_BOMB_STATE"), story.Get("UTWIG_BOMB_ON_SHIP"),
            story.Get("CHMMR_HOME_VISITS"), story.Get("CHMMR_STACK")));
        Assert.Equal((1000000u, 0, 1000u, 0), (guest.Hold.ResourceUnits, guest.Hold.Landers, guest.Flight.Fuel, guest.Hold.Crew));
        Assert.All(guest.Hold.Minerals, a => Assert.Equal(0, a));
        Assert.All(guest.Modules.DriveSlots, s => Assert.Equal(ShipModules.FusionThruster, s));
        Assert.All(guest.Modules.JetSlots, s => Assert.Equal(ShipModules.TurningJets, s));
        Assert.Equal(new byte[] { 18, 19, 17, 15, 14, 15, 17, 18, 19, 16, 22, 22, 22, 22, 22, 22 }, guest.Modules.ModuleSlots);
        Assert.Equal((0, 0), (story.ModuleCost("PLANET_LANDER"), story.ModuleCost("STORAGE_BAY")));
        Assert.Equal(hostRu, world.Host.Ship.Hold.ResourceUnits);   // the answerer's ship alone
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.AllianceMade);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
    }

    // ---------------------------------------------------------------- Procyon's Ilwrath

    [ContentFact]
    public void ProcyonsIlwrathAreReachedByStartStateOrStartDevicesWhileTheyLive()
    {
        // genchmmr.c:108: with the Sun Device aboard (--start-state SUN_DEVICE_ON_SHIP=1, or --start-devices
        // naming it), the Ilwrath not deceived and alive (StartSphereTracking), the Chmmr not unleashed: the
        // Ilwrath's endless fleet at the Chmmr's world (planet 1) and their talk at bit 6.
        foreach (bool devices in new[] { false, true })
        {
            var host = Session();
            if (devices)
                host.StartWithDevices(new[] { "SUN_EFFICIENCY_DEVICE" });
            else
                host.StartWithState(new[] { "SUN_DEVICE_ON_SHIP=1" });
            var world = host.World;
            int star = Star(world, "CHMMR_DEFINED");
            host.StartInOrbit(star, 1, null);
            var m = world.MeetingOf(host.HostId)!;
            Assert.IsType<IlwrathScript>(m.Talk!.Script);
            Assert.Equal(1 << 6, m.Talk.OwnFields["GLOBAL_FLAGS_AND_DATA"]);
            Assert.True(m.Infinite);
        }
        // The Ilwrath extinct: no talk, an orbit.
        var world2 = NewGame();
        world2.Story!.Set("SUN_DEVICE_ON_SHIP", 1);
        // (StartSphereTracking: extinct is no ships left and the race dead.)
        world2.Story.SetFleet("ILWRATH_SHIP", world2.Story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        world2.Story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        int procyon = Star(world2, "CHMMR_DEFINED");
        Assert.Null(world2.WorldTalkAt(procyon, 1, null));
    }

    // ---------------------------------------------------------------- every race

    [ContentFact]
    public void EveryRaceTalksAndEveryRacesGroupsFly()
    {
        // Every race's script is ported: each race's groups fly (GameWorld.TalkingRaces, every race), and a
        // group of each race with a conversation in space (RACE_COMMUNICATION) is met with its talk; the
        // humans and the Syreen have none (INVALID_CONVERSATION): UQM never meets their ships.
        var world = NewGame();
        var rules = world.Story!.Rules;
        Assert.Equal(rules.Races.ToHashSet(), world.EncounterRaces);
        foreach (string race in rules.Races)
        {
            var talk = world.RaceTalk(world.Host, rules.RaceIndex(race), 1);
            if (race is "HUMAN_SHIP" or "SYREEN_SHIP")
                Assert.Null(talk);
            else
                Assert.NotNull(talk);
        }
        Assert.IsType<ArilouScript>(world.RaceTalk(world.Host, Race(world, "ARILOU_SHIP"), 1)!.Script);
        Assert.IsType<ChmmrScript>(world.RaceTalk(world.Host, Race(world, "CHMMR_SHIP"), 1)!.Script);
        foreach (string script in new[] { "arilou", "syreen", "chmmr" })
        {
            Assert.Contains(script, TalkLibrary.Ported);
            Assert.False(TalkRules.IsStory(script, world.Story));   // ordinary, under their race's lock
        }
    }

    [ContentFact]
    public void AGroupWhoseTalkCantBeMadeIsntMet()
    {
        // The stand-in meeting is gone: without the content (no talk library) no talk can be made, and a
        // group touching a player passes through them as one whose race's lock another holds does.
        var world = NewGame();
        world.TalkLibrary = null;
        world.MeetingTalks = null;   // the races' own talks (the tests' silent ones off)
        world.PutInHyperspace("h");
        var f = world.Host.Ship.Flight;
        f.PlaceAtUniverse(5800, 6100);
        f.Stop();
        f.StarJustLeft = null;
        int r = Race(world, "URQUAN_SHIP");
        var e = new HyperEncounter
        {
            Race = r, Roller = "h", OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        e.Ships.Add(world.Story!.Rules.RaceShips[r].Crew);
        world.Encounters!.List.Add(e);
        Frames(world, 60);
        Assert.Null(world.MeetingOf("h"));
        Assert.Empty(world.HyperspaceMeetings);
        Assert.Null(world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        // In a system the same: the group flies through the player, no meeting.
        world.PutInSystem("h", GroupStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[GroupStar].Groups.Clear();
        world.AddEncounterNear("h", Race(world, "URQUAN_SHIP"), 1);
        Frames(world, 600);
        Assert.Null(world.MeetingOf("h"));
        Assert.Empty(world.SystemMeetings);
    }

    [ContentFact]
    public void StartEncounterRefusesARaceWithNoTalkInSpace()
    {
        // RACE_COMMUNICATION's INVALID_CONVERSATION: the Syreen and the humans are never met in space; a
        // group of them is refused, by ship or by race, before anything changes. The Arilou and the Chmmr come.
        var host = Session();
        foreach (string spec in new[] { "syreen", "human", "SYREEN_SHIP", "HUMAN_SHIP:2" })
        {
            var e = Assert.Throws<ArgumentException>(() => host.StartEncounter(spec));
            Assert.Contains("no talk in space", e.Message);
        }
        Assert.Equal(0, host.World.Story!.Get("STARBASE_AVAILABLE"));   // the opening not run: nothing changed
        Assert.Equal("A group of 1 Chmmr ship appears by the host in the system", host.StartEncounter("chmmr:1"));
        Assert.Equal("A group of 2 Arilou ships appears by the host in the system", host.StartEncounter("arilou:2"));
    }
}
