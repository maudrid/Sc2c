// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Content;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The starbase's screens (docs/plans/stage-3.md, item 11) against what
/// UQM's own starbase.c, outfit.c, sis.c, menu.c and border.c draw
/// (tests/golden/screens.json).
/// </summary>
[Trait("Topic", "drawing")]
public class StarbaseScreensTests
{
    private static readonly JsonElement Root = Golden.Load("screens");
    private static readonly JsonElement Recorded = Root.GetProperty("scenarios");
    private static readonly Dictionary<string, int> Leading = new() { ["starcon"] = 11, ["tiny"] = 10, ["micro"] = 15 };

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    private static readonly Lazy<Dictionary<(string, int), URect>> Rects = new(() => TestContent.With(p =>
    {
        var sheets = new AnimationSheets(p);
        var rects = new Dictionary<(string, int), URect>();
        foreach (var name in Root.GetProperty("frames").EnumerateObject().Select(f => f.Name))
        {
            var frames = sheets[name];
            for (int i = 0; i < frames.Count; ++i)
                rects[(name, i)] = new URect(-frames[i].Frame.HotSpotX, -frames[i].Frame.HotSpotY, frames[i].Image.Width,
                    frames[i].Image.Height);
        }
        return rects;
    }));

    internal static UqmCanvas Canvas() => new(
        sheet => Root.GetProperty("frames").GetProperty(sheet).GetInt32(), (sheet, i) => Rects.Value[(sheet, i)], Leading);

    internal static string Placeholder(int index) => $"{{gs:{index}}}";

    // A scenario's ship as golden/screens.c is given it.
    internal static SisState Ship(JsonElement sc)
    {
        var ship = sc.GetProperty("ship");
        var prices = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "new-game.json")))!["prices"]!;
        var costs = prices["module_cost"]!.AsObject().ToDictionary(k => k.Key, k => k.Value!.GetValue<int>());
        foreach (var c in sc.GetProperty("costs").EnumerateObject())
            costs[c.Name] = c.Value.GetInt32();
        byte Module(string name) => name == "EMPTY" ? ShipModules.EmptyModule : (byte)ShipModules.Names.ToList().IndexOf(name);
        var drives = ship.GetProperty("drives").Ints();
        var jets = ship.GetProperty("jets").Ints();
        var state = sc.GetProperty("state");
        int State(string f) => state.TryGetProperty(f, out var v) ? v.GetInt32() : 0;
        return new SisState(
            ship.GetProperty("modules").EnumerateArray().Select(m => Module(m.GetString()!)).ToArray(),
            Enumerable.Range(0, 11).Select(i => drives.Contains(i) ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
            Enumerable.Range(0, 8).Select(i => jets.Contains(i) ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
            ship.GetProperty("crew").GetInt32(), ship.GetProperty("fuel").GetUInt32(), ship.GetProperty("landers").GetInt32(),
            ship.GetProperty("ru").GetUInt32(), ship.GetProperty("elements").Ints().Sum(), ship.GetProperty("ship").GetString()!,
            ship.GetProperty("captain").GetString()!, State("CHMMR_BOMB_STATE"), State("LANDER_SHIELDS"),
            sc.GetProperty("fuel_cost").GetInt32(), sc.GetProperty("crew_cost").GetInt32(),
            m => costs.GetValueOrDefault(ShipModules.Names[m], 0));
    }

    // A drawing call as golden/screens.c writes it.
    internal static string Json(UqmDraw d) => (d.Kind switch
    {
        UqmDrawKind.Stamp => new JsonArray("s", d.Sheet, d.Frame, d.X, d.Y),
        UqmDrawKind.FilledStamp => new JsonArray("f", d.Sheet, d.Frame, d.X, d.Y, d.Color.R, d.Color.G, d.Color.B),
        UqmDrawKind.Rect => new JsonArray("r", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
        UqmDrawKind.Outline => new JsonArray("o", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
        UqmDrawKind.Point => new JsonArray("p", d.X, d.Y, d.Color.R, d.Color.G, d.Color.B),
        UqmDrawKind.Clear => new JsonArray("c", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
        _ => new JsonArray("t", d.Font, d.X, d.Y, (int)d.Align, d.Count, d.Text, d.Color.R, d.Color.G, d.Color.B, d.Effect),
    }).ToJsonString();

    private static void Same(IEnumerable<JsonElement> want, IEnumerable<UqmDraw> got, string where)
    {
        var w = want.Select(e => JsonNode.Parse(e.GetRawText())!.ToJsonString()).ToList();
        var g = got.Select(Json).ToList();
        for (int i = 0; i < Math.Max(w.Count, g.Count); ++i)
            Assert.True(i < w.Count && i < g.Count && w[i] == g[i],
                $"{where}, call {i}:\n UQM  {(i < w.Count ? w[i] : "(none)")}\n port {(i < g.Count ? g[i] : "(none)")}");
    }

    [ContentTheory]
    [MemberData(nameof(Scenarios))]
    public void TheFirstDrawingIsUqms(string name)
    {
        var sc = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var step = sc.GetProperty("steps")[0].GetString()!;
        var first = sc.GetProperty("drawn")[0].EnumerateArray();
        var screens = new StarbaseScreens(Canvas(), Placeholder);
        if (step.StartsWith("base:"))
            screens.Menu(int.Parse(step[5..]), 0);
        else
            screens.Outfit(Ship(sc), 0, 0, false);
        Same(first, screens.Drawn, $"{name} {step}");
    }
}
