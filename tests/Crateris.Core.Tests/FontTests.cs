// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using Crateris.Core.Content;

namespace Crateris.Core.Tests;

/// <summary>
/// Fonts and text layout against UQM's own _GetFontData, TextRect and
/// _text_blt (tests/golden/fonts.json, as hashes), and PNG decoding.
/// </summary>
public class FontTests
{
    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static string GlyphJson(FontGlyph g) =>
        $"[{g.Char},{g.Width},{g.Height},{g.HotSpotX},{g.HotSpotY},{g.DispWidth},{g.DispHeight},\"{Convert.ToHexString(g.Alpha).ToLowerInvariant()}\"]";

    private static string LayoutJson(TextLayout t)
    {
        var sb = new StringBuilder($"[[{(t.Drawn ? 1 : 0)},{t.X},{t.Y},{t.Width},{t.Height},{t.CharCount}]");
        foreach (var p in t.Glyphs)
            sb.Append($",[{p.Glyph.Char},{p.X},{p.Y}]");
        return sb.Append(']').ToString();
    }

    [ContentFact]
    public void EveryFontLoadsAsUqmLoadsIt()
    {
        var golden = Golden.Load("fonts");
        var folders = golden.GetProperty("resources").EnumerateObject()
            .GroupBy(r => r.Value.GetString()!).ToDictionary(g => g.Key, g => g.First().Name);
        int glyphs = 0;
        TestContent.With(package =>
        {
            foreach (var f in golden.GetProperty("fonts").EnumerateObject())
            {
                var font = UqmFont.Load(package, folders[f.Name]);
                Assert.True(f.Value.GetProperty("metrics").GetString() == Hash($"[{font.Leading},{font.LeadingWidth}]"),
                    $"{f.Name}: leading {font.Leading}, leading width {font.LeadingWidth}");
                var want = f.Value.GetProperty("glyphs").EnumerateObject().ToDictionary(g => int.Parse(g.Name), g => g.Value.GetString());
                Assert.Equal(want.Keys.Order(), font.Glyphs.Keys.Order());
                foreach (var g in font.Glyphs.Values)
                    Assert.True(want[g.Char] == Hash(GlyphJson(g)), $"{f.Name} character {g.Char}: {GlyphJson(g)[..Math.Min(60, GlyphJson(g).Length)]}...");
                glyphs += font.Glyphs.Count;
            }
        });
        Assert.True(glyphs > 2500, $"only {glyphs} characters");
    }

    [ContentFact]
    public void TextIsLaidOutAsUqmLaysItOut()
    {
        var golden = Golden.Load("fonts");
        var (fonts, game) = TestContent.With(package => (
            new[] { "font.starcon", "font.tiny", "font.micro" }.ToDictionary(k => k, k => UqmFont.Load(package, k)),
            GameStrings.Load(package)));
        int n = 0;
        foreach (var c in golden.GetProperty("texts").EnumerateArray())
        {
            var font = fonts[c.GetProperty("font").GetString()!];
            var align = (TextAlign)c.GetProperty("align").GetInt32();
            byte[] text = c.TryGetProperty("text", out var hex)
                ? Convert.FromHexString(hex.GetString()!)
                : game.Table.Raw(c.GetProperty("game_string").GetInt32())[..^1].ToArray();
            var layout = font.Layout(text, 0, 0, align);
            string what = c.TryGetProperty("text", out _) ? $"\"{Encoding.UTF8.GetString(text)}\"" : $"game string {c.GetProperty("game_string")}";
            Assert.True(c.GetProperty("result").GetString() == Hash(LayoutJson(layout)),
                $"{what} in {c.GetProperty("font")} aligned {align}: {LayoutJson(layout)}");
            n++;
        }
        Assert.True(n > 300, $"only {n} texts");
    }

    // A result as images.c prints it: [0], or [1, bpp, alpha, width, height, key, palette, pixels].
    private static string ImageJson(byte[] data)
    {
        Png png;
        try
        {
            png = Png.Decode(data);
        }
        catch (ContentPackageException)
        {
            return "[0]";
        }
        string hex(byte[]? b) => b is null ? "" : Convert.ToHexString(b).ToLowerInvariant();
        return $"[1,{png.BitsPerPixel},{(png.HasAlpha ? 1 : 0)},{png.Width},{png.Height},{png.ColorKey},\"{hex(png.Palette)}\",\"{hex(png.Pixels)}\"]";
    }

    [Fact]
    public void ImagesOfOurOwnLoadAsUqmLoadsThem()
    {
        foreach (var c in Golden.Load("images").GetProperty("own").EnumerateArray())
        {
            string want = System.Text.Json.JsonSerializer.Serialize(c.GetProperty("result"));
            string got = ImageJson(Convert.FromHexString(c.GetProperty("png").GetString()!));
            Assert.True(want == got, $"{c.GetProperty("name")}: {got[..Math.Min(got.Length, 120)]}, UQM {want[..Math.Min(want.Length, 120)]}");
        }
    }

    [Fact]
    public void AnImageTooLargeForLibpngIsRefusedNotACrash()
    {
        // A header claiming 2,000,000 x 2,000,000 pixels, past libpng's limit of 1,000,000.
        var png = Convert.FromHexString(Golden.Load("images").GetProperty("own")[0].GetProperty("png").GetString()!);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 2_000_000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 2_000_000);
        uint crc = 0xffffffff;
        foreach (byte b in png.AsSpan(12, 17))
        {
            crc ^= b;
            for (int k = 0; k < 8; ++k)
                crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        crc ^= 0xffffffff;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), crc);
        var e = Assert.Throws<ContentPackageException>(() => Png.Decode(png));
        Assert.Contains("outside what libpng reads", e.Message);
    }

    [ContentFact]
    public void EveryKindOfImageInThePackageLoadsAsUqmLoadsIt()
    {
        int n = 0;
        TestContent.With(package =>
        {
            foreach (var kind in Golden.Load("images").GetProperty("content").EnumerateObject())
            {
                foreach (var c in kind.Value.EnumerateArray())
                {
                    string path = c.GetProperty("path").GetString()!;
                    Assert.True(c.GetProperty("result").GetString() == Hash(ImageJson(package.ReadFile(path))), $"{kind.Name}: {path}");
                    n++;
                }
            }
        });
        Assert.True(n > 200, $"only {n} images");
    }

    [ContentFact]
    public void AFewWordsMeasureAsInTheGame()
    {
        var font = TestContent.With(p => UqmFont.Load(p, "font.starcon"));
        Assert.Equal(11, font.Leading);
        var scan = font.Layout("Scan", 0, 0);
        Assert.Equal(4, scan.Glyphs.Count);
        Assert.Equal(font.Width("Scan"), scan.Width);
        Assert.Equal(-(scan.Width >> 1), font.Layout("Scan", 0, 0, TextAlign.Center).X);
        Assert.Equal(UqmFont.MaxDeltas, font.Layout(new string('x', 150), 0, 0).Glyphs.Count);
    }
}
