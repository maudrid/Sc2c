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
/// Several player ships in one battle (stage 4a item 5), without the
/// content, on sheets of our own: four players against the Ilwrath, joining
/// mid-battle, no friendly fire (shots, ships and point defence passing
/// teammates), homing and the AI choosing among the players, the next ship
/// and the winner with several, retreat, the camera following the first
/// player and the enemy, the arena's size and contact across its seam.
/// UQM's traces (BattleEngineTests, ShipBattleTests, BattleAiTests) check
/// that one ship a side in UQM's arena is still UQM's battle.
/// </summary>
[Trait("Topic", "combat")]
public class SeveralShipsTests
{
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly ShipDescription Sis = Ships[ShipDescriptions.Flagship];
    private static readonly ShipDescription Ilwrath = Ships[ShipDescriptions.Ilwrath];

    // A hull 20 pixels square (80 world units), shots 4.
    private static readonly Views Hull = Three(Solid("hull", 16, 20)), Shot = Three(Solid("shot", 16, 4));

    private const int East = 4, West = 12, North = 0;

    [Fact]
    public void FourPlayersFightTheIlwrathTogether()
    {
        var (battle, players, ilwrath) = Fought(4);
        Cleared(battle, players[0], ilwrath);
        Assert.Equal(players, battle.Fleet(0));
        Assert.Equal(new byte[] { 4, 1 }, battle.BattleCounter);
        Assert.Equal(4, players.Select(p => p.Ship).Distinct().Count());

        // Each ship's input is asked for, the players' in the order they joined, then the enemy's.
        var asked = new List<Starship>();
        battle.Step(ship =>
        {
            asked.Add(ship);
            return BattleInput.None;
        });
        Assert.Equal(players.Append(ilwrath), asked);

        // The players die one by one. No teammate takes a dead one's place
        // (each keeps its own element), the enemy wins each death (its
        // victory ditty), and the battle goes on until the last is gone.
        var elements = players.Select(p => p.Ship!).ToArray();
        for (int i = 0; i < players.Length; ++i)
        {
            var dead = players[i];
            battle.DoDamage(dead.Ship!, (short)dead.Ship!.CrewLevel);
            battle.Step(None);
            Assert.True((ilwrath.CurStatusFlags & StatusFlags.PlayVictoryDitty) != 0, "the enemy won");
            Assert.All(players, p => Assert.True((p.CurStatusFlags & StatusFlags.PlayVictoryDitty) == 0));
            for (int frame = 0; frame < 300 && dead.Ship != null && battle.InBattle; ++frame)
                battle.Step(None);
            Assert.Null(dead.Ship);
            Assert.False(dead.Available);
            Assert.Equal(players.Length - 1 - i, battle.BattleCounter[0]);
            for (int j = i + 1; j < players.Length; ++j)
                Assert.Same(elements[j], players[j].Ship);
            Assert.Equal(i < players.Length - 1, battle.InBattle);
        }
    }

    [Fact]
    public void TheWinnerIsTheFirstOfTheOtherSidesShipsAlive()
    {
        var (battle, players, ilwrath) = Fought(4);
        Cleared(battle, players[0], ilwrath);
        // Each ship joining is put at the list's head: the last to join first.
        Assert.Equal(new[] { players[3].Ship, players[2].Ship, players[1].Ship, players[0].Ship, ilwrath.Ship },
            battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) != 0));

        // The player at the list's head dies, and while it explodes the
        // Ilwrath dies: the winner is the first player alive, not the one
        // exploding (UQM takes the first ship, and has no winner if it's dead).
        battle.DoDamage(players[3].Ship!, (short)players[3].Ship!.CrewLevel);
        battle.Step(None);
        battle.DoDamage(ilwrath.Ship!, (short)ilwrath.Ship!.CrewLevel);
        battle.Step(None);
        Assert.Equal(new[] { false, false, true, false },
            players.Select(p => (p.CurStatusFlags & StatusFlags.PlayVictoryDitty) != 0));
        Assert.True((ilwrath.CurStatusFlags & StatusFlags.PlayVictoryDitty) == 0);

        for (int frame = 0; frame < 300 && battle.InBattle; ++frame)
            battle.Step(None);
        Assert.False(battle.InBattle);
        Assert.Equal(new byte[] { 3, 0 }, battle.BattleCounter);
        Assert.All(players.Take(3), p => Assert.NotNull(p.Ship));
    }

    [Fact]
    public void ShotsAndShipsPassThroughTeammates()
    {
        var (battle, players, ilwrath) = Fought(3);
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        var (shooter, teammate) = (players[0], players[1]);
        ushort crew = teammate.RaceDesc!.CrewLevel, enemyCrew = ilwrath.RaceDesc!.CrewLevel;

        // A shot of the first player's flies east through a teammate to the Ilwrath.
        Put(battle, teammate.Ship!, cx, cy);
        var shot = Missile(battle, shooter, cx - 400, cy, East, 32, life: 60);
        for (int frame = 0; frame < 40; ++frame)
            battle.Step(None);
        Assert.Equal(crew, teammate.RaceDesc.CrewLevel);
        Assert.Equal(enemyCrew - 2, ilwrath.RaceDesc.CrewLevel);
        Assert.False(Live(battle, shot), "the shot hit the Ilwrath");

        // Two teammates' shots meet head on, and fly on.
        var east = Missile(battle, shooter, cx - 200, cy + 1200, East, 16, life: 40);
        var west = Missile(battle, teammate, cx + 200, cy + 1200, West, 16, life: 40);
        for (int frame = 0; frame < 20; ++frame)
            battle.Step(None);
        Assert.True(Live(battle, east) && Live(battle, west), "teammates' shots passed through each other");
        Assert.True(east.Current.X > west.Current.X, "they crossed");

        // A teammate flies through the shooter: neither bumps.
        Put(battle, teammate.Ship!, cx - 600, cy + 300);
        teammate.Ship!.Velocity.SetVector(32, North);
        for (int frame = 0; frame < 20; ++frame)
            battle.Step(None);
        Assert.True(teammate.Ship.Current.Y < shooter.Ship!.Current.Y - 160, "the teammate flew on through");
        Assert.Equal((short)0, teammate.Ship.Velocity.Current().dx);
        Assert.True(teammate.Ship.Velocity.Current().dy < 0);
        Assert.True(shooter.Ship.Velocity.IsZero);
        Assert.Equal(crew, teammate.RaceDesc.CrewLevel);
    }

    [Fact]
    public void PointDefenceLeavesTeammatesBe()
    {
        var (battle, players, ilwrath) = Fought(2, loadout: () => Loadout((5, ShipModules.AntimissileDefense)));
        Cleared(battle, players[0], ilwrath);
        var (defender, teammate) = (players[0], players[1]);
        var at = defender.Ship!;
        int range = Battle.DisplayToWorld(Sis.Define("LASER_RANGE"));

        // A teammate and its shot well within the defence's reach: no laser, no energy spent.
        Put(battle, teammate.Ship!, at.Current.X + range / 2, at.Current.Y);
        Missile(battle, teammate, at.Current.X, (short)(at.Current.Y - range / 2), East, 0, life: 30);
        byte energy = defender.RaceDesc!.EnergyLevel;
        battle.Step(ship => ship == defender ? BattleInput.Special : BattleInput.None);
        Assert.Empty(Lasers(battle, defender));
        Assert.Equal(energy, defender.RaceDesc.EnergyLevel);

        // An enemy shot as near: a laser at it, paid for.
        Missile(battle, ilwrath, at.Current.X, (short)(at.Current.Y + range / 2), East, 0, life: 30);
        battle.Step(ship => ship == defender ? BattleInput.Special : BattleInput.None);
        var laser = Assert.Single(Lasers(battle, defender));
        Assert.True(energy > defender.RaceDesc.EnergyLevel);
        Assert.True(laser.Velocity.Current().dy > 0, "towards the enemy's shot, below");
    }

    [Fact]
    public void TheAiChangesTargetAmongThePlayers()
    {
        var (battle, players, ilwrath) = Fought(4);
        Cleared(battle, players[0], ilwrath);
        var ai = ilwrath.Ship!;

        // The nearest player ship by straight-line distance is the enemy.
        int[] dx = { 1500, 0, -2000, 0 }, dy = { 0, 1000, 0, -2500 };
        for (int i = 0; i < 4; ++i)
            Put(battle, players[i].Ship!, ai.Current.X + dx[i], ai.Current.Y + dy[i]);
        Assert.Same(players[1].Ship, Chosen(battle, ilwrath));

        // It moves away: the next nearest.
        Put(battle, players[1].Ship!, ai.Current.X, ai.Current.Y + 3000);
        Assert.Same(players[0].Ship, Chosen(battle, ilwrath));

        // Another comes closer.
        Put(battle, players[3].Ship!, ai.Current.X + 300, ai.Current.Y - 300);
        Assert.Same(players[3].Ship, Chosen(battle, ilwrath));

        // The nearest dies: it's no longer anyone's enemy (non-solid).
        battle.DoDamage(players[3].Ship!, (short)players[3].Ship!.CrewLevel);
        battle.Step(None);
        Assert.Same(players[0].Ship, Chosen(battle, ilwrath));
    }

    [Fact]
    public void TheAiFiresOnlyAtItsNearestEnemy()
    {
        // The enemy a flagship flown by the AI, its gun in the nose, facing
        // north and unable to turn or thrust this frame; one player 600 units
        // behind it, another 800 ahead in its sights. It fires only at its
        // enemy, the nearest (ship_intelligence's ship_weapons is asked about
        // that one ship): not now, and once the nearer has gone far, at the other.
        var enemyCode = new SisShip(Sis, Hull, Shot, Loadout());
        var (battle, players, enemy) = Fought(2, enemy: new Starship(enemyCode, 1, 0, 0));
        Cleared(battle, players[0], enemy);
        var ai = enemy.Ship!;
        enemy.ShipFacing = North;
        ai.Current.Frame = ai.Next.Frame = Hull[0].SetAbs(North);
        Put(battle, players[0].Ship!, ai.Current.X, ai.Current.Y + 600);
        Put(battle, players[1].Ship!, ai.Current.X, ai.Current.Y - 800);
        enemy.WeaponCounter = 0;

        BattleInput Fly()
        {
            ai.TurnWait = ai.ThrustWait = 5;
            enemy.ShipInputState = StatusFlags.None;
            return battle.ComputerIntelligence(enemy);
        }

        Assert.Same(players[0].Ship, Chosen(battle, enemy));
        Assert.Equal(BattleInput.None, Fly() & BattleInput.Weapon);
        Put(battle, players[0].Ship!, ai.Current.X, ai.Current.Y + 3000);
        Assert.Same(players[1].Ship, Chosen(battle, enemy));
        Assert.Equal(BattleInput.Weapon, Fly() & BattleInput.Weapon);
    }

    [Fact]
    public void HomingTakesTheNearestOpposingShipAndKeepsIt()
    {
        var (battle, players, ilwrath) = Fought(4);
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        Put(battle, ilwrath.Ship!, cx, cy);
        // By |dx| + |dy|: 1,000 by 1,000 is 2,000 off; 1,600 straight ahead is nearer.
        int[] dx = { 1000, 1600, -2500, 0 }, dy = { 1000, 0, 0, 2200 };
        for (int i = 0; i < 4; ++i)
            Put(battle, players[i].Ship!, cx + dx[i], cy + dy[i]);

        // The Ilwrath's shot homes in on the nearest player.
        var tracker = Missile(battle, ilwrath, cx, cy, North, 0, life: 30);
        ushort facing = North;
        battle.TrackShip(tracker, ref facing);
        Assert.Same(players[1].Ship, tracker.Target);
        // and keeps it once chosen, though another comes nearer.
        Put(battle, players[2].Ship!, cx - 300, cy);
        battle.TrackShip(tracker, ref facing);
        Assert.Same(players[1].Ship, tracker.Target);

        // A player's shot never homes in on a teammate, however near.
        var own = Missile(battle, players[0], players[0].Ship!.Current.X, players[0].Ship!.Current.Y, North, 0, life: 30);
        facing = North;
        battle.TrackShip(own, ref facing);
        Assert.Same(ilwrath.Ship, own.Target);
    }

    [Fact]
    public void AShipJoinsMidBattle()
    {
        var places = new List<string>();
        var (battle, players, ilwrath) = Fought(1, places: places);
        Assert.Equal(new byte[] { 1, 1 }, battle.BattleCounter);

        // Between frames, it joins: added to its side, counted, warping in
        // at the list's head where spawn_ship puts a ship (its facing, then
        // six numbers a place tried), clear of the planet and every ship.
        var joiner = Player();
        places.Clear();
        Assert.True(battle.Join(joiner));
        Assert.Equal(new[] { players[0], joiner }, battle.Fleet(0));
        Assert.Equal(new byte[] { 2, 1 }, battle.BattleCounter);
        Assert.Same(joiner.Ship, battle.List.Head);
        Assert.Equal(places.Count, places.Count(p => p == "spawn_ship"));
        Assert.Equal(1, places.Count % 6);
        var e = joiner.Ship!;
        foreach (var other in new[] { players[0].Ship!, ilwrath.Ship! })
            Assert.True(Math.Abs(other.Current.X - e.Current.X) >= 80 || Math.Abs(other.Current.Y - e.Current.Y) >= 80,
                "clear of the other ships");
        var planet = battle.List.Elements.Single(p => Battle.GravityMass(p.MassPoints));
        int pdx = Battle.WorldToDisplay(battle.WrapDeltaX(planet.Current.X - e.Current.X));
        int pdy = Battle.WorldToDisplay(battle.WrapDeltaY(planet.Current.Y - e.Current.Y));
        Assert.True(pdx * pdx + pdy * pdy > 255 * 255, "out of the planet's pull");

        battle.Step(None);
        Assert.Equal((PrimType.None, true), (e.Prim, (e.StateFlags & ElementFlags.NonSolid) != 0));
        WarpIn(battle);
        Assert.Equal((PrimType.Stamp, ElementFlags.None), (e.Prim, e.StateFlags & (ElementFlags.NonSolid | ElementFlags.FiniteLife)));

        // Refused: the ship already in, during a frame, and the list full (nothing changed).
        Assert.Throws<ArgumentException>(() => battle.Join(joiner));
        Assert.Throws<InvalidOperationException>(() => battle.Step(ship =>
        {
            battle.Join(Player());
            return BattleInput.None;
        }));
        var taken = new List<Element>();
        while (battle.List.Alloc() is { } slot)
            taken.Add(slot);
        uint seed = battle.Random.Seed;
        var full = Player();
        Assert.False(battle.Join(full));
        Assert.Equal((2, (byte)2, seed), (battle.Fleet(0).Count, battle.BattleCounter[0], battle.Random.Seed));
        Assert.Null(full.RaceDesc);
        foreach (var slot in taken)
            battle.List.Free(slot);

        // A ship its own code refuses (a player ship with crew of its own) changes nothing.
        int count = battle.List.Count;
        seed = battle.Random.Seed;
        var refused = new Starship(new SisShip(Sis, Hull, Shot, Loadout()), 0, 5, 0);
        Assert.ThrowsAny<Exception>(() => battle.Join(refused));
        Assert.Equal((2, (byte)2, seed, count), (battle.Fleet(0).Count, battle.BattleCounter[0], battle.Random.Seed, battle.List.Count));
        Assert.True(refused.Available);
        Assert.Null(refused.RaceDesc);
        Assert.Null(refused.Ship);
        // Nor can a ship of no side join.
        Assert.Throws<ArgumentException>(() => battle.Join(new Starship(new SisShip(Sis, Hull, Shot, Loadout()), 2, 0, 0)));

        // A ship that joins as the side's last one explodes keeps the battle going.
        foreach (var p in battle.Fleet(0))
            battle.DoDamage(p.Ship!, (short)p.Ship!.CrewLevel);
        battle.Step(None);
        var rescuer = Player();
        Assert.True(battle.Join(rescuer));
        for (int frame = 0; frame < 300 && (players[0].Ship != null || joiner.Ship != null); ++frame)
            battle.Step(None);
        Assert.True(battle.InBattle);
        Assert.Equal(new byte[] { 1, 1 }, battle.BattleCounter);
        Assert.NotNull(rescuer.Ship);

        // A spent ship can't join again; nor can any once the battle is over.
        Assert.Throws<ArgumentException>(() => battle.Join(joiner));
        battle.DoDamage(rescuer.Ship!, (short)rescuer.Ship!.CrewLevel);
        for (int frame = 0; frame < 300 && battle.InBattle; ++frame)
            battle.Step(None);
        Assert.False(battle.InBattle);
        Assert.Throws<InvalidOperationException>(() => battle.Join(Player()));
    }

    [Fact]
    public void NoneJoinsABattleDecided()
    {
        // The enemy has no ship left able to fight (its last exploding): the
        // battle is decided, and no player may join, changing nothing.
        var (battle, _, ilwrath) = Fought(1);
        battle.DoDamage(ilwrath.Ship!, (short)ilwrath.Ship!.CrewLevel);
        battle.Step(None);
        Assert.True(battle.InBattle);
        uint seed = battle.Random.Seed;
        int count = battle.List.Count;
        var late = Player();
        Assert.Throws<InvalidOperationException>(() => battle.Join(late));
        Assert.Equal((1, (byte)1, (byte)0, seed, count),
            (battle.Fleet(0).Count, battle.BattleCounter[0], battle.BattleCounter[1], battle.Random.Seed, battle.List.Count));
        Assert.Null(late.RaceDesc);

        // Nor an enemy ship, the players having none left.
        var (lost, players, _) = Fought(1);
        lost.DoDamage(players[0].Ship!, (short)players[0].Ship!.CrewLevel);
        lost.Step(None);
        Assert.Throws<InvalidOperationException>(() => lost.Join(Avenger()));
        Assert.Single(lost.Fleet(1));
    }

    [Fact]
    public void ASideCountsAtMost255Ships()
    {
        var (battle, _, _) = Fought(1, ArenaSize.Scaled(2));
        for (int i = 1; i < byte.MaxValue; ++i)
            Assert.True(battle.Join(Player()));
        Assert.Equal(byte.MaxValue, battle.BattleCounter[0]);
        Assert.Throws<InvalidOperationException>(() => battle.Join(Player()));
        Assert.Equal(byte.MaxValue, battle.Fleet(0).Count);
    }

    [Fact]
    public void OnePlayerRetreatsAndTheOthersFightOn()
    {
        var (battle, players, _) = Fought(3, runAwayAllowed: true);
        battle.Step(ship => ship == players[1] ? BattleInput.Escape : BattleInput.None);
        Assert.Equal(new byte[] { 2, 1 }, battle.BattleCounter);
        for (int frame = 0; frame < 300 && players[1].Ship != null; ++frame)
            battle.Step(None);
        Assert.True(battle.InBattle);
        Assert.Null(players[1].Ship);
        Assert.False(players[1].Available);
        Assert.True(players[1].CrewLevel > 0, "it left with its crew");
        Assert.NotNull(players[0].Ship);
        Assert.NotNull(players[2].Ship);
    }

    [Fact]
    public void TheCameraFollowsTheFirstPlayerFightingAndTheEnemy()
    {
        var (battle, players, ilwrath) = Fought(3, ArenaSize.Scaled(1.5));
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        Put(battle, players[0].Ship!, cx - 2000, cy + 600);
        Put(battle, ilwrath.Ship!, cx - 1200, cy + 600);
        Put(battle, players[1].Ship!, cx + 1500, cy - 2500);
        Put(battle, players[2].Ship!, cx + 3000, cy + 2000);
        Assert.Equal(new[] { true, false, false, true },
            players.Append(ilwrath).Select(p => battle.Followed(p.Ship!)));

        // The frame moves so the two it follows are about the arena's centre.
        battle.Step(None);
        AssertCentred(battle, players[0], ilwrath, cx, cy);

        // The first player dies: the camera follows the next still fighting.
        battle.DoDamage(players[0].Ship!, (short)players[0].Ship!.CrewLevel);
        battle.Step(None);
        Assert.Equal(new[] { false, true, false, true },
            players.Append(ilwrath).Select(p => battle.Followed(p.Ship!)));
        battle.Step(None);
        AssertCentred(battle, players[1], ilwrath, cx, cy);
    }

    // The two ships' midpoint (the shorter way) about the arena's centre.
    private static void AssertCentred(Battle battle, Starship a, Starship b, int cx, int cy)
    {
        int midX = a.Ship!.Current.X + battle.WrapDeltaX(b.Ship!.Current.X - a.Ship.Current.X) / 2;
        int midY = a.Ship.Current.Y + battle.WrapDeltaY(b.Ship.Current.Y - a.Ship.Current.Y) / 2;
        Assert.InRange(midX, cx - 8, cx + 8);
        Assert.InRange(midY, cy - 8, cy + 8);
    }

    [Fact]
    public void TheCameraFollowsAPlayerKilledMidFrameToTheFramesEnd()
    {
        // The first player, after a teammate who joined later in the list,
        // is killed by a shot in the frame's collisions: the camera follows
        // it to the frame's end (not the enemy alone, which would slow its
        // scroll and zoom it in), then the teammate.
        var (battle, players, ilwrath) = Fought(2, ArenaSize.Scaled(1.5));
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        var first = players[0];
        first.Ship!.CrewLevel = 1;
        first.RaceDesc!.CrewLevel = 1;
        Missile(battle, ilwrath, cx - 400, cy, West, 32, life: 30);
        int zoom = battle.ZoomOut;
        for (int frame = 0; frame < 20 && first.RaceDesc!.CrewLevel != 0; ++frame)
        {
            battle.Step(None);
            Assert.Equal((0, 0), ((int)battle.ScrollX, (int)battle.ScrollY));
            Assert.Equal(zoom, battle.ZoomOut);
        }
        Assert.Equal(0, first.RaceDesc!.CrewLevel);
        Assert.Same(players[1].Ship, battle.FollowedElement(0));
        battle.Step(None);
        AssertCentred(battle, players[1], ilwrath, cx, cy);
    }

    [Fact]
    public void TheCameraLeavesAPlayerRetreating()
    {
        // The first player retreats: from then the camera follows the
        // teammate still fighting, never no player, until the first is gone.
        var (battle, players, ilwrath) = Fought(2, ArenaSize.Scaled(1.5), runAwayAllowed: true);
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        battle.Step(ship => ship == players[0] ? BattleInput.Escape : BattleInput.None);
        Assert.Same(players[1].Ship, battle.FollowedElement(0));
        battle.Step(None);
        AssertCentred(battle, players[1], ilwrath, cx, cy);
        for (int frame = 0; frame < 300 && players[0].Ship != null; ++frame)
        {
            battle.Step(None);
            Assert.Same(players[1].Ship, battle.FollowedElement(0));
            Assert.Equal((0, 0), ((int)battle.ScrollX, (int)battle.ScrollY));
        }
        Assert.Null(players[0].Ship);
    }

    [Fact]
    public void TheCameraStaysOnALastPlayerDyingAndLeavesOneGone()
    {
        // The first player dies, then the second as the first explodes: the
        // camera stays on the second (the one it followed), not back to the
        // first, and follows a player every frame until the second's explosion
        // ends; the first, once gone (its element no longer a ship's, waiting
        // for the enemy's victory ditty), is never followed again.
        var (battle, players, ilwrath) = Fought(2, ArenaSize.Scaled(1.5));
        Cleared(battle, players[0], ilwrath);
        var (first, second) = (players[0], players[1]);
        battle.DoDamage(first.Ship!, (short)first.Ship!.CrewLevel);
        battle.Step(None);
        battle.Step(None);
        Assert.Same(second.Ship, battle.FollowedElement(0));
        var element = second.Ship!;
        battle.DoDamage(element, (short)element.CrewLevel);
        int frames = 0;
        for (; frames < 300 && !battle.LeavesThisFrame(element); ++frames)
        {
            Assert.Same(element, battle.FollowedElement(0));
            battle.Step(None);
            Assert.Equal(2, battle.ShipsFollowed);
        }
        Assert.True(frames >= Battle.NumExplosionFrames * 3, $"followed through its explosion, not {frames} frames");
        // Its last frame as a ship: none of the side's followed, as UQM's.
        Assert.Null(battle.FollowedElement(0));
        Assert.True(first.Ship == null || (first.Ship.StateFlags & ElementFlags.PlayerShip) == 0);
    }

    [Fact]
    public void TheCameraKeepsToASideThroughItsFollowedShipsLastFrame()
    {
        // A second player retreats; the first, followed, is killed. In the
        // frame its explosion ends (its element stops being a ship's), the
        // camera follows the second, still flashing, not nothing of the side.
        var (battle, players, ilwrath) = Fought(2, ArenaSize.Scaled(1.5), runAwayAllowed: true);
        Cleared(battle, players[0], ilwrath);
        var (first, second) = (players[0], players[1]);
        battle.Step(ship => ship == second ? BattleInput.Escape : BattleInput.None);
        battle.DoDamage(first.Ship!, (short)first.Ship!.CrewLevel);
        var element = first.Ship!;
        for (int frame = 0; frame < 100 && !battle.LeavesThisFrame(element); ++frame)
        {
            battle.Step(None);
            Assert.Equal(2, battle.ShipsFollowed);
        }
        Assert.True(battle.LeavesThisFrame(element));
        Assert.Same(second.Ship, battle.FollowedElement(0));
        battle.Step(None);
        Assert.Equal(2, battle.ShipsFollowed);
        Assert.Equal((ElementFlags)0, element.StateFlags & ElementFlags.PlayerShip);
        Assert.True(battle.InBattle);
    }

    [Fact]
    public void TheCameraNeverFollowsAShipsElementDoneWith()
    {
        // Three players. The first dies and its explosion ends (its element
        // waits on for the enemy's victory ditty, no longer a ship's); the
        // third retreats; the second, followed, dies. In the frame its
        // explosion ends, the camera follows the third, not the first's element.
        var (battle, players, ilwrath) = Fought(3, ArenaSize.Scaled(1.5), runAwayAllowed: true);
        Cleared(battle, players[0], ilwrath);
        var (first, second, third) = (players[0], players[1], players[2]);
        battle.DoDamage(first.Ship!, (short)first.Ship!.CrewLevel);
        for (int frame = 0; frame < 100 && (first.Ship!.StateFlags & ElementFlags.PlayerShip) != 0; ++frame)
            battle.Step(None);
        Assert.NotNull(first.Ship);
        Assert.Equal((ElementFlags)0, first.Ship!.StateFlags & ElementFlags.PlayerShip);
        battle.Step(ship => ship == third ? BattleInput.Escape : BattleInput.None);
        Assert.Same(second.Ship, battle.FollowedElement(0));
        var element = second.Ship!;
        battle.DoDamage(element, (short)element.CrewLevel);
        for (int frame = 0; frame < 100 && !battle.LeavesThisFrame(element); ++frame)
        {
            battle.Step(None);
            Assert.Equal(2, battle.ShipsFollowed);
        }
        Assert.NotNull(first.Ship);
        Assert.Same(third.Ship, battle.FollowedElement(0));
        battle.Step(None);
        Assert.Equal(2, battle.ShipsFollowed);
    }

    [Fact]
    public void AnArenaThreeTimesUqmsRuns()
    {
        // The biggest arena: ships flying and firing about it, the AI after
        // them, every place within it (lines may end past an edge for a frame).
        var size = ArenaSize.Scaled(ArenaSize.MaxScale);
        int shots = 0, crewLost = 0;
        foreach (uint seed in new uint[] { 3, 31 })
        {
            var battle = NewBattle(seed, size, maxElements: 300);
            for (int i = 0; i < 2; ++i)
                Assert.True(battle.Join(Player()));
            var ships = battle.Fleet(0).Concat(battle.Fleet(1)).ToList();
            int crew = ships.Sum(s => s.RaceDesc!.CrewLevel);
            for (int frame = 0; frame < 600 && battle.InBattle; ++frame)
            {
                battle.Step(ship => ship.PlayerNr == 1 ? battle.ComputerIntelligence(ship)
                    : BattleInput.Thrust | BattleInput.Weapon | (frame / 20 % 3 == 0 ? BattleInput.Left : BattleInput.None));
                foreach (var e in battle.List.Elements)
                {
                    int slack = e.Prim == PrimType.Line ? size.Width : 0;
                    Assert.InRange(e.Current.X, -slack, size.Width - 1 + slack);
                    Assert.InRange(e.Current.Y, -slack, size.Height - 1 + slack);
                }
                shots += battle.List.Elements.Count(e => e.Parent != null && (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                          && e.Prim == PrimType.Stamp);
            }
            crewLost += crew - ships.Sum(s => s.RaceDesc?.CrewLevel ?? s.CrewLevel);
        }
        Assert.True(shots > 50, $"only {shots} frames of shots");
        Assert.True(crewLost > 0, "nothing was hit");
    }

    [Fact]
    public void TheArenasSizeIsASetting()
    {
        Assert.Equal((8192, 7680), (ArenaSize.Uqm.Width, ArenaSize.Uqm.Height));
        Assert.Equal(ArenaSize.Uqm, ArenaSize.Scaled(1));
        var size = ArenaSize.Scaled(1.5);
        Assert.Equal((12288, 11520), (size.Width, size.Height));
        Assert.Equal((24576, 23040), (ArenaSize.Scaled(3).Width, ArenaSize.Scaled(3).Height));
        foreach (double scale in new[] { 0.99, 3.01, double.NaN })
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaSize.Scaled(scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArenaSize(8184, 7680));
        Assert.Throws<ArgumentException>(() => new ArenaSize(8192, 7684));

        bool farOut = false;
        for (uint seed = 1; seed <= 10; ++seed)
        {
            var battle = NewBattle(seed, size, maxElements: 300);
            // The random places (the ships', the planet's, the asteroids' off
            // the seam, and a ship joining) reach past UQM's arena.
            Assert.True(battle.Join(Player()));
            farOut |= battle.List.Elements.Any(a => (a.Current.X >= Battle.LogSpaceWidth && a.Current.X != size.Width)
                                                  || (a.Current.Y >= Battle.LogSpaceHeight && a.Current.Y != size.Height));
            Assert.True(battle.AcrossSeam);
            Assert.Equal(300, battle.List.Capacity);
            // The asteroids start on the arena's seam.
            Assert.All(battle.List.Elements.Where(a => a.MassPoints == 3), a => Assert.True(
                a.Current.X is 0 || a.Current.X == size.Width || a.Current.Y is 0 || a.Current.Y == size.Height,
                "an asteroid starts on the seam"));
            for (int frame = 0; frame < 60; ++frame)
            {
                battle.Step(None);
                Assert.All(battle.List.Elements.Where(a => a.Prim != PrimType.Line), a =>
                {
                    Assert.InRange(a.Current.X, 0, size.Width - 1);
                    Assert.InRange(a.Current.Y, 0, size.Height - 1);
                });
            }
            // The view's centre is the arena's; its size UQM's (MAX_ZOOM_OUT 1024: 8,192 * zoom / 4,096).
            Assert.Equal(Battle.DisplayAlign(size.Width / 2 - Battle.LogSpaceWidth * battle.ZoomOut / 4096), battle.SpaceOrgX);
            Assert.Equal(Battle.DisplayAlign(size.Height / 2 - Battle.LogSpaceHeight * battle.ZoomOut / 4096), battle.SpaceOrgY);
        }
        Assert.True(farOut, "places beyond UQM's arena");

        var uqms = NewBattle(1, null);
        Assert.False(uqms.AcrossSeam);
        Assert.Equal(DisplayList.MaxDisplayElements, uqms.List.Capacity);
        Assert.Equal(ArenaSize.Uqm, uqms.Size);
        var big = NewBattle(1, size);
        Assert.Equal((0, 12284, 12286), (big.WrapX(size.Width), big.WrapX(-4), big.WrapX(12286)));
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.5, true)]
    public void ContactIsTestedAcrossTheSeamInABiggerArena(double scale, bool across)
    {
        var size = ArenaSize.Scaled(scale);
        var (battle, players, ilwrath) = Fought(2, size, loadout: () => Loadout((5, ShipModules.AntimissileDefense)));
        var (_, cy) = Cleared(battle, players[0], ilwrath);
        int w = size.Width;
        var escort = players[1];
        Assert.False(battle.Followed(escort.Ship!));
        Assert.Equal(across, battle.AcrossSeam);

        // A still shot of the Ilwrath's touching the escort across the seam.
        ushort crew = escort.RaceDesc!.CrewLevel;
        Put(battle, escort.Ship!, w - 16, cy);
        Missile(battle, ilwrath, 16 + 0, (short)cy, East, 0, life: 30);
        for (int frame = 0; frame < 3; ++frame)
            battle.Step(None);
        Assert.Equal(across ? crew - 2 : crew, escort.RaceDesc.CrewLevel);
        Assert.Equal(0, battle.ScrollX);

        // Point defence reaches across it (the arena's height: its width,
        // 12,288, is 6 times 2,048, and a laser's 16-bit velocity would hide
        // an aim off by it).
        int h = size.Height, x = w / 2 - 3000;
        Put(battle, escort.Ship!, x, h - 200);
        Missile(battle, ilwrath, x, 100, East, 0, life: 30);
        battle.Step(ship => ship == escort ? BattleInput.Special : BattleInput.None);
        Assert.Equal(across ? 1 : 0, Lasers(battle, escort).Count);
        if (across)
        {
            // Aimed the shorter way: it ends at the shot, 300 units south over the seam.
            var laser = Lasers(battle, escort)[0];
            Assert.InRange(battle.WrapDeltaX(laser.Current.X - x), -16, 16);
            Assert.InRange(battle.WrapDeltaY(laser.Current.Y - 100), -16, 16);
        }

        // A second Ilwrath, not the one the camera follows, and the escort
        // flying east into it across the seam: they bump.
        var second = Avenger();
        Assert.True(battle.Join(second));
        WarpIn(battle);
        Assert.False(battle.Followed(second.Ship!));
        Put(battle, second.Ship!, 20, cy - 1500);
        Put(battle, escort.Ship!, w - 64, cy - 1500);
        escort.Ship!.Velocity.SetVector(8, East);
        var flying = escort.Ship.Velocity.Current();
        for (int frame = 0; frame < 3; ++frame)
            battle.Step(None);
        Assert.Equal(across, escort.Ship.Velocity.Current() != flying);
        if (across)
        {
            // Each pushed away from the other the shorter way: the escort
            // back west, the Ilwrath east (collide's angle between them).
            Assert.True(escort.Ship.Velocity.Current().dx < flying.dx);
            var pushed = second.Ship!.Velocity.Current();
            Assert.True(pushed.dx > 0 && pushed.dy == 0, $"the Ilwrath pushed east, not ({pushed.dx}, {pushed.dy})");
        }
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.5, true)]
    public void TheIlwrathUncloakingAimsAcrossTheSeamInABiggerArena(double scale, bool across)
    {
        // A second Ilwrath (one the camera doesn't follow) cloaks, then fires
        // from black at a player 400 units west of it over the seam: it
        // turns to where its target will be (ilwrath_preprocess), the shorter way.
        var size = ArenaSize.Scaled(scale);
        var (battle, players, ilwrath) = Fought(2, size);
        var (_, cy) = Cleared(battle, players[0], ilwrath);
        var second = Avenger();
        Assert.True(battle.Join(second));
        WarpIn(battle);
        Put(battle, second.Ship!, 100, cy + 2000);
        Put(battle, players[1].Ship!, size.Width - 300, cy + 2000);
        battle.Step(ship => ship == second ? BattleInput.Special : BattleInput.None);
        for (int frame = 0; frame < 100 && !Battle.ObjectCloaked(second.Ship!); ++frame)
            battle.Step(None);
        Assert.True(Battle.ObjectCloaked(second.Ship!));
        battle.Step(ship => ship == second ? BattleInput.Weapon : BattleInput.None);
        Assert.Equal(across ? West : East, second.ShipFacing);
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.5, true)]
    public void TheAiSpawningAndFloatingCrewMeasureAcrossTheSeamInABiggerArena(double scale, bool across)
    {
        var size = ArenaSize.Scaled(scale);
        var tracking = Loadout((4, ShipModules.TrackingSystem));
        var enemyCode = new SisShip(Sis, Hull, Shot, tracking);
        var (battle, players, enemy) = Fought(2, size, enemy: new Starship(enemyCode, 1, 0, 0));
        var (cx, cy) = Cleared(battle, players[0], enemy);
        int w = size.Width, h = size.Height;
        var (player, ai) = (players[0].Ship!, enemy.Ship!);

        // The AI's plotting: a player's shot 400 units west of the enemy
        // across the seam, flying east at it, will hit it (PlotIntercept).
        Put(battle, ai, 200, cy);
        var shot = Missile(battle, players[0], w - 200, cy, East, 32, life: 30);
        var velocity = shot.Velocity;
        Put(battle, shot, shot.Current.X, shot.Current.Y);
        shot.Velocity = velocity;
        shot.Intersect.Frame = shot.Current.Frame.Shape;
        Assert.Equal(across, battle.PlotIntercept(shot, ai, 30, 0) != 0);
        Assert.Equal(across, battle.PlotIntercept(shot, ai, 30, (ushort)Battle.DisplayToWorld(20)) != 0);
        battle.List.Remove(shot);
        battle.List.Free(shot);

        // The flagship's AI fires its tracking shots at an enemy its way
        // across the seam: the enemy facing north, the player 400 units
        // north of it over the seam and 150 east, out of its gun's line
        // (ship_weapons finds no hit) but within its octant.
        enemy.ShipFacing = North;
        ai.Current.Frame = ai.Next.Frame = Hull[0].SetAbs(North);
        Put(battle, ai, cx, 100);
        Put(battle, player, cx + 150, h - 300);
        enemy.WeaponCounter = 0;
        ai.TurnWait = ai.ThrustWait = 5;
        enemy.ShipInputState = StatusFlags.None;
        Assert.Same(player, Chosen(battle, enemy));
        Assert.False(battle.ShipWeapons(ai, player, 0), "out of its gun's line");
        Assert.Equal(across, (battle.ComputerIntelligence(enemy) & BattleInput.Weapon) != 0);

        // Spawning keeps clear of a ship across the seam (TimeSpaceMatterConflict).
        Put(battle, ai, 20, cy);
        Put(battle, player, w - 20, cy);
        Assert.Equal(across, battle.TimeSpaceMatterConflict(player));

        // Crew floating from a ship going nowhere towards one across the seam
        // set out on the side facing it (AbandonShip): east of the player,
        // the target 400 units east over the seam.
        Put(battle, player, w - 200, cy);
        player.Velocity.SetComponents(0, 0);   // set to nothing: no way of travel (FULL_CIRCLE)
        Put(battle, ai, 200, cy);
        battle.AbandonShip(player, ai, 1);
        var crew = battle.List.Elements.Single(e => (e.StateFlags & ElementFlags.CrewObject) != 0);
        Assert.Equal(across, crew.Current.X > player.Current.X);
    }

    [Fact]
    public void PlotInterceptAcrossTheSeamOfTheBiggestArena()
    {
        // A player's shot near the arena's east edge flying east, the Ilwrath
        // 8,788 units east of it over the seam: moved by the arena's size it
        // is past a COORD's 32,767, and the AI still sees the hit coming.
        var size = ArenaSize.Scaled(ArenaSize.MaxScale);
        var (battle, players, ilwrath) = Fought(1, size);
        var (_, cy) = Cleared(battle, players[0], ilwrath);
        foreach (int shotX in new[] { 24000, 18000 })
        {
            var ai = ilwrath.Ship!;
            Put(battle, ai, shotX + 8788 - size.Width, cy);
            var shot = Steady(battle, Missile(battle, players[0], shotX, cy, East, 96, life: 200), shotX, cy);
            Assert.NotEqual(0, battle.PlotIntercept(shot, ai, 200, 0));
            Assert.NotEqual(0, battle.PlotIntercept(shot, ai, 200, (ushort)Battle.DisplayToWorld(20)));
            battle.List.Remove(shot);
            battle.List.Free(shot);
        }
    }

    [Fact]
    public void GravityHomingAndCrewMeasureTheBiggerArenasWayRound()
    {
        // In an arena 1.5 times UQM's (12,288 wide) the shorter way round is
        // the arena's, not UQM's 8,192 wrap.
        var size = ArenaSize.Scaled(1.5);
        var (battle, players, ilwrath) = Fought(3, size);
        var (_, cy) = Cleared(battle, players[0], ilwrath, keep: e => Battle.GravityMass(e.MassPoints));
        var planet = battle.List.Elements.Single(e => Battle.GravityMass(e.MassPoints));

        // The planet pulls a ship 800 units west of it over the seam, not
        // one 7,692 east (4,596 west the arena's way round, 500 UQM's).
        int y = cy + 3000;
        Put(battle, planet, 300, y);
        Put(battle, players[1].Ship!, 300 - 800, y);
        Put(battle, players[2].Ship!, 300 + 7692, y);
        battle.Step(None);
        Assert.False(players[1].Ship!.Velocity.IsZero, "pulled across the seam");
        Assert.True(players[2].Ship!.Velocity.IsZero, "too far the arena's way round");
        battle.List.Remove(planet);
        battle.List.Free(planet);

        // The Ilwrath's shot homes in on the ship 4,000 east, not the one
        // 5,000 east (3,192 west UQM's way round).
        Put(battle, players[1].Ship!, 500 + 5000, y);
        Put(battle, players[2].Ship!, 500 + 4000, y);
        var tracker = Missile(battle, ilwrath, 500, y, North, 0, life: 30);
        ushort facing = North;
        battle.TrackShip(tracker, ref facing);
        Assert.Same(players[2].Ship, tracker.Target);
        Assert.Equal(1, facing);   // turning east
        battle.List.Remove(tracker);
        battle.List.Free(tracker);

        // Crew floating 5,000 west of the Ilwrath drift east to it, 4 units a
        // frame (but their first, as they appear).
        battle.AbandonShip(players[1].Ship!, ilwrath.Ship!, 1);
        var crew = battle.List.Elements.Single(e => (e.StateFlags & ElementFlags.CrewObject) != 0);
        var at = ilwrath.Ship!;
        Put(battle, crew, at.Current.X - 5000, at.Current.Y + 400);
        for (int frame = 0; frame < 3; ++frame)
            battle.Step(None);
        Assert.Equal(5000 - 2 * 4, battle.WrapDeltaX(at.Current.X - crew.Current.X));
        Assert.Equal(400 - 2 * 4, battle.WrapDeltaY(crew.Current.Y - at.Current.Y));
    }

    [Fact]
    public void TheCameraCentresShipsFarApartInTheBiggerArena()
    {
        // The two it follows 5,000 apart (more than half UQM's arena, less
        // than half this one): their midpoint the shorter way is centred.
        var size = ArenaSize.Scaled(1.5);
        var (battle, players, ilwrath) = Fought(1, size);
        var (cx, cy) = Cleared(battle, players[0], ilwrath);
        Put(battle, players[0].Ship!, cx - 2500, cy);
        Put(battle, ilwrath.Ship!, cx + 2500, cy);
        battle.Step(None);
        AssertCentred(battle, players[0], ilwrath, cx, cy);

        // Their midpoint 5,000 from the centre: the frame scrolls 5,000.
        Put(battle, players[0].Ship!, cx + 5000 - 300, cy + 500);
        Put(battle, ilwrath.Ship!, cx + 5000 + 300, cy + 500);
        battle.Step(None);
        Assert.Equal(-5000, battle.ScrollX);
        AssertCentred(battle, players[0], ilwrath, cx, cy);
    }

    [Fact]
    public void FloatingCrewStillMeetsAsteroids()
    {
        // Crew (neutral) and an asteroid (neutral) aren't teammates: the crew
        // hit the asteroid and are gone, as in UQM.
        var (battle, players, ilwrath) = Fought(2);
        var (_, cy) = Cleared(battle, players[0], ilwrath, keep: e => e.MassPoints == 3);
        var asteroid = battle.List.Elements.First(e => e.MassPoints == 3);
        Put(battle, asteroid, 2000, cy + 3000);
        battle.AbandonShip(players[1].Ship!, ilwrath.Ship!, 1);
        var crew = battle.List.Elements.Single(e => (e.StateFlags & ElementFlags.CrewObject) != 0);
        Put(battle, crew, 2000, cy + 2000);
        battle.Step(None);
        Assert.Contains(crew, battle.List.Elements);
        Put(battle, crew, asteroid.Current.X, asteroid.Current.Y);
        battle.Step(None);
        Assert.DoesNotContain(crew, battle.List.Elements);
        Assert.Contains(asteroid, battle.List.Elements);
    }

    // ---- The battle ----

    // A battle of the flagship (or a player ship of the loadout given)
    // against the Ilwrath (or the enemy given), the other players joining
    // before the first frame, run until every ship has warped in.
    private static (Battle Battle, Starship[] Players, Starship Enemy) Fought(int players, ArenaSize? size = null,
        bool runAwayAllowed = false, Func<SisLoadout>? loadout = null, Starship? enemy = null, List<string>? places = null)
    {
        var first = Player(loadout?.Invoke());
        enemy ??= Avenger();
        var battle = new Battle(Arena(), new[] { new[] { first }, new[] { enemy } }, 777,
            runAwayAllowed: runAwayAllowed, randomPlaces: places, size: size, maxElements: 300);
        var all = new List<Starship> { first };
        for (int i = 1; i < players; ++i)
        {
            var p = Player(loadout?.Invoke());
            Assert.True(battle.Join(p));
            all.Add(p);
        }
        WarpIn(battle);
        return (battle, all.ToArray(), enemy);
    }

    private static Battle NewBattle(uint seed, ArenaSize? size, int maxElements = DisplayList.MaxDisplayElements) =>
        new(Arena(), new[] { new[] { Player() }, new[] { Avenger() } }, seed, size: size, maxElements: maxElements);

    private static BattleArena Arena() => new(new Views(Solid("stars", 3, 1)), Three(Solid("asteroid", 21, 10)),
        Three(Solid("blast", 8, 5)), Three(Solid("boom", 9, 3)), Three(Solid("planet", 1, 60)));

    private static Starship Player(SisLoadout? loadout = null) => new(new SisShip(Sis, Hull, Shot, loadout ?? Loadout()), 0, 0, 0);

    private static Starship Avenger() => new(new IlwrathShip(Ilwrath, Hull, Shot, noCloak: true), 1, 22, 0) { CaptainsNameIndex = 5 };

    // A gun in the nose, two thrusters and jets, a crew pod, and any modules more.
    private static SisLoadout Loadout(params (int Slot, byte Module)[] more)
    {
        var modules = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        modules[2] = ShipModules.CrewPod;
        modules[15] = ShipModules.GunWeapon;
        foreach (var (slot, module) in more)
            modules[slot] = module;
        return new SisLoadout(
            Enumerable.Range(0, ShipModules.DriveSlotCount).Select(i => i < 2 ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, ShipModules.JetSlotCount).Select(i => i < 2 ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            modules, 30);
    }

    private static BattleInput None(Starship ship) => BattleInput.None;

    // Every ship with an element drawn and solid.
    private static void WarpIn(Battle battle)
    {
        bool Warped() => Enumerable.Range(0, Battle.NumSides).SelectMany(battle.Fleet).Where(s => s.Ship != null)
            .All(s => s.Ship!.Prim == PrimType.Stamp && (s.Ship.StateFlags & (ElementFlags.Appearing | ElementFlags.NonSolid)) == 0);
        for (int i = 0; i < 200 && !Warped(); ++i)
            battle.Step(None);
        Assert.True(Warped(), "every ship warped in");
    }

    // The planet and asteroids taken out; the two ships the camera follows
    // put still either side of the arena's centre, so the frame stays where
    // it is. Returns the centre.
    private static (int X, int Y) Cleared(Battle battle, Starship first, Starship enemy, Func<Element, bool>? keep = null)
    {
        bool kept = false;
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0).ToList())
        {
            if (!kept && keep?.Invoke(e) == true)
            {
                kept = true;
                continue;
            }
            battle.List.Remove(e);
            battle.List.Free(e);
        }
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, first.Ship!, cx - 600, cy);
        Put(battle, enemy.Ship!, cx + 600, cy);
        foreach (var s in battle.Fleet(0).Concat(battle.Fleet(1)).Where(s => s.Ship != null && s != first && s != enemy))
            Put(battle, s.Ship!, cx - 600, cy - 2400 + 400 * battle.Fleet(0).ToList().IndexOf(s));
        battle.Step(None);
        Assert.Equal((0, 0), ((int)battle.ScrollX, (int)battle.ScrollY));
        return (cx, cy);
    }

    // A shot put still at a place, then flying on as it was, its shape set
    // (as the AI's ship_weapons sets up a shot just made).
    private static Element Steady(Battle battle, Element shot, int x, int y)
    {
        var velocity = shot.Velocity;
        Put(battle, shot, x, y);
        shot.Velocity = velocity;
        shot.Intersect.Frame = shot.Current.Frame.Shape;
        return shot;
    }

    // An element put still at a place (world units), taken round the arena,
    // its contact test's start and end there too.
    private static void Put(Battle battle, Element e, int x, int y)
    {
        e.Current.X = e.Next.X = (short)battle.WrapX(x);
        e.Current.Y = e.Next.Y = (short)battle.WrapY(y);
        e.Intersect.OriginX = e.Intersect.EndX = Battle.WorldToDisplay(e.Current.X);
        e.Intersect.OriginY = e.Intersect.EndY = Battle.WorldToDisplay(e.Current.Y);
        e.Velocity.Zero();
    }

    // A ship's shot (a missile doing 2 damage) from a place, flying a facing's way at a speed (world units a frame).
    private static Element Missile(Battle battle, Starship from, int x, int y, int facing, int speed, int life)
    {
        var block = new MissileBlock
        {
            Cx = (short)x, Cy = (short)y, Face = (ushort)facing, Index = (ushort)facing, Sender = from.PlayerNr,
            Flags = ElementFlags.IgnoreSimilar, Speed = (short)speed, HitPoints = 1, Damage = 2, Life = (ushort)life,
            FrameArray = Shot,
        };
        var e = battle.InitializeMissile(block)!;
        e.Parent = from;
        battle.List.Put(e);
        return e;
    }

    // Whether a shot is still flying: in the list, solid and not gone.
    private static bool Live(Battle battle, Element e) =>
        battle.List.Elements.Contains(e) && (e.StateFlags & (ElementFlags.Disappearing | ElementFlags.NonSolid)) == 0;

    // The lasers a ship's point defence fired this frame.
    private static List<Element> Lasers(Battle battle, Starship ship) =>
        battle.List.Elements.Where(e => e.Prim == PrimType.Line && e.Parent == ship).ToList();

    // The enemy ship the AI chooses (ENEMY_SHIP_INDEX), its input otherwise not used.
    private static Element? Chosen(Battle battle, Starship ai)
    {
        var rd = ai.RaceDesc!;
        var own = rd.Intelligence!;
        Element? found = null;
        rd.Intelligence = (ship, concerns, count) => found = concerns[Battle.EnemyShipIndex].Object;
        var state = ai.ShipInputState;
        try
        {
            battle.ComputerIntelligence(ai);
        }
        finally
        {
            rd.Intelligence = own;
            ai.ShipInputState = state;
        }
        return found;
    }

    private static Sheet Solid(string id, int count, int size) => new(id, count,
        Enumerable.Range(0, count).Select(_ => new IntersectFrame(size, size, size / 2, size / 2,
            Enumerable.Repeat((byte)1, size * size).ToArray())).ToList());

    private static Views Three(Sheet big) => new(big, new Sheet(big.Id + ".medium", big.Count), new Sheet(big.Id + ".small", big.Count));
}
