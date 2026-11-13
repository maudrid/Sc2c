// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/process.c: PreProcessQueue's
// camera for two ships, CalcView, RedrawQueue's drawing; src/uqm/status.c's
// slots), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Combat;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>
/// One player's camera on a battle (stage 4a item 12, decision 5): UQM's
/// camera between the player's ship and the enemy, zooming to keep both in
/// view. The battle's own camera (the host's moving frame) follows side 0's
/// first ship still fighting; its player takes it as it is
/// (<see cref="BattleFighterSnapshot.Followed"/>), so their view is UQM's
/// exactly. Every other player's view keeps its own offset from the
/// battle's frame, moved each frame as <c>PreProcessQueue</c> and
/// <c>CalcView</c> move UQM's for two ships: the elements as sent, plus the
/// offset, are where this camera's frame has them.
/// </summary>
public sealed class BattleView
{
    private int? _battle;
    private bool _own;
    private BattleCamera _cam;

    /// <summary>The camera the frame is drawn from: its zoom and corner, and the scroll the stars move by.</summary>
    public BattleCamera Camera => _cam;

    /// <summary>What this camera's frame adds to the battle's places (world units, within the arena's size).</summary>
    public int OffsetX { get; private set; }

    /// <inheritdoc cref="OffsetX"/>
    public int OffsetY { get; private set; }

    /// <summary>Whether the camera is the player's own rather than the battle's.</summary>
    public bool Own => _own;

    /// <summary>The frame's camera for <paramref name="viewer"/> in <paramref name="b"/>, after its last.</summary>
    public void Follow(BattleSnapshot b, string viewer)
    {
        bool fresh = _battle != b.Id;
        _battle = b.Id;
        var me = b.FighterOf(viewer);
        var host = b.Camera;
        if (me is null || me.Followed)
        {
            // The battle's camera: from its own, the frame as the host's (a jump: the stars placed afresh).
            _cam = _own && !fresh ? host with { ViewState = 2 } : host;
            _own = false;
            OffsetX = OffsetY = 0;
            return;
        }
        if (fresh || !_own)
        {   // From the battle's camera as it is: the same zoom and frame.
            _cam = host with { ViewState = 2 };
            _own = true;
            OffsetX = OffsetY = 0;
            if (fresh)
                return;
        }
        var zoom = host.Step ? BattleZoom.Step : BattleZoom.Smooth;
        int w = b.Width, h = b.Height;
        int minimum = host.Step ? 0 : 1 << BattleCamera.ZoomShift;
        int zoomOut = _cam.ZoomOut;

        // PreProcessQueue with two ships, the player's and the enemy's (a gone one not followed).
        var ships = new List<(int X, int Y)>();
        if (!me.Status.Gone)
            ships.Add((Wrap(me.X + OffsetX, w), Wrap(me.Y + OffsetY, h)));
        if (!b.Enemy.Gone)
            ships.Add((Wrap(b.EnemyX + OffsetX, w), Wrap(b.EnemyY + OffsetY, h)));
        int originX = w >> 1, originY = h >> 1;
        int reduction = zoomOut;
        for (int i = 0; i < ships.Count; ++i)
        {
            if (i == 0)
                (originX, originY) = (Battle.DisplayAlign(ships[0].X), Battle.DisplayAlign(ships[0].Y));
            int dx = Battle.WrapDelta(Battle.DisplayAlign(ships[i].X) - originX, w);
            int dy = Battle.WrapDelta(Battle.DisplayAlign(ships[i].Y) - originY, h);
            originX = Battle.DisplayAlign(originX + (dx >> 1));
            originY = Battle.DisplayAlign(originY + (dy >> 1));
            reduction = Battle.CalcReduction(zoom, zoomOut, (short)Math.Abs(dx), (short)Math.Abs(dy));
        }
        if (ships.Count == 0 && reduction < minimum)
            reduction = minimum;

        // CalcView: the scroll that centres the camera, and the zoom.
        int sx = Battle.WrapDelta((w >> 1) - originX, w), sy = Battle.WrapDelta((h >> 1) - originY, h);
        if (ships.Count == 1)
        {
            sx = Math.Clamp(sx, -Core.Uqm.Units.OrgJumpX, Core.Uqm.Units.OrgJumpX);
            sy = Math.Clamp(sy, -Core.Uqm.Units.OrgJumpY, Core.Uqm.Units.OrgJumpY);
        }
        if (ships.Count == 0)
            sx = sy = 0;
        int view;
        short orgX = (short)_cam.OrgX, orgY = (short)_cam.OrgY;
        if (zoomOut == reduction)
            view = sx == 0 && sy == 0 && host.ScrollX == 0 && host.ScrollY == 0 ? 0 : 1;
        else
        {
            reduction = Battle.ZoomTo(zoom, zoomOut, reduction, ships.Count);
            (orgX, orgY) = Battle.SpaceOrg(zoom, new ArenaSize(w, h), reduction);
            zoomOut = reduction;
            view = 2;
        }
        OffsetX = Wrap(OffsetX + sx, w);
        OffsetY = Wrap(OffsetY + sy, h);
        // The stars move with the battle's frame and this camera's on it.
        _cam = new BattleCamera(view, (short)(host.ScrollX + sx), (short)(host.ScrollY + sy), zoomOut, orgX, orgY, host.Step);
    }

    private static int Wrap(int v, int size) => ((v % size) + size) % size;

    /// <summary>An element of the battle in this camera's frame (a line's end kept its distance from its start, as UQM's).</summary>
    public BattleElementSnapshot InFrame(BattleElementSnapshot e, int width, int height)
    {
        if (OffsetX == 0 && OffsetY == 0)
            return e;
        short x = (short)Wrap(e.X + OffsetX, width), y = (short)Wrap(e.Y + OffsetY, height);
        return e with { X = x, Y = y, X2 = (short)(x + (e.X2 - e.X)), Y2 = (short)(y + (e.Y2 - e.Y)) };
    }
}

/// <summary>
/// A battle drawn for one player as UQM draws it (stage 4a item 12): the
/// space window each frame (the stars' three planes, then every element at
/// the zoom's size, from <see cref="BattleView"/>'s camera), the status
/// panel's two slots (the enemy's above, the player's own below, drawn
/// incrementally as UQM's: <see cref="Status"/>'s calls are only what
/// changed), and the arena's minimap over the space window's corner (this
/// port's: UQM's arena is never bigger than its widest view).
/// </summary>
public sealed class BattleScreen
{
    /// <summary>The space window: the screen's left, beside the status panel.</summary>
    public static readonly URect SpaceClip = new(0, 0, UqmCanvas.SpaceWidth, UqmCanvas.SpaceHeight);

    public const int MinimapWidth = 48, MinimapMargin = 4;

    private readonly UqmCanvas _canvas;
    private readonly BattleStatus _status;
    private readonly Func<BattleShipStatus, string?, StatusShip> _statusShip;
    private readonly BattleView _view = new();
    private BattleGalaxy? _galaxy;
    private int? _battle;
    private bool _enemyShown, _mineShown;

    /// <param name="canvas">Where the status panel's calls go (<see cref="Status"/>).</param>
    /// <param name="statusShip">A ship's look in the panel from its status (and its player, null for the enemy): its icons, captain and names.</param>
    public BattleScreen(UqmCanvas canvas, Func<BattleShipStatus, string?, StatusShip> statusShip)
    {
        _canvas = canvas;
        _status = new BattleStatus(canvas);
        _statusShip = statusShip;
    }

    public BattleView View => _view;

    /// <summary>The status panel's calls made by the last frame (the canvas's; whoever paints them clears it).</summary>
    public List<UqmDraw> Status => _canvas.Drawn;

    /// <summary>Whether the last frame was a new battle's first: the status panel is drawn afresh, the screen should be cleared first.</summary>
    public bool Fresh { get; private set; }

    /// <summary>
    /// A frame of <paramref name="b"/> as <paramref name="viewer"/> sees it:
    /// the status panel's changes added to <see cref="Status"/>, and the
    /// space window's calls returned, the window cleared first, the minimap
    /// last.
    /// </summary>
    /// <param name="sizeOf">A big sheet's id at a zoom's size (<see cref="BattleSpace.SizeOf"/>).</param>
    public List<UqmDraw> Frame(BattleSnapshot b, string viewer, Func<string, int, string> sizeOf)
    {
        Fresh = _battle != b.Id;
        if (Fresh)
        {
            _battle = b.Id;
            _galaxy = new BattleGalaxy(Battle.Galaxy(b.StarSeed));
            _enemyShown = _mineShown = false;
        }
        _view.Follow(b, viewer);
        var cam = _view.Camera;
        _galaxy!.Move(cam);
        var prims = _galaxy.Prims
            .Concat(b.Elements.Select(e => BattleSpace.Of(_view.InFrame(e, b.Width, b.Height), cam, sizeOf)));
        var drawn = new List<UqmDraw> { new(UqmDrawKind.Clear, 0, 0, SpaceClip.W, SpaceClip.H, default, Clip: SpaceClip) };
        drawn.AddRange(prims.Select(p => Draw(p, cam.Scale)));
        StatusFrame(b, viewer);
        Minimap(b, viewer, drawn);
        return drawn;
    }

    private void StatusFrame(BattleSnapshot b, string viewer)
    {
        StatusState State(BattleShipStatus s) => new(s.Crew, s.Energy, s.Flags, s.ElementCrew, s.LifeSpan, s.Gone);
        if (!_enemyShown)
        {
            _status.Init(BattleStatus.EnemySlot, _statusShip(b.Enemy, null), State(b.Enemy));
            _enemyShown = true;
        }
        else
            _status.Update(BattleStatus.EnemySlot, State(b.Enemy));
        if (b.FighterOf(viewer) is not { } me)
            return;
        if (!_mineShown)
        {
            _status.Init(BattleStatus.PlayerSlot, _statusShip(me.Status, viewer), State(me.Status));
            _mineShown = true;
        }
        else
            _status.Update(BattleStatus.PlayerSlot, State(me.Status));
    }

    /// <summary>A colour as an element keeps it (RGB15) on the screen (each 5 bits to 8, as UQM's).</summary>
    public static Rgb FromRgb15(int c)
    {
        static byte C(int v) => (byte)((v << 3) | (v >> 2));
        return new Rgb(C((c >> 10) & 31), C((c >> 5) & 31), C(c & 31));
    }

    // DrawBatch's call for a primitive: in the space window, at the zoom's graphic scale.
    private static UqmDraw Draw(BattlePrim p, int scale) => p.Type switch
    {
        PrimType.Stamp => new UqmDraw(UqmDrawKind.Stamp, p.X, p.Y, Sheet: p.Sheet, Frame: p.Frame, Clip: SpaceClip, Scale: scale),
        PrimType.StampFill => new UqmDraw(UqmDrawKind.FilledStamp, p.X, p.Y, Color: FromRgb15(p.Color), Sheet: p.Sheet, Frame: p.Frame,
            Clip: SpaceClip, Scale: scale),
        PrimType.Line => new UqmDraw(UqmDrawKind.Line, p.X, p.Y, p.X2, p.Y2, FromRgb15(p.Color), Clip: SpaceClip),
        _ => new UqmDraw(UqmDrawKind.Point, p.X, p.Y, Color: FromRgb15(p.Color), Clip: SpaceClip),
    };

    private static readonly Rgb MapFrame = new(0x52, 0x52, 0x52), MapBack = new(0, 0, 0), Mine = new(0x00, 0xFF, 0x00),
        Teammate = new(0x52, 0x52, 0xFF), Enemy = new(0xFF, 0x00, 0x00), Wreck = new(0x84, 0x84, 0x84);

    /// <summary>The minimap's box, in the space window's bottom right: the arena, in its proportions.</summary>
    public static URect MinimapRect(int width, int height)
    {
        int h = Math.Max(1, MinimapWidth * height / width);
        return new URect(SpaceClip.W - MinimapWidth - MinimapMargin - 2, SpaceClip.H - h - MinimapMargin - 2, MinimapWidth + 2, h + 2);
    }

    // The arena with every ship as a dot, centred on the player's own (on the enemy without one).
    private void Minimap(BattleSnapshot b, string viewer, List<UqmDraw> drawn)
    {
        var box = MinimapRect(b.Width, b.Height);
        drawn.Add(new UqmDraw(UqmDrawKind.Rect, box.X, box.Y, box.W, box.H, MapBack, Clip: SpaceClip));
        drawn.Add(new UqmDraw(UqmDrawKind.Outline, box.X, box.Y, box.W, box.H, MapFrame, Clip: SpaceClip));
        var me = b.FighterOf(viewer);
        int cx = me?.X ?? b.EnemyX, cy = me?.Y ?? b.EnemyY;
        int iw = box.W - 2, ih = box.H - 2;
        void Dot(int x, int y, Rgb c)
        {
            int mx = box.X + 1 + iw / 2 + BattleSnapshot.Wrap(x - cx, b.Width) * iw / b.Width;
            int my = box.Y + 1 + ih / 2 + BattleSnapshot.Wrap(y - cy, b.Height) * ih / b.Height;
            mx = Math.Clamp(mx, box.X + 1, box.X + box.W - 3);
            my = Math.Clamp(my, box.Y + 1, box.Y + box.H - 3);
            drawn.Add(new UqmDraw(UqmDrawKind.Rect, mx, my, 2, 2, c, Clip: SpaceClip));
        }
        foreach (var f in b.Fighters.Where(f => f.PlayerId != viewer))
            Dot(f.X, f.Y, f.Status.Gone || f.Status.Crew == 0 ? Wreck : Teammate);
        if (!b.Enemy.Gone)
            Dot(b.EnemyX, b.EnemyY, Enemy);
        if (me is not null)
            Dot(me.X, me.Y, me.Status.Gone || me.Status.Crew == 0 ? Wreck : Mine);
    }
}
