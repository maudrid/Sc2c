// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Laid out after The Ur-Quan Masters 0.8.0's cargo display
// (src/uqm/planets/cargo.c: DrawCargoStrings, DrawCargoDisplay,
// DrawElementAmount, ShowRemainingCapacity), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under
// the GNU GPL 2 or later.

using System.Globalization;
using System.Text;
using Crateris.Core.Content;
using Crateris.Core.Session;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// The giving screen (stage 3a, items 9 and 11), drawn as if UQM had one:
/// UQM has no giving between players, so it's laid out after the
/// Manifest's cargo display (cargo.c's DrawCargoDisplay and
/// DrawElementAmount, as <see cref="MineralSelling"/> ports them), in its
/// place in the status area, with its box, colours, fonts, icons and lit
/// row. Under its title (our own word, <see cref="Title"/>, as no game
/// string has one) are the player given to (their name, cut to fit the
/// box), the resource units, the eight element categories, the room the
/// receiver has for minerals (the cargo display's Free row), and, below
/// the line, the bio data; each of the giver's rows has what they have
/// (the cargo display's worth column) and the amount to give (its amount
/// column), under the column heads "Have" and "Give" (a number too long
/// for its row's room is left out, the amount to give kept). The row the cursor
/// is on is lit as DrawElementAmount lights one. Cargo goes only to a
/// player in the same solar system: when the receiver is elsewhere the
/// cargo rows are dimmed, their icons drawn as shapes in the dim colour.
/// </summary>
public static class GivingScreen
{
    /// <summary>The title, in the StarCon font as the cargo display's "CARGO".</summary>
    public const string Title = "GIVE";

    /// <summary>The column heads: what the giver has, and what they'll give.</summary>
    public const string HaveHead = "Have", GiveHead = "Give";

    /// <summary>The resource units row's label (drawn only if the wallet's number leaves it room).</summary>
    public const string UnitsLabel = "RU";

    /// <summary>The cargo rows' colour while the receiver is in another system.</summary>
    public static readonly Rgb Dim = Rgb.From15(0x07, 0x07, 0x10);

    // cargo.c's places, the rows pushed down by the receiver's and the units' rows.
    private const int BoxX = 2, BoxY = 20, FieldWidth = UqmCanvas.StatusWidth - 5;
    private const int Spacing = 9, RowHeight = Spacing - 2, TextBaseline = 6;
    private const int ToOrgY = 29, UnitsOrgY = 44, ElementOrgY = UnitsOrgY + Spacing;
    private const int FreeOrgY = ElementOrgY + Categories * Spacing, BioOrgY = FreeOrgY + 12;
    private const int BoxBottom = BioOrgY + Spacing;   // the box's last line, as the cargo display's
    private const int Col0 = 7, HaveCol = 36, GiveCol = 58, Gap = 3, SelOrgX = Col0 + 7 + 5;
    private const int WideX = 4, WideWidth = GiveCol - WideX + 1;   // the rows without an icon, lit across the box
    private const int Categories = Core.World.ShipHold.Categories;
    private const int FirstIcon = 4 * 2 + 3;   // NUM_SCANDOT_TRANSITIONS * 2 + 3
    private const int BioIcon = 68;

    private static readonly Rgb Black = Rgb.From15(0, 0, 0);

    /// <summary>The status area's part it draws on, in the screen's pixels: its box.</summary>
    public static readonly URect Area = new(UqmCanvas.SpaceWidth + BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY + 1);

    /// <summary>The widest a receiver's name may be drawn, in pixels (the lit row less a pixel each side).</summary>
    public const int NameRoom = WideWidth - 2;

    /// <summary>A row's top, in the status area (<see cref="TradeScreen"/>'s rows).</summary>
    public static int RowY(int row) => row switch
    {
        TradeScreen.PlayerRow => ToOrgY,
        TradeScreen.UnitsRow => UnitsOrgY,
        TradeScreen.BioRow => BioOrgY,
        _ => ElementOrgY + (row - 2) * Spacing,
    };

    /// <summary>The Free row's top: the receiver's room for minerals.</summary>
    public const int FreeY = FreeOrgY;

    /// <summary>
    /// Draws the screen on <paramref name="canvas"/> (its frames:
    /// graphics.miscdata's; its fonts starcon and tiny), in the status area.
    /// </summary>
    /// <param name="shown">The screen as it stands (<see cref="TradeScreen.Follow"/>).</param>
    /// <param name="gameString">The game's strings (CARGO_STRING_BASE's "Free").</param>
    /// <param name="tinyWidth">A text's width in the tiny font, in pixels (to cut the receiver's name to fit).</param>
    public static void Draw(UqmCanvas canvas, TradeShown shown, Func<int, string> gameString, Func<string, int> tinyWidth)
    {
        var old = canvas.SetContext(canvas.Status);

        // The box, as DrawCargoDisplay's (DrawStarConBox), two rows taller.
        canvas.StarConBox(new URect(BoxX, BoxY, FieldWidth + 1, BoxBottom - BoxY + 1), 1, CargoDisplay.BoxMedium,
            CargoDisplay.BoxDark, true, CargoDisplay.Back);
        canvas.SetFont("starcon");
        canvas.SetForeground(CargoDisplay.SelectedAmount);
        canvas.Text((UqmCanvas.StatusWidth >> 1) - 1, 27, TextAlign.Center, Title);

        canvas.SetFont("tiny");
        // The player given to, across the box.
        bool lit = shown.Row == TradeScreen.PlayerRow;
        canvas.SetForeground(lit ? CargoDisplay.SelectedBack : CargoDisplay.Back);
        canvas.FillRect(new URect(WideX, ToOrgY, WideWidth, RowHeight));
        canvas.SetForeground(lit ? CargoDisplay.SelectedAmount : CargoDisplay.Amount);
        canvas.Text(WideX + (WideWidth >> 1), ToOrgY + TextBaseline, TextAlign.Center,
            shown.To is null ? "-" : Cut(shown.ToName, NameRoom, tinyWidth));

        // The column heads.
        canvas.SetForeground(CargoDisplay.Worth);
        canvas.Text(HaveCol, UnitsOrgY - 1, TextAlign.Right, HaveHead);
        canvas.Text(GiveCol, UnitsOrgY - 1, TextAlign.Right, GiveHead);

        // The icons' size, as DrawCargoDisplay takes it: the first category's.
        bool dim = !shown.Here;
        int frame = canvas.Frame(MineralSelling.IconSheet, FirstIcon);
        var bounds = canvas.FrameRect(MineralSelling.IconSheet, frame);
        var icon = new URect(Col0, 0, bounds.W, bounds.H);
        int sx = icon.X + (icon.W >> 1);

        // The resource units: no icon (UQM has none), so lit across the box, labelled if there's room.
        Amount(TradeScreen.UnitsRow, dimmed: false);

        // Each element's icon and amounts, then the receiver's room and the bio data's, as DrawCargoDisplay draws them.
        for (int i = 0; i < Categories; ++i)
        {
            Icon(RowY(2 + i), frame);
            frame = canvas.Frame(MineralSelling.IconSheet, frame + 5);
            Amount(2 + i, dim);
        }
        Icon(BioOrgY, BioIcon);
        Amount(TradeScreen.BioRow, dim);

        // The line over the bio data, and the receiver's room (the cargo display's Free row, ShowRemainingCapacity).
        canvas.SetForeground(CargoDisplay.SelectedBack);
        canvas.FillRect(new URect(4, BioOrgY - 2, FieldWidth - 3, 1));
        canvas.SetForeground(dim ? Dim : CargoDisplay.SelectedBack);
        canvas.Text(5, FreeOrgY + TextBaseline, TextAlign.Left, gameString(GameStrings.CargoBase + 1));
        canvas.SetForeground(dim ? Dim : CargoDisplay.Worth);
        canvas.Text(GiveCol + 1, FreeOrgY + TextBaseline, TextAlign.Right, shown.Room is int room ? N(room) : "");

        canvas.SetContext(old);

        void Icon(int y, int f)
        {
            canvas.SetForeground(Black);
            icon.Y = y;
            canvas.FillRect(icon);
            if (dim)
            {
                canvas.SetForeground(Dim);
                canvas.FilledStamp(MineralSelling.IconSheet, f, sx, icon.Y + (icon.H >> 1));
            }
            else
                canvas.Stamp(MineralSelling.IconSheet, f, sx, icon.Y + (icon.H >> 1));
        }

        // DrawElementAmount, with what the giver has in the worth's column and the amount to give in the amount's.
        void Amount(int row, bool dimmed)
        {
            bool selected = shown.Row == row;
            bool wide = row == TradeScreen.UnitsRow;
            int x = wide ? WideX : SelOrgX;
            var r = new URect(x, RowY(row), GiveCol - x + 1, RowHeight);
            canvas.SetForeground(selected ? CargoDisplay.SelectedBack : CargoDisplay.Back);
            canvas.FillRect(r);
            int y = r.Y + TextBaseline;
            string have = N(shown.Have[row]), give = N(shown.Give[row]);
            // The units' numbers may be long: what they have moves left of a wide amount to give, and the label
            // gives way to them.
            // A number too long for the room left of it (past the box's edge, or over the icon) is left out.
            int haveAt = wide ? Math.Min(HaveCol, GiveCol - tinyWidth(give) - Gap) : HaveCol;
            int left = wide ? WideX + 1 : icon.X + icon.W + 1;
            canvas.SetForeground(dimmed ? Dim : selected ? CargoDisplay.SelectedWorth : CargoDisplay.Worth);
            if (wide && WideX + 1 + tinyWidth(UnitsLabel) + Gap <= haveAt - tinyWidth(have))
                canvas.Text(WideX + 1, y, TextAlign.Left, UnitsLabel);
            if (haveAt - tinyWidth(have) >= left)
                canvas.Text(haveAt, y, TextAlign.Right, have);
            canvas.SetForeground(dimmed ? Dim : selected ? CargoDisplay.SelectedAmount : CargoDisplay.Amount);
            canvas.Text(GiveCol, y, TextAlign.Right, give);
        }
    }

    /// <summary>
    /// The text below the box: the notice while there is one (a gift given
    /// or refused, another player's report), else who it gives to
    /// (<paramref name="to"/>), then the keys, in at most
    /// <paramref name="most"/> lines (a <paramref name="most"/> under one taken as one) that
    /// <paramref name="fits"/>. A notice
    /// too long to leave the keys room has every line (the keys are back
    /// once it's gone). Each line with whether it's the keys'.
    /// </summary>
    public static List<(string Line, bool Keys)> Below(string to, string notice, string keys, int most, Func<string, bool> fits)
    {
        most = Math.Max(1, most);
        var keyLines = Wrap(keys, Math.Min(KeyLines, most - 1), fits);
        var top = notice != "" ? notice : to;
        var lines = Wrap(top, int.MaxValue, fits);
        if (notice != "" && lines.Count + keyLines.Count > most)
            keyLines.Clear();
        return Cut(lines, most - keyLines.Count, fits).Select(l => (l, false))
            .Concat(keyLines.Select(l => (l, true))).ToList();
    }

    /// <summary>The most lines the keys below the box take.</summary>
    public const int KeyLines = 3;

    /// <summary>
    /// The text below the box (who it gives to, a notice, the keys) wrapped
    /// at words, and at its own line breaks (a blank line left out), into
    /// lines that <paramref name="fits"/>, at most
    /// <paramref name="most"/>: one cut short ends in an ellipsis, a word
    /// too long for that last line cut a whole character at a time. A word
    /// too long for a line of its own (a long name) is broken, a whole
    /// character at a time, over as many lines as it takes.
    /// </summary>
    public static List<string> Wrap(string text, int most, Func<string, bool> fits)
    {
        var lines = new List<string>();
        foreach (var given in text.Split('\n'))
        {
            var line = "";
            foreach (var word in given.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(w => Broken(w, fits)))
            {
                string longer = line == "" ? word : line + " " + word;
                if (line != "" && !fits(longer))
                {
                    lines.Add(line);
                    line = word;
                }
                else
                    line = longer;
            }
            if (line != "")
                lines.Add(line);
        }
        return Cut(lines, most, fits);
    }

    // Lines wrapped, at most most of them: the last of those cut short ends in an ellipsis.
    private static List<string> Cut(List<string> wrapped, int most, Func<string, bool> fits)
    {
        if (wrapped.Count <= most)
            return wrapped;
        if (most <= 0)
            return new List<string>();
        var lines = wrapped.Take(most).ToList();
        string last = lines[^1];
        while (last.Contains(' ') && !fits(last + "…"))
            last = last[..last.LastIndexOf(' ')];
        while (!fits(last + "…") && new StringInfo(last).LengthInTextElements > 1)
            last = new StringInfo(last).SubstringByTextElements(0, new StringInfo(last).LengthInTextElements - 1);
        lines[^1] = last + "…";
        return lines;
    }

    // A word too long for a line, in pieces each as long as fits (at least a character); a word that fits whole.
    private static IEnumerable<string> Broken(string word, Func<string, bool> fits)
    {
        if (fits(word))
        {
            yield return word;
            yield break;
        }
        var piece = new StringBuilder();
        var letters = StringInfo.GetTextElementEnumerator(word);
        while (letters.MoveNext())
        {
            string letter = letters.GetTextElement();
            if (piece.Length > 0 && !fits(piece + letter))
            {
                yield return piece.ToString();
                piece.Clear();
            }
            piece.Append(letter);
        }
        if (piece.Length > 0)
            yield return piece.ToString();
    }

    private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="name"/> cut to its longest start no wider than
    /// <paramref name="room"/> pixels, a whole character at a time (a
    /// letter with its accents, never half of one), the whole of it if it
    /// fits.
    /// </summary>
    public static string Cut(string name, int room, Func<string, int> width)
    {
        if (width(name) <= room)
            return name;
        var cut = new StringBuilder();
        var letters = StringInfo.GetTextElementEnumerator(name);
        while (letters.MoveNext())
        {
            string letter = letters.GetTextElement();
            if (width(cut + letter) > room)
                break;
            cut.Append(letter);
        }
        return cut.ToString();
    }
}
