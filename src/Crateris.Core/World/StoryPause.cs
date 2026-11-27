// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoTimePassage,
// src/uqm/planets/generate/genpet.c's ZapToUrquanEncounter, and
// src/uqm/comm.c's InitCommunication, which tracks the talk's race's
// sphere), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Comm;

namespace Crateris.Core.World;

/// <summary>
/// A story pause any sequence can take (stage 3c item 2; stage 3,
/// decisions 6 and 7; the stage 3c plan's co-op rule: a time skip is a
/// story pause for everyone, its fade on every screen): the game waits for
/// everyone while it lasts, and everyone is shown it. It has a time skip
/// (<see cref="TimeSkip"/>: the fade to black, then its days, each running
/// its events, no hyperspace encounter rolled), a story talk shown on
/// everyone's screen that its starter answers (or who the replies pass to
/// as they leave, decision 7), or both, the talk after the days (as the
/// allied commander after the starbase's two weeks; the Talking Pet's 15
/// days, the bomb's 14 and the Black Ur-Quan's last talk are stage 3c's
/// later items'). When it ends, the sequence that took it goes on
/// (<see cref="Then"/>). The starbase's opening has its own pause, its
/// visit (<see cref="StarbaseVisit"/>), whose two weeks are the same
/// <see cref="TimeSkip"/> and which can be taken back; a story pause's
/// days, like UQM's, can't be: its skip keeps what they changed
/// (<see cref="Skip"/>) for a sequence that needs to.
/// <para>
/// Its talk is story by its rule (<see cref="TalkRules"/>) as the pause is
/// taken, as every story talk is: it takes no dialogue lock, and a race
/// met in space while it's story can't start an ordinary one. It isn't saved: a save made during
/// it is of the game as it stands (the days in it once they've passed).
/// </para>
/// </summary>
public sealed class StoryPause : TalkSequence
{
    private readonly (string Script, Dictionary<string, int> Own, string? Race, CommIntro? Intro)? _talk;

    /// <param name="world">The world it pauses.</param>
    /// <param name="starter">Who its talk's replies are first (and who it's started by).</param>
    /// <param name="firstRound">The game's sets of replies so far (<see cref="TalkSequence.FirstRound"/>).</param>
    /// <param name="what">What it is, in words, for the host's log.</param>
    /// <param name="days">The days its skip passes, or 0 for none.</param>
    /// <param name="script">Its story talk's script, after the days; null for none.</param>
    /// <param name="own">The talk's own fields (GLOBAL_FLAGS_AND_DATA and the like).</param>
    /// <param name="race">The talk's race, whose sphere it starts tracking (RACE_SHIP_FOR_COMM); null for none.</param>
    /// <param name="intro">How the talk's screen comes in (SetCommIntroMode before InitCommunication); null for the default.</param>
    internal StoryPause(GameWorld world, Player starter, int firstRound, string what, int days,
        string? script = null, Dictionary<string, int>? own = null, string? race = null, CommIntro? intro = null)
        : base(world, starter, firstRound)
    {
        if (days <= 0 && script is null)
            throw new ArgumentException("a story pause skips days, has a talk, or both");
        if (script is not null && world.TalkLibrary is null)
            throw new InvalidOperationException("a story pause's talk needs the talks' content");
        if (script is not null && !TalkRules.IsStory(script, world.Story!))
            throw new InvalidOperationException($"the {script} talk isn't story: a story pause's talk is (TalkRules)");
        What = what;
        Skip = days > 0 ? new TimeSkip(days) : null;
        if (script is not null)
            _talk = (script, own ?? new Dictionary<string, int>(), race, intro);
        // Begun by the world as it makes it its pause (Begin).
    }

    /// <summary>What it is, in words (the host's log).</summary>
    public string What { get; }

    /// <summary>Its time skip, if it has one.</summary>
    public override TimeSkip? Skip { get; }

    /// <summary>What the sequence that took it does as it ends (the world calls it once, after the pause has lifted).</summary>
    public Action<StoryPause>? Then { get; init; }

    /// <summary>Whether its talk, if any, has been held so far (the replier's lines and replies).</summary>
    public bool Talked => Talks.Count > 0;

    /// <summary>A story pause is story until it's over: everyone waits and is shown it.</summary>
    public override bool Story => !Done;

    /// <summary>
    /// It begins (the world has made it its pause): its skip's fade, or
    /// with none, its talk.
    /// </summary>
    internal void Begin()
    {
        WasStory = true;
        if (Skip is not null)
        {
            Step = VisitStep.TimePassing;
            Talk = null;
        }
        else
            AfterDays();
    }

    /// <summary>A world frame: its skip's fade, the days at its end, then its talk.</summary>
    internal void Frame()
    {
        if (Step == VisitStep.TimePassing && Skip!.Frame(_world))
            AfterDays();
    }

    // The days have passed (or there were none): the talk, or the end.
    private void AfterDays()
    {
        if (_talk is not { } t)
        {
            Finish();
            return;
        }
        // Story as its rule said as the pause was taken (the days may change what it reads): shown to everyone.
        StartTalk(VisitStep.Story, t.Script, new Dictionary<string, int>(t.Own), story: true, t.Intro);
    }

    private protected override void BeforeTalk(string script)
    {
        // InitCommunication: StartSphereTracking for the talk's race.
        if (_talk?.Race is { } race)
            _world.Story!.StartSphereTracking(race);
    }

    private protected override void TalkEnded() => Finish();

    private void Finish()
    {
        Step = VisitStep.Done;
        Talk = null;
    }
}
