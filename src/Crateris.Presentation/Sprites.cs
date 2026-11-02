// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (libs/graphics/cmap.c: SetColorMap;
// gfxload.c: process_image; tfb_prim.c: TFB_Prim_Stamp; sdl/canvas.c:
// TFB_DrawCanvas_Image's palette; setup.c and hyper.c's colour tables),
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

/// <summary>
/// UQM's colour maps (cmap.c): 250 numbered palettes of 256 colours, set
/// from colour table entries by SetColorMap. A palette image whose frame
/// names a colour map is drawn with that map's colours in place of its own.
/// </summary>
public sealed class ColorMaps
{
    /// <summary>MAX_COLORMAPS.</summary>
    public const int Count = 250;

    private readonly Rgb[]?[] _maps = new Rgb[Count][];

    /// <summary>A colour map, or null if nothing has set it.</summary>
    public Rgb[]? this[int index] => index >= 0 && index < Count ? _maps[index] : null;

    /// <summary>
    /// SetColorMap: an entry is its first and last map numbers, then 256
    /// RGB colours for each map. A last map past the end is cut to the
    /// end; an entry that starts past the end, or ends before it starts,
    /// sets nothing (UQM logs it and returns FALSE).
    /// </summary>
    public bool Set(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 2)
            throw new ContentPackageException("a colour table entry is too short");
        int start = entry[0], end = entry[1];
        if (start > end || start >= Count)
            return false;
        end = Math.Min(end, Count - 1);
        if (entry.Length < 2 + (end - start + 1) * 768)
            throw new ContentPackageException("a colour table entry ends before its colours do");
        for (int m = start, at = 2; m <= end; ++m)
        {
            var map = new Rgb[256];
            for (int i = 0; i < 256; ++i, at += 3)
                map[i] = new Rgb(entry[at], entry[at + 1], entry[at + 2]);
            _maps[m] = map;
        }
        return true;
    }

    public ColorMaps Clone()
    {
        var c = new ColorMaps();
        Array.Copy(_maps, c._maps, Count);
        return c;
    }
}

/// <summary>A frame ready to draw: RGBA pixels (alpha 0 or 255, or the image's own) and the hot spot.</summary>
public sealed record SpriteImage(int Width, int Height, int HotSpotX, int HotSpotY, byte[] Rgba);

/// <summary>
/// The frames of a GFXRES resource, drawn as UQM draws them. Transparency
/// is the colour key process_image sets (<see cref="ProcessImage.ColorKey"/>,
/// the same rule the contact shapes start from), or an alpha channel's
/// alpha. A palette image is drawn with its frame's colour map if the
/// frame names one (TFB_Prim_Stamp, TFB_DrawCanvas_Image).
/// </summary>
public sealed class SpriteSheet
{
    private readonly IReadOnlyList<(AnimationFrame Frame, Png Image)> _frames;

    private SpriteSheet(IReadOnlyList<(AnimationFrame, Png)> frames) => _frames = frames;

    /// <summary>A sheet from the package's decoded sheets (shared with the contact shapes).</summary>
    public static SpriteSheet Load(AnimationSheets sheets, string resourceId)
    {
        var frames = sheets[resourceId];
        if (frames.Count == 0)
            throw new ContentPackageException($"{resourceId} has no images");
        return new SpriteSheet(frames);
    }

    public int Count => _frames.Count;

    /// <summary>SetAbsFrameIndex: the index wraps at the number of frames.</summary>
    public AnimationFrame Frame(int index) => _frames[index % _frames.Count].Frame;

    /// <summary>A frame drawn with UQM's colour maps as they stand.</summary>
    public SpriteImage Render(int index, ColorMaps maps)
    {
        var (frame, png) = _frames[index % _frames.Count];
        bool palette = png.BytesPerPixel == 1;
        Rgb[]? map = null;
        if (palette && frame.ColorMapIndex != -1)
            map = maps[frame.ColorMapIndex]
                ?? throw new ContentPackageException($"{frame.Path} is drawn with colour map {frame.ColorMapIndex}, which nothing has set");
        long key = ProcessImage.ColorKey(frame, png);
        var rgba = new byte[png.Width * png.Height * 4];
        for (int y = 0, o = 0; y < png.Height; ++y)
            for (int x = 0; x < png.Width; ++x, o += 4)
            {
                var (r, g, b, a) = png.GetRgba(x, y);
                long pixel = r | (g << 8) | (b << 16);
                if (palette)
                {
                    pixel = png.Pixels[y * png.Width + x];
                    if (map != null)
                        (r, g, b) = (map[pixel].R, map[pixel].G, map[pixel].B);
                }
                (rgba[o], rgba[o + 1], rgba[o + 2]) = (r, g, b);
                rgba[o + 3] = png.HasAlpha ? a : pixel == key ? (byte)0 : (byte)255;
            }
        return new SpriteImage(png.Width, png.Height, frame.HotSpotX, frame.HotSpotY, rgba);
    }
}

/// <summary>
/// The sprites of the system views and hyperspace, with UQM's colour maps:
/// the main table set at start (setup.c), then hyperspace's (hyper.c); each
/// world is drawn with its colour family's entry of the planets' table and
/// the sun with its star colour's entry of the true-space table (DrawSystem).
/// </summary>
public sealed class SpaceSprites
{
    /// <summary>The entries of colortable.orbplan (colour families) and colortable.truespace (star colours).</summary>
    public const int WorldColors = 9, SunColors = Star.StarColors;

    private readonly Dictionary<(SystemSheet, int, int), SpriteImage> _system = new();
    private readonly Dictionary<(bool, int), SpriteImage> _hyper = new();

    /// <summary>
    /// Reads the sheets and colour tables and draws every frame in every
    /// colour it's used in, so anything wrong with them is found here, as
    /// the content is opened, not while drawing. Any failure is a
    /// <see cref="ContentPackageException"/>.
    /// </summary>
    public SpaceSprites(ContentPackage package) : this(package, new AnimationSheets(package))
    {
    }

    /// <summary>The sprites, from sheets decoded once for everything that uses them.</summary>
    public SpaceSprites(ContentPackage package, AnimationSheets sheets)
    {
        try
        {
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            maps.Set(BinaryTable.Load(package, "colortable.hyperspace")[0]);
            var worldColors = BinaryTable.Load(package, "colortable.orbplan");
            var sunColors = BinaryTable.Load(package, "colortable.truespace");
            Worlds = SpriteSheet.Load(sheets, "graphics.planets");
            Sun = SpriteSheet.Load(sheets, "graphics.truespacesun");
            Junk = SpriteSheet.Load(sheets, "graphics.orbitbackground");
            Flagship = SpriteSheet.Load(sheets, "graphics.flagship");
            HyperStars = SpriteSheet.Load(sheets, "graphics.hyperstars");
            HyperFlagship = SpriteSheet.Load(sheets, "ship.flagship.graphics.hyperspace");

            // DrawSystem sets a world's or the sun's colour table entry before stamping it.
            void Sheet(SystemSheet sheet, SpriteSheet frames, BinaryTable? table, int entries)
            {
                for (int e = 0; e < entries; ++e)
                {
                    var m = maps;
                    if (table != null)
                    {
                        m = maps.Clone();
                        m.Set(table[e]);
                    }
                    for (int f = 0; f < frames.Count; ++f)
                        _system[(sheet, f, table != null ? e : -1)] = frames.Render(f, m);
                }
            }
            Sheet(SystemSheet.Worlds, Worlds, worldColors, WorldColors);
            Sheet(SystemSheet.Sun, Sun, sunColors, SunColors);
            Sheet(SystemSheet.Junk, Junk, null, 1);
            Sheet(SystemSheet.Flagship, Flagship, null, 1);
            for (int f = 0; f < HyperStars.Count; ++f)
                _hyper[(false, f)] = HyperStars.Render(f, maps);
            for (int f = 0; f < HyperFlagship.Count; ++f)
                _hyper[(true, f)] = HyperFlagship.Render(f, maps);
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the system and hyperspace sprites couldn't be read: {e.Message}", e);
        }
    }

    public SpriteSheet Worlds { get; }
    public SpriteSheet Sun { get; }
    public SpriteSheet Junk { get; }
    public SpriteSheet Flagship { get; }
    public SpriteSheet HyperStars { get; }
    public SpriteSheet HyperFlagship { get; }

    /// <summary>Every drawn frame of the system views and hyperspace (to make textures of them up front).</summary>
    public IEnumerable<SpriteImage> All => _system.Values.Concat(_hyper.Values);

    /// <summary>A system view's stamp drawn.</summary>
    public SpriteImage Image(SystemStamp s) => Image(s.Sheet, s.Frame, s.ColorEntry);

    /// <summary>
    /// A frame of a system sheet; worlds and the sun in a colour table
    /// entry, the others with -1. The frame wraps at the sheet's size
    /// (SetAbsFrameIndex).
    /// </summary>
    public SpriteImage Image(SystemSheet sheet, int frame, int colorEntry = -1)
    {
        int count = sheet switch
        {
            SystemSheet.Worlds => Worlds.Count,
            SystemSheet.Sun => Sun.Count,
            SystemSheet.Junk => Junk.Count,
            _ => Flagship.Count,
        };
        return _system[(sheet, frame % count, sheet is SystemSheet.Worlds or SystemSheet.Sun ? colorEntry : -1)];
    }

    /// <summary>
    /// A star in hyperspace by its frame (<see cref="Star.HyperspaceFrame"/>),
    /// or with <paramref name="flagship"/> the flagship by frame (its facing).
    /// </summary>
    public SpriteImage Hyper(bool flagship, int frame) =>
        _hyper[(flagship, frame % (flagship ? HyperFlagship.Count : HyperStars.Count))];
}
