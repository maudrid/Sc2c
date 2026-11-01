// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Buffers.Binary;
using System.IO.Compression;

namespace Crateris.Core.Content;

/// <summary>
/// A PNG image as UQM's SDL_image would load it: 8 bits per channel (16-bit
/// samples cut to their high byte, as <c>png_set_strip_16</c> does), no
/// gamma or colour correction, and an alpha channel only if the PNG has one
/// (<see cref="HasAlpha"/>). Pixels are RGBA, row by row.
/// </summary>
public sealed class Png
{
    private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a };

    private Png(int width, int height, bool hasAlpha, byte[] rgba)
    {
        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
        Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Whether the PNG has an alpha channel (colour types 4 and 6).</summary>
    public bool HasAlpha { get; }

    /// <summary>The pixels as RGBA bytes; without <see cref="HasAlpha"/>, every alpha is 255.</summary>
    public byte[] Rgba { get; }

    /// <summary>Whether the data starts with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>Decodes a non-interlaced PNG; anything else is a <see cref="ContentPackageException"/>.</summary>
    public static Png Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
            throw new ContentPackageException("not a PNG image");
        int pos = 8;
        int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();
        while (pos + 12 <= data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]));
            var type = data.Slice(pos + 4, 4);
            if (pos + 12 + length > data.Length)
                throw new ContentPackageException("a PNG chunk runs past the end of the image");
            var body = data.Slice(pos + 8, length);
            pos += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                depth = body[8];
                colorType = body[9];
                interlace = body[12];
            }
            else if (type.SequenceEqual("PLTE"u8))
                palette = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8))
                idat.Write(body);
            else if (type.SequenceEqual("IEND"u8))
                break;
        }
        if (colorType < 0 || width <= 0 || height <= 0)
            throw new ContentPackageException("a PNG image without a valid header");
        if (interlace != 0)
            throw new ContentPackageException("interlaced PNG images aren't read yet");
        int channels = colorType switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new ContentPackageException($"PNG colour type {colorType} isn't valid"),
        };
        if (depth is not (1 or 2 or 4 or 8 or 16) || (depth < 8 && colorType is not (0 or 3)) || (depth == 16 && colorType == 3))
            throw new ContentPackageException($"PNG bit depth {depth} isn't valid for colour type {colorType}");
        if (colorType == 3 && palette is null)
            throw new ContentPackageException("a palette PNG image without a palette");

        int bitsPerPixel = channels * depth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int bpp = Math.Max(1, bitsPerPixel / 8);   // filter distance in bytes
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int done = 0;
            while (done < raw.Length)
            {
                int n = z.Read(raw, done, raw.Length - done);
                if (n == 0)
                    throw new ContentPackageException("a PNG image's data ends early");
                done += n;
            }
        }

        var rgba = new byte[width * height * 4];
        var prev = new byte[stride];
        var line = new byte[stride];
        for (int y = 0; y < height; ++y)
        {
            int filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, line, 0, stride);
            Unfilter(filter, line, prev, bpp);
            for (int x = 0; x < width; ++x)
            {
                int o = (y * width + x) * 4;
                switch (colorType)
                {
                    case 0:
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = Gray(line, x, depth);
                        rgba[o + 3] = 255;
                        break;
                    case 3:
                        int i = Sample(line, x, depth);
                        if (3 * i + 2 >= palette!.Length)
                            throw new ContentPackageException("a PNG image uses a colour its palette doesn't have");
                        rgba[o] = palette[3 * i];
                        rgba[o + 1] = palette[3 * i + 1];
                        rgba[o + 2] = palette[3 * i + 2];
                        rgba[o + 3] = 255;
                        break;
                    default:
                        int step = depth / 8;
                        int s = x * channels * step;
                        byte c0 = line[s];
                        if (channels <= 2)
                        {
                            rgba[o] = rgba[o + 1] = rgba[o + 2] = c0;
                            rgba[o + 3] = channels == 2 ? line[s + step] : (byte)255;
                        }
                        else
                        {
                            rgba[o] = c0;
                            rgba[o + 1] = line[s + step];
                            rgba[o + 2] = line[s + 2 * step];
                            rgba[o + 3] = channels == 4 ? line[s + 3 * step] : (byte)255;
                        }
                        break;
                }
            }
            (prev, line) = (line, prev);
        }
        return new Png(width, height, colorType is 4 or 6, rgba);
    }

    // A sample of depth bits (1, 2, 4, 8, or the high byte of 16).
    private static int Sample(byte[] line, int x, int depth)
    {
        if (depth >= 8)
            return line[x * (depth / 8)];
        int bit = x * depth;
        return (line[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
    }

    // A grey sample scaled to 8 bits, as png_set_expand_gray_1_2_4_to_8 does.
    private static byte Gray(byte[] line, int x, int depth)
    {
        int v = Sample(line, x, depth);
        return depth switch { 1 => (byte)(v * 255), 2 => (byte)(v * 85), 4 => (byte)(v * 17), _ => (byte)v };
    }

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
}
