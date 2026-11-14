// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.World;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Enemy fleets (stage 4b item 5): several enemy ships fought one at a
/// time, the next warping in as one dies; the salvage on a win, the first
/// ship's race's bounty for each; the fleet left after a battle lost;
/// infinite fleets bringing their ship back; --start-battle RACE,RACE.
/// In a world of hyperspace only, on sheets of our own (WorldBattleTests'),
/// but for --start-battle, which needs the content.
/// </summary>
[Trait("Topic", "combat")]
public class EnemyFleetTests
{
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(DataDir);

    // Started by the host against a fleet, every other player flying in, everyone warped in.
    private static WorldBattle StartedAgainst(GameWorld world, EnemyFleet fleet, BattleStart? start = null)
    {
        var wb = world.StartBattle("host", fleet, start);
        FlyIn(world, world.Players.Where(p => !p.IsHost).Select(p => p.Id).ToArray());
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Assert.True(Warped(wb.Battle), "every ship warped in");
        return wb;
    }

    // The enemy now killed, and frames run (disarmed) until the next has warped in.
    private static Starship KillAndNext(GameWorld world, WorldBattle wb)
    {
        var dead = wb.Enemy;
        Kill(wb.Battle, dead);
        // Dying (no crew) until new_ship, then the next (or the same, brought back) warping in.
        for (int i = 0; i < 200 && (wb.Enemy.RaceDesc is not { CrewLevel: > 0 } || !Warped(wb.Battle)); ++i)
            RunDisarmed(world, wb, 1);
        Assert.True(wb.Enemy.RaceDesc is { CrewLevel: > 0 } && Warped(wb.Battle), "the next enemy warped in");
        return wb.Enemy;
    }

    private static BattleSnapshot SnapshotOf(GameWorld world, WorldBattle wb) =>
        world.Snapshot().Battles!.Single(b => b.Id == wb.Id);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AFleetComesInOneAtATimeAndAWinPaysTheFirstRacesBountyForEach(int players)
    {
        var world = NewWorld(players);
        var fleet = new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath, 9), new EnemyFleetShip("spathi"), new EnemyFleetShip("arilou", 4) });
        var wb = StartedAgainst(world, fleet);
        Assert.Equal(fleet, wb.EnemyFleet);
        Assert.Equal(new[] { Ilwrath, "spathi", "arilou" }, wb.Enemies.Select(e => e.Ship.Id));
        Assert.Equal(wb.Enemies.Select(e => e.Starship), wb.Battle.Fleet(1));
        Assert.Equal(Ilwrath, wb.EnemyShip.Id);   // the encounter's race, whichever is fighting
        Assert.Equal(3, wb.Battle.BattleCounter[1]);

        // The first, with its crew; the others wait.
        Assert.Same(wb.Enemies[0].Starship, wb.Enemy);
        Assert.Equal((ushort)9, wb.Enemy.RaceDesc!.CrewLevel);
        Assert.All(wb.Enemies.Skip(1), e => Assert.Null(e.Starship.Ship));
        Assert.Equal(Ilwrath, SnapshotOf(world, wb).Enemy.Ship);

        // Each dies, and the next warps in with its crew (its race's starting crew where none is given).
        var spathi = KillAndNext(world, wb);
        Assert.Same(wb.Enemies[1].Starship, spathi);
        Assert.Equal((ushort)Ships["spathi"].Crew, spathi.RaceDesc!.CrewLevel);
        Assert.Equal(("spathi", "spathi"), (wb.EnemyShipNow.Id, SnapshotOf(world, wb).Enemy.Ship));
        Assert.Equal(Ilwrath, wb.EnemyShip.Id);
        Assert.Equal(2, wb.Battle.BattleCounter[1]);
        Assert.False(wb.Enemies[0].Starship.Available);
        Assert.Null(wb.Outcome);
        var arilou = KillAndNext(world, wb);
        Assert.Same(wb.Enemies[2].Starship, arilou);
        Assert.Equal((ushort)4, arilou.RaceDesc!.CrewLevel);
        Assert.Equal("arilou", SnapshotOf(world, wb).Enemy.Ship);
        Assert.All(wb.Fighters, f => Assert.Null(f.Exit));

        // The last: won, each of the three paying the Ilwrath's 1,000 / 8 (not the Spathi's 225 nor the Arilou's 200).
        Kill(wb.Battle, arilou);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 375u), (wb.Outcome, wb.SalvagePaid));
        Assert.Equal(375u, (uint)wb.Fighters.Sum(f => f.Salvage));
        Assert.Equal(new EnemyFleet(Array.Empty<EnemyFleetShip>()), wb.EnemyFleetLeft);   // none left
        Assert.Same(wb.Enemies[2].Starship, wb.Enemy);   // the last there was
    }

    [Fact]
    public void AFleetsShipsOfOneRaceHaveCaptainsOfTheirOwn()
    {
        var world = NewWorld();
        var wb = world.StartBattle("host", EnemyFleet.Of(Enumerable.Repeat(Ilwrath, 16).ToArray()));
        var names = wb.Enemies.Select(e => (int)e.Starship.CaptainsNameIndex).ToList();
        Assert.Equal(16, names.Distinct().Count());   // NameCaptain: drawn again while another of its race has the name
        Assert.All(names, n => Assert.InRange(n, Battle.CaptainNameOffset, Battle.CaptainNameOffset + Battle.NumCaptainsNames - 1));

        // Of other races, a name may come again (NameCaptain looks only at its own race's).
        var mixed = NewWorld().StartBattle("host", EnemyFleet.Of(Enumerable.Range(0, 16).Select(i => i % 2 == 0 ? Ilwrath : "spathi").ToArray()));
        var ilwrath = mixed.Enemies.Where(e => e.Ship.Id == Ilwrath).Select(e => e.Starship.CaptainsNameIndex).ToList();
        var spathi = mixed.Enemies.Where(e => e.Ship.Id == "spathi").Select(e => e.Starship.CaptainsNameIndex).ToList();
        Assert.Equal((8, 8), (ilwrath.Distinct().Count(), spathi.Distinct().Count()));
        Assert.NotEmpty(ilwrath.Intersect(spathi));

        // One ship's is the first draw, as before fleets.
        world = NewWorld();
        var one = world.StartBattle("host", Ilwrath);
        var other = NewWorld().StartBattle("host", EnemyFleet.Of(Ilwrath, "spathi"));
        Assert.Equal(one.Enemy.CaptainsNameIndex, other.Enemies[0].Starship.CaptainsNameIndex);
    }

    [Fact]
    public void ABattleLostLeavesTheFleetWithTheCrewItHas()
    {
        var world = NewWorld(2);
        var wb = StartedAgainst(world, EnemyFleet.Of(Ilwrath, "spathi", "arilou"));
        var spathi = KillAndNext(world, wb);
        wb.Battle.DeltaCrew(spathi.Ship!, -5);
        int crew = spathi.RaceDesc!.CrewLevel;
        Assert.Equal(Ships["spathi"].Crew - 5, crew);

        // Everyone retreats: lost, nothing paid; the Ilwrath destroyed is gone, the Spathi keeps its crew, the Arilou its own.
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            RunDisarmed(world, wb, 1, _ => i == 0 ? ShipInput.TakeOff : ShipInput.None);
        Assert.Equal((BattleOutcome.Lost, 0u), (wb.Outcome, wb.SalvagePaid));
        Assert.All(wb.Fighters, f => Assert.Equal((BattleExit.Retreated, 0u), (f.Exit, f.Salvage)));
        Assert.Equal(new EnemyFleet(new[] { new EnemyFleetShip("spathi", crew), new EnemyFleetShip("arilou", Ships["arilou"].Crew) }),
            wb.EnemyFleetLeft);
        Assert.Null(EnemyFleet.Of(Ilwrath).Ships[0].Crew);
    }

    [Fact]
    public void ABattleLostBeforeAnyEnemyDiesLeavesTheWholeFleet()
    {
        var world = NewWorld();
        var wb = StartedAgainst(world, new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath, 7), new EnemyFleetShip("spathi", 3) }));
        wb.Battle.DeltaCrew(wb.Enemy.Ship!, -2);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            RunDisarmed(world, wb, 1, _ => i == 0 ? ShipInput.TakeOff : ShipInput.None);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Equal(new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath, 5), new EnemyFleetShip("spathi", 3) }), wb.EnemyFleetLeft);
    }

    [Fact]
    public void AnInfiniteFleetBringsItsShipBackWithFullCrewAndANewCaptain()
    {
        var world = NewWorld(2);
        var wb = StartedAgainst(world, EnemyFleet.InfiniteOf("syreen"));
        var ship = Assert.Single(wb.Battle.Fleet(1));
        Assert.True(wb.Battle.InfiniteEnemy);
        Assert.Equal((ushort)Ships["syreen"].MaxCrew, ship.RaceDesc!.CrewLevel);
        // GetEncounterStarShip counts its ship each time it's picked: 2 with the first.
        Assert.Equal(2, wb.Battle.BattleCounter[1]);
        var places = wb.Battle.Random.Places = new List<string>();

        var captains = new List<byte> { ship.CaptainsNameIndex };
        for (int round = 0; round < 3; ++round)
        {
            places.Clear();
            Assert.Same(ship, KillAndNext(world, wb));
            Assert.True(ship.Available);
            Assert.Equal((ushort)Ships["syreen"].MaxCrew, ship.RaceDesc!.CrewLevel);
            Assert.Equal(2, wb.Battle.BattleCounter[1]);
            Assert.Contains("PickCaptainName", places);   // the battle's own draw, as UQM's TFB_Random
            captains.Add(ship.CaptainsNameIndex);
            Assert.InRange(ship.CaptainsNameIndex, Battle.CaptainNameOffset, Battle.CaptainNameOffset + Battle.NumCaptainsNames - 1);
            Assert.Null(wb.Outcome);
        }
        Assert.All(wb.Fighters, f => Assert.Null(f.Exit));

        // Never beaten: the players leaving lose it, the fleet as it was.
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            RunDisarmed(world, wb, 1, _ => i == 0 ? ShipInput.TakeOff : ShipInput.None);
        Assert.Equal((BattleOutcome.Lost, 0u), (wb.Outcome, wb.SalvagePaid));
        Assert.Equal(EnemyFleet.InfiniteOf("syreen"), wb.EnemyFleetLeft);
    }

    [Fact]
    public void AFirstRaceWithoutAShipCostPaysNoSalvage()
    {
        // The Androsynth are no race of the full game's (not in avail_race_q): UQM shows no tally and pays nothing.
        var world = NewWorld();
        var wb = StartedAgainst(world, EnemyFleet.Of("androsyn", Ilwrath));
        KillAndNext(world, wb);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 0u), (wb.Outcome, wb.SalvagePaid));
        Assert.Equal(0u, world.Host.Ship.Hold.ResourceUnits);

        // Second, it pays the first race's bounty.
        world = NewWorld();
        wb = StartedAgainst(world, EnemyFleet.Of(Ilwrath, "androsyn"));
        KillAndNext(world, wb);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 250u), (wb.Outcome, wb.SalvagePaid));
    }

    [Fact]
    public void AGuestCanJoinWhileTheFleetHasShipsLeft()
    {
        var world = NewWorld(2);
        var wb = world.StartBattle("host", EnemyFleet.Of(Ilwrath, "spathi"));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        KillAndNext(world, wb);
        wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;
        FlyIn(world, "g1");
        Assert.Equal(new[] { "host", "g1" }, wb.Fighters.Select(f => f.Player.Id));
        Assert.Equal(2, wb.Battle.BattleCounter[0]);
    }

    [Fact]
    public void AFleetIsRefusedWhenItCantBeFought()
    {
        // Refused before anything changes: the ship flies on.
        var world = NewWorld();
        world.Host.Ship.Flight.Velocity.SetComponents(8, 0);
        Assert.Throws<ArgumentException>(() => world.StartBattle("host", new EnemyFleet(Array.Empty<EnemyFleetShip>())));
        Assert.Throws<ArgumentException>(() => world.StartBattle("host", EnemyFleet.Of(Ilwrath, "probe")));
        Assert.Throws<ArgumentException>(() => world.StartBattle("host",
            new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath), new EnemyFleetShip(Ilwrath) }, Infinite: true)));
        Assert.Throws<ArgumentException>(() => world.StartBattle("host",
            new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath, 3) }, Infinite: true)));
        Assert.Throws<ArgumentException>(() => world.StartBattle("host", EnemyFleet.Of(Ilwrath), new BattleStart(EnemyCrew: 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.StartBattle("host",
            new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath), new EnemyFleetShip("arilou", 7) })));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.StartBattle("host",
            new EnemyFleet(new[] { new EnemyFleetShip(Ilwrath), new EnemyFleetShip("arilou", 0) })));
        Assert.Empty(world.Battles);
        Assert.Null(world.Host.Ship.Battle);
        Assert.False(world.Host.Ship.Flight.Velocity.IsZero, "still flying");

        // The one-ship start keeps its crew in its BattleStart, as the Earth fight's.
        var wb = world.StartBattle("host", Ilwrath, new BattleStart(EnemyCrew: 7));
        Assert.Equal(new BattleStart(EnemyCrew: 7), wb.Start);
        Assert.Equal(EnemyFleet.Of(Ilwrath, 7), wb.EnemyFleet);
    }

    [Fact]
    public void ABattleOfAnInfiniteFleetIsOneShip()
    {
        var graphics = new SolidGraphics();
        Starship Enemy() => new(RaceShips.Code(Ships[Ilwrath], graphics.Views, false), 1, 22, 22);
        var player = new Starship(RaceShips.Code(Ships[Ilwrath], graphics.Views, false), 0, 22, 22);
        Assert.Throws<ArgumentException>(() => new Battle(graphics.Arena(0),
            new[] { new[] { player }, new[] { Enemy(), Enemy() } }, 1, infiniteEnemy: true));
    }

    [ContentFact]
    public void StartBattleTakesAFleet()
    {
        var systems = WorldBattleTests.Systems.Value;
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host",
            systems, TestContent.Shapes);
        host.World.BattleKit = BattleReplay.Kit.Value;
        Assert.Throws<ArgumentException>(() => host.StartBattle("ilwrath,probe"));
        Assert.Throws<ArgumentException>(() => host.StartBattle("ilwrath,"));
        Assert.Empty(host.World.Battles);
        Assert.Equal("Starting in a battle against a fleet of 3 (spathi, ilwrath, spathi) by Earth", host.StartBattle("spathi, ilwrath,spathi"));
        var wb = Assert.Single(host.World.Battles);
        Assert.Equal(EnemyFleet.Of("spathi", Ilwrath, "spathi"), wb.EnemyFleet);
        Assert.Equal("spathi", wb.EnemyShip.Id);
    }
}
