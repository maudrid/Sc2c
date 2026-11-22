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
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Ilwrath and the Spathi met in space and at their worlds
/// (docs/plans/stage-3b.md, item 7): their talks as RaceCommunication picks
/// them, the Ilwrath homeworld's orbit starting the Ilwrath's talk against
/// their infinite fleet (genilw.c), Spathiwa's moon running the Spathi at
/// home (spahome.c, stage 3b-2 item 2: HomeTalkTests), and the opening's
/// Ilwrath at Earth holding the Ilwrath's talk. The scripts' every path is
/// TalkTests' recording.
/// </summary>
[Trait("Topic", "world")]
public class IlwrathSpathiMeetingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));

    // A new game with the talks' content, the locks and the battles; the
    // races that fly are the game's own.
    private static GameWorld NewGame(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        world.TalkLibrary = Library.Value;
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

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    private static int Star(GameWorld world, string special) => world.Systems!.Map.Stars.First(s => s.Special == special).Number;

    // A group of a race in hyperspace right by the host, the host in hyperspace.
    private static HyperEncounter GroupByHost(GameWorld world, string race, int ships = 2)
    {
        world.PutInHyperspace("h");
        var f = world.Host.Ship.Flight;
        f.PlaceAtUniverse(5800, 6100);
        f.Stop();
        f.StarJustLeft = null;
        var e = new HyperEncounter
        {
            Race = Race(world, race), Roller = "h", OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story!.Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 300 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    [ContentFact]
    public void TheIlwrathMetInSpaceTalkThenFight()
    {
        var world = NewGame();
        GroupByHost(world, "ILWRATH_SHIP");
        var m = Meet(world, "h");
        // Hostile (init_ilwrath_comm): the encounter screen; hailed, their space hello.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.IsType<IlwrathScript>(m.Talk!.Script);
        Assert.Equal("h", world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "INIT_HELLO_SPACE"));
        Assert.Equal(1, world.Story!.Get("ILWRATH_VISITS"));
        Assert.Equal(1, world.Story.Fleet("ILWRATH_SHIP").KnownStrength);   // the sphere tracked
        // News, then goodbye: the battle against their two Avengers.
        Assert.True(world.ChooseReply("h", Reply(m.Talk, "whats_up_space_1"), m.Round));
        Assert.Equal(1, world.Story.Get("ILWRATH_INFO"));
        MeetingTests.HearClosing(world, "h");   // their last words on the screen first (item 11)
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(new[] { "ilwrath", "ilwrath" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
        Assert.False(m.Battle.EnemyFleet.Infinite);
        Assert.Null(world.DialogueLocks.Holder("ILWRATH_CONVERSATION"));
    }

    [ContentFact]
    public void TheSpathiMetInSpacePartInPeaceOrFightAndAreAngryAfter()
    {
        var world = NewGame();
        var group = GroupByHost(world, "SPATHI_SHIP");
        var m = Meet(world, "h");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.IsType<SpathiScript>(m.Talk!.Script);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "INIT_NEUTRAL_HELLO_SPACE"));
        Assert.True(world.ChooseReply("h", Reply(m.Talk, "part_in_peace"), m.Round));
        MeetingTests.HearClosing(world, "h");
        // In peace: no battle, the group one-shot, the Spathi as they were.
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        Assert.Null(m.Battle);
        Assert.Equal(HyperEncounter.OneShot, group.Flags & HyperEncounter.OneShot);
        Assert.Equal((0, 1), (world.Story!.Get("SPATHI_MANNER"), world.Story.Get("SPATHI_VISITS")));
        Frames(world, 60);

        GroupByHost(world, "SPATHI_SHIP", ships: 1);
        m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "SUBSEQUENT_NEUTRAL_HELLO_SPACE"));
        Assert.True(world.ChooseReply("h", Reply(m.Talk!, "prepare_to_die"), m.Round));
        Assert.True(world.ChooseReply("h", Reply(m.Talk!, "attack_you_now"), m.Round));
        MeetingTests.HearClosing(world, "h");
        // A fight: post_spathi_enc makes them angry, their visits counted afresh.
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("spathi", Assert.Single(m.Battle!.EnemyFleet.Ships).Ship);
        Assert.Equal((1, 0), (world.Story.Get("SPATHI_MANNER"), world.Story.Get("SPATHI_VISITS")));
    }

    [ContentFact]
    public void OrbitingTheIlwrathHomeworldStartsTheirTalkAgainstAnInfiniteFleet()
    {
        var world = NewGame();
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var ship = world.Host.Ship;
        var m = world.MeetingOf("h")!;
        // No orbit: the ship by the world, in the meeting (genilw.c).
        Assert.Null(ship.Ip!.Orbit);
        Assert.Equal(IpFlight.EscapeFrames, ship.Ip.EscapeCounter);
        Assert.Equal(((int, int?)?)(0, null), m.World);
        Assert.True(m.Infinite);
        Assert.Equal((int?)star, m.Star);
        Assert.Null(m.Group);
        Assert.Equal("h", world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        Assert.Equal(1, world.Story!.Fleet("ILWRATH_SHIP").KnownStrength);
        // The talk knows it's at the homeworld (its own GLOBAL_FLAGS_AND_DATA), in a system of random groups.
        var talk = m.Talk!;
        Assert.IsType<IlwrathScript>(talk.Script);
        Assert.Equal(1 << 7, talk.OwnFields["GLOBAL_FLAGS_AND_DATA"]);
        Assert.Equal((TalkActivity.InInterplanetary, (string?)null), (talk.Activity, talk.BattleGroup));
        // The system's Ilwrath stand down (NotifyOthers IPNL_ALL_CLEAR).
        var d = world.SystemGroups!.Systems[star];
        Assert.NotEmpty(d.Groups);
        Assert.All(d.Groups.Where(g => g.Race == m.Race), g => Assert.NotEqual(0, g.Task & IpGroup.IgnoreFlagship));
        // Hostile: the encounter screen; hailed, the homeworld's hello.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(talk, "INIT_HOME_HELLO"));
        Assert.Equal(1, world.Story.Get("ILWRATH_HOME_VISITS"));
        Assert.True(world.ChooseReply("h", Reply(talk, "want_info_on_gods"), m.Round));
        Assert.True(Said(talk, "SO_MUCH_TO_KNOW"));
        Assert.True(world.ChooseReply("h", Reply(talk, "enough_gods"), m.Round));
        Assert.True(world.ChooseReply("h", Reply(talk, "bye_homeworld"), m.Round));
        MeetingTests.HearClosing(world, "h");
        // The battle against their infinite fleet: one Avenger, back each time it dies.
        Assert.Equal(MeetingStep.Battle, m.Step);
        var wb = m.Battle!;
        Assert.True(wb.EnemyFleet.Infinite);
        Assert.Equal("ilwrath", Assert.Single(wb.EnemyFleet.Ships).Ship);
        Assert.True(wb.Battle.InfiniteEnemy);
        Assert.Null(world.DialogueLocks.Holder("ILWRATH_CONVERSATION"));
        // The host retreats: the meeting over, the race's groups turned on
        // the players as the host comes back into flight there (DoMissions).
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
            Frames(world, 1, ShipInput.TakeOff);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Null(world.MeetingOf("h"));
        Frames(world, 2);
        Assert.Contains(d.Groups, g => g.Race == m.Race && g.DestLoc == IpGroup.InterceptPlayer);
        Assert.Equal(Activity.InnerSystem, ship.Activity);
    }

    [ContentFact]
    public void FlyingIntoTheIlwrathHomeworldStartsTheirTalkWhateverTheirMission()
    {
        var world = NewGame();
        world.Story!.Set("ILWRATH_DECEIVED", 1);   // on their way to the Thraddash: not hostile
        int star = Star(world, "ILWRATH_DEFINED");
        world.Story.SetFleet("ILWRATH_SHIP", world.Story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        world.Story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        world.PutInOrbit("h", star, 0);   // extinct: an orbit, then out of it by the world
        // Living again (StartSphereTracking: no strength, but not extinct).
        world.Story.SetAlliance("ILWRATH_SHIP", Allied.Bad);
        Frames(world, 1, ShipInput.Action);
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
        // Away from the world, round and back into it (the world at the inner view's centre).
        Frames(world, 30, ShipInput.Thrust);
        for (int i = 0; i < 400 && world.MeetingOf("h") is null; ++i)
        {
            var q = world.Host.Ship.Ip!;
            double angle = Math.Atan2(-q.Location.X, q.Location.Y);   // 0 north, clockwise
            int toward = (int)Math.Round(angle / (2 * Math.PI) * 16 + 16) % 16;
            int turn = (toward - q.Facing + 16) % 16;
            Frames(world, 1, turn == 0 ? ShipInput.Thrust : turn < 8 ? ShipInput.Right : ShipInput.Left);
        }
        var m = world.MeetingOf("h")!;
        Assert.Equal(((int, int?)?)(0, null), m.World);
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        // Their homeworld branch whatever their mission: hostile, the screen;
        // a hail and a goodbye fight (bye_homeworld is CombatIsInevitable).
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "INIT_HOME_HELLO"));
    }

    [ContentFact]
    public void AnAttackAtTheHomeworldFightsTheInfiniteFleet()
    {
        var world = NewGame();
        world.PutInOrbit("h", Star(world, "ILWRATH_DEFINED"), 0);
        var m = world.MeetingOf("h")!;
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.DoesNotContain(m.Talk!.Events, e => e is LineQueued);
    }

    [ContentFact]
    public void AnotherPlayerTalkingToTheIlwrathKeepsTheHomeworldAnOrdinaryFlyBy()
    {
        var world = NewGame("g");
        GroupByHost(world, "ILWRATH_SHIP");
        Meet(world, "h");
        Assert.Equal("h", world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        world.TakeNotices();
        world.PutInOrbit("g", Star(world, "ILWRATH_DEFINED"), 0);
        // No meeting and no orbit for the guest: they're told who's talking.
        Assert.Null(world.MeetingOf("g"));
        Assert.Null(world.Find("g")!.Ship.Ip!.Orbit);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "Host is talking to the Ilwrath."));
    }

    // The host's homeworld meeting at its battle, the host retreated from it
    // while the guest fights on (handoff question 3): the meeting unfinished,
    // the race's lock let go. The guest is kept alive and unharmed.
    private static (Meeting Meeting, int Star) HostOutOfTheHomeworldsBattleGuestFightingOn(GameWorld world)
    {
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        world.PutInSystem("g", star);
        var gip = world.Find("g")!.Ship.Ip!;
        gip.PlaceInOrbit(0, null);
        gip.ReturnFromOrbit();   // the guest by the same world
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.True(world.LetIntoBattle("g"));
        Frames(world, 3);
        Assert.Same(m.Battle, world.Find("g")!.Ship.Battle);
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
            HostFlies(world, m, ShipInput.TakeOff);
        Assert.Null(world.Host.Ship.Battle);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Same(m, world.MeetingOf("h"));
        Assert.Null(world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        world.TakeNewMeetings();
        return (m, star);
    }

    // A frame of the host's input while the guest fights on in the
    // meeting's battle, kept alive and unharmed.
    private static void HostFlies(GameWorld world, Meeting m, ShipInput input)
    {
        world.SubmitInput("h", world.Frame + 1, input);
        world.SubmitInput("g", world.Frame + 1, ShipInput.None);
        if (m.Battle!.Enemy.RaceDesc is { } enemy)
            enemy.EnergyLevel = 0;
        foreach (var f in m.Battle.Fighters)
            if (f.Player.Id == "g" && f.Starship.RaceDesc is { } rd)
                rd.CrewLevel = rd.MaxCrew;
        world.Step();
    }

    // The guest retreats from the meeting's battle: it's over, and the meeting with it.
    private static void GuestRetreats(GameWorld world)
    {
        for (int i = 0; i < 300 && world.Find("g")!.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.None);
            world.SubmitInput("g", world.Frame + 1, ShipInput.TakeOff);
            world.Step();
        }
        Assert.Null(world.Find("g")!.Ship.Battle);
    }

    [ContentFact]
    public void AHostOutOfTheHomeworldsBattleFliesBackIntoItAsAnOrdinaryFlyBy()
    {
        var world = NewGame("g");
        var (m, star) = HostOutOfTheHomeworldsBattleGuestFightingOn(world);
        var ship = world.Host.Ship;
        // Away from the world, round and back into it, while the guest fights on.
        for (int i = 0; i < 30; ++i)
            HostFlies(world, m, ShipInput.Thrust);
        Assert.NotEqual(new IpPoint(0, 0), ship.Ip!.Location);
        bool reached = false;
        for (int i = 0; i < 400 && !reached; ++i)
        {
            var q = ship.Ip!;
            double angle = Math.Atan2(-q.Location.X, q.Location.Y);   // 0 north, clockwise
            int toward = (int)Math.Round(angle / (2 * Math.PI) * 16 + 16) % 16;
            int turn = (toward - q.Facing + 16) % 16;
            HostFlies(world, m, turn == 0 ? ShipInput.Thrust : turn < 8 ? ShipInput.Right : ShipInput.Left);
            reached = ship.Ip!.Location == new IpPoint(0, 0);   // touched: put back by the world (ReturnFromOrbit)
        }
        Assert.True(reached);
        Assert.Same(m.Battle, world.Find("g")!.Ship.Battle);
        // No talk and no orbit: the ship by the world, the meeting the same.
        Assert.Same(m, world.MeetingOf("h"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Empty(world.TakeNewMeetings());
        Assert.Null(world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        Assert.Null(ship.Ip!.Orbit);
        Assert.Equal(Activity.InnerSystem, ship.Activity);
        for (int i = 0; i < 5; ++i)
            HostFlies(world, m, ShipInput.None);
        Assert.Same(m, world.MeetingOf("h"));
        Assert.Null(ship.Ip!.Orbit);
        // The guest's battle over, the meeting ends: the Ilwrath turn on the players there.
        GuestRetreats(world);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.MeetingOf("h"));
        var d = world.SystemGroups!.Systems[star];
        Assert.Equal(m.Race, d.EncounterRace);
        Frames(world, 2);
        Assert.Contains(d.Groups, g => g.Race == m.Race && g.DestLoc == IpGroup.InterceptPlayer);
        // Back into the homeworld now, the host meets the Ilwrath again.
        world.PutInOrbit("h", star, 0);
        Assert.NotSame(m, world.MeetingOf("h"));
        Assert.Equal(MeetingStep.Encounter, world.MeetingOf("h")!.Step);
    }

    [ContentFact]
    public void AHostOutOfTheHomeworldsBattlePutInItsOrbitStartsNoTalk()
    {
        var world = NewGame("g");
        var (m, star) = HostOutOfTheHomeworldsBattleGuestFightingOn(world);
        world.TakeNotices();
        world.PutInOrbit("h", star, 0);
        Assert.Same(m, world.MeetingOf("h"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Empty(world.TakeNewMeetings());
        Assert.Null(world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        Assert.DoesNotContain(world.TakeNotices(), n => n.PlayerId == "h");
        // The meeting ends with the guest's battle, as ever.
        GuestRetreats(world);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(m.Race, world.SystemGroups!.Systems[star].EncounterRace);
    }

    [ContentFact]
    public void TheIlwrathExtinctTheirHomeworldIsOrbitedAsAnyWorld()
    {
        var world = NewGame();
        var story = world.Story!;
        story.SetFleet("ILWRATH_SHIP", story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        int star = Star(world, "ILWRATH_DEFINED");
        Assert.Equal(-1, world.WorldTalkRace(star, 0, null));
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        // Their other worlds, and the homeworld's moons, never start it.
        var living = NewGame();
        Assert.Equal(-1, living.WorldTalkRace(star, 1, null));
        Assert.Equal(-1, living.WorldTalkRace(star, 0, 0));
        Assert.Equal(Race(living, "ILWRATH_SHIP"), living.WorldTalkRace(star, 0, null));
    }

    [ContentFact]
    public void SpathiwasMoonRunsTheSpathiAtHome()
    {
        // genspa.c starts SPATHI_CONVERSATION with GLOBAL_FLAGS_AND_DATA bit
        // 7, which init_race runs as the Spathi at home (spahome.c, stage
        // 3b-2 item 2: HomeTalkTests).
        var world = NewGame();
        Assert.Equal("spahome", TalkLibrary.ScriptFor(Scripts, "SPATHI_CONVERSATION", world.Story!, 1 << 7));
        Assert.Equal("spathi", TalkLibrary.ScriptFor(Scripts, "SPATHI_CONVERSATION", world.Story!, 0));
        Assert.Equal("comandr", TalkLibrary.ScriptFor(Scripts, "COMMANDER_CONVERSATION",
            StoryState.NewGame(world.Story.Rules, world.Clock), 0));
        Assert.Equal("starbas", TalkLibrary.ScriptFor(Scripts, "COMMANDER_CONVERSATION", world.Story, 0));
        Assert.Equal("ilwrath", TalkLibrary.ScriptFor(Scripts, "ILWRATH_CONVERSATION", world.Story, 1 << 7));
        int star = Star(world, "SPATHI_DEFINED");
        Assert.Equal(Race(world, "SPATHI_SHIP"), world.WorldTalkRace(star, 0, 0));
        Assert.IsType<SpathiHomeScript>(world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1, 1 << 7)!.Script);
        Assert.IsType<SpathiScript>(world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1, 0)!.Script);
        world.PutInOrbit("h", star, 0, 0);
        Assert.IsType<SpathiHomeScript>(world.MeetingOf("h")!.Talk!.Script);
        Assert.Null(world.Host.Ship.Ip!.Orbit);
    }

    [ContentFact]
    public void TheOpeningsIlwrathAtEarthHoldTheIlwrathsTalk()
    {
        // While the opening's Ilwrath answer the probe at Earth
        // (PROBE_ILWRATH_ENCOUNTER: their talk story, TalkRules), an Ilwrath
        // group passes through a player it touches, who's told; their
        // homeworld starts nothing.
        var world = NewGame();
        world.Story!.Set("PROBE_ILWRATH_ENCOUNTER", 1);
        world.TakeNotices();
        var group = GroupByHost(world, "ILWRATH_SHIP");
        Frames(world, 60);
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(group.MeetingPlayer);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "Someone is talking to the Ilwrath."));
        // Another race's world isn't held by it (the Spathi at home, theirs).
        Assert.Equal(Race(world, "SPATHI_SHIP"), world.WorldTalkRace(Star(world, "SPATHI_DEFINED"), 0, 0));
        world.PutInOrbit("h", Star(world, "ILWRATH_DEFINED"), 0);
        Assert.Null(world.MeetingOf("h"));
        // Once it's over, they're met.
        world.Story.Set("PROBE_ILWRATH_ENCOUNTER", 0);
        world.Host.Ship.Ip!.EscapeCounter = 0;
        world.PutInOrbit("h", Star(world, "ILWRATH_DEFINED"), 0);
        Assert.NotNull(world.MeetingOf("h"));
    }

    [ContentFact]
    public void TheGodsOrderingTheIlwrathAgainstTheThraddashSetsTheirMissionAside()
    {
        // The broadcaster's branch (3b-2's), through the world's talk: the
        // event AddEvent adds is the story's, set aside until 3c runs it.
        var world = NewGame();
        var talk = world.RaceTalk(world.Host, Race(world, "ILWRATH_SHIP"), 1, 1 << 4)!;
        talk.Activity = TalkActivity.InInterplanetary;
        talk.Start();
        Assert.True(Said(talk, "ILWRATH_BELIEVE"));
        talk.Choose(Reply(talk, "go_kill_thraddash"));
        Assert.Equal(1, world.Story!.Get("ILWRATH_DECEIVED"));
        Assert.Contains(new EventAdded(true, 0, 0, 0, "ADVANCE_ILWRATH_MISSION"), talk.Events);
        var aside = Assert.Single(world.Story.Events.SetAside, e => e.Event == "ADVANCE_ILWRATH_MISSION");
        Assert.Equal((world.Clock.Year, world.Clock.Month, world.Clock.Day), (aside.Year, aside.Month, aside.Day));
    }

    [ContentFact]
    public void FwiffoJoinsTheHostsFleetAloneInHisEluder()
    {
        // Fwiffo on Pluto (3c's start), through the world's talk: the Eluder
        // joins the host's fleet with one crew, captained by Fwiffo
        // (SetEscortCrewComplement); a ship of the race already there with
        // its race's crew would be the one changed, as UQM's first match.
        var world = NewGame();
        world.Story!.Set("FOUND_PLUTO_SPATHI", 1);
        var rules = world.Story.Rules;
        world.Fleet.Add(rules, "SPATHI_SHIP", 1, () => 0);
        world.Fleet.SetCrew(rules, 0, 5);   // not the starting crew: passed over
        var talk = world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1)!;
        talk.Activity = TalkActivity.InInterplanetary;
        talk.Start();
        Assert.True(Said(talk, "SORRY_ABOUT_THAT"));
        foreach (int r in new[] { 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 })
            talk.Choose(r);
        talk.Choose(Reply(talk, "join_us"));
        Assert.True(Said(talk, "WILL_JOIN"));
        Assert.Contains(new EscortCrewSet(6, 1, 21), talk.Events);
        Assert.Equal(new[] { (5, 5), (1, 21) }, world.Fleet.Ships.Select(s => (s.Crew, (int)s.Captain)));
        Assert.Equal(Segue.Peace, talk.Segue);
        Assert.Equal(2, world.Story.Get("FOUND_PLUTO_SPATHI"));
    }

    [ContentFact]
    public void AMeetingsTalkIsGivenItsAnswerersArilouSpaceSide()
    {
        // ARILOU_SPACE_SIDE, each ship's own (the stage 3b-2 plan's item 1):
        // the answerer's, in normal space (0) until stage 3c's QuasiSpace sets it.
        var world = NewGame();
        var talk = world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1)!;
        Assert.Equal(0, talk.OwnFields["ARILOU_SPACE_SIDE"]);
        world.Host.Ship.ArilouSpaceSide = 2;
        talk = world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1)!;
        Assert.Equal(2, talk.OwnFields["ARILOU_SPACE_SIDE"]);
        Assert.Equal(2, talk.Get("ARILOU_SPACE_SIDE"));
        Assert.True(StoryState.NotShared.ContainsKey("ARILOU_SPACE_SIDE"));   // kept out of the shared story
    }

    [ContentTheory]
    [InlineData(1, "WILL_JOIN")]
    [InlineData(0, "TOO_SCARY")]
    public void FwiffoJoinsOnlyWithRoomInTheFleet(int room, string line)
    {
        // EscortFeasibilityStudy: the room left in the host's fleet.
        var world = NewGame();
        world.Story!.Set("FOUND_PLUTO_SPATHI", 1);
        uint n = 0;
        world.Fleet.Add(world.Story.Rules, "ARILOU_SHIP", HostFleet.MaxShips - room, () => n++);
        var talk = world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1)!;
        talk.Start();
        foreach (int r in new[] { 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 })
            talk.Choose(r);
        talk.Choose(Reply(talk, "join_us"));
        Assert.True(Said(talk, line));
        Assert.Equal(HostFleet.MaxShips, world.Fleet.Count);   // full either way
    }

    [ContentFact]
    public void TheHostsLogShowsTheHomeworldsInfiniteFleet()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        host.World.BattleKit = WorldBattleTests.Kit();
        host.World.Story!.Set("STARBASE_AVAILABLE", 1);
        var log = new List<string>();
        host.Logged += log.Add;
        host.World.PutInOrbit("host", Star(host.World, "ILWRATH_DEFINED"), 0);
        host.Step(ShipInput.None);
        Assert.Contains("host at the encounter screen: an infinite fleet of Ilwrath ships, hail or attack", log);
    }
}
