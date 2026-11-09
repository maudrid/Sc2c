// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The allied commander buying the hold's minerals, on the seller's own
/// screen (stage 3a, item 11): UQM's SellMinerals counting the hold down
/// (<see cref="MineralSelling"/>) in the status area's part for it, left
/// of the communication screen where the orbit screen's cargo display
/// goes, the cargo display and the resource units below it, on the
/// client's clock. While it's up the player's keys are its own (any key
/// sells the rest at once, as UQM's); once it's put away the commander's
/// reply plays. A count-down that can't be drawn is dropped, and the
/// reply plays at once.
/// </summary>
public partial class SellingView : Node2D
{
    private ContentPackage? _content;
    private AnimationSheets? _sheets;
    private ColorMaps? _maps;
    private GameStrings? _strings;
    private SpriteSheet? _icons;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<int, SpriteImage> _frames = new();

    private MineralSelling? _selling;
    private ScreenRaster? _raster;
    private ImageTexture? _texture;
    private bool _changed;
    private string _counted = "";

    public SellingView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The client's clock, in milliseconds (SDL_GetTicks').</summary>
    private static long Now => (long)Time.GetTicksMsec();

    /// <summary>The content: the element icons, the fonts, the game's strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets)
    {
        _content = content;
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

    /// <summary>Whether the count-down is shown (the cargo display not yet put away): the reply's lines wait, and the keys are its.</summary>
    public bool Active => _selling is { Done: false };

    /// <summary>When the lines held for <paramref name="linesKey"/>'s count-down may play, in UQM's ticks: -1 while they wait; now if none held them.</summary>
    public long LinesFrom(string linesKey) =>
        _counted != linesKey || _selling is null ? (long)(Now * 840 / 1000)
        : _selling.Done ? _selling.DoneAt * 840 / 1000 : -1;

    /// <summary>
    /// The player's reply sold their minerals (<paramref name="sale"/>,
    /// with the lines it was for, <see cref="TalkView.LinesKey"/>): the
    /// hold is counted down, once for those lines. <paramref name="held"/>
    /// are the keys down as it starts (held, as UQM's input would see them).
    /// </summary>
    public void Count(MineralSale sale, string linesKey, IEnumerable<Key> held)
    {
        if (linesKey == _counted || _content is null)
            return;
        _counted = linesKey;
        try
        {
            var canvas = new UqmCanvas(_ => { Frame(0); return _icons!.Count; }, (_, i) =>
            {
                var f = Frame(i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, new Dictionary<string, int> { ["starcon"] = _fonts["starcon"].Leading, ["tiny"] = _fonts["tiny"].Leading });
            _selling = new MineralSelling(canvas, sale, i => _strings![i], Now);
            foreach (var key in held)
                _selling.Held((int)key);
            _raster = new ScreenRaster((_, i) => Frame(i), f => _fonts[f]);
            _raster.Paint(_selling.Backdrop);
            _changed = true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"The minerals' count-down couldn't be drawn: {e.Message}");
            Stop();
        }
    }

    /// <summary>The talk is gone: the count-down with it (the next talk's sale counts afresh).</summary>
    public void Stop()
    {
        _selling = null;
        _raster = null;
        _counted = "";
        QueueRedraw();
    }

    /// <summary>A key went down (any key: UQM's AnyButtonPress).</summary>
    public void KeyDown(Key key) => _selling?.KeyDown(Now, (int)key);

    /// <summary>A key came up.</summary>
    public void KeyUp(Key key) => _selling?.KeyUp(Now, (int)key);

    /// <summary>The window lost the keyboard: every key let go.</summary>
    public void LetGo() => _selling?.LetGo(Now);

    public override void _Process(double delta)
    {
        if (_selling is null || _raster is null || _selling.Done)
            return;
        try
        {
            var drawn = _selling.Run(Now);
            foreach (var (_, call) in drawn)
                _raster.Paint(call);
            if (drawn.Count > 0)
                _changed = true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"The minerals' count-down couldn't be drawn: {e.Message}");
            _raster = null;
            _selling = null;   // the lines play at once
            QueueRedraw();
            return;
        }
        if (_selling.Done)
        {
            QueueRedraw();   // put away
            return;
        }
        if (_changed)
        {
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            _changed = false;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // The status area's part for it, until it's put away.
        if (!Active || _texture is null)
            return;
        var area = MineralSelling.Area;
        var source = new Rect2(area.X, area.Y, area.W, area.H);
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, source.Size * SisScreen.Scale), source);
    }
}
