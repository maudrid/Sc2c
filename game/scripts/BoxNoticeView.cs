// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The notice (a gift, another player's report), below the Manifest's cargo
/// display or Devices list while either covers the status area and the
/// status panel is hidden: in the engine's font, as the giving screen shows
/// it below its box, in the lines there are above the orbit menu (a cut
/// ending in an ellipsis, <see cref="GivingScreen.Wrap"/>).
/// </summary>
public partial class BoxNoticeView : Node2D
{
    private string _text = "";

    /// <summary>The notice shown ("" for none).</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
                QueueRedraw();
            _text = value;
        }
    }

    /// <summary>How wide the text may be (to the space screen's left, less a margin).</summary>
    public float Width { get; set; } = 260;

    /// <summary>How many lines fit above the orbit menu.</summary>
    public int MostLines { get; set; } = 8;

    private const int FontSize = 13, LineHeight = 15;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        float y = 12;
        foreach (var line in GivingScreen.Wrap(_text, MostLines,
                     s => font.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X <= Width))
        {
            DrawString(font, new Vector2(0, y), line, HorizontalAlignment.Left, Width, FontSize, Colors.Khaki);
            y += LineHeight;
        }
    }
}
