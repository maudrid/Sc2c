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
using Crateris.Core.Net;
using Crateris.Core.Planetside;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Spathi and the Slylandro at home (docs/plans/stage-3b-2.md, item
/// 2): Spathiwa's moon starting the Spathi at home (spahome.c) against
/// their infinite fleet, with the password, the quest and the alliance;
/// the Evil Ones on Spathiwa counted as genspa.c counts them; the
/// Slylandro gas giant starting the Slylandro at home (slyhome.c) with no
/// ships, under the probes' lock, and the self-destruct code and its
/// ramp-down. The scripts' every path is TalkTests' recording.
/// </summary>
[Trait("Topic", "world")]
public class HomeTalkTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

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

    private static void Frames(GameWorld world, int frames, ShipInput input = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, input);
            world.Step();
        }
    }

    private static int Star(GameWorld world, string special) => world.Systems!.Map.Stars.First(s => s.Special == special).Number;

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // Replies chosen in turn by their phrases.
    private static void Say(GameWorld world, string id, Meeting m, params string[] phrases)
    {
        foreach (var p in phrases)
            Assert.True(world.ChooseReply(id, Reply(m.Talk!, p), m.Round), p);
    }

    // Orbiting Spathiwa's moon (genspa.c) after a meeting there: the ship by the moon, out of orbit.
    private static Meeting OrbitSpathiwasMoon(GameWorld world, string id = "h")
    {
        var ship = world.Find(id)!.Ship;
        if (ship.Ip is { } ip)
            ip.EscapeCounter = 0;
        world.PutInOrbit(id, Star(world, "SPATHI_DEFINED"), 0, 0);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing at Spathiwa's moon");
    }

    // ---- Spathiwa's moon

    [ContentFact]
    public void SpathiwasMoonStartsTheSpathiAtHomeAgainstTheirInfiniteFleet()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "SPATHI_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        var m = OrbitSpathiwasMoon(world);
        var ship = world.Host.Ship;
        // No orbit: the ship by the moon, in the meeting, against their infinite fleet (CloneShipFragment).
        Assert.Null(ship.Ip!.Orbit);
        Assert.Equal(IpFlight.EscapeFrames, ship.Ip.EscapeCounter);
        Assert.Equal(((int, int?)?)(0, 0), m.World);
        Assert.True(m.Infinite);
        Assert.Equal(Race(world, "SPATHI_SHIP"), m.Race);
        Assert.Equal("h", world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        Assert.Equal(1, story.Fleet("SPATHI_SHIP").KnownStrength);   // StartSphereTracking
        // The Spathi at home (init_race by bit 7), told where they are.
        var talk = m.Talk!;
        Assert.IsType<SpathiHomeScript>(talk.Script);
        Assert.Equal(1 << 7, talk.OwnFields["GLOBAL_FLAGS_AND_DATA"]);
        // The system's Spathi stand down, and the guest there is told so.
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "g" && n.Text == "The Spathi hold off: Host is meeting them.");
        // Not at peace (SPATHI_MANNER 0): the encounter screen; hailed, the password asked.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(talk, "WHAT_IS_PASSWORD"));
        Assert.Equal("comm.safeones.music", talk.Events.OfType<SongChosen>().Single().Resource);
        Assert.Equal(1, story.Get("SPATHI_HOME_VISITS"));
        // Without the password (from the Melnorme or Fwiffo), the answerer's own names offered; a wrong one is a fight.
        Assert.Contains(talk.Replies, r => r.Text.Contains("Vindicator"));
        Say(world, "h", m, "guph_florp");
        Assert.True(Said(talk, "WRONG_PASSWORD"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal("spathi", Assert.Single(m.Battle.EnemyFleet.Ships).Ship);
        // post_spahome_enc: angry from now on, the visits from the start.
        Assert.Equal((1, 0, 0), (story.Get("SPATHI_MANNER"), story.Get("SPATHI_VISITS"), story.Get("SPATHI_HOME_VISITS")));
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
            Frames(world, 1, ShipInput.TakeOff);
        Assert.Equal(MeetingStep.Done, m.Step);
        // Never in orbit: the generator returns after the talk whatever happened.
        Assert.Null(ship.Ip!.Orbit);
        Assert.Equal(Activity.InnerSystem, ship.Activity);
        // Spathiwa itself is orbited as any world.
        ship.Ip.EscapeCounter = 0;
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, ship.Activity);
    }

    [ContentFact]
    public void ThePasswordTheQuestAndThePartyMakeTheAlliance()
    {
        // The Melnorme's password (--start-state KNOW_SPATHI_PASSWORD=1
        // SPATHI_HOME_VISITS=7, their trading not ported yet), the council,
        // the quest taken; the Evil Ones eliminated, the party; the alliance
        // made, told to the guest, the shield event queued (run six months
        // on since stage 3c item 1).
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_SPATHI_PASSWORD", 1);
        story.Set("SPATHI_HOME_VISITS", 7);
        var m = OrbitSpathiwasMoon(world);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Say(world, "h", m, "good_password", "we_come_in_peace", "hand_in_friendship", "stronger", "yes_we_are",
            "how_prove", "what_test", "tell_evil", "i_accept");
        Assert.True(Said(m.Talk!, "AWAIT_RETURN"));
        Assert.Equal((1, 3), (story.Get("SPATHI_QUEST"), story.Get("SPATHI_MANNER")));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);   // at peace: no fight
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.Host.Ship.Ip!.Orbit);

        // At peace now (SPATHI_MANNER 3): no encounter screen. The quest's
        // question; without the Evil Ones gone, a lie.
        m = OrbitSpathiwasMoon(world);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "HOW_GO_EFFORTS"));
        Say(world, "h", m, "bye_before_party");
        MeetingTests.HearClosing(world, "h");

        // The Evil Ones eliminated (Spathiwa's pickups: EvilOnesOnSpathiwaAreCountedAsTaken).
        story.Set("SPATHI_CREATURES_EXAMINED", 1);
        story.Set("SPATHI_CREATURES_ELIMINATED", 1);
        m = OrbitSpathiwasMoon(world);
        Say(world, "h", m, "killed_them_all_1");
        Assert.True(Said(m.Talk!, "WILL_CHECK_1"));
        Assert.Equal(1, story.Get("SPATHI_PARTY"));
        Assert.Equal(0, story.Get("LIED_ABOUT_CREATURES"));
        MeetingTests.HearClosing(world, "h");

        // The party, and the alliance.
        world.TakeNotices();
        m = OrbitSpathiwasMoon(world);
        Assert.True(Said(m.Talk!, "MUST_PARTY_1"));
        Say(world, "h", m, "deals_a_deal", "how_long", "reneging", "return_beasts", "do_as_we_say");
        Assert.True(Said(m.Talk!, "DEPART_FOR_EARTH"));
        Assert.Equal(Allied.Good, story.Alliances[Race(world, "SPATHI_SHIP")]);
        Assert.Contains(new RaceAllied(Race(world, "SPATHI_SHIP"), true, 1, true), m.Talk!.Events);
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "g"
            && n.Text == "Host made an alliance with the Spathi: their ships are for sale at the starbase.");
        Assert.Contains(story.Events, e => e.Event == "SPATHI_SHIELD_EVENT");
        MeetingTests.HearClosing(world, "h");

        // Allied: the allies' hello.
        m = OrbitSpathiwasMoon(world);
        Assert.True(Said(m.Talk!, "HELLO_ALLIES_1"));
        Assert.Equal(1, m.Talk!.ColorMap);
    }

    [ContentFact]
    public void FwiffoInTheHostsFleetIsAHostageWhoeverAnswers()
    {
        // HaveEscortShip reads the host's fleet, whoever answers (the plan's
        // question 4): a guest at the council with Fwiffo in the host's
        // fleet can hold him hostage; without him, say he was killed.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("FOUND_PLUTO_SPATHI", 2);
        world.Fleet.Add(story.Rules, "SPATHI_SHIP", 1, () => 0);
        var m = OrbitSpathiwasMoon(world, "g");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        // Fwiffo found: the password is known.
        Assert.True(world.ChooseReply("g", Reply(m.Talk!, "good_password"), m.Round));
        Assert.True(world.ChooseReply("g", Reply(m.Talk!, "spathi_on_pluto"), m.Round));
        Assert.Contains(m.Talk!.Replies, r => r.Phrase == Scripts["spahome"].PhraseNumber("hostage"));
        Assert.DoesNotContain(m.Talk.Replies, r => r.Phrase == Scripts["spahome"].PhraseNumber("killed_fwiffo"));
        Assert.True(world.ChooseReply("g", Reply(m.Talk, "hostage"), m.Round));
        Assert.True(Said(m.Talk, "GUN_TO_HEAD"));
        Assert.Equal(3, story.Get("FOUND_PLUTO_SPATHI"));

        // Without him in the host's fleet: killed_fwiffo instead.
        var other = NewGame("g");
        other.Story!.Set("FOUND_PLUTO_SPATHI", 2);
        m = OrbitSpathiwasMoon(other, "g");
        Assert.True(other.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(other.ChooseReply("g", Reply(m.Talk!, "good_password"), m.Round));
        Assert.True(other.ChooseReply("g", Reply(m.Talk!, "spathi_on_pluto"), m.Round));
        Assert.Contains(m.Talk!.Replies, r => r.Phrase == Scripts["spahome"].PhraseNumber("killed_fwiffo"));
        Assert.DoesNotContain(m.Talk.Replies, r => r.Phrase == Scripts["spahome"].PhraseNumber("hostage"));
    }

    [ContentFact]
    public void AnotherPlayerTalkingToTheSpathiKeepsTheMoonAnOrdinaryFlyBy()
    {
        var world = NewGame("g");
        var first = OrbitSpathiwasMoon(world, "g");
        Assert.Equal("g", world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        world.TakeNotices();
        world.PutInOrbit("h", Star(world, "SPATHI_DEFINED"), 0, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "g is talking to the Spathi."));
        Assert.Same(first, world.MeetingOf("g"));
    }

    [ContentFact]
    public void ShieldedSpathiwasMoonIsOrbitedAsAnyWorld()
    {
        // genspa.c: once the Spathi have shielded themselves (3c's event), no talk.
        var world = NewGame();
        world.Story!.Set("SPATHI_SHIELDED_SELVES", 1);
        world.PutInOrbit("h", Star(world, "SPATHI_DEFINED"), 0, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    // ---- the Evil Ones (genspa.c's GenerateSpathi_pickupLife)

    [Fact]
    public void TheEvilOnesAreCountedAsGenspaCountsThem()
    {
        var story = StoryState.NewGame(StoryRules.Load(DataDir), new GameClock(2155, 2, 17));
        var spathiwa = new WorldId(5, 0, null);
        // Another world's life: nothing (the default generator's).
        StoryWorlds.PickupLife("SPATHI_DEFINED", new WorldId(5, 0, 0), story, 0);
        StoryWorlds.PickupLife("SOL_DEFINED", new WorldId(5, 0, null), story, 31);
        Assert.Equal((0, 0), (story.Get("SPATHI_CREATURES_EXAMINED"), story.Get("SPATHI_CREATURES_ELIMINATED")));
        // The first examined; the last of the 32 (31 taken before it) eliminates them.
        StoryWorlds.PickupLife("SPATHI_DEFINED", spathiwa, story, 0);
        Assert.Equal((1, 0), (story.Get("SPATHI_CREATURES_EXAMINED"), story.Get("SPATHI_CREATURES_ELIMINATED")));
        StoryWorlds.PickupLife("SPATHI_DEFINED", spathiwa, story, 30);
        Assert.Equal(0, story.Get("SPATHI_CREATURES_ELIMINATED"));
        StoryWorlds.PickupLife("SPATHI_DEFINED", spathiwa, story, 31);
        Assert.Equal(1, story.Get("SPATHI_CREATURES_ELIMINATED"));
    }

    [ContentTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvilOnesOnSpathiwaAreCountedAsTaken(bool last)
    {
        // A canned Evil One brought aboard a lander on Spathiwa: examined;
        // with the other 31 taken before (by anyone: the world's taken nodes
        // are everyone's), eliminated.
        var world = NewGame();
        int star = Star(world, "SPATHI_DEFINED");
        var spathiwa = new WorldId(star, 0, null);
        world.PutInOrbit("h", star, 0);
        Assert.Equal(StoryWorlds.EvilOnes, world.Host.Ship.Orbit!.Nodes!.Life.Count);
        const int keep = 7;
        if (last)
        {
            for (int n = 0; n < StoryWorlds.EvilOnes; ++n)
                if (n != keep)
                    world.Planets.SetRetrieved(spathiwa, ScanType.Biological, n);
        }
        Assert.Equal(DispatchRefusal.None, world.Dispatch("h", 120, 40));
        var lander = world.Host.Ship.Lander!;
        var surface = world.SurfaceOf(spathiwa)!;
        for (int i = 0; i < 400 && lander.Phase != LanderPhase.Ground; ++i)
            Frames(world, 1);
        Assert.Equal(LanderPhase.Ground, lander.Phase);
        var one = surface.Elements.First(e => e.Kind == SurfaceElementKind.Creature && e.Node == keep);
        Assert.Equal(23, one.LifeKind);   // NUM_CREATURE_TYPES: an Evil One
        // Canned (shotCreature: its value, 1, its mass_points) and under the lander.
        one.HitPoints = 0;
        one.MassPoints = 1;
        one.Sheet = SurfaceSheet.Canister;
        one.Frame = 0;
        for (int i = 0; i < 20 && !world.Planets.IsRetrieved(spathiwa, ScanType.Biological, keep); ++i)
        {
            one.X = lander.X;
            one.Y = lander.Y;
            Frames(world, 1);
        }
        Assert.True(world.Planets.IsRetrieved(spathiwa, ScanType.Biological, keep));
        Assert.Equal(1, world.Story!.Get("SPATHI_CREATURES_EXAMINED"));
        Assert.Equal(last ? 1 : 0, world.Story.Get("SPATHI_CREATURES_ELIMINATED"));
    }

    [ContentFact]
    public void AMineralTakenOnSpathiwaCountsNoEvilOne()
    {
        // Spathiwa's minerals go to GenerateDefault_pickupMinerals (genspa.c),
        // never to pickupLife: with 31 Evil Ones taken, a mineral leaves both
        // flags as they were.
        var world = NewGame();
        int star = Star(world, "SPATHI_DEFINED");
        var spathiwa = new WorldId(star, 0, null);
        world.PutInOrbit("h", star, 0);
        for (int n = 1; n < StoryWorlds.EvilOnes; ++n)
            world.Planets.SetRetrieved(spathiwa, ScanType.Biological, n);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("h", 120, 40));
        var lander = world.Host.Ship.Lander!;
        var surface = world.SurfaceOf(spathiwa)!;
        for (int i = 0; i < 400 && lander.Phase != LanderPhase.Ground; ++i)
            Frames(world, 1);
        Assert.Equal(LanderPhase.Ground, lander.Phase);
        var mineral = surface.Elements.First(e => e.Kind == SurfaceElementKind.Mineral);
        int node = mineral.Node;
        for (int i = 0; i < 20 && !world.Planets.IsRetrieved(spathiwa, ScanType.Mineral, node); ++i)
        {
            mineral.X = lander.X;
            mineral.Y = lander.Y;
            Frames(world, 1);
        }
        Assert.True(world.Planets.IsRetrieved(spathiwa, ScanType.Mineral, node));
        Assert.Equal(0, world.Story!.Get("SPATHI_CREATURES_EXAMINED"));
        Assert.Equal(0, world.Story.Get("SPATHI_CREATURES_ELIMINATED"));
    }

    // ---- the Slylandro gas giant

    // Orbiting the Slylandro gas giant (gensly.c).
    private static Meeting OrbitTheGasGiant(GameWorld world, string id = "h")
    {
        world.PutInOrbit(id, Star(world, "SLYLANDRO_DEFINED"), 3);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing at the gas giant");
    }

    [ContentFact]
    public void TheGasGiantStartsTheSlylandroAtHomeWithNoShipsUnderTheProbesLock()
    {
        var world = NewGame("g");
        int star = Star(world, "SLYLANDRO_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        var m = OrbitTheGasGiant(world);
        var talk = m.Talk!;
        Assert.IsType<SlylandroHomeScript>(talk.Script);
        Assert.False(talk.OwnFields.ContainsKey("GLOBAL_FLAGS_AND_DATA"));   // gensly.c sets none
        Assert.Empty(m.Ships);
        Assert.False(m.Infinite);
        Assert.Equal(((int, int?)?)(3, null), m.World);
        // No ships, no BuildBattle: no escape counter; at peace: no encounter screen.
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(talk, "HELLO_1"));
        // The probes' lock (RACE_SHIP_FOR_COMM): nobody else talks to a probe meanwhile.
        Assert.Equal("h", world.DialogueLocks!.Holder("SLYLANDRO_HOME_CONVERSATION"));
        Assert.Equal("h", world.DialogueLocks.Holder("SLYLANDRO_CONVERSATION"));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "Host is meeting the Slylandro."));
        // The names spliced into the first reply are the answerer's.
        Assert.Contains(talk.Replies, r => r.Text.Contains("Vindicator"));
        Say(world, "h", m, "bye");
        Assert.True(Said(talk, "GOODBYE_1"));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(m.Battle);
        Assert.Null(world.DialogueLocks.Holder("SLYLANDRO_CONVERSATION"));
        // Never in orbit: the gas giant isn't entered (gensly.c returns after the talk).
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
        Assert.Equal(1, world.Story!.Get("SLYLANDRO_HOME_VISITS"));
    }

    [ContentFact]
    public void TheSelfDestructCodeRampsTheProbesDownAndDestroysThem()
    {
        // The probe's bug shown and its programming known (as the
        // recording's probe-bug start): the code asked for. DESTRUCT_CODE_ON_SHIP
        // set, SLYLANDRO_MULTIPLIER up by one and at once down by one
        // (SLYLANDRO_RAMP_DOWN run now), the next ramp-down in 23 days.
        var world = NewGame("g");
        var story = world.Story!;
        foreach (var (field, value) in new[]
                 {
                     ("SLYLANDRO_HOME_VISITS", 1), ("SLYLANDRO_STACK1", 7), ("SLYLANDRO_STACK2", 1), ("SLYLANDRO_STACK3", 3),
                     ("SLYLANDRO_STACK4", 2), ("PLAYER_KNOWS_PROBE", 1), ("PROBE_EXHIBITED_BUG", 1), ("SLYLANDRO_STACK5", 1),
                     ("SLYLANDRO_STACK6", 1), ("SLYLANDRO_STACK7", 3), ("SLYLANDRO_STACK8", 3), ("SLYLANDRO_STACK9", 3),
                     ("PLAYER_KNOWS_PROGRAM", 1), ("PLAYER_KNOWS_PRIORITY", 1), ("PLAYER_KNOWS_EFFECTS", 1),
                     ("SLYLANDRO_MULTIPLIER", 3),
                 })
            story.Set(field, value);
        var m = OrbitTheGasGiant(world, "g");
        Say(world, "g", m, "know_more_probe", "talk_more_probe_attack", "probe_has_bug", "tell_me_about_rep_2",
            "what_about_rep_priorities", "think_about_rep_priorities", "mega_self_destruct");
        Assert.True(Said(m.Talk!, "WHY_YES_THERE_IS"));
        Assert.Equal((1, 1), (story.Get("DESTRUCT_CODE_ON_SHIP"), story.Get("SLYLANDRO_KNOW_BROKEN")));
        Assert.Contains(new StateSet("SLYLANDRO_MULTIPLIER", 4), m.Talk!.Events);
        Assert.Equal(3, story.Get("SLYLANDRO_MULTIPLIER"));   // the ramp-down's first step, run at once
        var next = Assert.Single(story.Events, e => e.Event == "SLYLANDRO_RAMP_DOWN");
        var due = new GameClock(world.Clock.Year, world.Clock.Month, world.Clock.Day);
        for (int d = 0; d < 23; ++d)
            due.NextDay();
        Assert.Equal((due.Year, due.Month, due.Day), (next.Year, next.Month, next.Day));
        // The probes aren't asked about any more; bye.
        Assert.DoesNotContain(m.Talk.Replies, r => r.Phrase == Scripts["slyhome"].PhraseNumber("what_are_probes"));
        Say(world, "g", m, "bye");
        MeetingTests.HearClosing(world, "g");

        // The recall greeting on the next visit, once.
        m = OrbitTheGasGiant(world, "g");
        Assert.True(Said(m.Talk!, "RECALL_PROGRAM_1"));
        Assert.Equal(1, story.Get("RECALL_VISITS"));
    }
}
