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
        // The Ilwrath ship at Earth, which the opening brings, is story;
        // stage 3b, decision 6: every other Ilwrath talk (met in space, at
        // their homeworld) is ordinary, under the Ilwrath's lock.
        ["ilwrath"] = story => story.Get("PROBE_ILWRATH_ENCOUNTER") != 0,
        // The allied commander: story when VisitStarBase starts him on
        // arrival (the starbase ready, the Chmmr's bomb installed); from
        // the starbase's menu, ordinary.
        ["starbas"] = story => story.Get("MOONBASE_ON_SHIP") != 0 || story.Get("CHMMR_BOMB_STATE") == 2,
        // Stage 3b, decision 6: no talk with a race's ships is story, the
        // drone's (which runs the Ur-Quan's script) included: each is its
        // answerer's, under the race's dialogue lock.
        ["urquan"] = _ => false,
        // The Kohr-Ah met, the same; but once they've destroyed every race
        // (KOHR_AH_KILLED_ALL, stage 3c item 4) their talk is the game's end,
        // the Black Ur-Quan's last, shown to everyone on a story pause.
        ["blackur"] = story => story.Get("KOHR_AH_KILLED_ALL") != 0,
        // Stage 3b-1 item 7: the Spathi met in space, ordinary (decision 6);
        // Fwiffo on Pluto (3c's) too, as a talk with a race's ship.
        ["spathi"] = _ => false,
        // Stage 3b-1 item 8: the Slylandro probe met in space, ordinary
        // (decision 6), as any talk with a race's ships.
        ["slyland"] = _ => false,
        // Stage 3b-2 item 2: the Spathi at home (Spathiwa's moon) and the
        // Slylandro at home (their gas giant), ordinary, as every 3b-2
        // script's is (the plan's scope): each under its race's lock (the
        // Slylandro at home with the probes, RACE_SHIP_FOR_COMM).
        ["spahome"] = _ => false,
        ["slyhome"] = _ => false,
        // Stage 3b-2 item 4: the Melnorme, met in hyperspace and at the super
        // giants, ordinary, under their lock (but the rescue-only talk, which
        // takes none: the owner's question 7).
        ["melnorm"] = _ => false,
        // Stage 3b-2 item 5: Tanaka (or Katana) at the Shofixti's system, and
        // the Zoq-Fot-Pik (their scout, their homeworld), ordinary, each under
        // its race's lock.
        ["shofixt"] = _ => false,
        ["zoqfot"] = _ => false,
        // Stage 3b-2 item 6: the VUX (in space, at home, Admiral ZEX at the
        // Maidens' world) and the Yehat (in space, at home), ordinary, each
        // under its race's lock; the Yehat rebels' under the Yehat's
        // (RACE_SHIP_FOR_COMM).
        ["vux"] = _ => false,
        ["yehat"] = _ => false,
        ["rebel"] = _ => false,
        // Stage 3b-2 item 7: the Orz (in space, at home, at the Taalo
        // protector's moon) and the Pkunk (in space, at home), ordinary, each
        // under its race's lock.
        ["orz"] = _ => false,
        ["pkunk"] = _ => false,
        // Stage 3b-2 item 8: the Druuge (in space, at their trade world, at
        // the bomb's moon) and the Mycon (in space, at home, at the Sun
        // Device's world), ordinary, each under its race's lock.
        ["druuge"] = _ => false,
        ["mycon"] = _ => false,
        // Stage 3b-2 item 9: the Utwig (in space, at home, at the bomb's
        // moon) and the Supox (in space, at home), ordinary, each under its
        // own race's lock.
        ["utwig"] = _ => false,
        ["supox"] = _ => false,
        // Stage 3b-2 item 10: the Thraddash (in space, at home, at the Aqua
        // Helix's world) and the Umgah (in space, at home), ordinary, each
        // under its race's lock; the Talking Pet's under the Umgah's
        // (RACE_SHIP_FOR_COMM), though nothing in play starts it before
        // stage 3c.
        ["thradd"] = _ => false,
        ["umgah"] = _ => false,
        ["talkpet"] = _ => false,
        // Stage 3b-2 item 11: the Arilou (in hyperspace; at home in
        // QuasiSpace, stage 3c's), the Syreen (at their starbase) and the
        // Chmmr (a group --start-encounter brings, until 3c's worlds and
        // devices), ordinary, each under its race's lock. The Chmmr's bomb
        // branch is ordinary too until stage 3c settles whose ship carries
        // the bomb (the plan's question 8).
        ["arilou"] = _ => false,
        ["syreen"] = _ => false,
        ["chmmr"] = _ => false,
    };

    /// <summary>Whether a talk with this script, in the story as it stands as it starts, is story.</summary>
    public static bool IsStory(string script, StoryState story) =>
        Rules.TryGetValue(script, out var rule)
            ? rule(story)
            : throw new KeyNotFoundException($"no rule for the {script} conversation");

    /// <summary>The scripts with a rule.</summary>
    public static IReadOnlyCollection<string> Scripts => Rules.Keys;
}
