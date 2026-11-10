// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/weapon.c, weapon.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>LASER_BLOCK: a laser from (cx, cy) to (cx + ex, cy + ey), starting pixoffs pixels along its facing.</summary>
public struct LaserBlock
{
    public short Cx, Cy, Ex, Ey;
    public ElementFlags Flags;
    public short Sender;
    public short PixOffs;
    public ushort Face;
    public int Color;
}

/// <summary>MISSILE_BLOCK: a shot from (cx, cy), pixoffs pixels along its facing, at speed world units a frame.</summary>
public struct MissileBlock
{
    public short Cx, Cy;
    public ElementFlags Flags;
    public short Sender;
    public short PixOffs, Speed, HitPoints, Damage;
    public ushort Face, Index, Life;
    public Views FrameArray;
    public ElementProcess? PreprocessFunc;
    public short BlastOffs;
}

// weapon.c: shots, their hits and homing.
public sealed partial class Battle
{
    private const int LaserLife = 1;

    /// <summary>initialize_laser: the laser's element, not yet in the list, or null with the list full.</summary>
    public Element? InitializeLaser(in LaserBlock block)
    {
        var laser = List.Alloc();
        if (laser == null)
            return null;
        laser.PlayerNr = block.Sender;
        laser.HitPoints = 1;
        laser.MassPoints = 1;
        laser.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | block.Flags;
        laser.LifeSpan = LaserLife;
        laser.CollisionFunc = WeaponCollisionCallback;
        laser.BlastOffset = 1;
        int angle = UqmMath.FacingToAngle(block.Face);
        laser.Current.X = (short)(block.Cx + Cosine(angle, DisplayToWorld(block.PixOffs)));
        laser.Current.Y = (short)(block.Cy + Sine(angle, DisplayToWorld(block.PixOffs)));
        laser.Prim = PrimType.Line;
        laser.PrimColor = block.Color;
        laser.Current.Frame = Arena.StarsInSpace.Dec();
        laser.Current.FrameArray = Arena.Stars;
        laser.Velocity.SetComponents(
            (short)Velocity.WorldToVelocity(block.Cx + block.Ex - laser.Current.X),
            (short)Velocity.WorldToVelocity(block.Cy + block.Ey - laser.Current.Y));
        return laser;
    }

    /// <summary>initialize_missile: the missile's element, not yet in the list, or null with the list full.</summary>
    public Element? InitializeMissile(in MissileBlock block)
    {
        var missile = List.Alloc();
        if (missile == null)
            return null;
        missile.HitPoints = (byte)block.HitPoints;
        missile.MassPoints = (byte)block.Damage;
        missile.PlayerNr = block.Sender;
        missile.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | block.Flags;
        missile.LifeSpan = block.Life;
        missile.Prim = PrimType.Stamp;
        missile.Current.FrameArray = block.FrameArray;
        missile.Current.Frame = block.FrameArray[0].SetAbs(block.Index);
        missile.PreprocessFunc = block.PreprocessFunc;
        missile.CollisionFunc = WeaponCollisionCallback;
        missile.BlastOffset = (byte)block.BlastOffs;

        int angle = UqmMath.FacingToAngle(block.Face);
        missile.Current.X = (short)(block.Cx + Cosine(angle, DisplayToWorld(block.PixOffs)));
        missile.Current.Y = (short)(block.Cy + Sine(angle, DisplayToWorld(block.PixOffs)));

        short dx = Cosine(angle, Velocity.WorldToVelocity(block.Speed));
        short dy = Sine(angle, Velocity.WorldToVelocity(block.Speed));
        missile.Velocity.SetComponents(dx, dy);

        missile.Current.X -= (short)Velocity.VelocityToWorld(dx);
        missile.Current.Y -= (short)Velocity.VelocityToWorld(dy);
        return missile;
    }

    private void WeaponCollisionCallback(Element weapon, (short X, short Y) at, Element hit, (short X, short Y) hitAt) =>
        WeaponCollision(weapon, at, hit, hitAt);

    /// <summary>
    /// weapon_collision: a shot hits; it does its damage and goes, leaving a
    /// blast (the blast sheet's, or the shot's own last frames). Returns the
    /// blast, already in the list, or null.
    /// </summary>
    public Element? WeaponCollision(Element weapon, (short X, short Y) at, Element hit, (short X, short Y) hitAt)
    {
        if ((weapon.StateFlags & ElementFlags.Collision) != 0)   // if already did effect
            return null;

        short damage = weapon.MassPoints;
        if (damage != 0 && ((hit.StateFlags & ElementFlags.FiniteLife) != 0 || hit.LifeSpan == NormalLife))
        {
            DoDamage(hit, damage);
            if (hit.HitPoints != 0)
                weapon.StateFlags |= ElementFlags.Collision;
        }

        if ((hit.StateFlags & ElementFlags.FiniteLife) == 0
            || ((hit.StateFlags & ElementFlags.Collision) == 0 && weapon.HitPoints <= hit.MassPoints))
        {
            if (weapon.Prim != PrimType.Line)
                weapon.StateFlags |= ElementFlags.Disappearing;

            weapon.HitPoints = 0;
            weapon.LifeSpan = 0;
            weapon.StateFlags |= ElementFlags.Collision | ElementFlags.NonSolid;

            var blast = List.Alloc();
            if (blast != null)
            {
                List.Put(blast);
                blast.PlayerNr = weapon.PlayerNr;
                blast.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
                blast.Prim = PrimType.Stamp;
                blast.Current.X = DisplayToWorld(at.X);
                blast.Current.Y = DisplayToWorld(at.Y);

                ushort angle = weapon.Velocity.TravelAngle;
                short blastOffs = weapon.BlastOffset;
                if (blastOffs > 0)
                {
                    blast.Current.X += Cosine(angle, DisplayToWorld(blastOffs));
                    blast.Current.Y += Sine(angle, DisplayToWorld(blastOffs));
                }

                int blastIndex = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle + UqmMath.HalfCircle));
                blastIndex = ((blastIndex >> 2) << 1) + ((blastIndex & 0x3) != 0 ? 1 : 0);

                int numBlastFrames = weapon.Next.Frame.Count;
                if (numBlastFrames <= UqmMath.AngleToFacing(UqmMath.FullCircle))
                {
                    blast.LifeSpan = 2;
                    blast.Current.FrameArray = Arena.Blast;
                    blast.Current.Frame = Arena.Blast[0].SetAbs(blastIndex);
                }
                else
                {
                    blast.LifeSpan = (ushort)(numBlastFrames - UqmMath.AngleToFacing(UqmMath.FullCircle));
                    blast.TurnWait = blast.NextTurn = 0;
                    blast.PreprocessFunc = AnimationPreprocess;
                    blast.Current.FrameArray = weapon.Next.FrameArray;
                    blast.Current.Frame = blast.Current.FrameArray![0].SetAbs(UqmMath.AngleToFacing(UqmMath.FullCircle));
                }
                return blast;
            }
        }
        return null;
    }

    /// <summary>
    /// TrackShip: the nearest opposing ship (by |dx| + |dy|, across the
    /// seam), uncloaked unless the tracker is a ship warping in, becomes the
    /// tracker's target and is kept once chosen; <paramref name="facing"/>
    /// turns a step towards it. Returns how many facings it is away, or -1
    /// without one.
    /// </summary>
    public short TrackShip(Element tracker, ref ushort facing)
    {
        short bestDelta = 0, bestDeltaFacing = -1;
        ushort pfacing = facing;

        void Check(Element trackee)
        {
            var starShip = trackee.Parent!;
            if (trackee.LifeSpan == 0 || starShip.RaceDesc!.CrewLevel == 0)
                return;
            short dx, dy;
            if ((tracker.StateFlags & ElementFlags.PreProcess) != 0)
            {
                dx = (short)(trackee.Next.X - tracker.Next.X);
                dy = (short)(trackee.Next.Y - tracker.Next.Y);
            }
            else
            {
                dx = (short)(trackee.Current.X - tracker.Current.X);
                dy = (short)(trackee.Current.Y - tracker.Current.Y);
            }
            dx = WrapDeltaX(dx);
            dy = WrapDeltaY(dy);
            short deltaFacing = (short)UqmMath.NormalizeFacing(
                UqmMath.AngleToFacing(UqmMath.ArcTan(dx, dy)) - pfacing);
            if (dx < 0)
                dx = (short)-dx;
            if (dy < 0)
                dy = (short)-dy;
            dx += dy;   // an approximation of the distance
            if (bestDelta == 0 || dx < bestDelta)
            {
                bestDelta = dx;
                bestDeltaFacing = deltaFacing;
                tracker.Target = trackee;
            }
        }

        var target = tracker.Target;
        if (target != null)
        {
            tracker.Target = null;
            Check(target);
        }
        else
        {
            for (var trackee = List.Head; trackee != null; trackee = trackee.Succ)
            {
                if ((trackee.StateFlags & ElementFlags.PlayerShip) != 0 && trackee.PlayerNr != tracker.PlayerNr
                    && (!ObjectCloaked(trackee)
                        || ((tracker.StateFlags & ElementFlags.PlayerShip) != 0
                            && (tracker.StateFlags & ElementFlags.Appearing) != 0)))
                    Check(trackee);
            }
        }

        if (bestDeltaFacing > 0)
        {
            int f = pfacing;
            const int halfCircleFacing = UqmMath.Facings / 2;
            if (bestDeltaFacing == halfCircleFacing)
                f += (((byte)Random.Next("TrackShip") & 1) << 1) - 1;
            else if (bestDeltaFacing < halfCircleFacing)
                ++f;
            else
                --f;
            facing = UqmMath.NormalizeFacing(f);
        }
        return bestDeltaFacing;
    }

    /// <summary>OBJECT_CLOAKED: not drawn, or drawn filled black (the Ilwrath cloaked).</summary>
    public static bool ObjectCloaked(Element e) =>
        e.Prim >= PrimType.None || (e.Prim == PrimType.StampFill && e.PrimColor == 0);
}
