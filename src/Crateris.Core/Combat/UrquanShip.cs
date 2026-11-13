// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/urquan/urquan.c: init_urquan, initialize_fusion,
// fighter_postprocess, fighter_preprocess, fighter_collision,
// spawn_fighters, urquan_intelligence, urquan_postprocess), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// urquan.c: the Ur-Quan Dreadnought. Its weapon is a fusion blast ahead;
/// its special launches two fighters (one with only two crew left), each
/// a crewman, which fly at the nearest opposing ship
/// (<see cref="Battle.TrackShip"/>), circling it and zapping it with
/// their lasers, and fly back to their own ship to come aboard again when
/// they've flown long enough or it has no crew left to fight with.
/// <para>
/// Its AI pursues the enemy ship, and launches fighters while it has crew
/// to spare and the enemy has no point defence. The fighters are its
/// SEEKING_SPECIAL shots: they take the very sheets in
/// <see cref="RaceDesc.Special"/>; the AI counts them in its special wait
/// between its looks.
/// </para>
/// </summary>
public sealed class UrquanShip : IShipCode
{
    public const string Id = "urquan";

    private static readonly int FighterLaserColor = Battle.Rgb15(0x1F, 0x1F, 0x0A);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _fusion, _fighter;

    /// <param name="description">The Ur-Quan's description (data/ships.json's urquan).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="fusion">Its fusion blast's sheets (ship_data.weapon).</param>
    /// <param name="fighter">
    /// The fighters' sheets (ship_data.special), the very instance the
    /// fighters take: its SEEKING_SPECIAL flag makes the AI tell them by it.
    /// </param>
    public UrquanShip(ShipDescription description, Views? hull, Views? fusion, Views? fighter)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _fusion = fusion;
        _fighter = fighter;
    }

    // urquan.c's #defines the fighters' code uses.
    private sealed record Fighters(int SpecialEnergyCost, int SpecialWait, int FighterOffset, int FighterSpeed,
        int OneWayFlight, int TrackThreshold, int FighterLife, int FighterHits, int FighterMass,
        int FighterWeaponWait, int FighterLaserRange);

    /// <summary>init_urquan: urquan_desc with its sheets and urquan.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _fusion, _fighter);
        var fusion = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("URQUAN_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        var f = new Fighters(d.Define("SPECIAL_ENERGY_COST"), d.Define("SPECIAL_WAIT"), d.Define("FIGHTER_OFFSET"),
            d.Define("FIGHTER_SPEED"), d.Define("ONE_WAY_FLIGHT"), d.Define("TRACK_THRESHOLD"), d.Define("FIGHTER_LIFE"),
            d.Define("FIGHTER_HITS"), d.Define("FIGHTER_MASS"), d.Define("FIGHTER_WEAPON_WAIT"),
            d.Define("FIGHTER_LASER_RANGE"));
        rd.InitWeapon = e => InitializeFusion(battle, e, fusion);
        rd.Postprocess = e => Postprocess(battle, e, f);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // initialize_fusion: a fusion blast ahead.
    private static IReadOnlyList<Element?> InitializeFusion(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        return new[] { battle.InitializeMissile(block) };
    }

    // fighter_postprocess: the fighter zaps its laser the way it saved (its
    // FIGHTER_ZAP sound), then waits to again.
    private static void FighterPostprocess(Battle battle, Element e, Fighters f)
    {
        var starShip = e.Parent!;
        var block = new LaserBlock
        {
            Cx = e.Next.X, Cy = e.Next.Y, Face = e.ThrustWait,
            Sender = e.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)f.FighterOffset,
            Color = FighterLaserColor,
        };
        block.Ex = UqmMath.Cosine(UqmMath.FacingToAngle(block.Face), f.FighterLaserRange);
        block.Ey = UqmMath.Sine(UqmMath.FacingToAngle(block.Face), f.FighterLaserRange);
        var laser = battle.InitializeLaser(block);
        if (laser != null)
        {
            laser.Parent = starShip;
            battle.List.Put(laser);
        }

        e.PostprocessFunc = null;
        e.ThrustWait = (byte)f.FighterWeaponWait;
    }

    // fighter_preprocess: each fighter counts itself in the special wait
    // (for the AI). Once clear of the ship, and not turned by a bump this
    // frame: on its way out (an odd frame, a target, or one found) it
    // ignores its own ship's things; on its way back (flown long enough,
    // or the ship without crew to fight with... its own ship's crew gone
    // means it stays out) it heads for its own ship, to be touched. With a
    // target within three quarters of its laser's reach and the laser
    // ready, it zaps after moving; on its way out it flies to a point 30
    // pixels to its side of the target's nose, circling it.
    private static void FighterPreprocess(Battle battle, Element e, Fighters f)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        ++rd.Characteristics.SpecialWait;
        if (f.FighterLife - e.LifeSpan <= f.TrackThreshold || (e.StateFlags & ElementFlags.Changing) != 0)
            return;

        bool enroute = true;
        int crew = rd.CrewLevel, life = e.LifeSpan;
        ushort facing, origFacing;
        origFacing = facing = e.Current.Frame.Index;
        if (((life & 1) != 0 || e.Target != null || battle.TrackShip(e, ref facing) >= 0)
            && (crew == 0 || life >= f.OneWayFlight))
            e.StateFlags |= ElementFlags.IgnoreSimilar;
        else if (crew != 0)
        {
            var ship = starShip.Ship!;
            short dx = battle.WrapDeltaX(ship.Current.X - e.Current.X);
            short dy = battle.WrapDeltaY(ship.Current.Y - e.Current.Y);
            facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(dx, dy)));
            e.StateFlags &= ~ElementFlags.IgnoreSimilar;
            enroute = false;
        }

        if (e.ThrustWait > 0)
            --e.ThrustWait;

        if (e.Target != null)
        {
            var target = e.Target;
            int dx = battle.WrapDeltaX(target.Current.X - e.Current.X);
            int dy = battle.WrapDeltaY(target.Current.Y - e.Current.Y);

            int reach = f.FighterLaserRange * 3 / 4;
            if (e.ThrustWait == 0
                && Math.Abs(dx) < reach && Math.Abs(dy) < reach
                && dx * dx + dy * dy < reach * reach)
            {
                e.ThrustWait = (byte)UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan((short)dx, (short)dy)));
                e.PostprocessFunc = p => FighterPostprocess(battle, p, f);
            }

            if (enroute)
            {
                int targetFacing = target.Current.Frame.Index;
                int side = (e.TurnWait & (byte)StatusFlags.Left) != 0 ? targetFacing - 4 : targetFacing + 4;
                dx += UqmMath.Cosine(UqmMath.FacingToAngle(side), Battle.DisplayToWorld(30));
                dy += UqmMath.Sine(UqmMath.FacingToAngle(side), Battle.DisplayToWorld(30));
                facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan((short)dx, (short)dy)));
            }
        }
        e.StateFlags |= ElementFlags.Changing;

        if (facing != origFacing)
            e.Next.Frame = e.Next.Frame.SetAbs(facing);
        e.Velocity.SetVector(f.FighterSpeed, facing);
    }

    // fighter_collision: a fighter hitting the planet goes on as a copy of
    // itself turned a quarter circle away along it; one hitting anything
    // not its own ship's does its damage (no blast offset); one touching
    // its own ship comes aboard (its FIGHTERS_RETURN sound). Each is then
    // gone, uncounted from the special wait.
    private static void FighterCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Fighters f)
    {
        var starShip = e0.Parent!;
        if (Battle.GravityMass(e1.MassPoints))
        {
            var fighter = battle.List.Alloc();
            if (fighter != null)
            {
                fighter.CopyFrom(e0);
                fighter.StateFlags &= ~ElementFlags.PreProcess;
                fighter.StateFlags |= ElementFlags.Changing;
                fighter.Next = fighter.Current;
                int travelFacing = fighter.Velocity.TravelAngle;
                // The contact points across the seam where contact is tested so (decision 5).
                int deltaFacing = UqmMath.NormalizeAngle(UqmMath.ArcTan(
                    battle.SeamDisplayDeltaX(at1.X - at0.X), battle.SeamDisplayDeltaY(at1.Y - at0.Y)) - travelFacing);
                if (deltaFacing == 0)
                {
                    if ((fighter.TurnWait & (byte)StatusFlags.Left) != 0)
                        travelFacing -= UqmMath.Quadrant;
                    else
                        travelFacing += UqmMath.Quadrant;
                }
                else if (deltaFacing <= UqmMath.HalfCircle)
                    travelFacing -= UqmMath.Quadrant;
                else
                    travelFacing += UqmMath.Quadrant;

                int facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.NormalizeAngle(travelFacing)));
                fighter.Next.Frame = fighter.Next.Frame.SetAbs(facing);
                fighter.Velocity.SetVector(f.FighterSpeed, facing);

                battle.List.Put(fighter);
            }

            e0.StateFlags |= ElementFlags.Disappearing | ElementFlags.Collision;
        }
        else if (e0.Parent != e1.Parent)
        {
            e0.BlastOffset = 0;
            battle.WeaponCollision(e0, at0, e1, at1);
            e0.StateFlags |= ElementFlags.Disappearing | ElementFlags.Collision;
        }
        else if ((e1.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            battle.DeltaCrew(e1, 1);
            e0.StateFlags |= ElementFlags.Disappearing | ElementFlags.Collision;
        }

        if ((e0.StateFlags & ElementFlags.Disappearing) != 0)
        {
            e0.StateFlags &= ~ElementFlags.Disappearing;

            e0.HitPoints = 0;
            e0.LifeSpan = 0;
            e0.StateFlags |= ElementFlags.NonSolid;

            --starShip.RaceDesc!.Characteristics.SpecialWait;
        }
    }

    // spawn_fighters: two fighters (one if the ship has two crew or
    // fewer), each a crewman off the ship, out of its back either side,
    // the first turning left about its target, the second right.
    private static void SpawnFighters(Battle battle, Element e, Fighters f)
    {
        var starShip = e.Parent!;
        int facing = starShip.ShipFacing + UqmMath.AngleToFacing(UqmMath.HalfCircle);
        short deltaX = UqmMath.Cosine(UqmMath.FacingToAngle(facing), Battle.DisplayToWorld(14));
        short deltaY = UqmMath.Sine(UqmMath.FacingToAngle(facing), Battle.DisplayToWorld(14));

        int i = e.CrewLevel > 2 ? 2 : 1;
        Element? fighter;
        while (i-- != 0 && (fighter = battle.List.Alloc()) != null)
        {
            battle.DeltaCrew(e, -1);

            battle.List.Put(fighter);
            fighter.HitPoints = (ushort)f.FighterHits;
            fighter.MassPoints = (byte)f.FighterMass;
            fighter.ThrustWait = (byte)(f.TrackThreshold + 1);
            fighter.PlayerNr = e.PlayerNr;
            fighter.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.CrewObject
                                 | ElementFlags.IgnoreSimilar;
            fighter.LifeSpan = (ushort)f.FighterLife;
            fighter.Prim = PrimType.Stamp;
            fighter.PreprocessFunc = p => FighterPreprocess(battle, p, f);
            fighter.PostprocessFunc = null;
            fighter.CollisionFunc = (e0, at0, e1, at1) => FighterCollision(battle, e0, at0, e1, at1, f);
            fighter.DeathFunc = null;

            fighter.Current.X = e.Next.X;
            fighter.Current.Y = e.Next.Y;
            int fighterFacing;
            if (i == 1)
            {
                fighter.TurnWait = (byte)StatusFlags.Left;
                fighterFacing = UqmMath.NormalizeFacing(facing + 2);
                fighter.Current.X += (short)(deltaX - deltaY);
                fighter.Current.Y += (short)(deltaY + deltaX);
            }
            else
            {
                fighter.TurnWait = (byte)StatusFlags.Right;
                fighterFacing = UqmMath.NormalizeFacing(facing - 2);
                fighter.Current.X += (short)(deltaX + deltaY);
                fighter.Current.Y += (short)(deltaY - deltaX);
            }
            short sx = UqmMath.Cosine(UqmMath.FacingToAngle(fighterFacing), Velocity.WorldToVelocity(f.FighterSpeed));
            short sy = UqmMath.Sine(UqmMath.FacingToAngle(fighterFacing), Velocity.WorldToVelocity(f.FighterSpeed));
            fighter.Velocity.SetComponents(sx, sy);
            fighter.Current.X -= (short)Velocity.VelocityToWorld(sx);
            fighter.Current.Y -= (short)Velocity.VelocityToWorld(sy);

            var special = starShip.RaceDesc!.Special!;
            fighter.Current.FrameArray = special;
            fighter.Current.Frame = special[0].SetAbs(fighterFacing);
            fighter.Parent = starShip;
        }
    }

    // urquan_intelligence: it pursues the enemy ship, and a shot it's
    // enticed by that lasts (or crew within 8 frames), or a heavy one 2
    // frames away with the enemy beyond 16. With its special ready, more
    // than a quarter of its crew, and an enemy ship without point defence,
    // it launches fighters when fewer than 6 are out (counted since its
    // last look), or the enemy is slow and not beyond its top speed, or
    // the enemy is within 12 frames while it turns with half its energy.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;

        concerns[Battle.EnemyShipIndex].MoveState = MoveState.Pursue;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null
            && weapon.MoveState == MoveState.Entice
            && ((weapon.Object.StateFlags & ElementFlags.CrewObject) == 0 || weapon.WhichTurn <= 8)
            && ((weapon.Object.StateFlags & ElementFlags.FiniteLife) == 0
                || (weapon.Object.MassPoints >= 4
                    && weapon.WhichTurn == 2
                    && concerns[Battle.EnemyShipIndex].WhichTurn > 16)))
            weapon.MoveState = MoveState.Pursue;

        battle.ShipIntelligence(ship, concerns, concernCounter);

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        var enemyStarShip = enemy.Object?.Parent;
        if (starShip.SpecialCounter == 0
            && enemy.Object != null
            && rd.CrewLevel > rd.MaxCrew >> 2
            && (enemyStarShip!.RaceDesc!.ShipFlags & ShipFlags.PointDefense) == 0
            && (rd.Characteristics.SpecialWait < 6
                || (enemyStarShip.RaceDesc.ManeuverabilityIndex <= Battle.SlowShip
                    && (enemyStarShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) == 0)
                || (enemy.WhichTurn <= 12
                    && (starShip.ShipInputState & (StatusFlags.Left | StatusFlags.Right)) != 0
                    && rd.EnergyLevel >= (byte)(rd.MaxEnergy >> 1))))
            starShip.ShipInputState |= StatusFlags.Special;
        else
            starShip.ShipInputState &= ~StatusFlags.Special;

        rd.Characteristics.SpecialWait = 0;
    }

    // urquan_postprocess: the special, when ready, with crew to spare and
    // paid for, launches fighters (its LAUNCH_FIGHTERS sound).
    private static void Postprocess(Battle battle, Element e, Fighters f)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && e.CrewLevel > 1
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-f.SpecialEnergyCost))
        {
            SpawnFighters(battle, e, f);
            starShip.SpecialCounter = (byte)f.SpecialWait;
        }
    }
}
