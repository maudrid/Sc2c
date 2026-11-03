// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The lander on the surface (stage 2b, items 5 and 6) against UQM's own
/// lander.c, frame by frame: landing, driving, turning, the poles and the
/// wrap, firing, taking off and TFB_Random's draws (tests/golden/lander.json),
/// and picking up mineral nodes, with the bolts that destroy them, partial
/// pickups and the cargo into the ship (lander-pickups.json, with the
/// content's shapes).
/// </summary>
public class LanderTests
{
    private static readonly JsonElement Recorded = Golden.Load("lander");
    private static readonly Lazy<JsonElement> Pickups = new(() => Golden.Load("lander-pickups"));
    private static readonly SystemCatalog Systems = SystemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    public static IEnumerable<object[]> Landings() =>
        Recorded.GetProperty("landings").EnumerateArray().Select(l => new object[] { l.GetProperty("name").GetString()! });

    public static IEnumerable<object[]> PickupLandings() =>
        Golden.Load("lander-pickups").GetProperty("landings").EnumerateArray()
            .Select(l => new object[] { l.GetProperty("name").GetString()! });

    private static JsonElement Landing(JsonElement file, string name) =>
        file.GetProperty("landings").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name);

    private static LanderUpgrades Upgrades(int flags) =>
        ((flags & 1) != 0 ? LanderUpgrades.Speed : 0) | ((flags & 2) != 0 ? LanderUpgrades.Shot : 0)
        | ((flags & 4) != 0 ? LanderUpgrades.Cargo : 0);

    [Theory]
    [MemberData(nameof(Landings))]
    public void EveryFrameIsUqms(string name) => Replay(Landing(Recorded, name), null);

    [ContentTheory]
    [MemberData(nameof(PickupLandings))]
    public void EveryPickupIsUqms(string name) => Replay(Landing(Pickups.Value, name), TestContent.Shapes);

    /// <summary>
    /// Replays a recorded landing frame by frame: the lander, the moving
    /// elements, the nodes left, the lander's hold and mineral text, the
    /// nodes taken; then what the ship got back.
    /// </summary>
    private static void Replay(JsonElement landing, Content.ContactShapes? shapes)
    {
        string name = landing.GetProperty("name").GetString()!;
        var input = landing.GetProperty("input");
        var minerals = input.GetProperty("nodes").EnumerateArray()
            .Select((n, i) => new SurfaceMineral(i, n[2].GetInt32(), n[3].GetInt32(), n[0].GetInt32(), n[1].GetInt32()))
            .ToList();
        var surface = new PlanetsideSurface(new TfbRandom(landing.GetProperty("seed").GetUInt32()), minerals,
            Systems.Generator.Types.ElementCategory, shapes);
        // The bays' room left as recorded: one bay, the rest already full of common elements.
        int room = input.GetProperty("storage").GetInt32();
        var hold = Hold(input.GetProperty("crew").GetInt32(), Upgrades(landing.GetProperty("upgrades").GetInt32()));
        var already = new int[ShipHold.Categories];
        already[0] = ShipHold.StorageBayCapacity - room;
        hold.Restore(new HoldState(hold.Crew, hold.Landers, already, 0, hold.Upgrades));
        var site = input.GetProperty("site").Ints();
        hold.LanderLeaves();
        var lander = surface.Land(hold, site[0], site[1]);

        // The steps' flags, one a ground frame; then take off.
        var flags = input.GetProperty("steps").EnumerateArray()
            .SelectMany(s => Enumerable.Repeat((LanderInput)s[1].GetInt32(), s[0].GetInt32()))
            .GetEnumerator();
        int frame = 0;
        foreach (var want in landing.GetProperty("frames").EnumerateArray())
        {
            var keys = LanderInput.None;
            if (lander.Phase == LanderPhase.Ground)
                keys = flags.MoveNext() ? flags.Current : LanderInput.TakeOff;
            surface.Step(_ => keys);
            string phase = lander.FramePhase switch
            {
                LanderPhase.Landing => "L",
                LanderPhase.Ground => "G",
                _ => "T",
            };
            var moving = surface.Elements.Where(e => e.Kind == SurfaceElementKind.Bolt).Select(e => $"[{e.X}, {e.Y}, {e.LifeSpan}, 0]");
            int nodes = surface.Elements.Count(e => e.Kind == SurfaceElementKind.Mineral);
            var text = lander.Text is { } t ? $"{t.Frames} '{t.Amount}' {t.X},{t.Y}" : "0 '' 0,0";
            var taken = surface.Taken.Select(n => $"[{(int)n.Scan}, {n.Node}]");
            string got = $"{phase} {lander.X},{lander.Y} facing {lander.Facing} offset {-lander.Height} crew {lander.Crew} "
                + $"seed {surface.Random.Seed} [{string.Join(", ", moving)}] nodes {nodes} "
                + $"hold {lander.MineralMass} {lander.BioData} [{string.Join(", ", lander.Minerals)}] text {text} "
                + $"taken [{string.Join(", ", taken)}]";
            var wantMoving = want[7].EnumerateArray().Select(b => $"[{b[0]}, {b[1]}, {b[2]}, {b[3]}]");
            var h = want[9].Ints();
            var wt = want[10];
            string wantText = $"{wt[0]} '{wt[1].GetString()}' {wt[2]},{wt[3]}";
            var wantTaken = want[11].EnumerateArray().Select(n => $"[{n[0]}, {n[1]}]");
            string expected = $"{want[0].GetString()} {want[1]},{want[2]} facing {want[3]} offset {want[4]} crew {want[5]} "
                + $"seed {want[6]} [{string.Join(", ", wantMoving)}] nodes {want[8]} "
                + $"hold {h[0]} {h[1]} [{string.Join(", ", h.Skip(2))}] text {wantText} "
                + $"taken [{string.Join(", ", wantTaken)}]";
            Assert.True(expected == got, $"{name}, frame {frame} ({keys}):\n want {expected}\n got  {got}");
            frame++;
        }
        Assert.Equal(LanderPhase.Gone, lander.Phase);
        Assert.Empty(surface.Landers);
        // The crew went aboard from the ship and came back with the cargo (AnimateLanderWarmup, PlanetSide).
        Assert.Equal(landing.GetProperty("crew_left").GetInt32(), lander.Crew);
        Assert.Equal(landing.GetProperty("ship_crew").GetInt32(), hold.Crew);
        Assert.Equal(landing.GetProperty("landers").GetInt32(), hold.Landers);
        Assert.Null(hold.LanderOut);
        if (landing.TryGetProperty("element_amounts", out var amounts))
        {
            var got = hold.Minerals.Select((m, i) => m - already[i]).ToArray();
            Assert.Equal(amounts.Ints(), got);
            Assert.Equal(landing.GetProperty("total_bio_mass").GetInt32(), hold.BioData);
        }
    }

    private static ShipHold Hold(int crew, LanderUpgrades upgrades = LanderUpgrades.None) =>
        new(new ShipLoadout(1, 1, 0, 0, crew, 0)) { Upgrades = upgrades };

    // A ship's lander sent out (as GameWorld.Dispatch does) and landed.
    private static Lander SendDown(PlanetsideSurface surface, ShipHold hold, int x, int y)
    {
        hold.LanderLeaves();
        return surface.Land(hold, x, y);
    }

    [Fact]
    public void TheCrewGoAboardFromTheShipAndComeBack()
    {
        var hold = Hold(5);
        var surface = new PlanetsideSurface(new TfbRandom(3));
        Assert.Throws<InvalidOperationException>(() => surface.Land(hold, 100, 100));   // not sent out
        var lander = SendDown(surface, hold, 100, 100);
        Assert.Equal((5, 0), (lander.Crew, hold.Crew));   // all five; the ship keeps none
        Assert.NotNull(hold.LanderOut);
        Assert.Throws<InvalidOperationException>(() => surface.Land(hold, 100, 100));   // already here
        while (lander.Phase != LanderPhase.Gone)
            surface.Step(l => l.Phase == LanderPhase.Ground ? LanderInput.TakeOff : LanderInput.None);
        Assert.Equal(5, hold.Crew);
        Assert.Null(hold.LanderOut);
        Assert.Empty(surface.Landers);
        hold.LanderLeaves();
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Land(hold, PlanetsideMap.Width, 0));
    }

    [Fact]
    public void ALanderWhoseHoldWasRestoredStillLeavesTheSurface()
    {
        // A save counts a lander out as back aboard; a hold restored from it
        // has no lander out when the lander takes off.
        var hold = Hold(50);
        var surface = new PlanetsideSurface(new TfbRandom(5));
        var lander = SendDown(surface, hold, 400, 150);
        hold.Restore(hold.Save());
        while (lander.Phase != LanderPhase.Gone)
            surface.Step(l => l.Phase == LanderPhase.Ground ? LanderInput.TakeOff : LanderInput.None);
        Assert.Empty(surface.Landers);
        Assert.Equal(50, hold.Crew);
    }

    [Fact]
    public void KeysAreAskedForBeforeAnythingMoves()
    {
        var surface = new PlanetsideSurface(new TfbRandom(6));
        var a = SendDown(surface, Hold(50), 400, 150);
        uint seed = surface.Random.Seed;
        Assert.Throws<InvalidOperationException>(() => surface.Step(_ => throw new InvalidOperationException()));
        Assert.Equal((seed, LanderPhase.Landing), (surface.Random.Seed, a.FramePhase));
        // A lander landed from the callback starts next frame.
        var late = Hold(50);
        Lander? b = null;
        surface.Step(l => { b ??= SendDown(surface, late, 600, 150); return LanderInput.None; });
        Assert.Equal(2, surface.Landers.Count);
        Assert.Equal(Lander.OffScreen, b!.Height);
    }

    [Fact]
    public void TwoLandersShareTheSurfaceFrameWithoutDoublingIt()
    {
        // Each lander moves once a frame and draws its own hazard roll;
        // what's on the surface moves once, however many landers there are.
        var alone = new PlanetsideSurface(new TfbRandom(42));
        var a = SendDown(alone, Hold(50), 300, 150);
        var pair = new PlanetsideSurface(new TfbRandom(42));
        var b = SendDown(pair, Hold(50), 300, 150);
        var other = SendDown(pair, Hold(50), 600, 150);
        int frames = 0;
        static LanderInput Keys(Lander l) => l.Phase == LanderPhase.Ground ? LanderInput.Fire : LanderInput.None;
        while (a.Phase != LanderPhase.Ground)
        {
            alone.Step(Keys);
            pair.Step(Keys);
            frames++;
        }
        alone.Step(Keys);
        pair.Step(Keys);   // one bolt each
        frames++;
        var bolt = alone.Elements.Single();
        var same = pair.Elements.Single(e => (e.LifeSpan, e.Y) == (bolt.LifeSpan, bolt.Y) && Math.Abs(e.X - b.X) < 20);
        for (int i = 0; i < 5; ++i)
        {
            alone.Step(Keys);
            pair.Step(Keys);
            frames++;
        }
        Assert.Equal(Lander.BoltLife - 6, bolt.LifeSpan);
        // Three draws for each landing (facing, then x and y), then a hazard roll a lander a frame.
        var want = new TfbRandom(42);
        for (int i = 0; i < 2 * 3 + 2 * frames; ++i)
            want.Next();
        Assert.Equal(want.Seed, pair.Random.Seed);
        Assert.Equal((bolt.LifeSpan, bolt.X - a.X, bolt.Y - a.Y), (same.LifeSpan, same.X - b.X, same.Y - b.Y));
        Assert.Equal(2, pair.Landers.Count);
    }

    [ContentFact]
    public void TheLandersAndNodesShapesAreTheOnesUqmTested()
    {
        // The recorder's masks of graphics.lander and graphics.miscdata, as
        // CheckObjectCollision's DrawablesIntersect read them.
        foreach (var sheet in Pickups.Value.GetProperty("masks").EnumerateObject())
        {
            var frames = TestContent.Shapes.Sheets[sheet.Name];
            var want = sheet.Value.EnumerateArray().ToList();
            Assert.Equal(want.Count, frames.Count);
            for (int i = 0; i < want.Count; ++i)
            {
                var f = frames[i];
                string? hash = f.Mask is null ? null : Golden.Sha256(f.Mask.Select(b => b != 0 ? (byte)1 : (byte)0).ToArray());
                Assert.True((want[i][0].GetInt32(), want[i][1].GetInt32(), want[i][2].GetInt32(), want[i][3].GetInt32(),
                        want[i][4].ValueKind == JsonValueKind.Null ? null : want[i][4].GetString())
                    == (f.Width, f.Height, f.HotSpotX, f.HotSpotY, hash), $"{sheet.Name} frame {i}");
            }
        }
    }

    // A surface with deposits right where a still lander comes down, so it
    // touches them on its first frame on the ground.
    private static (PlanetsideSurface, Lander, ShipHold) Among(params (int Element, int Size, int Amount)[] deposits)
    {
        const uint seed = 77;
        var probe = SendDown(new PlanetsideSurface(new TfbRandom(seed)), Hold(50), 484, 148);
        var minerals = deposits.Select((d, i) => new SurfaceMineral(i, probe.X >> 2, probe.Y >> 2, d.Element, (d.Amount << 8) | d.Size));
        var surface = new PlanetsideSurface(new TfbRandom(seed), minerals, Systems.Generator.Types.ElementCategory, TestContent.Shapes);
        var hold = Hold(50);
        var lander = SendDown(surface, hold, 484, 148);
        while (lander.Phase != LanderPhase.Ground)
            surface.Step(_ => LanderInput.None);
        return (surface, lander, hold);
    }

    [ContentFact]
    public void ALanderTakesWhatItHasRoomForAndLeavesTheRest()
    {
        // Two deposits of 40 under the lander: the first taken (node 1, last
        // node first) fits, the second only in part.
        var (surface, lander, hold) = Among((0, 1, 40), (1, 1, 40));
        surface.Step(_ => LanderInput.None);
        Assert.Equal(50, lander.MineralMass);
        Assert.Equal(2, surface.Taken.Count);
        Assert.Equal(10, lander.Text!.Value.Amount);          // the last pickup's amount
        Assert.Equal(Lander.TextFrames - 1, lander.Text.Value.Frames);
        surface.Step(_ => LanderInput.None);
        Assert.Equal(0, surface.Elements.Count(e => e.Kind == SurfaceElementKind.Mineral));   // both gone
        while (lander.Phase != LanderPhase.Gone)
            surface.Step(l => l.Phase == LanderPhase.Ground ? LanderInput.TakeOff : LanderInput.None);
        // 40 of element 1, then 10 of element 0, each into its category.
        var want = new int[ShipHold.Categories];
        want[Systems.Generator.Types.ElementCategory(1)] += 40;
        want[Systems.Generator.Types.ElementCategory(0)] += 10;
        Assert.Equal(50, hold.MineralMass);
        Assert.Equal(want, hold.Minerals);
    }

    [ContentFact]
    public void AFullLanderLeavesADepositWhereItIs()
    {
        // The last node is first in the queue (GeneratePlanetSide), so node 1 fills the lander.
        var (surface, lander, _) = Among((0, 1, 30), (0, 1, 50));
        surface.Step(_ => LanderInput.None);
        Assert.Equal(50, lander.MineralMass);
        Assert.Equal((ScanType.Mineral, 1), Assert.Single(surface.Taken));   // node 0 stays
        for (int i = 0; i < 3; ++i)
            surface.Step(_ => LanderInput.None);
        Assert.Equal(1, surface.Elements.Count(e => e.Kind == SurfaceElementKind.Mineral));
    }

    [Fact]
    public void TfbRandomIsUqms()
    {
        // TFB_Random from seed 1: the textbook generator's first numbers
        // match it, until a step goes negative.
        var r = new TfbRandom(1);
        Assert.Equal(16807u, r.Next());
        Assert.Equal(282475249u, r.Next());
        Assert.Equal(1622650073u, r.Next());
        Assert.Equal(1u, new TfbRandom(0).Seed);
        Assert.Equal(1u, new TfbRandom(2147483648u).Seed);
    }

    [Fact]
    public void TheLanderComesDownNearTheSiteAndOffThePoles()
    {
        for (uint seed = 1; seed < 500; ++seed)
        {
            var north = SendDown(new PlanetsideSurface(new TfbRandom(seed)), Hold(12), 0, 0);
            Assert.InRange(north.Y, 0, Lander.RandomMiss);
            Assert.True(north.X <= Lander.RandomMiss || north.X >= PlanetsideMap.Width - Lander.RandomMiss, $"x {north.X}");
            var south = SendDown(new PlanetsideSurface(new TfbRandom(seed)), Hold(12), 0, PlanetsideMap.Height - 1);
            Assert.InRange(south.Y, PlanetsideMap.Height - 1 - Lander.RandomMiss, PlanetsideMap.Height - 1);
        }
        Assert.Equal(new[] { 1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 66, 78, 91 }, Lander.Heights);
    }
}
