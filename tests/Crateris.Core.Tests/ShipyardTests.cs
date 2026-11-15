// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Session;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The shipyard and the roster with the host's fleet (stage 4b item 7):
/// building only the allied races' ships, from the host's wallet, into
/// the slot under the cursor; crew with UQM's refunds; the last crew's
/// refund and the buy-back; a ship of no crew scrapped however the crew
/// editing ends, and left out of a save; guests' shipyard as before, and
/// unable to touch the fleet; the roster's crew moves, the host's only.
/// The rules key by key against UQM are <see cref="ShipyardGoldenTests"/>.
/// </summary>
[Trait("Topic", "fleet")]
public class ShipyardTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static StoryRules Rules => OutfitTests.Rules;
    private const string Human = "HUMAN_SHIP", Spathi = "SPATHI_SHIP", Syreen = "SYREEN_SHIP", Arilou = "ARILOU_SHIP";

    // A new game's flagship with this wallet and crew, its story with these races allied too, and a fleet.
    private static (OutfitTests.TestShip Ship, StoryState Story, HostFleet Fleet, Outfitting Rules) Yard(
        uint ru = 20000, int crew = 40, params string[] allied)
    {
        string modules = string.Join(", ", new[] { "EMPTY", "FUEL_TANK", "CREW_POD" }.Concat(Enumerable.Repeat("EMPTY", 5))
            .Append("STORAGE_BAY").Concat(Enumerable.Repeat("EMPTY", 6)).Append("GUN_WEAPON").Select(m => $"\"{m}\""));
        var sc = JsonDocument.Parse("{\"state\": {}, \"costs\": {}, \"fuel_cost\": 20, \"crew_cost\": 3, "
            + $"\"ship\": {{\"crew\": {crew}, \"fuel\": 1000, \"landers\": 1, \"ru\": {ru}, \"elements\": [0, 0, 0, 0, 0, 0, 0, 0], "
            + $"\"modules\": [{modules}], \"drives\": [5, 6], \"jets\": [0, 6]}}}}").RootElement;
        var (ship, story) = OutfitTests.Start(sc);
        foreach (var r in allied)
            story.SetAlliance(r, Allied.Good);
        var fleet = new HostFleet();
        var random = new Uqm.TfbRandom(7);
        return (ship, story, fleet, new Outfitting(ship, story, fleet, random.Next));
    }

    // The shipyard's menu opened on Crew: the cursor on the flagship.
    private static ShipyardMenu Opened(Outfitting rules)
    {
        var yard = new ShipyardMenu(rules, rules.Apply);
        yard.Key(MenuKey.Select);
        yard.TakeOps();
        return yard;
    }

    private static void Press(ShipyardMenu yard, params MenuKey[] keys)
    {
        foreach (var k in keys)
            yard.Key(k);
    }

    [Fact]
    public void TheListToBuildIsTheAlliedRacesShipsInUqmsOrder()
    {
        var (_, story, _, rules) = Yard(allied: new[] { Spathi, Arilou });
        Assert.Equal(new[] { Arilou, Human, Spathi }, rules.BuildList);
        story.SetAlliance(Arilou, Allied.Dead);
        Assert.Equal(new[] { Human, Spathi }, rules.BuildList);
        // The drone, never allied in UQM, never built.
        story.SetAlliance("URQUAN_DRONE_SHIP", Allied.Good);
        Assert.DoesNotContain("URQUAN_DRONE_SHIP", rules.BuildList);
        // A guest's rules have no fleet: nothing to build.
        Assert.Empty(new Outfitting(rules.Ship, story).BuildList);
    }

    [Fact]
    public void TheHostBuildsAnAlliedShipIntoTheSlotUnderTheCursor()
    {
        var (ship, story, fleet, rules) = Yard(allied: Spathi);
        fleet.Restore(Rules, new[] { new FleetShip(Arilou, 6, 9, 1), new FleetShip(Arilou, 6, 10, 5) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Right, MenuKey.Right, MenuKey.Right);   // slot 3
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Select));
        Assert.Equal((YardMode.Build, 0, Human), (yard.Mode, (int)yard.RaceItem, yard.BuildRace));
        Assert.Equal(ShipyardMenu.ModifyCrewFlag, yard.Item);
        Press(yard, MenuKey.Right);
        Assert.Equal(Spathi, yard.BuildRace);
        Press(yard, MenuKey.Down);   // down goes on too, wrapping round
        Assert.Equal(Human, yard.BuildRace);
        Press(yard, MenuKey.Left);   // left goes back, wrapping round
        Assert.Equal(Spathi, yard.BuildRace);
        Press(yard, MenuKey.Up, MenuKey.Up);
        Assert.Equal(Spathi, yard.BuildRace);
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Select));
        // Built into slot 3, between the ships of slots 1 and 5, with one crew; the cost paid; its crew edited next.
        Assert.Equal(new[] { 1, 3, 5 }, fleet.Ships.Select(s => s.Slot));
        Assert.Equal((Spathi, 1), (fleet.InSlot(3)!.Race, fleet.InSlot(3)!.Crew));
        Assert.Equal(20000u - 1800, ship.ResourceUnits);
        Assert.Equal(0, story.CrewPurchased);   // the ship's one crew isn't counted as bought
        Assert.Equal((YardMode.Crew, 3), (yard.Mode, yard.EditingSlot));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew),
            new ScreenOp(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined, -1800) }, yard.TakeOps());
        // Its captain is named as NameCaptain names them: one of the 16, drawn again while a Spathi has it.
        Assert.InRange(fleet.InSlot(3)!.Captain, HostFleet.CaptainNameOffset, HostFleet.CaptainNameOffset + 15);

        // Only allied races, only empty slots in range, only with the cost.
        Assert.False(rules.Apply(new BuildShip(4, "ORZ_SHIP")));
        Assert.False(rules.Apply(new BuildShip(3, Human)));
        Assert.False(rules.Apply(new BuildShip(12, Human)));
        Assert.False(rules.Apply(new BuildShip(-1, Human)));
        Assert.Equal(3, fleet.Count);
        // Cancel in the list goes back to the cursor, drawing the menu where the list was.
        Press(yard, MenuKey.Select, MenuKey.Right);   // crew editing left; slot 4
        Press(yard, MenuKey.Select);
        Assert.Equal(YardMode.Build, yard.Mode);
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Cancel));
        Assert.Equal((YardMode.Ships, 4), (yard.Mode, (int)yard.Cursor));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) }, yard.TakeOps());
    }

    [Fact]
    public void TheShipLitInTheListIsKeptPastACancelButNotPastLeavingTheHangar()
    {
        // delta_item's low byte: kept as the list is cancelled, set to 0 as DoModifyShips opens again from the menu.
        var (_, _, _, rules) = Yard(allied: new[] { Spathi, Arilou });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select, MenuKey.Right, MenuKey.Right, MenuKey.Cancel);
        Assert.Equal((YardMode.Ships, 2, 2), (yard.Mode, (int)yard.RaceItem, yard.Item));
        Press(yard, MenuKey.Cancel);
        Assert.Equal(YardMode.Menu, yard.Mode);
        Press(yard, MenuKey.Select);
        Assert.Equal((YardMode.Ships, 0, ShipyardMenu.Flagship), (yard.Mode, yard.Item, yard.Cursor));
    }

    [Fact]
    public void AFleetShipsCrewEditingFlashesItsCrewCount()
    {
        // DMS_FlashEscortShipCrewCount: the slot's bottom 5 rows (SHIP_WIN_HEIGHT - 6 down), where ShowShipCrew writes.
        var slot = Crateris.Presentation.StarbaseScreens.YardFlash(YardMode.Ships, 7)!.Value;
        var crew = Crateris.Presentation.StarbaseScreens.YardFlash(YardMode.Crew, 7)!.Value;
        Assert.Equal((slot.X, slot.Y + slot.H - 6, slot.W, 5), (crew.X, crew.Y, crew.W, crew.H));
        Assert.NotEqual(crew, Crateris.Presentation.StarbaseScreens.YardFlash(YardMode.Crew, ShipyardMenu.Flagship));
        Assert.Null(Crateris.Presentation.StarbaseScreens.YardFlash(YardMode.Build, 7));
    }

    [Fact]
    public void BuildingIsRefusedTooPoorAndTheHangarFullHasNoEmptySlot()
    {
        var (ship, _, fleet, rules) = Yard(ru: 1099);
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select);
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Select));
        Assert.Equal((YardMode.Build, 0, 1099u), (yard.Mode, fleet.Count, ship.ResourceUnits));
        ship.ResourceUnits = 1100;
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Select));
        Assert.Equal((1, 0u), (fleet.Count, ship.ResourceUnits));

        // The hangar full: select on any slot edits its ship's crew, never builds.
        var full = Yard(allied: Spathi);
        full.Fleet.Restore(Rules, Enumerable.Range(0, 12).Select(i => new FleetShip(Spathi, 10, (byte)(5 + i), i)));
        yard = Opened(full.Rules);
        for (int i = 0; i < 12; ++i)
        {
            Press(yard, MenuKey.Down);
            if (ShipyardMenu.OnFlagship(yard.Cursor))
                continue;
            Press(yard, MenuKey.Select);
            Assert.Equal((YardMode.Crew, (int?)yard.Cursor), (yard.Mode, yard.EditingSlot));
            Press(yard, MenuKey.Select, MenuKey.Right);
        }
        Assert.All(Enumerable.Range(0, 12), slot => Assert.False(full.Rules.Apply(new BuildShip(slot, Spathi))));
        Assert.Equal(12, full.Fleet.Count);
        Assert.Throws<ArgumentException>(() => full.Fleet.Build(Rules, Spathi, 0, 1, () => 0));
    }

    [Fact]
    public void AFleetShipsCrewIsHiredToItsRacesStartingCrewWithUqmsRefunds()
    {
        var (ship, story, fleet, rules) = Yard(ru: 100000, allied: Syreen);
        fleet.Restore(Rules, new[] { new FleetShip(Human, 16, 5, 0), new FleetShip(Syreen, 10, 5, 1) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select);
        // Up to the race's starting 18, the crew price each, each counted as bought.
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Up));
        Assert.Equal((18, 100000u - 6, 2), (fleet.InSlot(0)!.Crew, ship.ResourceUnits, story.CrewPurchased));
        // Down: the crew price back.
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));
        Assert.Equal((17, 100000u - 3, 1), (fleet.InSlot(0)!.Crew, ship.ResourceUnits, story.CrewPurchased));
        // A Syreen to its starting 12, not its most 42 (the template's crew_level).
        Press(yard, MenuKey.Select, MenuKey.Right, MenuKey.Select, MenuKey.Up, MenuKey.Up);
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Up));
        Assert.Equal(12, fleet.InSlot(1)!.Crew);
        // The wallet short of the crew price: refused.
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));
        ship.ResourceUnits = 2;
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Up));
        Assert.Equal((11, 2u), (fleet.InSlot(1)!.Crew, ship.ResourceUnits));
    }

    [Fact]
    public void TheCrewPriceRisesAtTheThresholdAndTheDismissalOnItGivesBackTwoLess()
    {
        var (ship, story, fleet, rules) = Yard(ru: 10000);
        story.Set("CREW_PURCHASED0", 999 & 0xFF);
        story.Set("CREW_PURCHASED1", 999 >> 8);
        fleet.Restore(Rules, new[] { new FleetShip(Human, 5, 5, 0) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select);
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));   // the thousandth: the price rises after this one
        Assert.Equal((5, 1000, 9997u), (story.CrewCost, story.CrewPurchased, ship.ResourceUnits));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.Gauges, 0, 0, -3),
            new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) }, yard.TakeOps());
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));   // on the threshold: 5 - 2 back, and the price falls
        Assert.Equal((3, 999, 10000u), (story.CrewCost, story.CrewPurchased, ship.ResourceUnits));
        // With the Shofixti allied, the count stays, but the price still moves as it would.
        story.SetAlliance("SHOFIXTI_SHIP", Allied.Good);
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal((5, 999), (story.CrewCost, story.CrewPurchased));
    }

    [Fact]
    public void TheLastCrewSentAwayGivesTheShipsCostBackAndHiringAgainBuysItBack()
    {
        var (ship, story, fleet, rules) = Yard(ru: 5000, allied: Spathi);
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 2, 5, 0) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select);
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));
        Assert.Equal((1, 5003u), (fleet.InSlot(0)!.Crew, ship.ResourceUnits));
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));   // the last: the ship's 1800 back, the ship left with none
        Assert.Equal((0, 6803u, 0), (fleet.InSlot(0)!.Crew, ship.ResourceUnits, story.CrewPurchased));
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Down));
        Assert.Equal((YardMode.Crew, 0), (yard.Mode, yard.EditingSlot));
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));      // bought back at its cost, counted as crew bought
        Assert.Equal((1, 5003u, 1), (fleet.InSlot(0)!.Crew, ship.ResourceUnits, story.CrewPurchased));
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));      // then the crew price
        Assert.Equal((2, 5000u), (fleet.InSlot(0)!.Crew, ship.ResourceUnits));
        // Bought back with less than its cost but the crew price: the wallet emptied (DeltaSISGauges), as UQM's.
        Press(yard, MenuKey.Down, MenuKey.Down);
        ship.ResourceUnits = 10;
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal((1, 0u), (fleet.InSlot(0)!.Crew, ship.ResourceUnits));
    }

    [Theory]
    [InlineData(MenuKey.Select)]
    [InlineData(MenuKey.Cancel)]
    public void AShipLeftWithNoCrewIsScrappedAsItsCrewEditingEnds(MenuKey leave)
    {
        var (ship, _, fleet, rules) = Yard(ru: 0, allied: Spathi);
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 1, 5, 0), new FleetShip(Spathi, 3, 6, 1) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select, MenuKey.Down);
        Assert.Equal((0, 1800u), (fleet.InSlot(0)!.Crew, ship.ResourceUnits));
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.None, yard.Key(leave));
        Assert.Null(fleet.InSlot(0));
        Assert.Equal(1800u, ship.ResourceUnits);   // its cost came back with its last crew
        Assert.Equal((YardMode.Ships, 0), (yard.Mode, (int)yard.Cursor));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined, ScreenOp.Undefined) },
            yard.TakeOps());
        // A ship with crew left as it is; and scrapping is only for a ship of none.
        Press(yard, MenuKey.Right, MenuKey.Select, MenuKey.Down, leave);
        Assert.Equal(2, fleet.InSlot(1)!.Crew);
        Assert.False(rules.Apply(new ScrapShip(1)));
        Assert.False(rules.Apply(new ScrapShip(0)));
        // The empty slot can be built into again (back in the list).
        Press(yard, MenuKey.Left, MenuKey.Select);
        Assert.Equal(YardMode.Build, yard.Mode);
    }

    [Fact]
    public void WhetherASlotIsBuiltIntoOrCrewedFollowsTheFleet()
    {
        // GetEscortByStarShipIndex at each key: a ship a talk puts in the slot being built into is crewed instead.
        var (_, _, fleet, rules) = Yard(allied: Spathi);
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Select);
        Assert.Equal(YardMode.Build, yard.Mode);
        fleet.Add(Rules, Arilou, 1, () => 0);   // the first free slot: the cursor's
        Assert.Equal((YardMode.Crew, 0), (yard.Mode, yard.EditingSlot));
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Up));   // the Arilou's full: its 6
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Down));
        fleet.Remove(Arilou);
        Assert.Equal(YardMode.Build, yard.Mode);
    }

    [Fact]
    public void AGuestsShipyardCrewsTheirOwnShipAndCantTouchTheFleet()
    {
        var (ship, story, fleet, _) = Yard(ru: 5000, allied: Spathi);
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 5, 5, 0) });
        var guest = new Outfitting(ship, story);   // a guest's rules: no fleet
        var yard = Opened(guest);
        Press(yard, MenuKey.Down);
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Select));   // a ship there
        Press(yard, MenuKey.Right);
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Select));   // an empty slot
        Assert.Equal((YardMode.Ships, (int?)null), (yard.Mode, yard.EditingSlot));
        foreach (var action in new OutfitAction[] { new BuildShip(1, Spathi), new HireShipCrew(0), new DismissShipCrew(0), new ScrapShip(0) })
            Assert.False(guest.Apply(action));
        Assert.Equal((5, 1, 5000u), (fleet.InSlot(0)!.Crew, fleet.Count, ship.ResourceUnits));
        // Their own ship's crew as before.
        Press(yard, MenuKey.Down, MenuKey.Down, MenuKey.Select);
        Assert.True(yard.CrewMode);
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal(41, ship.Crew);
    }

    [Fact]
    public void TheRostersCursorAndItsCrewMovesAreRosterCs()
    {
        // Three ships: the points of the first three of SUPPORT_SHIP_PTS, sorted on X then Y.
        var roster = new RosterMenu(3);
        Assert.Equal(((3, 62), 0), (roster.Point, roster.Selected));
        var fleet = new HostFleet();
        fleet.Restore(Rules, new[] { new FleetShip(Human, 10, 5, 2), new FleetShip(Spathi, 1, 5, 4), new FleetShip(Syreen, 20, 5, 9) });
        var crew = (Ship: 30, Room: 50);
        var move = new TestFleet(fleet, () => crew.Ship, c => crew.Ship = c, () => crew.Room);
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Down, move));
        Assert.Equal(((3, 78), 2), (roster.Point, roster.Selected));
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Right, move));
        Assert.Equal(((45, 62), 1), (roster.Point, roster.Selected));
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Select, move));
        Assert.True(roster.ModifyingCrew);
        // The Spathi in slot 4 keeps its one crew; takes crew up to its 30.
        Assert.Equal(MenuOutcome.Refused, roster.Press(MenuKey.Down, move));
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Up, move));
        Assert.Equal((2, 29), (fleet.InSlot(4)!.Crew, crew.Ship));
        Assert.Equal(MenuOutcome.None, roster.Press(MenuKey.Left, move));   // across does nothing while the crew changes
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Cancel, move));   // cancel ends the crew change, then leaves
        Assert.False(roster.ModifyingCrew);
        // The Syreen above its starting 12 (crew picked up in a battle) takes none, but gives back.
        Press(roster, move, MenuKey.Right);   // across from the right column's top: the left's top (roster.c's arithmetic)
        Assert.Equal(((3, 62), 0), (roster.Point, roster.Selected));
        Press(roster, move, MenuKey.Down, MenuKey.Select);
        Assert.Equal(2, roster.Selected);
        Assert.Equal(MenuOutcome.Refused, roster.Press(MenuKey.Up, move));
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Down, move));
        Assert.Equal((19, 30), (fleet.InSlot(9)!.Crew, crew.Ship));
        // The flagship full: no crew back; empty: none to give.
        crew.Room = 30;
        Assert.Equal(MenuOutcome.Refused, roster.Press(MenuKey.Down, move));
        crew.Ship = 0;
        Assert.Equal(MenuOutcome.Refused, roster.Press(MenuKey.Up, move));
        Assert.Equal(19, fleet.InSlot(9)!.Crew);
        Press(roster, move, MenuKey.Select);
        Assert.Equal(MenuOutcome.Moved, roster.Press(MenuKey.Cancel, move));
        Assert.True(roster.Left);
        Assert.Equal(MenuOutcome.None, roster.Press(MenuKey.Select, move));
        // One ship: no moving about.
        var one = new RosterMenu(1);
        Assert.Equal(MenuOutcome.None, one.Press(MenuKey.Down, move));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RosterMenu(0));
        Assert.False(fleet.MoveRosterCrew(Rules, 0, 2, 10, 50));
        Assert.False(fleet.MoveRosterCrew(Rules, 3, 1, 10, 50));
    }

    private static void Press(RosterMenu roster, IRosterFleet fleet, params MenuKey[] keys)
    {
        foreach (var k in keys)
            roster.Press(k, fleet);
    }

    private sealed class TestFleet : IRosterFleet
    {
        private readonly HostFleet _fleet;
        private readonly Func<int> _crew, _room;
        private readonly Action<int> _setCrew;

        public TestFleet(HostFleet fleet, Func<int> crew, Action<int> setCrew, Func<int> room) =>
            (_fleet, _crew, _setCrew, _room) = (fleet, crew, setCrew, room);

        public IReadOnlyList<FleetShip> Ships => _fleet.Ships;

        public bool MoveCrew(int index, int delta)
        {
            if (!_fleet.MoveRosterCrew(Rules, index, delta, _crew(), _room()))
                return false;
            _setCrew(_crew() - delta);
            return true;
        }
    }

    [Fact]
    public void TheOrbitMenusRosterIsTheHostsAndAGuestsComesLater()
    {
        var menu = new OrbitMenu(new WorldId(Sol, Earth, Luna), Systems[Sol].EnterOrbit(Earth, Luna));
        Press(menu, MenuKey.Down, MenuKey.Down, MenuKey.Select, MenuKey.Down, MenuKey.Down);
        Assert.Equal(OrbitMenuItem.Roster, menu.Selected);
        // A guest: no fleet, so the Roster comes later (as before stage 4b item 7).
        Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select, _ => 0));
        // The host with no fleet ships: nothing opens (RosterMenu returns FALSE).
        var fleet = new HostFleet();
        int crew = 30;
        menu.Fleet = new TestFleet(fleet, () => crew, c => crew = c, () => 50);
        Assert.Equal(MenuOutcome.None, menu.Press(MenuKey.Select, _ => 0));
        Assert.Null(menu.Roster);
        fleet.Restore(Rules, new[] { new FleetShip(Human, 10, 5, 0) });
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Select, _ => 0));
        Assert.NotNull(menu.Roster);
        Press(menu, MenuKey.Select, MenuKey.Up, MenuKey.Up);
        Assert.Equal((12, 28), (fleet.InSlot(0)!.Crew, crew));
        Press(menu, MenuKey.Cancel, MenuKey.Cancel);
        Assert.Null(menu.Roster);
        Assert.Equal((OrbitMenu.MenuPage.Manifest, OrbitMenuItem.Roster), (menu.Page, menu.Selected));   // back at the Manifest
    }

    private static void Press(OrbitMenu menu, params MenuKey[] keys)
    {
        foreach (var k in keys)
            menu.Press(k, _ => 0);
    }

    // ---------------------------------------------------------------- the world, with the content

    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));
    private const int Sol = 82, Earth = 2, Luna = 1;

    // An allied game (the host and a guest by Earth's starbase, ready long since), the Spathi allied too.
    private static GameWorld AlliedGame()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", 3);
        story.Set("STARBASE_DAY", 3);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);   // no news
        story.SetAlliance(Spathi, Allied.Good);
        world.CommanderMet = true;
        foreach (var p in world.Players.Where(p => p.Present))
        {
            world.PutInOrbit(p.Id, Sol, Earth, Luna);
            p.Ship.Ip!.ReturnFromOrbit();
            p.Ship.Hold.ResourceUnits = 10000;
        }
        return world;
    }

    // Docked, on the shipyard's cursor (the menu's Shipyard, then Crew).
    private static Player InTheShipyard(GameWorld world, string id)
    {
        var p = world.Find(id)!;
        world.VisitStarbase(p);
        Assert.Equal(Activity.Starbase, p.Ship.Activity);
        Keys(world, id, MenuKey.Down, MenuKey.Down, MenuKey.Select, MenuKey.Select);
        Assert.Equal(YardMode.Ships, p.Ship.Visit!.Menu!.Yard!.Mode);
        return p;
    }

    private static void Keys(GameWorld world, string id, params MenuKey[] keys)
    {
        foreach (var k in keys)
            world.StarbaseKey(id, k, out _);
    }

    [ContentFact]
    public void TheHostBuildsAndCrewsFromTheHostsWalletAndAGuestCant()
    {
        var world = AlliedGame();
        var host = InTheShipyard(world, "host");
        var guest = InTheShipyard(world, "guest");
        // The host: slot 0, the list (Arilou not allied: the Earthling first), down to the Spathi, build, two more crew.
        Keys(world, "host", MenuKey.Down, MenuKey.Select, MenuKey.Down, MenuKey.Select, MenuKey.Up, MenuKey.Up);
        var built = Assert.Single(world.Fleet.Ships);
        Assert.Equal((Spathi, 3, 0), (built.Race, built.Crew, built.Slot));
        Assert.Equal(10000u - 1800 - 2 * 3, host.Ship.Hold.ResourceUnits);
        Assert.Equal(2, world.Story!.CrewPurchased);
        Assert.Equal(YardMode.Crew, world.StarbaseFor("host")!.YardMode);
        Keys(world, "host", MenuKey.Select);
        // The guest: the same keys are refused on the slots, and change nothing of the fleet or anyone's wallet.
        int refusals = world.StarbaseFor("guest")!.Refusals;
        Keys(world, "guest", MenuKey.Down, MenuKey.Select, MenuKey.Right, MenuKey.Select);
        Assert.Equal(refusals + 2, world.StarbaseFor("guest")!.Refusals);
        Assert.Equal(YardMode.Ships, world.StarbaseFor("guest")!.YardMode);
        foreach (var action in new OutfitAction[] { new BuildShip(1, Spathi), new HireShipCrew(0), new DismissShipCrew(0), new ScrapShip(0) })
            Assert.False(world.Outfit("guest", action));
        Assert.Equal(built, Assert.Single(world.Fleet.Ships));
        Assert.Equal(10000u, guest.Ship.Hold.ResourceUnits);
        // The guest's own crew as before: the flagship's row (their own ship), select, down sends one away.
        int crew = guest.Ship.Hold.Crew;
        Keys(world, "guest", MenuKey.Up, MenuKey.Select, MenuKey.Down);
        Assert.Equal(crew - 1, guest.Ship.Hold.Crew);
        Assert.Equal(10000u + 3, guest.Ship.Hold.ResourceUnits);
        // The host's view carries the race lit in the list (not encoded: the wire is item 9).
        Keys(world, "host", MenuKey.Right, MenuKey.Select);
        var view = world.StarbaseFor("host")!;
        Assert.Equal((YardMode.Build, Human), (view.YardMode, view.YardBuildRace));
    }

    [ContentFact]
    public void AShipOfNoCrewIsScrappedWhenTheShipyardIsLeftAnyWay()
    {
        // Select and cancel are the menu's (above); here the visit ending under it: the host leaving, the game loaded.
        var world = AlliedGame();
        world.AddFleetShips(Spathi, 2);
        var host = InTheShipyard(world, "host");
        Keys(world, "host", MenuKey.Down, MenuKey.Select);
        world.Fleet.SetCrew(Rules, 0, 1);
        Keys(world, "host", MenuKey.Down);
        Assert.Equal(0, world.Fleet.InSlot(0)!.Crew);
        uint ru = host.Ship.Hold.ResourceUnits;
        // While its crew is edited the ship stays, frame after frame.
        world.Step();
        world.Step();
        Assert.NotNull(world.Fleet.InSlot(0));
        // A save meanwhile leaves it out, as leaving would scrap it; the wallet has its cost back.
        var json = HostSave.ToJson(world);
        var saved = JsonNode.Parse(json)!["fleet"]!.AsArray();
        Assert.Equal(new[] { 1 }, saved.Select(s => s!["slot"]!.GetValue<int>()));
        var loaded = HostSave.FromJson(json, Systems, TestContent.Shapes);
        Assert.Equal(new[] { 1 }, loaded.Fleet.Ships.Select(s => s.Slot));
        Assert.Equal(ru, loaded.Host.Ship.Hold.ResourceUnits);
        // The host leaving (a disconnect) ends the visit: scrapped at once.
        world.Leave("host");
        Assert.Null(host.Ship.Visit);
        Assert.Null(world.Fleet.InSlot(0));
        Assert.NotNull(world.Fleet.InSlot(1));

        // An older save's ship of no crew loads, and goes in the first frame (no shipyard is editing it).
        var node = JsonNode.Parse(json)!;
        node["fleet"]!.AsArray().Add(new JsonObject { ["race"] = Spathi, ["crew"] = 0, ["captain"] = 9, ["slot"] = 5 });
        var old = HostSave.FromJson(node.ToJsonString(), Systems, TestContent.Shapes);
        Assert.Equal(0, old.Fleet.InSlot(5)!.Crew);
        old.Step();
        Assert.Null(old.Fleet.InSlot(5));
        Assert.NotNull(old.Fleet.InSlot(1));
    }

    [ContentFact]
    public void TheVisitEndingUnderTheShipyardScrapsItsShipOfNoCrew()
    {
        var world = AlliedGame();
        world.AddFleetShips(Spathi, 1);
        var host = InTheShipyard(world, "host");
        Keys(world, "host", MenuKey.Down, MenuKey.Select);
        for (int i = 0; i < 30; ++i)
            Keys(world, "host", MenuKey.Down);
        Assert.Equal((0, (int?)0), (world.Fleet.InSlot(0)!.Crew, host.Ship.Visit!.Menu!.Yard!.EditingSlot));
        // The visit's menu gone some other way than the keys (as its abandoning does): the next frame scraps it.
        host.Ship.Visit.Menu.Yard = null;
        world.Step();
        Assert.Empty(world.Fleet.Ships);
    }

    [ContentFact]
    public void TheRosterMovesCrewForTheHostInFlightOnly()
    {
        var world = AlliedGame();
        world.AddFleetShips(Spathi, 1);
        world.Fleet.SetCrew(Rules, 0, 20);
        var host = world.Host;
        int crew = host.Ship.Hold.Crew;
        Assert.Null(world.RosterFor("guest"));
        var roster = world.RosterFor("host")!;
        Assert.Equal(1, roster.Count);
        Assert.True(roster.MoveCrew(0, 1));
        Assert.Equal((21, crew - 1), (world.Fleet.InSlot(0)!.Crew, host.Ship.Hold.Crew));
        Assert.True(world.MoveRosterCrew("host", 0, -1));
        Assert.Equal((20, crew), (world.Fleet.InSlot(0)!.Crew, host.Ship.Hold.Crew));
        // Not a guest's; not with a bad delta or ship; not docked; not while paused.
        Assert.False(world.MoveRosterCrew("guest", 0, 1));
        Assert.False(world.MoveRosterCrew("host", 0, 2));
        Assert.False(world.MoveRosterCrew("host", 1, 1));
        world.SetHostPause(true);
        Assert.False(world.MoveRosterCrew("host", 0, 1));
        world.SetHostPause(false);
        world.VisitStarbase(host);
        Assert.False(world.MoveRosterCrew("host", 0, 1));
        Assert.Equal((20, crew), (world.Fleet.InSlot(0)!.Crew, host.Ship.Hold.Crew));
    }
}
