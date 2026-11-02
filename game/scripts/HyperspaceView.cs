// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Linq;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The hyperspace screen as UQM draws it (<see cref="HyperScreen"/>): the
/// dark red background, the stars near the ship from their sprites, and
/// the flagships of the players in hyperspace by facing, three times UQM's
/// size. Each player's name is written beside their ship (the engine's own).
/// </summary>
public partial class HyperspaceView : Control
{
    /// <summary>LoadHyperspace's background: MAKE_RGB15 (0x07, 0x00, 0x00).</summary>
    private static readonly Color Background = Placeholder.ToColor(Rgb.From15(0x07, 0x00, 0x00));

    private readonly StarMap _map;
    private WorldSnapshot? _snapshot;
    private string _me = "";

    public HyperspaceView(StarMap map)
    {
        _map = map;
        ClipContents = true;
        Size = Placeholder.Size;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public HyperspaceView() : this(null!)
    {
    }

    /// <summary>The sprites (set once the content is open, before the game starts).</summary>
    public SpriteTextures? Sprites { get; set; }

    public void Show(WorldSnapshot? snapshot, string me)
    {
        _snapshot = snapshot;
        _me = me;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        // The local player's ship, or until it's known, the first one.
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me) ?? _snapshot?.Ships.FirstOrDefault();
        if (me == null || me.Activity != Activity.Hyperspace || Sprites == null)
            return;

        foreach (Star star in _map.InYRange(me.UniverseY - HyperScreen.StarReachY, me.UniverseY + HyperScreen.StarReachY))
        {
            if (!HyperScreen.Shows(star, me.UniverseX, me.UniverseY))
                continue;
            var (x, y) = HyperScreen.Star(star, me.LogX, me.LogY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(false, SpaceSprites.StarFrame(star)), x, y);
        }

        // Only ships in hyperspace are drawn here (stage 2, decision 3), and
        // only those near enough to be on the screen. As in UQM, a ship
        // shows no thrust outside battle (ion trails are battle only).
        var font = ThemeDB.FallbackFont;
        foreach (var ship in _snapshot!.Ships.Where(s => s.Activity == Activity.Hyperspace
                     && HyperScreen.ShowsShip(s.UniverseX, s.UniverseY, me.UniverseX, me.UniverseY)))
        {
            var (x, y) = HyperScreen.Ship(ship.LogX, ship.LogY, ship.ViewX, ship.ViewY, me.LogX, me.LogY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(true, ship.Facing), x, y);
            DrawString(font, new Vector2(x + 9, y - 6) * Placeholder.Scale, ship.Name, HorizontalAlignment.Left, -1, 14,
                Placeholder.ShipColor(ship, _me));
        }
    }
}
