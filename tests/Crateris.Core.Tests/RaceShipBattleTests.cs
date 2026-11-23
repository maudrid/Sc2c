// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Stage 4b's race ships against UQM's own (tests/golden/battle-races-1.json
/// to 3.json, recorded by tools/extract/golden/battle.c with each ship's own
/// UQM code): each ship ported (<see cref="RaceShips.Ported"/>) replayed in
/// its two battles, by keys against the flagship and flown by the AI
/// against the flagship's AI, every frame compared (the elements' colours,
/// the random draws under UQM's names, each side's input) and the crew
/// each side had left when the battle ended. Each frame also goes on the
/// wire (stage 4b item 9): its elements and its ships' status panels and
/// captain's windows as the snapshot sends them, encoded and decoded whole,
/// every stamp's sheet one the client loads, at each zoom's size, with the
/// frame in it; the captain's windows' marks sent (those still showing)
/// drawing what every mark made draws, and costing little.
/// </summary>
[Trait("Topic", "combat")]
public class RaceShipBattleTests
{
    private static readonly Lazy<JsonElement>[] Parts = Enumerable.Range(1, RaceBattleTraceTests.Parts.Length)
        .Select(n => new Lazy<JsonElement>(() => Golden.Load($"battle-races-{n}"))).ToArray();

    /// <summary>Each ported ship's battles, by its part's file.</summary>
    public static IEnumerable<object[]> Battles() =>
        Enumerable.Range(0, RaceBattleTraceTests.Parts.Length).SelectMany(part =>
            BattleReplay.Names($"battle-races-{part + 1}")
                .Select(n => (string)n[0])
                .Where(name => RaceShips.Ported.Contains(name[..name.IndexOf('-')]))
                .Select(name => new object[] { part + 1, name }));

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheBattleIsUqms(int part, string name)
    {
        var reference = Parts[part - 1].Value;
        var recorded = BattleReplay.Recorded(reference, name);
        var input = recorded.GetProperty("input");
        var ai = input.TryGetProperty("ai", out var sides)
            ? sides.EnumerateArray().Select(a => a.GetString() == "awesome" ? CyborgRating.Awesome
                : throw new InvalidDataException($"a rating {a}")).Select(r => (CyborgRating?)r).ToList()
            : null;
        int[] fleetSizes = input.TryGetProperty("fleet", out var fleet) ? fleet.Ints() : new[] { 1, 1 };
        // shofixti-old-*: Tanaka's damaged scout (the Shofixti not recruited).
        bool recruited = input.TryGetProperty("shofixti_recruited", out var rec) && rec.GetBoolean();
        var sis = new List<SisShip>();
        var fleets = input.GetProperty("ships").EnumerateArray().Select((s, side) =>
            (IReadOnlyList<Starship>)Enumerable.Range(0, fleetSizes[side]).Select(_ =>
            {
                var (code, crew) = ShipBattleTests.Ship(s, shofixtiRecruited: recruited);
                if (code is SisShip flagship)
                    sis.Add(flagship);
                // Its status panel's icons, which the VUX's limpets are placed on (ModifySilhouette).
                var icons = RaceShips.Icons(BattleReplay.Ships[s.GetProperty("ship").GetString()!], r => BattleReplay.Sheets.Value.Views(r));
                return new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5), Icons = icons };
            }).ToList()).ToList();

        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean() && !sis.Any(s => s.BombCarrier);
        var shipIds = input.GetProperty("ships").EnumerateArray().Select(s => s.GetProperty("ship").GetString()!)
            .Select(id => id == ShofixtiShip.Id && !recruited ? ShipDescriptions.TanakasScout : id).ToArray();
        int frame = 0, mostMarks = 0, mostShown = 0, mostBytes = 0;
        var battle = BattleReplay.Run(reference, name, fleets, runaway, colours: true, ai, b =>
        {
            var (marks, shown, bytes) = OnTheWire(name, frame++, b, shipIds);
            (mostMarks, mostShown, mostBytes) = (Math.Max(mostMarks, marks), Math.Max(mostShown, shown), Math.Max(mostBytes, bytes));
        });
        ShipBattleTests.CheckFreed(reference, name, battle, sis);
        // The captain's windows' marks still showing, as sent: well within a snapshot's budget (1,500 bytes, the
        // network test's) at the busiest frame. The most made are orz-keys' marines' 124, of which 8 still show
        // (66 bytes); vux-keys' 13 limpets, all showing, cost the most (89 bytes).
        Assert.True(mostBytes < 150, $"{name}: the marks cost {mostBytes} bytes in a frame ({mostShown} sent of {mostMarks} made)");
    }

    // A captain's window's marks drawn in order on a window with nothing on it: each pixel, the frame and the place in
    // it whose pixel is drawn there (a stamp's solid pixels; an erase clears its rectangle).
    internal static Dictionary<(int, int), (string?, int, int, int)> Picture(IEnumerable<SilhouetteMark> marks)
    {
        var picture = new Dictionary<(int, int), (string?, int, int, int)>();
        foreach (var m in marks)
        {
            var r = m.Frame.Rect;
            int x0 = m.X - r.HotSpotX, y0 = m.Y - r.HotSpotY;
            for (int y = 0; y < r.Height; ++y)
                for (int x = 0; x < r.Width; ++x)
                    if (m.Erase)
                        picture.Remove((x0 + x, y0 + y));
                    else if (m.Frame.Shape.Solid(x, y))
                        picture[(x0 + x, y0 + y)] = (m.Frame.Sheet?.Id, m.Frame.Index, x, y);
        }
        return picture;
    }

    // Every battle resource the client loads (BattleSprites: BattleResources.All), with its frames as UQM's loader loads them.
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Resources = new(() =>
        Golden.Load("battle-masks").GetProperty("frames").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32()));

    // The frame as a snapshot sends it, each side's ship a player's (side 0's, "p") or the enemy (side 1's): through the
    // codec whole, every sheet a stamp or a captain's window's mark draws one the client loads, with the frame in it.
    // Returns the marks made on the side's windows, those sent, and what they cost in the message.
    private static (int Marks, int Shown, int Bytes) OnTheWire(string name, int frame, Battle battle, string[] shipIds)
    {
        string at = $"{name} frame {frame}";
        BattleShipStatus Status(int side) => battle.Fleet(side).FirstOrDefault(s => s.RaceDesc != null) is { } s
            ? GameWorld.StatusSnapshot(shipIds[side], s) : new BattleShipStatus(shipIds[side], 0, 0, 0, 0, Gone: true);
        var elements = battle.List.Elements.Where(battle.Drawn).Select(GameWorld.ElementSnapshot).ToList();
        var snapshot = new BattleSnapshot(1, battle.Size.Width, battle.Size.Height, Status(1), 0, 0,
            new[] { new BattleFighterSnapshot("p", Status(0), 0, 0, true) }, elements, battle.Camera, battle.StarSeed);
        var w = new Net.WireWriter();
        Net.SnapshotCodec.Encode(w, new WorldSnapshot(frame + 1, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { snapshot }),
            null, "p");
        var got = Assert.Single(Net.SnapshotCodec.Decode(w.Written).Battles!);
        Assert.True(snapshot.Equals(got), $"{at}: the battle changed on the wire");
        Assert.Equal(elements.Count, got.Elements.Count);
        foreach (var e in elements.Where(e => e.Prim is PrimType.Stamp or PrimType.StampFill))
            for (int size = 0; size < 3; ++size)
            {
                string sheet = Crateris.Presentation.BattleSpace.SizeOf(e.Sheet, size);
                Assert.True(Resources.Value.TryGetValue(sheet, out int count), $"{at}: a stamp of {sheet}, which the client doesn't load");
                Assert.True(e.Frame < count, $"{at}: {sheet} frame {e.Frame} of {count}");
            }
        foreach (var m in got.Fighters.Select(f => f.Status).Append(got.Enemy).SelectMany(s => s.Marks!))
        {
            Assert.True(Resources.Value.TryGetValue(m.Sheet, out int count), $"{at}: a captain's window's mark of {m.Sheet}, which the client doesn't load");
            Assert.True(m.Frame < count, $"{at}: a mark of {m.Sheet} frame {m.Frame} of {count}");
        }
        // The marks sent, those still showing, draw what every mark made draws.
        foreach (var s in battle.Fleet(0).Concat(battle.Fleet(1)).Where(s => s.Silhouette.Count > 0))
        {
            var all = Picture(s.Silhouette);
            var shown = Picture(s.Silhouette.Shown);
            Assert.True(all.Count == shown.Count && all.All(p => shown.TryGetValue(p.Key, out var q) && q == p.Value),
                $"{at}: the marks shown draw another picture than every mark made");
        }
        var bare = snapshot with
        {
            Enemy = snapshot.Enemy with { Marks = null },
            Fighters = snapshot.Fighters.Select(f => f with { Status = f.Status with { Marks = null } }).ToArray(),
        };
        Net.SnapshotCodec.Encode(w, new WorldSnapshot(frame + 1, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { bare }), null, "p");
        int without = w.Length;
        Net.SnapshotCodec.Encode(w, new WorldSnapshot(frame + 1, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { snapshot }), null, "p");
        var ships = battle.Fleet(0).Concat(battle.Fleet(1)).ToList();
        return (ships.Sum(s => s.Silhouette.Count), ships.Sum(s => s.Silhouette.Shown.Count), w.Length - without);
    }
}
