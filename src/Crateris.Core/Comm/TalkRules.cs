// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;

namespace Crateris.Core.Comm;

/// <summary>
/// Which conversations are story (stage 3, decision 6): a story talk
/// pauses the game for everyone and is shown to all, the player who
/// started it replying; an ordinary one is its player's alone, under the
/// dialogue lock (item 7). Each script's rule is here, so 3b and 3c add
/// theirs the same way. (A table in the code, as data/ holds only what's
/// extracted from UQM.)
/// </summary>
public static class TalkRules
{
    private static readonly Dictionary<string, Func<StoryState, bool>> Rules = new()
    {
        // The commander before the alliance: the opening.
        ["comandr"] = _ => true,
        // The Ilwrath ship at Earth, which the opening brings.
        ["ilwrath"] = story => story.Get("PROBE_ILWRATH_ENCOUNTER") != 0,
        // The allied commander: story when VisitStarBase starts him on
        // arrival (the starbase ready, the Chmmr's bomb installed); from
        // the starbase's menu, ordinary.
        ["starbas"] = story => story.Get("MOONBASE_ON_SHIP") != 0 || story.Get("CHMMR_BOMB_STATE") == 2,
    };

    /// <summary>Whether a talk with this script, in the story as it stands as it starts, is story.</summary>
    public static bool IsStory(string script, StoryState story) =>
        Rules.TryGetValue(script, out var rule)
            ? rule(story)
            : throw new KeyNotFoundException($"no rule for the {script} conversation");

    /// <summary>The scripts with a rule.</summary>
    public static IReadOnlyCollection<string> Scripts => Rules.Keys;
}
