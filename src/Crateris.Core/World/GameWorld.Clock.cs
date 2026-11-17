// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (clock.c GameClockTick and
// MoveGameClockDays), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>
/// The clock's events (docs/plans/stage-3b.md, item 1): each new day, as
/// the world's frames pass it or as days are skipped, the story's events
/// that are due run (processClockDayEvents), as UQM's GameClockTick and
/// MoveGameClockDays run them.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// The clock's events on the clock's day (<see cref="StoryState.RunDueEvents"/>):
    /// as a day begins, and on loading a save, whose events whose day has
    /// passed then run at once. What was taken off the queue, in order (none
    /// without a story).
    /// </summary>
    internal List<StoryState.DueEvent> RunDayEvents() =>
        Story?.RunDueEvents(Clock, HyperspaceEncounterRolls) ?? new();

    /// <summary>
    /// MoveGameClockDays (clock.c): days pass at once, each running its
    /// events, and the day after starts afresh (tick_count = day_in_ticks),
    /// as the starbase's two weeks pass. What was taken off the queue, in
    /// order, the days' one after another.
    /// </summary>
    internal List<StoryState.DueEvent> MoveClockDays(int days)
    {
        var done = new List<StoryState.DueEvent>();
        for (; days > 0; --days)
        {
            Clock.NextDay();
            done.AddRange(RunDayEvents());
        }
        Clock.FramesIntoDay = 0;
        return done;
    }

    /// <summary>
    /// HYPERSPACE_ENCOUNTER_EVENT's check_hyperspace_encounter, called as
    /// the event runs each day: in UQM, the draw for the flagship if it's
    /// in hyperspace; here, for each player in hyperspace (stage 3b,
    /// decision 3). Item 3 draws them; until then nothing is drawn, as no
    /// race has a fleet yet (item 2).
    /// </summary>
    private void HyperspaceEncounterRolls()
    {
        ++HyperspaceEncounterDays;
    }

    /// <summary>How many times the clock's encounter event has asked for the day's draws (tests; item 3 draws then).</summary>
    internal int HyperspaceEncounterDays { get; private set; }
}
