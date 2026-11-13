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
using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Session;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The battle (stage 4a item 12): UQM's whole screen, the space window and
/// the status panel, as <see cref="BattleScreen"/> draws each frame the
/// host sends, painted by <see cref="ScreenRaster"/> from the battle's
/// sprites (<see cref="BattleSprites"/>, made as the content is opened).
/// The status panel is drawn incrementally, as UQM's is, so the raster is
/// kept from frame to frame. A battle frame already drawn (the host's
/// pause sends the same one again) draws nothing new.
/// </summary>
public partial class BattleScreenView : Node2D
{
    /// <summary>The screen's scale, as the starbase's.</summary>
    public const float Px = StarbaseScreenView.Px;

    public static readonly Vector2 Size = new Vector2(ScreenRaster.Width, ScreenRaster.Height) * Px;

    private BattleSprites? _sprites;
    private ShipDescriptions? _ships;
    private ContentPackage? _content;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<string, StringTable?> _raceStrings = new();
    private BattleScreen? _screen;
    private UqmCanvas? _canvas;
    private ScreenRaster? _raster;
    private ImageTexture? _texture;
    private long _frame = -1;

    public BattleScreenView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The content: every battle sprite drawn up front, the fonts, the ships' race strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets, ShipDescriptions ships, PlanetTypes planets)
    {
        _content = content;
        _ships = ships;
        _sprites = new BattleSprites(content, sheets, BattleKit.Fighters(ships), planets);
        _raceStrings.Clear();
        foreach (var f in new[] { "starcon", "tiny" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
        Reset();
    }

    private void Reset()
    {
        _screen = null;
        _raster = null;
        _frame = -1;
    }

    /// <summary>Whether the screen couldn't be drawn (its content broken): the battle is said in the status lines then.</summary>
    public bool Broken { get; private set; }

    private string RaceString(ShipDescription d, int index)
    {
        if (!_raceStrings.TryGetValue(d.Id, out var table))
            _raceStrings[d.Id] = table = d.RaceStrings is { } id && _content is not null ? StringTable.Load(_content, id) : null;
        return table is not null && index >= 0 && index < table.Count ? table[index] : "";
    }

    // A ship's look in the status panel: a player's flagship by its own names (status.c's SIS branch), the
    // enemy by its race's name and its captain's.
    private StatusShip StatusShip(BattleShipStatus s, string? playerId, WorldSnapshot? snapshot)
    {
        var d = _ships![s.Ship];
        bool sis = playerId is not null && s.Captain == 0;
        var ship = playerId is null ? null : snapshot?.Ships.FirstOrDefault(x => x.Id == playerId);
        return new StatusShip(d.Graphics.Icons, d.Graphics.Captain, s.MaxCrew, s.MaxEnergy,
            (d.Flags & ShipFlags.CrewImmune) != 0, (d.Flags & ShipFlags.PlayerCaptain) != 0, sis,
            sis ? ship?.ShipName ?? ship?.Name ?? "" : RaceString(d, 1),
            sis ? ship?.CaptainName ?? "" : RaceString(d, s.Captain));
    }

    /// <summary>Draws the player's battle as the host last sent it, if it's a frame not drawn yet: whether it's shown.</summary>
    public bool Show(PlayerSession player)
    {
        if (_sprites is null || Broken || player.Battle is not { } battle || player.Snapshot is not { } snapshot)
            return false;
        if (snapshot.Frame == _frame && _screen is not null)
            return true;   // no new snapshot yet
        try
        {
            if (_screen is null)
            {
                _canvas = new UqmCanvas(_sprites.Count, (sheet, i) =>
                {
                    var f = _sprites.Image(sheet, i);
                    return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
                }, _fonts.ToDictionary(f => f.Key, f => f.Value.Leading));
                _screen = new BattleScreen(_canvas, (s, id) => StatusShip(s, id, player.Snapshot));
            }
            var space = _screen.Frame(battle, player.PlayerId, BattleSpace.SizeOf);
            _frame = snapshot.Frame;
            if (space is null)
                return true;   // the battle's frame already drawn: the host's pause holds it
            if (_screen.Fresh || _raster is null)
                _raster = new ScreenRaster(_sprites.Image, f => _fonts[f]);
            _raster.Paint(space);
            _raster.Paint(_screen.Status);
            _screen.Status.Clear();
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            QueueRedraw();
            return true;
        }
        catch (Exception e) when (e is KeyNotFoundException or ContentPackageException or InvalidOperationException or IndexOutOfRangeException)
        {
            GD.PrintErr($"The battle couldn't be drawn: {e.Message}");
            Broken = true;
            Reset();
            return false;
        }
    }

    /// <summary>The battle's screen is put away (another screen is up): the next battle starts afresh.</summary>
    public void Leave()
    {
        if (_screen is not null)
            Reset();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        if (_texture is null || _screen is null)
            return;
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
    }
}
