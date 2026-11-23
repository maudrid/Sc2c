// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Shofixti and the Zoq-Fot-Pik in the world (the stage 3b-2 plan's
/// item 5): Tanaka's group at the Shofixti's system met with his talk, his
/// damaged scout in its battles, the two fights he survives and the third
/// meeting that wins him over, his death marked as a player leaves with
/// his group gone, Katana after him with the Maidens aboard, the recruited
/// Shofixti flying off; the Zoq-Fot-Pik scout met and flying off, their
/// homeworld's talk (no standing down) and the alliance. The scripts'
/// every path is TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class ShofixtiZoqFotPikTests
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

    // The system's own group (Tanaka's, the scout's) brought by the player
    // in their view, coming at them (as AddEncounterNear places a group).
    private static IpGroup Bring(GameWorld world, string id, int star)
    {
        var d = world.SystemGroups!.Systems[star];
        var g = d.Groups.Single();
        var ip = world.Find(id)!.Ship.Ip!;
        g.SysLoc = ip.View;
        g.Task = IpGroup.OnStation;
        g.DestLoc = IpGroup.InterceptPlayer;
        g.LocX = (short)(ip.Location.X + 600);
        g.LocY = ip.Location.Y;
        d.Elements = null;
        return g;
    }

    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 600 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

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

    // Tanaka's (or Katana's) meeting at the Shofixti's system, the host there.
    private static Meeting MeetTanaka(GameWorld world, int star)
    {
        Bring(world, "h", star);
        var m = Meet(world, "h");
        Assert.Equal(Race(world, "SHOFIXTI_SHIP"), m.Race);
        Assert.IsType<ShofixtiScript>(m.Talk!.Script);
        // At peace (init_shofixti_comm): no encounter screen, under the Shofixti's lock.
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Equal("h", world.DialogueLocks!.Holder("SHOFIXTI_CONVERSATION"));
        return m;
    }

    // A hostile reply, the fight it starts against his damaged scout, and the host's retreat: he survives.
    private static void FightAndRetreat(GameWorld world, string reply)
    {
        Choose(world, "h", reply);
        var m = MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        var (enemy, ship) = m.Battle!.Enemies.Single();
        Assert.Equal(ShipDescriptions.TanakasScout, ship.Id);
        Assert.Equal(1, enemy.CrewLevel);
        Retreat(world, "h");
        Assert.Null(world.MeetingOf("h"));
    }

    [ContentFact]
    public void TanakaSurvivesTwoFightsThenTheThirdMeetingWinsHimOver()
    {
        var world = NewGame();
        int star = Star(world, "SHOFIXTI_DEFINED");
        world.PutInSystem("h", star);
        Frames(world, 1);
        // GenerateShofixti_initNpcs: his group of one ship of one crew, his captain Tanaka.
        var d = world.SystemGroups!.Systems[star];
        Assert.Equal("SHOFIXTI_DEFINED", d.Defined);
        var own = Assert.Single(world.SystemGroups.Defined["SHOFIXTI_DEFINED"]);
        Assert.Equal(new[] { 1 }, own.Ships);
        Assert.Equal(new[] { 5 + 16 }, own.Captains);

        // The first meeting: his first greeting; "no one insults" fights.
        var m = MeetTanaka(world, star);
        Assert.True(Said(m.Talk!, "HOSTILE_TANAKA_1"));
        Assert.Null(TalkView.Of(m)!.Speakers);   // his lines have no callbacks
        FightAndRetreat(world, "no_one_insults");
        Assert.Equal(1, world.Story!.Get("SHOFIXTI_STACK2"));
        // He's alive, in the system with his one crew.
        Assert.Equal(new[] { 1 }, Assert.Single(world.SystemGroups.Defined["SHOFIXTI_DEFINED"]).Ships);

        // The second: "mighty words" fights again.
        m = MeetTanaka(world, star);
        Assert.True(Said(m.Talk!, "HOSTILE_TANAKA_2"));
        FightAndRetreat(world, "mighty_words");
        Assert.Equal(2, world.Story.Get("SHOFIXTI_STACK2"));

        // The third: "donkey breath", and he realises his mistake: friendly from now on.
        m = MeetTanaka(world, star);
        Choose(world, "h", "donkey_breath");
        Assert.True(Said(m.Talk!, "DGRUNTI"));
        Assert.Equal(3, world.Story.Get("SHOFIXTI_STACK2"));
        Choose(world, "h", "i_am_nice");
        Choose(world, "h", "yes_and_no");
        Choose(world, "h", "go_ahead");
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(m.Battle);
        // Not recruited: he stays, and isn't sent off.
        var tanaka = d.Groups.Single();
        Assert.NotEqual(IpGroup.Flee, tanaka.Task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup));
        // Met again, friendly.
        m = MeetTanaka(world, star);
        Assert.True(Said(m.Talk!, "FRIENDLY_HELLO"));
        // Leaving with him alive marks nothing.
        Choose(world, "h", "bye0");
        MeetingTests.HearClosing(world, "h");
        world.PutInHyperspace("h");
        Assert.Equal(0, world.Story.Get("SHOFIXTI_KIA"));
    }

    // Tanaka (or Katana) met and fought, his scout destroyed.
    private static void DestroyHim(GameWorld world, int star)
    {
        MeetTanaka(world, star);
        KillHim(world);
    }

    // From his hostile talk: a fight, his scout destroyed.
    private static void KillHim(GameWorld world)
    {
        Choose(world, "h", "dont_want_to_fight");   // always offered, and fights
        var m = MeetingTests.HearClosing(world, "h");
        var wb = m.Battle!;
        // Hit once it has warped in.
        for (int i = 0; i < 300 && wb.Enemies[0].Starship.Ship is null; ++i)
            Frames(world, 1);
        Frames(world, 30);
        WorldBattleTests.Kill(wb.Battle, wb.Enemies[0].Starship);
        for (int i = 0; i < 600 && world.MeetingOf("h") is not null; ++i)
            Frames(world, 1);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Null(world.MeetingOf("h"));
    }

    [ContentFact]
    public void TanakasDeathIsMarkedAsAPlayerLeavesWithHisGroupGoneThenKatanaComesWithTheMaidens()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "SHOFIXTI_DEFINED");
        world.PutInSystem("h", star);
        world.PutInSystem("g", star);
        Frames(world, 1);
        story.Set("SHOFIXTI_VISITS", 3);
        DestroyHim(world, star);
        var d = world.SystemGroups!.Systems[star];
        Assert.Empty(d.Groups);
        Assert.Equal(0, story.Get("SHOFIXTI_KIA"));   // not until someone leaves
        // The guest leaves the system first: his death is marked then (uninitNpcs), his visits counted afresh.
        world.PutInHyperspace("g");
        Assert.Equal((1, 0, 0), (story.Get("SHOFIXTI_KIA"), story.Get("SHOFIXTI_VISITS"), story.Get("SHOFIXTI_BRO_KIA")));
        world.PutInHyperspace("h");
        Assert.Equal(0, story.Get("SHOFIXTI_BRO_KIA"));   // no Maidens aboard: Katana isn't marked
        Frames(world, 1);

        // Without the Maidens nobody's there; with them (3c's pickup: --start-devices) Katana is.
        world.PutInSystem("h", star);
        Frames(world, 1);
        Assert.Null(world.SystemGroups.Systems[star].Defined);
        world.PutInHyperspace("h");
        Frames(world, 1);
        story.Set("MAIDENS_ON_SHIP", 1);
        world.PutInSystem("h", star);
        Frames(world, 1);
        d = world.SystemGroups.Systems[star];
        Assert.Equal("SHOFIXTI_DEFINED", d.Defined);
        var katana = Assert.Single(world.SystemGroups.Defined["SHOFIXTI_DEFINED"]);
        Assert.Equal(new[] { 5 + 16 + 1 }, katana.Captains.ToArray());
        var m = MeetTanaka(world, star);
        Assert.True(Said(m.Talk!, "HOSTILE_KATANA_1"));
        // Killed too: his brother's death marked as the host leaves.
        KillHim(world);
        world.PutInHyperspace("h");
        Assert.Equal(1, story.Get("SHOFIXTI_BRO_KIA"));
    }

    [ContentFact]
    public void TheRecruitedShofixtiFlyOffAndArentMarkedLost()
    {
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "SHOFIXTI_DEFINED");
        story.Set("SHOFIXTI_STACK2", 3);
        story.Set("MAIDENS_ON_SHIP", 1);
        world.PutInSystem("h", star);
        Frames(world, 1);
        var m = MeetTanaka(world, star);
        Assert.True(Said(m.Talk!, "FRIENDLY_HELLO"));
        Choose(world, "h", "important_duty");
        Choose(world, "h", "procreating_wildly");
        Choose(world, "h", "females");
        Assert.True(Said(m.Talk!, "LEAPING_HAPPINESS"));
        Assert.Equal((1, 0), (story.Get("SHOFIXTI_RECRUITED"), story.Get("MAIDENS_ON_SHIP")));
        // The return event: set aside until 3c runs the calendar.
        Assert.Contains(m.Talk!.Events, e => e is EventAdded { Event: "SHOFIXTI_RETURN_EVENT" });
        MeetingTests.HearClosing(world, "h");
        // check_old_shofixti: his group, re-forming from the touch, flies off out of the system.
        var d = world.SystemGroups!.Systems[star];
        var tanaka = d.Groups.Single();
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship | IpGroup.ReformGroup, (byte)0), (tanaka.Task, tanaka.DestLoc));
        for (int i = 0; i < 4000 && tanaka.InSystem; ++i)
            Frames(world, 1);
        Assert.False(tanaka.InSystem);
        world.PutInHyperspace("h");
        Frames(world, 1);
        // Gone for good, and not lost: recruited.
        Assert.Empty(world.SystemGroups.Defined["SHOFIXTI_DEFINED"].Single().Ships);
        Assert.Equal(0, story.Get("SHOFIXTI_KIA"));
        world.PutInSystem("h", star);
        Frames(world, 1);
        Assert.Null(world.SystemGroups.Systems[star].Defined);
    }

    [ContentFact]
    public void TheZoqFotPikScoutIsMetAndFliesOff()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "ZOQ_SCOUT_DEFINED");
        world.PutInSystem("h", star);
        Frames(world, 1);
        var d = world.SystemGroups!.Systems[star];
        Assert.Equal("ZOQ_SCOUT_DEFINED", d.Defined);
        Bring(world, "h", star);
        var m = Meet(world, "h");
        Assert.Equal(Race(world, "ZOQFOTPIK_SHIP"), m.Race);
        Assert.IsType<ZoqFotPikScript>(m.Talk!.Script);
        // Not allied: hostile, so the encounter screen first.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "SCOUT_HELLO0"));
        Assert.Equal(1, story.Get("MET_ZOQFOT"));
        // Every line spoken by Zoq or Pik, named by its callback (sent to the client: the two speakers).
        Assert.All(m.Talk!.Events.OfType<LineQueued>(), l => Assert.Contains(l.Callback, new[] { "SelectAlienZOQ", "SelectAlienPIK" }));
        // The view sends each line's speaker (protocol 30), Zoq first and alternating, through the codec whole.
        var view = TalkView.Of(m)!;
        Assert.Equal(view.Lines.Count, view.Speakers!.Count);
        Assert.Equal(new[] { "SelectAlienZOQ", "SelectAlienPIK", "SelectAlienZOQ" }, view.Speakers.Take(3));
        var sent = TalkView.Decode(view.Encode())!;
        Assert.True(view.Same(sent));
        Assert.Equal(view.Speakers, sent.Speakers);
        Assert.False(view.Same(view with { Speakers = view.Speakers.Select(_ => "SelectAlienPIK").ToList() }));
        Choose(world, "h", "valuable_info");
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(world.MeetingOf("h"));
        // GenerateZoqFotPikScout_reinitNpcs: the scout flies off out of the system.
        var scout = d.Groups.Single();
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship | IpGroup.ReformGroup, (byte)0), (scout.Task, scout.DestLoc));
        for (int i = 0; i < 4000 && scout.InSystem; ++i)
            Frames(world, 1);
        Assert.False(scout.InSystem);
        world.PutInHyperspace("h");
        Frames(world, 1);
        // Gone for good: met, it isn't made again.
        world.PutInSystem("g", star);
        Frames(world, 1);
        Assert.Null(world.SystemGroups.Systems[star].Defined);
        Assert.Empty(world.SystemGroups.Systems[star].Groups);
    }

    [ContentFact]
    public void TheScoutFliesOffAfterABattleItSurvives()
    {
        var world = NewGame();
        int star = Star(world, "ZOQ_SCOUT_DEFINED");
        world.PutInSystem("h", star);
        Frames(world, 1);
        Bring(world, "h", star);
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "h");
        var scout = world.SystemGroups!.Systems[star].Groups.Single();
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship | IpGroup.ReformGroup, (byte)0), (scout.Task, scout.DestLoc));
    }

    [ContentFact]
    public void TheHomeworldsTalkMakesTheAllianceAndTellsEveryone()
    {
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "ZOQFOT_DEFINED");
        world.PutInOrbit("g", star, 0);
        var m = Assert.IsType<Meeting>(world.MeetingOf("g"));
        Assert.IsType<ZoqFotPikScript>(m.Talk!.Script);
        Assert.Equal(1 << 7, m.Talk.Get("GLOBAL_FLAGS_AND_DATA"));
        // Not allied: the encounter screen (an infinite fleet), then the home's first meeting.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "INIT_HOME_HELLO0"));
        world.TakeNotices();
        // Asked to the end: we are, then each question, then how can I help, sure.
        foreach (var r in new[] { "we_are_vindicator0", "your_race", "where_from", "what_emergency", "how_can_i_help", "sure" })
            Choose(world, "g", r);
        Assert.Equal(Story.Allied.Good, story.Alliances[Race(world, "ZOQFOTPIK_SHIP")]);
        Assert.Contains(m.Talk.Events, e => e is EventAdded { Event: "ZOQFOT_DISTRESS_EVENT" });
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.AllianceMade);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        // Allied: the next orbit's talk is at peace, the allies' greeting.
        world.PutInHyperspace("g");
        Frames(world, 1);
        world.PutInOrbit("g", star, 0);
        m = world.MeetingOf("g")!;
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "ALLIED_HOME_HELLO_10"));
    }
}
