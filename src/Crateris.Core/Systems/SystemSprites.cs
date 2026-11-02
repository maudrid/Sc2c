// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/solarsys.c: sortPlanetPositions,
// ValidateOrbit's choice of image, CalcSunSize, SetPlanetColorMap,
// DrawInnerPlanets, DrawSystem and CreateStarBackGround),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>The sprite sheets of a system's views (the numbers are the recorder's).</summary>
public enum SystemSheet
{
    /// <summary>graphics.flagship: the flagship, by facing.</summary>
    Flagship = 1,

    /// <summary>graphics.planets: worlds, (size &lt;&lt; 4) + facing towards the light.</summary>
    Worlds = 2,

    /// <summary>graphics.orbitbackground: the background stars, the starbase, the shield and the Sa-Matra.</summary>
    Junk = 3,

    /// <summary>graphics.truespacesun: the sun, by star size and zoom.</summary>
    Sun = 5,
}

/// <summary>
/// One sprite drawn in a system view: a frame of a sheet at a place on the
/// system screen (the frame's hot spot goes there), and for worlds and the
/// sun the colour table entry it's drawn with (-1 for the others). World
/// says what it shows: in the outer view the planet's number (-1 the sun),
/// in the inner view the moon's number (-1 the planet itself), and -1 for
/// background stars.
/// </summary>
public readonly record struct SystemStamp(SystemSheet Sheet, int Frame, int X, int Y, int ColorEntry, int World = -1);

/// <summary>
/// What UQM's DrawSystem stamps in the outer and inner views, in its
/// order, and the system's star background (CreateStarBackGround).
/// </summary>
public static class SystemSprites
{
    public const int StarbaseFrame = 16;
    public const int ShieldFrame = 17;
    public const int SaMatraFrame = 19;

    /// <summary>The background: 8 dim pieces drawn 5 times each, then 8 bright ones 30 times each.</summary>
    public const int DimPieces = 8, DimDrawn = 5, BrightPieces = 8, BrightDrawn = 30;

    /// <summary>PLANCOLOR: a colour family's entry in colortable.orbplan.</summary>
    public static int ColorEntry(string bodyColor) => bodyColor switch
    {
        "BLUE_BODY" => 0,
        "GREEN_BODY" => 1,
        "ORANGE_BODY" => 2,
        "RED_BODY" => 3,
        "WHITE_BODY" or "GRAY_BODY" => 4,
        "YELLOW_BODY" => 5,
        "CYAN_BODY" => 6,
        "PURPLE_BODY" => 7,
        "VIOLET_BODY" => 8,
        _ => throw new ArgumentException($"unknown planet colour {bodyColor}"),
    };

    /// <summary>
    /// sortPlanetPositions: the sun (-1) and the planets in drawing order.
    /// UQM sorts once on entering the system, by each one's location
    /// (the sun's is 0, 0), not where it's drawn, so the order is the same
    /// at every zoom.
    /// </summary>
    public static int[] DrawOrder(SolarSystem system)
    {
        int n = system.Planets.Count;
        var order = new int[n + 1];
        for (int i = 0; i <= n; ++i)
            order[i] = i - 1;
        int Y(int i) => i < 0 ? 0 : system.Planets[i].Y;
        for (int i = 0; i <= n; ++i)
            for (int j = n; j > i; --j)
                if (Y(order[i]) > Y(order[j]))
                    (order[i], order[j]) = (order[j], order[i]);
        return order;
    }

    /// <summary>
    /// ValidateOrbit's image for a world: an orbital frame by size, lit from
    /// the sun (a moon by its planet's direction from the sun), or the
    /// starbase's or the Sa-Matra's.
    /// </summary>
    public static (SystemSheet Sheet, int Frame) WorldImage(SystemGenerator generator, WorldDesc world, int size, WorldDesc lightFrom)
    {
        if (WorldTypes.HasPlanetData(world.DataIndex))
        {
            int angle = UqmMath.ArcTan(lightFrom.X, lightFrom.Y);
            return (SystemSheet.Worlds, (size << UqmMath.FacingShift) + UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle)));
        }
        return (SystemSheet.Junk, world.DataIndex == WorldTypes.SaMatra ? SaMatraFrame : StarbaseFrame);
    }

    private static int Entry(SystemGenerator generator, WorldDesc w) =>
        ColorEntry(generator.Types[WorldTypes.PlanetType(w.DataIndex)].BodyColor);

    private static SystemStamp World(SystemGenerator generator, WorldDesc w, int size, WorldDesc lightFrom, IpPoint at, int world)
    {
        var (sheet, frame) = WorldImage(generator, w, size, lightFrom);
        return new SystemStamp(sheet, frame, at.X, at.Y, sheet == SystemSheet.Worlds ? Entry(generator, w) : -1, world);
    }

    /// <summary>DrawSystem's outer view at a zoom: the sun and planets in <see cref="DrawOrder"/>.</summary>
    public static List<SystemStamp> Outer(SolarSystem system, int zoomRadius)
    {
        var stamps = new List<SystemStamp>();
        int add = SystemLayout.OuterSizeAdd(zoomRadius);
        foreach (int i in DrawOrder(system))
        {
            if (i < 0)
                stamps.Add(new SystemStamp(SystemSheet.Sun, system.SunType + SystemLayout.SunSize(zoomRadius),
                    SystemLayout.CenterX, SystemLayout.CenterY, (int)system.Star.Color));
            else
            {
                WorldDesc pl = system.Planets[i];
                stamps.Add(World(system.Generator, pl, SystemLayout.ImageSize(system.Generator, pl.DataIndex, add), pl,
                    SystemLayout.OuterPlanet(pl, zoomRadius), i));
            }
        }
        return stamps;
    }

    /// <summary>
    /// DrawInnerPlanets: the planet in the middle (a shielded one as its
    /// shield), then its moons in order.
    /// </summary>
    public static List<SystemStamp> Inner(SolarSystem system, int planet)
    {
        var stamps = new List<SystemStamp>();
        WorldDesc pl = system.Planets[planet];
        var middle = new IpPoint(SystemLayout.CenterX, SystemLayout.CenterY);
        var s = World(system.Generator, pl, SystemLayout.ImageSize(system.Generator, pl.DataIndex, SystemLayout.InnerPlanetSizeAdd), pl, middle, -1);
        if (WorldTypes.HasPlanetData(pl.DataIndex) && WorldTypes.IsShielded(pl.DataIndex))
            s = new SystemStamp(SystemSheet.Junk, ShieldFrame, middle.X, middle.Y, -1);
        stamps.Add(s);
        WorldDesc[] moons = system.Moons(planet);
        for (int m = 0; m < moons.Length; ++m)
            stamps.Add(World(system.Generator, moons[m], SystemLayout.ImageSize(system.Generator, moons[m].DataIndex, SystemLayout.MoonSizeAdd), pl,
                SystemLayout.InnerMoon(moons[m]), m));
        return stamps;
    }

    /// <summary>
    /// CreateStarBackGround: the background stars, from the system
    /// generator's random numbers seeded with the star's seed. Each is a
    /// frame of the orbit background at a place on the system screen.
    /// </summary>
    public static List<SystemStamp> StarBackground(Star star)
    {
        var rng = new SysRandom(star.Seed);
        var stamps = new List<SystemStamp>(DimPieces * DimDrawn + BrightPieces * BrightDrawn);
        int frame = 0;
        void Pieces(int pieces, int drawn)
        {
            for (int i = 0; i < pieces; ++i, ++frame)
                for (int j = 0; j < drawn; ++j)
                {
                    uint v = rng.Random();
                    stamps.Add(new SystemStamp(SystemSheet.Junk, frame,
                        (ushort)v % IpGeometry.ScreenWidth, (ushort)(v >> 16) % IpGeometry.ScreenHeight, -1));
                }
        }
        Pieces(DimPieces, DimDrawn);
        Pieces(BrightPieces, BrightDrawn);
        return stamps;
    }
}
