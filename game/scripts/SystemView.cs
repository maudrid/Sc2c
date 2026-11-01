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
using Crateris.Core.Systems;
using Crateris.Core.World;
using Godot;

namespace Crateris.Game;

/// <summary>
/// Placeholder drawing shared by the system and orbit views until the UQM
/// content is downloaded: UQM's 242 x 227 system screen scaled up, worlds
/// as circles in placeholder colours for their colour family, and the
/// provisional contact circles' sizes (docs/differences.md rows 23 and 25).
/// </summary>
public static class Placeholder
{
    public const float Scale = 3f;
    public static readonly Vector2 Size = new(IpGeometry.ScreenWidth * Scale, IpGeometry.ScreenHeight * Scale);

    public static Color ToColor(Rgb c) => Color.Color8(c.R, c.G, c.B);

    /// <summary>Placeholder colours for UQM's planet colour families (PLANCOLOR).</summary>
    public static Color Body(string family) => family switch
    {
        "BLUE_BODY" => new Color(0.30f, 0.45f, 0.95f),
        "CYAN_BODY" => new Color(0.30f, 0.85f, 0.90f),
        "GREEN_BODY" => new Color(0.35f, 0.80f, 0.35f),
        "ORANGE_BODY" => new Color(0.95f, 0.60f, 0.20f),
        "PURPLE_BODY" => new Color(0.65f, 0.35f, 0.85f),
        "RED_BODY" => new Color(0.90f, 0.25f, 0.20f),
        "VIOLET_BODY" => new Color(0.80f, 0.45f, 0.90f),
        "WHITE_BODY" => new Color(0.90f, 0.90f, 0.90f),
        "YELLOW_BODY" => new Color(0.95f, 0.90f, 0.30f),
        _ => Colors.Gray,
    };

    public static Color Star(StarColor c) => c switch
    {
        StarColor.Blue => new Color(0.55f, 0.65f, 1f),
        StarColor.Green => new Color(0.5f, 1f, 0.5f),
        StarColor.Orange => new Color(1f, 0.65f, 0.25f),
        StarColor.Red => new Color(1f, 0.35f, 0.3f),
        StarColor.White => new Color(1f, 1f, 1f),
        _ => new Color(1f, 0.95f, 0.45f),
    };

    /// <summary>A world's colour: its type's colour family, grey for the starbase and the Sa-Matra.</summary>
    public static Color World(SolarSystem system, WorldDesc w) => WorldTypes.HasPlanetData(w.DataIndex)
        ? Body(system.Generator.Types[WorldTypes.PlanetType(w.DataIndex)].BodyColor)
        : new Color(0.7f, 0.7f, 0.75f);

    /// <summary>A world image's drawn radius in system pixels (the provisional contact circles).</summary>
    public static float Radius(int imageSize) => imageSize < 0
        ? IpContactShape.Provisional.JunkRadius
        : IpContactShape.Provisional.PlanetRadius[Math.Min(imageSize, IpContactShape.Provisional.PlanetRadius.Count - 1)];

    public static void Ellipse(CanvasItem c, IpRect r, Color color, float width = 1.5f)
    {
        var centre = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f) * Scale;
        var radii = new Vector2(r.Width / 2f, r.Height / 2f) * Scale;
        int n = Math.Clamp((int)(radii.X / 3), 48, 720);
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; ++i)
        {
            float a = Mathf.Tau * i / n;
            pts[i] = centre + new Vector2(Mathf.Cos(a) * radii.X, Mathf.Sin(a) * radii.Y);
        }
        c.DrawPolyline(pts, color, width);
    }

    /// <summary>A ship: a triangle pointing along a 16-way facing (0 up), with its name.</summary>
    public static void Ship(CanvasItem c, Vector2 p, int facing, Color color, string name, bool thrusting = false)
    {
        float angle = facing * Mathf.Tau / 16f;
        var dir = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
        var side = new Vector2(-dir.Y, dir.X);
        c.DrawColoredPolygon(new[] { p + dir * 11, p - dir * 7 + side * 6, p - dir * 7 - side * 6 }, color);
        if (thrusting)
            c.DrawLine(p - dir * 8, p - dir * 14, Colors.OrangeRed, 3);
        c.DrawString(ThemeDB.FallbackFont, p + new Vector2(11, -9), name, HorizontalAlignment.Left, -1, 14, color);
    }

    /// <summary>A player in orbit: a diamond beside the world, with the name.</summary>
    public static void OrbitMarker(CanvasItem c, Vector2 p, float worldRadius, Color color, string name, int index)
    {
        var at = p + new Vector2(worldRadius + 8 + index * 14, -worldRadius - 6);
        c.DrawColoredPolygon(new[] { at + new Vector2(0, -5), at + new Vector2(5, 0), at + new Vector2(0, 5), at + new Vector2(-5, 0) }, color);
        c.DrawString(ThemeDB.FallbackFont, at + new Vector2(7, -4), name + " (orbit)", HorizontalAlignment.Left, -1, 13, color);
    }

    public static Color ShipColor(ShipSnapshot s, string me) =>
        s.Id == me ? Colors.Cyan : (s.IsHost ? Colors.Gold : Colors.LightGreen);
}

/// <summary>
/// The outer system view (sun, orbits and planets at the ship's zoom) and
/// the inner system view (the planet, its moons and the arc of its own
/// orbit), as UQM lays them out (<see cref="SystemLayout"/>), with the
/// ships in the same view and markers for players orbiting a world shown
/// (stage 2, decisions 3 and 8).
/// </summary>
public partial class SystemView : Control
{
    private readonly SystemCatalog _systems;
    private readonly Dictionary<string, SmoothedMotion> _motion = new();
    private WorldSnapshot? _snapshot;
    private long _lastFrame = -1;
    private string _me = "";
    private readonly PlaybackClock _clock = new();

    /// <summary>The names of stars and worlds (set once the content is open, before the game starts).</summary>
    public Names Names { get; set; } = null!;

    public SystemView(SystemCatalog systems)
    {
        _systems = systems;
        ClipContents = true;
        Size = Placeholder.Size;
    }

    public SystemView() : this(null!)
    {
    }

    /// <summary>A new snapshot (once a world frame).</summary>
    public void Show(WorldSnapshot snapshot, string me)
    {
        _snapshot = snapshot;
        _me = me;
        if (snapshot.Frame == _lastFrame)
            return;
        _lastFrame = snapshot.Frame;
        _clock.Observe(snapshot.Frame);
        foreach (var s in snapshot.Ships.Where(s => s.Activity is Activity.OuterSystem or Activity.InnerSystem))
        {
            if (!_motion.TryGetValue(s.Id, out var m))
                _motion[s.Id] = m = new SmoothedMotion();
            m.Add(snapshot.Frame, s.ScreenX, s.ScreenY, (s.SystemStar, s.InnerPlanet, s.InnerPlanet is null ? s.ZoomRadius : 0));
        }
        foreach (var gone in _motion.Keys.Where(id => !snapshot.Ships.Any(s => s.Id == id)).ToList())
            _motion.Remove(gone);
    }

    /// <summary>
    /// Local time passes: ships are drawn at a steady time on the local
    /// clock, so unevenly arriving snapshots still draw evenly.
    /// </summary>
    public void Advance(double seconds)
    {
        _clock.Advance(seconds);
        if (Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.02f, 0.05f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.3f, 0.3f, 0.4f), false, 1);
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me);
        if (me?.SystemStar is not int star || me.Activity is not (Activity.OuterSystem or Activity.InnerSystem))
            return;
        SolarSystem system = _systems[star];
        var font = ThemeDB.FallbackFont;

        // Players orbiting a world of this system, by world.
        var orbiting = _snapshot!.Ships.Where(s => s.Activity == Activity.Orbit && s.SystemStar == star).ToList();

        if (me.InnerPlanet is int planet)
        {
            WorldDesc pl = system.Planets[planet];
            Color orbitColor = Placeholder.ToColor(SystemLayout.OrbitColors[system.OrbitColor(planet)]);
            Placeholder.Ellipse(this, SystemLayout.InnerPlanetOrbit(pl), orbitColor);
            WorldDesc[] moons = system.Moons(planet);
            foreach (var moon in moons)
                Placeholder.Ellipse(this, SystemLayout.InnerMoonOrbit(moon), orbitColor);

            var centre = new Vector2(SystemLayout.CenterX, SystemLayout.CenterY) * Placeholder.Scale;
            float r = Placeholder.Radius(SystemLayout.ImageSize(system.Generator, pl.DataIndex, SystemLayout.InnerPlanetSizeAdd)) * Placeholder.Scale;
            DrawCircle(centre, r, Placeholder.World(system, pl));
            if (WorldTypes.IsShielded(pl.DataIndex))
                DrawArc(centre, r + 5, 0, Mathf.Tau, 64, new Color(0.6f, 0.8f, 1f, 0.8f), 2);
            Markers(orbiting.Where(s => s.OrbitPlanet == planet && s.OrbitMoon is null), centre, r);

            for (int m = 0; m < moons.Length; ++m)
            {
                var at = ToView(SystemLayout.InnerMoon(moons[m]));
                float mr = Placeholder.Radius(SystemLayout.ImageSize(system.Generator, moons[m].DataIndex, SystemLayout.MoonSizeAdd)) * Placeholder.Scale;
                if (WorldTypes.HasPlanetData(moons[m].DataIndex))
                    DrawCircle(at, mr, Placeholder.World(system, moons[m]));
                else
                    DrawRect(new Rect2(at - new Vector2(mr, mr), new Vector2(mr, mr) * 2), Placeholder.World(system, moons[m]));
                Markers(orbiting.Where(s => s.OrbitPlanet == planet && s.OrbitMoon == m), at, mr);
            }
            // UQM's title over the inner system is the planet's name, its message line the star's.
            DrawString(font, new Vector2(8, 18), $"{Names.PlanetName(system.Star, planet)}, {Names.Cluster(system.Star)}"
                + (moons.Length > 0 ? $" ({moons.Length} moons)" : ""),
                HorizontalAlignment.Left, -1, 15, Colors.LightBlue);
        }
        else
        {
            int zoom = me.ZoomRadius;
            for (int p = 0; p < system.Planets.Count; ++p)
                Placeholder.Ellipse(this, SystemLayout.OuterOrbit(system.Planets[p], zoom),
                    Placeholder.ToColor(SystemLayout.OrbitColors[system.OrbitColor(p)]));

            var centre = new Vector2(SystemLayout.CenterX, SystemLayout.CenterY) * Placeholder.Scale;
            DrawCircle(centre, (3 + 2 * SystemLayout.SunSize(zoom)) * Placeholder.Scale, Placeholder.Star(system.Star.Color));

            int add = SystemLayout.OuterSizeAdd(zoom);
            for (int p = 0; p < system.Planets.Count; ++p)
            {
                WorldDesc pl = system.Planets[p];
                var at = ToView(SystemLayout.OuterPlanet(pl, zoom));
                float r = Placeholder.Radius(SystemLayout.ImageSize(system.Generator, pl.DataIndex, add)) * Placeholder.Scale;
                if (WorldTypes.HasPlanetData(pl.DataIndex))
                    DrawCircle(at, r, Placeholder.World(system, pl));
                else
                    DrawRect(new Rect2(at - new Vector2(r, r), new Vector2(r, r) * 2), Placeholder.World(system, pl));
                Markers(orbiting.Where(s => s.OrbitPlanet == p), at, r);
            }
            // UQM's message line is the star's name, its title the star's coordinates.
            DrawString(font, new Vector2(8, 18), $"{Names.Cluster(system.Star)}   {system.Star.X / 10.0:0.0} : {system.Star.Y / 10.0:0.0}",
                HorizontalAlignment.Left, -1, 15, Colors.LightBlue);
            DrawString(font, new Vector2(8, 36), $"zoom {IpGeometry.MaxZoomRadius / zoom}x",
                HorizontalAlignment.Left, -1, 13, Colors.Gray);
        }

        // Ships in this same view.
        foreach (var s in _snapshot.Ships.Where(s => s.Activity == me.Activity && s.SystemStar == star && s.InnerPlanet == me.InnerPlanet))
        {
            var (x, y) = _motion.TryGetValue(s.Id, out var m) ? m.AtTime(_clock.Time) : (s.ScreenX, s.ScreenY);
            Placeholder.Ship(this, new Vector2((float)x, (float)y) * Placeholder.Scale, s.IpFacing,
                Placeholder.ShipColor(s, _me), s.Name, s.Thrusting);
        }
    }

    private void Markers(IEnumerable<ShipSnapshot> ships, Vector2 at, float radius)
    {
        int i = 0;
        foreach (var s in ships)
            Placeholder.OrbitMarker(this, at, radius, Placeholder.ShipColor(s, _me), s.Name, i++);
    }

    private static Vector2 ToView(IpPoint p) => new Vector2(p.X, p.Y) * Placeholder.Scale;
}

/// <summary>
/// The orbit screen: the world (a placeholder circle), the orbit menu, and
/// in the scan menu the planet report and the scan map with its dots.
/// </summary>
public partial class OrbitView : Control
{
    private readonly SystemCatalog _systems;
    private OrbitMenu? _menu;
    private WorldDesc _world;   // the orbited world, worked out once per visit
    private Func<ScanType, uint> _taken = _ => 0;
    private WorldSnapshot? _snapshot;
    private string _me = "";
    private string _message = "";

    /// <summary>The names of stars and worlds (set once the content is open, before the game starts).</summary>
    public Names Names { get; set; } = null!;

    /// <summary>UQM's words and fonts for the orbit screen (set with <see cref="Names"/>).</summary>
    public OrbitScreenText Text { get; set; } = null!;

    public OrbitView(SystemCatalog systems)
    {
        _systems = systems;
        ClipContents = false;
        TextureFilter = TextureFilterEnum.Nearest;   // UQM's fonts, pixel for pixel
        Size = Placeholder.Size;
    }

    public OrbitView() : this(null!)
    {
    }

    public void Show(OrbitMenu? menu, Func<ScanType, uint> taken, WorldSnapshot? snapshot, string me, string message)
    {
        if (menu != null && menu != _menu)
        {
            WorldId w = menu.World;
            _world = w.Moon is int m ? _systems[w.Star].Moons(w.Planet)[m] : _systems[w.Star].Planets[w.Planet];
        }
        _menu = menu;
        _taken = taken;
        _snapshot = snapshot;
        _me = me;
        _message = message;
        QueueRedraw();
    }

    // Placeholder colours for the mineral categories (COMMON .. EXOTIC);
    // UQM's come from its content images.
    private static readonly Color[] MineralColors =
    {
        new(0.55f, 0.55f, 1f), new(1f, 0.3f, 0.3f), new(0.7f, 0.7f, 0.7f), new(0.4f, 0.9f, 1f),
        new(0.4f, 1f, 0.4f), new(1f, 0.85f, 0.2f), new(1f, 0.55f, 0.1f), new(0.9f, 0.4f, 1f),
    };

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.02f, 0.05f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.3f, 0.3f, 0.4f), false, 1);
        if (_menu is null)
            return;
        WorldId w = _menu.World;
        SolarSystem system = _systems[w.Star];
        WorldDesc desc = _world;
        OrbitData orbit = _menu.Orbit;

        // The world, above the scan map.
        float mapTop = (IpGeometry.ScreenHeight - Surface.MapHeight) * Placeholder.Scale;
        var centre = new Vector2(Size.X / 2, mapTop / 2);
        float radius = mapTop * 0.36f;
        DrawCircle(centre, radius, Placeholder.World(system, desc));
        if (_menu.Shielded)
            DrawArc(centre, radius + 8, 0, Mathf.Tau, 96, new Color(0.6f, 0.8f, 1f, 0.8f), 3);
        // UQM's title over the scan screen (GetPlanetTitle, in MicroFont); above the
        // screen, in the engine's font, which world and star it is.
        UqmText.Draw(this, Text.Title(Names.Title(system, w.Planet, w.Moon)), Vector2.Zero, Placeholder.Scale);
        DrawString(font, new Vector2(0, -6), $"{Names.PlanetOrMoon(system.Star, w.Planet, w.Moon)}, {Names.Cluster(system.Star)}",
            HorizontalAlignment.Left, -1, 13, Colors.Gray);

        // Others orbiting the same world.
        int i = 0;
        foreach (var s in _snapshot?.Ships.Where(s => s.Id != _me && s.Activity == Activity.Orbit && s.SystemStar == w.Star
                     && s.OrbitPlanet == w.Planet && s.OrbitMoon == w.Moon) ?? Enumerable.Empty<ShipSnapshot>())
            Placeholder.OrbitMarker(this, centre, radius, Placeholder.ShipColor(s, _me), s.Name, i++);

        if (_menu.Scan.Visible)
        {
            if (orbit.Scan is { } report)
                foreach (var t in Text.Report(report))
                    UqmText.Draw(this, t, Vector2.Zero, Placeholder.Scale);
            DrawMap(font, mapTop);
        }
        DrawMenu(font);
    }

    private void DrawMap(Font font, float mapTop)
    {
        var scan = _menu!.Scan;
        var map = new Rect2(0, mapTop, Size.X, Surface.MapHeight * Placeholder.Scale);
        DrawRect(map, new Color(0.12f, 0.1f, 0.08f));
        if (scan.Tint is ScanType tint)
        {
            // The map is redrawn tinted down to the scan line.
            Color c = Placeholder.ToColor(OrbitScan.TintColors[(int)tint]);
            float lineY = Math.Min(scan.Line, Surface.MapHeight - 1) * Placeholder.Scale;
            if (scan.Line >= 0)
                DrawRect(new Rect2(map.Position, new Vector2(map.Size.X, lineY + Placeholder.Scale)), new Color(c, 0.25f));
            if (scan.Sweeping && scan.Line >= 0 && scan.Line < Surface.MapHeight)
                DrawLine(new Vector2(0, mapTop + lineY), new Vector2(Size.X, mapTop + lineY), c, 2);
            // Which scan is drawn, in the scan menu's words (UQM shows only the tint).
            var label = Text.StarCon.Layout(Text.MenuEntry(tint switch
            {
                ScanType.Mineral => OrbitMenuItem.MineralScan,
                ScanType.Energy => OrbitMenuItem.EnergyScan,
                _ => OrbitMenuItem.BiologicalScan,
            }), IpGeometry.ScreenWidth >> 1, IpGeometry.ScreenHeight - Surface.MapHeight - 3, TextAlign.Center);
            UqmText.Draw(this, new DrawnText(Text.StarCon, label, OrbitScan.TextColors[(int)tint]), Vector2.Zero, Placeholder.Scale);
        }
        DrawRect(map, new Color(0.4f, 0.4f, 0.5f), false, 1);

        foreach (var dot in scan.Dots(_taken))
        {
            var at = map.Position + new Vector2(dot.Info.X + 0.5f, dot.Info.Y + 0.5f) * Placeholder.Scale;
            float r = (1f + dot.Frame) * Placeholder.Scale * 0.6f;
            Color c = dot.Flash is { } f ? Placeholder.ToColor(f) : dot.Scan switch
            {
                ScanType.Mineral => MineralColors[_systems.Generator.Types.ElementCategory(dot.Info.Type)],
                ScanType.Energy => new Color(1f, 0.35f, 0.35f),
                _ => new Color(0.4f, 1f, 0.4f),
            };
            DrawCircle(at, r, c);
        }
    }

    private void DrawMenu(Font font)
    {
        // UQM's PC menu box (DrawPCMenu, in the status area's coordinates), to the
        // left of the space screen below the status lines.
        var items = _menu!.Items;
        int selected = items.ToList().IndexOf(_menu.Selected);
        var menu = Text.Menu(items, selected);
        var areaAt = new Vector2(-Position.X + 12, 400);
        var origin = areaAt - new Vector2(OrbitScreenText.MenuAreaX - 1, OrbitScreenText.MenuAreaY) * Placeholder.Scale;
        UqmText.Draw(this, menu, origin, Placeholder.Scale);

        // Entries that come in a later stage say so, in the engine's font beside the box.
        float right = areaAt.X + (OrbitScreenText.MenuAreaWidth + 1) * Placeholder.Scale + 6;
        for (int i = 0; i < items.Count; ++i)
        {
            if (!OrbitMenu.Available(items[i]) && menu.Texts[i].Layout.Glyphs.Count > 0)
                DrawString(font, new Vector2(right, origin.Y + menu.Texts[i].Layout.Glyphs[0].Y * Placeholder.Scale),
                    "(later)", HorizontalAlignment.Left, -1, 13, Colors.Gray);
        }
        float below = areaAt.Y + OrbitScreenText.MenuAreaHeight * Placeholder.Scale;
        float width = Position.X - 24;
        DrawString(font, new Vector2(areaAt.X, below + 22), _message, HorizontalAlignment.Left, width, 14, Colors.Khaki);
        DrawString(font, new Vector2(areaAt.X, below + 44), "Arrows: choose. Enter: select. Esc: back",
            HorizontalAlignment.Left, width, 13, Colors.Gray);
    }

    public static string Label(OrbitMenuItem item) => item switch
    {
        OrbitMenuItem.Scan => "Scan",
        OrbitMenuItem.Starmap => "Starmap",
        OrbitMenuItem.Manifest => "Manifest",
        OrbitMenuItem.GameMenu => "Game menu",
        OrbitMenuItem.Navigate => "Navigate (leave orbit)",
        OrbitMenuItem.Cargo => "Cargo",
        OrbitMenuItem.Devices => "Devices",
        OrbitMenuItem.Roster => "Roster",
        OrbitMenuItem.ExitManifest => "Exit",
        OrbitMenuItem.MineralScan => "Mineral scan",
        OrbitMenuItem.EnergyScan => "Energy scan",
        OrbitMenuItem.BiologicalScan => "Biological scan",
        OrbitMenuItem.AutoScan => "Auto scan",
        OrbitMenuItem.DispatchLander => "Dispatch lander",
        _ => "Exit scan",
    };
}
