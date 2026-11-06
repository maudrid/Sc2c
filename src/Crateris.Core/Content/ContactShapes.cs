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
        Lander = sheets["graphics.lander"];
        MiscData = sheets["graphics.miscdata"];
        Canister = sheets["graphics.lifecan"];
        Quake = sheets["graphics.quake"];
        Lightning = sheets["graphics.lightning"];
        Lava = sheets["graphics.lavaspot"];
        Moonbase = sheets["graphics.moonbase"];
        Life = Enumerable.Range(0, LifeSheets).Select(n => sheets[$"graphics.life.{n}"]).ToArray();
    }

    /// <summary>The creature sheets: graphics.life.0 to 25 (load_life_form), one a kind.</summary>
    public const int LifeSheets = 26;

    /// <summary>The resources the shapes come from.</summary>
    public static readonly IReadOnlyList<string> Resources = new[]
    {
        "graphics.flagship", "graphics.planets", "graphics.orbitbackground", "graphics.stars",
        "ship.flagship.graphics.hyperspace", "graphics.hyperstars", "graphics.ambient",
        "graphics.lander", "graphics.miscdata", "graphics.lifecan",
        "graphics.quake", "graphics.lightning", "graphics.lavaspot", "graphics.moonbase",
    }.Concat(Enumerable.Range(0, LifeSheets).Select(n => $"graphics.life.{n}")).ToArray();

    /// <summary>Every sheet's frames, by resource id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IntersectFrame>> Sheets { get; }

    public IReadOnlyList<IntersectFrame> IpFlagship { get; }
    public IReadOnlyList<IntersectFrame> Worlds { get; }
    public IReadOnlyList<IntersectFrame> Junk { get; }
    public IntersectFrame IpPoint { get; }
    public IReadOnlyList<IntersectFrame> HyperFlagship { get; }
    public IReadOnlyList<IntersectFrame> HyperStars { get; }
    public IntersectFrame HyperPoint { get; }

    /// <summary>The lander's sheet (LanderFrame[0]): its 16 facings, then its shots' 16.</summary>
    public IReadOnlyList<IntersectFrame> Lander { get; }

    /// <summary>The scan dots and the nodes on the surface (MiscDataFrame).</summary>
    public IReadOnlyList<IntersectFrame> MiscData { get; }

    /// <summary>A canned creature (graphics.lifecan, PlanetSideFrame[0]).</summary>
    public IReadOnlyList<IntersectFrame> Canister { get; }

    /// <summary>The hazards' sheets: an earthquake (LanderFrame[1]), lightning ([2]) and a lava spot ([3]).</summary>
    public IReadOnlyList<IntersectFrame> Quake { get; }

    /// <inheritdoc cref="Quake"/>
    public IReadOnlyList<IntersectFrame> Lightning { get; }

    /// <inheritdoc cref="Quake"/>
    public IReadOnlyList<IntersectFrame> Lava { get; }

    /// <summary>The Lunar base, Luna's energy node (graphics.moonbase, the Sol generator's PlanetSideFrame[1]).</summary>
    public IReadOnlyList<IntersectFrame> Moonbase { get; }

    /// <summary>graphics.lightning's last frames, the strike on the ground, which UQM never shows: lightning shows one of the others at random (lightning_process).</summary>
    public const int LightningStrikeFrames = 7;

    /// <summary>Each kind of creature's sheet, by kind.</summary>
    public IReadOnlyList<IReadOnlyList<IntersectFrame>> Life { get; }

    /// <summary>The frames of MiscDataFrame the mineral nodes on the surface are drawn and touched with: from NUM_SCANDOT_TRANSITIONS * 2, five a category.</summary>
    public const int FirstMineralFrame = 4 * 2, MineralFramesPerCategory = 5;

    /// <summary>Reads the shapes; anything wrong with them is a <see cref="ContentPackageException"/>.</summary>
    public static ContactShapes Load(ContentPackage package) => Load(new AnimationSheets(package));

    /// <summary>
    /// Reads the shapes from sheets decoded once for everything that uses
    /// them. Every frame UQM can test for contact must have a known mask:
    /// the flagship's 16 facings in a system and in hyperspace, every
    /// world image, the starbase and the Sa-Matra, the hyperspace stars of
    /// every size and colour, and the one-pixel frames. Anything wrong is
    /// a <see cref="ContentPackageException"/>, found here rather than in
    /// the middle of a game.
    /// </summary>
    public static ContactShapes Load(AnimationSheets decoded)
    {
        ContactShapes shapes;
        try
        {
            var sheets = new Dictionary<string, IReadOnlyList<IntersectFrame>>();
            foreach (string id in Resources)
            {
                var frames = decoded[id].Select(f => Frame(f.Frame, f.Image)).ToList();
                if (frames.Count == 0)
                    throw new ContentPackageException($"{id} has no images");
                sheets[id] = frames;
            }
            shapes = new ContactShapes(sheets);
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the sprites' shapes couldn't be read: {e.Message}", e);
        }
        void Need(string what, IReadOnlyList<IntersectFrame> frames, int count, params int[] only)
        {
            if (frames.Count < Math.Max(count, only.Length == 0 ? 0 : only.Max() + 1))
                throw new ContentPackageException($"{what} has {frames.Count} frames; contact needs more");
            foreach (int i in only.Length > 0 ? only : Enumerable.Range(0, count))
                if (frames[i].Mask is null)
                    throw new ContentPackageException($"{what} frame {i} is tested for contact, but its shape can't be read (a true colour image without alpha)");
        }
        Need("graphics.flagship", shapes.IpFlagship, Uqm.UqmMath.Facings);
        Need("graphics.planets", shapes.Worlds, Systems.WorldImages.WorldFrames);
        Need("graphics.orbitbackground", shapes.Junk, 0, Systems.WorldImages.StarbaseFrame, Systems.WorldImages.SaMatraFrame);
        Need("ship.flagship.graphics.hyperspace", shapes.HyperFlagship, Uqm.UqmMath.Facings);
        Need("graphics.hyperstars", shapes.HyperStars, Enum.GetValues<Data.StarSize>().Length * Data.Star.StarColors);
        Need("graphics.stars", new[] { shapes.IpPoint }, 1);
        Need("graphics.ambient", new[] { shapes.HyperPoint }, 1);
        Need("graphics.lander", shapes.Lander, 2 * Uqm.UqmMath.Facings);
        Need("graphics.miscdata", shapes.MiscData, 0, Enumerable.Range(FirstMineralFrame,
            Data.PlanetTypes.ElementCategoryNames.Count * MineralFramesPerCategory).ToArray());
        // A creature or canister shows any of its sheet's frames.
        Need("graphics.lifecan", shapes.Canister, Math.Max(1, shapes.Canister.Count));
        for (int n = 0; n < LifeSheets; ++n)
            Need($"graphics.life.{n}", shapes.Life[n], Math.Max(1, shapes.Life[n].Count));
        // Quakes and lava spots show every frame; lightning, the last seven
        // aside, one at random, and touches nothing.
        Need("graphics.quake", shapes.Quake, shapes.Quake.Count);
        Need("graphics.lavaspot", shapes.Lava, shapes.Lava.Count);
        // An energy node shows every frame of its sheet.
        Need("graphics.moonbase", shapes.Moonbase, shapes.Moonbase.Count);
        if (shapes.Lightning.Count <= LightningStrikeFrames)
            throw new ContentPackageException($"graphics.lightning has {shapes.Lightning.Count} frames; lightning needs more than {LightningStrikeFrames}");
        return shapes;
    }

    /// <summary>
    /// A frame's mask. The colour key is the one process_image sets
    /// (<see cref="ProcessImage.ColorKey"/>); TFB_DrawCanvas_Intersect then
    /// counts a pixel as touching if it isn't fully transparent (with an
    /// alpha channel) or isn't the colour key (0 when there's none). A
    /// true colour image without alpha is converted to the screen's format
    /// first, which isn't modelled: its mask is unknown.
    /// </summary>
    public static IntersectFrame Frame(AnimationFrame frame, Png png)
    {
        byte[]? mask = null;
        if (png.BytesPerPixel == 1)
        {
            long key = ProcessImage.ColorKey(frame, png);
            if (key < 0)
                key = 0;
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
