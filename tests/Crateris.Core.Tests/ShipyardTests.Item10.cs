// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Net;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// Stage 4b item 10: the fleet drawn. What the shipyard's menu lists to
/// draw for the hangar and the list of ships to build, the fleet going with
/// the starbase's drawing (and on the wire), another player's change to it
/// drawn, the hangar's doors holding the drawing after them, the orbit
/// menu's Roster for the host, and the pick screen over the battle for the
/// player choosing. Their drawing against UQM's is StarbaseScreensTests'.
/// </summary>
public partial class ShipyardTests
{
    private static int RaceOf(string race) => Rules.RaceIndex(race);

    [Fact]
    public void TheListToBuildShowsItsShipAsItOpensAndAsItMoves()
    {
        var (_, _, _, rules) = Yard(allied: new[] { Spathi, Arilou });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down);
        yard.TakeOps();
        // Select on an empty slot: DrawRaceStrings (pMS, 0), the list's first (the Arilou, in avail_race_q's order).
        yard.Key(MenuKey.Select);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.RaceStrings, RaceOf(Arilou)) }, yard.TakeOps());
        yard.Key(MenuKey.Right);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.RaceStrings, RaceOf(Human)) }, yard.TakeOps());
        yard.Key(MenuKey.Down);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.RaceStrings, RaceOf(Spathi)) }, yard.TakeOps());
        yard.Key(MenuKey.Right);   // wrapping round
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.RaceStrings, RaceOf(Arilou)) }, yard.TakeOps());
        yard.Key(MenuKey.Up);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.RaceStrings, RaceOf(Spathi)) }, yard.TakeOps());
        yard.Key(MenuKey.Cancel);   // the menu where the list was, nothing more
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) }, yard.TakeOps());

        // A list of one: the arrows change nothing, so nothing is drawn (currentShip == LOBYTE (delta_item)).
        var one = Opened(Yard().Rules);
        Press(one, MenuKey.Down, MenuKey.Select);
        one.TakeOps();
        Press(one, MenuKey.Right, MenuKey.Left, MenuKey.Up, MenuKey.Down);
        Assert.Empty(one.TakeOps());
    }

    [Fact]
    public void AFleetShipsCrewCountIsDrawnAsItsCrewChanges()
    {
        var (_, _, fleet, rules) = Yard(ru: 10000, allied: Spathi);
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 29, 5, 4) });
        var yard = Opened(rules);
        Press(yard, MenuKey.Down, MenuKey.Right, MenuKey.Right, MenuKey.Right, MenuKey.Right, MenuKey.Select);
        yard.TakeOps();
        // Hired: the wallet's gauge, then the crew count (DMS_HireEscortShipCrew's ShowShipCrew).
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.Gauges, 0, 0, -3), new ScreenOp(ScreenOpKind.ShipCrew, 4) }, yard.TakeOps());
        // A hire refused (the race's starting crew): nothing drawn.
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Up));
        Assert.Empty(yard.TakeOps());
        // Sent away to none: the last's refund; then a dismissal refused still shows the count (SCRAP).
        for (int i = 0; i < 30; ++i)
            yard.Key(MenuKey.Down);
        Assert.Equal(0, fleet.InSlot(4)!.Crew);
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Down));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ShipCrew, 4) }, yard.TakeOps());
        // Left with none: scrapped behind its closing doors (its race given, the ship gone), then the SIS display.
        yard.Key(MenuKey.Select);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ShipScrapped, 4, RaceOf(Spathi)),
            new ScreenOp(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined, ScreenOp.Undefined) }, yard.TakeOps());
        // The flagship's crew: no ship's count drawn.
        Press(yard, MenuKey.Up, MenuKey.Up, MenuKey.Select);
        yard.TakeOps();
        yard.Key(MenuKey.Up);
        Assert.DoesNotContain(yard.TakeOps(), op => op.Kind == ScreenOpKind.ShipCrew);
    }

    [Fact]
    public void TheFleetGoesWithTheStarbasesDrawingOnTheWire()
    {
        static StarbaseShip Ship(IReadOnlyList<HangarShip>? fleet) =>
            new(new byte[ShipModules.ModuleSlotCount], new byte[ShipModules.DriveSlotCount], new byte[ShipModules.JetSlotCount],
                10, 100, 1, 50, 0, null, null, 0, 0, 20, 3, new int[ShipModules.Modules], 50, fleet);
        static StarbaseShip RoundTrip(StarbaseShip s) =>
            StarbaseDrawing.Decode(StarbaseDrawing.Encode(new[] { new StarbaseDrawing(s, new[] { new ScreenOp(ScreenOpKind.SisDisplay) }) }))
                .Single().Ship!;
        var fleet = new[] { new HangarShip(0, 6, 30), new HangarShip(3, 0, 6), new HangarShip(11, 18, 42) };
        Assert.Equal(fleet, RoundTrip(Ship(fleet)).Fleet);
        Assert.Null(RoundTrip(Ship(null)).Fleet);
        Assert.Null(RoundTrip(Ship(Array.Empty<HangarShip>())).Fleet);
        Assert.Equal(new HangarShip(11, ScreenOp.MostRaces - 1, ushort.MaxValue),
            RoundTrip(Ship(new[] { new HangarShip(11, ScreenOp.MostRaces - 1, ushort.MaxValue) })).Fleet!.Single());
        // The encoder sends 12 at most; the decoder refuses what it never writes.
        var thirteen = Enumerable.Range(0, 13).Select(i => new HangarShip(i, 0, 1)).ToArray();
        Assert.Equal(12, RoundTrip(Ship(thirteen)).Fleet!.Count);
        foreach (var bad in new[]
                 {
                     new[] { new HangarShip(3, 0, 1), new HangarShip(3, 0, 1) },   // a slot twice
                     new[] { new HangarShip(4, 0, 1), new HangarShip(2, 0, 1) },   // out of order
                     new[] { new HangarShip(12, 0, 1) },                           // past the hangar
                     new[] { new HangarShip(0, ScreenOp.MostRaces, 1) },           // no such race
                     new[] { new HangarShip(0, 0, ushort.MaxValue + 1) },          // too much crew
                 })
            Assert.Throws<InvalidDataException>(() => RoundTrip(Ship(bad)));
        // The new operations' bounds.
        Assert.True(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipBuilt, 11)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipBuilt, 12)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipCrew, -1)));
        Assert.True(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipScrapped, 0, ScreenOp.MostRaces - 1)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipScrapped, 0, ScreenOp.MostRaces)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.ShipScrapped, 12, 0)));
        Assert.True(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.RaceStrings, -1)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.RaceStrings, -2)));
        Assert.False(StarbaseDrawing.Valid(new ScreenOp(ScreenOpKind.RaceStrings, ScreenOp.MostRaces)));
    }

    [Fact]
    public void TheHangarDrawsEachSlotAsAnothersChangeLeftIt()
    {
        var was = new[] { new HangarShip(0, 6, 30), new HangarShip(2, 0, 6), new HangarShip(5, 2, 18), new HangarShip(7, 3, 9) };
        var now = new[] { new HangarShip(0, 6, 28), new HangarShip(1, 2, 1), new HangarShip(2, 0, 6), new HangarShip(7, 4, 9) };
        Assert.Equal(new[]
        {
            new ScreenOp(ScreenOpKind.ShipCrew, 0),             // crew changed
            new ScreenOp(ScreenOpKind.ShipBuilt, 1),            // come
            new ScreenOp(ScreenOpKind.ShipScrapped, 5, 2),      // gone
            new ScreenOp(ScreenOpKind.ShipScrapped, 7, 3),      // another race in its place: the old out, the new in
            new ScreenOp(ScreenOpKind.ShipBuilt, 7),
        }, StarbaseMenu.HangarChanges(was, now));
        Assert.Empty(StarbaseMenu.HangarChanges(was, was));
    }

    [Fact]
    public void TheHangarsDoorsHoldTheDrawingAfterThemUntilTheyreDone()
    {
        var picture = new StarbasePicture(StarbaseScreensTests.Canvas(), StarbaseScreensTests.Placeholder, 29, 16,
            s => SisState.Of(s, "", ""), StarbaseScreensTests.TestRace);
        var ship = StarbaseScreensTests.Ship(new[] { new HangarShip(0, RaceOf(Spathi), 30), new HangarShip(7, RaceOf(Human), 3) });
        var opened = picture.Draw(new[] { new StarbaseDrawing(ship, new[] { new ScreenOp(ScreenOpKind.YardOpened),
            new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.SaveLoad) }) }, 1000);
        // The opening drawn, its doors waiting a frame (SleepThreadUntil), the menu after them held.
        Assert.True(picture.Animating);
        Assert.DoesNotContain(opened, d => d.Kind == UqmDrawKind.Clear && d.Clip.W == 34);
        Assert.Empty(picture.Tick(1000 + StarbasePicture.DoorsTicks - 1));
        var frames = new List<List<UqmDraw>>();
        for (long t = 1000 + StarbasePicture.DoorsTicks; frames.Count < HangarDoors.Frames; t += StarbasePicture.DoorsTicks)
        {
            var f = picture.Tick(t);
            // Each frame: both windows cleared and drawn with their doors, a pixel further open than the last.
            Assert.Equal(2, f.Count(d => d.Kind == UqmDrawKind.Clear));
            Assert.Equal(-1 - frames.Count, f.First(d => d.Sheet == StarbaseScreens.ShipyardSheet && d.Frame == 1).X
                - (UqmCanvas.SisOrgX + 0));
            frames.Add(f);
            Assert.Equal(frames.Count < HangarDoors.Frames, picture.Animating);   // the menu came with the last frame
        }
        Assert.Contains(frames[^1], d => d.Kind == UqmDrawKind.Text && d.Text.Contains($"{{gs:"));
        Assert.False(picture.Animating);

        // Flushed at once, the same calls in the same order, but the power lines on their clock.
        var flushed = new StarbasePicture(StarbaseScreensTests.Canvas(), StarbaseScreensTests.Placeholder, 29, 16,
            s => SisState.Of(s, "", ""), StarbaseScreensTests.TestRace);
        var all = flushed.Draw(new[] { new StarbaseDrawing(ship, new[] { new ScreenOp(ScreenOpKind.YardOpened),
            new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.SaveLoad) }) }, 1000);
        all.AddRange(flushed.Flush(1000));
        var stepped = opened.Concat(frames.SelectMany(f => f)).Where(d => d.Frame != StarbaseScreens.PowerLinesFrame || d.Sheet != StarbaseScreens.ShipyardSheet);
        Assert.Equal(stepped, all.Where(d => d.Frame != StarbaseScreens.PowerLinesFrame || d.Sheet != StarbaseScreens.ShipyardSheet));
        // A scrap's doors close: they start apart and meet.
        var scrap = new StarbasePicture(StarbaseScreensTests.Canvas(), StarbaseScreensTests.Placeholder, 29, 16,
            s => SisState.Of(s, "", ""), StarbaseScreensTests.TestRace);
        scrap.Draw(new[] { new StarbaseDrawing(ship, new[] { new ScreenOp(ScreenOpKind.ShipScrapped, 3, RaceOf(Spathi)) }) }, 0);
        var closing = scrap.Flush(0).Where(d => d.Sheet == StarbaseScreens.ShipyardSheet && d.Frame == 2).Select(d => d.X).ToList();
        Assert.Equal(HangarDoors.Frames, closing.Count);
        Assert.True(closing[0] > closing[^1]);
        Assert.Empty(scrap.Flush(0));   // nothing more
    }

    [Fact]
    public void TheRosterIsOfferedToTheHostAloneWithoutItsLaterMark()
    {
        var menu = new OrbitMenu(new WorldId(Sol, Earth, Luna), Systems[Sol].EnterOrbit(Earth, Luna));
        Assert.False(menu.Offers(OrbitMenuItem.Roster));    // a guest's: no fleet, it comes later
        Assert.False(menu.Offers(OrbitMenuItem.Starmap));
        Assert.True(menu.Offers(OrbitMenuItem.Cargo));
        int crew = 30;
        menu.Fleet = new TestFleet(new HostFleet(), () => crew, c => crew = c, () => 50);
        Assert.True(menu.Offers(OrbitMenuItem.Roster));     // the host's, drawn since stage 4b item 10
        Assert.False(menu.Offers(OrbitMenuItem.GameMenu));
    }

    [ContentFact]
    public void AGuestsHangarShowsTheHostsFleetAndTheHostsChangesToIt()
    {
        var world = AlliedGame();
        InTheShipyard(world, "host");
        InTheShipyard(world, "guest");
        world.TakeStarbaseDrawing("guest");
        world.TakeStarbaseDrawing("host");
        // The host builds a Spathi into slot 0 and hires a crew member for it.
        Keys(world, "host", MenuKey.Down, MenuKey.Select, MenuKey.Down, MenuKey.Select);
        Assert.Contains(world.TakeStarbaseDrawing("host").SelectMany(d => d.Ops), op => op == new ScreenOp(ScreenOpKind.ShipBuilt, 0));
        var seen = world.TakeStarbaseDrawing("guest");
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ShipBuilt, 0), new ScreenOp(ScreenOpKind.SisDisplay) }, seen.SelectMany(d => d.Ops));
        Assert.Equal(new HangarShip(0, RaceOf(Spathi), 1), Assert.Single(seen.Last().Ship!.Fleet!));
        Keys(world, "host", MenuKey.Up);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ShipCrew, 0), new ScreenOp(ScreenOpKind.SisDisplay) },
            world.TakeStarbaseDrawing("guest").SelectMany(d => d.Ops));
        // Sent away to none and scrapped: the guest's doors close on it.
        Keys(world, "host", MenuKey.Down, MenuKey.Down, MenuKey.Select);
        Assert.Null(world.Fleet.InSlot(0));
        var ops = world.TakeStarbaseDrawing("guest").SelectMany(d => d.Ops).ToList();
        Assert.Contains(new ScreenOp(ScreenOpKind.ShipScrapped, 0, RaceOf(Spathi)), ops);
        Assert.Equal(ScreenOpKind.SisDisplay, ops[^1].Kind);
        // A guest outfitting sees the fleet's ships in its status area drawn afresh too.
        Keys(world, "host", MenuKey.Select, MenuKey.Select);   // the list again, and the Earthling built
        Assert.NotNull(world.Fleet.InSlot(0));
        Keys(world, "guest", MenuKey.Cancel, MenuKey.Cancel, MenuKey.Up, MenuKey.Select);   // out to the starbase's menu, to outfitting
        Assert.Equal(StarbaseScreen.Outfit, world.StarbaseFor("guest")!.Screen);
        world.TakeStarbaseDrawing("guest");
        Keys(world, "host", MenuKey.Up);
        Assert.Contains(world.TakeStarbaseDrawing("guest").SelectMany(d => d.Ops), op => op.Kind == ScreenOpKind.SisDisplay);
    }

    [ContentFact]
    public void AFleetShipsCrewChangedOutsideTheShipyardIsDrawnOnADockedPlayersHangar()
    {
        // A battle's or the roster's crews written to the fleet (SetCrew) change nothing a docked player's own key or
        // the story does: the fleet's version is what's looked at.
        var world = AlliedGame();
        world.Fleet.Restore(Rules, new[] { new FleetShip(Spathi, 10, 5, 2) });
        InTheShipyard(world, "guest");
        world.TakeStarbaseDrawing("guest");
        world.Fleet.SetCrew(Rules, 2, 7);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ShipCrew, 2), new ScreenOp(ScreenOpKind.SisDisplay) },
            world.TakeStarbaseDrawing("guest").SelectMany(d => d.Ops));
        Assert.Empty(world.TakeStarbaseDrawing("guest"));
    }

    // A ship's code for a status alone (nothing loads it).
    private sealed class NoCode : IShipCode
    {
        public RaceDesc Load(Battle battle, Starship starship) => throw new InvalidOperationException();
    }

    [Fact]
    public void TheStatusSentHasTheKeysAndEnergyLabelThePanelDrew()
    {
        const StatusFlags keys = StatusFlags.Left | StatusFlags.Thrust | StatusFlags.Weapon;
        // A ship fleeing (flee_preprocess clears its keys for PreProcessStatus, PostProcessStatus not run): the keys as
        // they are; the energy label as PostProcessStatus last read it.
        var s = new Starship(new NoCode(), 1, 5, 10) { Ship = new Element(0) { CrewLevel = 5 } };
        s.CurStatusFlags = StatusFlags.None;
        s.OldStatusFlags = keys | StatusFlags.LowOnEnergy;
        Assert.Equal(StatusFlags.LowOnEnergy, GameWorld.StatusSnapshot("ilwrath", s).Flags);
        s.CurStatusFlags = keys | StatusFlags.LowOnEnergy;
        s.OldStatusFlags = keys;
        Assert.Equal(keys, GameWorld.StatusSnapshot("ilwrath", s).Flags);
        // Out of crew: the keys PostProcessStatus read before clearing them.
        s.Ship.CrewLevel = 0;
        s.CurStatusFlags = StatusFlags.None;
        s.OldStatusFlags = keys;
        Assert.Equal(keys, GameWorld.StatusSnapshot("ilwrath", s).Flags);
    }

    // ---- the pick screen over the battle

    private sealed class Look : IPickLook
    {
        public string ShipName(string player) => $"ship of {player}";

        public PickShipLit Flagship(string player) => new($"commander of {player}", 30, 50);

        public string? Icons(string ship) => $"{ship}.icons";

        public string Captain(string ship, int captain) => $"{ship} captain {captain}";
    }

    private static BattleSnapshot PickBattle(int frame, BattlePickSnapshot? pick, bool canPick = true) =>
        new(1, 8192, 7680, new BattleShipStatus("ilwrath", 22, 22, 16, 16, 5), 3000, 3000,
            new[]
            {
                new BattleFighterSnapshot("host", new BattleShipStatus("spathi", 4, 30, 0, 10, 7, Gone: true), 1000, 1000, true,
                    new BattleArmadaSnapshot(new[]
                    {
                        new BattleFleetShipSnapshot(0, "spathi", 7, 4, 30, BattleExit.Retreated, true, false),
                        new BattleFleetShipSnapshot(4, "arilou", 9, 6, 6, null, false, canPick),
                    }, pick)),
                new BattleFighterSnapshot("guest", new BattleShipStatus(ShipDescriptions.Flagship, 10, 20, 5, 10), 1500, 1500),
            },
            Array.Empty<BattleElementSnapshot>(), new BattleCamera(0, 0, 0, 256, 0, 0, false, frame), 7);

    private static UqmCanvas PickCanvas() =>
        new(_ => 20, (s, _) => s == PickScreenDrawing.PickSheet ? new URect(-52, -108, 150, 70) : new URect(0, 0, 16, 16),
            new Dictionary<string, int> { ["starcon"] = 11, ["tiny"] = 10 });

    private static StatusShip StatusLook(BattleShipStatus s, string? player) =>
        new("icons", "captain", s.MaxCrew, s.MaxEnergy, false, false, false, s.Ship, "captain");

    [Fact]
    public void ThePickScreenIsDrawnForThePlayerChoosingAsTheCursorMoves()
    {
        var canvas = PickCanvas();
        var screen = new BattleScreen(canvas, StatusLook, new Look());
        // The cursor on the Arilou (row 0, column 5: slot 4): the frame, the ships, its captain and crew.
        Assert.NotNull(screen.Frame(PickBattle(1, new BattlePickSnapshot(0, 5)), "host", BattleSpace.SizeOf));
        var drawn = canvas.Drawn.ToList();
        Assert.Contains(drawn, d => d.Sheet == PickScreenDrawing.PickSheet);
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.Text && d.Text == "ship of host");
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.Text && d.Text == "arilou captain 9");
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.Text && d.Text == "6");
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.FilledStamp && d.Sheet == "spathi.icons");   // fled: blue
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.Stamp && d.Sheet == "arilou.icons");
        var flash = screen.PickFlash!.Value;
        // The same battle frame (held while the host chooses alone), the cursor moved to the flagship: drawn.
        canvas.Drawn.Clear();
        Assert.Empty(screen.Frame(PickBattle(1, new BattlePickSnapshot(0, 3)), "host", BattleSpace.SizeOf)!);
        Assert.Contains(canvas.Drawn, d => d.Kind == UqmDrawKind.Text && d.Text == "commander of host");
        Assert.Contains(canvas.Drawn, d => d.Kind == UqmDrawKind.Text && d.Text == "30/50");
        Assert.NotEqual(flash, screen.PickFlash);
        // Nothing changed: nothing drawn.
        canvas.Drawn.Clear();
        Assert.Null(screen.Frame(PickBattle(1, new BattlePickSnapshot(0, 3)), "host", BattleSpace.SizeOf));
        Assert.Empty(canvas.Drawn);
        // A slot whose ship can't be picked (fled, or taken by a talk): no captain, no crew.
        canvas.Drawn.Clear();
        screen.Frame(PickBattle(2, new BattlePickSnapshot(0, 5), canPick: false), "host", BattleSpace.SizeOf);
        Assert.DoesNotContain(canvas.Drawn, d => d.Kind == UqmDrawKind.Text && d.Text.StartsWith("arilou captain"));
        // Picked: no pick screen, no flash.
        canvas.Drawn.Clear();
        screen.Frame(PickBattle(3, null), "host", BattleSpace.SizeOf);
        Assert.DoesNotContain(canvas.Drawn, d => d.Sheet == PickScreenDrawing.PickSheet);
        Assert.Null(screen.PickFlash);

        // Another player in the battle never sees it: the battle runs on for them.
        var other = PickCanvas();
        var guest = new BattleScreen(other, StatusLook, new Look());
        guest.Frame(PickBattle(1, new BattlePickSnapshot(0, 5)), "guest", BattleSpace.SizeOf);
        Assert.DoesNotContain(other.Drawn, d => d.Sheet == PickScreenDrawing.PickSheet);
        Assert.Null(guest.PickFlash);
        // Nor does a screen without the pick screen's looks.
        var plain = new BattleScreen(PickCanvas(), StatusLook);
        plain.Frame(PickBattle(1, new BattlePickSnapshot(0, 5)), "host", BattleSpace.SizeOf);
        Assert.Null(plain.PickFlash);
    }
}
