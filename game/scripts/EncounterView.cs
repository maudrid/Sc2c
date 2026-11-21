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
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The encounter screen and the battle-won screen in the space window
/// (stage 3b-1 item 11): what <see cref="EncounterScreen"/> and
/// <see cref="BattleWonScreen"/> draw, painted with
/// <see cref="ScreenRaster"/> and shown in the system screen's place. The
/// encounter screen is drawn once a meeting (the choices are the status
/// lines'); the battle-won screen runs on the client's clock, a key held
/// hurrying it and Escape ending it, and closes the session's tally at its
/// end. Anything that can't be drawn is left to the status lines.
/// </summary>
public partial class EncounterView : Node2D
{
    private const float Px = SisScreen.Scale;
    private static readonly URect Window = new(UqmCanvas.SisOrgX, UqmCanvas.SisOrgY, UqmCanvas.SisScreenWidth, UqmCanvas.SisScreenHeight);

    private AnimationSheets? _sheets;
    private ColorMaps? _main;
    private GameStrings? _strings;
    private ShipDescriptions? _ships;
    private PlanetTypes? _planets;
    private Names? _names;
    private StarMap? _map;
    private ContentPackage? _content;
    private readonly Dictionary<string, UqmFont> _fonts = new();
    private readonly Dictionary<string, SpriteSheet> _loaded = new();
    private readonly Dictionary<(string, int), SpriteImage> _frames = new();
    private readonly Dictionary<string, StringTable?> _raceStrings = new();

    private ScreenRaster? _raster;
    private ImageTexture? _texture;
    private bool _changed;
    private MeetingView? _drawnMeeting;
    private BattleNotice? _drawnTally;
    private BattleWonScreen? _won;
    private UqmCanvas? _wonCanvas;
    private int _wonPainted;

    public EncounterView() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>The client's clock, in UQM's ticks (ONE_SECOND 840).</summary>
    private static long Now => (long)(Time.GetTicksMsec() * 840 / 1000);

    public void UseContent(ContentPackage content, AnimationSheets sheets, ShipDescriptions ships, PlanetTypes planets, Names names,
        StarMap map)
    {
        _content = content;
        _sheets = sheets;
        _ships = ships;
        _planets = planets;
        _names = names;
        _map = map;
        _main = new ColorMaps();
        _main.Set(BinaryTable.Load(content, "colortable.main")[0]);
        _strings = GameStrings.Load(content);
        foreach (var f in new[] { "starcon", "tiny", "micro" })
            _fonts[f] = UqmFont.Load(content, "font." + f);
    }

    private SpriteSheet Sheet(string id)
    {
        if (!_loaded.TryGetValue(id, out var sheet))
            _loaded[id] = sheet = SpriteSheet.Load(_sheets!, id);
        return sheet;
    }

    private SpriteImage Frame(string sheet, int index)
    {
        if (!_frames.TryGetValue((sheet, index), out var image))
            _frames[(sheet, index)] = image = Sheet(sheet).Render(index, _main!);
        return image;
    }

    private UqmCanvas NewCanvas() => new(s => Sheet(s).Count, (s, i) =>
    {
        var f = Frame(s, i);
        return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
    }, _fonts.ToDictionary(f => f.Key, f => f.Value.Leading));

    private bool HasContent => _content is not null;

    /// <summary>
    /// The player's meeting at its encounter screen: drawn once a meeting
    /// (and again for another). False if it can't be (no content, or a
    /// race or world this client can't draw).
    /// </summary>
    public bool ShowEncounter(MeetingView meeting)
    {
        if (!HasContent)
            return false;
        if (_drawnMeeting is { } d && d.Id == meeting.Id && d.Well == meeting.Well && _texture is not null)
            return true;
        try
        {
            if (meeting.Ships.Count == 0)
                throw new ArgumentException("a meeting with no ships");
            string Icons(string id) => _ships![id].Graphics.Icons ?? throw new KeyNotFoundException($"{id} has no icons");
            var shipIcons = meeting.Infinite
                ? Enumerable.Repeat(Icons(meeting.Ships[0].Ship), EncounterScreen.MostShips).ToList()
                : meeting.Ships.Select(s => Icons(s.Ship)).ToList();
            string well = (WorldTypes.IsShielded(meeting.Well) ? _planets!.ShieldedBattleSprites
                : _planets![WorldTypes.PlanetType(meeting.Well)].BattleSprites)[0];
            string? cluster = null, world = "";
            if (meeting.Star is int s)
            {
                var star = _map!.Stars[s];
                cluster = _names!.Cluster(star);
                world = meeting.Planet is int p ? _names.PlanetOrMoon(star, p, meeting.Moon)
                    : meeting.Named is int n ? _names.PlanetName(star, n) : "";
            }
            var canvas = NewCanvas();
            EncounterScreen.Draw(canvas, i => _strings![i], cluster, world, well, shipIcons);
            _raster = new ScreenRaster(Frame, f => _fonts[f]);
            _raster.Paint(canvas.Drawn);
            _changed = true;
            _drawnMeeting = meeting;
            _drawnTally = null;
            QueueRedraw();
            return true;
        }
        catch (Exception e) when (e is KeyNotFoundException or ContentPackageException or IndexOutOfRangeException
                                      or ArgumentException or InvalidOperationException)
        {
            GD.PrintErr($"The encounter screen couldn't be drawn: {e.Message}");
            _drawnMeeting = null;
            return false;
        }
    }

    /// <summary>
    /// The battle-won screen for the session's tally, on the client's clock:
    /// begun as a tally comes, run on with the keys
    /// (<paramref name="held"/>: any key down; <paramref name="escape"/>:
    /// Escape), and the session's tally closed at its end. False if it
    /// can't be drawn (the tally's closed then too).
    /// </summary>
    public bool ShowTally(PlayerSession session, bool held, bool escape)
    {
        if (session.Tally is not { } tally)
            return false;
        if (!HasContent)
        {
            session.CloseTally();
            return false;
        }
        try
        {
            if (!ReferenceEquals(tally, _drawnTally))
            {
                var ship = _ships![tally.Enemy];
                string icons = ship.Graphics.Icons ?? throw new KeyNotFoundException($"{ship.Id} has no icons");
                _wonCanvas = NewCanvas();
                _wonCanvas.SetContext(_wonCanvas.Space);
                _wonCanvas.SetForeground(new Rgb(0, 0, 0));
                _wonCanvas.FillRect(new URect(0, 0, Window.W, Window.H));   // DrawSISFrame's black window
                string shipName = session.Me is { } me ? ShipNames.Ship(me, _strings!) : "";
                _won = new BattleWonScreen(_wonCanvas, i => _strings![i], shipName, RaceName(ship), icons, tally.Destroyed,
                    tally.Bounty, tally.Salvage, Now);
                _raster = new ScreenRaster(Frame, f => _fonts[f]);
                _wonPainted = 0;
                _drawnTally = tally;
                _drawnMeeting = null;
            }
            bool done = _won!.Advance(Now, held, escape);
            var drawn = _wonCanvas!.Drawn;
            if (drawn.Count > _wonPainted)
            {
                _raster!.Paint(drawn.Skip(_wonPainted));
                _wonPainted = drawn.Count;
                _changed = true;
                QueueRedraw();
            }
            if (done)
                session.CloseTally();
            return true;
        }
        catch (Exception e) when (e is KeyNotFoundException or ContentPackageException or IndexOutOfRangeException
                                      or ArgumentException or InvalidOperationException)
        {
            GD.PrintErr($"The battle-won screen couldn't be drawn: {e.Message}");
            session.CloseTally();
            return false;
        }
    }

    // The race's name: its race strings' first (FragPtr->race_strings).
    private string RaceName(ShipDescription ship)
    {
        if (!_raceStrings.TryGetValue(ship.Id, out var table))
            _raceStrings[ship.Id] = table = ship.RaceStrings is { } id ? StringTable.Load(_content!, id) : null;
        return table is { Count: > 0 } ? table[0] : "";
    }

    public override void _Draw()
    {
        if (_raster is null)
            return;
        if (_changed || _texture is null)
        {
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            _changed = false;
        }
        DrawTextureRectRegion(_texture, new Rect2(Vector2.Zero, new Vector2(Window.W, Window.H) * Px),
            new Rect2(Window.X, Window.Y, Window.W, Window.H));
    }
}
