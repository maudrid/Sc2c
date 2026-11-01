// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/velocity.c, velocity.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Uqm;

/// <summary>
/// VELOCITY_DESC: a fixed-point velocity. Components are kept as a whole
/// number of world units per frame (Vector) plus a fraction in 1/32 units
/// (Fract), stepped with an error term so the average speed is exact.
/// Field types match UQM (COUNT, COORD) so overflow behaves the same.
/// </summary>
public struct Velocity
{
    public const int Shift = 5;
    public const int Scale = 1 << Shift;

    public ushort TravelAngle;
    public short VectorWidth, VectorHeight;
    public short FractWidth, FractHeight;
    public short ErrorWidth, ErrorHeight;
    public short IncrWidth, IncrHeight;

    public static int WorldToVelocity(int l) => l << Shift;

    public static int VelocityToWorld(int v) => v >> Shift;

    private static int Remainder(int v) => v & (Scale - 1);

    private static short MakeWord(int lo, int hi) => (short)(ushort)(((byte)hi << 8) | (byte)lo);

    private static byte HiByte(short x) => (byte)((ushort)x >> 8);

    private static byte LoByte(short x) => (byte)(ushort)x;

    public readonly bool IsZero =>
        VectorWidth == 0 && VectorHeight == 0 && IncrWidth == 0 && IncrHeight == 0
        && FractWidth == 0 && FractHeight == 0;

    public void Zero() => this = default;

    /// <summary>All nine fields, for saving: TravelAngle, vector, fract, error, incr (width then height).</summary>
    public readonly int[] ToArray() => new int[]
    {
        TravelAngle, VectorWidth, VectorHeight, FractWidth, FractHeight, ErrorWidth, ErrorHeight, IncrWidth, IncrHeight,
    };

    /// <summary>The inverse of <see cref="ToArray"/>.</summary>
    public static Velocity FromArray(int[] v)
    {
        if (v.Length != 9)
            throw new InvalidDataException("velocity needs 9 values");
        return new Velocity
        {
            TravelAngle = (ushort)v[0], VectorWidth = (short)v[1], VectorHeight = (short)v[2],
            FractWidth = (short)v[3], FractHeight = (short)v[4], ErrorWidth = (short)v[5],
            ErrorHeight = (short)v[6], IncrWidth = (short)v[7], IncrHeight = (short)v[8],
        };
    }

    /// <summary>GetCurrentVelocityComponents: velocity in 1/32 world units.</summary>
    public readonly (short dx, short dy) Current() =>
        ((short)(WorldToVelocity(VectorWidth) + (FractWidth - HiByte(IncrWidth))),
         (short)(WorldToVelocity(VectorHeight) + (FractHeight - HiByte(IncrHeight))));

    /// <summary>GetNextVelocityComponents: world units moved in numFrames.</summary>
    public (short dx, short dy) Next(ushort numFrames)
    {
        ushort e = (ushort)((ushort)ErrorWidth + (ushort)FractWidth * numFrames);
        short dx = (short)(VectorWidth * numFrames + (sbyte)LoByte(IncrWidth) * (e >> Shift));
        ErrorWidth = (short)Remainder(e);

        e = (ushort)((ushort)ErrorHeight + (ushort)FractHeight * numFrames);
        short dy = (short)(VectorHeight * numFrames + (sbyte)LoByte(IncrHeight) * (e >> Shift));
        ErrorHeight = (short)Remainder(e);
        return (dx, dy);
    }

    /// <summary>SetVelocityVector: magnitude in world units along a facing.</summary>
    public void SetVector(int magnitude, int facing)
    {
        ushort angle = TravelAngle = (ushort)UqmMath.FacingToAngle(UqmMath.NormalizeFacing(facing));
        magnitude = (short)WorldToVelocity(magnitude);
        short dx = UqmMath.Cosine(angle, magnitude);
        short dy = UqmMath.Sine(angle, magnitude);
        SetParts(dx, dy);
    }

    /// <summary>SetVelocityComponents: dx, dy in 1/32 world units.</summary>
    public void SetComponents(short dx, short dy)
    {
        ushort angle = UqmMath.ArcTan(dx, dy);
        if (angle == UqmMath.FullCircle)
            Zero();
        else
            SetParts(dx, dy);
        TravelAngle = angle;
    }

    /// <summary>DeltaVelocityComponents: add dx, dy (1/32 world units).</summary>
    public void Delta(int dx, int dy)
    {
        dx += WorldToVelocity(VectorWidth) + (FractWidth - HiByte(IncrWidth));
        dy += WorldToVelocity(VectorHeight) + (FractHeight - HiByte(IncrHeight));
        SetComponents((short)dx, (short)dy);
    }

    private void SetParts(short dx, short dy)
    {
        if (dx >= 0)
        {
            VectorWidth = (short)VelocityToWorld(dx);
            IncrWidth = MakeWord(1, 0);
        }
        else
        {
            dx = (short)-dx;
            VectorWidth = (short)-VelocityToWorld(dx);
            IncrWidth = MakeWord(0xFF, Remainder(dx) << 1);
        }
        if (dy >= 0)
        {
            VectorHeight = (short)VelocityToWorld(dy);
            IncrHeight = MakeWord(1, 0);
        }
        else
        {
            dy = (short)-dy;
            VectorHeight = (short)-VelocityToWorld(dy);
            IncrHeight = MakeWord(0xFF, Remainder(dy) << 1);
        }
        FractWidth = (short)Remainder(dx);
        FractHeight = (short)Remainder(dy);
        ErrorWidth = ErrorHeight = 0;
    }
}
