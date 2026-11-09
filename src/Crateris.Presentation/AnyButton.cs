// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/gameinp.c's
// AnyButtonPress and FlushInput with libs/input/sdl/vcontrol.c's key
// counts and frame-start bit), Copyright Paul Reiche, Fred Ford and Toys
// for Bob, Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2
// or later.

namespace Crateris.Presentation;

/// <summary>
/// The keys as UQM's AnyButtonPress sees them, for the screens that wait
/// on it (the lander report, the mineral selling): a key held, or one
/// that went down since the last look (VControl's frame-start bit, which
/// each look clears), even if it's up again. The keys are told as they go
/// down and come up, each at its time on the screen's clock, in the order
/// they happen; a look takes those up to its time.
/// </summary>
internal sealed class AnyButton
{
    private const int AllKeys = int.MinValue;

    private readonly Queue<(long At, int Key, bool Down)> _keys = new();
    // How many times each key is held, each key on its own (VControl counts per control, so keys
    // bound to one share a count; per key, a key forgotten by FlushInput coming up lowers nothing
    // else's, and nothing here depends on the bindings).
    private readonly Dictionary<int, int> _held = new();
    private bool _started;   // VCONTROL_STARTBIT: gone down since the last look

    /// <summary>Key <paramref name="key"/> went down at <paramref name="at"/>.</summary>
    public void Down(long at, int key) => _keys.Enqueue((at, key, true));

    /// <summary>Key <paramref name="key"/> came up at <paramref name="at"/>: one not held (forgotten, or never seen going down) changes nothing.</summary>
    public void Up(long at, int key) => _keys.Enqueue((at, key, false));

    /// <summary>Every key let go at <paramref name="at"/> (the window lost the keyboard: no key comes up there, as SDL's does).</summary>
    public void LetGo(long at) => _keys.Enqueue((at, AllKeys, false));

    /// <summary>Key <paramref name="key"/> is held as the screen starts: held, without the frame-start bit (an earlier look took it).</summary>
    public void Held(int key)
    {
        if (key == 0 || !_held.ContainsKey(key))
            _held[key] = _held.GetValueOrDefault(key) + 1;
    }

    // The keys that have gone down or up by now.
    private void Events(long now)
    {
        while (_keys.Count > 0 && _keys.Peek().At <= now)
        {
            var (_, key, down) = _keys.Dequeue();
            if (key == AllKeys)
                _held.Clear();
            else if (down)
            {
                // A key (not 0, the recordings' one control) can't go down twice: down again, its
                // coming up was lost (the window's focus away a moment), so it's held once.
                _held[key] = key != 0 && _held.ContainsKey(key) ? 1 : _held.GetValueOrDefault(key) + 1;
                _started = true;
            }
            else if (_held.TryGetValue(key, out int n))
            {
                if (n > 1)
                    _held[key] = n - 1;
                else
                    _held.Remove(key);
            }
        }
    }

    /// <summary>AnyButtonPress at <paramref name="now"/>: a key held, or one gone down since the last look (cleared by BeginInputFrame).</summary>
    public bool Press(long now)
    {
        Events(now);
        bool pressed = _held.Count > 0 || _started;
        _started = false;
        return pressed;
    }

    /// <summary>FlushInput at <paramref name="now"/>: every key held is forgotten (VControl_ResetInput).</summary>
    public void Flush(long now)
    {
        Events(now);
        _held.Clear();
        _started = false;
    }
}
