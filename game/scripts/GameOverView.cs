// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Godot;

namespace Crateris.Game;

/// <summary>
/// The game over (stage 4a item 12, decision 12): UQM, its flagship lost,
/// leaves the game and fades back to its start (starcon.c leaves its loop
/// with <c>CrewEnlisted</c> ~0); here every player's screen fades to black
/// over a second, then says the game is over and the key that ends it. The
/// host's game then ends, its save as it was before (nothing is saved
/// after the flagship's loss), and the guests are let go.
/// </summary>
public partial class GameOverView : Node2D
{
    /// <summary>How long the fade to black takes, in seconds.</summary>
    public const double FadeSeconds = 1.0;

    public const string Title = "GAME OVER", Why = "The flagship was lost.", Again = "The host's save is kept as it was: the loss isn't saved.",
        KeyLine = "Press Enter or Esc to leave.";

    private double _shown = -1;

    /// <summary>The window's size, which the fade covers.</summary>
    public Vector2 Size { get; set; } = new(1024, 768);

    /// <summary>Whether the fade is over and the words are up: a key now ends the game.</summary>
    public bool Shown => _shown >= FadeSeconds;

    /// <summary>Shown (the game is over) or not, as time passes.</summary>
    public void Follow(bool over, double seconds)
    {
        Visible = over;
        if (!over)
        {
            _shown = -1;
            return;
        }
        bool wasReady = Shown;
        _shown = _shown < 0 ? 0 : _shown + seconds;
        if (_shown < FadeSeconds || !wasReady)
            QueueRedraw();
    }

    public override void _Draw()
    {
        float fade = (float)System.Math.Clamp(_shown / FadeSeconds, 0, 1);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, fade));
        if (!Shown)
            return;
        var font = ThemeDB.FallbackFont;
        float y = Size.Y / 2 - 60;
        DrawString(font, new Vector2(0, y), Title, HorizontalAlignment.Center, Size.X, 48, Colors.Red);
        DrawString(font, new Vector2(0, y + 50), Why, HorizontalAlignment.Center, Size.X, 20, Colors.White);
        DrawString(font, new Vector2(0, y + 80), Again, HorizontalAlignment.Center, Size.X, 16, Colors.Gray);
        DrawString(font, new Vector2(0, y + 120), KeyLine, HorizontalAlignment.Center, Size.X, 16, Colors.Gray);
    }
}
