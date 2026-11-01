// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/solarsys.c: GetOrbitRect,
// GetPlanetOrbitRect, ValidateOrbit, CalcSunSize, DrawSystem, and the
// orbit colours of LoadSolarSys),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>A rectangle on the system screen (UQM's RECT, 16-bit fields).</summary>
public readonly record struct IpRect(int X, int Y, int Width, int Height)
{
    public bool Contains(IpPoint p) => p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
}

/// <summary>An RGB colour (8 bits a channel).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>MAKE_RGB15: UQM's 5-bit channels widened to 8 bits.</summary>
    public static Rgb From15(int r, int g, int b) => new(Widen(r), Widen(g), Widen(b));

    private static byte Widen(int c) => (byte)((c << 3) | (c >> 2));
}

/// <summary>
/// Where things are drawn in a system's views, on UQM's 242 x 227 system
/// screen: the orbit ellipses, the planets in the outer view at a zoom, and
/// the planet and its moons in the inner view. Used by the flight (which
/// touches what is drawn) and by the client (which draws it), so both agree.
/// </summary>
public static class SystemLayout
{
    public const int CenterX = IpGeometry.ScreenWidth >> 1;
    public const int CenterY = IpGeometry.ScreenHeight >> 1;

    /// <summary>The orbit colours, cold to hot (temp_color_array in LoadSolarSys).</summary>
    public static readonly IReadOnlyList<Rgb> OrbitColors = new[]
    {
        Rgb.From15(0x00, 0x00, 0x0E),
        Rgb.From15(0x00, 0x06, 0x08),
        Rgb.From15(0x00, 0x0B, 0x00),
        Rgb.From15(0x0F, 0x00, 0x00),
        Rgb.From15(0x0F, 0x08, 0x00),
    };

    /// <summary>GetOrbitRect.</summary>
    public static IpRect OrbitRect(int dx, int dy, int radius, int xnumer, int ynumer, int denom)
    {
        short x = (short)(CenterX + (long)-dx * xnumer / denom);
        short y = (short)(CenterY + (long)-dy * ynumer / denom);
        short w = (short)((long)radius * (xnumer << 1) / denom);
        short h = (short)(w >> 1);
        return new IpRect(x, y, w, h);
    }

    /// <summary>
    /// GetPlanetOrbitRect: the ellipse a world's orbit is drawn as. With
    /// sizeNumer above DISPLAY_FACTOR (the inner view's arc of the planet's
    /// own orbit) it's offset by the world's position.
    /// </summary>
    public static IpRect WorldOrbitRect(WorldDesc w, int sizeNumer, int dyNumer, int denom)
    {
        int dx = w.Radius, dy = w.Radius;
        if (sizeNumer > IpGeometry.DisplayFactor)
        {
            dx += w.X;
            dy = (dy + w.Y) << 1;
        }
        return OrbitRect(dx, dy, w.Radius, sizeNumer, dyNumer, denom);
    }

    /// <summary>ValidateOrbit's image position (planets in the outer view, moons in the inner).</summary>
    public static IpPoint ImageOrigin(WorldDesc w, int sizeNumer, int dyNumer, int denom)
    {
        IpRect r = WorldOrbitRect(w, sizeNumer, dyNumer, denom);
        int x = (short)(r.X + (r.Width >> 1));
        int y = (short)(r.Y + (r.Height >> 1));
        x += (int)((long)w.X * sizeNumer / denom);
        y += (int)((long)w.Y * (sizeNumer / 2) / denom);
        return new IpPoint((short)x, (short)y);
    }

    /// <summary>
    /// A world's image size, 0..5 (UQM's orbital frame sizes, from the
    /// planet type's size plus <paramref name="add"/>), or -1 for the
    /// starbase and the Sa-Matra, which are drawn from other images.
    /// </summary>
    public static int ImageSize(SystemGenerator generator, byte dataIndex, int add)
    {
        if (!WorldTypes.HasPlanetData(dataIndex))
            return -1;
        return (int)generator.Types[WorldTypes.PlanetType(dataIndex)].Size + add;
    }

    /// <summary>How much bigger planets are drawn in the outer view at a zoom (ValidateOrbit).</summary>
    public static int OuterSizeAdd(int zoomRadius)
    {
        int add = 0;
        if (zoomRadius <= IpGeometry.MaxZoomRadius >> 2)
        {
            ++add;
            if (zoomRadius == IpGeometry.MinZoomRadius)
                ++add;
        }
        return add;
    }

    /// <summary>The added size of the planet in the middle of the inner view.</summary>
    public const int InnerPlanetSizeAdd = 3;

    /// <summary>The added size of moons in the inner view.</summary>
    public const int MoonSizeAdd = 2;

    /// <summary>CalcSunSize: the sun's image, 0 (small, zoomed out) to 2 (zoomed in).</summary>
    public static int SunSize(int zoomRadius)
    {
        int index = 0;
        if (zoomRadius <= IpGeometry.MaxZoomRadius >> 1)
        {
            ++index;
            if (zoomRadius <= IpGeometry.MaxZoomRadius >> 2)
                ++index;
        }
        return index;
    }

    // ---- what DrawSystem draws ----

    /// <summary>A planet's place in the outer view.</summary>
    public static IpPoint OuterPlanet(WorldDesc planet, int zoomRadius) =>
        ImageOrigin(planet, IpGeometry.DisplayFactor, IpGeometry.DisplayFactor / 4, zoomRadius);

    /// <summary>A planet's orbit in the outer view.</summary>
    public static IpRect OuterOrbit(WorldDesc planet, int zoomRadius) =>
        WorldOrbitRect(planet, IpGeometry.DisplayFactor, IpGeometry.DisplayFactor / 4, zoomRadius);

    /// <summary>A moon's place in the inner view.</summary>
    public static IpPoint InnerMoon(WorldDesc moon) => ImageOrigin(moon, 2, 1, 2);

    /// <summary>A moon's orbit in the inner view.</summary>
    public static IpRect InnerMoonOrbit(WorldDesc moon) => WorldOrbitRect(moon, 2, 1, 2);

    /// <summary>The arc of the planet's own orbit round the sun, through the middle of the inner view.</summary>
    public static IpRect InnerPlanetOrbit(WorldDesc planet) =>
        WorldOrbitRect(planet, IpGeometry.DisplayFactor * 4, IpGeometry.DisplayFactor, planet.Radius);
}
