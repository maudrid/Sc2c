// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Content;

/// <summary>
/// The content notice shown on the first-run and about screens. The text
/// is the one quoted in <c>docs/design.md</c> ("First-run content
/// download"), a draft to check with a lawyer before public release; a
/// test keeps the two the same, so a change goes in the design first.
/// </summary>
public static class ContentNotice
{
    public const string Title = "Game content from The Ur-Quan Masters";

    public static readonly IReadOnlyList<string> Paragraphs = new[]
    {
        "To play, Attack on Crateris needs the content of The Ur-Quan Masters 0.8.0: graphics, sounds, music, " +
        "dialogue and voiceovers. It will be downloaded from the official Ur-Quan Masters project.",
        "The content is copyright © 1992, 1993, 2002 Toys for Bob, Inc. or its respective creators. The game was " +
        "created by Paul Reiche III and Fred Ford. It is licensed under the Creative Commons " +
        "Attribution-NonCommercial-ShareAlike 2.5 license (https://creativecommons.org/licenses/by-nc-sa/2.5/). " +
        "You may not use it commercially.",
        "Attack on Crateris is a separate, free program under the GNU GPL (version 2 or later). It is not made or " +
        "endorsed by Toys for Bob, Paul Reiche III, Fred Ford or The Ur-Quan Masters project.",
    };

    public static string Body => string.Join("\n\n", Paragraphs);
}
