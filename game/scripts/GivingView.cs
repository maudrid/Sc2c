// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using Crateris.Core.Content;
using Crateris.Core.Session;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The giving screen (stage 3a, items 9 and 11), while it's open: the box
/// <see cref="GivingScreen"/> lays out, painted by <see cref="ScreenRaster"/>
/// into the status area's part for it, where the Manifest's cargo display
/// and the minerals' count-down go, at the space screen's scale (the status
/// panel's text hidden meanwhile); below it, in the engine's font as the
/// cargo display's description goes below the orbit menu, who it gives to,
/// the notice (a gift given or refused) and the keys.
/// </summary>
public partial class GivingView : Node2D
{
    private AnimationSheets? _sheets;
    private ColorMaps? _maps;
    private GameStrings? _strings;
    private SpriteSheet? _icons;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<int, SpriteImage> _frames = new();

    private ImageTexture? _texture;
    private List<UqmDraw>? _drawn;
    private string _to = "", _notice = "";

    /// <summary>The keys, below the box.</summary>
    public const string Keys = "Up, down: the row. Left, right: who, how much (PgUp, PgDn: more). Enter: give. T, Esc, Backspace: close";

    public GivingView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The content: the element icons, the fonts, the game's strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets)
    {
        _sheets = sheets;
        _maps = new ColorMaps();
        _maps.Set(BinaryTable.Load(content, "colortable.main")[0]);
        _strings = GameStrings.Load(content);
        foreach (var f in new[] { "starcon", "tiny" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
    }

    private SpriteImage Frame(int index)
    {
        _icons ??= SpriteSheet.Load(_sheets!, MineralSelling.IconSheet);
        index %= _icons.Count;
        if (!_frames.TryGetValue(index, out var image))
            _frames[index] = image = _icons.Render(index, _maps!);
        return image;
    }

    /// <summary>Whether the screen couldn't be drawn: it's closed whenever it opens, with a notice.</summary>
    public bool Broken { get; private set; }

    /// <summary>
    /// Shows the player's giving screen as it stands, or nothing if it's
    /// closed (or the content isn't loaded): whether it's shown.
    /// </summary>
    public bool Show(PlayerSession player)
    {
        if (player.Trade is not { } trade || _sheets is null)
        {
            if (_drawn is not null)
            {
                _drawn = null;
                QueueRedraw();
            }
            return false;
        }
        var shown = trade.Follow(player.Snapshot, player.PlayerId);
        string to = shown.To is null ? "There's no one else here." : shown.Here
            ? $"To {shown.ToName}, in this system."
            : $"To {shown.ToName}: resource units only, as cargo goes only within a system.";
        if (to != _to || player.Notice != _notice)
        {
            (_to, _notice) = (to, player.Notice);
            QueueRedraw();
        }
        try
        {
            var canvas = new UqmCanvas(_ => { Frame(0); return _icons!.Count; }, (_, i) =>
            {
                var f = Frame(i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, _fonts.ToDictionary(f => f.Key, f => f.Value.Leading));
            GivingScreen.Draw(canvas, shown, i => _strings![i], _fonts["tiny"].Width);
            if (_drawn is not null && _drawn.SequenceEqual(canvas.Drawn))
                return true;
            _drawn = canvas.Drawn;
            var raster = new ScreenRaster((_, i) => Frame(i), f => _fonts[f]);
            raster.Paint(_drawn);
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, raster.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            QueueRedraw();
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"The giving screen couldn't be drawn: {e.Message}");
            _sheets = null;   // not tried again: the game closes it (Broken)
            Broken = true;
            _drawn = null;
            QueueRedraw();
            return false;
        }
    }

    public override void _Draw()
    {
        if (_drawn is null || _texture is null)
            return;
        var area = GivingScreen.Area;
        var source = new Rect2(area.X, area.Y, area.W, area.H);
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, source.Size * SisScreen.Scale), source);
        // Below the box, as the cargo display's description is below the orbit menu: who it gives to, or the
        // notice while there is one (a gift given or refused), then the keys, in the lines there are.
        var font = ThemeDB.FallbackFont;
        var keys = Wrap(font, Keys, KeyLines);
        var top = Wrap(font, _notice != "" ? _notice : _to, Math.Max(1, MostLines - keys.Count));
        float y = area.H * SisScreen.Scale + 12;
        foreach (var line in top)
        {
            DrawString(font, new Vector2(0, y), line, HorizontalAlignment.Left, Width, FontSize, _notice != "" ? Colors.Khaki : Colors.White);
            y += LineHeight;
        }
        foreach (var line in keys)
        {
            DrawString(font, new Vector2(0, y), line, HorizontalAlignment.Left, Width, FontSize, Colors.Gray);
            y += LineHeight;
        }
    }

    /// <summary>How wide the text below the box may be (to the space screen's left, less a margin).</summary>
    public float Width { get; set; } = 260;

    /// <summary>How many lines of text fit below the box: five above the orbit menu or the lander's gauges, more elsewhere.</summary>
    public int MostLines { get; set; } = 5;

    private const int FontSize = 13, LineHeight = 15, KeyLines = 3;

    // Text wrapped at words to the width, in at most some lines: one cut short ends in an ellipsis.
    private List<string> Wrap(Font font, string text, int most)
    {
        bool Fits(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X <= Width;
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string longer = line == "" ? word : line + " " + word;
            if (line != "" && !Fits(longer))
            {
                lines.Add(line);
                line = word;
            }
            else
                line = longer;
        }
        if (line != "")
            lines.Add(line);
        if (lines.Count <= most)
            return lines;
        lines.RemoveRange(most, lines.Count - most);
        string last = lines[^1];
        while (last.Contains(' ') && !Fits(last + "…"))
            last = last[..last.LastIndexOf(' ')];
        lines[^1] = last + "…";
        return lines;
    }
}
