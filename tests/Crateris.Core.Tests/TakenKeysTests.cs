// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Presentation;

namespace Crateris.Core.Tests;

[Trait("Topic", "lander")]
public class TakenKeysTests
{
    private const long Esc = 4194305, Space = 32;

    [Fact]
    public void AKeyThatClosedTheAboutScreenIsntTheGamesTillItComesUp()
    {
        var keys = new TakenKeys();
        keys.Take(Esc);   // it closed the about screen
        // Held: not the lander's take-off key, nor (repeating) the orbit menu's.
        Assert.False(keys.Free(Esc));
        Assert.False(keys.Pass(Esc, echo: true));
        Assert.True(keys.Free(Space));
        Assert.True(keys.Pass(Space, echo: true));
        // Up, and down again: the game's.
        keys.Up(Esc);
        Assert.True(keys.Free(Esc));
        Assert.True(keys.Pass(Esc, echo: false));
    }

    [Fact]
    public void AKeyGoingDownAfreshIsTheGamesThoughItsComingUpWasLost()
    {
        var keys = new TakenKeys();
        keys.Take(Esc);
        Assert.True(keys.Pass(Esc, echo: false));   // it came up unseen, and went down again
        Assert.True(keys.Free(Esc));
        Assert.True(keys.Pass(Esc, echo: true));
        // The window's focus gone: none is held back.
        keys.Take(Space);
        keys.Clear();
        Assert.True(keys.Free(Space));
    }

    [Fact]
    public void AScreenStartingWithKeysHeldSeesThoseAKeyPressedThenWouldReachAndTakesThemAll()
    {
        const long F1 = 4194332, Down = 4194322, PhysEsc = 1, PhysSpace = 2, PhysF1 = 3, PhysDown = 4;
        var held = new[] { (PhysEsc, Esc), (PhysSpace, Space), (PhysF1, F1), (PhysDown, Down) };
        var keys = new TakenKeys();
        keys.Take(Esc);   // the Esc that closed the about screen, held since
        // Seen held: not F1, nor the key another screen took; by their physical keys.
        Assert.Equal(new[] { PhysSpace, PhysDown }, keys.TakeHeld(held, shut: false, except: F1));
        // All but F1 taken: the game's again only once they've come up.
        Assert.False(keys.Free(Space));
        Assert.False(keys.Free(Down));
        Assert.False(keys.Free(Esc));
        Assert.True(keys.Free(F1));
        // A screen that gets no keys (under the about screen, a scripted player's) sees none held, and takes them all the same.
        var shut = new TakenKeys();
        Assert.Empty(shut.TakeHeld(held, shut: true, except: F1));
        Assert.False(shut.Free(Space));
        Assert.True(shut.Free(F1));
    }
}
