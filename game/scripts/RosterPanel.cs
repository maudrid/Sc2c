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
/// <see cref="RosterFollower"/> draws it each frame (the roster's steps as
/// its keys were taken, the fleet's and flagship's crews from the host's
/// own live world); painted here. The orbit view shows it where its status
/// area is, above the menu box.
/// </summary>
public sealed class RosterPanel
{
    private readonly Func<(UqmCanvas Canvas, ScreenRaster Raster, StarbaseScreens Screens, GameStrings Strings)?> _newArea;
    private readonly RosterFollower _follower;
    private ScreenRaster? _raster, _nextRaster;
    private GameStrings? _strings;
    private ImageTexture? _texture;

    public RosterPanel(Func<(UqmCanvas, ScreenRaster, StarbaseScreens, GameStrings)?> newArea, Func<int, YardRace?> races,
        Func<string, int> raceIndex)
    {
        _newArea = newArea;
        _follower = new RosterFollower(() =>
        {
            if (_newArea() is not { } area)
                return null;
            (_nextRaster, _strings) = (area.Raster, area.Strings);
            return (area.Canvas, area.Screens);
        }, races, raceIndex);
        // The names need the content's strings before the first roster: taken with the first area.
        if (_newArea() is { } first)
            _strings = first.Strings;
    }

    /// <summary>The status area painted (the whole screen's raster; the status area is its right 64 columns), or null while closed.</summary>
    public ImageTexture? Texture => _follower.Open ? _texture : null;

    /// <summary>Where the highlight flashes (the status message while the crew is changed), in the screen's pixels.</summary>
    public URect? Flash => _follower.Flash;

    /// <summary>
    /// A frame: the roster as it stands (<paramref name="menu"/>, null when
    /// closed) and the fleet it acts on (<paramref name="fleet"/>, the
    /// host's live world's), over the host's ship as the snapshot has it,
    /// at <paramref name="now"/> (UQM's ticks).
    /// </summary>
    public void Update(RosterMenu? menu, IRosterFleet? fleet, ShipSnapshot? host, Func<string, int> raceIndex, long now)
    {
        if (_strings is null && menu is not null && _newArea() is { } a)
            _strings = a.Strings;
        _follower.Update(menu, fleet, () => host is { Modules: not null, Hold: not null } && _strings is not null && fleet is not null
            ? RosterFollower.Sis(host, fleet, _strings, raceIndex) : null, now);
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
