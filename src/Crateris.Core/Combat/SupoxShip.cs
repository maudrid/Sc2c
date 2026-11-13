// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/supox/supox.c: init_supox, supox_intelligence,
// initialize_horn, supox_preprocess), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// supox.c: the Supox Blade. Its weapon is a glob ahead; its special,
/// held, makes thrust and turning into sideways and backwards thrust (the
/// ship keeps facing as it was), at no cost.
/// <para>
/// Its AI pursues with the special held (strafing and firing) when lined
/// up with the enemy ship near and coming at it, and side-steps a shot
/// it's dodging.
/// </para>
/// </summary>
public sealed class SupoxShip : IShipCode
{
    public const string Id = "supox";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _glob;

    /// <param name="description">The Supox's description (data/ships.json's supox).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="glob">Its weapon's sheets (ship_data.weapon).</param>
    public SupoxShip(ShipDescription description, Views? hull, Views? glob)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _glob = glob;
    }

    /// <summary>init_supox: supox_desc with its sheets and supox.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        // No special sheets (the special draws none).
        var rd = RaceShips.NewRaceDesc(d, _hull, _glob, null);
        var horn = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("SUPOX_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        rd.Preprocess = Preprocess;
        rd.InitWeapon = e => InitializeHorn(battle, e, horn);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    private const int Octant = UqmMath.Quadrant >> 1;

    // supox_intelligence: the special held, pursuing, when lined up with
    // the enemy ship within 12 frames and it faces the ship (within an
    // octant); held, thrusting and firing; and with the special ready, a
    // side-step from a shot it's avoiding, not thrusting, turning as it
    // was or a random way.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter != 0 || enemy.Object == null)
            starShip.ShipInputState &= ~StatusFlags.Special;
        else
        {
            // Across the seam where contact is tested so (decision 5).
            short deltaX = battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X);
            short deltaY = battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y);
            int directionAngle = UqmMath.ArcTan(deltaX, deltaY);

            int shipAngle = UqmMath.FacingToAngle(starShip.ShipFacing);
            bool linedUp = UqmMath.NormalizeAngle(UqmMath.NormalizeAngle(directionAngle - shipAngle) + UqmMath.Quadrant)
                           <= UqmMath.HalfCircle;

            if (!linedUp
                || enemy.WhichTurn > 20
                || UqmMath.NormalizeAngle(enemy.Facing - (shipAngle + UqmMath.HalfCircle) + Octant) > UqmMath.Quadrant)
                starShip.ShipInputState &= ~StatusFlags.Special;
            else if (linedUp && enemy.WhichTurn <= 12)
                starShip.ShipInputState |= StatusFlags.Special;

            if ((starShip.ShipInputState & StatusFlags.Special) != 0)
                enemy.MoveState = MoveState.Pursue;
        }

        battle.ShipIntelligence(ship, concerns, concernCounter);

        if ((starShip.ShipInputState & StatusFlags.Special) != 0)
            starShip.ShipInputState |= StatusFlags.Thrust | StatusFlags.Weapon;

        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (starShip.SpecialCounter == 0
            && weapon.Object != null
            && weapon.MoveState == MoveState.Avoid
            && ship.TurnWait == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Thrust;
            starShip.ShipInputState |= StatusFlags.Special;
            if ((starShip.CurStatusFlags & (StatusFlags.Left | StatusFlags.Right)) == 0)
                starShip.ShipInputState |= (StatusFlags)(1 << (int)((byte)battle.Random.Next("supox_intelligence") & 1));
            else
                starShip.ShipInputState |= starShip.CurStatusFlags & (StatusFlags.Left | StatusFlags.Right);
        }
    }

    // initialize_horn: a glob ahead.
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

    // supox_preprocess: with the special held, thrust goes backwards and
    // left or right sideways (backwards and to the side together, an octant
    // off), the ship facing as it was; the keys' waits taken as if turning
    // and thrusting.
    private static void Preprocess(Element e)
    {
        var starShip = e.Parent!;
        var flags = starShip.CurStatusFlags;
        if ((flags & StatusFlags.Special) == 0)
            return;

        int addFacing = 0;
        if ((flags & StatusFlags.Thrust) != 0)
        {
            if (e.ThrustWait == 0)
                ++e.ThrustWait;
            addFacing = UqmMath.AngleToFacing(UqmMath.HalfCircle);
        }
        if ((flags & StatusFlags.Left) != 0)
        {
            if (e.TurnWait == 0)
                ++e.TurnWait;
            if (addFacing != 0)
                addFacing += UqmMath.AngleToFacing(Octant);
            else
                addFacing = -UqmMath.AngleToFacing(UqmMath.Quadrant);
        }
        else if ((flags & StatusFlags.Right) != 0)
        {
            if (e.TurnWait == 0)
                ++e.TurnWait;
            if (addFacing != 0)
                addFacing -= UqmMath.AngleToFacing(Octant);
            else
                addFacing = UqmMath.AngleToFacing(UqmMath.Quadrant);
        }

        if (addFacing != 0)
        {
            ushort facing = starShip.ShipFacing;
            starShip.ShipFacing = UqmMath.NormalizeFacing(facing + addFacing);
            var thrustStatus = Battle.InertialThrust(e);
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed | StatusFlags.ShipInGravityWell);
            starShip.CurStatusFlags |= thrustStatus;
            starShip.ShipFacing = facing;
        }
    }
}
