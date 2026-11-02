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
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;
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
    private static readonly Color Background = Placeholder.ToColor(Rgb.From15(0x07, 0x00, 0x00));

    private readonly StarMap _map;
    private WorldSnapshot? _snapshot;
    private string _me = "";
    private long _lastFrame = -1;

    // Each ship last seen in hyperspace, and the entries into stars being played.
    private readonly Dictionary<string, ShipSnapshot> _inHyperspace = new();
    private readonly Dictionary<string, Entry> _entries = new();

    /// <summary>A ship's entry into a star (<see cref="StarEntry"/>), from the hyperspace frame after contact.</summary>
    private sealed record Entry(ShipSnapshot Last, IReadOnlyList<StarEntry.Step> Steps, long StartFrame);

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
        if (snapshot != null && snapshot.Frame != _lastFrame)
        {
            _lastFrame = snapshot.Frame;
            Track(snapshot);
        }
        QueueRedraw();
    }

    /// <summary>
    /// Whether a ship's entry into a star is still playing. The server puts
    /// the ship in the system at once (docs/differences.md row 24); its
    /// player's screen stays on hyperspace until the entry has played.
    /// </summary>
    public bool Entering(string id) => _entries.ContainsKey(id);

    /// <summary>Starts an entry for each ship that has just gone from hyperspace into a star, and ends finished ones.</summary>
    private void Track(WorldSnapshot snapshot)
    {
        foreach (var ship in snapshot.Ships)
        {
            if (ship.Activity == Activity.Hyperspace)
            {
                _inHyperspace[ship.Id] = ship;
                _entries.Remove(ship.Id);
            }
            else if (_inHyperspace.Remove(ship.Id, out var last) && ship.Activity == Activity.OuterSystem
                     && ship.SystemStar is int star && Sprites != null)
            {
                var (x, y) = (last.LogX + (long)(last.ViewX - HyperFlight.CenterX), last.LogY + (long)(last.ViewY - HyperFlight.CenterY));
                _entries[ship.Id] = new Entry(last, StarEntry.Animate(x, y, last.Facing, _map.Stars[star],
                    Sprites.Sprites.HyperFlagship.Count), snapshot.Frame);
            }
        }
        foreach (var id in _inHyperspace.Keys.Where(id => !snapshot.Ships.Any(s => s.Id == id)).ToList())
            _inHyperspace.Remove(id);
        foreach (var (id, e) in _entries.ToList())
            if (snapshot.Frame - e.StartFrame > e.Steps.Count || !snapshot.Ships.Any(s => s.Id == id))
                _entries.Remove(id);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        // The local player's ship, or until it's known, the first one; while
        // its entry into a star plays, where it was in hyperspace.
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me) ?? _snapshot?.Ships.FirstOrDefault();
        if (me != null && _entries.TryGetValue(me.Id, out var mine))
            me = mine.Last;
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

        // Ships entering a star: turning and drifting onto it, as UQM plays it.
        foreach (var (id, e) in _entries)
        {
            int step = (int)Math.Clamp(_snapshot.Frame - e.StartFrame - 1, 0, e.Steps.Count - 1);
            var at = e.Steps[step];
            if (!HyperScreen.ShowsShip(Units.LogXToUniverse((int)at.X), Units.LogYToUniverse((int)at.Y), me.UniverseX, me.UniverseY))
                continue;
            var (x, y) = HyperScreen.Sprite(at.X, at.Y, me.LogX, me.LogY);
            Sprites.Draw(this, Sprites.Sprites.Hyper(true, at.Frame), x, y);
            DrawString(font, new Vector2(x + 9, y - 6) * Placeholder.Scale, e.Last.Name, HorizontalAlignment.Left, -1, 14,
                Placeholder.ShipColor(e.Last, _me));
        }
    }
}
