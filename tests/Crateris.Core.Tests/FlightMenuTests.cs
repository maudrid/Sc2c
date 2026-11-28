// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.StoryPickupsTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The hyperspace and system menus (the stage 3c plan's item 8; its
/// question 4), as UQM's PC menus have them (HyperspaceMenu, SolarSysMenu,
/// DoMenuChooser): Esc in flight opens them on Starmap; Starmap, Manifest,
/// Game menu, Navigate; the Manifest as orbit's, its Devices list's select
/// asking the host to use the device, the list closing on a use and waiting
/// for the answer; and the Sun Device's flash holding the screen.
/// </summary>
[Trait("Topic", "systems")]
public class FlightMenuTests
{
    private static readonly StoryRules Rules = StoryRules.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    // ------------------------------------------------------------ the menu

    [Fact]
    public void TheMenuIsUqmsPcMenuStartingOnStarmap()
    {
        var menu = new FlightMenu(inSystem: false);
        Assert.Equal(new[] { OrbitMenuItem.Starmap, OrbitMenuItem.Manifest, OrbitMenuItem.GameMenu, OrbitMenuItem.Navigate },
            menu.Items);
        Assert.Equal(OrbitMenuItem.Starmap, menu.Selected);
        // Round it goes, both ways.
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Up));
        Assert.Equal(OrbitMenuItem.Navigate, menu.Selected);
        menu.Press(MenuKey.Right);
        Assert.Equal(OrbitMenuItem.Starmap, menu.Selected);
        // Cancel on the first page does nothing (DoMenuChooser passes it on; the menus ignore it).
        Assert.Equal(MenuOutcome.None, menu.Press(MenuKey.Cancel));
        // The star map and the game menu come later; Navigate is back to flight.
        Assert.Equal(MenuOutcome.NotYet, menu.Press(MenuKey.Select));
        Assert.False(menu.Offers(OrbitMenuItem.Starmap));
        menu.Press(MenuKey.Down);
        menu.Press(MenuKey.Down);
        Assert.Equal((OrbitMenuItem.GameMenu, MenuOutcome.NotYet), (menu.Selected, menu.Press(MenuKey.Select)));
        menu.Press(MenuKey.Down);
        Assert.Equal((OrbitMenuItem.Navigate, MenuOutcome.CloseMenu), (menu.Selected, menu.Press(MenuKey.Select)));
    }

    [Fact]
    public void TheManifestIsOrbitsAndCancelGoesBackToIt()
    {
        var menu = new FlightMenu(inSystem: true) { Inventory = () => new[] { 3, 13 } };
        menu.Press(MenuKey.Down);
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Select));
        Assert.Equal(ShipMenu.MenuPage.Manifest, menu.Page);
        Assert.Equal(new[] { OrbitMenuItem.Cargo, OrbitMenuItem.Devices, OrbitMenuItem.Roster, OrbitMenuItem.ExitManifest },
            menu.Items);
        Assert.Equal(OrbitMenuItem.Cargo, menu.Selected);
        // The cargo display: a unit dropped, cancel back to the Manifest.
        menu.Press(MenuKey.Select);
        Assert.True(menu.CargoOpen);
        menu.Press(MenuKey.Down);
        Assert.Equal((1, MenuOutcome.DiscardCargo), (menu.CargoSelected, menu.Press(MenuKey.Select)));
        menu.Press(MenuKey.Cancel);
        Assert.False(menu.CargoOpen);
        // The Devices list: select uses the device lit, cancel closes it.
        menu.Press(MenuKey.Down);
        menu.Press(MenuKey.Select);
        Assert.Equal(new[] { 3, 13 }, menu.DeviceList);
        menu.Press(MenuKey.Down);
        Assert.Equal((1, MenuOutcome.UseDevice), (menu.DeviceCursor, menu.Press(MenuKey.Select)));
        Assert.NotNull(menu.DeviceList);   // the list stays until the host's answer
        menu.Press(MenuKey.Cancel);
        Assert.Null(menu.DeviceList);
        // A guest's Roster comes later; Exit, or cancel, back to the first page on Manifest.
        menu.Press(MenuKey.Down);
        Assert.Equal((OrbitMenuItem.Roster, MenuOutcome.NotYet), (menu.Selected, menu.Press(MenuKey.Select)));
        Assert.Equal(MenuOutcome.Moved, menu.Press(MenuKey.Cancel));
        Assert.Equal((ShipMenu.MenuPage.Orbit, OrbitMenuItem.Manifest), (menu.Page, menu.Selected));
        menu.Press(MenuKey.Select);
        menu.Press(MenuKey.Up);
        Assert.Equal(OrbitMenuItem.ExitManifest, menu.Selected);
        menu.Press(MenuKey.Select);
        Assert.Equal((ShipMenu.MenuPage.Orbit, OrbitMenuItem.Manifest), (menu.Page, menu.Selected));
    }

    [Fact]
    public void TheDevicesListDoesntOpenWithNothingAboard()
    {
        var menu = new FlightMenu(false);
        menu.Press(MenuKey.Down);
        menu.Press(MenuKey.Select);
        menu.Press(MenuKey.Down);
        Assert.Equal(MenuOutcome.None, menu.Press(MenuKey.Select));
        Assert.Null(menu.DeviceList);
    }

    // ------------------------------------------------------------ in the session

    private static PlayerSession Session(GameWorld world, string id)
    {
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, id, TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        return me;
    }

    private static void Observe(GameWorld world, PlayerSession me)
    {
        Frames(world, 1);
        me.Observe(world.Snapshot());
    }

    [Fact]
    public void EscInFlightOpensTheMenuAndTheShipGetsNoKeysTillNavigateClosesIt()
    {
        var world = NewGame();
        var me = Session(world, "guest");
        Assert.Equal(ShipInput.Thrust, me.NextInput(ShipInput.Thrust));
        Assert.Contains("Arrows: turn and thrust; Esc: the menu", me.StatusLines());
        me.PressMenu(MenuKey.Select);   // not Esc: nothing opens
        Assert.Null(me.FlightMenu);
        me.PressMenu(MenuKey.Cancel);
        Assert.NotNull(me.FlightMenu);
        Assert.False(me.FlightMenu!.InSystem);
        Assert.Equal(ShipInput.None, me.NextInput(ShipInput.Thrust));
        Assert.Contains(me.StatusLines(), l => l.StartsWith("The hyperspace menu"));
        // The star map comes later.
        me.PressMenu(MenuKey.Select);
        Assert.Equal("Starmap comes in a later stage.", me.FlightMessage);
        // Navigate: back to flight, the keys the ship's once they've come up.
        me.PressMenu(MenuKey.Up);
        me.PressMenu(MenuKey.Select);
        Assert.Null(me.FlightMenu);
        Assert.Equal(ShipInput.None, me.NextInput(ShipInput.Action));   // the Enter that chose it, still held
        Assert.Equal(ShipInput.None, me.NextInput(ShipInput.None));
        Assert.Equal(ShipInput.Thrust, me.NextInput(ShipInput.Thrust));
    }

    [ContentFact]
    public void InASystemItsTheSystemMenuWhichClosesAsTheShipLeavesFlight()
    {
        var world = NewGame();
        int sol = Star(world, "SOL_DEFINED");
        world.PutInSystem("host", sol);
        var me = Session(world, "host");
        Observe(world, me);
        me.PressMenu(MenuKey.Cancel);
        Assert.True(me.FlightMenu!.InSystem);
        // Into orbit (a set-up's: UQM's menu holds the ship still): the menu is gone, orbit's there.
        world.PutInOrbit("host", sol, 3);
        Observe(world, me);
        Assert.Null(me.FlightMenu);
        Assert.NotNull(me.OrbitMenu);
        // Back in hyperspace, opened; then into a system: gone.
        world.PutInHyperspace("host");
        Observe(world, me);
        me.PressMenu(MenuKey.Cancel);
        Assert.NotNull(me.FlightMenu);
        world.PutInSystem("host", sol);
        Observe(world, me);
        Assert.Null(me.FlightMenu);
    }

    [Fact]
    public void AGuestsDeviceIsUsedThroughTheHostTheListWaitingForItsAnswer()
    {
        var world = NewGame();
        world.SetDevice("guest", "UMGAH_BROADCASTERS_ON_SHIP");
        world.SetDevice("guest", "ROSY_SPHERE_ON_SHIP");
        var me = Session(world, "guest");
        me.PressMenu(MenuKey.Cancel);   // the hyperspace menu
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);   // Manifest
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);   // Devices
        var list = me.FlightMenu!.DeviceList!;
        int caster = Rules.Devices.All.Single(d => d.Name == "UMGAH_HYPERWAVE_DEVICE").Id;
        int rosy = Rules.Devices.All.Single(d => d.Name == "ROSY_SPHERE_DEVICE").Id;
        Assert.Equal(new[] { rosy, caster }, list);
        // The Sphere without the Ultron: the host says nothing happens; the list stays open.
        me.PressMenu(MenuKey.Select);
        Assert.True(me.DeviceAsked);
        Assert.Equal(rosy, me.TakeDeviceUse());
        Assert.Null(me.TakeDeviceUse());   // asked once
        me.PressMenu(MenuKey.Down);   // the list waits for the answer
        Assert.Equal(0, me.FlightMenu.DeviceCursor);
        me.DeviceUsed(world.UseDevice("guest", rosy));
        Assert.False(me.DeviceAsked);
        Assert.Equal("Nothing happens.", me.FlightMessage);
        Assert.NotNull(me.FlightMenu.DeviceList);
        // The broadcaster: used, the list closed, back at the Manifest with Devices lit.
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);
        int? asked = me.TakeDeviceUse();
        Assert.Equal(caster, asked);
        me.DeviceUsed(world.UseDevice("guest", asked!.Value));
        Assert.Null(me.FlightMenu.DeviceList);
        Assert.Equal((ShipMenu.MenuPage.Manifest, OrbitMenuItem.Devices), (me.FlightMenu.Page, me.FlightMenu.Selected));
        Assert.True(world.Find("guest")!.Ship.UsedBroadcaster);
    }

    [Fact]
    public void TheSunDevicesFlashHoldsTheScreenThreeSecondsAndFadesBackInTwo()
    {
        var world = NewGame();
        var me = Session(world, "host");
        me.DeviceUsed(DeviceOutcome.Flash);
        Assert.Equal(0, me.Flash);
        me.Advance(1);
        Assert.Equal(1, me.Flash);
        me.PressMenu(MenuKey.Cancel);   // held: no keys
        Assert.Null(me.FlightMenu);
        me.Advance(2.5);
        me.PressMenu(MenuKey.Cancel);   // fading back: the keys are the menus' again
        Assert.NotNull(me.FlightMenu);
        me.Advance(1.5);
        Assert.Null(me.Flash);
        Assert.Equal("", PlayerSession.DeviceMessage(DeviceOutcome.Flash));
        // How white the screen is meanwhile (devices.c: a second to white, two held, two back).
        Assert.Equal(new[] { 0, 0.5, 1, 1, 0.75, 0 },
            new double?[] { null, 0.5, 1, 2.9, 3.5, 5 }.Select(Crateris.Presentation.DeviceFlashes.SunWhite));
    }

    [Fact]
    public void TheBombsFlashGoesWhiteThenBlackBeforeTheGameOversWords()
    {
        // restart.c: FadeAllToWhite over an eighth of a second, a 60th, the screen white, FadeAllToBlack over an
        // eighth, then three seconds.
        Assert.Equal((0.0, 0.0, false), Crateris.Presentation.DeviceFlashes.Bomb(0));
        Assert.Equal((0.5, 0.0, false), Crateris.Presentation.DeviceFlashes.Bomb(1.0 / 16));
        Assert.Equal((1.0, 0.0, false), Crateris.Presentation.DeviceFlashes.Bomb(1.0 / 8 + 1.0 / 120));
        var mid = Crateris.Presentation.DeviceFlashes.Bomb(1.0 / 8 + 1.0 / 60 + 1.0 / 16);
        Assert.Equal((1.0, false), (mid.White, mid.Over));
        Assert.Equal(0.5, mid.Black, 9);
        Assert.Equal((1.0, 1.0, false), Crateris.Presentation.DeviceFlashes.Bomb(2));
        Assert.True(Crateris.Presentation.DeviceFlashes.Bomb(3.27).Over);
    }
}
