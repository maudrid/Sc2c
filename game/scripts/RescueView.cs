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
/// The rescue screen (stage 4a item 12), while it's open: the box
/// <see cref="RescueDrawing"/> lays out, painted by <see cref="ScreenRaster"/>
/// into the status area's part for it, where the giving screen goes; below
/// it, in the engine's font, the notice (a refusal) or what it does, and
/// the keys.
/// </summary>
public partial class RescueView : Node2D
{
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private ImageTexture? _texture;
    private List<UqmDraw>? _drawn;
    private string _notice = "";

    /// <summary>The keys, below the box.</summary>
    public const string Keys = "Up, down: the row. Left, right: which wreck, how much crew (PgUp, PgDn: more). Enter: give. Esc, Backspace: close";

    public RescueView() => TextureFilter = TextureFilterEnum.Nearest;

    public void UseContent(ContentPackage content)
    {
        foreach (var f in new[] { "starcon", "tiny" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
        _drawn = null;
        Broken = false;
    }

    /// <summary>Whether the screen couldn't be drawn: it's closed whenever it opens, with a notice.</summary>
    public bool Broken { get; private set; }

    /// <summary>How wide the text below the box may be.</summary>
    public float Width { get; set; } = 260;

    /// <summary>Shows the player's rescue screen as it stands, or nothing if it's closed: whether it's shown.</summary>
    public bool Show(PlayerSession player)
    {
        if (player.Rescue is not { } rescue || _fonts.Count == 0 || Broken)
        {
            if (_drawn is not null)
            {
                _drawn = null;
                QueueRedraw();
            }
            return false;
        }
        if (player.Notice != _notice)
        {
            _notice = player.Notice;
            QueueRedraw();
        }
        try
        {
            var shown = rescue.Follow(player.Snapshot, player.PlayerId);
            var canvas = new UqmCanvas(_ => 1, (_, _) => default, _fonts.ToDictionary(f => f.Key, f => f.Value.Leading));
            RescueDrawing.Draw(canvas, shown, _fonts["tiny"].Width);
            if (_drawn is not null && _drawn.SequenceEqual(canvas.Drawn))
                return true;
            _drawn = canvas.Drawn;
            var raster = new ScreenRaster((_, _) => throw new InvalidOperationException("no frames"), f => _fonts[f]);
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
            GD.PrintErr($"The rescue screen couldn't be drawn: {e.Message}");
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
        var area = RescueDrawing.Area;
        var source = new Rect2(area.X, area.Y, area.W, area.H);
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, source.Size * SisScreen.Scale), source);
        var font = ThemeDB.FallbackFont;
        float y = area.H * SisScreen.Scale + 12;
        foreach (var (line, keys) in GivingScreen.Below("Give the wreck crew of your own: it flies again.", _notice, Keys, 12,
                     s => font.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X <= Width))
        {
            DrawString(font, new Vector2(0, y), line, HorizontalAlignment.Left, Width, FontSize,
                keys ? Colors.Gray : _notice != "" ? Colors.Khaki : Colors.White);
            y += LineHeight;
        }
    }

    private const int FontSize = 13, LineHeight = 15;
}
