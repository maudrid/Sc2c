// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0: DrawablesIntersect and
// frame_intersect (src/libs/graphics/intersec.c), BoxIntersect
// (src/libs/graphics/boxint.c) and the pixel test of
// TFB_DrawCanvas_Intersect (src/libs/graphics/sdl/canvas.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Uqm;

/// <summary>
/// A sprite frame as UQM's intersection sees it: its size, hot spot and
/// collision mask (one byte a pixel, row by row, nonzero where it can
/// touch). A frame whose mask isn't known (a true colour image without
/// alpha, which UQM converts to the screen's format) has a null mask and
/// can't be tested.
/// </summary>
public sealed class IntersectFrame
{
    public IntersectFrame(int width, int height, int hotSpotX, int hotSpotY, byte[]? mask)
    {
        if (mask != null && mask.Length != width * height)
            throw new ArgumentException("the mask isn't width x height");
        Width = width;
        Height = height;
        HotSpotX = hotSpotX;
        HotSpotY = hotSpotY;
        Mask = mask;
    }

    public int Width { get; }
    public int Height { get; }
    public int HotSpotX { get; }
    public int HotSpotY { get; }
    public byte[]? Mask { get; }

    internal bool Solid(int x, int y) => (Mask ?? throw new InvalidOperationException(
        "a frame whose mask isn't known was tested for contact"))[y * Width + x] != 0;
}

/// <summary>
/// UQM's INTERSECT_CONTROL: a frame moving from its origin to its end
/// point over one frame's time, and the last time it was found clear.
/// </summary>
public sealed class IntersectControl
{
    public IntersectControl(IntersectFrame frame, short x, short y)
    {
        Frame = frame;
        OriginX = EndX = x;
        OriginY = EndY = y;
    }

    public IntersectFrame Frame { get; set; }
    public short OriginX { get; set; }
    public short OriginY { get; set; }
    public short EndX { get; set; }
    public short EndY { get; set; }
    public ushort LastTime { get; set; }
}

/// <summary>
/// UQM's sprite intersection: whether two frames moving over one frame's
/// time touch, pixel against pixel, and when. Times are in 1/256 of the
/// frame: 1 is the start, MAX_TIME_VALUE the end; 0 means they don't touch.
/// </summary>
public static class Intersection
{
    public const int TimeShift = 8;
    public const ushort MaxTimeValue = (1 << TimeShift) + 1;

    private struct Rect
    {
        public short X, Y, W, H;
    }

    /// <summary>DrawablesIntersect. On contact, both controls' end points become where they touched.</summary>
    public static ushort DrawablesIntersect(IntersectControl c0, IntersectControl c1, ushort maxTime)
    {
        if (maxTime == 0)
            return 0;
        if (maxTime > MaxTimeValue)
            maxTime = MaxTimeValue;
        c0.LastTime = c1.LastTime = 0;

        Rect r0 = default, r1 = default;
        r0.X = c0.OriginX;
        r0.Y = c0.OriginY;
        r1.X = c1.OriginX;
        r1.Y = c1.OriginY;
        r0.W = (short)(c0.EndX - r0.X);
        r0.H = (short)(c0.EndY - r0.Y);
        r1.W = (short)(c1.EndX - r1.X);
        r1.H = (short)(c1.EndY - r1.Y);
        IntersectFrame f0 = c0.Frame, f1 = c1.Frame;
        r0.X = (short)(r0.X - f0.HotSpotX);
        r0.Y = (short)(r0.Y - f0.HotSpotY);
        r1.X = (short)(r1.X - f1.HotSpotX);
        r1.Y = (short)(r1.Y - f1.HotSpotY);

        short dy = (short)(r1.Y - r0.Y);
        short timeY0 = (short)(dy - f0.Height + 1);
        short timeY1 = (short)(dy + f1.Height - 1);
        dy = (short)(r0.H - r1.H);
        if (!((timeY0 <= 0 && timeY1 >= 0) || (timeY0 > 0 && dy >= timeY0) || (timeY1 < 0 && dy <= timeY1)))
            return 0;

        short dx = (short)(r1.X - r0.X);
        short timeX0 = (short)(dx - f0.Width + 1);
        short timeX1 = (short)(dx + f1.Width - 1);
        dx = (short)(r0.W - r1.W);
        if (!((timeX0 <= 0 && timeX1 >= 0) || (timeX0 > 0 && dx >= timeX0) || (timeX1 < 0 && dx <= timeX1)))
            return 0;

        if (dx == 0 && dy == 0)
            timeY0 = timeY1 = 0;
        else
        {
            short t;
            long timeBeg, timeEnd, fract;
            if (timeY1 < 0)
            {
                t = timeY0;
                timeY0 = (short)-timeY1;
                timeY1 = (short)-t;
            }
            else if (timeY0 <= 0)
            {
                if (dy < 0)
                    timeY1 = (short)-timeY0;
                timeY0 = 0;
            }
            if (dy < 0)
                dy = (short)-dy;
            if (dy < timeY1)
                timeY1 = dy;
            --timeY0;   // "just to be safe, widen search area"
            ++timeY1;

            if (timeX1 < 0)
            {
                t = timeX0;
                timeX0 = (short)-timeX1;
                timeX1 = (short)-t;
            }
            else if (timeX0 <= 0)
            {
                if (dx < 0)
                    timeX1 = (short)-timeX0;
                timeX0 = 0;
            }
            if (dx < 0)
                dx = (short)-dx;
            if (dx < timeX1)
                timeX1 = dx;
            --timeX0;
            ++timeX1;

            if (dx == 0)
            {
                timeBeg = timeY0;
                timeEnd = timeY1;
                fract = dy;
            }
            else if (dy == 0)
            {
                timeBeg = timeX0;
                timeEnd = timeX1;
                fract = dx;
            }
            else
            {
                long tx = (long)timeX0 * dy, ty = (long)timeY0 * dx;
                timeBeg = tx < ty ? ty : tx;
                tx = (long)timeX1 * dy;
                ty = (long)timeY1 * dx;
                timeEnd = tx > ty ? ty : tx;
                fract = (long)dx * dy;
            }

            if ((timeBeg <<= TimeShift) < fract)
                timeY0 = 0;
            else
                timeY0 = (short)(timeBeg / fract);

            if (timeEnd >= fract || (timeEnd <<= TimeShift) >= fract * maxTime)
                timeY1 = (short)(maxTime - 1);
            else
                timeY1 = (short)((short)((timeEnd + fract - 1) / fract) - 1);
        }

        if (timeY0 <= timeY1)
        {
            ushort time = FrameIntersect(c0, ref r0, c1, ref r1, (ushort)timeY0, (ushort)timeY1);
            if (time != 0)
            {
                c0.EndX = (short)(r0.X + f0.HotSpotX);
                c0.EndY = (short)(r0.Y + f0.HotSpotY);
                c1.EndX = (short)(r1.X + f1.HotSpotX);
                c1.EndY = (short)(r1.Y + f1.HotSpotY);
                return time;
            }
        }
        return 0;
    }

    private static ushort FrameIntersect(IntersectControl c0, ref Rect pr0, IntersectControl c1, ref Rect pr1, ushort t0, ushort t1)
    {
        IntersectFrame f0 = c0.Frame, f1 = c1.Frame;
        Rect ib0 = new() { X = pr0.X, Y = pr0.Y, W = (short)f0.Width, H = (short)f0.Height };
        Rect ib1 = new() { X = pr1.X, Y = pr1.Y, W = (short)f1.Width, H = (short)f1.Height };

        short dx0 = pr0.W, dy0 = pr0.H, xincr0 = 1, yincr0 = 1;
        if (dx0 < 0)
        {
            xincr0 = -1;
            dx0 = (short)-dx0;
        }
        if (dy0 < 0)
        {
            yincr0 = -1;
            dy0 = (short)-dy0;
        }
        short cycle0 = dx0 >= dy0 ? dx0 : dy0;
        short xerror0 = cycle0, yerror0 = cycle0;

        short dx1 = pr1.W, dy1 = pr1.H, xincr1 = 1, yincr1 = 1;
        if (dx1 < 0)
        {
            xincr1 = -1;
            dx1 = (short)-dx1;
        }
        if (dy1 < 0)
        {
            yincr1 = -1;
            dy1 = (short)-dy1;
        }
        short cycle1 = dx1 >= dy1 ? dx1 : dy1;
        short xerror1 = cycle1, yerror1 = cycle1;

        bool check0 = false, check1 = false;
        short timeError0, timeError1;
        bool first = false;   // "goto CheckFirstIntersection"
        if (t0 <= 1)
        {
            timeError0 = timeError1 = 0;
            if (t0 == 0)
            {
                ++t0;
                first = true;
            }
        }
        else
        {
            Advance(ref ib0, ref pr0, cycle0, dx0, dy0, xincr0, yincr0, ref xerror0, ref yerror0, t0, out timeError0);
            Advance(ref ib1, ref pr1, cycle1, dx1, dy1, xincr1, yincr1, ref xerror1, ref yerror1, t0, out timeError1);
        }

        if (!first)
            c0.LastTime = c1.LastTime = t0;   // skipped by the goto
        do
        {
            if (!first)
            {
                ++t0;
                if ((timeError0 += cycle0) >= (1 << TimeShift))
                {
                    if ((xerror0 -= dx0) <= 0)
                    {
                        ib0.X += xincr0;
                        xerror0 += cycle0;
                    }
                    if ((yerror0 -= dy0) <= 0)
                    {
                        ib0.Y += yincr0;
                        yerror0 += cycle0;
                    }
                    check0 = true;
                    timeError0 -= 1 << TimeShift;
                }
                if ((timeError1 += cycle1) >= (1 << TimeShift))
                {
                    if ((xerror1 -= dx1) <= 0)
                    {
                        ib1.X += xincr1;
                        xerror1 += cycle1;
                    }
                    if ((yerror1 -= dy1) <= 0)
                    {
                        ib1.Y += yincr1;
                        yerror1 += cycle1;
                    }
                    check1 = true;
                    timeError1 -= 1 << TimeShift;
                }
                if (!(check0 || check1))
                    continue;
            }
            first = false;
            if (BoxIntersect(ib0, ib1, out Rect inter) && ImagesIntersect(f0, ib0, f1, ib1, inter))
                return t0;
            if (check0)
            {
                pr0.X = ib0.X;
                pr0.Y = ib0.Y;
                c0.LastTime = t0;
                check0 = false;
            }
            if (check1)
            {
                pr1.X = ib1.X;
                pr1.Y = ib1.Y;
                c1.LastTime = t0;
                check1 = false;
            }
        } while (t0 <= t1);
        return 0;
    }

    /// <summary>frame_intersect's start part way through the frame's time, for one box.</summary>
    private static void Advance(ref Rect ib, ref Rect pr, short cycle, short dx, short dy, short xincr, short yincr,
        ref short xerror, ref short yerror, ushort t0, out short timeError)
    {
        ushort start = (ushort)((ushort)cycle * (ushort)(t0 - 1));
        timeError = (short)(start & ((1 << TimeShift) - 1));
        if ((start >>= TimeShift) > 0)
        {
            long error;
            short delta;
            if ((error = xerror - (long)dx * start) > 0)
                xerror = (short)error;
            else
            {
                delta = (short)(-(short)(error / cycle) + 1);
                ib.X = (short)(ib.X + xincr * delta);
                xerror = (short)(error + (long)cycle * delta);
            }
            if ((error = yerror - (long)dy * start) > 0)
                yerror = (short)error;
            else
            {
                delta = (short)(-(short)(error / cycle) + 1);
                ib.Y = (short)(ib.Y + yincr * delta);
                yerror = (short)(error + (long)cycle * delta);
            }
            pr.X = ib.X;
            pr.Y = ib.Y;
        }
    }

    /// <summary>BoxIntersect: whether two boxes overlap, and where.</summary>
    private static bool BoxIntersect(Rect r1, Rect r2, out Rect inter)
    {
        const byte left = 1, top = 2, right = 4, bottom = 8, noclip = 128;
        byte code = noclip;
        inter = default;
        short x1 = (short)(r1.X - r2.X);
        short w1 = r1.W, w2 = r2.W, delta;
        if ((delta = (short)(w2 - x1)) <= w1)
        {
            if (delta != w1)
            {
                w1 = delta;
                code &= unchecked((byte)~noclip);
            }
            code |= right;
        }
        if (x1 <= 0)
        {
            if (x1 < 0)
            {
                w1 += x1;
                x1 = 0;
                code &= unchecked((byte)~noclip);
            }
            code |= left;
        }
        if (w1 <= 0)
            return false;
        short y1 = (short)(r1.Y - r2.Y);
        short h1 = r1.H, h2 = r2.H;
        if ((delta = (short)(h2 - y1)) <= h1)
        {
            if (delta != h1)
            {
                h1 = delta;
                code &= unchecked((byte)~noclip);
            }
            code |= bottom;
        }
        if (y1 <= 0)
        {
            if (y1 < 0)
            {
                h1 += y1;
                y1 = 0;
                code &= unchecked((byte)~noclip);
            }
            code |= top;
        }
        if (h1 <= 0)
            return false;
        inter.X = (short)(x1 + r2.X);
        inter.Y = (short)(y1 + r2.Y);
        inter.W = w1;
        inter.H = h1;
        return code != 0;
    }

    /// <summary>TFB_DrawCanvas_Intersect: any pixel of the overlap solid in both frames.</summary>
    private static bool ImagesIntersect(IntersectFrame f0, Rect b0, IntersectFrame f1, Rect b1, Rect inter)
    {
        int ox0 = inter.X - b0.X, oy0 = inter.Y - b0.Y;
        int ox1 = inter.X - b1.X, oy1 = inter.Y - b1.Y;
        for (int y = 0; y < inter.H; ++y)
            for (int x = 0; x < inter.W; ++x)
                if (f0.Solid(x + ox0, y + oy0) && f1.Solid(x + ox1, y + oy1))
                    return true;
        return false;
    }
}
