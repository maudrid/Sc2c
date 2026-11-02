// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/plangen.c
// GeneratePlanetSurface, RenderTopography),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Presentation;

/// <summary>A world's surface: its height map and its coloured map (the scan screen's map), <see cref="SurfaceMap.Width"/> x <see cref="SurfaceMap.Height"/>, row by row.</summary>
public sealed record PlanetSurface(sbyte[] Heights, Rgb[] Colors);

/// <summary>A world as the orbit screen shows it: the surface (the scan screen's map, with a shield's red) and the rotating planet (<see cref="PlanetSurfaces.Orbited"/>).</summary>
public sealed record OrbitedWorld(PlanetSurface Surface, PlanetSphere Sphere);

/// <summary>
/// Worlds' surfaces as UQM's GeneratePlanetSurface makes them on entering
/// orbit: the height map (<see cref="SurfaceMap"/>) coloured through the
/// planet type's translate table and colour map, chosen by the surface
/// temperature; Earth's from its own two images instead. Tables are read
/// from the content once per planet type.
/// </summary>
public sealed class PlanetSurfaces
{
    /// <summary>HOT_THRESHOLD and COLD_THRESHOLD: above them, the hot (2) and temperate (1) tables; otherwise the cold one (0).</summary>
    public const int HotThreshold = 100, ColdThreshold = -40;

    /// <summary>The resource of Earth's surface: its colours (frame 0) and elevations (frame 1).</summary>
    public const string EarthSurfaceResource = "graphics.earthmask";

    private readonly ContentPackage _package;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BinaryTable> _tables = new();
    private PlanetSurface? _earth;

    public PlanetSurfaces(ContentPackage package) => _package = package;

    /// <summary>
    /// The surface of a world, as UQM makes it on entering its orbit;
    /// <paramref name="orbit"/> is the planet report from entering it
    /// (<see cref="SolarSystem.EnterOrbit"/>), whose surface temperature
    /// picks the colours. A shielded world's coloured map has the shield's
    /// red added (ApplyShieldTint), as UQM shows it.
    /// </summary>
    public PlanetSurface For(SolarSystem system, int planet, int? moon, PlanetInfo orbit)
    {
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        var surface = Generated(system, planet, moon, orbit);
        return WorldTypes.IsShielded(world.DataIndex) ? ShieldTinted(surface) : surface;
    }

    /// <summary>
    /// Everything the orbit screen shows of a world, as GeneratePlanetSurface
    /// makes it on entering orbit: the surface (<see cref="For"/>, with a
    /// shield's red) and the rotating planet, which wraps the map as made,
    /// before the shield's tint, with its light map (flat for a gas giant),
    /// lit from where the world is (a moon from its planet's place) and
    /// tilted by its axial tilt. The orbit screen and the tests both build it
    /// here. Takes some milliseconds; safe to call from any thread.
    /// </summary>
    public OrbitedWorld Orbited(SolarSystem system, int planet, int? moon, PlanetInfo orbit)
    {
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        var generated = Generated(system, planet, moon, orbit);
        var type = system.Generator.Types[WorldTypes.PlanetType(world.DataIndex)];
        bool shielded = WorldTypes.IsShielded(world.DataIndex);
        var sun = system.Planets[planet];
        var sphere = new PlanetSphere(generated.Colors,
            PlanetSphere.LightMap(generated.Heights, type.Algorithm == MapAlgorithm.GasGiant),
            sun.X, sun.Y, orbit.AxialTilt, shielded);
        return new OrbitedWorld(shielded ? ShieldTinted(generated) : generated, sphere);
    }

    /// <summary>
    /// A world's planetside map, the one the lander drives over, coloured
    /// as the scan map is (GeneratePlanetSurface: TopoScale4x, then
    /// RenderTopography at four times the size), or null if the lander
    /// can't go down there (shielded, or a gas giant). Its heights are
    /// <see cref="PlanetsideMap.Generate"/>'s.
    /// </summary>
    public Rgb[]? Planetside(SolarSystem system, int planet, int? moon, PlanetInfo orbit)
    {
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        if (!PlanetsideMap.Landable(world.DataIndex, orbit))
            return null;
        var type = system.Generator.Types[WorldTypes.PlanetType(world.DataIndex)];
        var heights = PlanetsideMap.Generate(Generated(system, planet, moon, orbit).Heights, type);
        return Color(heights, type, orbit.SurfaceTemperature,
            Table(type.ColorMapResource), Table(type.TranslateTableResource));
    }

    /// <summary>A surface with the shield's red added to its coloured map (ApplyShieldTint).</summary>
    public static PlanetSurface ShieldTinted(PlanetSurface surface) =>
        surface with { Colors = Array.ConvertAll(surface.Colors, c => AddClipped(c, ShieldTint)) };

    /// <summary>ApplyShieldTint's colour, added at full strength (DRAW_ADDITIVE, DRAW_FACTOR_1).</summary>
    public static readonly Rgb ShieldTint = new(0xff, 0x00, 0x00);

    /// <summary>UQM's additive drawing at full strength (renderpixel_additive, ADDITIVE_FACTOR_1): each channel added, kept to 255.</summary>
    public static Rgb AddClipped(Rgb dst, Rgb src) =>
        new((byte)Math.Min(255, dst.R + src.R), (byte)Math.Min(255, dst.G + src.G), (byte)Math.Min(255, dst.B + src.B));

    /// <summary>
    /// UQM's additive drawing at a factor below full (modulated_sum): each
    /// channel plus (source x factor) &gt;&gt; 8, kept to 255. The scan tint is
    /// drawn so at DRAW_FACTOR_1 / 2 (127; USE_ADDITIVE_SCAN_BLIT in pl_stuff.c).
    /// </summary>
    public static Rgb AddModulated(Rgb dst, Rgb src, int factor) => new(
        (byte)Math.Clamp(dst.R + ((src.R * factor) >> 8), 0, 255),
        (byte)Math.Clamp(dst.G + ((src.G * factor) >> 8), 0, 255),
        (byte)Math.Clamp(dst.B + ((src.B * factor) >> 8), 0, 255));

    /// <summary>The scan tint's strength (DRAW_FACTOR_1 / 2).</summary>
    public const int ScanTintFactor = 0xff / 2;

    /// <summary>A coloured map with a scan's tint added, as renderTintFrame makes the tint frame.</summary>
    public static Rgb[] Tinted(Rgb[] colors, Rgb tint) => Array.ConvertAll(colors, c => AddModulated(c, tint, ScanTintFactor));

    /// <summary>The surface as made and coloured, before any shield's tint (what RenderTopography draws).</summary>
    public PlanetSurface Generated(SolarSystem system, int planet, int? moon, PlanetInfo orbit)
    {
        if (system.Star.Special == "SOL_DEFINED" && planet == 2 && moon is null)
            return LazyInitializer.EnsureInitialized(ref _earth, Earth);
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        var type = system.Generator.Types[WorldTypes.PlanetType(world.DataIndex)];
        var heights = SurfaceMap.Generate(type, world.RandSeed);
        return new PlanetSurface(heights, Color(heights, type, orbit.SurfaceTemperature,
            Table(type.ColorMapResource), Table(type.TranslateTableResource)));
    }

    private BinaryTable Table(string id) => _tables.GetOrAdd(id, i => BinaryTable.Load(_package, i));

    /// <summary>
    /// RenderTopography: each elevation (made non-negative for gas giants;
    /// otherwise raised by the type's base elevation and kept to 0..255)
    /// picks a colour index through the translate table's xlat_tab, and the
    /// colour map gives its colour, halved to UQM's 5 bits a channel.
    /// </summary>
    public static Rgb[] Color(sbyte[] heights, PlanetType type, int surfaceTemperature,
        BinaryTable colorMap, BinaryTable translate)
    {
        int index = surfaceTemperature > HotThreshold ? 2 : surfaceTemperature > ColdThreshold ? 1 : 0;
        var cbase = colorMap[index];
        var xlat = translate[index];   // XLAT_DESC: level_tab (3 SIZEs), then xlat_tab[256]
        const int xlatTab = 6;
        if (xlat.Length < xlatTab + 256 || cbase.Length < 2)
            throw new ContentPackageException($"{type.Name}'s colour tables are too short");
        var colors = new Rgb[heights.Length];
        for (int i = 0; i < heights.Length; ++i)
        {
            int d = heights[i];
            if (type.Algorithm == MapAlgorithm.GasGiant)
                d &= 255;
            else
                d = Math.Clamp(d + type.BaseElevation, 0, 255);
            int c = (short)(xlat[xlatTab + d] - cbase[0]);
            int at = 2 + c * 3;
            if (at < 0 || at + 2 >= cbase.Length)
                throw new ContentPackageException($"{type.Name}'s translate table points past its colour map");
            colors[i] = new Rgb(Widen(cbase[at] >> 1), Widen(cbase[at + 1] >> 1), Widen(cbase[at + 2] >> 1));
        }
        return colors;
    }

    // CC5TO8 into a BYTE, as colorFromRgb15 does (a value over 31 is cut, as in UQM).
    private static byte Widen(int c) => (byte)((c << 3) | (c >> 2));

    /// <summary>
    /// Earth: its surface image is the coloured map, and its second image's
    /// palette indexes, less 128, the heights (GeneratePlanetSurface with a
    /// surface definition frame).
    /// </summary>
    private PlanetSurface Earth()
    {
        // As GeneratePlanetSurface: frame 0 the surface, frame 1 (if any) the elevations.
        var frames = Animation.LoadImages(_package, EarthSurfaceResource);
        if (frames.Count == 0)
            throw new ContentPackageException($"{EarthSurfaceResource} has no images");
        var surface = frames[0].Image;
        var elevation = frames.Count > 1 ? frames[1].Image : null;
        foreach (var image in elevation is null ? new[] { surface } : new[] { surface, elevation })
        {
            // UQM rescales other sizes (RescaleFrame); 0.8.0's are the map's size.
            if (image.Width != SurfaceMap.Width || image.Height != SurfaceMap.Height)
                throw new ContentPackageException($"{EarthSurfaceResource} isn't {SurfaceMap.Width} x {SurfaceMap.Height}");
        }
        if (elevation is { BytesPerPixel: not 1 })
            throw new ContentPackageException($"{EarthSurfaceResource}'s elevations aren't palette indexes");
        var colors = new Rgb[SurfaceMap.Width * SurfaceMap.Height];
        var heights = new sbyte[colors.Length];
        for (int y = 0; y < SurfaceMap.Height; ++y)
        {
            for (int x = 0; x < SurfaceMap.Width; ++x)
            {
                var (r, g, b, _) = surface.GetRgba(x, y);
                colors[y * SurfaceMap.Width + x] = new Rgb(r, g, b);
                // Without elevation data the planet is "flat as a pancake".
                heights[y * SurfaceMap.Width + x] = elevation is null
                    ? (sbyte)0
                    : unchecked((sbyte)(elevation.Pixels[y * SurfaceMap.Width + x] - 128));
            }
        }
        return new PlanetSurface(heights, colors);
    }
}
