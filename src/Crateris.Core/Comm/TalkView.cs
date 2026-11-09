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
/// lines since the player's last reply (each track with what was spliced
/// into it; across the visit's talks, so a talk's closing lines show
/// before the next one's), the replies offered, and which set of replies
/// this is (<see cref="Round"/>, which a reply is sent with, so one meant
/// for a set already answered can't answer the next). Once the visit is
/// over, its closing lines with no replies (<see cref="VisitStep.Done"/>).
/// <see cref="Replier"/> is the player who answers it: everyone is shown a
/// story talk, and only they reply (decision 7); <see cref="Said"/> is the
/// reply they last chose, which the lines answer ("" before any).
/// Sent to them by the host when it changes, as a reliable message;
/// <see cref="Protocol.NoView"/> means no talk.
/// </summary>
public sealed record TalkView(VisitStep Step, string Script, IReadOnlyList<string> Lines, IReadOnlyList<string> Replies,
    int Round = 0, string Replier = "", string Said = "", IReadOnlyList<TalkPage>? Paging = null)
{
    /// <summary>The most paging a view carries (<see cref="Paging"/>): past it, the replier's paging isn't followed.</summary>
    public const int MostPaging = 256;

    /// <summary>
    /// How the replier has paged these lines and their replies so far, in
    /// order, for everyone watching a story talk to follow (decision 7):
    /// empty for new lines.
    /// </summary>
    public IReadOnlyList<TalkPage> PagingSoFar => Paging ?? Array.Empty<TalkPage>();

    /// <summary>What the lines are: a view with other lines (or another reply said, or another set of replies) is paged afresh.</summary>
    public string LinesKey => $"{Round}\u0001{Said}\u0001{string.Join("\u0002", Lines)}";

    /// <summary>
    /// <see cref="LinesKey"/> as a number the same on every machine
    /// (FNV-1a over its UTF-8): a page is sent with the lines it was for,
    /// so one meant for lines just replaced can't page the new ones.
    /// </summary>
    public int LinesHash
    {
        get
        {
            uint h = 2166136261;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(LinesKey))
                h = (h ^ b) * 16777619;
            return unchecked((int)h);
        }
    }

    /// <summary>The view of a visit as it stands (its closing lines once it's over), or null without one.</summary>
    public static TalkView? Of(StarbaseVisit? visit)
    {
        if (visit is null)
            return null;
        // From the player's last reply, through the talks after it.
        int talkFrom = 0, eventFrom = 0, round = visit.FirstRound;
        string said = "";
        RepliesOffered? offered = null;
        for (int t = 0; t < visit.Talks.Count; ++t)
        {
            var events = visit.Talks[t].Events;
            for (int i = 0; i < events.Count; ++i)
            {
                if (events[i] is ReplyChosen chosen)
                {
                    (talkFrom, eventFrom) = (t, i + 1);
                    said = offered?.Replies[chosen.Index].Text ?? "";
                }
                else if (events[i] is RepliesOffered o)
                {
                    offered = o;
                    round++;
                }
            }
        }
        var lines = new List<string>();
        for (int t = talkFrom; t < visit.Talks.Count; ++t)
        {
            foreach (var e in visit.Talks[t].Events.Skip(t == talkFrom ? eventFrom : 0))
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
        }
        var talk = visit.Talk;
        string script = (talk ?? visit.Talks.LastOrDefault())?.Script.Phrases.Name ?? "";
        var view = new TalkView(visit.Step, script, lines, talk?.Replies.Select(r => r.Text).ToList() ?? new List<string>(), round,
            visit.Replier.Id, said);
        return visit.Paging is { } paging && paging.Key == view.LinesKey ? view with { Paging = paging.Pages.ToList() } : view;
    }

    public byte[] Encode()
    {
        var w = new WireWriter();
        w.UInt((ulong)Step + 1);
        w.String(Script);
        w.UInt((ulong)Round);
        w.String(Replier);
        w.String(Said);
        w.UInt((ulong)Lines.Count);
        foreach (var l in Lines)
            w.String(l);
        w.UInt((ulong)Replies.Count);
        foreach (var r in Replies)
            w.String(r);
        w.UInt((ulong)PagingSoFar.Count);
        foreach (var p in PagingSoFar)
            w.Byte((byte)p);
        return w.Written.ToArray();
    }

    /// <summary>A view from the host; null for none (empty). A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static TalkView? Decode(ReadOnlySpan<byte> data)
    {
        if (Protocol.IsNoView(data))
            return null;
        var r = new WireReader(data);
        ulong step = r.UInt();
        if (step == 0 || step - 1 > (ulong)VisitStep.Done)
            throw new InvalidDataException("Bad talk step.");
        string script = r.String() ?? "";
        ulong round = r.UInt();
        if (round > int.MaxValue)
            throw new InvalidDataException("Bad talk round.");
        string replier = r.String() ?? "";
        string said = r.String() ?? "";
        var lines = new List<string>();
        for (ulong n = Count(r.UInt(), 1000); n > 0; --n)
            lines.Add(r.String() ?? "");
        var replies = new List<string>();
        for (ulong n = Count(r.UInt(), Talk.MaxReplies); n > 0; --n)
            replies.Add(r.String() ?? "");
        var paging = new List<TalkPage>();
        for (ulong n = Count(r.UInt(), MostPaging); n > 0; --n)
            paging.Add(r.Byte() is var p && p <= (byte)TalkPage.SummaryTurn ? (TalkPage)p
                : throw new InvalidDataException($"Bad talk paging {p}."));
        if (!r.AtEnd)
            throw new InvalidDataException("A talk has bytes after its end.");
        return new TalkView((VisitStep)(int)(step - 1), script, lines, replies, (int)round, replier, said,
            paging.Count == 0 ? null : paging);
    }

    private static ulong Count(ulong n, int most) =>
        n <= (ulong)most ? n : throw new InvalidDataException($"A talk has {n} of what it has at most {most} of.");

    public bool Same(TalkView? other) =>
        other is not null && Step == other.Step && Script == other.Script && Round == other.Round && Replier == other.Replier && Said == other.Said
        && Lines.SequenceEqual(other.Lines) && Replies.SequenceEqual(other.Replies) && PagingSoFar.SequenceEqual(other.PagingSoFar);
}
