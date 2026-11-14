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
using Crateris.Presentation;
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
        var wb = world.StartBattle("host", EnemyFleet.Of(Enumerable.Repeat(Ilwrath, EnemyFleet.MostShips).ToArray()));
        var names = wb.Enemies.Select(e => (int)e.Starship.CaptainsNameIndex).ToList();
        Assert.Equal(14, names.Distinct().Count());   // NameCaptain: drawn again while another of its race has the name
        Assert.All(names, n => Assert.InRange(n, Battle.CaptainNameOffset, Battle.CaptainNameOffset + Battle.NumCaptainsNames - 1));

        // Of other races, a name may come again (NameCaptain looks only at its own race's).
        var mixed = NewWorld().StartBattle("host", EnemyFleet.Of(Enumerable.Range(0, 14).Select(i => i % 2 == 0 ? Ilwrath : "spathi").ToArray()));
        var ilwrath = mixed.Enemies.Where(e => e.Ship.Id == Ilwrath).Select(e => e.Starship.CaptainsNameIndex).ToList();
        var spathi = mixed.Enemies.Where(e => e.Ship.Id == "spathi").Select(e => e.Starship.CaptainsNameIndex).ToList();
        Assert.Equal((7, 7), (ilwrath.Distinct().Count(), spathi.Distinct().Count()));
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
    public void EveryRaceShipPaysItsRacesBounty()
    {
        // races.h's RACE_SHIP_COST / 8, by ship: the Earthling's is HUMAN_SHIP's, the Ur-Quan's URQUAN_SHIP's,
        // the Kohr-Ah's BLACK_URQUAN_SHIP's; the three races with no fleet in the full game pay none.
        var expected = new Dictionary<string, int>
        {
            ["arilou"] = 200, ["chmmr"] = 375, ["human"] = 137, ["orz"] = 287, ["pkunk"] = 250, ["shofixti"] = 62,
            ["spathi"] = 225, ["supox"] = 200, ["thradd"] = 125, ["utwig"] = 275, ["vux"] = 150, ["yehat"] = 287,
            ["melnorme"] = 450, ["druuge"] = 212, ["ilwrath"] = 125, ["mycon"] = 262, ["slylandr"] = 550, ["umgah"] = 87,
            ["urquan"] = 375, ["zoqfot"] = 75, ["syreen"] = 162, ["blackurq"] = 375,
            ["androsyn"] = 0, ["chenjesu"] = 0, ["mmrnmhrm"] = 0,
        };
        var kit = Kit();
        Assert.Equal(expected.Keys.Order(), BattleKit.Enemies.Order());
        Assert.All(BattleKit.Enemies, id => Assert.Equal(expected[id], kit.Salvage(Ships[id])));
        // The flagship, the Sa-Matra and the probe are no race's: an error, not a ship paying nothing.
        Assert.Throws<ArgumentException>(() => kit.Salvage(Ships[ShipDescriptions.Flagship]));
        Assert.Throws<ArgumentException>(() => kit.Salvage(Ships["probe"]));
        // A race in the data without its cost is an error too.
        var costless = new BattleKit(Ships, new SolidGraphics(), new Dictionary<string, int>());
        Assert.Throws<KeyNotFoundException>(() => costless.Salvage(Ships[Ilwrath]));
        Assert.Equal(0, costless.Salvage(Ships["androsyn"]));

        // In a battle: a fleet of Ur-Quan, Spathi, won, pays the Ur-Quan's twice.
        var world = NewWorld();
        var wb = StartedAgainst(world, EnemyFleet.Of("urquan", "spathi"));
        KillAndNext(world, wb);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 750u), (wb.Outcome, wb.SalvagePaid));
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

        // And while an enemy ship that isn't the last is dying (not yet replaced).
        world = NewWorld(2);
        wb = world.StartBattle("host", EnemyFleet.Of(Ilwrath, "spathi"));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        var first = wb.Enemy;
        Kill(wb.Battle, first);
        RunDisarmed(world, wb, 1);
        Assert.Same(first, wb.Enemy);   // exploding
        Assert.Equal(1, wb.Battle.BattleCounter[1]);
        FlyIn(world, "g1");
        Assert.Equal(new[] { "host", "g1" }, wb.Fighters.Select(f => f.Player.Id));
    }

    [Fact]
    public void ABattleLostByDeathsMidFleetLeavesTheRest()
    {
        // A guest's escort starts it alone: its death loses the battle (the flagship's would end the game).
        var world = NewWorld(2);
        world.Find("g1")!.Ship.Flight.LogX += 5000;   // out of the host's reach
        var wb = world.StartBattle("g1", EnemyFleet.Of(Ilwrath, "spathi", "arilou"));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        var spathi = KillAndNext(world, wb);
        wb.Battle.DeltaCrew(spathi.Ship!, -4);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Lost, BattleExit.Destroyed), (wb.Outcome, Fighter(wb, "g1").Exit));
        Assert.Equal(new EnemyFleet(new[] { new EnemyFleetShip("spathi", Ships["spathi"].Crew - 4), new EnemyFleetShip("arilou", Ships["arilou"].Crew) }),
            wb.EnemyFleetLeft);
    }

    [Fact]
    public void ADisconnectMidFleetLeavesTheOthersFightingTheNext()
    {
        var world = NewWorld(2);
        var wb = StartedAgainst(world, EnemyFleet.Of(Ilwrath, "spathi"));
        var spathi = KillAndNext(world, wb);
        world.Leave("g1");
        Assert.Equal(BattleExit.Disconnected, Fighter(wb, "g1").Exit);
        Assert.Same(spathi, wb.Enemy);
        Assert.Null(wb.Outcome);
        Kill(wb.Battle, spathi);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 250u), (wb.Outcome, wb.SalvagePaid));   // the Ilwrath's twice, all to the host
        Assert.Equal(250u, Fighter(wb, "host").Salvage);

        // Against an infinite fleet, a guest disconnecting leaves the host fighting; the host retreating then loses it, the fleet as it was.
        world = NewWorld(2);
        wb = StartedAgainst(world, EnemyFleet.InfiniteOf("syreen"));
        KillAndNext(world, wb);
        world.Leave("g1");
        Assert.Null(wb.Outcome);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            RunDisarmed(world, wb, 1, _ => i == 0 ? ShipInput.TakeOff : ShipInput.None);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Equal(EnemyFleet.InfiniteOf("syreen"), wb.EnemyFleetLeft);
    }

    // A battle against two Pkunk, started after the frames that make its seed one where the first is reborn
    // (pkunk_preprocess's phoenix drawn at warp-in), run until its first Pkunk is back, warped in again.
    private static readonly Lazy<int> RebornSkip = new(() =>
        Enumerable.Range(0, 40).FirstOrDefault(skip => PkunkBattle(skip).Reborn, -1) is var s and >= 0 ? s
            : throw new InvalidOperationException("no seed found where the first Pkunk is reborn"));

    private static (GameWorld World, WorldBattle Battle, bool Reborn) PkunkBattle(int skip, Action<GameWorld, WorldBattle>? eachFrame = null)
    {
        var world = NewWorld();
        Run(world, skip);
        var wb = StartedAgainst(world, EnemyFleet.Of("pkunk", "pkunk"));
        eachFrame?.Invoke(world, wb);
        var first = wb.Enemy;
        Kill(wb.Battle, first);
        for (int i = 0; i < 200 && (wb.Enemy.RaceDesc is not { CrewLevel: > 0 } || !Warped(wb.Battle)); ++i)
        {
            RunDisarmed(world, wb, 1);
            eachFrame?.Invoke(world, wb);
        }
        return (world, wb, wb.Enemy == first);
    }

    private static (GameWorld, WorldBattle) FirstPkunkReborn(Action<GameWorld, WorldBattle>? eachFrame = null)
    {
        var (world, wb, reborn) = PkunkBattle(RebornSkip.Value, eachFrame);
        Assert.True(reborn);
        return (world, wb);
    }

    [Fact]
    public void EachPkunkOfAFleetStartsItsInsultsAfresh()
    {
        // pkunk.c's LastSound, set back as each Pkunk ship is made (init_pkunk), not as one is reborn (new_pkunk).
        int lastBefore = -1;
        var (world, wb) = FirstPkunkReborn((_, b) =>
        {
            if (b.Enemy.RaceDesc is not { CrewLevel: > 0 })
                b.Battle.ShipState<PkunkShip.Insults>().LastSound = lastBefore = 5;
        });
        var insults = wb.Battle.ShipState<PkunkShip.Insults>();
        Assert.Equal((5, 5), (lastBefore, insults.LastSound));   // reborn: kept
        var second = KillAndNextWith(world, wb, () => insults.LastSound = 5);
        Assert.Same(wb.Enemies[1].Starship, second);
        Assert.Equal(0, insults.LastSound);   // the next ship made: afresh
    }

    // KillAndNext, with something done each frame while the enemy is dying.
    private static Starship KillAndNextWith(GameWorld world, WorldBattle wb, Action dying)
    {
        var dead = wb.Enemy;
        Kill(wb.Battle, dead);
        for (int i = 0; i < 200 && (wb.Enemy.RaceDesc is not { CrewLevel: > 0 } || !Warped(wb.Battle)); ++i)
        {
            if (wb.Enemy.RaceDesc is not { CrewLevel: > 0 })
                dying();
            RunDisarmed(world, wb, 1);
        }
        return wb.Enemy;
    }

    // A client's battle screen fed the world's snapshot each frame, noting the frames its enemy status is drawn afresh in.
    private sealed class Watcher
    {
        private readonly BattleScreen _screen;
        private int _frame;
        public readonly List<int> Fresh = new();
        public readonly List<int> Crew = new();

        public Watcher() => _screen = new BattleScreen(new UqmCanvas(_ => 1, (_, _) => default, new Dictionary<string, int>()),
            (s, id) =>
            {
                if (id is null)
                    Fresh.Add(_frame);
                return new StatusShip(null, null, s.MaxCrew, s.MaxEnergy, false, false, id != null, "n", "c");
            });

        public void See(GameWorld world, WorldBattle wb)
        {
            var b = world.Snapshot().Battles!.Single(x => x.Id == wb.Id);
            _frame = Crew.Count;
            Crew.Add(b.Enemy.Crew);
            _screen.Frame(b, "host", BattleSpace.SizeOf);
        }

        // Every fresh drawing but the first is in the first frame with crew after frames without.
        public void AssertEachFreshIsAnArrival(int count)
        {
            Assert.Equal(count, Fresh.Count);
            Assert.All(Fresh.Skip(1), f => Assert.True(Crew[f] > 0 && Crew[f - 1] == 0, $"frame {f}: crew {Crew[f - 1]} then {Crew[f]}"));
            for (int f = 1; f < Crew.Count; ++f)
                if (Crew[f] > 0 && Crew[f - 1] == 0)
                    Assert.Contains(f, Fresh);
        }
    }

    [Fact]
    public void TheEnemysStatusIsDrawnAfreshInTheFrameEachShipComesBack()
    {
        // An infinite fleet's ship, back with its element (never gone): drawn afresh each time, and not while it waits dead.
        var world = NewWorld();
        var wb = StartedAgainst(world, EnemyFleet.InfiniteOf("syreen"));
        var watcher = new Watcher();
        watcher.See(world, wb);
        for (int round = 0; round < 2; ++round)
        {
            Kill(wb.Battle, wb.Enemy);
            for (int i = 0; i < 200 && (wb.Enemy.RaceDesc is not { CrewLevel: > 0 } || !Warped(wb.Battle)); ++i)
            {
                RunDisarmed(world, wb, 1);
                watcher.See(world, wb);
            }
        }
        Assert.All(world.Snapshot().Battles!, b => Assert.False(b.Enemy.Gone));
        watcher.AssertEachFreshIsAnArrival(3);

        // A Pkunk reborn: new_pkunk's APPEARING, ship_preprocess's InitShipStatus.
        var pkunk = new Watcher();
        var (w2, b2) = FirstPkunkReborn((w, b) => pkunk.See(w, b));
        pkunk.AssertEachFreshIsAnArrival(2);
    }

    [Fact]
    public void AFleetIsRefusedWhenItCantBeFought()
    {
        // Refused before anything changes: the ship flies on.
        var world = NewWorld();
        world.Host.Ship.Flight.Velocity.SetComponents(8, 0);
        Assert.Throws<ArgumentException>(() => world.StartBattle("host", new EnemyFleet(Array.Empty<EnemyFleetShip>())));
        // MAX_SHIPS_PER_SIDE's 14 at most (17 of one race couldn't each have a name of their own, of 16).
        Assert.Throws<ArgumentException>(() => world.StartBattle("host", EnemyFleet.Of(Enumerable.Repeat(Ilwrath, 15).ToArray())));
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

        // A fleet is its own: the list it was made from changed later changes nothing.
        var list = new List<EnemyFleetShip> { new(Ilwrath) };
        var fleet = new EnemyFleet(list);
        list.Add(new("spathi"));
        Assert.Equal(EnemyFleet.Of(Ilwrath), fleet);
        var other = new List<EnemyFleetShip> { new("arilou") };
        var changed = fleet with { Ships = other };
        other.Add(new("spathi"));
        Assert.Equal(EnemyFleet.Of("arilou"), changed);

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
        Assert.Throws<ArgumentException>(() => host.StartBattle(string.Join(",", Enumerable.Repeat("spathi", 15))));
        Assert.Empty(host.World.Battles);
        Assert.Equal(0, host.World.Story!.Get("STARBASE_AVAILABLE"));   // refused before the opening is put behind it
        Assert.Equal("Starting in a battle against a fleet of 3 (spathi, ilwrath, spathi) by Earth", host.StartBattle("spathi, ilwrath,spathi"));
        var wb = Assert.Single(host.World.Battles);
        Assert.Equal(EnemyFleet.Of("spathi", Ilwrath, "spathi"), wb.EnemyFleet);
        Assert.Equal("spathi", wb.EnemyShip.Id);
    }
}
