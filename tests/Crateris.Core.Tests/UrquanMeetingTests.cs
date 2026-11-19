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
/// The Ur-Quan, their drone and the Kohr-Ah met in space
/// (docs/plans/stage-3b.md, item 6): their talks made from the content as
/// RaceCommunication picks them (<see cref="GameWorld.RaceTalk"/>), where
/// the talk is as the scripts read it, the drone's message and its flight
/// off, the colony's guard, the battles, the surrender, and the host's log
/// of a meeting. The scripts' every path is TalkTests' recording.
/// </summary>
[Trait("Topic", "world")]
public class UrquanMeetingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));
    private const int SolStar = 82, Earth = 2;

    // A new game with the talks' content, the locks and the battles; the
    // races that fly are the game's own (the Ur-Quan, the drone, the
    // Kohr-Ah); every talk made is kept.
    private sealed class Game
    {
        public required GameWorld World;
        public readonly List<Talk> Talks = new();
    }

    private static Game NewGame(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        var game = new Game { World = world };
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
        world.MeetingTalks = (p, race, seed) =>
        {
            var talk = world.RaceTalk(p, race, seed);
            if (talk is not null)
                game.Talks.Add(talk);
            return talk;
        };
        return game;
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

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    // A group of a race in hyperspace right by a player.
    private static HyperEncounter GroupBy(GameWorld world, string id, string race, int ships = 2)
    {
        var f = world.Find(id)!.Ship.Flight;
        var e = new HyperEncounter
        {
            Race = Race(world, race), Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story!.Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    // The players in hyperspace, the host at (5800, 6100) and each guest 600 units east.
    private static void InHyperspace(GameWorld world)
    {
        short x = 5800;
        foreach (var p in world.Players)
        {
            world.PutInHyperspace(p.Id);
            p.Ship.Flight.PlaceAtUniverse(x, 6100);
            p.Ship.Flight.Stop();
            p.Ship.Flight.StarJustLeft = null;
            x += 600;
        }
    }

    // Steps until the player's meeting is past its spin (or 300 frames).
    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 300 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    // The reply offered with this phrase id.
    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    [ContentFact]
    public void TheDroneAtEarthGivesItsMessageThenFliesOff()
    {
        var game = NewGame();
        var world = game.World;
        world.PutInOrbit("h", SolStar, Earth);
        world.Host.Ship.Ip!.ReturnFromOrbit();   // in Earth's inner system, the drone orbiting there
        for (int i = 0; i < 600 && world.SystemMeetings.Count == 0; ++i)
            Frames(world, 1);
        var met = Assert.Single(world.SystemMeetings);
        Assert.Equal(Race(world, "URQUAN_DRONE_SHIP"), met.Race);
        // The drone's URQUAN_DRONE_CONVERSATION runs the Ur-Quan's script, as
        // Sol's own group in a system (BattleGroupRef): not hostile, no
        // encounter screen; its message, and the talk over at once.
        var talk = Assert.Single(game.Talks);
        Assert.IsType<UrquanScript>(talk.Script);
        Assert.Equal((TalkActivity.InInterplanetary, "URQUAN_PROBE_GRPOFFS0"), (talk.Activity, talk.BattleGroup));
        Assert.True(Said(talk, "SEND_MESSAGE"));
        Assert.True(talk.Ended);
        Assert.Equal(Segue.Peace, talk.Segue);
        Assert.Equal(1, world.Story!.Get("PROBE_MESSAGE_DELIVERED"));
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        // The drone's race has no sphere to track (InitCommunication skips it), nor the Ur-Quan's from it.
        Assert.Equal(0, world.Story.Fleet("URQUAN_SHIP").KnownStrength);
        // check_probe: the drone flies off, the flagship ignored.
        var drone = world.SystemGroups!.Systems[SolStar].Groups.Single(g => g.Race == met.Race);
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship, (byte)0), (drone.Task, drone.DestLoc));
        // It flies out of Earth's inner system and out of Sol (kept, out of
        // the system, until the last player leaves: FlushGroupInfo), never
        // coming at the host again.
        for (int i = 0; i < 3000 && drone.InSystem; ++i)
            Frames(world, 1);
        Assert.False(drone.InSystem);
        Assert.Single(world.SystemMeetings);
        world.PutInHyperspace("h");
        Frames(world, 1);
        Assert.Empty(world.SystemGroups.Defined["SOL_DEFINED"].Single().Ships);   // gone for good
    }

    [ContentFact]
    public void TheUrQuanInHyperspaceAreHostileTheirTalkRunsAndEndsInBattle()
    {
        var game = NewGame();
        var world = game.World;
        InHyperspace(world);
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        var m = Meet(world, "h");
        var talk = m.Talk!;
        Assert.Same(talk, Assert.Single(game.Talks));
        Assert.IsType<UrquanScript>(talk.Script);
        Assert.Equal((TalkActivity.InHyperspace, (string?)null), (talk.Activity, talk.BattleGroup));
        // Hostile: the encounter screen, under the Ur-Quan's lock.
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.Equal("h", world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(talk, "INIT_HELLO"));
        Assert.Equal(1, world.Story!.Get("URQUAN_VISITS"));
        // The answerer's ship: their names spliced into the talk's lines are theirs.
        Assert.Equal(world.Host.Ship.Hold.Crew, talk.Ship.Crew);
        Assert.True(world.ChooseReply("h", Reply(talk, "you_must_surrender"), m.Round));
        Assert.True(Said(talk, "NOPE"));
        Assert.Equal(Segue.Hostile, talk.Segue);
        // The battle against the group's dreadnoughts, the lock let go.
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(new[] { "urquan", "urquan" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
        Assert.Null(world.DialogueLocks.Holder("URQUAN_CONVERSATION"));
        Assert.Equal("h", group.MeetingPlayer);
    }

    [ContentFact]
    public void TheKohrAhInASystemAreHostileAndAnAttackFightsTheirMarauders()
    {
        var game = NewGame();
        var world = game.World;
        // A system with no special groups: BattleGroupRef 0.
        const int UrQuanStar = 311;
        world.PutInSystem("h", UrQuanStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[UrQuanStar].Groups.Clear();
        world.AddEncounterNear("h", Race(world, "BLACK_URQUAN_SHIP"), 3);
        var m = Meet(world, "h");
        var talk = m.Talk!;
        Assert.IsType<BlackUrquanScript>(talk.Script);
        Assert.Equal((TalkActivity.InInterplanetary, (string?)null), (talk.Activity, talk.BattleGroup));
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.DoesNotContain(talk.Events, e => e is LineQueued);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(new[] { "blackurq", "blackurq", "blackurq" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
    }

    [ContentFact]
    public void TheKohrAhsHailOffersTheirRepliesAndAGoodbyeFights()
    {
        var game = NewGame();
        var world = game.World;
        InHyperspace(world);
        GroupBy(world, "h", "BLACK_URQUAN_SHIP", ships: 1);
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        var talk = m.Talk!;
        Assert.True(Said(talk, "HELLO_AND_DIE_1"));
        Assert.True(world.ChooseReply("h", Reply(talk, "bye"), m.Round));
        Assert.True(Said(talk, "GOODBYE_AND_DIE"));
        Assert.Equal(1, world.Story!.Get("KOHR_AH_BYES"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("blackurq", m.Battle!.EnemyFleet.Ships.Single().Ship);
    }

    [ContentFact]
    public void TheColonysGuardCatchesTheShipAndFights()
    {
        var game = NewGame();
        var world = game.World;
        int colony = WorldBattleTests.Systems.Value.Map.Stars.Single(s => s.Special == "START_COLONY_DEFINED").Number;
        world.PutInOrbit("h", colony, 0);
        world.Host.Ship.Ip!.ReturnFromOrbit();   // by the colony's world, which the guard orbits
        var m = Meet(world, "h");
        Assert.Equal(Race(world, "URQUAN_SHIP"), m.Race);
        var talk = m.Talk!;
        Assert.Equal((TalkActivity.InInterplanetary, "COLONY_GRPOFFS0"), (talk.Activity, talk.BattleGroup));
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(talk, "CAUGHT_YA"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(0, world.Story!.Get("URQUAN_VISITS"));   // not a visit of theirs
    }

    // The Ur-Quan's surrender: i_surrender, then i_will_surrender.
    private static Meeting Surrender(GameWorld world, string id)
    {
        var m = Meet(world, id);
        Assert.True(world.EncounterChoice(id, EncounterAction.Hail));
        var talk = m.Talk!;
        Assert.True(world.ChooseReply(id, Reply(talk, "i_surrender"), m.Round));
        Assert.True(world.ChooseReply(id, Reply(talk, "i_will_surrender"), m.Round));
        Assert.True(Said(talk, "GOOD_CHOICE"));
        Assert.Equal(Segue.Defeat, talk.Segue);
        // The talk shows the crew as UQM's (COUNT)~0.
        Assert.Equal(Talk.DefeatedCrew, talk.Events.OfType<ShipChanged>().Last().Crew);
        return m;
    }

    [ContentFact]
    public void TheFlagshipSurrenderingIsTheGameOver()
    {
        var game = NewGame("g");
        var world = game.World;
        InHyperspace(world);
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        var m = Surrender(world, "h");
        Assert.True(world.GameOver);
        Assert.True(world.Surrendered);
        Assert.True(world.FlagshipLost);
        Assert.Equal(0, world.Host.Ship.Hold.Crew);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(m.Battle);
        Assert.Equal(HyperEncounter.Reforming | HyperEncounter.OneShot, group.Flags & (HyperEncounter.Reforming | HyperEncounter.OneShot));
        long frame = world.Frame;
        Frames(world, 5);
        Assert.Equal(frame, world.Frame);   // the world has stopped
    }

    [ContentFact]
    public void AGuestSurrenderingIsAWreckAndTheGameGoesOn()
    {
        var game = NewGame("g");
        var world = game.World;
        InHyperspace(world);
        GroupBy(world, "g", "URQUAN_SHIP");
        var m = Surrender(world, "g");
        var guest = world.Find("g")!.Ship;
        Assert.False(world.GameOver);
        Assert.True(guest.Wreck);
        Assert.Equal(0, guest.Hold.Crew);
        // The talk was the answerer's ship's (PlayerTalkShip): the guest's, not the host's.
        Assert.Equal(0, m.Talk!.Ship.Crew);
        Assert.NotEqual(0, world.Host.Ship.Hold.Crew);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        long frame = world.Frame;
        Frames(world, 5);
        Assert.Equal(frame + 5, world.Frame);
        Assert.True(guest.Wreck);
        Assert.Equal(0, guest.AppearFrames);   // not spinning back into hyperspace: it doesn't fly
    }

    [Fact]
    public void TheSpecialSystemsOwnGroupsOffsetsAreTheirGenerators()
    {
        // Each generator's GLOBAL (BattleGroupRef) = GET_GAME_STATE_32 (field)
        // (gensol.c, gencol.c, gensam.c, genshof.c, genzfpscout.c, genmel.c).
        Assert.Equal("URQUAN_PROBE_GRPOFFS0", SystemGroups.OffsetField("SOL_DEFINED"));
        Assert.Equal("COLONY_GRPOFFS0", SystemGroups.OffsetField("START_COLONY_DEFINED"));
        Assert.Equal("SAMATRA_GRPOFFS0", SystemGroups.OffsetField("SAMATRA_DEFINED"));
        Assert.Equal("SHOFIXTI_GRPOFFS0", SystemGroups.OffsetField("SHOFIXTI_DEFINED"));
        Assert.Equal("ZOQFOT_GRPOFFS0", SystemGroups.OffsetField("ZOQ_SCOUT_DEFINED"));
        for (int i = 0; i < 9; ++i)
            Assert.Equal($"MELNORME{i}_GRPOFFS0", SystemGroups.OffsetField($"MELNORME{i}_DEFINED"));
        // A system of random groups, or one whose generator keeps none of its own (BattleGroupRef 0).
        Assert.Null(SystemGroups.OffsetField(null));
        Assert.Null(SystemGroups.OffsetField("ZOQFOT_DEFINED"));
        Assert.Null(SystemGroups.OffsetField("BOMB_DEFINED"));
        var fields = StoryRules.Load(DataDir).Table;
        Assert.All(new[] { "SOL_DEFINED", "START_COLONY_DEFINED", "SAMATRA_DEFINED", "SHOFIXTI_DEFINED", "ZOQ_SCOUT_DEFINED", "MELNORME8_DEFINED" },
            special => Assert.Equal(8, fields[SystemGroups.OffsetField(special)!].Bits));
    }

    [ContentFact]
    public void OnlyTheTalkingRacesTalksAreMadeAndNoneIsStory()
    {
        var game = NewGame();
        var world = game.World;
        var rules = world.Story!.Rules;
        foreach (string race in GameWorld.TalkingRaces)
        {
            var talk = world.RaceTalk(world.Host, Race(world, race), 1);
            Assert.NotNull(talk);
            Assert.False(TalkRules.IsStory(talk!.Script.Phrases.Name, world.Story));
            Assert.Contains(talk.Script.Phrases.Name, TalkLibrary.Ported);
        }
        Assert.IsType<UrquanScript>(world.RaceTalk(world.Host, Race(world, "URQUAN_DRONE_SHIP"), 1)!.Script);
        // The Ilwrath's script is only its Earth branch until item 7: their groups met talk as no race's yet.
        Assert.Null(world.RaceTalk(world.Host, Race(world, "ILWRATH_SHIP"), 1));
        Assert.Null(world.RaceTalk(world.Host, Race(world, "SPATHI_SHIP"), 1));
        // Without the content, none.
        world.TalkLibrary = null;
        Assert.Null(world.RaceTalk(world.Host, Race(world, "URQUAN_SHIP"), 1));
        Assert.All(rules.Races.Where(r => !GameWorld.TalkingRaces.Contains(r)),
            r => Assert.DoesNotContain(r, world.EncounterRaces));
    }

    [ContentFact]
    public void TheHostsLogShowsTheMeetingAndItsRepliesAreChosenByTheTesting()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        host.World.BattleKit = WorldBattleTests.Kit();
        var log = new List<string>();
        host.Logged += log.Add;
        host.World.PutInHyperspace("host");
        host.StartAt(5800, 6100);
        host.StartEncounter("urquan:1");
        host.EncounterChoice = EncounterAction.Hail;
        host.MeetingReplies.Enqueue(0);   // you_must_surrender
        for (int i = 0; i < 400 && host.World.Host.Ship.Battle is null; ++i)
            host.Step(ShipInput.None);
        Assert.NotNull(host.World.Host.Ship.Battle);
        Assert.Empty(host.MeetingReplies);
        var talk = log.SkipWhile(l => !l.StartsWith("host talks to the Ur-Quan", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(talk);
        Assert.Contains(log, l => l.StartsWith("host at the encounter screen: 1 Ur-Quan ship", StringComparison.Ordinal));
        Assert.Contains(talk, l => l.StartsWith("  Ur-Quan: ", StringComparison.Ordinal));
        Assert.Contains(talk, l => l.StartsWith("  host's reply 0: ", StringComparison.Ordinal));
        Assert.Contains("  host chose reply 0", talk);
        Assert.Contains("  The talk with the Ur-Quan ends: hostile", talk);
        Assert.Single(log, l => l == "host talks to the Ur-Quan");
        Assert.Contains("host fights the Ur-Quan", talk);
        Assert.True(log.IndexOf("host fights the Ur-Quan") > log.IndexOf("  The talk with the Ur-Quan ends: hostile"));
    }

    [ContentFact]
    public void TheHostsLogShowsAMeetingBegunAndOverInOneFrame()
    {
        // The drone at Earth: its message, the meeting over at once.
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        var log = new List<string>();
        host.Logged += log.Add;
        host.World.PutInOrbit("host", SolStar, Earth);
        host.World.Host.Ship.Ip!.ReturnFromOrbit();
        for (int i = 0; i < 600 && host.World.SystemMeetings.Count == 0; ++i)
            host.Step(ShipInput.None);
        var meeting = log.SkipWhile(l => l != "host talks to the Ur-Quan").ToList();
        Assert.Equal(4, meeting.Count);
        Assert.StartsWith("  Ur-Quan: ", meeting[1]);
        Assert.Equal(new[] { "  The talk with the Ur-Quan ends: peace", "host's meeting with the Ur-Quan is over" }, meeting.Skip(2));
        host.Step(ShipInput.None);
        Assert.Equal(meeting.Count, log.SkipWhile(l => l != "host talks to the Ur-Quan").Count());
    }
}
