// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Crateris.Core.World;
using Godot;

namespace Crateris.Game;

/// <summary>Star positions from data/starmap.json (no names: those are content).</summary>
public sealed record Starmap(IReadOnlyList<(int X, int Y, int Size)> Stars)
{
    public static Starmap Load(string dataDir)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "starmap.json")));
        var stars = doc.RootElement.GetProperty("stars").EnumerateArray().Select(s =>
            (s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(),
             s.GetProperty("size").GetString() switch { "SUPER_GIANT_STAR" => 3, "GIANT_STAR" => 2, _ => 1 }))
            .ToList();
        return new Starmap(stars);
    }
}

/// <summary>
/// Placeholder hyperspace view until the UQM content is downloaded and
/// drawn: stars are dots, ships are triangles with names, centred on the
/// local player's ship. 2 pixels per universe unit (0.1 map units).
/// </summary>
public partial class HyperspaceView : Node2D
{
    private const float PixelsPerUnit = 2f;
    private readonly Starmap _map;
    private WorldSnapshot? _snapshot;
    private string _me = "";

    public HyperspaceView(Starmap map) => _map = map;

    public HyperspaceView() : this(new Starmap(Array.Empty<(int, int, int)>()))
    {
    }

    public void Show(WorldSnapshot? snapshot, string me)
    {
        _snapshot = snapshot;
        _me = me;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0f, 0f));
        if (_snapshot == null)
            return;
        var me = _snapshot.Ships.FirstOrDefault(s => s.Id == _me) ?? _snapshot.Ships.FirstOrDefault();
        if (me == null)
            return;

        var centre = size / 2;
        Vector2 ToScreen(float ux, float uy) =>
            centre + new Vector2(ux - me.UniverseX, -(uy - me.UniverseY)) * PixelsPerUnit;

        if (me.Activity != Activity.Hyperspace)
        {
            // In a system: the system and orbit views draw over this.
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0.03f));
            return;
        }

        foreach (var (x, y, s) in _map.Stars)
        {
            var p = ToScreen(x, y);
            if (p.X < -10 || p.Y < -10 || p.X > size.X + 10 || p.Y > size.Y + 10)
                continue;
            DrawCircle(p, 1.5f + s, new Color(1f, 0.85f, 0.6f));
        }

        var font = ThemeDB.FallbackFont;
        // Only ships in hyperspace are drawn here (stage 2, decision 3).
        foreach (var ship in _snapshot.Ships.Where(s => s.Activity == Activity.Hyperspace))
        {
            var p = ToScreen(ship.UniverseX, ship.UniverseY);
            float angle = ship.Facing * Mathf.Tau / 16f; // facing 0 points up
            var dir = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            var side = new Vector2(-dir.Y, dir.X);
            var color = ship.Id == _me ? Colors.Cyan : (ship.IsHost ? Colors.Gold : Colors.LightGreen);
            DrawColoredPolygon(new[] { p + dir * 12, p - dir * 8 + side * 7, p - dir * 8 - side * 7 }, color);
            if (ship.Thrusting)
                DrawLine(p - dir * 9, p - dir * 16, Colors.OrangeRed, 3);
            DrawString(font, p + new Vector2(12, -10), ship.Name, HorizontalAlignment.Left, -1, 14, color);
        }
    }
}
