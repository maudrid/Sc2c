// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;
using Crateris.Core.World;

namespace Crateris.Core.Comm;

/// <summary>
/// What the player in a talk is shown, in words, until the communication
/// screen (stage 3a, item 11): who is talking (the script), the alien's
/// lines since the last reply (each track with what was spliced into it),
/// and the replies offered; or, while time passes between the visit's
/// talks, that. Sent to them by the host when it changes, as a reliable
/// message; an empty message means no talk.
/// </summary>
public sealed record TalkView(VisitStep Step, string Script, IReadOnlyList<string> Lines, IReadOnlyList<string> Replies)
{
    /// <summary>The view of a visit as it stands, or null when it has no talk to show.</summary>
    public static TalkView? Of(StarbaseVisit? visit)
    {
        if (visit is null || visit.Done)
            return null;
        if (visit.Talk is not { } talk)
            return new TalkView(visit.Step, "", Array.Empty<string>(), Array.Empty<string>());
        int from = 0;
        for (int i = talk.Events.Count - 1; i >= 0; --i)
        {
            if (talk.Events[i] is ReplyChosen)
            {
                from = i + 1;
                break;
            }
        }
        var lines = new List<string>();
        foreach (var e in talk.Events.Skip(from))
        {
            switch (e)
            {
                case LineQueued { Clip: not null } l:
                    lines.Add(l.Text);
                    break;
                case LineQueued l when lines.Count > 0:
                    lines[^1] += l.Text;
                    break;
                case MultiTrackQueued m when lines.Count > 0:
                    lines[^1] += System.Text.Encoding.UTF8.GetString(m.TextBytes);
                    break;
            }
        }
        return new TalkView(visit.Step, visit.Talk.Script.Phrases.Name, lines, talk.Replies.Select(r => r.Text).ToList());
    }

    public byte[] Encode()
    {
        var w = new WireWriter();
        w.UInt((ulong)Step + 1);
        w.String(Script);
        w.UInt((ulong)Lines.Count);
        foreach (var l in Lines)
            w.String(l);
        w.UInt((ulong)Replies.Count);
        foreach (var r in Replies)
            w.String(r);
        return w.Written.ToArray();
    }

    /// <summary>A view from the host; null for none (empty). A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static TalkView? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return null;
        var r = new WireReader(data);
        ulong step = r.UInt();
        if (step == 0 || !Enum.IsDefined((VisitStep)(int)(step - 1)))
            throw new InvalidDataException("Bad talk step.");
        string script = r.String() ?? "";
        var lines = new List<string>();
        for (ulong n = Count(r.UInt(), 1000); n > 0; --n)
            lines.Add(r.String() ?? "");
        var replies = new List<string>();
        for (ulong n = Count(r.UInt(), Talk.MaxReplies); n > 0; --n)
            replies.Add(r.String() ?? "");
        return new TalkView((VisitStep)(int)(step - 1), script, lines, replies);
    }

    private static ulong Count(ulong n, int most) =>
        n <= (ulong)most ? n : throw new InvalidDataException($"A talk has {n} of what it has at most {most} of.");

    public bool Same(TalkView? other) =>
        other is not null && Step == other.Step && Script == other.Script
        && Lines.SequenceEqual(other.Lines) && Replies.SequenceEqual(other.Replies);
}
