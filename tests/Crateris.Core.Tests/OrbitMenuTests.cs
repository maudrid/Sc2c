// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The orbit screen's menus and scans, as UQM's PC menus and ScanPlanet
/// work them, with the scan showing what is left on the host right now.
/// </summary>
public class OrbitMenuTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82;
    private static readonly Func<ScanType, uint> NothingTaken = _ => 0;

    private static OrbitMenu Orbit(int star, int planet, int? moon = null) =>
        new(new WorldId(star, planet, moon), Systems[star].EnterOrbit(planet, moon));

    private static OrbitMenu Luna() => Orbit(Sol, 2, 1);

    // Seconds for n scan lines (SCAN_LINE_WAIT ticks each).
    private static double Lines(int n) => (double)n * OrbitScan.TicksPerLine / OrbitScan.TicksPerSecond;

    [Fact]
    public void OrbitMenuIsUqmsPcMenuAndWrapsRound()
    {
        var menu = Luna();
        Assert.Equal(new[] { OrbitMenuItem.Scan, OrbitMenuItem.Starmap, OrbitMenuItem.Manifest,
            OrbitMenuItem.GameMenu, OrbitMenuItem.Navigate }, menu.Items);
        Assert.Equal(OrbitMenuItem.Scan, menu.Selected);
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Up, NothingTaken));
        Assert.Equal(OrbitMenuItem.Navigate, menu.Selected);
        menu.Press(MenuKey.Right, NothingTaken);
        Assert.Equal(OrbitMenuItem.Scan, menu.Selected);
        Assert.Equal(MenuOutcome.None, menu.Press(MenuKey.Cancel, NothingTaken));
    }

    [Fact]
    public void NavigateLeavesOrbit()
    {
        var menu = Luna();
        menu.Press(MenuKey.Up, NothingTaken);
        Assert.Equal(MenuOutcome.LeaveOrbit, menu.Press(MenuKey.Select, NothingTaken));
    }

    [Fact]
    public void ScanMenuOpensOnAutoScanAndClosesBackOnScan()
    {
        var menu = Luna();
        menu.Press(MenuKey.Select, NothingTaken);
        Assert.Equal(OrbitMenu.MenuPage.Scan, menu.Page);
        Assert.Equal(new[] { OrbitMenuItem.MineralScan, OrbitMenuItem.EnergyScan, OrbitMenuItem.BiologicalScan,
            OrbitMenuItem.AutoScan, OrbitMenuItem.DispatchLander, OrbitMenuItem.ExitScan }, menu.Items);
        Assert.Equal(OrbitMenuItem.AutoScan, menu.Selected);
        Assert.True(menu.Scan.Visible);

        menu.Press(MenuKey.Cancel, NothingTaken);
        Assert.Equal(OrbitMenu.MenuPage.Orbit, menu.Page);
        Assert.Equal(OrbitMenuItem.Scan, menu.Selected);
        Assert.False(menu.Scan.Visible);

        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Down, NothingTaken);
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(OrbitMenuItem.ExitScan, menu.Selected);
        menu.Press(MenuKey.Select, NothingTaken);
        Assert.Equal(OrbitMenu.MenuPage.Orbit, menu.Page);
    }

    [Fact]
    public void EntriesForLaterStagesSaySo()
    {
        var menu = Luna();
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select, NothingTaken));   // Starmap
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Select, NothingTaken));    // Manifest opens
        Assert.Equal(new[] { OrbitMenuItem.Cargo, OrbitMenuItem.Devices, OrbitMenuItem.Roster, OrbitMenuItem.ExitManifest },
            menu.Items);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            Assert.False(OrbitMenu.Available(menu.Selected));
            Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select, NothingTaken));
            menu.Press(MenuKey.Down, NothingTaken);
        }
        menu.Press(MenuKey.Select, NothingTaken);                                     // Exit
        Assert.Equal(OrbitMenuItem.Manifest, menu.Selected);
        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Cancel, NothingTaken);                                     // cancel leaves it too
        Assert.Equal(OrbitMenuItem.Manifest, menu.Selected);
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select, NothingTaken));   // Game menu

        menu.Press(MenuKey.Down, NothingTaken);
        menu.Press(MenuKey.Down, NothingTaken);
        menu.Press(MenuKey.Select, NothingTaken);                                     // Scan
        menu.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(OrbitMenuItem.DispatchLander, menu.Selected);
        Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select, NothingTaken));   // the lander is stage 2b
    }

    [Fact]
    public void ShieldedWorldsCantBeScannedAndGasGiantsCantBeLandedOn()
    {
        var earth = Orbit(Sol, 2);
        Assert.True(earth.Shielded);
        earth.Press(MenuKey.Select, NothingTaken);
        Assert.Equal(MenuOutcome.Refused, earth.Press(MenuKey.Select, NothingTaken));   // Auto scan
        Assert.False(earth.Scan.Sweeping);
        earth.Press(MenuKey.Down, NothingTaken);
        Assert.Equal(MenuOutcome.Refused, earth.Press(MenuKey.Select, NothingTaken));   // Dispatch

        var jupiter = Orbit(Sol, 4);
        Assert.True(jupiter.GasGiant);
        jupiter.Press(MenuKey.Select, NothingTaken);
        Assert.Equal(MenuOutcome.ScanStarted, jupiter.Press(MenuKey.Select, NothingTaken));
        jupiter.Advance(10);
        Assert.Equal(OrbitMenuItem.DispatchLander, jupiter.Selected);
        Assert.Equal(MenuOutcome.Refused, jupiter.Press(MenuKey.Select, NothingTaken));
    }

    [Fact]
    public void ScanIsDrawnLineByLineAtUqmsSpeed()
    {
        Assert.Equal(84, OrbitScan.Lines);
        Assert.Equal(17, OrbitScan.TicksPerLine);

        var menu = Luna();
        var minerals = Systems[Sol].EnterOrbit(2, 1).Nodes!.Minerals;
        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Up, NothingTaken);
        menu.Press(MenuKey.Up, NothingTaken);
        menu.Press(MenuKey.Up, NothingTaken);
        Assert.Equal(OrbitMenuItem.MineralScan, menu.Selected);
        Assert.Equal(MenuOutcome.ScanStarted, menu.Press(MenuKey.Select, NothingTaken));
        Assert.Equal(ScanType.Mineral, menu.Scan.Current);
        Assert.Equal(0, menu.Scan.Line);

        // A node appears when the line reaches it, flashes white to
        // turquoise for eight lines as it grows, then is drawn normally.
        var node = minerals.Select((n, i) => (n, i)).First(x => x.n.Y > 10 && x.n.Y < 60);
        int y = node.n.Y;
        menu.Advance(Lines(y) - 0.5 / OrbitScan.TicksPerSecond);
        Assert.Equal(y - 1, menu.Scan.Line);
        Assert.DoesNotContain(menu.Scan.Dots(NothingTaken), d => d.Node == node.i);
        menu.Advance(Lines(1));
        var dot = Assert.Single(menu.Scan.Dots(NothingTaken), d => d.Node == node.i);
        Assert.Equal(new Rgb(0xFF, 0xFF, 0xFF), dot.Flash);
        Assert.Equal(0, dot.Frame);
        menu.Advance(Lines(OrbitScan.FlashLines - 1));
        dot = Assert.Single(menu.Scan.Dots(NothingTaken), d => d.Node == node.i);
        Assert.Equal(new Rgb(0, 0xFF, 0xFF), dot.Flash);
        menu.Advance(Lines(1));
        dot = Assert.Single(menu.Scan.Dots(NothingTaken), d => d.Node == node.i);
        Assert.Null(dot.Flash);
        Assert.Equal((node.n.Density & 0xFF) + 1, dot.Frame);

        // The whole scan takes SCAN_LINES line waits, 1.7 seconds.
        Assert.True(menu.Scan.Sweeping);
        menu.Advance(Lines(OrbitScan.Lines) - Lines(y + OrbitScan.FlashLines + 1) - 0.5 / OrbitScan.TicksPerSecond);
        Assert.True(menu.Scan.Sweeping);
        menu.Advance(1.0 / OrbitScan.TicksPerSecond);   // one clock tick more
        Assert.False(menu.Scan.Sweeping);
        Assert.Equal(ScanType.Mineral, menu.Scan.Tint);   // the map stays tinted with this scan
        Assert.Equal(minerals.Count, menu.Scan.Dots(NothingTaken).Count());
        Assert.Equal(OrbitMenuItem.MineralScan, menu.Selected);
    }

    [Fact]
    public void AutoScanDoesAllThreeThenMovesToTheLander()
    {
        var menu = Luna();
        var nodes = Systems[Sol].EnterOrbit(2, 1).Nodes!;
        menu.Press(MenuKey.Select, NothingTaken);
        Assert.Equal(MenuOutcome.ScanStarted, menu.Press(MenuKey.Select, NothingTaken));
        Assert.Equal(OrbitMenuItem.DispatchLander, menu.Selected);
        foreach (var scan in new[] { ScanType.Mineral, ScanType.Energy, ScanType.Biological })
        {
            Assert.Equal(scan, menu.Scan.Current);
            Assert.All(menu.Scan.Dots(NothingTaken), d => Assert.Equal(scan, d.Scan));
            menu.Advance(Lines(OrbitScan.Lines));
        }
        Assert.False(menu.Scan.Sweeping);
        Assert.Null(menu.Scan.Tint);   // the plain map, with everything found
        var dots = menu.Scan.Dots(NothingTaken).ToList();
        Assert.Equal(nodes.Minerals.Count + nodes.Energy.Count + nodes.Life.Count, dots.Count);
        Assert.Equal(10, dots.Count(d => d.Scan == ScanType.Biological));   // the Brainbox Bulldozers
    }

    [Fact]
    public void AKeyFinishesTheScanBeingDrawn()
    {
        var menu = Luna();
        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Select, NothingTaken);    // Auto
        menu.Advance(Lines(10));
        Assert.Equal(MenuOutcome.ScanSkipped, menu.Press(MenuKey.Down, NothingTaken));
        Assert.Equal(ScanType.Energy, menu.Scan.Current);
        Assert.Equal(0, menu.Scan.Line);
        Assert.Equal(OrbitMenuItem.DispatchLander, menu.Selected);   // the key only finished the scan
        menu.Press(MenuKey.Select, NothingTaken);
        menu.Press(MenuKey.Select, NothingTaken);
        Assert.False(menu.Scan.Sweeping);
        Assert.True(menu.Scan.Found(ScanType.Biological));
    }

    [Fact]
    public void ScansShowWhatIsLeftRightNow()
    {
        var menu = Luna();
        var life = Systems[Sol].EnterOrbit(2, 1).Nodes!.Life;
        uint taken = 0b11;   // a friend on the surface has taken nodes 0 and 1
        Func<ScanType, uint> host = s => s == ScanType.Biological ? taken : 0;

        menu.Press(MenuKey.Select, host);
        menu.Press(MenuKey.Left, host);    // Biological
        Assert.Equal(OrbitMenuItem.BiologicalScan, menu.Selected);
        menu.Press(MenuKey.Select, host);
        menu.Advance(Lines(OrbitScan.Lines));
        Assert.Equal(life.Count - 2, menu.Scan.Dots(host).Count());

        // They take another: it disappears from the map.
        taken |= 0b100;
        Assert.Equal(life.Count - 3, menu.Scan.Dots(host).Count());
        Assert.DoesNotContain(menu.Scan.Dots(host), d => d.Node == 2);

        // Leaving the scan menu hides the map; coming back shows what was found.
        menu.Press(MenuKey.Cancel, host);
        Assert.Empty(menu.Scan.Dots(host));
        menu.Press(MenuKey.Select, host);
        Assert.Null(menu.Scan.Tint);
        Assert.Equal(life.Count - 3, menu.Scan.Dots(host).Count());
    }

    [Fact]
    public void AWorldThatCantBeOrbitedHasNoOrbitMenu()
    {
        var sol = Systems[Sol];
        int starbase = Array.FindIndex(sol.Moons(2), m => !WorldTypes.HasPlanetData(m.DataIndex));
        Assert.Throws<ArgumentException>(() => Orbit(Sol, 2, starbase));
    }
}
