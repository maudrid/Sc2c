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

using Crateris.Core.Systems;

namespace Crateris.Core.Content;

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
/// follows process_image: a palette image's transparent index from the
/// .ani file (or none for -1), a true-colour image's black for index 0;
/// otherwise the PNG's own colour key, and an alpha channel's alpha. A
/// palette image is drawn with its frame's colour map if the frame names
/// one (TFB_Prim_Stamp, TFB_DrawCanvas_Image).
/// </summary>
public sealed class SpriteSheet
{
    private readonly IReadOnlyList<(AnimationFrame Frame, Png Image)> _frames;

    private SpriteSheet(IReadOnlyList<(AnimationFrame, Png)> frames) => _frames = frames;

    public static SpriteSheet Load(ContentPackage package, string resourceId)
    {
        var frames = Animation.LoadImages(package, resourceId);
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
        var rgba = new byte[png.Width * png.Height * 4];
        if (png.BytesPerPixel == 1)
        {
            byte[] own = png.Palette!;
            Rgb[]? map = null;
            if (frame.ColorMapIndex != -1)
                map = maps[frame.ColorMapIndex]
                    ?? throw new ContentPackageException($"{frame.Path} is drawn with colour map {frame.ColorMapIndex}, which nothing has set");
            long key = frame.TransparentIndex >= 0 ? frame.TransparentIndex
                : frame.TransparentIndex == -1 ? -1 : png.ColorKey;
            for (int i = 0, o = 0; i < png.Pixels.Length; ++i, o += 4)
            {
                int p = png.Pixels[i];
                if (map != null)
                    (rgba[o], rgba[o + 1], rgba[o + 2]) = (map[p].R, map[p].G, map[p].B);
                else
                    (rgba[o], rgba[o + 1], rgba[o + 2]) = (own[p * 3], own[p * 3 + 1], own[p * 3 + 2]);
                rgba[o + 3] = p == key ? (byte)0 : (byte)255;
            }
        }
        else
        {
            long key = frame.TransparentIndex == 0 ? 0 : frame.TransparentIndex == -1 ? -1 : png.ColorKey;
            int bpp = png.BytesPerPixel;
            for (int i = 0, o = 0; i < png.Width * png.Height; ++i, o += 4)
            {
                byte r = png.Pixels[i * bpp], g = png.Pixels[i * bpp + 1], b = png.Pixels[i * bpp + 2];
                (rgba[o], rgba[o + 1], rgba[o + 2]) = (r, g, b);
                if (png.HasAlpha)
                    rgba[o + 3] = png.Pixels[i * bpp + 3];
                else
                    rgba[o + 3] = (r | (g << 8) | (b << 16)) == key ? (byte)0 : (byte)255;
            }
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
    private readonly ColorMaps _base = new();
    private readonly BinaryTable _worldColors, _sunColors;
    private readonly Dictionary<(SystemSheet, int, int), SpriteImage> _system = new();
    private readonly Dictionary<(bool, int), SpriteImage> _hyper = new();

    public SpaceSprites(ContentPackage package)
    {
        _base.Set(BinaryTable.Load(package, "colortable.main")[0]);
        _base.Set(BinaryTable.Load(package, "colortable.hyperspace")[0]);
        _worldColors = BinaryTable.Load(package, "colortable.orbplan");
        _sunColors = BinaryTable.Load(package, "colortable.truespace");
        Worlds = SpriteSheet.Load(package, "graphics.planets");
        Sun = SpriteSheet.Load(package, "graphics.truespacesun");
        Junk = SpriteSheet.Load(package, "graphics.orbitbackground");
        Flagship = SpriteSheet.Load(package, "graphics.flagship");
        HyperStars = SpriteSheet.Load(package, "graphics.hyperstars");
        HyperFlagship = SpriteSheet.Load(package, "ship.flagship.graphics.hyperspace");
    }

    public SpriteSheet Worlds { get; }
    public SpriteSheet Sun { get; }
    public SpriteSheet Junk { get; }
    public SpriteSheet Flagship { get; }
    public SpriteSheet HyperStars { get; }
    public SpriteSheet HyperFlagship { get; }

    /// <summary>A system view's stamp drawn (kept once made).</summary>
    public SpriteImage Image(SystemStamp s) => Image(s.Sheet, s.Frame, s.ColorEntry);

    public SpriteImage Image(SystemSheet sheet, int frame, int colorEntry = -1)
    {
        lock (_system)
        {
            if (_system.TryGetValue((sheet, frame, colorEntry), out var image))
                return image;
            var maps = _base;
            if (sheet is SystemSheet.Worlds or SystemSheet.Sun)
            {
                maps = _base.Clone();
                maps.Set((sheet == SystemSheet.Worlds ? _worldColors : _sunColors)[colorEntry]);
            }
            image = (sheet switch
            {
                SystemSheet.Worlds => Worlds,
                SystemSheet.Sun => Sun,
                SystemSheet.Junk => Junk,
                _ => Flagship,
            }).Render(frame, maps);
            return _system[(sheet, frame, colorEntry)] = image;
        }
    }

    /// <summary>
    /// A star in hyperspace (AddStars: STAR_TYPE * NUM_STAR_COLORS +
    /// STAR_COLOR), or with <paramref name="flagship"/> the flagship by facing.
    /// </summary>
    public SpriteImage Hyper(bool flagship, int frame)
    {
        lock (_hyper)
        {
            if (!_hyper.TryGetValue((flagship, frame), out var image))
                _hyper[(flagship, frame)] = image = (flagship ? HyperFlagship : HyperStars).Render(frame, _base);
            return image;
        }
    }

    /// <summary>NUM_STAR_COLORS.</summary>
    public const int StarColors = 6;

    public static int StarFrame(Crateris.Core.Data.Star star) => (int)star.Size * StarColors + (int)star.Color;
}
