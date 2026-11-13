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
/// Stage 4b's race ships with several players (stage 4's co-op rules), each
/// ported ship of item 2 (<see cref="Ships"/>) on the content's own sheets:
/// flown by the computer as the enemy against two and four players (its AI
/// takes the nearest player ship every frame; no player's element ever
/// touches a teammate's), its homing (the Arilou's laser, the Spathi's
/// B.U.T.T.) taking the nearest player, two players flying it on side 0
/// against the Ilwrath (its shots are side 0's and reach the Ilwrath, never
/// each other), and a battle in the world started against it. UQM's traces
/// (<see cref="RaceShipBattleTests"/>) check one ship a side.
/// </summary>
[Trait("Topic", "combat")]
public class RaceShipCoopTests
{
    /// <summary>The ships of stage 4b item 2.</summary>
    public static readonly string[] Ships = RaceBattleTraceTests.Parts[0];

    private static readonly ShipDescription Sis = BattleReplay.Ships[ShipDescriptions.Flagship];

    public static IEnumerable<object[]> ShipsAndPlayers() =>
        Ships.SelectMany(s => new[] { new object[] { s, 2 }, new object[] { s, 4 } });

    public static IEnumerable<object[]> EachShip() => Ships.Select(s => new object[] { s });

    [Fact]
    public void ItemTwosShipsArePortedAndCanBeFought()
    {
        foreach (string id in Ships)
        {
            Assert.Contains(id, RaceShips.Ported);
            Assert.Contains(id, BattleKit.Enemies);
        }
        Assert.Throws<ArgumentException>(() => RaceShips.Code(BattleReplay.Ships["vux"], _ => null));
        Assert.Throws<ArgumentException>(() => new SpathiShip(BattleReplay.Ships["arilou"], null, null, null));
    }

    [ContentTheory]
    [MemberData(nameof(ShipsAndPlayers))]
    public void TheComputerFliesItAgainstSeveralPlayers(string race, int players)
    {
        // Every ship flown by the AI, the players' flagships too, in the
        // world's arena (1.5 times UQM's) and list.
        var team = Enumerable.Range(0, players).Select(_ => Player()).ToArray();
        var enemy = Race(race, 1);
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { enemy } }, 4242,
            size: ArenaSize.Scaled(1.5), maxElements: 300);
        foreach (var p in team.Skip(1))
            Assert.True(battle.Join(p));
        var contacts = new ContactCheck(battle);

        int chosen = 0;
        var targets = new HashSet<Starship>();
        for (int frame = 0; frame < 900 && battle.InBattle; ++frame)
        {
            contacts.Watch();
            battle.Step(ship =>
            {
                if (ship != enemy)
                    return battle.ComputerIntelligence(ship);
                // The enemy ship its AI takes is the nearest player ship by straight-line distance.
                var rd = enemy.RaceDesc!;
                var own = rd.Intelligence!;
                rd.Intelligence = (s, concerns, count) =>
                {
                    var (nearest, turns) = Nearest(battle, s);
                    var found = concerns[Battle.EnemyShipIndex].Object;
                    Assert.Equal(nearest == null, found == null);
                    if (found != null)
                    {
                        Assert.Equal(turns, Turns(battle, s, found));
                        Assert.Equal(0, found.PlayerNr);
                        ++chosen;
                        targets.Add(found.Parent!);
                    }
                    own(s, concerns, count);
                };
                try
                {
                    return battle.ComputerIntelligence(ship);
                }
                finally
                {
                    rd.Intelligence = own;
                }
            });
        }
        Assert.True(chosen > 100, $"the AI chose an enemy {chosen} times");
        Assert.True(targets.Count > 1, "it took more than one player as its enemy");
        Assert.True(contacts.Opposing > 0, "the players' and the enemy's elements met");
    }

    [ContentTheory]
    [InlineData("arilou", 2)]
    [InlineData("arilou", 4)]
    [InlineData("spathi", 2)]
    [InlineData("spathi", 4)]
    public void ItsHomingTakesTheNearestPlayer(string race, int players)
    {
        // The race ship still at the centre facing north; the players still
        // about it, the second nearest by |dx| + |dy| (TrackShip's measure):
        // south, 1,000 away; then 1,500 east, 2,000 north, 2,400 north-west.
        var team = Enumerable.Range(0, players).Select(_ => Player()).ToArray();
        var enemy = Race(race, 1);
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { enemy } }, 99, maxElements: 300);
        foreach (var p in team.Skip(1))
            Assert.True(battle.Join(p));
        WarpIn(battle);
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0).ToList())
        {
            battle.List.Remove(e);
            battle.List.Free(e);
        }
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, enemy.Ship!, cx, cy);
        enemy.ShipFacing = 0;
        enemy.Ship!.Current.Frame = enemy.Ship.Next.Frame = enemy.RaceDesc!.Ship![0].SetAbs(0);
        int[] dx = { 1500, 0, 0, -1200 }, dy = { 0, 1000, -2000, -1200 };
        for (int i = 0; i < players; ++i)
            Put(battle, team[i].Ship!, cx + dx[i], cy + dy[i]);
        var nearest = team[1].Ship!;

        // Fired: the Arilou's weapon, the Spathi's special.
        var key = race == "arilou" ? BattleInput.Weapon : BattleInput.Special;
        battle.Step(ship => ship == enemy ? key : BattleInput.None);
        var shot = Assert.Single(battle.List.Elements, e => e.Parent == enemy && (e.StateFlags & ElementFlags.PlayerShip) == 0);
        if (race == "arilou")
        {
            // The laser aimed straight at it (south).
            var (vx, vy) = shot.Velocity.Current();
            Assert.Equal((short)0, vx);
            Assert.True(vy > 0, "towards the nearest player, south");
        }
        else
        {
            // The B.U.T.T. is told from the gun's shots by its sheets, the RACE_DESC's very special (SEEKING_SPECIAL).
            Assert.Same(enemy.RaceDesc.Special, shot.Current.FrameArray);
            for (int frame = 0; frame < 4 && shot.Target == null; ++frame)
                battle.Step(_ => BattleInput.None);
            Assert.Same(nearest, shot.Target);
            // and keeps it, though another comes nearer.
            Put(battle, team[0].Ship!, shot.Current.X + 200, shot.Current.Y);
            for (int frame = 0; frame < 4; ++frame)
                battle.Step(_ => BattleInput.None);
            Assert.Same(nearest, shot.Target);
        }
    }

    [ContentTheory]
    [MemberData(nameof(EachShip))]
    public void PlayersFlyItAgainstTheIlwrath(string race)
    {
        // Two players fly it on side 0 (their keys from its own AI, as
        // UQM's Cyborg would press them) against the Ilwrath flown by the AI.
        var team = new[] { Race(race, 0), Race(race, 0) };
        var ilwrath = new Starship(ShipBattleTests.Ship(IlwrathInput()).Code, 1, 22, 0) { CaptainsNameIndex = 5 };
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { ilwrath } }, 31337,
            size: ArenaSize.Scaled(1.5), maxElements: 300);
        Assert.True(battle.Join(team[1]));
        var contacts = new ContactCheck(battle);

        int ownElements = 0;
        for (int frame = 0; frame < 1500 && battle.InBattle; ++frame)
        {
            contacts.Watch();
            battle.Step(ship => battle.ComputerIntelligence(ship));
            // Their ships and shots are side 0's (their ion trails neutral).
            foreach (var e in battle.List.Elements.Where(e => e.Parent != null && team.Contains(e.Parent)
                                                              && e.PlayerNr != Battle.NeutralPlayerNum))
            {
                Assert.Equal(0, e.PlayerNr);
                if ((e.StateFlags & ElementFlags.PlayerShip) == 0)
                    ++ownElements;
            }
        }
        Assert.True(ownElements > 0, "they fired");
        Assert.True(contacts.HitBy(ilwrath, team) > 0, "their elements reached the Ilwrath");
    }

    [ContentTheory]
    [InlineData("zoqfot", 1.0, false)]
    [InlineData("zoqfot", 1.5, true)]
    [InlineData("umgah", 1.0, false)]
    [InlineData("umgah", 1.5, true)]
    [InlineData("spathi", 1.0, false)]
    [InlineData("spathi", 1.5, true)]
    [InlineData("supox", 1.0, false)]
    [InlineData("supox", 1.5, true)]
    [InlineData("thradd", 1.0, false)]
    [InlineData("thradd", 1.5, true)]
    public void ItsAiMeasuresAcrossTheSeamInABiggerArena(string race, double scale, bool across)
    {
        // The race ship by the arena's west edge, the player just across the
        // seam to its west, 200 units away. Its special decides on where the
        // player is: the Zoq-Fot-Pik faces west, the player dead ahead, and
        // sticks its tongue out (zoqfot.c:299); the Umgah faces east, the
        // player behind it, and zips back (umgah.c:240); the Spathi faces
        // east, the player behind and near, and fires its B.U.T.T.; the
        // Supox faces west, lined up with the player facing it, and strafes;
        // the Thraddash faces east, the player behind it and flying at it,
        // and burns. In UQM's arena, as UQM's, its AI measures the long way
        // round, the player ahead or behind, and it doesn't.
        var player = Player();
        var enemy = Race(race, 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { enemy } }, 7,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        WarpIn(battle);
        Clear(battle);
        int cy = battle.Size.Height / 2;
        ushort facing = race is "zoqfot" or "supox" ? (ushort)12 : (ushort)4;
        Face(enemy, facing);
        Face(player, 4);   // east, at the race ship
        Put(battle, enemy.Ship!, 100, cy);
        Put(battle, player.Ship!, battle.Size.Width - 100, cy);
        if (race == "thradd")
            player.Ship!.Velocity.SetComponents(1024, 0);   // flying east, faster than STATIONARY_SPEED
        enemy.SpecialCounter = 0;
        enemy.ShipInputState = StatusFlags.None;
        enemy.OldStatusFlags = StatusFlags.None;
        Assert.Equal(across, (battle.ComputerIntelligence(enemy) & BattleInput.Special) != 0);
    }

    [ContentFact]
    public void TheAiMeasuresATeleportingArilouByItsHull()
    {
        // The AI's footprint of its ship (tactical_intelligence's
        // ship_bounds, for keeping clear of the planet) is the first frame of
        // the sheet its contact was last set up with: teleporting, non-solid
        // and drawn with its warp sheet (17 by 13), the Arilou is still
        // measured by its hull's (17 by 14). At every distance from the
        // planet it sees the planet as it would flying.
        var player = Player();
        var arilou = Race("arilou", 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { arilou } }, 11, maxElements: 300);
        WarpIn(battle);
        Clear(battle, keepPlanet: true);
        var planet = battle.List.Elements.Single(e => Battle.GravityMass(e.MassPoints));
        var ship = arilou.Ship!;
        var rd = arilou.RaceDesc!;
        Assert.NotEqual(rd.Ship![0].Shape.Height, rd.Special![0].Shape.Height);
        Put(battle, player.Ship!, planet.Current.X, planet.Current.Y - 3000);
        var own = rd.Intelligence!;
        (bool, ushort, ushort) seen = default;
        rd.Intelligence = (s, concerns, count) => seen = (concerns[Battle.GravityMassIndex].Object != null,
            concerns[Battle.GravityMassIndex].WhichTurn, concerns[Battle.GravityMassIndex].Facing);

        (bool, ushort, ushort) Sees(int d, bool teleporting)
        {
            Put(battle, ship, planet.Current.X + d, planet.Current.Y);
            ship.TurnWait = ship.ThrustWait = 0;
            ship.StateFlags = teleporting ? ship.StateFlags | ElementFlags.NonSolid : ship.StateFlags & ~ElementFlags.NonSolid;
            ship.Next.FrameArray = teleporting ? rd.Special : rd.Ship;
            ship.Next.Frame = teleporting ? rd.Special[0] : rd.Ship[0].SetAbs(arilou.ShipFacing);
            seen = default;
            battle.ComputerIntelligence(arilou);
            return seen;
        }

        int seenAt = 0;
        for (int d = 200; d < 1200; d += 4)
        {
            var flying = Sees(d, teleporting: false);
            if (flying.Item1)
                ++seenAt;
            Assert.Equal(flying, Sees(d, teleporting: true));
        }
        rd.Intelligence = own;
        Assert.InRange(seenAt, 1, 249);   // seen near, not far
    }

    [ContentFact]
    public void TheDruugesFurnaceKeepsItsLastCrew()
    {
        // Pressing its special with its energy low: on 2 crew it burns one
        // for 16 energy; on 1 it doesn't.
        foreach (var (crew, after, energy) in new[] { (2, 1, 16), (1, 1, 0) })
        {
            var (battle, druuge, _) = Lab("druuge", crew);
            var rd = druuge.RaceDesc!;
            rd.EnergyLevel = 0;
            druuge.EnergyCounter = 50;
            battle.Step(ship => ship == druuge ? BattleInput.Special : BattleInput.None);
            Assert.Equal((after, energy), ((int)rd.CrewLevel, (int)rd.EnergyLevel));
        }
    }

    [ContentFact]
    public void TheDruugesAiBurnsCrewToFireButNotItsLast()
    {
        // Flown by the AI with the enemy near and no energy to fire: on 2
        // crew it presses its special (the furnace); on 1 it doesn't.
        foreach (var (crew, burns) in new[] { (2, true), (1, false) })
        {
            var (battle, druuge, enemy) = Lab("druuge", crew, enemyAt: 400);
            druuge.RaceDesc!.EnergyLevel = 0;
            var input = battle.ComputerIntelligence(druuge);
            Assert.True((input & BattleInput.Weapon) != 0, "it wants to fire");
            Assert.Equal(burns, (input & BattleInput.Special) != 0);
        }
    }

    [ContentFact]
    public void TheDruugesShotKnocksOnlyAShipItLeavesWithCrew()
    {
        // Its slug (6 damage) hits the still Ilwrath ahead: one of 22 crew
        // is knocked along; one of 6, killed by it, isn't.
        foreach (var (crew, knocked) in new[] { (22, true), (6, false) })
        {
            var (battle, druuge, ilwrath) = Lab("druuge", 14, enemyAt: 600, enemyCrew: crew);
            var hit = false;
            for (int frame = 0; frame < 30 && !hit; ++frame)
            {
                battle.Step(ship => ship == druuge && frame == 0 ? BattleInput.Weapon : BattleInput.None);
                hit = ilwrath.RaceDesc!.CrewLevel < crew;
            }
            Assert.True(hit, "the slug hit it");
            Assert.Equal(knocked, !ilwrath.Ship!.Velocity.IsZero);
        }
    }

    [ContentFact]
    public void TheZoqFotPiksTongueHurtsOnlyInItsFirstFrame()
    {
        // The tongue out at the Ilwrath ahead: there from the first frame,
        // it takes 12 crew; put there a frame after the tongue came out, it
        // touches it but takes none (tongue_collision's damage only while
        // the special's wait is whole).
        foreach (bool fromTheStart in new[] { true, false })
        {
            var (battle, zfp, ilwrath) = Lab("zoqfot", 10, enemyAt: fromTheStart ? 160 : 4000);
            var contacts = new ContactCheck(battle);
            contacts.Watch();
            battle.Step(ship => ship == zfp ? BattleInput.Special : BattleInput.None);
            contacts.Watch();
            battle.Step(_ => BattleInput.None);
            if (!fromTheStart)
                Put(battle, ilwrath.Ship!, zfp.Ship!.Current.X + 160, zfp.Ship.Current.Y);
            for (int frame = 0; frame < 4; ++frame)
            {
                contacts.Watch();
                battle.Step(_ => BattleInput.None);
            }
            Assert.True(contacts.HitBy(ilwrath, new[] { zfp }) > 0, "the tongue touched it");
            Assert.Equal(fromTheStart ? 22 - 12 : 22, ilwrath.RaceDesc!.CrewLevel);
        }
    }

    [ContentFact]
    public void TheYehatsShieldEndsWhenItsLifeIsCutShort()
    {
        // Shielded (drawn with its shield sheet), its life span put back to
        // NORMAL_LIFE (as a collision would): its next step draws its hull again.
        var (battle, yehat, _) = Lab("yehat", 20);
        var rd = yehat.RaceDesc!;
        battle.Step(ship => ship == yehat ? BattleInput.Special : BattleInput.None);
        Assert.Same(rd.Special, yehat.Ship!.Current.FrameArray);
        yehat.Ship.LifeSpan = Battle.NormalLife;
        battle.Step(_ => BattleInput.None);
        Assert.Same(rd.Ship, yehat.Ship.Current.FrameArray);
    }

    [ContentFact]
    public void TheYehatsShieldCostsNothingWhileItsNonSolid()
    {
        // Its shield up (its special waiting, its life span over NORMAL_LIFE):
        // each frame costs 3 energy, but not while it's non-solid.
        foreach (bool nonSolid in new[] { false, true })
        {
            var (battle, yehat, _) = Lab("yehat", 20);
            var rd = yehat.RaceDesc!;
            battle.Step(ship => ship == yehat ? BattleInput.Special : BattleInput.None);
            yehat.EnergyCounter = 50;
            yehat.SpecialCounter = 2;   // still waiting after this frame's count down
            byte energy = rd.EnergyLevel;
            if (nonSolid)
                yehat.Ship!.StateFlags |= ElementFlags.NonSolid;
            battle.Step(_ => BattleInput.None);
            Assert.Equal(nonSolid ? energy : energy - 3, rd.EnergyLevel);
        }
    }

    // A race ship of side 0 (with its crew) still at the centre facing
    // east, the Ilwrath (side 1, not cloaking, of its crew) still ahead of
    // it or far off, the planet and asteroids taken out.
    private static (Battle Battle, Starship Race, Starship Ilwrath) Lab(string race, int crew, int enemyAt = 4000,
        int enemyCrew = 22)
    {
        var d = BattleReplay.Ships[race];
        var ship = new Starship(RaceShips.Code(d, r => BattleReplay.Sheets.Value.Views(r)), 0, (ushort)crew, (ushort)d.MaxCrew);
        var ilwrath = new Starship(ShipBattleTests.Ship(IlwrathInput(), noCloak: true).Code, 1, (ushort)enemyCrew, 0)
            { CaptainsNameIndex = 5 };
        var battle = new Battle(Arena(), new[] { new[] { ship }, new[] { ilwrath } }, 808, maxElements: 300);
        WarpIn(battle);
        Clear(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Face(ship, 4);
        Face(ilwrath, 4);
        Put(battle, ship.Ship!, cx, cy);
        Put(battle, ilwrath.Ship!, cx + enemyAt, cy);
        ship.WeaponCounter = ship.SpecialCounter = 0;
        ship.ShipInputState = ship.OldStatusFlags = StatusFlags.None;
        return (battle, ship, ilwrath);
    }

    // Everything but the ships (and the planet, if kept) taken out.
    private static void Clear(Battle battle, bool keepPlanet = false)
    {
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                          && !(keepPlanet && Battle.GravityMass(e.MassPoints))).ToList())
        {
            battle.List.Remove(e);
            battle.List.Free(e);
        }
    }

    // A ship turned to a facing, drawn so.
    private static void Face(Starship s, ushort facing)
    {
        s.ShipFacing = facing;
        s.Ship!.Current.Frame = s.Ship.Next.Frame = s.RaceDesc!.Ship![0].SetAbs(facing);
    }

    [ContentFact]
    public void TheArilousTeleportReachesTheWholeArena()
    {
        // In an arena three times UQM's, the Arilou teleports 30 times; the
        // places it's put (arilou_preprocess's, before the camera's scroll)
        // are all within the arena, and reach past UQM's.
        var player = Player();
        var arilou = Race("arilou", 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { arilou } }, 2024,
            size: ArenaSize.Scaled(3), maxElements: 300);
        WarpIn(battle);
        var rd = arilou.RaceDesc!;
        var own = rd.Preprocess!;
        var places = new List<(int X, int Y)>();
        rd.Preprocess = e =>
        {
            bool moving = ReferenceEquals(e.Next.FrameArray, rd.Special) && e.LifeSpan == 3;
            own(e);
            if (moving)
                places.Add((e.Next.X, e.Next.Y));
        };
        for (int jump = 0; jump < 30 && battle.InBattle; ++jump)
        {
            rd.EnergyLevel = rd.MaxEnergy;
            arilou.SpecialCounter = 0;
            battle.Step(ship => ship == arilou ? BattleInput.Special : BattleInput.None);
            for (int frame = 0; frame < 10; ++frame)
                battle.Step(_ => BattleInput.None);
        }
        Assert.Equal(30, places.Count);
        Assert.All(places, p => Assert.True(p.X >= 0 && p.X < battle.Size.Width && p.Y >= 0 && p.Y < battle.Size.Height, $"{p}"));
        Assert.Contains(places, p => p.X >= Battle.LogSpaceWidth);
        Assert.Contains(places, p => p.Y >= Battle.LogSpaceHeight);
    }

    [ContentTheory]
    [MemberData(nameof(EachShip))]
    public void ItsShotsPassItsTeammates(string race)
    {
        // A player flying it, still at the centre facing east, presses its
        // weapon, its special, then both, 30 frames each, with a teammate
        // flying it 40 pixels ahead and another 40 behind, still and
        // pressing nothing: neither is touched by it, loses crew or is
        // moved. The same presses with the Ilwrath in the place ahead hurt it.
        var (touched, team, _) = Pressed(race, enemyAhead: false);
        Assert.Equal(0, touched);
        Assert.All(team.Skip(1), t =>
        {
            Assert.Equal(BattleReplay.Ships[race].Crew, t.RaceDesc!.CrewLevel);
            Assert.True(t.Ship!.Velocity.IsZero, "not moved");
        });
        var (hits, _, ilwrath) = Pressed(race, enemyAhead: true);
        Assert.True(hits > 0, "the shots reached the Ilwrath ahead");
        Assert.True(ilwrath.RaceDesc!.CrewLevel < 22, "and hurt it");
    }

    // The presses of ItsShotsPassItsTeammates: how often the presser's
    // elements touched the ship ahead.
    private static (int Touched, Starship[] Team, Starship Ilwrath) Pressed(string race, bool enemyAhead)
    {
        var team = new[] { Race(race, 0), Race(race, 0), Race(race, 0) };
        var ilwrath = new Starship(ShipBattleTests.Ship(IlwrathInput(), noCloak: true).Code, 1, 22, 0) { CaptainsNameIndex = 5 };
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { ilwrath } }, 5150, maxElements: 300);
        foreach (var t in team.Skip(1))
            Assert.True(battle.Join(t));
        WarpIn(battle);
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0).ToList())
        {
            battle.List.Remove(e);
            battle.List.Free(e);
        }
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        const int east = 4, gap = 160;
        foreach (var t in team.Append(ilwrath))
        {
            t.ShipFacing = east;
            t.Ship!.Current.Frame = t.Ship.Next.Frame = t.RaceDesc!.Ship![0].SetAbs(east);
        }
        Put(battle, team[0].Ship!, cx, cy);
        Put(battle, team[2].Ship!, cx - gap, cy);
        var ahead = enemyAhead ? ilwrath : team[1];
        Put(battle, ahead.Ship!, cx + gap, cy);
        Put(battle, (enemyAhead ? team[1] : ilwrath).Ship!, cx, cy + 2400);
        var contacts = new ContactCheck(battle);
        var keys = new[] { BattleInput.Weapon, BattleInput.Special, BattleInput.Weapon | BattleInput.Special };
        for (int frame = 0; frame < 90 && battle.InBattle; ++frame)
        {
            contacts.Watch();
            battle.Step(ship => ship == team[0] ? keys[frame / 30] : BattleInput.None);
        }
        return (contacts.HitBy(ahead, new[] { team[0] }), team, ilwrath);
    }

    [Theory]
    [MemberData(nameof(EachShip))]
    public void ABattleInTheWorldIsFoughtAgainstIt(string race)
    {
        // On sheets of our own: the host starts it, a guest flies in, and the AI flies it at them.
        var world = WorldBattleTests.NewWorld(2);
        var wb = world.StartBattle("host", race);
        Assert.Equal(race, wb.EnemyShip.Id);
        Assert.Equal(RaceShips.Code(wb.EnemyShip, _ => null).GetType(), wb.Enemy.Code.GetType());
        Assert.Equal((ushort)wb.EnemyShip.Crew, wb.Enemy.RaceDesc!.CrewLevel);
        WorldBattleTests.FlyIn(world, "g1");
        Assert.Equal(2, wb.Battle.BattleCounter[0]);
        int flown = 0, fired = 0;
        for (int i = 0; i < 400 && wb.Outcome is null; ++i)
        {
            WorldBattleTests.Run(world, 1);
            if ((wb.Enemy.ShipInputState & StatusFlags.Keys) != 0)
                ++flown;
            fired += wb.Battle.List.Elements.Count(e => e.Parent == wb.Enemy && (e.StateFlags & ElementFlags.PlayerShip) == 0);
        }
        Assert.True(flown > 0, "the AI flew it");
        Assert.True(fired > 0, "it fired or used its special");
    }

    // ---- The battle ----

    // The Ilwrath, as the recordings name it.
    private static System.Text.Json.JsonElement IlwrathInput() =>
        System.Text.Json.JsonDocument.Parse("""{"ship": "ilwrath", "kind": "ilwrath", "crew": 22}""").RootElement;

    // A race ship of a side, with its starting crew, its sheets the content's.
    private static Starship Race(string id, int side)
    {
        var d = BattleReplay.Ships[id];
        return new Starship(RaceShips.Code(d, r => BattleReplay.Sheets.Value.Views(r)), (short)side, (ushort)d.Crew,
            (ushort)d.MaxCrew) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) };
    }

    // A player's flagship: a gun in the nose, two thrusters and jets, a crew pod.
    private static Starship Player()
    {
        var modules = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        modules[2] = ShipModules.CrewPod;
        modules[15] = ShipModules.GunWeapon;
        var loadout = new SisLoadout(
            Enumerable.Range(0, ShipModules.DriveSlotCount).Select(i => i < 2 ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, ShipModules.JetSlotCount).Select(i => i < 2 ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            modules, 30);
        return new Starship(new SisShip(Sis, BattleReplay.ViewsOf(Sis.Graphics.Ship), BattleReplay.ViewsOf(Sis.Graphics.Weapon),
            loadout), 0, 0, 0);
    }

    private static BattleArena Arena() =>
        BattleReplay.Sheets.Value.Arena(new[] { "planet.water.large", "planet.water.medium", "planet.water.small" });

    // The nearest player ship the AI could take as its enemy (tactical_intelligence's
    // CollisionPossible with its ship, measured as it measures), and how far.
    private static (Element? Ship, ushort Turns) Nearest(Battle battle, Element ai)
    {
        Element? best = null;
        ushort bestTurns = ushort.MaxValue;
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) != 0 && e.PlayerNr == 0
                     && Battle.Colliding(e) && (e.StateFlags & ai.StateFlags & ElementFlags.Collision) == 0))
        {
            ushort turns = Turns(battle, ai, e);
            if (turns <= bestTurns)
                (best, bestTurns) = (e, turns);
        }
        return (best, bestTurns);
    }

    private static ushort Turns(Battle battle, Element ai, Element e)
    {
        short dx = battle.WrapDeltaX((short)(e.Next.X - ai.Next.X)), dy = battle.WrapDeltaY((short)(e.Next.Y - ai.Next.Y));
        ushort turns = Battle.WorldToTurn(UqmMath.SquareRoot((uint)((long)dx * dx + (long)dy * dy)));
        return turns == 0 ? (ushort)1 : turns;
    }

    // Every element's contacts watched: no two teammates' elements ever
    // touch (decision 2, no friendly fire); those of opposing sides counted.
    private sealed class ContactCheck
    {
        private readonly Battle _battle;
        private readonly Dictionary<Element, ElementCollision> _watching = new();
        private readonly List<(Starship? A, Starship? B)> _met = new();

        public ContactCheck(Battle battle) => _battle = battle;

        public int Opposing => _met.Count(m => m.A != null && m.B != null && m.A.PlayerNr != m.B.PlayerNr);

        public int HitBy(Starship ship, IReadOnlyCollection<Starship> by) =>
            _met.Count(m => (m.A == ship && m.B != null && by.Contains(m.B)) || (m.B == ship && m.A != null && by.Contains(m.A)));

        // Before a frame: each element's contact callback wrapped (shots made
        // in a frame's last step touch nothing until the next).
        public void Watch()
        {
            foreach (var e in _battle.List.Elements)
            {
                if (e.CollisionFunc is not { } inner || (_watching.TryGetValue(e, out var w) && w == inner))
                    continue;
                ElementCollision watched = (e0, at0, e1, at1) =>
                {
                    Assert.False(Battle.Teammates(e0, e1), $"teammates' elements touched: {e0} and {e1}");
                    if (e0.PlayerNr != Battle.NeutralPlayerNum && e1.PlayerNr != Battle.NeutralPlayerNum)
                        _met.Add((e0.Parent, e1.Parent));
                    inner(e0, at0, e1, at1);
                };
                e.CollisionFunc = watched;
                _watching[e] = watched;
            }
        }
    }

    // Every ship with an element drawn and solid.
    private static void WarpIn(Battle battle)
    {
        bool Warped() => Enumerable.Range(0, Battle.NumSides).SelectMany(battle.Fleet).Where(s => s.Ship != null)
            .All(s => s.Ship!.Prim == PrimType.Stamp && (s.Ship.StateFlags & (ElementFlags.Appearing | ElementFlags.NonSolid)) == 0);
        for (int i = 0; i < 200 && !Warped(); ++i)
            battle.Step(_ => BattleInput.None);
        Assert.True(Warped(), "every ship warped in");
    }

    // An element put still at a place, its contact test's start and end there too.
    private static void Put(Battle battle, Element e, int x, int y)
    {
        e.Current.X = e.Next.X = (short)battle.WrapX(x);
        e.Current.Y = e.Next.Y = (short)battle.WrapY(y);
        e.Intersect.OriginX = e.Intersect.EndX = Battle.WorldToDisplay(e.Current.X);
        e.Intersect.OriginY = e.Intersect.EndY = Battle.WorldToDisplay(e.Current.Y);
        e.Velocity.Zero();
    }
}
