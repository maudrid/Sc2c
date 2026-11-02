// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0: the colour keys process_image
// (src/libs/graphics/gfxload.c) sets, and the pixels TFB_DrawCanvas_Intersect
// (src/libs/graphics/sdl/canvas.c) counts as touching, Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Content;

/// <summary>
/// The shapes UQM tests for contact, from the content package: the
/// flagship's frames in a system and in hyperspace, the worlds' frames, the
/// orbit background (the starbase and the Sa-Matra), the hyperspace stars,
/// and the one-pixel frame UQM tests a new contact with (the last frame of
/// graphics.stars in a system, of graphics.ambient in hyperspace:
/// DecFrameIndex (stars_in_space)).
/// </summary>
public sealed class ContactShapes
{
    private ContactShapes(IReadOnlyDictionary<string, IReadOnlyList<IntersectFrame>> sheets)
    {
        Sheets = sheets;
        IpFlagship = sheets["graphics.flagship"];
        Worlds = sheets["graphics.planets"];
        Junk = sheets["graphics.orbitbackground"];
        IpPoint = sheets["graphics.stars"][^1];
        HyperFlagship = sheets["ship.flagship.graphics.hyperspace"];
        HyperStars = sheets["graphics.hyperstars"];
        HyperPoint = sheets["graphics.ambient"][^1];
    }

    /// <summary>The resources the shapes come from.</summary>
    public static readonly IReadOnlyList<string> Resources = new[]
    {
        "graphics.flagship", "graphics.planets", "graphics.orbitbackground", "graphics.stars",
        "ship.flagship.graphics.hyperspace", "graphics.hyperstars", "graphics.ambient",
    };

    /// <summary>Every sheet's frames, by resource id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IntersectFrame>> Sheets { get; }

    public IReadOnlyList<IntersectFrame> IpFlagship { get; }
    public IReadOnlyList<IntersectFrame> Worlds { get; }
    public IReadOnlyList<IntersectFrame> Junk { get; }
    public IntersectFrame IpPoint { get; }
    public IReadOnlyList<IntersectFrame> HyperFlagship { get; }
    public IReadOnlyList<IntersectFrame> HyperStars { get; }
    public IntersectFrame HyperPoint { get; }

    /// <summary>Reads the shapes; anything wrong with them is a <see cref="ContentPackageException"/>.</summary>
    public static ContactShapes Load(ContentPackage package)
    {
        try
        {
            var sheets = new Dictionary<string, IReadOnlyList<IntersectFrame>>();
            foreach (string id in Resources)
            {
                var frames = Animation.LoadImages(package, id).Select(f => Frame(f.Frame, f.Image)).ToList();
                if (frames.Count == 0)
                    throw new ContentPackageException($"{id} has no images");
                sheets[id] = frames;
            }
            return new ContactShapes(sheets);
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the sprites' shapes couldn't be read: {e.Message}", e);
        }
    }

    /// <summary>
    /// A frame's mask. process_image sets the colour key: a palette image's
    /// transparent index from the .ani file (none for -1), a true colour
    /// image's black for 0, otherwise the PNG's own key.
    /// TFB_DrawCanvas_Intersect then counts a pixel as touching if it isn't
    /// fully transparent (with an alpha channel) or isn't the colour key (0
    /// without one). A true colour image without alpha is converted to the
    /// screen's format first, which isn't modelled: its mask is unknown.
    /// </summary>
    public static IntersectFrame Frame(AnimationFrame frame, Png png)
    {
        byte[]? mask = null;
        if (png.BytesPerPixel == 1)
        {
            long key = frame.TransparentIndex >= 0 ? frame.TransparentIndex
                : frame.TransparentIndex == -1 ? 0
                : png.ColorKey >= 0 ? png.ColorKey : 0;
            mask = png.Pixels.Select(p => (byte)(p != key ? 1 : 0)).ToArray();
        }
        else if (png.HasAlpha)
        {
            mask = new byte[png.Width * png.Height];
            for (int i = 0; i < mask.Length; ++i)
                mask[i] = (byte)(png.Pixels[i * 4 + 3] != 0 ? 1 : 0);
        }
        return new IntersectFrame(png.Width, png.Height, frame.HotSpotX, frame.HotSpotY, mask);
    }
}
