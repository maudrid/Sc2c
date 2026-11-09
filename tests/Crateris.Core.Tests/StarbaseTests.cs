// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The allied starbase (docs/plans/stage-3.md, item 8): docking, the
/// menu, the commander's talk under the dialogue lock, outfitting and the
/// shipyard through the host's screens, leaving, and the ship's slots in
/// saves and snapshots.
/// </summary>
[Trait("Topic", "starbase")]
public class StarbaseTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));
    private const int Sol = 82, Earth = 2, Luna = 1;

    // An allied game: the host (and a guest) by the starbase in Earth's
    // inner system, the starbase ready long since.
    private static GameWorld Allied(bool guest = false)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        if (guest)
        {
            Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
            world.UndockShown("guest");
        }
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", 3);
        story.Set("STARBASE_DAY", 3);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);   // no news
        world.CommanderMet = true;
        foreach (var p in world.Players.Where(p => p.Present))
        {
            world.PutInOrbit(p.Id, Sol, Earth, Luna);
            p.Ship.Ip!.ReturnFromOrbit();
        }
        return world;
    }

    private static Player Docked(GameWorld world, string id = "host")
    {
        var p = world.Find(id)!;
        world.VisitStarbase(p);
        Assert.Equal(Activity.Starbase, p.Ship.Activity);
        return p;
    }

    private static void Keys(GameWorld world, string id, params MenuKey[] keys)
    {
        foreach (var k in keys)
            world.StarbaseKey(id, k, out _);
    }

    private static int Reply(GameWorld world, string id, string phrase)
    {
        var talk = world.Find(id)!.Ship.Visit!.Talk!;
        int n = Scripts["starbas"].PhraseNumber(phrase);
        int i = talk.Replies.Select(r => r.Phrase).ToList().IndexOf(n);
        Assert.True(i >= 0, $"{phrase} isn't offered");
        return i;
    }

    [ContentFact]
    public void TouchingTheAlliedStarbaseDocksAtItsMenu()
    {
        var world = Allied();
        var host = Docked(world);
        Assert.Null(world.Visit);          // no opening: nothing for everyone
        Assert.False(world.StoryPaused);
        var view = world.StarbaseFor("host")!;
        Assert.Equal((StarbaseScreen.Menu, StarbaseMenu.TalkCommander), (view.Screen, view.State));
        // The menu goes round: up from Talk is Leave.
        Keys(world, "host", MenuKey.Up);
        Assert.Equal(StarbaseMenu.DepartBase, world.StarbaseFor("host")!.State);
        Keys(world, "host", MenuKey.Down, MenuKey.Right);
        Assert.Equal(StarbaseMenu.OutfitStarship, world.StarbaseFor("host")!.State);
        // The ship is drawn in its system for everyone, docked.
        Assert.Equal(Activity.Starbase, world.Snapshot().Ships.Single(s => s.Id == "host").Activity);
        // Leave: back in flight by the starbase.
        Keys(world, "host", MenuKey.Left, MenuKey.Left, MenuKey.Select);
        Assert.Equal(Activity.InnerSystem, host.Ship.Activity);
        Assert.Null(world.StarbaseFor("host"));
    }

    [ContentFact]
    public void TheCommandersTalkFromTheMenuIsOrdinaryAndGreetsOnceAVisit()
    {
        var world = Allied();
        var host = Docked(world);
        Keys(world, "host", MenuKey.Select);          // Talk
        var visit = host.Ship.Visit!;
        Assert.Equal((VisitStep.MenuTalk, Activity.Talk, StarbaseScreen.Talk),
            (visit.Step, host.Ship.Activity, world.StarbaseFor("host")!.Screen));
        Assert.False(world.StoryPaused);              // an ordinary talk: the game goes on
        Assert.Equal("host", world.DialogueLocks!.Holder("COMMANDER_CONVERSATION"));
        // The first talk of a visit greets the player (NORMAL_HELLO).
        int hello = Scripts["starbas"].PhraseNumber("NORMAL_HELLO_A0");
        Assert.Contains(visit.Talk!.Events.OfType<LineQueued>(), l => l.Phrase >= hello && l.Phrase < hello + 16);
        Assert.True(world.ChooseReply("host", Reply(world, "host", "goodbye_commander"), visit.Round));
        Assert.Equal((VisitStep.Menu, StarbaseScreen.Menu), (visit.Step, world.StarbaseFor("host")!.Screen));
        Assert.Null(world.DialogueLocks.Holder("COMMANDER_CONVERSATION"));
        // Again: greeted already this visit (RETURN_HELLO).
        Keys(world, "host", MenuKey.Select);
        Assert.Contains(visit.Talk!.Events.OfType<LineQueued>(), l => l.Phrase == Scripts["starbas"].PhraseNumber("RETURN_HELLO"));
        world.ChooseReply("host", Reply(world, "host", "goodbye_commander"));
        // A new visit greets again (DoStarBase's leaving clears STARBASE_VISITED).
        Keys(world, "host", MenuKey.Up, MenuKey.Select);
        Docked(world);
        Keys(world, "host", MenuKey.Select);
        Assert.Contains(host.Ship.Visit!.Talk!.Events.OfType<LineQueued>(), l => l.Phrase >= hello && l.Phrase < hello + 16);
    }

    [ContentFact]
    public void ASecondPlayerIsToldWhoIsTalkingToTheCommander()
    {
        var world = Allied(guest: true);
        Docked(world);
        Docked(world, "guest");     // each docked player has their own menu at once
        Keys(world, "host", MenuKey.Select);
        Keys(world, "guest", MenuKey.Select);
        var view = world.StarbaseFor("guest")!;
        Assert.Equal((StarbaseScreen.Menu, "Host"), (view.Screen, view.Busy));
        // The rest of the menu works meanwhile.
        Keys(world, "guest", MenuKey.Down, MenuKey.Select);
        Assert.Equal(StarbaseScreen.Outfit, world.StarbaseFor("guest")!.Screen);
        Keys(world, "guest", MenuKey.Cancel, MenuKey.Up);
        // Once the host is done, the guest may talk.
        world.ChooseReply("host", Reply(world, "host", "goodbye_commander"));
        Keys(world, "guest", MenuKey.Select);
        Assert.Equal(VisitStep.MenuTalk, world.Find("guest")!.Ship.Visit!.Step);
        Assert.Equal("", world.StarbaseFor("guest")!.Busy);
    }

    [ContentFact]
    public void SellingMineralsGoesIntoTheTalkersWallet()
    {
        var world = Allied(guest: true);
        var guest = Docked(world, "guest");
        guest.Ship.Hold.LanderLeaves();
        guest.Ship.Hold.LanderReturns(guest.Ship.Hold.LanderOut!.Value.Crew, new[] { 0, 0, 0, 0, 0, 0, 10, 0 }, 0);
        Keys(world, "guest", MenuKey.Select);
        world.ChooseReply("guest", Reply(world, "guest", "have_minerals"));
        // 10 radioactives at 8 each.
        Assert.Equal((80u, 0u), (guest.Ship.Hold.ResourceUnits, world.Host.Ship.Hold.ResourceUnits));
        Assert.Equal(0, guest.Ship.Hold.MineralMass);
        Assert.Equal(1, world.Story!.Get("LIGHT_MINERAL_LOAD"));   // the group's count (decision 5)
    }

    [ContentFact]
    public void OutfittingThroughTheMenuBuysFromTheWalletAndRefitsTheShip()
    {
        var world = Allied();
        var host = Docked(world);
        host.Ship.Hold.ResourceUnits = 10000;
        uint capacity = host.Ship.Flight.FuelCapacity;
        int speed = host.Ship.Ip!.MaxSpeed;
        // Outfit, Modules; the first module slot is empty: a crew pod... and
        // right to a storage bay, then a fuel tank.
        Keys(world, "host", MenuKey.Down, MenuKey.Select, MenuKey.Down, MenuKey.Select, MenuKey.Select);
        Assert.Equal(ShipModules.CrewPod, world.StarbaseFor("host")!.OutfitState);
        Keys(world, "host", MenuKey.Right, MenuKey.Right, MenuKey.Select);
        Assert.Equal(ShipModules.FuelTank, host.Ship.Modules.ModuleSlots[0]);
        Assert.Equal(10000u - 10 * 50, host.Ship.Hold.ResourceUnits);
        Assert.Equal(capacity + Hyperspace.Fuel.TankSize, host.Ship.Flight.FuelCapacity);
        // Up twice to the drives, right to slot 0 (empty), a thruster.
        Keys(world, "host", MenuKey.Up, MenuKey.Up, MenuKey.Select, MenuKey.Select);
        Assert.Equal(3, host.Ship.Modules.Thrusters);
        Assert.True(host.Ship.Ip.MaxSpeed > speed);
        Assert.Equal(Hyperspace.ShipDrive.ForHyperspace(3, 2), host.Ship.Flight.Drive);
        // Fuel: buy a unit.
        Keys(world, "host", MenuKey.Cancel, MenuKey.Up, MenuKey.Select, MenuKey.Up);
        Assert.Equal(1100u, host.Ship.Flight.Fuel);
        // The snapshot carries the slots.
        Assert.Equal(host.Ship.Modules.Save(), world.Snapshot().Ships.Single(s => s.Id == "host").Modules);
    }

    [ContentFact]
    public void TheShipyardHiresCrewAtTheSharedPrice()
    {
        var world = Allied(guest: true);
        var guest = Docked(world, "guest");
        guest.Ship.Hold.ResourceUnits = 100;
        guest.Ship.Hold.SetCrew(5);
        Keys(world, "guest", MenuKey.Down, MenuKey.Down, MenuKey.Select, MenuKey.Select, MenuKey.Select, MenuKey.Up, MenuKey.Up);
        Assert.Equal((7, 94u), (guest.Ship.Hold.Crew, guest.Ship.Hold.ResourceUnits));
        Assert.Equal(2, world.Story!.CrewPurchased);
        // The escort's kit holds 10: the sixth more is refused.
        Keys(world, "guest", MenuKey.Up, MenuKey.Up, MenuKey.Up);
        int refusals = world.StarbaseFor("guest")!.Refusals;
        Keys(world, "guest", MenuKey.Up);
        Assert.Equal((10, refusals + 1), (guest.Ship.Hold.Crew, world.StarbaseFor("guest")!.Refusals));
    }

    [ContentFact]
    public void AnEscortBuysOnlyFuelCrewLandersAndStorageBays()
    {
        var world = Allied(guest: true);
        var guest = Docked(world, "guest");
        guest.Ship.Hold.ResourceUnits = 100000;
        var slot = guest.Ship.Modules.ModuleSlots.ToList().IndexOf(ShipModules.EmptyModule);
        Assert.False(world.Outfit("guest", new Install(SlotKind.Module, slot, ShipModules.CrewPod)));
        Assert.False(world.Outfit("guest", new Install(SlotKind.Drive, 0, ShipModules.FusionThruster)));
        Assert.True(world.Outfit("guest", new Install(SlotKind.Module, slot, ShipModules.StorageBay)));
        Assert.Equal(1000, guest.Ship.Hold.StorageCapacity);
        Assert.True(world.Outfit("guest", new Install(SlotKind.Lander, 1, ShipModules.PlanetLander)));
        Assert.False(world.Outfit("guest", new Remove(SlotKind.Module, guest.Ship.Modules.ModuleSlots.ToList().IndexOf(ShipModules.FuelTank))));
        Assert.True(world.Outfit("guest", new Remove(SlotKind.Module, slot)));
        // A flagship may buy them all.
        Docked(world);
        world.Host.Ship.Hold.ResourceUnits = 100000;
        Assert.True(world.Outfit("host", new Install(SlotKind.Module, 0, ShipModules.CrewPod)));
        Assert.Equal(100, world.Host.Ship.Hold.CrewCapacity);
    }

    [ContentFact]
    public void TheRulesRefuseWhatTheMenuWouldNeverOffer()
    {
        var world = Allied();
        Docked(world);
        world.Host.Ship.Hold.ResourceUnits = 100000;
        Assert.False(world.Outfit("host", new Install(SlotKind.Module, 2, ShipModules.StorageBay)));   // slot 2 holds the crew pod
        Assert.False(world.Outfit("host", new Install(SlotKind.Module, 5, ShipModules.GunWeapon)));    // weapons at the front and back only
        Assert.False(world.Outfit("host", new Install(SlotKind.Module, 0, ShipModules.BlasterWeapon))); // not for sale yet
        Assert.False(world.Outfit("host", new Install(SlotKind.Module, 16, ShipModules.StorageBay)));
        Assert.False(world.Outfit("host", new Install(SlotKind.Drive, 0, ShipModules.TurningJets)));
        Assert.False(world.Outfit("host", new ChangeFuel(1)));
        Assert.True(world.Outfit("host", new Install(SlotKind.Module, 0, ShipModules.GunWeapon)));
        // Not docked: nothing.
        Keys(world, "host", MenuKey.Up, MenuKey.Select);
        Assert.False(world.Outfit("host", new Install(SlotKind.Module, 3, ShipModules.StorageBay)));
    }

    [ContentFact]
    public void OnlyTheHostSavesFromTheStarbase()
    {
        var world = Allied(guest: true);
        Docked(world);
        Docked(world, "guest");
        foreach (var id in new[] { "host", "guest" })
            Keys(world, id, MenuKey.Down, MenuKey.Select, MenuKey.Down, MenuKey.Down);
        Assert.Equal(OutfitOutcome.SaveLoad, world.StarbaseKey("host", MenuKey.Select, out bool save));
        Assert.True(save);
        Assert.Equal(OutfitOutcome.Refused, world.StarbaseKey("guest", MenuKey.Select, out save));
        Assert.False(save);
    }

    [ContentFact]
    public void LeavingTheGameWhileDockedEndsTheVisitAndTheTalkTakingNothingBack()
    {
        var world = Allied(guest: true);
        var guest = Docked(world, "guest");
        guest.Ship.Hold.ResourceUnits = 5000;
        Assert.True(world.Outfit("guest", new ChangeFuel(100)));
        Keys(world, "guest", MenuKey.Select);
        world.Leave("guest");
        Assert.Null(guest.Ship.Visit);
        Assert.Null(world.DialogueLocks!.Holder("COMMANDER_CONVERSATION"));
        Assert.Equal(5000u - 20, guest.Ship.Hold.ResourceUnits);   // the fuel bought is kept
    }

    [ContentFact]
    public void ASaveWhileDockedKeepsTheShipsSlotsInFlightByTheStarbase()
    {
        var world = Allied();
        var host = Docked(world);
        host.Ship.Hold.ResourceUnits = 10000;
        Assert.True(world.Outfit("host", new Install(SlotKind.Module, 0, ShipModules.FuelTank)));
        Assert.True(world.Outfit("host", new Install(SlotKind.Drive, 0, ShipModules.FusionThruster)));
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        var ship = loaded.Host.Ship;
        Assert.Equal(host.Ship.Modules.Save(), ship.Modules.Save());
        Assert.Equal((Activity.InnerSystem, host.Ship.Flight.FuelCapacity, host.Ship.Ip!.MaxSpeed),
            (ship.Activity, ship.Flight.FuelCapacity, ship.Ip!.MaxSpeed));
        // An older save's ship has its loadout's slots.
        var old = System.Text.Json.Nodes.JsonNode.Parse(HostSave.ToJson(world))!;
        old["version"] = 8;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes));
        foreach (var p in old["players"]!.AsArray())
            p!.AsObject().Remove("modules");
        var older = HostSave.FromJson(old.ToJsonString(), Systems, TestContent.Shapes).Host.Ship;
        Assert.Equal(ShipModules.FromLoadout(ShipKind.Flagship, older.Loadout).Save(), older.Modules.Save());
    }

    [Fact]
    public void ANewFlagshipsSlotsAreANewGamesInUqm()
    {
        var loadout = NewGameData.Load(DataDir, Systems.Map).Flagship;
        var m = ShipModules.FromLoadout(ShipKind.Flagship, loadout);
        Assert.Equal(new[] { 20, 20, 20, 20, 20, 1, 1, 20, 20, 20, 20 }, m.DriveSlots.Select(x => (int)x));
        Assert.Equal(new[] { 2, 21, 21, 21, 21, 21, 2, 21 }, m.JetSlots.Select(x => (int)x));
        Assert.Equal(new[] { 22, 5, 3, 22, 22, 22, 22, 22, 4, 22, 22, 22, 22, 22, 22, 9 }, m.ModuleSlots.Select(x => (int)x));
        Assert.Equal((50, 500, 6000u), (m.CrewCapacity, m.StorageCapacity, m.FuelCapacity));
        var escort = ShipModules.FromLoadout(ShipKind.Escort, new GameSettings().EscortLoadout);
        Assert.Equal((10, 0), (escort.CrewCapacity, escort.Count(ShipModules.GunWeapon)));
        Assert.Throws<InvalidDataException>(() => ShipModules.Restore(m.Save() with { Modules = new byte[3] }));
        Assert.Throws<InvalidDataException>(() => ShipModules.Restore(m.Save() with { Drives = Enumerable.Repeat((byte)2, 11).ToArray() }));
    }

    [ContentFact]
    public void AGuestsKeysAndRenamingGoThroughTheHostAndItIsSentItsScreen()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        var world = host.World;
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        host.Hello(1, "guest", "Guest", Protocol.Version);
        world.UndockShown("guest");
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        world.Find("guest")!.Ship.Ip!.ReturnFromOrbit();
        host.GuestRename(1, "Not yet", "");
        Assert.NotEqual("Not yet", world.Find("guest")!.Ship.Name);   // only docked
        world.VisitStarbase(world.Find("guest")!);
        var sent = host.StarbaseUpdates().Single(u => u.Peer == 1);
        Assert.Equal(world.StarbaseFor("guest"), StarbaseView.Decode(sent.Data));
        host.GuestStarbaseKey(1, (int)MenuKey.Down);
        Assert.Equal(StarbaseMenu.OutfitStarship, StarbaseView.Decode(host.StarbaseUpdates().Single(u => u.Peer == 1).Data)!.State);
        Assert.Empty(host.StarbaseUpdates());   // unchanged: nothing sent
        host.GuestStarbaseKey(1, 99);           // no such key: ignored
        host.GuestRename(1, "Starlight", "");
        Assert.Equal("Starlight", world.Find("guest")!.Ship.Name);
        // Leaving the starbase: an empty screen.
        host.GuestStarbaseKey(1, (int)MenuKey.Down);
        host.GuestStarbaseKey(1, (int)MenuKey.Down);
        host.GuestStarbaseKey(1, (int)MenuKey.Select);
        Assert.Equal(Net.Protocol.NoView, host.StarbaseUpdates().Single(u => u.Peer == 1).Data);
    }

    [Fact]
    public void TheStarbaseScreensMessageRoundTripsAndBadOnesAreRefused()
    {
        var view = new StarbaseView(StarbaseScreen.Outfit, 1, ShipModules.EmptyModule, 15, true, 2, YardMode.Crew, 0xF3, "Zoë", 7);
        Assert.Equal(view, StarbaseView.Decode(view.Encode()));
        Assert.Null(StarbaseView.Decode(Array.Empty<byte>()));
        var bytes = view.Encode();
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode(new byte[] { 9 }));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { State = 4 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode(bytes[..3]));
        // Outfitting's slot within its row's slots: the jets have 8, the landers 10, the drives 11.
        Assert.Equal(7, StarbaseView.Decode((view with { OutfitState = ShipModules.EmptyJet, OutfitItem = 7 }).Encode())!.OutfitItem);
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { OutfitState = ShipModules.EmptyJet, OutfitItem = 8 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { OutfitState = ShipModules.TurningJets, OutfitItem = 12 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { OutfitState = ShipModules.PlanetLander, OutfitItem = 10 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { OutfitState = ShipModules.EmptyDrive, OutfitItem = 11 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { OutfitItem = 16 }).Encode()));
        // Off Modules, the menu's entries and the fuel.
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { Installing = false, OutfitState = 5, OutfitItem = 0 }).Encode()));
        // The shipyard's mode and cursor: a hangar slot (0 to 11) or the flagship with a column.
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { YardMode = (YardMode)3 }).Encode()));
        Assert.Equal(11, StarbaseView.Decode((view with { YardCursor = 11 }).Encode())!.YardCursor);
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { YardCursor = 12 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { YardCursor = 0xF6 }).Encode()));
        Assert.Throws<InvalidDataException>(() => StarbaseView.Decode((view with { YardCursor = 0x10 }).Encode()));
    }

    [ContentFact]
    public void TheClientSendsItsStarbaseKeysAndShowsRefusalsAndABusyCommander()
    {
        var world = Allied();
        Docked(world);
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        me.Observe(world.Snapshot());
        me.ShowStarbase(world.StarbaseFor("host"));
        Assert.Equal(PlayerScreen.Starbase, me.Screen);   // the starbase's screens, drawn
        me.PressMenu(MenuKey.Down);
        me.PressMenu(MenuKey.Select);
        Assert.Equal(new[] { MenuKey.Down, MenuKey.Select }, me.TakeStarbaseKeys());
        Assert.Empty(me.TakeStarbaseKeys());
        Assert.Contains(me.StatusLines(), l => l.Contains("[Talk to the commander]"));
        me.ShowStarbase(me.Starbase! with { Busy = "Guest" });
        Assert.Contains("talking with Guest", me.Notice);
        me.ShowStarbase(me.Starbase! with { Refusals = me.Starbase.Refusals + 1 });
        Assert.Contains("can't", me.Notice);
        me.Rename("Rocinante", null);
        Assert.Equal(("Rocinante", (string?)null), me.TakeRename());
    }

    [ContentFact]
    public void TheStarterSeesTheClosingLinesAsTheOpeningDocksAndNotAgainOnLeaving()
    {
        var world = Allied();
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        var me = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count);
        world.VisitStarbase(world.Host);
        for (int i = 0; i < StarbaseVisit.TimePassingFrames; ++i)
        {
            world.SubmitInput("host", world.Frame + 1, ShipInput.None);
            world.Step();
        }
        me.Observe(world.Snapshot());
        me.ShowTalk(world.TalkFor("host"));
        world.ChooseReply("host", Reply(world, "host", "goodbye_commander"));
        me.Observe(world.Snapshot());
        me.ShowTalk(world.TalkFor("host"));
        string goodbye = me.Notice;
        Assert.NotEqual("", goodbye);                     // shown as the opening docks
        Keys(world, "host", MenuKey.Up, MenuKey.Select);   // Leave
        me.Notify("");
        me.ShowTalk(world.TalkFor("host"));
        Assert.Equal("", me.Notice);                      // not again
        Assert.Null(me.Talk);
    }

    [ContentFact]
    public void ATalkCutOffKeepsItsBulletinsAsGiven()
    {
        var world = Allied(guest: true);
        world.Story!.Set32("STARBASE_BULLETS0", 0xFFFFFFFF & ~(1u << 6));
        world.Story.Set("ZOQFOT_DISTRESS", 1);    // bulletin 6
        Docked(world, "guest");
        Keys(world, "guest", MenuKey.Select);
        Assert.Equal(1u << 6, ((Comm.Scripts.StarbaseScript)world.Find("guest")!.Ship.Visit!.Talk!.Script).GivenBulletins);
        // A save in the middle of it keeps it given...
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        Assert.Equal(0xFFFFFFFFu, loaded.Story!.Get32("STARBASE_BULLETS0"));
        Assert.Equal(0xFFFFFFFFu & ~(1u << 6), world.Story.Get32("STARBASE_BULLETS0"));
        // ...and so does leaving.
        world.Leave("guest");
        Assert.Equal(0xFFFFFFFFu, world.Story.Get32("STARBASE_BULLETS0"));
    }

    [ContentFact]
    public void TheCommandersAdviceGradesTheFlagshipWhoeverAsks()
    {
        var world = Allied(guest: true);
        Docked(world, "guest");
        Keys(world, "guest", MenuKey.Select);
        var talk = world.Find("guest")!.Ship.Visit!.Talk!;
        Assert.Equal(world.Host.Ship.Modules.ModuleNames, talk.Ship.ModuleSlots);
        Assert.Equal((2, 2), (talk.Ship.Thrusters, talk.Ship.Jets));
        world.Host.Ship.Modules.SetDrive(0, ShipModules.FusionThruster);
        Assert.Equal(3, talk.Ship.Thrusters);
        Assert.Equal(world.Find("guest")!.Ship.Hold.Crew, talk.Ship.Crew);   // the talker's own crew
    }

    [ContentFact]
    public void WithoutTheStarbasesDateTheTimedBulletinsArentDue()
    {
        var world = Allied();
        world.Story!.Set("STARBASE_MONTH", 0);
        world.Story.Set("STARBASE_DAY", 0);
        world.Story.Set32("STARBASE_BULLETS0", 0);
        Docked(world);
        Keys(world, "host", MenuKey.Select);
        Assert.Equal(VisitStep.MenuTalk, world.Host.Ship.Visit!.Step);
    }

    [ContentFact]
    public void AStarterAwayAsTheirOpeningDocksComesBackInFlight()
    {
        var world = Allied(guest: true);
        world.Story!.Set("MOONBASE_ON_SHIP", 1);
        var guest = world.Find("guest")!;
        world.VisitStarbase(guest);
        world.Leave("guest");                      // the replies pass to the host
        Assert.Equal("host", world.Visit!.Replier.Id);
        for (int i = 0; i < StarbaseVisit.TimePassingFrames; ++i)
        {
            world.SubmitInput("host", world.Frame + 1, ShipInput.None);
            world.Step();
        }
        world.ChooseReply("host", world.Visit!.Talk!.Replies.Select(r => r.Function).ToList().IndexOf("ByeBye"));
        Assert.Null(world.Visit);
        Assert.Null(guest.Ship.Visit);
        Assert.Equal(JoinResult.Rejoined, world.Join("guest", "Guest"));
        Assert.Equal(Activity.InnerSystem, guest.Ship.Activity);
    }

    // A new game's flagship and prices, for the shipyard's menu.
    private static Outfitting YardRules()
    {
        string modules = string.Join(", ", new[] { "EMPTY", "FUEL_TANK", "CREW_POD" }.Concat(Enumerable.Repeat("EMPTY", 5))
            .Append("STORAGE_BAY").Concat(Enumerable.Repeat("EMPTY", 6)).Append("GUN_WEAPON").Select(m => $"\"{m}\""));
        var sc = System.Text.Json.JsonDocument.Parse("{\"state\": {}, \"costs\": {}, \"fuel_cost\": 20, \"crew_cost\": 3, "
            + "\"ship\": {\"crew\": 40, \"fuel\": 1000, \"landers\": 1, \"ru\": 2000, \"elements\": [0, 0, 0, 0, 0, 0, 0, 0], "
            + $"\"modules\": [{modules}], \"drives\": [5, 6], \"jets\": [0, 6]}}}}").RootElement;
        var (ship, story) = OutfitTests.Start(sc);
        return new Outfitting(ship, story);
    }

    [Fact]
    public void ARefusedSaveDrawsNothingAndARenameRedrawsTheNames()
    {
        // A guest's Save/Load is refused, and draws nothing, as any refusal; the host's draws the menu again.
        var yard = new ShipyardMenu(YardRules(), _ => true);
        yard.TakeOps();
        yard.Key(MenuKey.Down);
        yard.TakeOps();
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Select, canSave: false));
        Assert.Empty(yard.TakeOps());
        Assert.Equal(OutfitOutcome.SaveLoad, yard.Key(MenuKey.Select));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.SaveLoad) }, yard.TakeOps());
        var rules = YardRules();
        var outfit = new OutfitMenu(rules, rules.Apply);
        outfit.Open();
        outfit.Key(MenuKey.Down);
        outfit.Key(MenuKey.Down);
        outfit.TakeOps();
        Assert.Equal(OutfitOutcome.Refused, outfit.Key(MenuKey.Select, canSave: false));
        Assert.Empty(outfit.TakeOps());
        // Renamed while outfitting: onNamingDone's flagship name, and the captain's.
        outfit.Renamed(ship: true, captain: true);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.FlagshipName), new ScreenOp(ScreenOpKind.CaptainsName) }, outfit.TakeOps());
    }

    [Fact]
    public void TheShipyardsMenuGoesRoundItsCursorRoundTheHangarAndItsCrewModeHiresAndSendsAway()
    {
        var acts = new List<OutfitAction>();
        var yard = new ShipyardMenu(YardRules(), a => { acts.Add(a); return a is HireCrew; });
        yard.Key(MenuKey.Up);
        Assert.Equal(ShipyardMenu.Exit, yard.State);
        yard.Key(MenuKey.Right);
        Assert.Equal(ShipyardMenu.Crew, yard.State);
        // Crew: the cursor on the flagship (DoModifyShips), then its crew.
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Select));
        Assert.Equal((YardMode.Ships, ShipyardMenu.Flagship), (yard.Mode, yard.Cursor));
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Select));
        Assert.True(yard.CrewMode);
        Assert.Equal(OutfitOutcome.Acted, yard.Key(MenuKey.Up));
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Down));
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Left));   // only up and down change the crew
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Cancel));
        Assert.Equal(YardMode.Ships, yard.Mode);
        // Round the hangar: up from the flagship to the bottom row, along it, and down to the flagship again.
        yard.Key(MenuKey.Up);
        Assert.Equal(6, yard.Cursor);
        yard.Key(MenuKey.Left);
        Assert.Equal(11, yard.Cursor);
        Assert.Equal(OutfitOutcome.Refused, yard.Key(MenuKey.Select));   // building fleet ships: stage 4
        yard.Key(MenuKey.Down);
        Assert.Equal(0xF5, yard.Cursor);   // the flagship, the column kept
        Assert.True(ShipyardMenu.OnFlagship(yard.Cursor));
        yard.Key(MenuKey.Right);           // left and right don't leave the flagship
        Assert.Equal(0xF5, yard.Cursor);
        yard.Key(MenuKey.Down);            // down from the flagship: the top row
        Assert.Equal(5, yard.Cursor);
        yard.Key(MenuKey.Up);
        yard.Key(MenuKey.Select);
        Assert.True(yard.CrewMode);
        yard.Key(MenuKey.Select);
        Assert.Equal(OutfitOutcome.None, yard.Key(MenuKey.Cancel));
        Assert.Equal((YardMode.Menu, ShipyardMenu.Crew), (yard.Mode, yard.State));
        yard.Key(MenuKey.Down);
        Assert.Equal(OutfitOutcome.SaveLoad, yard.Key(MenuKey.Select));
        yard.Key(MenuKey.Down);
        Assert.Equal(OutfitOutcome.Left, yard.Key(MenuKey.Select));
        Assert.Equal(OutfitOutcome.Left, new ShipyardMenu(YardRules(), _ => true).Key(MenuKey.Cancel));
        Assert.Equal(new OutfitAction[] { new HireCrew(), new DismissCrew() }, acts);
    }

    // What the host has drawn for a player since the last take, through its message as the client gets it.
    private static List<StarbaseDrawing> Drawn(GameWorld world, string id = "host") =>
        world.TakeStarbaseDrawing(id) is { Count: > 0 } d ? StarbaseDrawing.Decode(StarbaseDrawing.Encode(d)) : new();

    [ContentFact]
    public void TheHostSendsEachKeysDrawingWithTheShipAsThatKeyLeftIt()
    {
        var world = Allied();
        var host = Docked(world);
        host.Ship.Hold.ResourceUnits = 10000;
        // Docked: DoStarBase drawn afresh, the drawing not reading the ship.
        var d = Drawn(world).Single();
        Assert.Null(d.Ship);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuOpened, StarbaseMenu.TalkCommander) }, d.Ops);
        Assert.Empty(Drawn(world));   // taken: nothing more
        Keys(world, "host", MenuKey.Down);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuMoved, StarbaseMenu.TalkCommander, StarbaseMenu.OutfitStarship) },
            Drawn(world).Single().Ops);
        // Outfitting opens, drawn with the ship: the flagship's UQM names are the client's to read.
        Keys(world, "host", MenuKey.Select);
        d = Drawn(world).Single();
        Assert.Equal(ScreenOpKind.OutfitOpened, d.Ops[0].Kind);
        Assert.Equal(host.Ship.Modules.ModuleSlots, d.Ship!.Modules);
        Assert.Equal((null, null, 10000u), (d.Ship.ShipName, d.Ship.Captain, d.Ship.ResourceUnits));
        Assert.Equal(world.Story!.ModuleCost("FUEL_TANK"), d.Ship.ModuleCosts[ShipModules.FuelTank]);
        // Two units of fuel bought before the drawing is taken: each drawn with its own ship.
        uint fuel = host.Ship.Flight.Fuel;
        Keys(world, "host", MenuKey.Select, MenuKey.Up, MenuKey.Up);
        var bought = Drawn(world);
        Assert.Equal(2, bought.Count);
        Assert.Equal(new[] { fuel + 100, fuel + 200 }, bought.Select(b => b.Ship!.Fuel));
        Assert.All(bought, b => Assert.Equal(new[] { ScreenOpKind.Gauges, ScreenOpKind.RedistributeFuel }, b.Ops.Select(o => o.Kind)));
        // Renamed: the names drawn, the new one sent.
        world.Rename("host", "Rocinante", null);
        d = Drawn(world).Single();
        Assert.Equal(("Rocinante", (string?)null), (d.Ship!.ShipName, d.Ship.Captain));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.FlagshipName) }, d.Ops);
        // Leaving outfitting: back on the menu, drawn afresh on its cursor.
        Keys(world, "host", MenuKey.Cancel, MenuKey.Cancel);
        Assert.Equal(new ScreenOp(ScreenOpKind.MenuOpened, StarbaseMenu.OutfitStarship), Drawn(world).Last().Ops.Last());
    }

    [ContentFact]
    public void AGuestsDrawingGoesToItsPeerAndALostPictureIsDrawnAfresh()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        var world = host.World;
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        host.Hello(1, "guest", "Guest", Protocol.Version);
        world.UndockShown("guest");
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        world.Find("guest")!.Ship.Ip!.ReturnFromOrbit();
        Assert.Empty(host.StarbaseDrawings());   // not docked: nothing
        world.VisitStarbase(world.Find("guest")!);
        var sent = host.StarbaseDrawings().Single();
        Assert.Equal(1, sent.Peer);
        Assert.Equal(ScreenOpKind.MenuOpened, StarbaseDrawing.Decode(sent.Data).Single().Ops.Single().Kind);
        Assert.Empty(host.HostStarbaseDrawing());   // the host isn't docked
        host.GuestStarbaseKey(1, (int)MenuKey.Down);
        host.GuestStarbaseKey(1, (int)MenuKey.Select);
        host.GuestStarbaseKey(1, (int)MenuKey.Down);   // outfitting's menu on Modules
        var drawn = StarbaseDrawing.Decode(host.StarbaseDrawings().Single().Data);
        Assert.Equal(new[] { ScreenOpKind.MenuMoved, ScreenOpKind.OutfitOpened, ScreenOpKind.MenuStrings },
            drawn.Select(x => x.Ops[0].Kind));
        // A guest's captain is the name they joined with; its escort's name the host's setting.
        Assert.Equal(("Guest", world.Find("guest")!.Ship.Name), (drawn[1].Ship!.Captain, drawn[1].Ship!.ShipName));
        // The client lost its picture: the screen drawn afresh, as it stands (outfitting, on Modules).
        host.GuestStarbaseRedraw(1);
        drawn = StarbaseDrawing.Decode(host.StarbaseDrawings().Single().Data);
        Assert.Equal(new[] { ScreenOpKind.OutfitOpened, ScreenOpKind.SisDisplay, ScreenOpKind.MenuStrings },
            drawn.Single().Ops.Select(o => o.Kind));
        Assert.Equal(OutfitMenu.Modules, drawn.Single().Ops[2].B);
        // Drawing no one takes is dropped past its bound, and drawn afresh when next taken.
        for (int i = 0; i < StarbaseMenu.MostKept + 10; ++i)
            world.StarbaseKey("guest", MenuKey.Down, out _);
        drawn = StarbaseDrawing.Decode(host.StarbaseDrawings().Single().Data);
        Assert.Equal(ScreenOpKind.OutfitOpened, drawn.Single().Ops[0].Kind);
        Assert.Empty(host.StarbaseDrawings());
    }

    [Fact]
    public void TheStarbaseDrawingMessageRoundTripsAndBadOnesAreRefused()
    {
        var ship = new StarbaseShip(new byte[ShipModules.ModuleSlotCount], new byte[ShipModules.DriveSlotCount],
            new byte[ShipModules.JetSlotCount], 31, 1234, 2, 4321, 77, "Zoë's", null, 3, 9, 20, 3,
            Enumerable.Range(0, ShipModules.Modules).ToArray(), 50);
        var ops = new List<ScreenOp>
        {
            new(ScreenOpKind.Gauges, ScreenOp.Undefined, -150, 9), new(ScreenOpKind.ShipPiece, ShipModules.FuelTank, 15),
            new(ScreenOpKind.CrewPoint, 1, 30), new(ScreenOpKind.MenuMoved, 3, 0),
        };
        var drawings = new[] { new StarbaseDrawing(ship, ops), new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.MenuOpened, 2) }) };
        var back = StarbaseDrawing.Decode(StarbaseDrawing.Encode(drawings));
        Assert.Equal(ops, back[0].Ops);
        Assert.Equal(ship.ModuleCosts, back[0].Ship!.ModuleCosts);
        Assert.Equal(ship with { Modules = back[0].Ship!.Modules, Drives = back[0].Ship!.Drives, Jets = back[0].Ship!.Jets,
            ModuleCosts = back[0].Ship!.ModuleCosts }, back[0].Ship);
        Assert.Null(back[1].Ship);
        Assert.Empty(StarbaseDrawing.Decode(StarbaseDrawing.Encode(Array.Empty<StarbaseDrawing>())));
        void Bad(StarbaseDrawing d) =>
            Assert.Throws<InvalidDataException>(() => StarbaseDrawing.Decode(StarbaseDrawing.Encode(new[] { d })));
        // Operations the screens can't draw: no such kind, a slot past its row, a menu's entry past its end.
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp((ScreenOpKind)99) }));
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.ShipPiece, ShipModules.TurningJets, 8) }));
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.ModuleStrings, ShipModules.EmptyLander + 1) }));
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, 3) }));
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.MenuOpened, 4) }));
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.CrewPoint, 2, 0) }));
        Bad(new StarbaseDrawing(null, Enumerable.Repeat(new ScreenOp(ScreenOpKind.FlagshipStats), 65).ToList()));
        // A ship the screens can't draw: a slot that's no piece, more landers than there's room for.
        var slots = new byte[ShipModules.ModuleSlotCount];
        slots[3] = ShipModules.EmptyLander + 1;
        Bad(new StarbaseDrawing(ship with { Modules = slots }, ops));
        Bad(new StarbaseDrawing(ship with { Landers = ShipHold.MaxLanders + 1 }, ops));
        Bad(new StarbaseDrawing(ship with { Fuel = StarbaseShip.MostFuel + 1 }, ops));   // the screens loop over the fuel
        Bad(new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.OutfitOpened, StarbaseShip.MostFuel + 1) }));
        var bytes = StarbaseDrawing.Encode(drawings);
        Assert.Throws<InvalidDataException>(() => StarbaseDrawing.Decode(bytes.Append((byte)0).ToArray()));
        Assert.Throws<InvalidDataException>(() => StarbaseDrawing.Decode(bytes[..^1]));
    }

    [ContentFact]
    public void BackFromTheCommanderTheMenuIsDrawnAfreshAndAStartDockedGameIsAtIt()
    {
        var world = Allied();
        var host = Docked(world);
        world.TakeStarbaseDrawing("host");
        Keys(world, "host", MenuKey.Select);          // Talk: the communication screen, nothing drawn here
        Assert.Empty(world.TakeStarbaseDrawing("host"));
        world.ChooseReply("host", Reply(world, "host", "goodbye_commander"), host.Ship.Visit!.Round);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuOpened, StarbaseMenu.TalkCommander) },
            world.TakeStarbaseDrawing("host").Single().Ops);
        // --start-docked: a new game, the opening over, docked at the menu.
        var session = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems, TestContent.Shapes);
        session.World.TalkLibrary = Library.Value;
        session.World.DialogueLocks = DialogueLocks.Load(DataDir);
        Assert.Contains("docked", session.StartDocked());
        Assert.Equal(StarbaseScreen.Menu, session.StarbaseFor("host")!.Screen);
        Assert.Equal(ScreenOpKind.MenuOpened, session.HostStarbaseDrawing().Single().Ops.Single().Kind);
    }

    [Fact]
    public void TheScreensDrawnAfreshAreAsTheyStand()
    {
        var gauges = new ScreenOp(ScreenOpKind.SisDisplay);
        // The shipyard: as it opens, the status area, the menu on its entry.
        var yard = new ShipyardMenu(YardRules(), _ => true);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.YardOpened), gauges, new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) },
            yard.Redraw());
        yard.Key(MenuKey.Down);
        Assert.Equal(new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.SaveLoad), yard.Redraw().Last());
        // Outfitting: as it opens with the fuel aboard now, the status area, then the menu or the radar.
        var rules = YardRules();
        var outfit = new OutfitMenu(rules, rules.Apply);
        outfit.Open();
        var opened = new ScreenOp(ScreenOpKind.OutfitOpened, (int)rules.Ship.Fuel);
        Assert.Equal(new[] { opened, gauges }, outfit.Redraw());                       // on Fuel
        outfit.Key(MenuKey.Select);
        Assert.Equal(new[] { opened, gauges }, outfit.Redraw());                       // in the fuel gauge
        outfit.Key(MenuKey.Select);
        outfit.Key(MenuKey.Down);
        outfit.Key(MenuKey.Down);
        Assert.Equal(new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmFuel, OutfitMenu.SaveLoad), outfit.Redraw().Last());
        outfit.Key(MenuKey.Up);
        outfit.Key(MenuKey.Select);                                                  // Modules: the first module slot
        var radar = outfit.Redraw();
        Assert.Equal(new[] { ScreenOpKind.OutfitOpened, ScreenOpKind.SisDisplay, ScreenOpKind.ClearRadar, ScreenOpKind.ModuleStrings },
            radar.Select(o => o.Kind));
        Assert.Equal(rules.Piece(SlotKind.Module, outfit.Item), radar[3].A);
        outfit.Key(MenuKey.Down);                                                    // the landers' row
        while (rules.Piece(SlotKind.Lander, outfit.Item) != ShipModules.EmptyLander)
            outfit.Key(MenuKey.Right);
        outfit.Key(MenuKey.Select);                                                  // choosing what to buy
        Assert.True(outfit.Choosing);
        Assert.Equal(new ScreenOp(ScreenOpKind.ModuleStrings, outfit.State), outfit.Redraw().Last());
    }

    [ContentFact]
    public void WhatOthersChangeWhileOneIsDockedIsDrawnOnTheirScreen()
    {
        var world = Allied(guest: true);
        var host = Docked(world);
        var guest = world.Find("guest")!;
        guest.Ship.Hold.ResourceUnits = 1000;
        var minerals = new int[8];
        minerals[3] = 20;
        guest.Ship.Hold.LanderLeaves();
        guest.Ship.Hold.LanderReturns(guest.Ship.Hold.LanderOut!.Value.Crew, minerals, 0);
        var none = new int[8];
        // On the menu nothing shows the ship: a gift draws nothing.
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("guest", "host", 10, none, 0)));
        Assert.DoesNotContain(Drawn(world).SelectMany(d => d.Ops), o => o.Kind == ScreenOpKind.Gauges);
        // Outfitting: units given draw the units' message, with the ship that has them.
        Keys(world, "host", MenuKey.Down, MenuKey.Select);
        Drawn(world);
        uint ru = host.Ship.Hold.ResourceUnits;
        world.Give(new Gift("guest", "host", 25, none, 0));
        var d = Drawn(world).Single();
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.Gauges, 0, 0, ScreenOp.Undefined) }, d.Ops);
        Assert.Equal(ru + 25, d.Ship!.ResourceUnits);
        Assert.Empty(Drawn(world));   // drawn once
        // Given before a key in the same frame: drawn first, the key's drawing over it.
        world.Give(new Gift("guest", "host", 5, none, 0));
        Keys(world, "host", MenuKey.Down);
        Assert.Equal(new[] { ScreenOpKind.Gauges, ScreenOpKind.MenuStrings }, Drawn(world).Select(x => x.Ops[0].Kind));
        // Minerals: the status area, which shows the storage bays.
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("guest", "host", 0, new[] { 0, 0, 0, 5, 0, 0, 0, 0 }, 0)));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.SisDisplay) }, Drawn(world).Single().Ops);
        // In Modules, choosing: a module's price set by someone's talk redraws it.
        Keys(world, "host", MenuKey.Select, MenuKey.Select);   // Modules, on the first module slot (empty): choosing
        var outfit = host.Ship.Visit!.Menu!.Outfit!;
        Assert.True(outfit.Choosing);
        Drawn(world);
        var name = ShipModules.Names[outfit.State];
        world.Story!.SetModuleCost(name, world.Story.ModuleCost(name) + 1);
        d = Drawn(world).Single();
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ModuleStrings, outfit.State) }, d.Ops);
        Assert.Equal(world.Story.ModuleCost(name), d.Ship!.ModuleCosts[outfit.State]);
        // The shipyard: the crew price moved by another's purchases redraws its menu; minerals redraw the
        // shipyard, whose blueprint shows them too.
        Keys(world, "host", MenuKey.Cancel, MenuKey.Cancel, MenuKey.Cancel, MenuKey.Cancel, MenuKey.Down, MenuKey.Select);
        Assert.Equal(StarbaseScreen.Shipyard, world.StarbaseFor("host")!.Screen);
        Keys(world, "host", MenuKey.Down);
        Drawn(world);
        world.Story.SetCrewCost(world.Story.CrewCost + 2);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.SaveLoad) }, Drawn(world).Single().Ops);
        world.Give(new Gift("guest", "host", 0, new[] { 0, 0, 0, 5, 0, 0, 0, 0 }, 0));
        Assert.Equal(host.Ship.Visit!.Menu!.Yard!.Redraw(), Drawn(world).Single().Ops);
    }

    [ContentFact]
    public void OthersChangesAreDrawnOnceWhereverTheDockedPlayerIs()
    {
        var world = Allied(guest: true);
        var host = Docked(world);
        var guest = world.Find("guest")!;
        guest.Ship.Hold.ResourceUnits = 1000;
        var cargo = new int[8];
        cargo[3] = 20;
        guest.Ship.Hold.LanderLeaves();
        guest.Ship.Hold.LanderReturns(guest.Ship.Hold.LanderOut!.Value.Crew, cargo, 0);
        var none = new int[8];
        var some = new[] { 0, 0, 0, 2, 0, 0, 0, 0 };
        // Talking to the commander: nothing drawn here, and outfitting opened after draws it once, afresh.
        Drawn(world);
        Keys(world, "host", MenuKey.Select);
        world.Give(new Gift("guest", "host", 10, none, 0));
        Assert.Empty(Drawn(world));
        world.ChooseReply("host", Reply(world, "host", "goodbye_commander"), host.Ship.Visit!.Round);
        Keys(world, "host", MenuKey.Down, MenuKey.Select);
        Assert.Equal(new[] { ScreenOpKind.MenuOpened, ScreenOpKind.MenuMoved, ScreenOpKind.OutfitOpened },
            Drawn(world).Select(d => d.Ops[0].Kind));
        // Units and minerals in one gift: the status area covers both.
        world.Give(new Gift("guest", "host", 10, some, 0));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.SisDisplay) }, Drawn(world).Single().Ops);
        // Given, then renamed: the gift drawn first.
        world.Give(new Gift("guest", "host", 10, none, 0));
        world.Rename("host", "Rocinante", null);
        Assert.Equal(new[] { ScreenOpKind.Gauges, ScreenOpKind.FlagshipName }, world.TakeStarbaseDrawing("host").Select(d => d.Ops[0].Kind));
        // The client lost its picture: one drawing afresh, the gift in it.
        world.RedrawStarbase("host");
        world.Give(new Gift("guest", "host", 10, none, 0));
        var fresh = Drawn(world).Single();
        Assert.Equal(ScreenOpKind.OutfitOpened, fresh.Ops[0].Kind);
        Assert.Equal(host.Ship.Hold.ResourceUnits, fresh.Ship!.ResourceUnits);
        Assert.Empty(Drawn(world));
        // In Modules on a slot's piece (the first module slot past the empty one): its price, set elsewhere, redrawn.
        Keys(world, "host", MenuKey.Down, MenuKey.Select);   // Modules
        Assert.True(host.Ship.Visit!.Menu!.Outfit!.Installing);
        for (int i = 0; i < ShipModules.ModuleSlotCount && host.Ship.Visit.Menu.Outfit.Shown >= ShipModules.EmptySlot; ++i)
            Keys(world, "host", MenuKey.Right);
        Assert.True(host.Ship.Visit.Menu.Outfit.Shown < ShipModules.EmptySlot);
        byte piece = host.Ship.Visit.Menu.Outfit.Shown;
        Drawn(world);
        world.Story!.SetModuleCost(ShipModules.Names[piece], world.Story.ModuleCost(ShipModules.Names[piece]) + 1);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.ModuleStrings, piece) }, Drawn(world).Single().Ops);
        // A lander sold: the radar shows the empty mark, whatever the slot now counts, and a lander's price
        // changing then draws nothing over it.
        Keys(world, "host", MenuKey.Down);
        Assert.Equal(SlotKind.Lander, host.Ship.Visit.Menu.Outfit.Row);
        host.Ship.Hold.SetLanders(3);
        Keys(world, "host", MenuKey.Select);
        Assert.Equal(ShipModules.EmptyLander, host.Ship.Visit.Menu.Outfit.Shown);
        Drawn(world);
        world.Story.SetModuleCost("PLANET_LANDER", world.Story.ModuleCost("PLANET_LANDER") + 1);
        Assert.Empty(Drawn(world));
        // The bomb's state, moved by another's talk: the screen drawn afresh.
        world.Story.Set("CHMMR_BOMB_STATE", 3);
        Assert.Equal(host.Ship.Visit.Menu.Redraw(), Drawn(world).Single().Ops);
        world.Story.Set("CHMMR_BOMB_STATE", 0);
        Drawn(world);
        // The shipyard: units given draw the units' message; another player docked there hiring past the
        // threshold moves the shared crew price, which the host's menu shows.
        Keys(world, "host", MenuKey.Cancel, MenuKey.Cancel, MenuKey.Cancel, MenuKey.Down, MenuKey.Select);
        Assert.Equal(StarbaseScreen.Shipyard, world.StarbaseFor("host")!.Screen);
        Drawn(world);
        world.Give(new Gift("guest", "host", 10, none, 0));
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.Gauges, 0, 0, ScreenOp.Undefined) }, Drawn(world).Single().Ops);
        int threshold = world.Story.Rules.CrewExpenseThreshold - 1;
        world.Story.Set("CREW_PURCHASED0", threshold & 0xFF);
        world.Story.Set("CREW_PURCHASED1", threshold >> 8);
        int price = world.Story.CrewCost;
        guest.Ship.Hold.SetCrew(1);
        Docked(world, "guest");
        Keys(world, "guest", MenuKey.Down, MenuKey.Down, MenuKey.Select, MenuKey.Select, MenuKey.Select, MenuKey.Up);
        Assert.NotEqual(price, world.Story.CrewCost);
        Assert.Equal(new[] { new ScreenOp(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) }, Drawn(world).Single().Ops);
    }
}
