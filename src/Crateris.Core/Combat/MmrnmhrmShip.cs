// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/mmrnmhrm/mmrnmhrm.c: init_mmrnmhrm, missile_preprocess,
// mmrnmhrm_intelligence, twin_laser_collision, initialize_dual_weapons,
// mmrnmhrm_postprocess, mmrnmhrm_preprocess, uninit_mmrnmhrm), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// mmrnmhrm.c: the Mmrnmhrm X-Form. Its special transforms it between two
/// ships with their own characteristics: the X-wing, nimble, with twin
/// lasers; and the Y-wing (its special sheets), fast but slow to turn,
/// with twin torpedoes that home in on the nearest opposing ship
/// (<see cref="Battle.TrackShip"/>).
/// <para>
/// Its AI transforms to close in or get away as the enemy ship moves, and
/// as the Y-wing fires whenever it isn't transforming.
/// </para>
/// </summary>
public sealed class MmrnmhrmShip : IShipCode
{
    public const string Id = "mmrnmhrm";

    private static readonly int LaserColor = Battle.Rgb15(0x1F, 0x0A, 0x0A);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _torpedo, _yWing;

    /// <param name="description">The Mmrnmhrm's description (data/ships.json's mmrnmhrm).</param>
    /// <param name="hull">The X-wing's sheets (ship_data.ship).</param>
    /// <param name="torpedo">The Y-wing's torpedoes' sheets (ship_data.weapon).</param>
    /// <param name="yWing">The Y-wing's sheets (ship_data.special).</param>
    public MmrnmhrmShip(ShipDescription description, Views? hull, Views? torpedo, Views? yWing)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _torpedo = torpedo;
        _yWing = yWing;
    }

    /// <summary>
    /// init_mmrnmhrm: mmrnmhrm_desc (the X-wing's characteristics) with its
    /// sheets and mmrnmhrm.c's battle code, the Y-wing's characteristics
    /// kept aside (its MMRNMHRM_DATA) for the transformation to swap in.
    /// </summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _torpedo, _yWing);
        Characteristics? otherWing = new()
        {
            MaxThrust = (ushort)d.Define("YWING_MAX_THRUST"), ThrustIncrement = (ushort)d.Define("YWING_THRUST_INCREMENT"),
            EnergyRegeneration = (byte)d.Define("YWING_ENERGY_REGENERATION"),
            WeaponEnergyCost = (byte)d.Define("YWING_WEAPON_ENERGY_COST"),
            SpecialEnergyCost = (byte)d.Define("YWING_SPECIAL_ENERGY_COST"), EnergyWait = (byte)d.Define("YWING_ENERGY_WAIT"),
            TurnWait = (byte)d.Define("YWING_TURN_WAIT"), ThrustWait = (byte)d.Define("YWING_THRUST_WAIT"),
            WeaponWait = (byte)d.Define("YWING_WEAPON_WAIT"), SpecialWait = (byte)d.Define("YWING_SPECIAL_WAIT"),
            ShipMass = (byte)d.Define("SHIP_MASS"),
        };
        int missileSpeed = d.Define("MISSILE_SPEED"), trackWait = d.Define("TRACK_WAIT");
        var torpedo = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Speed = (short)missileSpeed,
            HitPoints = (short)d.Define("MISSILE_HITS"), Damage = (short)d.Define("MISSILE_DAMAGE"),
            Life = (ushort)d.Define("MISSILE_LIFE"), BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => MissilePreprocess(battle, e, missileSpeed, trackWait),
        };
        int centerOffs = d.Define("CENTER_OFFS"), wingOffs = d.Define("WING_OFFS"), laserRange = d.Define("LASER_RANGE"),
            launchOffs = d.Define("LAUNCH_OFFS");
        rd.Preprocess = e => Preprocess(battle, e);
        rd.Postprocess = e => Postprocess(e, ref otherWing);
        rd.InitWeapon = e => InitializeDualWeapons(battle, e, torpedo, centerOffs, wingOffs, laserRange, launchOffs,
            trackWait);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        rd.Uninit = _ => otherWing = null;
        return rd;
    }

    // missile_preprocess: every TRACK_WAIT + 1 frames, a step towards its target.
    private static void MissilePreprocess(Battle battle, Element e, int speed, int trackWait)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            ushort facing = e.Next.Frame.Index;
            if (battle.TrackShip(e, ref facing) > 0)
            {
                e.Next.Frame = e.Next.Frame.SetAbs(facing);
                e.StateFlags |= ElementFlags.Changing;
                e.Velocity.SetVector(speed, facing);
            }

            e.TurnWait = (byte)trackWait;
        }
    }

    // mmrnmhrm_intelligence: the stock AI; then, able to transform (ready
    // and with the energy), not firing, with an enemy ship: the X-wing
    // turns into the Y-wing when the enemy is beyond 8 frames and slow, or
    // moving away from it; the Y-wing turns back when the enemy is within
    // 8 frames, or within 16 and closing, or beyond 32 and moving away.
    // The Y-wing fires whenever it isn't transforming.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        bool canTransform = starShip.SpecialCounter == 0
                            && rd.EnergyLevel >= rd.Characteristics.SpecialEnergyCost;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        var enemyStarShip = enemy.Object?.Parent;

        battle.ShipIntelligence(ship, concerns, concernCounter);

        starShip.ShipInputState &= ~StatusFlags.Special;
        if (canTransform && enemy.Object != null && (starShip.ShipInputState & StatusFlags.Weapon) == 0)
        {
            short deltaX, deltaY;
            int travelAngle, directionAngle;
            (deltaX, deltaY) = enemy.Object.Velocity.Current();
            if (deltaX == 0 && deltaY == 0)
                directionAngle = travelAngle = 0;
            else
            {
                // Across the seam where contact is tested so (decision 5).
                deltaX = battle.SeamDeltaX(enemy.Object.Current.X - ship.Current.X);
                deltaY = battle.SeamDeltaY(enemy.Object.Current.Y - ship.Current.Y);
                directionAngle = UqmMath.ArcTan((short)-deltaX, (short)-deltaY);
                travelAngle = enemy.Object.Velocity.TravelAngle;
            }

            const int octant = UqmMath.Quadrant / 2;
            if (ReferenceEquals(ship.Next.FrameArray, rd.Ship))
            {
                if (enemy.WhichTurn > 8
                    && (enemyStarShip!.RaceDesc!.ManeuverabilityIndex <= Battle.SlowShip
                        || UqmMath.NormalizeAngle(directionAngle - travelAngle + UqmMath.Quadrant) > UqmMath.HalfCircle))
                    starShip.ShipInputState |= StatusFlags.Special;
            }
            else
            {
                var (shipDx, shipDy) = ship.Velocity.Current();
                deltaX -= shipDx;
                deltaY -= shipDy;
                travelAngle = UqmMath.ArcTan(deltaX, deltaY);
                if (enemy.WhichTurn < 16)
                {
                    if (enemy.WhichTurn <= 8
                        || UqmMath.NormalizeAngle(directionAngle - travelAngle + octant) <= UqmMath.Quadrant)
                        starShip.ShipInputState |= StatusFlags.Special;
                }
                else if (enemy.WhichTurn > 32
                         && UqmMath.NormalizeAngle(directionAngle - travelAngle + UqmMath.Quadrant) > UqmMath.HalfCircle)
                    starShip.ShipInputState |= StatusFlags.Special;
            }
        }

        if (ReferenceEquals(ship.Current.FrameArray, rd.Special))
        {
            if ((starShip.ShipInputState & StatusFlags.Special) == 0 && enemy.Object != null)
                starShip.ShipInputState |= StatusFlags.Weapon;
            else
                starShip.ShipInputState &= ~StatusFlags.Weapon;
        }
    }

    // twin_laser_collision: the lasers pass by their own ship.
    private static void TwinLaserCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1)
    {
        if ((e1.StateFlags & ElementFlags.PlayerShip) == 0 || e0.PlayerNr != e1.PlayerNr)
            battle.WeaponCollision(e0, at0, e1, at1);
    }

    // initialize_dual_weapons: the X-wing's two lasers, either side of its
    // nose; the Y-wing's two torpedoes, each turned out a facing.
    private static IReadOnlyList<Element?> InitializeDualWeapons(Battle battle, Element ship, MissileBlock torpedo,
        int centerOffs, int wingOffs, int laserRange, int launchOffs, int trackWait)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        ushort facing = starShip.ShipFacing;
        int angle = UqmMath.FacingToAngle(facing);
        short cx = (short)(ship.Next.X + UqmMath.Cosine(angle, centerOffs));
        short cy = (short)(ship.Next.Y + UqmMath.Sine(angle, centerOffs));

        var weapons = new Element?[2];
        if (ReferenceEquals(ship.Next.FrameArray, rd.Ship))
        {
            var laser = new LaserBlock
            {
                Sender = ship.PlayerNr, Flags = ElementFlags.None, PixOffs = 0, Color = LaserColor, Face = facing,
            };
            short ex = (short)(cx + UqmMath.Cosine(angle, laserRange));
            short ey = (short)(cy + UqmMath.Sine(angle, laserRange));
            short offsX = (short)-UqmMath.Sine(angle, wingOffs);
            short offsY = UqmMath.Cosine(angle, wingOffs);

            for (int i = 0; i < 2; ++i)
            {
                laser.Cx = (short)(i == 0 ? cx + offsX : cx - offsX);
                laser.Cy = (short)(i == 0 ? cy + offsY : cy - offsY);
                laser.Ex = (short)(ex - laser.Cx);
                laser.Ey = (short)(ey - laser.Cy);
                var l = battle.InitializeLaser(laser);
                if (l != null)
                    l.CollisionFunc = (e0, at0, e1, at1) => TwinLaserCollision(battle, e0, at0, e1, at1);
                weapons[i] = l;
            }
        }
        else
        {
            torpedo.FrameArray = rd.Weapon!;
            torpedo.Sender = ship.PlayerNr;
            torpedo.Face = torpedo.Index = UqmMath.NormalizeFacing(facing - 1);
            short offsX = (short)-UqmMath.Sine(UqmMath.FacingToAngle(torpedo.Face), launchOffs);
            short offsY = UqmMath.Cosine(UqmMath.FacingToAngle(torpedo.Face), launchOffs);

            torpedo.Cx = (short)(cx + offsX);
            torpedo.Cy = (short)(cy + offsY);
            weapons[0] = battle.InitializeMissile(torpedo);
            if (weapons[0] != null)
                weapons[0]!.TurnWait = (byte)trackWait;

            torpedo.Face = torpedo.Index = UqmMath.NormalizeFacing(facing + 1);
            torpedo.Cx = (short)(cx - offsX);
            torpedo.Cy = (short)(cy - offsY);
            weapons[1] = battle.InitializeMissile(torpedo);
            if (weapons[1] != null)
                weapons[1]!.TurnWait = (byte)trackWait;
        }
        return weapons;
    }

    // mmrnmhrm_postprocess: as it transforms (its TRANSFORM sound), the
    // weapon is ready again and the characteristics swap with the other
    // wing's; the AI looks at its manoeuvrability afresh. The Y-wing has a
    // long reach and a seeking weapon (and isn't at its top speed); the
    // X-wing a close one, immediate.
    private static void Postprocess(Element e, ref Characteristics? otherWing)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if (ReferenceEquals(e.Next.FrameArray, e.Current.FrameArray))
            return;

        starShip.WeaponCounter = 0;

        // Swap the characteristics around.
        if (otherWing == null)
            return;   // no ship data (?!)
        var temp = otherWing;
        otherWing = rd.Characteristics;
        rd.Characteristics = temp;
        rd.ManeuverabilityIndex = 0;

        if (ReferenceEquals(e.Next.FrameArray, rd.Special))
        {
            rd.WeaponRange = Battle.LongRangeWeapon - 1;
            rd.ShipFlags &= ~ShipFlags.ImmediateWeapon;
            rd.ShipFlags |= ShipFlags.SeekingWeapon;
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
        }
        else
        {
            rd.WeaponRange = Battle.CloseRangeWeapon;
            rd.ShipFlags &= ~ShipFlags.SeekingWeapon;
            rd.ShipFlags |= ShipFlags.ImmediateWeapon;
            if ((starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) != 0)
                starShip.CurStatusFlags |= StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed;
        }
    }

    // mmrnmhrm_preprocess: the special, when ready and paid for (or the
    // text flashes), transforms the ship into the other wing.
    private static void Preprocess(Battle battle, Element e)
    {
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
            return;
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 && starShip.SpecialCounter == 0)
        {
            // Either it transforms or the text flashes.
            if (battle.DeltaEnergy(e, (short)-rd.Characteristics.SpecialEnergyCost))
            {
                e.Next.FrameArray = ReferenceEquals(e.Next.FrameArray, rd.Ship) ? rd.Special : rd.Ship;
                e.Next.Frame = Frame.SetEqu(e.Next.FrameArray![0], e.Next.Frame);
                e.StateFlags |= ElementFlags.Changing;

                starShip.SpecialCounter = rd.Characteristics.SpecialWait;
            }
        }
    }
}
