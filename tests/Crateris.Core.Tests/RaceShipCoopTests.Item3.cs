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
}
