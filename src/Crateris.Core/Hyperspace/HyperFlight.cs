// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0: ship_preprocess and inertial_thrust
// (src/uqm/ship.c), sis_hyper_preprocess (src/uqm/ships/sis_ship/sis_ship.c),
// MoveSIS (src/uqm/hyper.c), PreProcess and CalcView (src/uqm/process.c) and
// DeltaSISGauges_fuelDelta (src/uqm/sis.c). Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

[Flags]
public enum ShipInput : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Thrust = 4,
}

/// <summary>
/// One ship flying in hyperspace, stepped one battle frame (1/24 s) at a
/// time exactly as UQM steps the flagship: input, turning and thrust (or
/// slowing down when not thrusting), movement, then the view scroll that
/// moves the ship's hyperspace position and burns fuel.
///
/// UQM keeps this state in globals and statics (one flagship). Here each
/// player's ship has its own instance. Not ported for stage 1: autopilot,
/// QuasiSpace (ARILOU_SPACE_SIDE), encounters and the hyperspace menu.
/// </summary>
public sealed class HyperFlight
{
    // Status flags (races.h).
    public const ushort StatusLeft = 1 << 0;
    public const ushort StatusRight = 1 << 1;
    public const ushort StatusThrust = 1 << 2;
    public const ushort StatusWeapon = 1 << 3;
    public const ushort StatusSpecial = 1 << 4;
    public const ushort StatusAtMaxSpeed = 1 << 7;
    public const ushort StatusBeyondMaxSpeed = 1 << 8;
    public const ushort StatusInGravityWell = 1 << 9;

    private const int CenterX = Units.LogSpaceWidth >> 1;
    private const int CenterY = Units.LogSpaceHeight >> 1;

    public ShipDrive Drive { get; set; }

    /// <summary>Fuel tank capacity in 1/100 units (GetFuelTankCapacity).</summary>
    public uint FuelCapacity { get; set; }

    public ushort Facing { get; set; }
    public Velocity Velocity;
    public ushort Status { get; set; }
    public byte TurnWait { get; set; }
    public byte ThrustWait { get; set; }

    /// <summary>Hyperspace position in logical units (GLOBAL_SIS log_x/log_y).</summary>
    public int LogX { get; set; }
    public int LogY { get; set; }

    /// <summary>Fuel on board in 1/100 units (GLOBAL_SIS FuelOnBoard).</summary>
    public uint Fuel { get; set; }

    // The ship's place in the hyperspace view (ELEMENT next.location).
    // The view scrolls each frame to bring it back to the centre.
    public short ViewX { get; set; } = CenterX;
    public short ViewY { get; set; } = CenterY;

    // MoveSIS fuel accounting (statics in hyper.c).
    public ushort FuelDistX { get; set; }
    public ushort FuelDistY { get; set; }
    public ushort FuelDistExtra { get; set; }
    public byte FuelTicks { get; set; } = 1;

    public HyperFlight(ShipDrive drive, uint fuelCapacity)
    {
        Drive = drive;
        FuelCapacity = fuelCapacity;
    }

    public short UniverseX => Units.LogXToUniverse(LogX);

    public short UniverseY => Units.LogYToUniverse(LogY);

    public void PlaceAtUniverse(short x, short y)
    {
        LogX = Units.UniverseToLogX(x);
        LogY = Units.UniverseToLogY(y);
    }

    /// <summary>Entering hyperspace resets the fuel counters (LoadHyperspace).</summary>
    public void ResetFuelCounters()
    {
        FuelDistX = FuelDistY = FuelDistExtra = 0;
        FuelTicks = 1;
    }

    /// <summary>Come to a standstill where the ship is (used when a player leaves).</summary>
    public void Stop()
    {
        Velocity.Zero();
        Status = 0;
        TurnWait = ThrustWait = 0;
        ViewX = CenterX;
        ViewY = CenterY;
    }

    /// <summary>Advance one battle frame with this frame's input.</summary>
    public void Step(ShipInput input)
    {
        Preprocess((ushort)((byte)input & (StatusLeft | StatusRight | StatusThrust)));

        // PreProcess (process.c): move by velocity.
        var (dx, dy) = Velocity.Next(1);
        ViewX += dx;
        ViewY += dy;

        // CalcView (process.c): scroll the view back to the ship, at most
        // ORG_JUMP per frame, moving the hyperspace position with it.
        int vdx = Units.WrapDeltaX((short)(CenterX - ViewX));
        int vdy = Units.WrapDeltaY((short)(CenterY - ViewY));
        short sdx = (short)Math.Clamp(vdx, -Units.OrgJumpX, Units.OrgJumpX);
        short sdy = (short)Math.Clamp(vdy, -Units.OrgJumpY, Units.OrgJumpY);
        if (sdx != 0 || sdy != 0)
            MoveSis(ref sdx, ref sdy);
        ViewX += sdx;
        ViewY += sdy;
    }

    // ship_preprocess with sis_hyper_preprocess as the ship's preprocess
    // function.
    private void Preprocess(ushort input)
    {
        ushort cur = (ushort)(Status & ~(StatusLeft | StatusRight | StatusThrust | StatusWeapon | StatusSpecial));
        cur |= input;
        Status = cur;

        HyperPreprocess();
        cur = Status;

        if (TurnWait != 0)
            --TurnWait;
        else if ((cur & (StatusLeft | StatusRight)) != 0)
        {
            Facing = (cur & StatusLeft) != 0
                ? UqmMath.NormalizeFacing(Facing - 1)
                : UqmMath.NormalizeFacing(Facing + 1);
            TurnWait = (byte)Drive.TurnWait;
        }

        if (ThrustWait != 0)
            --ThrustWait;
        else if ((cur & StatusThrust) != 0)
        {
            ushort thrustStatus = InertialThrust();
            Status &= unchecked((ushort)~(StatusAtMaxSpeed | StatusBeyondMaxSpeed | StatusInGravityWell));
            Status |= thrustStatus;
            ThrustWait = (byte)Drive.ThrustWait;
        }
    }

    // sis_hyper_preprocess, without autopilot: when not thrusting (or out of
    // fuel), the ship slows down by one thrust step per thrust_wait.
    private void HyperPreprocess()
    {
        int accelerateDirection = 0;
        short udx = 0, udy = 0;

        if ((Status & StatusThrust) == 0 || Fuel == 0)
        {
            accelerateDirection = -1;
            var (cdx, cdy) = Velocity.Current();
            udx = (short)(cdx << 4);
            udy = (short)(cdy << 4);
            Status &= unchecked((ushort)~StatusThrust);
        }

        if (ThrustWait == 0 && accelerateDirection != 0)
        {
            short velocityIncrement = (short)Velocity.WorldToVelocity(Drive.ThrustIncrement);
            ushort dist = UqmMath.SquareRoot((uint)((long)udx * udx + (long)udy * udy));
            if (dist == 0)
                dist = 1;

            var (dx, dy) = Velocity.Current();
            short speed = (short)UqmMath.SquareRoot((uint)((long)dx * dx + (long)dy * dy));

            // AccelerateDirection < 0: slow down one step.
            dy = (short)((speed / velocityIncrement - 1) * velocityIncrement);
            if (dy < speed - velocityIncrement)
                dy = (short)(speed - velocityIncrement);
            if ((speed = dy) < 0)
                speed = 0;
            Status &= unchecked((ushort)~StatusAtMaxSpeed);

            dx = (short)((long)udx * speed / dist);
            dy = (short)((long)udy * speed / dist);
            Velocity.SetComponents(dx, dy);
            ThrustWait = (byte)Drive.ThrustWait;
        }
    }

    // inertial_thrust (ship.c). Hyperspace has no gravity wells.
    private ushort InertialThrust()
    {
        int currentAngle = UqmMath.FacingToAngle(Facing);
        ushort travelAngle = Velocity.TravelAngle;
        int thrustIncrement = Drive.ThrustIncrement;
        int maxThrust = Drive.MaxThrust;

        if (thrustIncrement == maxThrust)
        {
            Velocity.SetVector(maxThrust, Facing);
            return StatusAtMaxSpeed;
        }
        if (travelAngle == currentAngle
            && (Status & (StatusAtMaxSpeed | StatusBeyondMaxSpeed)) != 0
            && (Status & StatusInGravityWell) == 0)
        {
            return (ushort)(Status & (StatusAtMaxSpeed | StatusBeyondMaxSpeed));
        }

        short inc = (short)Velocity.WorldToVelocity(thrustIncrement);
        var (curDx, curDy) = Velocity.Current();
        uint currentSpeed = VelocitySquared(curDx, curDy);
        short deltaX = (short)(curDx + UqmMath.Cosine(currentAngle, inc));
        short deltaY = (short)(curDy + UqmMath.Sine(currentAngle, inc));
        uint desiredSpeed = VelocitySquared(deltaX, deltaY);
        uint maxSpeed = VelocitySquared((short)Velocity.WorldToVelocity(maxThrust), 0);

        if (desiredSpeed <= maxSpeed)
        {
            Velocity.SetComponents(deltaX, deltaY);
        }
        else if (desiredSpeed < currentSpeed)
        {
            // Slowing down after going beyond max speed.
            Velocity.SetComponents(deltaX, deltaY);
            return StatusAtMaxSpeed | StatusBeyondMaxSpeed;
        }
        else if (travelAngle == currentAngle)
        {
            if (currentSpeed <= maxSpeed)
                Velocity.SetVector(maxThrust, Facing);
            return StatusAtMaxSpeed;
        }
        else
        {
            // At max speed, thrusting at an angle only turns the travel vector.
            Velocity v = Velocity;
            v.Delta(
                UqmMath.Cosine(currentAngle, inc >> 1) - UqmMath.Cosine(travelAngle, inc),
                UqmMath.Sine(currentAngle, inc >> 1) - UqmMath.Sine(travelAngle, inc));
            var (vdx, vdy) = v.Current();
            desiredSpeed = VelocitySquared(vdx, vdy);
            if (desiredSpeed > maxSpeed)
            {
                if (desiredSpeed < currentSpeed)
                    Velocity = v;
                return StatusAtMaxSpeed | StatusBeyondMaxSpeed;
            }
            Velocity = v;
        }
        return 0;
    }

    private static uint VelocitySquared(short dx, short dy) => (uint)((long)dx * dx + (long)dy * dy);

    // MoveSIS (hyper.c): move the hyperspace position, stop at the edges of
    // the universe, and burn fuel by distance travelled.
    private void MoveSis(ref short pdx, ref short pdy)
    {
        short newDx = pdx;
        LogX -= newDx;
        if (LogX < 0)
        {
            newDx = (short)(newDx + LogX);
            LogX = 0;
        }
        else if (LogX > Units.MaxXLogical)
        {
            newDx = (short)(newDx + (LogX - Units.MaxXLogical));
            LogX = Units.MaxXLogical;
        }

        short newDy = pdy;
        LogY -= newDy;
        if (LogY < 0)
        {
            newDy = (short)(newDy + LogY);
            LogY = 0;
        }
        else if (LogY > Units.MaxYLogical)
        {
            newDy = (short)(newDy + (LogY - Units.MaxYLogical));
            LogY = Units.MaxYLogical;
        }

        if (newDx != pdx || newDy != pdy)
        {
            pdx = newDx;
            pdy = newDy;
            // The ship is put where the clipped scroll will leave it.
            ViewX = (short)(CenterX - newDx);
            ViewY = (short)(CenterY - newDy);
        }

        if (Fuel != 0)
        {
            if (newDx < 0)
                newDx = (short)-newDx;
            FuelDistX = (ushort)(FuelDistX + newDx);
            if (newDy < 0)
                newDy = (short)-newDy;
            FuelDistY = (ushort)(FuelDistY + newDy);

            uint adjDx = (uint)Units.LogXToUniverse(16 * FuelDistX);
            uint adjDy = (uint)(Units.MaxYUniverse - Units.LogYToUniverse(16 * FuelDistY));
            ushort hyperDist = (ushort)(UqmMath.SquareRoot(adjDx * adjDx + adjDy * adjDy) + FuelDistExtra);
            ushort curFuelTicks = (ushort)(hyperDist >> 4);

            if (curFuelTicks > FuelTicks)
            {
                DeltaFuel((short)(FuelTicks - curFuelTicks));
                if (curFuelTicks > 0x00FF)
                {
                    FuelDistX = 0;
                    FuelDistExtra = (ushort)(hyperDist & ((1 << 4) - 1));
                    FuelDistY = 0;
                    curFuelTicks = 0;
                }
                FuelTicks = (byte)curFuelTicks;
            }
        }
    }

    // DeltaSISGauges_fuelDelta (sis.c), without the drawing.
    private void DeltaFuel(short delta)
    {
        if (delta == 0)
            return;
        if (delta < 0 && Fuel <= (uint)-delta)
            Fuel = 0;
        else
        {
            Fuel = (uint)(Fuel + delta);
            if (Fuel > FuelCapacity)
                Fuel = FuelCapacity;
        }
    }
}
