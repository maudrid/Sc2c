// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/collide.c,
// src/uqm/gravity.c), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

// collide.c's momentum and gravity.c's pull.
public sealed partial class Battle
{
    private const int CollisionTurnWait = 1;
    private const int CollisionThrustWait = 3;
    private const int GravityThreshold = 255;

    // collide: two solid elements bounce off each other by their masses
    // (the angle between them across the seam where contact is tested so).
    private void Collide(Element e0, Element e1)
    {
        short dxRel = SeamDeltaX(e0.Next.X - e1.Next.X);
        short dyRel = SeamDeltaY(e0.Next.Y - e1.Next.Y);
        short impactAngle0 = (short)UqmMath.ArcTan(dxRel, dyRel);
        short impactAngle1 = (short)UqmMath.NormalizeAngle(impactAngle0 + UqmMath.HalfCircle);

        var (dx0, dy0) = e0.Velocity.Current();
        short travelAngle0 = (short)e0.Velocity.TravelAngle;
        var (dx1, dy1) = e1.Velocity.Current();
        short travelAngle1 = (short)e1.Velocity.TravelAngle;
        dxRel = (short)(dx0 - dx1);
        dyRel = (short)(dy0 - dy1);
        short relTravelAngle = (short)UqmMath.ArcTan(dxRel, dyRel);
        short speed = (short)UqmMath.SquareRoot((uint)((long)dxRel * dxRel + (long)dyRel * dyRel));

        short directness = (short)UqmMath.NormalizeAngle(relTravelAngle - impactAngle0);
        if (directness <= UqmMath.Quadrant || directness >= UqmMath.HalfCircle + UqmMath.Quadrant)
        {
            // shapes just scraped each other but still collided,
            // they will collide again unless we fudge it.
            directness = UqmMath.HalfCircle;
            impactAngle0 = (short)(travelAngle0 + UqmMath.HalfCircle);
            impactAngle1 = (short)(travelAngle1 + UqmMath.HalfCircle);
        }

        if (e0.Next.X == e0.Current.X && e0.Next.Y == e0.Current.Y
            && e1.Next.X == e1.Current.X && e1.Next.Y == e1.Current.Y)
        {
            if ((e0.StateFlags & e1.StateFlags & ElementFlags.DefyPhysics) != 0)
            {
                const int octant = UqmMath.FullCircle >> 3;
                impactAngle0 = (short)(travelAngle0 + (UqmMath.HalfCircle - octant));
                impactAngle1 = (short)(travelAngle1 + (UqmMath.HalfCircle - octant));
                e0.Velocity.Zero();
                e1.Velocity.Zero();
            }
            e0.StateFlags |= ElementFlags.DefyPhysics | ElementFlags.Collision;
            e1.StateFlags |= ElementFlags.DefyPhysics | ElementFlags.Collision;
        }

        short mass0 = e0.MassPoints, mass1 = e1.MassPoints;
        long scalar = (long)Sine(directness, speed << 1) * (mass0 * mass1);

        if (!GravityMass(e0.MassPoints + 1))
            Bounce(e0, mass0, mass1, scalar, impactAngle0);
        if (!GravityMass(e1.MassPoints + 1))
            Bounce(e1, mass1, mass0, scalar, impactAngle1);
    }

    private static void Bounce(Element e, short mass, short otherMass, long scalar, short impactAngle)
    {
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            e.Parent!.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
            if ((e.StateFlags & ElementFlags.DefyPhysics) == 0)
            {
                if (e.TurnWait < CollisionTurnWait)
                    e.TurnWait += CollisionTurnWait;
                if (e.ThrustWait < CollisionThrustWait)
                    e.ThrustWait += CollisionThrustWait;
            }
        }

        short speed = (short)(scalar / ((long)mass * (mass + otherMass)));
        e.Velocity.Delta(Cosine(impactAngle, speed), Sine(impactAngle, speed));

        var (dx, dy) = e.Velocity.Current();
        if (dx < 0)
            dx = (short)-dx;
        if (dy < 0)
            dy = (short)-dy;
        if (Velocity.VelocityToWorld(dx + dy) < ScaledOne)
            e.Velocity.SetComponents(Cosine(impactAngle, Velocity.WorldToVelocity(ScaledOne) - 1),
                Sine(impactAngle, Velocity.WorldToVelocity(ScaledOne) - 1));
    }

    /// <summary>
    /// CalculateGravity: a planet pulls every solid element near it a world
    /// unit a frame towards it; for anything else, whether a planet is near
    /// (where nothing may spawn).
    /// </summary>
    private bool CalculateGravity(Element e)
    {
        bool hasGravity = Colliding(e) && GravityMass(e.MassPoints + 1);
        for (var test = List.Head; test != null; test = test.Succ)
        {
            if (test == e || !Colliding(test))
                continue;
            bool testHasGravity = GravityMass(test.MassPoints + 1);
            if (testHasGravity == hasGravity)
                continue;

            short dx, dy;
            if ((e.StateFlags & ElementFlags.PreProcess) == 0)
            {
                dx = (short)(e.Current.X - test.Current.X);
                dy = (short)(e.Current.Y - test.Current.Y);
            }
            else
            {
                dx = (short)(e.Next.X - test.Next.X);
                dy = (short)(e.Next.Y - test.Next.Y);
            }
            dx = WrapDeltaX(dx);
            dy = WrapDeltaY(dy);
            ushort absDx = (ushort)(dx >= 0 ? dx : -dx);
            ushort absDy = (ushort)(dy >= 0 ? dy : -dy);
            absDx = (ushort)(absDx >> OneShift);
            absDy = (ushort)(absDy >> OneShift);
            if (absDx <= GravityThreshold && absDy <= GravityThreshold)
            {
                uint distSquared = (uint)(absDx * absDx) + (uint)(absDy * absDy);
                if (distSquared <= GravityThreshold * GravityThreshold)
                {
                    if (testHasGravity)
                        return true;
                    ushort angle = UqmMath.ArcTan(dx, dy);
                    test.Velocity.Delta(Cosine(angle, Velocity.WorldToVelocity(1)), Sine(angle, Velocity.WorldToVelocity(1)));
                    if ((test.StateFlags & ElementFlags.PlayerShip) != 0)
                    {
                        test.Parent!.CurStatusFlags &= ~StatusFlags.ShipAtMaxSpeed;
                        test.Parent.CurStatusFlags |= StatusFlags.ShipInGravityWell;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// TimeSpaceMatterConflict: whether the element, where it is now,
    /// overlaps anything solid or a ship (across the seam where contact is
    /// tested so).
    /// </summary>
    internal bool TimeSpaceMatterConflict(Element e)
    {
        var control = StillControl(e);
        for (var test = List.Head; test != null; test = test.Succ)
        {
            if (test != e && (Colliding(test) || (test.StateFlags & ElementFlags.PlayerShip) != 0)
                && Intersect(control, StillControl(test), MaxTimeValue) != 0)
                return true;
        }
        return false;
    }

    // An element's shape where it is, not moving.
    private static IntersectControl StillControl(Element e)
    {
        var stamp = StampOf(e.Current);
        var shape = stamp.IsNull ? null! : stamp.Shape;
        short x = WorldToDisplay(e.Current.X), y = WorldToDisplay(e.Current.Y);
        return new IntersectControl { Frame = shape, OriginX = x, OriginY = y, EndX = x, EndY = y };
    }
}
