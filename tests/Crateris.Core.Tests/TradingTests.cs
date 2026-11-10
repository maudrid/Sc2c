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
        me.PressMenu(MenuKey.Cancel);
        Assert.Null(me.Trade);
        me.ShowGift(new Gift("guest", "host", 5, NoMinerals, 0));
        Assert.Equal("Guest gave you 5 RU.", me.Notice);
        me.TradeRefused(TradeRefusal.NoRoom);
        Assert.Equal(Gift.Why(TradeRefusal.NoRoom), me.Notice);
    }

    [ContentFact]
    public void NoGiftWhileTheStoryWaitsNorToOrFromTheOpeningsStarter()
    {
        var world = TwoPlayers(sameSystem: true);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, Data.ConversationScripts.Load(DataDir)));
        world.Host.Ship.Hold.ResourceUnits = 100;
        world.Find("guest")!.Ship.Hold.ResourceUnits = 100;
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.VisitStarbase(world.Host);
        Assert.True(world.StoryPaused);
        Assert.Equal(TradeRefusal.Paused, world.Give(new Gift("guest", "host", 50, NoMinerals, 0)));
        Assert.Equal(TradeRefusal.Paused, world.Give(new Gift("host", "guest", 50, NoMinerals, 0)));
        world.Leave("host");     // the replies pass to the guest; the opening may yet be undone
        Assert.Equal((100u, 100u), (world.Host.Ship.Hold.ResourceUnits, world.Find("guest")!.Ship.Hold.ResourceUnits));
        Assert.Empty(world.TakeGifts());
    }

    [ContentFact]
    public void AGiftToAShipWhoseLanderIsOutLeavesTheLandersRoomAndSurvivesASave()
    {
        var world = TwoPlayers(sameSystem: true);
        Load(world.Host, Minerals(0, 50), 0);
        var guest = world.Find("guest")!;
        guest.Ship.Hold.Restore(guest.Ship.Hold.Save() with { Minerals = Minerals(7, guest.Ship.Hold.StorageCapacity - 60) });
        guest.Ship.Hold.LanderLeaves();      // it may bring up 50
        Assert.Equal(TradeRefusal.NoRoom, world.Give(new Gift("host", "guest", 0, Minerals(0, 11), 0)));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 0, Minerals(0, 10), 0)));
        guest.Ship.Hold.LanderReturns(guest.Ship.Hold.LanderOut!.Value.Crew, Minerals(1, 50), 0);
        Assert.Equal(0, guest.Ship.Hold.FreeStorage);
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        Assert.Equal(0, loaded.Find("guest")!.Ship.Hold.FreeStorage);
    }

    [ContentFact]
    public void AGiftBetweenTwoGuestsIsToldToBothAndNotTheHost()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        host.Hello(1, "a", "A", Protocol.Version);
        host.Hello(2, "b", "B", Protocol.Version);
        host.World.Find("a")!.Ship.Hold.ResourceUnits = 10;
        Assert.Equal(TradeRefusal.None, host.GuestGive(1, new Gift("a", "b", 10, NoMinerals, 0).Encode()));
        var (mine, theirs) = host.TakeGifts();
        Assert.Empty(mine);
        Assert.Equal(new long[] { 1, 2 }, theirs.Select(t => t.Peer).OrderBy(p => p));
        Assert.Throws<InvalidDataException>(() => Gift.Decode(new Gift(new string('x', Gift.MaxId + 1), "b", 1, NoMinerals, 0).Encode()));
    }

    [ContentFact]
    public void TheGivingScreenKeepsItsKeysFromTheShipAndTheMenusAndWaitsForTheirRelease()
    {
        var world = TwoPlayers(sameSystem: true);
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.NextInput(Hyperspace.ShipInput.None);
        Assert.NotNull(me.OrbitMenu);
        int closed = 0;
        me.TradeClosed += () => ++closed;
        me.ToggleTrade();
        Assert.NotNull(me.Trade);
        Assert.Equal(0, closed);
        me.PressMenu(MenuKey.Cancel);       // Esc closes it, and says so as it does (the game takes the keys held)...
        Assert.Null(me.Trade);
        Assert.Equal(1, closed);
        // ...and the still-held Esc (the lander's take-off) waits for its release.
        Assert.Equal(Hyperspace.ShipInput.None, me.NextInput(Hyperspace.ShipInput.TakeOff));
        Assert.Equal(Hyperspace.ShipInput.None, me.NextInput(Hyperspace.ShipInput.None));
        // Alone, it doesn't open.
        var alone = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        world.Leave("guest");
        alone.Observe(world.Snapshot());
        alone.ToggleTrade();
        Assert.Null(alone.Trade);
    }

    [ContentFact]
    public void TheGivingScreenShowsWhoItGivesToFromTheStartAndFollowsTheHold()
    {
        var world = TwoPlayers(sameSystem: true);
        Load(world.Host, Minerals(2, 12), 3);
        world.Host.Ship.Hold.ResourceUnits = 150;
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.ToggleTrade();
        Assert.Equal("guest", me.Trade!.To);   // from the start
        // Before any key: the first other player, in this system, with their room; what the host has.
        var shown = me.Trade!.Follow(me.Snapshot, "host");
        Assert.Equal(("guest", "Guest", true, TradeScreen.UnitsRow), (shown.To, shown.ToName, shown.Here, shown.Row));
        Assert.Equal(world.Find("guest")!.Ship.Hold.RoomForCargo, shown.Room);
        Assert.Equal(new long[] { 0, 150, 0, 0, 12, 0, 0, 0, 0, 0, 3 }, shown.Have);
        Assert.All(shown.Give, a => Assert.Equal(0, a));
        // An amount chosen is never more than the player has now.
        me.PressMenu(MenuKey.PageUp);
        Assert.Equal(100, me.Trade.Follow(me.Snapshot, "host").Give[TradeScreen.UnitsRow]);
        world.Host.Ship.Hold.ResourceUnits = 40;
        world.SubmitInput("host", world.Frame + 1, Hyperspace.ShipInput.None);
        world.Step();
        me.Observe(world.Snapshot());
        Assert.Equal(40, me.Trade.Follow(me.Snapshot, "host").Give[TradeScreen.UnitsRow]);
        // The guest leaving the system: they're elsewhere; leaving the game: no one.
        world.Find("guest")!.Ship.Ip!.ReturnFromOrbit();
        Assert.True(me.Trade.Follow(world.Snapshot(), "host").Here);   // still in Sol
        var gone = world.Snapshot() with { Ships = world.Snapshot().Ships.Select(s => s.Id == "guest" ? s with { SystemStar = null } : s).ToList() };
        Assert.False(me.Trade.Follow(gone, "host").Here);
        var alone = world.Snapshot() with { Ships = world.Snapshot().Ships.Where(s => s.Id == "host").ToList() };
        shown = me.Trade.Follow(alone, "host");
        Assert.Equal((null, "", false, (int?)null), (shown.To, shown.ToName, shown.Here, shown.Room));
        // The panel lists nothing of it while it's open (it's drawn over the status area), and offers it once it's closed.
        Assert.DoesNotContain(me.StatusLines(), l => l.Contains("give", StringComparison.OrdinalIgnoreCase));
        int closed = 0;
        me.TradeClosed += () => ++closed;
        me.ToggleTrade();   // T closes it, saying so once
        Assert.Contains("T: give another player something", me.StatusLines());
        Assert.Equal(1, closed);
    }

    [ContentFact]
    public void TheReceiversRoomIsTheHostsWithTheirLanderDown()
    {
        foreach (int free in new[] { 300, 60, 30 })
        {
            var world = TwoPlayers(sameSystem: true);
            var guest = world.Find("guest")!;
            guest.Ship.Hold.Restore(guest.Ship.Hold.Save() with { Minerals = Minerals(7, guest.Ship.Hold.StorageCapacity - free) });
            var ship = world.Snapshot().Ships.Single(s => s.Id == "guest");
            Assert.Equal(guest.Ship.Hold.RoomForCargo, TradeScreen.RoomFor(ship));
            Assert.Equal(free, TradeScreen.RoomFor(ship));
            Assert.Equal(DispatchRefusal.None, world.Dispatch("guest", 100, 100));
            ship = world.Snapshot().Ships.Single(s => s.Id == "guest");
            Assert.Equal(Activity.Planetside, ship.Activity);
            Assert.Equal(guest.Ship.Hold.RoomForCargo, TradeScreen.RoomFor(ship));   // free less the lander's 50, or none
            // A gift since it left lessens the free room, and the sum still matches the host's.
            if (guest.Ship.Hold.RoomForCargo > 5)
            {
                Load(world.Host, Minerals(0, 5), 0);
                Assert.Equal(TradeRefusal.None, world.Give(new Gift("host", "guest", 0, Minerals(0, 5), 0)));
                Assert.Equal(guest.Ship.Hold.RoomForCargo, TradeScreen.RoomFor(world.Snapshot().Ships.Single(s => s.Id == "guest")));
            }
            // Minerals they give away while their lander is down free room the lander didn't keep.
            Assert.Equal(TradeRefusal.None, world.Give(new Gift("guest", "host", 0, Minerals(7, 20), 0)));
            Assert.Equal(guest.Ship.Hold.RoomForCargo, TradeScreen.RoomFor(world.Snapshot().Ships.Single(s => s.Id == "guest")));
            if (free == 30)
                Assert.Equal(20, TradeScreen.RoomFor(world.Snapshot().Ships.Single(s => s.Id == "guest")));   // 50 free, the lander keeping 30
            // Back, or lost: the room is the free storage again.
            int crew = free == 60 ? 0 : guest.Ship.Hold.LanderOut!.Value.Crew;
            guest.Ship.Hold.LanderReturns(crew, Minerals(0, 0), 0);
            Assert.Equal(guest.Ship.Hold.FreeStorage, TradeScreen.RoomFor(world.Snapshot().Ships.Single(s => s.Id == "guest")));
            Assert.Null(TradeScreen.RoomFor(ship with { Hold = null }));
        }
    }

    [ContentFact]
    public void TheGivingScreenDoesntOpenInATalkAndClosesWhenOneShows()
    {
        var world = TwoPlayers(sameSystem: true);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, Data.ConversationScripts.Load(DataDir)));
        world.DialogueLocks = Comm.DialogueLocks.Load(DataDir);
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);
        world.CommanderMet = true;
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.VisitStarbase(world.Host);
        Assert.Equal(Activity.Starbase, world.Host.Ship.Activity);
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.ShowStarbase(world.StarbaseFor("host"));
        // Docked at the menu, it opens, and has the keys.
        me.ToggleTrade();
        Assert.NotNull(me.Trade);
        me.PressMenu(MenuKey.Down);
        Assert.Empty(me.TakeStarbaseKeys());
        // Talking to the commander (a key sent before it opened): the talk shows, and it closes, saying so as it does.
        int closed = 0;
        me.TradeClosed += () => ++closed;
        world.StarbaseKey("host", MenuKey.Select, out _);
        me.Observe(world.Snapshot());
        me.ShowStarbase(world.StarbaseFor("host"));
        me.ShowTalk(world.TalkFor("host"));
        Assert.True(me.TalkShown);
        Assert.Null(me.Trade);
        Assert.Equal(1, closed);
        // In the talk it doesn't open, nor is it offered.
        me.ToggleTrade();
        Assert.Null(me.Trade);
        Assert.Equal("Not during a talk.", me.Notice);
        Assert.DoesNotContain("T: give another player something", me.StatusLines());
    }

    [ContentFact]
    public void TheGivingScreenClosesWhenTheStoryPausesTheGame()
    {
        var world = TwoPlayers(sameSystem: true);
        world.TalkLibrary = TestContent.With(p => Comm.TalkLibrary.Load(p, Data.ConversationScripts.Load(DataDir)));
        var guest = new PlayerSession(Systems, null, "guest", TestContent.Shapes.HyperFlagship.Count);
        guest.Observe(world.Snapshot());
        guest.ToggleTrade();
        Assert.NotNull(guest.Trade);
        int closed = 0;
        guest.TradeClosed += () => ++closed;
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.VisitStarbase(world.Host);   // the opening: the game waits for everyone
        Assert.True(world.StoryPaused);
        world.SubmitInput("host", world.Frame + 1, Hyperspace.ShipInput.None);
        world.Step();
        guest.Observe(world.Snapshot());
        Assert.Null(guest.Trade);
        Assert.Equal(1, closed);
        guest.ToggleTrade();
        Assert.Null(guest.Trade);
        Assert.Equal(1, closed);   // a refused opening closes nothing
        Assert.Equal(Gift.Why(TradeRefusal.Paused), guest.Notice);
    }

    [ContentFact]
    public void AScriptsShipInputWaitsWhileTheGivingScreenIsOpen()
    {
        var world = TwoPlayers(sameSystem: false);
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count,
            InputScript.Parse("thrust:1,give+thrust:2,give+thrust:1"));
        int closed = 0;
        me.TradeClosed += () => ++closed;
        me.Observe(world.Snapshot());
        me.NextInput(Hyperspace.ShipInput.None);
        Assert.Null(me.Trade);
        Assert.Equal(Hyperspace.ShipInput.None, me.NextInput(Hyperspace.ShipInput.None));   // opened: the thrust waits
        Assert.NotNull(me.Trade);
        Assert.Equal(0, closed);
        Assert.Equal(Hyperspace.ShipInput.None, me.NextInput(Hyperspace.ShipInput.None));
        Assert.Equal(Hyperspace.ShipInput.Thrust, me.NextInput(Hyperspace.ShipInput.None));   // closed: it flies
        Assert.Null(me.Trade);
        Assert.Equal(1, closed);   // the script's give closing it says so, as a key's does
    }

    [ContentFact]
    public void StartUnitsFillTheHostsWallet()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        Assert.Contains("2500", host.StartWithUnits(2500));   // --start-units (testing: giving them)
        Assert.Equal(2500u, host.World.Host.Ship.Hold.ResourceUnits);
    }
}
