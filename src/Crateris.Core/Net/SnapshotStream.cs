// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Net;

/// <summary>
/// The host's side of the snapshot stream (docs/plans/stage-2b.md, item 4):
/// the last <see cref="History"/> snapshots, and for each guest the newest
/// one it's known to have. Each guest is sent a delta from that one; a
/// guest with none (just joined, or silent for longer than the history
/// holds) gets a full snapshot. A lost message costs nothing: the next
/// delta is from the same base, so it covers the lost one too. Guests say
/// which snapshot they have with the input they send every frame. Guests
/// on the same base get the same message, encoded once.
/// </summary>
public sealed class SnapshotSender
{
    /// <summary>Snapshots kept to send deltas from: a second of world frames.</summary>
    public const int History = 24;

    private readonly WorldSnapshot?[] _history = new WorldSnapshot?[History];
    private readonly Dictionary<long, Guest> _guests = new();
    private readonly WireWriter _writer = new();
    // The latest snapshot's messages, by base frame (-1 for the full one).
    private readonly Dictionary<long, byte[]> _messages = new();
    private WorldSnapshot? _latest;

    private sealed class Guest
    {
        /// <summary>The first frame sent to it: it can't have anything older.</summary>
        public long FirstSent = -1;
        /// <summary>The newest frame sent to it.</summary>
        public long LastSent = -1;
        /// <summary>The newest frame it's known to have, or -1.</summary>
        public long Acknowledged = -1;
    }

    /// <summary>The latest snapshot, to be sent to every guest.</summary>
    public void Record(WorldSnapshot snapshot)
    {
        if (_latest is not null && snapshot.Frame <= _latest.Frame)
            throw new ArgumentException("snapshots are recorded in frame order", nameof(snapshot));
        _latest = snapshot;
        _history[snapshot.Frame % History] = snapshot;
        _messages.Clear();
    }

    /// <summary>
    /// The latest snapshot as the message for one guest: a delta from the
    /// newest snapshot it has, if that's still kept, otherwise in full.
    /// Guests on the same base share the message; don't change it.
    /// </summary>
    public byte[] For(long peer)
    {
        var latest = _latest ?? throw new InvalidOperationException("no snapshot recorded yet");
        if (!_guests.TryGetValue(peer, out var guest))
            _guests[peer] = guest = new Guest();
        var baseSnapshot = BaseFor(guest, latest);
        long key = baseSnapshot?.Frame ?? -1;
        if (!_messages.TryGetValue(key, out var message))
        {
            SnapshotCodec.Encode(_writer, latest, baseSnapshot);
            _messages[key] = message = _writer.Written.ToArray();
        }
        if (guest.FirstSent < 0)
            guest.FirstSent = latest.Frame;
        guest.LastSent = latest.Frame;
        return message;
    }

    /// <summary>
    /// A guest says it has the snapshot of this frame. Frames it can't have
    /// (never sent to it) are ignored, and so are older ones than it has
    /// said before.
    /// </summary>
    public void Acknowledge(long peer, long frame)
    {
        if (_guests.TryGetValue(peer, out var g) && frame >= g.FirstSent && frame <= g.LastSent && frame > g.Acknowledged)
            g.Acknowledged = frame;
    }

    /// <summary>A guest has gone: a returning one starts with a full snapshot.</summary>
    public void Forget(long peer) => _guests.Remove(peer);

    private WorldSnapshot? BaseFor(Guest g, WorldSnapshot latest)
    {
        if (g.Acknowledged < 0 || g.Acknowledged >= latest.Frame)
            return null;
        var kept = _history[g.Acknowledged % History];
        return kept?.Frame == g.Acknowledged ? kept : null;
    }
}

/// <summary>
/// A guest's side of the snapshot stream: decodes each message, full or a
/// delta from a snapshot it has kept, and keeps the last
/// <see cref="Keep"/> (more than the host's history, so it still has any
/// base the host picks).
/// </summary>
public sealed class SnapshotReceiver
{
    public const int Keep = 32;

    private readonly WorldSnapshot?[] _kept = new WorldSnapshot?[Keep];
    private readonly Func<long, WorldSnapshot?> _baseOf;

    public SnapshotReceiver() => _baseOf = Kept;

    /// <summary>
    /// The snapshot a message carries, or null if it's a delta from a
    /// snapshot not kept here (the host then sends a delta from one that is,
    /// or a full one, once the guest's acknowledgements show what it has).
    /// <paramref name="full"/> says whether it was a full snapshot. Bad
    /// data is refused with <see cref="InvalidDataException"/>.
    /// </summary>
    public WorldSnapshot? Receive(ReadOnlySpan<byte> data, out bool full)
    {
        var snapshot = SnapshotCodec.Decode(data, _baseOf, out long? baseFrame);
        full = baseFrame is null;
        if (snapshot is not null)
            _kept[snapshot.Frame % Keep] = snapshot;
        return snapshot;
    }

    /// <inheritdoc cref="Receive(ReadOnlySpan{byte}, out bool)"/>
    public WorldSnapshot? Receive(ReadOnlySpan<byte> data) => Receive(data, out _);

    private WorldSnapshot? Kept(long frame) => _kept[frame % Keep] is { } s && s.Frame == frame ? s : null;
}
