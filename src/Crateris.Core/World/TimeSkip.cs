// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoTimePassage,
// src/uqm/planets/generate/genpet.c's ZapToUrquanEncounter: FadeScreen
// (FadeAllToBlack, ONE_SECOND * 2), then MoveGameClockDays), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>
/// A time skip (stage 3c item 2): the screen fades to black on every
/// screen over two seconds of world frames, then days pass at once, each
/// running its events (MoveGameClockDays), with no hyperspace encounter
/// rolled (the stage 3c plan's question 3: UQM's one player is never in
/// hyperspace during a skip; the fleets still move and every event still
/// runs). What the days' events changed is kept
/// (<see cref="StoryState.Changes"/>), so a sequence that can be taken
/// back takes the days back with it (the starbase's opening). Part of a
/// story pause: the starbase visit's two weeks (<see cref="StarbaseVisit"/>)
/// and any other sequence's (<see cref="StoryPause"/>).
/// </summary>
public sealed class TimeSkip
{
    /// <summary>The fade before the days pass (FadeAllToBlack, ONE_SECOND * 2), in world frames.</summary>
    public const int FadeFrames = 2 * GameClock.FramesPerSecond;

    public TimeSkip(int days)
    {
        if (days <= 0)
            throw new ArgumentOutOfRangeException(nameof(days), "a skip passes at least a day");
        Days = days;
        FadeLeft = FadeFrames;
    }

    /// <summary>The days it skips (LOST_DAYS: the starbase's and the bomb's 14, the Talking Pet's 15).</summary>
    public int Days { get; }

    /// <summary>World frames left of the fade (0 once the days have passed).</summary>
    public int FadeLeft { get; private set; }

    /// <summary>Whether the days have passed.</summary>
    public bool Skipped { get; private set; }

    /// <summary>What the days' events changed (null before they pass, or once taken back).</summary>
    public StoryState.Changes? Changes { get; private set; }

    /// <summary>
    /// A world frame of the skip: the fade goes on; at its end the days pass
    /// (MoveGameClockDays: each day's events run, no encounter rolled, and
    /// the day after starts afresh). True on the frame they pass.
    /// </summary>
    internal bool Frame(GameWorld world)
    {
        if (Skipped || --FadeLeft > 0)
            return false;
        FadeLeft = 0;
        var story = world.Story!;
        var before = story.Save();
        world.MoveClockDays(Days, rolls: false);
        Changes = StoryState.Changes.Between(before, story.Save());
        Skipped = true;
        return true;
    }

    /// <summary>
    /// The days' events taken back from a story, if they've passed: the
    /// fields they changed as before them, the crew's price, the clock's
    /// events (queued and set aside) as before them, and the races' fleets
    /// and standings they changed as before them.
    /// </summary>
    internal void TakeBackFrom(StoryState story)
    {
        if (Skipped && Changes is { } changes)
            changes.TakeBackFrom(story);
    }

    /// <summary>The date without the skip: its days, if they've passed, taken back.</summary>
    public (int Year, int Month, int Day) DateWithout(GameClock clock)
    {
        var d = new DateTime(clock.Year, clock.Month, clock.Day).AddDays(Skipped ? -Days : 0);
        return (d.Year, d.Month, d.Day);
    }

    /// <summary>The skip taken back for good (its days put back on the clock by its sequence): as if it never passed.</summary>
    internal void Forget()
    {
        Skipped = false;
        Changes = null;
    }
}
