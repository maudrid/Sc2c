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
}
