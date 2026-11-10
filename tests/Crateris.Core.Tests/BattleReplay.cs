// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;

namespace Crateris.Core.Tests;

/// <summary>
/// Replays a battle UQM's own engine recorded (tools/extract/golden/battle.c:
/// tests/golden/battle.json, battle-ships.json, battle-ai.json) from its
/// seed and keys (or its AI), comparing every frame's random generator,
/// camera, ships, elements (as the netplay checksum takes them) and random
/// draws, and where recorded each side's input.
/// </summary>
internal static class BattleReplay
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly PlanetTypes Planets = PlanetTypes.Load(Data);
    public static readonly ShipDescriptions Ships = ShipDescriptions.Load(Data);
    private static readonly ShipDescription[] Fighters = { Ships[ShipDescriptions.Flagship], Ships[ShipDescriptions.Ilwrath] };

    /// <summary>The flagship's, the Ilwrath's and the arena's sheets, from the content.</summary>
    public static readonly Lazy<BattleSheets> Sheets = new(() => TestContent.With(p =>
    {
        var decoded = new AnimationSheets(p);
        foreach (string id in BattleResources.All(Fighters, Planets))
            _ = decoded[id];
        var sheets = new BattleSheets(decoded, BattleShapes.Load(decoded, Fighters, Planets));
        // Every sheet made now: the test classes replaying battles run at
        // once, and BattleSheets makes each sheet when first asked for.
        foreach (string id in BattleResources.All(Fighters, Planets))
            _ = sheets[id];
        return sheets;
    }));

    /// <summary>A ship's sheets at three sizes (ship_data.ship or .weapon).</summary>
    public static Views ViewsOf(IReadOnlyList<string?> resources) => Sheets.Value.Views(resources)!;

    /// <summary>The names of a reference file's battles.</summary>
    public static IEnumerable<object[]> Names(string file) =>
        Golden.Load(file).GetProperty("battles").EnumerateArray().Select(b => new object[] { b.GetProperty("name").GetString()! });

    /// <summary>A recorded battle by name.</summary>
    public static JsonElement Recorded(JsonElement reference, string name) =>
        reference.GetProperty("battles").EnumerateArray().First(b => b.GetProperty("name").GetString() == name);

    /// <summary>
    /// The battle replayed from its input with <paramref name="fleets"/>
    /// (each side's ships, made from the input's ships), each frame compared
    /// with UQM's; <paramref name="colours"/> when its elements end with
    /// their primitive's colour. A side with a rating in <paramref name="ai"/>
    /// is flown by the AI (<see cref="Battle.ComputerIntelligence"/>), the
    /// rest by the steps' keys; when the battle recorded each frame's inputs,
    /// they're compared too.
    /// </summary>
    public static Battle Run(JsonElement reference, string name, IReadOnlyList<IReadOnlyList<Starship>> fleets,
        bool runAwayAllowed, bool colours, IReadOnlyList<CyborgRating?>? ai = null)
    {
        var recorded = Recorded(reference, name);
        var input = recorded.GetProperty("input");
        var sheetNumbers = reference.GetProperty("sheets").EnumerateArray()
            .Select((s, i) => (s.GetString()!, i)).ToDictionary(x => x.Item1, x => x.i);
        string planet = input.GetProperty("planet").GetString()!;
        var arena = Sheets.Value.Arena(new[] { $"{planet}.large", $"{planet}.medium", $"{planet}.small" });
        var places = new List<string>();
        var zoom = input.TryGetProperty("zoom", out var z) && z.GetString() == "step" ? BattleZoom.Step : BattleZoom.Smooth;
        var battle = new Battle(arena, fleets, input.GetProperty("seed").GetUInt32(), zoom, runAwayAllowed, places);

        // InitGalaxy's stars.
        Assert.Equal(recorded.GetProperty("stars").EnumerateArray().Select(p => (p[0].GetInt32(), p[1].GetInt32())),
            battle.Stars.Select(p => ((int)p.X, (int)p.Y)));

        var frames = recorded.GetProperty("frames").EnumerateArray().ToList();
        Check(name, 0, frames[0], battle, places, sheetNumbers, colours);
        var events = input.TryGetProperty("events", out var ev)
            ? ev.EnumerateArray().Select(e => e.Ints()).ToList() : new List<int[]>();
        var recordedInputs = recorded.TryGetProperty("inputs", out var ins)
            ? ins.EnumerateArray().Select(i => i.Ints()).ToList() : null;
        var inputs = new int[Battle.NumSides];
        int frame = 0;
        foreach (var step in input.GetProperty("steps").EnumerateArray().Select(s => s.Ints()))
        {
            for (int i = 0; i < step[0] && battle.InBattle; ++i, ++frame)
            {
                // The draws an event makes count in the frame it comes before.
                places.Clear();
                foreach (var e in events.Where(e => e[0] == frame))
                    battle.AbandonShip(InBattle(battle, e[1]), InBattle(battle, e[2]), e[3]);
                Array.Fill(inputs, -1);
                battle.Step(ship =>
                {
                    var keys = (BattleInput)step[1 + ship.PlayerNr];
                    var given = ai?[ship.PlayerNr] is { } rating ? battle.ComputerIntelligence(ship, rating) : keys;
                    inputs[ship.PlayerNr] = (int)given;
                    return given;
                });
                Assert.True(frame + 1 < frames.Count, $"{name}: the battle runs past UQM's {frames.Count - 1} frames");
                if (recordedInputs != null)
                    Assert.True(recordedInputs[frame + 1].SequenceEqual(inputs),
                        $"{name} frame {frame + 1}: inputs [{string.Join(",", inputs)}], UQM [{string.Join(",", recordedInputs[frame + 1])}]");
                Check(name, frame + 1, frames[frame + 1], battle, places, sheetNumbers, colours);
            }
        }
        Assert.Equal(frames.Count - 1, frame);
        Assert.Equal(recorded.GetProperty("ended").GetBoolean(), !battle.InBattle);
        return battle;
    }

    private static void Check(string name, int n, JsonElement want, Battle battle, List<string> places,
        IReadOnlyDictionary<string, int> sheetNumbers, bool colours)
    {
        string at = $"{name} frame {n}";
        string draws = "[" + string.Join(",", places.Select(p => $"\"{p}\"")) + "]";
        bool full = want.GetArrayLength() == 10;
        if (full)
            Assert.True(want[9].GetRawText() == draws, $"{at}: draws {Runs(places)}, UQM {Runs(want[9].EnumerateArray().Select(p => p.GetString()!))}");
        Assert.True(want[0].GetUInt32() == battle.Random.Seed, $"{at}: seed {battle.Random.Seed}, UQM {want[0]}");
        Assert.True((want[1].GetInt32(), want[2].GetInt32()) == (battle.ScrollX, battle.ScrollY),
            $"{at}: scroll ({battle.ScrollX}, {battle.ScrollY}), UQM ({want[1]}, {want[2]})");
        Assert.True(want[3].GetInt32() == battle.ZoomOut, $"{at}: zoom {battle.ZoomOut}, UQM {want[3]}");
        Assert.True(want[4].GetInt32() == battle.ViewState, $"{at}: view {battle.ViewState}, UQM {want[4]}");
        Assert.True((want[5].GetInt32(), want[6].GetInt32()) == (battle.SpaceOrgX, battle.SpaceOrgY),
            $"{at}: the view's corner ({battle.SpaceOrgX}, {battle.SpaceOrgY}), UQM ({want[5]}, {want[6]})");

        string ships = ShipsText(battle);
        var elements = battle.List.Elements.Select(e => ElementText(e, sheetNumbers, colours)).ToList();
        if (!full)
        {
            // A long battle's later frames: the count and a hash.
            Assert.True(want[7].GetInt32() == elements.Count, $"{at}: {elements.Count} elements, UQM {want[7]}");
            string text = "[" + ships + ",[" + string.Join(",", elements) + "]," + draws + "]";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
            Assert.True(want[8].GetString() == hash, $"{at}: the frame differs from UQM's (hashed): {text}");
            return;
        }
        Assert.True(want[7].GetRawText() == ships, $"{at}: ships {ships}, UQM {want[7].GetRawText()}");
        var wantElements = want[8].EnumerateArray().Select(e => e.GetRawText()).ToList();
        for (int i = 0; i < Math.Min(wantElements.Count, elements.Count); ++i)
            Assert.True(wantElements[i] == elements[i], $"{at}: element {i} is {elements[i]}, UQM {wantElements[i]}");
        Assert.True(wantElements.Count == elements.Count, $"{at}: {elements.Count} elements, UQM {wantElements.Count}");
    }

    // The side's ship in the battle.
    private static Element InBattle(Battle battle, int side) => battle.Fleet(side).First(s => s.Ship != null).Ship!;

    private static string ShipsText(Battle battle) => "[" + string.Join(",", Enumerable.Range(0, Battle.NumSides).Select(side =>
        "[" + string.Join(",", battle.Fleet(side).Select(s =>
        {
            var rd = s.RaceDesc;
            return rd == null ? "null" : Ints(rd.CrewLevel, rd.EnergyLevel, s.ShipFacing, s.EnergyCounter, s.WeaponCounter,
                s.SpecialCounter, (int)s.CurStatusFlags, s.Ship != null ? 1 : 0);
        })) + "]")) + "]";

    private static string ElementText(Element e, IReadOnlyDictionary<string, int> sheetNumbers, bool colours)
    {
        var v = e.Velocity;
        var fields = new List<int>
        {
            (int)e.StateFlags, e.LifeSpan, e.CrewLevel, e.MassPoints, e.TurnWait, e.ThrustWait,
            v.TravelAngle, v.VectorWidth, v.VectorHeight, v.FractWidth, v.FractHeight, v.ErrorWidth, v.ErrorHeight,
            v.IncrWidth, v.IncrHeight, e.Current.X, e.Current.Y, e.Next.X, e.Next.Y,
            e.Current.Frame.Sheet is { } sheet ? sheetNumbers[sheet.Id] : -1, e.Current.Frame.Index, e.PlayerNr,
            (int)e.Prim, e.Prim == PrimType.StampFill && e.PrimColor == 0 ? 1 : 0, e.Slot, e.Target?.Slot ?? 0,
        };
        if (colours)
            fields.Add(e.PrimColor);
        return Ints(fields.ToArray());
    }

    // Draws as runs of the same place, "place x count".
    private static string Runs(IEnumerable<string> places)
    {
        var runs = new List<(string Place, int Count)>();
        foreach (string p in places)
            if (runs.Count > 0 && runs[^1].Place == p)
                runs[^1] = (p, runs[^1].Count + 1);
            else
                runs.Add((p, 1));
        return string.Join(", ", runs.Select(r => $"{r.Place} x {r.Count}"));
    }

    private static string Ints(params int[] values) => "[" + string.Join(",", values) + "]";
}
