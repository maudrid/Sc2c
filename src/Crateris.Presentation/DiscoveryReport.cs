// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/report.c:
// ClearReportArea, MakeReport, DoDiscoveryReport; src/uqm/util.c's waits:
// WaitForAnyButtonUntil, WaitForNoInputUntil; src/uqm/gameinp.c's
// AnyButtonPress and FlushInput with libs/input/sdl/vcontrol.c's key
// counts), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using System.Text;
using Crateris.Core.Content;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// The lander report (DoDiscoveryReport) as UQM types it into the scan
/// map's area (docs/plans/stage-3.md, item 11): the area cleared to its
/// grid of cells (ClearReportArea), the text a letter at a time in the
/// lander's font, a twentieth of a second a letter, a quarter more after
/// a comma, half a second after the end of a sentence and a twentieth
/// more after each word; a page ending with the end-of-page string when
/// the rest doesn't fit, and waiting for a key; a key while a page is
/// typed draws the rest of the report a page at once. The fonts are UQM's
/// default, OPT_PC: the cells are frame 21 of graphics.orbitbackground,
/// the text in the colours of graphics.landerfonteffect.
/// <para>
/// It runs as UQM's code runs, on the client's clock (UQM's ticks, 840 a
/// second): the waits look at the keys every fortieth of a second as
/// util.c's do, and the keys are seen as UQM's input sees them, a key
/// held or one that went down since the last look (<see cref="KeyDown"/>
/// and <see cref="KeyUp"/> say when). <see cref="Run"/> runs it up to a
/// time and gives what it drew, each call at its time, as UQM's drawing
/// calls on the canvas (checked call for call against UQM's own,
/// tests/golden/report.json).
/// </para>
/// </summary>
public sealed class DiscoveryReport
{
    /// <summary>SpaceJunkFrame: the report area's cells are its frame 21 (OPT_PC), their size its frame 18's.</summary>
    public const string CellSheet = "graphics.orbitbackground";

    /// <summary>The lander's font effect (LANDER_FONTEFF_PMAP_ANIM): the text takes its colours from its frame 0 (OPT_PC).</summary>
    public const string FontEffectSheet = "graphics.landerfonteffect";

    /// <summary>The lander's font (LANDER_FONT, font.lander), by the name the calls give it.</summary>
    public const string Font = "lander";

    /// <summary>The end-of-page string: GAME_STRING (SCAN_STRING_BASE + NUM_SCAN_TYPES).</summary>
    public const int EndPageString = GameStrings.ScanBase + 3;

    /// <summary>The scan map's size (MAP_WIDTH, MAP_HEIGHT): the scan context's.</summary>
    public const int MapWidth = UqmCanvas.SisScreenWidth, MapHeight = 75 - UqmCanvas.SafeY;

    private const int NumCellCols = MapWidth / 6, NumCellRows = MapHeight / 6;
    private const long OneSecond = 840;
    private const long Infinite = -1;   // WAIT_INFINITE

    private static readonly Rgb Black = Rgb.From15(0, 0, 0);
    private static readonly Rgb CellColor = Rgb.From15(0x00, 0x07, 0x00), TextColor = Rgb.From15(0x00, 0x1F, 0x00);

    private readonly UqmCanvas _canvas;
    private readonly UqmCanvas.Context _scan;
    private readonly int[] _report;     // the report's characters (code points)
    private readonly int[] _reportAt;   // each one's place in the text, and the text's length after them
    private readonly string _reportText;
    private readonly int[] _end;        // end_page_buf: the end-of-page string and a newline
    private readonly int[] _endAt;
    private readonly string _endText;
    private readonly IEnumerator<long> _run;
    private readonly List<(long At, UqmDraw Call)> _drawn = new();
    private readonly Queue<(long At, int Key, bool Down)> _keys = new();
    private long _clock, _wake;
    private bool _finished;
    // How many times each key is held, each key on its own (VControl counts per control, so keys
    // bound to one share a count; per key, a key forgotten by FlushInput coming up lowers nothing
    // else's, and nothing here depends on the bindings).
    private readonly Dictionary<int, int> _held = new();
    private bool _started;   // VCONTROL_STARTBIT: gone down since the last look
    private bool _result;    // the last wait's

    /// <param name="canvas">The canvas the report draws on (its frames: graphics.orbitbackground's).</param>
    /// <param name="text">The report: its string table entry.</param>
    /// <param name="endPage">The end-of-page string (GAME_STRING (<see cref="EndPageString"/>)).</param>
    /// <param name="start">When the report starts, on the clock.</param>
    public DiscoveryReport(UqmCanvas canvas, string text, string endPage, long start)
    {
        _canvas = canvas;
        // GetScanContext: the scan map's area, at the bottom right of the SIS window (CreateScanContext).
        var space = canvas.Space.Clip!.Value;
        _scan = new UqmCanvas.Context
        {
            Clip = new URect(space.X + space.W - MapWidth, space.Y + space.H - MapHeight, MapWidth, MapHeight),
        };
        (_report, _reportAt) = CodePoints(text);
        _reportText = text;
        _endText = endPage + "\n";
        (_end, _endAt) = CodePoints(_endText);
        _clock = _wake = start;
        _run = DoDiscoveryReport().GetEnumerator();
    }

    /// <summary>Where the report is drawn, on the screen: the scan map's area.</summary>
    public URect Area => _scan.Clip!.Value;

    /// <summary>Whether the report has put the screen back (its last page's key came): it's no longer shown.</summary>
    public bool Restored { get; private set; }

    /// <summary>When it put the screen back, on the clock.</summary>
    public long RestoredAt { get; private set; } = -1;

    /// <summary>Whether the report is over: the screen put back and every key up (WaitForNoInput), when the keys are the player's again.</summary>
    public bool Done => _finished;

    /// <summary>When it was over, on the clock.</summary>
    public long DoneAt { get; private set; } = -1;

    /// <summary>Whether a page is up, waiting for a key (the next page, or the report put away).</summary>
    public bool Waiting { get; private set; }

    /// <summary>Key <paramref name="key"/> went down at <paramref name="at"/> (keys come in the order they happen).</summary>
    public void KeyDown(long at, int key = 0) => _keys.Enqueue((at, key, true));

    /// <summary>Key <paramref name="key"/> came up at <paramref name="at"/>: one not held (forgotten, or held before the report) changes nothing.</summary>
    public void KeyUp(long at, int key = 0) => _keys.Enqueue((at, key, false));

    /// <summary>Every key let go at <paramref name="at"/> (the window lost the keyboard: no key comes up there, as SDL's does).</summary>
    public void LetGo(long at) => _keys.Enqueue((at, AllKeys, false));

    private const int AllKeys = int.MinValue;

    /// <summary>Runs the report up to <paramref name="now"/>: what it drew meanwhile, each call at its time.</summary>
    public List<(long At, UqmDraw Call)> Run(long now)
    {
        _drawn.Clear();
        while (!_finished && _wake <= now)
        {
            int from = _canvas.Drawn.Count;
            long at = _clock;
            bool more = _run.MoveNext();
            for (int i = from; i < _canvas.Drawn.Count; ++i)
                _drawn.Add((at, _canvas.Drawn[i]));
            _canvas.Drawn.RemoveRange(from, _canvas.Drawn.Count - from);
            if (!more)
            {
                _finished = true;
                DoneAt = _clock;
            }
            else
                _wake = _run.Current;
        }
        return new List<(long, UqmDraw)>(_drawn);
    }

    private static (int[] Chars, int[] At) CodePoints(string s)
    {
        var chars = new List<int>();
        var at = new List<int>();
        int i = 0;
        foreach (var r in s.EnumerateRunes())
        {
            chars.Add(r.Value);
            at.Add(i);
            i += r.Utf16SequenceLength;
        }
        at.Add(i);
        return (chars.ToArray(), at.ToArray());
    }

    // ------------------------------------------------------------ the keys and the clock

    // The keys that have gone down or up by now.
    private void KeyEvents()
    {
        while (_keys.Count > 0 && _keys.Peek().At <= _clock)
        {
            var (_, key, down) = _keys.Dequeue();
            if (key == AllKeys)
                _held.Clear();
            else if (down)
            {
                _held[key] = _held.GetValueOrDefault(key) + 1;
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

    // AnyButtonPress: a key held, or one gone down since the last look (cleared by BeginInputFrame).
    private bool AnyButtonPress()
    {
        KeyEvents();
        bool pressed = _held.Count > 0 || _started;
        _started = false;
        return pressed;
    }

    // FlushInput: every key held is forgotten (VControl_ResetInput).
    private void FlushInput()
    {
        KeyEvents();
        _held.Clear();
        _started = false;
    }

    // SleepThread (ONE_SECOND / 40), as the waits look at the keys.
    private long Sleep()
    {
        _clock += OneSecond / 40;
        return _clock;
    }

    private IEnumerable<long> WaitForNoInputUntil(long timeOut, bool resetInput)
    {
        bool buttonPressed = AnyButtonPress();
        while (buttonPressed && (timeOut == Infinite || _clock < timeOut))
        {
            yield return Sleep();
            buttonPressed = AnyButtonPress();
        }
        if (resetInput)
            FlushInput();
        _result = !buttonPressed;
    }

    private IEnumerable<long> WaitForAnyButtonUntil(bool newButton, long timeOut, bool resetInput)
    {
        if (newButton)
        {
            foreach (long t in WaitForNoInputUntil(timeOut, false))
                yield return t;
            if (!_result)
            {
                _result = false;
                yield break;
            }
        }
        bool buttonPressed = AnyButtonPress();
        while (!buttonPressed && (timeOut == Infinite || _clock < timeOut))
        {
            yield return Sleep();
            buttonPressed = AnyButtonPress();
        }
        if (resetInput)
            FlushInput();
        _result = buttonPressed;
    }

    // ------------------------------------------------------------ report.c

    private IEnumerable<long> DoDiscoveryReport()
    {
        var oldContext = _canvas.SetContext(_scan);
        string oldFont = _canvas.SetFont(Font);
        var oldEffect = _canvas.SetFontEffect(0, FontEffectSheet);
        foreach (long t in MakeReport())
            yield return t;
        _canvas.SetFontEffect(oldEffect.Frame, oldEffect.Sheet);
        _canvas.SetFont(oldFont);
        // Restore previous screen.
        Restored = true;
        RestoredAt = _clock;
        Waiting = false;
        _canvas.SetContext(oldContext);
        foreach (long t in WaitForNoInputUntil(Infinite, true))
            yield return t;
    }

    private void ClearReportArea()
    {
        var r = _canvas.FrameRect(CellSheet, 21);
        _canvas.SetBackground(Black);
        _canvas.Clear();
        _canvas.SetForeground(CellColor);
        int startx = 1 + (r.W >> 1) - 1;
        int y0 = 1;
        for (int y = 0; y < NumCellRows; ++y)
        {
            int x0 = startx;
            for (int x = 0; x < NumCellCols; ++x)
            {
                _canvas.Stamp(CellSheet, 21, x0, y0);
                x0 += r.W + 1;
            }
            y0 += r.H + 1;
        }
    }

    // A pointer into the report or end_page_buf: getCharFromString gives 0 past the end, and moves on.
    private struct Ptr
    {
        public bool End;   // in end_page_buf
        public int I;
    }

    private int CharAt(Ptr p)
    {
        var s = p.End ? _end : _report;
        return p.I < s.Length ? s[p.I] : 0;
    }

    private int GetChar(ref Ptr p)
    {
        int c = CharAt(p);
        ++p.I;
        return c;
    }

    private string TextFrom(Ptr p)
    {
        var (at, text) = p.End ? (_endAt, _endText) : (_reportAt, _reportText);
        return p.I < at.Length ? text[at[p.I]..] : "";
    }

    // UniChar_isGraph.
    private static bool IsGraph(int ch) => (ch > 0xa0 && (ch < 0xE000 || ch > 0xF8FF)) || (ch > 0x20 && ch < 0x7f);

    private IEnumerable<long> MakeReport()
    {
        int endPageLen = _end.Length;   // utf8StringCount (end_page_buf)
        int lastC = 0;
        int rowCells = 0;
        var r = _canvas.FrameRect(CellSheet, 18);
        int baselineX = 0, baselineY = 0;
        var pStr = new Ptr();   // t.pStr
        int strLen = _report.Length;
        bool sleepy = true;

        FlushInput();
        InitPageCell();
        while (strLen > 0)
        {
            var pLastStr = pStr;

            // Scan for LFs in the remaining string; a trailing LF will be ignored.
            int lfPos;
            var pNextStr = pStr;
            for (lfPos = strLen; lfPos > 0 && GetChar(ref pNextStr) != '\n'; --lfPos)
            {
            }

            int colCells = 0;
            // Check if the remaining text fits on the current screen.
            if (rowCells == NumCellRows - 1 && (strLen > NumCellCols || lfPos > 1))
            {
                colCells = (NumCellCols >> 1) - (endPageLen >> 1);
                pStr = new Ptr { End = true };
                strLen += endPageLen;
            }
            baselineX = 1 + (r.W >> 1) + colCells * (r.W + 1) - 1;
            do
            {
                var wordEnd = pStr;
                pNextStr = pStr;
                while (IsGraph(GetChar(ref pNextStr)))
                    wordEnd = pNextStr;
                int wordChars = wordEnd.I - pStr.I;
                if ((colCells += wordChars) <= NumCellCols)
                {
                    if ((strLen -= wordChars) != 0)
                        --strLen;
                    long timeOut = _clock;
                    while (wordChars-- > 0)
                    {
                        pNextStr = pStr;
                        int c = GetChar(ref pNextStr);
                        _canvas.Text(baselineX, baselineY, TextAlign.Left, TextFrom(pStr), 1);
                        if (sleepy)
                        {
                            if (c == ',')
                                timeOut += OneSecond / 4;
                            if (c == '.' || c == '!' || c == '?')
                                timeOut += OneSecond / 2;
                            else
                                timeOut += OneSecond / 20;
                            if (wordChars == 0)
                                timeOut += OneSecond / 20;
                            foreach (long t in WaitForAnyButtonUntil(true, timeOut, false))
                                yield return t;
                            if (_result)
                                sleepy = false;   // the rest drawn at once
                        }
                        pStr = pNextStr;
                        baselineX += r.W + 1;
                    }
                    ++colCells;
                    lastC = GetChar(ref pStr);
                    baselineX += r.W + 1;
                }
            } while (colCells <= NumCellCols && lastC != '\n' && strLen > 0);

            baselineY += r.H + 1;
            if (++rowCells == NumCellRows || strLen == 0)
            {
                pStr = pLastStr;
                Waiting = true;
                foreach (long t in WaitForAnyButtonUntil(true, Infinite, false))
                    yield return t;
                Waiting = false;
                if (!_result)
                    break;
                InitPageCell();
            }
        }

        void InitPageCell()
        {
            baselineY = r.H + 1;
            rowCells = 0;
            if (strLen > 0)
            {
                ClearReportArea();
                _canvas.SetForeground(TextColor);
            }
        }
    }
}
