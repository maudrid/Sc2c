// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The sprites of the system views against tests/golden/system-sprites.json,
/// what UQM's own DrawSystem and CreateStarBackGround stamp for every star:
/// each sprite's sheet, frame, place and colour table entry, in UQM's order.
/// </summary>
public class SystemSpritesTests
{
    private static readonly SystemCatalog Catalog = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Show(IEnumerable<SystemStamp> s) =>
        string.Join(", ", s.Select(t => $"[{(int)t.Sheet}, {t.Frame}, {t.X}, {t.Y}, {t.ColorEntry}]"));

    private static string Show(System.Text.Json.JsonElement stamps) =>
        string.Join(", ", stamps.EnumerateArray().Select(t => "[" + string.Join(", ", t.Ints()) + "]"));

    [Fact]
    public void EverySystemViewIsStampedAsUqmStampsIt()
    {
        var failures = new StringBuilder();
        int views = 0, stamps = 0;
        var systems = Golden.Load("system-sprites").GetProperty("systems").EnumerateArray().ToList();
        Assert.Equal(Catalog.Map.Stars.Count, systems.Count);
        foreach (var sys in systems)
        {
            int star = sys.GetProperty("star").GetInt32();
            SolarSystem system = Catalog[star];
            int zoom = IpGeometry.MaxZoomRadius;
            foreach (var want in sys.GetProperty("outer").EnumerateArray())
            {
                string got = Show(SystemSprites.Outer(system, zoom)), uqm = Show(want);
                if (got != uqm && failures.Length < 2000)
                    failures.AppendLine($"star {star} zoom {zoom}: {got}\n  UQM {uqm}");
                stamps += want.GetArrayLength();
                views++;
                zoom >>= 1;
            }
            Assert.Equal(IpGeometry.MinZoomRadius >> 1, zoom);
            int planet = 0;
            foreach (var want in sys.GetProperty("inner").EnumerateArray())
            {
                string got = Show(SystemSprites.Inner(system, planet)), uqm = Show(want);
                if (got != uqm && failures.Length < 2000)
                    failures.AppendLine($"star {star} planet {planet} inner: {got}\n  UQM {uqm}");
                stamps += want.GetArrayLength();
                views++;
                planet++;
            }
            Assert.Equal(system.Planets.Count, planet);
        }
        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(views > 4000 && stamps > 10000, $"{views} views, {stamps} stamps");
    }

    [Fact]
    public void EveryStarBackgroundIsUqms()
    {
        var wrong = new List<int>();
        foreach (var sys in Golden.Load("system-sprites").GetProperty("systems").EnumerateArray())
        {
            int star = sys.GetProperty("star").GetInt32();
            var bytes = new List<byte>();
            foreach (var s in SystemSprites.StarBackground(Catalog.Map.Stars[star]))
                foreach (int v in new[] { s.Frame, s.X, s.Y })
                {
                    bytes.Add((byte)v);
                    bytes.Add((byte)(v >> 8));
                }
            if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant() != sys.GetProperty("background").GetString())
                wrong.Add(star);
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} backgrounds differ from UQM's, such as star {wrong.FirstOrDefault()}");
    }

    [Fact]
    public void ShieldsStarbasesAndColoursAreUqms()
    {
        // Sol: Earth is shielded in a new game, so its inner view shows the
        // shield; its moon Luna is a planet image, and the starbase is the
        // orbit background's starbase frame.
        var sol = Catalog[Catalog.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var earth = SystemSprites.Inner(sol, 2);
        Assert.Equal((SystemSheet.Junk, SystemSprites.ShieldFrame), (earth[0].Sheet, earth[0].Frame));
        Assert.Contains(earth.Skip(1), s => s.Sheet == SystemSheet.Junk && s.Frame == SystemSprites.StarbaseFrame);
        Assert.Contains(earth.Skip(1), s => s.Sheet == SystemSheet.Worlds);
        // Sol is a yellow dwarf; its sun is frame 0 zoomed out, 2 zoomed in.
        var outer = SystemSprites.Outer(sol, IpGeometry.MaxZoomRadius);
        Assert.Equal(new SystemStamp(SystemSheet.Sun, 0, 121, 113, 5), outer.Single(s => s.Sheet == SystemSheet.Sun));
        Assert.Equal(2, SystemSprites.Outer(sol, IpGeometry.MinZoomRadius).Single(s => s.Sheet == SystemSheet.Sun).Frame);
        // Each stamp says which world it shows.
        Assert.Equal(new[] { -1, 0, 1 }, earth.Select(s => s.World));
        Assert.Equal(Enumerable.Range(-1, sol.Planets.Count + 1).OrderBy(i => i), outer.Select(s => s.World).OrderBy(i => i));
        Assert.Equal(SystemSprites.DrawOrder(sol), outer.Select(s => s.World));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 4, 5, 6, 7, 8 },
            new[] { "BLUE_BODY", "GREEN_BODY", "ORANGE_BODY", "RED_BODY", "WHITE_BODY", "GRAY_BODY", "YELLOW_BODY", "CYAN_BODY", "PURPLE_BODY", "VIOLET_BODY" }
                .Select(SystemSprites.ColorEntry));
    }
}
