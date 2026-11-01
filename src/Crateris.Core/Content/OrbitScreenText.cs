// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/scan.c
// PrintCoarseScanPC, PrintScanTitlePC; src/uqm/menu.c DrawPCMenu,
// DrawPCMenuFrame, DrawMenuStateStrings; src/uqm/colors.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;

namespace Crateris.Core.Content;

/// <summary>Text to draw: laid out in a font, in a colour.</summary>
public sealed record DrawnText(UqmFont Font, TextLayout Layout, Rgb Color);

/// <summary>A filled rectangle (or, with <see cref="Outline"/>, its one-pixel outline) to draw.</summary>
public sealed record DrawnRect(int X, int Y, int Width, int Height, Rgb Color, bool Outline = false);

/// <summary>A PC menu as DrawPCMenu draws it: rectangles first, then the entries' text.</summary>
public sealed record DrawnMenu(IReadOnlyList<DrawnRect> Rects, IReadOnlyList<DrawnText> Texts);

/// <summary>
/// The orbit screen's words as UQM's PC version draws them, in UQM's
/// fonts and colours and at UQM's positions (in UQM pixels): the title and
/// the coarse scan report (PlanetContext, the space screen above the scan
/// map), and the PC menus (the status area's menu box).
/// </summary>
public sealed class OrbitScreenText
{
    /// <summary>SCAN_LEADING_PC: the report's line spacing.</summary>
    public const int ScanLeading = 14;
    public const int LeftSideX = 5;                             // LEFT_SIDE_BASELINE_X_PC
    public const int RightSideX = IpGeometry.ScreenWidth - 75;  // RIGHT_SIDE_BASELINE_X_PC
    public const int ScanBaselineY = 40;                        // SCAN_BASELINE_Y_PC

    /// <summary>PC_MENU_HEIGHT: one menu entry.</summary>
    public const int MenuEntryHeight = 8;

    /// <summary>The first PC menu entry for the orbit screen (PM_ALT_SCAN in menustat.h); the others follow it in <see cref="OrbitMenuItem"/>'s order.</summary>
    public const int PmAltScan = 39;

    public static readonly Rgb TitleColor = Rgb.From15(0x00, 0x00, 0x15);           // SCAN_PC_TITLE_COLOR
    public static readonly Rgb InfoColor = Rgb.From15(0x0F, 0x00, 0x19);            // SCAN_INFO_COLOR
    public static readonly Rgb MenuAreaColor = Rgb.From15(0x0A, 0x0A, 0x0A);        // DrawMenuStateStrings' fill
    public static readonly Rgb MenuBackground = Rgb.From15(0x00, 0x00, 0x15);       // PCMENU_BACKGROUND_COLOR
    public static readonly Rgb MenuTopLeftBorder = Rgb.From15(0x0F, 0x0F, 0x0F);    // PCMENU_TOP_LEFT_BORDER_COLOR
    public static readonly Rgb MenuBottomRightBorder = Rgb.From15(0x06, 0x06, 0x06); // PCMENU_BOTTOM_RIGHT_BORDER_COLOR
    public static readonly Rgb MenuText = Rgb.From15(0x00, 0x15, 0x15);             // PCMENU_TEXT_COLOR
    public static readonly Rgb MenuSelectedText = Rgb.From15(0x0A, 0x1F, 0x1F);     // PCMENU_SELECTION_TEXT_COLOR
    public static readonly Rgb MenuSelectedBackground = Rgb.From15(0x0A, 0x0A, 0x1F); // PCMENU_SELECTION_BACKGROUND_COLOR

    /// <summary>The menu box in the status area (DrawMenuStateStrings: RADAR_X, RADAR_Y and RADAR_WIDTH, relative to StatusContext).</summary>
    public const int MenuAreaX = 3, MenuAreaY = 173, MenuAreaWidth = 58, MenuAreaHeight = 64;

    private readonly GameStrings _strings;

    public OrbitScreenText(GameStrings strings, UqmFont starCon, UqmFont tiny, UqmFont micro)
    {
        _strings = strings;
        StarCon = starCon;
        Tiny = tiny;
        Micro = micro;
    }

    /// <summary>StarConFont (the menus), TinyFont (the report), MicroFont (the title).</summary>
    public UqmFont StarCon { get; }
    public UqmFont Tiny { get; }
    public UqmFont Micro { get; }

    public static OrbitScreenText Load(ContentPackage package) => new(GameStrings.Load(package),
        UqmFont.Load(package, "font.starcon"), UqmFont.Load(package, "font.tiny"), UqmFont.Load(package, "font.micro"));

    /// <summary>A menu entry's words: GAME_STRING (MAINMENU_STRING_BASE + PM_ALT_...).</summary>
    public string MenuEntry(OrbitMenuItem item) => _strings[GameStrings.MainMenuBase + PmAltScan + (int)item];

    /// <summary>The title, centred at the top of the space screen in MicroFont (PrintCoarseScanPC).</summary>
    public DrawnText Title(string title) =>
        new(Micro, Micro.Layout(title, IpGeometry.ScreenWidth >> 1, 13, TextAlign.Center), TitleColor);

    /// <summary>
    /// The coarse scan report as PrintCoarseScanPC draws it: each label in
    /// the title colour, its value straight after it in the info colour, in
    /// TinyFont, five lines on each side.
    /// </summary>
    public IReadOnlyList<DrawnText> Report(PlanetReport r)
    {
        var drawn = new List<DrawnText>();
        string S(int n) => _strings[GameStrings.OrbitScanBase + n];
        string Value(long hundredths, int unit) => PlanetReport.ScanValue(hundredths) + S(unit);
        int y = ScanBaselineY;

        void Line(int x, int label, string value)
        {
            // PrintScanTitlePC: the label, then the value at the end of TextRect's width.
            var l = Tiny.Layout(S(label), x, y);
            drawn.Add(new DrawnText(Tiny, l, TitleColor));
            int valueX = x + (l.Drawn ? l.Width : 0);
            drawn.Add(new DrawnText(Tiny, Tiny.Layout(value, valueX, y), InfoColor));
            y += ScanLeading;
        }

        Line(LeftSideX, 0, Value(r.OrbitAu100, 1));                                    // "Orbit: ", " a.u."
        Line(LeftSideX, 2, r.Atmosphere switch
        {
            AtmosphereReading.SuperThick => S(3),                                      // "Super Thick"
            AtmosphereReading.Vacuum => S(4),                                          // "Vacuum"
            _ => Value(r.Atmospheres100, 5),                                           // " atm"
        });
        Line(LeftSideX, 6, $"{r.TemperatureC}° c");                               // "Temp: "
        Line(LeftSideX, 7, r.WeatherClass is int w ? $"{S(9)} {w}" : S(8));            // "Weather: ", "Class", "None"
        Line(LeftSideX, 10, r.TectonicsClass is int t ? $"{S(9)} {t}" : S(8));         // "Tectonics: "
        y = ScanBaselineY;
        Line(RightSideX, 11, Value(r.EarthMasses100, 12));                             // "Mass: ", " e.s."
        Line(RightSideX, 13, Value(r.EarthRadii100, 12));                              // "Radius: "
        Line(RightSideX, 14, Value(r.Gravity100, 15));                                 // "Gravity: ", " g."
        Line(RightSideX, 16, Value(r.Days100, 17));                                    // "Day: ", " days"
        Line(RightSideX, 18, $"{r.TiltDegrees}°");                                // "Tilt: "
        return drawn;
    }

    /// <summary>
    /// A PC menu as DrawMenuStateStrings and DrawPCMenu draw it, in the
    /// status area's coordinates: the area filled, the framed box centred in
    /// it, a bar behind the selected entry, and each entry in StarConFont.
    /// </summary>
    public DrawnMenu Menu(IReadOnlyList<OrbitMenuItem> items, int selected, bool highlight = true)
    {
        var rects = new List<DrawnRect>();
        var texts = new List<DrawnText>();
        // DrawMenuStateStrings' rectangle, then DrawPCMenu.
        int x = MenuAreaX - 1, y = MenuAreaY, w = MenuAreaWidth + 1, h = MenuAreaHeight;
        rects.Add(new DrawnRect(x, y, w, h, MenuAreaColor));
        if (items.Count * MenuEntryHeight <= h)
            y += (h - items.Count * MenuEntryHeight) / 2;
        h = items.Count * MenuEntryHeight + 4;

        // DrawPCMenuFrame: the top and left border, the bottom and right one a pixel in, the background.
        rects.Add(new DrawnRect(x, y, w, h, MenuTopLeftBorder, Outline: true));
        x++; y++; w--; h--;
        rects.Add(new DrawnRect(x, y, w, h, MenuBottomRightBorder, Outline: true));
        w--; h--;
        rects.Add(new DrawnRect(x, y, w, h, MenuBackground));

        int baseX = x + 2, baseY = y + MenuEntryHeight - 1;
        x++;
        w -= 2;
        for (int i = 0; i < items.Count; ++i)
        {
            bool isSelected = highlight && i == selected;
            if (isSelected)
                rects.Add(new DrawnRect(x, baseY - MenuEntryHeight + 2, w, MenuEntryHeight - 1, MenuSelectedBackground));
            texts.Add(new DrawnText(StarCon, StarCon.Layout(MenuEntry(items[i]), baseX, baseY),
                isSelected ? MenuSelectedText : MenuText));
            baseY += MenuEntryHeight;
        }
        return new DrawnMenu(rects, texts);
    }
}
