// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Systems;

namespace Crateris.Core.Tests;

/// <summary>The orbit screen's words: UQM's menu numbering, and the report and menus laid out as UQM's PC version does.</summary>
public class OrbitScreenTextTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public void MenuEntriesAreNumberedAsInMenustatH()
    {
        var menu = Golden.Load("string-numbering").GetProperty("menu");
        int Pm(string name) => menu.GetProperty(name).GetInt32();
        Assert.Equal(OrbitScreenText.PmAltScan, Pm("PM_ALT_SCAN"));
        Assert.Equal((int)OrbitMenuItem.Navigate, Pm("PM_ALT_NAVIGATE") - Pm("PM_ALT_SCAN"));
        Assert.Equal((int)OrbitMenuItem.Cargo, Pm("PM_ALT_CARGO") - Pm("PM_ALT_SCAN"));
        Assert.Equal((int)OrbitMenuItem.ExitManifest, Pm("PM_ALT_EXIT_MANIFEST") - Pm("PM_ALT_SCAN"));
        Assert.Equal((int)OrbitMenuItem.MineralScan, Pm("PM_ALT_MSCAN") - Pm("PM_ALT_SCAN"));
        Assert.Equal((int)OrbitMenuItem.ExitScan, Pm("PM_ALT_EXIT_SCAN") - Pm("PM_ALT_SCAN"));
    }

    [ContentFact]
    public void TheMenusAreInUqmsWords()
    {
        var text = TestContent.With(OrbitScreenText.Load);
        Assert.Equal("scan", text.MenuEntry(OrbitMenuItem.Scan));
        Assert.Equal("starmap", text.MenuEntry(OrbitMenuItem.Starmap));
        Assert.Equal("navigate", text.MenuEntry(OrbitMenuItem.Navigate));
        foreach (OrbitMenuItem item in Enum.GetValues<OrbitMenuItem>())
            Assert.False(string.IsNullOrEmpty(text.MenuEntry(item)), $"{item} has no words");
    }

    [ContentFact]
    public void TheReportIsLaidOutAsPrintCoarseScanPc()
    {
        var text = TestContent.With(OrbitScreenText.Load);
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var report = sol.EnterOrbit(2, 1).Scan!;   // Luna
        var drawn = text.Report(report);
        Assert.Equal(20, drawn.Count);
        string Words(DrawnText d) => string.Concat(d.Layout.Glyphs.Select(g => char.ConvertFromUtf32(g.Glyph.Char)));
        Assert.Equal("Orbit:", Words(drawn[0]).TrimEnd());
        Assert.Equal("1.00 a.u.", Words(drawn[1]).Trim());
        Assert.Equal("Vacuum", Words(drawn[3]));
        Assert.Equal(OrbitScreenText.TitleColor, drawn[0].Color);
        Assert.Equal(OrbitScreenText.InfoColor, drawn[1].Color);
        // Each value starts where its label's TextRect ends; lines are SCAN_LEADING_PC apart; the right column at 167.
        Assert.Equal(OrbitScreenText.LeftSideX + drawn[0].Layout.Width, drawn[1].Layout.X);
        Assert.Equal(OrbitScreenText.ScanBaselineY + OrbitScreenText.ScanLeading, drawn[2].Layout.Glyphs[0].Y);
        Assert.Equal(167, drawn[10].Layout.X);
        Assert.Equal(OrbitScreenText.ScanBaselineY, drawn[10].Layout.Glyphs[0].Y);
    }

    [ContentFact]
    public void NumbersAreWrittenAsUqmWritesThemInAnyLocale()
    {
        var text = TestContent.With(OrbitScreenText.Load);
        var sol = Systems[Systems.Map.Stars.First(s => s.Special == "SOL_DEFINED").Number];
        var report = sol.EnterOrbit(2, 1).Scan!;   // Luna: -18 degrees
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Swedish writes minus as U+2212 and decimals with a comma.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            Assert.Equal("\u2212", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NegativeSign);
            var drawn = text.Report(report);
            string Words(DrawnText d) => string.Concat(d.Layout.Glyphs.Select(g => char.ConvertFromUtf32(g.Glyph.Char)));
            Assert.Equal("-18\u00b0 c", Words(drawn[5]));
            Assert.Equal("1.00 a.u.", Words(drawn[1]).Trim());
            Assert.Equal("0.25 e.s.", Words(drawn[13]).Trim());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [ContentFact]
    public void TheMenuBoxIsDrawnAsDrawPcMenu()
    {
        var text = TestContent.With(OrbitScreenText.Load);
        var items = new[] { OrbitMenuItem.Scan, OrbitMenuItem.Starmap, OrbitMenuItem.Manifest, OrbitMenuItem.GameMenu, OrbitMenuItem.Navigate };
        var menu = text.Menu(items, 1);
        // The area, two borders, the background, the selection bar.
        Assert.Equal(5, menu.Rects.Count);
        Assert.Equal(new DrawnRect(2, 173, 59, 64, OrbitScreenText.MenuAreaColor), menu.Rects[0]);
        // Five entries of 8 pixels, centred in 64: (64 - 40) / 2 = 12 down; the box is 44 high.
        Assert.Equal(new DrawnRect(2, 185, 59, 44, OrbitScreenText.MenuTopLeftBorder, true), menu.Rects[1]);
        Assert.Equal(new DrawnRect(3, 186, 57, 42, OrbitScreenText.MenuBackground), menu.Rects[3]);
        Assert.Equal(new DrawnRect(4, 186 + 7 + 8 - 8 + 2, 55, 7, OrbitScreenText.MenuSelectedBackground), menu.Rects[4]);
        Assert.Equal(5, menu.Texts.Count);
        Assert.Equal(5, menu.Texts[0].Layout.Glyphs[0].X);
        Assert.Equal(186 + 7, menu.Texts[0].Layout.Glyphs[0].Y);
        Assert.Equal(OrbitScreenText.MenuSelectedText, menu.Texts[1].Color);
        Assert.Equal(OrbitScreenText.MenuText, menu.Texts[0].Color);
    }
}
