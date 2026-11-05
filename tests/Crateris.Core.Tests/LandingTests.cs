// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Dispatching the lander (stage 2b, item 3): DoScan's checks, the fuel a
/// landing costs for every world against UQM's own getLandingFuelNeeded
/// (tests/golden/landing.json), and the landing site picker against
/// DoPickPlanetSide's rules.
/// </summary>
public class LandingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82;
    private static readonly Func<ScanType, uint> NothingTaken = _ => 0;

    private static OrbitData Orbit(int star, int planet, int? moon = null) => Systems[star].EnterOrbit(planet, moon);

    [Fact]
    public void EveryWorldsLandingChecksAndFuelAreUqms()
    {
        var worlds = Golden.Load("landing").GetProperty("worlds").EnumerateArray().ToList();
        Assert.True(worlds.Count > 3000, $"only {worlds.Count} worlds");
        int dispatchable = 0;
        foreach (var w in worlds)
        {
            int star = w[0].GetInt32(), planet = w[1].GetInt32(), moon = w[2].GetInt32();
            string where = $"star {star} planet {planet} moon {moon}";
            var orbit = Orbit(star, planet, moon < 0 ? null : moon);
            Assert.True(orbit.Entered == (w[3].GetInt32() == 1), $"{where}: entered");
            if (!orbit.Entered)
                continue;
            bool can = Landing.World(orbit) == DispatchRefusal.None;
            Assert.True(can == (w[4].GetInt32() == 1), $"{where}: dispatchable");
            Assert.True(Landing.FuelNeeded(orbit.Report!) == (uint)w[5].GetInt32(), $"{where}: fuel");
            // The planetside map is made for the same worlds.
            Assert.True(can == PlanetsideMap.Landable(SurfaceMapTests.World(star, planet, moon).DataIndex, orbit.Report!),
                $"{where}: planetside map");
            if (can)
                dispatchable++;
        }
        Assert.True(dispatchable > 2000, $"only {dispatchable} worlds to land on");
    }

    [Fact]
    public void ALandingCostsTwiceTheGravityUpToThreeUnits()
    {
        var luna = Orbit(Sol, 2, 1).Report!;
        Assert.Equal((uint)luna.SurfaceGravity * 2, Landing.FuelNeeded(luna));
        Assert.Equal(300u, Landing.FuelNeeded(luna with { SurfaceGravity = 151 }));
        Assert.Equal(300u, Landing.FuelNeeded(luna with { SurfaceGravity = 150 }));
        Assert.Equal(298u, Landing.FuelNeeded(luna with { SurfaceGravity = 149 }));
    }

    [Fact]
    public void DoScanChecksTheWorldThenFuelThenLanderThenCrew()
    {
        var luna = Orbit(Sol, 2, 1);
        uint fuel = Landing.FuelNeeded(luna.Report!);
        Assert.Equal(DispatchRefusal.None, Landing.Check(luna, fuel, 1, 1));   // exactly enough fuel
        Assert.Equal(DispatchRefusal.Fuel, Landing.Check(luna, fuel - 1, 0, 0));
        Assert.Equal(DispatchRefusal.NoLander, Landing.Check(luna, fuel, 0, 0));
        Assert.Equal(DispatchRefusal.NoCrew, Landing.Check(luna, fuel, 1, 0));
        Assert.Equal(DispatchRefusal.Shielded, Landing.Check(Orbit(Sol, 2), 0, 0, 0));    // Earth
        Assert.Equal(DispatchRefusal.GasGiant, Landing.Check(Orbit(Sol, 4), 0, 0, 0));    // Jupiter
    }

    [Fact]
    public void TheSiteStartsMidMapAndMovesAPixelAFrame()
    {
        var site = new LandingSite();
        // ScanSystem: (MAP_WIDTH >> 1) << MAG_SHIFT, (MAP_HEIGHT >> 1) << MAG_SHIFT.
        Assert.Equal(((SurfaceMap.Width >> 1) << 2, (SurfaceMap.Height >> 1) << 2), (site.X, site.Y));
        Assert.Equal((SurfaceMap.Width >> 1, SurfaceMap.Height >> 1), site.Cursor);
        site.BeginPick();
        site.Advance(1.0, HeldKeys.Right);   // 40 frames
        Assert.Equal(((SurfaceMap.Width >> 1) + 40) << 2, site.X);
        // Right wins over left, down over up, as DoPickPlanetSide tests them.
        site.Frame(HeldKeys.Left | HeldKeys.Right | HeldKeys.Up | HeldKeys.Down);
        Assert.Equal((((SurfaceMap.Width >> 1) + 41) << 2, ((SurfaceMap.Height >> 1) + 1) << 2), (site.X, site.Y));
        int x = site.X;
        site.Frame(HeldKeys.None);
        Assert.Equal(x, site.X);
    }

    [Fact]
    public void TheSiteWrapsEastAndWestAndStopsAtThePoles()
    {
        var site = new LandingSite();
        site.Move(-(SurfaceMap.Width >> 1), 0);
        Assert.Equal(0, site.X);
        site.Move(-1, 0);
        Assert.Equal(PlanetsideMap.Width - 4, site.X);   // west of the first pixel: the last
        site.Move(1, 0);
        Assert.Equal(0, site.X);

        int top = site.Y >> 2;
        for (int i = 0; i < top + 5; ++i)
            site.Move(0, -1);
        Assert.Equal(0, site.Y);   // a move past the pole leaves it there
        for (int i = 0; i < SurfaceMap.Height + 5; ++i)
            site.Move(0, 1);
        Assert.Equal((SurfaceMap.Height - 1) << 2, site.Y);
        site.Move(1, 1);
        Assert.Equal(((SurfaceMap.Height - 1) << 2, 4), (site.Y, site.X));   // the pole stops only the north-south move
    }

    [Fact]
    public void TheCursorFlashesUpAndDownEverySixteenthOfASecondAndStartsAgainWhenMoved()
    {
        Assert.Equal((21, 52), (LandingSite.TicksPerFrame, LandingSite.FlashTicks));
        var site = new LandingSite();
        site.BeginPick();
        var levels = new List<int>();
        for (int i = 0; i < 80; ++i)
        {
            site.Frame(HeldKeys.None);
            levels.Add(site.FlashLevel);
        }
        // From black, 2 up each 52 ticks (every 2 or 3 picker frames) to 0x1A, then down again.
        Assert.Equal(0, levels[0]);
        Assert.Equal(2, levels[2]);   // the third frame, at tick 63: past 52
        Assert.Equal(LandingSite.FlashMax, levels.Max());
        int peak = levels.IndexOf(LandingSite.FlashMax);
        Assert.True(levels.Take(peak + 1).Zip(levels.Skip(1).Take(peak)).All(p => p.Second >= p.First), "rising");
        Assert.True(levels[peak + 4] < LandingSite.FlashMax, "falling again");
        Assert.All(levels, l => Assert.True(l % 2 == 0 && l >= 0 && l <= LandingSite.FlashMax));

        site.Frame(HeldKeys.Up);
        Assert.Equal(0, site.FlashLevel);   // moved: black again
        Assert.Equal(Rgb.From15(0, 0, 0), site.FlashColor);
    }

    [Fact]
    public void DispatchOpensThePickerAndTheSiteIsKeptUntilTheScanMenuCloses()
    {
        var menu = new OrbitMenu(new WorldId(Sol, 2, 1), Orbit(Sol, 2, 1));
        (uint, int, int)? kit = (1000u, 1, 50);
        Assert.Null(menu.Site);
        menu.Press(MenuKey.Select, NothingTaken);   // Scan
        Assert.NotNull(menu.Site);
        menu.Press(MenuKey.Down, NothingTaken);     // Dispatch lander
        Assert.Equal(MenuOutcome.PickingSite, menu.Press(MenuKey.Select, NothingTaken, () => kit));
        Assert.True(menu.Picking);
        Assert.Equal(MenuOutcome.None, menu.Press(MenuKey.Left, NothingTaken));   // held keys move it, not presses
        menu.Advance(0.25, HeldKeys.Left);
        int x = menu.Site!.X;
        Assert.Equal(((SurfaceMap.Width >> 1) - 10) << 2, x);
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Cancel, NothingTaken));
        Assert.False(menu.Picking);
        Assert.Equal(OrbitMenuItem.DispatchLander, menu.Selected);
        menu.Advance(1, HeldKeys.Left);
        Assert.Equal(x, menu.Site.X);                                           // only while picking

        menu.Press(MenuKey.Select, NothingTaken, () => kit);
        Assert.Equal(x, menu.Site.X);                                           // kept for the scan menu
        Assert.Equal(MenuOutcome.Dispatch, menu.Press(MenuKey.Select, NothingTaken));
        Assert.False(menu.Picking);

        menu.Press(MenuKey.Cancel, NothingTaken);   // the scan menu closes
        Assert.Null(menu.Site);
        menu.Press(MenuKey.Select, NothingTaken);   // and opens again in the middle
        Assert.Equal((SurfaceMap.Width >> 1) << 2, menu.Site!.X);
    }

    [Fact]
    public void DispatchIsRefusedWithoutFuelALanderOrCrew()
    {
        var menu = new OrbitMenu(new WorldId(Sol, 2, 1), Orbit(Sol, 2, 1));
        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(MenuOutcome.Refused, menu.Press(MenuKey.Select, NothingTaken, () => (0u, 1, 50)));
        Assert.Equal(DispatchRefusal.Fuel, menu.LastRefusal);
        Assert.Equal(MenuOutcome.Refused, menu.Press(MenuKey.Select, NothingTaken, () => (1000u, 0, 50)));
        Assert.Equal(DispatchRefusal.NoLander, menu.LastRefusal);
        Assert.Equal(MenuOutcome.Refused, menu.Press(MenuKey.Select, NothingTaken, () => (1000u, 1, 0)));
        Assert.Equal(DispatchRefusal.NoCrew, menu.LastRefusal);
        Assert.False(menu.Picking);
    }

    [ContentFact]   // the host's world needs the contact shapes
    public void TheHostTakesTheFuelAndTheLanderGoes()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        world.PutInOrbit("host", Sol, 2, 1);
        var ship = world.Host.Ship;
        uint cost = Landing.FuelNeeded(ship.Orbit!.Report!);
        Assert.True(cost > 0);
        uint before = ship.Flight.Fuel;
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 100, 100));
        Assert.Equal(before - cost, ship.Flight.Fuel);
        Assert.NotNull(ship.Hold.LanderOut);
        Assert.Equal(DispatchRefusal.LanderOut, world.Dispatch("host", 100, 100));   // it's already down
        Assert.Equal(before - cost, ship.Flight.Fuel);

        world.PutInOrbit("host", Sol, 2);   // Earth: shielded
        Assert.Equal(DispatchRefusal.Shielded, world.Dispatch("host", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Dispatch("host", PlanetsideMap.Width, 0));
    }

    [ContentFact]   // the host's world needs the contact shapes
    public void TheHostRefusesWithTooLittleFuelAndChangesNothing()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        world.PutInOrbit("host", Sol, 2, 1);
        var ship = world.Host.Ship;
        ship.Flight.Fuel = Landing.FuelNeeded(ship.Orbit!.Report!) - 1;
        Assert.Equal(DispatchRefusal.Fuel, world.Dispatch("host", 0, 0));
        Assert.Equal(Landing.FuelNeeded(ship.Orbit.Report!) - 1, ship.Flight.Fuel);
        Assert.Null(ship.Hold.LanderOut);
    }
}
