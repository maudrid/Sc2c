// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Session;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The giving screen as it's drawn (docs/plans/stage-3.md, item 11): laid
/// out after the Manifest's cargo display in the status area, with its
/// title, the player given to (their name cut to fit), what the giver has
/// and the amounts to give on each row, the receiver's room, the lit row,
/// and the cargo rows dimmed when the receiver isn't in the giver's system;
/// all within its area, painted from the content too.
/// </summary>
[Trait("Topic", "drawing")]
public class GivingScreenTests
{
    private const int IconW = 7, IconH = 7;

    // A canvas whose every frame is an icon's size; the tiny font's digits are 4 pixels and a space.
    private static UqmCanvas Canvas() => new(_ => 100, (_, _) => new URect(-3, -3, IconW, IconH), new Dictionary<string, int>());

    private static int Width(string text) => text.Length == 0 ? 0 : text.Length * 5 - 1;

    private static string GameString(int n) => n == GameStrings.CargoBase + 1 ? "Free" : $"{{gs:{n}}}";

    private static TradeShown Shown(int row = TradeScreen.UnitsRow, string name = "Guest", bool here = true, int? room = 469,
        long units = 2500, int give = 300)
    {
        var have = new long[TradeScreen.Rows];
        var amounts = new int[TradeScreen.Rows];
        have[TradeScreen.UnitsRow] = units;
        amounts[TradeScreen.UnitsRow] = give;
        for (int i = 0; i < 8; ++i)
        {
            have[2 + i] = 10 * (i + 1);
            amounts[2 + i] = i;
        }
        have[TradeScreen.BioRow] = 7;
        amounts[TradeScreen.BioRow] = 2;
        return new TradeShown(row, "guest", name, here, room, have, amounts);
    }

    private static List<UqmDraw> Draw(TradeShown shown)
    {
        var canvas = Canvas();
        GivingScreen.Draw(canvas, shown, GameString, Width);
        return canvas.Drawn;
    }

    private static IEnumerable<UqmDraw> Texts(List<UqmDraw> drawn) => drawn.Where(d => d.Kind == UqmDrawKind.Text);

    // The status area's left, in the screen's pixels.
    private const int S = 256;

    [Fact]
    public void ItHasItsTitleTheReceiverTheHeadsAndEachRowsNumbers()
    {
        var drawn = Draw(Shown());
        var texts = Texts(drawn).ToList();
        var title = texts.Single(t => t.Font == "starcon");
        Assert.Equal((GivingScreen.Title, S + 31, 27, TextAlign.Center), (title.Text, title.X, title.Y, title.Align));
        Assert.Equal("GIVE", GivingScreen.Title);
        Assert.Contains(texts, t => t.Text == "Guest" && t.Y == GivingScreen.RowY(TradeScreen.PlayerRow) + 6);
        Assert.Contains(texts, t => t.Text == GivingScreen.HaveHead && t.Align == TextAlign.Right);
        Assert.Contains(texts, t => t.Text == GivingScreen.GiveHead && t.X == S + 58);
        // Each row: what the giver has (right at 36), then the amount to give (right at 58), on the row's baseline.
        void Row(int row, string have, string give)
        {
            int y = GivingScreen.RowY(row) + 6;
            var at = texts.Where(t => t.Y == y && t.Font == "tiny" && t.Align == TextAlign.Right).OrderBy(t => t.X).ToList();
            Assert.Equal(new[] { (have, S + 36), (give, S + 58) }, at.Select(t => (t.Text, t.X)));
        }
        Row(TradeScreen.UnitsRow, "2500", "300");
        for (int i = 0; i < 8; ++i)
            Row(2 + i, (10 * (i + 1)).ToString(), i.ToString());
        Row(TradeScreen.BioRow, "7", "2");
        // The units' label, the receiver's room as the cargo display's Free row.
        Assert.Contains(texts, t => t.Text == GivingScreen.UnitsLabel && t.Y == GivingScreen.RowY(TradeScreen.UnitsRow) + 6);
        Assert.Contains(texts, t => t.Text == "Free" && t.Y == GivingScreen.FreeY + 6);
        Assert.Contains(texts, t => t.Text == "469" && t.Y == GivingScreen.FreeY + 6 && t.X == S + 59);
        // The icons: the eight categories' (MiscDataFrame's from 11, five apart) and the bio data's (68).
        var stamps = drawn.Where(d => d.Kind == UqmDrawKind.Stamp).ToList();
        Assert.Equal(Enumerable.Range(0, 8).Select(i => 11 + 5 * i).Append(68), stamps.Select(s => s.Frame));
        Assert.All(stamps, s => Assert.Equal(MineralSelling.IconSheet, s.Sheet));
        // Every row below the receiver's lies a row (9 pixels) apart, the bio data's after the Free row and the line.
        Assert.Equal(GivingScreen.RowY(TradeScreen.UnitsRow) + 9, GivingScreen.RowY(2));
        Assert.Equal(GivingScreen.FreeY, GivingScreen.RowY(9) + 9);
        Assert.Equal(GivingScreen.FreeY + 12, GivingScreen.RowY(TradeScreen.BioRow));
    }

    [Fact]
    public void TheBoxAndColoursAreTheCargoDisplays()
    {
        var drawn = Draw(Shown());
        var rects = drawn.Where(d => d.Kind == UqmDrawKind.Rect).ToList();
        // DrawStarConBox: the medium edges, the dark ones, the inside in the cargo display's back colour.
        Assert.Equal(CargoDisplay.BoxMedium, rects[0].Color);
        Assert.Equal((S + 2, 20, 60, 1), (rects[0].X, rects[0].Y, rects[0].Width, rects[0].Height));
        Assert.Contains(rects, r => r.Color == CargoDisplay.BoxDark);
        var inside = rects[4];
        Assert.Equal((S + 3, 21, 58, GivingScreen.Area.H - 2, CargoDisplay.Back), (inside.X, inside.Y, inside.Width, inside.Height, inside.Color));
        Assert.Equal(CargoDisplay.SelectedAmount, Texts(drawn).Single(t => t.Text == GivingScreen.Title).Color);
        // The line over the bio data.
        Assert.Contains(rects, r => r.Y == GivingScreen.RowY(TradeScreen.BioRow) - 2 && r.Height == 1 && r.Color == CargoDisplay.SelectedBack);
    }

    [Theory]
    [InlineData(TradeScreen.PlayerRow)]
    [InlineData(TradeScreen.UnitsRow)]
    [InlineData(4)]
    [InlineData(TradeScreen.BioRow)]
    public void TheRowTheCursorIsOnIsLitAsDrawElementAmountLightsOne(int row)
    {
        var drawn = Draw(Shown(row));
        // Each row's fill: lit only on the cursor's row.
        for (int r = 0; r < TradeScreen.Rows; ++r)
        {
            int y = GivingScreen.RowY(r);
            var fill = drawn.Single(d => d.Kind == UqmDrawKind.Rect && d.Y == y && d.Height == 7 && d.X != S + 7);
            Assert.Equal(r == row ? CargoDisplay.SelectedBack : CargoDisplay.Back, fill.Color);
            Assert.Equal(S + 58, fill.X + fill.Width - 1);   // to the amount's column, as UQM's
            var texts = Texts(drawn).Where(t => t.Y == y + 6).ToList();
            if (r == TradeScreen.PlayerRow)
                Assert.Equal(r == row ? CargoDisplay.SelectedAmount : CargoDisplay.Amount, texts.Single().Color);
            else
            {
                Assert.Equal(r == row ? CargoDisplay.SelectedWorth : CargoDisplay.Worth, texts.First(t => t.X == S + 36).Color);
                Assert.Equal(r == row ? CargoDisplay.SelectedAmount : CargoDisplay.Amount, texts.Single(t => t.X == S + 58).Color);
            }
        }
        // The cargo rows' fill starts past the icons, as UQM's; the receiver's and the units' across the box.
        Assert.Equal(S + 19, drawn.First(d => d.Kind == UqmDrawKind.Rect && d.Y == GivingScreen.RowY(2) && d.Height == 7 && d.X != S + 7).X);
        Assert.Equal(S + 4, drawn.First(d => d.Kind == UqmDrawKind.Rect && d.Y == GivingScreen.RowY(TradeScreen.UnitsRow)).X);
    }

    [Fact]
    public void ALongNameIsCutToFitTheBoxAndAShortOneIsWhole()
    {
        string Name(TradeShown s) => Texts(Draw(s)).Single(t => t.Y == GivingScreen.RowY(TradeScreen.PlayerRow) + 6).Text;
        Assert.Equal("Zoë", Name(Shown(name: "Zoë")));
        string cut = Name(Shown(name: "Captain Guestington of the Vindicator"));
        Assert.StartsWith(cut, "Captain Guestington of the Vindicator");
        Assert.True(Width(cut) <= GivingScreen.NameRoom);
        Assert.True(Width(cut + "x") > GivingScreen.NameRoom);
        Assert.Equal(53, GivingScreen.NameRoom);
        // A whole character at a time: never half of one written in two code units, nor a letter without its accent
        // (this test's widths count code units, so 44 pixels hold 9 of them, which would split the ninth character).
        Assert.Equal(new string('é', 9), GivingScreen.Cut(new string('é', 20), 44, Width));
        Assert.Equal(string.Concat(Enumerable.Repeat("😀", 4)), GivingScreen.Cut(string.Concat(Enumerable.Repeat("😀", 10)), 44, Width));
        Assert.Equal(string.Concat(Enumerable.Repeat("e\u0301", 4)),
            GivingScreen.Cut(string.Concat(Enumerable.Repeat("e\u0301", 10)), 44, Width));
        Assert.Equal("", GivingScreen.Cut("x", 0, Width));
        // No one to give to: a dash.
        Assert.Equal("-", Name(Shown() with { To = null, ToName = "" }));
    }

    [Fact]
    public void TheTextBelowTheBoxWrapsAtWordsAndEndsInAnEllipsisWhenCut()
    {
        bool Fits(string s) => s.Length <= 12;
        Assert.Equal(new[] { "one two", "three four" }, GivingScreen.Wrap("one two\nthree four", 3, Fits));
        // Its own line breaks kept, blank lines left out.
        Assert.Equal(new[] { "one", "two three", "four" }, GivingScreen.Wrap("one\ntwo three\n\nfour", 5, Fits));
        // A notice's page number ends its last line if it fits there, else has one of its own; it stays when the rest is cut.
        Assert.Equal(new[] { "one", "two (1/2)" }, GivingScreen.Wrap("one\ntwo\n(1/2)", 5, Fits));
        Assert.Equal(new[] { "one", "two three", "(1/2)" }, GivingScreen.Wrap("one\ntwo three\n(1/2)", 5, Fits));
        Assert.Equal(new[] { "a", "b", "c…", "(2/3)" }, GivingScreen.Wrap("a\nb\nc\nd\ne\n(2/3)", 4, Fits));
        Assert.Equal(new[] { "a…" }, GivingScreen.Wrap("a\nb\n(2/3)", 1, Fits));
        Assert.Equal(new[] { "(1/2)" }, GivingScreen.Wrap("\n(1/2)", 3, Fits));
        Assert.Equal(new[] { "one two", "three four…" }, GivingScreen.Wrap("one two three four five", 2, Fits));
        // Whole words are dropped from the last line to make room for the ellipsis.
        Assert.Equal(new[] { "abcde…" }, GivingScreen.Wrap("abcde fghijk lmn", 1, Fits));
        // A word too long for the last line is cut, a whole character at a time.
        Assert.Equal(new[] { "abcdefghijk…" }, GivingScreen.Wrap("abcdefghijklmnop qr", 1, Fits));
        Assert.Equal(new[] { "😀😀😀😀😀…" }, GivingScreen.Wrap(string.Concat(Enumerable.Repeat("😀", 9)) + " x", 1, Fits));
        Assert.Empty(GivingScreen.Wrap("", 2, Fits));
        // A word too long for a line of its own (a long name) is broken over lines, a whole character at a time.
        Assert.Equal(new[] { "To", "abcdefghijkl", "mnopqrstuvwx", "yz, here." },
            GivingScreen.Wrap("To abcdefghijklmnopqrstuvwxyz, here.", 5, Fits));
        Assert.Equal(new[] { "😀😀😀😀😀😀", "😀😀 x" }, GivingScreen.Wrap(string.Concat(Enumerable.Repeat("😀", 8)) + " x", 5, Fits));
    }

    [Fact]
    public void BelowTheBoxANoticeTooLongToLeaveTheKeysRoomHasEveryLine()
    {
        bool Fits(string s) => s.Length <= 12;
        const string keys = "keys one two three";   // two lines
        // Who it gives to, then the keys.
        Assert.Equal(new[] { ("To someone,", false), ("here.", false), ("keys one two", true), ("three", true) },
            GivingScreen.Below("To someone, here.", "", keys, 5, Fits));
        // A notice takes its place; one too long for the room the keys leave has it all, the keys left out.
        Assert.Equal(new[] { ("Gave ten.", false), ("keys one two", true), ("three", true) },
            GivingScreen.Below("To someone, here.", "Gave ten.", keys, 5, Fits));
        var report = string.Join(" ", Enumerable.Repeat("word", 8));   // four lines
        Assert.Equal(Enumerable.Repeat(("word word", false), 4), GivingScreen.Below("To someone.", report, keys, 5, Fits));
        Assert.Equal(new[] { ("word word", false), ("word word", false), ("word word…", false) },
            GivingScreen.Below("To someone.", report, keys, 3, Fits));
        // Who it gives to keeps the keys, cut to the lines they leave.
        Assert.Equal(new[] { ("word word…", false), ("keys one two", true), ("three", true) },
            GivingScreen.Below(report, "", keys, 3, Fits));
        // A paged notice keeps its number however it's cut, and counts it where it ends its last line.
        Assert.Equal(new[] { ("word word", false), ("word (1/2)", false), ("keys one two", true), ("three", true) },
            GivingScreen.Below("To someone.", "word word\nword\n(1/2)", keys, 5, Fits));
        Assert.Equal(new[] { ("word word", false), ("word word…", false), ("(1/2)", false) },
            GivingScreen.Below("To someone.", report + "\n(1/2)", keys, 3, Fits));
        // However few the lines, the top has one, and no more are given than there are.
        Assert.Equal(new[] { ("word word…", false), ("keys one…", true) }, GivingScreen.Below(report, "", keys, 2, Fits));
        Assert.Equal(new[] { ("word word…", false) }, GivingScreen.Below(report, "", keys, 1, Fits));
        Assert.Equal(new[] { ("word word…", false) }, GivingScreen.Below(report, "", keys, 0, Fits));
    }

    [Fact]
    public void WithTheReceiverElsewhereTheCargoRowsAreDimmedAndTheUnitsAreNot()
    {
        var drawn = Draw(Shown(here: false, row: 3));
        var texts = Texts(drawn).ToList();
        for (int r = 2; r < TradeScreen.Rows; ++r)
            Assert.All(texts.Where(t => t.Y == GivingScreen.RowY(r) + 6), t => Assert.Equal(GivingScreen.Dim, t.Color));
        Assert.All(texts.Where(t => t.Y == GivingScreen.FreeY + 6), t => Assert.Equal(GivingScreen.Dim, t.Color));
        Assert.DoesNotContain(texts.Where(t => t.Y == GivingScreen.RowY(TradeScreen.UnitsRow) + 6), t => t.Color == GivingScreen.Dim);
        // The icons are their shapes in the dim colour; the lit row is still lit.
        Assert.Empty(drawn.Where(d => d.Kind == UqmDrawKind.Stamp));
        var shapes = drawn.Where(d => d.Kind == UqmDrawKind.FilledStamp).ToList();
        Assert.Equal(9, shapes.Count);
        Assert.All(shapes, s => Assert.Equal(GivingScreen.Dim, s.Color));
        Assert.Contains(drawn, d => d.Kind == UqmDrawKind.Rect && d.Y == GivingScreen.RowY(3) && d.Color == CargoDisplay.SelectedBack);
        // Without the receiver's hold, no room is shown.
        Assert.Contains(Texts(Draw(Shown(room: null))), t => t.Y == GivingScreen.FreeY + 6 && t.Text == "" && t.Align == TextAlign.Right);
    }

    [Theory]
    [InlineData(2500, 300, true, 36)]         // the label, then the wallet in its column
    [InlineData(12345, 100, false, 36)]       // five digits: the label gives way
    [InlineData(123456, 1234, false, 36)]     // six digits still fit
    [InlineData(54321, 12345, false, 31)]     // a wide amount to give: the wallet moves left of it
    [InlineData(123456, 12345, false, -1)]    // with no room left for it, the wallet is left out
    [InlineData(4000000000, 2000000000, false, -1)]
    public void TheUnitsRowMakesRoomForLongNumbers(long units, int give, bool label, int haveAt)
    {
        var texts = Texts(Draw(Shown(units: units, give: give))).Where(t => t.Y == GivingScreen.RowY(TradeScreen.UnitsRow) + 6).ToList();
        Assert.Equal(label, texts.Any(t => t.Text == GivingScreen.UnitsLabel));
        if (haveAt < 0)
            Assert.DoesNotContain(texts, t => t.Text == units.ToString());
        else
            Assert.Equal(S + haveAt, texts.Single(t => t.Text == units.ToString()).X);
        Assert.Contains(texts, t => t.Text == give.ToString() && t.X == S + 58);   // the amount to give always shows
        // Nothing overlaps: each text ends before the next starts.
        var spans = texts.Select(t => t.Align == TextAlign.Right ? (t.X - Width(t.Text), t.X) : (t.X, t.X + Width(t.Text)))
            .OrderBy(s => s.Item1).ToList();
        for (int i = 1; i < spans.Count; ++i)
            Assert.True(spans[i - 1].Item2 < spans[i].Item1, $"{spans[i - 1]} runs into {spans[i]}");
    }

    [Theory]
    [InlineData(true, TradeScreen.PlayerRow, 999999, 99999, 9999)]
    [InlineData(false, TradeScreen.BioRow, 999999, 1234, 99999)]
    [InlineData(true, TradeScreen.UnitsRow, 4000000000, 2000000000, 1000000)]
    public void EverythingIsDrawnInsideItsArea(bool here, int row, long units, int give, int lots)
    {
        var area = GivingScreen.Area;
        Assert.Equal((S + 2, 20), (area.X, area.Y));
        Assert.True(area.X + area.W <= S + 64 && area.Y + area.H <= 173, "the box stays above the orbit menu, in the status area");
        var shown = Shown(row, "Captain Guestington of the Vindicator", here, units: units, give: give);
        shown = shown with { Have = shown.Have.Select((h, i) => i > TradeScreen.UnitsRow ? lots : h).ToArray() };
        var drawn = Draw(shown);
        // Inside the box's border (the box itself aside), and no number over an icon.
        foreach (var d in drawn.Skip(5))
        {
            Assert.Equal(new URect(S, 0, 64, 240), d.Clip);   // drawn in the status area's context
            var (x0, y0, x1, y1) = d.Kind switch
            {
                UqmDrawKind.Text => d.Align switch
                {
                    TextAlign.Right => (d.X - Width(d.Text), d.Y - 6, d.X, d.Y),
                    TextAlign.Center => (d.X - Width(d.Text) / 2, d.Y - 6, d.X + Width(d.Text) / 2, d.Y),
                    _ => (d.X, d.Y - 6, d.X + Width(d.Text), d.Y),
                },
                UqmDrawKind.Stamp or UqmDrawKind.FilledStamp => (d.X - 3, d.Y - 3, d.X - 3 + IconW, d.Y - 3 + IconH),
                _ => (d.X, d.Y, d.X + d.Width, d.Y + d.Height),
            };
            Assert.True(x0 >= area.X + 1 && y0 >= area.Y + 1 && x1 <= area.X + area.W - 1 && y1 <= area.Y + area.H - 1,
                $"{d.Kind} {d.Text} ({x0}, {y0})-({x1}, {y1}) outside {area}'s border");
            if (d.Kind == UqmDrawKind.Text && d.Y > GivingScreen.RowY(TradeScreen.UnitsRow) + 6 && d.Align == TextAlign.Right)
                Assert.True(x0 > S + 7 + IconW, $"{d.Text} runs over the icons");
        }
    }

    [ContentFact]
    public void PaintedFromTheContentItStaysInItsArea()
    {
        foreach (bool here in new[] { true, false })
        {
            var (raster, cut) = TestContent.With(p =>
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
                GivingScreen.Draw(canvas, Shown(TradeScreen.PlayerRow, "Captain Guestington of the Vindicator", here, units: 999999,
                    give: 99999), i => strings[i], fonts["tiny"].Width);
                Assert.Equal("Free", Texts(canvas.Drawn).Single(t => t.Y == GivingScreen.FreeY + 6 && t.Align == TextAlign.Left).Text);
                var raster = new ScreenRaster((_, i) => icons.Render(i, maps), f => fonts[f]);
                raster.Paint(canvas.Drawn);
                string name = Texts(canvas.Drawn).Single(t => t.Y == GivingScreen.RowY(TradeScreen.PlayerRow) + 6).Text;
                return (raster, (name, fonts["tiny"].Width(name)));
            });
            Assert.True(cut.Item2 <= GivingScreen.NameRoom && cut.name.Length is > 5 and < 20, $"{cut.name}: {cut.Item2} pixels");
            var area = GivingScreen.Area;
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
            // The box's corners are its edges' colours: the whole box is painted.
            int corner = (area.Y * ScreenRaster.Width + area.X) * 4;
            Assert.Equal(CargoDisplay.BoxMedium, new Systems.Rgb(raster.Rgba[corner], raster.Rgba[corner + 1], raster.Rgba[corner + 2]));
        }
    }
}
