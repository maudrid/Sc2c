// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/trans.c, src/uqm/units.h,
// src/libs/math/sqrt.c), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Uqm;

/// <summary>
/// UQM's fixed-point trigonometry and square root, bit for bit.
/// Angles run 0..63 (FULL_CIRCLE 64); facings 0..15. Angle 0 points up
/// (negative y), angles grow clockwise.
/// </summary>
public static class UqmMath
{
    public const int CircleShift = 6;
    public const int FullCircle = 1 << CircleShift;
    public const int HalfCircle = FullCircle >> 1;
    public const int Quadrant = FullCircle >> 2;
    public const int FacingShift = 4;
    public const int SinShift = 14;

    // sinetab from trans.c: 16384 * sin, starting at -1 for angle 0.
    private static readonly short[] SineTable =
    {
        -16384, -16305, -16069, -15678, -15136, -14449, -13622, -12664,
        -11585, -10393, -9102, -7723, -6269, -4756, -3196, -1605,
        0, 1605, 3196, 4756, 6269, 7723, 9102, 10393,
        11585, 12664, 13622, 14449, 15136, 15678, 16069, 16305,
        16384, 16305, 16069, 15678, 15136, 14449, 13622, 12664,
        11585, 10393, 9102, 7723, 6269, 4756, 3196, 1605,
        0, -1605, -3196, -4756, -6269, -7723, -9102, -10393,
        -11585, -12664, -13622, -14449, -15136, -15678, -16069, -16305,
    };

    // atantab from ARCTAN in trans.c.
    private static readonly ushort[] AtanTable =
    {
        0, 0, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5,
        5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 8, 8, 8,
    };

    public static ushort NormalizeAngle(int a) => (ushort)(a & (FullCircle - 1));

    public static ushort NormalizeFacing(int f) => (ushort)(f & ((1 << FacingShift) - 1));

    public static int FacingToAngle(int f) => f << (CircleShift - FacingShift);

    public static int AngleToFacing(int a) =>
        (a + (1 << (CircleShift - FacingShift - 1))) >> (CircleShift - FacingShift);

    public static short SinVal(int angle) => SineTable[NormalizeAngle(angle)];

    /// <summary>SINE (a, m) from units.h.</summary>
    public static short Sine(int angle, int magnitude) =>
        (short)(((long)SinVal(angle) * magnitude) >> SinShift);

    /// <summary>COSINE (a, m) from units.h.</summary>
    public static short Cosine(int angle, int magnitude) => Sine(angle + Quadrant, magnitude);

    /// <summary>
    /// ARCTAN (dx, dy) from trans.c. Returns FullCircle (64) when both are 0.
    /// </summary>
    public static ushort ArcTan(short deltaX, short deltaY)
    {
        short v1 = deltaX, v2 = deltaY;
        if (v1 == 0 && v2 == 0)
            return FullCircle;
        if (v1 < 0)
            v1 = (short)-v1;
        if (v2 < 0)
            v2 = (short)-v2;
        if (v1 > v2)
            v1 = (short)(Quadrant - AtanTable[(int)((((uint)v2 << (CircleShift - 1)) + (uint)(v1 >> 1)) / (uint)v1)]);
        else
            v1 = (short)AtanTable[(int)((((uint)v1 << (CircleShift - 1)) + (uint)(v2 >> 1)) / (uint)v2)];
        if (deltaX < 0)
            v1 = (short)(FullCircle - v1);
        if (deltaY > 0)
            v1 = (short)(HalfCircle - v1);
        return NormalizeAngle(v1);
    }

    /// <summary>square_root from libs/math/sqrt.c (integer square root).</summary>
    public static ushort SquareRoot(uint value)
    {
        ushort sigWord, mask, result, shift;

        if ((sigWord = (ushort)(value >> 16)) > 0)
        {
            for (mask = 1 << 15, shift = 31; (mask & sigWord) == 0; mask >>= 1, --shift)
            {
            }
            shift >>= 1;
            mask = (ushort)(1 << shift);
            result = mask;
            uint maskSquared, resultShift;
            maskSquared = resultShift = (uint)mask << shift;
            value -= maskSquared;
            while ((mask >>= 1) != 0)
            {
                maskSquared >>= 2;
                uint remainder;
                if ((remainder = resultShift + maskSquared) > value)
                    resultShift >>= 1;
                else
                {
                    value -= remainder;
                    resultShift = (resultShift >> 1) + maskSquared;
                    result |= mask;
                }
            }
            return result;
        }
        if ((sigWord = (ushort)value) > 0)
        {
            for (mask = 1 << 15, shift = 15; (mask & sigWord) == 0; mask >>= 1, --shift)
            {
            }
            shift >>= 1;
            mask = (ushort)(1 << shift);
            result = mask;
            ushort maskSquared, resultShift;
            maskSquared = resultShift = (ushort)(mask << shift);
            sigWord -= maskSquared;
            while ((mask >>= 1) != 0)
            {
                maskSquared >>= 2;
                ushort remainder;
                if ((remainder = (ushort)(resultShift + maskSquared)) > sigWord)
                    resultShift >>= 1;
                else
                {
                    sigWord -= remainder;
                    resultShift = (ushort)((resultShift >> 1) + maskSquared);
                    result |= mask;
                }
            }
            return result;
        }
        return 0;
    }
}
