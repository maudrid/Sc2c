// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// A player ship's battle description from its own slots and crew
/// (<see cref="SisShip"/>, sis_ship.c's init_sis, stage 4 decision 3), and
/// what it gives back when it's freed, without the content: an escort's
/// kit with a gun builds and fights, and a ship destroyed reports so
/// without UQM's game-over sentinel.
/// </summary>
[Trait("Topic", "combat")]
public class SisShipTests
{
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly ShipDescription Sis = Ships[ShipDescriptions.Flagship];

    private static byte[] Modules(params (int Slot, byte Module)[] modules)
    {
        var slots = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        foreach (var (slot, module) in modules)
            slots[slot] = module;
        return slots;
    }

    private static byte[] Drives(int count) => Enumerable.Range(0, ShipModules.DriveSlotCount)
        .Select(i => i < count ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray();

    private static byte[] Jets(int count) => Enumerable.Range(0, ShipModules.JetSlotCount)
        .Select(i => i < count ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray();

    // The escort's kit as a new guest gets it, with a gun in the nose (item 8 gives it one).
    private static ShipModules EscortWithGun()
    {
        var modules = ShipModules.FromLoadout(ShipKind.Escort, new GameSettings().EscortLoadout);
        modules.SetModule(15, ShipModules.GunWeapon);
        return modules;
    }

    [Fact]
    public void AnEscortsKitWithAGunBuilds()
    {
        var settings = new GameSettings();
        var modules = EscortWithGun();
        var (rd, blasters, trackers) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Escort, modules, settings.EscortLoadout.Crew));

        // Its crew: the kit's 10, all aboard, and the captain.
        Assert.Equal((11, 11), (rd.CrewLevel, rd.MaxCrew));
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.PlayerCaptain, rd.ShipFlags);
        var drive = ShipDrive.ForBattle(settings.EscortLoadout.Thrusters, settings.EscortLoadout.Jets);
        var c = rd.Characteristics;
        Assert.Equal((drive.MaxThrust, drive.ThrustWait, drive.TurnWait), (c.MaxThrust, c.ThrustWait, c.TurnWait));
        Assert.Equal((Sis.MaxEnergy, Sis.MaxEnergy), (rd.EnergyLevel, rd.MaxEnergy));
        Assert.Equal(1 + 2, c.WeaponEnergyCost);   // WEAPON_ENERGY_COST and the gun's
        Assert.Equal(0, c.SpecialEnergyCost);
        Assert.Equal(0, trackers);
        var gun = Assert.Single(blasters);
        Assert.Equal((0, 0, Sis.Define("SIS_VERT_OFFSET"), Sis.Define("BLASTER_DAMAGE"), Sis.Define("BLASTER_LIFE")),
            ((int)gun.Face, (int)gun.Index, (int)gun.PixOffs, (int)gun.Damage, (int)gun.Life));

        // And it flies, fires, and dies, on sheets of its own.
        var escort = new SisShip(Sis, Three(Solid("hull", 16, 20)), Three(Solid("blaster", 27, 4)),
            SisLoadout.From(ShipKind.Escort, modules, settings.EscortLoadout.Crew));
        var battle = NewBattle(escort);
        var ship = battle.Fleet(0)[0];
        for (int i = 0; i < 30; ++i)
            battle.Step(new[] { BattleInput.Weapon, BattleInput.None });
        Assert.Equal(11, ship.RaceDesc!.CrewLevel);
        Assert.Contains(battle.List.Elements, e => e.Parent == ship && (e.StateFlags & ElementFlags.FiniteLife) != 0
                                                   && e.MassPoints == Sis.Define("BLASTER_DAMAGE"));
        Assert.True(ship.RaceDesc.EnergyLevel < Sis.MaxEnergy);
        Assert.Null(escort.Result);

        // Destroyed: the crew left is none, and it says so; no sentinel.
        battle.DoDamage(ship.Ship!, 11);
        for (int i = 0; i < 200 && battle.InBattle; ++i)
            battle.Step(new[] { BattleInput.None, BattleInput.None });
        Assert.False(battle.InBattle);
        Assert.Equal(new SisBattleResult(0, Destroyed: true), escort.Result);
        Assert.Null(ship.RaceDesc);
    }

    [Fact]
    public void ADeadShipsResultIsntASentinel()
    {
        var escort = new SisShip(Sis, null, null, SisLoadout.From(ShipKind.Escort, EscortWithGun(), 4));
        var starship = new Starship(escort, 0, 0, 0);
        var rd = escort.Load(null!, starship);
        rd.CrewLevel = 0;
        rd.Uninit!(rd);
        Assert.Equal(new SisBattleResult(0, true), escort.Result);

        // Alive: the crew left less the captain.
        rd = escort.Load(null!, starship);
        Assert.Null(escort.Result);
        rd.CrewLevel = 3;
        rd.Uninit!(rd);
        Assert.Equal(new SisBattleResult(2, false), escort.Result);
        rd.CrewLevel = 1;   // the captain alone
        rd.Uninit!(rd);
        Assert.Equal(new SisBattleResult(0, false), escort.Result);
    }

    [Fact]
    public void AFlagshipHoldsCrewInPodsOnly()
    {
        // A new game's flagship: its crew pod's 50, no kit crew.
        var start = NewGameData.Load(Path.Combine(AppContext.BaseDirectory, "data")).Flagship;
        var flagship = new PlayerShip(ShipKind.Flagship, start);
        Assert.Equal(0, flagship.Modules.KitCrew);
        var (rd, _, _) = SisShip.Describe(Sis, SisLoadout.From(ShipKind.Flagship, flagship.Modules, start.Crew));
        Assert.Equal((start.Crew + 1, flagship.Modules.CrewCapacity + 1), (rd.CrewLevel, rd.MaxCrew));

        // Crew beside the pods is refused for a flagship: its loadout, its
        // saved slots, and its battle description.
        Assert.Throws<ArgumentException>(() => ShipModules.FromLoadout(ShipKind.Flagship, start with { CrewCapacity = 60 }));
        var withKit = new ShipModules(flagship.Modules.DriveSlots, flagship.Modules.JetSlots, flagship.Modules.ModuleSlots, 10);
        Assert.Throws<ArgumentException>(() => new PlayerShip(ShipKind.Flagship, start, withKit));
        Assert.Throws<ArgumentException>(() => SisLoadout.From(ShipKind.Flagship, withKit, 10));
        // An escort's kit holds its own.
        Assert.Equal(10, SisLoadout.From(ShipKind.Escort, withKit, 10).KitCrew);
    }

    [Fact]
    public void APlayerShipsStarshipHasNoCrewOfItsOwn()
    {
        // spawn_ship would put a Starship's crew in place of the loadout's,
        // without the captain: refused.
        var escort = new SisShip(Sis, null, null, SisLoadout.From(ShipKind.Escort, EscortWithGun(), 10));
        Assert.Throws<InvalidOperationException>(() => escort.Load(null!, new Starship(escort, 0, 10, 0)));
        Assert.Equal(11, escort.Load(null!, new Starship(escort, 0, 0, 0)).CrewLevel);
    }

    [Fact]
    public void TwoPlayerShipsEachHaveTheirOwnResult()
    {
        // An escort's kit and a new game's flagship, one after the other on
        // one side: each built from its own slots, each with its own result.
        var start = NewGameData.Load(Path.Combine(AppContext.BaseDirectory, "data")).Flagship;
        var flagshipModules = ShipModules.FromLoadout(ShipKind.Flagship, start);
        var escort = new SisShip(Sis, Three(Solid("hull", 16, 20)), Three(Solid("blaster", 27, 4)),
            SisLoadout.From(ShipKind.Escort, EscortWithGun(), 10));
        var flagship = new SisShip(Sis, Three(Solid("hull", 16, 20)), Three(Solid("blaster", 27, 4)),
            SisLoadout.From(ShipKind.Flagship, flagshipModules, start.Crew));
        var battle = NewBattle(escort, flagship);
        var (first, second) = (battle.Fleet(0)[0], battle.Fleet(0)[1]);
        Assert.Equal((11, 11), (first.RaceDesc!.CrewLevel, first.RaceDesc.MaxCrew));
        Assert.Null(second.RaceDesc);

        for (int i = 0; i < 30; ++i)   // warped in
            battle.Step(new[] { BattleInput.None, BattleInput.None });
        battle.DoDamage(first.Ship!, 11);
        for (int i = 0; i < 200 && second.RaceDesc == null; ++i)
            battle.Step(new[] { BattleInput.None, BattleInput.None });
        Assert.Equal(new SisBattleResult(0, Destroyed: true), escort.Result);
        Assert.Null(flagship.Result);
        Assert.Equal((start.Crew + 1, flagshipModules.CrewCapacity + 1), (second.RaceDesc!.CrewLevel, second.RaceDesc.MaxCrew));

        // The flagship hit, then freed as the battle's end frees the ships left (UninitShips).
        for (int i = 0; i < 30; ++i)
            battle.Step(new[] { BattleInput.None, BattleInput.None });
        battle.DoDamage(second.Ship!, 5);
        second.RaceDesc.Uninit!(second.RaceDesc);
        Assert.Equal(new SisBattleResult(start.Crew - 5, Destroyed: false), flagship.Result);
        Assert.Equal(new SisBattleResult(0, Destroyed: true), escort.Result);
    }

    [Fact]
    public void TheSlotsMakeTheShip()
    {
        // Guns in every weapon slot: a blaster ahead, a spread pair of
        // cannons, guns either side, a cannon behind; tracking (four, three
        // count), point defence (nine, held to eight), dynamos (four,
        // held at 4) and furnaces; crew pods.
        var modules = Modules((15, ShipModules.BlasterWeapon), (14, ShipModules.CannonWeapon), (13, ShipModules.GunWeapon),
            (0, ShipModules.CannonWeapon), (1, ShipModules.TrackingSystem), (2, ShipModules.TrackingSystem),
            (3, ShipModules.TrackingSystem), (4, ShipModules.TrackingSystem), (5, ShipModules.DynamoUnit),
            (6, ShipModules.DynamoUnit), (7, ShipModules.DynamoUnit), (8, ShipModules.DynamoUnit),
            (9, ShipModules.ShivaFurnace), (10, ShipModules.ShivaFurnace), (11, ShipModules.CrewPod), (12, ShipModules.CrewPod));
        var (rd, blasters, trackers) = SisShip.Describe(Sis, new SisLoadout(Drives(11), Jets(8), modules, 70));
        var c = rd.Characteristics;
        Assert.Equal(3, trackers);
        // 1, three trackers (3 each), and twice each weapon slot's kind: 2, 3, 1, 3.
        Assert.Equal(1 + 3 * 3 + 2 * (2 + 3 + 1 + 3), c.WeaponEnergyCost);
        Assert.Equal((4, 1 + 2), (c.EnergyWait, c.EnergyRegeneration));
        Assert.Equal((71, 101), (rd.CrewLevel, rd.MaxCrew));
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.FiresLeft | ShipFlags.FiresRight | ShipFlags.FiresAft
                     | ShipFlags.PlayerCaptain, rd.ShipFlags);
        Assert.Equal((36, 1, 1), (c.MaxThrust, c.ThrustWait, c.TurnWait));
        // The nose, the spread pair, the sides, the tail.
        Assert.Equal(new[] { (0, 9, 4), (1, 16, 6), (15, 16, 6), (4, 0, 2), (12, 0, 2), (8, 16, 6) },
            blasters.Select(b => ((int)b.Face, (int)b.Index, (int)b.Damage)));
        Assert.Equal(new[] { 28, 28, 28, 20, 20, 28 }, blasters.Select(b => (int)b.PixOffs));
        Assert.Equal(new[] { 15, 18, 18, 12, 12, 18 }, blasters.Select(b => (int)b.Life));

        // Point defence: one energy a module, at most eight.
        var defence = Modules(Enumerable.Range(0, 9).Select(i => (i, ShipModules.AntimissileDefense)).ToArray());
        (rd, blasters, _) = SisShip.Describe(Sis, new SisLoadout(Drives(0), Jets(0), defence, 0));
        Assert.Equal(8, rd.Characteristics.SpecialEnergyCost);
        Assert.Equal(ShipFlags.PointDefense | ShipFlags.PlayerCaptain, rd.ShipFlags);
        Assert.Empty(blasters);
        // Nothing: the captain alone, the base drive.
        (rd, _, _) = SisShip.Describe(Sis, new SisLoadout(Drives(0), Jets(0), Modules(), 0));
        Assert.Equal((1, 1), (rd.CrewLevel, rd.MaxCrew));
        Assert.Equal((12, 6, 17, 10), (rd.Characteristics.MaxThrust, rd.Characteristics.ThrustWait,
            rd.Characteristics.TurnWait, rd.Characteristics.EnergyWait));
    }

    [Fact]
    public void TheBattlesDriveIsInitSiss()
    {
        // ShipDrive's base values are sis_desc's (data/ships.json).
        var c = Sis.Characteristics;
        var none = ShipDrive.ForBattle(0, 0);
        Assert.Equal(((c.MaxThrust / c.ThrustIncrement + 1) * c.ThrustIncrement, c.ThrustIncrement, c.ThrustWait, c.TurnWait),
            (none.MaxThrust, none.ThrustIncrement, none.ThrustWait, none.TurnWait));
        // Hyperspace's is 4 slower before the rounding up.
        for (int thrusters = 0; thrusters <= ShipDrive.MaxThrusters; ++thrusters)
        {
            var battle = ShipDrive.ForBattle(thrusters, 3);
            var hyper = ShipDrive.ForHyperspace(thrusters, 3);
            Assert.Equal(((10 + 2 * thrusters) / 4 + 1) * 4, battle.MaxThrust);
            Assert.Equal(((6 + 2 * thrusters) / 4 + 1) * 4, hyper.MaxThrust);
            Assert.Equal((hyper.ThrustWait, hyper.TurnWait), (battle.ThrustWait, battle.TurnWait));
        }
    }

    [Fact]
    public void TheBombsCarrierIsSaid()
    {
        var loadout = new SisLoadout(Drives(2), Jets(2), Modules((15, ShipModules.GunWeapon)), 10, carriesBomb: true);
        Assert.True(new SisShip(Sis, null, null, loadout).BombCarrier);
        Assert.False(new SisShip(Sis, null, null, SisLoadout.From(ShipKind.Escort, EscortWithGun(), 10)).BombCarrier);
        Assert.Throws<ArgumentException>(() => SisLoadout.From(ShipKind.Escort, EscortWithGun(), 10, carriesBomb: true));
        Assert.Throws<ArgumentException>(() => new SisShip(Ships[ShipDescriptions.Ilwrath], null, null, loadout));
        Assert.Throws<ArgumentException>(() => new SisLoadout(Drives(2), Jets(2), new byte[3], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SisLoadout(Drives(2), Jets(2), Modules(), -1));
        // The most crew, sixteen crew pods, the kit's and the captain, fits a COUNT, and no more.
        var pods = Modules(Enumerable.Range(0, ShipModules.ModuleSlotCount).Select(i => (i, ShipModules.CrewPod)).ToArray());
        var (most, _, _) = SisShip.Describe(Sis, new SisLoadout(Drives(0), Jets(0), pods, 0, kitCrew: 64734));
        Assert.Equal(ushort.MaxValue, most.MaxCrew);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SisLoadout(Drives(2), Jets(2), Modules(), 0, kitCrew: 64735));
    }

    // A battle on sheets of our own (solid squares): the ship against the Ilwrath.
    private static Battle NewBattle(params SisShip[] ships)
    {
        var arena = new BattleArena(new Views(Solid("stars", 3, 1)), Three(Solid("asteroid", 21, 10)),
            Three(Solid("blast", 8, 5)), Three(Solid("boom", 9, 3)), Three(Solid("planet", 1, 60)));
        var ilwrath = new IlwrathShip(Ships[ShipDescriptions.Ilwrath], Three(Solid("avenger", 16, 20)),
            Three(Solid("fire", 8, 4)), noCloak: true);
        return new Battle(arena, new[]
        {
            (IReadOnlyList<Starship>)ships.Select(ship => new Starship(ship, 0, 0, 0)).ToList(),
            new[] { new Starship(ilwrath, 1, 7, 0) { CaptainsNameIndex = 5 } },
        }, 4242);
    }

    private static Sheet Solid(string id, int count, int size) => new(id, count,
        Enumerable.Range(0, count).Select(_ => new IntersectFrame(size, size, size / 2, size / 2,
            Enumerable.Repeat((byte)1, size * size).ToArray())).ToList());

    private static Views Three(Sheet big) => new(big, new Sheet(big.Id + ".medium", big.Count), new Sheet(big.Id + ".small", big.Count));
}
