// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The game over (stage 4a item 12, decision 12): UQM, its flagship lost
/// or surrendered to the Ur-Quan (stage 3b-1 item 6), or every race
/// destroyed by the Kohr-Ah (stage 3c item 4, after the Black Ur-Quan's
/// last talk), leaves the game and
/// fades back to its start (starcon.c leaves its loop with
/// <c>CrewEnlisted</c> ~0); here every player's screen fades to black over
/// a second (the Utwig bomb's, stage 3c item 8: white, then black, as
/// RestartMenu's flash), then says the game is over, which of the four ended it
/// (<c>Why</c>) and the key that ends it. The host's game then ends, its
/// save as it was before (nothing is saved once the game is lost), and the
/// guests are let go.
/// </summary>
public partial class GameOverView : Node2D
{
    /// <summary>How long the fade to black takes, in seconds.</summary>
    public const double FadeSeconds = 1.0;

    public const string Title = "GAME OVER", Lost = "The flagship was lost.", Surrendered = "The flagship surrendered.",
        KilledAll = "The Kohr-Ah have destroyed every race.", Bomb = "The flagship set off the Utwig bomb.", Again = "The host's save is kept as it was: the loss isn't saved.",
        KeyLine = "Press Enter or Esc to leave.";

    private double _shown = -1;

    /// <summary>Why the game is over: the flagship lost, its surrender, or the Kohr-Ah's victory (PlayerSession.Loss).</summary>
    public string Why { get; set; } = Lost;

    /// <summary>
    /// Whether the game over is the Utwig bomb's (stage 3c item 8): the
    /// screen goes white and then black as RestartMenu has it
    /// (<see cref="DeviceFlashes.Bomb"/>) before the words, not the second's fade.
    /// </summary>
    public bool BombFlash { get; set; }

    /// <summary>The window's size, which the fade covers.</summary>
    public Vector2 Size { get; set; } = new(1024, 768);

    /// <summary>Whether the fade is over and the words are up: a key now ends the game.</summary>
    public bool Shown => _shown >= (BombFlash ? DeviceFlashes.BombSeconds : FadeSeconds);

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
        if (!Shown || !wasReady)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (BombFlash)
        {
            var (white, black, _) = DeviceFlashes.Bomb(_shown);
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, (float)white));
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, (float)black));
        }
        else
        {
            float fade = (float)System.Math.Clamp(_shown / FadeSeconds, 0, 1);
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, fade));
        }
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
