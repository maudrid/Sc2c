// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/libs/graphics/sdl/png2sdl.c
// TFB_png_to_sdl), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the libpng transformations it asks for.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Buffers.Binary;
using System.IO.Compression;

namespace Crateris.Core.Content;

/// <summary>
/// A PNG image as UQM's own loader (<c>TFB_png_to_sdl</c>) leaves it in an
/// SDL surface:
/// <list type="bullet">
/// <item>16-bit samples cut to their high byte; interlaced images
/// de-interlaced; 1, 2 and 4-bit samples widened to a byte each.</item>
/// <item>Grey images: 8 bits per pixel with a grey palette.</item>
/// <item>Palette images: 8 bits per pixel, the palette, and a colour key if
/// the transparency chunk makes exactly one entry fully transparent and
/// the rest opaque. With any other transparency, the image is expanded to
/// 32-bit RGBA instead.</item>
/// <item>RGB images: 24 bits per pixel, with a colour key from the
/// transparency chunk (its low bytes, as UQM's loader truncates them).</item>
/// <item>Grey with alpha, and RGBA: 32-bit RGBA.</item>
/// </list>
/// A grey image with a transparency chunk would give UQM a 16-bit surface
/// without colour masks; no UQM image has one, and it isn't read. Anything
/// libpng would refuse (a bad CRC, data cut short, an invalid header) is a
/// <see cref="ContentPackageException"/>, as UQM's loader returns nothing.
/// </summary>
public sealed class Png
{
    /// <summary>libpng's default limit on width and height (PNG_USER_WIDTH_MAX, PNG_USER_HEIGHT_MAX).</summary>
    public const int MaxDimension = 1_000_000;

    private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a };
    private static readonly uint[] CrcTable = MakeCrcTable();

    private Png(int width, int height, int bitsPerPixel, bool hasAlpha, long colorKey, byte[]? palette, byte[] pixels)
    {
        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        HasAlpha = hasAlpha;
        ColorKey = colorKey;
        Palette = palette;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>8 (palette or grey), 24 (RGB) or 32 (RGBA).</summary>
    public int BitsPerPixel { get; }

    public int BytesPerPixel => BitsPerPixel / 8;

    /// <summary>Whether the surface has an alpha channel (SDL's Amask): the 32-bit ones.</summary>
    public bool HasAlpha { get; }

    /// <summary>
    /// The colour key, or -1: for 8 bits a palette index, for 24 bits the
    /// pixel value R | G &lt;&lt; 8 | B &lt;&lt; 16 (SDL_MapRGB).
    /// </summary>
    public long ColorKey { get; }

    /// <summary>For 8-bit surfaces, 256 RGB entries (768 bytes; entries the PNG doesn't set are 0); otherwise null.</summary>
    public byte[]? Palette { get; }

    /// <summary>The pixels, row by row, <see cref="BytesPerPixel"/> each (R, G, B, A order), without SDL's row padding.</summary>
    public byte[] Pixels { get; }

    /// <summary>SDL_GetRGBA of a pixel: through the palette for 8 bits; alpha 255 without an alpha channel.</summary>
    public (byte R, byte G, byte B, byte A) GetRgba(int x, int y)
    {
        int o = (y * Width + x) * BytesPerPixel;
        if (BytesPerPixel == 1)
        {
            int i = Pixels[o] * 3;
            return (Palette![i], Palette[i + 1], Palette[i + 2], 255);
        }
        return (Pixels[o], Pixels[o + 1], Pixels[o + 2], HasAlpha ? Pixels[o + 3] : (byte)255);
    }

    /// <summary>Whether the data starts with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>Reads a PNG as UQM's loader does; what it can't load is a <see cref="ContentPackageException"/>.</summary>
    public static Png Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
            throw new ContentPackageException("not a PNG image");
        int pos = 8;
        int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
        bool haveHeader = false;
        byte[]? plte = null, trns = null;
        using var idat = new MemoryStream();
        // UQM's loader stops once it has the image data (it skips
        // png_read_end), so nothing after the last IDAT chunk is read.
        while (true)
        {
            if (pos + 8 <= data.Length && idat.Length > 0 && !data.Slice(pos + 4, 4).SequenceEqual("IDAT"u8))
                break;
            if (pos + 12 > data.Length)
                throw new ContentPackageException("a PNG image ends early");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length)
                throw new ContentPackageException("a PNG chunk runs past the end of the image");
            var typeAndBody = data.Slice(pos + 4, 4 + (int)length);
            var type = typeAndBody[..4];
            var body = typeAndBody[4..];
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 8 + (int)length)..]);
            pos += 12 + (int)length;
            bool critical = (type[0] & 0x20) == 0;
            if (Crc(typeAndBody) != crc)
            {
                if (critical)
                    throw new ContentPackageException($"a PNG {System.Text.Encoding.ASCII.GetString(type)} chunk is damaged");
                continue;   // libpng skips a damaged ancillary chunk
            }
            if (type.SequenceEqual("IHDR"u8))
            {
                if (body.Length != 13)
                    throw new ContentPackageException("a PNG image's header is the wrong size");
                width = ReadDimension(body);
                height = ReadDimension(body[4..]);
                depth = body[8];
                colorType = body[9];
                if (body[10] != 0 || body[11] != 0 || body[12] > 1)
                    throw new ContentPackageException("a PNG image uses an unknown compression, filter or interlace method");
                interlace = body[12];
                haveHeader = true;
            }
            else if (!haveHeader)
                throw new ContentPackageException("a PNG image doesn't start with its header");
            else if (type.SequenceEqual("PLTE"u8))
                plte = body.ToArray();
            else if (type.SequenceEqual("tRNS"u8))
                trns = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8))
                idat.Write(body);
            else if (type.SequenceEqual("IEND"u8))
                throw new ContentPackageException("a PNG image has no image data");
        }
        int channels = colorType switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new ContentPackageException($"PNG colour type {colorType} isn't valid"),
        };
        if (depth is not (1 or 2 or 4 or 8 or 16) || (depth < 8 && colorType is not (0 or 3)) || (depth == 16 && colorType == 3))
            throw new ContentPackageException($"PNG bit depth {depth} isn't valid for colour type {colorType}");
        if (colorType == 3 && (plte is null || plte.Length % 3 != 0 || plte.Length == 0 || plte.Length > 768))
            throw new ContentPackageException("a palette PNG image without a valid palette");
        if (colorType == 0 && trns is not null)
            throw new ContentPackageException("grey PNG images with a transparent colour aren't read (UQM's loader can't use them)");

        // The rows of each pass (one pass unless interlaced), unfiltered, as samples.
        byte[] inflated = Inflate(idat);
        var samples = new int[(long)width * height * channels <= int.MaxValue
            ? width * height * channels
            : throw new ContentPackageException("a PNG image is too large")];
        int used = 0;
        var passes = interlace == 1
            ? new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) }
            : new[] { (0, 0, 1, 1) };
        foreach (var (x0, y0, dx, dy) in passes)
        {
            int pw = width > x0 ? (width - x0 + dx - 1) / dx : 0;
            int ph = height > y0 ? (height - y0 + dy - 1) / dy : 0;
            if (pw == 0 || ph == 0)
                continue;
            int stride = (int)(((long)pw * channels * depth + 7) / 8);
            int bpp = Math.Max(1, channels * depth / 8);
            var prev = new byte[stride];
            var line = new byte[stride];
            for (int r = 0; r < ph; ++r)
            {
                if (used + 1 + stride > inflated.Length)
                    throw new ContentPackageException("a PNG image's data ends early");
                int filter = inflated[used];
                Array.Copy(inflated, used + 1, line, 0, stride);
                used += 1 + stride;
                Unfilter(filter, line, prev, bpp);
                int y = y0 + r * dy;
                for (int c = 0; c < pw; ++c)
                {
                    int x = x0 + c * dx;
                    for (int k = 0; k < channels; ++k)
                        samples[(y * width + x) * channels + k] = Sample(line, c * channels + k, depth);
                }
                (prev, line) = (line, prev);
            }
        }

        // The surface TFB_png_to_sdl makes of it.
        int n = width * height;
        switch (colorType)
        {
            case 0:
            {
                // png_set_expand: grey widened to 8 bits; a grey palette.
                var pixels = new byte[n];
                for (int i = 0; i < n; ++i)
                    pixels[i] = Widen(samples[i], depth);
                var palette = new byte[768];
                for (int i = 0; i < 256; ++i)
                    palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)i;
                return new Png(width, height, 8, false, -1, palette, pixels);
            }
            case 3:
            {
                long key = -1;
                bool expand = false;
                if (trns is not null && trns.Length <= plte!.Length / 3)
                {
                    // Exactly one fully transparent entry and the rest opaque: a colour key.
                    int t = -1, j;
                    for (j = 0; j < trns.Length; j++)
                    {
                        if (trns[j] == 0)
                        {
                            if (t >= 0)
                                break;
                            t = j;
                        }
                        else if (trns[j] != 255)
                            break;
                    }
                    if (j == trns.Length)
                        key = t;
                    else
                        expand = true;
                }
                if (expand)
                {
                    var rgba = new byte[n * 4];
                    for (int i = 0; i < n; ++i)
                    {
                        int index = samples[i];
                        if (index * 3 + 2 < plte!.Length)
                        {
                            rgba[i * 4] = plte[index * 3];
                            rgba[i * 4 + 1] = plte[index * 3 + 1];
                            rgba[i * 4 + 2] = plte[index * 3 + 2];
                        }
                        rgba[i * 4 + 3] = index < trns!.Length ? trns[index] : (byte)255;
                    }
                    return new Png(width, height, 32, true, -1, null, rgba);
                }
                var pixels = new byte[n];
                for (int i = 0; i < n; ++i)
                    pixels[i] = (byte)samples[i];
                var palette = new byte[768];
                Array.Copy(plte!, palette, plte!.Length);
                return new Png(width, height, 8, false, key, palette, pixels);
            }
            case 2:
            {
                var pixels = new byte[n * 3];
                for (int i = 0; i < n * 3; ++i)
                    pixels[i] = High(samples[i], depth);
                long key = -1;
                if (trns is not null && trns.Length == 6)
                    key = trns[1] | (trns[3] << 8) | (trns[5] << 16);   // (Uint8) of each 16-bit value
                return new Png(width, height, 24, false, key, null, pixels);
            }
            default:
            {
                // RGBA, and grey with alpha (png_set_gray_to_rgb).
                var pixels = new byte[n * 4];
                for (int i = 0; i < n; ++i)
                {
                    if (colorType == 4)
                    {
                        pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = High(samples[i * 2], depth);
                        pixels[i * 4 + 3] = High(samples[i * 2 + 1], depth);
                    }
                    else
                    {
                        for (int k = 0; k < 4; ++k)
                            pixels[i * 4 + k] = High(samples[i * 4 + k], depth);
                    }
                }
                return new Png(width, height, 32, true, -1, null, pixels);
            }
        }
    }

    private static int ReadDimension(ReadOnlySpan<byte> b)
    {
        uint v = BinaryPrimitives.ReadUInt32BigEndian(b);
        if (v == 0 || v > MaxDimension)
            throw new ContentPackageException($"a PNG image's size ({v}) is outside what libpng reads");
        return (int)v;
    }

    private static byte[] Inflate(MemoryStream idat)
    {
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var output = new MemoryStream();
        try
        {
            z.CopyTo(output);
        }
        catch (InvalidDataException e)
        {
            throw new ContentPackageException("a PNG image's data is damaged", e);
        }
        return output.ToArray();
    }

    // The k-th sample of a row of depth bits each.
    private static int Sample(byte[] line, int k, int depth)
    {
        switch (depth)
        {
            case 16:
                return (line[k * 2] << 8) | line[k * 2 + 1];
            case 8:
                return line[k];
            default:
                int bit = k * depth;
                return (line[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
        }
    }

    // png_set_strip_16: the high byte.
    private static byte High(int sample, int depth) => depth == 16 ? (byte)(sample >> 8) : (byte)sample;

    // png_set_expand for grey: scaled to 8 bits; 16 bits cut to the high byte.
    private static byte Widen(int sample, int depth) => depth switch
    {
        1 => (byte)(sample * 255),
        2 => (byte)(sample * 85),
        4 => (byte)(sample * 17),
        16 => (byte)(sample >> 8),
        _ => (byte)sample,
    };

    private static void Unfilter(int filter, byte[] line, byte[] prev, int bpp)
    {
        for (int i = 0; i < line.Length; ++i)
        {
            int a = i >= bpp ? line[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            line[i] = filter switch
            {
                0 => line[i],
                1 => (byte)(line[i] + a),
                2 => (byte)(line[i] + b),
                3 => (byte)(line[i] + ((a + b) >> 1)),
                4 => (byte)(line[i] + Paeth(a, b, c)),
                _ => throw new ContentPackageException($"PNG filter type {filter} isn't valid"),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint c = 0xffffffff;
        foreach (byte b in data)
            c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffff;
    }

    private static uint[] MakeCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; ++n)
        {
            uint c = n;
            for (int k = 0; k < 8; ++k)
                c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
