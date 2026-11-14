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

// Stage 4b item 3's own cases (the Utwig, Mycon, Androsynth, Mmrnmhrm,
// Chenjesu, Slylandro, Ur-Quan and Kohr-Ah): their homing taking the
// nearest player, the Ur-Quan's fighters flying back to their own ship,
// their code and AIs measuring across the seam in a bigger arena, and the
// paths UQM's traces don't reach.
public partial class RaceShipCoopTests
{
    // Each homing shot of item 3's ships, and the keys that make it: the
    // Mycon's plasmoid, the Androsynth's bubbles, the Mmrnmhrm's Y-wing
    // torpedoes (transformed first), the Chenjesu's DOGI, the Slylandro's
    // lightning, the Ur-Quan's fighters, the Kohr-Ah's blades (let go, and
    // stopped).
    public static IEnumerable<object[]> HomersAndPlayers() =>
        new[] { "mycon", "androsyn", "mmrnmhrm", "chenjesu", "slylandr", "urquan", "blackurq" }
            .SelectMany(s => new[] { new object[] { s, 2 }, new object[] { s, 4 } });

    [ContentTheory]
    [MemberData(nameof(HomersAndPlayers))]
    public void ItsHomersTakeTheNearestPlayer(string race, int players)
    {
        // As ItsHomingTakesTheNearestPlayer: the race ship still at the
        // centre facing north; the second player the nearest, 1,000 south.
        var team = Enumerable.Range(0, players).Select(_ => Player()).ToArray();
        var enemy = Race(race, 1);
        var battle = new Battle(Arena(), new[] { new[] { team[0] }, new[] { enemy } }, 99, maxElements: 300);
        foreach (var p in team.Skip(1))
            Assert.True(battle.Join(p));
        WarpIn(battle);
        Clear(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Put(battle, enemy.Ship!, cx, cy);
        Face(enemy, 0);
        int[] dx = { 1500, 0, 0, -1200 }, dy = { 0, 1000, -2000, -1200 };
        for (int i = 0; i < players; ++i)
            Put(battle, team[i].Ship!, cx + dx[i], cy + dy[i]);
        var nearest = team[1].Ship!;
        var rd = enemy.RaceDesc!;

        BattleInput Keys(int frame) => race switch
        {
            "mmrnmhrm" => frame == 0 ? BattleInput.Special : BattleInput.Weapon,
            "chenjesu" or "urquan" => frame == 0 ? BattleInput.Special : BattleInput.None,
            _ => frame == 0 ? BattleInput.Weapon : BattleInput.None,
        };

        Element? shot = null;
        for (int frame = 0; frame < 60 && shot == null; ++frame)
        {
            battle.Step(ship => ship == enemy ? Keys(frame) : BattleInput.None);
            var homing = battle.List.Elements.Where(e => e.Parent == enemy && (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                         && e.Target != null).ToList();
            Assert.All(homing, e => Assert.Same(nearest, e.Target));
            shot = homing.FirstOrDefault();
        }
        Assert.NotNull(shot);
        if (race is "chenjesu" or "urquan")
        {
            // The DOGI and fighters are told from the weapon's shots by
            // their sheets, the RACE_DESC's very special (SEEKING_SPECIAL).
            Assert.Same(rd.Special, shot!.Current.FrameArray);
        }
        if (race == "slylandr")
            return;   // each bolt lives a frame: the next takes its own

        // It keeps it, though another comes nearer.
        Put(battle, team[0].Ship!, shot!.Current.X + 200, shot.Current.Y);
        for (int frame = 0; frame < 4; ++frame)
            battle.Step(_ => BattleInput.None);
        Assert.Same(nearest, shot.Target);
    }

    [ContentFact]
    public void TheUrQuansFightersFlyBackToTheirOwnShip()
    {
        // Two players fly Ur-Quan, the first launching its fighters (two
        // crew) at the Ilwrath far ahead, made to outlast them; the second,
        // a teammate, waits still by the first. The fighters go for the
        // Ilwrath, and when they've flown long enough come back aboard
        // their own ship, not the teammate's: each ship has its 42 crew again.
        var (battle, urquan, ilwrath) = Lab("urquan", 42, enemyAt: 3000);
        var teammate = Race("urquan", 0);
        Assert.True(battle.Join(teammate));
        WarpIn(battle);
        Clear(battle);
        Face(teammate, 4);
        Put(battle, teammate.Ship!, urquan.Ship!.Current.X, urquan.Ship.Current.Y + 600);
        Put(battle, urquan.Ship, urquan.Ship.Current.X, urquan.Ship.Current.Y);
        ilwrath.Ship!.CrewLevel = ilwrath.RaceDesc!.CrewLevel = 400;
        var contacts = new ContactCheck(battle);

        int fewest = 42;
        for (int frame = 0; frame < 450 && battle.InBattle; ++frame)
        {
            contacts.Watch();
            battle.Step(ship => ship == urquan && frame == 0 ? BattleInput.Special : BattleInput.None);
            fewest = Math.Min(fewest, urquan.RaceDesc!.CrewLevel);
            Assert.Equal(42, teammate.RaceDesc!.CrewLevel);
        }
        Assert.Equal(40, fewest);
        Assert.True(contacts.HitBy(ilwrath, new[] { urquan }) > 0, "the fighters reached the Ilwrath");
        Assert.True(ilwrath.RaceDesc!.CrewLevel < 400, "and zapped it");
        Assert.Equal(42, urquan.RaceDesc!.CrewLevel);
        Assert.DoesNotContain(battle.List.Elements, e => ReferenceEquals(e.Current.FrameArray, urquan.RaceDesc.Special));
    }

    [ContentTheory]
    [InlineData(1.0, false)]
    [InlineData(1.5, true)]
    public void TheSlylandroHarvestsAcrossTheSeamInABiggerArena(double scale, bool across)
    {
        // The Slylandro by the arena's west edge with no energy, an
        // asteroid just across the seam to its west, 200 units away: its
        // special harvests it (slylandr.c:310) in a bigger arena, measured
        // the short way round; in UQM's, as UQM's, it's the long way and
        // too far.
        var player = Player();
        var sly = Race("slylandr", 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { sly } }, 7,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        WarpIn(battle);
        var asteroid = battle.List.Elements.First(e => e.PlayerNr == Battle.NeutralPlayerNum
                                                       && (e.StateFlags & ElementFlags.FiniteLife) == 0
                                                       && !Battle.GravityMass(e.MassPoints) && e.MassPoints != 0);
        foreach (var e in battle.List.Elements.Where(e => (e.StateFlags & ElementFlags.PlayerShip) == 0 && e != asteroid).ToList())
        {
            battle.List.Remove(e);
            battle.List.Free(e);
        }
        int cy = battle.Size.Height / 2;
        Put(battle, sly.Ship!, 100, cy);
        Put(battle, asteroid, battle.Size.Width - 100, cy);
        Put(battle, player.Ship!, battle.Size.Width / 2, cy + 3000);
        sly.RaceDesc!.EnergyLevel = 0;
        sly.SpecialCounter = sly.WeaponCounter = 0;
        sly.CurStatusFlags = StatusFlags.Special;
        // Its step after moving, by itself: a frame's scroll would move it
        // before the asteroid (later in the list) is.
        sly.RaceDesc.Postprocess!(sly.Ship!);
        Assert.Equal(across, sly.RaceDesc.EnergyLevel == sly.RaceDesc.MaxEnergy);
        Assert.Equal(across ? sly.RaceDesc.Characteristics.SpecialWait : 0, sly.SpecialCounter);
        Assert.Equal(across, asteroid.LifeSpan == 0 && (asteroid.StateFlags & ElementFlags.NonSolid) != 0);
    }

    [ContentTheory]
    [InlineData("mycon", 1.0, false)]
    [InlineData("mycon", 1.5, true)]
    [InlineData("androsyn", 1.0, false)]
    [InlineData("androsyn", 1.5, true)]
    [InlineData("mmrnmhrm", 1.0, false)]
    [InlineData("mmrnmhrm", 1.5, true)]
    public void ItsOwnAiMeasuresAcrossTheSeamInABiggerArena(string race, double scale, bool across)
    {
        // As ItsAiMeasuresAcrossTheSeamInABiggerArena: the race ship by the
        // arena's west edge, the player across the seam to its west. What
        // the ship's own AI decides on where the player is:
        // - the Mycon faces east, the player 200 behind it: it fires only
        //   if it thinks the player is ahead (mycon.c's own aim), as it
        //   does the long way round in UQM's arena, not in a bigger one;
        // - the Androsynth, low on energy, flies west at the player 600
        //   away: it fires its bubbles when the player is the way it flies
        //   (androsyn.c:353), across the seam in a bigger arena;
        // - the Mmrnmhrm's X-wing, the player 1,200 away flying west, away
        //   from it: it turns into the Y-wing to chase (mmrnmhrm.c), not
        //   when the long way round has the player flying at it.
        var player = Player();
        var enemy = Race(race, 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { enemy } }, 7,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        WarpIn(battle);
        Clear(battle);
        int cy = battle.Size.Height / 2;
        int gap = race switch { "mycon" => 200, "androsyn" => 600, _ => 1200 };
        Face(enemy, race == "androsyn" ? (ushort)12 : (ushort)4);
        Face(player, 0);
        Put(battle, enemy.Ship!, 100, cy);
        Put(battle, player.Ship!, battle.Size.Width - gap + 100, cy);
        var rd = enemy.RaceDesc!;
        if (race == "androsyn")
        {
            rd.EnergyLevel = 3;   // below a third: no Blazer
            enemy.Ship!.Velocity.SetVector(24, 12);   // flying west
        }
        if (race == "mmrnmhrm")
        {
            player.Ship!.Velocity.SetVector(64, 12);   // flying west, away
            player.RaceDesc!.ManeuverabilityIndex = Battle.MediumShip;   // not a slow ship
        }
        enemy.WeaponCounter = enemy.SpecialCounter = 0;
        enemy.ShipInputState = StatusFlags.None;
        enemy.OldStatusFlags = StatusFlags.None;
        var input = battle.ComputerIntelligence(enemy);
        switch (race)
        {
            case "mycon":
                Assert.Equal(!across, (input & BattleInput.Weapon) != 0);
                break;
            case "androsyn":
                Assert.Equal(across, (input & BattleInput.Weapon) != 0);
                break;
            default:
                Assert.Equal(across, (input & BattleInput.Special) != 0);
                break;
        }
    }

    [ContentTheory]
    [InlineData(1.0, false)]
    [InlineData(1.5, true)]
    public void TheChenjesusDogiChasesAcrossTheSeamInABiggerArena(double scale, bool across)
    {
        // A DOGI by the arena's west edge flying north, the player across
        // the seam to its west, 400 away (facing north, not at it): it
        // turns west at the player in a bigger arena (doggy_preprocess's
        // aim, measured the short way round), east in UQM's, as UQM's.
        var player = Player();
        var chenjesu = Race("chenjesu", 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { chenjesu } }, 7,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        WarpIn(battle);
        Clear(battle);
        int cy = battle.Size.Height / 2;
        Face(player, 0);
        Put(battle, chenjesu.Ship!, battle.Size.Width / 2, cy);
        Put(battle, player.Ship!, battle.Size.Width - 300, cy);
        chenjesu.RaceDesc!.EnergyLevel = chenjesu.RaceDesc.MaxEnergy;
        battle.Step(ship => ship == chenjesu ? BattleInput.Special : BattleInput.None);
        var doggy = Assert.Single(battle.List.Elements, e => ReferenceEquals(e.Current.FrameArray, chenjesu.RaceDesc.Special));
        Put(battle, doggy, 100, cy);
        doggy.Velocity.SetVector(32, 0);
        doggy.ThrustWait = 0;
        doggy.Target = null;
        battle.Step(_ => BattleInput.None);
        Assert.Same(player.Ship, doggy.Target);
        Assert.Equal(across ? 12 : 4, UqmMath.AngleToFacing(doggy.Velocity.TravelAngle));
    }

    [ContentTheory]
    [InlineData(1.0, 0)]
    [InlineData(1.5, 8)]
    public void TheUrQuansFighterTurnsOffThePlanetAcrossTheSeam(double scale, int facing)
    {
        // A fighter flying east touches the planet at the arena's top edge,
        // just across the seam to its north (the contact points either side
        // of it, as contact gives them): its copy turns a quarter away from
        // the planet, south, in a bigger arena; in UQM's the contact points'
        // difference is the long way round, the planet seen south, and it
        // turns north, as UQM's.
        var player = Player();
        var urquan = Race("urquan", 1);
        var battle = new Battle(Arena(), new[] { new[] { player }, new[] { urquan } }, 7,
            size: ArenaSize.Scaled(scale), maxElements: 300);
        WarpIn(battle);
        Clear(battle, keepPlanet: true);
        var planet = battle.List.Elements.Single(e => Battle.GravityMass(e.MassPoints));
        battle.Step(ship => ship == urquan ? BattleInput.Special : BattleInput.None);
        var fighter = battle.List.Elements.First(e => ReferenceEquals(e.Current.FrameArray, urquan.RaceDesc!.Special));
        fighter.Velocity.SetVector(32, 4);   // east
        int x = Battle.WorldToDisplay((short)(battle.Size.Width / 2)), h = Battle.WorldToDisplay((short)battle.Size.Height);
        var before = battle.List.Elements.ToHashSet();
        fighter.CollisionFunc!(fighter, ((short)x, 1), planet, ((short)(x + 1), (short)(h - 2)));
        var copy = Assert.Single(battle.List.Elements, e => !before.Contains(e));
        Assert.Equal(facing, copy.Next.Frame.Index);
        Assert.Equal(facing * 4, copy.Velocity.TravelAngle);
        Assert.True(fighter.LifeSpan == 0 && (fighter.StateFlags & ElementFlags.NonSolid) != 0, "the fighter itself is gone");
    }
    [ContentTheory]
    [InlineData("chenjesu")]
    [InlineData("blackurq")]
    public void ItsAiHoldsOnlyItsOwnShot(string race)
    {
        // Two players fly it, the AI pressing the keys of the one whose
        // weapon was held a frame ago (the Chenjesu's crystal, the Kohr-Ah's
        // blade, flying on while held): only its teammate has a shot out,
        // flying at the Ilwrath ahead. The AI takes no teammate's shot for
        // its own (UQM's side has one ship): it has none out, so it lets
        // the key go.
        var (battle, teammate, ilwrath) = Lab(race, BattleReplay.Ships[race].Crew, enemyAt: 1200);
        var ship = Race(race, 0);
        Assert.True(battle.Join(ship));
        WarpIn(battle);
        Clear(battle);
        int cx = battle.Size.Width / 2, cy = battle.Size.Height / 2;
        Face(ship, 4);
        Face(teammate, 4);
        Put(battle, ship.Ship!, cx, cy + 400);
        Put(battle, teammate.Ship!, cx, cy);
        Put(battle, ilwrath.Ship!, cx + 1200, cy);
        Assert.Same(ship.Ship, battle.List.Head);   // its shots would be after it: the Kohr-Ah's AI looks there
        battle.Step(s => s == teammate ? BattleInput.Weapon : BattleInput.None);
        battle.Step(s => s == teammate ? BattleInput.Weapon : BattleInput.None);
        var shot = Assert.Single(battle.List.Elements, e => e.Parent == teammate && (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                             && (e.StateFlags & ElementFlags.NonSolid) == 0);
        Assert.Same(ship.RaceDesc!.Weapon, shot.Next.FrameArray);   // its sheets are the ship's too
        Put(battle, ship.Ship!, cx, cy + 400);
        ship.WeaponCounter = 0;
        ship.OldStatusFlags = ship.ShipInputState = StatusFlags.Weapon;
        Assert.Equal(BattleInput.None, battle.ComputerIntelligence(ship) & BattleInput.Weapon);
    }
    // ---- Paths UQM's traces don't reach ----

    [ContentFact]
    public void TheUtwigsUnpaidShieldTakesAwayItsDefences()
    {
        // The special held with energy: the shield is up (drawn filled, its
        // life span over a ship's), its point and shield defence kept for
        // the AI. Without energy: no shield, and the AI no longer sees
        // either defence (utwig_preprocess).
        foreach (var (energy, shielded) in new[] { (5, true), (0, false) })
        {
            var (battle, utwig, _) = Lab("utwig", 20);
            var rd = utwig.RaceDesc!;
            rd.EnergyLevel = (byte)energy;
            battle.Step(ship => ship == utwig ? BattleInput.Special : BattleInput.None);
            const ShipFlags defences = ShipFlags.PointDefense | ShipFlags.ShieldDefense;
            Assert.Equal(shielded ? defences : 0, rd.ShipFlags & defences);
            Assert.Equal(shielded ? PrimType.StampFill : PrimType.Stamp, utwig.Ship!.Prim);
            Assert.Equal(shielded ? Battle.NormalLife + 1 : Battle.NormalLife, utwig.Ship.LifeSpan);
        }
    }

    [ContentFact]
    public void TheUtwigsAiShieldsOnlyWithEnergy()
    {
        // The Ilwrath's flames coming at it: with energy its AI raises the
        // shield (three times in four); with none it never tries.
        foreach (var (energy, shields) in new[] { (5, true), (0, false) })
        {
            var (battle, utwig, ilwrath) = Lab("utwig", 20, enemyAt: 260);
            Face(ilwrath, 12);
            int raised = 0;
            for (int frame = 0; frame < 8; ++frame)
            {
                battle.Step(ship => ship == ilwrath ? BattleInput.Weapon : BattleInput.None);
                utwig.RaceDesc!.EnergyLevel = (byte)energy;
                utwig.SpecialCounter = 0;
                if ((battle.ComputerIntelligence(utwig) & BattleInput.Special) != 0)
                    ++raised;
            }
            Assert.Equal(shields, raised > 0);
        }
    }

    [ContentFact]
    public void TheMyconGrowsCrewOnlyToItsMost()
    {
        // All its energy for up to 4 crew: from 12, 16; from 18, only 20.
        foreach (var (crew, after) in new[] { (12, 16), (18, 20) })
        {
            var (battle, mycon, _) = Lab("mycon", crew);
            battle.Step(ship => ship == mycon ? BattleInput.Special : BattleInput.None);
            Assert.Equal(after, mycon.RaceDesc!.CrewLevel);
            Assert.Equal(0, mycon.RaceDesc.EnergyLevel);
        }
    }

    [ContentFact]
    public void TheMmrnmhrmsTransformSwapsItsCharacteristics()
    {
        // Into the Y-wing: its characteristics the Y-wing's (turning every
        // 15 frames), a long reach and a seeking weapon, and its
        // manoeuvrability to be worked out afresh by the AI; back again,
        // the X-wing's.
        var (battle, mm, _) = Lab("mmrnmhrm", 20);
        var rd = mm.RaceDesc!;
        rd.ManeuverabilityIndex = 77;
        battle.Step(ship => ship == mm ? BattleInput.Special : BattleInput.None);
        Assert.Same(rd.Special, mm.Ship!.Current.FrameArray);
        Assert.Equal(14, rd.Characteristics.TurnWait);
        Assert.Equal(0, rd.ManeuverabilityIndex);
        Assert.Equal(Battle.LongRangeWeapon - 1, rd.WeaponRange);
        Assert.Equal(ShipFlags.SeekingWeapon, rd.ShipFlags & (ShipFlags.SeekingWeapon | ShipFlags.ImmediateWeapon));
        rd.EnergyLevel = rd.MaxEnergy;
        mm.CurStatusFlags |= StatusFlags.ShipAtMaxSpeed;
        battle.Step(ship => ship == mm ? BattleInput.Special : BattleInput.None);
        Assert.Same(rd.Ship, mm.Ship.Current.FrameArray);
        Assert.Equal(2, rd.Characteristics.TurnWait);
        Assert.Equal(Battle.CloseRangeWeapon, rd.WeaponRange);
        Assert.Equal(ShipFlags.ImmediateWeapon, rd.ShipFlags & (ShipFlags.SeekingWeapon | ShipFlags.ImmediateWeapon));
    }

    [ContentFact]
    public void TheMmrnmhrmsLasersPassItsOwnShip()
    {
        // Its X-wing's lasers touching its own ship do nothing
        // (twin_laser_collision); touching the Ilwrath they hurt it.
        var (battle, mm, ilwrath) = Lab("mmrnmhrm", 20);
        var lasers = mm.RaceDesc!.InitWeapon!(mm.Ship!).Select(l => l!).ToList();
        Assert.Equal(2, lasers.Count);
        Assert.All(lasers, l => Assert.Equal(PrimType.Line, l.Prim));
        var at = ((short)0, (short)0);
        lasers[0].CollisionFunc!(lasers[0], at, mm.Ship!, at);
        Assert.Equal(20, mm.RaceDesc!.CrewLevel);
        lasers[1].CollisionFunc!(lasers[1], at, ilwrath.Ship!, at);
        Assert.Equal(21, ilwrath.RaceDesc!.CrewLevel);
    }

    [ContentFact]
    public void TheChenjesusCountAboveOneHoldsBackItsDogi()
    {
        // Its special with all its energy: a DOGI comes out with its counter
        // at 0; at 2 (only UQM's standard computer opponent leaves it so)
        // the counter is pushed past the most DOGI, and none does.
        foreach (var (counter, released) in new[] { (0, true), (2, false) })
        {
            var (battle, chenjesu, _) = Lab("chenjesu", 36);
            chenjesu.SpecialCounter = (byte)counter;
            battle.Step(ship => ship == chenjesu ? BattleInput.Special : BattleInput.None);
            Assert.Equal(released, battle.List.Elements.Any(e => ReferenceEquals(e.Current.FrameArray, chenjesu.RaceDesc!.Special)));
        }
    }

    [ContentFact]
    public void TheUrQuanLaunchesAFighterACrewmanWhileItHasCrewToSpare()
    {
        // Two fighters from 3 crew (1 left); one from 2 (1 left); none from 1.
        foreach (var (crew, fighters) in new[] { (3, 2), (2, 1), (1, 0) })
        {
            var (battle, urquan, _) = Lab("urquan", crew);
            battle.Step(ship => ship == urquan ? BattleInput.Special : BattleInput.None);
            Assert.Equal(fighters, battle.List.Elements.Count(e => ReferenceEquals(e.Current.FrameArray, urquan.RaceDesc!.Special)));
            Assert.Equal(crew - fighters, urquan.RaceDesc!.CrewLevel);
        }
    }

    [ContentFact]
    public void TheUrQuansFighterGoneIsUncounted()
    {
        // A fighter hitting the Ilwrath is gone, and takes itself off the
        // count of fighters the AI reads (its special wait).
        var (battle, urquan, ilwrath) = Lab("urquan", 42);
        battle.Step(ship => ship == urquan ? BattleInput.Special : BattleInput.None);
        var fighter = battle.List.Elements.First(e => ReferenceEquals(e.Current.FrameArray, urquan.RaceDesc!.Special));
        var rd = urquan.RaceDesc!;
        rd.Characteristics.SpecialWait = 5;
        var at = ((short)0, (short)0);
        fighter.CollisionFunc!(fighter, at, ilwrath.Ship!, at);
        Assert.Equal(4, rd.Characteristics.SpecialWait);
        Assert.Equal(0, fighter.LifeSpan);
    }

    [ContentFact]
    public void TheUrQuansAiLaunchesWithCrewToSpareAtAShipWithoutPointDefence()
    {
        // The Ilwrath near: with all its crew the AI launches fighters; with
        // a quarter of its crew, or against a ship with point defence, it
        // doesn't. Each look starts the fighters' count again.
        foreach (var (crew, pointDefence, launches) in new[] { (42, false, true), (10, false, false), (42, true, false) })
        {
            var (battle, urquan, ilwrath) = Lab("urquan", crew, enemyAt: 1200);
            if (pointDefence)
                ilwrath.RaceDesc!.ShipFlags |= ShipFlags.PointDefense;
            urquan.RaceDesc!.Characteristics.SpecialWait = 3;
            Assert.Equal(launches, (battle.ComputerIntelligence(urquan) & BattleInput.Special) != 0);
            Assert.Equal(0, urquan.RaceDesc.Characteristics.SpecialWait);
        }
    }

    [ContentFact]
    public void TheKohrAhKeepsEightBlades()
    {
        // Each blade counts itself as it moves: the ninth counted goes.
        // A ship without crew counts none, and none go.
        foreach (var (counted, crew, goes) in new[] { (7, 42, false), (8, 42, true), (8, 0, false) })
        {
            var (battle, kohrah, _) = Lab("blackurq", 42);
            battle.Step(ship => ship == kohrah ? BattleInput.Weapon : BattleInput.None);
            var saw = battle.List.Elements.Single(e => e.Parent == kohrah && (e.StateFlags & ElementFlags.PlayerShip) == 0
                                                       && e.LifeSpan > 0);
            kohrah.RaceDesc!.Characteristics.SpecialWait = (byte)counted;
            kohrah.Ship!.CrewLevel = (ushort)crew;
            saw.PreprocessFunc!(saw);
            Assert.Equal(goes, (saw.StateFlags & ElementFlags.Disappearing) != 0);
        }
    }

    [ContentFact]
    public void TheKohrAhsStoppedBladeDriftsOnlyAtAShipInReach()
    {
        // A blade let go stops, then drifts at the Ilwrath if it's within
        // ACTIVATE_RANGE (224 pixels); one farther off stays still.
        foreach (var (at, drifts) in new[] { (900, true), (2400, false) })
        {
            var (battle, kohrah, _) = Lab("blackurq", 42, enemyAt: at);
            battle.Step(ship => ship == kohrah ? BattleInput.Weapon : BattleInput.None);
            for (int frame = 0; frame < 20; ++frame)
                battle.Step(_ => BattleInput.None);
            var saw = battle.List.Elements.Single(e => e.Parent == kohrah && (e.StateFlags & ElementFlags.PlayerShip) == 0);
            Assert.Equal(drifts, !saw.Velocity.IsZero);
        }
    }
    // A ship's own AI given what tactical_intelligence would have found:
    // the enemy ship (the Ilwrath) and, as its weapon, the element given,
    // with their move states and distances. Returns the concerns after.
    private static EvaluateDesc[] OwnAi(Battle battle, Starship ship, Element enemy, MoveState enemyState,
        ushort enemyTurns, Element? weapon = null, MoveState weaponState = MoveState.NoMovement, ushort weaponTurns = 0)
    {
        var concerns = new EvaluateDesc[Battle.FirstEmptyIndex + 1];
        for (int i = 0; i <= Battle.FirstEmptyIndex; ++i)
            concerns[i] = new EvaluateDesc { MoveState = MoveState.NoMovement, WhichTurn = ushort.MaxValue };
        concerns[Battle.EnemyShipIndex] = new EvaluateDesc { Object = enemy, MoveState = enemyState, WhichTurn = enemyTurns };
        if (weapon != null)
            concerns[Battle.EnemyWeaponIndex] = new EvaluateDesc { Object = weapon, MoveState = weaponState, WhichTurn = weaponTurns };
        if (ship.RaceDesc!.ManeuverabilityIndex == 0)
            ship.RaceDesc.ManeuverabilityIndex = Battle.MediumShip;
        ship.RaceDesc.Intelligence!(ship.Ship!.Copy(), concerns, Battle.FirstEmptyIndex);
        return concerns;
    }

    [ContentFact]
    public void TheMyconDoesntThrustAfterWhatItPursues()
    {
        // Enticed by something lasting (the Ilwrath itself as the shot to
        // mind), the Mycon pursues it, but doesn't thrust at it ("don't
        // pursue seekers").
        var (battle, mycon, ilwrath) = Lab("mycon", 20, enemyAt: 2000);
        OwnAi(battle, mycon, ilwrath.Ship!, MoveState.Pursue, 31, ilwrath.Ship, MoveState.Entice, 31);
        Assert.Equal(StatusFlags.None, mycon.ShipInputState & StatusFlags.Thrust);
    }

    [ContentFact]
    public void TheChenjesuWaitsAfterLettingGoAtAShot()
    {
        // A DOGI just out (the counter 1), its crystal key held a frame ago
        // and no crystal out, a shot it's enticed by 4 frames off: it lets
        // the key go and waits 3 frames to fire again.
        var (battle, chenjesu, ilwrath) = Lab("chenjesu", 36, enemyAt: 2000);
        chenjesu.SpecialCounter = 1;
        chenjesu.OldStatusFlags = chenjesu.ShipInputState = StatusFlags.Weapon;
        OwnAi(battle, chenjesu, ilwrath.Ship!, MoveState.Pursue, 31, ilwrath.Ship, MoveState.Entice, 4);
        Assert.Equal(StatusFlags.None, chenjesu.ShipInputState & StatusFlags.Weapon);
        Assert.Equal(3, chenjesu.WeaponCounter);
    }

    [ContentFact]
    public void TheAndrosynthLowOnEnergyEnticesTheShipNear()
    {
        // The enemy ship within 16 frames: with energy left the AI leaves
        // it pursued; below a third of its energy, it entices it.
        foreach (var (energy, state) in new[] { (24, MoveState.Pursue), (5, MoveState.Entice) })
        {
            var (battle, andro, ilwrath) = Lab("androsyn", 20, enemyAt: 2000);
            andro.RaceDesc!.EnergyLevel = (byte)energy;
            andro.SpecialCounter = 0;
            ilwrath.RaceDesc!.WeaponRange = Battle.LongRangeWeapon;   // neither close nor to be rammed
            ilwrath.RaceDesc.ManeuverabilityIndex = Battle.SlowShip;
            var concerns = OwnAi(battle, andro, ilwrath.Ship!, MoveState.Pursue, 10);
            Assert.Equal(state, concerns[Battle.EnemyShipIndex].MoveState);
        }
    }

    [ContentFact]
    public void TheUtwigPursuesOnlyWithEnergy()
    {
        // An enemy ship without an immediate weapon: with energy the AI
        // pursues it; without, it leaves it as it was found.
        foreach (var (energy, state) in new[] { (5, MoveState.Pursue), (0, MoveState.Avoid) })
        {
            var (battle, utwig, ilwrath) = Lab("utwig", 20, enemyAt: 2000);
            utwig.RaceDesc!.EnergyLevel = (byte)energy;
            var concerns = OwnAi(battle, utwig, ilwrath.Ship!, MoveState.Avoid, 31);
            Assert.Equal(state, concerns[Battle.EnemyShipIndex].MoveState);
        }
    }
}
