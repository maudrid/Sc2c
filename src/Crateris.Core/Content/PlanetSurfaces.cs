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

using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Core.Content;

/// <summary>A world's surface: its height map and its coloured map (the scan screen's map), <see cref="SurfaceMap.Width"/> x <see cref="SurfaceMap.Height"/>, row by row.</summary>
public sealed record PlanetSurface(sbyte[] Heights, Rgb[] Colors);

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
    /// The surface of a world that can be orbited, as UQM makes it on
    /// entering its orbit. A shielded world's coloured map has the shield's
    /// red added (ApplyShieldTint), as UQM shows it.
    /// </summary>
    public PlanetSurface For(SolarSystem system, int planet, int? moon)
    {
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        var surface = Generated(system, planet, moon);
        return WorldTypes.IsShielded(world.DataIndex)
            ? surface with { Colors = Array.ConvertAll(surface.Colors, c => AddClipped(c, ShieldTint)) }
            : surface;
    }

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
    public PlanetSurface Generated(SolarSystem system, int planet, int? moon)
    {
        if (system.Star.Special == "SOL_DEFINED" && planet == 2 && moon is null)
            return LazyInitializer.EnsureInitialized(ref _earth, Earth);
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        var orbit = system.EnterOrbit(planet, moon);
        if (orbit.Report is not PlanetInfo info)
            throw new ArgumentException($"planet {planet} moon {moon} of star {system.Star.Number} can't be orbited");
        var type = system.Generator.Types[WorldTypes.PlanetType(world.DataIndex)];
        var heights = SurfaceMap.Generate(type, world.RandSeed);
        return new PlanetSurface(heights, Color(heights, type, info.SurfaceTemperature,
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
        var frames = Animation.Load(_package, EarthSurfaceResource);
        if (frames.Count < 2)
            throw new ContentPackageException($"{EarthSurfaceResource} needs two frames");
        var surface = Png.Decode(_package.ReadFile(frames[0].Path));
        var elevation = Png.Decode(_package.ReadFile(frames[1].Path));
        foreach (var image in new[] { surface, elevation })
        {
            // UQM rescales other sizes (RescaleFrame); 0.8.0's are the map's size.
            if (image.Width != SurfaceMap.Width || image.Height != SurfaceMap.Height)
                throw new ContentPackageException($"{EarthSurfaceResource} isn't {SurfaceMap.Width} x {SurfaceMap.Height}");
        }
        if (elevation.BytesPerPixel != 1)
            throw new ContentPackageException($"{EarthSurfaceResource}'s elevations aren't palette indexes");
        var colors = new Rgb[SurfaceMap.Width * SurfaceMap.Height];
        var heights = new sbyte[colors.Length];
        for (int y = 0; y < SurfaceMap.Height; ++y)
        {
            for (int x = 0; x < SurfaceMap.Width; ++x)
            {
                var (r, g, b, _) = surface.GetRgba(x, y);
                colors[y * SurfaceMap.Width + x] = new Rgb(r, g, b);
                heights[y * SurfaceMap.Width + x] = unchecked((sbyte)(elevation.Pixels[y * SurfaceMap.Width + x] - 128));
            }
        }
        return new PlanetSurface(heights, colors);
    }
}

/// <summary>One frame of a UQM animation file (.ani): its image file and hot spot.</summary>
public sealed record AnimationFrame(string Path, int TransparentIndex, int ColorMapIndex, int HotSpotX, int HotSpotY);

/// <summary>
/// A GFXRES resource's animation file (.ani), as UQM's _GetCelData reads
/// its lines (sscanf "%s %d %d %d %d"): an image file name, the
/// transparent colour index, the colour map index and the hot spot. File
/// names are relative to the .ani file's folder. UQM drops a frame whose
/// image doesn't load, so later frames move up; whoever loads the images
/// does the same. Every line of 0.8.0's files has all five fields; a line
/// without them is refused rather than read with UQM's leftover values.
/// </summary>
public static class Animation
{
    public static IReadOnlyList<AnimationFrame> Load(ContentPackage package, string resourceId)
    {
        var entry = package.Resource(resourceId);
        if (entry.Type != "GFXRES")
            throw new ContentPackageException($"{resourceId} is {entry.Type}, not graphics");
        string folder = entry.Value.Contains('/') ? entry.Value[..(entry.Value.LastIndexOf('/') + 1)] : "";
        var frames = new List<AnimationFrame>();
        foreach (var line in System.Text.Encoding.UTF8.GetString(package.ReadFile(entry.Value)).Split('\n'))
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0)
                continue;
            if (f.Length < 5 || !f[1..5].All(v => int.TryParse(v, out _)))
                throw new ContentPackageException($"{entry.Value} has a line UQM wouldn't read fully: \"{line.Trim()}\"");
            frames.Add(new AnimationFrame(folder + f[0], int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4])));
        }
        return frames;
    }
}
