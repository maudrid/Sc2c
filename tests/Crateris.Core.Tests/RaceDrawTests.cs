// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// Every race ship's views drawn as UQM draws them (docs/plans/stage-4b.md,
/// item 10), against tests/golden/battle-races-draw.json: each race ship's
/// battle by keys (and the Pkunk's rebirth) replayed through the port's
/// engine (against battle-races-N.json's traces, as
/// <see cref="RaceShipBattleTests"/>), each frame sent as the host sends it
/// to the player the camera follows and drawn by <see cref="BattleScreen"/>:
/// the status panel's two slots UQM's pixel for pixel every frame (the race
/// ship's icons, captain's window and names; the Shofixti's Glory Device's
/// frames, the Orz's marines and the VUX's limpets on the windows, the
/// Shofixti's second ship and the Pkunk reborn drawn afresh), and the space
/// window's calls UQM's at the frames recorded.
/// </summary>
[Trait("Topic", "drawing")]
public class RaceDrawTests
{
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle-races-draw"));

    private static readonly Lazy<JsonElement>[] Parts = Enumerable.Range(1, RaceBattleTraceTests.Parts.Length)
        .Select(n => new Lazy<JsonElement>(() => Golden.Load($"battle-races-{n}"))).ToArray();

    public static IEnumerable<object[]> Battles() => BattleReplay.Names("battle-races-draw");

    // A ship's look in the panel, as the game's (BattleScreenView's) but with the recording's strings: the flagship by
    // its own names, a race ship by "string N" of its race's strings.
    private static StatusShip StatusOf(BattleShipStatus s, string? player)
    {
        var d = BattleReplay.Ships[s.Ship];
        bool sis = player is not null && s.Captain == 0;
        return new StatusShip(d.Graphics.Icons, d.Graphics.Captain, s.MaxCrew, s.MaxEnergy, (d.Flags & ShipFlags.CrewImmune) != 0,
            BattleScreen.PlayerCaptain(s.Ship, d.Flags), sis, sis ? "Vindicator" : "string 1", sis ? "Zelnick" : $"string {s.Captain}");
    }

    private static string Short(UqmDraw d) => d.Kind switch
    {
        UqmDrawKind.Stamp => $"[s {d.Sheet}:{d.Frame} {d.X - UqmCanvas.SpaceWidth},{d.Y}]",
        UqmDrawKind.FilledStamp => $"[f {d.Sheet}:{d.Frame} {d.X - UqmCanvas.SpaceWidth},{d.Y} {d.Color}]",
        UqmDrawKind.Text => $"[t {d.X - UqmCanvas.SpaceWidth},{d.Y} {d.Text}]",
        _ => $"[{d.Kind} {d.X - UqmCanvas.SpaceWidth},{d.Y} {d.Width}x{d.Height} {d.Color.R},{d.Color.G},{d.Color.B} clip {d.Clip}]",
    };

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheRaceShipsViewsAreUqms(string name)
    {
        var drawRecord = BattleReplay.Recorded(Reference.Value, name);
        int part = drawRecord.GetProperty("part").GetInt32();
        var sheets = drawRecord.GetProperty("sheets").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = drawRecord.GetProperty("drawn").EnumerateArray().ToList();
        var reference = Parts[part - 1].Value;
        var input = BattleReplay.Recorded(reference, name).GetProperty("input");
        int[] fleetSizes = input.TryGetProperty("fleet", out var fleet) ? fleet.Ints() : new[] { 1, 1 };
        var sis = new List<SisShip>();
        var shipIds = input.GetProperty("ships").EnumerateArray().Select(s => s.GetProperty("ship").GetString()!).ToArray();
        var fleets = input.GetProperty("ships").EnumerateArray().Select((s, side) =>
            (IReadOnlyList<Starship>)Enumerable.Range(0, fleetSizes[side]).Select(_ =>
            {
                var (code, crew) = ShipBattleTests.Ship(s);
                if (code is SisShip flagship)
                    sis.Add(flagship);
                var icons = RaceShips.Icons(BattleReplay.Ships[shipIds[side]], r => BattleReplay.Sheets.Value.Views(r));
                return new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5), Icons = icons };
            }).ToList()).ToList();
        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean() && !sis.Any(s => s.BombCarrier);

        var pics = BattleDrawTests.Pics.Value;
        var canvas = new UqmCanvas(pics.Count, (sheet, i) =>
        {
            var f = pics.Frame(sheet, i);
            return new URect(-f.HotSpotX, -f.HotSpotY, f.Width, f.Height);
        }, BattleDrawTests.Leading);
        var screen = new BattleScreen(canvas, StatusOf);
        var uqm = pics.Raster();
        var port = pics.Raster();
        int frame = 0, spaces = 0;
        var last = new Starship?[2];
        int centres = 0;
        BattleReplay.Run(reference, name, fleets, runaway, colours: true, eachFrame: battle =>
        {
            int n = frame++;
            if (n == 0)
                return;   // nothing drawn before the first frame
            Assert.True(n < drawn.Count, $"{name}: the port's battle runs past UQM's {drawn.Count} frames drawn");
            // Each side's ship in the battle, or the last one, gone (the world keeps a gone ship's status).
            (Starship? Ship, BattleShipStatus Status) Side(int side)
            {
                if (battle.Fleet(side).FirstOrDefault(s => s.RaceDesc != null) is { } s)
                    last[side] = s;
                return (last[side], GameWorld.StatusSnapshot(shipIds[side], last[side]!));
            }
            var (me, mine) = Side(0);
            var (enemy, theirs) = Side(1);
            var snapshot = new BattleSnapshot(1, battle.Size.Width, battle.Size.Height, theirs,
                enemy?.Ship?.Current.X ?? 0, enemy?.Ship?.Current.Y ?? 0,
                new[] { new BattleFighterSnapshot("p", mine, me?.Ship?.Current.X ?? 0, me?.Ship?.Current.Y ?? 0, true) },
                battle.List.Elements.Where(battle.Drawn).Select(GameWorld.ElementSnapshot).ToList(),
                battle.Camera, battle.StarSeed);
            canvas.Drawn.Clear();
            var space = screen.Frame(snapshot, "p", BattleSpace.SizeOf)!;
            if (drawn[n][1].ValueKind == JsonValueKind.Array)
            {
                ++spaces;
                var calls = drawn[n][1].EnumerateArray().ToList();
                int scale = calls[^1].GetInt32();
                var expected = calls.Take(calls.Count - 1).Select(p => BattleDrawTests.SpaceCall(p.Ints(), sheets, scale)).ToList();
                static UqmDraw Drawn(UqmDraw d) => d.Kind == UqmDrawKind.Stamp ? d with { Color = default } : d;
                var ours = space.Skip(1).Take(expected.Count).ToList();
                for (int i = 0; i < expected.Count; ++i)
                    Assert.True(i < ours.Count && Drawn(ours[i]) == Drawn(expected[i]),
                        $"{name} frame {n} call {i}: UQM {expected[i]}, the screen {(i < ours.Count ? ours[i] : null)}");
                // Nothing more in the space but the minimap after it (its box first).
                BattleDrawTests.MinimapFollows(space, expected.Count, battle.Size, $"{name} frame {n}");
            }
            var theirCalls = drawn[n][0].EnumerateArray().Select(c => BattleDrawTests.StatusCall(c, sheets)).ToList();
            // Each erase drawn within the rectangle UQM's was (InitShipStatus' clip, its top rounded down to even),
            // in order: the pixels alone can't tell a row the same either way.
            BattleDrawTests.SameClips(theirCalls, screen.Status, $"{name} frame {n}");
            uqm.Paint(theirCalls);
            port.Paint(screen.Status);
            var diff = BattleDrawTests.Diff(uqm, port);
            // The one known difference (docs/differences.md): a ship whose crew runs out in its own postprocess (the
            // Shofixti's Glory Device) has UQM's PostProcessStatus draw its explosion's last step there and then, the
            // window's centre black, which the next frame's first step covers; the port can't tell that frame from a
            // crew running out after it, and draws the steps from the next.
            var centre = (UqmCanvas.SpaceWidth + BattleStatus.CaptainXOffs + (BattleStatus.CaptainWidth >> 1),
                BattleStatus.EnemySlot + BattleStatus.CaptainYOffs + ((BattleStatus.CaptainHeight + 1) >> 1));
            if (diff.Count == 1 && diff[0] == centre && theirs.ElementCrew == 0 && theirs.LifeSpan == 0)
            {
                ++centres;
                port.Paint(new[] { new UqmDraw(UqmDrawKind.Rect, centre.Item1, centre.Item2, 1, 1, new Rgb(0, 0, 0),
                    Clip: new URect(0, 0, ScreenRaster.Width, ScreenRaster.Height)) });
                diff = BattleDrawTests.Diff(uqm, port);
            }
            Assert.True(diff.Count == 0, $"{name} frame {n}: {diff.Count} status pixels differ, first at {diff.FirstOrDefault()}; "
                + $"UQM drew {string.Join(" ", drawn[n][0].EnumerateArray().Take(40))}, the port {string.Join(" ", screen.Status.Take(40).Select(Short))}");
        });
        Assert.Equal(drawn.Count, frame);
        Assert.Equal(name == "shofixti-keys" ? 1 : 0, centres);   // its Glory Device goes off once; its second ship lives
        Assert.True(spaces > 4, $"{name}: only {spaces} frames' space compared");
    }
}
