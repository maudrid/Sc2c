// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (clock.c AddEvent and ValidateEvent),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Collections;
using Crateris.Core.World;

namespace Crateris.Core.Story;

/// <summary>An event on the clock: which (gameev.h's names, data/events.json) and the day it falls on.</summary>
public sealed record StoryEvent(string Event, int Year, int Month, int Day);

/// <summary>
/// The clock's events in date order, as clock.c keeps them: a new event
/// goes after every event on its day or before it, so events on one day
/// run in the order they were added. Running them as their days come is
/// stage 3c's; until then they are kept and saved, and the clock passes
/// their days. UQM's clock runs only the first event and only on its day
/// (processClockDayEvents), so stage 3c has to run those already due when
/// it starts, or one in the past would hold up the rest.
/// </summary>
public sealed class EventQueue : IEnumerable<StoryEvent>
{
    /// <summary>The queue holds NUM_EVENTS events, one of each kind (InitGameClock, clock.c); AllocEvent gives no more.</summary>
    public int MaxEvents => _rules.EventIds.Count;

    private readonly List<StoryEvent> _events = new();
    private readonly StoryRules _rules;
    private readonly Action _changed;

    internal EventQueue(StoryRules rules, Action changed)
    {
        _rules = rules;
        _changed = changed;
    }

    public int Count => _events.Count;

    public StoryEvent this[int index] => _events[index];

    /// <summary>
    /// The queuing half of AddEvent: a relative date is counted from the
    /// clock's day (ValidateEvent), and a date before today, or a full
    /// queue, queues nothing. False if nothing was queued.
    /// </summary>
    public bool Add(bool relative, int months, int days, int years, string evt, GameClock clock)
    {
        if (!_rules.EventIds.ContainsKey(evt))
            throw new KeyNotFoundException($"no event {evt}");
        if (months < 0 || days < 0 || years < 0)
            throw new ArgumentOutOfRangeException(nameof(months), "an event's date is counted in unsigned numbers (COUNT)");
        int month = months, day = days, year = years;
        if (relative)
        {
            month += clock.Month - 1;
            year += clock.Year + month / 12;
            month = month % 12 + 1;
            day += clock.Day;
            while (day > GameClock.DaysInMonth(month, year))
            {
                day -= GameClock.DaysInMonth(month, year);
                if (++month > 12)
                {
                    month = 1;
                    ++year;
                }
            }
        }
        if (Before(year, month, day, clock.Year, clock.Month, clock.Day) || _events.Count >= MaxEvents)
            return false;
        int at = 0;
        while (at < _events.Count && !Before(year, month, day, _events[at].Year, _events[at].Month, _events[at].Day))
            ++at;
        _events.Insert(at, new StoryEvent(evt, year, month, day));
        _changed();
        return true;
    }

    internal void Restore(IEnumerable<StoryEvent> events)
    {
        _events.Clear();
        foreach (var e in events)
        {
            if (e is null || !_rules.EventIds.ContainsKey(e.Event ?? ""))
                throw new InvalidDataException($"Story state: no event {e?.Event}.");
            if (e.Month is < 1 or > 12 || e.Day < 1 || e.Day > GameClock.DaysInMonth(e.Month, e.Year))
                throw new InvalidDataException($"Story state: {e.Event} is on no such day.");
            if (_events.Count > 0 && Before(e.Year, e.Month, e.Day, _events[^1].Year, _events[^1].Month, _events[^1].Day))
                throw new InvalidDataException("Story state: the events aren't in date order.");
            if (_events.Count >= MaxEvents)
                throw new InvalidDataException($"Story state: more than {MaxEvents} events.");
            _events.Add(e);
        }
    }

    private static bool Before(int y, int m, int d, int y2, int m2, int d2) =>
        y < y2 || (y == y2 && (m < m2 || (m == m2 && d < d2)));

    public IEnumerator<StoryEvent> GetEnumerator() => _events.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
