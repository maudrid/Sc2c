// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/plangen.c
// GeneratePlanetSurface, MakeCrater, MakeStorms, MakeGasGiant, DitherMap,
// ValidateMap; src/uqm/planets/gentopo.c DeltaTopography),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.Systems;

/// <summary>
/// A world's surface height map, made as UQM's GeneratePlanetSurface makes
/// it on entering orbit: <see cref="Width"/> x <see cref="Height"/> signed
/// bytes, row by row. The port keeps UQM's integer types (COUNT is 16-bit
/// unsigned, SIZE and COORD 16-bit signed, elevations signed bytes), since
/// the maps depend on where they wrap.
/// </summary>
public static class SurfaceMap
{
    public const int Width = Surface.MapWidth;     // MAP_WIDTH
    public const int Height = Surface.MapHeight;   // MAP_HEIGHT

    private const int RangeShift = 6;               // RANGE_SHIFT
    private const int NumBandColors = 4;            // NUM_BAND_COLORS
    private const int DitherVariance = 1 << (RangeShift - 3);

    /// <summary>The height map of a world of this type and seed (not Earth's, which is an image).</summary>
    public static sbyte[] Generate(PlanetType type, uint randSeed)
    {
        var rng = new SysRandom(randSeed);
        var map = new sbyte[Width * Height];
        switch (type.Algorithm)
        {
            case MapAlgorithm.GasGiant:
                MakeGasGiant(rng, (ushort)type.NumFaults, map, Width, Height, (short)type.FaultDepth);
                break;
            default:
                if (type.NumFaults != 0)
                    DeltaTopography(rng, (ushort)type.NumFaults, map, 0, Width, Height, (short)type.FaultDepth);
                for (int i = 0; i < type.NumBlemishes; ++i)
                {
                    ushort loword = rng.RandomWord();
                    int width = ((loword >> 8) & 31) switch
                    {
                        0 => (loword & 0xff) % (Height >> 2) + (Height >> 2),
                        1 or 2 or 3 or 4 => (loword & 0xff) % (Height >> 3) + (Height >> 3),
                        _ => (loword & 0xff) % (Height >> 4) + 4,
                    };
                    loword = rng.RandomWord();
                    int x = (loword >> 8) % (Width - width);
                    int y = (loword & 0xff) % (Height - width);
                    MakeCrater(map, x, y, width, width, (short)(type.FaultDepth << 2), (short)-(type.FaultDepth << 2), false);
                }
                if (type.Algorithm == MapAlgorithm.Cratered)
                    DitherMap(rng, map);
                ValidateMap(map);
                break;
        }
        return map;
    }

    /// <summary>
    /// DeltaTopography (gentopo.c): faults, each raising the map on one side
    /// of a pair of lines and lowering it on the other. The rectangle starts
    /// at <paramref name="start"/> in <paramref name="map"/>.
    /// </summary>
    private static void DeltaTopography(SysRandom rng, ushort iterations, sbyte[] map, int start,
        int rectWidth, int rectHeight, short depthDelta)
    {
        short width = (short)rectWidth, height = (short)rectHeight;
        short deltaY = (short)((height - 1) << 1);
        do
        {
            if ((rng.Random() & 1) == 0)
                depthDelta = (short)-depthDelta;
            uint randVal = rng.Random();
            ushort w1 = (ushort)randVal, w2 = (ushort)(randVal >> 16);

            short x0Top = (short)((w1 & 0xff) % width);
            short x0Bot = (short)((w1 >> 8) % width);
            short x0Incr, dx0 = (short)((x0Bot - x0Top) << 1), err0;
            if (dx0 >= 0)
                x0Incr = 1;
            else
            {
                x0Incr = -1;
                dx0 = (short)-dx0;
            }
            err0 = dx0 > deltaY ? (short)-(dx0 >> 1) : (short)-(deltaY >> 1);

            short x1Top = (short)((w2 & 0xff) % (width - 1) + x0Top + 1);
            short x1Bot = (short)((w2 >> 8) % (width - 1) + x0Bot + 1);
            short x1Incr, dx1 = (short)((x1Bot - x1Top) << 1), err1;
            if (dx1 >= 0)
                x1Incr = 1;
            else
            {
                x1Incr = -1;
                dx1 = (short)-dx1;
            }
            err1 = dx1 > deltaY ? (short)-(dx1 >> 1) : (short)-(deltaY >> 1);

            int dst = start + x0Top;
            ushort h = (ushort)height;
            do
            {
                ushort w;
                w1 = (ushort)(x1Top - x0Top);
                w2 = (ushort)(width - w1);
                if (x0Top + w1 > width)
                    w = (ushort)(width - x0Top);
                else
                {
                    w = w1;
                    x0Top = (short)(x0Top + w1);
                }
                w1 = (ushort)(w1 - w);
                while (w-- != 0)
                    Add(map, dst++, depthDelta);
                if (w1 == 0)
                {
                    if (x0Top == width)
                    {
                        x0Top = 0;
                        dst -= width;
                    }
                }
                else
                {
                    x0Top = (short)w1;
                    dst -= width;
                    do
                        Add(map, dst++, depthDelta);
                    while (--w1 != 0);
                }

                if (x0Top + w2 > width)
                    w = (ushort)(width - x0Top);
                else
                {
                    w = w2;
                    x0Top = (short)(x0Top + w2);
                }
                w2 = (ushort)(w2 - w);
                while (w-- != 0)
                    Add(map, dst++, (short)-depthDelta);
                if (w2 == 0)
                {
                    if (x0Top == width)
                    {
                        x0Top = 0;
                        dst -= width;
                    }
                }
                else
                {
                    x0Top = (short)w2;
                    dst -= width;
                    do
                        Add(map, dst++, (short)-depthDelta);
                    while (--w2 != 0);
                }

                dst += rectWidth;
                if (deltaY >= dx0)
                {
                    if ((err0 = (short)(err0 + dx0)) >= 0)
                    {
                        dst += x0Incr;
                        x0Top = (short)(x0Top + x0Incr);
                        err0 = (short)(err0 - deltaY);
                    }
                }
                else
                {
                    do
                    {
                        dst += x0Incr;
                        x0Top = (short)(x0Top + x0Incr);
                    } while ((err0 = (short)(err0 + deltaY)) < 0);
                    err0 = (short)(err0 - dx0);
                }

                if (deltaY >= dx1)
                {
                    if ((err1 = (short)(err1 + dx1)) >= 0)
                    {
                        x1Top = (short)(x1Top + x1Incr);
                        err1 = (short)(err1 - deltaY);
                    }
                }
                else
                {
                    do
                        x1Top = (short)(x1Top + x1Incr);
                    while ((err1 = (short)(err1 + deltaY)) < 0);
                    err1 = (short)(err1 - dx1);
                }
            } while (--h != 0);
        } while (--iterations != 0);
    }

    // d = *lpDst + delta, stored only if it fits a signed byte.
    private static void Add(sbyte[] map, int at, short delta)
    {
        short d = (short)(map[at] + delta);
        if (d >= -128 && d <= 127)
            map[at] = (sbyte)d;
    }

    // DepthArray[i] += delta: the signed byte wraps.
    private static void Wrap(sbyte[] map, int at, int delta) => map[at] = unchecked((sbyte)(map[at] + delta));

    /// <summary>MakeCrater: an ellipse in the rectangle, its rim raised by rimDelta and its inside by craterDelta (zeroed first with setDepth).</summary>
    private static void MakeCrater(sbyte[] map, int cornerX, int cornerY, int extentW, int extentH,
        short rimDelta, short craterDelta, bool setDepth)
    {
        short a = (short)(extentW >> 1), b = (short)(extentH >> 1);
        short x = 0, y = b;
        long aSq = (uint)(a * a), twoASq = aSq << 1;
        long bSq = (uint)(b * b), twoBSq = bSq << 1;
        long dx = 0, dy = twoASq * b;
        long d = bSq - (dy >> 1) + (aSq >> 2);
        a = (short)(a + cornerX);
        b = (short)(b + cornerY);
        ushort top = (ushort)((b - y) * Width), bot = (ushort)((b + y) * Width);
        ushort rimPixels = 1;

        void Clear(int index, int lf, int rt)
        {
            for (int i = lf; i <= rt; ++i)
                map[index + i] = 0;
        }

        while (dx < dy)
        {
            if (d > 0)
            {
                short lf = (short)(a - x), rt = (short)(a + x);
                if (setDepth)
                {
                    Clear(top, lf, rt);
                    Clear(bot, lf, rt);
                }
                if (lf == rt)
                {
                    Wrap(map, top + lf, rimDelta);
                    Wrap(map, bot + lf, rimDelta);
                    rimPixels = 0;
                }
                else
                {
                    do
                    {
                        Wrap(map, top + lf, rimDelta);
                        Wrap(map, bot + lf, rimDelta);
                        if (lf != rt)
                        {
                            Wrap(map, top + rt, rimDelta);
                            Wrap(map, bot + rt, rimDelta);
                        }
                        ++lf;
                        --rt;
                    } while (--rimPixels != 0);
                    while (lf < rt)
                    {
                        Wrap(map, top + lf, craterDelta);
                        Wrap(map, bot + lf, craterDelta);
                        Wrap(map, top + rt, craterDelta);
                        Wrap(map, bot + rt, craterDelta);
                        ++lf;
                        --rt;
                    }
                    if (lf == rt)
                    {
                        Wrap(map, top + lf, craterDelta);
                        Wrap(map, bot + lf, craterDelta);
                    }
                }
                --y;
                top += Width;
                bot -= Width;
                dy -= twoASq;
                d -= dy;
            }
            ++rimPixels;
            ++x;
            dx += twoBSq;
            d += bSq + dx;
        }

        d += ((((aSq - bSq) * 3) >> 1) - (dx + dy)) >> 1;

        while (y > 0)
        {
            short lf = (short)(a - x), rt = (short)(a + x);
            if (setDepth)
            {
                Clear(top, lf, rt);
                Clear(bot, lf, rt);
            }
            if (lf == rt)
            {
                Wrap(map, top + lf, rimDelta);
                Wrap(map, bot + lf, rimDelta);
            }
            else
            {
                do
                {
                    Wrap(map, top + lf, rimDelta);
                    Wrap(map, bot + lf, rimDelta);
                    if (lf != rt)
                    {
                        Wrap(map, top + rt, rimDelta);
                        Wrap(map, bot + rt, rimDelta);
                    }
                    ++lf;
                    --rt;
                } while (--rimPixels != 0);
                while (lf < rt)
                {
                    Wrap(map, top + lf, craterDelta);
                    Wrap(map, bot + lf, craterDelta);
                    Wrap(map, top + rt, craterDelta);
                    Wrap(map, bot + rt, craterDelta);
                    ++lf;
                    --rt;
                }
                if (lf == rt)
                {
                    Wrap(map, top + lf, craterDelta);
                    Wrap(map, bot + lf, craterDelta);
                }
            }
            if (d < 0)
            {
                ++x;
                dx += twoBSq;
                d += dx;
            }
            rimPixels = 1;
            --y;
            top += Width;
            bot -= Width;
            dy -= twoASq;
            d += aSq - dy;
        }

        {
            short lf = (short)(a - x), rt = (short)(a + x);
            if (setDepth)
                Clear(top, lf, rt);
            if (lf == rt)
                Wrap(map, top + lf, rimDelta);
            else
            {
                do
                {
                    Wrap(map, top + lf, rimDelta);
                    if (lf != rt)
                        Wrap(map, top + rt, rimDelta);
                    ++lf;
                    --rt;
                } while (--rimPixels != 0);
                while (lf < rt)
                {
                    Wrap(map, top + lf, craterDelta);
                    Wrap(map, top + rt, craterDelta);
                    ++lf;
                    --rt;
                }
                if (lf == rt)
                    Wrap(map, top + lf, craterDelta);
            }
        }
    }

    /// <summary>DitherMap: each point moved up or down a little, using each random number a byte at a time.</summary>
    private static void DitherMap(SysRandom rng, sbyte[] map)
    {
        uint randVal = 0;
        for (int i = 0; i < Width * Height; ++i)
        {
            if ((i & 3) == 0)
                randVal = rng.Random();
            else
                randVal >>= 8;
            Wrap(map, i, DitherVariance / 2 - (int)(randVal & (DitherVariance - 1)));
        }
    }

    private struct StormRect
    {
        public short X, Y, W, H;
    }

    /// <summary>MakeStorms: a gas giant's storms, rings of craters that don't overlap.</summary>
    private static void MakeStorms(SysRandom rng, ushort stormCount, sbyte[] map)
    {
        var storms = new StormRect[8];   // MAX_STORMS
        ushort i = stormCount;
        int p = i;
        while (i-- != 0)
        {
            --p;
            ushort loword;
            bool intersect;
            do
            {
                intersect = false;
                uint randVal = rng.Random();
                ushort hiword = (ushort)(randVal >> 16);
                storms[p].H = (short)(((hiword >> 8) & 31) switch
                {
                    0 => (hiword & 0xff) % (Height >> 2) + (Height >> 2),
                    1 or 2 or 3 or 4 => (hiword & 0xff) % (Height >> 3) + (Height >> 3),
                    _ => (hiword & 0xff) % (Height >> 4) + 4,
                });
                if (storms[p].H <= 4)
                    storms[p].H += 4;

                randVal = rng.Random();
                loword = (ushort)randVal;
                storms[p].W = (short)(storms[p].H + (loword & 0xff) % storms[p].H);
                storms[p].X = (short)((loword >> 8) % (Width - storms[p].W));
                storms[p].Y = (short)((loword & 0xff) % (Height - storms[p].H));

                for (int j = i + 1; j < stormCount; ++j)
                {
                    short x = (short)(storms[j].X - storms[p].X);
                    short y = (short)(storms[j].Y - storms[p].Y);
                    short w = (short)(x + storms[j].W + 4);
                    short h = (short)(y + storms[j].H + 4);
                    intersect = w > 0 && h > 0 && x < storms[p].W + 4 && y < storms[p].H + 4;
                    if (intersect)
                        break;
                }
            } while (intersect);

            ref StormRect r = ref storms[p];
            MakeCrater(map, r.X, r.Y, r.W, r.H, 6, 6, false);
            Shrink(ref r);

            short bandDelta = (short)((loword >> 8) & ((3 << RangeShift) + 20));
            MakeCrater(map, r.X, r.Y, r.W, r.H, bandDelta, bandDelta, true);
            Shrink(ref r);

            bandDelta += 2;
            if (r.W > 2 && r.H > 2)
            {
                MakeCrater(map, r.X, r.Y, r.W, r.H, bandDelta, bandDelta, true);
                Shrink(ref r);
            }
            bandDelta += 2;
            if (r.W > 2 && r.H > 2)
            {
                MakeCrater(map, r.X, r.Y, r.W, r.H, bandDelta, bandDelta, true);
                Shrink(ref r);
            }
            bandDelta += 4;
            MakeCrater(map, r.X, r.Y, r.W, r.H, bandDelta, bandDelta, true);
        }
    }

    private static void Shrink(ref StormRect r)
    {
        ++r.X;
        ++r.Y;
        r.W -= 2;
        r.H -= 2;
    }

    /// <summary>MakeGasGiant: bands of colour with faults along their edges, then storms, then dither.</summary>
    private static void MakeGasGiant(SysRandom rng, ushort numBands, sbyte[] map, int rectWidth, int rectHeight, short depthDelta)
    {
        ushort bandHeight = (ushort)(rectHeight / numBands);
        short bandBump = (short)(rectHeight % numBands);
        short bandError = (short)(numBands >> 1);
        int dst = 0;
        short bandDelta = (short)(((rng.RandomWord() & (NumBandColors - 1)) << RangeShift) + (1 << (RangeShift - 1)));
        short lastY = 0, nextY = 0;
        for (ushort i = numBands; i > 0; --i)
        {
            uint randVal = rng.Random();
            ushort loword = (ushort)randVal, hiword = (ushort)(randVal >> 16);
            short curY;
            nextY = (short)(nextY + bandHeight);
            if ((bandError = (short)(bandError - bandBump)) < 0)
            {
                ++nextY;
                bandError = (short)(bandError + numBands);
            }
            if (i == 1)
                curY = (short)rectHeight;
            else
            {
                curY = (short)(nextY + ((bandHeight - 2) >> 1) - ((hiword & 0xff) % (bandHeight - 2) + 1));
                DeltaTopography(rng, 50, map, (curY - 2) * rectWidth, rectWidth, 5, depthDelta);
            }
            for (ushort j = (ushort)(curY - lastY); j > 0; --j)
            {
                for (ushort k = (ushort)rectWidth; k > 0; --k)
                    Wrap(map, dst++, bandDelta);
            }
            lastY = curY;
            bandDelta = (short)((bandDelta + ((((loword & 0xff) & 1) << 1) - 1 << RangeShift))
                & ((1 << RangeShift) * NumBandColors - 1));
        }
        MakeStorms(rng, (ushort)(4 + (rng.Random() & 3) + 1), map);
        DitherMap(rng, map);
    }

    /// <summary>
    /// ValidateMap: finds the elevation the first row mostly keeps (across
    /// jumps of more than 128, where a signed byte wrapped), then flattens
    /// every such jump in the whole map to the value before it.
    /// </summary>
    private static void ValidateMap(sbyte[] map)
    {
        byte state = 0;
        var count = new byte[2];
        var lb = new sbyte[2];
        int at = 0;
        sbyte last = map[at++];
        ushort i = Width - 1;
        do
        {
            if (count[state]++ == 0)
                lb[state] = last;
            if (last > map[at])
            {
                if (last - map[at] > 128)
                    state ^= 1;
            }
            else if (map[at] - last > 128)
                state ^= 1;
            last = map[at++];
        } while (--i != 0);

        last = count[0] > count[1] ? lb[0] : lb[1];
        for (at = 0; at < Width * Height; ++at)
        {
            if (last > map[at])
            {
                if (last - map[at] > 128)
                    map[at] = last;
            }
            else if (map[at] - last > 128)
                map[at] = last;
            last = map[at];
        }
    }
}
