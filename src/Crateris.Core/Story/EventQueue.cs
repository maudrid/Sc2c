// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (clock.c AddEvent, ValidateEvent and
// processClockDayEvents),
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
/// run in the order they were added. Each day the events at the head that
/// are due are taken off and run (<see cref="StoryState.RunDueEvents"/>,
/// processClockDayEvents). Until stage 3c item 4 the events the port
/// didn't run yet (until 3c item 1 every event but the hyperspace
/// encounters and the Slylandro ramps, until item 3 the races' missions,
/// until item 4 the Kohr-Ah's victory and genocide) were taken off on
/// their day all the same and set aside (<see cref="SetAside"/>, stage 3b
/// decision 9), so they didn't hold up the rest; a save holding some has
/// each run, from where the clock is (<see cref="StoryState.RunDueEvents"/>).
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
    /// The events taken off the queue on their day without running, as the
    /// port didn't run them yet (stage 3b, decision 9; none since stage 3c
    /// item 4, which runs every event), in the order they fell due (so in
    /// date order), each with its date, as a save holds them: each is run,
    /// and taken off this list, the first time the clock's events run
    /// (<see cref="StoryState.RunDueEvents"/>: on loading). Saved with the
    /// queue. They aren't in the queue, so they don't
    /// count towards <see cref="MaxEvents"/> (UQM's would have run, freeing
    /// their places).
    /// </summary>
    public IReadOnlyList<StoryEvent> SetAside => _setAside;

    private readonly List<StoryEvent> _setAside = new();

    /// <summary>Whether the event at the head is due: its day is the clock's or before it.</summary>
    internal bool HeadDue(GameClock clock) =>
        _events.Count > 0 && !Before(clock.Year, clock.Month, clock.Day, _events[0].Year, _events[0].Month, _events[0].Day);

    /// <summary>RemoveEvent on the head: takes it off the queue.</summary>
    internal StoryEvent TakeHead()
    {
        var head = _events[0];
        _events.RemoveAt(0);
        _changed();
        return head;
    }

    /// <summary>Takes the first event set aside off the list (the earliest), to run it at last (stage 3c: "from where the clock is").</summary>
    internal StoryEvent TakeSetAside()
    {
        var e = _setAside[0];
        _setAside.RemoveAt(0);
        _changed();
        return e;
    }

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

    /// <summary>
    /// The queue and the events set aside as saved (or as they were: a
    /// visit taken back). Each list in date order; the queue at most
    /// <see cref="MaxEvents"/>. A bad one is an <see cref="InvalidDataException"/>.
    /// </summary>
    internal void Restore(IEnumerable<StoryEvent> events, IEnumerable<StoryEvent>? setAside = null)
    {
        var queue = Checked(events, "events", MaxEvents);
        var aside = Checked(setAside ?? Enumerable.Empty<StoryEvent>(), "events set aside", int.MaxValue);
        _events.Clear();
        _events.AddRange(queue);
        _setAside.Clear();
        _setAside.AddRange(aside);
        _changed();
    }

    private List<StoryEvent> Checked(IEnumerable<StoryEvent> events, string what, int most)
    {
        var list = new List<StoryEvent>();
        foreach (var e in events)
        {
            if (e is null || !_rules.EventIds.ContainsKey(e.Event ?? ""))
                throw new InvalidDataException($"Story state: no event {e?.Event}.");
            if (e.Month is < 1 or > 12 || e.Day < 1 || e.Day > GameClock.DaysInMonth(e.Month, e.Year))
                throw new InvalidDataException($"Story state: {e.Event} is on no such day.");
            if (list.Count > 0 && Before(e.Year, e.Month, e.Day, list[^1].Year, list[^1].Month, list[^1].Day))
                throw new InvalidDataException($"Story state: the {what} aren't in date order.");
            if (list.Count >= most)
                throw new InvalidDataException($"Story state: more than {most} {what}.");
            list.Add(e);
        }
        return list;
    }

    /// <summary>Whether <paramref name="a"/>'s day comes before <paramref name="b"/>'s.</summary>
    internal static bool Before(StoryEvent a, StoryEvent b) => Before(a.Year, a.Month, a.Day, b.Year, b.Month, b.Day);

    private static bool Before(int y, int m, int d, int y2, int m2, int d2) =>
        y < y2 || (y == y2 && (m < m2 || (m == m2 && d < d2)));

    public IEnumerator<StoryEvent> GetEnumerator() => _events.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
