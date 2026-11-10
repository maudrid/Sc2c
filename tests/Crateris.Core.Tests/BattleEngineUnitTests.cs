// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle engine's parts without the content (BattleEngineTests checks
/// it whole against UQM's traces, with the content): the display list's
/// slots and order, frame indexing, and a battle's start on sheets of our own.
/// </summary>
[Trait("Topic", "combat")]
public class BattleEngineUnitTests
{
    [Fact]
    public void TheListHandsOutSlotsAsUqmsFreeListDoes()
    {
        var list = new DisplayList(4);
        var a = list.Alloc()!;
        var b = list.Alloc()!;
        var c = list.Alloc()!;
        Assert.Equal(new[] { 1, 2, 3 }, new[] { a.Slot, b.Slot, c.Slot });
        list.Free(b);
        list.Free(a);
        // Last freed first, the slot's element cleared.
        a.LifeSpan = 7;
        var again = list.Alloc()!;
        Assert.Same(a, again);
        Assert.Equal(0, again.LifeSpan);
        Assert.Same(b, list.Alloc());
        Assert.Equal(4, list.Alloc()!.Slot);
        Assert.Null(list.Alloc());   // full: allocating fails, quietly
    }

    [Fact]
    public void ElementsGoAtTheTailOrBeforeAnother()
    {
        var list = new DisplayList();
        Element a = list.Alloc()!, b = list.Alloc()!, c = list.Alloc()!, d = list.Alloc()!;
        list.Put(a);
        list.Put(b);
        list.Insert(c, list.Head);   // at the head (InsertElement before the head)
        list.Insert(d, b);
        Assert.Equal(new[] { c, a, d, b }, list.Elements);
        list.Remove(c);
        list.Remove(b);
        Assert.Equal(new[] { a, d }, list.Elements);
        Assert.Same(a, list.Head);
        Assert.Same(d, list.Tail);
        Assert.Throws<InvalidOperationException>(() => list.Remove(b));
        Assert.Throws<InvalidOperationException>(() => list.Free(a));   // still in the list
    }

    [Fact]
    public void FramesIndexAsPixmapDoes()
    {
        var sheet = new Sheet("s", 5);
        var other = new Sheet("t", 3);
        var f = new Frame(sheet, 0);
        Assert.Equal(2, f.SetAbs(7).Index);
        Assert.Equal(4, f.SetRel(-1).Index);
        Assert.Equal(1, f.SetAbs(4).SetRel(-8).Index);
        Assert.Equal(0, f.SetAbs(4).Inc().Index);
        Assert.Equal(4, f.Dec().Index);
        Assert.Equal(new Frame(other, 1), Frame.SetEqu(new Frame(other, 0), f.SetAbs(4)));
        Assert.True(Frame.SetEqu(default, f).IsNull);
        // Frames compare as UQM's pointers: the same sheet and index.
        Assert.NotEqual(new Frame(sheet, 1), new Frame(new Sheet("s", 5), 1));
        Assert.Equal(0, default(Frame).Count);
        Assert.Throws<InvalidOperationException>(() => new Frame(sheet, 0).Shape);   // no shapes: not tested for contact
    }

    [Fact]
    public void ABattleStartsAsUqmsDoes()
    {
        var places = new List<string>();
        var battle = NewBattle(42, places);
        // InitGalaxy's 360, then each asteroid's 9 (WRAP_X reads its
        // argument three times, each a number), the planet's 6 a try, each
        // ship's facing and 6 a try, the enemy's first.
        Assert.Equal(Enumerable.Repeat("InitGalaxy", 360), places.Take(360));
        Assert.Equal(45, places.Count(p => p == "spawn_asteroid"));
        Assert.Equal(0, places.Count(p => p == "spawn_planet") % 6);
        Assert.Equal(2, places.Count(p => p == "spawn_ship") % 6);
        Assert.Equal(places.Skip(360).OrderBy(p => p switch
        {
            "spawn_asteroid" => 0, "spawn_planet" => 1, _ => 2,
        }), places.Skip(360));

        // The ships at the head, the player's first; the asteroids on the seam, then the planet.
        var elements = battle.List.Elements.ToList();
        Assert.Equal(8, elements.Count);
        Assert.Same(battle.Fleet(0)[0].Ship, elements[0]);
        Assert.Same(battle.Fleet(1)[0].Ship, elements[1]);
        Assert.All(elements.Skip(2).Take(5), a => Assert.True(a.Current.X is 0 or Battle.LogSpaceWidth
            || a.Current.Y is 0 or Battle.LogSpaceHeight, "an asteroid starts on the seam"));
        Assert.Equal(200, elements[7].MassPoints);
        Assert.Equal(180, battle.Stars.Count);

        // Warping in: unseen and untouchable, then a ship.
        for (int i = 0; i < 20; ++i)
        {
            battle.Step(new[] { BattleInput.None, BattleInput.None });
            var ship = battle.Fleet(0)[0].Ship!;
            if (i == 0)
                Assert.Equal((PrimType.None, true), (ship.Prim, (ship.StateFlags & ElementFlags.NonSolid) != 0));
        }
        var warped = battle.Fleet(0)[0].Ship!;
        Assert.Equal((PrimType.Stamp, ElementFlags.None), (warped.Prim, warped.StateFlags & (ElementFlags.NonSolid | ElementFlags.FiniteLife)));

        // The same seed, the same battle.
        var replay = NewBattle(42, null);
        for (int i = 0; i < 20; ++i)
            replay.Step(new[] { BattleInput.None, BattleInput.None });
        Assert.Equal(battle.Random.Seed, replay.Random.Seed);
        Assert.Equal(battle.List.Elements.Select(e => (e.Current.X, e.Current.Y)), replay.List.Elements.Select(e => (e.Current.X, e.Current.Y)));
    }

    [Fact]
    public void AShotIsntFiredWithTheListFull()
    {
        var battle = NewBattle(7, null);
        var taken = new List<Element>();
        while (battle.List.Alloc() is { } e)
            taken.Add(e);
        Assert.Equal(DisplayList.MaxDisplayElements - 8, taken.Count);
        var ship = battle.Fleet(0)[0].Ship!;
        var block = new MissileBlock
        {
            Cx = ship.Current.X, Cy = ship.Current.Y, FrameArray = ship.Current.FrameArray!, Speed = 10, Life = 5,
            Damage = 1, HitPoints = 1,
        };
        Assert.Null(battle.InitializeMissile(block));
        Assert.Null(battle.InitializeLaser(new LaserBlock { Cx = ship.Current.X, Cy = ship.Current.Y }));
    }

    // A battle on sheets of our own: solid squares.
    private static Battle NewBattle(uint seed, List<string>? places)
    {
        static Sheet Solid(string id, int count, int size) => new(id, count,
            Enumerable.Range(0, count).Select(_ => new IntersectFrame(size, size, size / 2, size / 2,
                Enumerable.Repeat((byte)1, size * size).ToArray())).ToList());
        static Views Three(Sheet big) => new(big, new Sheet(big.Id + ".medium", big.Count), new Sheet(big.Id + ".small", big.Count));
        var arena = new BattleArena(new Views(Solid("stars", 3, 1)), Three(Solid("asteroid", 21, 10)),
            Three(Solid("blast", 8, 5)), Three(Solid("boom", 9, 3)), Three(Solid("planet", 1, 60)));
        var code = new SquareShip(Three(Solid("hull", 16, 20)), Three(Solid("shot", 8, 4)));
        return new Battle(arena, new[]
        {
            (IReadOnlyList<Starship>)new[] { new Starship(code, 0, 10, 10) },
            new[] { new Starship(code, 1, 10, 10) },
        }, seed, randomPlaces: places);
    }

    private sealed class SquareShip : IShipCode
    {
        private readonly Views _hull, _shot;

        public SquareShip(Views hull, Views shot) => (_hull, _shot) = (hull, shot);

        public RaceDesc Load(Battle battle, Starship starship) => new()
        {
            CrewLevel = 10, MaxCrew = 10, EnergyLevel = 10, MaxEnergy = 10,
            Characteristics = new Characteristics { MaxThrust = 20, ThrustIncrement = 4, ShipMass = 5, TurnWait = 1 },
            Ship = _hull, Weapon = _shot, InitWeapon = _ => Array.Empty<Element?>(),
        };
    }
}
