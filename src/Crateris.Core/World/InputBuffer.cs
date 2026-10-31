// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;

namespace Crateris.Core.World;

/// <summary>
/// One player's inputs, keyed by the frame they are meant for. Clients send
/// each input for "latest frame seen + input delay"; the host applies it on
/// that frame. An input that arrives late is applied on the next frame; a
/// frame with no input repeats the last one, so a lost packet doesn't stop
/// the ship.
/// </summary>
public sealed class InputBuffer
{
    private readonly SortedDictionary<long, ShipInput> _pending = new();
    private ShipInput _last;

    public void Submit(long frame, ShipInput input) => _pending[frame] = input;

    /// <summary>The input to apply on this frame.</summary>
    public ShipInput Take(long frame)
    {
        while (_pending.Count > 0)
        {
            var first = _pending.First();
            if (first.Key > frame)
                break;
            _last = first.Value;
            _pending.Remove(first.Key);
        }
        return _last;
    }

    public void Clear()
    {
        _pending.Clear();
        _last = ShipInput.None;
    }
}
