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
        bool step = recorded.GetProperty("input").TryGetProperty("zoom", out var z) && z.GetString() == "step";
        int frames = 0;
        Replay(recorded, (frame, battle, own) =>
        {
            frames = frame;
            if (frame == 0)
                return;   // nothing drawn before the first frame
            var cam = new BattleCamera(battle.ViewState, battle.ScrollX, battle.ScrollY, battle.ZoomOut, battle.SpaceOrgX, battle.SpaceOrgY, step);
            galaxy.Move(cam);
            var ours = galaxy.Prims
                .Concat(battle.List.Elements.Where(e => e.Prim != PrimType.None)
                    .Select(e => BattleSpace.Of(World.GameWorld.ElementSnapshot(e), cam, BattleSpace.SizeOf)))
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

    // The status area's pixels that differ.
    private static List<(int X, int Y)> Diff(ScreenRaster a, ScreenRaster b)
    {
        var out_ = new List<(int, int)>();
        for (int y = 0; y < ScreenRaster.Height; ++y)
            for (int x = UqmCanvas.SpaceWidth; x < ScreenRaster.Width; ++x)
            {
                int o = (y * ScreenRaster.Width + x) * 4;
                if (a.Rgba[o] != b.Rgba[o] || a.Rgba[o + 1] != b.Rgba[o + 1] || a.Rgba[o + 2] != b.Rgba[o + 2])
                    out_.Add((x, y));
            }
        return out_;
    }
}
