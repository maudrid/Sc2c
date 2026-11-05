// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// UQM's sprites as textures, and their shapes filled with one colour
/// (DrawFilledStamp), drawn three times UQM's size with the hot spot at the
/// stamp's place.
/// </summary>
public sealed class StampTextures
{
    private readonly Dictionary<SpriteImage, ImageTexture> _plain = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpriteImage, ImageTexture> _shape = new(ReferenceEqualityComparer.Instance);

    public ImageTexture Plain(SpriteImage image)
    {
        if (!_plain.TryGetValue(image, out var t))
            _plain[image] = t = ImageTexture.CreateFromImage(
                Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Rgba));
        return t;
    }

    // The frame's shape in white, to be drawn modulated by the fill colour.
    private ImageTexture Shape(SpriteImage image)
    {
        if (!_shape.TryGetValue(image, out var t))
        {
            var rgba = new byte[image.Rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                rgba[i + 3] = image.Rgba[i + 3] == 0 ? (byte)0 : (byte)255;
            }
            _shape[image] = t = ImageTexture.CreateFromImage(
                Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, rgba));
        }
        return t;
    }

    /// <summary>
    /// One of UQM's drawing calls (<see cref="SurfaceDraw"/>) at a view's
    /// origin: stamps from the surface's sprites, rectangles, text in its
    /// font and alignment; the planetside map is the caller's to draw.
    /// </summary>
    public void Draw(CanvasItem canvas, Vector2 origin, SurfaceDraw d, PlanetsideSprites sprites, OrbitScreenText? text)
    {
        switch (d.Kind)
        {
            case SurfaceDrawKind.Stamp:
                Draw(canvas, sprites.Image(d.Sheet, d.Frame), origin, d.X, d.Y);
                break;
            case SurfaceDrawKind.Filled:
                Draw(canvas, sprites.Image(d.Sheet, d.Frame), origin, d.X, d.Y, d.Color);
                break;
            case SurfaceDrawKind.Rectangle:
                canvas.DrawRect(new Rect2(origin + new Vector2(d.X, d.Y) * SisScreen.Scale,
                    new Vector2(d.Width, d.Height) * SisScreen.Scale), UqmText.ToColor(d.Color));
                break;
            case SurfaceDrawKind.Text when text is not null:
                var font = d.Font == SurfaceFont.StarCon ? text.StarCon : text.Tiny;
                UqmText.Draw(canvas, new DrawnText(font, font.Layout(d.Text, d.X, d.Y, d.Align), d.Color), origin, SisScreen.Scale);
                break;
        }
    }

    public void Draw(CanvasItem canvas, SpriteImage image, Vector2 origin, int x, int y, Rgb? fill = null)
    {
        var at = origin + new Vector2(x - image.HotSpotX, y - image.HotSpotY) * SisScreen.Scale;
        var rect = new Rect2(at, new Vector2(image.Width, image.Height) * SisScreen.Scale);
        if (fill is { } c)
            canvas.DrawTextureRect(Shape(image), rect, false, UqmText.ToColor(c));
        else
            canvas.DrawTextureRect(Plain(image), rect, false);
    }
}

/// <summary>
/// The planet's surface while the local player's lander is on it, as UQM
/// shows it (ScrollPlanetSide, RedrawSurfaceScan): the planetside map
/// scrolling under the lander, everything on the surface, the other
/// landers, the mineral text; the scan map below with what the scans found
/// and the lander's place; and the lander's gauges where the orbit menu
/// was. All from the snapshot (<see cref="PlanetsideScreen"/>).
/// </summary>
public partial class PlanetsideView : Control
{
    private readonly SystemCatalog _systems;
    private readonly StampTextures _textures = new();
    private Control _surfaceArea = null!;
    private Crateris.Core.Session.PlayerSession? _player;
    private WorldId? _world;
    private Task<(Rgb[] Map, Rgb[] Planetside)>? _building;
    private ImageTexture? _mapTexture, _planetsideTexture;

    /// <summary>The surface's sprites, the worlds' surfaces and UQM's words and fonts (set once the content is open).</summary>
    public PlanetsideSprites? Sprites { get; set; }

    /// <inheritdoc cref="Sprites"/>
    public PlanetSurfaces? Surfaces { get; set; }

    /// <inheritdoc cref="Sprites"/>
    public OrbitScreenText? Text { get; set; }

    public PlanetsideView(SystemCatalog systems)
    {
        _systems = systems;
        TextureFilter = TextureFilterEnum.Nearest;
        Size = SisScreen.Size;
        ClipContents = false;
    }

    public PlanetsideView() : this(null!)
    {
    }

    public override void _Ready()
    {
        // The surface view is clipped to its own area (PlanetContext).
        _surfaceArea = new Control
        {
            ClipContents = true,
            Size = new Vector2(PlanetsideScreen.Width, PlanetsideScreen.Height) * SisScreen.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _surfaceArea.Draw += DrawSurface;
        AddChild(_surfaceArea);
    }

    public void Show(Crateris.Core.Session.PlayerSession player)
    {
        _player = player;
        var menu = player.OrbitMenu;
        if (menu != null && menu.World != _world && menu.Orbit.Report is { } report && Surfaces != null)
        {
            // Made off the main thread (some milliseconds), as the orbit view's planet is.
            _world = menu.World;
            _mapTexture = _planetsideTexture = null;
            var (system, surfaces, planet, moon) = (_systems[menu.World.Star], Surfaces, menu.World.Planet, menu.World.Moon);
            _building = Task.Run(() =>
            {
                var surface = surfaces!.For(system, planet, moon, report);
                return (surface.Colors, surfaces.Planetside(system, planet, moon, report, surface) ?? surface.Colors);
            });
        }
        if (_building is { IsCompleted: true } built)
        {
            _building = null;
            var (map, planetside) = built.GetAwaiter().GetResult();
            _mapTexture = Texture(map, SurfaceMap.Width, SurfaceMap.Height);
            _planetsideTexture = planetside.Length == PlanetsideMap.Width * PlanetsideMap.Height
                ? Texture(planetside, PlanetsideMap.Width, PlanetsideMap.Height) : null;
        }
        QueueRedraw();
        _surfaceArea?.QueueRedraw();
    }

    private (SurfaceSnapshot Surface, LanderSnapshot Lander)? Mine()
    {
        if (_player is null || Core.Net.SnapshotCodec.SurfaceSeen(_player.Snapshot, _player.PlayerId) is not { } surface)
            return null;
        foreach (var l in surface.Landers)
        {
            if (l.PlayerId == _player.PlayerId)
                return (surface, l);
        }
        return null;
    }

    private void Stamp(CanvasItem canvas, Vector2 origin, SurfaceDraw d)
    {
        if (Sprites is null)
            return;
        if (d.Kind == SurfaceDrawKind.Map)
        {
            if (_planetsideTexture is not null)
                canvas.DrawTextureRect(_planetsideTexture, new Rect2(origin + new Vector2(d.X, d.Y) * SisScreen.Scale,
                    new Vector2(PlanetsideMap.Width, PlanetsideMap.Height) * SisScreen.Scale), false);
            return;
        }
        _textures.Draw(canvas, origin, d, Sprites, Text);
    }

    private void DrawSurface()
    {
        _surfaceArea.DrawRect(new Rect2(Vector2.Zero, _surfaceArea.Size), Colors.Black);
        if (Mine() is not var (surface, _) || _player is null || Sprites is null)
            return;
        var deposits = PlanetsideScreen.Deposits(_player.Deposits(), _systems.Generator.Types.ElementCategory);
        foreach (var d in PlanetsideScreen.Draw(surface, _player.PlayerId, deposits,
                     e => Text?.ElementName(e) ?? ""))
            Stamp(_surfaceArea, Vector2.Zero, d);
    }

    public override void _Draw()
    {
        // The border between the surface and the scan map (DrawPlanetSurfaceBorder), then the map.
        float mapTop = (IpGeometry.ScreenHeight - SurfaceMap.Height) * SisScreen.Scale;
        DrawRect(new Rect2(0, PlanetsideScreen.Height * SisScreen.Scale, Size.X, mapTop - PlanetsideScreen.Height * SisScreen.Scale),
            UqmText.ToColor(Rgb.From15(0x0A, 0x0A, 0x0A)));
        var map = new Rect2(0, mapTop, Size.X, SurfaceMap.Height * SisScreen.Scale);
        if (_mapTexture != null)
            DrawTextureRect(_mapTexture, map, false);
        else
            DrawRect(map, new Color(0.12f, 0.1f, 0.08f));
        if (Mine() is not var (surface, lander) || _player?.OrbitMenu is not { } menu || menu.Orbit.Nodes is not { } nodes)
            return;
        foreach (var d in PlanetsideScreen.ScanMap(surface, _player.PlayerId, nodes, menu.Scan.Found, _player.TakenAsDrawn,
                     _systems.Generator.Types.ElementCategory))
            Stamp(this, map.Position, d);

        // The gauges in the radar box, where the orbit menu was.
        if (_player.Me?.Hold is { } hold)
        {
            var radar = SisScreen.StatusOrigin(this) + new Vector2(SisScreen.RadarX, SisScreen.RadarY) * SisScreen.Scale;
            foreach (var d in LanderGauges.Draw(lander, hold))
                Stamp(this, radar, d);
        }
    }

    private static ImageTexture Texture(Rgb[] colors, int width, int height)
    {
        var data = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; ++i)
        {
            data[i * 3] = colors[i].R;
            data[i * 3 + 1] = colors[i].G;
            data[i * 3 + 2] = colors[i].B;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rgb8, data));
    }
}
