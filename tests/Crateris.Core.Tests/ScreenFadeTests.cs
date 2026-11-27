// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The whole screen's fade (stage 3c item 2): a time skip fades it to
/// black over two seconds (FadeAllToBlack), a talk whose screen comes in
/// with CIM_FADE_IN_SCREEN fades it back over its time from where it
/// stands (FadeAllToColor), and with neither it's shown as it is.
/// </summary>
[Trait("Topic", "drawing")]
public class ScreenFadeTests
{
    private static TalkView Skip => new(VisitStep.TimePassing, "", Array.Empty<string>(), Array.Empty<string>(), Replier: "h");

    private static TalkView Commander(CommIntro? intro, string line = "Ready.") =>
        new(VisitStep.StarbaseReady, "starbas", new[] { line }, new[] { "Bye" }, 4, "h",
            Screen: new TalkScreen(0, Array.Empty<Crateris.Core.Data.CommAnimationFlags>(), true, Array.Empty<ScreenChange>(), intro));

    [Fact]
    public void ASkipFadesToBlackAndTheTalkAfterItFadesBackIn()
    {
        var fade = new ScreenFade();
        fade.Follow(null, false, false, 0);
        Assert.Equal(0, fade.Black(0));
        // DoTimePassage: two seconds (1680 ticks) to black, from when it's first shown.
        fade.Follow(Skip, true, false, 100);
        fade.Follow(Skip, true, false, 940);
        Assert.Equal(0.5, fade.Black(940), 3);
        Assert.Equal(1, fade.Black(1780));
        Assert.Equal(1, fade.Black(5000));
        // The allied commander fades in over 2 s from black (CIM_FADE_IN_SCREEN), once for his lines.
        var ready = Commander(StarbaseVisit.AfterTheDays);
        fade.Follow(ready, true, true, 6000);
        Assert.Equal(1, fade.Black(6000));
        fade.Follow(ready, true, true, 6840);
        Assert.Equal(0.5, fade.Black(6840), 3);
        Assert.Equal(0, fade.Black(7680));
        fade.Follow(ready, true, true, 8000);
        Assert.Equal(0, fade.Black(8000));
        // A view after a reply: nothing comes in.
        fade.Follow(Commander(null, "Goodbye."), true, true, 8100);
        Assert.Equal(0, fade.Black(8100));
    }

    [Fact]
    public void ASkipWithNoTalkAfterItComesBackAtOnceAndAFadeInStartsFromWhereTheScreenStands()
    {
        var fade = new ScreenFade();
        fade.Follow(Skip, true, false, 0);
        fade.Follow(Skip, true, false, 840);
        Assert.Equal(0.5, fade.Black(840), 3);
        // The pause over with no talk: shown as it is.
        fade.Follow(null, false, false, 900);
        Assert.Equal(0, fade.Black(900));
        // A fade-in on a screen not faded (the Chmmr's without the Sun Device's flash yet) shows nothing.
        fade.Follow(Commander(new CommIntro(CommIntroMode.FadeInScreen, 1680)), false, true, 1000);
        Assert.Equal(0, fade.Black(1000));
        Assert.Equal(0, fade.Black(1840));
        // Half faded when the talk comes: back from there, over its time.
        fade = new ScreenFade();
        fade.Follow(Skip, true, false, 0);
        fade.Follow(Commander(new CommIntro(CommIntroMode.FadeInScreen, 4200), "Wake."), true, true, 840);
        Assert.Equal(0.5, fade.Black(840), 3);
        Assert.Equal(0.25, fade.Black(840 + 2100), 3);
        Assert.Equal(0, fade.Black(840 + 4200));
        // Crossfades aren't drawn as fades (they cut), nor is an intro on a talk not shown.
        fade = new ScreenFade();
        fade.Follow(Skip, true, false, 0);
        fade.Follow(Commander(new CommIntro(CommIntroMode.CrossfadeScreen, 0)), true, true, 2000);
        Assert.Equal(0, fade.Black(2000));
        fade = new ScreenFade();
        fade.Follow(Skip, true, false, 0);
        fade.Follow(Commander(StarbaseVisit.AfterTheDays), true, false, 2000);
        Assert.Equal(0, fade.Black(2000));
    }
}
