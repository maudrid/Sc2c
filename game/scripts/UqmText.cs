// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using Crateris.Core.Content;
using Crateris.Core.Systems;
using Godot;

namespace Crateris.Game;

/// <summary>
/// Draws text laid out in UQM's fonts (<see cref="DrawnText"/>) and UQM's
/// rectangles, in UQM pixels scaled up. Each character becomes a white
/// texture with the font's alpha, drawn in the text's colour, as UQM blends
/// a font character over the screen. The canvas item should filter its
/// textures to the nearest pixel, so the characters stay sharp.
/// </summary>
public static class UqmText
{
    private static readonly Dictionary<FontGlyph, ImageTexture> Textures = new();

    public static Color ToColor(Rgb c) => Color.Color8(c.R, c.G, c.B);

    private static ImageTexture Texture(FontGlyph g)
    {
        if (!Textures.TryGetValue(g, out var t))
        {
            var data = new byte[g.Width * g.Height * 4];
            for (int i = 0; i < g.Alpha.Length; ++i)
            {
                data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = 255;
                data[i * 4 + 3] = g.Alpha[i];
            }
            t = ImageTexture.CreateFromImage(Image.CreateFromData(g.Width, g.Height, false, Image.Format.Rgba8, data));
            Textures[g] = t;
        }
        return t;
    }

    /// <summary>Draws the text with UQM pixel (0, 0) at <paramref name="origin"/>.</summary>
    public static void Draw(CanvasItem c, DrawnText text, Vector2 origin, float scale)
    {
        Color color = ToColor(text.Color);
        foreach (var p in text.Layout.Glyphs)
            c.DrawTextureRect(Texture(p.Glyph),
                new Rect2(origin + new Vector2(p.Left, p.Top) * scale, new Vector2(p.Glyph.Width, p.Glyph.Height) * scale),
                false, color);
    }

    /// <summary>Draws a rectangle, or its one-pixel outline, as UQM's DrawFilledRectangle and DrawRectangle do.</summary>
    public static void Draw(CanvasItem c, DrawnRect r, Vector2 origin, float scale)
    {
        Color color = ToColor(r.Color);
        Rect2 Px(int x, int y, int w, int h) => new(origin + new Vector2(x, y) * scale, new Vector2(w, h) * scale);
        if (!r.Outline)
        {
            c.DrawRect(Px(r.X, r.Y, r.Width, r.Height), color);
            return;
        }
        c.DrawRect(Px(r.X, r.Y, r.Width, 1), color);
        c.DrawRect(Px(r.X, r.Y + r.Height - 1, r.Width, 1), color);
        c.DrawRect(Px(r.X, r.Y, 1, r.Height), color);
        c.DrawRect(Px(r.X + r.Width - 1, r.Y, 1, r.Height), color);
    }

    public static void Draw(CanvasItem c, DrawnMenu menu, Vector2 origin, float scale)
    {
        foreach (var r in menu.Rects)
            Draw(c, r, origin, scale);
        foreach (var t in menu.Texts)
            Draw(c, t, origin, scale);
    }
}
