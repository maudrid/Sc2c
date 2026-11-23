// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: AlienTalkSegue,
// whose first call draws the screen in CommData's colour map, TalkSegue's
// talking animation, SelectResponse's StopTrack; commanim.c: the ambient
// animations' flags read as they run; cmap.c: XFormColorMap), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Net;

namespace Crateris.Core.Comm;

/// <summary>What a talk's script changes on the communication screen.</summary>
public enum ScreenChangeKind : byte
{
    /// <summary>The colours fade to colour map <see cref="ScreenChange.Value"/> over <see cref="ScreenChange.Time"/> (XFormColorMap).</summary>
    Fade,

    /// <summary>Ambient animation <see cref="ScreenChange.Value"/> turned on (its ANIM_DISABLED cleared).</summary>
    AmbientOn,

    /// <summary>Ambient animation <see cref="ScreenChange.Value"/> turned off (ANIM_DISABLED set).</summary>
    AmbientOff,

    /// <summary>The alien's mouth moves with its lines again (EnableTalkingAnim (TRUE)).</summary>
    TalkingOn,

    /// <summary>The alien's mouth stays still (EnableTalkingAnim (FALSE)).</summary>
    TalkingOff,
}

/// <summary>
/// A change to the communication screen, made as line <see cref="Line"/>
/// of the view's lines starts playing (0: as the lines start; the lines'
/// count: once they've all played).
/// </summary>
public readonly record struct ScreenChange(int Line, ScreenChangeKind Kind, int Value = 0, int Time = 0);

/// <summary>
/// The communication screen as a talk's script leaves it, for a talk's
/// view (<see cref="TalkView.Screen"/>): how the screen is when the view's
/// lines start, for a screen opened afresh (a player joining or coming
/// back, a new talk), and what the script changes on it as the lines play
/// (<see cref="Changes"/>), which every screen showing the lines makes.
/// <see cref="ColorMap"/> is the colour map shown: the one the screen was
/// first drawn in (CommData.AlienColorMap at the talk's first segue, as the
/// script's init and intro left it), or the one the last fade went to.
/// <see cref="Ambient"/> is each ambient animation's flags and
/// <see cref="Talking"/> whether the mouth moves with the lines
/// (EnableTalkingAnim).
/// <para>
/// UQM runs a reply's script function at once, its lines only queued
/// (NPCPhrase), so a change it makes is seen as the lines start, unless
/// the script first plays the lines up to a track (AlienTalkSegue): then
/// as the next track starts. A bare change of the colour map's index after
/// the screen is drawn shows nothing (UQM sets the colour map only as the
/// screen is first drawn), so it isn't a change here; nor is an ambient
/// animation's other flags (the Zoq-Fot-Pik's smoke waiting for Pik is
/// their speakers', <see cref="TalkView.Speakers"/>).
/// </para>
/// </summary>
public sealed record TalkScreen(int ColorMap, IReadOnlyList<CommAnimationFlags> Ambient, bool Talking, IReadOnlyList<ScreenChange> Changes)
{
    /// <summary>MAX_ANIMATIONS: the most ambient animations a race has.</summary>
    public const int MostAmbient = 20;

    /// <summary>The most changes a view carries (a reply's script makes a few at most).</summary>
    public const int MostChanges = 256;

    /// <summary>The highest colour map a view names (a colour table's entries are counted in a byte).</summary>
    public const int MostColorMap = 255;

    /// <summary>The longest fade, in UQM's ticks (XFormColorMap's SIZE).</summary>
    public const int MostTime = short.MaxValue;

    /// <summary>
    /// The screen of <paramref name="talk"/> for a view whose lines start
    /// at its event <paramref name="from"/> (after the player's last reply,
    /// or 0) and at the view's line <paramref name="firstLine"/> (the lines
    /// before them another talk's closing lines); null before the screen
    /// is loaded (a meeting at its encounter screen).
    /// </summary>
    public static TalkScreen? Of(Talk talk, int from, int firstLine = 0)
    {
        var events = talk.Events;
        var flags = talk.Description.Ambient.Select(a => a.Flags).ToArray();
        int colorMap = 0, palette = 0;
        int? fadedBefore = null;
        bool talking = true, loaded = false, drawn = false, finished = false;
        (int Map, CommAnimationFlags[] Flags, bool Talking)? start = null;
        var changes = new List<ScreenChange>();
        // Where the script is in the lines (the view's line its changes are
        // made at), and the track each of its lines plays in (from 1, since
        // the reply: the tracks AlienTalkSegue waits for).
        int line = firstLine;
        var tracks = new List<int>();
        for (int i = 0; i < events.Count; ++i)
        {
            bool shown = i >= from;
            if (i == from && drawn)
                start = (palette, flags.ToArray(), talking);
            var e = events[i];
            // The screen is drawn at the talk's first segue (AlienTalkSegue's
            // first call, or DoCommunication's as the intro returns), in the
            // colour map the init and intro left; a fade started before it
            // goes on over it.
            if (loaded && !drawn && e is TalkWait or RepliesOffered or TalkEnded or ReplyChosen)
            {
                drawn = true;
                if (shown)
                    start = (colorMap, flags.ToArray(), talking);
                palette = fadedBefore ?? colorMap;
            }
            switch (e)
            {
                case ScreenChanged s:
                    if (drawn && shown)
                        for (int k = 0; k < Math.Min(s.Ambient.Count, flags.Length); ++k)
                            if (((s.Ambient[k] ^ flags[k]) & CommAnimationFlags.Disabled) != 0)
                                changes.Add(new ScreenChange(line,
                                    (s.Ambient[k] & CommAnimationFlags.Disabled) != 0 ? ScreenChangeKind.AmbientOff : ScreenChangeKind.AmbientOn, k));
                    loaded = true;
                    colorMap = s.ColorMap;
                    flags = s.Ambient.ToArray();
                    break;
                case ColorTransform c:
                    if (shown)
                        changes.Add(new ScreenChange(line, ScreenChangeKind.Fade, c.Map, c.Time));
                    if (drawn)
                        palette = c.Map;
                    else
                        fadedBefore = c.Map;
                    break;
                case TalkingAnimation t:
                    if (drawn && shown)
                        changes.Add(new ScreenChange(line, t.Enabled ? ScreenChangeKind.TalkingOn : ScreenChangeKind.TalkingOff));
                    talking = t.Enabled;
                    break;
                case LineQueued { Clip: not null } when shown:
                    tracks.Add(talk.TrackOf(i));
                    break;
                case TalkWait w when shown && !finished:
                    // The lines up to the track have played: what the script
                    // does next is seen as the next starts. Once none is left
                    // (TalkingFinished), the segues after it play nothing.
                    line = Math.Max(line, firstLine + tracks.Count(t => t <= w.Track));
                    finished = w.Track >= (tracks.Count > 0 ? tracks[^1] : 0);
                    break;
            }
        }
        if (!loaded)
            return null;
        start ??= (drawn ? palette : colorMap, flags.ToArray(), talking);
        // More than a view carries can't come from one reply's script; kept to the bound if it did.
        return new TalkScreen(start.Value.Map, start.Value.Flags, start.Value.Talking,
            changes.Count > MostChanges ? changes.Take(MostChanges).ToList() : changes);
    }

    /// <summary>
    /// The screen once all its changes are made, with none left: the
    /// colour map the last fade went to, the ambient animations turned on
    /// or off, the talking as the last switch left it (for a view with no
    /// lines, the visit's end).
    /// </summary>
    public TalkScreen AfterChanges()
    {
        int map = ColorMap;
        var ambient = Ambient.ToArray();
        bool talking = Talking;
        foreach (var c in Changes)
            switch (c.Kind)
            {
                case ScreenChangeKind.Fade: map = c.Value; break;
                case ScreenChangeKind.AmbientOn when c.Value < ambient.Length: ambient[c.Value] &= ~CommAnimationFlags.Disabled; break;
                case ScreenChangeKind.AmbientOff when c.Value < ambient.Length: ambient[c.Value] |= CommAnimationFlags.Disabled; break;
                case ScreenChangeKind.TalkingOn: talking = true; break;
                case ScreenChangeKind.TalkingOff: talking = false; break;
            }
        return new TalkScreen(map, ambient, talking, Array.Empty<ScreenChange>());
    }

    internal void Encode(WireWriter w, int lines)
    {
        w.UInt((ulong)ColorMap);
        w.UInt((ulong)Ambient.Count);
        foreach (var f in Ambient)
            w.Byte((byte)f);
        w.Byte(Talking ? (byte)1 : (byte)0);
        w.UInt((ulong)Changes.Count);
        foreach (var c in Changes)
        {
            // At one of the lines or after them: one past their end (a view's lines cut) goes at their end.
            w.UInt((ulong)Math.Clamp(c.Line, 0, lines));
            w.Byte((byte)c.Kind);
            switch (c.Kind)
            {
                case ScreenChangeKind.Fade:
                    w.UInt((ulong)c.Value);
                    w.UInt((ulong)c.Time);
                    break;
                case ScreenChangeKind.AmbientOn or ScreenChangeKind.AmbientOff:
                    w.UInt((ulong)c.Value);
                    break;
            }
        }
    }

    /// <summary>A screen from the host, for a view of <paramref name="lines"/> lines; a bad one is an <see cref="InvalidDataException"/>.</summary>
    internal static TalkScreen Decode(ref WireReader r, int lines)
    {
        int map = Bounded(r.UInt(), MostColorMap, "colour map");
        int count = Bounded(r.UInt(), MostAmbient, "ambient animations");
        var ambient = new CommAnimationFlags[count];
        for (int i = 0; i < count; ++i)
            ambient[i] = (CommAnimationFlags)r.Byte();
        bool talking = r.Byte() switch
        {
            0 => false,
            1 => true,
            var b => throw new InvalidDataException($"Bad talk screen talking {b}."),
        };
        int n = Bounded(r.UInt(), MostChanges, "screen changes");
        var changes = new List<ScreenChange>(n);
        for (int i = 0; i < n; ++i)
        {
            int line = Bounded(r.UInt(), lines, "screen change's line");
            var kind = r.Byte() is var k && k <= (byte)ScreenChangeKind.TalkingOff ? (ScreenChangeKind)k
                : throw new InvalidDataException($"Bad talk screen change {k}.");
            changes.Add(kind switch
            {
                ScreenChangeKind.Fade => new ScreenChange(line, kind, Bounded(r.UInt(), MostColorMap, "fade's colour map"),
                    Bounded(r.UInt(), MostTime, "fade's time")),
                ScreenChangeKind.AmbientOn or ScreenChangeKind.AmbientOff =>
                    new ScreenChange(line, kind, Bounded(r.UInt(), count - 1, "ambient animation")),
                _ => new ScreenChange(line, kind),
            });
        }
        return new TalkScreen(map, ambient, talking, changes);
    }

    private static int Bounded(ulong n, int most, string what) =>
        n <= (ulong)Math.Max(most, 0) && most >= 0 ? (int)n : throw new InvalidDataException($"A talk's screen has a bad {what} ({n}).");

    public bool Same(TalkScreen? other) =>
        other is not null && ColorMap == other.ColorMap && Talking == other.Talking && Ambient.SequenceEqual(other.Ambient)
        && Changes.SequenceEqual(other.Changes);
}
