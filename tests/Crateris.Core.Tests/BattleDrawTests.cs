// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Xunit;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle drawn as UQM draws it (docs/plans/stage-4.md, item 12),
/// against tests/golden/battle-draw.json: its fights replayed through the
/// port's engine, each frame's status panel (<see cref="BattleStatus"/>)
/// and space drawn and compared with what UQM
/// drew.
/// </summary>
[Trait("Topic", "drawing")]
public class BattleDrawTests
{
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle-draw"));

    public static IEnumerable<object[]> Battles() => BattleReplay.Names("battle-draw");

    private static readonly Lazy<Pictures> Pics = new(() => TestContent.With(p => new Pictures(p)));

    private sealed class Pictures
    {
        private readonly AnimationSheets _sheets;
        private readonly ColorMaps _main;
        private readonly Dictionary<string, UqmFont> _fonts;
        private readonly ConcurrentDictionary<(string, int), SpriteImage> _frames = new();
        private readonly ConcurrentDictionary<string, SpriteSheet> _loaded = new();

        public Pictures(ContentPackage p)
        {
            _sheets = new AnimationSheets(p);
            _main = new ColorMaps();
            _main.Set(BinaryTable.Load(p, "colortable.main")[0]);
            _fonts = new() { ["starcon"] = UqmFont.Load(p, "font.starcon"), ["tiny"] = UqmFont.Load(p, "font.tiny") };
        }

        private SpriteSheet Sheet(string s) => _loaded.GetOrAdd(s, id => SpriteSheet.Load(_sheets, id));

        public int Count(string sheet) => Sheet(sheet).Count;

        public SpriteImage Frame(string sheet, int i) => _frames.GetOrAdd((sheet, i), _ => Sheet(sheet).Render(i, _main));

        public ScreenRaster Raster() => new(Frame, f => _fonts[f]);
    }

    private static readonly Dictionary<string, int> Leading = new() { ["starcon"] = 11, ["tiny"] = 10, ["micro"] = 15 };

    // A recorded status call as the port's canvas would make it, in the screen's coordinates.
    private static UqmDraw StatusCall(JsonElement e, IReadOnlyList<string> sheets)
    {
        var a = e.EnumerateArray().ToArray();
        int I(int k) => a[k].GetInt32();
        Rgb C(int k) => new((byte)I(k), (byte)I(k + 1), (byte)I(k + 2));
        const int x0 = UqmCanvas.SpaceWidth;   // StatusContext's corner, and its clip
        var clip = new URect(x0, 0, UqmCanvas.StatusWidth, UqmCanvas.StatusHeight);
        return (a[0].GetString() switch
        {
            "s" => new UqmDraw(UqmDrawKind.Stamp, x0 + I(3), I(4), Sheet: sheets[I(1)], Frame: I(2)),
            "f" => new UqmDraw(UqmDrawKind.FilledStamp, x0 + I(3), I(4), Color: C(5), Sheet: sheets[I(1)], Frame: I(2)),
            "r" => new UqmDraw(UqmDrawKind.Rect, x0 + I(1), I(2), I(3), I(4), C(5)),
            "o" => new UqmDraw(UqmDrawKind.Outline, x0 + I(1), I(2), I(3), I(4), C(5)),
            "t" => new UqmDraw(UqmDrawKind.Text, x0 + I(2), I(3), Color: C(6), Font: a[1].GetString()!, Align: (TextAlign)I(4),
                Text: a[5].GetString()!),
            var k => throw new InvalidDataException($"a status call {k}"),
        }) with { Clip = clip };
    }

    /// <summary>The recorded battle replayed through the port's engine, <paramref name="frame"/> called after each frame (and the first, before any).</summary>
    private static void Replay(JsonElement recorded, Action<int, Battle, IReadOnlyList<Starship>> frame)
    {
        var input = recorded.GetProperty("input");
        var ships = input.GetProperty("ships").EnumerateArray().ToList();
        var fleets = ships.Select((s, side) =>
        {
            var (code, crew) = ShipBattleTests.Ship(s);
            return (IReadOnlyList<Starship>)new[] { new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) } };
        }).ToList();
        string planet = input.GetProperty("planet").GetString()!;
        var arena = BattleReplay.Sheets.Value.Arena(new[] { $"{planet}.large", $"{planet}.medium", $"{planet}.small" });
        var zoom = input.TryGetProperty("zoom", out var z) && z.GetString() == "step" ? BattleZoom.Step : BattleZoom.Smooth;
        var battle = new Battle(arena, fleets, input.GetProperty("seed").GetUInt32(), zoom, false, new List<string>());
        var own = fleets.Select(f => f[0]).ToList();
        frame(0, battle, own);
        int n = 0;
        foreach (var step in input.GetProperty("steps").EnumerateArray().Select(s => s.Ints()))
            for (int i = 0; i < step[0] && battle.InBattle; ++i)
            {
                battle.Step(ship => (BattleInput)step[1 + ship.PlayerNr]);
                frame(++n, battle, own);
            }
    }

    private static StatusShip StatusOf(Starship s)
    {
        var d = BattleReplay.Ships[s.PlayerNr == 0 ? ShipDescriptions.Flagship : ShipDescriptions.Ilwrath];
        var r = s.RaceDesc!;
        bool sis = s.PlayerNr == 0 && s.CaptainsNameIndex == 0;
        return new StatusShip(d.Graphics.Icons, d.Graphics.Captain, r.MaxCrew, r.MaxEnergy,
            (r.ShipFlags & ShipFlags.CrewImmune) != 0, (r.ShipFlags & ShipFlags.PlayerCaptain) != 0, sis,
            sis ? "Vindicator" : "string 1", sis ? "Zelnick" : $"string {s.CaptainsNameIndex}");
    }

    private static StatusState StateOf(Starship s) =>
        new(s.RaceDesc!.CrewLevel, s.RaceDesc.EnergyLevel, s.CurStatusFlags, s.Ship?.CrewLevel ?? 0, s.Ship?.LifeSpan ?? 0,
            s.Ship is null);

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheStatusPanelIsUqms(string name)
    {
        var recorded = BattleReplay.Recorded(Reference.Value, name);
        var sheets = Reference.Value.GetProperty("sheets").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = recorded.GetProperty("drawn").EnumerateArray().ToList();
        var pics = Pics.Value;
        var canvas = new UqmCanvas(pics.Count, (_, _) => default, Leading);
        var status = new BattleStatus(canvas);
        var uqm = pics.Raster();
        var port = pics.Raster();
        var shown = new HashSet<int>();
        int frames = 0;
        Replay(recorded, (frame, battle, own) =>
        {
            Assert.True(frame < drawn.Count, $"{name}: the port's battle runs past UQM's {drawn.Count} frames");
            canvas.Drawn.Clear();
            // ship_preprocess starts a slot on its ship's first frame (APPEARING), after the spawn's.
            foreach (var s in own.Where(s => frame > 0 && s.RaceDesc is not null))
            {
                int slot = s.PlayerNr == 0 ? BattleStatus.PlayerSlot : BattleStatus.EnemySlot;
                if (shown.Add(slot))
                    status.Init(slot, StatusOf(s), StateOf(s));
                else
                    status.Update(slot, StateOf(s));
            }
            var theirs = drawn[frame][0].EnumerateArray().Select(c => StatusCall(c, sheets)).ToList();
            uqm.Paint(theirs);
            port.Paint(canvas.Drawn);
            var diff = Diff(uqm, port);
            Assert.True(diff.Count == 0, $"{name} frame {frame}: {diff.Count} status pixels differ, first at {diff.FirstOrDefault()}; "
                + $"UQM drew {string.Join(" ", drawn[frame][0].EnumerateArray().Take(6))}, the port {string.Join(" ", canvas.Drawn.Take(6))}");
            frames = frame;
        });
        Assert.Equal(drawn.Count - 1, frames);
    }

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheSpaceIsUqms(string name)
    {
        var recorded = BattleReplay.Recorded(Reference.Value, name);
        var sheets = Reference.Value.GetProperty("sheets").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = recorded.GetProperty("drawn").EnumerateArray().ToList();
        var stars = recorded.GetProperty("stars").EnumerateArray().Select(p => ((short)p[0].GetInt32(), (short)p[1].GetInt32())).ToList();
        var galaxy = new BattleGalaxy(stars);
        (int X, int Y) scrolled = (0, 0);
        bool step = recorded.GetProperty("input").TryGetProperty("zoom", out var z) && z.GetString() == "step";
        int frames = 0;
        Replay(recorded, (frame, battle, own) =>
        {
            frames = frame;
            if (frame == 0)
            {   // nothing drawn before the first frame; the stars come from the seed sent
                Assert.Equal(stars, Battle.Galaxy(battle.StarSeed));
                return;
            }
            var cam = battle.Camera;
            scrolled = (scrolled.X + battle.ScrollX, scrolled.Y + battle.ScrollY);
            Assert.Equal(new BattleCamera(battle.ViewState, battle.ScrollX, battle.ScrollY, battle.ZoomOut, battle.SpaceOrgX, battle.SpaceOrgY, step,
                frame, scrolled.X, scrolled.Y), cam);
            galaxy.Move(cam);
            // A screen that missed frames places the stars afresh from the scroll so far: UQM's places again.
            if (frame % 37 == 0)
            {
                var placed = new BattleGalaxy(Battle.Galaxy(battle.StarSeed));
                placed.Place(cam);
                Assert.Equal(galaxy.Prims, placed.Prims);
            }
            var ours = galaxy.Prims
                .Concat(battle.List.Elements.Where(e => e.Prim != PrimType.None)
                    .Select(e => BattleSpace.Of(GameWorld.ElementSnapshot(e), cam, BattleSpace.SizeOf)))
                .ToList();
            var theirs = drawn[frame][1].EnumerateArray().ToList();
            int scale = theirs[^1].GetInt32();
            var prims = theirs.Take(theirs.Count - 1).Select(p =>
            {
                var a = p.Ints();
                return new BattlePrim((PrimType)a[0], a[1], a[2], a[3], a[4], a[5] < 0 ? "" : sheets[a[5]], a[6], a[7]);
            }).ToList();
            // A stamp's colour isn't drawn: only a filled stamp's, a point's and a line's.
            static BattlePrim Drawn(BattlePrim p) => p.Type == PrimType.Stamp ? p with { Color = 0 } : p;
            Assert.Equal(scale, cam.Scale);
            for (int i = 0; i < Math.Max(prims.Count, ours.Count); ++i)
                Assert.True(i < prims.Count && i < ours.Count && Drawn(prims[i]) == Drawn(ours[i]),
                    $"{name} frame {frame} primitive {i}: UQM {(i < prims.Count ? prims[i] : null)}, the port {(i < ours.Count ? ours[i] : null)}");
        });
        Assert.Equal(drawn.Count - 1, frames);
    }

    // The frame as the host sends it to the player whose ship the camera follows (side 0's, "p").
    private static BattleSnapshot Snapshot(Battle battle, IReadOnlyList<Starship> own)
    {
        static BattleShipStatus Status(string ship, Starship s) => s.RaceDesc is { } r
            ? new(ship, r.CrewLevel, r.MaxCrew, r.EnergyLevel, r.MaxEnergy, s.CaptainsNameIndex, s.CurStatusFlags,
                s.Ship?.CrewLevel ?? 0, s.Ship?.LifeSpan ?? 0, s.Ship is null)
            : new(ship, s.CrewLevel, s.MaxCrew, 0, 0, s.CaptainsNameIndex, s.CurStatusFlags, Gone: s.Ship is null);
        var me = own[0];
        var fighter = new BattleFighterSnapshot("p", Status(ShipDescriptions.Flagship, me), me.Ship?.Current.X ?? 0, me.Ship?.Current.Y ?? 0,
            true);   // the one player: their view is the battle's camera
        var enemy = own[1];
        return new BattleSnapshot(1, battle.Size.Width, battle.Size.Height, Status(ShipDescriptions.Ilwrath, enemy),
            enemy.Ship?.Current.X ?? 0, enemy.Ship?.Current.Y ?? 0, new[] { fighter },
            battle.List.Elements.Where(e => e.Prim != PrimType.None).Select(GameWorld.ElementSnapshot).ToList(),
            battle.Camera, battle.StarSeed);
    }

    // A recorded space primitive as the screen draws it.
    private static UqmDraw SpaceCall(int[] a, IReadOnlyList<string> sheets, int scale)
    {
        var clip = BattleScreen.SpaceClip;
        var c = a[7] < 0 ? default : BattleScreen.FromRgb15(a[7]);
        return (PrimType)a[0] switch
        {
            PrimType.Stamp => new UqmDraw(UqmDrawKind.Stamp, a[1], a[2], Sheet: sheets[a[5]], Frame: a[6], Clip: clip, Scale: scale),
            PrimType.StampFill => new UqmDraw(UqmDrawKind.FilledStamp, a[1], a[2], Color: c, Sheet: sheets[a[5]], Frame: a[6], Clip: clip,
                Scale: scale),
            PrimType.Line => new UqmDraw(UqmDrawKind.Line, a[1], a[2], a[3], a[4], c, Clip: clip),
            _ => new UqmDraw(UqmDrawKind.Point, a[1], a[2], Color: c, Clip: clip),
        };
    }

    /// <summary>
    /// The whole screen from what the host sends: the followed player's
    /// battle snapshot each frame through <see cref="BattleScreen"/>, its
    /// space window's calls UQM's DrawBatch's (after the clear, before the
    /// minimap) and its status panel UQM's, pixel for pixel.
    /// </summary>
    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheScreenFromTheSnapshotIsUqms(string name)
    {
        var recorded = BattleReplay.Recorded(Reference.Value, name);
        var sheets = Reference.Value.GetProperty("sheets").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = recorded.GetProperty("drawn").EnumerateArray().ToList();
        var pics = Pics.Value;
        var canvas = new UqmCanvas(pics.Count, (_, _) => default, Leading);
        Starship? player = null, enemy = null;
        var screen = new BattleScreen(canvas, (s, id) => StatusOf(id is null ? enemy! : player!));
        // Screens sent only every third frame, from each of the three first frames (the others lost).
        var skippers = Enumerable.Range(0, 3)
            .Select(_ => new BattleScreen(new UqmCanvas(pics.Count, (_, _) => default, Leading), (s, id) => StatusOf(id is null ? enemy! : player!)))
            .ToList();
        var skippedRasters = Enumerable.Range(0, 3).Select(_ => pics.Raster()).ToList();
        var uqmSeen = new byte[3][];   // UQM's status panel as each saw it last
        var uqm = pics.Raster();
        var port = pics.Raster();
        int frames = 0;
        Replay(recorded, (frame, battle, own) =>
        {
            frames = frame;
            if (frame == 0)
                return;
            (player, enemy) = (own[0], own[1]);
            canvas.Drawn.Clear();
            var snapshot = Snapshot(battle, own);
            var space = screen.Frame(snapshot, "p", BattleSpace.SizeOf)!;
            Assert.Null(screen.Frame(snapshot, "p", BattleSpace.SizeOf));   // the same frame again (the host's pause): nothing new
            // A screen sent only every third frame (the others lost) draws the stars and elements as UQM's too.
            var skipper = skippers[frame % 3];
            var skipping = skipper.Frame(snapshot, "p", BattleSpace.SizeOf);
            skippedRasters[frame % 3].Paint(skipper.Status);
            skipper.Status.Clear();
            Assert.True(screen.View.Camera == battle.Camera, $"{name} frame {frame}: the followed player's camera isn't the battle's: {screen.View.Camera} {battle.Camera}");
            var theirs = drawn[frame][1].EnumerateArray().ToList();
            int scale = theirs[^1].GetInt32();
            var expected = theirs.Take(theirs.Count - 1).Select(p => SpaceCall(p.Ints(), sheets, scale)).ToList();
            static UqmDraw Drawn(UqmDraw d) => d.Kind == UqmDrawKind.Stamp ? d with { Color = default } : d;
            var ours = space.Skip(1).Take(expected.Count).ToList();
            if (skipping is not null)
            {
                var sk = skipping.Skip(1).Take(expected.Count).ToList();
                // The stars within a pixel (UQM leaves a plane's places as they were while its first star stays put on
                // the screen, CmpMovePoints, though another may have moved a pixel); the elements exactly.
                for (int i = 0; i < ours.Count; ++i)
                    Assert.True(i < BattleGalaxy.NumStars
                            ? Drawn(ours[i]) with { X = 0, Y = 0 } == Drawn(sk[i]) with { X = 0, Y = 0 }
                              && Math.Abs(ours[i].X - sk[i].X) <= 1 && Math.Abs(ours[i].Y - sk[i].Y) <= 1
                            : Drawn(ours[i]) == Drawn(sk[i]),
                        $"{name} frame {frame} call {i}: every frame {ours[i]}, every third {sk[i]}");
            }
            for (int i = 0; i < expected.Count; ++i)
                Assert.True(i < ours.Count && Drawn(ours[i]) == Drawn(expected[i]),
                    $"{name} frame {frame} call {i}: UQM {expected[i]}, the screen {(i < ours.Count ? ours[i] : null)}");
            uqm.Paint(drawn[frame][0].EnumerateArray().Select(c => StatusCall(c, sheets)));
            port.Paint(screen.Status);
            uqmSeen[frame % 3] = (byte[])uqm.Rgba.Clone();
            var diff = Diff(uqm, port);
            Assert.True(diff.Count == 0, $"{name} frame {frame}: {diff.Count} status pixels differ, first at {diff.FirstOrDefault()}");
        });
        Assert.Equal(drawn.Count - 1, frames);
        // Each screen sent every third frame ends with UQM's status panel as at the last frame it saw (an explosion's flash among it: each
        // step missed drawn as the next is seen).
        for (int phase = 0; phase < 3; ++phase)
        {
            var missed = Diff(uqmSeen[phase], skippedRasters[phase].Rgba);
            Assert.True(missed.Count == 0,
                $"{name}: sent every third frame from {phase}, {missed.Count} status pixels differ at the end, first at {missed.FirstOrDefault()}");
        }
    }

    /// <summary>
    /// A ship's explosion flashing in its captain's window, each step drawn
    /// on the last: a panel that missed frames during it (the end too, its
    /// slot stopping as the life starts again) ends as one that saw them all.
    /// </summary>
    [ContentTheory]
    [InlineData(new[] { 35, 20 })]
    [InlineData(new[] { 35 })]
    [InlineData(new[] { 30, 29, 3 })]
    [InlineData(new int[0])]
    [InlineData(new[] { 25 })]
    [InlineData(new[] { 25, 24 }, false)]
    [InlineData(new[] { 29, 28 }, false)]
    [InlineData(new[] { 0 }, false)]
    [InlineData(new int[0], false)]
    public void TheExplosionsFlashIsWholeWhateverFramesAreMissed(int[] seen, bool outSeen = true)
    {
        var pics = Pics.Value;
        var d = BattleReplay.Ships[ShipDescriptions.Ilwrath];
        var ship = new StatusShip(d.Graphics.Icons, d.Graphics.Captain, 22, 16, false, false, false, "string 1", "string 5");
        ScreenRaster Run(IReadOnlyList<int> lives)
        {
            var canvas = new UqmCanvas(pics.Count, (_, _) => default, Leading);
            var status = new BattleStatus(canvas);
            status.Init(BattleStatus.EnemySlot, ship, new StatusState(22, 16, StatusFlags.None, 22, 1));
            if (lives != All || outSeen)   // its crew seen out (do_damage leaves its life 0), or that frame missed
                status.Update(BattleStatus.EnemySlot, new StatusState(0, 16, StatusFlags.None, 0, 0));
            foreach (int life in lives)
                status.Update(BattleStatus.EnemySlot, new StatusState(0, 16, StatusFlags.None, 0, life));
            status.Update(BattleStatus.EnemySlot, new StatusState(0, 16, StatusFlags.None, 0, 72));   // its life going again: the end
            var raster = pics.Raster();
            raster.Paint(canvas.Drawn);
            return raster;
        }
        var all = Run(All);
        var some = Run(seen);
        var diff = Diff(all, some);
        Assert.True(diff.Count == 0, $"seen {string.Join(",", seen)}{(outSeen ? "" : ", its crew running out not")}: "
            + $"{diff.Count} pixels differ, first at {diff.FirstOrDefault()}");
    }

    private static readonly int[] All = Enumerable.Range(0, 36).Reverse().ToArray();   // every life of an explosion

    // The status area's pixels that differ.
    private static List<(int X, int Y)> Diff(ScreenRaster a, ScreenRaster b) => Diff(a.Rgba, b.Rgba);

    private static List<(int X, int Y)> Diff(byte[] a, byte[] b)
    {
        var out_ = new List<(int, int)>();
        for (int y = 0; y < ScreenRaster.Height; ++y)
            for (int x = UqmCanvas.SpaceWidth; x < ScreenRaster.Width; ++x)
            {
                int o = (y * ScreenRaster.Width + x) * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2])
                    out_.Add((x, y));
            }
        return out_;
    }
}
