// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/slylandr/slylandr.c: init_slylandro,
// lightning_postprocess, lightning_collision, initialize_lightning,
// slylandro_intelligence, harvest_space_junk, slylandro_postprocess,
// slylandro_preprocess), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// slylandr.c: the Slylandro Probe. It flies always at its top speed and
/// turns about on the thrust key; its weapon is lightning, a chain of
/// short bolts crackling towards the nearest opposing ship
/// (<see cref="Battle.TrackShip"/>) for half its wait; its special
/// harvests the asteroids near it into energy, its only energy.
/// <para>
/// Its AI never dodges shots (the encounter's rule), fires at a ship
/// within 14 frames, and harvests an asteroid as near.
/// </para>
/// </summary>
public sealed class SlylandroShip : IShipCode
{
    public const string Id = "slylandr";

    // The bolts' colours, the first chosen at random.
    private static readonly int[] LightningColors =
    {
        Battle.Rgb15(0x1F, 0x1F, 0x1F), Battle.Rgb15(0x16, 0x17, 0x1F), Battle.Rgb15(0x06, 0x07, 0x1F),
        Battle.Rgb15(0x00, 0x00, 0x18),
    };

    private readonly ShipDescription _description;
    private readonly Views? _hull;

    /// <param name="description">The Slylandro's description (data/ships.json's slylandr).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship); its lightning and harvest draw none.</param>
    public SlylandroShip(ShipDescription description, Views? hull)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
    }

    // slylandr.c's #defines its code uses.
    private sealed record Defines(int MaxEnergy, int WeaponWait, int LaserLength, int HarvestRange);

    /// <summary>init_slylandro: slylandro_desc with its sheets and slylandr.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, null, null);
        var k = new Defines(d.Define("MAX_ENERGY"), d.Define("WEAPON_WAIT"), d.Define("LASER_LENGTH"),
            d.Define("HARVEST_RANGE"));
        rd.Preprocess = Preprocess;
        rd.Postprocess = e => Postprocess(battle, e, k);
        rd.InitWeapon = e => new[] { InitializeLightning(battle, e, k) };
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // lightning_postprocess: the bolt, while it has frames left and hit
    // nothing, crackles on with another from its end.
    private static void LightningPostprocess(Battle battle, Element e, Defines k)
    {
        if (e.TurnWait != 0 && (e.StateFlags & ElementFlags.Collision) == 0)
        {
            var lightning = InitializeLightning(battle, e, k);
            if (lightning != null)
                battle.List.Put(lightning);
        }
    }

    // lightning_collision: a hit stops the lightning, and cuts the weapon's
    // wait short by the frames it had left; then the hit.
    private static void LightningCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines k)
    {
        var starShip = e0.Parent!;
        if (starShip.WeaponCounter > k.WeaponWait >> 1)
            starShip.WeaponCounter = (byte)(k.WeaponWait - starShip.WeaponCounter);
        starShip.WeaponCounter = (byte)(starShip.WeaponCounter - e0.TurnWait);
        e0.TurnWait = 0;

        battle.WeaponCollision(e0, at0, e1, at1);
    }

    // initialize_lightning: a bolt from the ship (or the bolt before),
    // towards the nearest opposing ship with a random swerve (anywhere
    // without one), of random length. From the ship it takes a random
    // colour, and as many frames to go on as the weapon's wait is from its
    // nearer end; from a bolt, that bolt's colour and a frame fewer.
    private static Element? InitializeLightning(Battle battle, Element e, Defines k)
    {
        var block = new LaserBlock
        {
            Cx = e.Next.X, Cy = e.Next.Y, Ex = 0, Ey = 0,
            Sender = e.PlayerNr, Flags = ElementFlags.IgnoreSimilar, Face = 0, PixOffs = 0,
        };
        var laser = battle.InitializeLaser(block);
        if (laser == null)
            return null;

        var starShip = e.Parent!;
        laser.PostprocessFunc = l => LightningPostprocess(battle, l, k);
        laser.CollisionFunc = (e0, at0, e1, at1) => LightningCollision(battle, e0, at0, e1, at1, k);

        uint randVal = battle.Random.Next("initialize_lightning");
        int angle;
        int delta;
        if ((e.StateFlags & ElementFlags.PlayerShip) == 0)
        {
            angle = e.Velocity.TravelAngle;
            ushort facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle));
            delta = battle.TrackShip(e, ref facing);

            laser.TurnWait = (byte)(e.TurnWait - 1);
            laser.PrimColor = e.PrimColor;
        }
        else
        {
            ushort facing = starShip.ShipFacing;
            e.Target = null;
            delta = battle.TrackShip(e, ref facing);
            e.Target = null;
            angle = UqmMath.FacingToAngle(facing);

            if ((laser.TurnWait = starShip.WeaponCounter) == 0)
                laser.TurnWait = (byte)k.WeaponWait;

            if (laser.TurnWait > k.WeaponWait >> 1)
                laser.TurnWait = (byte)(k.WeaponWait - laser.TurnWait);

            laser.PrimColor = LightningColors[((randVal & 0xFFFF) >> 8) & 3];
        }

        ushort low = (ushort)randVal;
        const int halfCircleFacing = UqmMath.Facings / 2;
        if (delta == -1 || delta == halfCircleFacing)
            angle += low;
        else if (delta == 0)
            angle += (low & 1) != 0 ? -1 : 1;
        else if (delta < halfCircleFacing)
            angle += low & (UqmMath.Quadrant - 1);
        else
            angle -= low & (UqmMath.Quadrant - 1);
        int speed = Velocity.WorldToVelocity(Battle.DisplayToWorld((int)((randVal >> 16) & (uint)(k.LaserLength - 1)) + 4));
        laser.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));

        laser.Parent = starShip;
        return laser;
    }

    // slylandro_intelligence: no dodging shots (in an encounter, as every
    // battle here is); out of energy and with its special ready, and no
    // planet near, it looks only at the enemy ship and the first empty
    // concern (asteroids). An enemy ship pursued within 6 frames is
    // enticed instead. It fires at an enemy ship within 14 frames, and,
    // short of energy, harvests an asteroid within 14.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        concerns[Battle.EnemyWeaponIndex].Object = null;   // no dodging in the role-playing game

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter == 0
            && rd.EnergyLevel == 0
            && concerns[Battle.GravityMassIndex].Object == null)
            concernCounter = Battle.FirstEmptyIndex + 1;
        if (enemy.Object != null && enemy.MoveState == MoveState.Pursue && enemy.WhichTurn <= 6)
            enemy.MoveState = MoveState.Entice;

        ++ship.ThrustWait;
        battle.ShipIntelligence(ship, concerns, concernCounter);
        --ship.ThrustWait;

        if (enemy.Object != null && enemy.WhichTurn <= 14)
            starShip.ShipInputState |= StatusFlags.Weapon;
        else
            starShip.ShipInputState &= ~StatusFlags.Weapon;

        starShip.ShipInputState &= ~StatusFlags.Special;
        if (rd.EnergyLevel < rd.MaxEnergy)
        {
            ref var junk = ref concerns[Battle.FirstEmptyIndex];
            if (junk.Object != null && junk.WhichTurn <= 14)
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // harvest_space_junk: every asteroid (neutral, lasting, not the planet)
    // the ship could touch within HARVEST_RANGE pixels, measured across the
    // seam where contact is (decision 5), is broken up into nothing; any
    // fills the ship's energy (its sound).
    private static bool HarvestSpaceJunk(Battle battle, Element e, Defines k)
    {
        bool harvested = false;
        for (var obj = battle.List.Head; obj != null;)
        {
            var next = obj.Successor;
            if ((obj.StateFlags & (ElementFlags.Appearing | ElementFlags.PlayerShip | ElementFlags.FiniteLife)) == 0
                && obj.PlayerNr == Battle.NeutralPlayerNum
                && !Battle.GravityMass(obj.MassPoints)
                && Battle.CollisionPossible(obj, e))
            {
                int dx = battle.SeamDeltaX(obj.Next.X - e.Next.X), dy = battle.SeamDeltaY(obj.Next.Y - e.Next.Y);
                if (dx < 0)
                    dx = -dx;
                if (dy < 0)
                    dy = -dy;
                dx = Battle.WorldToDisplay((short)dx);
                dy = Battle.WorldToDisplay((short)dy);
                if (dx <= k.HarvestRange && dy <= k.HarvestRange
                    && dx * dx + dy * dy <= k.HarvestRange * k.HarvestRange)
                {
                    obj.LifeSpan = 0;
                    obj.StateFlags |= ElementFlags.NonSolid;

                    if (!harvested)
                    {
                        harvested = true;
                        battle.DeltaEnergy(e, (short)k.MaxEnergy);
                    }
                }
            }
            obj = next;
        }
        return harvested;
    }

    // slylandro_postprocess: while the weapon's wait runs (its first half,
    // as the bolts count it), lightning from the ship each frame; the
    // special, when ready, harvests, and only a harvest starts its wait.
    private static void Postprocess(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        if (starShip.WeaponCounter != 0 && starShip.WeaponCounter < k.WeaponWait)
        {
            var lightning = InitializeLightning(battle, e, k);
            if (lightning != null)
                battle.List.Put(lightning);
        }

        if (starShip.SpecialCounter == 0
            && (starShip.CurStatusFlags & StatusFlags.Special) != 0
            && HarvestSpaceJunk(battle, e, k))
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
    }

    // slylandro_preprocess: thrust pressed turns it about; it turns on the
    // keys every turn wait + 1 frames, and every thrust wait + 1 flies on
    // at its top speed whichever way it faces; its frame moves on each
    // frame (it spins).
    private static void Preprocess(Element e)
    {
        if ((e.StateFlags & (ElementFlags.Appearing | ElementFlags.NonSolid)) != 0)
            return;
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        int facing = starShip.ShipFacing;
        if ((starShip.CurStatusFlags & StatusFlags.Thrust) != 0 && (starShip.OldStatusFlags & StatusFlags.Thrust) == 0)
            facing += UqmMath.AngleToFacing(UqmMath.HalfCircle);

        if (e.TurnWait == 0)
        {
            e.TurnWait += (byte)(rd.Characteristics.TurnWait + 1);
            if ((starShip.CurStatusFlags & StatusFlags.Left) != 0)
                --facing;
            else if ((starShip.CurStatusFlags & StatusFlags.Right) != 0)
                ++facing;
        }

        starShip.ShipFacing = UqmMath.NormalizeFacing(facing);

        if (e.ThrustWait == 0)
        {
            e.ThrustWait += (byte)(rd.Characteristics.ThrustWait + 1);

            e.Velocity.SetVector(rd.Characteristics.MaxThrust, starShip.ShipFacing);
            starShip.CurStatusFlags |= StatusFlags.ShipAtMaxSpeed;
            starShip.CurStatusFlags &= ~StatusFlags.ShipInGravityWell;
        }

        e.Next.Frame = e.Next.Frame.Inc();
        e.StateFlags |= ElementFlags.Changing;
    }
}
