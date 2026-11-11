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
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Battles in the world (stage 4a item 6): starting one, its marker,
/// joining by flying into it, retreating, disconnecting, winning and losing
/// with one to four players, the salvage and its remainder, a battle no one
/// is left in, saves during one, and the escape counter. Most run in a
/// world of hyperspace only, on sheets of our own, without the content;
/// those in Earth's system (the escape counter, a story pause mid-battle,
/// --start-battle) need it.
/// </summary>
[Trait("Topic", "combat")]
public class WorldBattleTests
{
    internal static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(DataDir);
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    internal const string Ilwrath = ShipDescriptions.Ilwrath;

    /// <summary>The battle's sheets: solid squares, ships' hulls 20 pixels, everything else smaller.</summary>
    internal sealed class SolidGraphics : IBattleGraphics
    {
        private static readonly HashSet<string> Hulls = Ships.All
            .Select(s => ShipGraphics.Sheets(s.Graphics.Ship)?[0]).OfType<string>().ToHashSet();
        private readonly Dictionary<string, Views> _views = new();

        public Views? Views(IReadOnlyList<string?> resources)
        {
            if (ShipGraphics.Sheets(resources) is not { } sheets)
                return null;
            lock (_views)
            {
                if (!_views.TryGetValue(sheets[0], out var views))
                    _views[sheets[0]] = views = Hulls.Contains(sheets[0])
                        ? BattleEndTests.Three(BattleEndTests.Solid(sheets[0], 16, 20))
                        : BattleEndTests.Three(BattleEndTests.Solid(sheets[0], 24, 4));
                return views;
            }
        }

        public BattleArena Arena(byte battlePlanet) => new(new Views(BattleEndTests.Solid("stars", 3, 1)),
            BattleEndTests.Three(BattleEndTests.Solid("asteroid", 21, 10)), BattleEndTests.Three(BattleEndTests.Solid("blast", 8, 5)),
            BattleEndTests.Three(BattleEndTests.Solid("boom", 9, 3)), BattleEndTests.Three(BattleEndTests.Solid($"planet{battlePlanet}", 1, 60)));
    }

    internal static BattleKit Kit() => new(Ships, new SolidGraphics(), BattleKit.LoadShipCosts(DataDir));

    // A world of hyperspace only, the host and guests g1, g2 ... together at the start.
    internal static GameWorld NewWorld(int players = 1, GameSettings? settings = null)
    {
        var world = GameWorld.NewGame(settings ?? new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.BattleKit = Kit();
        for (int i = 1; i < players; ++i)
        {
            Assert.Equal(JoinResult.Joined, world.Join($"g{i}", $"Guest {i}"));
            world.UndockShown($"g{i}");
        }
        return world;
    }

    internal static void Run(GameWorld world, int frames, Func<string, ShipInput>? keys = null)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, keys?.Invoke(p.Id) ?? ShipInput.None);
            world.Step();
        }
    }

    // Frames with the enemy unable to fire or cloak (its weapon and special waiting), so nothing it does decides a test.
    internal static void RunDisarmed(GameWorld world, WorldBattle wb, int frames, Func<string, ShipInput>? keys = null)
    {
        for (int i = 0; i < frames && wb.Outcome is null; ++i)
        {
            wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;
            Run(world, 1, keys);
        }
    }

    // Until the battle is over, or 600 frames.
    internal static void RunOut(GameWorld world, WorldBattle wb, Func<string, ShipInput>? keys = null)
    {
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            Run(world, 1, keys);
        Assert.NotNull(wb.Outcome);
    }

    // Ships flying into the marker in hyperspace: out of its reach for a
    // frame, then back where they were (a frame each way).
    internal static void FlyIn(GameWorld world, params string[] ids)
    {
        foreach (var id in ids)
            world.Find(id)!.Ship.Flight.LogX += 1000;
        Run(world, 1);
        foreach (var id in ids)
            world.Find(id)!.Ship.Flight.LogX -= 1000;
        Run(world, 1);
    }

    // Started by the host, every other player flying into its marker,
    // everyone warped in (the battle's frames up to 60).
    internal static WorldBattle Started(GameWorld world, BattleStart? start = null)
    {
        var wb = world.StartBattle("host", Ilwrath, start);
        FlyIn(world, world.Players.Where(p => !p.IsHost).Select(p => p.Id).ToArray());
        Assert.Equal(world.Players.Select(p => p.Id), wb.Fighters.Select(f => f.Player.Id));
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Assert.True(Warped(wb.Battle), "every ship warped in");
        return wb;
    }

    // Every ship in, warped in (the Ilwrath may be cloaking: its stamp filled).
    internal static bool Warped(Battle battle) => battle.Fleet(0).Concat(battle.Fleet(1)).Where(s => s.Ship != null)
        .All(s => s.Ship!.Prim is PrimType.Stamp or PrimType.StampFill
                  && (s.Ship.StateFlags & (ElementFlags.Appearing | ElementFlags.NonSolid)) == 0);

    internal static void Kill(Battle battle, Starship ship) => battle.DoDamage(ship.Ship!, (short)ship.RaceDesc!.CrewLevel);

    internal static BattleFighter Fighter(WorldBattle wb, string id) => wb.FighterOf(id)!;

    [Fact]
    public void ABattleStartsWhereTheShipIsWithItsMarker()
    {
        var world = NewWorld(2);
        var host = world.Host.Ship;
        host.Flight.Velocity.SetComponents(8, 0);
        var wb = world.StartBattle("host", Ilwrath);
        Assert.Equal(Activity.Battle, host.Activity);
        Assert.True(host.Flight.Velocity.IsZero, "stopped where it is");
        Assert.Equal(new BattlePlace(null, null, host.Flight.LogX, host.Flight.LogY), wb.Place);
        Assert.Equal(new[] { wb }, world.Battles);
        Assert.Equal(new[] { wb }, world.MarkersFor("g1"));
        Assert.Empty(world.MarkersFor("host"));   // in it

        // The settings' arena and list, the planet a random type's (in hyperspace); the Ilwrath of 22 crew,
        // which cloaks; the flagship with its crew.
        var battle = wb.Battle;
        Assert.InRange(wb.BattlePlanet, 0, WorldTypes.NumberOfPlanetTypes - 1);
        Assert.Equal($"planet{wb.BattlePlanet}", battle.Arena.Planet[0].Sheet!.Id);
        Assert.Equal(ArenaSize.Scaled(1.5), battle.Size);
        Assert.Equal(300, battle.List.Capacity);
        Assert.Equal((ushort)22, wb.Enemy.RaceDesc!.CrewLevel);
        Assert.False(((IlwrathShip)wb.Enemy.Code).NoCloak);
        var fighter = Fighter(wb, "host");
        Assert.Equal((ushort)51, fighter.Starship.RaceDesc!.CrewLevel);
        Assert.False(fighter.Code.BombCarrier);
        Assert.Equal((50, 1000u), (fighter.JoinedHold.Crew, fighter.JoinedFuel));

        // A ship in a battle, an unknown or unported race, a world without the battle's content: refused.
        Assert.Throws<InvalidOperationException>(() => world.StartBattle("host", Ilwrath));
        Assert.Throws<ArgumentException>(() => world.StartBattle("g1", "vux"));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.StartBattle("g1", Ilwrath, new BattleStart(EnemyCrew: 23)));
        var bare = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        Assert.Throws<InvalidOperationException>(() => bare.StartBattle("host", Ilwrath));

        // The Earth fight's: 7 crew, no cloak.
        var other = world.StartBattle("g1", Ilwrath, new BattleStart(EnemyCrew: 7, NoCloak: true, PaysSalvage: false));
        Assert.Equal((ushort)7, other.Enemy.RaceDesc!.CrewLevel);
        Assert.True(((IlwrathShip)other.Enemy.Code).NoCloak);
    }

    [Fact]
    public void FlyingIntoTheMarkerJoins()
    {
        var world = NewWorld(3);
        var wb = world.StartBattle("host", Ilwrath);
        // Those there as it starts are outside it: waiting, steering or
        // flying off, they don't join.
        Run(world, 5, id => id == "g1" ? ShipInput.Left : ShipInput.None);
        Run(world, 2, id => id == "g2" ? ShipInput.Thrust : ShipInput.None);
        Assert.Single(wb.Fighters);
        // Out of its reach and back in, they do.
        FlyIn(world, "g1");
        Assert.Equal(new[] { "host", "g1" }, wb.Fighters.Select(f => f.Player.Id));
        var g1 = world.Find("g1")!.Ship;
        Assert.Equal((Activity.Battle, true), (g1.Activity, g1.Flight.Velocity.IsZero));
        Assert.Same(wb.Battle.Fleet(0)[1], Fighter(wb, "g1").Starship);
        Assert.Equal(2, wb.Battle.BattleCounter[0]);

        // A ship just beyond its reach doesn't; coming within it does.
        var g2 = world.Find("g2")!.Ship;
        g2.Flight.Stop();
        g2.Flight.LogX = wb.Place.X + world.Settings.BattleMarkerReach * 4 + 4;
        g2.Flight.LogY = wb.Place.Y;
        Run(world, 2);
        Assert.Equal(2, wb.Fighters.Count);
        g2.Flight.LogX -= 8;
        Run(world, 1);
        Assert.Equal(3, wb.Fighters.Count);
    }

    [Fact]
    public void ANewGuestStartsByTheMarkerOutsideIt()
    {
        // Row 14: a guest joining the game for the first time while the host
        // is in a battle is put at the host's place, its marker, outside it.
        var world = NewWorld();
        var wb = world.StartBattle("host", Ilwrath);
        Assert.Equal(JoinResult.Joined, world.Join("g1", "Guest 1"));
        world.UndockShown("g1");
        var g1 = world.Find("g1")!.Ship;
        Assert.Equal((wb.Place.X, wb.Place.Y), (g1.Flight.LogX, g1.Flight.LogY));
        Run(world, 3, id => id == "g1" ? ShipInput.Left | ShipInput.Thrust : ShipInput.None);
        Assert.Equal(Activity.Hyperspace, g1.Activity);
        Assert.Single(wb.Fighters);
        FlyIn(world, "g1");
        Assert.Equal(Activity.Battle, g1.Activity);
    }

    [Fact]
    public void TwoBattlesAtOnce()
    {
        var world = NewWorld(3);
        var first = world.StartBattle("host", Ilwrath);
        var g1 = world.Find("g1")!.Ship;
        g1.Flight.LogX += 20000;
        Run(world, 1);
        var second = world.StartBattle("g1", Ilwrath);
        Assert.Equal(new[] { first, second }, world.Battles);
        Assert.NotEqual(first.Battle.Random.Seed, second.Battle.Random.Seed);
        Assert.Equal(new[] { first, second }, world.MarkersFor("g2"));
        // Flying into the second's marker joins that one only.
        var g2 = world.Find("g2")!.Ship;
        g2.Flight.LogX += 10000;
        Run(world, 1);
        (g2.Flight.LogX, g2.Flight.LogY) = (second.Place.X, second.Place.Y);
        Run(world, 1);
        Assert.Equal((null, "g2"), (first.FighterOf("g2")?.Player.Id, second.FighterOf("g2")?.Player.Id));
        for (int i = 0; i < 60 && !(Warped(first.Battle) && Warped(second.Battle)); ++i)
            Run(world, 1);
        Kill(first.Battle, first.Enemy);
        Kill(second.Battle, second.Enemy);
        RunOut(world, first);
        RunOut(world, second);
        Assert.Equal((BattleOutcome.Won, BattleOutcome.Won), (first.Outcome!.Value, second.Outcome!.Value));
        Assert.Equal((125u, 63u, 62u), (world.Host.Ship.Hold.ResourceUnits, g1.Hold.ResourceUnits, g2.Hold.ResourceUnits));
        Assert.Empty(world.Battles);
    }

    [Fact]
    public void ABattleTooFullToJoinNowIsSaidOnceAndTriedAgain()
    {
        var world = NewWorld(2);
        var wb = world.StartBattle("host", Ilwrath);
        var taken = new List<Element>();
        while (wb.Battle.List.Alloc() is { } e)
            taken.Add(e);
        FlyIn(world, "g1");
        Run(world, 2);
        Assert.Single(wb.Fighters);
        Assert.Equal(new[] { ("g1", "The battle is too crowded to join just now.") }, world.TakeBattleNotices());
        foreach (var e in taken)
            wb.Battle.List.Free(e);
        Run(world, 1);   // still in the marker: tried again
        Assert.Equal(2, wb.Fighters.Count);
        Assert.Empty(world.TakeBattleNotices());
    }

    [Theory]
    [InlineData(1, new uint[] { 125 })]
    [InlineData(2, new uint[] { 63, 62 })]
    [InlineData(3, new uint[] { 43, 41, 41 })]
    [InlineData(4, new uint[] { 32, 31, 31, 31 })]
    public void WinningSplitsTheSalvageTheRemainderToTheFirst(int players, uint[] shares)
    {
        var world = NewWorld(players);
        var wb = Started(world);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Equal(125u, wb.SalvagePaid);   // an Ilwrath's 1,000 / 8
        Assert.Equal(shares, wb.Fighters.Select(f => f.Salvage));
        foreach (var f in wb.Fighters)
        {
            var ship = f.Player.Ship;
            Assert.Equal(BattleExit.Fought, f.Exit);
            Assert.Equal((Activity.Hyperspace, f.Salvage, f.CrewLeft), (ship.Activity, ship.Hold.ResourceUnits, ship.Hold.Crew));
            Assert.Equal(f.JoinedFuel, ship.Flight.Fuel);
            Assert.True(f.CrewLeft <= f.JoinedHold.Crew);
        }
        Assert.Empty(world.Battles);
        Assert.Equal(new[] { wb }, world.TakeEndedBattles());
        Assert.Empty(world.MarkersFor("host"));
        Assert.False(world.GameOver);

        // A battle that pays none (the Earth Ilwrath's) pays none.
        world = NewWorld(players);
        wb = Started(world, new BattleStart(PaysSalvage: false));
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal((BattleOutcome.Won, 0u), (wb.Outcome, wb.SalvagePaid));
        Assert.All(world.Players, p => Assert.Equal(0u, p.Ship.Hold.ResourceUnits));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void LosingDestroysEveryShipAndTheFlagshipsEndsTheGame(int players)
    {
        var world = NewWorld(players);
        var wb = Started(world);
        // The guests die first: each watches the battle on.
        foreach (var f in wb.Fighters.Reverse())
        {
            Kill(wb.Battle, f.Starship);
            Run(world, 1);
            Assert.Equal((BattleExit.Destroyed, Activity.Battle), (f.Exit, f.Player.Ship.Activity));
        }
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.All(wb.Fighters, f =>
        {
            Assert.Equal((BattleExit.Destroyed, 0u), (f.Exit, f.Salvage));
            Assert.Equal((true, 0, Activity.Hyperspace), (f.Player.Ship.Destroyed, f.Player.Ship.Hold.Crew, f.Player.Ship.Activity));
        });
        Assert.True(world.GameOver);
        long frame = world.Frame;
        Run(world, 5);
        Assert.Equal(frame, world.Frame);   // the world stops
    }

    [Fact]
    public void AnEscortDestroyedGetsNoSalvageAndFliesNoMore()
    {
        var world = NewWorld(3);
        var wb = Started(world);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Equal(new uint[] { 63, 0, 62 }, wb.Fighters.Select(f => f.Salvage));
        var g1 = world.Find("g1")!.Ship;
        Assert.True(g1.Destroyed);
        Assert.False(world.GameOver);
        var at = (g1.Flight.LogX, g1.Flight.LogY, g1.Flight.Facing);
        Run(world, 10, _ => ShipInput.Thrust | ShipInput.Left);
        Assert.Equal(at, (g1.Flight.LogX, g1.Flight.LogY, g1.Flight.Facing));
        Assert.True(g1.Flight.Velocity.IsZero);
        // Nor joins a battle.
        var next = world.StartBattle("host", Ilwrath);
        Assert.Empty(world.MarkersFor("g1"));
        Assert.Equal(new[] { next }, world.MarkersFor("g2"));
        FlyIn(world, "g1");
        Assert.Null(next.FighterOf("g1"));
    }

    [Fact]
    public void RetreatingLeavesWithItsCrewForFiveUnitsOfFuel()
    {
        var world = NewWorld(2);
        var guest = world.Find("g1")!.Ship;
        guest.Flight.Fuel = 300;
        var wb = Started(world);
        wb.Battle.DeltaCrew(Fighter(wb, "g1").Starship.Ship!, -3);
        RunDisarmed(world, wb, 1, id => id == "g1" ? ShipInput.TakeOff : ShipInput.None);
        Assert.True(wb.Battle.RunningAway(Fighter(wb, "g1").Starship));
        for (int i = 0; i < 400 && guest.Battle is not null; ++i)
            RunDisarmed(world, wb, 1);
        var f = Fighter(wb, "g1");
        Assert.Equal((BattleExit.Retreated, 7), (f.Exit, f.CrewLeft));
        Assert.Equal((Activity.Hyperspace, 7, 0u), (guest.Activity, guest.Hold.Crew, guest.Flight.Fuel));   // 3 units, down to 0
        Assert.True(wb.Battle.InBattle, "the host fights on");
        Assert.Contains("g1", wb.Left);

        // It can't join again.
        wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;
        FlyIn(world, "g1");
        Assert.Equal(BattleExit.Retreated, Fighter(wb, "g1").Exit);
        Assert.Equal(2, wb.Fighters.Count);

        // The host retreating too leaves no one in it: lost, the enemy left as it is.
        uint fuel = world.Host.Ship.Flight.Fuel;
        int crew = Fighter(wb, "host").Starship.RaceDesc!.CrewLevel - 1;
        RunDisarmed(world, wb, 1, id => id == "host" ? ShipInput.TakeOff : ShipInput.None);
        RunDisarmed(world, wb, 600);
        Assert.Equal((BattleOutcome.Lost, BattleExit.Retreated, crew), (wb.Outcome, Fighter(wb, "host").Exit, Fighter(wb, "host").CrewLeft));
        Assert.Equal(fuel - GameWorld.RetreatFuelCost, world.Host.Ship.Flight.Fuel);
        Assert.True(wb.Enemy.CrewLevel > 0);
        Assert.Equal(Activity.Hyperspace, world.Host.Ship.Activity);
        Assert.False(world.GameOver);
    }

    [Fact]
    public void RetreatIsAllowedOnlyAfterTheStarbaseAndNotWithTheBomb()
    {
        bool Ran(GameWorld world, WorldBattle wb, string id)
        {
            Run(world, 1, p => p == id ? ShipInput.TakeOff : ShipInput.None);
            return wb.Battle.RunningAway(Fighter(wb, id).Starship);
        }

        // Before the starbase is available, no one runs.
        var world = NewWorld(2);
        world.Story = StoryState.NewGame(Rules, world.Clock);
        var wb = Started(world);
        Assert.False(Ran(world, wb, "host"));
        Assert.False(Ran(world, wb, "g1"));

        // After, everyone may but the flagship carrying the bomb (UQM's
        // BOMB_CARRIER): only that ship can't.
        world = NewWorld(2);
        world.Story = StoryState.NewGame(Rules, world.Clock);
        world.Story.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("CHMMR_BOMB_STATE", 3);
        wb = Started(world);
        Assert.True(Fighter(wb, "host").Code.BombCarrier);
        Assert.False(Ran(world, wb, "host"));
        Assert.True(Ran(world, wb, "g1"));

        // A battle that allows none (the Earth fight).
        world = NewWorld(2);
        wb = Started(world, new BattleStart(RetreatAllowed: false));
        Assert.False(Ran(world, wb, "g1"));
    }

    [Fact]
    public void ATeammateRunningAwayWhenTheLastFighterDiesHasRetreated()
    {
        var world = NewWorld(2);
        var wb = Started(world);
        uint fuel = world.Find("g1")!.Ship.Flight.Fuel;
        Run(world, 1, id => id == "g1" ? ShipInput.TakeOff : ShipInput.None);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        var f = Fighter(wb, "g1");
        Assert.Equal(BattleExit.Retreated, f.Exit);
        Assert.InRange(f.CrewLeft, 1, 10);
        Assert.Equal(f.CrewLeft, world.Find("g1")!.Ship.Hold.Crew);
        Assert.Equal(fuel - GameWorld.RetreatFuelCost, world.Find("g1")!.Ship.Flight.Fuel);
        Assert.False(world.Find("g1")!.Ship.Destroyed);
        Assert.True(world.GameOver);   // the flagship
    }

    [Fact]
    public void DisconnectingLeavesAtOnceAsARetreat()
    {
        // Even where a retreat isn't allowed (decision 10).
        var world = NewWorld(3);
        world.Story = StoryState.NewGame(Rules, world.Clock);
        var wb = Started(world);
        var g1 = world.Find("g1")!;
        world.Leave("g1");
        Assert.Empty(world.MarkersFor("g1"));   // away
        var f = Fighter(wb, "g1");
        Assert.Equal(BattleExit.Disconnected, f.Exit);
        Assert.InRange(f.CrewLeft, 1, 10);
        Assert.Equal((false, Activity.Hyperspace, f.CrewLeft), (g1.Present, g1.Ship.Activity, g1.Ship.Hold.Crew));
        Assert.Equal(f.JoinedFuel - GameWorld.RetreatFuelCost, g1.Ship.Flight.Fuel);
        Assert.Null(f.Starship.Ship);
        Assert.Equal(2, wb.Battle.BattleCounter[0]);
        Assert.True(wb.Battle.InBattle);

        // Back in the game, it can't join that battle again.
        Assert.Equal(JoinResult.Rejoined, world.Join("g1", "Guest 1"));
        FlyIn(world, "g1");
        Assert.Equal(BattleExit.Disconnected, Fighter(wb, "g1").Exit);
        Assert.Equal(Activity.Hyperspace, g1.Ship.Activity);

        // A destroyed ship's player disconnecting stays destroyed.
        Kill(wb.Battle, Fighter(wb, "g2").Starship);
        Run(world, 1);
        world.Leave("g2");
        Assert.Equal((BattleExit.Destroyed, Activity.Battle), (Fighter(wb, "g2").Exit, world.Find("g2")!.Ship.Activity));

        // The last one disconnecting leaves no one in it: it ends, lost.
        world.Leave("host");
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Empty(world.Battles);
        Assert.True(world.Find("g2")!.Ship.Destroyed);
        Assert.False(world.GameOver);
    }

    [Fact]
    public void DisconnectingWhileWarpingInLeaves()
    {
        var world = NewWorld(2);
        var wb = world.StartBattle("host", Ilwrath);
        FlyIn(world, "g1");
        var f = Fighter(wb, "g1");
        var element = f.Starship.Ship!;
        Assert.False(Warped(wb.Battle), "still warping in");
        world.Leave("g1");
        Assert.Equal((BattleExit.Disconnected, 10), (f.Exit, f.CrewLeft));
        Run(world, 60);
        Assert.True(wb.Battle.InBattle);
        Assert.DoesNotContain(wb.Battle.List.Elements, e => e == element && (e.StateFlags & ElementFlags.PlayerShip) != 0);
        Assert.Equal(1, wb.Battle.BattleCounter[0]);
    }

    [Fact]
    public void ABattleDecidedCantBeJoined()
    {
        var world = NewWorld(2);
        var wb = world.StartBattle("host", Ilwrath);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, wb.Enemy);
        Run(world, 1);
        Assert.Equal(0, wb.Battle.BattleCounter[1]);
        FlyIn(world, "g1");
        Assert.Null(wb.FighterOf("g1"));
        RunOut(world, wb);
        Assert.Equal(new uint[] { 125 }, wb.Fighters.Select(f => f.Salvage));
        Assert.Equal(0u, world.Find("g1")!.Ship.Hold.ResourceUnits);
    }

    [Fact]
    public void ASaveDuringABattleKeepsTheShipAsItJoined()
    {
        var world = NewWorld(2);
        var wb = Started(world);
        wb.Battle.DeltaCrew(Fighter(wb, "host").Starship.Ship!, -20);
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Run(world, 1);
        // The ships' holds and fuel changed meanwhile, as nothing in 4a changes them: saved as they joined.
        world.Host.Ship.Hold.SetCrew(3);
        world.Host.Ship.Hold.MoveCargo(new[] { 2, 0, 0, 0, 0, 0, 0, 0 }, 0, +1);
        world.Host.Ship.Flight.Fuel = 7;
        world.Find("g1")!.Ship.Flight.Fuel = 9;
        var loaded = HostSave.FromJson(HostSave.ToJson(world));
        Assert.Empty(loaded.Battles);
        Assert.Equal((50, 1000u, 0), (loaded.Host.Ship.Hold.Crew, loaded.Host.Ship.Flight.Fuel, loaded.Host.Ship.Hold.Minerals[0]));
        Assert.Equal((world.Host.Ship.Flight.LogX, world.Host.Ship.Flight.LogY),
            (loaded.Host.Ship.Flight.LogX, loaded.Host.Ship.Flight.LogY));
        // The guest destroyed in it is saved as the wreck it will be (item 7), its fuel as it joined.
        Assert.Equal((0, Fighter(wb, "g1").JoinedFuel, true),
            (loaded.Find("g1")!.Ship.Hold.Crew, loaded.Find("g1")!.Ship.Flight.Fuel, loaded.Find("g1")!.Ship.Destroyed));
        Assert.All(loaded.Players, p => Assert.Equal(Activity.Hyperspace, p.Ship.Activity));

        // Nothing goes in or out of a hold meanwhile.
        Assert.Equal(TradeRefusal.InBattle, world.Give(new Gift("host", "g1", 1, new int[ShipHold.Categories], 0)));
        Assert.False(world.DiscardCargo("host", 0));
        Assert.Equal(2, world.Host.Ship.Hold.Minerals[0]);
    }

    [Fact]
    public void ASnapshotSaysWhoIsInABattle()
    {
        var world = NewWorld(2);
        Started(world);
        var w = new Net.WireWriter(16);
        Net.SnapshotCodec.Encode(w, world.Snapshot());
        var got = Net.SnapshotCodec.Decode(w.Written);
        Assert.All(got.Ships, s => Assert.Equal(Activity.Battle, s.Activity));
    }

    [Fact]
    public void TheBattleInputIsThePlayersKeys()
    {
        var world = NewWorld(1);
        var wb = Started(world);
        var host = Fighter(wb, "host").Starship;
        Run(world, 1, _ => ShipInput.Left | ShipInput.Thrust | ShipInput.Fire | ShipInput.Special);
        Assert.Equal(StatusFlags.Left | StatusFlags.Thrust | StatusFlags.Weapon | StatusFlags.Special,
            host.ShipInputState & StatusFlags.Keys);
        Run(world, 1, _ => ShipInput.Right | ShipInput.Action);
        Assert.Equal(StatusFlags.Right, host.ShipInputState & StatusFlags.Keys);
    }

    [Fact]
    public void OverlappingMarkersAreJoinedOneAtATime()
    {
        // Two battles started in one frame at one place: each seeded apart,
        // and a ship flying in joins one of them only (the first).
        var world = NewWorld(3);
        var first = world.StartBattle("host", Ilwrath);
        var second = world.StartBattle("g1", Ilwrath);
        Assert.Equal(first.Place, second.Place);
        Assert.Equal(first.StartFrame, second.StartFrame);
        Assert.NotEqual(first.Battle.Random.Seed, second.Battle.Random.Seed);
        FlyIn(world, "g2");
        Assert.Equal((true, false), (first.FighterOf("g2") is not null, second.FighterOf("g2") is not null));
        Assert.Equal(1, second.Battle.BattleCounter[0]);
        // Flying out of both and back, it still can't join the other: it's in a battle.
        FlyIn(world, "g2");
        Assert.Null(second.FighterOf("g2"));
    }

    [Fact]
    public void AHyperspaceBattlesPlanetIsRandom()
    {
        var planets = new HashSet<byte>();
        for (int frames = 0; frames < 12; ++frames)
        {
            var world = NewWorld();
            Run(world, frames);
            var wb = world.StartBattle("host", Ilwrath);
            Assert.InRange(wb.BattlePlanet, 0, WorldTypes.NumberOfPlanetTypes - 1);
            planets.Add(wb.BattlePlanet);
        }
        Assert.True(planets.Count > 3, $"planets {string.Join(", ", planets)}");

        // BuildBattle's (COUNT)TFB_Random () % NUMBER_OF_PLANET_TYPES, from the
        // battle's own generator: the first battle of frame 0 is seeded 7919.
        var first = NewWorld().StartBattle("host", Ilwrath);
        uint draw = new Uqm.TfbRandom(~7919u).Next();
        Assert.NotEqual(draw % WorldTypes.NumberOfPlanetTypes, (uint)(ushort)draw % WorldTypes.NumberOfPlanetTypes);
        Assert.Equal((ushort)draw % WorldTypes.NumberOfPlanetTypes, first.BattlePlanet);
    }

    [Fact]
    public void GiftsToOrFromAShipInABattleAreRefused()
    {
        // The host and g1 in a battle, g2 and g3 out of it.
        var world = NewWorld(4);
        foreach (var p in world.Players)
            p.Ship.Hold.ResourceUnits = 10;
        var wb = world.StartBattle("host", Ilwrath);
        FlyIn(world, "g1");
        Assert.Equal(2, wb.Fighters.Count);
        Gift Units(string from, string to) => new(from, to, 1, new int[ShipHold.Categories], 0);
        Assert.Equal(TradeRefusal.InBattle, world.Give(Units("g1", "g2")));   // from one in a battle
        Assert.Equal(TradeRefusal.InBattle, world.Give(Units("g2", "g1")));   // to one
        Assert.Equal(TradeRefusal.InBattle, world.Give(Units("host", "g1")));
        Assert.Equal(TradeRefusal.None, world.Give(Units("g2", "g3")));       // between two out of it
        Assert.Equal(new uint[] { 10, 10, 9, 11 }, world.Players.Select(p => p.Ship.Hold.ResourceUnits));
    }

    // ------------------------------------------------------------ with the content

    internal const int Sol = 82, Earth = 2, Luna = 1;

    internal static readonly Lazy<SystemCatalog> Systems = new(() => SystemCatalog.Load(DataDir));

    internal static GameWorld InSol(int players = 1)
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host",
            systems, TestContent.Shapes);
        world.BattleKit = Kit();
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        for (int i = 1; i < players; ++i)
        {
            Assert.Equal(JoinResult.Joined, world.Join($"g{i}", $"Guest {i}"));
            world.UndockShown($"g{i}");
        }
        return world;
    }

    [ContentFact]
    public void InASystemTheMarkerIsInItsPartAndTheEscapeCounterFollows()
    {
        var world = InSol(2);
        var host = world.Host.Ship;
        var guest = world.Find("g1")!.Ship;
        // The guest flies off a little (touching nothing on its way) ...
        guest.Ip!.EscapeCounter = 250;
        Run(world, 12, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        var wb = world.StartBattle("host", Ilwrath);
        var ip = host.Ip!;
        Assert.Equal(new BattlePlace(Sol, Earth, ip.Location.X, ip.Location.Y), wb.Place);
        Assert.Equal(world.Systems![Sol].Planets[Earth].DataIndex, wb.BattlePlanet);
        Assert.Equal(new[] { wb }, world.MarkersFor("g1"));

        // ... and back into the marker, in Earth's inner system: it joins.
        int back = (guest.Ip!.Facing + 8) % 16;
        for (int i = 0; i < 300 && guest.Ip!.Facing != back; ++i)
            Run(world, 1, id => id == "g1" ? ShipInput.Left : ShipInput.None);
        for (int i = 0; i < 400 && guest.Battle is null; ++i)
            Run(world, 1, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        Assert.Equal(Activity.Battle, guest.Activity);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            Run(world, 1);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);

        // Back in flight, each can't touch anything for 110 frames of flight.
        foreach (var p in world.Players)
            Assert.Equal((Activity.InnerSystem, IpFlight.EscapeFrames), (p.Ship.Activity, p.Ship.Ip!.EscapeCounter));
        // UQM's ESCAPE_COUNTER, 110 frames of flight; 24 world frames are
        // 30 of flight, and the counter goes down with each: 88 world frames
        // are its 110.
        Assert.Equal(110, IpFlight.EscapeFrames);
        Run(world, 24);
        Assert.Equal(110 - 30, host.Ip!.EscapeCounter);
        Run(world, 87 - 24);
        Assert.InRange(host.Ip!.EscapeCounter, 1, 2);
        Run(world, 1);
        Assert.Equal(0, host.Ip!.EscapeCounter);
    }

    [ContentFact]
    public void InTheOuterSystemTheBattlesPlanetIsTheClosest()
    {
        // The guest flies out of Earth's inner system: the host's battle
        // there is in another part, not shown to it.
        var world = InSol(2);
        var guest = world.Find("g1")!.Ship;
        var wb = world.StartBattle("host", Ilwrath);
        for (int i = 0; i < 2000 && guest.Activity != Activity.OuterSystem; ++i)
            Run(world, 1, id => id == "g1" ? ShipInput.Thrust : ShipInput.None);
        Assert.Equal(Activity.OuterSystem, guest.Activity);
        Assert.Empty(world.MarkersFor("g1"));
        // Its battle there is by the planet closest on its screen: Earth, just left.
        var other = world.StartBattle("g1", Ilwrath);
        Assert.Equal(new BattlePlace(Sol, null, guest.Ip!.Location.X, guest.Ip.Location.Y), other.Place);
        Assert.Equal(world.Systems![Sol].Planets[Earth].DataIndex, other.BattlePlanet);
        Assert.Single(wb.Fighters);
        // The marker's reach is measured on the outer view at the ship's zoom (zoomed in here).
        Assert.True(guest.Ip.ZoomRadius < IpGeometry.MaxZoomRadius);
        Assert.True(world.InReach(other.Place, guest));
        // ip units a pixel at this zoom
        int unit = IpGeometry.DisplayToLocation(new IpPoint((short)(IpGeometry.ScreenWidth / 2 + 100), 0), guest.Ip.ZoomRadius).X / 100;
        Assert.True(world.InReach(other.Place with { X = other.Place.X + 7 * unit }, guest));
        Assert.False(world.InReach(other.Place with { X = other.Place.X + 10 * unit }, guest));
    }

    [ContentFact]
    public void APlayerInABattleStartsNoTalkAndIsntPutInOrbit()
    {
        var world = InSol(2);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.CommanderMet = true;
        world.StartBattle("host", Ilwrath);
        // The allied starbase: a guest touching it docks; the host in a battle doesn't.
        world.VisitStarbase(world.Find("g1")!);
        Assert.Equal(Activity.Starbase, world.Find("g1")!.Ship.Activity);
        world.VisitStarbase(world.Host);
        Assert.Null(world.Host.Ship.Visit);
        Assert.Equal(Activity.Battle, world.Host.Ship.Activity);
        Assert.Throws<InvalidOperationException>(() => world.PutInOrbit("host", Sol, Earth, Luna));
        Assert.Equal(Activity.Battle, world.Host.Ship.Activity);
        Assert.Null(world.Host.Ship.Ip!.Orbit);
    }

    [ContentFact]
    public void TheOuterSystemsBattlePlanetIsTheOneClosestOnTheScreen()
    {
        // From Earth's inner system out and on towards the sun, touching
        // nothing, until another planet's image is closest: the battle's planet is that one's.
        var world = InSol();
        var host = world.Host.Ship;
        var planets = world.Systems![Sol].Planets;
        int Closest()
        {
            var ip = host.Ip!;
            return Enumerable.Range(0, planets.Count).MinBy(p =>
            {
                var at = SystemLayout.OuterPlanet(planets[p], ip.ZoomRadius);
                return (long)(ip.Screen.X - at.X) * (ip.Screen.X - at.X) + (long)(ip.Screen.Y - at.Y) * (ip.Screen.Y - at.Y);
            });
        }
        for (int i = 0; i < 2000 && host.Activity != Activity.OuterSystem; ++i)
        {
            host.Ip!.EscapeCounter = 250;
            Run(world, 1, _ => ShipInput.Thrust);
        }
        Assert.Equal(Activity.OuterSystem, host.Activity);
        int sun = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan(
            (short)(SystemLayout.CenterX - host.Ip!.Screen.X), (short)(SystemLayout.CenterY - host.Ip.Screen.Y)));
        for (int i = 0; i < 2000 && (host.Ip!.Facing != sun || Closest() == Earth) && host.Activity == Activity.OuterSystem; ++i)
        {
            host.Ip.EscapeCounter = 250;
            Run(world, 1, _ => host.Ip!.Facing != sun ? ShipInput.Left : ShipInput.Thrust);
        }
        Assert.Equal(Activity.OuterSystem, host.Activity);
        int closest = Closest();
        Assert.NotEqual(Earth, closest);
        var wb = world.StartBattle("host", Ilwrath);
        Assert.Equal(planets[closest].DataIndex, wb.BattlePlanet);
    }

    [ContentFact]
    public void TheHostsStepTakesEndedBattlesAndNotices()
    {
        // Until item 11 sends them, they're logged and not kept.
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "host", "Host",
            Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        var log = new List<string>();
        host.Logged += log.Add;
        host.StartBattle(Ilwrath);
        Assert.Equal(JoinResult.Joined, host.World.Join("g1", "Guest 1"));
        var wb = host.World.Battles.Single();
        var guest = host.World.Find("g1")!.Ship;
        // The guest, flying off and back into it with the list full, is told so.
        var taken = new List<Element>();
        while (wb.Battle.List.Alloc() is { } e)
            taken.Add(e);
        void Fly(ShipInput keys)
        {
            host.World.SubmitInput("g1", host.World.Frame + 1, keys);
            host.Step(ShipInput.None);
        }
        guest.Ip!.EscapeCounter = 250;
        for (int i = 0; i < 12; ++i)
            Fly(ShipInput.Thrust);
        int back = (guest.Ip.Facing + 8) % 16;
        for (int i = 0; i < 300 && guest.Ip.Facing != back; ++i)
            Fly(ShipInput.Left);
        for (int i = 0; i < 400 && !log.Any(l => l.Contains("too crowded")); ++i)
            Fly(ShipInput.Thrust);
        Assert.Contains(log, l => l == "g1: The battle is too crowded to join just now.");
        Assert.Empty(host.World.TakeBattleNotices());
        foreach (var e in taken)
            wb.Battle.List.Free(e);
        Kill(wb.Battle, wb.Enemy);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            host.Step(ShipInput.None);
        Assert.Contains(log, l => l.StartsWith($"Battle {wb.Id} against the ilwrath won: host fought"));
        Assert.Empty(host.World.TakeEndedBattles());
    }

    [ContentFact]
    public void NoSaveIsMadeOnceTheGameIsOver()
    {
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "host", "Host",
            Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        host.StartBattle(Ilwrath);
        var wb = host.World.Battles.Single();
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            host.Step(ShipInput.None);
        Assert.NotNull(host.SaveJson());
        Kill(wb.Battle, wb.FighterOf("host")!.Starship);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            host.Step(ShipInput.None);
        Assert.True(host.World.GameOver);
        Assert.Null(host.SaveJson());
    }

    [ContentFact]
    public void TheEscapeCounterKeepsTheShipFromTheWorlds()
    {
        // From Luna, thrusting one way: without the counter the ship touches
        // a world on its way (an orbit, or an inner system); with it, none
        // (where UQM's counter is for the ships in a system only).
        bool Touched(int facing, byte counter)
        {
            var ip = InSol().Host.Ship.Ip!;
            for (int i = 0; i < 400 && ip.Facing != facing; ++i)
                Assert.Equal(IpEvent.None, ip.Step(ShipInput.Left));
            ip.EscapeCounter = counter;
            for (int i = 0; i < 200; ++i)
            {
                var e = ip.Step(ShipInput.Thrust);
                if (e is IpEvent.EnteredOrbit or IpEvent.EnteredInnerSystem)
                    return true;
                if (e is IpEvent.LeftSystem or IpEvent.LeftInnerSystem)
                    break;
            }
            Assert.Equal(counter > 200 ? counter - 200 : 0, ip.EscapeCounter);
            return false;
        }
        int way = Enumerable.Range(0, Uqm.UqmMath.Facings).First(f => Touched(f, 0));
        Assert.False(Touched(way, 250));
    }

    [ContentFact]
    public void TheIlwrathsTalkGivesItsStarterTheEscapeCounter()
    {
        var world = InSol();
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        var story = world.Story!;
        story.Set("RADIOACTIVES_PROVIDED", 1);
        story.Set("MOONBASE_DESTROYED", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        world.CommanderMet = true;
        world.VisitStarbase(world.Host);
        var visit = world.Visit!;
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
        var talk = visit.Talk!;
        int n = ConversationScripts.Load(DataDir)["comandr"].PhraseNumber("base_was_abandoned");
        Assert.True(world.ChooseReply("host", talk.Replies.Select(r => r.Phrase).ToList().IndexOf(n)));
        Assert.Equal(VisitStep.Ilwrath, visit.Step);
        Assert.Equal(IpFlight.EscapeFrames, world.Host.Ship.Ip!.EscapeCounter);
        // Taken back, the visit leaves none.
        world.Leave("host");
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
    }

    [ContentFact]
    public void AStoryTalkFreezesEveryBattle()
    {
        var world = InSol(2);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        var wb = world.StartBattle("host", Ilwrath);
        Run(world, 30);
        // The guest at the starbase with the Lunar base aboard: the starbase's story talk.
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Find("g1")!);
        Assert.True(world.StoryPaused);
        string Places() => string.Join(" ", wb.Battle.List.Elements.Select(e => $"{e.Current.X},{e.Current.Y},{e.LifeSpan}"));
        uint seed = wb.Battle.Random.Seed;
        string at = Places();
        Run(world, 40, _ => ShipInput.Fire | ShipInput.Thrust);
        Assert.Equal((seed, at), (wb.Battle.Random.Seed, Places()));
        // A player in a battle starts no story talk.
        world.VisitStarbase(world.Host);
        Assert.Null(world.Host.Ship.Visit);
        // The battle goes on once it's over.
        Run(world, StarbaseVisit.TimePassingFrames);
        var talk = world.Visit!.Talk!;
        int n = ConversationScripts.Load(DataDir)["starbas"].PhraseNumber("goodbye_commander");
        Assert.True(world.ChooseReply("g1", talk.Replies.Select(r => r.Phrase).ToList().IndexOf(n)));
        Assert.False(world.StoryPaused);
        Assert.Equal((seed, at), (wb.Battle.Random.Seed, Places()));
        Run(world, 1);
        Assert.NotEqual(at, Places());
    }

    [ContentFact]
    public void StartBattleStartsAfterTheOpeningByEarth()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host",
            systems, TestContent.Shapes);
        host.World.BattleKit = BattleReplay.Kit.Value;
        Assert.Throws<ArgumentException>(() => host.StartBattle("vux"));
        Assert.Contains("ilwrath", host.StartBattle("ilwrath"));
        var wb = Assert.Single(host.World.Battles);
        Assert.Equal((Sol, Earth), (wb.Place.Star, wb.Place.InnerPlanet));
        Assert.Equal(1, host.World.Story!.Get("STARBASE_AVAILABLE"));
        Assert.Equal(Activity.Battle, host.World.Host.Ship.Activity);
        // The content's sheets fight: the flagship's gun and the Ilwrath's hellfire.
        for (int i = 0; i < 60; ++i)
            host.Step(ShipInput.Fire);
        Assert.Contains(wb.Battle.List.Elements, e => e.Parent == wb.FighterOf("host")!.Starship && e != wb.FighterOf("host")!.Starship.Ship);
        // Escape retreats.
        for (int i = 0; i < 400 && host.World.Host.Ship.Battle is not null; ++i)
            host.Step(ShipInput.TakeOff);
        Assert.Equal(BattleExit.Retreated, wb.FighterOf("host")!.Exit);
        Assert.Equal(Activity.InnerSystem, host.World.Host.Ship.Activity);
    }
}
