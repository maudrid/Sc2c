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
/// The Utwig and the Supox in the world (the stage 3b-2 plan's item 9):
/// the Utwig's homeworld, met at the encounter screen and never orbited
/// while they live; their groups in space; mocked, a fight and their
/// hostility after, while a fight picked from the encounter screen isn't
/// held against the players; "real sorry about the Ultron" ending in
/// peace in the world (the hail from the encounter screen cleared init's
/// hostile segue) where the script run without the encounter screen, as
/// the recordings run it, ends hostile; the bomb's moon guarded by their
/// endless fleet until they have their Ultron back, then by the Druuge's
/// 5, then open; the Supox at home and in space, at peace with no
/// encounter screen, the broken Ultron handed over, and no reply of
/// theirs a fight (only the encounter screen's Attack, once they're
/// insulted); the Ultron shown broken (a sick trick, or a fight from a
/// hostile greeting) and mended (given by a guest: both races allied,
/// both alliances told to the others, the shipyard building their ships,
/// the mission's event set aside until 3c); the allies' 4 ships to the
/// host's fleet by its room, whoever answers; each race's sphere shown
/// by the other's talk and by the orbits; and the two races' locks, each
/// its own. Each expectation is UQM's (utwigc.c, supoxc.c, genutw.c,
/// gensup.c, comm.c's InitCommunication); the scripts' every path is
/// TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class UtwigSupoxTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;
    private const int Sol = 82, Earth = 2, Luna = 1;

    // The bomb's moon (genutw.c:107: matchWorld (5, 1)).
    private const int BombPlanet = 5, BombMoon = 1;

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

    private static bool Offered(Talk talk, string phrase) => Offers(talk).Contains(phrase);

    private static void Choose(GameWorld world, string id, params string[] phrases)
    {
        foreach (var phrase in phrases)
            Assert.True(world.ChooseReply(id, Reply(world.MeetingOf(id)!.Talk!, phrase)));
    }

    private static bool Said(Talk talk, string phrase) => Count(talk, phrase) > 0;

    // How many times the talk has said a line.
    private static int Count(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Count(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The lines the talk has said, by name, in order.
    private static string[] Lines(Talk talk)
    {
        var names = Scripts[talk.Script.Phrases.Name];
        return talk.Events.OfType<LineQueued>().Select(l => names.Phrases[l.Phrase - 1]).ToArray();
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

    // A device aboard, as --start-devices puts it (its story field at its value): the Ultron's states are
    // one field's values (ULTRON_CONDITION 1 to 4), its repairs stage 3c's.
    private static void Aboard(GameWorld world, string device)
    {
        var d = world.Systems!.StoryRules!.Devices.All.Single(x => x.Name == device);
        world.Story!.Set(d.Field!, d.Value ?? 1);
    }

    // The devices the group carries, by name (the Manifest's list).
    private static string[] Devices(GameWorld world)
    {
        var table = world.Systems!.StoryRules!.Devices;
        return table.Inventory(world.Story!.Get).Select(i => table.All[i].Name).ToArray();
    }

    // The broken Ultron handed over by the Supox at their homeworld to a player, who keeps it politely.
    private static void TakeUltron(GameWorld world, string id)
    {
        var m = Orbit(world, id, Star(world, "SUPOX_DEFINED"), 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Choose(world, id, "anyone_around_here", "whats_wrong_with_utwig", "whats_ultron", "what_do_i_do_now");
        MeetingTests.HearClosing(world, id);
        Assert.Equal(1, world.Story!.Get("ULTRON_CONDITION"));
    }

    // The mended Ultron (3c's repairs: the device set as --start-devices does) shown to the Utwig at
    // their homeworld by a player, who hails them from the encounter screen: they take it.
    private static Meeting GiveUltron(GameWorld world, string id)
    {
        Aboard(world, "ULTRON_3_DEVICE");
        var m = Orbit(world, id, Star(world, "UTWIG_DEFINED"), 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice(id, EncounterAction.Hail));
        Choose(world, id, "got_ultron");
        Assert.True(Said(m.Talk!, "HAPPY_DAYS"));
        MeetingTests.HearClosing(world, id);
        Assert.Null(m.Battle);
        return m;
    }

    // ------------------------------------------------------------ the Utwig

    [ContentFact]
    public void TheUtwigsHomeworldIsMetAtTheEncounterScreenAndNeverOrbited()
    {
        // genutw.c:104-139: the Utwig alive (StartSphereTracking, which shows their sphere), the first
        // planet of their system starts their talk at bit 7 with their groups stood down (NotifyOthers)
        // and an infinite fleet, and returns whatever the talk: no orbit. init_utwig_comm: hostile until
        // they have their Ultron back, so the encounter screen first.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "UTWIG_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        // Another planet of theirs, and the homeworld's moon: ordinary orbits, nothing tracked.
        foreach (var (planet, moon) in new (int, int?)[] { (1, null), (0, 0) })
        {
            Leave(world, "h");
            world.PutInOrbit("h", star, planet, moon);
            Assert.Null(world.MeetingOf("h"));
            Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        }
        Assert.Equal(0, story.Fleet("UTWIG_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, 0);
        Assert.IsType<UtwigScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        Assert.Equal(0, story.Fleet("SUPOX_SHIP").KnownStrength);
        Assert.Equal("h", world.DialogueLocks!.Holder("UTWIG_CONVERSATION"));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "The Utwig hold off: Host is meeting them."));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "NEUTRAL_HOMEWORLD_HELLO_1" }, Lines(m.Talk));
        Assert.Equal(1, story.Get("UTWIG_HOME_VISITS"));
        // Their first song: the other one is asked for only once they have their Ultron back.
        Assert.False(m.Talk.UseAltSong);
        Assert.False(m.Talk.SettlesWhenDropped);
        // "This is Captain ..." with the answerer's name and the alliance's; the Ur-Quan's questions take
        // the place of the Utwig's own until both are asked (utwigc.c:596-605 writes pStr[2] again).
        Assert.Equal(new[] { "we_are_vindicator0", "what_about_urquan_1", "bye_neutral" }, Offers(m.Talk));
        var names = new PlayerTalkShip(world.Host, world.TalkLibrary!.Strings, world);
        string weAre = m.Talk.Replies[0].Text;
        Assert.Contains(names.CaptainName, weAre);
        Assert.Contains("The New Alliance of Free Stars", weAre);
        Choose(world, "h", "we_are_vindicator0");
        Assert.True(Said(m.Talk, "WOULD_BE_HAPPY_BUT"));
        Assert.Equal(new[] { "why_sad", "what_about_urquan_1", "bye_neutral" }, Offers(m.Talk));
        // Asked from the Ur-Quan's stack (LastStack 3, whose pStr[3] is never set): nothing moves to the front.
        Choose(world, "h", "what_about_urquan_1");
        Assert.True(Said(m.Talk, "ABOUT_URQUAN_1"));
        Assert.Equal(new[] { "why_sad", "what_about_urquan_2", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "what_about_urquan_2");
        Assert.True(Said(m.Talk, "ABOUT_URQUAN_2"));
        Assert.Equal(new[] { "why_sad", "what_about_you_1", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "bye_neutral");
        Assert.True(Said(m.Talk, "GOODBYE_NEUTRAL"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((1, 2, 0, 0), (story.Get("UTWIG_STACK1"), story.Get("UTWIG_STACK2"), story.Get("UTWIG_WAR_NEWS"), story.Get("UTWIG_HOSTILE")));

        // The next visits, by the guest: the greetings to the fourth, which stays.
        foreach (var hello in new[] { "NEUTRAL_HOMEWORLD_HELLO_2", "NEUTRAL_HOMEWORLD_HELLO_3", "NEUTRAL_HOMEWORLD_HELLO_4", "NEUTRAL_HOMEWORLD_HELLO_4" })
        {
            m = Orbit(world, "g", star, 0);
            Assert.Equal(MeetingStep.Encounter, m.Step);
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
            Assert.Equal(new[] { hello }, Lines(m.Talk!));
            Choose(world, "g", "bye_neutral");
            MeetingTests.HearClosing(world, "g");
            Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        }
        Assert.Equal((3, 0), (story.Get("UTWIG_HOME_VISITS"), story.Get("UTWIG_VISITS")));   // the fourth's case keeps its count
    }

    [ContentFact]
    public void AttackedFromTheEncounterScreenTheUtwigFightAndHoldNoGrudge()
    {
        // comm.c:1393-1409: Attack sets the talk hostile and runs post_utwig_enc, which does nothing
        // (utwigc.c:963-967): the endless fleet's fight at the homeworld, no orbit after, and the
        // neutral greetings go on, the visit not counted (Intro never ran).
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "UTWIG_DEFINED");
        var m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal("utwig", m.Battle.EnemyFleet.Ships[0].Ship);
        Assert.Empty(Lines(m.Talk!));
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 0), (story.Get("UTWIG_HOSTILE"), story.Get("UTWIG_HOME_VISITS")));
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "NEUTRAL_HOMEWORLD_HELLO_1" }, Lines(m.Talk!));
    }

    [ContentFact]
    public void MockedTheUtwigFightAndAreHostileFromThenOnWhileSympathyEndsInPeace()
    {
        // utwigc.c:487-508: at UTWIG_STACK1 3 "don't be babies" is MOCK_OUR_PAIN, a fight and
        // UTWIG_HOSTILE with their visits cleared; "real sorry about the Ultron" is APPRECIATE_SYMPATHY
        // with no segue set: the talk ends as it stood. In the world it was hailed from the encounter
        // screen, which set it at peace (comm.c:1388-1392, SET_GAME_STATE (BATTLE_SEGUE, 0)): no fight.
        var world = NewGame("g");
        var story = world.Story!;
        // A group in space, met by the guest: the three questions down to the Ultron's loss.
        var m = MeetInSystem(world, "g", "UTWIG_SHIP");
        Assert.IsType<UtwigScript>(m.Talk!.Script);
        Assert.Equal((0, false, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "NEUTRAL_SPACE_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("UTWIG_VISITS"), story.Get("UTWIG_HOME_VISITS")));
        // "This is Captain ..." with the answering guest's name, not the host's.
        var guestNames = new PlayerTalkShip(world.Find("g")!, world.TalkLibrary!.Strings, world);
        var hostNames = new PlayerTalkShip(world.Host, world.TalkLibrary.Strings, world);
        Assert.NotEqual(hostNames.CaptainName, guestNames.CaptainName);
        Assert.Contains(guestNames.CaptainName, m.Talk.Replies[Reply(m.Talk, "we_are_vindicator0")].Text);
        Assert.DoesNotContain(hostNames.CaptainName, m.Talk.Replies[Reply(m.Talk, "we_are_vindicator0")].Text);
        Choose(world, "g", "we_are_vindicator0", "why_sad", "what_ultron");
        Assert.True(Said(m.Talk, "GLORIOUS_ULTRON"));
        Assert.Equal(new[] { "dont_be_babies", "real_sorry_about_ultron", "what_about_urquan_1", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "g", "real_sorry_about_ultron");
        Assert.True(Said(m.Talk, "APPRECIATE_SYMPATHY"));
        Assert.Empty(m.Talk.Replies);
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Equal((4, 0), (story.Get("UTWIG_STACK1"), story.Get("UTWIG_HOSTILE")));
        // From then on neither is offered (UTWIG_STACK1 4), in space or at home.
        m = MeetInSystem(world, "g", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "NEUTRAL_SPACE_HELLO_2" }, Lines(m.Talk!));
        Assert.Equal(new[] { "what_about_urquan_1", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "g", "bye_neutral");
        MeetingTests.HearClosing(world, "g");
        Away(world, "g");

        // The other answer, in a game of its own, at their homeworld, by the host: a fight against the
        // endless fleet. (UTWIG_INFO and the other places' visits set to see them cleared.)
        world = NewGame("g");
        story = world.Story!;
        foreach (var field in new[] { "UTWIG_INFO", "UTWIG_VISITS", "BOMB_VISITS" })
            story.Set(field, 1);
        int star = Star(world, "UTWIG_DEFINED");
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(1, story.Get("UTWIG_HOME_VISITS"));
        Choose(world, "h", "we_are_vindicator0", "why_sad", "what_ultron");
        Assert.Equal(3, story.Get("UTWIG_STACK1"));
        Choose(world, "h", "dont_be_babies");
        Assert.True(Said(m.Talk!, "MOCK_OUR_PAIN"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.Equal((4, 1, 0, 0, 0, 0), (story.Get("UTWIG_STACK1"), story.Get("UTWIG_HOSTILE"), story.Get("UTWIG_INFO"),
            story.Get("UTWIG_HOME_VISITS"), story.Get("UTWIG_VISITS"), story.Get("BOMB_VISITS")));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);

        // Hostile: their greeting is the whole talk and a fight (no Ultron aboard), at home and in space.
        m = Orbit(world, "h", star, 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_HOMEWORLD_HELLO_1" }, Lines(m.Talk!));
        Assert.Empty(m.Talk!.Replies);
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "h");
        m = MeetInSystem(world, "g", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_1" }, Lines(m.Talk!));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.False(m.Battle!.EnemyFleet.Infinite);
        Retreat(world, "g");
        Assert.Equal((1, 1), (story.Get("UTWIG_HOME_VISITS"), story.Get("UTWIG_VISITS")));
    }

    [ContentFact]
    public void TheScriptRunWithoutTheEncounterScreenLeavesSympathyHostileAsTheRecordingsHaveIt()
    {
        // The same replies two ways (the plan's "surprise"): the script as init left it (the recorder's
        // way, and TalkTests': Talk.Start, no encounter screen) ends hostile, init's segue standing;
        // loaded and hailed as the world does it from the encounter screen, in peace.
        var replies = new[] { "we_are_vindicator0", "why_sad", "what_ultron", "real_sorry_about_ultron" };
        var world = NewGame();
        var straight = world.RaceTalk(world.Host, Race(world, "UTWIG_SHIP"), 1)!;
        straight.Start();
        foreach (var reply in replies)
            straight.Choose(Reply(straight, reply));
        Assert.True(Said(straight, "APPRECIATE_SYMPATHY"));
        Assert.Equal((true, Segue.Hostile), (straight.Ended, straight.Segue));

        world = NewGame();
        var hailed = world.RaceTalk(world.Host, Race(world, "UTWIG_SHIP"), 1)!;
        hailed.Load();
        Assert.True(hailed.OffersAttack);
        hailed.Hail();
        foreach (var reply in replies)
            hailed.Choose(Reply(hailed, reply));
        Assert.True(Said(hailed, "APPRECIATE_SYMPATHY"));
        Assert.Equal((true, Segue.Peace), (hailed.Ended, hailed.Segue));
    }

    // ------------------------------------------------------------ the bomb's moon

    [ContentFact]
    public void TheUtwigsEndlessFleetGuardsTheBombsMoonAndFightsForIt()
    {
        // genutw.c:106-139: while the bomb isn't taken and the Utwig haven't their Ultron, the sixth
        // planet's second moon starts their talk at bit 6 against an infinite fleet, their groups stood
        // down, and returns whatever happened: the moon is never orbited while they guard it.
        // utwigc.c's BombWorld: the questions by BOMB_STACK1 and 2, "we will now take the bomb" a fight
        // and UTWIG_HOSTILE; hostile at the moon, a fight at once, Ultron or no (:728-732).
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "BOMB_DEFINED");
        world.PutInSystem("g", star);
        Frames(world, 1);
        Assert.Empty(world.SystemGroups!.Systems[star].Groups);   // GenerateUtwig_initNpcs: none while the bomb is there
        world.TakeNotices();
        // The planet itself, its first moon, and this system's first planet (the homeworld's place in
        // the Utwig's own system, same generator): ordinary orbits, nothing tracked.
        foreach (var (planet, moon) in new (int, int?)[] { (BombPlanet, null), (BombPlanet, 0), (0, null) })
        {
            Leave(world, "h");
            world.PutInOrbit("h", star, planet, moon);
            Assert.Null(world.MeetingOf("h"));
            Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        }
        Assert.Equal(0, story.Fleet("UTWIG_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.IsType<UtwigScript>(m.Talk!.Script);
        Assert.Equal((1 << 6, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("UTWIG_SHIP").KnownStrength);   // StartSphereTracking (UTWIG_SHIP), :112
        Assert.Equal(0, story.Fleet("DRUUGE_SHIP").KnownStrength);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "The Utwig hold off: Host is meeting them."));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "BOMB_WORLD_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0, 0), (story.Get("BOMB_VISITS"), story.Get("UTWIG_VISITS"), story.Get("UTWIG_HOME_VISITS")));
        Assert.Equal(new[] { "why_you_here", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "why_you_here");
        Assert.True(Said(m.Talk, "WE_GUARD_BOMB"));
        Assert.Equal(new[] { "what_about_bomb", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "what_about_bomb");
        Assert.True(Said(m.Talk, "ABOUT_BOMB"));
        Assert.Equal(new[] { "give_us_bomb_or_die", "may_we_have_bomb", "whats_up_bomb", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "may_we_have_bomb");
        Assert.True(Said(m.Talk, "NO_BOMB"));
        Assert.Equal(new[] { "please", "give_us_bomb_or_die", "whats_up_bomb", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "please");
        Assert.True(Said(m.Talk, "SORRY_NO_BOMB"));
        Choose(world, "h", "whats_up_bomb");
        Assert.True(Said(m.Talk, "GENERAL_INFO_BOMB_1"));
        // Their news heard (BOMB_INFO): the other farewell from then on.
        Assert.Equal(new[] { "give_us_bomb_or_die", "bye_bomb" }, Offers(m.Talk));
        Choose(world, "h", "bye_bomb");
        Assert.True(Said(m.Talk, "GOODBYE_BOMB"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 0, 1, 2, 2), (story.Get("BOMB_UNPROTECTED"), story.Get("UTWIG_HOSTILE"), story.Get("BOMB_INFO"),
            story.Get("BOMB_STACK1"), story.Get("BOMB_STACK2")));

        // The guest: the second greeting, their other news, the warning, and the fight for it.
        story.Set("UTWIG_VISITS", 3);
        story.Set("UTWIG_HOME_VISITS", 2);
        m = Orbit(world, "g", star, BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "BOMB_WORLD_HELLO_2" }, Lines(m.Talk!));
        Choose(world, "g", "whats_up_bomb");
        Assert.True(Said(m.Talk!, "GENERAL_INFO_BOMB_2"));
        Choose(world, "g", "give_us_bomb_or_die");
        Assert.True(Said(m.Talk!, "GUARDS_WARN"));
        Assert.Equal(new[] { "demand_bomb", "bye_bomb" }, Offers(m.Talk!));
        Choose(world, "g", "demand_bomb");
        Assert.True(Said(m.Talk!, "GUARDS_FIGHT"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.Equal((1, 0, 0, 0, 3), (story.Get("UTWIG_HOSTILE"), story.Get("UTWIG_VISITS"), story.Get("UTWIG_HOME_VISITS"),
            story.Get("BOMB_VISITS"), story.Get("BOMB_STACK1")));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal("utwig", m.Battle.EnemyFleet.Ships[0].Ship);
        Retreat(world, "g");
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Equal(0, story.Get("BOMB_UNPROTECTED"));

        // Hostile at the moon: a fight at once, even with the mended Ultron aboard (no "wait, we have the
        // Ultron" there), and the moon as closed as before.
        Aboard(world, "ULTRON_3_DEVICE");
        foreach (var hello in new[] { "HOSTILE_BOMB_HELLO_1", "HOSTILE_BOMB_HELLO_2", "HOSTILE_BOMB_HELLO_2" })
        {
            m = Orbit(world, "h", star, BombPlanet, BombMoon);
            Assert.Equal((MeetingStep.Encounter, true), (m.Step, m.Infinite));
            Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
            Assert.Equal(new[] { hello }, Lines(m.Talk!));
            Assert.Empty(m.Talk!.Replies);
            Assert.Equal(Segue.Hostile, m.Talk.Segue);
            MeetingTests.HearClosing(world, "h");
            Assert.Equal(MeetingStep.Battle, m.Step);
            Retreat(world, "h");
            Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        }
        Assert.Equal((1, 0, 0), (story.Get("BOMB_VISITS"), story.Get("UTWIG_HAVE_ULTRON"), story.Get("BOMB_UNPROTECTED")));
    }

    [ContentFact]
    public void TheUltronIsRefusedAtTheBombsMoonAndGivenAtHomeTheMoonIsTheDruugesThenOpen()
    {
        // utwigc.c:230-235, 697-699: at the bomb's moon "we have the Ultron" is NO_ULTRON_AT_BOMB and
        // REFUSED_ULTRON_AT_BOMB, offered no more there. genutw.c:110-178: once the Utwig have it (given
        // at their homeworld), the moon is the Druuge's 5 ships', not stood down, and with none left
        // BOMB_UNPROTECTED and the orbit.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "BOMB_DEFINED");
        Aboard(world, "ULTRON_3_DEVICE");
        var m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "why_you_here", "got_ultron", "bye_neutral" }, Offers(m.Talk!));
        world.TakeNotices();
        Choose(world, "h", "got_ultron");
        Assert.Equal(new[] { "BOMB_WORLD_HELLO_1", "NO_ULTRON_AT_BOMB" }, Lines(m.Talk!));
        Assert.Equal((1, 0, 4, Segue.Peace), (story.Get("REFUSED_ULTRON_AT_BOMB"), story.Get("UTWIG_HAVE_ULTRON"),
            story.Get("ULTRON_CONDITION"), m.Talk!.Segue));
        Assert.Equal(Allied.Bad, story.Alliance("UTWIG_SHIP"));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.AllianceMade);
        m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "why_you_here", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "h", "bye_neutral");
        MeetingTests.HearClosing(world, "h");

        // Given at their homeworld by the guest: the moon's guard is the Druuge's from then on.
        GiveUltron(world, "g");
        Assert.Equal(1, story.Get("UTWIG_HAVE_ULTRON"));
        world.PutInSystem("g", star);
        world.TakeNotices();
        m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.IsType<DruugeScript>(m.Talk!.Script);
        Assert.Equal((1 << 6, false, 5), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Ships.Count));
        Assert.NotEqual(0, story.Fleet("DRUUGE_SHIP").KnownStrength);
        Assert.Equal("h", world.DialogueLocks!.Holder("DRUUGE_CONVERSATION"));
        Assert.Null(world.DialogueLocks.Holder("UTWIG_CONVERSATION"));
        // No NotifyOthers for the Druuge (genutw.c:148): a meeting, not a hold-off.
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "Host is meeting the Druuge."));
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(5, m.Battle!.EnemyFleet.Ships.Count);
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 1);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(1, story.Get("BOMB_UNPROTECTED"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(((int, int?)?)(BombPlanet, BombMoon), world.Host.Ship.Ip!.Orbit);
        // Open to anyone from then on: the guest orbits it with no talk.
        Leave(world, "g");
        world.PutInOrbit("g", star, BombPlanet, BombMoon);
        Assert.Null(world.MeetingOf("g"));
        Assert.Equal(Activity.Orbit, world.Find("g")!.Ship.Activity);
    }

    // ------------------------------------------------------------ the Supox

    [ContentFact]
    public void TheSupoxHomeworldTalksAtPeaceAndIsNeverOrbited()
    {
        // gensup.c:80-102: the Supox alive (StartSphereTracking), the first planet of their system starts
        // their talk at bit 7 with their groups stood down and an infinite fleet, and returns whatever
        // the talk. init_supox_comm: at peace unless SUPOX_HOSTILE: no encounter screen, so no way to
        // attack them.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "SUPOX_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        // Each of its two moons (gensup.c:65: NumPlanets 2): an ordinary orbit, nothing tracked.
        foreach (int moon in new[] { 0, 1 })
        {
            Leave(world, "h");
            world.PutInOrbit("h", star, 0, moon);
            Assert.Null(world.MeetingOf("h"));
            Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        }
        Assert.Equal(0, story.Fleet("SUPOX_SHIP").KnownStrength);

        var m = Orbit(world, "h", star, 0);
        Assert.IsType<SupoxScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Talk), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.NotEqual(0, story.Fleet("SUPOX_SHIP").KnownStrength);
        Assert.Equal(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        Assert.Equal("h", world.DialogueLocks!.Holder("SUPOX_CONVERSATION"));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "The Supox hold off: Host is meeting them."));
        Assert.False(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.False(m.Talk.SettlesWhenDropped);
        Assert.Equal(new[] { "NEUTRAL_HOMEWORLD_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("SUPOX_HOME_VISITS"), story.Get("SUPOX_VISITS")));
        // They copy the captain: his name, his ship's, the alliance's, spliced into the replies in turn;
        // their species isn't asked about until that's through (pStr[1] cleared).
        var names = new PlayerTalkShip(world.Host, world.TalkLibrary!.Strings, world);
        Assert.Equal(new[] { "i_am0", "anyone_around_here", "bye_neutral" }, Offers(m.Talk));
        Assert.Contains(names.CaptainName, m.Talk.Replies[0].Text);
        Choose(world, "h", "i_am0");
        Assert.True(Said(m.Talk, "WE_ARE_SUPOX"));
        Assert.Equal(new[] { "my_ship0", "anyone_around_here", "bye_neutral" }, Offers(m.Talk));
        Assert.Contains(names.ShipName, m.Talk.Replies[0].Text);
        Choose(world, "h", "my_ship0");
        Assert.True(Said(m.Talk, "OUR_SHIP"));
        Assert.Equal(new[] { "from_alliance0", "anyone_around_here", "bye_neutral" }, Offers(m.Talk));
        Assert.Contains("The New Alliance of Free Stars", m.Talk.Replies[0].Text);
        Choose(world, "h", "from_alliance0", "are_you_copying", "why_copy");
        Assert.True(Said(m.Talk, "SYMBIOTS"));
        Assert.Equal(new[] { "tell_us_of_your_species", "anyone_around_here", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "tell_us_of_your_species", "plants_arent_intelligent");
        Assert.True(Said(m.Talk, "PROVES_WERE_SPECIAL"));
        Assert.Equal(new[] { "anyone_around_here", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "bye_neutral");
        Assert.True(Said(m.Talk, "GOODBYE_NEUTRAL"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((5, 2), (story.Get("SUPOX_STACK1"), story.Get("SUPOX_STACK2")));

        // The next visits, by the guest: the second greeting, which stays.
        foreach (int visit in new[] { 2, 3 })
        {
            m = Orbit(world, "g", star, 0);
            Assert.Equal(MeetingStep.Talk, m.Step);
            Assert.Equal(new[] { "NEUTRAL_HOMEWORLD_HELLO_2" }, Lines(m.Talk!));
            Choose(world, "g", "bye_neutral");
            MeetingTests.HearClosing(world, "g");
            Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        }
        Assert.Equal((1, 0), (story.Get("SUPOX_HOME_VISITS"), story.Get("SUPOX_VISITS")));   // the second's case keeps its count
    }

    [ContentFact]
    public void TheSupoxHandOverTheBrokenUltronAndNoReplyOfTheirsIsAFightOnlyTheEncounterScreensAttack()
    {
        // supoxc.c:383-410: "anyone around here?" shows the Utwig's sphere (StartSphereTracking
        // (UTWIG_SHIP)); asked on, they hand over the broken Ultron (ULTRON_CONDITION 1, the group's).
        // "Thanks, now we eat you" (:118-125) makes them hostile for good (SUPOX_HOSTILE) and still ends
        // in peace. Hostile, init_supox_comm leaves the talk hostile (the encounter screen), but the
        // greeting is the whole talk and sets it at peace (:582-597): only Attack fights them.
        var world = NewGame("g");
        var story = world.Story!;
        // No path through supoxc.c ends hostile: every end UQM's own script reached in the recording is peace.
        var ends = TalkTests.Recorded("supox").SelectMany(st => st.GetProperty("steps").EnumerateArray())
            .SelectMany(step => step.GetProperty("events").EnumerateArray())
            .Where(e => e.TryGetProperty("end", out _)).Select(e => e.GetProperty("end").GetInt32()).ToList();
        Assert.True(ends.Count > 200);
        Assert.All(ends, end => Assert.Equal((int)Segue.Peace, end));
        // A group in space, met by the guest: at peace, no encounter screen.
        var m = MeetInSystem(world, "g", "SUPOX_SHIP");
        Assert.IsType<SupoxScript>(m.Talk!.Script);
        Assert.Equal((0, false, MeetingStep.Talk), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.Equal(new[] { "NEUTRAL_SPACE_HELLO_1" }, Lines(m.Talk));
        Assert.Equal((1, 0), (story.Get("SUPOX_VISITS"), story.Get("SUPOX_HOME_VISITS")));
        Assert.Equal(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        Choose(world, "g", "anyone_around_here");
        Assert.True(Said(m.Talk, "UTWIG_NEARBY"));
        Assert.Equal(new SphereTracked(Race(world, "UTWIG_SHIP"), Race(world, "UTWIG_SHIP")), m.Talk.Events.OfType<SphereTracked>().Single());
        Assert.NotEqual(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        // The reply just asked from comes first.
        Assert.Equal(new[] { "whats_wrong_with_utwig", "i_am0", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "g", "whats_wrong_with_utwig");
        Assert.True(Said(m.Talk, "BROKE_ULTRON"));
        Assert.Empty(Devices(world));
        Choose(world, "g", "whats_ultron");
        Assert.True(Said(m.Talk, "TAKE_ULTRON"));
        Assert.Equal((1, 0), (story.Get("ULTRON_CONDITION"), story.Get("SUPOX_WAR_NEWS")));
        Assert.Equal(new[] { "ULTRON_0_DEVICE" }, Devices(world));
        Assert.Equal(new[] { "what_do_i_do_now", "thanks_now_we_eat_you" }, Offers(m.Talk));
        Choose(world, "g", "thanks_now_we_eat_you");
        Assert.True(Said(m.Talk, "HIDEOUS_MONSTERS"));
        Assert.Equal((1, 0, 0, Segue.Peace), (story.Get("SUPOX_HOSTILE"), story.Get("SUPOX_VISITS"), story.Get("SUPOX_HOME_VISITS"), m.Talk.Segue));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Equal(1, story.Get("ULTRON_CONDITION"));   // kept all the same
        Away(world, "g");

        // Hostile: the encounter screen; hailed, their greeting and peace, in space and at their homeworld
        // (SUPOX_VISITS counts both).
        m = MeetInSystem(world, "h", "SUPOX_SHIP");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_1" }, Lines(m.Talk!));
        Assert.Empty(m.Talk!.Replies);
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        int star = Star(world, "SUPOX_DEFINED");
        m = Orbit(world, "h", star, 0);
        Assert.Equal((MeetingStep.Encounter, true), (m.Step, m.Infinite));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "HOSTILE_SPACE_HELLO_2" }, Lines(m.Talk!));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((1, 0), (story.Get("SUPOX_VISITS"), story.Get("SUPOX_HOME_VISITS")));

        // Attack, from the encounter screen: the fight, in space against the group, at home against the
        // endless fleet; post_supox_enc does nothing, and the homeworld isn't orbited after.
        m = MeetInSystem(world, "g", "SUPOX_SHIP", ships: 2);
        Assert.True(world.EncounterChoice("g", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal((false, 2, "supox"), (m.Battle!.EnemyFleet.Infinite, m.Battle.EnemyFleet.Ships.Count, m.Battle.EnemyFleet.Ships[0].Ship));
        Retreat(world, "g");
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((1, 1, 1), (story.Get("SUPOX_HOSTILE"), story.Get("SUPOX_VISITS"), story.Get("ULTRON_CONDITION")));
    }

    [ContentFact]
    public void TheSupoxAreAskedAboutTheUtwigByWhetherTheyHaveBeenMetAndAboutTheRepairsByTheUltronsState()
    {
        // supoxc.c:487-503: with no Ultron aboard and nothing asked yet, "anyone around here?" is "what
        // relation to the Utwig?" once the Utwig have been met in space, at home or at the bomb's moon
        // (their visits' counters), and that one shows no sphere. :527-544: with the Ultron aboard, one
        // question about its repairs by its state, until answered (SUPOX_ULTRON_HELP).
        var world = NewGame();
        var story = world.Story!;
        // The Utwig met at the bomb's moon first (BOMB_VISITS).
        var m = Orbit(world, "h", Star(world, "BOMB_DEFINED"), BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Choose(world, "h", "bye_neutral");
        MeetingTests.HearClosing(world, "h");
        Assert.Equal((1, 0, 0), (story.Get("BOMB_VISITS"), story.Get("UTWIG_VISITS"), story.Get("UTWIG_HOME_VISITS")));
        // (Their sphere, shown by that orbit, hidden again to see that this question shows none.)
        story.SetFleet("UTWIG_SHIP", story.Fleet("UTWIG_SHIP") with { KnownStrength = 0 });
        int star = Star(world, "SUPOX_DEFINED");
        m = Orbit(world, "h", star, 0);
        Assert.Equal(new[] { "i_am0", "what_relation_to_utwig", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "h", "what_relation_to_utwig");
        Assert.True(Said(m.Talk!, "UTWIG_ALLIES"));
        Assert.Empty(m.Talk!.Events.OfType<SphereTracked>());
        Assert.Equal(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        Choose(world, "h", "whats_wrong_with_utwig", "whats_ultron", "what_do_i_do_now");
        Assert.True(Said(m.Talk, "FIX_IT"));
        Assert.Equal((0, Segue.Peace), (story.Get("SUPOX_HOSTILE"), m.Talk.Segue));
        MeetingTests.HearClosing(world, "h");

        // The repairs, by the Ultron's state (the devices as --start-devices sets them: 3c's repairs),
        // each in a game of its own: the question is asked once for good (SUPOX_ULTRON_HELP is never cleared).
        var asks = new[]
        {
            ("ULTRON_0_DEVICE", "where_get_repairs", "ANCIENT_RHYME"),
            ("ULTRON_1_DEVICE", "look_i_slightly_repaired", "GREAT_DO_MORE"),
            ("ULTRON_2_DEVICE", "look_i_repaired_lots", "ALMOST_THERE"),
            ("ULTRON_3_DEVICE", "got_fixed_ultron", "GOOD_GIVE_TO_UTWIG"),
        };
        foreach (var (device, reply, line) in asks)
        {
            world = NewGame();
            story = world.Story!;
            Aboard(world, device);
            Assert.Equal(new[] { device }, Devices(world));
            m = Orbit(world, "h", Star(world, "SUPOX_DEFINED"), 0);
            // Nothing of the Utwig to ask with the Ultron aboard.
            Assert.Equal(new[] { "i_am0", reply, "bye_neutral" }, Offers(m.Talk!));
            Choose(world, "h", reply);
            Assert.True(Said(m.Talk!, line));
            Assert.Equal(1, story.Get("SUPOX_ULTRON_HELP"));
            Assert.Equal(new[] { "i_am0", "bye_neutral" }, Offers(m.Talk!));
            Choose(world, "h", "bye_neutral");
            MeetingTests.HearClosing(world, "h");
            // Whatever its state after: not asked again.
            foreach (var (other, _, _) in asks)
            {
                Aboard(world, other);
                m = Orbit(world, "h", Star(world, "SUPOX_DEFINED"), 0);
                Assert.Equal(new[] { "i_am0", "bye_neutral" }, Offers(m.Talk!));
                Choose(world, "h", "bye_neutral");
                MeetingTests.HearClosing(world, "h");
            }
        }
    }

    // ------------------------------------------------------------ the Ultron

    [ContentFact]
    public void TheBrokenUltronShownIsASickTrickAndFromAHostileGreetingAFight()
    {
        // utwigc.c:237-271: the Ultron not mended whole (ULTRON_CONDITION below 4) shown in the neutral
        // talk is DONT_WANT_TO_LOOK and a sick trick, the second line the second time (UTWIG_INFO), at
        // peace; from the hostile greeting ("hey wait, we have the Ultron", the only reply, :732-739)
        // TAUNT_US_BUT_WE_LOOK, TRICKED_US and a fight.
        var world = NewGame("g");
        var story = world.Story!;
        TakeUltron(world, "g");
        Assert.Equal(new[] { "ULTRON_0_DEVICE" }, Devices(world));
        world.TakeNotices();
        // The host, who wasn't given it, shows it: the group's.
        var m = MeetInSystem(world, "h", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "we_are_vindicator0", "what_about_urquan_1", "got_ultron", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "h", "got_ultron");
        Assert.Equal(new[] { "NEUTRAL_SPACE_HELLO_1", "DONT_WANT_TO_LOOK", "SICK_TRICK_1" }, Lines(m.Talk!));
        Assert.Equal((1, 1, 0, Segue.Peace), (story.Get("UTWIG_INFO"), story.Get("ULTRON_CONDITION"), story.Get("UTWIG_HAVE_ULTRON"), m.Talk!.Segue));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        m = MeetInSystem(world, "h", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Choose(world, "h", "got_ultron");
        Assert.Equal(new[] { "NEUTRAL_SPACE_HELLO_2", "DONT_WANT_TO_LOOK", "SICK_TRICK_2" }, Lines(m.Talk!));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal(Allied.Bad, story.Alliance("UTWIG_SHIP"));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.AllianceMade);

        // Mocked (a fight), they're hostile; then the Ultron shown from their greeting, twice.
        m = MeetInSystem(world, "h", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Choose(world, "h", "we_are_vindicator0", "why_sad", "what_ultron", "dont_be_babies");
        MeetingTests.HearClosing(world, "h");
        Retreat(world, "h");
        Away(world, "h");
        Assert.Equal((1, 0), (story.Get("UTWIG_HOSTILE"), story.Get("UTWIG_INFO")));
        foreach (var (hello, tricked) in new[] { ("HOSTILE_SPACE_HELLO_1", "TRICKED_US_1"), ("HOSTILE_SPACE_HELLO_2", "TRICKED_US_2") })
        {
            m = MeetInSystem(world, "g", "UTWIG_SHIP");
            Assert.Equal(MeetingStep.Encounter, m.Step);
            Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
            Assert.Equal(new[] { "hey_wait_got_ultron" }, Offers(m.Talk!));
            Assert.Equal(Segue.Peace, m.Talk!.Segue);   // hailed: a fight only if the reply says so
            Choose(world, "g", "hey_wait_got_ultron");
            Assert.Equal(new[] { hello, "TAUNT_US_BUT_WE_LOOK", tricked }, Lines(m.Talk));
            Assert.Equal((Segue.Hostile, 1, 1), (m.Talk.Segue, story.Get("UTWIG_INFO"), story.Get("UTWIG_HOSTILE")));
            MeetingTests.HearClosing(world, "g");
            Assert.Equal(MeetingStep.Battle, m.Step);
            Retreat(world, "g");
        }
        Assert.Equal((1, 0), (story.Get("ULTRON_CONDITION"), story.Get("UTWIG_HAVE_ULTRON")));
    }

    [ContentFact]
    public void AGuestGivesTheMendedUltronAndBothRacesAreAlliedEveryoneToldAndTheShipyardBuildsTheirShips()
    {
        // utwigc.c:272-305: the Ultron mended whole (ULTRON_CONDITION 4) shown at their homeworld:
        // HAPPY_DAYS, OK_ATTACK_KOHRAH and the mission's event (AddEvent (RELATIVE_EVENT, 0, 0, 0,
        // ADVANCE_UTWIG_SUPOX_MISSION): due at once, set aside until 3c, question 3), UTWIG_HAVE_ULTRON,
        // ULTRON_CONDITION 5 (no device any more), every visit, news and grudge of both races cleared,
        // and both allied (SetRaceAllied twice). The shipyard's list follows the alliances.
        var world = NewGame("g", "k");
        var story = world.Story!;
        // (Counters of both races set, to see the gift clear them.)
        foreach (var (field, value) in new[] { ("UTWIG_VISITS", 2), ("SUPOX_VISITS", 3), ("SUPOX_HOME_VISITS", 2), ("BOMB_VISITS", 1),
                     ("SUPOX_INFO", 1), ("UTWIG_INFO", 1), ("SUPOX_WAR_NEWS", 2), ("UTWIG_WAR_NEWS", 2), ("SUPOX_HOSTILE", 1) })
            story.Set(field, value);
        // The host docked at the starbase meanwhile: neither race's ship can be built.
        var host = world.Host.Ship;
        world.CommanderMet = true;
        world.PutInOrbit("h", Sol, Earth, Luna);
        host.Ip!.ReturnFromOrbit();
        host.Hold.ResourceUnits = 10000;
        world.VisitStarbase(world.Host);
        Assert.Equal(Activity.Starbase, host.Activity);
        Assert.False(world.Outfit("h", new BuildShip(0, "UTWIG_SHIP")));
        Assert.False(world.Outfit("h", new BuildShip(0, "SUPOX_SHIP")));
        world.TakeNotices();

        Aboard(world, "ULTRON_3_DEVICE");
        Assert.Equal(new[] { "ULTRON_3_DEVICE" }, Devices(world));
        var m = Orbit(world, "g", Star(world, "UTWIG_DEFINED"), 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(new[] { "we_are_vindicator0", "what_about_urquan_1", "got_ultron", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "g", "got_ultron");
        Assert.Equal(new[] { "NEUTRAL_HOMEWORLD_HELLO_1", "DONT_WANT_TO_LOOK", "HAPPY_DAYS", "OK_ATTACK_KOHRAH" }, Lines(m.Talk!));
        Assert.Empty(m.Talk!.Replies);
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        Assert.Equal((1, 5), (story.Get("UTWIG_HAVE_ULTRON"), story.Get("ULTRON_CONDITION")));
        Assert.Empty(Devices(world));
        foreach (var field in new[] { "UTWIG_VISITS", "SUPOX_VISITS", "UTWIG_HOME_VISITS", "SUPOX_HOME_VISITS", "BOMB_VISITS", "SUPOX_INFO",
                     "UTWIG_INFO", "SUPOX_WAR_NEWS", "UTWIG_WAR_NEWS", "SUPOX_HOSTILE", "UTWIG_HOSTILE" })
            Assert.True(story.Get(field) == 0, field);
        Assert.Equal((Allied.Good, Allied.Good), (story.Alliance("UTWIG_SHIP"), story.Alliance("SUPOX_SHIP")));
        // The mission's event: set aside, dated today, not queued and not run (UQM runs it at once:
        // UTWIG_SUPOX_MISSION 1 by the end of the reply; here it waits for 3c).
        var today = new StoryEvent("ADVANCE_UTWIG_SUPOX_MISSION", world.Clock.Year, world.Clock.Month, world.Clock.Day);
        Assert.Equal(today, Assert.Single(story.Events.SetAside, e => e.Event == "ADVANCE_UTWIG_SUPOX_MISSION"));
        Assert.DoesNotContain(story.Events, e => e.Event == "ADVANCE_UTWIG_SUPOX_MISSION");
        Assert.Equal(0, story.Get("UTWIG_SUPOX_MISSION"));
        // The others (the host, docked, and the other guest) are each told of each alliance, in the
        // talk's order; the guest who made them isn't.
        var told = world.TakeNotices().Where(n => n.Event is NoticeEvent.AllianceMade or NoticeEvent.AllianceBroken or NoticeEvent.ShipsGiven).ToList();
        foreach (var other in new[] { "h", "k" })
            Assert.Equal(new[]
            {
                new WorldNotice(other, NoticeEvent.AllianceMade, "g", "Utwig"),
                new WorldNotice(other, NoticeEvent.AllianceMade, "g", "Supox"),
            }, told.Where(n => n.PlayerId == other));
        Assert.Equal(4, told.Count);
        Assert.Equal(new[] { "g made an alliance with the Utwig: their ships are for sale at the starbase.",
                "g made an alliance with the Supox: their ships are for sale at the starbase." },
            told.Where(n => n.PlayerId == "k").Select(n => n.Text));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Null(world.DialogueLocks!.Holder("UTWIG_CONVERSATION"));

        // The shipyard: both races' ships built now, by the host (the fleet is the host's).
        Assert.True(world.Outfit("h", new BuildShip(0, "UTWIG_SHIP")));
        Assert.True(world.Outfit("h", new BuildShip(1, "SUPOX_SHIP")));
        Assert.Equal((1, 1), (world.Fleet.CountOf("UTWIG_SHIP"), world.Fleet.CountOf("SUPOX_SHIP")));
        Assert.Equal(10000u - (uint)(story.Rules.RaceShip("UTWIG_SHIP").Cost + story.Rules.RaceShip("SUPOX_SHIP").Cost), host.Hold.ResourceUnits);

        // Allies: at peace from the start (no encounter screen), their other song asked for.
        m = Orbit(world, "g", Star(world, "UTWIG_DEFINED"), 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_1" }, Lines(m.Talk!));
        Assert.Equal((true, "comm.utwig.ultron.music"), (m.Talk!.UseAltSong, m.Talk.AltSong));
        Assert.False(Offered(m.Talk, "got_ultron"));
    }

    [ContentFact]
    public void GivenFromAHostileGreetingOrTooLateTheUltronAlliesThemAllTheSame()
    {
        // utwigc.c:241-242, 274-282: from the hostile greeting's only reply TAUNT_US_BUT_WE_LOOK, then
        // the same gift, their hostility cleared; with the Kohr-Ah's victory near (KOHR_AH_FRENZY)
        // TOO_LATE and no mission, the alliances made all the same.
        var world = NewGame("g");
        var story = world.Story!;
        // Mocked by the guest in space: hostile.
        var m = MeetInSystem(world, "g", "UTWIG_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Choose(world, "g", "we_are_vindicator0", "why_sad", "what_ultron", "dont_be_babies");
        MeetingTests.HearClosing(world, "g");
        Retreat(world, "g");
        Away(world, "g");
        Assert.Equal(1, story.Get("UTWIG_HOSTILE"));
        story.Set("KOHR_AH_FRENZY", 1);
        Aboard(world, "ULTRON_3_DEVICE");
        world.TakeNotices();
        m = Orbit(world, "h", Star(world, "UTWIG_DEFINED"), 0);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { "hey_wait_got_ultron" }, Offers(m.Talk!));
        Choose(world, "h", "hey_wait_got_ultron");
        Assert.Equal(new[] { "HOSTILE_HOMEWORLD_HELLO_1", "TAUNT_US_BUT_WE_LOOK", "HAPPY_DAYS", "TOO_LATE" }, Lines(m.Talk!));
        Assert.Equal((Segue.Peace, 1, 5, 0, 0), (m.Talk!.Segue, story.Get("UTWIG_HAVE_ULTRON"), story.Get("ULTRON_CONDITION"),
            story.Get("UTWIG_HOSTILE"), story.Get("UTWIG_HOME_VISITS")));
        Assert.Equal((Allied.Good, Allied.Good), (story.Alliance("UTWIG_SHIP"), story.Alliance("SUPOX_SHIP")));
        Assert.DoesNotContain(story.Events.SetAside, e => e.Event == "ADVANCE_UTWIG_SUPOX_MISSION");
        Assert.DoesNotContain(story.Events, e => e.Event == "ADVANCE_UTWIG_SUPOX_MISSION");
        Assert.Equal(new[] { ("g", "Utwig", "Host"), ("g", "Supox", "Host") },
            world.TakeNotices().Where(n => n.Event == NoticeEvent.AllianceMade).Select(n => (n.PlayerId, n.Race, n.By)));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
    }

    // ------------------------------------------------------------ the allies

    [ContentFact]
    public void TheAlliesGiveFourShipsEachToTheHostsFleetByItsRoomWhoeverAnswers()
    {
        // utwigc.c:307-320, 400-401; supoxc.c:132-145, 221-222: at home, while the mission hasn't started
        // (UTWIG_SUPOX_MISSION 0), "can you help?": HAVE_4_SHIPS and AddEscortShips (race, 4), which
        // adds as many as there's room for (the host's fleet, whoever answers: question 4). The Utwig
        // offer it only with room (EscortFeasibilityStudy in AlliedHome); the Supox always, and with no
        // room say DONT_NEED.
        var world = NewGame("g");
        var story = world.Story!;
        GiveUltron(world, "h");
        int utwig = Star(world, "UTWIG_DEFINED"), supox = Star(world, "SUPOX_DEFINED");
        world.TakeNotices();
        Assert.Empty(world.Fleet.Ships);

        // The guest at the Utwig's homeworld: 4 Utwig ships in the host's fleet, the host told.
        var m = Orbit(world, "g", utwig, 0);
        Assert.Equal((MeetingStep.Talk, true), (m.Step, m.Infinite));
        Assert.Equal(new[] { "learn_new_info", "what_now_homeworld", "how_is_ultron", "can_you_help", "bye_allied_homeworld" }, Offers(m.Talk!));
        Choose(world, "g", "can_you_help");
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_1", "HOW_HELP", "HAVE_4_SHIPS" }, Lines(m.Talk!));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        Assert.Equal(4, world.Fleet.CountOf("UTWIG_SHIP"));
        Assert.Equal(4, world.Fleet.Ships.Count);
        Assert.Equal(new[] { new WorldNotice("h", NoticeEvent.ShipsGiven, "g", "Utwig", 4) },
            world.TakeNotices().Where(n => n.Event == NoticeEvent.ShipsGiven));
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);

        // Room for 2 more: the Supox's 4 are 2, told as 2.
        world.AddFleetShips("SPATHI_SHIP", HostFleet.MaxShips - 4 - 2);
        Assert.Equal(2, world.Fleet.Room);
        m = Orbit(world, "g", supox, 0);
        Assert.Equal((MeetingStep.Talk, true), (m.Step, m.Infinite));
        Assert.Equal(new[] { "learn_new_info", "what_now_homeworld", "how_is_ultron", "can_you_help", "bye_allied_homeworld" }, Offers(m.Talk!));
        Choose(world, "g", "can_you_help");
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_1", "HOW_HELP", "HAVE_4_SHIPS" }, Lines(m.Talk!));
        Assert.Equal((2, 4, 0), (world.Fleet.CountOf("SUPOX_SHIP"), world.Fleet.CountOf("UTWIG_SHIP"), world.Fleet.Room));
        Assert.Equal(new[] { new WorldNotice("h", NoticeEvent.ShipsGiven, "g", "Supox", 2) },
            world.TakeNotices().Where(n => n.Event == NoticeEvent.ShipsGiven));
        Assert.Equal("2 Supox ships joined the fleet (g's talk).", new WorldNotice("h", NoticeEvent.ShipsGiven, "g", "Supox", 2).Text);
        MeetingTests.HearClosing(world, "g");

        // No room: the Utwig don't offer it; the Supox do, and have none to give. Nobody is told.
        m = Orbit(world, "h", utwig, 0);
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_2" }, Lines(m.Talk!));
        Assert.Equal(new[] { "learn_new_info", "what_now_homeworld", "how_is_ultron", "bye_allied_homeworld" }, Offers(m.Talk!));
        Choose(world, "h", "bye_allied_homeworld");
        Assert.True(Said(m.Talk!, "GOODBYE_ALLIED_HOMEWORLD"));
        MeetingTests.HearClosing(world, "h");
        m = Orbit(world, "h", supox, 0);
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_2" }, Lines(m.Talk!));
        Choose(world, "h", "can_you_help");
        Assert.Equal(new[] { "ALLIED_HOMEWORLD_HELLO_2", "HOW_HELP", "DONT_NEED" }, Lines(m.Talk!));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        Assert.Equal((2, 4, HostFleet.MaxShips), (world.Fleet.CountOf("SUPOX_SHIP"), world.Fleet.CountOf("UTWIG_SHIP"), world.Fleet.Ships.Count));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.ShipsGiven);
        MeetingTests.HearClosing(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void TheAlliesTalksGoByTheMissionsStateWhichStaysWhereTheGiftLeftItUntilStage3c()
    {
        // The mission's event is set aside (question 3), so UTWIG_SUPOX_MISSION stays 0 after the gift
        // where UQM's event, run at once (clock.c:234-238), makes it 1: until 3c the allies talk as UQM's
        // would had the Ultron come too late: at home their ships at each visit while there's room, no
        // news of the war; in space the greeting alone (utwigc.c:785-799). With the state 3c's event
        // would set (--start-state UTWIG_SUPOX_MISSION=1; the event also clears their visits and news):
        // no ships, the fleet on its way, and in space what's up before the Kohr-Ah.
        var world = NewGame("g");
        var story = world.Story!;
        GiveUltron(world, "g");
        int utwig = Star(world, "UTWIG_DEFINED"), supox = Star(world, "SUPOX_DEFINED");
        Assert.Equal(0, story.Get("UTWIG_SUPOX_MISSION"));
        // Twice at the Utwig's homeworld: 4 ships each time.
        foreach (int ships in new[] { 4, 8 })
        {
            var gift = Orbit(world, "h", utwig, 0);
            Choose(world, "h", "can_you_help");
            Assert.True(Said(gift.Talk!, "HAVE_4_SHIPS"));
            Assert.Equal(ships, world.Fleet.CountOf("UTWIG_SHIP"));
            MeetingTests.HearClosing(world, "h");
        }
        // In space: the greeting alone, at peace, for each race.
        foreach (var (race, visits) in new[] { ("UTWIG_SHIP", "UTWIG_VISITS"), ("SUPOX_SHIP", "SUPOX_VISITS") })
        {
            var met = MeetInSystem(world, "g", race);
            Assert.Equal(new[] { "HELLO_DURING_KOHRAH_SPACE_1" }, Lines(met.Talk!));
            Assert.Empty(met.Talk!.Replies);
            Assert.Equal((Segue.Peace, 1), (met.Talk.Segue, story.Get(visits)));
            MeetingTests.HearClosing(world, "g");
            Assert.Null(met.Battle);
        }

        // As 3c's event would leave it once run.
        story.Set("UTWIG_SUPOX_MISSION", 1);
        story.Set("UTWIG_VISITS", 0);
        story.Set("SUPOX_VISITS", 0);
        var m = Orbit(world, "h", utwig, 0);
        Assert.Equal(new[] { "how_goes_war", "learn_new_info", "what_now_homeworld", "how_is_ultron", "bye_allied_homeworld" }, Offers(m.Talk!));
        Choose(world, "h", "how_goes_war");
        Assert.True(Said(m.Talk!, "FLEET_ON_WAY"));
        Assert.False(Offered(m.Talk!, "how_goes_war"));
        Choose(world, "h", "learn_new_info");
        Assert.True(Said(m.Talk!, "NO_NEW_INFO"));
        Choose(world, "h", "bye_allied_homeworld");
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(8, world.Fleet.CountOf("UTWIG_SHIP"));
        m = Orbit(world, "h", supox, 0);
        Assert.Equal(new[] { "how_goes_war", "learn_new_info", "what_now_homeworld", "how_is_ultron", "bye_allied_homeworld" }, Offers(m.Talk!));
        Choose(world, "h", "bye_allied_homeworld");
        MeetingTests.HearClosing(world, "h");
        foreach (var race in new[] { "UTWIG_SHIP", "SUPOX_SHIP" })
        {
            var met = MeetInSystem(world, "g", race);
            Assert.Equal(MeetingStep.Talk, met.Step);
            Assert.Equal(new[] { "HELLO_BEFORE_KOHRAH_SPACE_1" }, Lines(met.Talk!));
            Assert.Equal(new[] { "whats_up_before_space", "what_now_before_space", "bye_before_space" }, Offers(met.Talk!));
            Choose(world, "g", "whats_up_before_space");
            Assert.True(Said(met.Talk!, "GENERAL_INFO_BEFORE_SPACE_1"));
            Choose(world, "g", "bye_before_space");
            Assert.True(Said(met.Talk!, "GOODBYE_BEFORE_SPACE"));
            MeetingTests.HearClosing(world, "g");
            Assert.Null(met.Battle);
        }
    }

    // ------------------------------------------------------------ the spheres and the locks

    [ContentFact]
    public void TheUtwigsTalkShowsTheSupoxsSphere()
    {
        // utwigc.c:521-529: "what about you?" the second time is ABOUT_US_2 and StartSphereTracking
        // (SUPOX_SHIP); the first and the third show nothing. Meeting the Utwig shows their own
        // (InitCommunication's StartSphereTracking, comm.c:1363).
        var world = NewGame();
        var story = world.Story!;
        Assert.Equal((0, 0), (story.Fleet("UTWIG_SHIP").KnownStrength, story.Fleet("SUPOX_SHIP").KnownStrength));
        var m = MeetInSystem(world, "h", "UTWIG_SHIP");
        Assert.NotEqual(0, story.Fleet("UTWIG_SHIP").KnownStrength);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        // The Ur-Quan's two questions first: only then are the Utwig's own offered.
        Choose(world, "h", "what_about_urquan_1", "what_about_urquan_2");
        Assert.Equal(new[] { "we_are_vindicator0", "what_about_you_1", "bye_neutral" }, Offers(m.Talk!));
        Choose(world, "h", "what_about_you_1");
        Assert.True(Said(m.Talk!, "ABOUT_US_1"));
        Assert.Empty(m.Talk!.Events.OfType<SphereTracked>());
        Assert.Equal(0, story.Fleet("SUPOX_SHIP").KnownStrength);
        // The reply just asked from comes first.
        Assert.Equal(new[] { "what_about_you_2", "we_are_vindicator0", "bye_neutral" }, Offers(m.Talk));
        Choose(world, "h", "what_about_you_2");
        Assert.True(Said(m.Talk, "ABOUT_US_2"));
        int supox = Race(world, "SUPOX_SHIP");
        Assert.Equal(new SphereTracked(supox, supox), m.Talk.Events.OfType<SphereTracked>().Single());
        Assert.NotEqual(0, story.Fleet("SUPOX_SHIP").KnownStrength);
        Choose(world, "h", "what_about_you_3");
        Assert.True(Said(m.Talk, "ABOUT_US_3"));
        Assert.Single(m.Talk.Events.OfType<SphereTracked>());
        Assert.Equal(new[] { "we_are_vindicator0", "bye_neutral" }, Offers(m.Talk));
        Assert.Equal(3, story.Get("UTWIG_WAR_NEWS"));
    }

    [ContentFact]
    public void TheUtwigsAndTheSupoxsLocksAreTheirOwn()
    {
        // The dialogue lock is a race's (decision 5; RACE_SHIP_FOR_COMM maps each conversation to its own
        // race): while the guest talks to the Supox, the host at their homeworld starts nothing and is
        // told, and meets the Utwig all the same; the Utwig's lock holds at their homeworld and at the
        // bomb's moon alike.
        var world = NewGame("g");
        int utwig = Star(world, "UTWIG_DEFINED"), supox = Star(world, "SUPOX_DEFINED"), bomb = Star(world, "BOMB_DEFINED");
        var mg = Orbit(world, "g", supox, 0);
        Assert.Equal(MeetingStep.Talk, mg.Step);
        world.TakeNotices();
        Leave(world, "h");
        world.PutInOrbit("h", supox, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "g is talking to the Supox."));
        var mh = Orbit(world, "h", utwig, 0);
        Assert.IsType<UtwigScript>(mh.Talk!.Script);
        Assert.Equal(("g", "h"), (world.DialogueLocks!.Holder("SUPOX_CONVERSATION"), world.DialogueLocks.Holder("UTWIG_CONVERSATION")));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        // The guest done with the Supox, at the bomb's moon while the host talks to the Utwig: nothing, and told.
        Choose(world, "g", "bye_neutral");
        MeetingTests.HearClosing(world, "g");
        Assert.Null(world.DialogueLocks.Holder("SUPOX_CONVERSATION"));
        world.TakeNotices();
        Leave(world, "g");
        world.PutInOrbit("g", bomb, BombPlanet, BombMoon);
        Assert.Null(world.MeetingOf("g"));
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "Host is talking to the Utwig."));
        // The Supox are free for the guest meanwhile.
        mg = Orbit(world, "g", supox, 0);
        Assert.IsType<SupoxScript>(mg.Talk!.Script);
        Assert.Equal(("g", "h"), (world.DialogueLocks.Holder("SUPOX_CONVERSATION"), world.DialogueLocks.Holder("UTWIG_CONVERSATION")));
        Assert.True(Offered(mh.Talk, "bye_neutral"));
    }
}
