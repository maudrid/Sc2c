// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/plangen.c
// GenerateLightMap, GenerateSphereMask, CreateSphereTiltMap,
// create_aa_points, RenderPlanetSphere, CreateShieldMask; pl_stuff.c
// InitSphereRotation, PrepareNextRotationFrame, RotatePlanetSphere),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// The rotating planet in orbit, as UQM renders it: the surface map wrapped
/// round a sphere of radius <see cref="Radius"/>, tilted by the axial tilt,
/// lit from the sun's direction, with each point's relief from the light
/// map; shielded worlds tinted red, with a red halo (<see cref="ShieldHalo"/>,
/// UQM's default PC shield, which doesn't throb). Frames are RGBA,
/// <see cref="Diameter"/> square, transparent outside the sphere.
/// UQM computes the sphere with C's double maths; .NET on Linux calls the
/// same C library, and the tests check the frames bit for bit.
/// </summary>
public sealed class PlanetSphere
{
    public const int Radius = 37;                       // RADIUS
    public const int Diameter = 2 * Radius + 1;         // DIAMETER
    public const int ShieldHaloWidth = 7;               // SHIELD_HALO
    public const int ShieldRadius = Radius + ShieldHaloWidth;
    public const int ShieldDiameter = 2 * ShieldRadius + 1;   // SHIELD_DIAM
    public const int SpanX = SurfaceMap.Height;          // SPHERE_SPAN_X (MAP_HEIGHT)

    /// <summary>PLANET_ORG_Y: the sphere's centre is at (SIS_SCREEN_WIDTH / 2, this) on the orbit screen.</summary>
    public const int CenterY = (IpGeometry.ScreenHeight - SurfaceMap.Height - 5 - 9) / 2;
    public const int CenterX = IpGeometry.ScreenWidth / 2;

    /// <summary>PLANET_ROTATION_RATE: game ticks (ONE_SECOND 840) per rotation step; a turn is 12 seconds.</summary>
    public const int TicksPerStep = 840 * 12 / SurfaceMap.Width;

    private const int RadiusSquared = Radius * Radius;
    private const int RadiusThreshold = (Radius + 1) * (Radius + 1);
    private const int DiffuseBits = 16, AaWeightBits = 16;
    private const double AmbientLight = 0.1, LightZ = 1.2;
    private const int ShieldGlow = 120, ShieldReflect = 100;   // SHIELD_GLOW_COMP, SHIELD_REFLECT_COMP

    private readonly Rgb[] _colors;
    private readonly sbyte[] _light;
    private readonly bool _shielded;
    private readonly uint[] _diffuse = new uint[Diameter * Diameter];   // light_diff
    private readonly AaPoint[] _rotate = new AaPoint[Diameter * Diameter];   // map_rotate

    private struct AaPoint
    {
        public short X0, Y0, X1, Y1, X2, Y2, X3, Y3;
        public uint M0, M1, M2, M3;
    }

    /// <summary>
    /// The sphere of a world: its coloured map (before any shield tint, as
    /// GeneratePlanetSurface reads it back), its light map
    /// (<see cref="LightMap"/>), and where it is from the sun (a moon uses
    /// its planet's place) and its axial tilt.
    /// </summary>
    public PlanetSphere(Rgb[] colors, sbyte[] lightMap, short sunX, short sunY, int axialTilt, bool shielded)
    {
        _colors = colors;
        _light = lightMap;
        _shielded = shielded;
        Direction = 1 - 2 * (axialTilt & 1);
        GenerateSphereMask(sunX, sunY);
        CreateSphereTiltMap(axialTilt);
    }

    /// <summary>Which way it turns: +1 or -1 map columns a step (InitSphereRotation: 1 - 2 * (AxialTilt &amp; 1)).</summary>
    public int Direction { get; }

    /// <summary>The map column at the front after <paramref name="steps"/> rotation steps (the first frame, InitSphereRotation's, is step 1).</summary>
    public int Offset(long steps) => (int)(((steps * Direction) % SurfaceMap.Width + SurfaceMap.Width) % SurfaceMap.Width);

    /// <summary>
    /// GenerateLightMap: the height map made into a light map, each point's
    /// relief against the weighted average of the points around it. Gas
    /// giants are flat (all zero), as GeneratePlanetSurface makes them.
    /// </summary>
    public static sbyte[] LightMap(sbyte[] heights, bool gasGiant)
    {
        const int w = SurfaceMap.Width, h = SurfaceMap.Height;
        var topo = (sbyte[])heights.Clone();
        if (gasGiant)
            return new sbyte[topo.Length];
        int min = 127, max = -128;
        foreach (sbyte v in topo)
        {
            if (v > max)
                max = v;
            if (v < min)
                min = v;
        }
        int med = (min + max) / 2;
        int spread = max - med;
        if (spread == 0)
        {
            if (max != 0)
                Array.Clear(topo);
            return topo;
        }
        int sfact = spread < 10 ? 30 : spread < 30 ? 60 : 100;
        for (int i = 0; i < topo.Length; ++i)
            topo[i] = unchecked((sbyte)((topo[i] - med) * sfact / spread));

        const int avgBlock = (h + 4) / 5, maxDist = (avgBlock + 1) >> 1, weightThres = maxDist * 2 / 3;
        const int blocks = 2 * maxDist + 1;
        var bMin = new int[blocks];
        var bMax = new int[blocks];
        var bAvg = new int[blocks];

        void Block(int slot, int x, int y)
        {
            int lo = 127, hi = -127, avg = 0, total = 0;
            x = (x + w) % w;
            int y0 = Math.Max(y - maxDist, 0), y1 = Math.Min(y + maxDist, h);
            for (int i = y0; i < y1; ++i)
            {
                int delta = Math.Abs(i - y), weight = 255;
                if (delta >= weightThres)
                    weight = weight * (maxDist - delta + 1) / (maxDist - weightThres + 2);
                int v = topo[i * w + x];
                if (v > hi)
                    hi = v;
                if (v < lo)
                    lo = v;
                avg += v * weight;
                total += weight;
            }
            avg /= total;
            bMin[slot] = lo;
            bMax[slot] = hi;
            bAvg[slot] = avg / (y1 - y0);
        }

        int at = 0;
        for (int y = 0; y < h; ++y)
        {
            for (int i = -maxDist; i < maxDist; ++i)
                Block((i + blocks) % blocks, i, y);
            for (int x = 0; x < w; ++x, ++at)
            {
                int avg = 0, total = 0;
                min = 127;
                max = -127;
                Block((x + maxDist) % blocks, x + maxDist, y);
                for (int i = x - maxDist; i <= x + maxDist; ++i)
                {
                    int delta = Math.Abs(i - x), weight = 255;
                    int slot = (i + blocks) % blocks;
                    if (delta >= weightThres)
                        weight = weight * (maxDist - delta + 1) / (maxDist - weightThres + 2);
                    if (bMax[slot] > max)
                        max = bMax[slot];
                    if (bMin[slot] < min)
                        min = bMin[slot];
                    avg += bAvg[slot] * weight;
                    total += weight;
                }
                avg /= total;
                med = (min + max) / 2;
                med = (med + avg) / 2;
                topo[at] = unchecked((sbyte)(topo[at] - med));
            }
        }
        return topo;
    }

    // GenerateSphereMask: the diffuse light at each point of the sphere, from the sun's direction.
    private void GenerateSphereMask(short locX, short locY)
    {
        const uint step = 1u << DiffuseBits;
        double lrad = Math.Sqrt(locX * locX + locY * locY);
        var (lx, ly, lz) = Norm(-(double)locX, -(double)locY, LightZ * lrad);
        for (int py = 0, y = -Radius; py < Diameter; ++py, ++y)
        {
            uint y2 = (uint)(y * y);
            for (int px = 0, x = -Radius; px < Diameter; ++px, ++x)
            {
                uint x2 = (uint)(x * x), rad2 = x2 + y2, diffInt = 0;
                if (rad2 < RadiusThreshold)
                {
                    var (nx, ny, nz) = Norm(x, y, Math.Sqrt(unchecked((uint)RadiusSquared - x2)) * Math.Sqrt(unchecked((uint)RadiusSquared - y2)) / Radius);
                    double diff = nx * lx + ny * ly + nz * lz;
                    if (diff < 0)
                        diff = 0.0;
                    if (diff < AmbientLight)
                        diff = AmbientLight;
                    if (rad2 > RadiusSquared)
                    {
                        diff *= 1 - (Math.Sqrt(rad2) - Radius);
                        if (diff < 0)
                            diff = 0;
                    }
                    diffInt = (uint)(diff * step);
                }
                _diffuse[py * Diameter + px] = diffInt;
            }
        }
    }

    // P3norm: the vector over its length, as (1 / mag) times each part.
    private static (double, double, double) Norm(double x, double y, double z)
    {
        double mag = Math.Sqrt(x * x + y * y + z * z);
        double k = 1 / mag;
        return (x * k, y * k, z * k);
    }

    // CreateSphereTiltMap: for each point of the sphere, where on the map it shows, tilted.
    private void CreateSphereTiltMap(int angle)
    {
        const double multx = SpanX / Math.PI, multy = SurfaceMap.Height / Math.PI, xadj = SpanX / 2.0;
        const double deg2rad = Math.PI * 2.0 / 360.0;   // M_DEG2RAD
        for (int y = -Radius; y <= Radius; y++)
        {
            int y2 = y * y;
            for (int x = -Radius; x <= Radius; x++)
            {
                ref AaPoint ppt = ref _rotate[(y + Radius) * Diameter + x + Radius];
                double rad2 = x * x + y2;
                if (rad2 >= RadiusThreshold)
                {
                    ppt.X0 = (short)(x + Radius);
                    ppt.Y0 = (short)(y + Radius);
                    ppt.M0 = 0;
                    continue;
                }
                double rad = Math.Sqrt(rad2);
                if (rad >= Radius)
                    rad = Radius - 0.1;
                double da = Math.Atan2(y, x);
                da += deg2rad * angle;
                double dx = rad * Math.Cos(da), dy = rad * Math.Sin(da);
                double xa = Math.Acos(-dx / Radius), ya = Math.Acos(-dy / Radius);
                double newx = multx * xa, newy = multy * ya;
                if (ya <= 0.05 || ya >= 3.1)
                    newx = xadj;
                else
                    newx = xadj + (newx - xadj) / Math.Sin(ya);
                CreateAaPoints(ref ppt, newx, newy);
            }
        }
    }

    // create_aa_points: the four map points around (x, y), weighted by distance; m sums to 65536.
    private static void CreateAaPoints(ref AaPoint ppt, double x, double y)
    {
        if (x < 0)
            x = 0;
        else if (x >= SpanX)
            x = SpanX - 1;
        if (y < 0)
            y = 0;
        else if (y >= SurfaceMap.Height)
            y = SurfaceMap.Height - 1;
        ppt.X0 = (short)x;
        ppt.Y0 = (short)y;
        double deltax = x - ppt.X0, deltay = y - ppt.Y0;
        if (deltax == 0 && deltay == 0)
        {
            ppt.M0 = 0;
            return;
        }
        short nextx = deltax != 0 ? (short)(ppt.X0 + 1) : ppt.X0;
        short nexty = deltay != 0 ? (short)(ppt.Y0 + 1) : ppt.Y0;
        ppt.X1 = nextx;
        ppt.Y1 = ppt.Y0;
        ppt.X2 = ppt.X0;
        ppt.Y2 = nexty;
        ppt.X3 = nextx;
        ppt.Y3 = nexty;
        double invx = 1.0 - Math.Abs(deltax);
        invx *= invx;
        double invy = 1.0 - Math.Abs(deltay);
        invy *= invy;
        deltax *= deltax;
        deltay *= deltay;
        double d1 = Math.Sqrt(deltax + deltay), d2 = Math.Sqrt(invx + deltay);
        double d3 = Math.Sqrt(deltax + invy), d4 = Math.Sqrt(invx + invy);
        double m0 = 1 / (1 + d1 * (1 / d2 + 1 / d3 + 1 / d4));
        double m1 = m0 * d1 / d2, m2 = m0 * d1 / d3, m3 = m0 * d1 / d4;
        ppt.M0 = (uint)(m0 * (1 << AaWeightBits) + 0.5);
        ppt.M1 = (uint)(m1 * (1 << AaWeightBits) + 0.5);
        ppt.M2 = (uint)(m2 * (1 << AaWeightBits) + 0.5);
        ppt.M3 = (uint)(m3 * (1 << AaWeightBits) + 0.5);
    }

    /// <summary>
    /// RenderPlanetSphere: the frame with map column <paramref name="offset"/>
    /// at the front, as RGBA bytes, <see cref="Diameter"/> square.
    /// </summary>
    public byte[] Render(int offset)
    {
        var frame = new byte[Diameter * Diameter * 4];
        const int w = SurfaceMap.Width;
        Rgb Pixel(int x, int y) => _colors[y * w + (x + offset) % w];
        int Elev(int x, int y) => _light[y * w + (offset + x) % w];

        for (int i = 0; i < Diameter * Diameter; ++i)
        {
            uint diffus = _diffuse[i];
            if (diffus == 0)
                continue;   // transparent
            ref AaPoint p = ref _rotate[i];
            byte r, g, b;
            int lvf;
            if (p.M0 == 0)
            {
                var c = Pixel(p.X0, p.Y0);
                (r, g, b) = (c.R, c.G, c.B);
                lvf = Elev(p.X0, p.Y0);
            }
            else
            {
                Rgb c0 = Pixel(p.X0, p.Y0), c1 = Pixel(p.X1, p.Y1), c2 = Pixel(p.X2, p.Y2), c3 = Pixel(p.X3, p.Y3);
                r = Avg(c0.R, c1.R, c2.R, c3.R, p);
                g = Avg(c0.G, c1.G, c2.G, c3.G, p);
                b = Avg(c0.B, c1.B, c2.B, c3.B, p);
                // UQM takes the light variance of the first point four times (ppt->p[0] for every i).
                int e = Elev(p.X0, p.Y0);
                int lvsum = unchecked((int)((uint)e * p.M0 + (uint)e * p.M1 + (uint)e * p.M2 + (uint)e * p.M3));
                lvf = lvsum >> AaWeightBits;
            }
            if (_shielded)
            {
                g = (byte)((g >> 1) + (g >> 2));   // a light red filter (3/4)
                b = (byte)((b >> 1) + (b >> 2));
                r = Light(r, diffus, lvf);
                g = Light(g, diffus, lvf);
                b = Light(b, diffus, lvf);
                int red = Light(ShieldReflect, diffus, 0) + ShieldGlow + r;   // glow + reflection, no throb
                r = (byte)Math.Min(red, 255);
            }
            else
            {
                r = Light(r, diffus, lvf);
                g = Light(g, diffus, lvf);
                b = Light(b, diffus, lvf);
            }
            frame[i * 4] = r;
            frame[i * 4 + 1] = g;
            frame[i * 4 + 2] = b;
            frame[i * 4 + 3] = 0xff;
        }
        return frame;
    }

    // get_avg_channel: the weighted sum of the four, kept to 255.
    private static byte Avg(byte c0, byte c1, byte c2, byte c3, in AaPoint p)
    {
        uint ci = unchecked(c0 * p.M0 + c1 * p.M1 + c2 * p.M2 + c3 * p.M3) >> AaWeightBits;
        return (byte)Math.Min(ci, 255u);
    }

    // calc_map_light: diffuse light, then the light variance for relief.
    private static byte Light(byte val, uint dif, int lvf)
    {
        int i = (int)((dif * val) >> DiffuseBits);
        i += (lvf * val) >> 7;
        return (byte)Math.Clamp(i, 0, 255);
    }

    /// <summary>
    /// CreateShieldMask: the shield's red halo round the sphere, RGBA,
    /// <see cref="ShieldDiameter"/> square, transparent over the planet.
    /// </summary>
    public static byte[] ShieldHalo()
    {
        const int glow = ShieldGlow + ShieldReflect, glowMin = glow >> 2;
        const int thres = (ShieldRadius + 1) * (ShieldRadius + 1);
        var halo = new byte[ShieldDiameter * ShieldDiameter * 4];
        int at = 0;
        for (int y = -ShieldRadius; y <= ShieldRadius; y++)
        {
            for (int x = -ShieldRadius; x <= ShieldRadius; ++x, at += 4)
            {
                int rad2 = x * x + y * y;
                int red = glow, alpha = 255;
                if (rad2 >= thres || rad2 <= RadiusSquared)
                    continue;   // outside, or the planet's own pixels: transparent
                double rad = Math.Sqrt(rad2);
                if (rad <= Radius + 0.8)
                {
                    alpha = (int)(red * (rad - Radius));   // antialiased edge
                    red = 255;
                }
                else
                {
                    red -= (int)((red - glowMin) * (rad - Radius) / ShieldHaloWidth);
                    if (red < 0)
                        red = 0;
                }
                halo[at] = (byte)red;
                halo[at + 3] = (byte)alpha;
            }
        }
        return halo;
    }
}
