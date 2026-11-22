// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Stopwatch = System.Diagnostics.Stopwatch;
using Crateris.Core.Combat;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Xunit.Abstractions;

namespace Crateris.Core.Tests;

/// <summary>
/// Snapshots on the wire (stage 2b, item 4): the binary format, deltas from
/// what each guest has, and the stream over loss and reordering. Run on
/// their own: one of them times the codec.
/// </summary>
[Collection(TimingSensitive.Name)]
[Trait("Topic", "net")]
public class SnapshotCodecTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private readonly ITestOutputHelper _out;

    public SnapshotCodecTests(ITestOutputHelper output) => _out = output;

    // ---- Random snapshots, reaching every field and its extremes ----

    private static int AnyInt(Random r) => r.Next(6) switch
    {
        0 => 0,
        1 => int.MinValue,
        2 => int.MaxValue,
        3 => r.Next(-3, 4),
        _ => r.Next(int.MinValue, int.MaxValue),
    };

    private static int? AnyOptional(Random r) => r.Next(3) == 0 ? null : r.Next(2) == 0 ? AnyInt(r) : r.Next(10);

    private static string AnyText(Random r) => r.Next(4) switch
    {
        0 => "",
        1 => "Zelnick",
        2 => "Ürquan 船 " + r.Next(),
        _ => new string('x', r.Next(300)),
    };

    private static string? AnyName(Random r) => r.Next(3) == 0 ? null : ShipNames.Fit(AnyText(r));

    // The host's fleet (stage 4b item 9): up to 12 ships in some of the slots, in their order.
    private static FleetSnapshot AnyFleet(Random r) => new(Enumerable.Range(0, HostFleet.MaxShips).Where(_ => r.Next(3) == 0)
        .Select(slot => new FleetShip(r.Next(4) == 0 ? AnyText(r) + "x" : "SPATHI_SHIP", r.Next(3) == 0 ? ushort.MaxValue : r.Next(43),
            (byte)r.Next(256), slot))
        .ToArray());

    // A ship, the host's with a fleet (or none) now and then.
    private static ShipSnapshot AnyShip(Random r, string id)
    {
        var ship = AnyShipWithoutFleet(r, id);
        return ship.IsHost && r.Next(3) != 0 ? ship with { Fleet = AnyFleet(r) } : ship;
    }

    private static ShipSnapshot AnyShipWithoutFleet(Random r, string id) => new ShipSnapshot(
        id, AnyText(r), r.Next(2) == 0, (ShipKind)r.Next(2),
        AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r),
        (uint)r.NextInt64(0, uint.MaxValue + 1L), (uint)r.NextInt64(0, uint.MaxValue + 1L), r.Next(2) == 0, r.Next(2) == 0,
        (Activity)r.Next(Enum.GetValues<Activity>().Length), AnyOptional(r), AnyOptional(r),
        AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyOptional(r), AnyOptional(r),
        AnyInt(r), AnyInt(r), (short)r.Next(short.MinValue, short.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1),
        r.Next(3) == 0 ? null : new StarEntryState(r.NextInt64(long.MinValue, long.MaxValue), AnyInt(r), AnyInt(r),
            (short)r.Next(short.MinValue, short.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1),
            AnyInt(r), AnyInt(r), AnyInt(r), r.Next(2) == 0 ? null : AnyText(r)),
        r.Next(3) == 0 ? null : new HoldSnapshot(AnyInt(r), AnyInt(r), AnyInt(r),
            Enumerable.Range(0, r.Next(9)).Select(_ => AnyInt(r)).ToArray(), AnyInt(r), (LanderUpgrades)r.Next(128),
            (uint)r.NextInt64(0, uint.MaxValue + 1L), r.Next(3) == 0 ? 0 : r.Next(int.MaxValue),
            r.Next(3) == 0 ? 0 : (uint)r.NextInt64(0, uint.MaxValue + 1L)))
        with { ShipName = AnyName(r), CaptainName = AnyName(r), Wrecked = r.Next(4) == 0 };

    private static WorldSnapshot AnySnapshot(Random r, long frame) => new WorldSnapshot(frame, AnyInt(r), AnyInt(r), AnyInt(r),
        Enumerable.Range(0, r.Next(5)).Select(i => AnyShip(r, "p" + r.Next(6))).DistinctBy(s => s.Id).ToArray(),
        r.Next(3) == 0 ? null : Enumerable.Range(0, r.Next(4)).Select(_ => new OrbitedWorldSnapshot(AnyInt(r), AnyInt(r),
            AnyOptional(r), (uint)r.NextInt64(0, uint.MaxValue + 1L), (uint)r.NextInt64(0, uint.MaxValue + 1L),
            (uint)r.NextInt64(0, uint.MaxValue + 1L))).ToArray())
        with { StoryPause = r.Next(3) == 0 ? AnyText(r) : null, HostPaused = r.Next(3) == 0, Markers = AnyMarkers(r) };

    private static BattleMarkerSnapshot[]? AnyMarkers(Random r) => r.Next(3) == 0 ? null
        : Enumerable.Range(0, r.Next(4)).Select(i => new BattleMarkerSnapshot(r.Next(1000), AnyOptional(r), AnyOptional(r), AnyInt(r), AnyInt(r), AnyText(r)))
            .ToArray();

    // A snapshot a little on from another: most ships move a little, some
    // change something else, ships come and go now and then.
    private static WorldSnapshot Next(Random r, WorldSnapshot s, long frame)
    {
        var ships = s.Ships.Select(sh => r.Next(10) switch
        {
            0 => AnyShip(r, sh.Id),
            1 => sh with { Hold = r.Next(2) == 0 ? null : new HoldSnapshot(r.Next(50), 1, 500, new int[8], r.Next(50)) },
            2 => sh with { Name = AnyText(r), Entry = null, ShipName = r.Next(2) == 0 ? sh.ShipName : AnyName(r) },
            9 when sh.IsHost => sh with
            {
                Fleet = sh.Fleet is { Ships.Count: > 0 } f && r.Next(2) == 0
                    ? new FleetSnapshot(f.Ships.Select((x, i) => i == 0 ? x with { Crew = x.Crew ^ 1 } : x).ToArray())
                    : r.Next(3) == 0 ? null : AnyFleet(r),
            },
            3 => sh,
            _ => sh with { LogX = sh.LogX + r.Next(-20, 21), LogY = unchecked(sh.LogY + r.Next(-20, 21)), Facing = r.Next(16) },
        }).ToList();
        if (r.Next(10) == 0 && ships.Count > 0)
            ships.RemoveAt(r.Next(ships.Count));
        if (r.Next(10) == 0 && ships.All(sh => sh.Id != "new" + frame))
            ships.Insert(r.Next(ships.Count + 1), AnyShip(r, "new" + frame));
        return new WorldSnapshot(frame, s.Year, r.Next(20) == 0 ? s.Month + 1 : s.Month, s.Day, ships,
            r.Next(10) == 0 ? AnySnapshot(r, 0).OrbitedWorlds : s.OrbitedWorlds,
            StoryPause: r.Next(8) switch { 0 => null, 1 => "p" + r.Next(6), _ => s.StoryPause },
            HostPaused: r.Next(8) == 0 ? !s.HostPaused : s.HostPaused,
            Markers: r.Next(8) == 0 ? AnyMarkers(r) : s.Markers);
    }

    private static void AssertSame(WorldSnapshot want, WorldSnapshot got)
    {
        Assert.Equal((want.Frame, want.Year, want.Month, want.Day), (got.Frame, got.Year, got.Month, got.Day));
        Assert.Equal(want.Ships, got.Ships);
        Assert.Equal(want.StoryPause, got.StoryPause);
        Assert.Equal(want.HostPaused, got.HostPaused);
        Assert.Equal(want.Markers ?? Array.Empty<BattleMarkerSnapshot>(), got.Markers ?? Array.Empty<BattleMarkerSnapshot>());
        if (want.OrbitedWorlds is null)
            Assert.Null(got.OrbitedWorlds);
        else
            Assert.Equal(want.OrbitedWorlds, got.OrbitedWorlds!);
    }

    private static byte[] Encode(WorldSnapshot s, WorldSnapshot? b = null)
    {
        var w = new WireWriter(16);
        SnapshotCodec.Encode(w, s, b);
        return w.Written.ToArray();
    }

    [Fact]
    public void EveryFieldSurvivesAFullSnapshot()
    {
        var r = new Random(1);
        for (int i = 0; i < 2000; ++i)
        {
            var s = AnySnapshot(r, r.NextInt64(0, long.MaxValue));
            AssertSame(s, SnapshotCodec.Decode(Encode(s)));
        }
    }

    [Fact]
    public void EveryFieldSurvivesADelta()
    {
        var r = new Random(2);
        for (int i = 0; i < 2000; ++i)
        {
            var b = AnySnapshot(r, r.Next(1000));
            var s = r.Next(2) == 0 ? AnySnapshot(r, b.Frame + 1 + r.Next(30)) : Next(r, b, b.Frame + 1 + r.Next(30));
            var bytes = Encode(s, b);
            Assert.Equal((s.Frame, (long?)b.Frame), SnapshotCodec.Peek(bytes));
            AssertSame(s, SnapshotCodec.Decode(bytes, b));
        }
    }

    [Fact]
    public void AShipThatDidntChangeCostsTwoBytesAndIsTheBasesOwnRecord()
    {
        var r = new Random(3);
        var b = AnySnapshot(r, 10) with { Ships = new[] { AnyShip(r, "a"), AnyShip(r, "b") } };
        var s = b with { Frame = 11 };
        var bytes = Encode(s, b);
        // kind, frame, back, parts, count, then each ship's base place and empty mask.
        Assert.Equal(1 + 1 + 1 + 1 + 1 + 2 * 2, bytes.Length);
        var got = SnapshotCodec.Decode(bytes, b);
        Assert.Same(b.Ships[0], got.Ships[0]);
        Assert.Same(b.OrbitedWorlds, got.OrbitedWorlds);
    }

    [Fact]
    public void TheRoomALandersKeepsIsSentWhenItAloneChanges()
    {
        var r = new Random(5);
        var hold = new HoldSnapshot(10, 1, 500, new int[8], 0);
        var b = AnySnapshot(r, 10) with { Ships = new[] { AnyShip(r, "a") with { Hold = hold } } };
        var s = b with { Frame = 11, Ships = new[] { b.Ships[0] with { Hold = hold with { LanderRoom = 50 } } } };
        Assert.Equal(50, SnapshotCodec.Decode(Encode(s, b), b).Ships[0].Hold!.LanderRoom);
        Assert.Equal(50, SnapshotCodec.Decode(Encode(s)).Ships[0].Hold!.LanderRoom);
        Assert.Equal(450, s.Ships[0].Hold!.RoomForCargo);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Encode(s with
        {
            Ships = new[] { s.Ships[0] with { Hold = hold with { LanderRoom = -1 } } },
        })));
    }

    [Fact]
    public void ADeltaNeedsItsOwnBase()
    {
        var r = new Random(4);
        var b = AnySnapshot(r, 5);
        var s = Next(r, b, 9);
        var bytes = Encode(s, b);
        Assert.Throws<ArgumentException>(() => SnapshotCodec.Decode(bytes));
        Assert.Throws<ArgumentException>(() => SnapshotCodec.Decode(bytes, b with { Frame = 6 }));
        Assert.Throws<ArgumentException>(() => Encode(b, s));   // a base after the snapshot
        Assert.Null(new SnapshotReceiver().Receive(bytes));      // not kept: dropped
    }

    [Fact]
    public void BadDataIsRefusedAsBadData()
    {
        var r = new Random(5);
        for (int i = 0; i < 3000; ++i)
        {
            var b = AnySnapshot(r, 3);
            var bytes = Encode(Next(r, b, 4), r.Next(2) == 0 ? b : null);
            // Cut short, a byte changed, or bytes added.
            byte[] bad = r.Next(3) switch
            {
                0 => bytes[..r.Next(bytes.Length)],
                1 => bytes.Select((x, k) => k == r.Next(bytes.Length) ? (byte)r.Next(256) : x).ToArray(),
                _ => bytes.Concat(new[] { (byte)r.Next(256) }).ToArray(),
            };
            try
            {
                var (_, baseFrame) = SnapshotCodec.Peek(bad);
                SnapshotCodec.Decode(bad, baseFrame is long f ? b with { Frame = f } : null);
            }
            catch (InvalidDataException)
            {
            }
            catch (ArgumentException)
            {
                // a changed byte named another base frame
            }
        }
    }

    [Fact]
    public void ADeltaCantRenameAShipOrRepeatOne()
    {
        var r = new Random(9);
        var b = new WorldSnapshot(1, 2155, 2, 17, new[] { AnyShip(r, "a") });
        // A delta whose ship from the base comes with a new id.
        var w = new WireWriter();
        w.Byte(1);              // a delta
        w.UInt(2);              // frame 2
        w.UInt(1);              // from frame 1
        w.Byte(0);              // the date and worlds as the base's
        w.UInt(1);              // one ship
        w.UInt(1);              // the base's first
        w.UInt(1UL << 24);      // its id changed
        w.String("b");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(w.Written, b));
        // Two ships with one id.
        var twice = b with { Ships = new[] { AnyShip(r, "a"), AnyShip(r, "a") } };
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Encode(twice)));
        // The host's pause is a byte, 0 or 1: anything else is refused.
        var paused = Encode(new WorldSnapshot(1, 2026, 1, 1, Array.Empty<ShipSnapshot>(), HostPaused: true));
        Assert.True(SnapshotCodec.Decode(paused).HostPaused);
        Assert.Equal((1, 0), (paused[^2], paused[^1]));   // the pause's byte, then the ships' count
        paused[^2] = 2;
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(paused));
    }

    // ---- The stream: the host's history and acknowledgements ----

    [Fact]
    public void TheStreamRebuildsEverySnapshotOverLossAndReordering()
    {
        var r = new Random(6);
        var sender = new SnapshotSender();
        var receiver = new SnapshotReceiver();
        var sent = new Dictionary<long, WorldSnapshot>();
        var inFlight = new List<(int Due, byte[] Bytes)>();
        var s = AnySnapshot(r, 0);
        long newest = -1;
        int full = 0, deltas = 0, rebuilt = 0, dropped = 0;
        for (int frame = 1; frame <= 3000; ++frame)
        {
            s = Next(r, s, frame);
            sender.Record(s);
            sent[frame] = s;
            var bytes = sender.For(1);
            if (SnapshotCodec.Peek(bytes).Base is null)
                full++;
            else
                deltas++;
            // A long outage now and then; otherwise a fifth lost, the rest
            // arriving up to 3 frames late, so some overtake others.
            bool outage = frame % 500 is >= 200 and < 240;
            if (!outage && r.Next(5) != 0)
                inFlight.Add((frame + r.Next(4), bytes));
            foreach (var m in inFlight.Where(m => m.Due <= frame).ToList())
            {
                inFlight.Remove(m);
                var got = receiver.Receive(m.Bytes);
                if (got is null)
                {
                    dropped++;
                    continue;
                }
                AssertSame(sent[got.Frame], got);
                rebuilt++;
                newest = Math.Max(newest, got.Frame);
            }
            // The guest's input says what it has; a fifth of inputs are lost too.
            if (newest >= 0 && !outage && r.Next(5) != 0)
                sender.Acknowledge(1, newest);
        }
        _out.WriteLine($"{full} full, {deltas} deltas, {rebuilt} rebuilt, {dropped} dropped (base not kept)");
        Assert.True(rebuilt > 2000, $"only {rebuilt} rebuilt");
        Assert.True(deltas > full * 10, $"{deltas} deltas for {full} full");
        Assert.True(full >= 6, "a full snapshot after each outage");   // the first, then after each long outage
    }

    [Fact]
    public void AcknowledgementsOfFramesNeverSentAreIgnored()
    {
        var r = new Random(7);
        var sender = new SnapshotSender();
        long? BaseOf(byte[] message) => SnapshotCodec.Peek(message).Base;
        var s = AnySnapshot(r, 10);
        sender.Record(s);
        sender.Acknowledge(1, 10);                // not sent to it yet
        Assert.Null(BaseOf(sender.For(1)));       // frame 10, in full
        sender.Record(s = Next(r, s, 11));
        sender.Acknowledge(1, 9);                 // before it joined
        sender.Acknowledge(1, 11);                // not sent yet
        Assert.Null(BaseOf(sender.For(1)));       // frame 11, in full
        sender.Acknowledge(1, 10);
        sender.Record(s = Next(r, s, 12));
        Assert.Equal(10, BaseOf(sender.For(1)));
        sender.Acknowledge(1, 9);                 // older than it said: ignored
        sender.Record(s = Next(r, s, 13));
        Assert.Equal(10, BaseOf(sender.For(1)));

        // Silent for longer than the history: a full snapshot again.
        for (int f = 14; f < 14 + SnapshotSender.History; ++f)
            sender.Record(s = Next(r, s, f));
        Assert.Null(BaseOf(sender.For(1)));
        sender.Acknowledge(1, s.Frame);
        sender.Forget(1);                         // gone: what it had is forgotten
        sender.Record(s = Next(r, s, s.Frame + 1));
        Assert.Null(BaseOf(sender.For(1)));
        Assert.Throws<ArgumentException>(() => sender.Record(s));
    }

    [Fact]
    public void GuestsOnTheSameBaseShareOneMessage()
    {
        var r = new Random(8);
        var sender = new SnapshotSender();
        var s = AnySnapshot(r, 1);
        sender.Record(s);
        var first = sender.For(1);
        Assert.Same(first, sender.For(2));        // both in full
        sender.Acknowledge(1, 1);
        sender.Acknowledge(2, 1);
        sender.Record(s = Next(r, s, 2));
        var delta = sender.For(1);
        Assert.Same(delta, sender.For(2));
        Assert.Null(SnapshotCodec.Peek(sender.For(3)).Base);   // a new guest: its own, in full
        Assert.NotSame(delta, sender.For(3));
    }

    // ---- A real game: size, time and the host session ----

    private static HostSession FourPlayers(out SystemCatalog systems)
    {
        systems = SystemCatalog.Load(DataDir);
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "host", "Zelnick",
            systems, TestContent.Shapes);
        for (int g = 1; g <= 3; ++g)
            host.Hello(g, "guest" + g, "Guest " + g, Protocol.Version);
        return host;
    }

    [ContentFact]
    public void AFourPlayerSnapshotFitsInOnePacketAndADeltaInAFewBytes()
    {
        var host = FourPlayers(out var systems);
        host.World.PutInOrbit("guest1", 82, 2, 1);   // Luna
        host.World.PutInOrbit("guest2", 82, 4);      // Jupiter
        var flying = ShipInput.Thrust | ShipInput.Left;
        WorldSnapshot snap = host.Step(flying);
        for (int i = 0; i < 100; ++i)
            snap = host.Step(flying);
        var writer = new WireWriter();
        SnapshotCodec.Encode(writer, snap);
        int fullSize = writer.Length;
        byte[] json = Protocol.Encode(snap);
        var before = snap;
        snap = host.Step(flying);
        SnapshotCodec.Encode(writer, snap, before);
        int movingDelta = writer.Length;
        // Everyone still: the host stopped, the others in orbit or idle.
        host.World.PutInOrbit("host", 82, 2);
        for (int i = 0; i < 5; ++i)
            snap = host.Step(ShipInput.None);
        before = snap;
        snap = host.Step(ShipInput.None);
        SnapshotCodec.Encode(writer, snap, before);
        int stillDelta = writer.Length;
        _out.WriteLine($"JSON {json.Length} bytes; full {fullSize}; delta with the host flying {movingDelta}; with nobody moving {stillDelta}");
        Assert.True(fullSize < 600, $"a full snapshot is {fullSize} bytes");
        Assert.True(fullSize * 4 < json.Length, $"{fullSize} bytes against JSON's {json.Length}");
        Assert.True(movingDelta < 40, $"a delta with one ship flying is {movingDelta} bytes");
        Assert.True(stillDelta <= 6 + 2 * snap.Ships.Count, $"a delta with nobody moving is {stillDelta} bytes");
    }

    [ContentFact]
    public void EncodingDoesntAllocateAndBothWaysTakeMicroseconds()
    {
        var host = FourPlayers(out _);
        host.World.AddFleetShips("ARILOU_SHIP", 3);   // the host's fleet in the host's ship (stage 4b item 9)
        WorldSnapshot before = host.Step(ShipInput.Thrust), snap = before;
        for (int i = 0; i < 50; ++i)
            (before, snap) = (snap, host.Step(ShipInput.Thrust | ShipInput.Right));
        var writer = new WireWriter();
        for (int i = 0; i < 1000; ++i)
        {
            SnapshotCodec.Encode(writer, snap);
            SnapshotCodec.Encode(writer, snap, before);
        }
        const int n = 20000;
        var clock = new Stopwatch();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        clock.Start();
        for (int i = 0; i < n; ++i)
            SnapshotCodec.Encode(writer, snap);
        double encodeFull = clock.Elapsed.TotalMicroseconds / n;
        clock.Restart();
        for (int i = 0; i < n; ++i)
            SnapshotCodec.Encode(writer, snap, before);
        double encodeDelta = clock.Elapsed.TotalMicroseconds / n;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        SnapshotCodec.Encode(writer, snap);
        byte[] full = writer.Written.ToArray();
        SnapshotCodec.Encode(writer, snap, before);
        byte[] delta = writer.Written.ToArray();
        for (int i = 0; i < 1000; ++i)
            SnapshotCodec.Decode(full);
        clock.Restart();
        for (int i = 0; i < n; ++i)
            SnapshotCodec.Decode(full);
        double decodeFull = clock.Elapsed.TotalMicroseconds / n;
        clock.Restart();
        for (int i = 0; i < n; ++i)
            SnapshotCodec.Decode(delta, before);
        double decodeDelta = clock.Elapsed.TotalMicroseconds / n;
        _out.WriteLine($"encode full {encodeFull:0.00} µs, delta {encodeDelta:0.00} µs; decode full {decodeFull:0.00} µs, delta {decodeDelta:0.00} µs");
        // Coverage (mapping the topics) counts every line, which allocates and takes time.
        if (Golden.Mapping)
            return;
        Assert.Equal(0, allocated);
        // Loose bounds, so a slow or busy test machine passes; JSON took 23 and 31 µs.
        Assert.True(encodeFull < 15 && encodeDelta < 15, $"encoding takes {encodeFull:0.0} / {encodeDelta:0.0} µs");
        Assert.True(decodeFull < 20 && decodeDelta < 20, $"decoding takes {decodeFull:0.0} / {decodeDelta:0.0} µs");
    }

    [ContentFact]
    public void TheHostSendsEachGuestADeltaFromWhatItsInputSaysItHas()
    {
        var host = FourPlayers(out _);
        var guest = new SnapshotReceiver();
        int delay = host.World.Settings.InputDelayFrames;
        var snap = host.Step(ShipInput.None);
        var first = host.SnapshotFor(1);
        Assert.Null(SnapshotCodec.Peek(first).Base);                  // nothing acknowledged: in full
        Assert.Equal(snap.Ships, guest.Receive(first)!.Ships);
        host.GuestInput(1, snap.Frame + delay, 0, snap.Frame);         // "I have this frame"
        var next = host.Step(ShipInput.Thrust);
        var bytes = host.SnapshotFor(1);
        Assert.Equal(snap.Frame, SnapshotCodec.Peek(bytes).Base);
        Assert.Equal(next.Ships, guest.Receive(bytes)!.Ships);
        Assert.Null(SnapshotCodec.Peek(host.SnapshotFor(2)).Base);    // another guest has said nothing

        // A guest leaving and coming back starts again in full.
        host.GuestLeft(1);
        host.Hello(1, "guest1", "Guest 1", Protocol.Version);
        host.Step(ShipInput.None);
        Assert.Null(SnapshotCodec.Peek(host.SnapshotFor(1)).Base);
        Assert.Equal(new long[] { 1, 2, 3 }, host.GuestPeers.Order());
    }

    // A battle by two players, a and b, far apart in an arena three times UQM's (UQM's widest view of a ship covers
    // all of UQM's own), and the enemy; elements by each, and one across the seam from a.
    private static BattleSnapshot Battle()
    {
        var status = new BattleShipStatus("sis_ship", 30, 50, 10, 42, 0, StatusFlags.Thrust | StatusFlags.Left, 30, 1);
        return new BattleSnapshot(3, 3 * 8192, 3 * 7680, new BattleShipStatus("ilwrath", 22, 22, 16, 16, 7, StatusFlags.Weapon, 0, 9, true), 1000, 100,
            new[] { new BattleFighterSnapshot("a", status, 100, 100, true), new BattleFighterSnapshot("b", status with { Crew = 1 }, 12000, 11000) },
            new[]
            {
                new BattleElementSnapshot(PrimType.Stamp, 100, 100, 0, 0, "ship.sis_ship.big", 3, -1),
                new BattleElementSnapshot(PrimType.StampFill, 1000, 100, 0, 0, "ship.ilwrath.big", 15, 0x123456),
                new BattleElementSnapshot(PrimType.Line, 24500, 23000, 24550, 23050, "battle.stars", 0, 0xFF0000),   // across the seam from a
                new BattleElementSnapshot(PrimType.Point, 12000, 11000, 0, 0, "battle.stars", 2, 7),                 // by b, far from a
            }, new BattleCamera(2, -12, 40, 640, -300, 200, false, 345, -98765, 4321), 123456789);
    }

    [Fact]
    public void ABattleIsSentToItsShipsOnlyWithTheElementsNearEach()
    {
        var battle = Battle();
        var s = new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { battle },
            Markers: new[] { new BattleMarkerSnapshot(3, 0, 2, 5, 6, "spathi") });
        WorldSnapshot For(string viewer, WorldSnapshot snap, WorldSnapshot? b = null)
        {
            var w = new WireWriter(16);
            SnapshotCodec.Encode(w, snap, b, viewer);
            return SnapshotCodec.Decode(w.Written, b);
        }
        var a = Assert.Single(For("a", s).Battles!);
        Assert.Equal(battle with { Elements = battle.Elements.Take(3).ToList() }, a);   // b's point is too far; the seam's line near
        var b = Assert.Single(For("b", s).Battles!);
        Assert.Equal(new[] { battle.Elements[3] }, b.Elements);
        Assert.Equal(2, b.Fighters.Count);   // every ship, for the minimap
        Assert.Null(For("c", s).Battles);   // not in it
        Assert.Equal(s.Markers, For("c", s).Markers);
        // Left: a delta from a frame in it says so; the markers, unchanged, are kept from the base.
        var gone = s with { Frame = 11, Battles = Array.Empty<BattleSnapshot>() };
        var d = For("a", gone, s);
        Assert.Null(d.Battles);
        Assert.Equal(s.Markers, d.Markers);
    }

    [Fact]
    public void BadBattlesAreRefused()
    {
        var s = new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { Battle() });
        byte[] For(BattleSnapshot battle)
        {
            var w = new WireWriter(16);
            SnapshotCodec.Encode(w, s with { Battles = new[] { battle } }, null, "a");
            return w.Written.ToArray();
        }
        var good = For(Battle());
        Assert.NotNull(SnapshotCodec.Decode(good).Battles);
        // An element whose kind is no prim, or undrawn: its byte after the elements' count.
        var bad = For(Battle() with { Elements = new[] { new BattleElementSnapshot((PrimType)9, 100, 100, 0, 0, "x", 0, 0) } });
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(bad));
        bad = For(Battle() with { Elements = new[] { new BattleElementSnapshot(PrimType.None, 100, 100, 0, 0, "x", 0, 0) } });
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(bad));
        // A view state or a step zoom past UQM's, a zoom below none.
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(For(Battle() with { Camera = Battle().Camera with { ViewState = 3 } })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(For(Battle() with { Camera = new BattleCamera(0, 0, 0, 3, 0, 0, true) })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(For(Battle() with { Camera = Battle().Camera with { ZoomOut = -1 } })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(For(Battle() with { Camera = Battle().Camera with { Frame = -1 } })));
        // Cut short anywhere.
        for (int n = good.Length - 1; n > good.Length - 40; --n)
            Assert.ThrowsAny<Exception>(() => SnapshotCodec.Decode(good.AsSpan(0, n)));
    }

    // ---- Stage 4b item 9: the host's fleet, the markers' race, the captain's windows, the pick screen ----

    private static readonly string[] SomeSheets = { "ship.vux.graphics.limpets.large", "ship.orz.graphics.turret.large", "", "x" };

    private static BattleShipStatus AnyStatus(Random r) => new(r.Next(2) == 0 ? "sis_ship" : "shofixti", r.Next(43), 42, r.Next(30), 30,
        r.Next(256), (StatusFlags)r.Next(64), r.Next(43), r.Next(30), r.Next(2) == 0, r.Next(3) == 0 ? ushort.MaxValue : r.Next(20),
        r.Next(3) == 0 ? null : Enumerable.Range(0, r.Next(6)).Select(i => new SilhouetteMarkSnapshot(SomeSheets[r.Next(SomeSheets.Length)],
            (ushort)r.Next(ushort.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1),
            r.Next(2) == 0, i * 1000 + r.Next(1000))).ToArray());   // their ordinals ascending

    private static BattleArmadaSnapshot AnyArmada(Random r) => new(
        Enumerable.Range(0, HostFleet.MaxShips).Where(_ => r.Next(3) == 0).Select(slot => new BattleFleetShipSnapshot(slot,
            r.Next(2) == 0 ? "spathi" : AnyText(r), r.Next(256), r.Next(3) == 0 ? ushort.MaxValue : r.Next(43), r.Next(43),
            r.Next(3) == 0 ? null : (BattleExit)r.Next(4), r.Next(2) == 0, r.Next(2) == 0)).ToArray(),
        r.Next(3) == 0 ? null : new BattlePickSnapshot(r.Next(PickShipMenu.Rows), r.Next(PickShipMenu.Columns + 1)));

    // A battle whose ships are all by one place, so each is sent every element.
    private static BattleSnapshot AnyBattle(Random r) => Battle() with
    {
        Enemy = AnyStatus(r),
        Fighters = Enumerable.Range(0, 1 + r.Next(4)).Select(i => new BattleFighterSnapshot("p" + i, AnyStatus(r), 100, 100, r.Next(2) == 0,
            r.Next(2) == 0 ? AnyArmada(r) : null)).ToArray(),
        Elements = Battle().Elements.Take(2).ToArray(),
    };

    private static WorldSnapshot For(string viewer, WorldSnapshot snap, WorldSnapshot? b = null)
    {
        var w = new WireWriter(16);
        SnapshotCodec.Encode(w, snap, b, viewer);
        return SnapshotCodec.Decode(w.Written, b);
    }

    [Fact]
    public void EveryBattleFieldSurvives()
    {
        var r = new Random(11);
        for (int i = 0; i < 2000; ++i)
        {
            var battle = AnyBattle(r);
            var s = new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { battle });
            var got = Assert.Single(For("p0", s).Battles!);
            Assert.Equal(battle, got);
            // Equal field by field, not only as the records compare.
            Assert.Equal(battle.Enemy.Marks ?? Array.Empty<SilhouetteMarkSnapshot>(), got.Enemy.Marks!);
            Assert.Equal(battle.Enemy.CaptainSpecial, got.Enemy.CaptainSpecial);
            for (int f = 0; f < battle.Fighters.Count; ++f)
            {
                Assert.Equal(battle.Fighters[f].Status.Marks ?? Array.Empty<SilhouetteMarkSnapshot>(), got.Fighters[f].Status.Marks!);
                Assert.Equal(battle.Fighters[f].Armada?.Ships, got.Fighters[f].Armada?.Ships);
                Assert.Equal(battle.Fighters[f].Armada?.Pick, got.Fighters[f].Armada?.Pick);
            }
        }
    }

    [Fact]
    public void TheNewFieldsAreComparedByValue()
    {
        var mark = new SilhouetteMarkSnapshot("s", 3, 4, 5, false);
        var status = new BattleShipStatus("vux", 1, 2, 3, 4, Marks: new[] { mark });
        Assert.Equal(status, status with { Marks = new[] { mark } });
        Assert.NotEqual(status, status with { Marks = new[] { mark with { Erase = true } } });
        Assert.NotEqual(status, status with { CaptainSpecial = 13 });
        Assert.Equal(status with { Marks = null }, status with { Marks = Array.Empty<SilhouetteMarkSnapshot>() });
        var ship = new BattleFleetShipSnapshot(1, "spathi", 5, 6, 30, null, false, true);
        var armada = new BattleArmadaSnapshot(new[] { ship }, new BattlePickSnapshot(1, 2));
        Assert.Equal(armada, armada with { Ships = new[] { ship } });
        Assert.NotEqual(armada, armada with { Ships = new[] { ship with { CanPick = false } } });
        Assert.NotEqual(armada, armada with { Pick = null });
        Assert.Same(ship, armada.InSlot(1));
        Assert.Null(armada.InSlot(2));
        var fleet = new FleetSnapshot(new[] { new FleetShip("SPATHI_SHIP", 3, 5, 2) });
        Assert.Equal(fleet, new FleetSnapshot(new[] { new FleetShip("SPATHI_SHIP", 3, 5, 2) }));
        Assert.NotEqual(fleet, new FleetSnapshot(new[] { new FleetShip("SPATHI_SHIP", 4, 5, 2) }));
        Assert.Equal(3, fleet.InSlot(2)!.Crew);
        Assert.Null(fleet.InSlot(3));
        // The pick's cursor as the pick screen's: its slot, none on the flagship's column.
        Assert.Equal((int?)8, new BattlePickSnapshot(1, 2).Slot);
        Assert.Equal((int?)9, new BattlePickSnapshot(1, 4).Slot);
        Assert.Null(new BattlePickSnapshot(1, 3).Slot);
        Assert.True(new BattlePickSnapshot(0, 3).OnFlagship);
        Assert.False(new BattlePickSnapshot(0, 4).OnFlagship);
    }

    [Fact]
    public void AShipsFleetIsSentWhenItAloneChanges()
    {
        var r = new Random(12);
        var fleet = new FleetSnapshot(new[] { new FleetShip("SPATHI_SHIP", 5, 7, 0), new FleetShip("ARILOU_SHIP", 6, 9, 3) });
        var host = AnyShipWithoutFleet(r, "host") with { IsHost = true, Fleet = fleet };
        var b = new WorldSnapshot(10, 2155, 1, 1, new[] { host, AnyShipWithoutFleet(r, "g") with { IsHost = false } });
        var crewed = b with { Frame = 11, Ships = new[] { host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0], fleet.Ships[1] with { Crew = 2 } }) }, b.Ships[1] } };
        var got = SnapshotCodec.Decode(Encode(crewed, b), b);
        Assert.Equal(crewed.Ships[0].Fleet, got.Ships[0].Fleet);
        Assert.Equal(2, got.Ships[0].Fleet!.InSlot(3)!.Crew);
        Assert.Null(got.Ships[1].Fleet);
        // Unchanged, it costs nothing and is the base's.
        var still = b with { Frame = 12 };
        Assert.Same(b.Ships[0].Fleet, SnapshotCodec.Decode(Encode(still, b), b).Ships[0].Fleet);
        // Gone (a host's ship sent without one) is sent too.
        Assert.Null(SnapshotCodec.Decode(Encode(b with { Frame = 13, Ships = new[] { host with { Fleet = null }, b.Ships[1] } }, b), b).Ships[0].Fleet);
        // An empty fleet is a fleet, not none.
        Assert.Empty(SnapshotCodec.Decode(Encode(b with { Ships = new[] { host with { Fleet = new FleetSnapshot(Array.Empty<FleetShip>()) } } })).Ships[0].Fleet!.Ships);
    }

    // The bytes of a message with one field changed: the place where two messages, differing only there, differ.
    private static byte[] Changed(byte[] good, byte[] other, byte to)
    {
        Assert.Equal(good.Length, other.Length);
        int at = Enumerable.Range(0, good.Length).Single(i => good[i] != other[i]);
        var bad = good.ToArray();
        bad[at] = to;
        return bad;
    }

    [Fact]
    public void BadFleetsMarkersAndArmadasAreRefused()
    {
        var r = new Random(13);
        var host = AnyShipWithoutFleet(r, "host") with { IsHost = true };
        var fleet = new FleetSnapshot(new[] { new FleetShip("SPATHI_SHIP", 5, 7, 0), new FleetShip("ARILOU_SHIP", 6, 9, 3) });
        byte[] Ships(params ShipSnapshot[] ships) => Encode(new WorldSnapshot(1, 2155, 1, 1, ships));
        Assert.Equal(fleet, SnapshotCodec.Decode(Ships(host with { Fleet = fleet })).Ships[0].Fleet);
        // A fleet on a ship not the host's; slots out of order, twice, or past the hangar's; a race of no name; a crew or captain too big.
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { IsHost = false, Fleet = fleet })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(fleet.Ships.Reverse().ToArray()) })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0], fleet.Ships[0] }) })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Slot = 12 } }) })));
        Assert.Equal(11, SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Slot = 11 } }) })).Ships[0].Fleet!.Ships[0].Slot);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Race = "" } }) })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Race = null! } }) })));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Crew = ushort.MaxValue + 1 } }) })));
        Assert.Equal(255, SnapshotCodec.Decode(Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Captain = 255 } }) }))
            .Ships[0].Fleet!.Ships[0].Captain);
        // More than the hangar's 12 ships: only 12 are sent.
        var thirteen = new FleetSnapshot(Enumerable.Range(0, 13).Select(i => fleet.Ships[0] with { Slot = i }).ToArray());
        Assert.Equal(12, SnapshotCodec.Decode(Ships(host with { Fleet = thirteen })).Ships[0].Fleet!.Ships.Count);

        // A marker with no race.
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Encode(new WorldSnapshot(1, 2155, 1, 1, Array.Empty<ShipSnapshot>(),
            Markers: new[] { new BattleMarkerSnapshot(1, null, null, 2, 3, null!) }))));

        // The battle's: a fleet ship's slots, exit, flags; the pick's row and column; a mark's sheet and flag; the captain's window's frame.
        var ship = new BattleFleetShipSnapshot(2, "spathi", 5, 6, 30, BattleExit.Retreated, true, false);
        var armada = new BattleArmadaSnapshot(new[] { ship, ship with { Slot = 4 } }, new BattlePickSnapshot(1, 6));
        var mark = new SilhouetteMarkSnapshot("ship.vux.graphics.limpets.large", 5, -3, 40, false);
        var good = Battle() with
        {
            Fighters = new[] { Battle().Fighters[0] with { Armada = armada, Status = Battle().Fighters[0].Status with { Marks = new[] { mark }, CaptainSpecial = 12 } } },
            Elements = Array.Empty<BattleElementSnapshot>(),
        };
        byte[] Of(BattleSnapshot b) { var w = new WireWriter(16); SnapshotCodec.Encode(w, new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { b }), null, "a"); return w.Written.ToArray(); }
        BattleSnapshot With(BattleArmadaSnapshot? a = null, SilhouetteMarkSnapshot? m = null, int special = 12) => good with
        {
            Fighters = new[] { good.Fighters[0] with { Armada = a ?? armada, Status = good.Fighters[0].Status with { Marks = new[] { m ?? mark }, CaptainSpecial = special } } },
        };
        var bytes = Of(good);
        Assert.Equal(good, Assert.Single(SnapshotCodec.Decode(bytes).Battles!));
        void Refused(BattleSnapshot b) => Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Of(b)));
        Refused(With(armada with { Ships = new[] { ship with { Slot = 4 }, ship } }));
        Refused(With(armada with { Ships = new[] { ship, ship } }));
        Refused(With(armada with { Ships = new[] { ship with { Slot = 12 } } }));
        Refused(With(armada with { Ships = new[] { ship with { Ship = null! } } }));
        Refused(With(armada with { Ships = new[] { ship with { Exit = (BattleExit)4 } } }));
        Refused(With(armada with { Ships = new[] { ship with { Captain = 256 } } }));
        Refused(With(armada with { Ships = new[] { ship with { Crew = ushort.MaxValue + 1 } } }));
        Refused(With(armada with { Ships = new[] { ship with { MaxCrew = ushort.MaxValue + 1 } } }));
        Refused(With(armada with { Pick = new BattlePickSnapshot(2, 0) }));
        Refused(With(armada with { Pick = new BattlePickSnapshot(0, 7) }));
        Refused(With(special: ushort.MaxValue + 1));
        Assert.Equal(BattleExit.Destroyed, SnapshotCodec.Decode(Of(With(armada with { Ships = new[] { ship with { Exit = BattleExit.Destroyed, Slot = 11 } } })))
            .Battles![0].Fighters[0].Armada!.Ships[0].Exit);
        var twelve = Enumerable.Range(0, 12).Select(i => ship with { Slot = i }).ToArray();
        Assert.Equal(twelve, Assert.Single(SnapshotCodec.Decode(Of(With(armada with { Ships = twelve.Append(ship with { Slot = 12 }).ToArray() })))
            .Battles!).Fighters[0].Armada!.Ships);   // only the hangar's 12 are sent
        // Bytes the encoder never writes: a fleet ship's flags past flown and pickable, a mark's flag past 1, a mark's
        // sheet past the battle's; and a pick's row past the rows.
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Changed(bytes, Of(With(armada with { Ships = new[] { ship with { CanPick = true }, ship with { Slot = 4 } } })), 4)));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Changed(bytes, Of(With(armada with { Pick = new BattlePickSnapshot(0, 6) })), 2)));
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Changed(bytes, Of(With(m: mark with { Erase = true })), 2)));
        var sheetAt = Changed(bytes, Of(With(m: mark with { Frame = 6 })), 0);   // the frame's byte; the sheet's is before it
        int frameAt = Enumerable.Range(0, bytes.Length).Single(i => bytes[i] != sheetAt[i]);
        var badSheet = bytes.ToArray();
        badSheet[frameAt - 1] = 1;   // the battle has one sheet, the mark's
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(badSheet));
        badSheet[frameAt - 1] = 0;
        Assert.Equal("ship.vux.graphics.limpets.large", SnapshotCodec.Decode(badSheet).Battles![0].Fighters[0].Status.Marks![0].Sheet);
        // A mark's frame past a frame's 65535 (as a number of three bytes).
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Spliced(bytes, frameAt, 0x80, 0x80, 0x04)));
        Assert.Equal(65535, SnapshotCodec.Decode(Spliced(bytes, frameAt, 0xFF, 0xFF, 0x03)).Battles![0].Fighters[0].Status.Marks![0].Frame);
        // The armada's presence and the pick's past 1: the byte after the fighter's Followed, and the byte before the pick's row.
        var followed = Of(good with { Fighters = new[] { good.Fighters[0] with { Followed = !good.Fighters[0].Followed } } });
        int followedAt = Enumerable.Range(0, bytes.Length).Single(i => bytes[i] != followed[i]);
        Assert.Equal(1, bytes[followedAt + 1]);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Changed(bytes, bytes.Select((b, i) => i == followedAt + 1 ? (byte)7 : b).ToArray(), 2)));
        var row = Of(With(armada with { Pick = new BattlePickSnapshot(0, 6) }));
        int rowAt = Enumerable.Range(0, bytes.Length).Single(i => bytes[i] != row[i]);
        Assert.Equal(1, bytes[rowAt - 1]);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Changed(bytes, bytes.Select((b, i) => i == rowAt - 1 ? (byte)7 : b).ToArray(), 2)));

        // A fleet ship's captain past a byte's (as a number of two bytes).
        var fleetBytes = Ships(host with { Fleet = fleet });
        var otherCaptain = Ships(host with { Fleet = new FleetSnapshot(new[] { fleet.Ships[0] with { Captain = 8 }, fleet.Ships[1] }) });
        int captainAt = Enumerable.Range(0, fleetBytes.Length).Single(i => fleetBytes[i] != otherCaptain[i]);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(Spliced(fleetBytes, captainAt, 0x80, 0x02)));
        Assert.Equal(255, SnapshotCodec.Decode(Spliced(fleetBytes, captainAt, 0xFF, 0x01)).Ships[0].Fleet!.Ships[0].Captain);
    }

    // The bytes with the one at a place replaced by others.
    private static byte[] Spliced(byte[] bytes, int at, params byte[] with) => bytes[..at].Concat(with).Concat(bytes[(at + 1)..]).ToArray();

    [Fact]
    public void AMarksSheetIsSentOnceWithTheElementsAndItsMarksPastTheLimitArent()
    {
        var mark = new SilhouetteMarkSnapshot("ship.vux.graphics.limpets.large", 5, -3, 40, false);
        // Past the limit, the newest are sent (the oldest are under them).
        var many = Enumerable.Range(0, SnapshotCodec.MostSilhouetteMarks + 5).Select(i => mark with { X = (short)i, Ordinal = i }).ToArray();
        var b = Battle() with
        {
            Enemy = Battle().Enemy with { Marks = many },
            Elements = new[] { Battle().Elements[0] with { Sheet = mark.Sheet } },
        };
        var w = new WireWriter(16);
        SnapshotCodec.Encode(w, new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { b }), null, "a");
        var bytes = w.Written.ToArray();
        var got = Assert.Single(SnapshotCodec.Decode(bytes).Battles!);
        Assert.Equal(many.Skip(5), got.Enemy.Marks!);
        Assert.Equal(b.Elements, got.Elements);
        // The sheet's name once in the message.
        var name = System.Text.Encoding.UTF8.GetBytes(mark.Sheet);
        Assert.Single(Enumerable.Range(0, bytes.Length - name.Length).Where(i => bytes.AsSpan(i, name.Length).SequenceEqual(name)));

        // The battle's sheets full with its elements': a mark of another sheet is left out, the others sent.
        var full = Battle() with
        {
            Enemy = Battle().Enemy with { Marks = new[] { mark with { Sheet = "s0", Frame = 8 }, mark with { Sheet = "another", Ordinal = 1 }, mark with { Sheet = "s0", Frame = 9, Ordinal = 2 } } },
            Elements = Enumerable.Range(0, SnapshotCodec.MostBattleSheets).Select(i => Battle().Elements[0] with { Sheet = "s" + i }).ToArray(),
        };
        SnapshotCodec.Encode(w, new WorldSnapshot(10, 2155, 1, 1, Array.Empty<ShipSnapshot>(), Battles: new[] { full }), null, "a");
        Assert.Equal(new[] { mark with { Sheet = "s0", Frame = 8 }, mark with { Sheet = "s0", Frame = 9, Ordinal = 2 } },
            SnapshotCodec.Decode(w.Written).Battles![0].Enemy.Marks);
    }

    [Fact]
    public void ManyWrecksFitASnapshot()
    {
        var r = new Random(5);
        var ships = Enumerable.Range(0, 200).Select(i => AnyShip(r, "w" + i) with { Wrecked = true }).ToArray();
        var s = new WorldSnapshot(1, 2155, 1, 1, ships);
        AssertSame(s, SnapshotCodec.Decode(Encode(s)));
    }
}
