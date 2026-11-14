// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/ship.c; src/uqm/status.c:
// DeltaEnergy, DeltaCrew, PostProcessStatus; src/uqm/shipstat.c:
// DeltaStatistics, their state without the drawing), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

// ship.c: a ship's energy, turning, thrust, firing and bumping; and
// status.c's crew and energy, which UQM keeps while drawing the status panel.
public sealed partial class Battle
{
    private const int MaxAllowedSpeed = (18 << OneShift) << Velocity.Shift;
    private const uint MaxAllowedSpeedSqr = (uint)MaxAllowedSpeed * MaxAllowedSpeed;

    /// <summary>animation_preprocess: the next frame every next_turn + 1 frames.</summary>
    public static void AnimationPreprocess(Element e)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            e.Next.Frame = e.Current.Frame.Inc();
            e.StateFlags |= ElementFlags.Changing;
            e.TurnWait = e.NextTurn;
        }
    }

    // velocity.h's VelocitySquared: SIZE arguments.
    private static uint VelocitySquared(short dx, short dy) => (uint)((long)dx * dx + (long)dy * dy);

    // inertial_thrust: a thrust along the ship's facing, up to its top speed.
    internal static StatusFlags InertialThrust(Element e)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        ref var velocity = ref e.Velocity;
        ushort currentAngle = (ushort)UqmMath.FacingToAngle(starShip.ShipFacing);
        ushort travelAngle = velocity.TravelAngle;
        ushort thrustIncrement = rd.Characteristics.ThrustIncrement;
        ushort maxThrust = rd.Characteristics.MaxThrust;
        if (thrustIncrement == maxThrust)
        {
            // inertialess acceleration (Skiff)
            velocity.SetVector(maxThrust, starShip.ShipFacing);
            return StatusFlags.ShipAtMaxSpeed;
        }
        if (travelAngle == currentAngle
            && (starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) != 0
            && (starShip.CurStatusFlags & StatusFlags.ShipInGravityWell) == 0)
            return starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);

        thrustIncrement = (ushort)Velocity.WorldToVelocity(thrustIncrement);
        var (curDx, curDy) = velocity.Current();
        uint currentSpeed = VelocitySquared(curDx, curDy);
        short dx = (short)(curDx + Cosine(currentAngle, thrustIncrement));
        short dy = (short)(curDy + Sine(currentAngle, thrustIncrement));
        uint desiredSpeed = VelocitySquared(dx, dy);
        uint maxSpeed = VelocitySquared((short)Velocity.WorldToVelocity(maxThrust), 0);

        if (desiredSpeed <= maxSpeed)
            velocity.SetComponents(dx, dy);   // normal acceleration
        else if (((starShip.CurStatusFlags & StatusFlags.ShipInGravityWell) != 0 && desiredSpeed <= MaxAllowedSpeedSqr)
                 || desiredSpeed < currentSpeed)
        {
            // in a gravity well within the most allowed, or slowing after a gravity whip
            velocity.SetComponents(dx, dy);
            return StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed;
        }
        else if (travelAngle == currentAngle)
        {
            // at top speed, the same way
            if (currentSpeed <= maxSpeed)
                velocity.SetVector(maxThrust, starShip.ShipFacing);
            return StatusFlags.ShipAtMaxSpeed;
        }
        else
        {
            // thrusting at an angle at top speed turns the travel only
            var v = velocity;
            v.Delta(Cosine(currentAngle, thrustIncrement >> 1) - Cosine(travelAngle, thrustIncrement),
                Sine(currentAngle, thrustIncrement >> 1) - Sine(travelAngle, thrustIncrement));
            (curDx, curDy) = v.Current();
            desiredSpeed = VelocitySquared(curDx, curDy);
            if (desiredSpeed > maxSpeed)
            {
                if (desiredSpeed < currentSpeed)
                    velocity = v;
                return StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed;
            }
            velocity = v;
        }
        return StatusFlags.None;
    }

    // ship_preprocess: warping in on its first frame; then energy, the
    // ship's own step, turning and thrust.
    private void ShipPreprocess(Element e)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        var curStatusFlags = starShip.CurStatusFlags & ~StatusFlags.Keys;
        if ((e.StateFlags & ElementFlags.Appearing) == 0)
            curStatusFlags |= starShip.ShipInputState & StatusFlags.Keys;
        else
        {
            // Preprocessing for the first time: InitShipStatus and the
            // captain's window are drawing (their crew and energy come out as they were).
            e.CrewLevel = rd.CrewLevel;
            starShip.Silhouette.Clear();   // DrawCaptainsWindow
            rd.Preprocess?.Invoke(e);
            if (e.Target == null)
                ShipTransition(e);
            else
                e.Target = null;   // the Pkunk's rebirth
            return;
        }
        starShip.CurStatusFlags = curStatusFlags;

        if (starShip.EnergyCounter != 0)
            --starShip.EnergyCounter;
        else if (rd.EnergyLevel < rd.MaxEnergy || (sbyte)rd.Characteristics.EnergyRegeneration < 0)
            DeltaEnergy(e, (sbyte)rd.Characteristics.EnergyRegeneration);

        if (rd.Preprocess != null)
        {
            rd.Preprocess(e);
            curStatusFlags = starShip.CurStatusFlags;
        }

        if (e.TurnWait != 0)
            --e.TurnWait;
        else if ((curStatusFlags & (StatusFlags.Left | StatusFlags.Right)) != 0)
        {
            starShip.ShipFacing = (curStatusFlags & StatusFlags.Left) != 0
                ? UqmMath.NormalizeFacing(starShip.ShipFacing - 1)
                : UqmMath.NormalizeFacing(starShip.ShipFacing + 1);
            e.Next.Frame = e.Next.Frame.SetAbs(starShip.ShipFacing);
            e.StateFlags |= ElementFlags.Changing;
            e.TurnWait = rd.Characteristics.TurnWait;
        }

        if (e.ThrustWait != 0)
            --e.ThrustWait;
        else if ((curStatusFlags & StatusFlags.Thrust) != 0)
        {
            var thrustStatus = InertialThrust(e);
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed | StatusFlags.ShipInGravityWell);
            starShip.CurStatusFlags |= thrustStatus;
            e.ThrustWait = rd.Characteristics.ThrustWait;
            if (!ObjectCloaked(e))
                SpawnIonTrail(e);
        }
        // PreProcessStatus only draws the captain's window.
    }

    // ship_postprocess: firing, and the ship's own step after moving.
    private void ShipPostprocess(Element e)
    {
        if (e.CrewLevel == 0)
            return;
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;

        if (starShip.WeaponCounter != 0)
            --starShip.WeaponCounter;
        else if ((starShip.CurStatusFlags & StatusFlags.Weapon) != 0
                 && DeltaEnergy(e, (short)-rd.Characteristics.WeaponEnergyCost))
        {
            var weapons = rd.InitWeapon!(e);
            foreach (var w in weapons)
            {
                if (w == null)
                    continue;
                w.Parent = starShip;
                List.Put(w);
            }
            starShip.WeaponCounter = rd.Characteristics.WeaponWait;
        }

        if (starShip.SpecialCounter != 0)
            --starShip.SpecialCounter;

        rd.Postprocess?.Invoke(e);
        PostProcessStatus(e);
    }

    /// <summary>collision (ship.c): a ship, an asteroid or the planet bumping; hitting the planet costs a quarter of what's left.</summary>
    public void Collision(Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        if ((e1.StateFlags & ElementFlags.FiniteLife) != 0)
            return;
        e0.StateFlags |= ElementFlags.Collision;
        if (GravityMass(e1.MassPoints))
        {
            // Collision with a planet.
            short damage = (short)(e0.HitPoints >> 2);
            if (damage == 0)
                damage = 1;
            DoDamage(e0, damage);
        }
    }

    // spawn_ship: the ship's element, at a random place clear of
    // everything, facing a random way, warping in on its first frame.
    private bool SpawnShip(Starship starShip)
    {
        var rd = starShip.Code.Load(this, starShip);
        starShip.RaceDesc = rd;
        starShip.ShipInputState = starShip.CurStatusFlags = starShip.OldStatusFlags = StatusFlags.None;

        // An encounter: the crew it comes with (the flagship's is its own, 0 here).
        if (starShip.CrewLevel != 0)
            rd.CrewLevel = starShip.CrewLevel;
        if (rd.CrewLevel > rd.MaxCrew)
            rd.CrewLevel = rd.MaxCrew;

        starShip.EnergyCounter = starShip.WeaponCounter = starShip.SpecialCounter = 0;

        var ship = starShip.Ship;
        if (ship == null)
        {
            ship = List.Alloc();
            if (ship != null)
                List.Insert(ship, List.Head);
        }
        starShip.Ship = ship;
        if (ship == null)
            return false;

        ship.PlayerNr = starShip.PlayerNr;
        ship.CrewLevel = 0;
        ship.MassPoints = rd.Characteristics.ShipMass;
        ship.StateFlags = ElementFlags.Appearing | ElementFlags.PlayerShip | ElementFlags.IgnoreSimilar;
        ship.TurnWait = 0;
        ship.ThrustWait = 0;
        ship.LifeSpan = NormalLife;
        ship.ColorCycleIndex = 0;
        ship.Prim = PrimType.Stamp;
        ship.Current.FrameArray = rd.Ship;
        starShip.ShipFacing = UqmMath.NormalizeFacing((int)Random.Next("spawn_ship"));
        ship.Current.Frame = rd.Ship![0].SetAbs(starShip.ShipFacing);
        do
        {
            ship.Current.X = RandomX("spawn_ship");
            ship.Current.Y = RandomY("spawn_ship");
        } while (CalculateGravity(ship) || TimeSpaceMatterConflict(ship));

        ship.PreprocessFunc = ShipPreprocess;
        ship.PostprocessFunc = ShipPostprocess;
        ship.DeathFunc = ShipDeath;
        ship.CollisionFunc = Collision;
        ship.Velocity.Zero();
        ship.Parent = starShip;
        ship.Target = null;
        return true;
    }

    /// <summary>
    /// DeltaEnergy: the ship's energy up (to its most) or down; false, and
    /// LOW_ON_ENERGY, if there isn't that much to spend. A change starts
    /// the wait before it regenerates.
    /// </summary>
    public bool DeltaEnergy(Element e, short energyDelta)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        bool ok = true;
        if (energyDelta >= 0)
        {
            if ((byte)(rd.EnergyLevel + (byte)energyDelta) > rd.MaxEnergy)
                energyDelta = (short)(rd.MaxEnergy - rd.EnergyLevel);
        }
        else if ((byte)-energyDelta > rd.EnergyLevel)
            ok = false;

        if (!ok)
            starShip.CurStatusFlags |= StatusFlags.LowOnEnergy;
        else
        {
            starShip.CurStatusFlags &= ~StatusFlags.LowOnEnergy;
            starShip.EnergyCounter = rd.Characteristics.EnergyWait;
            DeltaStatistics(rd, 0, energyDelta);
        }
        return ok;
    }

    /// <summary>DeltaCrew: the ship's crew up (to its most) or down; false when none are left.</summary>
    public bool DeltaCrew(Element e, short crewDelta)
    {
        var rd = e.Parent!.RaceDesc!;
        bool ok = true;
        if (crewDelta > 0)
        {
            e.CrewLevel = (ushort)(e.CrewLevel + crewDelta);
            if (e.CrewLevel > rd.MaxCrew)
            {
                crewDelta = (short)(rd.MaxCrew - rd.CrewLevel);
                e.CrewLevel = rd.MaxCrew;
            }
        }
        else if (crewDelta < 0)
        {
            if (e.CrewLevel > (ushort)-crewDelta)
                e.CrewLevel = (ushort)(e.CrewLevel + crewDelta);
            else
            {
                crewDelta = (short)-e.CrewLevel;
                e.CrewLevel = 0;
                ok = false;
            }
        }
        DeltaStatistics(rd, crewDelta, 0);
        return ok;
    }

    // shipstat.c's DeltaStatistics: the status panel's crew and energy.
    private static void DeltaStatistics(RaceDesc rd, short crewDelta, short energyDelta)
    {
        if (crewDelta != 0)
            rd.CrewLevel = (ushort)(rd.CrewLevel + crewDelta);
        if (energyDelta != 0)
            rd.EnergyLevel = (byte)(rd.EnergyLevel + energyDelta);
    }

    // status.c's PostProcessStatus: a ship without crew has no keys; the
    // keys as they were for the captain's window (old_status_flags).
    private static void PostProcessStatus(Element e)
    {
        var starShip = e.Parent!;
        if (starShip.CaptainsNameIndex == 0 && starShip.PlayerNr != 0)
            return;   // the Sa-Matra
        var curStatusFlags = starShip.CurStatusFlags;
        if (e.CrewLevel == 0)
            starShip.CurStatusFlags &= ~StatusFlags.Keys;
        starShip.OldStatusFlags = curStatusFlags;   // as read before the keys went
    }
}
