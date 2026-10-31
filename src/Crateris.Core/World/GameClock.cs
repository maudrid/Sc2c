// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Calendar ported from The Ur-Quan Masters 0.8.0 (src/uqm/clock.c:
// IsLeapYear, DaysInMonth, nextClockDay), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.World;

/// <summary>
/// The one shared game clock. Unlike UQM, it runs at a constant rate in
/// every activity (docs/differences.md row 1); only story pauses and the
/// host's pause stop it. Time is counted in battle frames (24 per second).
/// </summary>
public sealed class GameClock
{
    public const int FramesPerSecond = 24;

    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }

    /// <summary>Frames counted towards the next day.</summary>
    public int FramesIntoDay { get; set; }

    public bool Paused { get; set; }

    public GameClock(int year, int month, int day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    public static bool IsLeapYear(int year) =>
        (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0);

    public static int DaysInMonth(int month, int year)
    {
        int[] days = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        return month == 2 && IsLeapYear(year) ? 29 : days[month - 1];
    }

    /// <summary>Advance one frame at the given rate. Returns true when a day passes.</summary>
    public bool Tick(double secondsPerDay)
    {
        if (Paused)
            return false;
        int framesPerDay = Math.Max(1, (int)Math.Round(secondsPerDay * FramesPerSecond));
        if (++FramesIntoDay < framesPerDay)
            return false;
        FramesIntoDay = 0;
        NextDay();
        return true;
    }

    /// <summary>nextClockDay (clock.c).</summary>
    public void NextDay()
    {
        if (++Day > DaysInMonth(Month, Year))
        {
            Day = 1;
            if (++Month > 12)
            {
                Month = 1;
                ++Year;
            }
        }
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2}";
}
