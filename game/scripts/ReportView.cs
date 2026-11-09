// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using Crateris.Core.Content;
using Crateris.Core.Systems;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The lander report on the finder's screen (stage 3a, item 11): UQM's
/// DoDiscoveryReport (<see cref="DiscoveryReport"/>) typed into the scan
/// map's area, over the planet-side and orbit views' map (both have it in
/// the same place), in the lander's font and colours, on the client's
/// clock. While it's up the player's keys are its own, as UQM waits for
/// them (any key: the rest of the page at once, the next page, the report
/// put away); once it's put away and the keys are up, they're the
/// player's again. A report that can't be drawn is dropped and shown as a
/// notice instead (<see cref="Failed"/>).
/// </summary>
public partial class ReportView : Node2D
{
    private ContentPackage? _content;
    private AnimationSheets? _sheets;
    private ColorMaps? _maps;
    private UqmFont? _font;
    private string _endPage = "";
    private readonly Dictionary<string, SpriteSheet> _loaded = new();
    private readonly Dictionary<(string, int), SpriteImage> _frames = new();

    private DiscoveryReport? _report;
    private string _text = "";
    private ScreenRaster? _raster;
    private ImageTexture? _texture;
    private bool _changed;

    /// <summary>A report couldn't be drawn: its text, to show another way.</summary>
    public event Action<string>? Failed;

    public ReportView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The client's clock, in UQM's ticks (ONE_SECOND 840).</summary>
    private static long Now => (long)(Time.GetTicksMsec() * 840 / 1000);

    /// <summary>Whether a report is shown or still has the keys (put away, a key still down).</summary>
    public bool Active => _report is { Done: false };

    /// <summary>The content: the cells, the lander's font and font effect, the end-of-page string.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets)
    {
        _content = content;
        _sheets = sheets;
        _maps = new ColorMaps();
        _maps.Set(BinaryTable.Load(content, "colortable.main")[0]);
        _font = UqmFont.Load(content, "font." + DiscoveryReport.Font);
        _endPage = GameStrings.Load(content)[DiscoveryReport.EndPageString];
    }

    private SpriteImage Frame(string sheet, int index)
    {
        if (!_loaded.TryGetValue(sheet, out var frames))
            _loaded[sheet] = frames = SpriteSheet.Load(_sheets!, sheet);
        index %= frames.Count;
        if (!_frames.TryGetValue((sheet, index), out var image))
            _frames[(sheet, index)] = image = frames.Render(index, _maps!);
        return image;
    }

    /// <summary>Whether a report can be shown now: the content is here and none is up.</summary>
    public bool Ready => _content is not null && _report is null;

    /// <summary>Starts showing a report: its text (the string table's entry).</summary>
    public void Show(string text)
    {
        if (!Ready)
            return;
        _text = text;
        try
        {
            var canvas = new UqmCanvas(s => { Frame(s, 0); return _loaded[s].Count; }, (s, i) =>
            {
                var f = Frame(s, i);
                return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
            }, new Dictionary<string, int> { [DiscoveryReport.Font] = _font!.Leading });
            _report = new DiscoveryReport(canvas, text, _endPage, Now);
            _raster = new ScreenRaster(Frame, _ => _font!);
            _changed = true;
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    /// <summary>A key went down (any key: UQM's AnyButtonPress).</summary>
    public void KeyDown(Key key) => _report?.KeyDown(Now, (int)key);

    /// <summary>A key came up (sent whenever a report is running, wherever the key went down, so none is left held).</summary>
    public void KeyUp(Key key) => _report?.KeyUp(Now, (int)key);

    private void Fail(Exception e)
    {
        GD.PrintErr($"The lander report couldn't be drawn: {e.Message}");
        _report = null;
        _raster = null;
        Failed?.Invoke(_text);
    }

    public override void _Process(double delta)
    {
        if (_report is null || _raster is null)
            return;
        try
        {
            var drawn = _report.Run(Now);
            foreach (var (_, call) in drawn)
                _raster.Paint(call);
            if (drawn.Count > 0)
                _changed = true;
        }
        catch (Exception e)
        {
            Fail(e);
            QueueRedraw();
            return;
        }
        if (_report.Done)
        {
            _report = null;
            _raster = null;
            QueueRedraw();
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
        // The scan map's area of UQM's screen, where the views draw the map: the report until it's put away.
        if (_report is not { Restored: false } report || _texture is null)
            return;
        var area = report.Area;
        var source = new Rect2(area.X, area.Y, area.W, area.H);
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, source.Size * SisScreen.Scale), source);
    }
}
