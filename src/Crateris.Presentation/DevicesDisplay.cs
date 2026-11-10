// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/devices.c:
// DrawDevice, DrawDevicesDisplay, DrawDevices and DoManipulateDevices'
// drawing; src/libs/strings/unicode.c's utf8StringPos and skipUTF8Chars),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Content;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// The Manifest's Devices list as UQM draws it in the status area
/// (devices.c's DrawDevices; docs/plans/stage-3.md, item 11): a box with
/// the title (DEVICE_STRING_BASE's word, in the StarCon font), and five
/// rows from the top row shown, each the device's icon (MiscDataFrame's
/// 77 + device) and its name (the game string DEVICE_STRING_BASE + 1 +
/// device) on two lines in the tiny font, the first word on the first and
/// the rest on the second, the cursor's row lit. Drawn call for call as
/// UQM's code draws it (checked against UQM's own, tests/golden/
/// devices.json): <see cref="Draw"/> as DrawDevices, <see cref="Moved"/>
/// as DoManipulateDevices draws after a key.
/// </summary>
public static class DevicesDisplay
{
    /// <summary>MiscDataFrame: the device icons are its frames from <see cref="DeviceTable.FirstIconFrame"/>.</summary>
    public const string IconSheet = MineralSelling.IconSheet;

    // colors.h's Devices menu colours.
    public static readonly Rgb Back = Rgb.From15(0x00, 0x00, 0x14), Name = Rgb.From15(0x00, 0x14, 0x14),
        SelectedBack = Rgb.From15(0x0A, 0x0A, 0x1F), SelectedName = Rgb.From15(0x0A, 0x1F, 0x1F);

    // devices.c's places, in the status area.
    private const int IconWidth = 16, IconHeight = 16;
    private const int OrgY = 33, SpacingY = IconHeight + 2;
    private const int Col0 = 4, Col1 = 40;
    private const int FieldWidth = UqmCanvas.StatusWidth - 5;   // shipcont.h
    private const int SelOrgX = Col0 + IconWidth, SelWidth = FieldWidth + 1 - SelOrgX + 1;
    private const int IconOfsY = 1, NameOfsY = 2, TextBaseline = 6, TextSpacingY = 7;
    private const int BoxX = 2, BoxY = 20, BoxBottom = 129;

    /// <summary>MAX_VIS_DEVICES: the rows shown at once.</summary>
    public const int VisibleRows = (BoxBottom - OrgY) / SpacingY;

    /// <summary>
    /// The status area's part it draws on, in the screen's pixels: its box
    /// (DrawDevicesDisplay's, whose bottom line UQM draws a pixel lower than
    /// the cargo display's).
    /// </summary>
    public static readonly URect Area = new(UqmCanvas.SpaceWidth + BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY);

    /// <summary>
    /// DrawDevices: in the status area, the whole display first if
    /// <paramref name="old"/> is null (the list opened, or the page
    /// turned), else <paramref name="old"/>'s row unlit (if it isn't
    /// <paramref name="cursor"/>), then <paramref name="cursor"/>'s row
    /// lit. Rows are counted from <paramref name="top"/>, the top row
    /// shown.
    /// </summary>
    /// <param name="canvas">The canvas (its frames: graphics.miscdata's; its fonts starcon and tiny).</param>
    /// <param name="list">The devices aboard (InventoryDevices), in the list's order.</param>
    /// <param name="gameString">The game's strings (DEVICE_STRING_BASE's).</param>
    public static void Draw(UqmCanvas canvas, IReadOnlyList<int> list, int top, int? old, int cursor,
        Func<int, string> gameString)
    {
        var was = canvas.SetContext(canvas.Status);
        if (old is null)
        {
            DrawDisplay(canvas, list, top, gameString);
            old = cursor;   // not drawn unlit again this time
        }
        if (old != cursor)
            DrawDevice(canvas, list[old.Value], old.Value - top, false, gameString);
        DrawDevice(canvas, list[cursor], cursor - top, true, gameString);
        canvas.SetContext(was);
    }

    /// <summary>
    /// What DoManipulateDevices draws as a key moves the cursor from
    /// (<paramref name="oldCursor"/>, <paramref name="oldTop"/>) to
    /// (<paramref name="cursor"/>, <paramref name="top"/>): nothing if
    /// the cursor stayed, the whole display if the page turned, else the
    /// old row unlit and the new one lit.
    /// </summary>
    public static void Moved(UqmCanvas canvas, IReadOnlyList<int> list, int oldCursor, int oldTop, int cursor, int top,
        Func<int, string> gameString)
    {
        if (cursor == oldCursor)
            return;
        Draw(canvas, list, top, top != oldTop ? null : oldCursor, cursor, gameString);
    }

    // DrawDevicesDisplay: the box, the title, and each row shown unlit with its icon.
    private static void DrawDisplay(UqmCanvas canvas, IReadOnlyList<int> list, int top, Func<int, string> gameString)
    {
        canvas.StarConBox(new URect(BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY), 1, CargoDisplay.BoxMedium,
            CargoDisplay.BoxDark, true, Back);
        canvas.SetFont("starcon");
        canvas.SetForeground(SelectedName);
        canvas.Text((UqmCanvas.StatusWidth >> 1) - 1, BoxY + 7, TextAlign.Center, gameString(GameStrings.DeviceBase));
        for (int i = 0, cy = OrgY; i < VisibleRows && top + i < list.Count; ++i, cy += SpacingY)
        {
            canvas.Stamp(IconSheet, DeviceTable.FirstIconFrame + list[top + i], Col0, cy + IconOfsY);
            DrawDevice(canvas, list[top + i], i, false, gameString);
        }
    }

    // DrawDevice: the row's background, then the name's first word on one line and the rest on the next.
    private static void DrawDevice(UqmCanvas canvas, int device, int row, bool selected, Func<int, string> gameString)
    {
        var r = new URect(SelOrgX, OrgY + row * SpacingY + NameOfsY, SelWidth, TextSpacingY * 2);
        canvas.SetForeground(selected ? SelectedBack : Back);
        canvas.FillRect(r);
        canvas.SetFont("tiny");
        canvas.SetForeground(selected ? SelectedName : Name);
        var (first, count, rest) = Lines(gameString(DeviceTable.NameString(device)));
        canvas.Text(Col1, r.Y + TextBaseline, TextAlign.Center, first, count);
        canvas.Text(Col1, r.Y + TextBaseline + TextSpacingY, TextAlign.Center, rest);
    }

    /// <summary>
    /// A name's two lines as DrawDevice draws them: the whole name with the
    /// count of characters (code points) before its first space
    /// (utf8StringPos), -1 (all) if there's none; then what follows that
    /// space (skipUTF8Chars), nothing if there's none.
    /// </summary>
    public static (string Text, int Count, string Second) Lines(string name)
    {
        int pos = 0, at = -1, index = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value == ' ')
            {
                at = pos;
                break;
            }
            ++pos;
            index += rune.Utf16SequenceLength;
        }
        // CharCount (a COUNT: -1 is ~0, all); skipping CharCount + 1 characters, past the space or the end.
        return at < 0 ? (name, -1, "") : (name, at, name[(index + 1)..]);
    }
}
