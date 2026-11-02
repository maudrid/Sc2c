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
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The system and hyperspace sprites as textures, drawn as UQM stamps them:
/// the frame's hot spot at the stamp's place, three times UQM's size, pixel
/// for pixel (the canvas item's filter must be nearest).
/// </summary>
public sealed class SpriteTextures
{
    private readonly Dictionary<SpriteImage, ImageTexture> _textures = new(ReferenceEqualityComparer.Instance);

    /// <summary>Makes every frame's texture now, so drawing never has to.</summary>
    public SpriteTextures(SpaceSprites sprites)
    {
        Sprites = sprites;
        foreach (var image in sprites.All)
            Texture(image);
    }

    public SpaceSprites Sprites { get; }

    public ImageTexture Texture(SpriteImage image)
    {
        if (!_textures.TryGetValue(image, out var t))
            _textures[image] = t = ImageTexture.CreateFromImage(
                Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Rgba));
        return t;
    }

    /// <summary>Draws a frame with its hot spot at (x, y) in UQM's pixels.</summary>
    public void Draw(CanvasItem canvas, SpriteImage image, float x, float y)
    {
        var at = new Vector2(x - image.HotSpotX, y - image.HotSpotY) * SisScreen.Scale;
        canvas.DrawTextureRect(Texture(image), new Rect2(at, new Vector2(image.Width, image.Height) * SisScreen.Scale), false);
    }

    public void Draw(CanvasItem canvas, SystemStamp stamp) => Draw(canvas, Sprites.Image(stamp), stamp.X, stamp.Y);
}
