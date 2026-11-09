// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/sis.c's SetFlashRect: the
// highlight's merge factors and speed), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under the
// GNU GPL 2 or later.

using System;
using System.Collections.Generic;
using Crateris.Core.Content;
using Crateris.Core.Starbase;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The allied starbase's screens (stage 3a, item 11): UQM's whole 320 by
/// 240 screen, the menu, outfitting and the shipyard, as UQM draws them.
/// The host's drawings (<see cref="StarbaseDrawing"/>) are drawn by
/// <see cref="StarbasePicture"/> and painted over the picture as it stands
/// (<see cref="ScreenRaster"/>), the starbase turning and the power lines
/// on the client's clock; the flashing highlight is where the screen the
/// host last sent puts it (SetFlashRect), brighter by half every other
/// sixteenth of a second, as UQM's flashes.
/// </summary>
public partial class StarbaseScreenView : Node2D
{
    /// <summary>The screen's scale: UQM's whole screen at twice its size, beside the status text.</summary>
    public const float Px = 2f;

    /// <summary>The screen's size on the window.</summary>
    public static readonly Vector2 Size = new Vector2(ScreenRaster.Width, ScreenRaster.Height) * Px;
    private const long FlashTicks = 840 / 16;   // ONE_SECOND / 16: on, then off

    private ContentPackage? _content;
    private AnimationSheets? _sheets;
    private ColorMaps? _main;
    private BinaryTable? _hangar;
    private GameStrings? _strings;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<string, SpriteSheet> _loaded = new();
    private readonly Dictionary<(string, int, int), SpriteImage> _frames = new();

    private StarbasePicture? _picture;
    private ScreenRaster? _raster;
    private ImageTexture? _texture;
    private bool _changed;
    private URect? _flash;

    private bool _asked;

    /// <summary>
    /// There's no picture for the screen the host says is up (a drawing
    /// that couldn't be painted, or the content came after the drawing):
    /// the host is to draw it afresh. Asked once a picture.
    /// </summary>
    public event Action? Lost;

    public StarbaseScreenView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The client's clock, in UQM's ticks (ONE_SECOND 840).</summary>
    private static long Now => (long)(Time.GetTicksMsec() * 840 / 1000);

    /// <summary>The content: the screens' sheets, colour tables, fonts and the game's strings.</summary>
    public void UseContent(ContentPackage content, AnimationSheets sheets)
    {
        _content = content;
        _sheets = sheets;
        _main = new ColorMaps();
        _main.Set(BinaryTable.Load(content, "colortable.main")[0]);
        _hangar = BinaryTable.Load(content, StarbaseScreens.HangarColorTable);
        _strings = GameStrings.Load(content);
        foreach (var f in new[] { "starcon", "tiny", "micro" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
        Reset();
    }

    private SpriteSheet Sheet(string id)
    {
        if (!_loaded.TryGetValue(id, out var sheet))
            _loaded[id] = sheet = SpriteSheet.Load(_sheets!, id);
        return sheet;
    }

    // A frame in colortable.main's map, the shipyard's in the hangar's map the power lines are in.
    private SpriteImage Frame(string sheet, int index, int cmap)
    {
        if (sheet != StarbaseScreens.ShipyardSheet)
            cmap = -1;
        if (!_frames.TryGetValue((sheet, index, cmap), out var image))
        {
            var maps = _main!;
            if (cmap >= 0)
            {
                maps = _main!.Clone();
                maps.Set(_hangar![cmap]);
            }
            _frames[(sheet, index, cmap)] = image = Sheet(sheet).Render(index, maps);
        }
        return image;
    }

    // A fresh picture, black, waiting for the host's first drawing.
    private void Reset()
    {
        if (_content is null)
            return;
        var canvas = new UqmCanvas(s => Sheet(s).Count, (s, i) =>
        {
            var f = Frame(s, i, -1);
            return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
        }, new Dictionary<string, int>
        {
            ["starcon"] = _fonts["starcon"].Leading, ["tiny"] = _fonts["tiny"].Leading, ["micro"] = _fonts["micro"].Leading,
        });
        string flagship = ShipNames.FlagshipDefault(_strings!), commander = ShipNames.CommanderDefault(_strings!);
        _picture = new StarbasePicture(canvas, i => _strings![i], Sheet(StarbaseScreens.StarbaseSheet).Count, _hangar!.Count,
            s => SisState.Of(s, flagship, commander));
        _raster = new ScreenRaster((s, i) => Frame(s, i, _picture.ColorMap), f => _fonts[f]);
        _changed = true;
        _asked = false;
    }

    /// <summary>The host's drawings for this player, in order: drawn over the picture.</summary>
    public void Receive(IReadOnlyList<StarbaseDrawing> drawings)
    {
        if (_picture is null || drawings.Count == 0)
            return;
        try
        {
            Paint(_picture.Draw(drawings, Now));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or KeyNotFoundException
            or InvalidOperationException)
        {
            // A drawing this client can't paint: the picture starts afresh with the next screen drawn whole.
            GD.PrintErr($"The starbase's drawing couldn't be painted: {e.Message}");
            Reset();
        }
    }

    private void Paint(List<UqmDraw> calls)
    {
        if (calls.Count == 0)
            return;
        _raster!.Paint(calls);
        _changed = true;
    }

    /// <summary>The screen the host last sent: where the highlight flashes.</summary>
    public void Show(StarbaseView view)
    {
        if (view.Screen != StarbaseScreen.Talk && _picture is { Drawn: false } && !_asked)
        {
            _asked = true;
            Lost?.Invoke();
        }
        _flash = view.Screen switch
        {
            StarbaseScreen.Outfit => StarbaseScreens.OutfitFlash(view.OutfitState, view.OutfitItem, view.Installing),
            StarbaseScreen.Shipyard => StarbaseScreens.YardFlash(view.YardMode, view.YardCursor),
            _ => null,
        };
    }

    public override void _Process(double delta)
    {
        if (!Visible || _picture is null)
            return;
        try
        {
            Paint(_picture.Tick(Now));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or KeyNotFoundException
            or InvalidOperationException)
        {
            GD.PrintErr($"The starbase's drawing couldn't be painted: {e.Message}");
            Reset();
        }
        if (_changed && _raster is not null)
        {
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            _changed = false;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Colors.Black);
        if (_texture is null || _picture is not { Drawn: true })
            return;
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, size), false);
        // SetFlashRect's highlight: half again as bright (merge factors 3 / 2), then as drawn, a sixteenth of a second each.
        if (_flash is { } f && Now / FlashTicks % 2 == 0)
        {
            var r = new Rect2(f.X, f.Y, f.W, f.H);
            DrawTextureRectRegion(_texture, new Rect2(r.Position * Px, r.Size * Px), r, new Color(1.5f, 1.5f, 1.5f));
        }
    }
}
