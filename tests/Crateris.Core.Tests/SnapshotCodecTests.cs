// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Stopwatch = System.Diagnostics.Stopwatch;
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

    private static ShipSnapshot AnyShip(Random r, string id) => new(
        id, AnyText(r), r.Next(2) == 0, (ShipKind)r.Next(2),
        AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r),
        (uint)r.NextInt64(0, uint.MaxValue + 1L), (uint)r.NextInt64(0, uint.MaxValue + 1L), r.Next(2) == 0, r.Next(2) == 0,
        (Activity)r.Next(4), AnyOptional(r), AnyOptional(r),
        AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyInt(r), AnyOptional(r), AnyOptional(r),
        AnyInt(r), AnyInt(r), (short)r.Next(short.MinValue, short.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1),
        r.Next(3) == 0 ? null : new StarEntryState(r.NextInt64(long.MinValue, long.MaxValue), AnyInt(r), AnyInt(r),
            (short)r.Next(short.MinValue, short.MaxValue + 1), (short)r.Next(short.MinValue, short.MaxValue + 1),
            AnyInt(r), AnyInt(r), AnyInt(r), r.Next(2) == 0 ? null : AnyText(r)),
        r.Next(3) == 0 ? null : new HoldSnapshot(AnyInt(r), AnyInt(r), AnyInt(r),
            Enumerable.Range(0, r.Next(9)).Select(_ => AnyInt(r)).ToArray(), AnyInt(r), (LanderUpgrades)r.Next(128)));

    private static WorldSnapshot AnySnapshot(Random r, long frame) => new(frame, AnyInt(r), AnyInt(r), AnyInt(r),
        Enumerable.Range(0, r.Next(5)).Select(i => AnyShip(r, "p" + r.Next(6))).DistinctBy(s => s.Id).ToArray(),
        r.Next(3) == 0 ? null : Enumerable.Range(0, r.Next(4)).Select(_ => new OrbitedWorldSnapshot(AnyInt(r), AnyInt(r),
            AnyOptional(r), (uint)r.NextInt64(0, uint.MaxValue + 1L), (uint)r.NextInt64(0, uint.MaxValue + 1L),
            (uint)r.NextInt64(0, uint.MaxValue + 1L))).ToArray());

    // A snapshot a little on from another: most ships move a little, some
    // change something else, ships come and go now and then.
    private static WorldSnapshot Next(Random r, WorldSnapshot s, long frame)
    {
        var ships = s.Ships.Select(sh => r.Next(10) switch
        {
            0 => AnyShip(r, sh.Id),
            1 => sh with { Hold = r.Next(2) == 0 ? null : new HoldSnapshot(r.Next(50), 1, 500, new int[8], r.Next(50)) },
            2 => sh with { Name = AnyText(r), Entry = null },
            3 => sh,
            _ => sh with { LogX = sh.LogX + r.Next(-20, 21), LogY = unchecked(sh.LogY + r.Next(-20, 21)), Facing = r.Next(16) },
        }).ToList();
        if (r.Next(10) == 0 && ships.Count > 0)
            ships.RemoveAt(r.Next(ships.Count));
        if (r.Next(10) == 0 && ships.All(sh => sh.Id != "new" + frame))
            ships.Insert(r.Next(ships.Count + 1), AnyShip(r, "new" + frame));
        return new WorldSnapshot(frame, s.Year, r.Next(20) == 0 ? s.Month + 1 : s.Month, s.Day, ships,
            r.Next(10) == 0 ? AnySnapshot(r, 0).OrbitedWorlds : s.OrbitedWorlds);
    }

    private static void AssertSame(WorldSnapshot want, WorldSnapshot got)
    {
        Assert.Equal((want.Frame, want.Year, want.Month, want.Day), (got.Frame, got.Year, got.Month, got.Day));
        Assert.Equal(want.Ships, got.Ships);
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
}
