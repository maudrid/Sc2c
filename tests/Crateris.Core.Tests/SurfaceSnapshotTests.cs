// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Planetside;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Xunit.Abstractions;

namespace Crateris.Core.Tests;

/// <summary>
/// Surfaces in snapshots (stage 2b, item 9): each guest gets the surface
/// its lander is on and no other, in full or as a delta from the one it
/// was sent with its base, rebuilt exactly over loss and reordering, and
/// small enough.
/// </summary>
[Trait("Topic", "net")]
public class SurfaceSnapshotTests
{
    private readonly ITestOutputHelper _out;
    public SurfaceSnapshotTests(ITestOutputHelper output) => _out = output;

    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static int AnyInt(Random r) => r.Next(4) switch
    {
        0 => r.Next(int.MinValue, int.MaxValue),
        1 => r.Next(-5, 5),
        _ => r.Next(0, 1000),
    };

    private static LanderSnapshot AnyLander(Random r, string id)
    {
        int took = r.Next(13);
        return new(id, AnyInt(r), AnyInt(r), r.Next(16), AnyInt(r),
            (LanderPhase)r.Next(4), r.Next(took + 1), (LanderFlash)r.Next(3), r.Next(2) == 0, r.Next(7), AnyInt(r), r.Next(101), r.Next(51),
            r.Next(2) == 0 ? null : new MineralText(r.Next(32), AnyInt(r), r.Next(133), AnyInt(r), AnyInt(r)), took);
    }

    private static SurfaceElementSnapshot AnyElement(Random r, int id) => new(id, (SurfaceElementKind)r.Next(7),
        AnyInt(r), AnyInt(r), (SurfaceSheet)r.Next(7), r.Next(60), r.Next(26),
        r.Next(3) == 0 ? null : new Rgb((byte)r.Next(256), (byte)r.Next(256), (byte)r.Next(256)), AnyInt(r), r.Next(21));

    private static SurfaceSnapshot AnySurface(Random r, WorldId w, IEnumerable<string> players, ref int lastId)
    {
        int first = lastId;
        lastId += r.Next(1, 40);
        return new SurfaceSnapshot(w.Star, w.Planet, w.Moon, players.Select(p => AnyLander(r, p)).ToList(),
            Enumerable.Range(first + 1, lastId - first).Where(_ => r.Next(3) != 0).Select(id => AnyElement(r, id)).ToList());
    }

    // A surface a frame on: things move, a few change, some go, new ones
    // come at the head (bolts) and the end (hazards), landers come and go.
    private static SurfaceSnapshot Next(Random r, SurfaceSnapshot s, ref int lastId)
    {
        var elements = s.Elements.Where(_ => r.Next(15) != 0).Select(e => r.Next(6) switch
        {
            0 => AnyElement(r, e.Id),
            1 or 2 => e,
            _ => e with { X = e.X + r.Next(-3, 4), Y = e.Y + r.Next(-3, 4), Frame = (e.Frame + r.Next(2)) % 60 },
        }).ToList();
        if (r.Next(4) == 0)
            elements.Insert(0, AnyElement(r, ++lastId));
        if (r.Next(4) == 0)
            elements.Add(AnyElement(r, ++lastId));
        var landers = s.Landers.Select(l => r.Next(3) == 0 ? l : l with { X = l.X + r.Next(-2, 3), Crew = r.Next(l.CrewTaken + 1) }).ToList();
        if (r.Next(30) == 0 && landers.Count > 1)
            landers.RemoveAt(r.Next(landers.Count));
        if (r.Next(30) == 0 && landers.All(l => l.PlayerId != "other"))
            landers.Add(AnyLander(r, "other"));
        return s with { Landers = landers, Elements = elements,
            DepositsTaken = r.Next(8) == 0 ? new[] { r.Next(32) } : Array.Empty<int>() };
    }

    private static ShipSnapshot Ship(string id, WorldId? landedOn) => new(id, id, id == "host", ShipKind.Escort,
        0, 0, 0, 0, 0, 0, 0, 0, 0, false, false,
        landedOn is null ? Activity.Hyperspace : Activity.Planetside, landedOn?.Star, null, 0, 0, 0, 0, 0, 0,
        landedOn?.Planet, landedOn?.Moon);

    private static void AssertSameSurface(SurfaceSnapshot? want, IReadOnlyList<SurfaceSnapshot>? got, string what)
    {
        if (want is null)
        {
            Assert.True(got is null, $"{what}: a surface it can't see");
            return;
        }
        var s = Assert.Single(got!);
        Assert.Equal(want.World, s.World);
        Assert.Equal(want.Landers, s.Landers);
        Assert.Equal(want.Elements, s.Elements);
        Assert.Equal(want.DepositsTaken ?? Array.Empty<int>(), s.DepositsTaken);
    }

    [Fact]
    public void AGuestRebuildsItsOwnSurfaceOverLossAndReordering()
    {
        // The viewer lands on Luna, takes off, lands on Mars, comes back:
        // it gets its own surface, never the other one.
        var r = new Random(9);
        var luna = new WorldId(82, 2, 1);
        var mars = new WorldId(82, 3);
        int lastLuna = 0, lastMars = 1000;
        var lunaSurface = AnySurface(r, luna, new[] { "viewer", "host" }, ref lastLuna);
        var marsSurface = AnySurface(r, mars, new[] { "other" }, ref lastMars);
        var sender = new SnapshotSender();
        var receiver = new SnapshotReceiver();
        var sent = new Dictionary<long, WorldSnapshot>();
        var inFlight = new List<(int Due, byte[] Bytes)>();
        long newest = -1;
        int rebuilt = 0, withSurface = 0;
        for (int frame = 1; frame <= 3000; ++frame)
        {
            lunaSurface = Next(r, lunaSurface, ref lastLuna);
            marsSurface = Next(r, marsSurface, ref lastMars);
            WorldId? on = (frame / 300 % 4) switch { 0 => luna, 1 => null, 2 => mars, _ => luna };
            var surfaces = new List<SurfaceSnapshot> { lunaSurface, marsSurface };
            var snapshot = new WorldSnapshot(frame, 2155, 3, 1, new[] { Ship("host", luna), Ship("viewer", on) },
                null, surfaces);
            sender.Record(snapshot);
            sent[frame] = snapshot;
            var bytes = sender.For(1, "viewer");
            if (r.Next(5) != 0)
                inFlight.Add((frame + r.Next(4), bytes));
            foreach (var m in inFlight.Where(m => m.Due <= frame).ToList())
            {
                inFlight.Remove(m);
                var got = receiver.Receive(m.Bytes);
                if (got is null)
                    continue;
                var want = SnapshotCodec.SurfaceSeen(sent[got.Frame], "viewer");
                AssertSameSurface(want, got.Surfaces, $"frame {got.Frame}");
                Assert.Equal(sent[got.Frame].Ships, got.Ships);
                rebuilt++;
                if (want is not null)
                    withSurface++;
                newest = Math.Max(newest, got.Frame);
            }
            if (newest >= 0 && r.Next(5) != 0)
                sender.Acknowledge(1, newest);
        }
        Assert.True(rebuilt > 2000 && withSurface > 1000, $"{rebuilt} rebuilt, {withSurface} with a surface");
    }

    [Fact]
    public void ASurfaceDeltaNeedsTheBasesSurface()
    {
        var r = new Random(3);
        var luna = new WorldId(82, 2, 1);
        int last = 0;
        var surface = AnySurface(r, luna, new[] { "viewer" }, ref last);
        var landed = new WorldSnapshot(2, 2155, 3, 1, new[] { Ship("viewer", luna) }, null, new[] { surface });
        var flying = new WorldSnapshot(1, 2155, 3, 1, new[] { Ship("viewer", null) }, null, new[] { surface });
        var w = new WireWriter();
        // From a base where it wasn't landed: the surface comes in full.
        SnapshotCodec.Encode(w, landed, flying, "viewer");
        var got = SnapshotCodec.Decode(w.Written, SnapshotCodec.Decode(Full(flying), null));
        AssertSameSurface(surface, got.Surfaces, "full in a delta");
        // A delta of the surface against a base without one is bad data.
        var next = landed with { Frame = 3 };
        SnapshotCodec.Encode(w, next, landed, "viewer");
        var bytes = w.Written.ToArray();
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(bytes, landed with { Surfaces = null }));
        // No viewer, no surface.
        Assert.Null(SnapshotCodec.Decode(Full(landed)).Surfaces);
    }

    private static byte[] Full(WorldSnapshot s, string? viewer = null)
    {
        var w = new WireWriter();
        SnapshotCodec.Encode(w, s, null, viewer);
        return w.Written.ToArray();
    }

    [ContentFact]
    public void TwoLandersOnLunaFitInAPacketAndADeltaIsSmall()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Zelnick",
            systems, TestContent.Shapes);
        host.Hello(1, "guest", "Guest", Protocol.Version);
        host.World.UndockShown("guest");
        host.World.PutInOrbit("host", 82, 2, 1);
        host.World.PutInOrbit("guest", 82, 2, 1);
        Assert.Equal(DispatchRefusal.None, host.Dispatch("host", 484, 148));
        Assert.Equal(DispatchRefusal.None, host.GuestDispatch(1, 600, 150));
        var receiver = new SnapshotReceiver();
        int fullSize = 0, most = 0, total = 0, n = 0;
        var keys = new[] { ShipInput.Thrust, ShipInput.Thrust | ShipInput.Fire, ShipInput.Left | ShipInput.Thrust };
        for (int frame = 0; frame < 24 * 20; ++frame)
        {
            var input = keys[frame / 30 % keys.Length];
            host.World.SubmitInput("guest", host.World.Frame + 1, input);
            var snap = host.Step(input);
            var bytes = host.SnapshotFor(1);
            var got = receiver.Receive(bytes)!;
            AssertSameSurface(SnapshotCodec.SurfaceSeen(snap, "guest"), got.Surfaces, $"frame {snap.Frame}");
            host.GuestInput(1, host.World.Frame + 1, (int)input, got.Frame);
            if (SnapshotCodec.Peek(bytes).Base is null)
                fullSize = bytes.Length;
            else if (frame > 24 * 3)   // on the ground, driving
            {
                most = Math.Max(most, bytes.Length);
                total += bytes.Length;
                n++;
            }
        }
        var last = host.World.Snapshot();
        var surface = SnapshotCodec.SurfaceSeen(last, "guest")!;
        _out.WriteLine($"full {fullSize} bytes with {surface.Elements.Count} elements; deltas {total / n} on average, {most} at most");
        Assert.Equal(2, surface.Landers.Count);
        Assert.True(fullSize < 1200, $"a full snapshot with a surface is {fullSize} bytes");
        Assert.True(total / n < 200 && most < 600, $"deltas {total / n} on average, {most} at most");
    }
    [ContentFact]
    public void ACrowdedSurfacesSnapshotSize()
    {
        // The world with every hazard at its worst (star 240 planet 1), a
        // lander with every shield driving round until the queue fills.
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Zelnick",
            systems, TestContent.Shapes);
        host.Hello(1, "guest", "Guest", Protocol.Version);
        host.World.UndockShown("guest");
        host.World.PutInOrbit("guest", 240, 1);
        host.World.Find("guest")!.Ship.Hold.Upgrades = LanderUpgrades.All;
        Assert.Equal(DispatchRefusal.None, host.GuestDispatch(1, 484, 148));
        var receiver = new SnapshotReceiver();
        int most = 0, mostElements = 0, fullMost = 0;
        var w = new WireWriter();
        for (int frame = 0; frame < 24 * 60 && host.World.Find("guest")!.Ship.Lander is not null; ++frame)
        {
            var input = (frame / 20 % 3) switch { 0 => ShipInput.Thrust, 1 => ShipInput.Right, _ => ShipInput.Thrust | ShipInput.Fire };
            host.World.SubmitInput("guest", host.World.Frame + 1, input);
            var snap = host.Step(ShipInput.None);
            var bytes = host.SnapshotFor(1);
            var got = receiver.Receive(bytes)!;
            host.GuestInput(1, host.World.Frame + 1, (int)input, got.Frame);
            if (SnapshotCodec.SurfaceSeen(snap, "guest") is not { } surface)
                continue;
            AssertSameSurface(surface, got.Surfaces, $"frame {snap.Frame}");
            SnapshotCodec.Encode(w, snap, null, "guest");
            if (surface.Elements.Count > mostElements)
                (mostElements, fullMost) = (surface.Elements.Count, w.Length);
            if (SnapshotCodec.Peek(bytes).Base is not null)
                most = Math.Max(most, bytes.Length);
        }
        _out.WriteLine($"at most {mostElements} elements: full {fullMost} bytes; deltas {most} at most");
        Assert.True(mostElements > 60, $"only {mostElements} elements");
        Assert.True(most < 800, $"a delta is {most} bytes");
    }
    [ContentFact]
    public void GuestsShareAMessageOnlyWhenTheySeeTheSameSurface()
    {
        var systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Zelnick",
            systems, TestContent.Shapes);
        Assert.Equal(DispatchRefusal.NotInOrbit, host.GuestDispatch(9, 484, 148));   // not a guest
        host.GuestsStartInOrbit = true;
        host.World.PutInOrbit("host", 82, 2, 1);
        for (int g = 1; g <= 3; ++g)
            host.Hello(g, "guest" + g, "Guest " + g, Protocol.Version);
        Assert.All(host.World.Players, p => Assert.Equal(Activity.Orbit, p.Ship.Activity));   // --guests-in-orbit
        host.World.PutInOrbit("guest3", 82, 3);   // Mars
        Assert.Equal(DispatchRefusal.OffTheMap, host.GuestDispatch(1, -1, 148));
        Assert.Equal(DispatchRefusal.None, host.GuestDispatch(1, 484, 148));
        Assert.Equal(DispatchRefusal.None, host.GuestDispatch(2, 600, 148));
        Assert.Equal(DispatchRefusal.None, host.GuestDispatch(3, 484, 148));
        Assert.Equal(DispatchRefusal.None, host.Dispatch("host", 300, 148));
        host.Step(ShipInput.None);
        // Guests 1 and 2 on Luna share one message; 3, on Mars, has its own.
        var a = host.SnapshotFor(1);
        var b = host.SnapshotFor(2);
        var c = host.SnapshotFor(3);
        Assert.Same(a, b);
        Assert.NotSame(a, c);
        Assert.Equal(new WorldId(82, 3), Assert.Single(SnapshotCodec.Decode(c).Surfaces!).World);
        Assert.Equal(new WorldId(82, 2, 1), Assert.Single(SnapshotCodec.Decode(a).Surfaces!).World);
    }
    [Fact]
    public void ALanderThatWouldDrawOutsideUqmsTablesIsRefused()
    {
        var luna = new WorldId(82, 2, 1);
        var good = new LanderSnapshot("viewer", 1, 2, 3, 0, LanderPhase.Ground, 5, LanderFlash.Damage, true, 6, 0, 0, 0,
            new MineralText(31, 4, 132, 0, 0), 12);
        SurfaceSnapshot Surface(LanderSnapshot l, SurfaceElementSnapshot? e = null) => new(luna.Star, luna.Planet, luna.Moon,
            new[] { l }, e is null ? Array.Empty<SurfaceElementSnapshot>() : new[] { e });
        byte[] Bytes(SurfaceSnapshot surface) =>
            Full(new WorldSnapshot(1, 2155, 3, 1, new[] { Ship("viewer", luna) }, null, new[] { surface }), "viewer");
        Assert.NotNull(SnapshotCodec.Decode(Bytes(Surface(good))).Surfaces);
        foreach (var bad in new[]
                 {
                     good with { Facing = 16 }, good with { DamageIndex = 7 }, good with { Text = new MineralText(32, 4, 0, 0, 0) },
                     good with { Text = new MineralText(0, 4, 133, 0, 0) }, good with { Crew = 13 }, good with { CrewTaken = 13 },
                 })
            Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Bytes(Surface(bad))));
        var element = new SurfaceElementSnapshot(1, SurfaceElementKind.Creature, 0, 0, SurfaceSheet.Life, 0, 25, null, 0, 0);
        Assert.NotNull(SnapshotCodec.Decode(Bytes(Surface(good, element))).Surfaces);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Bytes(Surface(good, element with { LifeKind = 26 }))));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Bytes(Surface(good, element with { Frame = -1 }))));
    }
}
