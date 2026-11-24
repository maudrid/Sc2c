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
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Thraddash, the Umgah and the Talking Pet in the world (the stage
/// 3b-2 plan's item 10): the Thraddash's homeworld, met at the encounter
/// screen and never orbited while they live; their groups in space, "I've
/// got an idea" ending in peace with their mission's event set aside
/// until 3c, and the mission's talks by set-up; their alliance done for
/// real (25 of their ships destroyed in battles with them by two players,
/// their praise on the way, then their worship: allied at once, everyone
/// told, the shipyard building their ships, the culture's three choices
/// and the allies' talks after); the body count not counted once they
/// worship, and the fight for the stolen Helix; the Aqua Helix's world's
/// guard and what opens it (genthrad.c's conditions each way, leave to
/// land for allies); a worship dropped before its choices are made; the
/// Umgah before the Pet, every way out a fight; what the Umgah's world
/// does in the states the Pet's half of genpet.c handles (3c's: orbited
/// here), and nothing in play starting the Pet's talk; the Umgah's reward
/// by set-up (the answering guest's bio data, the 4 ships to the host's
/// fleet by its room, the fight at their endless fleet, their hostility
/// after), and a reward talk dropped after the bio data; the Talking
/// Pet's talk at a world the test builds as 3c will, under the Umgah's
/// lock (its zombies' ships read, the Umgah's sphere, its compulsion with
/// and without the Taalo protector, its deal, its coming aboard, and the
/// mind-control strobe in the talk's view each time it flashes); and the
/// locks. Each expectation is UQM's (thraddc.c, umgahc.c, talkpet.c,
/// genthrad.c, genpet.c, encount.c's UninitEncounter, comm.c's
/// InitCommunication), cited by function; the scripts' every path is
/// TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class ThraddashUmgahTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;
    private const int Sol = 82, Earth = 2, Luna = 1;

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
        Frames(world, 2);   // the system left is dropped with nobody in it
        Assert.Null(world.MeetingOf(id));
    }

    // The player's ship orbits a world, its last meeting's escape counter run out: the meeting that starts.
    private static Meeting Orbit(GameWorld world, string id, int star, int planet, int? moon = null)
    {
        Leave(world, id);
        world.PutInOrbit(id, star, planet, moon);
        return Assert.IsType<Meeting>(world.MeetingOf(id));
    }

    // The player's ship flies into a world that starts no talk and is never entered: no orbit, the ship by it.
    private static void Held(GameWorld world, string id, int star, int planet, int? moon = null)
    {
        Leave(world, id);
        world.PutInOrbit(id, star, planet, moon);
        Assert.Null(world.MeetingOf(id));
        var ship = world.Find(id)!.Ship;
        Assert.Equal((Activity.InnerSystem, star), (ship.Activity, ship.SystemStar));
        Assert.Null(ship.Ip!.Orbit);
    }

    // The player's ship orbits a world that starts no talk: an ordinary orbit.
    private static void Orbited(GameWorld world, string id, int star, int planet, int? moon = null)
    {
        Leave(world, id);
        world.PutInOrbit(id, star, planet, moon);
        Assert.Null(world.MeetingOf(id));
        Assert.Equal(Activity.Orbit, world.Find(id)!.Ship.Activity);
        Assert.Equal(((int, int?)?)(planet, moon), world.Find(id)!.Ship.Ip!.Orbit);
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

    // The lines the talk has said, by name, in order.
    private static string[] Lines(Talk talk)
    {
        var names = Scripts[talk.Script.Phrases.Name];
        return talk.Events.OfType<LineQueued>().Select(l => names.Phrases[l.Phrase - 1]).ToArray();
    }

    // The lines the talk has said since its last reply (or its start).
    private static string[] LastLines(Talk talk)
    {
        var names = Scripts[talk.Script.Phrases.Name];
        int from = talk.Events.ToList().FindLastIndex(e => e is ReplyChosen) + 1;
        return talk.Events.Skip(from).OfType<LineQueued>().Select(l => names.Phrases[l.Phrase - 1]).ToArray();
    }

    // The player retreats from their meeting's battle, their ship kept whole meanwhile (the tests here
    // aren't about who wins).
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

    // The meeting's talk ended hostile: its closing lines heard, the battle against the race's endless
    // fleet there, the player retreating; never an orbit. The player is then sent off to Sol (left by the
    // world, their ship would start its talk again as others' frames go by).
    private static void FightTheEndlessFleet(GameWorld world, string id, Meeting m, string ship)
    {
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        if (m.Step == MeetingStep.Closing)
            MeetingTests.HearClosing(world, id);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal(ship, m.Battle.EnemyFleet.Ships[0].Ship);
        Retreat(world, id);
        var s = world.Find(id)!.Ship;
        Assert.NotEqual(Activity.Orbit, s.Activity);
        Assert.Null(s.Ip!.Orbit);
        Assert.Equal(m.Star, s.SystemStar);
        Away(world, id);
    }

    // The meeting's talk ended hostile: its closing lines heard, and its battle won, every ship destroyed.
    private static void WinTheFight(GameWorld world, string id, Meeting m)
    {
        if (m.Step == MeetingStep.Closing)
            MeetingTests.HearClosing(world, id);
        Assert.Equal(MeetingStep.Battle, m.Step);
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 1);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Empty(m.Left!);
    }

    // The devices the group carries, by name (the Manifest's list).
    private static string[] Devices(GameWorld world)
    {
        var table = world.Systems!.StoryRules!.Devices;
        return table.Inventory(world.Story!.Get).Select(i => table.All[i].Name).ToArray();
    }

    private static string[] Told(GameWorld world, string id) =>
        world.TakeNotices().Where(n => n.PlayerId == id).Select(n => n.Text).ToArray();

    // A system's planet other than its first, with no moon asked for: an ordinary world there.
    private static int OtherPlanet(GameWorld world, int star)
    {
        Assert.True(world.Systems!.HasWorld(star, 1, null));
        return 1;
    }

    // ------------------------------------------------------------ the Thraddash

    [ContentFact]
    public void TheThraddashHomeworldIsMetAtTheEncounterScreenAndNeverOrbited()
    {
        // genthrad.c's GenerateThraddash_generateOrbital: the Thraddash alive (StartSphereTracking, which
        // shows their sphere), the first planet of their system starts their talk at bit 7 with their
        // groups stood down (NotifyOthers) and an infinite fleet, and returns whatever the talk: no
        // orbit. init_thradd_comm: hostile until they worship the players (THRADD_MANNER), so the
        // encounter screen first. thraddc.c's Intro and ThraddHostile for the talk.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "THRADD_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        // Another planet of theirs: an ordinary orbit, nothing tracked.
        Orbited(world, "h", star, OtherPlanet(world, star));
        Assert.Equal(0, story.Fleet("THRADDASH_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, 0);
        Assert.IsType<ThraddashScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("THRADDASH_SHIP").KnownStrength);
        Assert.Equal("h", world.DialogueLocks!.Holder("THRADD_CONVERSATION"));
        Assert.Contains("The Thraddash hold off: Host is meeting them.", Told(world, "g"));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HOMEWORLD_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("THRADD_HOME_VISITS"), story.Get("THRADD_VISITS")));
        Assert.False(m.Talk.SettlesWhenDropped);
        // ThraddHostile's replies in its order: their news, themselves, submission, friendship, goodbye.
        Assert.Equal(new[] { "whats_up_hostile_1", "what_about_you_1", "submit_1", "be_friends_1", "bye_hostile_1" }, Offers(m.Talk));
        Choose(world, "h", "whats_up_hostile_1");
        Assert.Equal(new[] { "GENERAL_INFO_HOSTILE_1" }, LastLines(m.Talk));
        // Their news once a talk (whats_up_hostile_2 is blanked with it).
        Assert.Equal(new[] { "what_about_you_1", "submit_1", "be_friends_1", "bye_hostile_1" }, Offers(m.Talk));
        Choose(world, "h", "be_friends_1");
        Assert.Equal(new[] { "NO_FRIENDS_1" }, LastLines(m.Talk));
        Assert.Equal(new[] { "what_about_you_1", "submit_1", "be_friends_2", "bye_hostile_1" }, Offers(m.Talk));
        Choose(world, "h", "be_friends_2");
        Assert.Equal(new[] { "NO_FRIENDS_2" }, LastLines(m.Talk));
        Assert.Equal(new[] { "what_about_you_1", "submit_1", "bye_hostile_1" }, Offers(m.Talk));
        Choose(world, "h", "what_about_you_1", "what_about_you_2", "what_about_urquan_1");
        Assert.Equal(new[] { "ABOUT_URQUAN_1" }, LastLines(m.Talk));
        Assert.Equal(new[] { "what_about_urquan_2", "submit_1", "bye_hostile_1" }, Offers(m.Talk));
        // "Goodbye": a fight against their endless fleet, and no orbit after.
        Choose(world, "h", "bye_hostile_1");
        Assert.Equal(new[] { "GOODBYE_HOSTILE_1" }, LastLines(m.Talk));
        FightTheEndlessFleet(world, "h", m, "thradd");
        Assert.Equal((1, 3, 1, 1, 0), (story.Get("THRADD_INFO"), story.Get("THRADD_STACK_1"), story.Get("THRADD_HOSTILE_STACK_3"),
            story.Get("THRADD_HOSTILE_STACK_5"), story.Get("THRADD_HOSTILE_STACK_2")));
        Assert.Equal(0, story.Get("THRADDASH_BODY_COUNT"));   // an endless fleet's ship is never counted

        // The next visits, by the guest: the greetings to the fourth, which stays; "we submit" once, then
        // its other line; each a fight.
        foreach (var (hello, reply, answer) in new[]
                 {
                     ("HOSTILE_HOMEWORLD_HELLO_2", "submit_1", "NO_SUBMIT_1"), ("HOSTILE_HOMEWORLD_HELLO_3", "submit_2", "NO_SUBMIT_2"),
                     ("HOSTILE_HOMEWORLD_HELLO_4", "bye_hostile_2", "GOODBYE_HOSTILE_2"), ("HOSTILE_HOMEWORLD_HELLO_4", "submit_2", "NO_SUBMIT_2"),
                 })
        {
            m = Orbit(world, "g", star, 0);
            Assert.Equal(MeetingStep.Encounter, m.Step);
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
            Assert.Equal(new[] { hello }, Lines(m.Talk!));
            Choose(world, "g", reply);
            Assert.Equal(new[] { answer }, LastLines(m.Talk!));
            FightTheEndlessFleet(world, "g", m, "thradd");
        }
        Assert.Equal((3, 0, 1), (story.Get("THRADD_HOME_VISITS"), story.Get("THRADD_VISITS"), story.Get("THRADD_HOSTILE_STACK_2")));
    }

    [ContentFact]
    public void AttackedFromTheEncounterScreenTheThraddashFightAndNothingOfTheirTalkIsCounted()
    {
        // comm.c's InitCommunication: Attack sets the talk hostile and runs post_thradd_enc, which does
        // nothing: the endless fleet's fight at the homeworld, no orbit after, the visit not counted
        // (Intro never ran), and none of the endless fleet's ships in the body count.
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "THRADD_DEFINED");
        var m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Empty(Lines(m.Talk!));
        FightTheEndlessFleet(world, "h", m, "thradd");
        Assert.Equal((0, 0, 0), (story.Get("THRADD_HOME_VISITS"), story.Get("THRADDASH_BODY_COUNT"), story.Get("THRADD_MANNER")));
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HOMEWORLD_HELLO_1" }, Lines(m.Talk!));
    }

    [ContentFact]
    public void AnIdeaSendsTheThraddashAtTheKohrAhInPeaceAndTheirMissionWaitsForStage3c()
    {
        // thraddc.c's ThraddHostile and ExitConversation: asked about themselves and the Ur-Quan down to
        // THRADD_STACK_1 4, "I've got an idea" is offered (unless KOHR_AH_FRENZY): GOOD_IDEA, the talk at
        // peace, AddEvent (RELATIVE_EVENT, 0, 0, 0, ADVANCE_THRADD_MISSION) (due at once: set aside until
        // 3c, question 3) and THRADD_STACK_1 5, so it's offered once.
        var world = NewGame("g");
        var story = world.Story!;
        var m = MeetInSystem(world, "g", "THRADDASH_SHIP");
        Assert.IsType<ThraddashScript>(m.Talk!.Script);
        Assert.Equal((MeetingStep.Encounter, false, 0), (m.Step, m.Infinite, m.Talk.Get("GLOBAL_FLAGS_AND_DATA")));
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("THRADD_VISITS"), story.Get("THRADD_HOME_VISITS")));
        Choose(world, "g", "what_about_you_1", "what_about_you_2", "what_about_urquan_1", "what_about_urquan_2");
        Assert.Equal(new[] { "whats_up_hostile_1", "got_idea", "submit_1", "be_friends_1", "bye_hostile_1" }, Offers(m.Talk));
        Choose(world, "g", "got_idea");
        Assert.Equal(new[] { "GOOD_IDEA" }, LastLines(m.Talk));
        Assert.Equal((Segue.Peace, 5, 0), (m.Talk.Segue, story.Get("THRADD_STACK_1"), story.Get("THRADD_MISSION")));
        var today = new StoryEvent("ADVANCE_THRADD_MISSION", world.Clock.Year, world.Clock.Month, world.Clock.Day);
        Assert.Equal(today, Assert.Single(story.Events.SetAside, e => e.Event == "ADVANCE_THRADD_MISSION"));
        Assert.DoesNotContain(story.Events, e => e.Event == "ADVANCE_THRADD_MISSION");
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Equal(Allied.Bad, story.Alliance("THRADDASH_SHIP"));

        // Until 3c runs the event the mission never starts: they greet as before, the idea not offered again.
        m = MeetInSystem(world, "h", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_2" }, Lines(m.Talk!));
        Assert.Equal(new[] { "whats_up_hostile_1", "submit_1", "be_friends_1", "bye_hostile_1" }, Offers(m.Talk!));
        Choose(world, "h", "bye_hostile_1");
        WinTheFight(world, "h", m);
        Assert.Equal(1, story.Get("THRADDASH_BODY_COUNT"));
        Assert.Single(story.Events.SetAside, e => e.Event == "ADVANCE_THRADD_MISSION");

        // As 3c's event will leave it (thradd_mission, gameev.c: THRADD_MISSION 1 as they set off): in
        // space their mission's greeting, told once in full (THRADD_MISSION_VISITS); at their homeworld
        // the ordinary greeting.
        story.Set("THRADD_MISSION", 1);
        m = MeetInSystem(world, "g", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "WE_GO_TO_IMPRESS_URQUAN_1" }, Lines(m.Talk!));
        Assert.Equal((2, 1), (story.Get("THRADD_VISITS"), story.Get("THRADD_MISSION_VISITS")));   // the visits not counted
        Choose(world, "g", "bye_hostile_2");
        WinTheFight(world, "g", m);
        m = MeetInSystem(world, "g", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "WE_GO_TO_IMPRESS_URQUAN_2" }, Lines(m.Talk!));
        Choose(world, "g", "bye_hostile_2");
        WinTheFight(world, "g", m);
        m = Orbit(world, "h", Star(world, "THRADD_DEFINED"), 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HOMEWORLD_HELLO_1" }, Lines(m.Talk!));
        Choose(world, "h", "bye_hostile_2");
        FightTheEndlessFleet(world, "h", m, "thradd");
        // Back from it (THRADD_MISSION 4): how it went, in two questions, the second closing the matter (5).
        story.Set("THRADD_MISSION", 4);
        m = MeetInSystem(world, "h", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_3" }, Lines(m.Talk!));
        Assert.Equal(new[] { "whats_up_hostile_1", "submit_1", "be_friends_1", "how_impressed_urquan_1", "bye_hostile_2" }, Offers(m.Talk!));
        Choose(world, "h", "how_impressed_urquan_1");
        Assert.Equal(new[] { "IMPRESSED_LIKE_SO_1" }, LastLines(m.Talk!));
        Choose(world, "h", "how_impressed_urquan_2");
        Assert.Equal(new[] { "IMPRESSED_LIKE_SO_2" }, LastLines(m.Talk!));
        Assert.Equal((5, 1), (story.Get("THRADD_MISSION"), story.Get("THRADD_HOSTILE_STACK_4")));
        Assert.Equal(new[] { "whats_up_hostile_1", "submit_1", "be_friends_1", "bye_hostile_2" }, Offers(m.Talk!));
    }

    [ContentFact]
    public void TheIdeaIsntOfferedOnceTheKohrAhHaveWon()
    {
        // ThraddHostile: at THRADD_STACK_1 4 with KOHR_AH_FRENZY (the Kohr-Ah's victory, 3c's event) the
        // idea isn't offered and the stack moves on to 5.
        var world = NewGame();
        var story = world.Story!;
        story.Set("KOHR_AH_FRENZY", 1);
        var m = MeetInSystem(world, "h", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Choose(world, "h", "what_about_you_1", "what_about_you_2", "what_about_urquan_1", "what_about_urquan_2");
        Assert.Equal(new[] { "whats_up_hostile_1", "submit_1", "be_friends_1", "bye_hostile_1" }, Offers(m.Talk!));
        Assert.Equal(5, story.Get("THRADD_STACK_1"));
        Assert.Empty(story.Events.SetAside.Where(e => e.Event == "ADVANCE_THRADD_MISSION"));
    }

    [ContentFact]
    public void TwentyFiveThraddashShipsDestroyedByTwoPlayersWinTheirWorshipAndTheAllianceIsToldAndTheirShipsBuilt()
    {
        // encount.c's UninitEncounter counts the Thraddash ships destroyed in battles with them
        // (THRADDASH_BODY_COUNT, to THRADDASH_BODY_THRESHOLD, 25), every player's (question 9).
        // thraddc.c's Intro: their praise as the count passes 8 and 16 (THRADD_BODY_LEVEL), and at 25
        // AMAZING_PERFORMANCE and ThraddWorship: THRADD_MANNER 1, their visits and THRADD_STACK_1
        // cleared, SetRaceAllied (THRADDASH_SHIP, TRUE), then the three choices (ThraddCulture,
        // ThraddDemeanor, ExitConversation).
        var world = NewGame("g", "k");
        var story = world.Story!;
        Away(world, "k");
        // The host destroys 9 (attacking from the encounter screen), then is praised and destroys 8 more.
        var m = MeetInSystem(world, "h", "THRADDASH_SHIP", 9);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WinTheFight(world, "h", m);
        Assert.Equal((9, 0), (story.Get("THRADDASH_BODY_COUNT"), story.Get("THRADD_BODY_LEVEL")));
        m = MeetInSystem(world, "h", "THRADDASH_SHIP", 8);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "ADEQUATE_PERFORMANCE", "HOSTILE_SPACE_HELLO_1" }, Lines(m.Talk!));
        Assert.Equal(1, story.Get("THRADD_BODY_LEVEL"));
        Choose(world, "h", "bye_hostile_1");
        WinTheFight(world, "h", m);
        Assert.Equal(17, story.Get("THRADDASH_BODY_COUNT"));
        // The guest: praised again (past 16), and destroys the last 8.
        m = MeetInSystem(world, "g", "THRADDASH_SHIP", 8);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "IMPRESSIVE_PERFORMANCE", "HOSTILE_SPACE_HELLO_2" }, Lines(m.Talk!));
        Assert.Equal(2, story.Get("THRADD_BODY_LEVEL"));
        Choose(world, "g", "bye_hostile_2");
        WinTheFight(world, "g", m);
        Assert.Equal((25, 0, Allied.Bad), (story.Get("THRADDASH_BODY_COUNT"), story.Get("THRADD_MANNER"), story.Alliance("THRADDASH_SHIP")));
        Away(world, "g");

        // The host docks at the starbase meanwhile: a Thraddash ship can't be built.
        var host = world.Host.Ship;
        world.CommanderMet = true;
        world.PutInOrbit("h", Sol, Earth, Luna);
        host.Ip!.ReturnFromOrbit();
        host.Hold.ResourceUnits = 10000;
        world.VisitStarbase(world.Host);
        Assert.Equal(Activity.Starbase, host.Activity);
        Assert.False(world.Outfit("h", new BuildShip(0, "THRADDASH_SHIP")));
        world.TakeNotices();

        // The third player meets them: still the encounter screen (the manner isn't set until the talk),
        // and hailed, their worship: allied at once, before any choice.
        m = MeetInSystem(world, "k", "THRADDASH_SHIP", 2);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("k", EncounterAction.Hail));
        Assert.Equal(new[] { "AMAZING_PERFORMANCE" }, Lines(m.Talk!));
        Assert.Equal((1, 0, 0, Allied.Good), (story.Get("THRADD_MANNER"), story.Get("THRADD_VISITS"), story.Get("THRADD_STACK_1"),
            story.Alliance("THRADDASH_SHIP")));
        Assert.Equal(new[] { "be_polite", "speak_pig_latin", "use_rhymes", "just_the_way_we_do" }, Offers(m.Talk!));
        Assert.True(m.Talk!.SettlesWhenDropped);
        // The others are told, each once; the talker isn't.
        var told = world.TakeNotices().Where(n => n.Event is NoticeEvent.AllianceMade or NoticeEvent.AllianceBroken or NoticeEvent.ShipsGiven).ToList();
        Assert.Equal(new[] { new WorldNotice("g", NoticeEvent.AllianceMade, "k", "Thraddash"), new WorldNotice("h", NoticeEvent.AllianceMade, "k", "Thraddash") },
            told.OrderBy(n => n.PlayerId));
        Assert.Equal("k made an alliance with the Thraddash: their ships are for sale at the starbase.", told[0].Text);
        // The shipyard builds their ships from now on (the fleet is the host's).
        Assert.True(world.Outfit("h", new BuildShip(0, "THRADDASH_SHIP")));
        Assert.Equal(1, world.Fleet.CountOf("THRADDASH_SHIP"));
        Assert.Equal(10000u - (uint)story.Rules.RaceShip("THRADDASH_SHIP").Cost, host.Hold.ResourceUnits);

        // The three choices: how they greet, their culture's name (the answerer's name in the third),
        // how they act; then "we have work to do", at peace.
        Choose(world, "k", "use_rhymes");
        Assert.Equal(new[] { "OK_RHYMES", "WHAT_NAME_FOR_CULTURE" }, LastLines(m.Talk));
        Assert.Equal(1, story.Get("THRADD_INTRO"));
        Assert.Equal(new[] { "you_decide", "fat", "the_slave_empire0" }, Offers(m.Talk));
        var names = new PlayerTalkShip(world.Find("k")!, world.TalkLibrary!.Strings, world);
        Assert.Contains(names.CaptainName, m.Talk.Replies[2].Text);
        Choose(world, "k", "the_slave_empire0");
        Assert.Equal(new[] { "OK_SLAVE", "HOW_SHOULD_WE_ACT" }, LastLines(m.Talk));
        Assert.Equal(3, story.Get("THRADD_CULTURE"));
        Assert.Equal(new[] { "contemplative", "friendly", "wacky", "just_like_us" }, Offers(m.Talk));
        Assert.True(m.Talk.SettlesWhenDropped);
        Choose(world, "k", "wacky");
        Assert.Equal(new[] { "OK_WACKY", "WORK_TO_DO" }, LastLines(m.Talk));
        Assert.Equal((2, Segue.Peace), (story.Get("THRADD_DEMEANOR"), m.Talk.Segue));
        Assert.False(m.Talk.SettlesWhenDropped);
        MeetingTests.HearClosing(world, "k");
        Assert.Null(m.Battle);
        Assert.Null(world.DialogueLocks!.Holder("THRADD_CONVERSATION"));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event is NoticeEvent.AllianceMade or NoticeEvent.AllianceBroken);
        Assert.Equal(25, story.Get("THRADDASH_BODY_COUNT"));

        // Allies: at peace from the start (no encounter screen), greeted in rhyme by the visit, welcomed
        // to space by the culture's name; how the culture goes told once in full (THRADD_DEMEANOR's bit 2).
        m = MeetInSystem(world, "g", "THRADDASH_SHIP");
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Equal(new[] { "HELLO_RHYME_1", "WELCOME_SPACE0", "SLAVE_EMPIRE", "WELCOME_SPACE1" }, Lines(m.Talk!));
        Assert.Equal(new[] { "whats_up_ally", "how_goes_culture", "bye_ally" }, Offers(m.Talk!));
        Choose(world, "g", "how_goes_culture");
        Assert.Equal(new[] { "WACKY_GOES_1" }, LastLines(m.Talk!));
        Assert.Equal(2 | 1 << 2, story.Get("THRADD_DEMEANOR"));
        Choose(world, "g", "whats_up_ally");
        Assert.Equal(new[] { "GENERAL_INFO_ALLY_1" }, LastLines(m.Talk!));
        Assert.Equal(new[] { "bye_ally" }, Offers(m.Talk!));
        Choose(world, "g", "bye_ally");
        Assert.Equal(new[] { "GOODBYE_ALLY_1" }, LastLines(m.Talk!));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // At their homeworld: no encounter screen either, the homeworld's welcome, and still no orbit.
        m = Orbit(world, "g", Star(world, "THRADD_DEFINED"), 0);
        Assert.Equal((MeetingStep.Talk, true), (m.Step, m.Infinite));
        Assert.Equal(new[] { "HELLO_RHYME_2", "WELCOME_HOMEWORLD0", "SLAVE_EMPIRE", "WELCOME_HOMEWORLD1" }, Lines(m.Talk!));
        Choose(world, "g", "how_goes_culture");
        Assert.Equal(new[] { "WACKY_GOES_2" }, LastLines(m.Talk!));
        Choose(world, "g", "bye_ally");
        Assert.Equal(new[] { "GOODBYE_ALLY_2" }, LastLines(m.Talk!));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Equal((2, 0, 2), (story.Get("THRADD_VISITS"), story.Get("THRADD_HOME_VISITS"), story.Get("THRADD_STACK_1")));
    }

    [ContentFact]
    public void OnceTheyWorshipTheirDeadArentCountedAndTheStolenHelixIsAFightWhereverTheyreMet()
    {
        // encount.c's UninitEncounter: nothing is counted while THRADD_MANNER is set. (A count below 25
        // with the manner set is a state UQM can't reach, the worship needing 25: set up here to see the
        // test at all, as a count at 25 can't grow.) thraddc.c's Intro: with the Aqua Helix taken
        // (AQUA_HELIX, stage 3c's pickup, which also sets HELIX_UNPROTECTED) they greet a thief and
        // fight, allied or not, by HELIX_VISITS; init_thradd_comm leaves the allies' talk at peace, so
        // no encounter screen comes first for them.
        var world = NewGame();
        var story = world.Story!;
        foreach (var (field, value) in new[] { ("THRADD_MANNER", 1), ("THRADD_CULTURE", 1), ("THRADDASH_BODY_COUNT", 3), ("AQUA_HELIX", 1),
                     ("HELIX_UNPROTECTED", 1) })
            story.Set(field, value);
        story.SetAlliance("THRADDASH_SHIP", Allied.Good);
        var m = MeetInSystem(world, "h", "THRADDASH_SHIP", 2);
        Assert.Equal(MeetingStep.Closing, m.Step);   // no encounter screen, no replies: the greeting is the talk
        Assert.Equal(new[] { "DIE_THIEF_1" }, Lines(m.Talk!));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        WinTheFight(world, "h", m);
        Assert.Equal((3, 1), (story.Get("THRADDASH_BODY_COUNT"), story.Get("HELIX_VISITS")));

        // Not their worshippers' any more (set up: UQM's manner is never taken back): counted, and the
        // encounter screen first.
        story.Set("THRADD_MANNER", 0);
        m = MeetInSystem(world, "h", "THRADDASH_SHIP", 2);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "DIE_THIEF_2" }, Lines(m.Talk!));
        WinTheFight(world, "h", m);
        Assert.Equal((5, 1), (story.Get("THRADDASH_BODY_COUNT"), story.Get("HELIX_VISITS")));
        // At their homeworld too: the thief's greeting, not the homeworld's.
        m = Orbit(world, "h", Star(world, "THRADD_DEFINED"), 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "DIE_THIEF_2" }, Lines(m.Talk!));
        FightTheEndlessFleet(world, "h", m, "thradd");
        Assert.Equal(0, story.Get("THRADD_HOME_VISITS"));
    }

    [ContentFact]
    public void TheAquaHelixsWorldIsGuardedByTheirEndlessFleetUntilTheyLetYouLandOrLeave()
    {
        // genthrad.c's GenerateThraddash_generateOrbital: the Helix's world (the first planet of its
        // system) starts their talk at bit 6 against an infinite fleet while the Thraddash live
        // (StartSphereTracking, first), HELIX_UNPROTECTED isn't set and their mission hasn't taken them
        // away ((BYTE)(THRADD_MISSION - 1) >= 3: the mission 0, or 4 and after); after the talk the same
        // test again: still guarded, no orbit whatever the talk or the battle. thraddc.c's HelixWorld
        // keeps its questions in THRADD_CULTURE, THRADD_INTRO and THRADD_DEMEANOR.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "AQUA_HELIX_DEFINED");
        world.TakeNotices();
        // Another planet there: an ordinary orbit, nothing tracked.
        Orbited(world, "h", star, OtherPlanet(world, star));
        Assert.Equal(0, story.Fleet("THRADDASH_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, 0);
        Assert.IsType<ThraddashScript>(m.Talk!.Script);
        Assert.Equal((1 << 6, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("THRADDASH_SHIP").KnownStrength);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HELIX_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0, 0), (story.Get("HELIX_VISITS"), story.Get("THRADD_VISITS"), story.Get("THRADD_HOME_VISITS")));
        Assert.Equal(new[] { "why_you_here_hostile", "what_about_this_world", "i_need_to_land_lie", "bye_hostile_helix" }, Offers(m.Talk));
        Choose(world, "h", "why_you_here_hostile");
        Assert.Equal(new[] { "NONE_OF_YOUR_CONCERN" }, LastLines(m.Talk));
        Assert.Equal(new[] { "demand_to_land", "what_about_this_world", "i_need_to_land_lie", "bye_hostile_helix" }, Offers(m.Talk));
        Choose(world, "h", "what_about_this_world");
        Assert.Equal(new[] { "BLUE_HELIX" }, LastLines(m.Talk));
        Choose(world, "h", "whats_helix_hostile");
        Assert.Equal(new[] { "HELIX_IS_HOSTILE" }, LastLines(m.Talk));
        Assert.Equal(new[] { "demand_to_land", "i_need_to_land_lie", "bye_hostile_helix" }, Offers(m.Talk));
        // The lie: caught, and a fight (ExitConversation answers it, so HelixWorld's own branch for it,
        // which would set THRADD_DEMEANOR, never runs: the lie is offered again next time).
        Choose(world, "h", "i_need_to_land_lie");
        Assert.Equal(new[] { "CAUGHT_LIE" }, LastLines(m.Talk));
        FightTheEndlessFleet(world, "h", m, "thradd");
        Assert.Equal((1, 2, 0, 0), (story.Get("THRADD_CULTURE"), story.Get("THRADD_INTRO"), story.Get("THRADD_DEMEANOR"), story.Get("HELIX_UNPROTECTED")));
        // The guest, next: the second greeting, the questions asked gone, each other way out a fight too.
        foreach (var (reply, answer) in new[] { ("demand_to_land", "NO_DEMAND"), ("bye_hostile_helix", "GOODBYE_HOSTILE_HELIX") })
        {
            m = Orbit(world, "g", star, 0);
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
            Assert.Equal(new[] { "HOSTILE_HELIX_HELLO_2" }, Lines(m.Talk!));
            Assert.Equal(new[] { "demand_to_land", "i_need_to_land_lie", "bye_hostile_helix" }, Offers(m.Talk!));
            Choose(world, "g", reply);
            Assert.Equal(new[] { answer }, LastLines(m.Talk!));
            FightTheEndlessFleet(world, "g", m, "thradd");
        }
        Assert.Equal((1, 0), (story.Get("HELIX_VISITS"), story.Get("HELIX_UNPROTECTED")));
        // Attacked from the encounter screen: the same fight, the world as guarded as before.
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        FightTheEndlessFleet(world, "h", m, "thradd");
        m = Orbit(world, "h", star, 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
    }

    [ContentFact]
    public void TheHelixsWorldOpensByTheGeneratorsConditionsEachWay()
    {
        // genthrad.c's test each way, by set-up where stage 3c makes the state (the mission's event, the
        // Ilwrath's war and the Helix's pickup set HELIX_UNPROTECTED; the Thraddash die in that war). Each
        // in a game of its own; the homeworld, whose test has neither condition, beside it.
        GameWorld With(params (string Field, int Value)[] fields)
        {
            var w = NewGame();
            foreach (var (field, value) in fields)
                w.Story!.Set(field, value);
            return w;
        }
        int Helix(GameWorld w) => Star(w, "AQUA_HELIX_DEFINED");
        int Home(GameWorld w) => Star(w, "THRADD_DEFINED");
        // Unprotected: orbited, their sphere shown all the same (StartSphereTracking comes first in the test).
        var world = With(("HELIX_UNPROTECTED", 1));
        Orbited(world, "h", Helix(world), 0);
        Assert.NotEqual(0, world.Story!.Fleet("THRADDASH_SHIP").KnownStrength);
        // Their homeworld doesn't read the flag: guarded as ever.
        world = With(("HELIX_UNPROTECTED", 1));
        Assert.Equal(1 << 7, Orbit(world, "h", Home(world), 0).Talk!.Get("GLOBAL_FLAGS_AND_DATA"));
        // Away on their mission (1 to 3): orbited; before it and once they're back (4 and after): guarded.
        foreach (var (mission, guarded) in new[] { (0, true), (1, false), (2, false), (3, false), (4, true), (5, true), (7, true) })
        {
            world = With(("THRADD_MISSION", mission));
            if (guarded)
            {
                var m = Orbit(world, "h", Helix(world), 0);
                Assert.Equal((1 << 6, true, MeetingStep.Encounter), (m.Talk!.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
            }
            else
                Orbited(world, "h", Helix(world), 0);
            // Their homeworld whatever the mission.
            world = With(("THRADD_MISSION", mission));
            Assert.Equal(1 << 7, Orbit(world, "h", Home(world), 0).Talk!.Get("GLOBAL_FLAGS_AND_DATA"));
        }
        // The Thraddash extinct (the Ilwrath's war ends both races; set up): both worlds orbited, nothing tracked.
        world = NewGame();
        world.Story!.SetAlliance("THRADDASH_SHIP", Allied.Dead);
        world.Story.SetFleet("THRADDASH_SHIP", world.Story.Fleet("THRADDASH_SHIP") with { ActualStrength = 0 });
        Orbited(world, "h", Helix(world), 0);
        Orbited(world, "h", Home(world), 0);
        Assert.Equal(0, world.Story.Fleet("THRADDASH_SHIP").KnownStrength);
    }

    [ContentFact]
    public void AlliesAreGivenLeaveToLandAtTheHelixsWorldAndItIsOrbitedFromThenOn()
    {
        // thraddc.c's ThraddAllies at bit 6: why they're there, what the Helix is, then "may I land?":
        // SURE_LAND, HELIX_UNPROTECTED, peace; genthrad.c's test after the talk then fails and the
        // generator falls through to the orbit. The alliance by set-up here (made for real above):
        // THRADD_MANNER and a culture named, as the worship leaves them.
        var world = NewGame("g");
        var story = world.Story!;
        foreach (var (field, value) in new[] { ("THRADD_MANNER", 1), ("THRADD_CULTURE", 2), ("THRADD_INTRO", 3), ("THRADDASH_BODY_COUNT", 25) })
            story.Set(field, value);
        story.SetAlliance("THRADDASH_SHIP", Allied.Good);
        int star = Star(world, "AQUA_HELIX_DEFINED");
        var m = Orbit(world, "g", star, 0);
        Assert.Equal((MeetingStep.Talk, true, 1 << 6), (m.Step, m.Infinite, m.Talk!.Get("GLOBAL_FLAGS_AND_DATA")));
        Assert.Equal(new[] { "HELLO_LIKE_YOU_1", "WELCOME_HELIX0", "FAT_JERKS", "WELCOME_HELIX1" }, Lines(m.Talk));
        Assert.Equal(new[] { "why_you_here_ally", "whats_up_ally", "how_goes_culture", "bye_ally" }, Offers(m.Talk));
        Choose(world, "g", "why_you_here_ally");
        Assert.Equal(new[] { "GUARDING_HELIX_ALLY" }, LastLines(m.Talk));
        Assert.Equal(new[] { "whats_helix_ally", "may_i_land", "whats_up_ally", "how_goes_culture", "bye_ally" }, Offers(m.Talk));
        // Leaving without asking: at peace, and still no orbit.
        Choose(world, "g", "bye_ally");
        MeetingTests.HearClosing(world, "g");
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Equal(0, story.Get("HELIX_UNPROTECTED"));

        m = Orbit(world, "g", star, 0);
        Assert.Equal(new[] { "HELLO_LIKE_YOU_2", "WELCOME_HELIX0", "FAT_JERKS", "WELCOME_HELIX1" }, Lines(m.Talk!));
        Choose(world, "g", "why_you_here_ally", "whats_helix_ally");
        Assert.Equal(new[] { "HELIX_IS_ALLY" }, LastLines(m.Talk!));
        Assert.Equal(new[] { "may_i_land", "whats_up_ally", "how_goes_culture", "bye_ally" }, Offers(m.Talk!));
        Choose(world, "g", "may_i_land");
        Assert.Equal(new[] { "SURE_LAND" }, LastLines(m.Talk!));
        Assert.Equal((1, Segue.Peace), (story.Get("HELIX_UNPROTECTED"), m.Talk!.Segue));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // The generator falls through: the guest's ship in orbit around the world.
        var ship = world.Find("g")!.Ship;
        Assert.Equal(Activity.Orbit, ship.Activity);
        Assert.Equal(((int, int?)?)(0, null), ship.Ip!.Orbit);
        // And anyone orbits it from then on, with no talk; the Helix itself is stage 3c's pickup.
        Orbited(world, "h", star, 0);
        Assert.Equal(0, story.Get("AQUA_HELIX"));
    }

    [ContentFact]
    public void AWorshipLeftBeforeItsChoicesAreMadeSettlesThemAsTheFirstReplies()
    {
        // UQM's talk can't be left; the port's can. The worship allies them at once and the culture's
        // three choices follow, one a reply, and a culture with no name is a state UQM's script asserts
        // against (GetCultureName): left before they're made, each not yet made is settled as its first
        // reply makes it (be_polite, you_decide, contemplative), over what the Helix's questions had
        // left in those fields, as the choices would have been written (CommScript.DroppedUnfinished).
        var world = NewGame("g", "k");
        var story = world.Story!;
        story.Set("THRADDASH_BODY_COUNT", 25);   // by set-up here (done for real above)
        // The Helix's questions' leavings (HelixWorld): THRADD_INTRO 2, and THRADD_DEMEANOR by hand
        // (its own branch for the lie never runs in UQM: set to see it written over).
        story.Set("THRADD_INTRO", 2);
        story.Set("THRADD_DEMEANOR", 1);
        var m = MeetInSystem(world, "g", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "AMAZING_PERFORMANCE" }, Lines(m.Talk!));
        Assert.True(m.Talk!.SettlesWhenDropped);
        world.TakeNotices();
        world.Leave("g");
        Assert.Equal((1, 0, 1, 0, Allied.Good), (story.Get("THRADD_MANNER"), story.Get("THRADD_INTRO"), story.Get("THRADD_CULTURE"),
            story.Get("THRADD_DEMEANOR"), story.Alliance("THRADDASH_SHIP")));
        Assert.Null(world.DialogueLocks!.Holder("THRADD_CONVERSATION"));
        // The next to meet them: allies, politely, their culture named.
        m = MeetInSystem(world, "h", "THRADDASH_SHIP");
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Equal(new[] { "HELLO_POLITE_1", "WELCOME_SPACE0", "CULTURE", "WELCOME_SPACE1" }, Lines(m.Talk!));
        Choose(world, "h", "how_goes_culture");
        Assert.Equal(new[] { "CONTEMP_GOES_1" }, LastLines(m.Talk!));
        Choose(world, "h", "bye_ally");
        MeetingTests.HearClosing(world, "h");

        // Left after the first choice: it stays, the other two settled. Saved meanwhile: the save has them
        // settled, the live talk goes on to its own choices.
        world = NewGame("k");
        story = world.Story!;
        story.Set("THRADDASH_BODY_COUNT", 25);
        m = MeetInSystem(world, "k", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("k", EncounterAction.Hail));
        Choose(world, "k", "just_the_way_we_do");
        Assert.Equal((3, 0), (story.Get("THRADD_INTRO"), story.Get("THRADD_CULTURE")));
        var saved = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes).Story!;
        Assert.Equal((1, 3, 1, 0, Allied.Good), (saved.Get("THRADD_MANNER"), saved.Get("THRADD_INTRO"), saved.Get("THRADD_CULTURE"),
            saved.Get("THRADD_DEMEANOR"), saved.Alliance("THRADDASH_SHIP")));
        Assert.Equal((0, MeetingStep.Talk), (story.Get("THRADD_CULTURE"), m.Step));   // the live game untouched
        Choose(world, "k", "fat");
        Assert.Equal(2, story.Get("THRADD_CULTURE"));
        world.Leave("k");
        Assert.Equal((3, 2, 0), (story.Get("THRADD_INTRO"), story.Get("THRADD_CULTURE"), story.Get("THRADD_DEMEANOR")));

        // Heard to its end: nothing more is settled by leaving at the closing lines.
        world = NewGame("k");
        story = world.Story!;
        story.Set("THRADDASH_BODY_COUNT", 25);
        m = MeetInSystem(world, "k", "THRADDASH_SHIP");
        Assert.True(world.EncounterChoice("k", EncounterAction.Hail));
        Choose(world, "k", "speak_pig_latin", "fat", "just_like_us");
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.False(m.Talk!.SettlesWhenDropped);
        world.Leave("k");
        Assert.Equal((2, 2, 3), (story.Get("THRADD_INTRO"), story.Get("THRADD_CULTURE"), story.Get("THRADD_DEMEANOR")));
    }

    // ------------------------------------------------------------ the Umgah

    [ContentFact]
    public void BeforeThePetEveryWayOutOfTheUmgahsTalkIsAFight()
    {
        // genpet.c's GenerateTalkingPet_generateOrbital, the Umgah's half: their world (the first planet
        // of their system) while they aren't zombies and they live starts their talk at bit 7 against an
        // infinite fleet, their groups stood down, and returns whatever the talk: no orbit.
        // init_umgah_comm: hostile without the Pet, so the encounter screen first. umgahc.c's
        // NormalUmgah and CombatIsInevitable: two questions, and every way out a fight;
        // post_umgah_enc marks them met while they aren't zombies (MET_NORMAL_UMGAH).
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "TALKING_PET_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        Orbited(world, "h", star, OtherPlanet(world, star));
        Assert.Equal(0, story.Fleet("UMGAH_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, 0);
        Assert.IsType<UmgahScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("UMGAH_SHIP").KnownStrength);
        Assert.Equal("h", world.DialogueLocks!.Holder("UMGAH_CONVERSATION"));
        Assert.Contains("The Umgah hold off: Host is meeting them.", Told(world, "g"));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HWLD_PRE_ZOMBIE_HELLO_1" }, Lines(m.Talk));
        Assert.Equal(new[] { "evil_blobbies_give_up", "whats_up_pre_zombie", "want_to_defeat_urquan", "can_we_be_friends", "bye_pre_zombie" }, Offers(m.Talk));
        Assert.False(m.Talk.SettlesWhenDropped);
        Choose(world, "h", "whats_up_pre_zombie");
        Assert.Equal(new[] { "GENERAL_INFO_PRE_ZOMBIE" }, LastLines(m.Talk));
        Choose(world, "h", "want_to_defeat_urquan");
        Assert.Equal(new[] { "FINE_BY_US" }, LastLines(m.Talk));
        Assert.Equal(new[] { "evil_blobbies_give_up", "can_we_be_friends", "bye_pre_zombie" }, Offers(m.Talk));
        Assert.Equal(0, story.Get("MET_NORMAL_UMGAH"));
        Choose(world, "h", "can_we_be_friends");
        Assert.Equal(new[] { "SURE_FRIENDS" }, LastLines(m.Talk));
        Assert.Equal((1, 1), (story.Get("UMGAH_MENTIONED_TRICKS"), story.Get("MET_NORMAL_UMGAH")));
        FightTheEndlessFleet(world, "h", m, "umgah");

        // The next visits, by the guest: the greetings to the fourth, which stays; "friends" asked once,
        // "evil blobbies, give up" once and then its other line; each a fight.
        foreach (var (hello, offers, reply, answer) in new[]
                 {
                     ("HWLD_PRE_ZOMBIE_HELLO_2", "evil_blobbies_give_up", "evil_blobbies_give_up", "NOT_EVIL_BLOBBIES"),
                     ("HWLD_PRE_ZOMBIE_HELLO_3", "evil_blobbies_must_die", "evil_blobbies_must_die", "OH_NO_WE_WONT"),
                     ("HWLD_PRE_ZOMBIE_HELLO_4", "evil_blobbies_must_die", "bye_pre_zombie", "GOODBYE_PRE_ZOMBIE"),
                     ("HWLD_PRE_ZOMBIE_HELLO_4", "evil_blobbies_must_die", "bye_pre_zombie", "GOODBYE_PRE_ZOMBIE"),
                 })
        {
            m = Orbit(world, "g", star, 0);
            Assert.Equal(MeetingStep.Encounter, m.Step);
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
            Assert.Equal(new[] { hello }, Lines(m.Talk!));
            Assert.Equal(new[] { offers, "whats_up_pre_zombie", "want_to_defeat_urquan", "bye_pre_zombie" }, Offers(m.Talk!));
            Choose(world, "g", reply);
            Assert.Equal(new[] { answer }, LastLines(m.Talk!));
            FightTheEndlessFleet(world, "g", m, "umgah");
        }
        Assert.Equal((3, 0, 1, 0), (story.Get("UMGAH_HOME_VISITS"), story.Get("UMGAH_VISITS"), story.Get("UMGAH_EVIL_BLOBBIES"), story.Get("UMGAH_HOSTILE")));

        // A group in space: its own greetings (UMGAH_VISITS), the same talk, and its fight won.
        m = MeetInSystem(world, "h", "UMGAH_SHIP", 2);
        Assert.Equal((MeetingStep.Encounter, false, 0), (m.Step, m.Infinite, m.Talk!.Get("GLOBAL_FLAGS_AND_DATA")));
        Assert.True(m.Talk.EnemyShips);   // the group's ships are there (the Umgah's script doesn't read it)
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "SPACE_PRE_ZOMBIE_HELLO_1" }, Lines(m.Talk));
        Choose(world, "h", "bye_pre_zombie");
        WinTheFight(world, "h", m);
        Assert.Equal((1, 3), (story.Get("UMGAH_VISITS"), story.Get("UMGAH_HOME_VISITS")));
    }

    [ContentFact]
    public void AttackedFromTheEncounterScreenTheUmgahAreMetAllTheSame()
    {
        // comm.c's InitCommunication runs the post-encounter function for an attack too: post_umgah_enc
        // sets MET_NORMAL_UMGAH though not a word was said, the visit not counted.
        var world = NewGame();
        var story = world.Story!;
        var m = Orbit(world, "h", Star(world, "TALKING_PET_DEFINED"), 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Empty(Lines(m.Talk!));
        FightTheEndlessFleet(world, "h", m, "umgah");
        Assert.Equal((1, 0, 0), (story.Get("MET_NORMAL_UMGAH"), story.Get("UMGAH_HOME_VISITS"), story.Get("UMGAH_HOSTILE")));
    }

    [ContentFact]
    public void TheUmgahsWorldInThePetsStatesIsHeldUntilStage3cAndNothingStartsThePetsTalk()
    {
        // genpet.c's Pet half is stage 3c's, whole (the plan's item 10): the zombies with the Pet's own
        // talk, its talk once the zombies are beaten or the Umgah are gone, the jump to the Ur-Quan.
        // None of its states is reached in play before 3c (UMGAH_ZOMBIE_BLOBBIES is set by the Arilou's
        // talk in QuasiSpace, the Umgah die by the Kohr-Ah's genocide, TALKING_PET by the Pet's talk);
        // set up, the port's table has the Umgah's half only, and their world is one never entered:
        int Umgah(GameWorld w) => Star(w, "TALKING_PET_DEFINED");
        void Extinct(GameWorld w)
        {
            w.Story!.Set("KOHR_AH_FRENZY", 1);   // the genocide's event sets it first (gameev.c)
            w.Story.SetAlliance("UMGAH_SHIP", Allied.Dead);
            w.Story.SetFleet("UMGAH_SHIP", w.Story.Fleet("UMGAH_SHIP") with { ActualStrength = 0 });
        }
        // The zombies: no talk and no orbit, the ship held by the world (stage 3a's Surface.EnterOrbit never
        // enters it; UQM: 10 Umgah ships and the Pet's talk, and no orbit either), their sphere not shown by it.
        var world = NewGame();
        world.Story!.Set("UMGAH_ZOMBIE_BLOBBIES", 1);
        Held(world, "h", Umgah(world), 0);
        Assert.Equal(0, world.Story.Fleet("UMGAH_SHIP").KnownStrength);
        Assert.Equal((0, 0), (world.Story.Get("TALKING_PET_HOME_VISITS"), world.Story.Get("PLAYER_HYPNOTIZED")));
        Assert.Null(world.DialogueLocks!.Holder("UMGAH_CONVERSATION"));
        // The Umgah extinct, the Pet not taken: held too (UQM: the Pet's deal, and no orbit either).
        world = NewGame();
        Extinct(world);
        Held(world, "h", Umgah(world), 0);
        // The Umgah extinct, the Pet taken: held still, where UQM's test fails and the world is orbited, its
        // ruins to be found (genpet.c): 3c's, with the Pet's half.
        world = NewGame();
        Extinct(world);
        world.Story!.Set("TALKING_PET", 1);
        Held(world, "h", Umgah(world), 0);
        // (The other planets there are orbited whatever the state.)
        Orbited(world, "h", Umgah(world), OtherPlanet(world, Umgah(world)));
        // The Pet taken, the Umgah alive: their talk, at peace (the reward: below).
        world = NewGame();
        world.Story!.Set("TALKING_PET", 1);
        var m = Orbit(world, "h", Umgah(world), 0);
        Assert.IsType<UmgahScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Talk, m.Step);

        // Nothing in play starts the Pet's talk: no world of the table, no race's ships in space, and no
        // ship or race --start-encounter could name.
        var rules = world.Story.Rules;
        var locks = world.DialogueLocks!;
        Assert.DoesNotContain(GameWorld.TalkWorlds, w => (w.Conversation ?? locks.ConversationOf(w.Race)) == "TALKING_PET_CONVERSATION");
        Assert.DoesNotContain(rules.Races, r => locks.ConversationOf(r) == "TALKING_PET_CONVERSATION");
        Assert.Null(rules.RaceOfShip("talkpet"));
        Assert.DoesNotContain(rules.Races, r => r.Contains("PET"));
        // Its script is ported and ordinary, under the Umgah's lock (RACE_SHIP_FOR_COMM).
        Assert.Contains("talkpet", TalkLibrary.Ported);
        Assert.False(TalkRules.IsStory("talkpet", world.Story));
        Assert.Equal("UMGAH_SHIP", locks.RaceOf("TALKING_PET_CONVERSATION"));

        // Zombies in space (an Umgah group, with the flag set up) talk as zombies by the Umgah's own
        // script: strangers greeted and questioned, leaving at peace; not marked met (post_umgah_enc).
        world = NewGame();
        world.Story!.Set("UMGAH_ZOMBIE_BLOBBIES", 1);
        m = MeetInSystem(world, "h", "UMGAH_SHIP");
        Assert.IsType<UmgahScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "UNKNOWN_ZOMBIE_HELLO_1" }, Lines(m.Talk));
        Assert.Equal(new[] { "evil_blobbies", "we_vindicator0", "know_any_jokes", "what_about_tpet", "bye_zombie" }, Offers(m.Talk));
        var names = new PlayerTalkShip(world.Host, world.TalkLibrary!.Strings, world);
        Assert.Contains(names.CaptainName, m.Talk.Replies[1].Text);
        Assert.Contains(names.ShipName, m.Talk.Replies[1].Text);
        Choose(world, "h", "what_about_tpet");
        Assert.Equal(new[] { "WHAT_TPET" }, LastLines(m.Talk));
        // The stack asked from comes first; the Pet asked about, "the Arilou told us" takes its place.
        Assert.Equal(new[] { "arilou_told_us", "evil_blobbies", "we_vindicator0", "know_any_jokes", "bye_zombie" }, Offers(m.Talk));
        Choose(world, "h", "bye_zombie");
        Assert.Equal(new[] { "GOODBYE_ZOMBIE" }, LastLines(m.Talk));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal((0, 0, 1), (world.Story.Get("MET_NORMAL_UMGAH"), world.Story.Get("KNOW_UMGAH_ZOMBIES"), world.Story.Get("UMGAH_VISITS")));
        // "The Arilou told us": the secret known, their visits counted afresh, and a fight; from then on
        // the greeting alone and a fight.
        m = MeetInSystem(world, "h", "UMGAH_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "UNKNOWN_ZOMBIE_HELLO_2" }, Lines(m.Talk!));
        Choose(world, "h", "what_about_tpet", "arilou_told_us");
        Assert.Equal(new[] { "THEN_DIE" }, LastLines(m.Talk!));
        Assert.Equal((1, 0), (world.Story.Get("KNOW_UMGAH_ZOMBIES"), world.Story.Get("UMGAH_VISITS")));
        WinTheFight(world, "h", m);
        m = MeetInSystem(world, "h", "UMGAH_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "REVEALED_ZOMBIE_HELLO_1" }, Lines(m.Talk!));
        Assert.Empty(m.Talk!.Replies);
        WinTheFight(world, "h", m);
    }

    [ContentTheory]
    [InlineData(4, 4)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    public void TheUmgahsRewardIsBioDataForTheAnswererAndFourShipsForTheHostThenAFight(int room, int given)
    {
        // umgahc.c's UmgahReward and CombatIsInevitable, with the Pet taken (TALKING_PET: set by the
        // Pet's talk, stage 3c's; set up here) at their homeworld: at peace from init, no encounter
        // screen; "you owe me" or "your largesse" adds 1000 / BIO_CREDIT_VALUE (500) to the bio data
        // aboard, either blanking both, so once; the only goodbye: FUNNY_IDEA, AddEscortShips
        // (UMGAH_SHIP, 4) with no test of room, UMGAH_HOSTILE, and the segue hostile: the fight
        // against their endless fleet. The bio data is the answering ship's, the ships the host's
        // fleet's, whoever answers (question 4).
        var world = NewGame("g", "k");
        var story = world.Story!;
        story.Set("TALKING_PET", 1);
        int star = Star(world, "TALKING_PET_DEFINED");
        var guest = world.Find("g")!.Ship;
        guest.Hold.SetBioData(7);
        world.Host.Ship.Hold.SetBioData(3);
        world.AddFleetShips("SPATHI_SHIP", HostFleet.MaxShips - room);
        Assert.Equal(room, world.Fleet.Room);
        world.TakeNotices();

        var m = Orbit(world, "g", star, 0);
        Assert.IsType<UmgahScript>(m.Talk!.Script);
        Assert.Equal((MeetingStep.Talk, true, 1 << 7), (m.Step, m.Infinite, m.Talk.Get("GLOBAL_FLAGS_AND_DATA")));
        Assert.Equal(new[] { "POST_ZOMBIE_HWLD_HELLO" }, Lines(m.Talk));
        Assert.Equal(new[] { "what_before_tpet", "owe_me_big_time", "our_largesse", "what_do_with_tpet", "so_what_for_now", "bye_post_zombie" }, Offers(m.Talk));
        Assert.False(m.Talk.SettlesWhenDropped);
        Choose(world, "g", "our_largesse");
        Assert.Equal(new[] { "GIVE_LIFEDATA" }, LastLines(m.Talk));
        Assert.Equal((507, 3), (guest.Hold.BioData, world.Host.Ship.Hold.BioData));
        Assert.Equal(new BioChanged(507), m.Talk.Events.OfType<BioChanged>().Single());
        Assert.True(m.Talk.SettlesWhenDropped);
        // Both replies gone with the one chosen; what they did before the Pet, then where the casters went.
        Assert.Equal(new[] { "what_before_tpet", "what_do_with_tpet", "so_what_for_now", "bye_post_zombie" }, Offers(m.Talk));
        Choose(world, "g", "what_before_tpet");
        Assert.Equal(new[] { "TRKD_SPATHI_AND_ILWRATH" }, LastLines(m.Talk));
        Choose(world, "g", "where_caster");
        Assert.Equal(new[] { "SPATHI_TOOK_THEM" }, LastLines(m.Talk));
        Choose(world, "g", "what_do_with_tpet");
        Assert.Equal(new[] { "TRICK_URQUAN" }, LastLines(m.Talk));
        Assert.Equal(new[] { "any_jokes", "so_what_for_now", "bye_post_zombie" }, Offers(m.Talk));
        Choose(world, "g", "any_jokes", "so_what_for_now");
        Assert.Equal(new[] { "DO_THIS_NOW" }, LastLines(m.Talk));
        Assert.Equal(new[] { "bye_post_zombie" }, Offers(m.Talk));
        Assert.Equal((507, 0, 0), (guest.Hold.BioData, story.Get("UMGAH_HOSTILE"), world.Fleet.CountOf("UMGAH_SHIP")));
        world.TakeNotices();
        Choose(world, "g", "bye_post_zombie");
        Assert.Equal(new[] { "FUNNY_IDEA" }, LastLines(m.Talk));
        Assert.Equal((given, room - given, 1, Segue.Hostile), (world.Fleet.CountOf("UMGAH_SHIP"), world.Fleet.Room, story.Get("UMGAH_HOSTILE"), m.Talk.Segue));
        Assert.False(m.Talk.SettlesWhenDropped);
        // The others are told of the ships that joined, by their number; of none, nothing.
        var told = world.TakeNotices().Where(n => n.Event == NoticeEvent.ShipsGiven).OrderBy(n => n.PlayerId).ToList();
        if (given == 0)
            Assert.Empty(told);
        else
        {
            Assert.Equal(new[] { new WorldNotice("h", NoticeEvent.ShipsGiven, "g", "Umgah", given), new WorldNotice("k", NoticeEvent.ShipsGiven, "g", "Umgah", given) }, told);
            Assert.Equal($"{given} Umgah ships joined the fleet (g's talk).", told[0].Text);
        }
        // Then the fight, against their endless fleet; no orbit; the bio data kept.
        FightTheEndlessFleet(world, "g", m, "umgah");
        Assert.Equal((507, 3, 1), (guest.Hold.BioData, world.Host.Ship.Hold.BioData, story.Get("MET_NORMAL_UMGAH")));

        // Hostile for good: the encounter screen, their greeting alone and a fight, at home and in space
        // (met by the third player: the host's fights would start at its fleet's pick screen).
        m = Orbit(world, "k", star, 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("k", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HELLO_1" }, Lines(m.Talk!));
        Assert.Empty(m.Talk!.Replies);
        FightTheEndlessFleet(world, "k", m, "umgah");
        m = MeetInSystem(world, "k", "UMGAH_SHIP");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("k", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HELLO_2" }, Lines(m.Talk!));
        WinTheFight(world, "k", m);
        Assert.Equal((given, 2, 507), (world.Fleet.CountOf("UMGAH_SHIP"), story.Get("UMGAH_VISITS"), guest.Hold.BioData));
    }

    [ContentFact]
    public void WithThePetTakenTheUmgahInSpaceSendThePlayersHomeAndTheRewardWaits()
    {
        // umgahc.c's Intro: with the Pet taken, away from their homeworld: REWARD_AT_HOMEWORLD_1, then _2,
        // no replies, at peace (init's segue, and no encounter screen). Met before they were zombies
        // (MET_NORMAL_UMGAH), the reward's talk has no questions about before the Pet.
        var world = NewGame();
        var story = world.Story!;
        story.Set("TALKING_PET", 1);
        story.Set("MET_NORMAL_UMGAH", 1);
        foreach (var hello in new[] { "REWARD_AT_HOMEWORLD_1", "REWARD_AT_HOMEWORLD_2", "REWARD_AT_HOMEWORLD_2" })
        {
            var s = MeetInSystem(world, "h", "UMGAH_SHIP");
            Assert.Equal(MeetingStep.Closing, s.Step);
            Assert.Equal(new[] { hello }, Lines(s.Talk!));
            Assert.Equal(Segue.Peace, s.Talk!.Segue);
            MeetingTests.HearClosing(world, "h");
            Assert.Null(s.Battle);
        }
        Assert.Equal((1, 0, 0), (story.Get("UMGAH_VISITS"), story.Get("UMGAH_HOSTILE"), world.Host.Ship.Hold.BioData));
        var m = Orbit(world, "h", Star(world, "TALKING_PET_DEFINED"), 0);
        Assert.Equal(new[] { "POST_ZOMBIE_HWLD_HELLO" }, Lines(m.Talk!));
        Assert.Equal(new[] { "owe_me_big_time", "our_largesse", "what_do_with_tpet", "so_what_for_now", "bye_post_zombie" }, Offers(m.Talk!));
        Choose(world, "h", "owe_me_big_time");
        Assert.Equal(new[] { "THANKS" }, LastLines(m.Talk!));
        Assert.Equal(500, world.Host.Ship.Hold.BioData);
        Assert.Equal(new[] { "what_do_with_tpet", "so_what_for_now", "bye_post_zombie" }, Offers(m.Talk!));
    }

    [ContentFact]
    public void ARewardTalkLeftAfterTheBioDataClosesTheRewardAndOneLeftBeforeItDoesnt()
    {
        // UQM's talk can't be left before its goodbye, which closes the reward (UMGAH_HOSTILE); the
        // port's can (its player leaves, the host saves), and the bio data is given at its reply: left
        // after it, the reward is closed all the same, without the goodbye's ships
        // (CommScript.DroppedUnfinished), or it could be had again and again.
        var world = NewGame("g", "k");
        var story = world.Story!;
        story.Set("TALKING_PET", 1);
        int star = Star(world, "TALKING_PET_DEFINED");
        // Left before the bio data: nothing settled, the reward as it was.
        var m = Orbit(world, "k", star, 0);
        Choose(world, "k", "what_do_with_tpet");
        Assert.False(m.Talk!.SettlesWhenDropped);
        world.Leave("k");
        Assert.Equal(0, story.Get("UMGAH_HOSTILE"));
        Assert.Null(world.DialogueLocks!.Holder("UMGAH_CONVERSATION"));

        // Saved after the bio data: the save has it and the reward closed; the live talk goes on.
        var guest = world.Find("g")!.Ship;
        m = Orbit(world, "g", star, 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Choose(world, "g", "owe_me_big_time");
        Assert.Equal((500, 0), (guest.Hold.BioData, story.Get("UMGAH_HOSTILE")));
        var saved = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Equal(1, saved.Story!.Get("UMGAH_HOSTILE"));
        Assert.Equal(0, saved.Fleet.CountOf("UMGAH_SHIP"));
        Assert.Equal((0, MeetingStep.Talk), (story.Get("UMGAH_HOSTILE"), m.Step));
        // Left after it: closed, no ships, nobody told of any.
        world.TakeNotices();
        world.Leave("g");
        Assert.Equal((1, 0), (story.Get("UMGAH_HOSTILE"), world.Fleet.CountOf("UMGAH_SHIP")));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.ShipsGiven);
        Assert.Null(world.DialogueLocks.Holder("UMGAH_CONVERSATION"));
        // The next to come: enemies, the encounter screen and their greeting alone.
        m = Orbit(world, "h", star, 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HELLO_1" }, Lines(m.Talk!));
        Assert.Equal(0, world.Host.Ship.Hold.BioData);
    }

    // ------------------------------------------------------------ the Talking Pet

    // The Umgah's world as stage 3c will wire genpet.c's Pet half, for the tests: the Pet's talk, with the
    // ships the test says are there (10 zombies' ships, or none once they're beaten or the Umgah are gone).
    private static TalkWorld PetsWorld(Func<int> ships) =>
        new("TALKING_PET_DEFINED", 0, null, "UMGAH_SHIP", 1 << 7, "the tests' (genpet.c's Pet half is stage 3c's)")
        {
            Conversation = "TALKING_PET_CONVERSATION",
            Guarded = (_, _) => true,
            Fleet = _ => ships(),
        };

    private static bool StrobeOff(TalkScreen screen) =>
        (screen.Ambient[TalkingPetScript.StrobeAnimation] & CommAnimationFlags.Disabled) != 0;

    [ContentFact]
    public void ThePetsTalkRunsUnderTheUmgahsLockReadsItsZombiesShipsAndCompelsOrFights()
    {
        // talkpet.c's Intro and CompelPlayer, at a world the test builds (nothing in play starts the
        // Pet's talk before 3c): with the enemy's ships there (GetHeadLink (&GLOBAL (npc_built_ship_q)))
        // the first visit's questions, every way out its compulsion: PLAYER_HYPNOTIZED, or with the
        // Taalo protector aboard CANT_COMPEL and a fight against the zombies' 10 ships. Under the
        // Umgah's lock (RACE_SHIP_FOR_COMM maps TALKING_PET_CONVERSATION to UMGAH_SHIP).
        var world = NewGame("g");
        var story = world.Story!;
        int ships = 10;
        world.WorldTalkTable = new[] { PetsWorld(() => ships) };
        story.Set("UMGAH_ZOMBIE_BLOBBIES", 1);
        story.Set("UMGAH_VISITS", 3);
        story.Set("TAALO_PROTECTOR_ON_SHIP", 1);   // the group's device (3c's pickup)
        int star = Star(world, "TALKING_PET_DEFINED");
        var m = Orbit(world, "g", star, 0);
        Assert.IsType<TalkingPetScript>(m.Talk!.Script);
        Assert.Equal((MeetingStep.Talk, false, 10, true), (m.Step, m.Infinite, m.Ships.Count, m.Talk.EnemyShips));
        Assert.Equal(("g", "g"), (world.DialogueLocks!.Holder("TALKING_PET_CONVERSATION"), world.DialogueLocks.Holder("UMGAH_CONVERSATION")));
        Assert.Equal(new[] { "HELLO_AT_UMGAH" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("TALKING_PET_HOME_VISITS"), story.Get("UMGAH_VISITS")));
        Assert.False(m.Talk.SettlesWhenDropped);
        // The zombies not known of: what it is, who the players are (the ship's name), goodbye.
        Assert.Equal(new[] { "what_are_you", "we_are_vindicator0", "bye_at_umgah" }, Offers(m.Talk));
        var names = new PlayerTalkShip(world.Find("g")!, world.TalkLibrary!.Strings, world);
        Assert.Contains(names.ShipName, m.Talk.Replies[1].Text);
        // The Umgah are the Pet's lock's: an Umgah group passes through the host, who is told who's talking.
        world.TakeNotices();
        world.PutInSystem("h", GroupStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[GroupStar].Groups.Clear();
        world.AddEncounterNear("h", Race(world, "UMGAH_SHIP"), 1);
        for (int i = 0; i < 400 && !world.TakeNotices().Any(n => (n.PlayerId, n.Text) == ("h", "g is talking to the Umgah.")); ++i)
        {
            Assert.True(i < 399, "the host wasn't told who's talking to the Umgah");
            Frames(world, 1);
        }
        Assert.Null(world.MeetingOf("h"));
        Away(world, "h");

        Choose(world, "g", "what_are_you");
        Assert.Equal(new[] { "JUST_TALKING_PET" }, LastLines(m.Talk));
        Assert.Equal(new[] { "talking_pets_dumb", "we_are_vindicator0", "bye_at_umgah" }, Offers(m.Talk));
        Choose(world, "g", "we_are_vindicator0");
        Assert.Equal(new[] { "GOOD_FOR_YOU" }, LastLines(m.Talk));
        Assert.Equal(new[] { "must_explain_presence", "talking_pets_dumb", "bye_at_umgah" }, Offers(m.Talk));
        // With the protector aboard it can't compel: the zombies known of, and a fight with their 10 ships.
        Choose(world, "g", "bye_at_umgah");
        Assert.Equal(new[] { "GOODBYE_AT_UMGAH", "CANT_COMPEL" }, LastLines(m.Talk));
        Assert.Equal((1, 0, Segue.Hostile), (story.Get("KNOW_UMGAH_ZOMBIES"), story.Get("PLAYER_HYPNOTIZED"), m.Talk.Segue));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal((false, 10, "umgah"), (m.Battle!.EnemyFleet.Infinite, m.Battle.EnemyFleet.Ships.Count, m.Battle.EnemyFleet.Ships[0].Ship));
        Assert.Null(world.DialogueLocks.Holder("TALKING_PET_CONVERSATION"));
        Retreat(world, "g");

        // Every later visit: the compulsion at once (HYPNOTIZE_AGAIN by the visit), the fight again.
        m = Orbit(world, "g", star, 0);
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.Equal(new[] { "HYPNOTIZE_AGAIN_1", "CANT_COMPEL" }, Lines(m.Talk!));
        Assert.Equal((2, Segue.Hostile), (story.Get("TALKING_PET_HOME_VISITS"), m.Talk!.Segue));
        MeetingTests.HearClosing(world, "g");
        Retreat(world, "g");
        // Without the protector: the captain hypnotised, at peace (3c's world answers that with the jump
        // to the Ur-Quan; here the flag is the test's to clear).
        story.Set("TAALO_PROTECTOR_ON_SHIP", 0);
        m = Orbit(world, "g", star, 0);
        Assert.Equal(new[] { "HYPNOTIZE_AGAIN_2" }, Lines(m.Talk!));
        Assert.Equal((1, Segue.Peace, MeetingStep.Closing), (story.Get("PLAYER_HYPNOTIZED"), m.Talk!.Segue, m.Step));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        story.Set("PLAYER_HYPNOTIZED", 0);

        // The first visit without the protector, the zombies known of: asked about, and any way out the
        // compulsion.
        world = NewGame("g");
        story = world.Story!;
        world.WorldTalkTable = new[] { PetsWorld(() => 10) };
        story.Set("UMGAH_ZOMBIE_BLOBBIES", 1);
        story.Set("KNOW_UMGAH_ZOMBIES", 1);
        m = Orbit(world, "g", star, 0);
        Assert.Equal(new[] { "what_are_you", "what_do_to_umgah", "we_are_vindicator0", "bye_at_umgah" }, Offers(m.Talk!));
        Choose(world, "g", "what_do_to_umgah");
        Assert.Equal(new[] { "DID_NOTHING" }, LastLines(m.Talk!));
        Assert.Equal(new[] { "umgah_zombies", "what_are_you", "we_are_vindicator0", "bye_at_umgah" }, Offers(m.Talk!));
        Choose(world, "g", "umgah_zombies");
        Assert.Equal(new[] { "WORKS_LIKE_THIS" }, LastLines(m.Talk!));
        Assert.Equal((1, Segue.Peace), (story.Get("PLAYER_HYPNOTIZED"), m.Talk!.Segue));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
    }

    [ContentFact]
    public void ThePetsDealBringsItAboardAndItsStrobeFlashesInTheTalksViewEachTime()
    {
        // talkpet.c's Intro with no enemy ships there (the zombies beaten): StartSphereTracking
        // (UMGAH_SHIP), the Umgah alive: LETS_MAKE_A_DEAL and PetDeal; "kill you" down to KillPet, whose
        // plea plays out and then flashes the mind-control strobe (MindControlStrobe: the one-shot
        // ambient animation turned on); every way out brings it aboard (ExitConversation: TALKING_PET,
        // TALKING_PET_ON_SHIP, the Umgah freed, their visits and ARILOU_STACK_2 cleared). Aboard
        // (PetDevice, PetInfo, MindControl): its powers asked about flash the strobe twice in one talk.
        // The talk's view carries each flash as a start of its own, though the talk's flags change only
        // the first time, and starts with the strobe off (differences.md row 83).
        var world = NewGame("g");
        var story = world.Story!;
        world.WorldTalkTable = new[] { PetsWorld(() => 0) };
        foreach (var (field, value) in new[] { ("UMGAH_ZOMBIE_BLOBBIES", 1), ("UMGAH_VISITS", 2), ("UMGAH_HOME_VISITS", 3), ("ARILOU_STACK_2", 1),
                     ("TALKING_PET_HOME_VISITS", 1), ("KNOW_UMGAH_ZOMBIES", 1) })
            story.Set(field, value);
        int star = Star(world, "TALKING_PET_DEFINED");
        int umgah = Race(world, "UMGAH_SHIP");
        var m = Orbit(world, "g", star, 0);
        Assert.IsType<TalkingPetScript>(m.Talk!.Script);
        Assert.Equal((MeetingStep.Talk, 0, false), (m.Step, m.Ships.Count, m.Talk.EnemyShips));
        Assert.Equal(new[] { "LETS_MAKE_A_DEAL" }, Lines(m.Talk));
        Assert.Equal(new SphereTracked(umgah, umgah), m.Talk.Events.OfType<SphereTracked>().Single());
        Assert.NotEqual(0, story.Fleet("UMGAH_SHIP").KnownStrength);
        Assert.Equal(new[] { "what_kind_of_deal", "what_are_you_really", "kill_you" }, Offers(m.Talk));
        int strobe = TalkingPetScript.StrobeAnimation;
        var view = TalkView.Of(m)!;
        Assert.True(StrobeOff(view.Screen!));
        Assert.Empty(view.Screen!.Changes);
        Choose(world, "g", "what_kind_of_deal");
        Assert.Equal(new[] { "HELP_DEFEAT_URQUAN" }, LastLines(m.Talk));
        Assert.Equal(new[] { "how_trust", "ok_lets_do_it", "what_are_you_really", "kill_you" }, Offers(m.Talk));
        Choose(world, "g", "what_are_you_really");
        Assert.Equal(new[] { "POOR_DNYARRI" }, LastLines(m.Talk));
        Assert.Equal(new[] { "how_trust", "ok_lets_do_it", "hard_to_believe", "kill_you" }, Offers(m.Talk));
        Choose(world, "g", "hard_to_believe");
        Assert.Equal(1, story.Get("DNYARRI_LIED"));
        Choose(world, "g", "kill_you");
        Assert.Equal(new[] { "PLEASE_DONT" }, LastLines(m.Talk));
        Assert.Equal(new[] { "how_trust", "ok_lets_do_it", "must_kill" }, Offers(m.Talk));
        Assert.Empty(TalkView.Of(m)!.Screen!.Changes);
        // Its plea, then the strobe: started once the line has played (the view's one line), the screen
        // the view starts in with it off.
        Choose(world, "g", "must_kill");
        Assert.Equal(new[] { "DONT_KILL" }, LastLines(m.Talk));
        Assert.Equal(new[] { "want_kill_1", "want_kill_2", "want_kill_3" }, Offers(m.Talk));
        view = TalkView.Of(m)!;
        Assert.Single(view.Lines);
        Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.AmbientStart, strobe) }, view.Screen!.Changes);
        Assert.True(StrobeOff(view.Screen));
        Assert.Equal(new[] { (strobe) }, m.Talk.AmbientStarts.Select(s => s.Index));
        // Over the wire as it is.
        Assert.True(view.Same(TalkView.Decode(view.Encode())));
        Assert.Equal(0, story.Get("TALKING_PET"));
        Choose(world, "g", "want_kill_2");
        Assert.Equal(new[] { "GLAD_YOU_WONT_KILL" }, LastLines(m.Talk));
        Assert.Equal((1, 1, 0, 0, 0, 0, Segue.Peace), (story.Get("TALKING_PET"), story.Get("TALKING_PET_ON_SHIP"), story.Get("UMGAH_ZOMBIE_BLOBBIES"),
            story.Get("UMGAH_VISITS"), story.Get("UMGAH_HOME_VISITS"), story.Get("ARILOU_STACK_2"), m.Talk.Segue));
        Assert.Contains("TALKING_PET_DEVICE", Devices(world));
        // The closing lines' view: the strobe not started again, and off in the screen it starts in,
        // though the talk's own flag stays on (nothing on the host turns a one-shot off).
        view = TalkView.Of(m)!;
        Assert.Empty(view.Screen!.Changes);
        Assert.True(StrobeOff(view.Screen));
        Assert.Equal(0, (int)(m.Talk.Ambient[strobe] & CommAnimationFlags.Disabled));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Null(world.DialogueLocks!.Holder("UMGAH_CONVERSATION"));

        // Aboard (its use as a device is 3c's; the test's world starts its talk): greeted by the visit,
        // the first greeting, about the protector, skipped without one.
        m = Orbit(world, "g", star, 0);
        Assert.Equal(new[] { "HELLO_AS_DEVICE_2" }, Lines(m.Talk!));
        Assert.Equal(2, story.Get("TALKING_PET_VISITS"));
        Assert.Equal(new[] { "whats_up_onboard", "any_suggestions", "about_your_race", "bye_onboard" }, Offers(m.Talk!));
        Choose(world, "g", "about_your_race", "what_about_physiology");
        Assert.Equal(new[] { "NO_TALK_ABOUT_SELF" }, LastLines(m.Talk!));
        Assert.Equal(new[] { "what_about_powers", "what_about_your_history", "enough_info" }, Offers(m.Talk!));
        int screens = m.Talk!.Events.Count(e => e is ScreenChanged);
        Choose(world, "g", "what_about_powers");
        Assert.Equal(new[] { "NOT_POWERS_BUT_FLOWERS" }, LastLines(m.Talk));
        Assert.Equal(new[] { "yes_flowers" }, Offers(m.Talk));
        view = TalkView.Of(m)!;
        Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.AmbientStart, strobe) }, view.Screen!.Changes);
        Assert.True(StrobeOff(view.Screen));
        Assert.Equal(screens + 1, m.Talk.Events.Count(e => e is ScreenChanged));   // the flag cleared: a change of the flags
        // The second flash: no change of the talk's flags (the recordings have none), a start in the view all the same.
        Choose(world, "g", "yes_flowers");
        Assert.Equal(new[] { "GOOD_HUMAN" }, LastLines(m.Talk));
        Assert.Equal(new[] { "wish_to_go_now" }, Offers(m.Talk));
        Assert.Equal(screens + 1, m.Talk.Events.Count(e => e is ScreenChanged));
        view = TalkView.Of(m)!;
        Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.AmbientStart, strobe) }, view.Screen!.Changes);
        Assert.True(StrobeOff(view.Screen));
        Assert.Equal(2, m.Talk.AmbientStarts.Count);
        Choose(world, "g", "wish_to_go_now");
        Assert.Equal(new[] { "EXCELLENT_IDEA" }, LastLines(m.Talk));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        Assert.Empty(TalkView.Of(m)!.Screen!.Changes);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
    }

    [ContentFact]
    public void WithTheUmgahGoneThePetSaysSoAndTrustIsAskedOnlyByOneItTriedToCompel()
    {
        // talkpet.c's Intro: StartSphereTracking (UMGAH_SHIP) 0 (the Umgah extinct: the Kohr-Ah's
        // genocide, which sets KOHR_AH_FRENZY first): UMGAH_ALL_GONE, and how_trust blanked unless the
        // players met the Pet before (TALKING_PET_HOME_VISITS) and have the Taalo protector.
        foreach (var (visits, taalo, trust) in new[] { (0, 0, false), (1, 0, false), (0, 1, false), (1, 1, true) })
        {
            var world = NewGame();
            var story = world.Story!;
            world.WorldTalkTable = new[] { PetsWorld(() => 0) };
            story.Set("KOHR_AH_FRENZY", 1);
            story.SetAlliance("UMGAH_SHIP", Allied.Dead);
            story.SetFleet("UMGAH_SHIP", story.Fleet("UMGAH_SHIP") with { ActualStrength = 0 });
            story.Set("TALKING_PET_HOME_VISITS", visits);
            story.Set("TAALO_PROTECTOR_ON_SHIP", taalo);
            int umgah = Race(world, "UMGAH_SHIP");
            var m = Orbit(world, "h", Star(world, "TALKING_PET_DEFINED"), 0);
            Assert.Equal(new[] { "UMGAH_ALL_GONE" }, Lines(m.Talk!));
            Assert.Equal(new SphereTracked(umgah, 0), m.Talk!.Events.OfType<SphereTracked>().Single());
            Assert.Equal(0, story.Fleet("UMGAH_SHIP").KnownStrength);
            Choose(world, "h", "what_kind_of_deal");
            Assert.Equal(trust ? new[] { "how_trust", "ok_lets_do_it", "what_are_you_really", "kill_you" }
                : new[] { "boneless_dweeb", "ok_lets_do_it", "what_are_you_really", "kill_you" }, Offers(m.Talk));
            Choose(world, "h", "ok_lets_do_it");
            Assert.Equal(new[] { "COMING_ABOARD" }, LastLines(m.Talk));
            Assert.Equal((1, 1, Segue.Peace), (story.Get("TALKING_PET"), story.Get("TALKING_PET_ON_SHIP"), m.Talk.Segue));
        }
    }

    // ------------------------------------------------------------ the locks

    [ContentFact]
    public void TheThraddashsAndTheUmgahsLocksAreTheirOwnAndTheThraddashsHoldsAtBothTheirWorlds()
    {
        // The dialogue lock is a race's (decision 5; RACE_SHIP_FOR_COMM): while the guest talks to the
        // Thraddash at their homeworld, the host at the Helix's world starts nothing and is told, and
        // meets the Umgah all the same.
        var world = NewGame("g");
        int thradd = Star(world, "THRADD_DEFINED"), helix = Star(world, "AQUA_HELIX_DEFINED"), umgah = Star(world, "TALKING_PET_DEFINED");
        var mg = Orbit(world, "g", thradd, 0);
        Assert.Equal(MeetingStep.Encounter, mg.Step);   // the lock is held from the touch
        world.TakeNotices();
        Leave(world, "h");
        world.PutInOrbit("h", helix, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Contains("g is talking to the Thraddash.", Told(world, "h"));
        Assert.Equal(0, world.Story!.Get("HELIX_VISITS"));
        var mh = Orbit(world, "h", umgah, 0);
        Assert.IsType<UmgahScript>(mh.Talk!.Script);
        Assert.Equal(("g", "h", "h"), (world.DialogueLocks!.Holder("THRADD_CONVERSATION"), world.DialogueLocks.Holder("UMGAH_CONVERSATION"),
            world.DialogueLocks.Holder("TALKING_PET_CONVERSATION")));
        // The guest's talk over (a fight: the lock is let go as the talk ends), the Helix's world is the host's to meet.
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Choose(world, "g", "bye_hostile_1");
        MeetingTests.HearClosing(world, "g");
        Assert.Null(world.DialogueLocks.Holder("THRADD_CONVERSATION"));
        Retreat(world, "g");
        Away(world, "g");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Null(world.DialogueLocks.Holder("UMGAH_CONVERSATION"));
        Retreat(world, "h");
        mh = Orbit(world, "h", helix, 0);
        Assert.IsType<ThraddashScript>(mh.Talk!.Script);
        // And while the host holds it there, the guest at their homeworld is told.
        world.TakeNotices();
        Leave(world, "g");
        world.PutInOrbit("g", thradd, 0);
        Assert.Null(world.MeetingOf("g"));
        Assert.Contains("Host is talking to the Thraddash.", Told(world, "g"));
    }
}
