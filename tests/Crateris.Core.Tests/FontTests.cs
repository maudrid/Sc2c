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

    [Fact]
    public void PngsOfEveryKindDecode()
    {
        // 2x1 images made for the test: grey, RGB, palette, grey+alpha, RGBA, and 1-bit grey.
        var cases = new (string Hex, bool Alpha, byte[] Rgba)[]
        {
            (MakePng(2, 1, 8, 0, new byte[] { 0, 10, 200 }), false, new byte[] { 10, 10, 10, 255, 200, 200, 200, 255 }),
            (MakePng(2, 1, 8, 2, new byte[] { 0, 1, 2, 3, 4, 5, 6 }), false, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }),
            (MakePng(2, 1, 8, 3, new byte[] { 0, 1, 0 }, new byte[] { 9, 8, 7, 6, 5, 4 }), false, new byte[] { 6, 5, 4, 255, 9, 8, 7, 255 }),
            (MakePng(2, 1, 8, 4, new byte[] { 0, 7, 8, 9, 10 }), true, new byte[] { 7, 7, 7, 8, 9, 9, 9, 10 }),
            (MakePng(2, 1, 8, 6, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }), true, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            (MakePng(2, 1, 1, 0, new byte[] { 0, 0b1000_0000 }), false, new byte[] { 255, 255, 255, 255, 0, 0, 0, 255 }),
            // Sub filter: the second pixel adds the first.
            (MakePng(2, 1, 8, 0, new byte[] { 1, 10, 5 }), false, new byte[] { 10, 10, 10, 255, 15, 15, 15, 255 }),
        };
        foreach (var (hex, alpha, rgba) in cases)
        {
            var png = Png.Decode(Convert.FromHexString(hex));
            Assert.Equal(2, png.Width);
            Assert.Equal(1, png.Height);
            Assert.Equal(alpha, png.HasAlpha);
            Assert.Equal(rgba, png.Rgba);
        }
        Assert.Throws<ContentPackageException>(() => Png.Decode(new byte[] { 1, 2, 3 }));
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

    // A PNG with one IDAT of the given filtered rows.
    private static string MakePng(int w, int h, int depth, int colorType, byte[] rows, byte[]? palette = null)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        void Chunk(string type, byte[] body)
        {
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
            ms.Write(len);
            var tb = Encoding.ASCII.GetBytes(type);
            ms.Write(tb);
            ms.Write(body);
            ms.Write(new byte[4]);   // the CRC, which the decoder doesn't check
        }
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)colorType;
        Chunk("IHDR", ihdr);
        if (palette != null)
            Chunk("PLTE", palette);
        using var z = new MemoryStream();
        using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
            zs.Write(rows);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", Array.Empty<byte>());
        return Convert.ToHexString(ms.ToArray());
    }
}
