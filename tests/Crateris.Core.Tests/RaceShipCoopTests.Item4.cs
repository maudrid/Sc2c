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

// Stage 4b item 4's own cases (the Earthling, Chmmr, Shofixti, Syreen,
// Orz, Melnorme, VUX and Pkunk): the co-op rules with two and four
// players (point defence and the Glory Device leaving teammates be, the
// song taking crew from every player in reach, the marines going for the
// nearest player and staying aboard the ship they boarded, the Pkunk's
// rebirth across the whole arena), their code and AIs measuring across
// the seam in a bigger arena, and the paths UQM's traces don't reach.
public partial class RaceShipCoopTests
{
    // ---- Point defence ----

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheEarthlingsPointDefenceLeavesTeammatesBe(int players)
    {
        // A player's Earthling at the centre; its teammates (flagships)
        // within its reach, each with a shot of its own beside it; the
        // Ilwrath far off. Its point defence, pressed, fires at none of
        // them and costs nothing. With the Ilwrath within reach too, it
        // fires at the Ilwrath alone.
        foreach (bool ilwrathNear in new[] { false, true })
        {
            var (battle, human, team, ilwrath) = Team("human", players);
            var rd = human.RaceDesc!;
            byte energy = rd.EnergyLevel;
            if (ilwrathNear)
                Put(battle, ilwrath.Ship!, human.Ship!.Current.X - 240, human.Ship.Current.Y);
            var made = PressedOnce(battle, human, BattleInput.Special);
            var lasers = made.Where(e => e.Parent == human && e.Prim == PrimType.Line).ToList();
            if (!ilwrathNear)
            {
                Assert.Empty(lasers);
                Assert.Equal(energy, rd.EnergyLevel);
                Assert.Equal(0, human.SpecialCounter);
            }
            else
            {
                var laser = Assert.Single(lasers);
                Assert.True(laser.Velocity.Current().dx < 0, "at the Ilwrath, west");
                Assert.Equal(energy - 4, rd.EnergyLevel);
            }
            Assert.All(team, t => Assert.Equal(t.Ship!.CrewLevel, t.RaceDesc!.CrewLevel));
        }
    }

    [ContentTheory]
    [InlineData("human", 2)]
    [InlineData("human", 4)]
    [InlineData("chmmr", 2)]
    [InlineData("chmmr", 4)]
    public void AnEnemysPointDefenceZapsEveryPlayersShot(string race, int players)
    {
        // The race ship (the computer's) at the centre, its point defence
        // ready; each player far off, a shot of each player's still within
        // its reach (the Chmmr's satellites' reach about its ship): each is zapped.
        var (battle, enemy, team) = Crowd(race, players);
        if (race == "chmmr")
            WaitForSatellites(battle, enemy);
        var shots = Shots(battle, team);
        // (Each frame's scroll moves everything: places are taken afresh after it.)
        int cx = enemy.Ship!.Current.X, cy = enemy.Ship.Current.Y;
        for (int i = 0; i < players; ++i)
        {
            int angle = UqmMath.FullCircle * i / players;
            Put(battle, shots[i], cx + UqmMath.Cosine(angle, 240), cy + UqmMath.Sine(angle, 240));
        }
        battle.Step(ship => ship == enemy && race == "human" ? BattleInput.Special : BattleInput.None);
        var zapped = battle.List.Elements.Where(e => e.Parent == enemy && e.Prim == PrimType.Line).ToList();
        if (race == "chmmr")
        {
            // A satellite zaps one at a time, then waits: every one in time.
            for (int frame = 0; frame < 12; ++frame)
            {
                foreach (var s in shots.Where(s => s.InList))
                    s.Velocity.Zero();
                battle.Step(_ => BattleInput.None);
            }
            Assert.All(shots, s => Assert.True(!s.InList || s.HitPoints < 3 || s.LifeSpan == 0, "zapped"));
        }
        else
            Assert.Equal(players, zapped.Count);
    }

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheChmmrsSatellitesLeaveTeammatesBe(int players)
    {
        // A player's Chmmr with its satellites out; a shot of each teammate
        // (flagships) within a satellite's reach: none is touched. An
        // Ilwrath shot there instead is zapped.
        foreach (bool ilwrathShot in new[] { false, true })
        {
            var (battle, chmmr, team, ilwrath) = Team("chmmr", players, teammates: _ => Player());
            WaitForSatellites(battle, chmmr);
            var satellites = battle.List.Elements.Where(e => e.Parent == chmmr && ReferenceEquals(e.Current.FrameArray, chmmr.RaceDesc!.Special)).ToList();
            var shots = ilwrathShot ? Shots(battle, new[] { ilwrath }) : Shots(battle, team.Skip(1).ToArray());
            var hp = shots.Select(s => s.HitPoints).ToArray();
            for (int frame = 0; frame < 6; ++frame)
            {
                foreach (var (s, i) in shots.Select((s, i) => (s, i)).Where(x => x.s.InList))
                {
                    Put(battle, s, satellites[i % 3].Current.X + 80, satellites[i % 3].Current.Y);
                    s.LifeSpan = 10;
                }
                battle.Step(_ => BattleInput.None);
            }
            var hurt = shots.Where((s, i) => !s.InList || s.LifeSpan == 0 || s.HitPoints < hp[i]).Count();
            Assert.Equal(ilwrathShot ? shots.Count : 0, hurt);
        }
    }

    // The Chmmr's satellites out (spawn_satellites, as it warps in).
    private static void WaitForSatellites(Battle battle, Starship chmmr)
    {
        int Satellites() => battle.List.Elements.Count(e => e.Parent == chmmr && ReferenceEquals(e.Current.FrameArray, chmmr.RaceDesc!.Special));
        for (int frame = 0; frame < 30 && Satellites() < 3; ++frame)
            battle.Step(_ => BattleInput.None);
        Assert.Equal(3, Satellites());
    }

    // ---- The Glory Device ----

    // The keys that set it off: pressed, let go, pressed, let go, pressed
    // (five changes of the key move the captain's window on from frame 12
    // to 17, the third from the end of its 20).
    private static readonly BattleInput[] Glory =
        { BattleInput.Special, BattleInput.None, BattleInput.Special, BattleInput.None, BattleInput.Special };

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheShofixtisGloryDeviceSparesTeammates(int players)
    {
        // A player's Shofixti blows itself up with its teammates within
        // reach, each with a shot beside it, and the Ilwrath within reach:
        // the teammates and their shots are untouched; the Ilwrath loses crew.
        var (battle, shofixti, team, ilwrath) = Team("shofixti", players);
        var shots = Shots(battle, team.Skip(1).ToArray());
        Put(battle, ilwrath.Ship!, shofixti.Ship!.Current.X - 200, shofixti.Ship.Current.Y);
        for (int i = 0; i < shots.Count; ++i)
            Put(battle, shots[i], shofixti.Ship.Current.X + 100, shofixti.Ship.Current.Y + 60 * i);
        var crew = team.Select(t => t.RaceDesc!.CrewLevel).ToArray();
        var hp = shots.Select(s => s.HitPoints).ToArray();
        foreach (var keys in Glory)
        {
            foreach (var s in shots)
                s.Velocity.Zero();
            battle.Step(ship => ship == shofixti ? keys : BattleInput.None);
        }
        Assert.Equal(0, shofixti.RaceDesc!.CrewLevel);
        Assert.Equal(crew.Skip(1), team.Skip(1).Select(t => t.RaceDesc!.CrewLevel));
        Assert.Equal(hp, shots.Select(s => s.HitPoints));
        Assert.All(shots, s => Assert.NotEqual(0, s.LifeSpan));
        Assert.True(ilwrath.RaceDesc!.CrewLevel < 22, "the Ilwrath lost crew");
    }

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void AnEnemyShofixtisGloryDeviceHitsEveryPlayerInReach(int players)
    {
        // The computer's Shofixti blows itself up with the players about
        // it, the last one beyond its reach: every one in reach loses 1 +
        // 18 * (180 - distance) / 180 crew, the one beyond none.
        var (battle, shofixti, team) = Crowd("shofixti", players);
        int cx = shofixti.Ship!.Current.X, cy = shofixti.Ship.Current.Y;
        int[] distance = Enumerable.Range(0, players).Select(i => i == players - 1 ? 190 : 40 + 40 * i).ToArray();
        for (int i = 0; i < players; ++i)
            Put(battle, team[i].Ship!, cx + Battle.DisplayToWorld(distance[i]) * (i % 2 == 0 ? 1 : -1), cy);
        var crew = team.Select(t => (int)t.RaceDesc!.CrewLevel).ToArray();
        foreach (var keys in Glory)
            battle.Step(ship => ship == shofixti ? keys : BattleInput.None);
        for (int i = 0; i < players; ++i)
        {
            int lost = distance[i] <= 180 ? 1 + 18 * (180 - distance[i]) / 180 : 0;
            Assert.Equal(crew[i] - lost, team[i].RaceDesc!.CrewLevel);
        }
    }

    [ContentFact]
    public void TheGloryDeviceGoesOffOnTheFifthChangeOfTheKey()
    {
        // Pressed and let go four times (four changes), the Shofixti lives;
        // the fifth sets it off, the ship losing all its crew at once.
        var (battle, shofixti, ilwrath) = Lab("shofixti", 6, enemyAt: 4000);
        foreach (var keys in Glory.Take(4))
            battle.Step(ship => ship == shofixti ? keys : BattleInput.None);
        Assert.Equal(6, shofixti.RaceDesc!.CrewLevel);
        battle.Step(ship => ship == shofixti ? BattleInput.Special : BattleInput.None);
        Assert.Equal(0, shofixti.RaceDesc.CrewLevel);
        Assert.Equal(22, ilwrath.RaceDesc!.CrewLevel);
    }

    [ContentFact]
    public void AShofixtiWithoutACaptainsWindowNeverGoesOff()
    {
        // Without its captain's sheets (UQM's captain_control without a
        // window), the key's changes count nothing and it never blows up.
        var d = BattleReplay.Ships["shofixti"];
        var code = new ShofixtiShip(d, BattleReplay.ViewsOf(d.Graphics.Ship), BattleReplay.ViewsOf(d.Graphics.Weapon),
            BattleReplay.ViewsOf(d.Graphics.Special), captain: null);
        var shofixti = new Starship(code, 0, 6, 6);
        var ilwrath = new Starship(ShipBattleTests.Ship(IlwrathInput(), noCloak: true).Code, 1, 22, 0) { CaptainsNameIndex = 5 };
        var battle = new Battle(Arena(), new[] { new[] { shofixti }, new[] { ilwrath } }, 808, maxElements: 300);
        WarpIn(battle);
        for (int frame = 0; frame < 20; ++frame)
            battle.Step(ship => ship == shofixti ? Glory[frame % 2 == 0 ? 0 : 1] : BattleInput.None);
        Assert.Equal(6, shofixti.RaceDesc!.CrewLevel);
    }

    [ContentFact]
    public void TheGloryDeviceKillsAnOrzMarineAboardAShipInReach()
    {
        // The player's Orz has a marine aboard the computer's Shofixti
        // (non-solid, aboard); the Shofixti blows itself up: the marine
        // aboard, in reach, is killed with it (ORZ_MARINE), not left to fly
        // free as the ship's crew runs out.
        var orz = Race("orz", 0);
        var shofixti = Race("shofixti", 1);
        var battle = new Battle(Arena(), new[] { new[] { orz }, new[] { shofixti } }, 808, maxElements: 300);
        WarpIn(battle);
        ClearArena(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, orz.Ship!, cx - 2000, cy);
        Put(battle, shofixti.Ship!, cx, cy);
        var marine = Board(battle, orz, shofixti.Ship!);
        foreach (var keys in Glory)
        {
            Put(battle, orz.Ship!, cx - 2000, cy);
            battle.Step(ship => ship == shofixti ? keys : BattleInput.None);
        }
        Assert.Equal(0, shofixti.RaceDesc!.CrewLevel);
        Assert.Equal(0, marine.LifeSpan);
    }

    // ---- The Syreen's song ----

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void AnEnemySyreensSongTakesCrewFromEveryPlayerInReach(int players)
    {
        // The computer's Syreen sings with the players about it, the last
        // beyond its reach: each in reach loses 8 * (208 - distance) / 208
        // + 1 crew, floating off; the one beyond none.
        var (battle, syreen, team) = Crowd("syreen", players);
        int cx = syreen.Ship!.Current.X, cy = syreen.Ship.Current.Y;
        int[] distance = Enumerable.Range(0, players).Select(i => i == players - 1 ? 220 : 30 + 50 * i).ToArray();
        for (int i = 0; i < players; ++i)
            Put(battle, team[i].Ship!, cx, cy + Battle.DisplayToWorld(distance[i]) * (i % 2 == 0 ? 1 : -1));
        var crew = team.Select(t => (int)t.RaceDesc!.CrewLevel).ToArray();
        battle.Step(ship => ship == syreen ? BattleInput.Special : BattleInput.None);
        int floating = 0;
        for (int i = 0; i < players; ++i)
        {
            int lost = distance[i] <= 208 ? 8 * (208 - distance[i]) / 208 + 1 : 0;
            Assert.Equal(crew[i] - lost, team[i].RaceDesc!.CrewLevel);
            floating += lost;
        }
        Assert.Equal(floating, battle.List.Elements.Count(e => (e.StateFlags & ElementFlags.CrewObject) != 0));
    }

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void APlayersSyreenSongSparesItsTeammates(int players)
    {
        // A player's Syreen sings with its teammates and the Ilwrath close
        // by: the teammates keep their crew; the Ilwrath loses some.
        var (battle, syreen, team, ilwrath) = Team("syreen", players);
        Put(battle, ilwrath.Ship!, syreen.Ship!.Current.X, syreen.Ship.Current.Y - 200);
        var crew = team.Select(t => t.RaceDesc!.CrewLevel).ToArray();
        battle.Step(ship => ship == syreen ? BattleInput.Special : BattleInput.None);
        Assert.Equal(crew.Skip(1), team.Skip(1).Select(t => t.RaceDesc!.CrewLevel));
        Assert.True(ilwrath.RaceDesc!.CrewLevel < 22, "the Ilwrath lost crew");
    }

    [ContentFact]
    public void TheSongLeavesEachShipOneCrewAndPassesAShipWithOne()
    {
        // A ship of 3 crew right by the Syreen would lose 9: it's left one.
        // A ship of one crew loses none.
        foreach (var (crew, after) in new[] { (3, 1), (1, 1) })
        {
            var (battle, syreen, ilwrath) = Lab("syreen", 12, enemyAt: 80, enemyCrew: crew);
            battle.Step(ship => ship == syreen ? BattleInput.Special : BattleInput.None);
            Assert.Equal(after, ilwrath.RaceDesc!.CrewLevel);
            Assert.Equal(crew - after, battle.List.Elements.Count(e => (e.StateFlags & ElementFlags.CrewObject) != 0));
        }
    }

    [ContentTheory]
    [InlineData(2)]
    [InlineData(4)]
    public void AnyShipPicksUpTheFloatingCrewAndWhatsLeftIsShared(int players)
    {
        // The computer's Syreen sings at the first player alone: its crew
        // float. Another player flies through some of them and picks them up.
        // The rest floating when the battle ends are shared between the
        // players left, as stage 4a shares them (BattleEndTests): the
        // players' crew comes back to what they had, as long as there's room.
        var (battle, syreen, team) = Crowd("syreen", players, race: "human");
        int cx = syreen.Ship!.Current.X, cy = syreen.Ship.Current.Y;
        foreach (var t in team)
            battle.DeltaCrew(t.Ship!, -4);
        Put(battle, team[0].Ship!, cx, cy + 120);
        var crew = team.Select(t => (int)t.RaceDesc!.CrewLevel).ToArray();
        battle.Step(ship => ship == syreen ? BattleInput.Special : BattleInput.None);
        var floating = battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.CrewObject) != 0).ToList();
        int lost = crew[0] - team[0].RaceDesc!.CrewLevel;
        Assert.True(lost >= 2, $"the song took {lost}");
        Assert.Equal(lost, floating.Count);

        // Another player picks one up.
        var picker = team[1];
        int before = picker.RaceDesc!.CrewLevel;
        var first = floating[0];
        Put(battle, picker.Ship!, first.Current.X, first.Current.Y);
        for (int frame = 0; frame < 3 && first.InList && first.LifeSpan > 0; ++frame)
            battle.Step(_ => BattleInput.None);
        Assert.True(picker.RaceDesc.CrewLevel > before, "the other player picked up crew");

        int left = battle.List.Elements.Count(e => (e.StateFlags & ElementFlags.CrewObject) != 0);
        int total = team.Sum(t => t.RaceDesc!.CrewLevel);
        battle.UninitShips();
        Assert.Equal(total + left, team.Sum(t => t.CrewLevel));
    }

    // ---- Helpers ----

    // A race ship of side 0 (the player's) at the centre facing east, its
    // teammates (more of the same, in the battle in turn) about it within
    // 50 pixels, the Ilwrath (side 1, not cloaking) far off; the planet and
    // asteroids taken out, every key's counter clear.
    private static (Battle Battle, Starship Race, Starship[] Team, Starship Ilwrath) Team(string race, int players,
        double scale = 1.0, Func<int, Starship>? teammates = null)
    {
        var team = Enumerable.Range(0, players).Select(i => i == 0 || teammates == null ? Race(race, 0) : teammates(i)).ToArray();
        var ilwrath = new Starship(ShipBattleTests.Ship(IlwrathInput(), noCloak: true).Code, 1, 22, 0) { CaptainsNameIndex = 5 };
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { ilwrath } }, 808,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        foreach (var t in team.Skip(1))
            Assert.True(battle.Join(t));
        WarpIn(battle);
        ClearArena(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, team[0].Ship!, cx, cy);
        for (int i = 1; i < players; ++i)
        {
            int angle = UqmMath.FullCircle * i / players;
            Put(battle, team[i].Ship!, cx + UqmMath.Cosine(angle, 200), cy + UqmMath.Sine(angle, 200));
        }
        Put(battle, ilwrath.Ship!, cx, cy + 4000);
        foreach (var t in team.Append(ilwrath))
        {
            Face(t, 4);
            t.WeaponCounter = t.SpecialCounter = 0;
            t.ShipInputState = t.OldStatusFlags = StatusFlags.None;
        }
        return (battle, team[0], team, ilwrath);
    }

    // The race ship as the computer's (side 1) at the centre facing north,
    // the players (flagships, or race ships of race) far off; the planet
    // and asteroids taken out.
    private static (Battle Battle, Starship Enemy, Starship[] Team) Crowd(string enemyRace, int players,
        double scale = 1.0, uint seed = 99, string? race = null)
    {
        var team = Enumerable.Range(0, players).Select(_ => race == null ? Player() : Race(race, 0)).ToArray();
        var enemy = Race(enemyRace, 1);
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { enemy } }, seed,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        foreach (var p in team.Skip(1))
            Assert.True(battle.Join(p));
        WarpIn(battle);
        ClearArena(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, enemy.Ship!, cx, cy);
        Face(enemy, 0);
        for (int i = 0; i < players; ++i)
            Put(battle, team[i].Ship!, cx + 3000, cy - 3000 + 1500 * i);
        enemy.WeaponCounter = enemy.SpecialCounter = 0;
        enemy.ShipInputState = enemy.OldStatusFlags = StatusFlags.None;
        return (battle, enemy, team);
    }

    // The planet, asteroids and warp images taken out (no side's); the
    // ships' own elements (the Chmmr's satellites coming, the Orz's turret) kept.
    private static void ClearArena(Battle battle)
    {
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                          && (e.Parent == null || e.PlayerNr == Battle.NeutralPlayerNum)).ToList())
        {
            battle.List.Remove(e);
            battle.List.Free(e);
        }
    }

    // A shot of each ship's (the flagship's gun, a race ship's weapon),
    // fired where it is and held still.
    private static List<Element> Shots(Battle battle, IReadOnlyList<Starship> ships)
    {
        var before = battle.List.Elements.ToHashSet();
        foreach (var s in ships)
        {
            s.WeaponCounter = 0;
            s.RaceDesc!.EnergyLevel = s.RaceDesc.MaxEnergy;
        }
        battle.Step(ship => ships.Contains(ship) ? BattleInput.Weapon : BattleInput.None);
        var shots = ships.Select(s => battle.List.Elements.First(e => !before.Contains(e) && e.Parent == s
                                                                      && (e.StateFlags & ElementFlags.PlayerShip) == 0)).ToList();
        foreach (var s in shots)
            s.Velocity.Zero();
        return shots;
    }

    // A frame with the ship pressing keys; the elements made in it.
    private static List<Element> PressedOnce(Battle battle, Starship ship, BattleInput keys)
    {
        var before = battle.List.Elements.ToHashSet();
        battle.Step(s => s == ship ? keys : BattleInput.None);
        return battle.List.Elements.Where(e => !before.Contains(e)).ToList();
    }

    // An Orz's marine launched and put aboard a ship (as marine_collision
    // does when it touches it): returns the marine.
    private static Element Board(Battle battle, Starship orz, Element ship)
    {
        orz.SpecialCounter = orz.WeaponCounter = 0;
        var made = PressedOnce(battle, orz, BattleInput.Weapon | BattleInput.Special);
        var marine = Assert.Single(made, e => ReferenceEquals(e.Current.FrameArray, orz.RaceDesc!.Special)
                                              && (e.StateFlags & ElementFlags.CrewObject) != 0);
        // Flying at it from just west of it (still things don't touch).
        Put(battle, marine, ship.Current.X - 60, ship.Current.Y);
        marine.Velocity.SetVector(40, 4);
        for (int frame = 0; frame < 10 && (marine.StateFlags & ElementFlags.NonSolid) == 0; ++frame)
            battle.Step(_ => BattleInput.None);
        Assert.True((marine.StateFlags & ElementFlags.NonSolid) != 0, "the marine boarded");
        return marine;
    }
}
