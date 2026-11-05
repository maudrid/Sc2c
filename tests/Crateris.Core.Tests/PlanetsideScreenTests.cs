// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The surface view and the lander's gauges (stage 2b, item 10) against
/// what UQM's own lander.c draws, frame by frame (lander-drawing.json):
/// every stamp, filled stamp and line of text on the surface view in
/// order, and the gauges as UQM leaves them after each frame.
/// </summary>
public class PlanetsideScreenTests
{
    private static readonly Lazy<JsonElement> Recorded = new(() => Golden.Load("lander-drawing"));
    private static readonly Lifeforms Life = Lifeforms.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    public static IEnumerable<object[]> Landings() =>
        Golden.Load("lander-drawing").GetProperty("landings").EnumerateArray()
            .Select(l => new object[] { l.GetProperty("name").GetString()! });

    private static LanderUpgrades Upgrades(int flags) =>
        ((flags & 1) != 0 ? LanderUpgrades.Speed : 0) | ((flags & 2) != 0 ? LanderUpgrades.Shot : 0)
        | ((flags & 4) != 0 ? LanderUpgrades.Cargo : 0) | ((flags & 8) != 0 ? LanderUpgrades.BioShield : 0)
        | ((flags & 16) != 0 ? LanderUpgrades.QuakeShield : 0) | ((flags & 32) != 0 ? LanderUpgrades.LightningShield : 0)
        | ((flags & 64) != 0 ? LanderUpgrades.LavaShield : 0);

    private static string Op(JsonElement e) => e[0].GetString() switch
    {
        "t" => $"t {e[1]},{e[2]} '{(e[5].GetInt32() < 0 ? e[4].GetString() : e[4].GetString()![..e[5].GetInt32()])}' {e[6]},{e[7]},{e[8]}",
        _ => string.Join(" ", e.EnumerateArray()),
    };

    private static string Op(SurfaceDraw d) => d.Kind switch
    {
        SurfaceDrawKind.Map => $"m {d.X} {d.Y}",
        SurfaceDrawKind.Stamp => $"s {(int)d.Sheet} {d.Frame} {d.X} {d.Y}",
        SurfaceDrawKind.Filled => $"f {(int)d.Sheet} {d.Frame} {d.X} {d.Y} {d.Color.R} {d.Color.G} {d.Color.B}",
        SurfaceDrawKind.Rectangle => $"r {d.X} {d.Y} {d.Width} {d.Height} {d.Color.R} {d.Color.G} {d.Color.B}",
        _ => $"t {d.X},{d.Y} '{d.Text}' {d.Color.R},{d.Color.G},{d.Color.B}",
    };

    // What a gauge stamp or rectangle stands for: a later one there replaces it.
    private static string Slot(string op)
    {
        var f = op.Split(' ');
        if (f[0] == "r")
            return op[..op.LastIndexOf(' ', op.LastIndexOf(' ', op.LastIndexOf(' ') - 1) - 1)];
        int frame = int.Parse(f[2]);
        return frame switch
        {
            32 or 33 => "base",
            34 or 58 => "gun",
            35 or 36 or 57 => "engine",
            41 or 42 => $"bio {f[4]}",
            43 or 44 => $"minerals {f[4]}",
            55 or 56 => $"crew {f[3]} {f[4]}",
            _ => $"frame {frame}",
        };
    }

    [ContentTheory]
    [MemberData(nameof(Landings))]
    public void TheSurfaceIsDrawnAsUqmDrawsIt(string name)
    {
        var landing = Recorded.Value.GetProperty("landings").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name);
        var input = landing.GetProperty("input");
        var placed = input.GetProperty("nodes").EnumerateArray().Select(n => n.Ints()).ToList();
        var minerals = placed.Where(n => n[0] == 0).Select((n, i) => new SurfaceMineral(i, n[3], n[4], n[1], n[2])).ToList();
        var creatures = placed.Where(n => n[0] == 2).Select((n, i) => new SurfaceCreature(i, n[3], n[4], n[1])).ToList();
        var chances = HazardChances.For(input.GetProperty("tectonics").GetInt32(), input.GetProperty("weather").GetInt32(),
            input.GetProperty("temperature").GetInt32());
        var surface = new PlanetsideSurface(new TfbRandom(landing.GetProperty("seed").GetUInt32()), minerals,
            Systems.Generator.Types.ElementCategory, TestContent.Shapes, creatures, Life, chances);
        int room = input.GetProperty("storage").GetInt32();
        var hold = new ShipHold(new ShipLoadout(1, 1, 0, 0, input.GetProperty("crew").GetInt32(), 0))
            { Upgrades = Upgrades(landing.GetProperty("upgrades").GetInt32()) };
        var already = new int[ShipHold.Categories];
        already[0] = ShipHold.StorageBayCapacity - room;
        hold.Restore(new HoldState(hold.Crew, hold.Landers, already, 0, hold.Upgrades));
        var site = input.GetProperty("site").Ints();
        hold.LanderLeaves();
        var shipHold = hold.Snapshot();   // as the lander left
        var lander = surface.Land(hold, site[0], site[1]);
        var flags = input.GetProperty("steps").EnumerateArray()
            .SelectMany(s => Enumerable.Repeat((LanderInput)s[1].GetInt32(), s[0].GetInt32())).GetEnumerator();
        var gauges = new Dictionary<string, string>();
        int frame = 0;
        foreach (var want in landing.GetProperty("frames").EnumerateArray())
        {
            var keys = LanderInput.None;
            if (lander.Phase == LanderPhase.Ground)
                keys = flags.MoveNext() ? flags.Current : LanderInput.TakeOff;
            surface.Step(_ => keys);
            if (lander.Phase == LanderPhase.Gone)
                break;   // given up: no surface to draw

            var snapshot = new SurfaceSnapshot(0, 0, null, new[] { LanderSnapshot.Of("me", lander) },
                surface.Elements.Where(e => e.Kind != SurfaceElementKind.Mineral).Select(SurfaceElementSnapshot.Of).ToList());
            var deposits = surface.Elements.Where(e => e.Kind == SurfaceElementKind.Mineral)
                .Select(e => new SurfaceDeposit(e.Node, e.X, e.Y, e.Frame)).ToList();
            var got = PlanetsideScreen.Draw(snapshot, "me", deposits).Select(Op).ToList();
            // The first frame's list starts with the launch animation
            // (AnimateLaunch) and the map drawn once for the screen's
            // transition (InitPlanetSide), before the landing.
            var wantOps = want[14].EnumerateArray().Select(Op).ToList();
            if (frame == 0)
                wantOps = wantOps.SkipWhile(o => !o.StartsWith("m ")).Skip(3).ToList();
            Assert.True(wantOps.SequenceEqual(got),
                $"{name}, frame {frame}: surface\n want {string.Join("; ", wantOps)}\n got  {string.Join("; ", got)}");

            foreach (var op in want[15].EnumerateArray().Select(Op))
                gauges[Slot(op)] = op;
            var ours = LanderGauges.Draw(LanderSnapshot.Of("me", lander), shipHold).Select(Op)
                .GroupBy(Slot).ToDictionary(g => g.Key, g => g.Last());
            Assert.True(gauges.OrderBy(g => g.Key).SequenceEqual(ours.OrderBy(g => g.Key)),
                $"{name}, frame {frame}: gauges\n want {string.Join("; ", gauges.OrderBy(g => g.Key).Select(g => g.Value))}\n got  {string.Join("; ", ours.OrderBy(g => g.Key).Select(g => g.Value))}");
            frame++;
        }
        Assert.True(frame > 150, $"only {frame} frames compared");
    }
}

/// <summary>The Manifest's cargo display (stage 2b, item 10) against UQM's own cargo.c (lander-drawing.json's cargo).</summary>
public class CargoDisplayTests
{
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static string Op(JsonElement e) => e[0].GetString() == "t"
        ? $"t {e[1]},{e[2]} {e[3]} '{e[4].GetString()}' {e[6]},{e[7]},{e[8]} font {e[9]}"
        : string.Join(" ", e.EnumerateArray());

    private static string Op(SurfaceDraw d) => d.Kind switch
    {
        SurfaceDrawKind.Stamp => $"s {(int)d.Sheet} {d.Frame} {d.X} {d.Y}",
        SurfaceDrawKind.Rectangle => $"r {d.X} {d.Y} {d.Width} {d.Height} {d.Color.R} {d.Color.G} {d.Color.B}",
        _ => $"t {d.X},{d.Y} {(int)d.Align} '{d.Text}' {d.Color.R},{d.Color.G},{d.Color.B} font {(d.Font == SurfaceFont.StarCon ? 1 : 2)}",
    };

    [ContentFact]
    public void TheCargoDisplayIsUqms()
    {
        var icon = TestContent.Shapes.MiscData[ContactShapes.FirstMineralFrame + 3];
        int n = 0;
        foreach (var c in Golden.Load("lander-drawing").GetProperty("cargo").EnumerateArray())
        {
            var input = c.GetProperty("input").Ints();
            var hold = new HoldSnapshot(0, 1, input[9], input[..8], input[8]);
            var got = CargoDisplay.Draw(hold, input[10], Systems.Generator.Types.CategoryWorth, (icon.Width, icon.Height))
                .Select(Op).ToList();
            var want = c.GetProperty("draws").EnumerateArray().Select(Op).ToList();
            Assert.True(want.SequenceEqual(got), $"case {n}:\n want {string.Join("; ", want)}\n got  {string.Join("; ", got)}");
            n++;
        }
        Assert.True(n >= 10);
    }
}
