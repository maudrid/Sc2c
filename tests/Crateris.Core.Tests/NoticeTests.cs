// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Notices of what other players set off (docs/plans/stage-3b.md, item 9,
/// decision 11; our own rule, decision 10): each event told once to the
/// players near it (in the same system, or near in hyperspace), never to
/// the one who set it off, so one player alone is told nothing new.
/// </summary>
[Trait("Topic", "world")]
public class NoticeTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private const int UrQuanStar = 311, SolStar = 82;

    private static void Run(GameWorld world, int frames = 1)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    private static List<(string, string)> Told(GameWorld world) =>
        world.TakeNotices().Select(n => (n.PlayerId, n.Text)).ToList();

    private static void Prepare(GameWorld world)
    {
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.Story.Set("STARBASE_AVAILABLE", 1);   // a retreat allowed, no probes daily
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        world.BattleKit = WorldBattleTests.Kit();
        world.DialogueLocks = DialogueLocks.Load(DataDir);
    }

    // ---- in a system

    // The players in the Ur-Quan's system, its groups taken away (the test adds its own).
    private static (GameWorld World, SystemDraw Draw) InUrQuanSystem(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        world.SystemSeeds = star => 1000u + (uint)star;
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        Prepare(world);
        foreach (var p in world.Players)
            world.PutInSystem(p.Id, UrQuanStar);
        Run(world);
        var sg = world.SystemGroups!;
        var d = sg.Systems[UrQuanStar];
        (d.Defined is { } special ? sg.Defined[special] : d.RandomGroups ??= new()).Clear();
        d.Groups.Clear();
        d.Elements = null;
        // Each player far from the others, at rest.
        int x = 6000;
        foreach (var p in world.Players)
        {
            Place(world, p.Id, x, 0);
            x -= 6000;
        }
        world.TakeNotices();
        return (world, d);
    }

    private static void Place(GameWorld world, string id, int x, int y) =>
        world.Find(id)!.Ship.Ip = SystemGroupTests.FlightAt(SystemGroupTests.Systems.Value[UrQuanStar], x, y);

    // A group of the Ur-Quan's at a place (standing down unless told otherwise, so it spots nobody).
    private static IpGroup AddGroup(GameWorld world, SystemDraw d, int x, int y, int ships = 1,
        byte task = IpGroup.OnStation | IpGroup.IgnoreFlagship, byte dest = 1, string race = "URQUAN_SHIP")
    {
        var sg = world.SystemGroups!;
        var list = d.Defined is { } special ? sg.Defined[special] : d.RandomGroups!;
        int id = list.Count == 0 ? 1 : list.Max(g => g.Id) + 1;
        int r = world.Story!.Rules.RaceIndex(race);
        list.Add(new GroupShips(id, r, Enumerable.Repeat(world.Story.Rules.RaceShips[r].Crew, ships)));
        var g = new IpGroup { GroupId = id, Race = r, SysLoc = 0, Task = task, DestLoc = dest, LocX = (short)x, LocY = (short)y };
        d.Groups.Add(g);
        d.Elements = null;
        return g;
    }

    // Steps until the player's meeting has begun (or 300 frames).
    private static Meeting MeetIn(GameWorld world, string id)
    {
        for (int i = 0; i < 300 && world.MeetingOf(id) is null; ++i)
            Run(world);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    // The meeting's battle won: its one enemy ship destroyed once it's in.
    private static void WinTheBattle(GameWorld world, Meeting m)
    {
        var wb = m.Battle!;
        for (int i = 0; i < 400 && wb.Enemy.RaceDesc is null; ++i)
            Run(world);
        Run(world, 30);   // in, and fighting
        WorldBattleTests.Kill(wb.Battle, wb.Enemy);
        for (int i = 0; i < 600 && m.Step == MeetingStep.Battle; ++i)
            Run(world);
        Assert.True(wb.Outcome == BattleOutcome.Won && m.Left!.Count == 0,
            $"{wb.Outcome} {m.Left?.Count} {string.Join(",", wb.Enemies.Select(e => e.Starship.CrewLevel))} {m.Step} {wb.Battle.InBattle}");
    }

    [ContentFact]
    public void TheOthersInTheSystemAreToldTheRaceHoldsOffAsAPlayerMeetsItAndThenThePeace()
    {
        var (world, d) = InUrQuanSystem("g", "k");
        var met = AddGroup(world, d, 6000, 0, ships: 2);
        AddGroup(world, d, 0, 9000);
        var m = MeetIn(world, "h");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        // The guests, wherever they are in the system, told once; the host nothing.
        Assert.Equal(new[] { ("g", "The Ur-Quan hold off: Host is meeting them."), ("k", "The Ur-Quan hold off: Host is meeting them.") },
            Told(world));
        Run(world, 20);
        Assert.Empty(Told(world));
        // The talk ends in peace (the stand-in hail: no talk of the race's here).
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Contains(met, d.Groups);
        Assert.Equal(new[] { ("g", "Host made peace with the Ur-Quan."), ("k", "Host made peace with the Ur-Quan.") }, Told(world));
        Run(world, 20);
        Assert.Empty(Told(world));
    }

    [ContentFact]
    public void APlayersBattleInTheSystemIsToldItsGroupChasingAnotherGoneAndTheRaceTurningOnThoseNotInIt()
    {
        var (world, d) = InUrQuanSystem("g", "k");
        var met = AddGroup(world, d, 6000, 0);
        AddGroup(world, d, 0, 9000);
        met.Chased.Add("g");   // it had flown at the guest
        var m = MeetIn(world, "h");
        Told(world);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(new[] { ("g", "Host is fighting the Ur-Quan."), ("k", "Host is fighting the Ur-Quan.") }, Told(world));
        WinTheBattle(world, m);
        Assert.DoesNotContain(met, d.Groups);
        // The group gone: told to the one it had chased; the battle's end to nobody else.
        Assert.Equal(new[] { ("g", "The Ur-Quan chasing you were destroyed in the battle Host started.") }, Told(world));
        // As the host comes back into flight the race turns on the players
        // (DoMissions): told to those who weren't in the battle.
        for (int i = 0; i < 10 && d.EncounterRace >= 0; ++i)
            Run(world);
        Assert.Equal(-1, d.EncounterRace);
        Assert.Equal(new[] { ("g", "The Ur-Quan's ships turn on you."), ("k", "The Ur-Quan's ships turn on you.") }, Told(world));
    }

    [ContentFact]
    public void AGuestInAPlayersBattleIsntToldTheRaceTurnsOnThemNorTheGroupItChasedGone()
    {
        var (world, d) = InUrQuanSystem("g");
        var met = AddGroup(world, d, 6000, 0);
        AddGroup(world, d, 0, 9000);
        met.Chased.Add("g");
        var m = MeetIn(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Place(world, "g", 6000, 0);
        world.Find("g")!.Ship.Ip!.EscapeCounter = 0;
        Assert.True(world.LetIntoBattle("g"));
        for (int i = 0; i < 10 && world.Find("g")!.Ship.Battle is null; ++i)
            Run(world);
        Assert.Same(m.Battle, world.Find("g")!.Ship.Battle);
        Assert.True(m.Step == MeetingStep.Battle, $"{m.Step} {m.Battle!.Outcome}");
        Told(world);
        WinTheBattle(world, m);
        for (int i = 0; i < 20 && d.EncounterRace >= 0; ++i)
            Run(world);
        Assert.Equal(-1, d.EncounterRace);
        Assert.Empty(Told(world));
    }

    [ContentFact]
    public void AGroupSpottingAPlayerTurnsTheRaceOnTheOthersWhoAreToldWhoWasSpotted()
    {
        var (world, d) = InUrQuanSystem("g");
        var spotter = AddGroup(world, d, 6800, 0, task: IpGroup.OnStation);
        for (int i = 0; i < 3 && spotter.DestLoc != IpGroup.InterceptPlayer; ++i)
            Run(world);
        Assert.Equal(IpGroup.InterceptPlayer, spotter.DestLoc);
        Assert.Equal(new[] { ("g", "The Ur-Quan turn on you: they spotted Host.") }, Told(world));
        Run(world, 5);
        Assert.Empty(Told(world).Where(t => t.Item2.Contains("spotted")));
        Assert.Contains("h", spotter.Chased);   // flying at the host now
        Assert.DoesNotContain("g", spotter.Chased);
    }

    [ContentFact]
    public void ATurningUndoneInTheSameFrameByAMeetingIsntTold()
    {
        // The host put on a group whose race spots them as they touch it:
        // the race turns and stands down again in one frame; only the
        // holding off is told.
        var (world, d) = InUrQuanSystem("g");
        AddGroup(world, d, 6000, 0, task: IpGroup.OnStation);
        MeetIn(world, "h");
        Assert.Equal(new[] { ("g", "The Ur-Quan hold off: Host is meeting them.") }, Told(world));
    }

    [ContentFact]
    public void APlayerOutOfTheSystemIsToldWhosMeetingWhomAPlayerAwayIsnt()
    {
        var (world, d) = InUrQuanSystem("g", "k");
        AddGroup(world, d, 6000, 0);
        world.Leave("k");   // away (disconnected): not told
        world.PutInHyperspace("g");
        MeetIn(world, "h");
        Assert.Equal(new[] { ("g", "Host is meeting the Ur-Quan.") }, Told(world));
    }

    [ContentFact]
    public void APlayerOutOfTheSystemIsToldTheRaceTurnsOnThePlayersThere()
    {
        // The owner, 2026-11-21: everyone is told; one out of the system in
        // words for anyone, not "on you". Spotted, then after a battle.
        var (world, d) = InUrQuanSystem("g", "k");
        world.PutInHyperspace("k");
        Run(world);
        Told(world);
        var spotter = AddGroup(world, d, 6800, 0, task: IpGroup.OnStation);
        AddGroup(world, d, 0, 9000);
        for (int i = 0; i < 3 && spotter.DestLoc != IpGroup.InterceptPlayer; ++i)
            Run(world);
        Assert.Equal(new[] { ("g", "The Ur-Quan turn on you: they spotted Host."),
            ("k", "The Ur-Quan spotted Host and turn on the players there.") }, Told(world));
        var m = MeetIn(world, "h");
        Assert.Same(spotter, m.Group);
        Told(world);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WinTheBattle(world, m);
        Told(world);
        for (int i = 0; i < 10 && d.EncounterRace >= 0; ++i)
            Run(world);
        Assert.Equal(-1, d.EncounterRace);
        Assert.Equal(new[] { ("g", "The Ur-Quan's ships turn on you."),
            ("k", "The Ur-Quan's ships turn on the players where Host fought them.") }, Told(world));
    }

    [ContentFact]
    public void OnePlayerAloneIsToldNothingNewInASystem()
    {
        // Spotted, met, fought and won, the race turning after; then a talk in peace.
        var (world, d) = InUrQuanSystem();
        var spotter = AddGroup(world, d, 6800, 0, task: IpGroup.OnStation);
        AddGroup(world, d, 0, 9000);
        var m = MeetIn(world, "h");
        Assert.Same(spotter, m.Group);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WinTheBattle(world, m);
        Assert.DoesNotContain(spotter, d.Groups);
        for (int i = 0; i < 10 && d.EncounterRace >= 0; ++i)
            Run(world);
        Assert.Equal(-1, d.EncounterRace);
        AddGroup(world, d, -3000, 0, ships: 2);
        Place(world, "h", -3000, 0);
        MeetIn(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Empty(world.TakeNotices());
    }

    [ContentFact]
    public void AWreckIsToldToo()
    {
        var (world, d) = InUrQuanSystem("g", "k");
        AddGroup(world, d, 6000, 0);
        world.Find("g")!.Ship.Destroyed = true;
        MeetIn(world, "h");
        Assert.Equal(new[] { ("g", "The Ur-Quan hold off: Host is meeting them."), ("k", "The Ur-Quan hold off: Host is meeting them.") },
            Told(world));
    }

    [ContentFact]
    public void AHostileTalkThatCantFightIsToldOverNotPeace()
    {
        // Review round 1: an attack with no battle's content ends the
        // meeting without a battle, as a talk ending hostile does with ships
        // that can't be fought: not peace.
        var (world, d) = InUrQuanSystem("g");
        AddGroup(world, d, 6000, 0);
        MeetIn(world, "h");
        Told(world);
        world.BattleKit = null;
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(new[] { ("g", "Host's meeting with the Ur-Quan is over.") }, Told(world));
    }

    [ContentFact]
    public void AGroupChasingAPlayerGoneAfterAnAttackWithNoBattleIsntToldTalkedAway()
    {
        // Review round 2: in hyperspace an attack with no battle's content
        // ends the meeting unfought, so the group goes one-shot; those it
        // chased are told it's gone after the meeting, not talked away.
        var world = InHyperspace("g", "n");
        var group = GroupBy(world, "h", "SPATHI_SHIP");
        group.Chased.Add("g");
        Meet(world, "h");
        Told(world);
        world.BattleKit = null;
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(new[] { ("g", "The Spathi chasing you are gone after Host's meeting with them."),
            ("n", "Host's meeting with the Spathi is over.") }, Told(world));
    }

    [ContentFact]
    public void AFleeingGroupSpottingAPlayerEveryFrameIsToldOnce()
    {
        // Review round 1: a group fleeing out of the system (not standing
        // down) spots a player near on every frame; only the frame that
        // turned the race (another group of it standing down) is told.
        var (world, d) = InUrQuanSystem("g");
        var fleeing = AddGroup(world, d, 6700, 0, task: IpGroup.Flee, dest: IpGroup.InterceptPlayer);
        var other = AddGroup(world, d, 0, 9000);
        Run(world, 2);
        Assert.Equal((IpGroup.InterceptPlayer, 0), (other.DestLoc, other.Task & IpGroup.IgnoreFlagship));
        Run(world, 6);
        Assert.Contains(d.Groups, g => g == fleeing);
        Assert.Equal(new[] { ("g", "The Ur-Quan turn on you: they spotted Host.") }, Told(world));
    }

    [ContentFact]
    public void TheDroneSentOffByAnothersTalkIsToldGoneToThoseItChased()
    {
        // The drone at Earth met and hailed: check_probe sends it out of the
        // system (it was flown at the guest): talked away.
        var world = SystemGroupTests.NewWorld();
        world.SystemSeeds = star => 1000u + (uint)star;
        Assert.Equal(JoinResult.Joined, world.Join("g", "g"));
        world.UndockShown("g");
        Prepare(world);
        world.PutInSystem("h", SolStar);
        world.PutInSystem("g", SolStar);
        Run(world);
        var d = world.SystemGroups!.Systems[SolStar];
        var drone = d.Groups.Single();
        Assert.Equal(world.Story!.Rules.RaceIndex("URQUAN_DRONE_SHIP"), drone.Race);
        drone.Chased.Add("g");
        // In Earth's inner system, where it orbits, away from the world and the starbase.
        (drone.LocX, drone.LocY) = ((short)3000, (short)-3000);
        d.Elements = null;
        world.Find("h")!.Ship.Ip = SystemGroupTests.FlightAt(SystemGroupTests.Systems.Value[SolStar], drone.LocX, drone.LocY,
            inner: drone.SysLoc - 1);
        world.Find("g")!.Ship.Ip = SystemGroupTests.FlightAt(SystemGroupTests.Systems.Value[SolStar], -6000, -6000);
        Run(world, 2);
        Assert.NotNull(world.MeetingOf("h"));
        Assert.Equal(new[] { ("g", "The Ur-Quan hold off: Host is meeting them.") }, Told(world));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(IpGroup.Flee, drone.Task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup));
        Assert.Equal(new[] { ("g", "The Ur-Quan chasing you are gone: Host talked them away.") }, Told(world));
    }

    private static readonly Lazy<TalkLibrary> Library =
        new(() => TestContent.With(p => TalkLibrary.Load(p, ConversationScripts.Load(DataDir))));

    [ContentFact]
    public void OrbitingARacesWorldHoldsItsGroupsOffForTheOthersAndItsBattleTurnsThem()
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("g", "g"));
        world.UndockShown("g");
        Prepare(world);
        world.EncounterRaces = GameWorld.TalkingRaces;
        world.TalkLibrary = Library.Value;
        int star = world.Systems!.Map.Stars.First(s => s.Special == "ILWRATH_DEFINED").Number;
        world.PutInSystem("g", star);
        world.TakeNotices();
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        Assert.Equal(new[] { ("g", "The Ilwrath hold off: Host is meeting them.") }, Told(world));
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(new[] { ("g", "Host is fighting the Ilwrath.") }, Told(world));
        // The host retreats; as they come back into flight the race turns on the players.
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            world.Step();
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Run(world, 3);
        var told = Told(world);
        Assert.Contains(("g", "The Ilwrath's ships turn on you."), told);
        Assert.DoesNotContain(told, t => t.Item1 == "h");
    }

    // ---- in hyperspace

    // A game in hyperspace: the host at (5800, 6100) in the Ur-Quan's
    // sphere, the guests "g" near (200 east), "n" near (100 east) and "k"
    // far (1200 east, outside the span), every race's groups kept, no day's roll.
    private static GameWorld InHyperspace(params string[] guests)
    {
        var systems = SystemGroupTests.Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        Prepare(world);
        foreach (var p in world.Players)
        {
            p.Ship.Flight.PlaceAtUniverse(p.Id switch { "g" => 6000, "k" => 7000, "n" => 5900, _ => 5800 }, 6100);
            p.Ship.Flight.StarJustLeft = null;
        }
        Run(world);
        world.TakeNotices();
        return world;
    }

    // A group of a race right by a player (as one kept from before, its element made on the next frame).
    private static HyperEncounter GroupBy(GameWorld world, string id, string race, int ships = 1, short dx = 10)
    {
        var f = world.Find(id)!.Ship.Flight;
        int r = world.Story!.Rules.RaceIndex(race);
        var e = new HyperEncounter
        {
            Race = r, Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + dx), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + dx)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story.Rules.RaceShips[r].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    // Steps until the player's meeting is past its spin (or 200 frames).
    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 200 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Run(world);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    [ContentFact]
    public void EveryPlayerNearOrFarInHyperspaceIsToldOfAnothersMeetingAndItsPeace()
    {
        // The owner, 2026-11-21: everyone is told, near or not.
        var world = InHyperspace("g", "k");
        GroupBy(world, "h", "SPATHI_SHIP");
        var m = Meet(world, "h");
        Assert.Equal(new[] { ("g", "Host is meeting the Spathi."), ("k", "Host is meeting the Spathi.") }, Told(world));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(new[] { ("g", "Host made peace with the Spathi."), ("k", "Host made peace with the Spathi.") }, Told(world));
        Run(world, 30);
        Assert.Empty(Told(world));
    }

    [ContentFact]
    public void AGroupChasingAPlayerTalkedAwayByAnotherIsToldGoneInstead()
    {
        var world = InHyperspace("g", "k");
        var group = GroupBy(world, "h", "SPATHI_SHIP");
        group.Chased.Add("g");
        group.Chased.Add("k");   // far off by now: told the meeting's end, as anyone
        Meet(world, "h");
        Told(world);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(new[] { ("g", "The Spathi chasing you are gone: Host talked them away."), ("k", "Host made peace with the Spathi.") },
            Told(world));
    }

    [ContentFact]
    public void ABattleNearInHyperspaceIsToldAsItStartsAndAGroupChasingAPlayerDestroyedAsItEnds()
    {
        var world = InHyperspace("g", "k");
        var group = GroupBy(world, "h", "SPATHI_SHIP");
        group.Chased.Add("g");
        var m = Meet(world, "h");
        Told(world);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(new[] { ("g", "Host is fighting the Spathi."), ("k", "Host is fighting the Spathi.") }, Told(world));
        WinTheBattle(world, m);
        // The battle's end to nobody but those the group chased.
        Assert.Equal(new[] { ("g", "The Spathi chasing you were destroyed in the battle Host started.") }, Told(world));
    }

    // A frame of input: the given player's keys, everyone else's none.
    private static void Fly(GameWorld world, string id, ShipInput input)
    {
        foreach (var p in world.Players.Where(p => p.Present))
            world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? input : ShipInput.None);
        world.Step();
    }

    [ContentFact]
    public void AOneShotGroupSurvivingABattleGoesAndThoseItChasedAreToldSo()
    {
        // Review round 1: a group one-shot from its roll (the probes; here
        // a Spathi group made so) survives the battle as the host retreats,
        // and goes as it's written back.
        var world = InHyperspace("g", "k");
        var group = GroupBy(world, "h", "SPATHI_SHIP", ships: 2);
        group.Flags |= HyperEncounter.OneShot;
        group.Chased.Add("g");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Told(world);
        for (int i = 0; i < 600 && m.Step == MeetingStep.Battle; ++i)
            Fly(world, "h", ShipInput.TakeOff);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.NotEmpty(m.Left!);
        Assert.Equal(new[] { ("g", "The Spathi chasing you are gone after the battle Host started.") }, Told(world));
        Run(world, 5);
        Assert.DoesNotContain(group, world.Encounters!.List);
    }

    // A meeting of the guest "g" with a group by them whose talk ends as given (after its one reply).
    private static Meeting TalkEnding(GameWorld world, Comm.Segue end, HyperEncounter group)
    {
        world.MeetingTalks = (_, _, seed) => MeetingTests.TestTalk(world.Story!, Comm.Segue.Peace, end, seed).Talk;
        var m = Meet(world, "g");
        Assert.Equal(MeetingStep.Talk, m.Step);
        Told(world);
        Assert.True(world.ChooseReply("g", 0));
        return m;
    }

    [ContentFact]
    public void ASurrenderIsToldAsSuchEvenToThoseTheGroupChased()
    {
        var world = InHyperspace("g", "n");
        var group = GroupBy(world, "g", "URQUAN_SHIP");
        group.Chased.Add("h");
        TalkEnding(world, Comm.Segue.Defeat, group);
        Assert.True(world.Find("g")!.Ship.Wreck);
        Assert.Equal(new[] { ("h", "g surrendered to the Ur-Quan."), ("n", "g surrendered to the Ur-Quan.") }, Told(world));
    }

    [ContentFact]
    public void AnInstantVictoryIsToldDestroyedAndToThoseTheGroupChasedGone()
    {
        var world = InHyperspace("g", "n");
        var group = GroupBy(world, "g", "SPATHI_SHIP");
        group.Chased.Add("h");
        var m = TalkEnding(world, Comm.Segue.Victory, group);
        Assert.True(m.InstantVictory);
        Assert.Equal(new[] { ("h", "The Spathi chasing you are gone: g destroyed them."), ("n", "g destroyed the Spathi.") },
            Told(world));
    }

    [ContentFact]
    public void AnInstantVictoryWinningNothingIsToldPeaceAndAOneShotGroupTalkedAway()
    {
        // While the Ur-Quan guard the Sa-Matra nothing is destroyed; the
        // probe (one-shot from its roll) goes all the same.
        var world = InHyperspace("g", "n");
        world.Story!.Set("URQUAN_PROTECTING_SAMATRA", 1);
        var group = GroupBy(world, "g", "SLYLANDRO_SHIP");
        group.Flags |= HyperEncounter.OneShot;
        group.Chased.Add("h");
        var m = TalkEnding(world, Comm.Segue.Victory, group);
        Assert.True(m.InstantVictory);
        Assert.NotEmpty(m.Left!);
        Assert.Equal(new[] { ("h", "The Slylandro chasing you are gone: g talked them away."), ("n", "g made peace with the Slylandro.") },
            Told(world));
    }

    [ContentFact]
    public void AnyBattleStartingIsToldToEveryone()
    {
        var world = InHyperspace("g", "k");
        world.StartBattle("g", EnemyFleet.Of("ilwrath"));
        Assert.Equal(new[] { ("h", "g is fighting the Ilwrath."), ("k", "g is fighting the Ilwrath.") }, Told(world));
    }

    [ContentFact]
    public void AGroupInHyperspaceKeepsWhomItsFlownAt()
    {
        var world = InHyperspace("g");
        var group = GroupBy(world, "g", "SPATHI_SHIP", dx: 60);
        for (int i = 0; i < 30 && group.Chased.Count == 0; ++i)
            Run(world);
        Assert.Equal(new[] { "g" }, group.Chased);
    }

    [ContentFact]
    public void OnePlayerAloneIsToldNothingNewInHyperspace()
    {
        var world = InHyperspace();
        GroupBy(world, "h", "SPATHI_SHIP");
        Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Run(world, 60);
        GroupBy(world, "h", "URQUAN_SHIP");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WinTheBattle(world, m);
        Assert.Empty(world.TakeNotices());
    }

    // ---- on the wire

    [ContentFact]
    public void TheHostSendsANoticeInWordsToTheGuestItsFor()
    {
        var systems = SystemGroupTests.Systems.Value;
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        host.World.BattleKit = WorldBattleTests.Kit();
        host.StartAt(5800, 6100);
        host.Hello(1, "g", "Guest", Protocol.Version);
        host.World.UndockShown("g");
        host.World.Find("g")!.Ship.Flight.PlaceAtUniverse(5900, 6100);
        host.Step(ShipInput.None);
        host.TakeBattleNotices();
        host.World.StartBattle("h", EnemyFleet.Of("ilwrath"));
        host.Step(ShipInput.None);
        var (own, guests) = host.TakeBattleNotices();
        Assert.DoesNotContain(own, n => n.Kind == BattleNoticeKind.Told);
        var told = Assert.Single(guests, g => g.Notice.Kind == BattleNoticeKind.Told);
        Assert.Equal((1L, "Host is fighting the Ilwrath."), (told.Peer, told.Notice.Text));
        // Unchanged on the wire: a notice in words, as a battle too full to join is sent.
        Assert.Equal(told.Notice, BattleNotice.Decode(told.Notice.Encode()));
        Assert.Equal("Host is fighting the Ilwrath.", told.Notice.Describe());
    }
}
