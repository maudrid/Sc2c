// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Melnorme credits and fuel between players (docs/plans/stage-3b-2.md,
/// item 3; docs/differences.md rows 19 and 79): each player's balance, in
/// the hold, the snapshot and the save; gifts of credits anywhere and at any
/// time, to a player in a talk too; fuel to a player close by (within a
/// screen in hyperspace, or in the same system), up to the giver's fuel and
/// the receiver's room; the giving screen's rows for both.
/// </summary>
[Trait("Topic", "trading")]
public class CreditsAndFuelTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly int[] NoMinerals = new int[8];
    private const int Sol = 82, Earth = 2, Luna = 1;

    // The host and a guest, both in hyperspace: the host at (x, y), the guest at (gx, gy).
    private static GameWorld InHyperspace(short x = 4000, short y = 4000, short gx = 4000, short gy = 4000)
    {
        var world = SystemGroupTests.NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        world.Host.Ship.Flight.PlaceAtUniverse(x, y);
        world.Find("guest")!.Ship.Flight.PlaceAtUniverse(gx, gy);
        Assert.All(world.Players, p => Assert.Null(p.Ship.SystemStar));
        return world;
    }

    private static Gift Credits(string from, string to, uint credits) => new(from, to, 0, NoMinerals, 0, Credits: credits);

    private static Gift Fuel(string from, string to, uint fuel) => new(from, to, 0, NoMinerals, 0, Fuel: fuel);

    [ContentFact]
    public void CreditsAreEachPlayersOwnAndGoAnywhere()
    {
        var world = InHyperspace(gx: 9000, gy: 9000);
        var host = world.Host.Ship.Hold;
        var guest = world.Find("guest")!.Ship.Hold;
        Assert.Equal((0u, 0u), (host.Credits, guest.Credits));   // a new game's: none
        host.Credits = 500;
        Assert.Equal(TradeRefusal.None, world.Give(Credits("h", "guest", 200)));
        Assert.Equal((300u, 200u), (host.Credits, guest.Credits));
        var gift = world.TakeGifts().Single();
        Assert.Equal(Credits("h", "guest", 200), gift);
        Assert.Equal("200 credits", gift.Describe());
        // Not more than the giver has, nor past the receiver's balance's top.
        Assert.Equal(TradeRefusal.NotEnough, world.Give(Credits("h", "guest", 301)));
        guest.Credits = uint.MaxValue;
        Assert.Equal(TradeRefusal.TooMuch, world.Give(Credits("h", "guest", 1)));
        Assert.Equal(300u, host.Credits);
        guest.Credits = 200;
        // Credits alone are something to give; nothing at all isn't.
        Assert.Equal(TradeRefusal.Nothing, world.Give(Credits("h", "guest", 0)));
        // Sent with the hold, each player's own.
        var ships = world.Snapshot().Ships;
        Assert.Equal((300u, 200u), (ships.Single(s => s.Id == "h").Hold!.Credits, ships.Single(s => s.Id == "guest").Hold!.Credits));
        // The text panel shows them beside the units.
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, "h", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        Assert.Contains(me.StatusLines(), l => l.EndsWith(", 0 RU, 300 Cr"));
    }

    [ContentFact]
    public void CreditsAndFuelReachAPlayerInATalk()
    {
        var world = InHyperspace();
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        int star = world.Systems!.Map.Stars.First(s => s.Special == "SLYLANDRO_DEFINED").Number;
        world.PutInSystem("h", star);
        // The guest talks to the Slylandro at their gas giant; the host, in their system, tops them up.
        world.PutInOrbit("guest", star, 3);
        var guest = world.Find("guest")!;
        Assert.Equal(Activity.Talk, guest.Ship.Activity);
        world.Host.Ship.Hold.Credits = 100;
        Assert.Equal(TradeRefusal.None, world.Give(Credits("h", "guest", 60)));
        uint before = guest.Ship.Flight.Fuel;
        Assert.Equal(TradeRefusal.None, world.Give(Fuel("h", "guest", 250)));
        Assert.Equal((60u, before + 250), (guest.Ship.Hold.Credits, guest.Ship.Flight.Fuel));
        Assert.NotNull(world.MeetingOf("guest")?.Talk);   // still talking
        // And the one in the talk gives back (the world doesn't refuse it; their screen waits for the talk's end).
        Assert.Equal(TradeRefusal.None, world.Give(Credits("guest", "h", 10)));
    }

    [ContentFact]
    public void FuelGoesOnlyToAPlayerCloseByUpToTheirRoom()
    {
        // Within a screen in hyperspace: 63 universe units across, 50 up and down (UNIT_SCREEN_WIDTH, _HEIGHT).
        var world = InHyperspace(4000, 4000, 4063, 4050);
        var host = world.Host.Ship;
        var guest = world.Find("guest")!.Ship;
        host.Flight.Fuel = 3000;
        guest.Flight.Fuel = 0;
        Assert.Equal(TradeRefusal.None, world.Give(Fuel("h", "guest", 1234)));
        Assert.Equal((1766u, 1234u), (host.Flight.Fuel, guest.Flight.Fuel));
        Assert.Equal("12.34 units of fuel", world.TakeGifts().Single().Describe());
        // A unit further either way: too far.
        guest.Flight.PlaceAtUniverse(4064, 4000);
        Assert.Equal(TradeRefusal.TooFar, world.Give(Fuel("h", "guest", 100)));
        guest.Flight.PlaceAtUniverse(4000, 3949);
        Assert.Equal(TradeRefusal.TooFar, world.Give(Fuel("h", "guest", 100)));
        // Resource units and credits still go, however far.
        host.Hold.ResourceUnits = 5;
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("h", "guest", 5, NoMinerals, 0)));
        // One in a system and one in hyperspace: too far, wherever the system's star is.
        world.PutInOrbit("h", Sol, Earth);
        guest.Flight.PlaceAtUniverse(host.Flight.UniverseX, host.Flight.UniverseY);
        Assert.Equal(TradeRefusal.TooFar, world.Give(Fuel("h", "guest", 100)));
        // In the same system, anywhere in it.
        world.PutInOrbit("guest", Sol, Earth, Luna);
        Assert.Equal(TradeRefusal.None, world.Give(Fuel("h", "guest", 100)));
        // Up to the giver's fuel and the receiver's room.
        Assert.Equal(TradeRefusal.NotEnough, world.Give(Fuel("h", "guest", host.Flight.Fuel + 1)));
        guest.Flight.Fuel = guest.Flight.FuelCapacity - 50;
        Assert.Equal(TradeRefusal.NoTankRoom, world.Give(Fuel("h", "guest", 51)));
        uint hostFuel = host.Flight.Fuel;
        Assert.Equal(TradeRefusal.None, world.Give(Fuel("h", "guest", 50)));
        Assert.Equal((hostFuel - 50, guest.Flight.FuelCapacity), (host.Flight.Fuel, guest.Flight.Fuel));
        Assert.Equal(TradeRefusal.NoTankRoom, world.Give(Fuel("h", "guest", 1)));
        // Fuel's refusals have their reasons.
        Assert.Contains("close by", Gift.Why(TradeRefusal.TooFar));
        Assert.Contains("fuel tanks", Gift.Why(TradeRefusal.NoTankRoom));
    }

    [Fact]
    public void CloseByIsTheSameSystemOrAScreenInHyperspace()
    {
        Assert.True(Gift.CloseBy(82, 0, 0, 82, 9999, 9999));
        Assert.False(Gift.CloseBy(82, 0, 0, 83, 0, 0));
        Assert.False(Gift.CloseBy(82, 100, 100, null, 100, 100));
        Assert.False(Gift.CloseBy(null, 100, 100, 82, 100, 100));
        Assert.True(Gift.CloseBy(null, 100, 100, null, 163, 50));
        Assert.True(Gift.CloseBy(null, 100, 100, null, 37, 150));
        Assert.False(Gift.CloseBy(null, 100, 100, null, 164, 100));
        Assert.False(Gift.CloseBy(null, 100, 100, null, 36, 100));
        Assert.False(Gift.CloseBy(null, 100, 100, null, 100, 151));
        Assert.Equal((63, 50), (HyperEncounters.ScreenX, HyperEncounters.ScreenY));
    }

    [ContentFact]
    public void FuelAndCreditsHaveTheGiftsOtherRefusals()
    {
        var world = InHyperspace();
        world.Host.Ship.Hold.Credits = 10;
        world.Host.Ship.Flight.Fuel = 1000;
        Assert.Equal(TradeRefusal.NoOne, world.Give(Fuel("h", "h", 100)));
        Assert.Equal(TradeRefusal.NoOne, world.Give(Credits("h", "nobody", 1)));
        world.SetHostPause(true);
        Assert.Equal(TradeRefusal.HostPaused, world.Give(Fuel("h", "guest", 100)));
        world.SetHostPause(false);
        world.Find("guest")!.Ship.Destroyed = true;
        Assert.Equal(TradeRefusal.Wrecked, world.Give(Fuel("h", "guest", 100)));
        Assert.Equal(TradeRefusal.Wrecked, world.Give(Credits("h", "guest", 1)));
        Assert.Equal((10u, 1000u), (world.Host.Ship.Hold.Credits, world.Host.Ship.Flight.Fuel));
        Assert.Empty(world.TakeGifts());
    }

    [Fact]
    public void AGiftsCreditsAndFuelGoOnTheWire()
    {
        var gift = new Gift("a", "b", 7, new[] { 1, 0, 0, 0, 0, 0, 0, 2 }, 3, Credits: 4000000000, Fuel: 123456);
        Assert.Equal(gift, Gift.Decode(gift.Encode()));
        Assert.NotEqual(gift, gift with { Credits = 1 });
        Assert.NotEqual(gift, gift with { Fuel = 1 });
        Assert.Equal("7 RU, 3 units of minerals, 3 bio data, 4000000000 credits, 1234.56 units of fuel", gift.Describe());
        // A gift from before them (protocol 27) is short: refused.
        var old = gift.Encode()[..^2];
        Assert.Throws<InvalidDataException>(() => Gift.Decode(old));
        // A hold's credits go in the snapshot.
        var hold = new HoldSnapshot(10, 1, 500, new int[8], 0, Credits: 4321);
        var ship = new ShipSnapshot("h", "Host", true, ShipKind.Flagship, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, Hold: hold);
        var snapshot = new WorldSnapshot(1, 2155, 2, 17, new[] { ship });
        var w = new WireWriter();
        SnapshotCodec.Encode(w, snapshot);
        var back = SnapshotCodec.Decode(w.Written.ToArray());
        Assert.Equal(4321u, back.Ships.Single().Hold!.Credits);
        Assert.Equal(hold, back.Ships.Single().Hold);
    }

    [ContentFact]
    public void CreditsAreSavedAndAnOlderSaveHasNone()
    {
        var world = InHyperspace();
        world.Host.Ship.Hold.Credits = 750;
        world.Find("guest")!.Ship.Hold.Credits = 25;
        string json = HostSave.ToJson(world);
        var node = JsonNode.Parse(json)!;
        Assert.Equal(14, node["version"]!.GetValue<int>());
        var loaded = HostSave.FromJson(json, SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal((750u, 25u), (loaded.Host.Ship.Hold.Credits, loaded.Find("guest")!.Ship.Hold.Credits));
        // A version 13 save has none, and one with credits is refused.
        node["version"] = 13;
        Assert.Contains("Melnorme credits", Assert.Throws<InvalidDataException>(
            () => HostSave.FromJson(node.ToJsonString(), SystemGroupTests.Systems.Value, TestContent.Shapes)).Message);
        foreach (var p in node["players"]!.AsArray())
            p!["hold"]!.AsObject().Remove("credits");
        var older = HostSave.FromJson(node.ToJsonString(), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.All(older.Players, p => Assert.Equal(0u, p.Ship.Hold.Credits));
        // A save with a lander out keeps them too.
        world.PutInOrbit("h", Sol, Earth);
        world.Host.Ship.Hold.LanderLeaves();
        var reloaded = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(750u, reloaded.Host.Ship.Hold.Credits);
    }

    [ContentFact]
    public void TheGivingScreenGivesCreditsAndWholeUnitsOfFuelUpToTheReceiversRoom()
    {
        var world = InHyperspace(4000, 4000, 4010, 4010);
        var host = world.Host.Ship;
        var guest = world.Find("guest")!.Ship;
        host.Hold.Credits = 150;
        host.Flight.Fuel = 4275;                              // 42.75 units: 42 whole ones to give
        guest.Flight.Fuel = guest.Flight.FuelCapacity - 1250;  // room for 12.5: 12 whole ones
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, "h", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.ToggleTrade();
        var shown = me.Trade!.Follow(me.Snapshot, "h");
        Assert.Equal((150L, 42L), (shown.Have[TradeScreen.CreditsRow], shown.Have[TradeScreen.FuelRow]));
        Assert.Equal((false, true, (int?)12), (shown.Here, shown.Near, shown.TankRoom));
        me.PressMenu(MenuKey.Down);             // the credits
        me.PressMenu(MenuKey.PageUp);           // by 100
        me.PressMenu(MenuKey.PageUp);           // only 150
        me.PressMenu(MenuKey.Select);
        Assert.Equal(Credits("h", "guest", 150), me.TakeGifts().Single());
        me.PressMenu(MenuKey.Down);             // the fuel
        me.PressMenu(MenuKey.PageUp);           // by 10
        me.PressMenu(MenuKey.PageUp);           // only the room's 12
        Assert.Equal(12, me.Trade.Amounts[TradeScreen.FuelRow]);
        me.PressMenu(MenuKey.Select);
        var fuel = me.TakeGifts().Single();
        Assert.Equal(Fuel("h", "guest", 1200), fuel);
        Assert.Equal(TradeRefusal.None, world.Give(fuel));
        // Further off: not close by (the row's dimmed; the host would refuse it).
        guest.Flight.PlaceAtUniverse(4100, 4000);
        Assert.False(me.Trade.Follow(world.Snapshot(), "h").Near);
        // The giver's fuel caps it too.
        host.Flight.Fuel = 250;
        guest.Flight.Fuel = 0;
        guest.Flight.PlaceAtUniverse(4000, 4000);
        world.Step();
        me.Observe(world.Snapshot());
        me.PressMenu(MenuKey.PageUp);
        Assert.Equal(2, me.Trade.Amounts[TradeScreen.FuelRow]);
        // Told as it goes, to both.
        me.ShowGift(Fuel("guest", "h", 150));
        Assert.Equal("Guest gave you 1.5 units of fuel.", me.Notice);
    }
}
