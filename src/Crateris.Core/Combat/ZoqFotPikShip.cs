// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/zoqfot/zoqfot.c: init_zoqfotpik, spit_preprocess,
// initialize_spit, tongue_postprocess, tongue_collision, spawn_tongue,
// zoqfotpik_intelligence, zoqfotpik_postprocess), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// zoqfot.c: the Zoq-Fot-Pik Stinger. Its weapon is a spray of spit that
/// slows and wobbles as it changes colour; its special, the tongue, a
/// proboscis stuck out for several frames that does its damage once,
/// whichever of its frames touches first.
/// <para>
/// Its AI sticks its tongue out when the enemy ship is near and dead
/// ahead, and otherwise sprays when the spit would hit within 20 pixels.
/// </para>
/// </summary>
public sealed class ZoqFotPikShip : IShipCode
{
    public const string Id = "zoqfot";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _spit, _tongue;

    /// <param name="description">The Zoq-Fot-Pik's description (data/ships.json's zoqfot).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="spit">Its spit's sheets (ship_data.weapon).</param>
    /// <param name="tongue">Its tongue's sheets (ship_data.special).</param>
    public ZoqFotPikShip(ShipDescription description, Views? hull, Views? spit, Views? tongue)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _spit = spit;
        _tongue = tongue;
    }

    /// <summary>init_zoqfotpik: zoqfotpik_desc with its sheets and zoqfot.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _spit, _tongue);
        int spitWait = d.Define("SPIT_WAIT");
        var spit = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("ZOQFOTPIK_OFFSET"),
            HitPoints = (short)d.Define("MISSILE_HITS"), Damage = (short)d.Define("MISSILE_DAMAGE"),
            Life = (ushort)d.Define("MISSILE_LIFE"), BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => SpitPreprocess(battle, e, spitWait),
        };
        var tongue = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Speed = (short)d.Define("TONGUE_SPEED"),
            HitPoints = (short)d.Define("TONGUE_HITS"), Damage = (short)d.Define("TONGUE_DAMAGE"), Life = 1,
            BlastOffs = (short)d.Define("TONGUE_OFFSET"),
        };
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST");
        rd.InitWeapon = e => InitializeSpit(battle, e, spit);
        rd.Postprocess = e => Postprocess(battle, e, tongue, specialEnergyCost);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // spit_preprocess: every SPIT_WAIT + 1 frames the next frame, slower
    // the further on (twice the frames left, in pixels), wobbling a little
    // at random on its second.
    private static void SpitPreprocess(Battle battle, Element e, int spitWait)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            e.Next.Frame = e.Next.Frame.Inc();
            int angle = e.Velocity.TravelAngle;
            int index = e.Next.Frame.Index;
            if (index == 1)
                angle += (int)((ushort)battle.Random.Next("spit_preprocess") % 3) - 1;

            ushort speed = (ushort)Velocity.WorldToVelocity(Battle.DisplayToWorld(e.Next.Frame.Count - index) << 1);
            e.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));

            e.TurnWait = (byte)spitWait;
            e.StateFlags |= ElementFlags.Changing;
        }
    }

    // initialize_spit: spit ahead, as fast as twice its frames in pixels.
    private static IReadOnlyList<Element?> InitializeSpit(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        var weapon = starShip.RaceDesc!.Weapon!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = weapon;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        block.Speed = (short)(Battle.DisplayToWorld(weapon[0].Count) << 1);
        return new[] { battle.InitializeMissile(block) };
    }

    // tongue_postprocess: the tongue goes on while it has frames left.
    private static void TonguePostprocess(Battle battle, Element e, MissileBlock block)
    {
        if (e.TurnWait != 0)
            SpawnTongue(battle, e, block);
    }

    // tongue_collision: the damage only from the tongue's first frame
    // touching; then the special's wait cut short by the frames it had
    // left, and the tongue stops.
    private static void TongueCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        var starShip = e0.Parent!;
        if (starShip.SpecialCounter == starShip.RaceDesc!.Characteristics.SpecialWait)
            battle.WeaponCollision(e0, at0, e1, at1);

        starShip.SpecialCounter = (byte)(starShip.SpecialCounter - e0.TurnWait);
        e0.TurnWait = 0;
        e0.StateFlags |= ElementFlags.NonSolid;
    }

    // spawn_tongue: the tongue's frame for the ship's facing, from the
    // ship (its frames left the special's wait) or from the tongue before
    // (a frame fewer, moved out by half its size along the facing).
    private static void SpawnTongue(Battle battle, Element e, MissileBlock block)
    {
        var starShip = e.Parent!;
        block.Cx = e.Next.X;
        block.Cy = e.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Special!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = e.PlayerNr;
        var tongue = battle.InitializeMissile(block);
        if (tongue == null)
            return;

        tongue.PostprocessFunc = t => TonguePostprocess(battle, t, block);
        tongue.CollisionFunc = (e0, at0, e1, at1) => TongueCollision(battle, e0, at0, e1, at1);
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
            tongue.TurnWait = starShip.SpecialCounter;
        else
        {
            tongue.TurnWait = (byte)(e.TurnWait - 1);

            // GetFrameRect: the frame's size.
            var r = tongue.Current.Frame.Shape;
            short xOffs = Battle.DisplayToWorld(r.Width >> 1);
            short yOffs = Battle.DisplayToWorld(r.Height >> 1);

            int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
            if (angle > UqmMath.HalfCircle && angle < UqmMath.FullCircle)
                tongue.Current.X -= xOffs;
            else if (angle > 0 && angle < UqmMath.HalfCircle)
                tongue.Current.X += xOffs;
            if (angle < UqmMath.Quadrant || angle > UqmMath.FullCircle - UqmMath.Quadrant)
                tongue.Current.Y -= yOffs;
            else if (angle > UqmMath.Quadrant && angle < UqmMath.FullCircle - UqmMath.Quadrant)
                tongue.Current.Y += yOffs;
        }

        tongue.Parent = starShip;
        battle.List.Put(tongue);
    }

    // zoqfotpik_intelligence: with the special ready, the tongue when the
    // enemy ship is within 4 frames (pursued) and dead ahead; the stock AI
    // with its weapon taken as waiting; then, its weapon ready and no
    // tongue to stick out, spit when it would hit the enemy ship, or a
    // shot it isn't dodging, within 20 pixels.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;

        bool giveTongueJob = false;
        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;

            ref var enemy = ref concerns[Battle.EnemyShipIndex];
            if (enemy.Object != null && enemy.WhichTurn <= 4)
            {
                giveTongueJob = true;
                enemy.MoveState = MoveState.Pursue;
                // Across the seam where contact is tested so (decision 5; zoqfot.c:299).
                short deltaX = battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X);
                short deltaY = battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y);
                if (starShip.ShipFacing == UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(deltaX, deltaY))))
                    starShip.ShipInputState |= StatusFlags.Special;
            }
        }

        ++starShip.WeaponCounter;
        battle.ShipIntelligence(ship, concerns, concernCounter);
        --starShip.WeaponCounter;

        if (starShip.WeaponCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Weapon;
            if (!giveTongueJob)
            {
                while (concernCounter-- > 0)
                {
                    ref var concern = ref concerns[concernCounter];
                    if (concern.Object != null
                        && (concernCounter == Battle.EnemyShipIndex
                            || (concernCounter == Battle.EnemyWeaponIndex && concern.MoveState != MoveState.Avoid))
                        && battle.ShipWeapons(ship, concern.Object, (ushort)Battle.DisplayToWorld(20)))
                    {
                        starShip.ShipInputState |= StatusFlags.Weapon;
                        break;
                    }
                }
            }
        }
    }

    // zoqfotpik_postprocess: the special, when ready and paid for, starts
    // the tongue (its STICK_OUT_TONGUE sound); while its wait runs, the
    // tongue's first frame comes out of the ship.
    private static void Postprocess(Battle battle, Element e, MissileBlock tongue, int specialEnergyCost)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-specialEnergyCost))
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;

        if (starShip.SpecialCounter != 0)
            SpawnTongue(battle, e, tongue);
    }
}
