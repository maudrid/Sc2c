// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>
/// The entries into stars a snapshot shows (<see cref="StarEntryState"/>,
/// sent by the host from the contact itself), as UQM plays them
/// (<see cref="StarEntry.Animate"/>): on the contact frame the ship is
/// where and as it touched the star; on each frame after, one step of the
/// entry. A client that missed snapshots picks the entry up at the right
/// step.
/// </summary>
public sealed class StarEntryTracker
{
    private readonly StarMap _map;
    private readonly int _frameCount;
    // Animations by contact (an escort following its host has the same one).
    private readonly Dictionary<StarEntryState, IReadOnlyList<StarEntry.Step>> _animations = new();
    private List<Entering> _entering = new();

    /// <summary>A ship entering a star at the snapshot's frame: the sheet's frame and its sprite's place (hyperspace logical units).</summary>
    public sealed record Entering(ShipSnapshot Ship, StarEntryState State, int Frame, long X, long Y);

    /// <param name="frameCount">The hyperspace flagship sheet's frame count (as the host's shapes have it).</param>
    public StarEntryTracker(StarMap map, int frameCount)
    {
        _map = map;
        _frameCount = frameCount;
    }

    /// <summary>The ships entering a star in the last snapshot observed.</summary>
    public IReadOnlyList<Entering> Current => _entering;

    /// <summary>The entries to draw: an escort following its host into the star rides in with the flagship.</summary>
    public IEnumerable<Entering> Drawn => _entering.Where(e => e.State.Following is null);

    public bool Playing(string id) => _entering.Any(e => e.Ship.Id == id);

    public Entering? Of(string id) => _entering.FirstOrDefault(e => e.Ship.Id == id);

    public void Observe(WorldSnapshot snapshot)
    {
        var now = new List<Entering>();
        foreach (var ship in snapshot.Ships)
        {
            if (ship.Entry is not { } e || !e.PlayingAt(snapshot.Frame))
                continue;
            var contact = e with { Following = null };
            if (!_animations.TryGetValue(contact, out var steps))
            {
                var (sx, sy) = e.Sprite;
                _animations[contact] = steps = StarEntry.Animate(sx, sy, e.Facing, _map.Stars[e.Star], _frameCount);
            }
            long k = snapshot.Frame - e.ContactFrame;
            if (k == 0)
            {
                var (x, y) = e.Sprite;
                now.Add(new Entering(ship, e, e.Facing, x, y));
            }
            else
            {
                var step = steps[(int)Math.Min(k, steps.Count) - 1];
                now.Add(new Entering(ship, e, step.Frame, step.X, step.Y));
            }
        }
        // Forget finished entries.
        foreach (var key in _animations.Keys.Where(k => !now.Any(n => n.State with { Following = null } == k)).ToList())
            _animations.Remove(key);
        _entering = now;
    }
}
