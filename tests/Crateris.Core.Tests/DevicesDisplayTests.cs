// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Content;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The Manifest's Devices list as it's drawn (docs/plans/stage-3.md, item
/// 11), against UQM's own DrawDevices (tests/golden/devices.json): the
/// list opened and each key's drawing, call for call; the picture after
/// each key the whole display with the cursor's row lit (as the client
/// draws it); and, painted from the content, every device's icon and name
/// inside the box. The recording's strings are its own, so the first
/// tests run without the content.
/// </summary>
[Trait("Topic", "drawing")]
public class DevicesDisplayTests
{
    private static readonly JsonElement Recorded = Golden.Load("devices").GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    private static JsonElement Scenario(string name) =>
        Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    // The recording's frames are MiscDataFrame's indexes as asked for (no wrapping), its sizes never asked.
    private static UqmCanvas Canvas() =>
        new(_ => 1000, (_, i) => throw new InvalidOperationException($"frame {i}'s size asked for"), new Dictionary<string, int>());

    // The recording program's game strings (golden/devices.c's GetStringAddress).
    private static readonly string[] More = { "", " b", " b c", "é b" };

    private static string GameString(int n) => $"{{gs:{n}}}{More[n % 4]}";

    private static MenuKey Key(string k) => k switch
    {
        "u" => MenuKey.Up,
        "d" => MenuKey.Down,
        "l" => MenuKey.Left,
        "r" => MenuKey.Right,
        "pu" => MenuKey.PageUp,
        "pd" => MenuKey.PageDown,
        "c" => MenuKey.Cancel,
        _ => throw new ArgumentException(k),
    };

    // A call of the port's as golden/devices.c writes UQM's.
    private static string Call(UqmDraw d)
    {
        var call = d.Kind switch
        {
            UqmDrawKind.Stamp => new JsonArray("s", d.Sheet, d.Frame, d.X, d.Y),
            UqmDrawKind.Rect => new JsonArray("r", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Text when d.Effect < 0 => new JsonArray("t", d.Font, d.X, d.Y, (int)d.Align, d.Count, d.Text, d.Color.R,
                d.Color.G, d.Color.B),
            _ => new JsonArray($"unexpected {d.Kind}"),
        };
        foreach (int v in new[] { d.Clip.X, d.Clip.Y, d.Clip.W, d.Clip.H })
            call.Add(v);
        return call.ToJsonString();
    }

    private static List<string> Want(JsonElement step) =>
        step.GetProperty("calls").EnumerateArray().Select(c => JsonNode.Parse(c.GetRawText())!.ToJsonString()).ToList();

    // A step: the list, the cursor and top row after it, whether the list was left, and what was drawn.
    private sealed record Step(IReadOnlyList<int> List, int Cursor, int Top, bool Left, List<UqmDraw> Calls);

    /// <summary>
    /// The port's drawing of a scenario, step by step: the list opened
    /// (DevicesMenu's first DrawDevices), then each key's
    /// (DoManipulateDevices'), with the cursor and top row as the port's
    /// <see cref="DevicesMenu"/> moves them.
    /// </summary>
    private static List<Step> Replay(JsonElement sc, Func<UqmCanvas> canvas, Func<int, string> gameString)
    {
        var list = sc.GetProperty("list").Ints();
        var steps = new List<Step>();
        if (list.Length == 0)
            return steps;
        var menu = new DevicesMenu(list);
        var c = canvas();
        DevicesDisplay.Draw(c, list, menu.Top, null, menu.Cursor, gameString);
        steps.Add(new Step(list, menu.Cursor, menu.Top, menu.Left, c.Drawn.ToList()));
        foreach (var k in sc.GetProperty("keys").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            c = canvas();
            var (cursor, top) = (menu.Cursor, menu.Top);
            menu.Press(Key(k));
            if (!menu.Left)
                DevicesDisplay.Moved(c, list, cursor, top, menu.Cursor, menu.Top, gameString);
            steps.Add(new Step(list, menu.Cursor, menu.Top, menu.Left, c.Drawn.ToList()));
            if (menu.Left)
                break;
        }
        return steps;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EachStepDrawsWhatUqmsDrewCallForCall(string name)
    {
        var sc = Scenario(name);
        var want = sc.GetProperty("steps").EnumerateArray().ToList();
        var got = Replay(sc, Canvas, GameString);
        Assert.Equal(want.Count, got.Count);
        for (int s = 0; s < want.Count; ++s)
        {
            var w = Want(want[s]);
            var g = got[s].Calls.Select(Call).ToList();
            for (int i = 0; i < Math.Max(w.Count, g.Count); ++i)
                Assert.True(i < w.Count && i < g.Count && w[i] == g[i],
                    $"{name}, step {s} ({want[s].GetProperty("step").GetString()}), call {i}:\n UQM  " +
                    $"{(i < w.Count ? w[i] : "(none)")}\n port {(i < g.Count ? g[i] : "(none)")}");
        }
    }

    [Fact]
    public void TheRecordingHasEveryKindOfCase()
    {
        Assert.Equal(DeviceTable.VisibleRows, DevicesDisplay.VisibleRows);
        var calls = Recorded.EnumerateArray().SelectMany(s => s.GetProperty("steps").EnumerateArray())
            .SelectMany(st => st.GetProperty("calls").EnumerateArray()).Where(c => c[0].GetString() == "t").ToList();
        // Each way a name splits: no space (all, then nothing), one, two, and a two-byte character before it.
        Assert.Contains(calls, c => c[5].GetInt32() == -1 && c[6].GetString() == "");
        Assert.Contains(calls, c => c[6].GetString() == "b c");
        Assert.Contains(calls, c => c[6].GetString()!.Contains('é') && c[5].GetInt32() == c[6].GetString()!.IndexOf(' '));
        // The title, and a list of more than three pages.
        Assert.Contains(calls, c => c[6].GetString() == GameString(GameStrings.DeviceBase));
        Assert.True(Scenario("every").GetProperty("list").GetArrayLength() > 3 * DevicesDisplay.VisibleRows);
        // A page turned (the whole display drawn again after a key), and a row moved within a page.
        var steps = Recorded.EnumerateArray().SelectMany(s => s.GetProperty("steps").EnumerateArray().Skip(1)).ToList();
        Assert.Contains(steps, st => st.GetProperty("calls").GetArrayLength() > 10);
        Assert.Contains(steps, st => st.GetProperty("calls").GetArrayLength() == 6);
        Assert.Contains(steps, st => st.GetProperty("calls").GetArrayLength() == 0 && st.GetProperty("left").GetInt32() == 0);
    }

    [Theory]
    [InlineData("Rosy Sphere", "Rosy Sphere", 4, "Sphere")]
    [InlineData("Wimbli's Trident", "Wimbli's Trident", 8, "Trident")]
    [InlineData("Ultron", "Ultron", -1, "")]
    [InlineData("Taalo Shield Device", "Taalo Shield Device", 5, "Shield Device")]
    [InlineData("\U0001F680x y", "\U0001F680x y", 2, "y")]   // a character outside the BMP is one, as UTF-8's
    [InlineData(" lead", " lead", 0, "lead")]
    public void ANamesFirstWordIsOneLineTheRestTheNext(string name, string text, int count, string rest) =>
        Assert.Equal((text, count, rest), DevicesDisplay.Lines(name));

    [Fact]
    public void ItsAreaIsTheBoxInTheStatusArea()
    {
        var c = Canvas();
        DevicesDisplay.Draw(c, new[] { 4, 27 }, 0, null, 1, GameString);
        var area = DevicesDisplay.Area;
        Assert.Equal(new URect(UqmCanvas.SpaceWidth + 2, 20, 60, 109), area);
        foreach (var d in c.Drawn)
        {
            Assert.Equal(new URect(UqmCanvas.SpaceWidth, 0, UqmCanvas.StatusWidth, UqmCanvas.StatusHeight), d.Clip);
            if (d.Kind == UqmDrawKind.Rect)
                Assert.True(d.X >= area.X && d.Y >= area.Y && d.X + d.Width <= area.X + area.W && d.Y + d.Height <= area.Y + area.H,
                    $"({d.X}, {d.Y}, {d.Width}, {d.Height}) outside {area}");
        }
        // The lit row is the second's, after the display.
        Assert.Equal(DevicesDisplay.SelectedBack, c.Drawn.Last(d => d.Kind == UqmDrawKind.Rect).Color);
        Assert.Equal(GameString(DeviceTable.NameString(27)), c.Drawn[^2].Text);
    }

    // The content's icons, fonts and strings, and a canvas and a painter with them.
    private sealed record Painting(Func<UqmCanvas> Canvas, Func<ScreenRaster> Raster, Func<int, string> GameString, int Frames);

    private static T WithContent<T>(Func<Painting, T> use) => TestContent.With(p =>
    {
        var sheets = new AnimationSheets(p);
        var maps = new ColorMaps();
        maps.Set(BinaryTable.Load(p, "colortable.main")[0]);
        var icons = SpriteSheet.Load(sheets, DevicesDisplay.IconSheet);
        var images = new Dictionary<int, SpriteImage>();
        SpriteImage Image(int i) => images.TryGetValue(i, out var im) ? im : images[i] = icons.Render(i, maps);
        var strings = GameStrings.Load(p);
        var fonts = new[] { "starcon", "tiny" }.ToDictionary(f => f, f => UqmFont.Load(p, "font." + f));
        return use(new Painting(() => new UqmCanvas(_ => icons.Count, (_, i) =>
            {
                var f = Image(i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, fonts.ToDictionary(f => f.Key, f => f.Value.Leading)),
            () => new ScreenRaster((_, i) => Image(i), f => fonts[f]), i => strings[i], icons.Count));
    });

    [ContentFact]
    public void AfterEachKeyThePictureIsTheWholeDisplayWithTheCursorLit()
    {
        // The client draws the whole display with the cursor's row lit whenever it changes: what UQM's screen shows
        // after its drawing, step by step.
        WithContent(paint =>
        {
            foreach (var name in new[] { "every", "several", "pages-15", "walk-3", "walk-7", "ultron-2" })
            {
                var steps = Replay(Scenario(name), paint.Canvas, paint.GameString);
                var screen = paint.Raster();
                for (int s = 0; s < steps.Count; ++s)
                {
                    screen.Paint(steps[s].Calls);
                    var step = steps[s];
                    if (step.Left)
                        break;
                    var c = paint.Canvas();
                    DevicesDisplay.Draw(c, step.List, step.Top, null, step.Cursor, paint.GameString);
                    var whole = paint.Raster();
                    whole.Paint(c.Drawn);
                    Assert.True(whole.Rgba.SequenceEqual(screen.Rgba), $"{name}, step {s}: the pictures differ");
                }
            }
            return 0;
        });
    }

    [ContentFact]
    public void ThePainterCountsANamesCharactersAsUqmDoes()
    {
        // A name's first line is drawn with a count of code points (utf8StringPos, getCharFromString's
        // characters), so a combining mark is one, not part of its letter's.
        WithContent(paint =>
        {
            var tiny = new UqmDraw(UqmDrawKind.Text, 290, 50, Color: DevicesDisplay.Name, Font: "tiny", Align: TextAlign.Center,
                Clip: new URect(UqmCanvas.SpaceWidth, 0, UqmCanvas.StatusWidth, UqmCanvas.StatusHeight));
            var counted = paint.Raster();
            counted.Paint(tiny with { Text = "E\u0301XY", Count = 3 });
            var whole = paint.Raster();
            whole.Paint(tiny with { Text = "E\u0301X" });
            var more = paint.Raster();
            more.Paint(tiny with { Text = "E\u0301XY" });
            Assert.True(counted.Rgba.SequenceEqual(whole.Rgba), "three code points aren't the first three drawn");
            Assert.False(counted.Rgba.SequenceEqual(more.Rgba), "the fourth drawn too");
            return 0;
        });
    }

    [ContentFact]
    public void PaintedFromTheContentEveryDeviceIsDrawnInsideTheBox()
    {
        var rules = StoryRules.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var listed = rules.Devices.All.Where(d => d.Listed).Select(d => d.Id).ToList();
        WithContent(paint =>
        {
            // MiscDataFrame has every device's icon: none wraps round its count.
            Assert.True(paint.Frames > DeviceTable.FirstIconFrame + listed.Max(), $"{paint.Frames} frames");
            var area = DevicesDisplay.Area;
            for (int top = 0; top < listed.Count; top += DevicesDisplay.VisibleRows)
                for (int cursor = top; cursor < Math.Min(listed.Count, top + DevicesDisplay.VisibleRows); ++cursor)
                {
                    var c = paint.Canvas();
                    DevicesDisplay.Draw(c, listed, top, null, cursor, paint.GameString);
                    // Each name's two lines: the content's names are two words.
                    var names = c.Drawn.Where(d => d.Kind == UqmDrawKind.Text && d.Font == "tiny").ToList();
                    Assert.All(names.Where((_, i) => i % 2 == 0), d => Assert.True(d.Count > 0, d.Text));
                    Assert.All(names.Where((_, i) => i % 2 == 1), d => Assert.NotEqual("", d.Text));
                    var raster = paint.Raster();
                    raster.Paint(c.Drawn);
                    var colours = new HashSet<(byte, byte, byte)>();
                    for (int y = 0; y < ScreenRaster.Height; ++y)
                        for (int x = 0; x < ScreenRaster.Width; ++x)
                        {
                            int o = (y * ScreenRaster.Width + x) * 4;
                            var col = (raster.Rgba[o], raster.Rgba[o + 1], raster.Rgba[o + 2]);
                            bool inside = x >= area.X && x < area.X + area.W && y >= area.Y && y < area.Y + area.H;
                            if (!inside)
                                Assert.True(col == (0, 0, 0), $"top {top}, cursor {cursor}: painted outside the box at ({x}, {y})");
                            else
                                colours.Add(col);
                        }
                    Assert.True(colours.Count > 6, $"only {colours.Count} colours in the box");
                    int corner = (area.Y * ScreenRaster.Width + area.X) * 4;
                    Assert.Equal(CargoDisplay.BoxMedium, new Rgb(raster.Rgba[corner], raster.Rgba[corner + 1], raster.Rgba[corner + 2]));
                    // Each icon is drawn: its row's left part isn't all the box's colour.
                    for (int row = 0; row < Math.Min(DevicesDisplay.VisibleRows, listed.Count - top); ++row)
                    {
                        int y0 = 33 + row * 18 + 1;
                        bool drawn = false;
                        for (int y = y0; y < y0 + 16 && !drawn; ++y)
                            for (int x = UqmCanvas.SpaceWidth + 4; x < UqmCanvas.SpaceWidth + 20 && !drawn; ++x)
                            {
                                int o = (y * ScreenRaster.Width + x) * 4;
                                drawn = new Rgb(raster.Rgba[o], raster.Rgba[o + 1], raster.Rgba[o + 2]) != DevicesDisplay.Back;
                            }
                        Assert.True(drawn, $"top {top}, row {row}: no icon");
                    }
                }
            return 0;
        });
    }
}
