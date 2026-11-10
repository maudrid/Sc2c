// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Presentation;

/// <summary>
/// The keys that went down into a screen that takes them (the about screen,
/// the lander report, the minerals' count-down, the line a name is typed
/// in), every one but F1 held as the minerals' count-down starts or the
/// giving screen closes, and the Enter or Esc held as the name line is put
/// away: the game's own keys ignore each until it comes up, so an Esc that
/// closed the about screen, held, doesn't then take the lander off, nor its
/// repeats leave orbit. A key that goes down again
/// without having been seen coming up (lost with the window's focus) is
/// the game's again.
/// </summary>
public sealed class TakenKeys
{
    private readonly HashSet<long> _keys = new();

    /// <summary>A key went down into a screen that takes it.</summary>
    public void Take(long key) => _keys.Add(key);

    /// <summary>A key came up.</summary>
    public void Up(long key) => _keys.Remove(key);

    /// <summary>The keys' coming up may be lost (the window's focus gone): none is held back.</summary>
    public void Clear() => _keys.Clear();

    /// <summary>
    /// A key going down (<paramref name="echo"/>: held, repeating) for the
    /// game: whether the game has it. A taken key's repeats don't; it going
    /// down afresh means its coming up was lost, and it is the game's again.
    /// </summary>
    public bool Pass(long key, bool echo)
    {
        if (!_keys.Contains(key))
            return true;
        if (echo)
            return false;
        _keys.Remove(key);
        return true;
    }

    /// <summary>Whether a key held is the game's (polled each frame: not taken).</summary>
    public bool Free(long key) => !_keys.Contains(key);

    /// <summary>
    /// A screen that takes keys starting with keys held (the minerals'
    /// count-down, whose UQM input sees a key held), or one closing whose
    /// keys mustn't repeat into what's under it (the giving screen, with
    /// <paramref name="shut"/>: it sees none): of those
    /// <paramref name="down"/> (each its physical key and its key), the
    /// physical keys it sees held, as a key pressed then would have gone to
    /// it: none if it gets no keys (<paramref name="shut"/>: under the about
    /// screen, or a scripted player's), and never <paramref name="except"/>
    /// (F1) nor one taken by another screen and held since. Every one held
    /// but <paramref name="except"/> is taken, the game's again only once
    /// it's come up.
    /// </summary>
    public IReadOnlyList<long> TakeHeld(IEnumerable<(long Physical, long Key)> down, bool shut, long except)
    {
        var held = down.Where(d => d.Key != except).ToList();
        var seen = shut ? new List<long>() : held.Where(d => Free(d.Key)).Select(d => d.Physical).ToList();
        foreach (var (_, key) in held)
            Take(key);
        return seen;
    }
}
