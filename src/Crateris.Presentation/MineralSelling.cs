// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/starbas/starbas.c:
// SellMinerals' counting; src/uqm/planets/cargo.c: DrawCargoStrings,
// DrawCargoDisplay, DrawElementAmount, ShowRemainingCapacity; src/uqm/sis.c:
// DeltaSISGauges' resource units; libs/threads/thrcommon.c's SleepThread,
// SleepThreadUntil and TaskSwitch with libs/time/sdl/sdltime.c's clock),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using System.Globalization;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// The allied commander buying the hold's minerals, as UQM's SellMinerals
/// counts it down in the status area before he replies (docs/plans/
/// stage-3.md, item 11): the cargo display drawn (DrawCargoStrings), half
/// a second, then each element category lit in turn and its units sold
/// one at a time, a millisecond each (TaskSwitch), the category's amount,
/// the room free and the resource units redrawn with each; a quarter of a
/// second after each category; a key sells the rest of everything at once
/// (the lit category's amount left as it was drawn, the rest not redrawn,
/// as UQM's); half a second more, and the cargo display is put away
/// (ClearSISRect), when the commander's reply plays.
/// <para>
/// It runs as UQM's code runs, on the client's clock in milliseconds
/// (SDL_GetTicks), the waits in UQM's ticks made from it as UQM's clock
/// makes them; the keys are seen as UQM's AnyButtonPress sees them, a
/// key held or one gone down since the last look (<see cref="KeyDown"/>,
/// <see cref="KeyUp"/>, <see cref="Held"/>). <see cref="Run"/> runs it
/// up to a time and gives what it drew, each call at its time, as UQM's
/// drawing calls on the canvas (checked call for call against UQM's own,
/// tests/golden/selling.json). <see cref="Backdrop"/> is the status
/// message as UQM's status area shows it there already: the resource
/// units before the sale.
/// </para>
/// </summary>
public sealed class MineralSelling
{
    /// <summary>MiscDataFrame: the element icons are its frames from 11, five apart, the bio data's 68.</summary>
    public const string IconSheet = "graphics.miscdata";

    /// <summary>The status area's part the selling draws on, in the screen's pixels: the cargo display and the status message below it.</summary>
    public static readonly URect Area = new(UqmCanvas.SpaceWidth + 2, 20, UqmCanvas.StatusWidth - 4, 137 - 20);

    private const int Categories = MineralSale.Categories;
    private const int ElementOrgY = 35, ElementSpacingY = 9, BioOrgY = 119, FreeOrgY = ElementOrgY + Categories * ElementSpacingY;
    private const int Col0 = 7, Col1 = 32, Col2 = 58, SelOrgX = Col0 + 7 + 5, SelWidth = Col2 - SelOrgX + 1;
    private const int TextBaseline = 6, FieldWidth = UqmCanvas.StatusWidth - 5;
    private const int FirstIcon = 4 * 2 + 3;   // NUM_SCANDOT_TRANSITIONS * 2 + 3
    private const long OneSecond = 840;
    private const int None = 0xFF;   // (BYTE)~0

    private static readonly Rgb Black = Rgb.From15(0, 0, 0);

    private readonly UqmCanvas _canvas;
    private readonly Func<int, string> _gameString;
    private readonly int[] _amounts;   // ElementAmounts
    private readonly IReadOnlyList<int> _worth;
    private readonly int _bio, _capacity, _bombState;
    private int _mass;                 // TotalElementMass
    private readonly AnyButton _input = new();
    private readonly IEnumerator<long> _run;
    private readonly List<(long At, UqmDraw Call)> _drawn = new();
    private long _ms, _wake;

    /// <param name="canvas">The canvas it draws on (its frames: graphics.miscdata's).</param>
    /// <param name="sale">The hold as it was, and what the status area shows beside it.</param>
    /// <param name="gameString">The game's strings (CARGO_STRING_BASE's and STATUS_STRING_BASE's).</param>
    /// <param name="start">When the selling starts, on the clock (milliseconds).</param>
    public MineralSelling(UqmCanvas canvas, MineralSale sale, Func<int, string> gameString, long start)
    {
        _canvas = canvas;
        _gameString = gameString;
        _amounts = sale.Amounts.ToArray();
        _worth = sale.Worth;
        _bio = sale.BioData;
        _capacity = sale.StorageCapacity;
        _bombState = sale.BombState;
        _mass = sale.Mass;
        ResourceUnits = sale.ResourceUnits;
        _ms = _wake = start;
        // What the status area shows there already: the resource units.
        StarbaseScreens.StatusMessage(canvas, ResourceUnits, _bombState, gameString);
        Backdrop = canvas.Drawn.ToList();
        canvas.Drawn.Clear();
        _run = SellMinerals().GetEnumerator();
    }

    /// <summary>The status message as it stands before the selling (DrawStatusMessage (NULL)): drawn under the rest.</summary>
    public IReadOnlyList<UqmDraw> Backdrop { get; }

    /// <summary>The resource units as the status message shows them.</summary>
    public uint ResourceUnits { get; private set; }

    /// <summary>Whether the cargo display is put away (ClearSISRect): the commander's reply plays.</summary>
    public bool Done { get; private set; }

    /// <summary>When it was put away, on the clock.</summary>
    public long DoneAt { get; private set; } = -1;

    /// <summary>Key <paramref name="key"/> went down at <paramref name="at"/> (keys come in the order they happen).</summary>
    public void KeyDown(long at, int key = 0) => _input.Down(at, key);

    /// <summary>Key <paramref name="key"/> came up at <paramref name="at"/>: one not held changes nothing.</summary>
    public void KeyUp(long at, int key = 0) => _input.Up(at, key);

    /// <summary>Key <paramref name="key"/> is held as the selling starts (it went down into the talk's replies).</summary>
    public void Held(int key = 0) => _input.Held(key);

    /// <summary>Every key let go at <paramref name="at"/> (the window lost the keyboard).</summary>
    public void LetGo(long at) => _input.LetGo(at);

    /// <summary>Runs the selling up to <paramref name="now"/> (milliseconds): what it drew meanwhile, each call at its time.</summary>
    public List<(long At, UqmDraw Call)> Run(long now)
    {
        _drawn.Clear();
        while (!Done && _wake <= now)
        {
            int from = _canvas.Drawn.Count;
            long at = _ms;
            bool more = _run.MoveNext();
            for (int i = from; i < _canvas.Drawn.Count; ++i)
                _drawn.Add((at, _canvas.Drawn[i]));
            _canvas.Drawn.RemoveRange(from, _canvas.Drawn.Count - from);
            if (!more)
            {
                Done = true;
                DoneAt = _ms;
            }
            else
                _wake = _run.Current;
        }
        return new List<(long, UqmDraw)>(_drawn);
    }

    // ------------------------------------------------------------ the clock

    // GetTimeCounter: UQM's ticks from the milliseconds (SDLWrapper_GetTimeCounter).
    private long Ticks => (_ms / 1000) * OneSecond + (_ms % 1000) * OneSecond / 1000;

    // SleepThreadUntil: the first millisecond whose ticks reach the time.
    private long SleepUntil(long wake)
    {
        while (Ticks < wake)
            ++_ms;
        return _ms;
    }

    private long Sleep(long period) => SleepUntil(Ticks + period);

    // TaskSwitch: a millisecond.
    private long TaskSwitch() => ++_ms;

    // ------------------------------------------------------------ starbas.c

    private IEnumerable<long> SellMinerals()
    {
        bool sleepy = true;
        for (int i = 0; i < Categories; ++i)
        {
            long timeIn = 0;
            if (i == 0)
            {
                DrawCargoStrings(None, None);
                yield return Sleep(OneSecond / 2);
                timeIn = Ticks;
                DrawCargoStrings(0, 0);
            }
            else if (sleepy)
            {
                DrawCargoStrings(i - 1, i);
                timeIn = Ticks;
            }

            int amount = _amounts[i];
            if (amount != 0)
            {
                do
                {
                    if (!sleepy || _input.Press(_ms))
                    {
                        sleepy = false;
                        _amounts[i] = 0;
                        _mass -= amount;
                        DeltaSisGauges(amount * _worth[i]);
                        break;
                    }
                    --_amounts[i];
                    --_mass;
                    yield return TaskSwitch();
                    timeIn = Ticks;
                    DrawCargoStrings(i, i);
                    ShowRemainingCapacity();
                    DeltaSisGauges(_worth[i]);
                } while (--amount != 0);
            }
            if (sleepy)
            {
                yield return SleepUntil(timeIn + OneSecond / 4);
                timeIn = Ticks;
            }
        }
        yield return Sleep(OneSecond / 2);
        // ClearSISRect (DRAW_SIS_DISPLAY): the cargo display put away.
    }

    // sis.c's DeltaSISGauges (0, 0, delta): the resource units' part.
    private void DeltaSisGauges(int delta)
    {
        if (delta == 0)
            return;
        var old = _canvas.SetContext(_canvas.Status);
        _canvas.SetFont("tiny");
        if (delta < 0 && ResourceUnits <= (uint)-delta)
            ResourceUnits = 0;
        else
            ResourceUnits = (uint)(ResourceUnits + delta);
        StarbaseScreens.StatusMessage(_canvas, ResourceUnits, _bombState, _gameString);
        _canvas.SetContext(old);
    }

    // ------------------------------------------------------------ cargo.c

    private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    private void ShowRemainingCapacity()
    {
        var old = _canvas.SetContext(_canvas.Status);
        _canvas.SetFont("tiny");
        var r = new URect(40, FreeOrgY, Col2 + 1 - 40 + 1, ElementSpacingY - 2);
        _canvas.SetForeground(CargoDisplay.Back);
        _canvas.FillRect(r);
        _canvas.SetForeground(CargoDisplay.Worth);
        // COUNT arithmetic: GetStorageBayCapacity () - TotalElementMass, printed with %u.
        _canvas.Text(Col2 + 1, r.Y + TextBaseline, TextAlign.Right, N((uint)(_capacity - _mass)));
        _canvas.SetContext(old);
    }

    private void DrawElementAmount(int element, bool selected)
    {
        var r = new URect(SelOrgX, element == Categories ? BioOrgY : ElementOrgY + element * ElementSpacingY, SelWidth,
            ElementSpacingY - 2);
        _canvas.SetForeground(selected ? CargoDisplay.SelectedBack : CargoDisplay.Back);
        _canvas.FillRect(r);
        int y = r.Y + TextBaseline;
        string amount;
        if (element == Categories)
            amount = N(_bio);
        else
        {
            _canvas.SetForeground(selected ? CargoDisplay.SelectedWorth : CargoDisplay.Worth);
            _canvas.Text(Col1, y, TextAlign.Right, N(_worth[element]));
            amount = N(_amounts[element]);
        }
        _canvas.SetForeground(selected ? CargoDisplay.SelectedAmount : CargoDisplay.Amount);
        _canvas.Text(Col2, y, TextAlign.Right, amount);
    }

    private void DrawCargoDisplay()
    {
        var r = new URect(2, 20, FieldWidth + 1, 129 - 20);
        _canvas.StarConBox(r, 1, CargoDisplay.BoxMedium, CargoDisplay.BoxDark, true, CargoDisplay.Back);

        // The "CARGO" title.
        _canvas.SetFont("starcon");
        _canvas.SetForeground(CargoDisplay.SelectedAmount);
        _canvas.Text((UqmCanvas.StatusWidth >> 1) - 1, 27, TextAlign.Center, _gameString(GameStrings.CargoBase));

        _canvas.SetFont("tiny");
        int frame = _canvas.Frame(IconSheet, FirstIcon);
        var bounds = _canvas.FrameRect(IconSheet, frame);
        r = new URect(Col0, 0, bounds.W, bounds.H);
        int sx = r.X + (r.W >> 1);
        int cy = ElementOrgY;

        // The column headings.
        _canvas.SetForeground(CargoDisplay.Worth);
        _canvas.Text(Col1, cy - 1, TextAlign.Right, "$");
        _canvas.Text(Col2, cy - 1, TextAlign.Right, "#");

        // Each element's icon and amounts.
        for (int i = 0; i < Categories; ++i, cy += ElementSpacingY)
        {
            _canvas.SetForeground(Black);
            r.Y = cy;
            _canvas.FillRect(r);
            _canvas.Stamp(IconSheet, frame, sx, r.Y + (r.H >> 1));
            frame = _canvas.Frame(IconSheet, frame + 5);
            DrawElementAmount(i, false);
        }

        // The bio data's.
        _canvas.SetForeground(Black);
        r.Y = BioOrgY;
        _canvas.FillRect(r);
        _canvas.Stamp(IconSheet, 68, sx, r.Y + (r.H >> 1));
        DrawElementAmount(Categories, false);

        // The line over the bio data, and "Free".
        _canvas.SetForeground(CargoDisplay.SelectedBack);
        _canvas.FillRect(new URect(4, BioOrgY - 2, FieldWidth - 3, 1));
        _canvas.Text(5, FreeOrgY + TextBaseline, TextAlign.Left, _gameString(GameStrings.CargoBase + 1));

        ShowRemainingCapacity();
    }

    private void DrawCargoStrings(int oldElement, int newElement)
    {
        var old = _canvas.SetContext(_canvas.Status);
        _canvas.SetFont("tiny");
        if (oldElement > Categories)
        {
            // Asked for the initial display.
            DrawCargoDisplay();
            oldElement = newElement;
        }
        if (oldElement != newElement)
            DrawElementAmount(oldElement, false);
        if (newElement != None)
            DrawElementAmount(newElement, true);
        _canvas.SetContext(old);
    }
}
