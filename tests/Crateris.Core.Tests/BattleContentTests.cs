// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle's content from the package: every sheet the flagship, the
/// Ilwrath and the arena use loads and draws, and the shapes a battle tests
/// for contact are UQM's (tests/golden/battle-masks.json, recorded by UQM's
/// own image loader).
/// </summary>
[Trait("Topic", "combat")]
public class BattleContentTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly PlanetTypes Planets = PlanetTypes.Load(Data);
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Data);
    private static readonly ShipDescription[] Fighters = { Ships[ShipDescriptions.Flagship], Ships[ShipDescriptions.Ilwrath] };

    private static readonly Lazy<AnimationSheets> Sheets = new(() => TestContent.With(p =>
    {
        // Decoded up front under the package's lock, as the sheets read it lazily.
        var sheets = new AnimationSheets(p);
        foreach (string id in BattleResources.All(Fighters, Planets))
            _ = sheets[id];
        return sheets;
    }));

    [ContentFact]
    public void EveryResourceTheBattleUsesLoads()
    {
        var sprites = TestContent.With(p => new BattleSprites(p, Sheets.Value, Fighters, Planets));
        var all = BattleResources.All(Fighters, Planets);
        Assert.Equal(all.OrderBy(r => r), sprites.Resources.OrderBy(r => r));
        // The package's sheets, as UQM 0.8.0's are: 16 facings of each
        // ship at each zoom, the flagship's 27 shots (blasters, cannons
        // and tracking), the hellfire's 8, both captains' 15 frames, the
        // status panel's icons, the stars' 3, explosions' 9, blasts' 8,
        // the asteroids' 21 (16 facings and the 5 of breaking up), the
        // status panel's 6 and a planet's one.
        foreach (var (resource, count) in new (string, int)[]
                 {
                     ("ship.flagship.graphics.flagship.large", 16), ("ship.flagship.graphics.flagship.medium", 16),
                     ("ship.flagship.graphics.flagship.small", 16), ("ship.flagship.graphics.blaster.large", 27),
                     ("ship.flagship.graphics.blaster.small", 27), ("ship.flagship.graphics.captain", 15),
                     ("ship.flagship.icons", 1),
                     ("ship.ilwrath.graphics.avenger.large", 16), ("ship.ilwrath.graphics.avenger.small", 16),
                     ("ship.ilwrath.graphics.fire.large", 8), ("ship.ilwrath.graphics.fire.medium", 8),
                     ("ship.ilwrath.graphics.captain", 15), ("ship.ilwrath.icons", 2),
                     (BattleResources.Stars, 3), (BattleResources.Explosion[0], 9), (BattleResources.Blast[2], 8),
                     (BattleResources.Asteroid[1], 21), (BattleResources.Status, 6),
                     ("planet.water.large", 1), ("planet.slaveshield.medium", 1),
                 })
            Assert.True(count == sprites.Count(resource), $"{resource}: {sprites.Count(resource)} frames, UQM {count}");
        // Every frame shows something but the asteroid's last at the
        // smallest zoom (it has broken up to nothing), each zoom smaller
        // than the one before.
        var empty = all.SelectMany(r => Enumerable.Range(0, sprites.Count(r))
                .Where(f => sprites.Image(r, f).Rgba.Where((_, i) => i % 4 == 3).All(a => a == 0))
                .Select(f => $"{r} {f}"))
            .ToList();
        Assert.Equal(new[] { "graphics.asteroid.small 20" }, empty);
        foreach (var views in BattleResources.Planets(Planets).Append(Fighters[0].Graphics.Ship).Append(BattleResources.Asteroid))
            Assert.True(sprites.Image(views[0]!, 0).Width > sprites.Image(views[1]!, 0).Width
                        && sprites.Image(views[1]!, 0).Width > sprites.Image(views[2]!, 0).Width, $"{views[0]}'s zooms");
        // A planet in battle is true colour, its black clear.
        var water = sprites.Image("planet.water.large", 0);
        Assert.Equal(0, water.Rgba[3]);
        Assert.Equal(255, water.Rgba[(water.Height / 2 * water.Width + water.Width / 2) * 4 + 3]);
    }

    [ContentFact]
    public void TheRaceStringsTheStatusPanelShowsLoad()
    {
        // The status panel names a ship from its race strings (shipstat.c:
        // entry 1) and its captain from one of the 16 from NAME_OFFSET, 5
        // (build.h, PickCaptainName). The flagship's names are the player's own.
        Assert.Null(Fighters[0].RaceStrings);
        var table = TestContent.With(p => StringTable.Load(p, Fighters[1].RaceStrings!));
        Assert.True(table.Count >= 5 + 16, $"{Fighters[1].RaceStrings} has {table.Count} strings");
        Assert.All(Enumerable.Range(5, 16).Append(1), i => Assert.NotEqual("", table[i]));
    }

    [ContentFact]
    public void TheShapesAreTheOnesUqmTests()
    {
        var shapes = BattleShapes.Load(Sheets.Value, Fighters, Planets);
        var want = Golden.Load("battle-masks").GetProperty("sheets");
        // The recording has the same sheets as the port tests.
        Assert.Equal(want.EnumerateObject().Select(s => s.Name).OrderBy(n => n), shapes.Sheets.Keys.OrderBy(n => n));
        Assert.Equal(BattleResources.ContactSheets(Fighters, Planets).OrderBy(n => n), shapes.Sheets.Keys.OrderBy(n => n));
        int frames = 0;
        foreach (var sheet in want.EnumerateObject())
        {
            var mine = shapes[sheet.Name];
            var uqm = sheet.Value.EnumerateArray().ToList();
            Assert.True(uqm.Count == mine.Count, $"{sheet.Name}: {mine.Count} frames, UQM {uqm.Count}");
            for (int i = 0; i < uqm.Count; ++i)
            {
                var f = mine[i];
                Assert.True((uqm[i][0].GetInt32(), uqm[i][1].GetInt32(), uqm[i][2].GetInt32(), uqm[i][3].GetInt32())
                    == (f.Width, f.Height, f.HotSpotX, f.HotSpotY), $"{sheet.Name} {i}: size or hot spot");
                Assert.True(uqm[i][4].GetString() == Golden.Sha256(f.Mask!), $"{sheet.Name} {i}: the mask differs from UQM's");
                frames++;
            }
        }
        Assert.Equal(16 + 27 + 16 + 8 + 21 + 8 + 9 + 3 + 60, frames);
        // A planet's black corners don't touch; its middle does.
        var water = shapes["planet.water.large"][0];
        Assert.Equal(0, water.Mask![0]);
        Assert.Equal(1, water.Mask[water.Height / 2 * water.Width + water.Width / 2]);
    }

    [ContentFact]
    public void ATrueColourFrameTouchesWhereItIsntItsKey()
    {
        // The conversion to the screen's format keeps the colours: a pixel
        // touches unless it's the colour key's colour (black with none).
        var png = Png.Decode(TestContent.With(p => p.ReadFile(Animation.Load(p, "planet.water.large")[0].Path)));
        Assert.Equal((3, false), (png.BytesPerPixel, png.HasAlpha));
        var keyed = ContactShapes.Frame(new AnimationFrame("x", 0, -1, 0, 0), png);
        var unkeyed = ContactShapes.Frame(new AnimationFrame("x", -1, -1, 0, 0), png);
        Assert.Equal(keyed.Mask, unkeyed.Mask);
        for (int i = 0; i < keyed.Mask!.Length; ++i)
            Assert.Equal((png.Pixels[i * 3] | png.Pixels[i * 3 + 1] | png.Pixels[i * 3 + 2]) != 0 ? 1 : 0, keyed.Mask[i]);
        Assert.Contains((byte)0, keyed.Mask);
        Assert.Contains((byte)1, keyed.Mask);
    }

    [Fact]
    public void ATrueColourFramesKeyComesFromItsPng()
    {
        // UQM's loader on a 24-bit image with a tRNS colour (images.json's
        // rgb8_key): its key is the PNG's (R | G << 8 | B << 16) for an .ani
        // transparent colour other than 0 or -1, and only that colour can't touch.
        var own = Golden.Load("images").GetProperty("own").EnumerateArray().First(c => c.GetProperty("name").GetString() == "rgb8_key");
        var result = own.GetProperty("result");
        var png = Png.Decode(Convert.FromHexString(own.GetProperty("png").GetString()!));
        long key = result[5].GetInt64();
        var pixels = Convert.FromHexString(result[7].GetString()!);
        var want = Enumerable.Range(0, pixels.Length / 3)
            .Select(i => (byte)((pixels[i * 3] | pixels[i * 3 + 1] << 8 | pixels[i * 3 + 2] << 16) != key ? 1 : 0)).ToArray();
        Assert.Equal(new byte[] { 1, 0, 1, 1, 1, 1 }, want);
        Assert.Equal(want, ContactShapes.Frame(new AnimationFrame("x", 5, -1, 0, 0), png).Mask);
        // With 0 or -1 the PNG's key is dropped: the key is black, or none, and
        // then too black can't touch (this image has no black; the water
        // planet's does: ATrueColourFrameTouchesWhereItIsntItsKey).
        Assert.Equal(new byte[] { 1, 1, 1, 1, 1, 1 }, ContactShapes.Frame(new AnimationFrame("x", 0, -1, 0, 0), png).Mask);
        Assert.Equal(new byte[] { 1, 1, 1, 1, 1, 1 }, ContactShapes.Frame(new AnimationFrame("x", -1, -1, 0, 0), png).Mask);
    }

    [ContentFact]
    public void ASheetWithoutImagesIsRefused()
    {
        var empty = new AnimationSheets(new Dictionary<string, IReadOnlyList<(AnimationFrame, Png)>> { ["graphics.stars"] = Array.Empty<(AnimationFrame, Png)>() });
        Assert.Throws<ContentPackageException>(() => BattleShapes.Load(empty, new[] { "graphics.stars" }));
        // A ship needs its 16 facings.
        var one = Sheets.Value["ship.ilwrath.graphics.avenger.large"].Take(1).ToList();
        var sheets = BattleResources.ContactSheets(Fighters[1..], Planets).ToDictionary(id => id, id => id == "ship.ilwrath.graphics.avenger.large" ? one : Sheets.Value[id]);
        Assert.Throws<ContentPackageException>(() => BattleShapes.Load(new AnimationSheets(sheets), Fighters[1..], Planets));
    }
}
