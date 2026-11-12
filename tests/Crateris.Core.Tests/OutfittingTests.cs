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
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.OutfitTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The escorts' combat modules (stage 4a item 8, decision 3), without the
/// content: the escort's kit's gun in the nose (a setting); outfitting
/// selling escorts the combat modules (guns, blasters and cannons in the
/// weapon slots, tracking, point defence, dynamos and furnaces) at the
/// flagship's prices, and refusing them the flagship's unique ones (crew
/// pods, fuel tanks, thrusters, jets, the bomb's parts); what they bought
/// flown in battle; and escorts saved before they had guns (save versions
/// up to 10) given the kit's gun on load, in the first free weapon slot UQM
/// fires from (15, 14, 13, then 0), or none if all are full.
/// </summary>
[Trait("Topic", "starbase")]
public class OutfittingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ShipDescription Sis = ShipDescriptions.Load(DataDir)[ShipDescriptions.Flagship];

    // Every module for sale (the Melnorme's too), at UQM's new game's prices and the Melnorme's.
    private static StoryState AllForSale()
    {
        var story = StoryState.NewGame(Rules, new GameClock(2155, 3, 4));
        foreach (var (module, cost) in new[] { ("BLASTER_WEAPON", 70), ("CANNON_WEAPON", 120), ("TRACKING_SYSTEM", 100),
                     ("ANTIMISSILE_DEFENSE", 70), ("SHIVA_FURNACE", 100), ("HIGHEFF_FUELSYS", 20) })
            story.SetModuleCost(module, cost);
        return story;
    }

    private static TestShip Ship(ShipKind kind, uint ru = 100000) => new()
    {
        Kind = kind,
        Modules = ShipModules.FromLoadout(kind, kind == ShipKind.Escort ? new GameSettings().EscortLoadout
            : NewGameData.Load(DataDir).Flagship),
        Crew = 10,
        Fuel = 1000,
        Landers = 1,
        ResourceUnits = ru,
    };

    private static readonly byte[] CombatModules =
    {
        ShipModules.DynamoUnit, ShipModules.ShivaFurnace, ShipModules.GunWeapon, ShipModules.BlasterWeapon,
        ShipModules.CannonWeapon, ShipModules.TrackingSystem, ShipModules.AntimissileDefense,
    };

    private static int[] Slots(ShipModules m, byte piece) =>
        Enumerable.Range(0, ShipModules.ModuleSlotCount).Where(i => m.ModuleSlots[i] == piece).ToArray();

    [Fact]
    public void TheEscortsKitHasAGunInTheNose()
    {
        // The default kit: its gun in slot 15, the nose, as a new flagship's; the rest as before.
        var kit = new GameSettings().EscortLoadout;
        Assert.Equal(1, kit.Guns);
        var m = ShipModules.FromLoadout(ShipKind.Escort, kit);
        Assert.Equal(new[] { 15 }, Slots(m, ShipModules.GunWeapon));
        Assert.Equal(new[] { 1 }, Slots(m, ShipModules.FuelTank));
        Assert.Equal(new[] { 8 }, Slots(m, ShipModules.StorageBay));
        Assert.Equal(10, m.CrewCapacity);
        Assert.True(m.HasWeapon);
        Assert.False(ShipModules.FromLoadout(ShipKind.Escort, kit with { Guns = 0 }).HasWeapon);

        // More guns go in the weapon slots in the order UQM fires from them: 15, 14, 13, then 0.
        Assert.Equal(new[] { 14, 15 }, Slots(ShipModules.FromLoadout(ShipKind.Escort, kit with { Guns = 2 }), ShipModules.GunWeapon));
        Assert.Equal(new[] { 13, 14, 15 }, Slots(ShipModules.FromLoadout(ShipKind.Escort, kit with { Guns = 3 }), ShipModules.GunWeapon));
        var four = ShipModules.FromLoadout(ShipKind.Escort, kit with { Guns = 4, StorageBays = 11 });
        Assert.Equal(new[] { 0, 13, 14, 15 }, Slots(four, ShipModules.GunWeapon));
        Assert.Equal(11, four.Count(ShipModules.StorageBay));   // the other modules round them

        // A new guest's escort has it, and so does its snapshot.
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        var guest = world.Find("guest")!.Ship;
        Assert.Equal(ShipModules.GunWeapon, guest.Modules.ModuleSlots[15]);
        Assert.Equal(guest.Modules.Save(), world.Snapshot().Ships.Single(s => s.Id == "guest").Modules);
        // A kit without guns: the host's choice.
        var unarmed = GameWorld.NewGame(new GameSettings { EscortLoadout = kit with { Guns = 0 } },
            NewGameData.Load(DataDir), "host", "Host");
        unarmed.Join("guest", "Guest");
        Assert.False(unarmed.Find("guest")!.Ship.Modules.HasWeapon);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(5, 1)]
    [InlineData(4, 12)]   // 4 guns, a fuel tank and 12 storage bays: 17 modules for 16 slots
    public void AKitWhoseGunsDontFitIsRefused(int guns, int bays)
    {
        var kit = new GameSettings().EscortLoadout with { Guns = guns, StorageBays = bays };
        Assert.ThrowsAny<ArgumentException>(() => ShipModules.FromLoadout(ShipKind.Escort, kit));
        // A hold made from it checks its guns too: 0 to 4.
        if (guns is < 0 or > 4)
            Assert.Throws<ArgumentOutOfRangeException>(() => ShipHold.Check(kit));
        else
            ShipHold.Check(kit);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameWorld.NewGame(new GameSettings { EscortLoadout = kit },
            NewGameData.Load(DataDir), "host", "Host"));
        // And from a save.
        var node = JsonNode.Parse(HostSave.ToJson(GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host")))!;
        node["settings"]!["escort_loadout"]!["guns"] = guns;
        node["settings"]!["escort_loadout"]!["storage_bays"] = bays;
        Assert.Contains("escort loadout", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString())).Message);
    }

    [Fact]
    public void AFlagshipsLoadoutHasNoGunsOfItsOwn()
    {
        // Its one gun is UQM's own, in slot 15; a loadout giving it more is refused.
        var flagship = NewGameData.Load(DataDir).Flagship;
        Assert.Equal(0, flagship.Guns);
        Assert.Equal(new[] { 15 }, Slots(ShipModules.FromLoadout(ShipKind.Flagship, flagship), ShipModules.GunWeapon));
        Assert.Throws<ArgumentException>(() => ShipModules.FromLoadout(ShipKind.Flagship, flagship with { Guns = 1 }));
        Assert.Throws<ArgumentException>(() => new PlayerShip(ShipKind.Flagship, flagship with { Guns = 1 },
            ShipModules.FromLoadout(ShipKind.Flagship, flagship)));
        var node = JsonNode.Parse(HostSave.ToJson(GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host")))!;
        node["players"]![0]!["loadout"]!["guns"] = 1;
        Assert.Contains("UQM's own", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString())).Message);
    }

    [Fact]
    public void AnEscortBuysTheCombatModulesAtTheFlagshipsPrices()
    {
        var story = AllForSale();
        var ship = Ship(ShipKind.Escort);
        var rules = new Outfitting(ship, story);
        var flagship = new Outfitting(Ship(ShipKind.Flagship), story);
        // Dynamos, furnaces, tracking and point defence anywhere free; guns, blasters and cannons in the free weapon slots.
        var buys = new (int Slot, byte Piece)[]
        {
            (2, ShipModules.DynamoUnit), (3, ShipModules.ShivaFurnace), (4, ShipModules.TrackingSystem),
            (5, ShipModules.AntimissileDefense), (14, ShipModules.GunWeapon), (13, ShipModules.BlasterWeapon),
            (0, ShipModules.CannonWeapon),
        };
        foreach (var (slot, piece) in buys)
        {
            uint ru = ship.ResourceUnits;
            int price = story.ModuleCost(ShipModules.Names[piece]) * Rules.ModuleCostScale;
            Assert.True(price > 0);
            Assert.Equal((price, price), (rules.Price(piece), flagship.Price(piece)));
            Assert.True(rules.Apply(new Install(SlotKind.Module, slot, piece)), ShipModules.Names[piece]);
            Assert.Equal((piece, ru - (uint)price), (ship.Modules.ModuleSlots[slot], ship.ResourceUnits));
        }
        // Flown in battle as the flagship's: the gun in the nose, a spread pair, both sides, the tail; tracking; point defence.
        var (rd, blasters, trackers) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Escort, ship.Modules, ship.Crew));
        // WEAPON_ENERGY_COST, twice each gun's kind (gun 1, blaster 2, cannon 3), and a tracker's.
        Assert.Equal(1 + 2 * (1 + 1 + 2 + 3) + Sis.Define("TRACKER_ENERGY_COST"), rd.Characteristics.WeaponEnergyCost);
        Assert.Equal(1 + 2 + 2 + 1, blasters.Count);
        Assert.Equal(1, trackers);
        Assert.Equal(Sis.Define("ANTIMISSILE_ENERGY_INC"), rd.Characteristics.SpecialEnergyCost);
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.FiresRight | ShipFlags.FiresLeft | ShipFlags.FiresAft
                     | ShipFlags.PointDefense | ShipFlags.PlayerCaptain, rd.ShipFlags);
        Assert.Equal(Sis.Characteristics.EnergyRegeneration + Sis.Define("SHIVA_ENERGY_REGEN_INC"), rd.Characteristics.EnergyRegeneration);
        Assert.Equal(Sis.Characteristics.EnergyWait - Sis.Define("DYNAMO_UNIT_ENERGY_WAIT_DEC"), rd.Characteristics.EnergyWait);
        Assert.Equal((11, 11), (rd.CrewLevel, rd.MaxCrew));   // its kit's crew: combat modules hold none

        // Each sold back at its price, the kit's gun too, as the flagship sells its own.
        foreach (var (slot, piece) in buys.Append((15, ShipModules.GunWeapon)))
        {
            uint ru = ship.ResourceUnits;
            Assert.True(rules.Apply(new Remove(SlotKind.Module, slot)), ShipModules.Names[piece]);
            Assert.Equal((ShipModules.EmptyModule, ru + (uint)rules.Price(piece)), (ship.Modules.ModuleSlots[slot], ship.ResourceUnits));
        }
        Assert.False(ship.Modules.HasWeapon);
        Assert.Equal(100000u + 2000, ship.ResourceUnits);   // the kit's gun was free
    }

    [Fact]
    public void AnEscortsWeaponsGoOnlyInTheWeaponSlots()
    {
        // As UQM's outfitting puts the flagship's: slots 0 and 13 to 15 (DoInstallModule).
        var ship = Ship(ShipKind.Escort);
        ship.Modules.SetModule(1, ShipModules.EmptyModule);   // the fuel tank's slot free too
        ship.Modules.SetModule(8, ShipModules.EmptyModule);
        var rules = new Outfitting(ship, AllForSale());
        foreach (byte weapon in new[] { ShipModules.GunWeapon, ShipModules.BlasterWeapon, ShipModules.CannonWeapon })
            for (int slot = 1; slot < 13; ++slot)
            {
                Assert.False(rules.CanHold(SlotKind.Module, slot, weapon));
                Assert.False(rules.Apply(new Install(SlotKind.Module, slot, weapon)));
            }
        Assert.Equal(100000u, ship.ResourceUnits);
        foreach (int slot in new[] { 0, 13, 14 })
            Assert.True(rules.Apply(new Install(SlotKind.Module, slot, ShipModules.CannonWeapon)));
        Assert.False(rules.Apply(new Install(SlotKind.Module, 15, ShipModules.BlasterWeapon)));   // the kit's gun is there
        // The others go anywhere free.
        foreach (byte piece in new[] { ShipModules.DynamoUnit, ShipModules.ShivaFurnace, ShipModules.TrackingSystem, ShipModules.AntimissileDefense })
            Assert.True(rules.CanHold(SlotKind.Module, 7, piece));
    }

    [Fact]
    public void AnEscortIsRefusedTheFlagshipsUniqueModules()
    {
        var story = AllForSale();
        var ship = Ship(ShipKind.Escort);
        var rules = new Outfitting(ship, story);
        var refused = new (SlotKind Kind, int Slot, byte Piece)[]
        {
            (SlotKind.Module, 2, ShipModules.CrewPod), (SlotKind.Module, 2, ShipModules.FuelTank),
            (SlotKind.Module, 2, ShipModules.HighEfficiencyFuelSystem), (SlotKind.Drive, 0, ShipModules.FusionThruster),
            (SlotKind.Jet, 1, ShipModules.TurningJets),
        };
        int version = ship.Modules.Version;
        foreach (var (kind, slot, piece) in refused)
        {
            Assert.False(rules.CanHold(kind, slot, piece), ShipModules.Names[piece]);
            Assert.False(rules.Apply(new Install(kind, slot, piece)), ShipModules.Names[piece]);
        }
        // Nor sold back: its kit's fuel tank, thrusters and jets.
        Assert.False(rules.Apply(new Remove(SlotKind.Module, 1)));
        Assert.False(rules.Apply(new Remove(SlotKind.Drive, ship.Modules.DriveSlots.ToList().IndexOf(ShipModules.FusionThruster))));
        Assert.False(rules.Apply(new Remove(SlotKind.Jet, ship.Modules.JetSlots.ToList().IndexOf(ShipModules.TurningJets))));
        Assert.Equal((version, 100000u), (ship.Modules.Version, ship.ResourceUnits));
        // The bomb's parts: never sold, to anyone.
        var flagship = new Outfitting(Ship(ShipKind.Flagship), story);
        for (byte bomb = ShipModules.BombModule0; bomb < ShipModules.Modules; ++bomb)
        {
            Assert.False(Outfitting.EscortMayHold(bomb));
            Assert.False(rules.CanHold(SlotKind.Module, 3, bomb));
            Assert.False(flagship.CanHold(SlotKind.Module, 3, bomb));
        }
        // Landers and storage bays as before; the flagship may buy every one refused here.
        Assert.True(rules.CanHold(SlotKind.Lander, 1, ShipModules.PlanetLander));
        Assert.True(rules.CanHold(SlotKind.Module, 2, ShipModules.StorageBay));
        foreach (var (kind, slot, piece) in refused)
            Assert.True(flagship.CanHold(kind, slot, piece), ShipModules.Names[piece]);
        // Exactly these: landers, storage bays and the combat modules.
        Assert.Equal(new[] { ShipModules.PlanetLander, ShipModules.StorageBay }.Concat(CombatModules).Order(),
            Enumerable.Range(0, ShipModules.Modules).Select(i => (byte)i).Where(Outfitting.EscortMayHold));
    }

    [Fact]
    public void AnEscortsCombatModulesFollowTheFlagshipsOtherRules()
    {
        // At a new game's prices only the gun and the dynamo are for sale.
        var ship = Ship(ShipKind.Escort, ru: 2500);
        var rules = new Outfitting(ship, StoryState.NewGame(Rules, new GameClock(2155, 3, 4)));
        foreach (byte piece in new[] { ShipModules.BlasterWeapon, ShipModules.CannonWeapon, ShipModules.ShivaFurnace,
                     ShipModules.TrackingSystem, ShipModules.AntimissileDefense })
            Assert.False(rules.Apply(new Install(SlotKind.Module, piece == ShipModules.ShivaFurnace ? 2 : 14, piece)));
        Assert.Equal(2000, rules.Price(ShipModules.GunWeapon));
        Assert.Equal(2000, rules.Price(ShipModules.DynamoUnit));
        Assert.True(rules.Apply(new Install(SlotKind.Module, 14, ShipModules.GunWeapon)));
        Assert.Equal(500u, ship.ResourceUnits);
        Assert.False(rules.Apply(new Install(SlotKind.Module, 2, ShipModules.DynamoUnit)));   // too dear
        Assert.False(rules.Apply(new Install(SlotKind.Module, 14, ShipModules.GunWeapon)));   // full
        Assert.Equal(500u, ship.ResourceUnits);
    }

    // The save as an older version made it: no guns in the loadouts or the
    // kit, none in an escort's slots (it couldn't buy any), and before 9 no slots.
    internal static JsonNode Older(string json, int version)
    {
        var node = JsonNode.Parse(json)!;
        node["version"] = version;
        node["settings"]!["escort_loadout"]!.AsObject().Remove("guns");
        foreach (var p in node["players"]!.AsArray())
        {
            p!["loadout"]!.AsObject().Remove("guns");
            if (version < 9)
                p.AsObject().Remove("modules");
            else if ((string)p["kind"]! == "escort")
                SetSlots(p, m =>
                {
                    for (int i = 0; i < m.Length; ++i)
                        if (ShipModules.IsWeapon(m[i]))
                            m[i] = ShipModules.EmptyModule;
                });
        }
        return node;
    }

    // A saved player's module slots changed (saved as base64).
    internal static void SetSlots(JsonNode player, Action<byte[]> change)
    {
        var m = Convert.FromBase64String((string)player["modules"]!["modules"]!);
        change(m);
        player["modules"]!["modules"] = Convert.ToBase64String(m);
    }

    private static JsonNode Player(JsonNode save, string id) =>
        save["players"]!.AsArray().Single(p => (string)p!["id"]! == id)!;

    [Theory]
    [InlineData(9, new int[0], 15)]
    [InlineData(10, new int[0], 15)]
    [InlineData(10, new[] { 15 }, 14)]
    [InlineData(9, new[] { 15, 14 }, 13)]
    [InlineData(10, new[] { 15, 14, 13 }, 0)]
    [InlineData(10, new[] { 15, 14, 13, 0 }, -1)]   // every weapon slot full: none
    [InlineData(10, new[] { 14, 0 }, 15)]
    [InlineData(9, new[] { 15, 13, 0 }, 14)]
    public void AnEscortSavedWithoutAGunIsGivenTheKitsInTheFirstFreeWeaponSlot(int version, int[] full, int given)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        var node = Older(HostSave.ToJson(world), version);
        SetSlots(Player(node, "guest"), m =>
        {
            foreach (int slot in full)
                m[slot] = ShipModules.StorageBay;   // escorts could buy storage bays anywhere
        });
        var loaded = HostSave.FromJson(node.ToJsonString());
        var m = loaded.Find("guest")!.Ship.Modules;
        Assert.Equal(given < 0 ? Array.Empty<int>() : new[] { given }, Slots(m, ShipModules.GunWeapon));
        Assert.Equal(full.Length + 1, m.Count(ShipModules.StorageBay));
        // Its loadout stays the one it was made with; the settings' kit has the default's gun.
        Assert.Equal(0, loaded.Find("guest")!.Ship.Loadout.Guns);
        Assert.Equal(new GameSettings().EscortLoadout, loaded.Settings.EscortLoadout);
        // The flagship is as it was.
        Assert.Equal(world.Host.Ship.Modules.Save(), loaded.Host.Ship.Modules.Save());
        // Saved again (version 11) and loaded: unchanged.
        var again = HostSave.FromJson(HostSave.ToJson(loaded));
        Assert.Equal(m.Save(), again.Find("guest")!.Ship.Modules.Save());
    }

    [Theory]
    [InlineData(15, 0)]   // a fuel tank and 15 storage bays: no room for a gun
    [InlineData(14, 1)]
    [InlineData(12, 1)]   // room for 3: the default kit's one
    public void AnOlderSavesKitHasTheGunsItHasRoomFor(int bays, int guns)
    {
        // A version 10 kit that fills its slots still loads, with as many of the default's guns as fit.
        var kit = new GameSettings().EscortLoadout with { StorageBays = bays, Guns = 0 };
        var world = GameWorld.NewGame(new GameSettings { EscortLoadout = kit }, NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        world.Join("sold", "Sold");
        var node10 = Older(HostSave.ToJson(world), 10);
        SetSlots(Player(node10, "sold"), m => m[14] = ShipModules.EmptyModule);   // a bay sold since
        var loaded = HostSave.FromJson(node10.ToJsonString());
        Assert.Equal(kit with { Guns = guns }, loaded.Settings.EscortLoadout);
        // One with a weapon slot free is given the default kit's gun there, whatever room its kit had.
        Assert.Equal(new[] { 14 }, Slots(loaded.Find("sold")!.Ship.Modules, ShipModules.GunWeapon));
        // Its escort's storage bays, put from slot 8 round, fill every weapon slot: it's given none.
        var old = loaded.Find("guest")!.Ship.Modules;
        Assert.All(ShipModules.WeaponSlots, slot => Assert.Equal(ShipModules.StorageBay, old.ModuleSlots[slot]));
        Assert.Equal(0, old.Count(ShipModules.GunWeapon));
        // A new guest's kit puts the guns first: in the nose.
        loaded.Join("new", "New");
        Assert.Equal(guns == 0 ? Array.Empty<int>() : new[] { 15 }, Slots(loaded.Find("new")!.Ship.Modules, ShipModules.GunWeapon));
        Assert.Equal(bays, loaded.Find("new")!.Ship.Modules.Count(ShipModules.StorageBay));
        // A version 11 save keeps the kit as it is: one that doesn't fit is refused.
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["settings"]!["escort_loadout"]!["guns"] = 3;
        if (bays + 1 + 3 > ShipModules.ModuleSlotCount)
            Assert.Contains("escort loadout", Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString())).Message);
        else
            Assert.Equal(3, HostSave.FromJson(node.ToJsonString()).Settings.EscortLoadout.Guns);
    }

    // The outfitting menu, opened in Modules on a slot of the modules' row.
    private static OutfitMenu InModules(TestShip ship, StoryState story, int slot)
    {
        var rules = new Outfitting(ship, story);
        var menu = new OutfitMenu(rules, rules.Apply);
        menu.Open();
        menu.Key(MenuKey.Down);                       // Modules
        menu.Key(MenuKey.Select);
        Assert.Equal((true, ShipModules.EmptyModule, 0), (menu.Installing, menu.State, menu.Item));
        for (int i = 0; i < slot; ++i)
            menu.Key(MenuKey.Right);
        Assert.Equal(slot, menu.Item);
        return menu;
    }

    // What choosing on the cursor's empty slot offers, from the first, going right round once.
    private static List<byte> Offers(OutfitMenu menu)
    {
        Assert.Equal(OutfitOutcome.None, menu.Key(MenuKey.Select));
        Assert.True(menu.Choosing);
        var offers = new List<byte> { menu.State };
        for (int i = 0; i < ShipModules.PurchaseModules; ++i)
        {
            menu.Key(MenuKey.Right);
            if (menu.State == offers[0])
                break;
            offers.Add(menu.State);
        }
        menu.Key(MenuKey.Cancel);
        Assert.False(menu.Choosing);
        return offers;
    }

    [Fact]
    public void TheMenuOffersAnEscortOnlyWhatItMayBuy()
    {
        var story = AllForSale();
        // In a weapon slot (0) the escort is offered the storage bay first, then the combat modules; never a crew pod or a tank.
        var first = InModules(Ship(ShipKind.Escort), story, 0);
        first.TakeOps();
        Assert.Equal(new[] { ShipModules.StorageBay }.Concat(CombatModules), Offers(first));
        Assert.Equal(new ScreenOp(ScreenOpKind.ModuleStrings, ShipModules.StorageBay), first.TakeOps()[0]);   // the radar shows it
        // Elsewhere (slot 2), no weapons; going left round too.
        var menu = InModules(Ship(ShipKind.Escort), story, 2);
        Assert.Equal(new[] { ShipModules.StorageBay, ShipModules.DynamoUnit, ShipModules.ShivaFurnace,
            ShipModules.TrackingSystem, ShipModules.AntimissileDefense }, Offers(menu));
        menu.Key(MenuKey.Select);
        menu.Key(MenuKey.Left);
        Assert.Equal(ShipModules.AntimissileDefense, menu.State);
        // Bought from the menu.
        menu.Key(MenuKey.Left);
        menu.Key(MenuKey.Left);
        Assert.Equal(ShipModules.ShivaFurnace, menu.State);
        Assert.Equal(OutfitOutcome.Acted, menu.Key(MenuKey.Select));
        // Before the Melnorme: the storage bay, the dynamo and the gun.
        var newGame = StoryState.NewGame(Rules, new GameClock(2155, 3, 4));
        Assert.Equal(new[] { ShipModules.StorageBay, ShipModules.DynamoUnit, ShipModules.GunWeapon },
            Offers(InModules(Ship(ShipKind.Escort), newGame, 0)));
        // The flagship is offered every module for sale, from the crew pod, as UQM's.
        Assert.Equal(new[] { ShipModules.CrewPod, ShipModules.StorageBay, ShipModules.FuelTank, ShipModules.HighEfficiencyFuelSystem }
                .Concat(CombatModules),
            Offers(InModules(Ship(ShipKind.Flagship), story, 0)));
    }

    [Fact]
    public void TheMenuOpensNoChoiceForAnEscortOnAnEmptyDriveOrJetSlot()
    {
        foreach (var kind in new[] { ShipKind.Escort, ShipKind.Flagship })
        {
            var ship = Ship(kind);
            var menu = InModules(ship, AllForSale(), 0);
            menu.Key(MenuKey.Up);                     // the jets
            Assert.Equal(ShipModules.EmptyJet, menu.State);
            menu.Key(MenuKey.Right);                  // slot 1: empty
            Assert.Equal(ShipModules.EmptyJet, ship.Modules.JetSlots[1]);
            menu.TakeOps();
            var outcome = menu.Key(MenuKey.Select);
            if (kind == ShipKind.Escort)
            {
                Assert.Equal((OutfitOutcome.Refused, false, ShipModules.EmptyJet), (outcome, menu.Choosing, menu.State));
                Assert.Empty(menu.TakeOps());
            }
            else
            {
                Assert.Equal((OutfitOutcome.None, true, ShipModules.TurningJets), (outcome, menu.Choosing, menu.State));
                menu.Key(MenuKey.Cancel);
            }
            menu.Key(MenuKey.Up);                     // the drives
            Assert.Equal(ShipModules.EmptyDrive, menu.State);
            Assert.Equal(ShipModules.EmptyDrive, ship.Modules.DriveSlots[0]);
            menu.TakeOps();
            Assert.Equal(kind == ShipKind.Escort ? OutfitOutcome.Refused : OutfitOutcome.None, menu.Key(MenuKey.Select));
            if (kind == ShipKind.Escort)
                Assert.Equal((false, ShipModules.EmptyDrive, 0, 0), (menu.Choosing, menu.State, menu.Item, menu.TakeOps().Count));
            if (kind == ShipKind.Flagship)
                menu.Key(MenuKey.Cancel);
            // Landers: both may choose one.
            menu.Key(MenuKey.Up);
            Assert.Equal(ShipModules.EmptyLander, menu.State);
            menu.Key(MenuKey.Right);
            Assert.Equal(OutfitOutcome.None, menu.Key(MenuKey.Select));
            Assert.Equal(ShipModules.PlanetLander, menu.State);
        }
    }

    [Fact]
    public void TheMenuOpensNoChoiceForAnEscortWithNothingOnOffer()
    {
        // Prices UQM never sets (the storage bay, the dynamo and the gun off sale): nothing for an escort in slot 2.
        var story = StoryState.NewGame(Rules, new GameClock(2155, 3, 4));
        foreach (var module in new[] { "STORAGE_BAY", "DYNAMO_UNIT", "GUN_WEAPON" })
            story.SetModuleCost(module, 0);
        var menu = InModules(Ship(ShipKind.Escort), story, 2);
        menu.TakeOps();
        Assert.Equal(OutfitOutcome.Refused, menu.Key(MenuKey.Select));
        Assert.Equal((false, ShipModules.EmptyModule, 2), (menu.Choosing, menu.State, menu.Item));
        Assert.Empty(menu.TakeOps());
        // The flagship's opens on the crew pod, as UQM's.
        var flagship = InModules(Ship(ShipKind.Flagship), story, 3);
        Assert.Equal(OutfitOutcome.None, flagship.Key(MenuKey.Select));
        Assert.Equal((true, ShipModules.CrewPod), (flagship.Choosing, flagship.State));
    }

    [Fact]
    public void AnEscortSavedBeforeSlotsHasTheKitsGunInTheNose()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        var loaded = HostSave.FromJson(Older(HostSave.ToJson(world), 8).ToJsonString());
        Assert.Equal(world.Find("guest")!.Ship.Modules.Save(), loaded.Find("guest")!.Ship.Modules.Save());
        Assert.Equal(new[] { 15 }, Slots(loaded.Find("guest")!.Ship.Modules, ShipModules.GunWeapon));
    }

    [Fact]
    public void OnlyAnOlderSavesEscortsAreGivenAGun()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.Join("guest", "Guest");
        var guest = world.Find("guest")!.Ship;
        // A version 11 escort that sold its gun keeps none: the version decides, not having no weapon.
        guest.Modules.SetModule(15, ShipModules.EmptyModule);
        var json = HostSave.ToJson(world);
        Assert.Equal(11, (int)JsonNode.Parse(json)!["version"]!);
        Assert.False(HostSave.FromJson(json).Find("guest")!.Ship.Modules.HasWeapon);
        // A flagship that sold its gun keeps none, from any version.
        world.Host.Ship.Modules.SetModule(15, ShipModules.EmptyModule);
        Assert.False(HostSave.FromJson(HostSave.ToJson(world)).Host.Ship.Modules.HasWeapon);
        Assert.False(HostSave.FromJson(Older(HostSave.ToJson(world), 10).ToJsonString()).Host.Ship.Modules.HasWeapon);
        // An older escort with a weapon (none could have one, but a cannon in the tail) is given none.
        var node = Older(HostSave.ToJson(world), 10);
        SetSlots(Player(node, "guest"), m => m[0] = ShipModules.CannonWeapon);
        var m = HostSave.FromJson(node.ToJsonString()).Find("guest")!.Ship.Modules;
        Assert.Equal((0, ShipModules.CannonWeapon), (m.Count(ShipModules.GunWeapon), m.ModuleSlots[0]));
        // A gun where it doesn't fire (slot 5) isn't a weapon: the kit's is given.
        node = Older(HostSave.ToJson(world), 10);
        SetSlots(Player(node, "guest"), m => m[5] = ShipModules.GunWeapon);
        Assert.Equal(new[] { 5, 15 }, Slots(HostSave.FromJson(node.ToJsonString()).Find("guest")!.Ship.Modules, ShipModules.GunWeapon));
        // Guns in an older save's loadouts or kit, which it can't have had, are taken as none: the default kit's is given.
        node = Older(HostSave.ToJson(world), 10);
        node["settings"]!["escort_loadout"]!["guns"] = 3;
        Player(node, "guest")["loadout"]!["guns"] = 2;
        Player(node, "host")["loadout"]!["guns"] = 1;
        var loaded = HostSave.FromJson(node.ToJsonString());
        Assert.Equal(new[] { 15 }, Slots(loaded.Find("guest")!.Ship.Modules, ShipModules.GunWeapon));
        Assert.Equal((1, 0, 0), (loaded.Settings.EscortLoadout.Guns, loaded.Find("guest")!.Ship.Loadout.Guns, loaded.Host.Ship.Loadout.Guns));
        // A version 11 save's kit of two guns gives a new guest two.
        var two = GameWorld.NewGame(new GameSettings { EscortLoadout = new GameSettings().EscortLoadout with { Guns = 2 } },
            NewGameData.Load(DataDir), "host", "Host");
        var reloaded = HostSave.FromJson(HostSave.ToJson(two));
        Assert.Equal(2, reloaded.Settings.EscortLoadout.Guns);
        reloaded.Join("guest", "Guest");
        Assert.Equal(new[] { 14, 15 }, Slots(reloaded.Find("guest")!.Ship.Modules, ShipModules.GunWeapon));
    }
}
