// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The mineral selling's count-down (docs/plans/stage-3.md, item 11)
/// against UQM's own SellMinerals (tests/golden/selling.json): every
/// scenario, run on the client's clock a frame at a time with the keys as
/// they come, draws what UQM's drew, call for call and each at the same
/// time (the long ones by their first and last calls, how many and a hash
/// of them all, as the recording keeps them), shows the resource units
/// UQM's status area showed before it, and puts the cargo display away
/// when UQM's did. The recording holds no content, so these run without it.
/// </summary>
[Trait("Topic", "drawing")]
public class MineralSellingTests
{
    private static readonly JsonElement Root = Golden.Load("selling");
    private static readonly JsonElement Recorded = Root.GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    private static JsonElement Scenario(string name) =>
        Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    // The recording's frames: graphics.miscdata's count and its first element icon's size.
    private static UqmCanvas Canvas() => new(_ => Root.GetProperty("frames").GetInt32(), (_, i) =>
    {
        var icon = Root.GetProperty("icon").Ints();
        return i == 11 ? new URect(0, 0, icon[0], icon[1]) : throw new InvalidOperationException($"frame {i}'s size asked for");
    }, new Dictionary<string, int>());

    private static string GameString(int n) => $"{{gs:{n}}}";

    private static MineralSale SaleOf(JsonElement sc) => new(sc.GetProperty("amounts").Ints(), Root.GetProperty("worth").Ints(),
        sc.GetProperty("bio").GetInt32(), sc.GetProperty("bays").GetInt32() * 500, sc.GetProperty("res_units").GetUInt32(),
        sc.GetProperty("bomb_state").GetInt32());

    // A call of the port's as golden/sell.c writes UQM's.
    private static JsonArray Call(UqmDraw d)
    {
        var call = d.Kind switch
        {
            UqmDrawKind.Stamp => new JsonArray("s", d.Sheet, d.Frame, d.X, d.Y),
            UqmDrawKind.Rect => new JsonArray("r", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Clear => new JsonArray("c", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Text when d.Effect < 0 => new JsonArray("t", d.Font, d.X, d.Y, (int)d.Align, d.Count, d.Text, d.Color.R,
                d.Color.G, d.Color.B),
            _ => new JsonArray($"unexpected {d.Kind}"),
        };
        foreach (int v in new[] { d.Clip.X, d.Clip.Y, d.Clip.W, d.Clip.H })
            call.Add(v);
        return call;
    }

    private static string Got(long at, UqmDraw d) => new JsonArray(at, Call(d)).ToJsonString();

    private static string Want(JsonElement timed) => JsonNode.Parse(timed.GetRawText())!.ToJsonString();

    private sealed record Result(List<string> Calls, long Done, uint ResourceUnits, List<string> Backdrop);

    /// <summary>
    /// The port's selling from <paramref name="start"/> (milliseconds), run
    /// a frame (a sixtieth of a second) at a time or all at once, the keys
    /// told as they come (shifted by start), until it's put away.
    /// </summary>
    private static Result Replay(JsonElement sc, long start, bool frames)
    {
        var selling = new MineralSelling(Canvas(), SaleOf(sc), GameString, start);
        var keys = sc.GetProperty("keys").EnumerateArray().Select(e => e.GetString()!).ToList();
        foreach (var k in keys.Where(k => k == "h"))
            selling.Held();
        var timed = keys.Where(k => k != "h").Select(k => (Down: k[0] == 'd', At: long.Parse(k[2..]) + start)).ToList();
        var calls = new List<string>();
        int next = 0;
        for (long now = start; ; now += frames ? 16 : 100_000)
        {
            for (; next < timed.Count && timed[next].At <= now; ++next)
            {
                if (timed[next].Down)
                    selling.KeyDown(timed[next].At);
                else
                    selling.KeyUp(timed[next].At);
            }
            calls.AddRange(selling.Run(now).Select(c => Got(c.At - start, c.Call)));
            if (selling.Done || now > start + 60_000)
                break;
        }
        return new Result(calls, selling.Done ? selling.DoneAt - start : -1, selling.ResourceUnits,
            selling.Backdrop.Select(d => Got(0, d)).ToList());
    }

    private static void Same(JsonElement sc, Result got, string where)
    {
        Assert.Equal(sc.GetProperty("before").EnumerateArray().Select(Want), got.Backdrop);
        List<string> want;
        int offset = 0;
        if (sc.TryGetProperty("calls", out var whole))
            want = whole.EnumerateArray().Select(Want).ToList();
        else
        {
            // The first and the last, how many, and a hash of them all.
            Assert.Equal(sc.GetProperty("count").GetInt32(), got.Calls.Count);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", got.Calls)))).ToLowerInvariant();
            var head = sc.GetProperty("head").EnumerateArray().Select(Want).ToList();
            var tail = sc.GetProperty("tail").EnumerateArray().Select(Want).ToList();
            Compare(head, got.Calls.Take(head.Count).ToList(), 0);
            Compare(tail, got.Calls.Skip(got.Calls.Count - tail.Count).ToList(), got.Calls.Count - tail.Count);
            Assert.True(sc.GetProperty("sha256").GetString() == hash, $"{where}: the calls' hash differs");
            want = null!;
        }
        if (want is not null)
            Compare(want, got.Calls, offset);
        Assert.Equal(sc.GetProperty("put_away").GetInt64(), got.Done);
        Assert.Equal(sc.GetProperty("res_units_after").GetUInt32(), got.ResourceUnits);

        void Compare(List<string> w, List<string> g, int from)
        {
            for (int i = 0; i < Math.Max(w.Count, g.Count); ++i)
                Assert.True(i < w.Count && i < g.Count && w[i] == g[i],
                    $"{where}, call {from + i}:\n UQM  {(i < w.Count ? w[i] : "(none)")}\n port {(i < g.Count ? g[i] : "(none)")}");
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TheHoldIsCountedDownCallForCallAsUqmsAtTheSameTimes(string name) =>
        Same(Scenario(name), Replay(Scenario(name), 0, frames: true), $"{name}, a frame at a time");

    [Theory]
    [InlineData("several/none")]
    [InlineData("several/between-categories")]
    [InlineData("all-eight/mashing")]
    [InlineData("big/mid-category")]
    public void RunAllAtOnceOrLaterOnTheClockItDrawsTheSame(string name)
    {
        var sc = Scenario(name);
        Same(sc, Replay(sc, 0, frames: false), $"{name}, all at once");
        // Whole seconds later: UQM's ticks are made from the milliseconds a second at a time.
        Same(sc, Replay(sc, 123_000, frames: true), $"{name}, started later");
    }

    [Fact]
    public void TheRecordingHasEveryKindOfCase()
    {
        var names = Scenarios().Select(o => (string)o[0]).ToList();
        foreach (var kind in new[] { "/none", "/at-first-wait", "/held-let-go", "/held", "/mid-category", "/last-category",
                     "/between-categories", "/tap-in-a-wait", "/at-the-end", "/mashing" })
            Assert.Contains(names, n => n.EndsWith(kind));
        var holds = Recorded.EnumerateArray().Select(s => s.GetProperty("amounts").Ints()).ToList();
        Assert.Contains(holds, h => h.Count(a => a > 0) == 1);                  // one category
        Assert.Contains(holds, h => h.All(a => a > 0));                          // all eight
        Assert.Contains(holds, h => h.Count(a => a > 0) > 1 && h.Any(a => a == 0)
            && Array.FindIndex(h, a => a == 0) < Array.FindLastIndex(h, a => a > 0));   // empty categories between
        Assert.Contains(holds, h => h.Sum() > 2000);                             // a big load
        Assert.Contains(Recorded.EnumerateArray(), s => s.GetProperty("bomb_state").GetInt32() >= 2);
        Assert.Contains(Recorded.EnumerateArray(), s => s.TryGetProperty("sha256", out _));
        // A key that sold the rest at once, and one that came too late to.
        Assert.Contains(Recorded.EnumerateArray(), s => s.GetProperty("put_away").GetInt64()
            < Scenario(s.GetProperty("name").GetString()!.Split('/')[0] + "/none").GetProperty("put_away").GetInt64());
        // The game's strings are the cargo display's and the status message's.
        Assert.Contains($"{{gs:{GameStrings.CargoBase}}}", Recorded.GetRawText());
        Assert.Contains($"{{gs:{GameStrings.CargoBase + 1}}}", Recorded.GetRawText());
        Assert.Contains($"{{gs:{GameStrings.StatusBase + 1}}}", Recorded.GetRawText());
        Assert.Contains($"{{gs:{GameStrings.StatusBase + 2}}}", Recorded.GetRawText());
    }

    [Fact]
    public void AKeyHeldAsItStartsCountsOnlyWhileHeld()
    {
        var sale = new MineralSale(new[] { 4, 0, 0, 0, 0, 0, 0, 0 }, new[] { 1, 2, 3, 4, 5, 6, 8, 25 }, 0, 500, 0, 0);
        // Let go before the first look: counted a unit at a time.
        var slow = new MineralSelling(Canvas(), sale, GameString, 0);
        slow.Held(5);
        slow.KeyUp(200, 5);
        slow.Run(100_000);
        // Still held at the first look: all at once.
        var quick = new MineralSelling(Canvas(), sale, GameString, 0);
        quick.Held(5);
        quick.Run(100_000);
        Assert.True(quick.DoneAt < slow.DoneAt);
        Assert.Equal(4u, slow.ResourceUnits);
        Assert.Equal(4u, quick.ResourceUnits);
        // A key never seen going down coming up changes nothing.
        var other = new MineralSelling(Canvas(), sale, GameString, 0);
        other.KeyUp(100, 9);
        other.Run(100_000);
        Assert.Equal(slow.DoneAt, other.DoneAt);
    }

    [Fact]
    public void TheWindowLosingTheKeyboardLetsGoOfTheKeysHeld()
    {
        var sale = new MineralSale(new[] { 40, 0, 0, 0, 0, 0, 0, 0 }, new[] { 1, 2, 3, 4, 5, 6, 8, 25 }, 0, 500, 0, 0);
        var plain = new MineralSelling(Canvas(), sale, GameString, 0);
        plain.Run(100_000);
        var selling = new MineralSelling(Canvas(), sale, GameString, 0);
        selling.Held(5);
        selling.LetGo(100);
        selling.Run(100_000);
        Assert.Equal(plain.DoneAt, selling.DoneAt);
    }

    // ------------------------------------------------------------ the picture

    [ContentFact]
    public void TheCountDownIsPaintedInTheStatusAreasPartForIt()
    {
        var raster = TestContent.With(p =>
        {
            var sheets = new AnimationSheets(p);
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(p, "colortable.main")[0]);
            var icons = SpriteSheet.Load(sheets, MineralSelling.IconSheet);
            var strings = GameStrings.Load(p);
            var fonts = new[] { "starcon", "tiny" }.ToDictionary(f => f, f => UqmFont.Load(p, "font." + f));
            var canvas = new UqmCanvas(_ => icons.Count, (_, i) =>
            {
                var f = icons.Render(i, maps);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, fonts.ToDictionary(f => f.Key, f => f.Value.Leading));
            var sc = Scenario("all-eight/none");
            var selling = new MineralSelling(canvas, SaleOf(sc), i => strings[i], 0);
            var raster = new ScreenRaster((_, i) => icons.Render(i, maps), f => fonts[f]);
            raster.Paint(selling.Backdrop);
            raster.Paint(selling.Run(1500).Select(c => c.Call));
            Assert.False(selling.Done);
            return raster;
        });
        var area = MineralSelling.Area;
        var colours = new HashSet<(byte, byte, byte)>();
        for (int y = 0; y < ScreenRaster.Height; ++y)
            for (int x = 0; x < ScreenRaster.Width; ++x)
            {
                int o = (y * ScreenRaster.Width + x) * 4;
                var c = (raster.Rgba[o], raster.Rgba[o + 1], raster.Rgba[o + 2]);
                bool inside = x >= area.X && x < area.X + area.W && y >= area.Y && y < area.Y + area.H;
                if (!inside)
                    Assert.True(c == (0, 0, 0), $"painted outside the area at ({x}, {y})");
                else
                    colours.Add(c);
            }
        Assert.True(colours.Count > 6, $"only {colours.Count} colours in the area");
    }
}
