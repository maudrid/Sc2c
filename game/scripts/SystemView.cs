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
using System.Threading.Tasks;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Godot;

namespace Crateris.Game;

/// <summary>
/// Drawing shared by the hyperspace, system and orbit views: UQM's 242 x
/// 227 system screen three times its size, the orbit ellipses, markers for
/// players in orbit, and the circle the orbit screen shows in placeholder
/// colours (by colour family) while the planet is made.
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

    /// <summary>A world's colour: its type's colour family, grey for the starbase and the Sa-Matra.</summary>
    public static Color World(SolarSystem system, WorldDesc w) => WorldTypes.HasPlanetData(w.DataIndex)
        ? Body(system.Generator.Types[WorldTypes.PlanetType(w.DataIndex)].BodyColor)
        : new Color(0.7f, 0.7f, 0.75f);

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

    private (int Star, List<SystemStamp> Stamps)? _background;

    /// <summary>The names of stars and worlds (set once the content is open, before the game starts).</summary>
    public Names Names { get; set; } = null!;

    /// <summary>The sprites (set once the content is open, before the game starts).</summary>
    public SpriteTextures? Sprites { get; set; }

    public SystemView(SystemCatalog systems)
    {
        _systems = systems;
        ClipContents = true;
        Size = Placeholder.Size;
        TextureFilter = TextureFilterEnum.Nearest;   // UQM's sprites, pixel for pixel
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
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _me);
        if (Sprites == null || me?.SystemStar is not int star || me.Activity is not (Activity.OuterSystem or Activity.InnerSystem))
            return;
        SolarSystem system = _systems[star];
        var font = ThemeDB.FallbackFont;

        // DrawSystem: the star background, the orbits, then the sun and worlds.
        if (_background?.Star != star)
            _background = (star, SystemSprites.StarBackground(system.Star));
        foreach (var stamp in _background.Value.Stamps)
            Sprites.Draw(this, stamp);

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

            var stamps = SystemSprites.Inner(system, planet);
            foreach (var stamp in stamps)
                Sprites.Draw(this, stamp);
            Markers(orbiting.Where(s => s.OrbitPlanet == planet && s.OrbitMoon is null), stamps[0]);
            for (int m = 0; m < moons.Length; ++m)
                Markers(orbiting.Where(s => s.OrbitPlanet == planet && s.OrbitMoon == m), stamps[m + 1]);
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

            var order = SystemSprites.DrawOrder(system);
            var stamps = SystemSprites.Outer(system, zoom);
            for (int i = 0; i < stamps.Count; ++i)
                Sprites.Draw(this, stamps[i]);
            for (int i = 0; i < stamps.Count; ++i)
                if (order[i] >= 0)
                    Markers(orbiting.Where(s => s.OrbitPlanet == order[i]), stamps[i]);
            // UQM's message line is the star's name, its title the star's coordinates.
            DrawString(font, new Vector2(8, 18), $"{Names.Cluster(system.Star)}   {system.Star.X / 10.0:0.0} : {system.Star.Y / 10.0:0.0}",
                HorizontalAlignment.Left, -1, 15, Colors.LightBlue);
            DrawString(font, new Vector2(8, 36), $"zoom {IpGeometry.MaxZoomRadius / zoom}x",
                HorizontalAlignment.Left, -1, 13, Colors.Gray);
        }

        // Ships in this same view: the flagship's image by facing, with the player's name.
        foreach (var s in _snapshot.Ships.Where(s => s.Activity == me.Activity && s.SystemStar == star && s.InnerPlanet == me.InnerPlanet))
        {
            var (x, y) = _motion.TryGetValue(s.Id, out var m) ? m.AtTime(_clock.Time) : (s.ScreenX, s.ScreenY);
            Sprites.Draw(this, Sprites.Sprites.Image(SystemSheet.Flagship, s.IpFacing), (float)x, (float)y);
            DrawString(font, new Vector2((float)x + 6, (float)y - 4) * Placeholder.Scale, s.Name,
                HorizontalAlignment.Left, -1, 14, Placeholder.ShipColor(s, _me));
        }
    }

    /// <summary>Markers for players orbiting a world, beside its image.</summary>
    private void Markers(IEnumerable<ShipSnapshot> ships, SystemStamp world)
    {
        var image = Sprites!.Sprites.Image(world);
        var at = new Vector2(world.X - image.HotSpotX + image.Width / 2f, world.Y - image.HotSpotY + image.Height / 2f) * Placeholder.Scale;
        float radius = image.Width / 2f * Placeholder.Scale;
        int i = 0;
        foreach (var s in ships)
            Placeholder.OrbitMarker(this, at, radius, Placeholder.ShipColor(s, _me), s.Name, i++);
    }
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

    // The layouts in UQM's fonts, made again only when what they show changes.
    private (string Words, DrawnText Text)? _title;
    private (PlanetReport Report, IReadOnlyList<DrawnText> Text)? _report;
    private (IReadOnlyList<OrbitMenuItem> Items, OrbitMenuItem Selected, DrawnMenu Menu)? _menuLayout;

    /// <summary>The names of stars and worlds (set once the content is open, before the game starts).</summary>
    public Names Names { get; set; } = null!;

    /// <summary>UQM's words and fonts for the orbit screen (set with <see cref="Names"/>).</summary>
    public OrbitScreenText Text { get; set; } = null!;

    /// <summary>The worlds' surfaces (set with <see cref="Names"/>).</summary>
    public PlanetSurfaces Surfaces { get; set; } = null!;

    private PlanetSurface? _surface;     // the orbited world's surface, made once per visit
    private ImageTexture? _surfaceMap;
    private readonly Dictionary<ScanType, ImageTexture> _tintedMaps = new();
    private PlanetSphere? _sphere;       // the rotating planet
    private ImageTexture? _sphereTexture;
    private long _sphereStep = -1;
    private Task<OrbitedWorld>? _building;   // the surface and sphere, being made off the main thread
    private static ImageTexture? _shieldHalo;
    private readonly PlaybackClock _clock = new();   // the shared world time the planet turns by
    private long _lastFrame = -1;

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
            _surface = null;
            _sphere = null;
            _sphereStep = -1;
            _surfaceMap = null;
            _tintedMaps.Clear();
            _building = null;
            if (menu.Orbit.Report is { } report)
            {
                // Made off the main thread (some milliseconds), so entering orbit
                // doesn't hold up a frame; the placeholder shows until it's ready.
                var (system, surfaces, planet, moon) = (_systems[w.Star], Surfaces, w.Planet, w.Moon);
                _building = Task.Run(() => surfaces.Orbited(system, planet, moon, report));
            }
        }
        if (_building is { IsCompleted: true } built)
        {
            _building = null;
            var orbited = built.GetAwaiter().GetResult();   // a failure is thrown here, on the main thread
            _surface = orbited.Surface;
            _sphere = orbited.Sphere;
            _surfaceMap = SurfaceTexture(_surface.Colors);
        }
        if (snapshot != null && snapshot.Frame != _lastFrame)
        {
            _lastFrame = snapshot.Frame;
            _clock.Observe(snapshot.Frame);
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
        var centre = new Vector2(PlanetSphere.CenterX, PlanetSphere.CenterY) * Placeholder.Scale;
        float radius = PlanetSphere.Radius * Placeholder.Scale;
        if (_sphere != null)
            DrawSphere();
        else
            DrawCircle(centre, radius, Placeholder.World(system, desc));
        // UQM's title over the scan screen (GetPlanetTitle, in MicroFont); above the
        // screen, in the engine's font, which world and star it is.
        string titleWords = Names.Title(system, w.Planet, w.Moon);
        if (_title?.Words != titleWords)
            _title = (titleWords, Text.Title(titleWords));
        UqmText.Draw(this, _title.Value.Text, Vector2.Zero, Placeholder.Scale);
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
            {
                if (!ReferenceEquals(_report?.Report, report))
                    _report = (report, Text.Report(report));
                foreach (var t in _report.Value.Text)
                    UqmText.Draw(this, t, Vector2.Zero, Placeholder.Scale);
            }
        }
        // UQM shows the surface map below the planet throughout orbit; scans draw over it.
        DrawMap(font, mapTop, _menu.Scan.Visible);
        DrawMenu(font);
    }

    private void DrawMap(Font font, float mapTop, bool scanning)
    {
        var scan = _menu!.Scan;
        var map = new Rect2(0, mapTop, Size.X, Surface.MapHeight * Placeholder.Scale);
        // The world's surface, as UQM generates and colours it.
        if (_surfaceMap != null)
            DrawTextureRect(_surfaceMap, map, false);
        else
            DrawRect(map, new Color(0.12f, 0.1f, 0.08f));
        DrawRect(map, new Color(0.4f, 0.4f, 0.5f), false, 1);
        if (!scanning)
            return;
        if (scan.Tint is ScanType tint)
        {
            // The map is redrawn tinted down to the scan line, as UQM's DrawPlanet
            // does: the tint colour added at half strength (renderTintFrame).
            Color c = Placeholder.ToColor(OrbitScan.TintColors[(int)tint]);
            int line = Math.Min(scan.Line, Surface.MapHeight - 1);
            float lineY = line * Placeholder.Scale;
            if (scan.Line >= 0 && _surface != null)
            {
                if (!_tintedMaps.TryGetValue(tint, out var tinted))
                    _tintedMaps[tint] = tinted = SurfaceTexture(PlanetSurfaces.Tinted(_surface.Colors, OrbitScan.TintColors[(int)tint]));
                DrawTextureRectRegion(tinted,
                    new Rect2(map.Position, new Vector2(map.Size.X, (line + 1) * Placeholder.Scale)),
                    new Rect2(0, 0, SurfaceMap.Width, line + 1));
            }
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
        if (!ReferenceEquals(_menuLayout?.Items, items) || _menuLayout.Value.Selected != _menu.Selected)
            _menuLayout = (items, _menu.Selected, Text.Menu(items, items.ToList().IndexOf(_menu.Selected)));
        var menu = _menuLayout.Value.Menu;
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

    /// <summary>Local time passes: the planet turns by the shared world time, smoothed as the ships are.</summary>
    public void Advance(double seconds) => _clock.Advance(seconds);

    // The rotating planet as UQM draws it (DrawPlanetSphere): the sphere, then the
    // shield's halo over it, centred at (SIS_SCREEN_WIDTH / 2, PLANET_ORG_Y), each
    // frame's hot spot one pixel past its radius. It turns one map column every
    // PLANET_ROTATION_RATE, by the shared world time (PlanetSphere.StepAt), so every
    // player at the world sees the same face.
    private void DrawSphere()
    {
        long steps = PlanetSphere.StepAt(_clock.Time);
        if (steps != _sphereStep)
        {
            var image = Image.CreateFromData(PlanetSphere.Diameter, PlanetSphere.Diameter, false, Image.Format.Rgba8,
                _sphere!.Render(_sphere.Offset(steps)));
            if (_sphereTexture == null)
                _sphereTexture = ImageTexture.CreateFromImage(image);
            else
                _sphereTexture.Update(image);
            _sphereStep = steps;
        }
        var at = new Vector2(PlanetSphere.CenterX - (PlanetSphere.Radius + 1), PlanetSphere.CenterY - (PlanetSphere.Radius + 1));
        DrawTextureRect(_sphereTexture!, new Rect2(at * Placeholder.Scale, Vector2.One * PlanetSphere.Diameter * Placeholder.Scale), false);
        if (_menu!.Shielded)
        {
            _shieldHalo ??= ImageTexture.CreateFromImage(Image.CreateFromData(PlanetSphere.ShieldDiameter, PlanetSphere.ShieldDiameter,
                false, Image.Format.Rgba8, PlanetSphere.ShieldHalo()));
            var h = new Vector2(PlanetSphere.CenterX - (PlanetSphere.ShieldRadius + 1), PlanetSphere.CenterY - (PlanetSphere.ShieldRadius + 1));
            DrawTextureRect(_shieldHalo, new Rect2(h * Placeholder.Scale, Vector2.One * PlanetSphere.ShieldDiameter * Placeholder.Scale), false);
        }
    }

    private static ImageTexture SurfaceTexture(Rgb[] colors)
    {
        var data = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; ++i)
        {
            data[i * 3] = colors[i].R;
            data[i * 3 + 1] = colors[i].G;
            data[i * 3 + 2] = colors[i].B;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(SurfaceMap.Width, SurfaceMap.Height, false, Image.Format.Rgb8, data));
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
