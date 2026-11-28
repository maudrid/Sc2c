// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (menu.c DoMenuChooser, NextMenuState,
// PreviousMenuState and the PC menu layout; hyper.c DoHyperspaceMenu and
// HyperspaceMenu; planets/solarsys.c DoSolarSysMenu and SolarSysMenu;
// planets/planets.c DoPlanetOrbit's manifest; planets/devices.c
// DevicesMenu; planets/cargo.c CargoMenu; roster.c RosterMenu),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// What the orbit menu (<see cref="OrbitMenu"/>) and the hyperspace and
/// system menus (<see cref="FlightMenu"/>) share, as UQM's PC menus do:
/// the cursor going round the open page's entries, and the Manifest
/// (Cargo, Devices, Roster, Exit) with its cargo display, Devices list and
/// the host's Roster.
/// </summary>
public abstract class ShipMenu
{
    public enum MenuPage { Orbit, Manifest, Scan }

    /// <summary>The Manifest (PM_ALT_CARGO .. PM_ALT_EXIT_MANIFEST).</summary>
    protected static readonly OrbitMenuItem[] ManifestPage =
        { OrbitMenuItem.Cargo, OrbitMenuItem.Devices, OrbitMenuItem.Roster, OrbitMenuItem.ExitManifest };

    /// <summary>The page open: <see cref="MenuPage.Orbit"/> the menu's first (the orbit menu, or the hyperspace and system menu's), the Manifest, or orbit's scan menu.</summary>
    public MenuPage Page { get; protected set; } = MenuPage.Orbit;

    /// <summary>The cursor's place on the open page.</summary>
    protected int Cursor { get; set; }

    /// <summary>The entries of the open menu, in order.</summary>
    public abstract IReadOnlyList<OrbitMenuItem> Items { get; }

    /// <summary>The highlighted entry.</summary>
    public OrbitMenuItem Selected => Items[Cursor];

    /// <summary>
    /// Whether an entry does something in this stage for anyone. The others
    /// are shown but say they come later (<see cref="MenuOutcome.NotYet"/>).
    /// The Roster is the host's (<see cref="Offers"/>).
    /// </summary>
    public static bool Available(OrbitMenuItem item) => item is not
        (OrbitMenuItem.Starmap or OrbitMenuItem.GameMenu or OrbitMenuItem.Roster);

    /// <summary>
    /// Whether an entry does something for this player: those
    /// <see cref="Available"/>, and the Roster for the host (with
    /// <see cref="Fleet"/>: worked since stage 4b item 7, drawn since item
    /// 10); a guest's Roster still says it comes later (their own escorts
    /// aren't a fleet).
    /// </summary>
    public bool Offers(OrbitMenuItem item) => Available(item) || item == OrbitMenuItem.Roster && Fleet is not null;

    /// <summary>
    /// The devices on this player's Devices list now (InventoryDevices: the
    /// shared ones and their own ship's, by the host's last snapshot); none
    /// if not given.
    /// </summary>
    public Func<IReadOnlyList<int>>? Inventory { get; set; }

    /// <summary>
    /// The host's fleet, for the Manifest's Roster (stage 4b item 7); null
    /// for a guest, who has none (stage 4b, decision 3): their Roster says
    /// it comes later, as before.
    /// </summary>
    public IRosterFleet? Fleet { get; set; }

    // The fleet ships the open roster was opened on: each one's race, slot and captain, in the queue's order.
    private IReadOnlyList<(string Race, int Slot, byte Captain)> _rosterShips = Array.Empty<(string, int, byte)>();

    private List<(string Race, int Slot, byte Captain)> RosterShips() =>
        Fleet!.Ships.Take(RosterMenu.ShipPoints.Count).Select(s => (s.Race, s.Slot, s.Captain)).ToList();

    // RosterMenu on the fleet as it is (none, closed, with no ships), the cursor on the first.
    private void OpenRoster()
    {
        _rosterShips = RosterShips();
        Roster = _rosterShips.Count == 0 ? null : new RosterMenu(_rosterShips.Count);
    }

    /// <summary>
    /// The Manifest's Roster, while it's open (RosterMenu); null when closed. It opens afresh on the next key
    /// when the fleet's ships have changed (a talk's), so between that change and the key it's the fleet's as
    /// it was: whatever draws it (stage 4b item 10) reads its ships by <see cref="RosterMenu.Selected"/> with care.
    /// </summary>
    public RosterMenu? Roster { get; private set; }

    /// <summary>The Manifest's Devices list, while it's open (DevicesMenu); null when closed.</summary>
    public DevicesMenu? Devices { get; private set; }

    /// <summary>The devices listed, while the list is open.</summary>
    public IReadOnlyList<int>? DeviceList => Devices?.List;

    /// <summary>The device lit, while the list is open.</summary>
    public int DeviceCursor => Devices?.Cursor ?? 0;

    /// <summary>The first row shown, while the list is open.</summary>
    public int DeviceTop => Devices?.Top ?? 0;

    /// <summary>
    /// The devices on the list now, while it's open: the list follows them,
    /// the cursor staying on its device if it's still there (else where it
    /// was, within the list), and closes if none are left.
    /// </summary>
    public void RefreshDevices(IReadOnlyList<int> list)
    {
        if (Devices is null || Devices.List.SequenceEqual(list))
            return;
        Devices = list.Count == 0 ? null : Devices.With(list);
    }

    /// <summary>
    /// Closes the Devices list, back at the Manifest with Devices lit: the
    /// game unable to draw it, or a device used (DoManipulateDevices leaves
    /// the list on a use; DevicesMenu then returns to the menu it came from).
    /// </summary>
    public void CloseDevices() => Devices = null;

    /// <summary>
    /// Whether the Manifest's cargo display is open (CargoMenu): the
    /// direction keys choose an element category, select drops a unit of
    /// it, cancel goes back to the Manifest.
    /// </summary>
    public bool CargoOpen { get; private set; }

    /// <summary>The element category the cargo display has lit, 0 to 7 (it opens on the first).</summary>
    public int CargoSelected { get; private set; }

    /// <summary>A key for the Manifest's open display (the Devices list, the Roster, the cargo display), or null if none is open.</summary>
    protected MenuOutcome? PressManifest(MenuKey key)
    {
        if (Devices is not null)
        {
            var outcome = Devices.Press(key);
            if (outcome == MenuOutcome.Moved && Devices.Left)
                Devices = null;
            return outcome;
        }
        if (Roster is not null)
        {
            // A talk (a guest's, in co-op) may add or take fleet ships while the roster is open, which UQM's can't:
            // the roster opens afresh on the fleet as it is (closing if none are left), the key taken by it. The
            // ships are told by race, slot and captain (their crew is the roster's to change), so one taken and
            // another given in its place between two keys counts too.
            if (!RosterShips().SequenceEqual(_rosterShips))
            {
                OpenRoster();
                return MenuOutcome.Moved;
            }
            var outcome = Roster.Press(key, Fleet!);
            if (Roster.Left)
                Roster = null;   // RosterMenu returns: back at the Manifest
            return outcome;
        }
        if (CargoOpen)
        {
            // DoDiscardCargo: the categories wrap round; bio data can't be chosen.
            switch (key)
            {
                case MenuKey.Up:
                case MenuKey.Left:
                    CargoSelected = (CargoSelected + 7) % 8;
                    return MenuOutcome.Moved;
                case MenuKey.Down:
                case MenuKey.Right:
                    CargoSelected = (CargoSelected + 1) % 8;
                    return MenuOutcome.Moved;
                case MenuKey.Select:
                    return MenuOutcome.DiscardCargo;
                default:
                    CargoOpen = false;
                    return MenuOutcome.Moved;
            }
        }
        return null;
    }

    /// <summary>The cursor one entry back or on, round the page (DoMenuChooser, PreviousMenuState and NextMenuState).</summary>
    protected MenuOutcome Step(MenuKey key)
    {
        Cursor = key is MenuKey.Up or MenuKey.Left ? (Cursor + Items.Count - 1) % Items.Count : (Cursor + 1) % Items.Count;
        return MenuOutcome.Moved;
    }

    /// <summary>Select on one of the Manifest's ways in or entries, or null for another entry.</summary>
    protected MenuOutcome? ChooseManifest(OrbitMenuItem item)
    {
        switch (item)
        {
            case OrbitMenuItem.Manifest:
                Open(MenuPage.Manifest, OrbitMenuItem.Cargo);
                return MenuOutcome.Moved;
            case OrbitMenuItem.Cargo:
                CargoOpen = true;
                CargoSelected = 0;
                return MenuOutcome.Moved;
            case OrbitMenuItem.Devices:
            {
                // DevicesMenu: with nothing aboard, nothing opens.
                var list = Inventory?.Invoke() ?? Array.Empty<int>();
                if (list.Count == 0)
                    return MenuOutcome.None;
                Devices = new DevicesMenu(list);
                return MenuOutcome.Moved;
            }
            case OrbitMenuItem.Roster when Fleet is not null:
                // RosterMenu: with no fleet ships, nothing opens.
                if (Fleet.Count == 0)
                    return MenuOutcome.None;
                OpenRoster();
                return MenuOutcome.Moved;
            case OrbitMenuItem.ExitManifest:
                Open(MenuPage.Orbit, OrbitMenuItem.Manifest);
                return MenuOutcome.Moved;
        }
        return null;
    }

    /// <summary>Opens a page with the cursor on an entry of it.</summary>
    protected void Open(MenuPage page, OrbitMenuItem at)
    {
        Page = page;
        Cursor = Math.Max(0, IndexOf(Items, at));
    }

    protected static int IndexOf(IReadOnlyList<OrbitMenuItem> items, OrbitMenuItem item)
    {
        for (int i = 0; i < items.Count; ++i)
        {
            if (items[i] == item)
                return i;
        }
        return -1;
    }
}

/// <summary>
/// The hyperspace and system menus (hyper.c's HyperspaceMenu, solarsys.c's
/// SolarSysMenu; the stage 3c plan's question 4), as UQM's PC menus have
/// them: opened by cancel in flight, the cursor on Starmap; Starmap,
/// Manifest, Game menu and Navigate (PM_ALT_STARMAP .. PM_ALT_NAVIGATE),
/// the Manifest as orbit's (Cargo, Devices, the host's Roster, Exit).
/// Navigate closes it, back to flight; cancel on the first page does
/// nothing (DoMenuChooser passes it on, and the menus ignore it), on the
/// Manifest goes back to it. The star map and the game menu say they come
/// later (the star map is a stage of its own, question 8; saving stays at
/// the starbase). The player's own screen, as orbit's: the world goes on
/// meanwhile (the ship gets no keys).
/// </summary>
public sealed class FlightMenu : ShipMenu
{
    private static readonly OrbitMenuItem[] FirstPage =
        { OrbitMenuItem.Starmap, OrbitMenuItem.Manifest, OrbitMenuItem.GameMenu, OrbitMenuItem.Navigate };

    /// <param name="inSystem">The system menu (SolarSysMenu) rather than the hyperspace menu.</param>
    public FlightMenu(bool inSystem) => InSystem = inSystem;

    /// <summary>Whether it's the system menu (in a system's flight), not the hyperspace menu.</summary>
    public bool InSystem { get; }

    public override IReadOnlyList<OrbitMenuItem> Items => Page == MenuPage.Manifest ? ManifestPage : FirstPage;

    /// <summary>One key press.</summary>
    public MenuOutcome Press(MenuKey key)
    {
        if (PressManifest(key) is { } manifest)
            return manifest;
        switch (key)
        {
            case MenuKey.Up:
            case MenuKey.Left:
            case MenuKey.Down:
            case MenuKey.Right:
                return Step(key);
            case MenuKey.Cancel:
                if (Page != MenuPage.Manifest)
                    return MenuOutcome.None;   // the menus ignore it
                Open(MenuPage.Orbit, OrbitMenuItem.Manifest);
                return MenuOutcome.Moved;
            case MenuKey.Select:
                if (ChooseManifest(Selected) is { } chosen)
                    return chosen;
                return Selected == OrbitMenuItem.Navigate ? MenuOutcome.CloseMenu : MenuOutcome.NotYet;
        }
        return MenuOutcome.None;
    }
}
