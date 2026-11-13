// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/thradd/thradd.c: init_thraddash, thraddash_intelligence,
// flame_napalm_preprocess, initialize_horn, thraddash_preprocess),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// thradd.c: the Thraddash Torch. Its weapon is an ion blaster ahead; its
/// special, an afterburner: held, the ship thrusts harder and faster than
/// its engines can, each frame paid for and leaving a still flame of napalm
/// behind that burns what touches it and fades.
/// <para>
/// Its AI pursues a slow or far enemy ship and entices a fast near one,
/// and burns to flee a shot or to close in.
/// </para>
/// </summary>
public sealed class ThraddashShip : IShipCode
{
    public const string Id = "thradd";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _horn, _napalm;

    /// <param name="description">The Thraddash's description (data/ships.json's thradd).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="horn">Its blaster's sheets (ship_data.weapon).</param>
    /// <param name="napalm">Its afterburner's flames' sheets (ship_data.special).</param>
    public ThraddashShip(ShipDescription description, Views? hull, Views? horn, Views? napalm)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _horn = horn;
        _napalm = napalm;
    }

    /// <summary>init_thraddash: thraddash_desc with its sheets and thradd.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _horn, _napalm);
        var horn = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("THRADDASH_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        int decayRate = d.Define("NAPALM_DECAY_RATE"), fades = d.Define("NUM_NAPALM_FADES");
        var napalm = new MissileBlock
        {
            Face = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Speed = 0,
            HitPoints = (short)d.Define("NAPALM_HITS"), Damage = (short)d.Define("NAPALM_DAMAGE"),
            Life = (ushort)d.Define("NAPALM_LIFE"), BlastOffs = (short)d.Define("NAPALM_OFFSET"),
            PreprocessFunc = e => FlameNapalmPreprocess(e, decayRate, fades),
        };
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"), weaponEnergyCost = d.Define("WEAPON_ENERGY_COST"),
            specialThrustIncrement = d.Define("SPECIAL_THRUST_INCREMENT"), specialMaxThrust = d.Define("SPECIAL_MAX_THRUST"),
            stationarySpeed = d.Define("STATIONARY_SPEED");
        rd.Preprocess = e => Preprocess(battle, e, napalm, specialEnergyCost, specialThrustIncrement, specialMaxThrust,
            decayRate);
        rd.InitWeapon = e => InitializeHorn(battle, e, horn);
        rd.Intelligence = (ship, concerns, count) =>
            Intelligence(battle, ship, concerns, count, stationarySpeed, weaponEnergyCost, specialEnergyCost);
        return rd;
    }

    private const int Octant = UqmMath.Quadrant >> 1;

    // thraddash_intelligence: pursuing an enemy ship far (over 8 frames) or
    // nearly still, enticing it otherwise; with the afterburner ready, it
    // burns away from a shot it's enticed by, unless turning towards it;
    // pursuing, it burns ahead with the energy to fire too, not turning,
    // and not already at full burn; enticing, it burns when far and not
    // turning, or when near with the enemy behind and coming at it. Burning,
    // it doesn't thrust.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        int stationarySpeed, int weaponEnergyCost, int specialEnergyCost)
    {
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object != null)
        {
            var (dx, dy) = enemy.Object.Velocity.Current();
            if (enemy.WhichTurn > 8 || (long)dx * dx + (long)dy * dy <= (long)stationarySpeed * stationarySpeed)
                enemy.MoveState = MoveState.Pursue;
            else
                enemy.MoveState = MoveState.Entice;
        }
        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        if (starShip.SpecialCounter != 0)
            return;
        starShip.ShipInputState &= ~StatusFlags.Special;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        const StatusFlags turning = StatusFlags.Left | StatusFlags.Right;
        if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
        {
            if ((starShip.ShipInputState & StatusFlags.Thrust) != 0
                || (ship.TurnWait == 0 && (starShip.ShipInputState & turning) == 0)
                || UqmMath.NormalizeFacing(UqmMath.AngleToFacing(weapon.Object.Velocity.TravelAngle + UqmMath.HalfCircle + Octant)
                                           - starShip.ShipFacing) > UqmMath.AngleToFacing(UqmMath.Quadrant))
                starShip.ShipInputState |= StatusFlags.Special;
        }
        else if (enemy.Object != null)
        {
            if (enemy.MoveState == MoveState.Pursue)
            {
                if (starShip.RaceDesc!.EnergyLevel >= weaponEnergyCost + specialEnergyCost
                    && ship.TurnWait == 0
                    && (starShip.ShipInputState & turning) == 0
                    && ((starShip.CurStatusFlags & StatusFlags.Special) == 0
                        || (starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) == 0))
                    starShip.ShipInputState |= StatusFlags.Special;
            }
            else if (enemy.MoveState == MoveState.Entice)
            {
                // Across the seam where contact is tested so (decision 5).
                short deltaX = battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X);
                short deltaY = battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y);
                int directionAngle = UqmMath.ArcTan(deltaX, deltaY);

                if ((enemy.WhichTurn > 24 && (starShip.ShipInputState & turning) == 0)
                    || (enemy.WhichTurn <= 16
                        && UqmMath.NormalizeAngle(directionAngle
                                                  - (UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle)
                                                  + UqmMath.Quadrant) <= UqmMath.HalfCircle
                        && (enemy.WhichTurn < 12
                            || UqmMath.NormalizeAngle(directionAngle
                                                      - (enemy.Object.Velocity.TravelAngle + UqmMath.HalfCircle)
                                                      + (Octant + 2)) <= (Octant + 2) << 1)))
                    starShip.ShipInputState |= StatusFlags.Special;
            }
        }

        if ((starShip.ShipInputState & StatusFlags.Special) != 0
            && starShip.RaceDesc!.EnergyLevel >= specialEnergyCost)
            starShip.ShipInputState &= ~StatusFlags.Thrust;
    }

    // flame_napalm_preprocess: still; on its first step it becomes solid
    // and drawn; then every NAPALM_DECAY_RATE + 1 frames it fades a frame,
    // held at its brightest fade while it has long to live.
    private static void FlameNapalmPreprocess(Element e, int decayRate, int fades)
    {
        e.Velocity.Zero();

        if ((e.StateFlags & ElementFlags.NonSolid) != 0)
        {
            e.StateFlags &= ~ElementFlags.NonSolid;
            e.StateFlags |= ElementFlags.Appearing;
            e.Prim = PrimType.Stamp;

            Battle.InitIntersectStartPoint(e);
            Battle.InitIntersectEndPoint(e);
            Battle.InitIntersectFrame(e);
        }
        else if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            if (e.LifeSpan <= fades * (decayRate + 1) || e.Current.Frame.Index != fades)
                e.Next.Frame = e.Current.Frame.Dec();
            else if (e.LifeSpan > fades * (decayRate + 1))
                e.Next.Frame = e.Current.Frame.SetAbs(e.Current.Frame.Count - 1);

            e.TurnWait = (byte)decayRate;
            e.StateFlags |= ElementFlags.Changing;
        }
    }

    // initialize_horn: an ion blast ahead.
    private static IReadOnlyList<Element?> InitializeHorn(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        return new[] { battle.InitializeMissile(block) };
    }

    // thraddash_preprocess: after the afterburner, a ship at top speed is
    // beyond its own; held and paid for, it thrusts with the afterburner's
    // increment and top speed (the thrust's wait taken), and leaves a flame
    // at the list's head, not drawn and non-solid until its first step.
    private static void Preprocess(Battle battle, Element e, MissileBlock block, int specialEnergyCost,
        int specialThrustIncrement, int specialMaxThrust, int decayRate)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) == 0)
        {
            if ((starShip.OldStatusFlags & StatusFlags.Special) != 0
                && (starShip.CurStatusFlags & StatusFlags.ShipAtMaxSpeed) != 0)
                starShip.CurStatusFlags |= StatusFlags.ShipBeyondMaxSpeed;
        }
        else if (battle.DeltaEnergy(e, (short)-specialEnergyCost))
        {
            var rd = starShip.RaceDesc!;
            if ((starShip.OldStatusFlags & StatusFlags.Special) == 0)
                starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);

            if (e.ThrustWait == 0)
                ++e.ThrustWait;

            var c = rd.Characteristics;
            ushort thrustIncrement = c.ThrustIncrement, maxThrust = c.MaxThrust;
            c.ThrustIncrement = (ushort)specialThrustIncrement;
            c.MaxThrust = (ushort)specialMaxThrust;
            var thrustStatus = Battle.InertialThrust(e);
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed | StatusFlags.ShipInGravityWell);
            starShip.CurStatusFlags |= thrustStatus;
            c.ThrustIncrement = thrustIncrement;
            c.MaxThrust = maxThrust;

            block.Cx = e.Next.X;
            block.Cy = e.Next.Y;
            block.FrameArray = rd.Special!;
            block.Index = (ushort)(rd.Special![0].Count - 1);
            block.Sender = e.PlayerNr;
            var trail = battle.InitializeMissile(block);
            if (trail != null)
            {
                trail.Parent = starShip;
                trail.Target = null;
                trail.TurnWait = (byte)decayRate;
                trail.StateFlags |= ElementFlags.NonSolid;
                trail.Prim = PrimType.None;
                // Usually done in its preprocess, but at the head it may never be preprocessed.
                trail.Next = trail.Current;
                trail.StateFlags |= ElementFlags.PreProcess;
                battle.List.Insert(trail, battle.List.Head);
                // Its sound.
            }
        }
    }
}
