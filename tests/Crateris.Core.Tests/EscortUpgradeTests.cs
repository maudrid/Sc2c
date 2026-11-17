// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using static Crateris.Core.Tests.OutfitTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The escorts' upgrades (docs/plans/escort-upgrades.md), without the
/// content: escorts buy crew pods, fuel tanks, fusion thrusters and turning
/// jets, each up to a host setting (<see cref="EscortCaps"/>: 8, 6, 4 and 2,
/// the kit's counted), at the flagship's prices and by its rules (selling a
/// pod the crew needs or a tank the fuel needs refused, the kit's sold back
/// too); what they hold follows the flagship's code (the most crew, the
/// fuel's room, the drive in flight and in battle); the caps are saved with
/// the settings, a save without them has the defaults, and caps out of range
/// are refused; the screens draw an escort's crew in its pods.
/// </summary>
[Trait("Topic", "starbase")]
public class EscortUpgradeTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ShipDescription Sis = ShipDescriptions.Load(DataDir)[ShipDescriptions.Flagship];

    private static TestShip Escort(uint ru = 100000) => new()
    {
        Kind = ShipKind.Escort,
        Modules = ShipModules.FromLoadout(ShipKind.Escort, new GameSettings().EscortLoadout),
        Crew = 10,
        Fuel = 1000,
        Landers = 1,
        ResourceUnits = ru,
    };

    private static StoryState NewGame() => StoryState.NewGame(Rules, new GameClock(2155, 3, 4));

    private static int Free(IReadOnlyList<byte> slots, byte empty) => slots.ToList().IndexOf(empty);

    // Buys a module into the first free slot of its row: whether it was bought.
    private static bool Buy(Outfitting rules, byte piece)
    {
        var m = rules.Ship.Modules;
        var (kind, slot) = piece switch
        {
            ShipModules.FusionThruster => (SlotKind.Drive, Free(m.DriveSlots, ShipModules.EmptyDrive)),
            ShipModules.TurningJets => (SlotKind.Jet, Free(m.JetSlots, ShipModules.EmptyJet)),
            // Not a weapon slot (0, 13 to 15), so the weapons stay free.
            _ => (SlotKind.Module, Enumerable.Range(1, 12).First(i => m.ModuleSlots[i] == ShipModules.EmptyModule)),
        };
        bool may = rules.CanHold(kind, slot, piece);
        bool bought = rules.Apply(new Install(kind, slot, piece));
        Assert.Equal(may, bought);
        return bought;
    }

    [Fact]
    public void TheCapsArePlaceholderSettings()
    {
        var caps = new GameSettings().EscortCaps;
        Assert.Equal(new EscortCaps(Thrusters: 8, Jets: 6, FuelTanks: 4, CrewPods: 2), caps);
        // Each below the flagship's slots for it.
        Assert.True(caps.Thrusters < ShipModules.DriveSlotCount && caps.Jets < ShipModules.JetSlotCount);
        Assert.Equal((8, 6, 4, 2, int.MaxValue, int.MaxValue), (caps.For(ShipModules.FusionThruster), caps.For(ShipModules.TurningJets),
            caps.For(ShipModules.FuelTank), caps.For(ShipModules.CrewPod), caps.For(ShipModules.StorageBay),
            caps.For(ShipModules.HighEfficiencyFuelSystem)));
    }

    [Fact]
    public void AnEscortBuysEachUpToItsCapTheKitsCounted()
    {
        var story = NewGame();
        var ship = Escort();
        var rules = new Outfitting(ship, story, escortCaps: new GameSettings().EscortCaps);
        var flagship = new Outfitting(new TestShip
        {
            Modules = ShipModules.FromLoadout(ShipKind.Flagship, NewGameData.Load(DataDir).Flagship),
            Crew = 10, Fuel = 1000, Landers = 1, ResourceUnits = 100000,
        }, story);
        // The kit: 2 thrusters, 2 jets, 1 fuel tank, no crew pod. Up to 8, 6, 4 and 2: 6, 4, 3 and 2 more.
        foreach (var (piece, more, cap) in new[] { (ShipModules.FusionThruster, 6, 8), (ShipModules.TurningJets, 4, 6),
                     (ShipModules.FuelTank, 3, 4), (ShipModules.CrewPod, 2, 2) })
        {
            int price = story.ModuleCost(ShipModules.Names[piece]) * Rules.ModuleCostScale;
            Assert.Equal((price, price), (rules.Price(piece), flagship.Price(piece)));   // the flagship's prices
            for (int i = 0; i < more; ++i)
            {
                Assert.False(rules.AtEscortCap(piece));
                uint ru = ship.ResourceUnits;
                Assert.True(Buy(rules, piece), $"{ShipModules.Names[piece]} {i}");
                Assert.Equal(ru - (uint)price, ship.ResourceUnits);
            }
            Assert.Equal(cap, rules.Held(piece));
            Assert.True(rules.AtEscortCap(piece));
            // Past it: refused, nothing changed.
            uint before = ship.ResourceUnits;
            int version = ship.Modules.Version;
            Assert.False(Buy(rules, piece));
            Assert.Equal((before, version), (ship.ResourceUnits, ship.Modules.Version));
            // The flagship has no cap but its slots.
            Assert.False(flagship.AtEscortCap(piece));
        }
        // 500 a thruster, jets or tank, 2,000 a crew pod.
        Assert.Equal(100000u - 6 * 500 - 4 * 500 - 3 * 500 - 2 * 2000, ship.ResourceUnits);
        // What it holds: its kit's 10 crew and 50 a pod; the reserve and 50 units a tank.
        Assert.Equal(110, ship.Modules.CrewCapacity);
        Assert.Equal(Fuel.Reserve + 4 * Fuel.TankSize, ship.Modules.FuelCapacity);
        // The flagship buys past them, to its slots.
        for (int i = 0; i < 9; ++i)
            Assert.True(Buy(flagship, ShipModules.FusionThruster));
        Assert.Equal(ShipModules.DriveSlotCount, flagship.Held(ShipModules.FusionThruster));
    }

    [Fact]
    public void TheHostSetsTheCaps()
    {
        var ship = Escort();
        var rules = new Outfitting(ship, NewGame(), escortCaps: new EscortCaps(Thrusters: 3, Jets: 1, FuelTanks: 1, CrewPods: 0));
        Assert.True(Buy(rules, ShipModules.FusionThruster));
        Assert.False(Buy(rules, ShipModules.FusionThruster));
        // Below the kit's: it keeps them and may sell, but buys none until under the cap.
        Assert.False(Buy(rules, ShipModules.TurningJets));
        Assert.False(Buy(rules, ShipModules.FuelTank));
        Assert.False(Buy(rules, ShipModules.CrewPod));
        Assert.True(rules.Apply(new Remove(SlotKind.Jet, Free(ship.Modules.JetSlots, ShipModules.TurningJets))));
        Assert.True(rules.Apply(new Remove(SlotKind.Jet, Free(ship.Modules.JetSlots, ShipModules.TurningJets))));
        Assert.True(Buy(rules, ShipModules.TurningJets));
        Assert.False(Buy(rules, ShipModules.TurningJets));
        // Without caps given, the defaults.
        Assert.Equal(new EscortCaps(), new Outfitting(ship, NewGame()).EscortCaps);
    }

    [Fact]
    public void AnEscortSellsAsTheFlagshipSells()
    {
        var story = NewGame();
        var ship = Escort();
        var rules = new Outfitting(ship, story);
        // The kit's thrusters, jets and fuel tank sell back at their price, as UQM's flagship sells its own.
        uint ru = ship.ResourceUnits;
        Assert.True(rules.Apply(new Remove(SlotKind.Drive, Free(ship.Modules.DriveSlots, ShipModules.FusionThruster))));
        Assert.True(rules.Apply(new Remove(SlotKind.Jet, Free(ship.Modules.JetSlots, ShipModules.TurningJets))));
        Assert.Equal(ru + 1000, ship.ResourceUnits);

        // A crew pod the crew needs is refused (CREW_POD_CAPACITY by the pods left, and the kit's 10).
        Assert.True(Buy(rules, ShipModules.CrewPod));
        int pod = Free(ship.Modules.ModuleSlots, ShipModules.CrewPod);
        ship.Crew = 11;
        Assert.False(rules.CanRemove(SlotKind.Module, pod));
        Assert.False(rules.Apply(new Remove(SlotKind.Module, pod)));
        ship.Crew = 10;
        ru = ship.ResourceUnits;
        Assert.True(rules.Apply(new Remove(SlotKind.Module, pod)));
        Assert.Equal(ru + 2000, ship.ResourceUnits);

        // A fuel tank the fuel needs (past the tanks left and the reserve) is refused, as UQM refuses it.
        Assert.True(Buy(rules, ShipModules.FuelTank));
        ship.Fuel = Fuel.Reserve + Fuel.TankSize + 1;
        int kitTank = Free(ship.Modules.ModuleSlots, ShipModules.FuelTank);
        Assert.False(rules.Apply(new Remove(SlotKind.Module, kitTank)));
        ship.Fuel = Fuel.Reserve + Fuel.TankSize;
        Assert.True(rules.Apply(new Remove(SlotKind.Module, kitTank)));
        Assert.False(rules.Apply(new Remove(SlotKind.Module, Free(ship.Modules.ModuleSlots, ShipModules.FuelTank))));
        ship.Fuel = Fuel.Reserve;
        Assert.True(rules.Apply(new Remove(SlotKind.Module, Free(ship.Modules.ModuleSlots, ShipModules.FuelTank))));
        Assert.Equal(0, ship.Modules.Count(ShipModules.FuelTank));
    }

    [Fact]
    public void CrewAndFuelAreBoughtIntoTheRoomTheyMake()
    {
        var story = NewGame();
        var ship = Escort();
        var rules = new Outfitting(ship, story);
        // The kit's 10 crew full: hiring refused until a pod is bought, then up to 60 at the group's price.
        Assert.False(rules.Apply(new HireCrew()));
        Assert.True(Buy(rules, ShipModules.CrewPod));
        int bought = story.CrewPurchased;
        for (int i = 0; i < 50; ++i)
            Assert.True(rules.Apply(new HireCrew()));
        Assert.False(rules.Apply(new HireCrew()));
        Assert.Equal((60, bought + 50), (ship.Crew, story.CrewPurchased));
        // Fuel: the kit's tank holds 60 units with the reserve; a second, 110.
        ship.Fuel = 0;
        while (rules.Apply(new ChangeFuel(Outfitting.FuelBar)))
        {
        }
        Assert.Equal(Fuel.Reserve + Fuel.TankSize, ship.Fuel);
        Assert.True(Buy(rules, ShipModules.FuelTank));
        while (rules.Apply(new ChangeFuel(Outfitting.FuelBar)))
        {
        }
        Assert.Equal(Fuel.Reserve + 2 * Fuel.TankSize, ship.Fuel);
    }

    [Fact]
    public void InBattleAnEscortsUpgradesAreTheFlagshipsCode()
    {
        // Eight thrusters, six jets and two crew pods: the drive as a flagship's with the same slots, the most crew
        // its pods' 100 and its kit's 10, and the captain.
        var ship = Escort();
        var rules = new Outfitting(ship, NewGame());
        foreach (var (piece, n) in new[] { (ShipModules.FusionThruster, 6), (ShipModules.TurningJets, 4), (ShipModules.CrewPod, 2) })
            for (int i = 0; i < n; ++i)
                Assert.True(Buy(rules, piece));
        ship.Crew = 80;
        var (rd, _, _) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Escort, ship.Modules, ship.Crew));
        var flagshipSlots = new ShipModules(ship.Modules.DriveSlots, ship.Modules.JetSlots, ship.Modules.ModuleSlots);
        var (flag, _, _) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Flagship, flagshipSlots, ship.Crew));
        Assert.Equal((81, 111), (rd.CrewLevel, rd.MaxCrew));
        Assert.Equal((81, 101), (flag.CrewLevel, flag.MaxCrew));
        var drive = ShipDrive.ForBattle(8, 6);
        Assert.Equal((drive.MaxThrust, drive.ThrustIncrement, drive.ThrustWait),
            (rd.Characteristics.MaxThrust, rd.Characteristics.ThrustIncrement, rd.Characteristics.ThrustWait));
        Assert.Equal((flag.Characteristics.MaxThrust, flag.Characteristics.ThrustIncrement, flag.Characteristics.ThrustWait,
                flag.Characteristics.TurnWait),
            (rd.Characteristics.MaxThrust, rd.Characteristics.ThrustIncrement, rd.Characteristics.ThrustWait, rd.Characteristics.TurnWait));
        // Faster than the kit's two and two.
        var (kit, _, _) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Escort, Escort().Modules, 10));
        Assert.True(rd.Characteristics.MaxThrust > kit.Characteristics.MaxThrust);
        Assert.True(rd.Characteristics.TurnWait < kit.Characteristics.TurnWait);
    }

    [Fact]
    public void AGuestsUpgradesAreFlownSavedAndSent()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        var guest = world.Find("guest")!.Ship;
        guest.Modules.SetDrive(0, ShipModules.FusionThruster);
        guest.Modules.SetJet(1, ShipModules.TurningJets);
        guest.Modules.SetModule(2, ShipModules.CrewPod);
        guest.Modules.SetModule(3, ShipModules.FuelTank);
        guest.Refit();
        // In flight: the flagship's drive for its thrusters and jets, its tanks' room, its pod's crew.
        Assert.Equal(ShipDrive.ForHyperspace(3, 3), guest.Flight.Drive);
        Assert.Equal(Fuel.Reserve + 2 * Fuel.TankSize, guest.Flight.FuelCapacity);
        Assert.Equal(60, guest.Hold.CrewCapacity);
        // Sent with the ship (its slots, as since protocol version 11) and saved (since save version 9): no new version.
        Assert.Equal(guest.Modules.Save(), world.Snapshot().Ships.Single(s => s.Id == "guest").Modules);
        var loaded = HostSave.FromJson(HostSave.ToJson(world));
        Assert.Equal(guest.Modules.Save(), loaded.Find("guest")!.Ship.Modules.Save());
        Assert.Equal(60, loaded.Find("guest")!.Ship.Hold.CrewCapacity);
        Assert.Equal(13, HostSave.Version);   // 12 then; 13 is the clock's events' (stage 3b item 1)
    }

    [Fact]
    public void TheCapsAreSavedAndASaveWithoutThemHasTheDefaults()
    {
        var caps = new EscortCaps(Thrusters: 10, Jets: 0, FuelTanks: 15, CrewPods: 5);
        var world = GameWorld.NewGame(new GameSettings { EscortCaps = caps }, NewGameData.Load(DataDir), "host", "Host");
        var json = HostSave.ToJson(world);
        Assert.Equal(caps, HostSave.FromJson(json).Settings.EscortCaps);
        // A save from before them (version 12 and older), or with them null, or with some left out: the defaults.
        var node = JsonNode.Parse(json)!;
        node["settings"]!.AsObject().Remove("escort_caps");
        Assert.Equal(new EscortCaps(), HostSave.FromJson(node.ToJsonString()).Settings.EscortCaps);
        node["settings"]!["escort_caps"] = null;
        Assert.Equal(new EscortCaps(), HostSave.FromJson(node.ToJsonString()).Settings.EscortCaps);
        node["settings"]!["escort_caps"] = new JsonObject { ["crew_pods"] = 1 };
        Assert.Equal(new EscortCaps(CrewPods: 1), HostSave.FromJson(node.ToJsonString()).Settings.EscortCaps);
        var old = OutfittingTests.Older(json, 10);
        old["settings"]!.AsObject().Remove("escort_caps");
        Assert.Equal(new EscortCaps(), HostSave.FromJson(old.ToJsonString()).Settings.EscortCaps);
    }

    [Theory]
    [InlineData("thrusters", -1)]
    [InlineData("thrusters", 11)]   // the flagship's 11 drive slots: no cap below it
    [InlineData("jets", 8)]
    [InlineData("fuel_tanks", 16)]
    [InlineData("crew_pods", -2)]
    [InlineData("crew_pods", 16)]
    public void CapsOutOfRangeAreRefused(string cap, int value)
    {
        var caps = cap switch
        {
            "thrusters" => new EscortCaps(Thrusters: value),
            "jets" => new EscortCaps(Jets: value),
            "fuel_tanks" => new EscortCaps(FuelTanks: value),
            _ => new EscortCaps(CrewPods: value),
        };
        Assert.Throws<ArgumentOutOfRangeException>(caps.Check);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameWorld.NewGame(new GameSettings { EscortCaps = caps },
            NewGameData.Load(DataDir), "host", "Host"));
        var node = JsonNode.Parse(HostSave.ToJson(GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host")))!;
        node["settings"]!["escort_caps"]![cap] = value;
        Assert.Contains("escort caps", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString())).Message);
    }

    [Fact]
    public void TheHighestCapsAreOneBelowTheFlagshipsSlots()
    {
        var most = new EscortCaps(Thrusters: 10, Jets: 7, FuelTanks: 15, CrewPods: 15);
        most.Check();
        new EscortCaps(0, 0, 0, 0).Check();
        _ = GameWorld.NewGame(new GameSettings { EscortCaps = most }, NewGameData.Load(DataDir), "host", "Host");
        Assert.Contains("isn't from 0 to 10 (below the flagship's 11)",
            Assert.Throws<ArgumentOutOfRangeException>(() => (most with { Thrusters = 11 }).Check()).Message);
    }

    [Fact]
    public void TheMenuOffersTheUpgradesAndRefusesThemPastTheCaps()
    {
        // A thruster at its cap: the drive row's choice opens on it, as the flagship's, and buying it is refused,
        // as one too dear is.
        var ship = Escort();
        var rules = new Outfitting(ship, NewGame(), escortCaps: new EscortCaps(Thrusters: 2));
        var menu = new OutfitMenu(rules, rules.Apply);
        menu.Open();
        menu.Key(MenuKey.Down);                       // Modules
        menu.Key(MenuKey.Select);
        menu.Key(MenuKey.Up);                         // the jets
        menu.Key(MenuKey.Up);                         // the drives, slot 0
        Assert.Equal(ShipModules.EmptyDrive, menu.State);
        Assert.Equal(OutfitOutcome.None, menu.Key(MenuKey.Select));
        Assert.Equal((true, ShipModules.FusionThruster), (menu.Choosing, menu.State));
        menu.TakeOps();
        Assert.Equal(OutfitOutcome.Refused, menu.Key(MenuKey.Select));
        Assert.Empty(menu.TakeOps());
        Assert.Equal((true, 2, 100000u), (menu.Choosing, ship.Modules.Thrusters, ship.ResourceUnits));
        // Sold one, it buys one again.
        menu.Key(MenuKey.Cancel);
        for (int i = 0; i < 5; ++i)
            menu.Key(MenuKey.Right);                  // slot 5: the kit's
        Assert.Equal(ShipModules.FusionThruster, ship.Modules.DriveSlots[5]);
        Assert.Equal(OutfitOutcome.Acted, menu.Key(MenuKey.Select));
        menu.Key(MenuKey.Left);
        Assert.Equal(OutfitOutcome.None, menu.Key(MenuKey.Select));
        Assert.Equal(OutfitOutcome.Acted, menu.Key(MenuKey.Select));
        Assert.Equal((2, ShipModules.FusionThruster), (ship.Modules.Thrusters, ship.Modules.DriveSlots[4]));
    }

    // The points a drawing draws.
    private static int Points(List<UqmDraw> calls) => calls.Count(c => c.Kind == UqmDrawKind.Point);

    private static StarbaseShip Drawn(byte[] modules, int crew) => new(modules,
        Enumerable.Repeat(ShipModules.EmptyDrive, ShipModules.DriveSlotCount).ToArray(),
        Enumerable.Repeat(ShipModules.EmptyJet, ShipModules.JetSlotCount).ToArray(), crew, 1000, 1, 100, 0, "Ship", "Captain",
        0, 0, 20, 3, Enumerable.Range(1, ShipModules.Modules).ToArray(), 50);

    [Fact]
    public void TheScreensDrawAnEscortsCrewInItsPods()
    {
        // The crew fill the pods first; an escort's past them are its kit's, which have no seats on the picture.
        var noPod = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        var pod = noPod.ToArray();
        pod[2] = ShipModules.CrewPod;
        int Drawing(StarbaseShip ship, params ScreenOp[] ops)
        {
            var picture = new StarbasePicture(StarbaseScreensTests.Canvas(), StarbaseScreensTests.Placeholder, 1, 1,
                s => SisState.Of(s, "", ""));
            picture.Draw(new[] { new StarbaseDrawing(ship, new[] { new ScreenOp(ScreenOpKind.YardOpened) }) }, 0);
            return Points(picture.Draw(new[] { new StarbaseDrawing(ship, ops) }, 0));
        }
        var blueprint = new ScreenOp(ScreenOpKind.Blueprint);
        // The shipyard's blueprint: a point a crew member in a pod (and those past it where the last was, as UQM's).
        Assert.Equal(Drawing(Drawn(pod, 0), blueprint) + 50, Drawing(Drawn(pod, 50), blueprint));
        Assert.Equal(Drawing(Drawn(pod, 0), blueprint) + 60, Drawing(Drawn(pod, 60), blueprint));
        // No pod: none for the kit's crew.
        Assert.Equal(Drawing(Drawn(noPod, 0), blueprint), Drawing(Drawn(noPod, 10), blueprint));
        // A crew member hired or sent away: its point in a pod, none past the pods.
        Assert.Equal(1, Drawing(Drawn(pod, 50), new ScreenOp(ScreenOpKind.CrewPoint, 1, 49)));
        Assert.Equal(1, Drawing(Drawn(pod, 49), new ScreenOp(ScreenOpKind.CrewPoint, 0, 49)));
        Assert.Equal(0, Drawing(Drawn(pod, 51), new ScreenOp(ScreenOpKind.CrewPoint, 1, 50)));
        Assert.Equal(0, Drawing(Drawn(noPod, 5), new ScreenOp(ScreenOpKind.CrewPoint, 0, 5)));
    }
}
