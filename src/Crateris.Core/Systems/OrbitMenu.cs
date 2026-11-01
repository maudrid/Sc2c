// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/planets.c DoPlanetOrbit and
// PlanetOrbitMenu; planets/scan.c ScanSystem, DoScan, ScanPlanet,
// DrawScannedStuff and GeneratePlanetSide; menu.c DoMenuChooser,
// NextMenuState, PreviousMenuState and the PC menu layout),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Systems;

/// <summary>An entry of the orbit screen's menus, in UQM's PC menu order.</summary>
public enum OrbitMenuItem
{
    // The orbit menu (PM_ALT_SCAN .. PM_ALT_NAVIGATE).
    Scan, Starmap, Manifest, GameMenu, Navigate,
    // The manifest (PM_ALT_CARGO .. PM_ALT_EXIT_MANIFEST).
    Cargo, Devices, Roster, ExitManifest,
    // The scan menu (PM_ALT_MSCAN .. PM_ALT_EXIT_SCAN).
    MineralScan, EnergyScan, BiologicalScan, AutoScan, DispatchLander, ExitScan,
}

/// <summary>The menu keys (UQM's KEY_MENU_*).</summary>
public enum MenuKey { Up, Down, Left, Right, Select, Cancel }

/// <summary>What a key did.</summary>
public enum MenuOutcome
{
    /// <summary>Nothing (a key the menu ignores here).</summary>
    None,
    /// <summary>The cursor moved, or a menu opened or closed.</summary>
    Moved,
    /// <summary>UQM refuses this here (MENU_SOUND_FAILURE): scanning a shielded world, landing on a gas giant.</summary>
    Refused,
    /// <summary>The entry works in a later stage (the starmap, the manifest's entries, the game menu, the lander).</summary>
    NotYet,
    /// <summary>A scan started (see <see cref="OrbitMenu.Scan"/>).</summary>
    ScanStarted,
    /// <summary>A key during a scan finished the scan type being drawn.</summary>
    ScanSkipped,
    /// <summary>Navigate: leave orbit. The client tells the host.</summary>
    LeaveOrbit,
}

/// <summary>
/// The orbit screen's menus and scans for one visit to a world, as UQM's
/// PC menus work them: the orbit menu (Scan, Starmap, Manifest, Game menu,
/// Navigate), the manifest (Cargo, Devices, Roster, Exit) and the scan menu
/// (Mineral, Energy, Biological, Auto, Dispatch lander, Exit). The cursor
/// wraps round. Entries for later stages are there and say so.
///
/// This is the player's own screen: nothing here goes to the host except
/// leaving orbit. Scans aren't stored (as in UQM): a scan shows the nodes
/// left at that moment, given by the host's taken-node masks, and shown
/// nodes another player takes disappear (<see cref="Dots"/>).
/// </summary>
public sealed class OrbitMenu
{
    public enum MenuPage { Orbit, Manifest, Scan }

    private static readonly OrbitMenuItem[] OrbitPage =
        { OrbitMenuItem.Scan, OrbitMenuItem.Starmap, OrbitMenuItem.Manifest, OrbitMenuItem.GameMenu, OrbitMenuItem.Navigate };
    private static readonly OrbitMenuItem[] ManifestPage =
        { OrbitMenuItem.Cargo, OrbitMenuItem.Devices, OrbitMenuItem.Roster, OrbitMenuItem.ExitManifest };
    private static readonly OrbitMenuItem[] ScanPage =
    {
        OrbitMenuItem.MineralScan, OrbitMenuItem.EnergyScan, OrbitMenuItem.BiologicalScan,
        OrbitMenuItem.AutoScan, OrbitMenuItem.DispatchLander, OrbitMenuItem.ExitScan,
    };

    private readonly OrbitData _orbit;
    private int _cursor;

    public OrbitMenu(WorldId world, OrbitData orbit)
    {
        if (!orbit.Entered)
            throw new ArgumentException($"{world} can't be orbited", nameof(orbit));
        World = world;
        _orbit = orbit;
        Scan = new OrbitScan(orbit.Nodes);
    }

    /// <summary>The world orbited.</summary>
    public WorldId World { get; }

    /// <summary>What entering orbit gave: the planet report and the nodes.</summary>
    public OrbitData Orbit => _orbit;

    public MenuPage Page { get; private set; } = MenuPage.Orbit;

    /// <summary>The entries of the open menu, in order.</summary>
    public IReadOnlyList<OrbitMenuItem> Items => Page switch
    {
        MenuPage.Orbit => OrbitPage,
        MenuPage.Manifest => ManifestPage,
        _ => ScanPage,
    };

    /// <summary>The highlighted entry.</summary>
    public OrbitMenuItem Selected => Items[_cursor];

    /// <summary>The scan display (shown while the scan menu is open).</summary>
    public OrbitScan Scan { get; }

    /// <summary>A shielded world can't be scanned (PLANET_SHIELDED).</summary>
    public bool Shielded => _orbit.Nodes is null;

    /// <summary>The lander can't go down to a gas giant (GAS_GIANT_ATMOSPHERE).</summary>
    public bool GasGiant => _orbit.Report?.AtmoDensity == PlanetInfo.GasGiantAtmosphere;

    /// <summary>
    /// Whether an entry does something in this stage. The others are
    /// shown but say they come later (<see cref="MenuOutcome.NotYet"/>).
    /// </summary>
    public static bool Available(OrbitMenuItem item) => item is not
        (OrbitMenuItem.Starmap or OrbitMenuItem.GameMenu or OrbitMenuItem.Cargo
         or OrbitMenuItem.Devices or OrbitMenuItem.Roster or OrbitMenuItem.DispatchLander);

    /// <summary>
    /// One key press. <paramref name="taken"/> gives the host's current
    /// taken-node mask for a scan type of this world; it's read when a scan
    /// starts.
    /// </summary>
    public MenuOutcome Press(MenuKey key, Func<ScanType, uint> taken)
    {
        if (Scan.Sweeping)
        {
            // WaitForAnyButtonUntil: any key finishes the scan being drawn.
            Scan.Skip();
            AfterScan();
            return MenuOutcome.ScanSkipped;
        }
        switch (key)
        {
            case MenuKey.Up:
            case MenuKey.Left:
                _cursor = (_cursor + Items.Count - 1) % Items.Count;
                return MenuOutcome.Moved;
            case MenuKey.Down:
            case MenuKey.Right:
                _cursor = (_cursor + 1) % Items.Count;
                return MenuOutcome.Moved;
            case MenuKey.Cancel:
                return Cancel();
            case MenuKey.Select:
                return Choose(taken);
        }
        return MenuOutcome.None;
    }

    /// <summary>Time passes (draws the scan line by line).</summary>
    public void Advance(double seconds)
    {
        Scan.Advance(seconds);
        AfterScan();
    }

    // DoScan: once an auto scan is done, the cursor moves to Dispatch lander.
    private void AfterScan()
    {
        if (!Scan.AutoFinished)
            return;
        Scan.AutoFinished = false;
        if (Page == MenuPage.Scan)
            _cursor = IndexOf(ScanPage, OrbitMenuItem.DispatchLander);
    }

    private MenuOutcome Cancel()
    {
        switch (Page)
        {
            case MenuPage.Scan:
                CloseScan();
                return MenuOutcome.Moved;
            case MenuPage.Manifest:
                Open(MenuPage.Orbit, OrbitMenuItem.Manifest);
                return MenuOutcome.Moved;
            default:
                return MenuOutcome.None;   // DoPlanetOrbit ignores cancel
        }
    }

    private MenuOutcome Choose(Func<ScanType, uint> taken)
    {
        OrbitMenuItem item = Selected;
        switch (item)
        {
            case OrbitMenuItem.Scan:
                // ScanSystem: the cursor starts on Auto, and what was found
                // earlier in this visit is shown again.
                Open(MenuPage.Scan, OrbitMenuItem.AutoScan);
                Scan.ShowFound();
                return MenuOutcome.Moved;
            case OrbitMenuItem.Manifest:
                Open(MenuPage.Manifest, OrbitMenuItem.Cargo);
                return MenuOutcome.Moved;
            case OrbitMenuItem.ExitManifest:
                Open(MenuPage.Orbit, OrbitMenuItem.Manifest);
                return MenuOutcome.Moved;
            case OrbitMenuItem.Navigate:
                return MenuOutcome.LeaveOrbit;
            case OrbitMenuItem.ExitScan:
                CloseScan();
                return MenuOutcome.Moved;
            case OrbitMenuItem.DispatchLander:
                if (Shielded || GasGiant)
                    return MenuOutcome.Refused;
                return MenuOutcome.NotYet;   // the lander is stage 2b
            case OrbitMenuItem.MineralScan:
            case OrbitMenuItem.EnergyScan:
            case OrbitMenuItem.BiologicalScan:
            case OrbitMenuItem.AutoScan:
                if (Shielded)
                    return MenuOutcome.Refused;
                Scan.Start(item == OrbitMenuItem.AutoScan ? null : (ScanType)(item - OrbitMenuItem.MineralScan), taken);
                return MenuOutcome.ScanStarted;
            default:
                return MenuOutcome.NotYet;
        }
    }

    private void CloseScan()
    {
        Scan.Hide();
        Open(MenuPage.Orbit, OrbitMenuItem.Scan);
    }

    private void Open(MenuPage page, OrbitMenuItem at)
    {
        Page = page;
        _cursor = Math.Max(0, IndexOf(Items, at));
    }

    private static int IndexOf(IReadOnlyList<OrbitMenuItem> items, OrbitMenuItem item)
    {
        for (int i = 0; i < items.Count; ++i)
        {
            if (items[i] == item)
                return i;
        }
        return -1;
    }
}

/// <summary>A node as drawn on the scan map.</summary>
/// <param name="Frame">
/// The dot's image: for minerals 0..4 within its category's five sizes,
/// for energy and life 0..3 (UQM's NUM_SCANDOT_TRANSITIONS images).
/// </param>
/// <param name="Flash">While the scan line has just passed it, the flash colour; null once drawn normally.</param>
public readonly record struct ScanDot(ScanType Scan, int Node, SurfaceNode Info, int Frame, Rgb? Flash);

/// <summary>
/// The scan map for one visit to a world: which scan types have been done
/// (and what they found), the line-by-line sweep of the scan being drawn,
/// and the dots to draw (ScanPlanet and DrawScannedStuff).
/// </summary>
public sealed class OrbitScan
{
    /// <summary>NUM_FLASH_COLORS: lines a dot flashes for after the scan line reaches it.</summary>
    public const int FlashLines = 8;

    /// <summary>SCAN_LINES: the map's lines, then the flashing, then the final frame.</summary>
    public const int Lines = Surface.MapHeight + FlashLines + 1;

    /// <summary>UQM's clock ticks a second (ONE_SECOND).</summary>
    public const int TicksPerSecond = 840;

    /// <summary>SCAN_LINE_WAIT: ticks a line (SCAN_DURATION, 7/4 of a second, over SCAN_LINES).</summary>
    public const int TicksPerLine = TicksPerSecond * 7 / 4 / Lines;

    /// <summary>NUM_SCANDOT_TRANSITIONS: images in an energy or life dot's flash and animation.</summary>
    public const int DotTransitions = 4;

    /// <summary>The scan line colours of the map, by scan type (SCAN_*_TINT_COLOR).</summary>
    public static readonly IReadOnlyList<Rgb> TintColors = new[]
    {
        Rgb.From15(0x1F, 0x00, 0x00),                                         // BRIGHT_RED_COLOR
        Rgb.From15(0x1F, 0x1F, 0x1F),                                         // WHITE_COLOR
        Rgb.From15(0x00, 0x1F, 0x00),                                         // BRIGHT_GREEN_COLOR
    };

    /// <summary>The scan names' colours, by scan type (SCAN_*_TEXT_COLOR).</summary>
    public static readonly IReadOnlyList<Rgb> TextColors = new[]
    {
        Rgb.From15(0x13, 0x00, 0x00),
        Rgb.From15(0x0C, 0x0C, 0x0C),
        Rgb.From15(0x00, 0x0E, 0x00),
    };

    private readonly SurfaceNodes? _nodes;
    private readonly IReadOnlyList<(int Node, SurfaceNode Info)>?[] _found = new IReadOnlyList<(int, SurfaceNode)>?[3];
    private readonly Queue<ScanType> _queue = new();
    private bool _auto;
    private int _ticks;
    private double _carry;
    private Func<ScanType, uint>? _takenAt;

    public OrbitScan(SurfaceNodes? nodes) => _nodes = nodes;

    /// <summary>Whether the map is showing (the scan menu is open).</summary>
    public bool Visible { get; private set; }

    /// <summary>The scan type being drawn or last drawn; null for the plain map (after an auto scan, or none yet).</summary>
    public ScanType? Tint { get; private set; }

    /// <summary>Whether a scan is being drawn line by line.</summary>
    public bool Sweeping => Current is not null;

    /// <summary>The scan type being drawn line by line.</summary>
    public ScanType? Current { get; private set; }

    /// <summary>
    /// The last line drawn (0 to <see cref="Lines"/> - 1), -1 before the
    /// first wait of a scan; the last line once a scan is done.
    /// </summary>
    public int Line { get; private set; } = Lines - 1;

    /// <summary>Whether the auto scan has just finished all three types (the menu then moves to Dispatch lander).</summary>
    internal bool AutoFinished { get; set; }

    /// <summary>Whether a scan type has been done this visit.</summary>
    public bool Found(ScanType scan) => _found[(int)scan] is not null;

    internal void ShowFound()
    {
        Visible = true;
        Tint = null;
    }

    internal void Hide()
    {
        Visible = false;
        Tint = null;
    }

    /// <summary>
    /// Starts one scan type, or all three in turn (auto scan, null). Each
    /// type finds the nodes left when its sweep starts.
    /// </summary>
    internal void Start(ScanType? scan, Func<ScanType, uint> taken)
    {
        if (_nodes is null)
            throw new InvalidOperationException("a shielded world can't be scanned");
        _queue.Clear();
        _auto = scan is null;
        if (scan is ScanType one)
            _queue.Enqueue(one);
        else
        {
            _queue.Enqueue(ScanType.Mineral);
            _queue.Enqueue(ScanType.Energy);
            _queue.Enqueue(ScanType.Biological);
        }
        _takenAt = taken;
        Visible = true;
        Next();
    }

    private void Next()
    {
        if (_queue.Count == 0)
        {
            Current = null;
            Line = Lines - 1;
            if (_auto)
            {
                Tint = null;   // the auto scan ends on the plain map with everything found
                AutoFinished = true;
                _auto = false;
            }
            return;
        }
        ScanType scan = _queue.Dequeue();
        _found[(int)scan] = PlanetWorldState.Remaining(_takenAt!(scan), NodesOf(scan));
        Current = scan;
        Tint = scan;
        Line = -1;   // ScanPlanet draws line i after (i + 1) waits
        _ticks = 0;
        _carry = 0;
    }

    /// <summary>A key during the sweep: the scan type being drawn is drawn finished, and the next starts.</summary>
    internal void Skip()
    {
        if (Current is null)
            return;
        Next();
    }

    internal void Advance(double seconds)
    {
        if (Current is null)
            return;
        _carry += seconds * TicksPerSecond;
        int whole = (int)_carry;
        _carry -= whole;
        _ticks += whole;
        // Line i is drawn once (i + 1) line waits have passed; the scan
        // type ends as its last line is drawn.
        while (Current is not null && _ticks >= (Line + 2) * TicksPerLine)
        {
            Line++;
            if (Line == Lines - 1)
            {
                int over = _ticks - Lines * TicksPerLine;
                Next();
                _ticks = Current is null ? 0 : over;
            }
        }
    }

    private IReadOnlyList<SurfaceNode> NodesOf(ScanType scan) => scan switch
    {
        ScanType.Mineral => _nodes!.Minerals,
        ScanType.Energy => _nodes!.Energy,
        _ => _nodes!.Life,
    };

    /// <summary>
    /// The dots to draw now. Only found nodes still on the surface are
    /// drawn (<paramref name="taken"/> is the host's current mask, so a
    /// node taken since the scan disappears). While a scan type is drawn
    /// or shown tinted, only its dots are; otherwise all found ones.
    /// </summary>
    public IEnumerable<ScanDot> Dots(Func<ScanType, uint> taken)
    {
        if (!Visible)
            yield break;
        for (int s = 0; s < 3; ++s)
        {
            var scan = (ScanType)s;
            if (_found[s] is not { } found || (Tint is { } t && t != scan))
                continue;
            uint mask = taken(scan);
            foreach (var (node, info) in found)
            {
                if (node < PlanetWorldState.MaxNodes && (mask & (1u << node)) != 0)
                    continue;
                int final = FinalFrame(scan, info);
                if (Current != scan)
                {
                    yield return new ScanDot(scan, node, info, final, null);
                    continue;
                }
                int dy = Line - info.Y;
                if (dy < 0)
                    continue;   // the line hasn't reached it
                if (dy >= FlashLines)
                {
                    yield return new ScanDot(scan, node, info, final, null);
                    continue;
                }
                // Flash from white towards the scan's contrast colour,
                // growing from the smallest image to the node's size.
                byte grad = (byte)(0xFF - 0xFF * dy / (FlashLines - 1));
                Rgb c = scan switch
                {
                    ScanType.Mineral => new Rgb(grad, 0xFF, 0xFF),
                    ScanType.Energy => new Rgb(0xFF, grad, grad),
                    _ => new Rgb(0xFF, grad, 0xFF),
                };
                yield return new ScanDot(scan, node, info, Math.Min(dy, final), c);
            }
        }
    }

    /// <summary>
    /// A dot's normal image. Minerals: the deposit's size class plus one
    /// (GeneratePlanetSide: LOBYTE (density) + 1) within the category's
    /// five images; energy and life: the last transition.
    /// </summary>
    public static int FinalFrame(ScanType scan, SurfaceNode info) =>
        scan == ScanType.Mineral ? (info.Density & 0xFF) + 1 : DotTransitions - 1;
}
