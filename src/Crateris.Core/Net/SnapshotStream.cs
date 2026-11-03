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
/// delta is from the same base, so it covers the lost one too. Guests
/// acknowledge with the input they send anyway: input is stamped for the
/// newest snapshot frame the guest has plus the input delay.
/// </summary>
public sealed class SnapshotSender
{
    /// <summary>Snapshots kept to send deltas from: a second of world frames.</summary>
    public const int History = 24;

    private readonly WorldSnapshot?[] _history = new WorldSnapshot?[History];
    private readonly Dictionary<long, Guest> _guests = new();
    private readonly WireWriter _writer = new();
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
    }

    /// <summary>
    /// The latest snapshot as the message for one guest: a delta from the
    /// newest snapshot it has, if that's still kept, otherwise in full. The
    /// bytes are valid until the next call.
    /// </summary>
    public ReadOnlySpan<byte> For(long peer)
    {
        var latest = _latest ?? throw new InvalidOperationException("no snapshot recorded yet");
        if (!_guests.TryGetValue(peer, out var guest))
            _guests[peer] = guest = new Guest();
        SnapshotCodec.Encode(_writer, latest, BaseFor(guest, latest));
        if (guest.FirstSent < 0)
            guest.FirstSent = latest.Frame;
        guest.LastSent = latest.Frame;
        return _writer.Written;
    }

    /// <summary>Whether the next message for a guest would be a full snapshot.</summary>
    public bool SendsFull(long peer) =>
        _latest is null || !_guests.TryGetValue(peer, out var g) || BaseFor(g, _latest) is null;

    /// <summary>
    /// A guest has the snapshot of this frame (its input's frame less the
    /// input delay). Frames it can't have (never sent to it) are ignored,
    /// and so are older ones than it has said before.
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

    /// <summary>
    /// The snapshot a message carries, or null if it's a delta from a
    /// snapshot not kept here (the host then sends a delta from one that is,
    /// or a full one, once the guest's acknowledgements show what it has).
    /// Bad data is refused with <see cref="InvalidDataException"/>.
    /// </summary>
    public WorldSnapshot? Receive(ReadOnlySpan<byte> data)
    {
        var (frame, baseFrame) = SnapshotCodec.Peek(data);
        WorldSnapshot? baseSnapshot = null;
        if (baseFrame is long bf)
        {
            baseSnapshot = _kept[bf % Keep];
            if (baseSnapshot?.Frame != bf)
                return null;
        }
        var snapshot = SnapshotCodec.Decode(data, baseSnapshot);
        _kept[frame % Keep] = snapshot;
        return snapshot;
    }
}
