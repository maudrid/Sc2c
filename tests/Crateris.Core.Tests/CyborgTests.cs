// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The AI (<see cref="Battle.TacticalIntelligence"/>) where it goes past
/// what UQM's traces can show, without the content: with several ships
/// about, it takes the nearest opposing one as its enemy (UQM's rule) and
/// ignores a ship of its own side (the co-op side check UQM lacks); each
/// ship's input is asked in ProcessInput's order, so the AI sees a retreat
/// made that frame; the Earth fight's Ilwrath never cloaks; and in the
/// last battle the AI doesn't fly.
/// </summary>
[Trait("Topic", "combat")]
public class CyborgTests
{
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly ShipDescription Sis = Ships[ShipDescriptions.Flagship];
    private static readonly ShipDescription Ilwrath = Ships[ShipDescriptions.Ilwrath];

    private static readonly Views Hull = Three(Solid("hull", 16, 20)), Shot = Three(Solid("shot", 16, 4));

    [Fact]
    public void TheAiTakesTheNearestOpposingShip()
    {
        var (battle, flagship, ilwrath) = Fought();
        var ai = ilwrath.Ship!;
        Put(flagship.Ship!, ai, 2400, 0);

        // A second player ship, nearer than the flagship: the enemy.
        var nearer = Decoy(battle, 0, ai, -800, 300);
        Assert.Same(nearer, Chosen(battle, ilwrath));

        // Farther than the flagship: the flagship.
        Put(nearer, ai, 0, 3000);
        Assert.Same(flagship.Ship, Chosen(battle, ilwrath));

        // By straight-line distance (cyborg.c), not TrackShip's |dx| + |dy|:
        // the flagship 1,414 units off diagonally (2,000 by |dx| + |dy|)
        // beats a ship 1,600 off straight ahead.
        Put(flagship.Ship!, ai, 1000, 1000);
        Put(nearer, ai, 1600, 0);
        Assert.Same(flagship.Ship, Chosen(battle, ilwrath));
    }

    [Fact]
    public void TheAiIgnoresAShipOfItsOwnSide()
    {
        var (battle, flagship, ilwrath) = Fought();
        var ai = ilwrath.Ship!;
        Put(flagship.Ship!, ai, 2400, 0);

        // A teammate of the Ilwrath's, much nearer than the flagship. UQM's
        // choice has no side check (cyborg.c skips only its own ship and
        // shots, by IGNORE_SIMILAR and the same STARSHIP) and would take it;
        // the port's takes the flagship.
        var teammate = Decoy(battle, 1, ai, 200, 100);
        Assert.Same(flagship.Ship, Chosen(battle, ilwrath));

        // With the flagship gone from the list, there's no enemy at all.
        battle.List.Remove(flagship.Ship!);
        Assert.Null(Chosen(battle, ilwrath));
        battle.List.Put(flagship.Ship!);

        // An opposing ship farther than the teammate but nearer than the
        // flagship is taken (UQM's would still take the teammate).
        var opposing = Decoy(battle, 0, ai, -700, 0);
        Assert.Same(opposing, Chosen(battle, ilwrath));
        Assert.NotSame(teammate, Chosen(battle, ilwrath));
    }

    [Fact]
    public void EachShipsInputIsAskedInProcessInputsOrder()
    {
        // Two ships a side: only the one in the battle (with an element) is asked.
        var (battle, flagship, ilwrath) = Fought(runAwayAllowed: true, fleet: 2);
        Assert.All(new[] { 0, 1 }, side => Assert.Null(battle.Fleet(side)[1].Ship));
        var asked = new List<Starship>();
        int massSeen = -1;
        battle.Step(ship =>
        {
            asked.Add(ship);
            if (ship.PlayerNr == 0)
                return BattleInput.Escape;
            // Side 0's retreat (DoRunAway) has happened by the time side 1 is asked.
            massSeen = flagship.Ship!.MassPoints;
            return battle.ComputerIntelligence(ship);
        });
        Assert.Equal(new[] { flagship, ilwrath }, asked);
        Assert.Equal(Battle.MaxShipMass * 10, massSeen);

        // The keys of one side apply to all its ships, as before.
        Assert.Throws<ArgumentException>(() => battle.Step(new[] { BattleInput.None }));
    }

    [Fact]
    public void TheEarthIlwrathNeverCloaks()
    {
        foreach (bool noCloak in new[] { true, false })
        {
            var (battle, flagship, ilwrath) = Fought(noCloak: noCloak);
            // The flagship far off (the AI cloaks when it isn't close enough to flame).
            Put(flagship.Ship!, ilwrath.Ship!, 3000, 3000);
            bool special = false;
            for (int i = 0; i < 40 && battle.InBattle; ++i)
                battle.Step(ship =>
                {
                    if (ship.PlayerNr == 0)
                        return BattleInput.None;
                    var input = battle.ComputerIntelligence(ship);
                    special |= (input & BattleInput.Special) != 0;
                    return input;
                });
            Assert.Equal(!noCloak, special);
            Assert.Equal(!noCloak, Battle.ObjectCloaked(ilwrath.Ship!));
        }
    }

    [Fact]
    public void TheAiDoesntFlyInTheLastBattle()
    {
        var (battle, _, ilwrath) = Fought();
        Assert.NotEqual(BattleInput.None, battle.ComputerIntelligence(ilwrath));
        Assert.Equal(BattleInput.None, battle.ComputerIntelligence(ilwrath, lastBattle: true));
    }

    [Fact]
    public void TheRatingsAimAndUseTheSpecialAsUqms()
    {
        // STANDARD_RATING adds to the special's counter each frame it flies
        // (never using it); the others don't.
        foreach (var rating in new[] { CyborgRating.Standard, CyborgRating.Good, CyborgRating.Awesome })
        {
            var (battle, _, ilwrath) = Fought();
            ilwrath.SpecialCounter = 0;
            battle.ComputerIntelligence(ilwrath, rating);
            Assert.Equal(rating == CyborgRating.Standard ? 1 : 0, ilwrath.SpecialCounter);
            Assert.Equal(rating, ilwrath.Rating);
        }
    }

    // A battle of a flagship against the Ilwrath on sheets of our own, run
    // until both have warped in (the AI flies only a ship that's drawn).
    private static (Battle Battle, Starship Flagship, Starship Ilwrath) Fought(bool runAwayAllowed = false,
        bool noCloak = false, int fleet = 1)
    {
        var arena = new BattleArena(new Views(Solid("stars", 3, 1)), Three(Solid("asteroid", 21, 10)),
            Three(Solid("blast", 8, 5)), Three(Solid("boom", 9, 3)), Three(Solid("planet", 1, 60)));
        Starship Flagship() => new(new SisShip(Sis, Hull, Shot, Loadout()), 0, 0, 0);
        Starship Avenger() => new(new IlwrathShip(Ilwrath, Hull, Shot, noCloak), 1, 22, 0) { CaptainsNameIndex = 5 };
        var fleets = new[]
        {
            Enumerable.Range(0, fleet).Select(_ => Flagship()).ToArray(),
            Enumerable.Range(0, fleet).Select(_ => Avenger()).ToArray(),
        };
        var (flagship, ilwrath) = (fleets[0][0], fleets[1][0]);
        var battle = new Battle(arena, fleets, 777, runAwayAllowed: runAwayAllowed);
        for (int i = 0; i < 200 && (flagship.Ship!.Prim == PrimType.None || ilwrath.Ship!.Prim == PrimType.None
                                    || (flagship.Ship.StateFlags & ElementFlags.Appearing) != 0); ++i)
            battle.Step(new[] { BattleInput.None, BattleInput.None });
        Assert.Equal(PrimType.Stamp, flagship.Ship!.Prim);
        Assert.Equal(PrimType.Stamp, ilwrath.Ship!.Prim);
        return (battle, flagship, ilwrath);
    }

    private static SisLoadout Loadout()
    {
        var modules = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        modules[2] = ShipModules.CrewPod;
        modules[15] = ShipModules.GunWeapon;
        return new SisLoadout(
            Enumerable.Range(0, ShipModules.DriveSlotCount).Select(i => i < 2 ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, ShipModules.JetSlotCount).Select(i => i < 2 ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            modules, 30);
    }

    // The enemy ship the AI chose this frame (ENEMY_SHIP_INDEX), its input otherwise not used.
    private static Element? Chosen(Battle battle, Starship ai)
    {
        var rd = ai.RaceDesc!;
        var own = rd.Intelligence!;
        Element? chosen = null;
        rd.Intelligence = (ship, concerns, count) =>
        {
            chosen = concerns[Battle.EnemyShipIndex].Object;
            own(ship, concerns, count);
        };
        try
        {
            battle.ComputerIntelligence(ai);
        }
        finally
        {
            rd.Intelligence = own;
        }
        return chosen;
    }

    // An element placed (dx, dy) world units from another, still.
    private static void Put(Element e, Element from, int dx, int dy)
    {
        e.Current.X = e.Next.X = (short)Battle.WrapX(from.Next.X + dx);
        e.Current.Y = e.Next.Y = (short)Battle.WrapY(from.Next.Y + dy);
        e.Velocity.Zero();
    }

    // Another ship of a side in the battle, as a side with several ships at
    // once will have (item 5): its own STARSHIP and element, placed by the AI's.
    private static Element Decoy(Battle battle, short side, Element near, int dx, int dy)
    {
        IShipCode code = side == 0
            ? new SisShip(Sis, Hull, Shot, Loadout())
            : new IlwrathShip(Ilwrath, Hull, Shot);
        var starship = new Starship(code, side, side == 0 ? (ushort)0 : (ushort)22, 0);
        starship.RaceDesc = code.Load(battle, starship);
        var e = battle.List.Alloc()!;
        e.StateFlags = ElementFlags.PlayerShip | ElementFlags.IgnoreSimilar;
        e.PlayerNr = side;
        e.Parent = starship;
        e.LifeSpan = Battle.NormalLife;
        e.CrewLevel = starship.RaceDesc.CrewLevel;
        e.MassPoints = starship.RaceDesc.Characteristics.ShipMass;
        e.Prim = PrimType.Stamp;
        e.Current.FrameArray = e.Next.FrameArray = Hull;
        e.Current.Frame = e.Next.Frame = Hull[0];
        Put(e, near, dx, dy);
        starship.Ship = e;
        battle.List.Put(e);
        return e;
    }

    private static Sheet Solid(string id, int count, int size) => new(id, count,
        Enumerable.Range(0, count).Select(_ => new IntersectFrame(size, size, size / 2, size / 2,
            Enumerable.Repeat((byte)1, size * size).ToArray())).ToList());

    private static Views Three(Sheet big) => new(big, new Sheet(big.Id + ".medium", big.Count), new Sheet(big.Id + ".small", big.Count));
}
