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
/// The clock's events (docs/plans/stage-3b.md, item 1; docs/plans/stage-3c.md,
/// item 1): each new day, as the world's frames pass it or as days are
/// skipped, the story's events that are due run (processClockDayEvents),
/// as UQM's GameClockTick and MoveGameClockDays run them, held by the
/// location gates while a player is in their race's system.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// The clock's events on the clock's day (<see cref="StoryState.RunDueEvents"/>):
    /// as a day begins, and on loading a save, whose events whose day has
    /// passed then run at once, and whose events set aside that the port
    /// now runs run then too, from where the clock is. What each that
    /// changed a race told (a fleet's arrival's too) is told to every
    /// player (<see cref="TellStoryEvent"/>), with any news left from events
    /// run outside a talk since the last day. What
    /// was taken off the queue, in order (none without a story).
    /// <paramref name="rolls"/>: whether the day's hyperspace encounters
    /// are rolled (not for <c>--start-date</c>'s days); <paramref name="loading"/>:
    /// a save is loading, nobody in the game yet, so the location gates
    /// ask where every saved player's ship is (<see cref="PlayerInSystem"/>).
    /// </summary>
    internal List<StoryState.DueEvent> RunDayEvents(bool rolls = true, bool loading = false)
    {
        if (Story is not { } story)
            return new();
        var done = story.RunDueEvents(Clock, rolls ? HyperspaceEncounterRolls : null,
            loading ? special => PlayerInSystem(special, everyone: true) : special => PlayerInSystem(special));
        foreach (var news in story.TakeNews())
            TellStoryEvent(news, "");
        return done;
    }

    /// <summary>
    /// MoveGameClockDays (clock.c): days pass at once, each running its
    /// events, and the day after starts afresh (tick_count = day_in_ticks),
    /// as the starbase's two weeks pass. What was taken off the queue, in
    /// order, the days' one after another. <paramref name="rolls"/>: as
    /// <see cref="RunDayEvents"/>'s.
    /// </summary>
    internal List<StoryState.DueEvent> MoveClockDays(int days, bool rolls = true)
    {
        var done = new List<StoryState.DueEvent>();
        for (; days > 0; --days)
        {
            Clock.NextDay();
            done.AddRange(RunDayEvents(rolls));
            DayForTests?.Invoke();
        }
        Clock.FramesIntoDay = 0;
        return done;
    }

    /// <summary>
    /// The location gates' question (the stage 3c plan's question 1):
    /// whether any player present is in the system of the special star
    /// named (gendef.h's name: SPATHI_DEFINED, ZOQFOT_DEFINED), flying,
    /// orbiting, landed, docked, or in a meeting, talk or battle there (the
    /// ship stays in its system for those). A wreck (a guest's ship lost,
    /// out of its battle) doesn't hold an event: it can't leave by itself.
    /// A player away doesn't either, but while a save loads
    /// (<paramref name="everyone"/>), before anyone is back in the game,
    /// every saved player's ship counts where the save has it. Without the
    /// systems, nobody is.
    /// </summary>
    internal bool PlayerInSystem(string special, bool everyone = false) =>
        InSystemForTests?.Invoke(special)
        ?? (Systems is { } systems
            && _players.Any(p => (everyone || p.Present) && !(p.Ship.Destroyed && p.Ship.Battle is null)
                                 && p.Ship.SystemStar is int star && systems.Map.Stars[star].Special == special));

    /// <summary>What a test does after each day <see cref="MoveClockDays"/> passes (a game over mid-skip), or null.</summary>
    internal Action? DayForTests { get; set; }

    /// <summary>The game lost, for tests (what the Kohr-Ah's genocide will do, stage 3c item 4).</summary>
    internal void LoseTheGameForTests() => LoseTheGame(surrendered: false);

    /// <summary>The location gates' answer in tests that have no systems (UQM's recorded flagship's place), or null.</summary>
    internal Func<string, bool>? InSystemForTests { get; set; }
}
