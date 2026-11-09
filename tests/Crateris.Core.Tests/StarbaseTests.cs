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
}
