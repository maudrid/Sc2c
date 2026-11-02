// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using Crateris.Core.Content;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>
/// Contact by sprite shape: the package's masks against UQM's loader and
/// canvas (tests/golden/sprite-masks.json), and the intersection against
/// UQM's own DrawablesIntersect on 4,000 cases (intersections.json).
/// </summary>
public class ContactTests
{
    private static readonly Lazy<ContactShapes> Shapes = new(() => TestContent.With(ContactShapes.Load));

    /// <summary>The recorder's sheet numbers (tools/extract/golden/systems.c).</summary>
    private static IReadOnlyList<IntersectFrame> Sheet(int sheet) => Shapes.Value.Sheets[sheet switch
    {
        1 => "graphics.flagship",
        2 => "graphics.planets",
        3 => "graphics.orbitbackground",
        4 => "graphics.stars",
        6 => "ship.flagship.graphics.hyperspace",
        7 => "graphics.hyperstars",
        8 => "graphics.ambient",
        _ => throw new ArgumentException($"sheet {sheet}"),
    }];

    [ContentFact]
    public void EveryMaskIsTheOneUqmTests()
    {
        int frames = 0;
        foreach (var sheet in Golden.Load("sprite-masks").GetProperty("sheets").EnumerateObject())
        {
            var mine = Shapes.Value.Sheets[sheet.Name];
            var want = sheet.Value.EnumerateArray().ToList();
            Assert.True(want.Count == mine.Count, $"{sheet.Name}: {mine.Count} frames, UQM {want.Count}");
            for (int i = 0; i < want.Count; ++i)
            {
                var f = mine[i];
                Assert.True((want[i][0].GetInt32(), want[i][1].GetInt32(), want[i][2].GetInt32(), want[i][3].GetInt32())
                    == (f.Width, f.Height, f.HotSpotX, f.HotSpotY), $"{sheet.Name} {i}: size or hot spot");
                string? hash = f.Mask is null ? null : Convert.ToHexString(SHA256.HashData(f.Mask)).ToLowerInvariant();
                Assert.True(want[i][4].ValueKind == System.Text.Json.JsonValueKind.Null ? hash is null : want[i][4].GetString() == hash,
                    $"{sheet.Name} {i}: the mask differs from UQM's");
                frames++;
            }
        }
        Assert.Equal(ContactShapes.Resources.Count, Shapes.Value.Sheets.Count);
        Assert.True(frames > 250, $"only {frames} frames");
        // The one-pixel frames UQM tests new contacts with.
        Assert.Equal((1, 1, 0, 0), (Shapes.Value.IpPoint.Width, Shapes.Value.IpPoint.Height, Shapes.Value.IpPoint.HotSpotX, Shapes.Value.IpPoint.HotSpotY));
        Assert.Equal(new byte[] { 1 }, Shapes.Value.HyperPoint.Mask);
    }

    [ContentFact]
    public void FramesTouchWhenUqmSaysTheyDo()
    {
        var cases = Golden.Load("intersections").GetProperty("cases").EnumerateArray().Select(c => c.Ints()).ToList();
        Assert.True(cases.Count >= 4000 && cases.Count(c => c[13] != 0) > 1000, "too few cases or contacts");
        foreach (var c in cases)
        {
            var c0 = new IntersectControl(Sheet(c[0])[c[1]], (short)c[2], (short)c[3]) { EndX = (short)c[4], EndY = (short)c[5] };
            var c1 = new IntersectControl(Sheet(c[6])[c[7]], (short)c[8], (short)c[9]) { EndX = (short)c[10], EndY = (short)c[11] };
            int t = Intersection.DrawablesIntersect(c0, c1, (ushort)c[12]);
            var got = new[] { t, c0.EndX, c0.EndY, c0.LastTime, c1.EndX, c1.EndY, c1.LastTime };
            Assert.True(got.SequenceEqual(c[13..]),
                $"case [{string.Join(", ", c[..13])}]: [{string.Join(", ", got)}], UQM [{string.Join(", ", c[13..])}]");
        }
    }

    [Fact]
    public void StillFramesTouchPixelAgainstPixel()
    {
        // Two 3x3 rings (solid edges, clear middle) and a single point.
        var ring = new IntersectFrame(3, 3, 1, 1, new byte[] { 1, 1, 1, 1, 0, 1, 1, 1, 1 });
        var point = new IntersectFrame(1, 1, 0, 0, new byte[] { 1 });
        Assert.Equal(1, Intersection.DrawablesIntersect(new IntersectControl(ring, 10, 10), new IntersectControl(point, 11, 10), Intersection.MaxTimeValue));
        Assert.Equal(0, Intersection.DrawablesIntersect(new IntersectControl(ring, 10, 10), new IntersectControl(point, 10, 10), Intersection.MaxTimeValue));
        Assert.Equal(0, Intersection.DrawablesIntersect(new IntersectControl(ring, 10, 10), new IntersectControl(point, 12, 10), Intersection.MaxTimeValue));
        Assert.Equal(0, Intersection.DrawablesIntersect(new IntersectControl(ring, 10, 10), new IntersectControl(point, 11, 10), 0));
        // A frame whose mask isn't known can't be tested.
        var unknown = new IntersectFrame(1, 1, 0, 0, null);
        Assert.Throws<InvalidOperationException>(() =>
            Intersection.DrawablesIntersect(new IntersectControl(unknown, 5, 5), new IntersectControl(point, 5, 5), 1));
    }
}
