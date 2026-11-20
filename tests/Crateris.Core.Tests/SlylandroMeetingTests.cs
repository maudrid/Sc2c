// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Slylandro probes (docs/plans/stage-3b.md, item 8): met in
/// hyperspace and in systems with their talk (slyland.c) as
/// RaceCommunication picks it, fought, or destroyed by the destruct code
/// (an instant victory); coming from the start of the game in hyperspace,
/// one at a time, and in systems by SLYLANDRO_MULTIPLIER. The script's
/// every path is TalkTests' recording, the probes' draws and flight
/// HyperEncounterTests' and SystemGroupTests'.
/// </summary>
[Trait("Topic", "world")]
public class SlylandroMeetingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // The system no race's sphere holds (9708:250) where UQM's draw with
    // seed 1 and the multiplier at 4 gives two groups of one probe
    // (tests/golden/system-groups.json, "the probes in a system").
    private const int NoSphereStar = 14;

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

    private static void Frames(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    private static int Probe(GameWorld world) => world.Story!.Rules.RaceIndex("SLYLANDRO_SHIP");

    // A group of probes in hyperspace right by a player, the player in
    // hyperspace, one-shot as a rolled one is (hyper.c).
    private static HyperEncounter ProbesBy(GameWorld world, string id, int ships = 1, short x = 9000, short y = 300)
    {
        world.PutInHyperspace(id);
        var f = world.Find(id)!.Ship.Flight;
        f.PlaceAtUniverse(x, y);
        f.Stop();
        f.StarJustLeft = null;
        var e = new HyperEncounter
        {
            Race = Probe(world), Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
            Flags = world.Encounters!.RolledFlags(world.Story!, Probe(world), f.Fuel),
        };
        Assert.Equal(HyperEncounter.OneShot, e.Flags);
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
        int n = Scripts["slyland"].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts["slyland"].PhraseNumber(phrase));

    // The bounty a probe destroyed pays (RACE_SHIP_COST / 8).
    private static uint Bounty(GameWorld world) => (uint)(world.Story!.Rules.RaceShip("SLYLANDRO_SHIP").Cost >> 3);

    [ContentFact]
    public void AProbeMetInHyperspaceTalksThenFights()
    {
        var world = NewGame();
        ProbesBy(world, "h");
        var m = Meet(world, "h");
        // Hostile (init_slyland_comm): the encounter screen, under the Slylandro's lock.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.IsType<SlylandroScript>(m.Talk!.Script);
        Assert.Equal("h", world.DialogueLocks!.Holder("SLYLANDRO_CONVERSATION"));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "WE_COME_IN_PEACE_1"));
        Assert.Equal(1, world.Story!.Get("SLYLANDRO_PROBE_VISITS"));
        // No destruct code aboard: the five replies.
        Assert.Equal(5, m.Talk.Replies.Count);
        Assert.Throws<Xunit.Sdk.XunitException>(() => Reply(m.Talk, "destruct_code"));
        Assert.True(world.ChooseReply("h", Reply(m.Talk, "threat_1")));
        Assert.True(Said(m.Talk, "PROGRAMMED_TO_DEFEND_1") && Said(m.Talk, "HOSTILE"));
        Assert.Equal((1, 1), (world.Story.Get("SLYLANDRO_PROBE_THREAT"), world.Story.Get("PROBE_EXHIBITED_BUG")));
        // The battle against the probe (stage 4b's slylandr), the lock let go.
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("slylandr", m.Battle!.EnemyShip.Id);
        Assert.False(m.InstantVictory);
        Assert.Null(world.DialogueLocks.Holder("SLYLANDRO_CONVERSATION"));
    }

    [ContentFact]
    public void TheDestructCodeDestroysTheProbesWithoutABattleAndPaysTheAnswerer()
    {
        var world = NewGame("g");
        world.Story!.Set("DESTRUCT_CODE_ON_SHIP", 1);
        var e = ProbesBy(world, "g", ships: 2);
        uint hostUnits = world.Host.Ship.Hold.ResourceUnits, guestUnits = world.Find("g")!.Ship.Hold.ResourceUnits;
        var m = Meet(world, "g");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal(6, m.Talk!.Replies.Count);
        Assert.Equal(0, Reply(m.Talk, "destruct_code"));
        Assert.True(world.ChooseReply("g", 0));
        Assert.True(Said(m.Talk, "DESTRUCT_SEQUENCE"));
        Assert.False(Said(m.Talk, "HOSTILE"));
        Assert.Equal(Segue.Victory, m.Talk.Segue);
        Assert.Equal(0, world.Story.Get("PROBE_EXHIBITED_BUG"));
        // An instant victory (UninitEncounter after Battle's instantVictory):
        // no battle, every probe destroyed, each one's bounty to the
        // answerer alone; the encounter written back with none (status 1:
        // re-forming, one-shot as every probe is from its roll) and gone;
        // the lock let go.
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.True(m.InstantVictory);
        Assert.Null(m.Battle);
        Assert.Null(world.Find("g")!.Ship.Battle);
        Assert.Empty(m.Left!);
        Assert.Equal(2 * Bounty(world), m.Salvage);
        Assert.Equal(550u * 2, m.Salvage);
        Assert.Equal(guestUnits + m.Salvage, world.Find("g")!.Ship.Hold.ResourceUnits);
        Assert.Equal(hostUnits, world.Host.Ship.Hold.ResourceUnits);
        Assert.Empty(e.Ships);
        Assert.Equal(HyperEncounter.Reforming | HyperEncounter.OneShot, e.Flags & (HyperEncounter.Reforming | HyperEncounter.OneShot));
        Assert.Null(world.DialogueLocks!.Holder("SLYLANDRO_CONVERSATION"));
        Frames(world, 2);
        Assert.DoesNotContain(e, world.Encounters!.List);
    }

    [ContentFact]
    public void GuardingTheSaMatraTheDestructCodeWinsNothing()
    {
        // UninitEncounter's VictoryState is 0 while the Ur-Quan protect the
        // Sa-Matra: nothing destroyed, no salvage, though no battle is fought.
        // The probe, one-shot from its roll, then goes all the same
        // (AddEncounterElement: re-forming and one-shot, no element made).
        var world = NewGame();
        world.Story!.Set("DESTRUCT_CODE_ON_SHIP", 1);
        world.Story.Set("URQUAN_PROTECTING_SAMATRA", 1);
        var e = ProbesBy(world, "h");
        uint units = world.Host.Ship.Hold.ResourceUnits;
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", 0));
        Assert.True(m.InstantVictory);
        Assert.Equal((0u, units), (m.Salvage, world.Host.Ship.Hold.ResourceUnits));
        Assert.Equal(new[] { 12 }, m.Left!.Select(l => l.Crew));
        Assert.Equal(new[] { 12 }, e.Ships);
        Assert.Equal(HyperEncounter.Reforming | HyperEncounter.OneShot, e.Flags & (HyperEncounter.Reforming | HyperEncounter.OneShot));
        Frames(world, 2);
        Assert.DoesNotContain(e, world.Encounters!.List);
        Assert.Null(e.Element);
    }

    [ContentFact]
    public void AProbeFoughtAndLeftWithShipsGoes()
    {
        // A battle the player retreats from, the probe left with its ships:
        // re-forming and one-shot (status 1, its roll's flag kept), so
        // AddEncounterElement makes no element and it goes, as every
        // one-shot group does after its meeting.
        var world = NewGame();
        var e = ProbesBy(world, "h");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        for (int i = 0; i < 400 && m.Step == MeetingStep.Battle; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.Step();
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(new[] { 12 }, m.Left!.Select(l => l.Crew));
        Assert.DoesNotContain(e, world.Encounters!.List);
    }

    [ContentFact]
    public void AGroupStartedByTheHostHasARolledGroupsFlags()
    {
        // AddEncounterNear (--start-encounter): the probes one-shot from the
        // start as hyper.c rolls them; the Melnorme one-shot only when they
        // come for an empty tank or the broadcaster; the others not.
        var world = NewGame();
        world.PutInHyperspace("h");
        world.AddEncounterNear("h", Probe(world), 1);
        Assert.Equal(HyperEncounter.OneShot, world.Encounters!.List[^1].Flags);
        world.AddEncounterNear("h", world.Story!.Rules.RaceIndex("URQUAN_SHIP"), 1);
        Assert.Equal(0, world.Encounters.List[^1].Flags);
        int melnorme = world.Story.Rules.RaceIndex("MELNORME_SHIP");
        Assert.NotEqual(0u, world.Host.Ship.Flight.Fuel);
        world.AddEncounterNear("h", melnorme, 1);
        Assert.Equal(0, world.Encounters.List[^1].Flags);
        world.Story.Set("USED_BROADCASTER", 1);
        world.AddEncounterNear("h", melnorme, 1);
        Assert.Equal(HyperEncounter.OneShot, world.Encounters.List[^1].Flags);
        world.Story.Set("MELNORME_ANGER", 3);
        world.AddEncounterNear("h", melnorme, 1);
        Assert.Equal(0, world.Encounters.List[^1].Flags);
    }

    // The host in the system no sphere holds, its two probes drawn (UQM's, seed 1).
    private static (GameWorld World, SystemDraw Draw) InProbesSystem(params string[] guests)
    {
        var world = NewGame(guests);
        world.Story!.Set("SLYLANDRO_MULTIPLIER", 4);
        world.SystemSeeds = _ => 1;
        world.PutInSystem("h", NoSphereStar);
        Frames(world, 1);
        var d = world.SystemGroups!.Systems[NoSphereStar];
        Assert.Equal(2, d.Groups.Count);
        Assert.All(d.Groups, g => Assert.Equal(Probe(world), g.Race));
        return (world, d);
    }

    private static Meeting MeetInSystem(GameWorld world, SystemDraw d, IpGroup target)
    {
        world.Host.Ship.Ip = SystemGroupTests.FlightAt(SystemGroupTests.Systems.Value[NoSphereStar], target.LocX, target.LocY);
        Frames(world, 2);
        var m = world.MeetingOf("h")!;
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.Same(target, m.Group);
        return m;
    }

    [ContentFact]
    public void ProbesInASystemComeByTheMultiplierAndFly()
    {
        var (world, d) = InProbesSystem();
        // The game's own races keep them (decision 2: their talk ported).
        Assert.Contains("SLYLANDRO_SHIP", world.EncounterRaces);
        Frames(world, 60);
        Assert.All(d.Groups, g => Assert.NotNull(g.Element));
        // The multiplier at 0 (before the first ramp-up has run, or the ramp-down's end): none drawn there.
        var none = NewGame();
        none.SystemSeeds = _ => 1;
        none.PutInSystem("h", NoSphereStar);
        Frames(none, 1);
        Assert.Empty(none.SystemGroups!.Systems[NoSphereStar].Groups);
    }

    [ContentFact]
    public void TheDestructCodeInASystemDestroysTheProbeAndTurnsTheOthersOnThePlayers()
    {
        var (world, d) = InProbesSystem();
        world.Story!.Set("DESTRUCT_CODE_ON_SHIP", 1);
        var target = d.Groups.Single(g => g.SysLoc == 0);   // the other in a planet's view
        var other = d.Groups.Single(g => g != target);
        var m = MeetInSystem(world, d, target);
        var random = d.Random;
        uint units = world.Host.Ship.Hold.ResourceUnits;
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", Reply(m.Talk!, "destruct_code")));
        // As after a battle won (comm.c's status 1; its clearing of the
        // Slylandro's group for status 0 never reached): the group gone, the
        // numbers seeded afresh (Battle's), the race turned on the players.
        Assert.True(m.InstantVictory);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(units + Bounty(world), world.Host.Ship.Hold.ResourceUnits);
        Assert.DoesNotContain(target, d.Groups);
        Assert.Empty(world.SystemGroups!.ShipsOf(d, target)!.Ships);   // dead in the draw
        Assert.NotSame(random, d.Random);
        Assert.Equal(Probe(world), d.EncounterRace);
        Frames(world, 2);
        Assert.Equal(-1, d.EncounterRace);
        Assert.Equal(IpGroup.InterceptPlayer, other.DestLoc);
        Assert.Equal(0, other.Task & IpGroup.IgnoreFlagship);
    }

    [ContentFact]
    public void TheProbeSaysWhereTheAnswererIsOnTheStarMap()
    {
        // In a system, the star's place (ExploreSolarSys sets log_x and log_y
        // so): 9708:250, so in the probe's coordinates +956.2, +937.5.
        var (world, d) = InProbesSystem();
        world.Story!.Set("SLYLANDRO_PROBE_ID", 3);
        // Its hyperspace flight left elsewhere: the star's place counts, not where it was.
        world.Host.Ship.Flight.PlaceAtUniverse(100, 100);
        var m = MeetInSystem(world, d, d.Groups.Single(g => g.SysLoc == 0));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        int from = m.Talk!.Events.Count;
        Assert.True(world.ChooseReply("h", Reply(m.Talk, "we_are_us_4")));
        var spliced = m.Talk.Events.Skip(from).OfType<MultiTrackQueued>().Select(t => Encoding.ASCII.GetString(t.TextBytes));
        Assert.Equal(new[] { "+", "956", ".", "2", ",", "+", "937", ".", "5" }, spliced);
        // "nine hundred fifty six": its three words' clips, the text one piece.
        Assert.Equal(4, m.Talk.Events.Skip(from).OfType<MultiTrackQueued>().ElementAt(1).Clips);
        Assert.Equal(3, world.Story.Get("SLYLANDRO_PROBE_ID"));

        // In hyperspace, the answerer's own place (a guest's).
        var hyper = NewGame("g");
        hyper.Story!.Set("SLYLANDRO_PROBE_ID", 3);
        ProbesBy(hyper, "g", x: 333 + 1234, y: 9812 - 3156);
        var hm = Meet(hyper, "g");
        var at = hyper.Find("g")!.Ship.Flight;
        Assert.True(hyper.EncounterChoice("g", EncounterAction.Hail));
        from = hm.Talk!.Events.Count;
        Assert.True(hyper.ChooseReply("g", Reply(hm.Talk, "we_are_us_4")));
        int dy = 9812 - Uqm.Units.LogYToUniverse(at.LogY), dx = Uqm.Units.LogXToUniverse(at.LogX) - 333;
        var want = new[] { "+", $"{dy / 10:000}", ".", $"{dy % 10}", ",", "+", $"{dx / 10:000}", ".", $"{dx % 10}" };
        Assert.Equal(want, hm.Talk.Events.Skip(from).OfType<MultiTrackQueued>().Select(t => Encoding.ASCII.GetString(t.TextBytes)));
        // A visit's talks' ship is its answerer's, the place too.
        var guest = hyper.Find("g")!;
        var answerer = new AnswererTalkShip(() => guest, hyper.TalkLibrary!.Strings, hyper);
        Assert.Equal((at.LogX, at.LogY), answerer.LogPlace);
        Assert.Equal((at.LogX, at.LogY), new PlayerTalkShip(guest, hyper.TalkLibrary.Strings, hyper).LogPlace);
    }

    [ContentFact]
    public void ProbesComeFromTheStartInHyperspaceOneAtATimeForEachPlayer()
    {
        // A new game as it starts: the starbase not yet available, so a probe
        // every day for a player in hyperspace (hyper.c: 100%), but only one
        // of their roll's at a time; kept (their talk ported) and flying.
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(1, world.Story!.Get("SLYLANDRO_MULTIPLIER"));   // the opening's ramp-up
        Assert.Equal(0, world.Story.Get("STARBASE_AVAILABLE"));
        Assert.Equal(JoinResult.Joined, world.Join("g", "g"));
        world.UndockShown("g");
        world.PutInHyperspace("h");
        world.PutInHyperspace("g");
        // Far from any sphere, and from each other.
        world.Host.Ship.Flight.PlaceAtUniverse(9000, 300);
        world.Find("g")!.Ship.Flight.PlaceAtUniverse(9000, 9500);
        foreach (var p in world.Players)
        {
            p.Ship.Flight.Stop();
            p.Ship.Flight.StarJustLeft = null;
        }
        world.EndOfFrame();
        for (int day = 0; day < 3; ++day)
        {
            world.MoveClockDays(1);
            var probes = world.Encounters!.List.Where(e => e.Race == Probe(world)).ToList();
            Assert.Equal(new[] { "g", "h" }, probes.Select(e => e.Roller).Order());
        }
        Frames(world, 5);
        Assert.Equal(2, world.Encounters!.List.Count(e => e.Race == Probe(world) && e.Element is not null));
        // The starbase available and the multiplier at 0: no more come.
        world.Encounters.List.Clear();
        world.Story.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        for (int day = 0; day < 5; ++day)
            world.MoveClockDays(1);
        Assert.DoesNotContain(world.Encounters.List, e => e.Race == Probe(world));
    }

    [ContentFact]
    public void TheHostsLogShowsTheProbesCoordinatesAndAnInstantVictory()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        host.World.BattleKit = WorldBattleTests.Kit();
        host.World.Story!.Set("STARBASE_AVAILABLE", 1);
        host.World.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        host.World.Story.Set("DESTRUCT_CODE_ON_SHIP", 1);
        host.World.Story.Set("SLYLANDRO_PROBE_ID", 3);
        var log = new List<string>();
        host.Logged += log.Add;
        host.World.PutInHyperspace("host");
        host.StartAt(9000, 300);
        host.StartEncounter("slylandr:2");
        host.EncounterChoice = EncounterAction.Hail;
        host.MeetingReplies.Enqueue(3);   // we_are_us_4: the coordinates (the destruct code first)
        for (int i = 0; i < 400 && host.World.Host.Ship.Battle is null; ++i)
            host.Step(ShipInput.None);
        Assert.Contains(log, l => l.StartsWith("  Slylandro: ", StringComparison.Ordinal));
        Assert.Contains("  Slylandro (on): +", log);
        Assert.Contains("host fights the Slylandro", log);

        var log2 = new List<string>();
        var again = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        again.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        again.World.DialogueLocks = DialogueLocks.Load(DataDir);
        again.World.BattleKit = WorldBattleTests.Kit();
        again.World.Story!.Set("STARBASE_AVAILABLE", 1);
        again.World.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        again.World.Story.Set("DESTRUCT_CODE_ON_SHIP", 1);
        again.Logged += log2.Add;
        again.World.PutInHyperspace("host");
        again.StartAt(9000, 300);
        again.StartEncounter("slylandr:2");
        again.EncounterChoice = EncounterAction.Hail;
        again.MeetingReplies.Enqueue(0);   // destruct_code
        for (int i = 0; i < 400 && !log2.Any(l => l.StartsWith("host's meeting", StringComparison.Ordinal)); ++i)
            again.Step(ShipInput.None);
        Assert.Contains("  The talk with the Slylandro ends: victory", log2);
        Assert.Contains($"host's meeting with the Slylandro is over: an instant victory, the Slylandro ships destroyed, {2 * Bounty(again.World)} RU of salvage", log2);
        Assert.DoesNotContain("host fights the Slylandro", log2);

        // While the Ur-Quan guard the Sa-Matra the code wins nothing: the log says nothing was destroyed.
        var log3 = new List<string>();
        var guarded = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        guarded.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        guarded.World.DialogueLocks = DialogueLocks.Load(DataDir);
        guarded.World.BattleKit = WorldBattleTests.Kit();
        guarded.World.Story!.Set("STARBASE_AVAILABLE", 1);
        guarded.World.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        guarded.World.Story.Set("DESTRUCT_CODE_ON_SHIP", 1);
        guarded.World.Story.Set("URQUAN_PROTECTING_SAMATRA", 1);
        guarded.Logged += log3.Add;
        guarded.World.PutInHyperspace("host");
        guarded.StartAt(9000, 300);
        guarded.StartEncounter("slylandr:2");
        guarded.EncounterChoice = EncounterAction.Hail;
        guarded.MeetingReplies.Enqueue(0);   // destruct_code
        for (int i = 0; i < 400 && !log3.Any(l => l.StartsWith("host's meeting", StringComparison.Ordinal)); ++i)
            guarded.Step(ShipInput.None);
        Assert.Contains("host's meeting with the Slylandro is over: an instant victory that wins nothing (the Ur-Quan guard the Sa-Matra), no ships destroyed", log3);
        Assert.DoesNotContain(log3, l => l.Contains("ships destroyed,", StringComparison.Ordinal));
        Assert.Equal(new[] { 12, 12 }, guarded.World.Encounters!.List.Single(e => e.Race == Probe(guarded.World)).Ships);
    }
}
