// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Wallets, gifts and cargo trading between players (docs/plans/stage-3.md,
/// item 9; docs/differences.md rows 11 and 22).
/// </summary>
[Trait("Topic", "trading")]
public class TradingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82, Earth = 2, Luna = 1;
    private static readonly int[] NoMinerals = new int[8];

    private static GameWorld TwoPlayers(bool sameSystem)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        world.PutInOrbit("host", Sol, Earth, null);
        if (sameSystem)
            world.PutInOrbit("guest", Sol, Earth, Luna);
        return world;
    }

    // The host's hold, with minerals and bio data brought up by its lander.
    private static void Load(Player p, int[] minerals, int bio)
    {
        var hold = p.Ship.Hold;
        hold.LanderLeaves();
        hold.LanderReturns(hold.LanderOut!.Value.Crew, minerals, bio);
    }

    private static int[] Minerals(int category, int amount)
    {
        var m = new int[8];
        m[category] = amount;
        return m;
    }

    [ContentFact]
    public void ResourceUnitsGoAnywhere()
    {
        var world = TwoPlayers(sameSystem: false);
        Assert.Null(world.Find("guest")!.Ship.SystemStar);   // in hyperspace, the host in Sol
        world.Host.Ship.Hold.ResourceUnits = 500;
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 200, NoMinerals, 0)));
        Assert.Equal((300u, 200u), (world.Host.Ship.Hold.ResourceUnits, world.Find("guest")!.Ship.Hold.ResourceUnits));
        Assert.Equal(new Gift("host", "guest", 200, NoMinerals, 0), world.TakeGifts().Single());
        Assert.Empty(world.TakeGifts());
    }

    [ContentFact]
    public void CargoGoesOnlyWithinASystem()
    {
        var world = TwoPlayers(sameSystem: false);
        Load(world.Host, Minerals(3, 20), 5);
        Assert.Equal(TradeRefusal.NotHere, world.Give(new Gift("host", "guest", 0, Minerals(3, 5), 0)));
        Assert.Equal(TradeRefusal.NotHere, world.Give(new Gift("host", "guest", 0, NoMinerals, 1)));
        world.PutInOrbit("guest", Sol, Earth, Luna);
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 0, Minerals(3, 5), 2)));
        var guest = world.Find("guest")!.Ship.Hold;
        Assert.Equal((5, 2, 15, 3), (guest.Minerals[3], guest.BioData, world.Host.Ship.Hold.Minerals[3], world.Host.Ship.Hold.BioData));
    }

    [ContentFact]
    public void MineralsGoUpToTheReceiversRoomAndBioDataWithoutLimit()
    {
        var world = TwoPlayers(sameSystem: true);
        var host = world.Host;
        Load(host, Minerals(0, 50), 50);
        var guest = world.Find("guest")!;
        guest.Ship.Hold.Restore(guest.Ship.Hold.Save() with { Minerals = Minerals(7, guest.Ship.Hold.StorageCapacity - 10) });
        Assert.Equal(TradeRefusal.NoRoom, world.Give(new Gift("host", "guest", 0, Minerals(0, 11), 0)));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 0, Minerals(0, 10), 50)));
        Assert.Equal(0, guest.Ship.Hold.FreeStorage);
        // A lander out keeps the room it left with.
        guest.Ship.Hold.Restore(guest.Ship.Hold.Save() with { Minerals = NoMinerals });
        guest.Ship.Hold.LanderLeaves();
        int room = guest.Ship.Hold.StorageCapacity - guest.Ship.Hold.LanderOut!.Value.MineralCapacity;
        Assert.Equal(room, guest.Ship.Hold.RoomForCargo);
    }

    [ContentFact]
    public void AGiftThatCantGoIsRefusedWithAReasonAndChangesNothing()
    {
        var world = TwoPlayers(sameSystem: true);
        world.Host.Ship.Hold.ResourceUnits = 100;
        Assert.Equal(TradeRefusal.NotEnough, world.Give(new Gift("host", "guest", 101, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.NotEnough, world.Give(new Gift("host", "guest", 0, Minerals(1, 1), 0)));
        Assert.Equal(TradeRefusal.NotEnough, world.Give(new Gift("host", "guest", 0, NoMinerals, 1)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(new Gift("host", "guest", 0, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(new Gift("host", "guest", 1, Minerals(1, -1), 0)));
        Assert.Equal(TradeRefusal.Nothing, world.Give(new Gift("host", "guest", 1, new int[3], 0)));
        Assert.Equal(TradeRefusal.NoOne, world.Give(new Gift("host", "host", 1, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.NoOne, world.Give(new Gift("host", "nobody", 1, NoMinerals, 0)));
        world.Find("guest")!.Ship.Hold.ResourceUnits = uint.MaxValue;
        Assert.Equal(TradeRefusal.TooMuch, world.Give(new Gift("host", "guest", 1, NoMinerals, 0)));
        world.Find("guest")!.Ship.Hold.ResourceUnits = 0;
        world.Leave("guest");
        Assert.Equal(TradeRefusal.NoOne, world.Give(new Gift("host", "guest", 1, NoMinerals, 0)));
        Assert.Equal(100u, world.Host.Ship.Hold.ResourceUnits);
        Assert.Empty(world.TakeGifts());
        foreach (var r in Enum.GetValues<TradeRefusal>().Skip(1))
            Assert.NotEqual("", Gift.Why(r));
    }

    [ContentFact]
    public void SavesKeepWhatWasGiven()
    {
        var world = TwoPlayers(sameSystem: true);
        Load(world.Host, Minerals(6, 30), 7);
        world.Host.Ship.Hold.ResourceUnits = 1000;
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 400, Minerals(6, 30), 7)));
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        var guest = loaded.Find("guest")!.Ship.Hold;
        Assert.Equal((400u, 30, 7), (guest.ResourceUnits, guest.Minerals[6], guest.BioData));
        Assert.Equal((600u, 0, 0), (loaded.Host.Ship.Hold.ResourceUnits, loaded.Host.Ship.Hold.MineralMass, loaded.Host.Ship.Hold.BioData));
    }

    [ContentFact]
    public void AGuestsGiftIsFromItselfWhateverItSaysAndBothAreTold()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        host.Hello(1, "guest", "Guest", Protocol.Version);
        host.World.UndockShown("guest");
        host.World.Find("guest")!.Ship.Hold.ResourceUnits = 50;
        host.World.Host.Ship.Hold.ResourceUnits = 50;
        // It claims to be from the host: the gift is the guest's own.
        Assert.Equal(TradeRefusal.None, host.GuestGive(1, new Gift("host", "host-nope", 0, NoMinerals, 0) with { To = "host", ResourceUnits = 20 }
            is var g ? g.Encode() : null!));
        Assert.Equal((70u, 30u), (host.World.Host.Ship.Hold.ResourceUnits, host.World.Find("guest")!.Ship.Hold.ResourceUnits));
        var (mine, theirs) = host.TakeGifts();
        Assert.Equal("guest", mine.Single().From);
        Assert.Equal((1L, "guest"), (theirs.Single().Peer, theirs.Single().Gift.From));
        Assert.Equal(TradeRefusal.Nothing, host.GuestGive(1, new byte[] { 1, 2, 3 }));
        Assert.Equal(TradeRefusal.NoOne, host.GuestGive(2, g.Encode()));
    }

    [Fact]
    public void AGiftsMessageRoundTripsAndBadOnesAreRefused()
    {
        var gift = new Gift("a", "Zoë", 123456, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 9);
        Assert.Equal(gift, Gift.Decode(gift.Encode()));
        Assert.Throws<InvalidDataException>(() => Gift.Decode(gift.Encode().Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => Gift.Decode(gift.Encode()[..5]));
        Assert.Equal("123456 RU, 36 units of minerals, 9 bio data", gift.Describe());
    }

    [ContentFact]
    public void TheGivingScreenMakesTheGiftWithinWhatThePlayerHas()
    {
        var world = TwoPlayers(sameSystem: true);
        Load(world.Host, Minerals(2, 12), 0);
        world.Host.Ship.Hold.ResourceUnits = 150;
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.ToggleTrade();
        Assert.Equal(Hyperspace.ShipInput.None, me.NextInput(Hyperspace.ShipInput.Thrust));   // the ship waits
        me.PressMenu(MenuKey.PageUp);            // the units, by 100
        me.PressMenu(MenuKey.PageUp);            // only 150 to give
        Assert.Equal(150, me.Trade!.Amounts[TradeScreen.UnitsRow]);
        me.PressMenu(MenuKey.Left);
        me.PressMenu(MenuKey.Select);
        Assert.Equal(new Gift("host", "guest", 149, NoMinerals, 0), me.TakeGifts().Single());
        me.PressMenu(MenuKey.Down);              // common
        me.PressMenu(MenuKey.Down);              // corrosive
        me.PressMenu(MenuKey.Down);              // base metal
        me.PressMenu(MenuKey.PageUp);
        me.PressMenu(MenuKey.Select);
        Assert.Equal(new Gift("host", "guest", 0, Minerals(2, 10), 0), me.TakeGifts().Single());
        Assert.Contains(me.StatusLines(), l => l.Contains("To: Guest"));
        me.PressMenu(MenuKey.Cancel);
        Assert.Null(me.Trade);
        me.ShowGift(new Gift("guest", "host", 5, NoMinerals, 0));
        Assert.Equal("Guest gave you 5 RU.", me.Notice);
        me.TradeRefused(TradeRefusal.NoRoom);
        Assert.Equal(Gift.Why(TradeRefusal.NoRoom), me.Notice);
    }
}
