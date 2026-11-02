// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// Colour maps as UQM's SetColorMap sets them, and the system and
/// hyperspace sprites drawn from the package with them.
/// </summary>
public class SpritesTests
{
    private static byte[] Entry(int start, int end, int maps, byte fill)
    {
        var e = new byte[2 + maps * 768];
        e[0] = (byte)start;
        e[1] = (byte)end;
        Array.Fill(e, fill, 2, e.Length - 2);
        return e;
    }

    [Fact]
    public void ColourMapsAreSetAsSetColorMapSetsThem()
    {
        var maps = new ColorMaps();
        Assert.True(maps.Set(Entry(3, 4, 2, 7)));
        Assert.Equal(new Rgb(7, 7, 7), maps[3]![255]);
        Assert.NotNull(maps[4]);
        Assert.Null(maps[5]);
        // An end past the last map is cut to it.
        Assert.True(maps.Set(Entry(249, 251, 1, 9)));
        Assert.Equal(new Rgb(9, 9, 9), maps[249]![0]);
        // A start past the end, or an end before the start, sets nothing.
        Assert.False(maps.Set(Entry(250, 250, 1, 1)));
        Assert.False(maps.Set(Entry(5, 4, 1, 1)));
        Assert.Null(maps[5]);
        Assert.Throws<ContentPackageException>(() => maps.Set(Entry(10, 11, 1, 1)));
    }

    [ContentFact]
    public void TheSpaceSpritesAreDrawnWithUqmsColourMaps()
    {
        var sprites = TestContent.With(p => new SpaceSprites(p));
        // UQM's sheets: 6 sizes of 16 facings of worlds, the sun's 3 star
        // sizes with 3 zooms, 22 pieces of orbit background, the flagship's
        // 16 facings, the hyperspace stars' 3 sizes of 6 colours and the
        // portal's 14 frames, and the hyperspace flagship's 16 facings with
        // more frames after them.
        Assert.Equal((96, 5, 22, 16, 32), (sprites.Worlds.Count, sprites.Sun.Count, sprites.Junk.Count, sprites.Flagship.Count, sprites.HyperStars.Count));
        Assert.Equal(34, sprites.HyperFlagship.Count);
        Assert.All(Enumerable.Range(0, 96), i => Assert.Equal(111, sprites.Worlds.Frame(i).ColorMapIndex));
        // Every frame is drawn as the content is opened, in every colour it's
        // used in: worlds in 9 colour families, the sun in 6 star colours.
        Assert.Equal(96 * 9 + 5 * 6 + 22 + 16 + 32 + 34, sprites.All.Count());

        // A world's image takes its colours from its colour family's map,
        // and its transparent index leaves the corners clear.
        var blue = sprites.Image(SystemSheet.Worlds, 5 << 4, 0);
        var red = sprites.Image(SystemSheet.Worlds, 5 << 4, 3);
        Assert.Equal(0, blue.Rgba[3]);
        Assert.Equal((blue.Width, blue.Height, blue.Rgba.Length), (red.Width, red.Height, red.Rgba.Length));
        Assert.NotEqual(blue.Rgba, red.Rgba);
        Assert.True(Sum(blue, 2) > Sum(blue, 0), "a blue world isn't blue");
        Assert.True(Sum(red, 0) > Sum(red, 2), "a red world isn't red");
        Assert.Same(blue, sprites.Image(SystemSheet.Worlds, 5 << 4, 0));

        // Every frame of every sheet can be drawn, with something visible.
        foreach (var (sheet, count, entries) in new[]
                 {
                     (SystemSheet.Worlds, 96, 9), (SystemSheet.Sun, 5, 6), (SystemSheet.Junk, 22, 1), (SystemSheet.Flagship, 16, 1),
                 })
            for (int e = 0; e < entries; ++e)
                for (int f = 0; f < count; ++f)
                {
                    var image = sprites.Image(sheet, f, entries > 1 ? e : -1);
                    Assert.True(image.Rgba.Where((_, i) => i % 4 == 3).Any(a => a != 0), $"{sheet} {f} ({e}) is empty");
                }
        for (int f = 0; f < 32; ++f)
            Assert.True(sprites.Hyper(false, f).Width > 0);
        for (int f = 0; f < 34; ++f)
            Assert.True(sprites.Hyper(true, f).Width > 0);
        // Hyperspace stars: a yellow dwarf is frame 5, a red super giant 15.
        var sol = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data")).Map.Stars.First(s => s.Special == "SOL_DEFINED");
        Assert.Equal(5, sol.HyperspaceFrame);
    }

    private static long Sum(SpriteImage image, int channel)
    {
        long sum = 0;
        for (int i = 0; i < image.Rgba.Length; i += 4)
            if (image.Rgba[i + 3] != 0)
                sum += image.Rgba[i + channel];
        return sum;
    }
}
