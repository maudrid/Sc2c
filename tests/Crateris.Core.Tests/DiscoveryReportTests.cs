// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Content;
using Crateris.Core.Story;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The lander report (docs/plans/stage-3.md, item 11) against UQM's own
/// report.c (tests/golden/report.json): every scenario's report, run on
/// the client's clock a frame at a time with the keys as they come, draws
/// what UQM's drew, call for call and each at the same time, and puts the
/// screen back and gives the keys back when UQM's did. The reports and the
/// end-of-page string are content, so the scenarios need it (the
/// recording names them, it doesn't hold them).
/// </summary>
[Trait("Topic", "drawing")]
public class DiscoveryReportTests
{
    private static readonly JsonElement Root = Golden.Load("report");
    private static readonly JsonElement Recorded = Root.GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    private static JsonElement Scenario(string name) =>
        Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static UqmCanvas Canvas() => new(_ => Root.GetProperty("frames").GetInt32(), (_, i) =>
    {
        var r = Root.GetProperty("rects").GetProperty(i.ToString()).Ints();
        return new URect(r[0], r[1], r[2], r[3]);
    }, new Dictionary<string, int>());

    private sealed record Texts(string Report, string EndPage);

    // The scenario's report and the end-of-page string, from the content.
    private static Texts TextsOf(JsonElement sc) => TestContent.With(p =>
    {
        var text = sc.GetProperty("text");
        string report = text.TryGetProperty("literal", out var literal) ? literal.GetString()!
            : StringTable.Load(p, text.GetProperty("table").GetString()!)[text.GetProperty("index").GetInt32()];
        return new Texts(report, GameStrings.Load(p)[Root.GetProperty("end_string").GetInt32()]);
    });

    // A recorded call with its text put in: "{report:N}" and "{end:N}" are the text from byte N of its UTF-8.
    private static string Want(JsonElement timed, Texts texts)
    {
        var node = JsonNode.Parse(timed.GetRawText())!.AsArray();
        var call = node[1]!.AsArray();
        if (call[0]!.GetValue<string>() == "t")
        {
            string where = call[6]!.GetValue<string>();
            int colon = where.IndexOf(':');
            string source = where[1..colon] == "report" ? texts.Report : texts.EndPage + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            call[6] = Encoding.UTF8.GetString(bytes, int.Parse(where[(colon + 1)..^1]), bytes.Length - int.Parse(where[(colon + 1)..^1]));
        }
        return node.ToJsonString();
    }

    // A call of the port's as golden/report.c writes UQM's.
    private static string Got(long at, UqmDraw d)
    {
        var call = d.Kind switch
        {
            UqmDrawKind.Stamp => new JsonArray("s", d.Sheet, d.Frame, d.X, d.Y),
            UqmDrawKind.FilledStamp => new JsonArray("f", d.Sheet, d.Frame, d.X, d.Y, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Clear => new JsonArray("c", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Text => new JsonArray("t", d.Font, d.X, d.Y, (int)d.Align, d.Count, d.Text, d.Color.R, d.Color.G, d.Color.B,
                d.EffectSheet, d.Effect),
            _ => new JsonArray($"unexpected {d.Kind}"),
        };
        foreach (int v in new[] { d.Clip.X, d.Clip.Y, d.Clip.W, d.Clip.H })
            call.Add(v);
        return new JsonArray(at, call).ToJsonString();
    }

    private sealed record Key(long At, bool Down);

    private static (List<Key> Keys, long? Stop) KeysOf(JsonElement sc)
    {
        var keys = new List<Key>();
        long? stop = null;
        foreach (string k in sc.GetProperty("keys").EnumerateArray().Select(e => e.GetString()!))
        {
            long at = long.Parse(k[(k.IndexOf(':') + 1)..]);
            if (k.StartsWith("stop:"))
                stop = at;
            else
                keys.Add(new Key(at, k[0] == 'd'));
        }
        return (keys, stop);
    }

    private sealed record Result(List<string> Calls, long Restored, long Done);

    /// <summary>
    /// The port's report from <paramref name="start"/>, run a frame (a
    /// sixtieth of a second) at a time, or all at once, the keys told as
    /// they come (shifted by start), until it's done or the recording stopped.
    /// </summary>
    private static Result Replay(JsonElement sc, Texts texts, long start, bool frames)
    {
        var (keys, stop) = KeysOf(sc);
        var report = new DiscoveryReport(Canvas(), texts.Report, texts.EndPage, start);
        var calls = new List<string>();
        long end = start + (stop ?? 10_000_000);
        int next = 0;
        for (long now = start; ; now = frames ? Math.Min(now + 14, end) : end)
        {
            for (; next < keys.Count && keys[next].At + start <= now; ++next)
            {
                if (keys[next].Down)
                    report.KeyDown(keys[next].At + start);
                else
                    report.KeyUp(keys[next].At + start);
            }
            calls.AddRange(report.Run(now).Select(c => Got(c.At - start, c.Call)));
            if (report.Done || now >= end)
                break;
        }
        return new Result(calls, report.Restored ? report.RestoredAt - start : -1, report.Done ? report.DoneAt - start : -1);
    }

    private static void Same(JsonElement sc, Texts texts, Result got, string where)
    {
        var want = sc.GetProperty("calls").EnumerateArray().Select(c => Want(c, texts)).ToList();
        for (int i = 0; i < Math.Max(want.Count, got.Calls.Count); ++i)
            Assert.True(i < want.Count && i < got.Calls.Count && want[i] == got.Calls[i],
                $"{where}, call {i}:\n UQM  {(i < want.Count ? want[i] : "(none)")}\n port {(i < got.Calls.Count ? got.Calls[i] : "(none)")}");
        Assert.Equal(sc.GetProperty("restored").GetInt64(), got.Restored);
        Assert.Equal(sc.GetProperty("released").GetInt64(), got.Done);
    }

    [ContentTheory]
    [MemberData(nameof(Scenarios))]
    public void TheReportIsDrawnCallForCallAsUqmsAtTheSameTimes(string name)
    {
        var sc = Scenario(name);
        var texts = TextsOf(sc);
        Same(sc, texts, Replay(sc, texts, 0, frames: true), $"{name}, a frame at a time");
    }

    [ContentTheory]
    [InlineData("text.moonbase:0/mid-typing")]
    [InlineData("text.moonbase:0/quick-taps")]
    [InlineData("many-lines/held-across-page-end")]
    public void RunAllAtOnceOrLaterOnTheClockItDrawsTheSame(string name)
    {
        var sc = Scenario(name);
        var texts = TextsOf(sc);
        Same(sc, texts, Replay(sc, texts, 0, frames: false), $"{name}, all at once");
        Same(sc, texts, Replay(sc, texts, 123_457, frames: true), $"{name}, started later");
    }

    [Fact]
    public void TheRecordingHasEveryKindOfCaseAndNoContentText()
    {
        var names = Scenarios().Select(o => (string)o[0]).ToList();
        foreach (var kind in new[] { "/pages", "/mid-typing", "/quick-taps", "/held-across-page-end", "/held-from-start",
                     "/at-end-string", "/mashing", "/none" })
            Assert.Contains(names, n => n.EndsWith(kind));
        Assert.Contains($"{StoryWorlds.MoonbaseReport}:0/pages", names);
        // Several pages, the end-of-page string, a report closed before its end.
        Assert.Contains(Recorded.EnumerateArray(), s => s.GetProperty("calls").EnumerateArray().Count(c => c[1][0].GetString() == "c") >= 3);
        Assert.Contains(Recorded.EnumerateArray(), s => s.GetProperty("calls").EnumerateArray()
            .Any(c => c[1][0].GetString() == "t" && c[1][6].GetString()!.StartsWith("{end:")));
        foreach (var s in Recorded.EnumerateArray())
            foreach (var c in s.GetProperty("calls").EnumerateArray().Where(c => c[1][0].GetString() == "t"))
                Assert.Matches(@"^\{(report|end):\d+\}$", c[1][6].GetString()!);
    }

    [Fact]
    public void ItWaitsAtThePagesEndForAKeyAndThenTheKeysAreThePlayersAgain()
    {
        var report = new DiscoveryReport(Canvas(), "Short.", "END", 0);
        Assert.NotEmpty(report.Run(4000));   // cleared and typed
        Assert.True(report.Waiting);
        Assert.False(report.Restored);
        report.KeyDown(5000);
        Assert.Empty(report.Run(5100));
        Assert.True(report.Restored);
        Assert.False(report.Done);           // the key still down
        report.KeyUp(5200);
        report.Run(5300);
        Assert.True(report.Done);
        report.KeyDown(7000);
        Assert.Empty(report.Run(9000));
    }

    [Fact]
    public void AKeyForgottenComingUpDoesntLetGoOfAnotherHeld()
    {
        const int Up = 1, Space = 2;
        var report = new DiscoveryReport(Canvas(), "Short.", "END", 0);
        report.KeyDown(0, Up);                // held as the report starts: forgotten (FlushInput)
        report.Run(4000);
        Assert.True(report.Waiting);
        report.KeyDown(5000, Space);
        report.Run(5100);
        Assert.True(report.Restored);
        report.KeyUp(5200, Up);               // the forgotten key: Space is still down
        report.Run(5300);
        Assert.False(report.Done);
        report.KeyUp(5400, Space);
        report.Run(5500);
        Assert.True(report.Done);
    }

    [Fact]
    public void KeysLetGoWithTheWindowsFocusDontKeepItWaiting()
    {
        var report = new DiscoveryReport(Canvas(), "Short.", "END", 0);
        report.Run(4000);
        report.KeyDown(4500, 7);                // Alt, its coming up seen by another window
        report.KeyDown(5000, 2);
        report.KeyUp(5100, 2);
        report.Run(5200);
        Assert.True(report.Restored);
        Assert.False(report.Done);
        report.LetGo(5300);
        report.Run(5400);
        Assert.True(report.Done);
    }

    [Fact]
    public void TheEndOfPageStringIsTheOneRecorded() =>
        Assert.Equal(Root.GetProperty("end_string").GetInt32(), DiscoveryReport.EndPageString);

    // ------------------------------------------------------------ the picture

    [ContentFact]
    public void TheReportIsPaintedInTheLanderFontsColoursInTheScanMapsArea()
    {
        var (raster, area) = TestContent.With(p =>
        {
            var sheets = new AnimationSheets(p);
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(p, "colortable.main")[0]);
            var loaded = new Dictionary<string, SpriteSheet>();
            SpriteImage Frame(string s, int i)
            {
                if (!loaded.TryGetValue(s, out var sheet))
                    loaded[s] = sheet = SpriteSheet.Load(sheets, s);
                return sheet.Render(i, maps);
            }
            var font = UqmFont.Load(p, "font.lander");
            var canvas = new UqmCanvas(s => loaded.GetValueOrDefault(s)?.Count ?? SpriteSheet.Load(sheets, s).Count, (s, i) =>
            {
                var f = Frame(s, i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, new Dictionary<string, int> { [DiscoveryReport.Font] = font.Leading });
            var report = new DiscoveryReport(canvas, StringTable.Load(p, StoryWorlds.MoonbaseReport)[0],
                GameStrings.Load(p)[DiscoveryReport.EndPageString], 0);
            var raster = new ScreenRaster(Frame, _ => font);
            raster.Paint(report.Run(100_000).Select(c => c.Call));
            Assert.True(report.Waiting);
            return (raster, report.Area);
        });
        // Inside the area only, and text there in more than the cells' colours.
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
        Assert.True(colours.Count > 3, $"only {colours.Count} colours in the area");
    }
}
