// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using Crateris.Core.Content;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The roster in flight (stage 4b item 10): the status area as UQM shows
/// it while the roster is open, with the roster's drawing over it, as
/// <see cref="RosterFollower"/> works it out each frame from the open
/// roster and the host's ship and fleet in the snapshot; painted here. The
/// orbit view shows it where its status area is, above the menu box.
/// </summary>
public sealed class RosterPanel
{
    private readonly Func<(UqmCanvas Canvas, ScreenRaster Raster, StarbaseScreens Screens, GameStrings Strings)?> _newArea;
    private readonly RosterFollower _follower;
    private ScreenRaster? _raster, _nextRaster;
    private GameStrings? _strings;
    private ImageTexture? _texture;

    public RosterPanel(Func<(UqmCanvas, ScreenRaster, StarbaseScreens, GameStrings)?> newArea, Func<int, YardRace?> races)
    {
        _newArea = newArea;
        _follower = new RosterFollower(() =>
        {
            if (_newArea() is not { } area)
                return null;
            (_nextRaster, _strings) = (area.Raster, area.Strings);
            return (area.Canvas, area.Screens);
        }, races);
        // The names need the content's strings before the first roster: taken with the first area.
        if (_newArea() is { } first)
            _strings = first.Strings;
    }

    /// <summary>The status area painted (the whole screen's raster; the status area is its right 64 columns), or null while closed.</summary>
    public ImageTexture? Texture => _follower.Open ? _texture : null;

    /// <summary>Where the highlight flashes (the status message while the crew is changed), in the screen's pixels.</summary>
    public URect? Flash => _follower.Flash;

    /// <summary>A frame: the roster as it stands (<paramref name="menu"/>, null when closed) over the host's ship, at <paramref name="now"/> (UQM's ticks).</summary>
    public void Update(RosterMenu? menu, ShipSnapshot? host, Func<string, int> raceIndex, long now)
    {
        if (_strings is null && _newArea() is { } a)
            _strings = a.Strings;
        bool ready = menu is not null && host is { Fleet: not null, Modules: not null, Hold: not null } && _strings is not null;
        _follower.Update(menu, ready ? RosterFollower.Sis(host!, _strings!, raceIndex) : null,
            ready ? RosterFollower.Ships(host!.Fleet!, raceIndex) : null, now);
        if (!_follower.Open || _follower.Canvas is not { } canvas)
            return;
        if (_follower.Fresh)
            _raster = _nextRaster;
        if (canvas.Drawn.Count == 0 && !_follower.Fresh || _raster is null)
            return;
        _raster.Paint(canvas.Drawn);
        canvas.Drawn.Clear();
        var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster.Rgba);
        if (_texture is null)
            _texture = ImageTexture.CreateFromImage(image);
        else
            _texture.Update(image);
    }
}
