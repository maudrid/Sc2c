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
/// The VUX and the Yehat in the world (the stage 3b-2 plan's item 6): the
/// VUX homeworld's talk and its fight against their endless fleet,
/// Admiral ZEX at the Maidens' world at peace, his fleet's fight, the
/// beast given and his death (the world orbited from then on); the Yehat
/// homeworld's talk; the revolt offered with a Shofixti ship in the host's
/// fleet whoever answers, its event set aside, the royalists after; the
/// rebels (brought as --start-encounter brings them until 3c's revolt
/// gives them a fleet) under the Yehat's lock, their ships to the host's
/// fleet. The scripts' every path is TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class VuxYehatTests
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
    public void TheVuxHomeworldsTalkEndsInAFightWithTheirEndlessFleet()
    {
        var world = NewGame();
        int star = Star(world, "VUX_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("h"));
        Assert.IsType<VuxScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.True(m.Infinite);
        Assert.Equal("h", world.DialogueLocks!.Holder("VUX_CONVERSATION"));
        // Hostile (init_vux_comm): the encounter screen, then the homeworld's greeting.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "HOMEWORLD_HELLO_1"));
        Assert.Equal(1, world.Story!.Get("VUX_HOME_VISITS"));
        // Their reasons asked, then the farewell: a fight.
        Choose(world, "h", "why_so_mean");
        Assert.True(Said(m.Talk, "URQUAN_SLAVES"));
        Choose(world, "h", "bye_hostile_space");
        Assert.True(Said(m.Talk, "GOODBYE_AND_DIE_HOSTILE_SPACE_1"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.All(m.Battle.EnemyFleet.Ships, s => Assert.Equal("vux", s.Ship));
        Retreat(world, "h");
        // genvux.c returns after the talk at the homeworld, whatever happened: no orbit.
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((1, 1), (world.Story.Get("VUX_STACK_1"), world.Story.Get("VUX_STACK_4")));
    }

    [ContentFact]
    public void ZexTalksAtPeaceAndKeepsTheWorldWhileHeLives()
    {
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "MAIDENS_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("h"));
        Assert.IsType<VuxScript>(m.Talk!.Script);
        Assert.Equal(1 << 6, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.True(m.Infinite);
        // At peace (bit 6): no encounter screen, his first greeting and his menagerie.
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk, "ZEX_HELLO_1"));
        Choose(world, "h", "what_you_do_here");
        Assert.True(Said(m.Talk, "MY_MENAGERIE"));
        Choose(world, "h", "bye_zex");
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Null(world.MeetingOf("h"));
        // He lives: the ship stays out of orbit (genvux.c returns while ZEX isn't dead).
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 1), (story.Get("ZEX_IS_DEAD"), story.Get("ZEX_STACK_1")));

        // Threatened ("take it by force", then "regardless"): a fight against his endless fleet; he lives on.
        world.PutInOrbit("h", star, 0);
        m = world.MeetingOf("h")!;
        Assert.True(Said(m.Talk!, "ZEX_HELLO_2"));
        Choose(world, "h", "take_by_force");
        Choose(world, "h", "regardless");
        Assert.True(Said(m.Talk!, "THEN_FIGHT"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 2, 0), (story.Get("ZEX_IS_DEAD"), story.Get("ZEX_STACK_3"), story.Get("ZEX_VISITS")));
    }

    [ContentFact]
    public void TheBeastGivenKillsZexAndTheWorldIsOrbitedFromThen()
    {
        var world = NewGame("g");
        var story = world.Story!;
        // The beast aboard (3c's pickup; --start-devices VUX_BEAST_DEVICE): the group's, whoever carries it.
        story.Set("VUX_BEAST_ON_SHIP", 1);
        int star = Star(world, "MAIDENS_DEFINED");
        world.PutInOrbit("g", star, 0);
        var m = world.MeetingOf("g")!;
        Assert.Equal("g", world.DialogueLocks!.Holder("VUX_CONVERSATION"));
        // Not offered until his wish is known: the creature asked about.
        Assert.False(Offered(m.Talk!, "i_have_beast"));
        foreach (var r in new[] { "what_you_do_here", "what_about_menagerie", "what_about_creature" })
            Choose(world, "g", r);
        Assert.Equal(1, story.Get("KNOW_ZEX_WANTS_MONSTER"));
        Choose(world, "g", "i_have_beast");
        Assert.True(Said(m.Talk!, "GIVE_BEAST"));
        Choose(world, "g", "why_trust_1");
        Choose(world, "g", "ok_take_beast");
        Assert.True(Said(m.Talk!, "FOOL_AIEE0"));
        Assert.Contains(m.Talk!.Events, e => e is ColorTransform { Map: 1 });   // his colours fade
        // Drawn on the client: the fade comes in the view, as FOOL_AIEE1 starts (differences.md row 83).
        var view = TalkView.Of(m)!;
        Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.Fade, 1, Talk.OneSecond / 4) }, view.Screen!.Changes);
        Assert.Equal(2, view.Lines.Count);
        Assert.Equal((1, 0), (story.Get("ZEX_IS_DEAD"), story.Get("VUX_BEAST_ON_SHIP")));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // genvux.c falls through to the orbit once he's dead.
        var guest = world.Find("g")!.Ship;
        Assert.Equal(Activity.Orbit, guest.Activity);
        Assert.Equal(((int, int?)?)(0, null), guest.Ip!.Orbit);
        // And from then on the world is orbited, by anyone: no talk.
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void AVuxGroupInSpaceIsMetWithTheirTalk()
    {
        var world = NewGame();
        var m = MeetInSystem(world, "h", "VUX_SHIP", 2);
        Assert.IsType<VuxScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "SPACE_HELLO_1"));
        Choose(world, "h", "kill_you_squids_1");
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(new[] { "vux", "vux" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
    }

    [ContentFact]
    public void TheYehatHomeworldRefusesTheAllianceAndFights()
    {
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "YEHAT_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("h"));
        Assert.IsType<YehatScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        Assert.True(m.Infinite);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "HOMEWORLD_HELLO_1"));
        // The alliance demanded with the answerer's names in it: "the enemy must die".
        int demand = Reply(m.Talk, "i_demand_you_ally_homeworld0");
        Assert.Contains("Zelnick", m.Talk.Replies[demand].Text);
        Choose(world, "h", "i_demand_you_ally_homeworld0");
        Assert.True(Said(m.Talk, "ENEMY_MUST_DIE"));
        Assert.Equal(1, story.Get("NO_YEHAT_ALLY_HOME"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.All(m.Battle!.EnemyFleet.Ships, s => Assert.Equal("yehat", s.Ship));
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void AGuestOffersTheHostsShofixtiAndTheYehatRevolt()
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("SHOFIXTI_VISITS", 1);   // Tanaka met
        // Without a Shofixti ship in the host's fleet: "he's not here", a trick, and a fight.
        var m = MeetInSystem(world, "g", "YEHAT_SHIP");
        Assert.IsType<YehatScript>(m.Talk!.Script);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Choose(world, "g", "shofixti_alive_1");
        Assert.True(Said(m.Talk, "SEND_HIM_OVER_1"));
        Assert.False(Offered(m.Talk, "ok_send"));
        Choose(world, "g", "not_here");
        Assert.True(Said(m.Talk, "JUST_A_TRICK_1"));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "g");
        Assert.Equal(0, story.Get("YEHAT_CIVIL_WAR"));

        // A Shofixti ship in the host's fleet (only --start-fleet brings one before 3c): the guest may send it.
        world.Fleet.Add(story.Rules, "SHOFIXTI_SHIP", 1, () => 0u);
        m = MeetInSystem(world, "g", "YEHAT_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Choose(world, "g", "shofixti_alive_2");   // told already
        Choose(world, "g", "ok_send");
        Assert.True(Said(m.Talk!, "WE_REVOLT"));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        Assert.Equal(1, story.Get("YEHAT_CIVIL_WAR"));
        // The revolt's event set aside until 3c runs the calendar.
        Assert.Contains(story.Events.SetAside, e => e.Event == "YEHAT_REBEL_EVENT");
        Assert.Equal(0, story.Fleet("YEHAT_REBEL_SHIP").ActualStrength);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Equal(1, world.Fleet.CountOf("SHOFIXTI_SHIP"));   // the ship stays (UQM's too)

        // The royalists from then on.
        m = MeetInSystem(world, "h", "YEHAT_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "ROYALIST_SPACE_HELLO_1"));
    }

    [ContentFact]
    public void TheRebelsTalkUnderTheYehatsLockAndGiveShipsToTheHostsFleet()
    {
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("YEHAT_CIVIL_WAR", 1);
        world.TakeNotices();
        // The rebels' group (--start-encounter YEHAT_REBEL_SHIP until 3c's revolt): at peace, no encounter screen.
        var m = MeetInSystem(world, "g", "YEHAT_REBEL_SHIP");
        Assert.Equal(Race(world, "YEHAT_REBEL_SHIP"), m.Race);
        Assert.IsType<YehatRebelScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk, "REBEL_HELLO_1"));
        // Their lock is the Yehat's: nobody meets a Yehat group meanwhile.
        Assert.Equal("g", world.DialogueLocks!.Holder("YEHAT_CONVERSATION"));
        Assert.Equal("g", world.DialogueLocks.Holder("YEHAT_REBEL_CONVERSATION"));
        Assert.False(world.MayMeet(world.Host, Race(world, "YEHAT_SHIP")));
        // Their ships: 4 royalist Yehat ships to the host's fleet, whoever answers; everyone told.
        Choose(world, "g", "any_ships");
        Assert.True(Said(m.Talk, "HAVE_ALL_SHIPS"));
        Assert.Equal(4, world.Fleet.CountOf("YEHAT_SHIP"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.ShipsGiven);
        // The next ships a month on.
        Assert.Equal((3, 17, 0), (story.Get("YEHAT_SHIP_MONTH"), story.Get("YEHAT_SHIP_DAY"), story.Get("YEHAT_SHIP_YEAR")));
        Choose(world, "g", "bye_rebel");
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.Null(world.DialogueLocks.Holder("YEHAT_CONVERSATION"));
        Assert.True(world.MayMeet(world.Host, Race(world, "YEHAT_SHIP")));
        // Asked again the same day: none yet.
        m = MeetInSystem(world, "h", "YEHAT_REBEL_SHIP");
        Assert.True(Said(m.Talk!, "REBEL_HELLO_2"));
        Choose(world, "h", "any_ships");
        Assert.True(Said(m.Talk!, "NO_SHIPS_YET"));
        Assert.Equal(4, world.Fleet.CountOf("YEHAT_SHIP"));
    }
}
