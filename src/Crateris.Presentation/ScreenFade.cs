// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoTimePassage
// and genpet.c's ZapToUrquanEncounter: FadeScreen (FadeAllToBlack,
// ONE_SECOND * 2); src/uqm/comm.c's CommIntroTransition: CIM_FADE_IN_SCREEN's
// FadeScreen (FadeAllToColor, fadeTime); libs/graphics/cmap.c: FadeScreen,
// which fades from where the screen's fade stands), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors,
// under the GNU GPL 2 or later.

using Crateris.Core.Comm;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>
/// The whole screen's fade to black and back (stage 3c item 2), which a
/// client draws over every screen: a story pause's time skip fades it to
/// black over two seconds on every player's screen (FadeAllToBlack, the
/// stage 3c plan's co-op rule), and a talk whose screen comes in with
/// CIM_FADE_IN_SCREEN (<see cref="TalkScreen.Intro"/>: the allied commander
/// after the two weeks, 2 s; the Chmmr emerging, 2 s; the Ur-Quan before
/// the hypnotised captain, 5 s) fades it back from where it stands
/// (FadeAllToColor). With neither, the screen is shown as it is: a skip
/// that no talk follows comes back at once. Timed by the client's own
/// clock, in UQM's ticks (ONE_SECOND 840), from when it's first shown.
/// </summary>
public sealed class ScreenFade
{
    /// <summary>The time skip's fade (FadeAllToBlack, ONE_SECOND * 2), in UQM's ticks.</summary>
    public const long TimePassingTicks = 2 * 840;

    private double _from, _to;
    private long _start, _length;
    private bool _passing;
    private string? _intro;

    /// <summary>
    /// Follows what the session shows, each frame: the talk shown (the
    /// story's while the game waits for it), whether the game is paused
    /// for the story, and whether the communication screen shows the talk.
    /// </summary>
    public void Follow(TalkView? talk, bool storyPaused, bool talkShown, long now)
    {
        if (storyPaused && talk?.Step == VisitStep.TimePassing)
        {
            if (!_passing)
                FadeTo(1, TimePassingTicks, now);
            _passing = true;
            return;
        }
        _passing = false;
        if (talkShown && talk?.Screen?.Intro is { Mode: CommIntroMode.FadeInScreen } intro)
        {
            // A talk's first view, its screen fading in: once for those lines.
            string key = talk.Script + "\u0001" + talk.LinesKey;
            if (key != _intro)
            {
                _intro = key;
                FadeTo(0, intro.Time, now);
                return;
            }
        }
        if (_to > 0)
            (_from, _to, _length) = (0, 0, 0);   // nothing fades it back: shown as it is
    }

    private void FadeTo(double to, long ticks, long now)
    {
        _from = Black(now);
        _to = to;
        _start = now;
        _length = Math.Max(0, ticks);
    }

    /// <summary>How black the screen is now, from 0 (as it is) to 1 (black).</summary>
    public double Black(long now)
    {
        if (_length <= 0 || now - _start >= _length)
            return _to;
        double t = Math.Max(0, now - _start) / (double)_length;
        return _from + (_to - _from) * t;
    }
}
