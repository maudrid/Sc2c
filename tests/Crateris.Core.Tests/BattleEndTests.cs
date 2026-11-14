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
/// A battle's end and a ship leaving it at once (stage 4a item 6), without
/// the content, on sheets of our own: UninitShips handing the floating crew
/// to the ship left (UQM's) or, with several left, to each in turn; and
/// <see cref="Battle.Leave"/>, a disconnect's leaving.
/// </summary>
[Trait("Topic", "combat")]
public class BattleEndTests
{
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly Views Hull = Three(Solid("hull", 16, 20)), Shot = Three(Solid("shot", 24, 4));

    [Fact]
    public void TheShipLeftTakesTheFloatingCrewUpToItsMost()
    {
        // As UQM's: all of it, to the one ship left with crew.
        var (battle, players, enemy) = Fought(1);
        battle.AbandonShip(enemy.Ship!, players[0].Ship!, 5);
        Assert.Equal(5, Floating(battle));
        battle.DoDamage(enemy.Ship!, (short)enemy.RaceDesc!.CrewLevel);
        battle.UninitShips();
        Assert.Equal(new SisBattleResult(30 + 5, false), Sis(players[0]).Result);
        Assert.Equal((ushort)36, players[0].CrewLevel);   // the captain counted
        Assert.Equal((ushort)0, enemy.CrewLevel);
        Assert.All(battle.Fleet(0).Concat(battle.Fleet(1)), s => Assert.Null(s.RaceDesc));
        Assert.False(battle.InBattle);

        // Up to its most: a crew pod's 50 and the captain.
        (battle, players, enemy) = Fought(1, crew: 48);
        battle.AbandonShip(enemy.Ship!, players[0].Ship!, 5);
        battle.DoDamage(enemy.Ship!, (short)enemy.RaceDesc!.CrewLevel);
        battle.UninitShips();
        Assert.Equal(new SisBattleResult(50, false), Sis(players[0]).Result);
    }

    [Fact]
    public void SeveralShipsLeftShareTheFloatingCrewInTurn()
    {
        // Three players left: 7 crew dealt one at a time in the order they
        // joined, 3, 2 and 2, each counted once.
        var (battle, players, enemy) = Fought(3);
        battle.AbandonShip(enemy.Ship!, players[2].Ship!, 7);
        battle.DoDamage(enemy.Ship!, (short)enemy.RaceDesc!.CrewLevel);
        battle.UninitShips();
        Assert.Equal(new[] { 33, 32, 32 }, players.Select(p => Sis(p).Result!.Value.CrewLeft));

        // One with no room takes none; the rest share it.
        (battle, players, enemy) = Fought(3, crew: 50);
        battle.DeltaCrew(players[1].Ship!, -10);
        battle.DeltaCrew(players[2].Ship!, -10);
        battle.AbandonShip(enemy.Ship!, players[0].Ship!, 5);
        battle.DoDamage(enemy.Ship!, (short)enemy.RaceDesc!.CrewLevel);
        battle.UninitShips();
        Assert.Equal(new[] { 50, 43, 42 }, players.Select(p => Sis(p).Result!.Value.CrewLeft));

        // The enemy left with crew too is given all of it, as UQM gives each
        // ship left all of it: each side its own, the players sharing theirs.
        (battle, players, enemy) = Fought(2);
        battle.DeltaCrew(enemy.Ship!, -5);
        battle.AbandonShip(players[1].Ship!, enemy.Ship!, 3);
        battle.UninitShips();
        Assert.Equal(new[] { 32, 28 }, players.Select(p => Sis(p).Result!.Value.CrewLeft));
        Assert.Equal((ushort)20, enemy.CrewLevel);

        // One ship a side left, both with crew: each takes all of it, as UQM's.
        (battle, players, enemy) = Fought(1);
        battle.DeltaCrew(enemy.Ship!, -5);
        battle.AbandonShip(players[0].Ship!, enemy.Ship!, 4);
        battle.UninitShips();
        Assert.Equal(30, Sis(players[0]).Result!.Value.CrewLeft);
        Assert.Equal((ushort)21, enemy.CrewLevel);
    }

    [Fact]
    public void AShipLeavesAtOnceWithItsCrew()
    {
        var (battle, players, enemy) = Fought(2);
        var (leaving, staying) = (players[1], players[0]);
        var element = leaving.Ship!;
        battle.DeltaCrew(element, -4);
        // Floating crew it gave stay; everything else of its goes, and nothing homes in on it.
        battle.AbandonShip(element, staying.Ship!, 2);
        var shot = Missile(battle, leaving);
        enemy.Ship!.Target = element;
        battle.Leave(leaving);
        Assert.Equal((null, null, false), (leaving.Ship, leaving.RaceDesc, leaving.Available));
        Assert.Equal(new SisBattleResult(30 - 4 - 2, false), Sis(leaving).Result);
        Assert.Equal((ushort)25, leaving.CrewLevel);
        Assert.Null(enemy.Ship.Target);
        Assert.Equal(new byte[] { 1, 1 }, battle.BattleCounter);
        Assert.True(battle.InBattle);
        battle.Step(_ => BattleInput.None);
        Assert.DoesNotContain(battle.List.Elements, e => e == element || e == shot);
        Assert.Equal(2, Floating(battle));
        Assert.Same(staying.Ship, battle.FollowedElement(0));
        Assert.Throws<ArgumentException>(() => battle.Join(leaving));   // spent

        // The last player leaving ends it, the enemy left as it is.
        battle.Leave(staying);
        Assert.False(battle.InBattle);
        Assert.Equal(new byte[] { 0, 1 }, battle.BattleCounter);
        Assert.NotNull(enemy.RaceDesc);
        battle.UninitShips();
        Assert.Equal((ushort)22, enemy.CrewLevel);
    }

    [Fact]
    public void AShipRunningAwayLeavesCountedOnce()
    {
        var (battle, players, _) = Fought(2, runAwayAllowed: true);
        battle.Step(s => s == players[1] ? BattleInput.Escape : BattleInput.None);
        Assert.True(battle.RunningAway(players[1]));
        Assert.False(battle.Fighting(players[1]));
        Assert.True(battle.Fighting(players[0]));
        Assert.Equal(1, battle.BattleCounter[0]);
        battle.Leave(players[1]);
        Assert.Equal(1, battle.BattleCounter[0]);
        Assert.True(battle.InBattle);

        // Warping in counts as in it: it leaves too.
        var late = Player();
        Assert.True(battle.Join(late));
        Assert.True((late.Ship!.StateFlags & ElementFlags.Appearing) != 0);
        battle.Leave(late);
        Assert.Equal(1, battle.BattleCounter[0]);
        Assert.Null(late.Ship);
    }

    [Fact]
    public void AShipWarpedOutOrWinningLeavesToo()
    {
        // How long a retreat takes, to the frame its ship is freed (the same battle each time).
        int frames = 0;
        var (battle, players, enemy) = Fought(2, runAwayAllowed: true);
        battle.Step(s => s == players[1] ? BattleInput.Escape : BattleInput.None);
        for (; players[1].RaceDesc != null; ++frames)
            battle.Step(_ => BattleInput.None);

        // Warped out and waiting for new_ship, the frame before: it leaves, counted once.
        (battle, players, enemy) = Fought(2, runAwayAllowed: true);
        battle.Step(s => s == players[1] ? BattleInput.Escape : BattleInput.None);
        for (int i = 0; i < frames - 1; ++i)
            battle.Step(_ => BattleInput.None);
        Assert.True(battle.RunningAway(players[1]));
        Assert.Equal((PrimType.None, 1), (players[1].Ship!.Prim, (int)battle.BattleCounter[0]));
        battle.Leave(players[1]);
        Assert.Equal((1, true), ((int)battle.BattleCounter[0], battle.InBattle));
        Assert.Equal(new SisBattleResult(30, false), Sis(players[1]).Result);

        // The winner, its enemy exploding, leaving: no one is left, and it's over.
        (battle, players, enemy) = Fought(1);
        battle.DoDamage(enemy.Ship!, (short)enemy.RaceDesc!.CrewLevel);
        battle.Step(_ => BattleInput.None);
        Assert.True((players[0].CurStatusFlags & StatusFlags.PlayVictoryDitty) != 0);
        battle.Leave(players[0]);
        Assert.Equal((false, (byte)0, (byte)0), (battle.InBattle, battle.BattleCounter[0], battle.BattleCounter[1]));
        battle.UninitShips();
        Assert.Equal((ushort)0, enemy.CrewLevel);
        Assert.Equal(new SisBattleResult(30, false), Sis(players[0]).Result);
    }

    [Fact]
    public void LeavingIsRefusedDyingDuringAFrameOrNotInIt()
    {
        var (battle, players, enemy) = Fought(2);
        Assert.Throws<ArgumentException>(() => battle.Leave(Player()));
        battle.DoDamage(players[1].Ship!, (short)players[1].RaceDesc!.CrewLevel);
        battle.Step(_ => BattleInput.None);
        Assert.Throws<InvalidOperationException>(() => battle.Leave(players[1]));   // dying: its death stands
        Assert.Throws<InvalidOperationException>(() => battle.Step(s =>
        {
            if (s == enemy)
                battle.Leave(players[0]);
            return BattleInput.None;
        }));
        Assert.True(battle.Fighting(players[0]));
    }

    private static int Floating(Battle battle) => battle.List.Elements.Count(e => (e.StateFlags & ElementFlags.CrewObject) != 0);

    private static SisShip Sis(Starship s) => (SisShip)s.Code;

    // A missile of the ship's, flying north from 400 units south of it.
    private static Element Missile(Battle battle, Starship from)
    {
        var block = new MissileBlock
        {
            Cx = from.Ship!.Current.X, Cy = (short)(from.Ship.Current.Y + 400), Face = 0, Index = 0, Sender = from.PlayerNr,
            Flags = ElementFlags.IgnoreSimilar, Speed = 8, HitPoints = 1, Damage = 2, Life = 100, FrameArray = Shot,
        };
        var e = battle.InitializeMissile(block)!;
        e.Parent = from;
        battle.List.Put(e);
        return e;
    }

    private static (Battle Battle, Starship[] Players, Starship Enemy) Fought(int players, int crew = 30, bool runAwayAllowed = false)
    {
        var all = Enumerable.Range(0, players).Select(_ => Player(crew)).ToArray();
        var enemy = new Starship(new IlwrathShip(Ships[ShipDescriptions.Ilwrath], Hull, Shot, noCloak: true), 1, 22, 22);
        var battle = new Battle(Arena(), new[] { new[] { all[0] }, new[] { enemy } }, 4321, runAwayAllowed: runAwayAllowed,
            size: ArenaSize.Scaled(1.5), maxElements: 300);
        foreach (var p in all.Skip(1))
            Assert.True(battle.Join(p));
        for (int i = 0; i < 200 && !Warped(battle); ++i)
            battle.Step(_ => BattleInput.None);
        Assert.True(Warped(battle));
        return (battle, all, enemy);
    }

    private static bool Warped(Battle battle) => battle.Fleet(0).Concat(battle.Fleet(1)).Where(s => s.Ship != null)
        .All(s => s.Ship!.Prim == PrimType.Stamp && (s.Ship.StateFlags & (ElementFlags.Appearing | ElementFlags.NonSolid)) == 0);

    // A gun in the nose, two thrusters and jets, a crew pod (50 crew and the captain at most).
    private static Starship Player(int crew = 30)
    {
        var modules = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        modules[2] = ShipModules.CrewPod;
        modules[15] = ShipModules.GunWeapon;
        var loadout = new SisLoadout(
            Enumerable.Range(0, ShipModules.DriveSlotCount).Select(i => i < 2 ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, ShipModules.JetSlotCount).Select(i => i < 2 ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            modules, crew);
        return new Starship(new SisShip(Ships[ShipDescriptions.Flagship], Hull, Shot, loadout), 0, 0, 0);
    }

    private static BattleArena Arena() => new(new Views(Solid("stars", 3, 1)), Three(Solid("asteroid", 21, 10)),
        Three(Solid("blast", 8, 5)), Three(Solid("boom", 9, 3)), Three(Solid("planet", 1, 60)));

    internal static Sheet Solid(string id, int count, int size) => new(id, count,
        Enumerable.Range(0, count).Select(_ => new IntersectFrame(size, size, size / 2, size / 2,
            Enumerable.Repeat((byte)1, size * size).ToArray())).ToList());

    // The medium and small sizes the big one's size (their frames' sizes, GetFrameRect's, read by some ships' code).
    internal static Views Three(Sheet big)
    {
        var rects = big.Shapes?.Select(s => new FrameRect(s.Width, s.Height, s.HotSpotX, s.HotSpotY)).ToList();
        return new(big, new Sheet(big.Id + ".medium", big.Count, rects: rects), new Sheet(big.Id + ".small", big.Count, rects: rects));
    }
}
