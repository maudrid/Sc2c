// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/devices.c's
// InvokeDevice, the Sun Device: FadeScreen (FadeAllToWhite, ONE_SECOND),
// two seconds' sleep, FadeScreen (FadeAllToColor, ONE_SECOND * 2);
// src/uqm/restart.c's RestartMenu, the Utwig bomb: FadeScreen
// (FadeAllToWhite, ONE_SECOND / 8), a 60th of a second, the screen cleared
// white, FadeScreen (FadeAllToBlack, ONE_SECOND / 8), three seconds),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Presentation;

/// <summary>
/// The devices' flashes (stage 3c item 8), as the whole screen's fades
/// UQM makes for them, by the seconds since they began: the Sun Device's
/// white flash on its user's screen, and the Utwig bomb's, which goes white
/// and then black before the game over's words (every player's, the game
/// over being everyone's). Each fade is FadeScreen's, a straight line from
/// where the screen stands to the colour.
/// </summary>
public static class DeviceFlashes
{
    /// <summary>The Sun Device's fade to white (FadeAllToWhite, ONE_SECOND), its hold (two seconds) and its fade back (ONE_SECOND * 2).</summary>
    public const double SunFadeIn = 1, SunHold = 2, SunFadeOut = 2;

    /// <summary>How white the Sun Device's flash leaves the screen, from 0 (as it is) to 1, <paramref name="seconds"/> after it began (null: none).</summary>
    public static double SunWhite(double? seconds) => seconds switch
    {
        null or < 0 => 0,
        < SunFadeIn => seconds.Value / SunFadeIn,
        < SunFadeIn + SunHold => 1,
        < SunFadeIn + SunHold + SunFadeOut => 1 - (seconds.Value - SunFadeIn - SunHold) / SunFadeOut,
        _ => 0,
    };

    /// <summary>The bomb's fade to white (ONE_SECOND / 8), the wait after it (ONE_SECOND / 60), the fade to black (ONE_SECOND / 8) and the black after (three seconds).</summary>
    public const double BombWhite = 1.0 / 8, BombWait = 1.0 / 60, BombBlack = 1.0 / 8, BombDark = 3;

    /// <summary>When the bomb's flash is over and the game over's words come.</summary>
    public const double BombSeconds = BombWhite + BombWait + BombBlack + BombDark;

    /// <summary>
    /// The bomb's flash <paramref name="seconds"/> after it began: how
    /// white the screen is (0 to 1), and how black over that white; and
    /// whether it's over (the game over's words then).
    /// </summary>
    public static (double White, double Black, bool Over) Bomb(double seconds)
    {
        if (seconds < BombWhite)
            return (System.Math.Max(0, seconds) / BombWhite, 0, false);
        double t = seconds - BombWhite - BombWait;
        if (t < 0)
            return (1, 0, false);
        if (t < BombBlack)
            return (1, t / BombBlack, false);
        return (1, 1, seconds >= BombSeconds);
    }
}
