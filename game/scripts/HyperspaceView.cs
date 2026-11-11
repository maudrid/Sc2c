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
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The hyperspace screen as UQM draws it (<see cref="HyperScreen"/>): the
/// dark red background, the stars near the ship from their sprites, and
/// the flagships of the players in hyperspace by facing, three times UQM's
/// size, and a ship flying into a star turning and drifting onto it as UQM
/// plays it (<see cref="StarEntry"/>). Each player's name is written beside
/// their ship (the engine's own).
/// </summary>
public partial class HyperspaceView : Control
{
    /// <summary>LoadHyperspace's background: MAKE_RGB15 (0x07, 0x00, 0x00).</summary>
    private static readonly Color Background = SisScreen.ToColor(Rgb.From15(0x07, 0x00, 0x00));

    private readonly StarMap _map;
    private WorldSnapshot? _snapshot;
    private string _me = "";
    private StarEntryTracker? _entries;

    public HyperspaceView(StarMap map)
    {
        _map = map;
        ClipContents = true;
        Size = SisScreen.Size;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public HyperspaceView() : this(null!)
    {
    }

    /// <summary>The sprites (set once the content is open, before the game starts).</summary>
    public SpriteTextures? Sprites { get; set; }

    /// <summary>A snapshot to draw, and the entries into stars it shows.</summary>
    public void Show(WorldSnapshot? snapshot, string me, StarEntryTracker entries)
    {
        _snapshot = snapshot;
        _me = me;
        _entries = entries;
        QueueRedraw();
    }

    // Flying in hyperspace, or in a battle there, waiting at its marker (until item 12 draws the battle).
    private static bool InHyperspace(ShipSnapshot s) =>
        s.Activity == Activity.Hyperspace || s.Activity == Activity.Battle && s.SystemStar is null;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        // The local player's ship, or until it's known, the first one.
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me) ?? _snapshot?.Ships.FirstOrDefault();
        if (me == null || Sprites == null || _entries == null)
            return;
        // Where the screen is: the ship's place, or while its entry into a
        // star plays, where it touched the star.
        int logX = me.LogX, logY = me.LogY;
        if (_entries.Of(me.Id) is { } mine)
            (logX, logY) = (mine.State.LogX, mine.State.LogY);
        else if (!InHyperspace(me))
            return;
        int ux = Units.LogXToUniverse(logX), uy = Units.LogYToUniverse(logY);

        foreach (Star star in _map.InYRange(uy - StarContact.ReachY, uy + StarContact.ReachY))
        {
            if (!StarContact.InReach(star, ux, uy))
                continue;
            var (x, y) = HyperScreen.Star(star, logX, logY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(false, star.HyperspaceFrame), x, y);
        }

        // Only ships in hyperspace are drawn here (stage 2, decision 3), and
        // only those near enough to be on the screen. As in UQM, a ship
        // shows no thrust outside battle (ion trails are battle only).
        var font = ThemeDB.FallbackFont;
        foreach (var ship in _snapshot!.Ships.Where(s => InHyperspace(s)
                     && StarContact.InReach(s.UniverseX, s.UniverseY, ux, uy)))
        {
            var (x, y) = HyperScreen.Ship(ship.LogX, ship.LogY, ship.ViewX, ship.ViewY, logX, logY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(true, ship.Facing), x, y);
            DrawString(font, new Vector2(x + 9, y - 6) * SisScreen.Scale, ship.Name, HorizontalAlignment.Left, -1, 14,
                SisScreen.ShipColor(ship, _me));
        }

        // Ships entering a star: turning and drifting onto it, as UQM plays
        // it (an escort that joined meanwhile rides in with the flagship).
        foreach (var e in _entries.Drawn)
        {
            if (!StarContact.InReach(Units.LogXToUniverse((int)e.X), Units.LogYToUniverse((int)e.Y), ux, uy))
                continue;
            var (x, y) = HyperScreen.Sprite(e.X, e.Y, logX, logY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(true, e.Frame), x, y);
            DrawString(font, new Vector2(x + 9, y - 6) * SisScreen.Scale, e.Ship.Name, HorizontalAlignment.Left, -1, 14,
                SisScreen.ShipColor(e.Ship, _me));
        }
    }
}
