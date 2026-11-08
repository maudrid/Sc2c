// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm.c's add_text,
// getLineWithinWidth, _count_lines, DrawSISComWindow, RefreshResponses and
// DoConvSummary; src/libs/sound/trackplayer.c's SplitSubPages), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>What one of the communication screen's text drawing calls is.</summary>
public enum CommTextKind
{
    /// <summary>A line of text (font_DrawText), in <see cref="CommTextDraw.Color"/>.</summary>
    Text,

    /// <summary>The alien's line (font_DrawTracedText): in <see cref="CommTextDraw.Color"/>, traced in <see cref="CommTextDraw.Trace"/>.</summary>
    Traced,

    /// <summary>A filled rectangle (DrawFilledRectangle): X, Y its corner, Count by Offset its size.</summary>
    Rectangle,

    /// <summary>ActivityFrame's frame <see cref="CommTextDraw.Count"/> (the replies' scroll arrows), X as for a frame 0 wide.</summary>
    Stamp,
}

/// <summary>
/// One of the communication screen's text drawing calls, in UQM's pixels
/// of the screen's window: a line from <see cref="Source"/> (the text's
/// place among those given; -1 the bullet; -2 MORE, in its dots) from byte
/// <see cref="Offset"/>, <see cref="Count"/> characters (-1 all).
/// </summary>
public readonly record struct CommTextDraw(CommTextKind Kind, int X, int Y, TextAlign Align, int Count, int Source, int Offset,
    Rgb Color, Rgb Trace = default);

/// <summary>
/// The talk's text as UQM's comm.c lays it out: the alien's subtitles
/// (paged by trackplayer.c's SplitSubPages, wrapped to its text width in
/// its font, placed by its baseline and alignment), the player's replies
/// (in the player's font, the lit one highlighted, with the scroll
/// arrows) and the conversation summary (the subtitles so far, in the tiny
/// font, a page at a time).
/// </summary>
public static class CommText
{
    /// <summary>SIS_SCREEN_WIDTH and SIS_SCREEN_HEIGHT: the communication window.</summary>
    public const int Width = 242, Height = 227;

    /// <summary>SLIDER_Y and SLIDER_HEIGHT: the line between the alien's window and the replies.</summary>
    public const int SliderY = 107, SliderHeight = 15;

    /// <summary>TEXT_X_OFFS.</summary>
    public const int TextXOffs = 1;

    /// <summary>TEXT_SPEED: milliseconds a character a page is shown, a second at least.</summary>
    public const int TextSpeed = 80;

    /// <summary>The colours (colors.h): COMM_PLAYER_BACKGROUND_COLOR, _TEXT_NORMAL_, _TEXT_HIGHLIGHT_; COMM_HISTORY_BACKGROUND_COLOR, _TEXT_; COMM_MORE_TEXT_COLOR.</summary>
    public static readonly Rgb PlayerBackground = new(0x00, 0x00, 0xA5), PlayerNormal = new(0x00, 0xA5, 0xA5),
        PlayerHighlight = new(0xD6, 0xD6, 0xD6), HistoryBackground = new(0x00, 0x29, 0x00),
        HistoryText = new(0x00, 0x84, 0x00), MoreText = new(0x00, 0xBD, 0x00);

    /// <summary>The bullet before each reply (STR_BULLET), and MORE's dots (STR_MIDDLE_DOT).</summary>
    public const string Bullet = "•", MiddleDot = "·";

    /// <summary>
    /// SplitSubPages: a line's pages, split at its line breaks, a page
    /// ending mid-sentence with "..." and the next starting "..", each
    /// shown 80 milliseconds a character, a second at least (the last
    /// page's time only a suggestion, as it plays to its end).
    /// </summary>
    public static List<(byte[] Page, int Millis)> SplitPages(ReadOnlySpan<byte> text)
    {
        const int maxPages = 50;
        var pages = new List<(byte[], int)>();
        bool leadEllipsis = false;
        int at = 0;
        while (pages.Count < maxPages && at < text.Length && text[at] != 0)
        {
            int pos = 0;
            while (at + pos < text.Length && text[at + pos] is not (0 or (byte)'\r' or (byte)'\n'))
                ++pos;
            bool more = at + pos < text.Length && text[at + pos] != 0;
            byte last = pos > 0 ? text[at + pos - 1] : (byte)0;
            bool afterEllipsis = more && pos > 0 && !IsPunct(last) && !IsSpace(last);
            var page = new List<byte>();
            if (leadEllipsis)
                page.AddRange(".."u8.ToArray());
            page.AddRange(text.Slice(at, pos).ToArray());
            if (afterEllipsis)
                page.AddRange("..."u8.ToArray());
            pages.Add((page.ToArray(), Math.Max(pos * TextSpeed, 1000)));
            leadEllipsis = afterEllipsis;
            at += pos;
            while (at < text.Length && text[at] is (byte)'\r' or (byte)'\n')
                ++at;
        }
        return pages;
    }

    // C's ispunct and isspace, in the C locale (a byte as an int).
    private static bool IsPunct(byte b) => b is > 32 and < 127 && !char.IsLetterOrDigit((char)b);

    private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';

    /// <summary>
    /// getLineWithinWidth: how much of the text from <paramref name="start"/>
    /// fits within the width, word by word, up to a line break or the end:
    /// <paramref name="charCount"/> (TEXT.CharCount, as TextRect leaves it)
    /// and where the next line starts. True if the whole line fitted.
    /// </summary>
    public static bool LineWithinWidth(UqmFont font, ReadOnlySpan<byte> text, int start, ref int charCount,
        out int next, int maxWidth, int maxChars)
    {
        int ptr = start, count = 0;
        maxChars = (ushort)maxChars;
        for (;;)
        {
            int wordStart = ptr;
            bool eol, done;
            for (;;)
            {
                if (ptr >= text.Length || text[ptr] == 0)
                {
                    eol = done = true;
                    break;
                }
                int ch = UqmFont.NextChar(text, ref ptr);
                eol = ch is 0 or '\n' or '\r';
                done = eol || count >= maxChars;
                if (done || ch == ' ')
                    break;
                count++;
            }
            int oldCount = charCount;
            var (width, clobbered) = font.TextRect(text[start..], count);
            charCount = clobbered;
            if (width >= maxWidth)
            {
                charCount = oldCount;
                next = wordStart;
                return false;
            }
            if (done)
            {
                next = ptr;
                return eol;
            }
            count++;   // the space between the words
        }
    }

    /// <summary>
    /// add_text (1, ...): the alien's subtitle page, wrapped to its text
    /// width in its font, each line traced, from its baseline as its
    /// vertical alignment places the lines (0 top, 1 middle, 2 bottom).
    /// </summary>
    public static List<CommTextDraw> Subtitle(UqmFont font, ReadOnlySpan<byte> page, int baselineX, int baselineY,
        TextAlign align, int valign, int textWidth, Rgb fore, Rgb back)
    {
        var draws = new List<CommTextDraw>();
        int leading = font.Leading;
        int y = baselineY;
        if (valign is 1 or 2)
        {
            int lines = 0, s = 0, count = 0;
            bool eol;
            do
            {
                ++lines;
                eol = LineWithinWidth(font, page, s, ref count, out int next, textWidth, 0xFFFF);
                s = next;
            } while (!eol);
            y -= valign == 2 ? leading * lines : leading * lines / 2;
            if (y < 0)
                y = 0;
        }
        int start = 0, charCount = 0xFFFF, maxChars = 0xFFFF;
        bool end;
        do
        {
            y += leading;
            end = LineWithinWidth(font, page, start, ref charCount, out int next, textWidth, maxChars);
            maxChars = (ushort)(maxChars - charCount);
            if (maxChars != 0)
                --maxChars;
            draws.Add(new CommTextDraw(CommTextKind.Traced, baselineX, y, align, Count(charCount), 0, start, fore, back));
            start = next;
        } while (!end && maxChars != 0);
        return draws;
    }

    private static int Count(int charCount) => charCount == 0xFFFF ? -1 : charCount;

    /// <summary>
    /// RefreshResponses: the replies' window (DrawSISComWindow) and the
    /// replies from <paramref name="top"/>, each after a bullet, the lit
    /// one highlighted, wrapped to the window, those below its foot not
    /// drawn; the up arrow if replies are above, the down arrow if some
    /// didn't fit.
    /// </summary>
    public static List<CommTextDraw> Replies(UqmFont font, IReadOnlyList<byte[]> replies, int top, int cur)
    {
        var draws = new List<CommTextDraw>();
        int leading = font.Leading;
        int textWidth = Width - 8 - (TextXOffs << 2);
        draws.Add(new CommTextDraw(CommTextKind.Rectangle, 0, SliderY + SliderHeight, TextAlign.Left, Width, -1,
            Height - (SliderY + SliderHeight), PlayerBackground));
        int y = SliderY + SliderHeight + 1;
        for (int r = top; r < replies.Count; ++r)
        {
            var color = r == cur ? PlayerHighlight : PlayerNormal;
            int bx = TextXOffs + 8, by = y + leading;
            draws.Add(new CommTextDraw(CommTextKind.Text, bx - 8, by, TextAlign.Left, -1, -1, 0, color));
            by -= leading;
            int start = 0, charCount = 0xFFFF, maxChars = 0xFFFF;
            bool end;
            do
            {
                by += leading;
                end = LineWithinWidth(font, replies[r], start, ref charCount, out int next, textWidth, maxChars);
                maxChars = (ushort)(maxChars - charCount);
                if (maxChars != 0)
                    --maxChars;
                if (by < Height)
                    draws.Add(new CommTextDraw(CommTextKind.Text, bx, by, TextAlign.Left, Count(charCount), r, start, color));
                start = next;
            } while (!end && maxChars != 0);
            y = by;
        }
        if (top > 0)
            draws.Add(new CommTextDraw(CommTextKind.Stamp, Width - 1, SliderY + SliderHeight + 1, TextAlign.Left, 6, -1, 0, default));
        else if (y > Height)
            draws.Add(new CommTextDraw(CommTextKind.Stamp, Width - 1, Height - 2, TextAlign.Left, 7, -1, 0, default));
        return draws;
    }

    /// <summary>DELTA_Y_SUMMARY and MAX_SUMM_ROWS: the summary's rows.</summary>
    public const int SummaryRowHeight = 8, SummaryRows = (Height - SliderY - SliderHeight) / SummaryRowHeight - 1;

    /// <summary>
    /// DoConvSummary: the talk's subtitles so far, page by page, each row
    /// a line of the tiny font wrapped to the window, a subtitle going on
    /// over a page's foot; MORE when more is to come.
    /// </summary>
    public static List<List<CommTextDraw>> Summary(UqmFont tiny, IReadOnlyList<byte[]> subtitles)
    {
        var pages = new List<List<CommTextDraw>>();
        int sub = 0;
        (int Sub, int Start)? leftOver = null;
        int maxWidth = Width - 2 - 2;
        bool first = true;
        while (first || sub < subtitles.Count || leftOver is not null)
        {
            first = false;
            var draws = new List<CommTextDraw>
            {
                new(CommTextKind.Rectangle, 0, 0, TextAlign.Left, Width, -1, Height - SliderY - SliderHeight + 2, HistoryBackground),
            };
            int y = SummaryRowHeight, row;
            for (row = 0; row < SummaryRows && sub < subtitles.Count; ++row, ++sub)
            {
                int start = 0;
                if (leftOver is { } lo)
                {
                    start = lo.Start;
                    leftOver = null;
                }
                var text = subtitles[sub];
                int charCount = 0xFFFF, next = start;
                for (; row < SummaryRows && !LineWithinWidth(tiny, text, start, ref charCount, out next, maxWidth, 0xFFFF); ++row)
                {
                    draws.Add(new CommTextDraw(CommTextKind.Text, 2, y, TextAlign.Left, Count(charCount), sub, start, HistoryText));
                    y += SummaryRowHeight;
                    start = next;
                    charCount = 0xFFFF;
                }
                if (row >= SummaryRows)
                {
                    leftOver = (sub, next);
                    break;
                }
                draws.Add(new CommTextDraw(CommTextKind.Text, 2, y, TextAlign.Left, Count(charCount), sub, start, HistoryText));
                y += SummaryRowHeight;
            }
            if (row >= SummaryRows && (sub < subtitles.Count || leftOver is not null))
                draws.Add(new CommTextDraw(CommTextKind.Text, Width >> 1, y, TextAlign.Center, -1, -2, 0, MoreText));
            pages.Add(draws);
        }
        return pages;
    }
}
