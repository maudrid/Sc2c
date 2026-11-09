// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: AlienTalkSegue,
// DoTalkSegue, PlayerResponseInput, FeedbackPlayerPhrase, DoConvSummary,
// UpdateAnimations; oscill.c: the slider; trackplayer.c: the subtitles'
// times), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using System.Text;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>What the communication screen does with a key (UQM's keys in a talk).</summary>
public enum CommKeyResult
{
    /// <summary>The screen used it (skipping, the summary, the replay).</summary>
    Used,

    /// <summary>Not the screen's: the replies' (up, down, select), for the session.</summary>
    Replies,

    /// <summary>Nothing to do with it now.</summary>
    Ignored,
}

/// <summary>
/// The communication screen, as UQM's comm.c runs it, on the client's own
/// clock (ticks, ONE_SECOND 840). The alien's lines since the player's
/// last reply play as subtitles, page by page for each page's time
/// (SplitSubPages), with the slider showing how far they are and the
/// alien talking (its talking animation) until they end; over them, the
/// reply the player chose (FeedbackPlayerPhrase). Then the replies, the
/// lit one the replier's cursor (none lit for a player watching). As
/// UQM's keys: while the lines play, Cancel skips to their end and Left
/// plays them again from the start; at the replies, Cancel shows the
/// summary of the lines (a page at a time; Select, Cancel or Right turns
/// it, and closes it after the last) and Left plays the lines again. The
/// alien's voice isn't played yet: the lines take their subtitles' time.
/// </summary>
public sealed class CommScreen
{
    /// <summary>The slider's button (ActivityFrame 2) and bar (5); the replies' arrows are 6 and 7.</summary>
    public const int SliderButtonFrame = 2, SliderFrame = 5;

    private readonly CommDescription _race;
    private readonly UqmFont _alienFont, _playerFont, _tiny;
    private readonly int _sliderSpace;
    private List<(byte[] Page, int Ticks)> _pages = new();
    private List<byte[]> _lines = new();
    private byte[] _said = Array.Empty<byte>();
    private List<byte[]> _replies = new();
    private long _start;
    private int _shownPage = -1;
    private bool _talking;
    private List<List<CommTextDraw>>? _summary;
    private int _summaryPage;
    private long _now;
    private string _key = "";
    private string _repliesKey = "";
    private int _top, _lastCursor;

    /// <param name="buttonWidth">The slider button's width (ActivityFrame 2): the slider moves over the window less it.</param>
    public CommScreen(AlienWindow window, CommDescription race, UqmFont alienFont, UqmFont playerFont, UqmFont tiny, int buttonWidth)
    {
        Window = window;
        _race = race;
        _alienFont = alienFont;
        _playerFont = playerFont;
        _tiny = tiny;
        _sliderSpace = CommText.Width - buttonWidth;
    }

    public AlienWindow Window { get; }

    /// <summary>The lines are playing: the replies wait.</summary>
    public bool Talking => _talking;

    /// <summary>The summary is open.</summary>
    public bool SummaryOpen => _summary is not null;

    /// <summary>The replies shown (as the host sent them), each in UTF-8.</summary>
    public IReadOnlyList<byte[]> Replies => _replies;

    /// <summary>
    /// The talk as the host sent it, at <paramref name="now"/>: new lines,
    /// or a new reply said, play from their start; the same again changes
    /// nothing. The replies can change without the lines (they're shown
    /// once the lines end).
    /// </summary>
    public void Show(IReadOnlyList<string> lines, string said, IReadOnlyList<string> replies, int round, long now)
    {
        _now = now;   // a page followed as it's shown (a replay) plays from now
        string repliesKey = $"{round}\u0001{string.Join("\u0002", replies)}";
        if (repliesKey != _repliesKey)
        {
            _repliesKey = repliesKey;
            _replies = replies.Select(r => Encoding.UTF8.GetBytes(r)).ToList();
            _top = _lastCursor = 0;   // DoResponsePhrase: top_response 0
        }
        string key = $"{round}\u0001{said}\u0001{string.Join("\u0002", lines)}";
        if (key == _key)
            return;
        _key = key;
        _lines = lines.Select(l => Encoding.UTF8.GetBytes(l)).ToList();
        _said = Encoding.UTF8.GetBytes(said);
        _pages = _lines.SelectMany(l => CommText.SplitPages(l)).Select(p => (p.Page, p.Millis * 840 / 1000)).ToList();
        _summary = null;
        Play(now);
    }

    private void Play(long now)
    {
        _start = now;
        _shownPage = -1;
        _talking = _pages.Count > 0;
        if (_talking)
            Window.Animation.StartTalking();
        Window.Clear();
    }

    private int TotalTicks => _pages.Sum(p => p.Ticks);

    private void EndLines()
    {
        _start = _now - TotalTicks;
        Update(_now);
    }

    /// <summary>The screen at <paramref name="now"/>: the pages turn, the lines end, the window animates. Whether anything changed.</summary>
    public bool Update(long now)
    {
        _now = now;
        bool changed = false;
        if (_talking)
        {
            long t = now - _start;
            int page = 0;
            long end = 0;
            while (page < _pages.Count && t >= (end += _pages[page].Ticks))
                ++page;
            if (page >= _pages.Count)
            {
                _talking = false;
                Window.Animation.StopTalking();
                page = -1;
            }
            if (page != _shownPage)
            {
                _shownPage = page;
                Window.Clear();   // ClearSubtitles: the picture is drawn again under the next page
                changed = true;
            }
        }
        return Window.Update(now) | changed;
    }

    /// <summary>A key, as UQM's talk takes it (see the class).</summary>
    public CommKeyResult Press(MenuKey key) => Press(key, out _);

    /// <summary>
    /// A key, as UQM's talk takes it (see the class); <paramref name="paged"/>
    /// what it did to the lines, for watchers to follow (null if nothing).
    /// </summary>
    public CommKeyResult Press(MenuKey key, out TalkPage? paged)
    {
        paged = PageFor(key);
        if (paged is { } p)
        {
            Page(p);
            _applied++;   // the host keeps it after those already followed (sent in order); counted even if refused (an ordinary talk, past the cap), where nothing more comes to follow
            return CommKeyResult.Used;
        }
        return _summary is null && !_talking && key is MenuKey.Up or MenuKey.Down or MenuKey.Select
            ? CommKeyResult.Replies : CommKeyResult.Ignored;
    }

    /// <summary>
    /// What a key does to the lines now: while they play, Cancel skips and
    /// Left plays them again (UQM's Right seeks on: the pages have no
    /// voice to seek yet); at the replies, Cancel opens the summary and
    /// Left plays the lines again; with the summary open, Select, Cancel
    /// and Right turn its page. Null for none.
    /// </summary>
    public TalkPage? PageFor(MenuKey key)
    {
        if (_summary is not null)
            return key is MenuKey.Select or MenuKey.Cancel or MenuKey.Right ? TalkPage.SummaryTurn : null;
        if (_talking)
            return key switch { MenuKey.Cancel => TalkPage.Skip, MenuKey.Left => TalkPage.Replay, _ => null };
        return key switch
        {
            MenuKey.Cancel => TalkPage.Summary,
            MenuKey.Left when _pages.Count > 0 => TalkPage.Replay,
            _ => null,
        };
    }

    /// <summary>
    /// The lines paged, by this player's key or as the replier paged them
    /// (a watcher following, whose screen may be a moment behind): a skip
    /// with the lines over does nothing; the summary opens over lines
    /// still playing as if they'd been skipped; a turn with no summary open
    /// does nothing.
    /// </summary>
    public void Page(TalkPage page)
    {
        switch (page)
        {
            case TalkPage.Skip when _talking:
                EndLines();
                break;
            case TalkPage.Replay when _pages.Count > 0:
                _summary = null;
                Play(_now);
                break;
            case TalkPage.Summary:
                if (_talking)
                    EndLines();
                _summary = CommText.Summary(_tiny, _lines);
                _summaryPage = 0;
                break;
            case TalkPage.SummaryTurn when _summary is not null:
                if (++_summaryPage >= _summary.Count)
                {
                    _summary = null;
                    Window.Clear();
                }
                break;
        }
    }

    private string _followKey = "";
    private int _applied;

    /// <summary>
    /// The replier's paging of the lines shown (<paramref name="linesKey"/>,
    /// the talk's <see cref="TalkView.LinesKey"/>; call after
    /// <see cref="Show"/>), as the host keeps it: everyone's screen follows
    /// what it hasn't applied yet (decision 7). The replier's own pages
    /// (<see cref="Press(MenuKey, out TalkPage?)"/>) count as applied, so
    /// the host's echo of them does nothing; a screen opened afresh (a
    /// player joining, the replier's screen reopened) or a watcher who has
    /// just become the replier follows all that came before.
    /// </summary>
    public void Follow(string linesKey, IReadOnlyList<TalkPage> paging)
    {
        if (linesKey != _followKey)
            (_followKey, _applied) = (linesKey, 0);
        for (; _applied < paging.Count; ++_applied)
            Page(paging[_applied]);
    }

    /// <summary>GetTrackPosition (sliderSpace): how far the button is along the slider.</summary>
    public int SliderOffset
    {
        get
        {
            int total = TotalTicks;
            if (!_talking || total == 0)
                return 0;
            return (int)(_sliderSpace * Math.Clamp(_now - _start, 0, total) / total);
        }
    }

    /// <summary>The text drawn, in the window's pixels, and the texts its <see cref="CommTextDraw.Source"/> numbers name.</summary>
    public sealed record Drawing(List<CommTextDraw> Over, IReadOnlyList<byte[]> OverTexts, UqmFont OverFont,
        List<CommTextDraw> Below, IReadOnlyList<byte[]> BelowTexts, UqmFont BelowFont);

    /// <summary>
    /// What is drawn as text now: over the alien's window, its subtitle
    /// page (in its font, traced) or the summary's page (the tiny font);
    /// below the slider, the reply said while the lines play, then the
    /// replies with <paramref name="cursor"/> lit (-1: none, a watcher's).
    /// </summary>
    public Drawing Draw(int cursor)
    {
        int top = TopFor(cursor);
        List<CommTextDraw> over;
        IReadOnlyList<byte[]> overTexts;
        UqmFont overFont;
        if (_summary is not null)
        {
            over = _summary[_summaryPage];
            overTexts = _lines;
            overFont = _tiny;
        }
        else if (_talking && _shownPage >= 0)
        {
            var page = _pages[_shownPage].Page;
            over = CommText.Subtitle(_alienFont, page, _race.TextBaselineX, _race.TextBaselineY, (TextAlign)_race.TextAlign,
                _race.TextValign, _race.TextWidth, new Rgb(_race.TextFore.R, _race.TextFore.G, _race.TextFore.B),
                new Rgb(_race.TextBack.R, _race.TextBack.G, _race.TextBack.B));
            overTexts = new[] { page };
            overFont = _alienFont;
        }
        else
        {
            over = new List<CommTextDraw>();
            overTexts = Array.Empty<byte[]>();
            overFont = _alienFont;
        }
        List<CommTextDraw> below;
        IReadOnlyList<byte[]> belowTexts;
        if (_talking || _replies.Count == 0)
        {
            below = CommText.FeedbackPhrase(_playerFont, _said);
            belowTexts = new[] { _said };
        }
        else
        {
            below = CommText.Replies(_playerFont, _replies, top, cursor);
            belowTexts = _replies;
        }
        return new Drawing(over, overTexts, overFont, below, belowTexts, _playerFont);
    }

    /// <summary>
    /// The first reply shown with <paramref name="cursor"/> lit, as
    /// PlayerResponseInput scrolls: a move above the first shown shows
    /// them from the start, and one to a reply that ends past the
    /// window's foot shows them from it.
    /// </summary>
    public int TopFor(int cursor)
    {
        if (cursor < 0 || cursor >= _replies.Count || cursor == _lastCursor)
            return _top;
        _lastCursor = cursor;
        if (cursor < _top)
            _top = 0;
        else if (CommText.ReplyEnd(_playerFont, _replies, _top, cursor) > CommText.Height)
            _top = cursor;
        return _top;
    }

    /// <summary>
    /// The bytes a text drawing call draws (its source's from its offset,
    /// its count of characters; -1 all). The game strings are
    /// FEEDBACK_STRING_BASE's: "(In response to your statement)" and MORE.
    /// </summary>
    public static byte[] TextOf(CommTextDraw d, IReadOnlyList<byte[]> texts, string feedback = "", string more = "")
    {
        byte[] source = d.Source switch
        {
            -1 => Encoding.UTF8.GetBytes(CommText.Bullet),
            -2 => Encoding.UTF8.GetBytes(CommText.MiddleDot + more + CommText.MiddleDot),
            -3 => Encoding.UTF8.GetBytes(feedback),
            var s => texts[s],
        };
        if (d.Source < 0)
            return source;
        int pos = d.Offset;
        if (d.Count >= 0)
            for (int n = 0; n < d.Count && pos < source.Length; ++n)
                UqmFont.NextChar(source, ref pos);
        else
            pos = source.Length;
        return source[d.Offset..pos];
    }
}
