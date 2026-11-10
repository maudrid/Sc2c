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
/// The Manifest's Devices list (stage 3a, items 10 and 11), while it's
/// open: the box <see cref="DevicesDisplay"/> draws as UQM's DrawDevices
/// does, each device's icon and its name a word a line, the cursor's row
/// lit, painted by <see cref="ScreenRaster"/> into the status area's part
/// for it, where the Manifest's cargo display goes, at the space screen's
/// scale (the status panel's text hidden meanwhile). It's drawn whole with
/// the cursor's row lit whenever that changes, which is the picture UQM's
/// drawing leaves after each key.
/// </summary>
public partial class DevicesView : Node2D
{
    private AnimationSheets? _sheets;
    private ColorMaps? _maps;
    private GameStrings? _strings;
    private SpriteSheet? _icons;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<int, SpriteImage> _frames = new();

    private ImageTexture? _texture;
    private List<UqmDraw>? _drawn;

    public DevicesView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The content: the device icons, the fonts, the game's strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets)
    {
        _sheets = sheets;
        _maps = new ColorMaps();
        _maps.Set(BinaryTable.Load(content, "colortable.main")[0]);
        _strings = GameStrings.Load(content);
        _icons = null;   // drawn afresh from this content
        _frames.Clear();
        _drawn = null;
        Broken = false;
        foreach (var f in new[] { "starcon", "tiny" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
    }

    private SpriteImage Frame(int index)
    {
        _icons ??= SpriteSheet.Load(_sheets!, DevicesDisplay.IconSheet);
        index %= _icons.Count;
        if (!_frames.TryGetValue(index, out var image))
            _frames[index] = image = _icons.Render(index, _maps!);
        return image;
    }

    /// <summary>Whether the list couldn't be drawn: it's closed whenever it opens, with a notice.</summary>
    public bool Broken { get; private set; }

    /// <summary>
    /// Shows the player's Devices list as it stands, or nothing if it's
    /// closed, a story talk pauses the game (it isn't listed meanwhile, as
    /// item 10 made it), or the content isn't loaded: whether it's shown.
    /// </summary>
    public bool Show(PlayerSession player)
    {
        if (player.OrbitMenu is not { DeviceList: { } list } menu || player.StoryPaused || _sheets is null)
        {
            if (_drawn is not null)
            {
                _drawn = null;
                QueueRedraw();
            }
            return false;
        }
        try
        {
            var canvas = new UqmCanvas(_ => { Frame(0); return _icons!.Count; }, (_, i) =>
            {
                var f = Frame(i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, _fonts.ToDictionary(f => f.Key, f => f.Value.Leading));
            DevicesDisplay.Draw(canvas, list, menu.DeviceTop, null, menu.DeviceCursor, i => _strings![i]);
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
            GD.PrintErr($"The Devices list couldn't be drawn: {e.Message}");
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
        var area = DevicesDisplay.Area;
        var source = new Rect2(area.X, area.Y, area.W, area.H);
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, source.Size * SisScreen.Scale), source);
    }
}
