// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The host's fleet ships in battle (stage 4b item 8): the pick screen's
/// cursor (pickship.c's DoPickBattleShip), the battle's armada (race_q[0]:
/// the fleet ships and the flagship, picked one at a time, the flagship
/// last), the world's pick (the host choosing on joining and after each
/// death or retreat; the battle waiting only while nobody else fights,
/// decision 2), a fleet ship flown as the host's ship, its death (gone,
/// decision 4) and retreat (out, its crew kept), the flagship's retreat's
/// fuel and death, one share of the salvage (decision 5), saves during a
/// battle, and --start-battle with --start-fleet. In a world of hyperspace
/// only, on sheets of our own (WorldBattleTests'), but for --start-battle.
/// </summary>
[Trait("Topic", "fleet")]
public class FleetBattleTests
{
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private const string Spathi = "SPATHI_SHIP", Arilou = "ARILOU_SHIP", Syreen = "SYREEN_SHIP";

    // ------------------------------------------------------------ the pick screen's cursor

    // DoPickBattleShip's ship_index for each place of the cursor: row * 6 + the column, less one past the flagship's.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 4, 3)]
    [InlineData(0, 6, 5)]
    [InlineData(1, 0, 6)]
    [InlineData(1, 2, 8)]
    [InlineData(1, 4, 9)]
    [InlineData(1, 6, 11)]
    public void EachPlaceOfTheCursorIsAHangarSlot(int row, int column, int slot)
    {
        var menu = new PickShipMenu();
        if (row == 1)
            menu.Press(MenuKey.Down, _ => false);
        for (int i = PickShipMenu.FlagshipColumn; i > column; --i)
            menu.Press(MenuKey.Left, _ => false);
        for (int i = PickShipMenu.FlagshipColumn; i < column; ++i)
            menu.Press(MenuKey.Right, _ => false);
        Assert.Equal((row, column), (menu.Row, menu.Column));
        Assert.Equal(slot, menu.Slot);
        Assert.False(menu.OnFlagship);
    }

    [Fact]
    public void TheCursorStartsOnTheFlagshipAndWrapsBothWays()
    {
        var menu = new PickShipMenu();
        Assert.Equal((0, 3), (menu.Row, menu.Column));
        Assert.True(menu.OnFlagship);
        Assert.Null(menu.Slot);
        // On the flagship's column, up and down change the row all the same (it's kept for the columns either side).
        Assert.Equal(PickOutcome.Moved, menu.Press(MenuKey.Up, _ => false));
        Assert.Equal((1, 3), (menu.Row, menu.Column));
        Assert.Null(menu.Slot);
        Assert.Equal(PickOutcome.Moved, menu.Press(MenuKey.Down, _ => false));
        Assert.Equal(0, menu.Row);
        menu.Press(MenuKey.Down, _ => false);
        Assert.Equal(PickOutcome.Moved, menu.Press(MenuKey.Left, _ => false));
        Assert.Equal(8, menu.Slot);   // row 1 kept
        // Left from the first column to the last, right from the last to the first.
        menu.Press(MenuKey.Left, _ => false);
        menu.Press(MenuKey.Left, _ => false);
        Assert.Equal(0, menu.Column);
        menu.Press(MenuKey.Left, _ => false);
        Assert.Equal(PickShipMenu.Columns, menu.Column);
        Assert.Equal(11, menu.Slot);
        menu.Press(MenuKey.Right, _ => false);
        Assert.Equal(0, menu.Column);
        Assert.Equal(6, menu.Slot);
        // Up from the first row to the last, down from the last to the first.
        menu.Press(MenuKey.Up, _ => false);
        Assert.Equal(0, menu.Row);
        menu.Press(MenuKey.Up, _ => false);
        Assert.Equal(1, menu.Row);
    }

    [Fact]
    public void SelectPicksOnlyWhatCanBePickedAndOtherKeysDoNothing()
    {
        var menu = new PickShipMenu();
        var asked = new List<int?>();
        Assert.Equal(PickOutcome.Chosen, menu.Press(MenuKey.Select, s => { asked.Add(s); return true; }));
        Assert.Equal(PickOutcome.None, menu.Press(MenuKey.Select, s => { asked.Add(s); return false; }));
        menu.Press(MenuKey.Left, _ => false);
        Assert.Equal(PickOutcome.Chosen, menu.Press(MenuKey.Select, s => { asked.Add(s); return s == 2; }));
        Assert.Equal(new int?[] { null, null, 2 }, asked);
        foreach (var key in new[] { MenuKey.Cancel, MenuKey.PageUp, MenuKey.PageDown })
        {
            Assert.Equal(PickOutcome.None, menu.Press(key, _ => true));
            Assert.Equal((0, 2), (menu.Row, menu.Column));
        }
    }

    // ------------------------------------------------------------ the battle's armada

    private static readonly BattleKit TheKit = Kit();

    // A race ship for a side, on our sheets.
    private static Starship Ship(string id, short side, int crew)
    {
        var d = TheKit.Ships[id];
        return new Starship(RaceShips.Code(d, TheKit.Graphics.Views), side, (ushort)crew, (ushort)d.MaxCrew) { CaptainsNameIndex = 5 };
    }

    private static Battle NewBattle(IReadOnlyList<Starship> side0, Starship enemy) =>
        new(TheKit.Graphics.Arena(0), new[] { side0, new[] { enemy } }, 1);

    // Frames with no keys (the enemy unflown) until done, the battle over, or so many.
    private static void RunUntil(Battle battle, Func<bool> done, int frames)
    {
        for (int i = 0; i < frames && battle.InBattle && !done(); ++i)
            battle.Step(_ => BattleInput.None);
    }

    [Fact]
    public void AnArmadaIsCountedWholeAndPicksItsShipsTheFlagshipLast()
    {
        var escort1 = Ship("spathi", 0, 5);
        var escort2 = Ship("spathi", 0, 6);
        var flagship = Ship("spathi", 0, 7);
        var armada = new Armada(new[] { escort1, escort2 }, flagship);
        Assert.Same(armada, flagship.Armada);
        Assert.Throws<ArgumentException>(() => new Armada(new[] { escort1 }, Ship("spathi", 0, 5)));   // in one already
        var battle = NewBattle(armada.Ships, Ship(Ilwrath, 1, 10));
        // BuildBattle's race_q[0] counted (CountLinks), none in yet: its player picks the first.
        Assert.Equal(3, battle.BattleCounter[0]);
        Assert.True(armada.Picking);
        Assert.Null(armada.Flying);
        Assert.All(armada.Ships, s => Assert.Null(s.Ship));
        Assert.Throws<ArgumentException>(() => battle.Join(escort1));   // an armada's ship is picked, not joined

        Assert.True(battle.Pick(escort2));
        Assert.False(armada.Picking);
        Assert.Same(escort2, armada.Flying);
        Assert.Equal((ushort)6, escort2.RaceDesc!.CrewLevel);
        Assert.Throws<ArgumentException>(() => battle.Pick(escort1));   // not picking now

        // The escort dies: uncounted, and, freed, its player picks again.
        RunUntil(battle, () => false, 40);
        battle.DoDamage(escort2.Ship!, 6);
        RunUntil(battle, () => false, 2);   // RecordShipDeath, as its death starts
        Assert.Equal(2, battle.BattleCounter[0]);
        Assert.Equal(new[] { escort1, flagship }, armada.Counted);
        RunUntil(battle, () => armada.Picking, 300);
        Assert.True(armada.Picking);
        Assert.True(battle.InBattle);
        Assert.Null(escort2.Ship);
        Assert.False(escort2.Available);
        Assert.False(armada.CanPick(escort2));

        // The flagship picked: the last choice (battle_counter = 1).
        Assert.True(battle.Pick(flagship));
        Assert.True(armada.FlagshipChosen);
        Assert.Equal(1, battle.BattleCounter[0]);
        Assert.Equal(new[] { flagship }, armada.Counted);
        Assert.False(armada.CanPick(escort1));
        // Its death ends the battle: no pick (the player is out of ships).
        RunUntil(battle, () => false, 40);
        battle.DoDamage(flagship.Ship!, 7);
        RunUntil(battle, () => !battle.InBattle, 300);
        Assert.False(battle.InBattle);
        Assert.False(armada.Picking);
    }

    [Fact]
    public void NoPickOnceTheEnemyHasNoShipLeft()
    {
        var escort = Ship("spathi", 0, 5);
        var flagship = Ship("spathi", 0, 7);
        var armada = new Armada(new[] { escort }, flagship);
        var enemy = Ship(Ilwrath, 1, 10);
        var battle = NewBattle(armada.Ships, enemy);
        Assert.True(battle.Pick(escort));
        RunUntil(battle, () => false, 40);
        // Both die together: GetArmadaStarShip returns at once (no opponents left), and the battle is over, the armada still counted.
        battle.DoDamage(enemy.Ship!, 10);
        battle.DoDamage(escort.Ship!, 5);
        RunUntil(battle, () => !battle.InBattle, 300);
        Assert.False(battle.InBattle);
        Assert.False(armada.Picking);
        Assert.Equal(1, battle.BattleCounter[0]);   // the flagship: VictoryState's win
        Assert.Equal(0, battle.BattleCounter[1]);
    }

    [Fact]
    public void AnArmadaLeavingTakesItsShipFlownOutWithItsCrew()
    {
        var escort = Ship("spathi", 0, 5);
        var flagship = Ship("spathi", 0, 7);
        var armada = new Armada(new[] { escort }, flagship);
        var battle = NewBattle(new[] { Ship("spathi", 0, 9) }, Ship(Ilwrath, 1, 10));
        battle.JoinArmada(armada);
        Assert.Equal(3, battle.BattleCounter[0]);
        Assert.True(armada.Picking);
        Assert.Throws<ArgumentException>(() => battle.JoinArmada(armada));
        Assert.True(battle.Pick(escort));
        RunUntil(battle, () => false, 40);
        Assert.Throws<ArgumentException>(() => battle.Leave(escort));   // an armada leaves whole
        battle.LeaveArmada(armada);
        Assert.Equal(1, battle.BattleCounter[0]);   // the guest's
        Assert.Empty(armada.Counted);
        Assert.Null(escort.Ship);
        Assert.Equal((ushort)5, escort.CrewLevel);
        Assert.False(flagship.Available);
        Assert.True(battle.InBattle);
    }

    // ------------------------------------------------------------ in the world

    // A world of hyperspace (WorldBattleTests'), with the story (the fleet's rules; the starbase available, so retreats
    // are allowed), and the host's fleet of these ships, each in the next slot with its crew.
    private static GameWorld WithFleet(int players, params (string Race, int Crew)[] ships)
    {
        var world = NewWorld(players);
        world.Story = StoryState.NewGame(Rules, world.Clock);
        world.Story.Set("STARBASE_AVAILABLE", 1);
        foreach (var (race, crew) in ships)
        {
            world.AddFleetShips(race, 1);
            world.Fleet.SetCrew(Rules, world.Fleet.Ships[^1].Slot, crew);
        }
        return world;
    }

    // The host's pick screen's cursor taken to a slot (null: the flagship), then select.
    private static PickOutcome PickAt(GameWorld world, int? slot)
    {
        var menu = world.Host.Ship.Battle!.FighterOf("host")!.Pick!;
        int row = slot is int s ? s / PickShipMenu.Columns : menu.Row;
        int column = slot is int t ? (t % PickShipMenu.Columns >= PickShipMenu.FlagshipColumn ? t % PickShipMenu.Columns + 1 : t % PickShipMenu.Columns)
            : PickShipMenu.FlagshipColumn;
        while (menu.Row != row)
            Assert.Equal(PickOutcome.Moved, world.PickShipKey("host", MenuKey.Down));
        while (menu.Column != column)
            Assert.Equal(PickOutcome.Moved, world.PickShipKey("host", menu.Column < column ? MenuKey.Right : MenuKey.Left));
        return world.PickShipKey("host", MenuKey.Select);
    }

    // Every element's place: the same from one frame to the next only if the battle didn't step.
    private static List<(short, short)> Places(Battle battle) => battle.List.Elements.Select(e => (e.Current.X, e.Current.Y)).ToList();

    // Frames (the enemy disarmed) until the host's ship flown is in, warped in.
    private static void WarpIn(GameWorld world, WorldBattle wb)
    {
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            RunDisarmed(world, wb, 1);
        Assert.True(Warped(wb.Battle));
    }

    // Frames (the enemy disarmed) until the host is on the pick screen again (or the battle is over).
    private static void UntilPicking(GameWorld world, WorldBattle wb, Func<string, ShipInput>? keys = null)
    {
        for (int i = 0; i < 400 && wb.Outcome is null && wb.FighterOf("host")!.Pick is null; ++i)
            RunDisarmed(world, wb, 1, keys);
        Assert.NotNull(wb.FighterOf("host")!.Pick);
    }

    [Fact]
    public void TheHostWithFleetShipsChoosesTheFirstWhileTheBattleWaits()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        var host = Fighter(wb, "host");
        Assert.Equal(new[] { "spathi", "arilou" }, host.FleetShips.Select(f => f.Ship.Id));
        Assert.NotNull(host.Armada);
        Assert.Equal(host.FleetShips.Select(f => f.Starship).Append(host.OwnStarship), wb.Battle.Fleet(0));
        Assert.Equal((ushort)20, host.FleetShips[0].Starship.CrewLevel);
        Assert.Equal(world.Fleet.Ships[0].Captain, host.FleetShips[0].Starship.CaptainsNameIndex);
        Assert.Equal(3, wb.Battle.BattleCounter[0]);
        Assert.NotNull(host.Pick);
        Assert.True(GameWorld.WaitsForPick(wb));
        // Alone, the battle waits while the host chooses (decision 2): nothing moves.
        var before = Places(wb.Battle);
        Run(world, 10);
        Assert.Equal(before, Places(wb.Battle));
        Assert.Null(wb.Outcome);
        // Choosing, the host watches from where the enemy is, their status the flagship's, not in yet.
        var choosing = world.Snapshot().Battles!.Single().FighterOf("host")!;
        Assert.Equal((wb.Enemy.Ship!.Current.X, wb.Enemy.Ship.Current.Y), (choosing.X, choosing.Y));
        Assert.Equal(ShipDescriptions.Flagship, choosing.Status.Ship);
        Assert.True(choosing.Status.Gone);
        // The cursor starts on the flagship; the Arilou in slot 1 is picked and warps in.
        Assert.Equal(new PickLit(ShipDescriptions.Flagship, 0, world.Host.Ship.Hold.Crew, world.Host.Ship.Hold.CrewCapacity, null), world.PickLitFor("host"));
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 1));
        Assert.Null(host.Pick);
        Assert.Null(world.PickLitFor("host"));
        var arilou = host.FleetShips[1].Starship;
        Assert.Same(arilou, host.Starship);
        Assert.Equal("arilou", host.ShipId);
        Assert.False(host.FlyingOwnShip);
        Assert.Equal((ushort)4, arilou.RaceDesc!.CrewLevel);
        Assert.False(GameWorld.WaitsForPick(wb));
        WarpIn(world, wb);
        Assert.NotEqual(before, Places(wb.Battle));
        // Its ship in the battle: the camera's (the moving frame follows it), the snapshot's status, the keys'.
        Assert.Same(arilou.Ship, wb.Battle.FollowedElement(0));
        var snap = world.Snapshot().Battles!.Single();
        var me = snap.FighterOf("host")!;
        Assert.Equal("arilou", me.Status.Ship);
        Assert.Equal(4, me.Status.Crew);
        Assert.Equal(arilou.CaptainsNameIndex, me.Status.Captain);
        Assert.True(me.Followed);
        Assert.Equal((arilou.Ship!.Current.X, arilou.Ship.Current.Y), (me.X, me.Y));
        var facing = arilou.ShipFacing;
        RunDisarmed(world, wb, 6, _ => ShipInput.Left);
        Assert.NotEqual(facing, arilou.ShipFacing);
        // The enemy's AI sees it as its opponent: the only ship on side 0.
        Assert.Equal(new[] { arilou }, wb.Battle.Fleet(0).Where(s => s.Ship != null));
    }

    [Fact]
    public void WithoutFleetShipsTheFlagshipGoesInAsBeforeAndAGuestHasNoArmada()
    {
        var world = WithFleet(2);
        var wb = Started(world);
        Assert.All(wb.Fighters, f => Assert.Null(f.Armada));
        Assert.All(wb.Fighters, f => Assert.Null(f.Pick));
        Assert.True(Fighter(wb, "host").FlyingOwnShip);

        // With fleet ships, a guest flying in brings none (only the host has a fleet, decision 3).
        world = WithFleet(2, (Spathi, 10));
        var wb2 = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        Assert.Null(Fighter(wb2, "g1").Armada);
        Assert.Empty(Fighter(wb2, "g1").FleetShips);
        Assert.NotNull(Fighter(wb2, "host").Armada);
        // The host flying in picks while the guest fights: the battle runs on (decision 2).
        Assert.NotNull(Fighter(wb2, "host").Pick);
        Assert.False(GameWorld.WaitsForPick(wb2));
        var before = Places(wb2.Battle);
        Run(world, 2);
        Assert.NotEqual(before, Places(wb2.Battle));
    }

    [Fact]
    public void AFleetShipThatDiesIsGoneAndTheHostChoosesAgain()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        var host = Fighter(wb, "host");
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        WarpIn(world, wb);
        var spathi = host.FleetShips[0];
        Kill(wb.Battle, spathi.Starship);
        // Dying, it's still the host's ship (watching it blow up); the flagship isn't lost.
        RunDisarmed(world, wb, 2);
        Assert.Equal(2, wb.Battle.BattleCounter[0]);
        Assert.False(world.FlagshipLost);
        Assert.Null(host.Exit);
        UntilPicking(world, wb);
        Assert.Equal(BattleExit.Destroyed, spathi.Exit);
        Assert.Equal(0, spathi.CrewLeft);
        Assert.Equal(new[] { Arilou }, world.Fleet.Ships.Select(s => s.Race));   // gone at once (decision 4)
        Assert.Null(host.Exit);
        Assert.Same(host.Player.Ship.Battle, wb);
        Assert.True(GameWorld.WaitsForPick(wb));
        // Its slot can't be picked again: select there does nothing.
        Assert.Equal(PickOutcome.None, PickAt(world, 0));
        Assert.Null(world.PickLitFor("host"));
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 1));
        Assert.Same(host.FleetShips[1].Starship, host.Starship);
    }

    [Fact]
    public void AFleetShipThatRetreatsKeepsItsCrewIsOutAndNoFuelIsPaid()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        uint fuel = world.Host.Ship.Flight.Fuel;
        var wb = world.StartBattle("host", Ilwrath);
        var host = Fighter(wb, "host");
        PickAt(world, 0);
        WarpIn(world, wb);
        var spathi = host.FleetShips[0];
        spathi.Starship.RaceDesc!.CrewLevel = 17;
        UntilPicking(world, wb, _ => ShipInput.TakeOff);
        Assert.Equal(BattleExit.Retreated, spathi.Exit);
        Assert.Equal(17, spathi.CrewLeft);
        Assert.Equal(fuel, world.Host.Ship.Flight.Fuel);   // not the host's last ship
        Assert.Null(host.Exit);
        Assert.Equal(20, world.Fleet.Ships[0].Crew);   // written as the host leaves the battle or it ends
        Assert.Equal(PickOutcome.None, PickAt(world, 0));   // out for this battle
        // The flagship picked: the last choice; its retreat is the host's, for 5 units of fuel.
        Assert.Equal(PickOutcome.Chosen, PickAt(world, null));
        Assert.True(host.FlyingOwnShip);
        Assert.Equal(1, wb.Battle.BattleCounter[0]);
        WarpIn(world, wb);
        for (int i = 0; i < 300 && host.Exit is null; ++i)
            RunDisarmed(world, wb, 1, _ => ShipInput.TakeOff);
        Assert.Equal(BattleExit.Retreated, host.Exit);
        Assert.Equal(fuel - GameWorld.RetreatFuelCost, world.Host.Ship.Flight.Fuel);
        Assert.Null(world.Host.Ship.Battle);
        Assert.Equal(new[] { (Spathi, 17), (Arilou, 4) }, world.Fleet.Ships.Select(s => (s.Race, s.Crew)));
        Assert.Null(host.FleetShips[1].Exit);   // never flown
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
    }

    [Fact]
    public void TheFlagshipsDeathIsStillTheGameOver()
    {
        var world = WithFleet(1, (Spathi, 20));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, null);
        WarpIn(world, wb);
        Kill(wb.Battle, Fighter(wb, "host").OwnStarship);
        for (int i = 0; i < 300 && !world.GameOver; ++i)
            RunDisarmed(world, wb, 1);
        Assert.True(world.GameOver);
        Assert.True(world.FlagshipLost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWinPaysTheHostOneShareWhateverTheyFlew(bool picking)
    {
        var world = WithFleet(2, (Syreen, 12), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        FlyIn(world, "g1");
        WarpIn(world, wb);
        var host = Fighter(wb, "host");
        var syreen = host.FleetShips[0];
        if (picking)
        {
            // The host's Syreen dies; while the host chooses, the guest fights on (decision 2) and the enemy dies:
            // won, the pick screen closes, and the host has a share.
            Kill(wb.Battle, syreen.Starship);
            UntilPicking(world, wb);
            Assert.False(GameWorld.WaitsForPick(wb));
            Kill(wb.Battle, wb.Enemy);
            RunDisarmed(world, wb, 1);
            Assert.Null(host.Pick);   // GetArmadaStarShip returns at once once the enemy has none left
            RunOut(world, wb);
        }
        else
        {
            // A Syreen that picked up crew beyond its starting 12 keeps it (up to its 42).
            syreen.Starship.RaceDesc!.CrewLevel = 30;
            Kill(wb.Battle, wb.Enemy);
            RunOut(world, wb);
        }
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Equal(new BattleExit?[] { BattleExit.Fought, BattleExit.Fought }, wb.Fighters.Select(f => f.Exit));
        Assert.Equal(125u, wb.SalvagePaid);
        Assert.Equal(new uint[] { 63, 62 }, wb.Fighters.Select(f => f.Salvage));   // one share each, the remainder to the first
        Assert.Null(world.Host.Ship.Battle);
        if (picking)
            Assert.Equal(new[] { Arilou }, world.Fleet.Ships.Select(s => s.Race));
        else
        {
            Assert.Equal(BattleExit.Fought, syreen.Exit);
            Assert.Equal(new[] { (Syreen, 30), (Arilou, 4) }, world.Fleet.Ships.Select(s => (s.Race, s.Crew)));
        }
    }

    [Fact]
    public void AloneTheEnemysLastShipDyingAsTheFleetShipDiesWinsAndTheHostIsPaid()
    {
        var world = WithFleet(1, (Spathi, 20));
        uint units = world.Host.Ship.Hold.ResourceUnits;
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        WarpIn(world, wb);
        Kill(wb.Battle, wb.Enemy);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        for (int i = 0; i < 400 && wb.Outcome is null; ++i)
            RunDisarmed(world, wb, 1);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);   // the flagship still counted: VictoryState's win
        Assert.Equal(units + 125, world.Host.Ship.Hold.ResourceUnits);
        Assert.Empty(world.Fleet.Ships);
    }

    [Fact]
    public void AGuestFlyingInWhileTheBattleWaitsJoinsOnceTheHostHasChosen()
    {
        var world = WithFleet(2, (Spathi, 20));
        var wb = world.StartBattle("host", Ilwrath);
        FlyIn(world, "g1");
        Assert.Null(wb.FighterOf("g1"));
        Assert.Null(world.Find("g1")!.Ship.Battle);
        Run(world, 3);
        Assert.Null(wb.FighterOf("g1"));   // still waiting by the marker
        PickAt(world, 0);
        Run(world, 1);
        Assert.NotNull(wb.FighterOf("g1"));
        Assert.Same(wb, world.Find("g1")!.Ship.Battle);
        // The host's ship first in the fleet's order: the moving frame follows it.
        WarpIn(world, wb);
        Assert.Same(Fighter(wb, "host").Starship.Ship, wb.Battle.FollowedElement(0));
    }

    [Fact]
    public void ASaveDuringTheBattleHasTheFleetAsItJoinedTheDestroyedGone()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4), (Syreen, 12));
        var wb = world.StartBattle("host", Ilwrath);
        var host = Fighter(wb, "host");
        PickAt(world, 0);
        WarpIn(world, wb);
        host.Starship.RaceDesc!.CrewLevel = 9;
        UntilPicking(world, wb, _ => ShipInput.TakeOff);   // the Spathi retreats with 9
        PickAt(world, 1);
        WarpIn(world, wb);
        Kill(wb.Battle, host.Starship);
        RunDisarmed(world, wb, 1);
        // Dying: gone from the save already.
        Assert.Equal(new[] { (Spathi, 20, 0), (Syreen, 12, 2) }, SavedFleet(world));
        UntilPicking(world, wb);
        Assert.Equal(new[] { (Spathi, 20, 0), (Syreen, 12, 2) }, SavedFleet(world));   // the Spathi as it joined
        Assert.Equal(new[] { Spathi, Syreen }, world.Fleet.Ships.Select(s => s.Race));
    }

    private static List<(string, int, int)> SavedFleet(GameWorld world) =>
        JsonNode.Parse(HostSave.ToJson(world))!["fleet"]!.AsArray()
            .Select(s => (s!["race"]!.GetValue<string>(), s["crew"]!.GetValue<int>(), s["slot"]!.GetValue<int>())).ToList();

    [Fact]
    public void TheHostDisconnectingTakesTheFleetShipOutWithItsCrew()
    {
        var world = WithFleet(2, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        FlyIn(world, "g1");
        WarpIn(world, wb);
        var host = Fighter(wb, "host");
        host.Starship.RaceDesc!.CrewLevel = 11;
        uint fuel = world.Host.Ship.Flight.Fuel;
        int crew = world.Host.Ship.Hold.Crew;
        world.Leave("host");
        Assert.Equal(BattleExit.Disconnected, host.Exit);
        Assert.Equal(crew, host.CrewLeft);
        Assert.Equal(crew, world.Host.Ship.Hold.Crew);
        Assert.Equal(fuel - GameWorld.RetreatFuelCost, world.Host.Ship.Flight.Fuel);
        Assert.Equal(BattleExit.Retreated, host.FleetShips[0].Exit);
        Assert.Equal(new[] { (Spathi, 11), (Arilou, 4) }, world.Fleet.Ships.Select(s => (s.Race, s.Crew)));
        Assert.Equal(1, wb.Battle.BattleCounter[0]);   // the guest's
        Assert.True(wb.Battle.InBattle);
    }

    [Fact]
    public void AShipATalkTookFromTheFleetCantBePicked()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        world.Fleet.Remove(Arilou);   // as a guest's talk might, meanwhile
        Assert.Equal(PickOutcome.None, PickAt(world, 1));
        // Another ship in the Spathi's slot (taken and given again, another captain): not the one that joined.
        var spathi = world.Fleet.Ships[0];
        world.Fleet.Restore(Rules, new[] { spathi with { Captain = (byte)(spathi.Captain == 5 ? 6 : 5) } });
        Assert.Equal(PickOutcome.None, PickAt(world, 0));
        world.Fleet.Restore(Rules, new[] { spathi });
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
    }

    [Fact]
    public void ThePickWaitsForTheWorld()
    {
        var world = WithFleet(1, (Spathi, 20));
        var wb = world.StartBattle("host", Ilwrath);
        Assert.True(world.SetHostPause(true));
        Assert.Equal(PickOutcome.None, world.PickShipKey("host", MenuKey.Select));
        Assert.True(world.SetHostPause(false));
        Assert.Equal(PickOutcome.None, world.PickShipKey("g1", MenuKey.Select));   // no such player
        Assert.Equal(PickOutcome.Chosen, world.PickShipKey("host", MenuKey.Select));
        Assert.True(Fighter(wb, "host").FlyingOwnShip);
        Assert.Equal(PickOutcome.None, world.PickShipKey("host", MenuKey.Select));   // no pick screen now
    }

    [Fact]
    public void TheHostsSessionSendsTheMenuKeysToThePickScreen()
    {
        var world = WithFleet(1, (Spathi, 20));
        var wb = world.StartBattle("host", Ilwrath);
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "host", 1) { PickScreen = world.PickScreenFor("host") };
        Assert.Null(world.PickScreenFor("nobody"));
        session.Observe(world.Snapshot());
        Assert.True(session.Picking);
        Assert.True(session.IgnoresRepeatedSelect);   // a held Enter or Space (firing) mustn't pick the flagship
        Assert.Contains(session.StatusLines(), l => l.StartsWith("Choose a ship: the flagship", StringComparison.Ordinal));
        session.PressMenu(MenuKey.Left);
        session.PressMenu(MenuKey.Left);
        session.PressMenu(MenuKey.Left);
        Assert.Contains(session.StatusLines(), l => l == "Choose a ship: the spathi in slot 1, crew 20/30");
        session.PressMenu(MenuKey.Select);
        Assert.False(session.Picking);
        Assert.False(session.IgnoresRepeatedSelect);
        Assert.Same(Fighter(wb, "host").FleetShips[0].Starship, Fighter(wb, "host").Starship);
    }

    [Fact]
    public void AnArmadaIsRefusedWhereItCantComeIn()
    {
        var escort = Ship("spathi", 0, 5);
        var armada = new Armada(new[] { escort }, Ship("spathi", 0, 7));
        var enemy = Ship(Ilwrath, 1, 10);
        var battle = NewBattle(new[] { Ship("spathi", 0, 9) }, enemy);
        Assert.Throws<ArgumentException>(() => battle.Join(escort));   // an armada's ship comes in with its armada
        Assert.Throws<ArgumentException>(() => battle.Pick(escort));   // not in the battle
        RunUntil(battle, () => false, 40);
        battle.DoDamage(enemy.Ship!, 10);
        RunUntil(battle, () => battle.BattleCounter[1] == 0, 5);
        Assert.Throws<InvalidOperationException>(() => battle.JoinArmada(armada));   // decided
        Assert.Empty(armada.Counted);
    }

    [Fact]
    public void AGuestsShipDyingDoesntBringInTheHostsShipsWaiting()
    {
        var world = WithFleet(2, (Spathi, 20));
        var wb = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        var host = Fighter(wb, "host");
        Assert.NotNull(host.Pick);
        WarpIn(world, wb);
        // The guest's ship dying while the host chooses: with no crew it isn't fighting, so the battle waits for the
        // host (its explosion too).
        Kill(wb.Battle, Fighter(wb, "g1").Starship);
        Assert.True(GameWorld.WaitsForPick(wb));
        var before = Places(wb.Battle);
        RunDisarmed(world, wb, 5);
        Assert.Equal(before, Places(wb.Battle));

        // The host flying a fleet ship, the guest's ship dies: new_ship brings in none of the host's ships waiting
        // (only the host picks them).
        world = WithFleet(2, (Spathi, 20));
        wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        FlyIn(world, "g1");
        WarpIn(world, wb);
        host = Fighter(wb, "host");
        var guest = Fighter(wb, "g1");
        Kill(wb.Battle, guest.Starship);
        for (int i = 0; i < 300 && guest.Starship.RaceDesc is not null; ++i)
            RunDisarmed(world, wb, 1);
        Assert.Null(guest.Starship.RaceDesc);   // freed by new_ship
        Assert.Null(host.OwnStarship.Ship);
        Assert.Same(host.FleetShips[0].Starship, host.Starship);
        Assert.Equal(2, wb.Battle.BattleCounter[0]);   // the host's Spathi and flagship
        Assert.Null(wb.Outcome);
    }

    [Fact]
    public void AFleetShipStillRunningAwayAtAWinRetreatedAndTheHostIsPaid()
    {
        var world = WithFleet(2, (Spathi, 20));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        FlyIn(world, "g1");
        WarpIn(world, wb);
        var host = Fighter(wb, "host");
        RunDisarmed(world, wb, 2, id => id == "host" ? ShipInput.TakeOff : ShipInput.None);
        Assert.True(wb.Battle.RunningAway(host.Starship));
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Equal(BattleExit.Retreated, host.FleetShips[0].Exit);
        Assert.Equal(BattleExit.Fought, host.Exit);
        Assert.Equal(63u, host.Salvage);
        Assert.Equal(20, world.Fleet.Ships[0].Crew);
    }

    [Fact]
    public void AFleetShipMayRetreatWithTheBombAboardTheFlagship()
    {
        var world = WithFleet(1, (Spathi, 20));
        world.Story!.Set("CHMMR_BOMB_STATE", 3);
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        WarpIn(world, wb);
        UntilPicking(world, wb, _ => ShipInput.TakeOff);
        Assert.Equal(BattleExit.Retreated, Fighter(wb, "host").FleetShips[0].Exit);
    }

    [Fact]
    public void AWinWhileTheHostChoosesTheirFirstShipPaysThemAShare()
    {
        var world = WithFleet(2, (Spathi, 20));
        var wb = world.StartBattle("g1", Ilwrath);
        FlyIn(world, "host");
        WarpIn(world, wb);
        Kill(wb.Battle, wb.Enemy);
        RunOut(world, wb);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        var host = Fighter(wb, "host");
        Assert.Equal(BattleExit.Fought, host.Exit);
        Assert.Null(host.Pick);
        Assert.Equal(new uint[] { 63, 62 }, wb.Fighters.Select(f => f.Salvage));
        Assert.Equal(new[] { (Spathi, 20) }, world.Fleet.Ships.Select(s => (s.Race, s.Crew)));
    }

    [Fact]
    public void APickWaitsForRoomInAFullListWhileTheBattleRunsOn()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        Fill(wb.Battle);
        var host = Fighter(wb, "host");
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        Assert.Null(host.Pick);
        Assert.NotSame(host.FleetShips[0].Starship, host.Starship);   // not in yet
        Assert.Null(host.FleetShips[0].Starship.Ship);
        Assert.False(GameWorld.WaitsForPick(wb));   // the battle runs on, so room comes
        Run(world, 5);
        Assert.Same(host.FleetShips[0].Starship, host.Starship);
        Assert.NotNull(host.Starship.Ship);
        Assert.True(host.FleetShips[0].Flown);

        // A pick waiting as the enemy's last ship dies comes to nothing, and the battle is won (the host still in it).
        var w2 = WithFleet(2, (Spathi, 20));
        var b2 = w2.StartBattle("g1", Ilwrath);
        FlyIn(w2, "host");
        WarpIn(w2, b2);
        Fill(b2.Battle);
        Assert.Equal(PickOutcome.Chosen, PickAt(w2, 0));
        var h2 = Fighter(b2, "host");
        Kill(b2.Battle, b2.Enemy);
        RunOut(w2, b2);
        Assert.Equal(BattleOutcome.Won, b2.Outcome);
        Assert.Null(h2.FleetShips[0].Starship.RaceDesc);
        Assert.False(h2.FleetShips[0].Flown);
        Assert.Equal(BattleExit.Fought, h2.Exit);
    }

    // The battle's list filled with elements that go in a few frames.
    private static void Fill(Battle battle)
    {
        while (!battle.List.Full)
        {
            var e = battle.List.Alloc()!;
            e.StateFlags = ElementFlags.NonSolid | ElementFlags.FiniteLife;
            e.LifeSpan = 3;
            e.PlayerNr = Battle.NeutralPlayerNum;
            battle.List.Put(e);
        }
    }

    [Fact]
    public void APkunkBeingRebornIsSavedAndADyingShipIsnt()
    {
        var world = WithFleet(1, ("PKUNK_SHIP", 8));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        WarpIn(world, wb);
        var pkunk = Fighter(wb, "host").Starship;
        pkunk.RaceDesc!.CrewLevel = 0;
        pkunk.Ship!.MassPoints = Battle.MaxShipMass + 1;   // intercept_pkunk_death's mark: to be reborn
        Assert.Equal(new[] { ("PKUNK_SHIP", 8, 0) }, SavedFleet(world));
        pkunk.Ship.MassPoints = Battle.MaxShipMass;
        Assert.Empty(SavedFleet(world));
    }

    [ContentFact]
    public void AStoryTalkFreezesTheBattleOfAFleetShipDying()
    {
        // Only the flagship's death runs its battle on through a story pause (the game over); a fleet ship's isn't it.
        var world = InSol(2);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        world.AddFleetShips(Spathi, 1);
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        WarpIn(world, wb);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        Run(world, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("MOONBASE_ON_SHIP", 1);
        world.VisitStarbase(world.Find("g1")!);
        Assert.True(world.StoryPaused);
        Assert.False(world.FlagshipLost);
        var before = Places(wb.Battle);
        Run(world, 5);
        Assert.Equal(before, Places(wb.Battle));
    }

    [ContentFact]
    public void StartBattleWithAFleetHasTheHostChoose()
    {
        var systems = WorldBattleTests.Systems.Value;
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Host",
            systems, TestContent.Shapes);
        host.World.BattleKit = BattleReplay.Kit.Value;
        host.StartWithFleet(new[] { "spathi:5", "arilou" });
        host.StartBattle("ilwrath");
        var wb = Assert.Single(host.World.Battles);
        var fighter = wb.FighterOf("host")!;
        Assert.NotNull(fighter.Pick);
        Assert.Equal(new[] { "spathi", "arilou" }, fighter.FleetShips.Select(f => f.Ship.Id));
        Assert.Equal(PickOutcome.Moved, host.World.PickShipKey("host", MenuKey.Left));
        Assert.Equal(PickOutcome.Moved, host.World.PickShipKey("host", MenuKey.Left));
        Assert.Equal(PickOutcome.Chosen, host.World.PickShipKey("host", MenuKey.Select));
        Assert.Equal("arilou", fighter.ShipId);
        host.Step(ShipInput.None);
        Assert.Equal("arilou", host.World.Snapshot().Battles!.Single().FighterOf("host")!.Status.Ship);
    }

    [ContentFact]
    public void TheHostOnThePickScreenRepliesToAStoryTalkPassedToThem()
    {
        // A guest's story talk while the host chooses; the guest disconnects and the replies pass to the host, whose
        // menu keys must reach the talk (the pick screen waits while the story does: nothing else could go on).
        var world = InSol(2);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, ConversationScripts.Load(DataDir)));
        world.AddFleetShips(Spathi, 1);
        var wb = world.StartBattle("host", Ilwrath);
        world.VisitStarbase(world.Find("g1")!);
        world.Leave("g1");
        Run(world, 1);
        Assert.Equal("host", world.Visit!.Replier.Id);
        var me = new PlayerSession(WorldBattleTests.Systems.Value, null, "host", 1) { PickScreen = world.PickScreenFor("host") };
        me.Observe(world.Snapshot());
        me.ShowTalk(world.TalkFor("host"));
        Assert.True(me.Picking && me.StoryPaused && me.Replying);
        me.PressMenu(MenuKey.Select);
        Assert.NotNull(me.TakeReply());
        Assert.NotNull(Fighter(wb, "host").Pick);   // still choosing, the cursor where it was
        Assert.Equal(PickShipMenu.FlagshipColumn, Fighter(wb, "host").Pick!.Column);
    }

    [Fact]
    public void WhileTheBattleWaitsForThePickTheHostsPlaceStaysPut()
    {
        // The Spathi flown off at speed and killed: the camera scrolled on its last frames; the battle then waits
        // for the host's pick, and the place the host watches from (the ship's last) stays where it was.
        var world = WithFleet(1, (Spathi, 30));
        var wb = world.StartBattle("host", Ilwrath);
        PickAt(world, 0);
        WarpIn(world, wb);
        RunDisarmed(world, wb, 40, _ => ShipInput.Thrust);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        UntilPicking(world, wb, _ => ShipInput.Thrust);
        Assert.True(GameWorld.WaitsForPick(wb));
        Assert.NotEqual((0, 0), ((int)wb.Battle.ScrollX, (int)wb.Battle.ScrollY));   // the last frame's scroll
        var me = world.Snapshot().Battles!.Single().FighterOf("host")!;
        var enemy = world.Snapshot().Battles!.Single();
        Run(world, 5);
        var later = world.Snapshot().Battles!.Single();
        Assert.Equal((me.X, me.Y), (later.FighterOf("host")!.X, later.FighterOf("host")!.Y));
        Assert.Equal((enemy.EnemyX, enemy.EnemyY), (later.EnemyX, later.EnemyY));   // the battle frozen (decision 2)
    }

    [ContentFact]
    public void DuringTheOpeningsFightASaveAndItsUndoKeepTheHostsBattleLosses()
    {
        // A guest fights the opening's Ilwrath (nothing paused) while the host fights elsewhere with fleet ships.
        var world = OpeningTests.Reported(2);
        world.AddFleetShips(Spathi, 1);
        world.AddFleetShips(Arilou, 1);
        var fight = OpeningTests.ToTheFight(world, "g1");
        var visit = world.Visit!;
        Assert.Equal(VisitStep.IlwrathFight, visit.Step);
        var wb = world.StartBattle("host", Ilwrath);
        Assert.NotSame(fight, wb);
        PickAt(world, 0);
        for (int i = 0; i < 60 && !Warped(wb.Battle); ++i)
            BothDisarmed(world, fight, wb);
        Kill(wb.Battle, Fighter(wb, "host").Starship);
        for (int i = 0; i < 400 && Fighter(wb, "host").Pick is null; ++i)
            BothDisarmed(world, fight, wb);
        var arilou = world.Fleet.Ships.Single();
        Assert.Equal(Arilou, arilou.Race);   // the Spathi destroyed is gone (decision 4)
        // The save is the game without the visit, but the Spathi's death isn't the visit's: it stays gone.
        Assert.Equal(new[] { (Arilou, arilou.Crew, arilou.Slot) }, SavedFleet(world));
        // The guest leaves the fight with the Ilwrath alive: the opening is taken back, the Spathi still gone.
        world.Leave("g1");
        for (int i = 0; i < 5 && !visit.Undone; ++i)
            Run(world, 1);
        Assert.True(visit.Undone);
        Assert.Equal(new[] { arilou }, world.Fleet.Ships);
    }

    // A frame of two battles, each enemy unable to fire.
    private static void BothDisarmed(GameWorld world, WorldBattle a, WorldBattle b)
    {
        foreach (var wb in new[] { a, b })
            if (wb.Outcome is null)
                wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;
        Run(world, 1);
    }

    [Fact]
    public void AShipATalkTakesWhileItsPickWaitsForRoomDoesntComeIn()
    {
        var world = WithFleet(1, (Spathi, 20), (Arilou, 4));
        var wb = world.StartBattle("host", Ilwrath);
        Fill(wb.Battle);
        var host = Fighter(wb, "host");
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 0));
        Assert.NotNull(host.PickedWaiting);
        world.Fleet.Remove(Spathi);   // as a guest's talk might, meanwhile
        Run(world, 5);
        Assert.Null(host.FleetShips[0].Starship.Ship);
        Assert.False(host.FleetShips[0].Flown);
        Assert.NotSame(host.FleetShips[0].Starship, host.Starship);
        Assert.Null(host.PickedWaiting);
        Assert.NotNull(host.Pick);   // the host chooses again
        Assert.Equal(PickOutcome.None, PickAt(world, 0));
        Assert.Equal(PickOutcome.Chosen, PickAt(world, 1));
    }
}
