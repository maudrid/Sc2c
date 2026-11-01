// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/libs/graphics/gfxload.c
// _GetFontData, processFontChar; font.c TextRect, _text_blt, getCharFrame;
// boxint.c BoxIntersect; libs/strings/unicode.c getCharFromString;
// libs/graphics/sdl/canvas.c TFB_DrawCanvas_GetFontCharData),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;

namespace Crateris.Core.Content;

/// <summary>Text alignment (TEXT_ALIGN in UQM): about the baseline's x.</summary>
public enum TextAlign { Left = 0, Center = 1, Right = 2 }

/// <summary>
/// One character of a UQM font (TFB_Char): its image as alpha values, row
/// by row, and where it sits: <see cref="HotSpotY"/> above the baseline,
/// <see cref="DispWidth"/> to the next character.
/// </summary>
public sealed record FontGlyph(int Char, int Width, int Height, int HotSpotX, int HotSpotY,
    int DispWidth, int DispHeight, byte[] Alpha);

/// <summary>A character as _text_blt draws it: the pen position on the baseline (the image goes at minus the hot spot).</summary>
public readonly record struct PlacedGlyph(FontGlyph Glyph, int X, int Y)
{
    /// <summary>The top left of the character's image.</summary>
    public int Left => X - Glyph.HotSpotX;
    public int Top => Y - Glyph.HotSpotY;
}

/// <summary>
/// Text laid out as UQM's font_DrawText does: TextRect's result (false for
/// nothing to draw), its rectangle and character count, and the characters
/// _text_blt draws.
/// </summary>
public sealed record TextLayout(bool Drawn, int X, int Y, int Width, int Height, int CharCount, IReadOnlyList<PlacedGlyph> Glyphs);

/// <summary>
/// A UQM font (a FONTRES resource: a folder of PNG images named by their
/// character number in hex, such as <c>base/fonts/starcon.fon/00041.png</c>),
/// read as <c>_GetFontData</c> reads it, and text laid out as UQM lays it out.
/// </summary>
public sealed class UqmFont
{
    /// <summary>MAX_DELTAS: font_DrawText lays out at most this many characters.</summary>
    public const int MaxDeltas = 100;

    private readonly Dictionary<int, FontGlyph> _glyphs;

    private UqmFont(Dictionary<int, FontGlyph> glyphs, int leading, int leadingWidth)
    {
        _glyphs = glyphs;
        Leading = leading;
        LeadingWidth = leadingWidth;
    }

    /// <summary>The line height: the tallest character's DispHeight, plus one.</summary>
    public int Leading { get; }

    /// <summary>The widest character's DispWidth.</summary>
    public int LeadingWidth { get; }

    /// <summary>The characters, by character number.</summary>
    public IReadOnlyDictionary<int, FontGlyph> Glyphs => _glyphs;

    /// <summary>Reads a FONTRES resource from the content package.</summary>
    public static UqmFont Load(ContentPackage package, string resourceId)
    {
        var entry = package.Resource(resourceId);
        if (entry.Type != "FONTRES")
            throw new ContentPackageException($"{resourceId} is {entry.Type}, not a font");
        string folder = entry.Value.TrimEnd('/') + "/";
        var files = package.Files.Where(f => f.StartsWith(folder, StringComparison.Ordinal)
                && f.Length > folder.Length && f.IndexOf('/', folder.Length) < 0)
            .Select(f => (Path: f, Name: f[folder.Length..]));
        var font = FromFiles(files.Select(f => (f.Name, (Func<byte[]>)(() => package.ReadFile(f.Path)))));
        if (font._glyphs.Count == 0)
            throw new ContentPackageException($"{resourceId} ({entry.Value}) has no characters");
        return font;
    }

    /// <summary>
    /// Builds a font from its files, as _GetFontData does: names with a '.'
    /// in them, read as a character number in hex up to the '.' (above
    /// 0xFFFF, or not a number, is skipped); images that don't load or are
    /// empty are skipped. Only PNG images are read (UQM's SDL_image also
    /// reads XPM; the only two in 0.8.0 are in the credits font, beside PNG
    /// images of the same characters). Where two files give one character,
    /// the first by name is kept.
    /// </summary>
    public static UqmFont FromFiles(IEnumerable<(string Name, Func<byte[]> Read)> files)
    {
        var chars = new SortedDictionary<int, FontGlyph>();
        foreach (var (name, read) in files.Where(f => f.Name.Contains('.')).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (CharNumber(name) is not uint index || index > 0xffff || chars.ContainsKey((int)index))
                continue;
            byte[] data = read();
            if (!Png.IsPng(data))
                continue;
            Png image;
            try
            {
                image = Png.Decode(data);
            }
            catch (ContentPackageException)
            {
                continue;   // UQM skips a character whose image doesn't load
            }
            if (image.Width == 0 || image.Height == 0)
                continue;
            chars[(int)index] = Glyph((int)index, image);
        }
        int leading = 0, leadingWidth = 0;
        foreach (var g in chars.Values)
        {
            leading = Math.Max(leading, g.DispHeight);
            leadingWidth = Math.Max(leadingWidth, g.DispWidth);
        }
        return new UqmFont(new Dictionary<int, FontGlyph>(chars), leading + 1, leadingWidth);
    }

    // sscanf (name, "%x.", &index): optional spaces and sign, an optional 0x, hex digits, then '.'.
    private static uint? CharNumber(string name)
    {
        int i = 0;
        while (i < name.Length && char.IsWhiteSpace(name[i]))
            i++;
        bool negative = false;
        if (i < name.Length && name[i] is '+' or '-')
            negative = name[i++] == '-';
        if (i + 2 < name.Length && name[i] == '0' && name[i + 1] is 'x' or 'X' && Uri.IsHexDigit(name[i + 2]))
            i += 2;
        int start = i;
        ulong value = 0;
        while (i < name.Length && Uri.IsHexDigit(name[i]))
        {
            value = Math.Min(value * 16 + (ulong)Convert.ToInt32(name[i].ToString(), 16), uint.MaxValue);
            i++;
        }
        if (i == start || i >= name.Length || name[i] != '.')
            return null;
        uint v = (uint)value;
        return negative ? unchecked(0u - v) : v;
    }

    // processFontChar, with the alpha as TFB_DrawCanvas_GetFontCharData makes it.
    private static FontGlyph Glyph(int index, Png image)
    {
        var alpha = new byte[image.Width * image.Height];
        for (int p = 0; p < alpha.Length; ++p)
        {
            int o = p * 4;
            alpha[p] = image.HasAlpha
                ? image.Rgba[o + 3]
                : (byte)((image.Rgba[o] * 2 + image.Rgba[o + 1] * 4 + image.Rgba[o + 2]) / 7);
        }
        int tune = image.Height == 8 ? -1 : image.Height == 9 ? -2 : image.Height > 9 ? -3 : 0;
        return new FontGlyph(index, image.Width, image.Height, 0, image.Height + tune,
            image.Width + 1, image.Height + 1, alpha);
    }

    /// <summary>getCharFrame: the character, or null if the font hasn't got it.</summary>
    public FontGlyph? Glyph(int ch) => _glyphs.GetValueOrDefault(ch);

    /// <summary>Lays out text at a baseline as font_DrawText does (TextRect, then _text_blt).</summary>
    public TextLayout Layout(string text, int baselineX, int baselineY, TextAlign align = TextAlign.Left) =>
        Layout(Encoding.UTF8.GetBytes(text), baselineX, baselineY, align);

    /// <summary>Lays out UTF-8 bytes (up to a NUL) as font_DrawText does.</summary>
    public TextLayout Layout(ReadOnlySpan<byte> utf8, int baselineX, int baselineY, TextAlign align = TextAlign.Left)
    {
        // TextRect, with font_DrawText's CharCount of ~0 cut to MAX_DELTAS.
        int charCount = MaxDeltas, numChars = MaxDeltas;
        int topY = 0, botY = 0, width = 0, lastDelta = -1;
        int pos = 0;
        int next = NextChar(utf8, ref pos);
        if (next == 0)
            numChars = 0;
        while (numChars-- > 0)
        {
            int lastWidth = width;
            int ch = next;
            if (numChars > 0)
            {
                next = NextChar(utf8, ref pos);
                if (next == 0)
                {
                    charCount -= numChars;
                    numChars = 0;
                }
            }
            if (Glyph(ch) is { DispWidth: > 0 } g)
            {
                int y = -g.HotSpotY;
                if (y < topY)
                    topY = y;
                y += g.DispHeight;
                if (y > botY)
                    botY = y;
                width += g.DispWidth;
            }
            lastDelta = (byte)(width - lastWidth);
        }
        int height = botY - topY;
        if (width <= 0 || height <= 0)
            return new TextLayout(false, baselineX, baselineY, 0, 0, charCount, Array.Empty<PlacedGlyph>());
        if (lastDelta > 0)
            --width;   // subtract off default character spacing
        int x = align switch { TextAlign.Left => 0, TextAlign.Center => -(width >> 1), _ => -width } + baselineX;
        int top = topY + baselineY;

        // _text_blt: each character whose box meets the rectangle.
        var placed = new List<PlacedGlyph>();
        int originX = x;
        pos = 0;
        numChars = charCount;
        next = NextChar(utf8, ref pos);
        if (next == 0)
            numChars = 0;
        while (numChars-- > 0)
        {
            int ch = next;
            if (numChars > 0)
            {
                next = NextChar(utf8, ref pos);
                if (next == 0)
                    numChars = 0;
            }
            if (Glyph(ch) is { DispWidth: > 0 } g)
            {
                if (Overlap(originX - g.HotSpotX, baselineY - g.HotSpotY, g.DispWidth, g.DispHeight, x, top, width, height))
                    placed.Add(new PlacedGlyph(g, originX, baselineY));
                originX += g.DispWidth;
            }
        }
        return new TextLayout(true, x, top, width, height, charCount, placed);
    }

    /// <summary>The width TextRect gives the text (0 for nothing drawn).</summary>
    public int Width(string text) => Layout(text, 0, 0).Width;

    // BoxIntersect's test: whether the two boxes share any pixel.
    private static bool Overlap(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2)
    {
        int dx = x1 - x2, w = w1, delta = w2 - dx;
        if (delta <= w)
            w = delta;
        if (dx < 0)
            w += dx;
        if (w <= 0)
            return false;
        int dy = y1 - y2, h = h1;
        delta = h2 - dy;
        if (delta <= h)
            h = delta;
        if (dy < 0)
            h += dy;
        return h > 0;
    }

    // getCharFromString: one character of UTF-8, or 0 at the end or for a bad
    // sequence (which then ends the text, since 0 is the end to its callers).
    private static int NextChar(ReadOnlySpan<byte> s, ref int pos)
    {
        byte b = At(s, pos);
        if (b < 0x80)
        {
            if (pos < s.Length)
                pos++;
            return b;
        }
        int need, result, min;
        if ((b & 0xe0) == 0xc0) { need = 1; result = b & 0x1f; min = 0x80; }
        else if ((b & 0xf0) == 0xe0) { need = 2; result = b & 0x0f; min = 0x800; }
        else if ((b & 0xf8) == 0xf0) { need = 3; result = b & 0x07; min = 0x10000; }
        else
        {
            Resync(s, ref pos);
            return 0;
        }
        pos++;
        for (int k = 0; k < need; ++k)
        {
            if ((At(s, pos) & 0xc0) != 0x80)
            {
                Resync(s, ref pos);
                return 0;
            }
            result = (result << 6) | (At(s, pos) & 0x3f);
            pos++;
        }
        if (result < min)
        {
            Resync(s, ref pos);
            return 0;
        }
        return result;
    }

    // The byte at i, or the terminating NUL past the end.
    private static byte At(ReadOnlySpan<byte> s, int i) => i < s.Length ? s[i] : (byte)0;

    private static void Resync(ReadOnlySpan<byte> s, ref int pos)
    {
        while (pos < s.Length && (s[pos] & 0xc0) == 0x80)
            pos++;
    }
}
