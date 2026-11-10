// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The layers: the simulation (Crateris.Core) knows nothing of what is drawn
/// (Crateris.Presentation) or of Godot, and the content's frames are
/// checked for contact as they're read.
/// </summary>
[Trait("Topic", "content")]
public class LayerTests
{
    [Fact]
    public void TheCoreDependsOnNeitherThePresentationNorGodot()
    {
        var core = typeof(GameWorld).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(typeof(SpaceSprites).Assembly.GetName().Name, core);
        Assert.DoesNotContain(core, n => n!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
        var presentation = typeof(SpaceSprites).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(presentation, n => n!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [ContentFact]
    public void AFrameTestedForContactHasAKnownShapeOfAnyKind()
    {
        var sheets = TestContent.With(p =>
        {
            var decoded = new AnimationSheets(p);
            return ContactShapes.Resources.Concat(new[] { "graphics.orbitbackground" }).Distinct()
                .ToDictionary(id => id, id => decoded[id]);
        });
        // The orbit background's last frame is a true colour image without
        // alpha, which UQM converts to the screen's format (TFB_DrawImage_New):
        // put where a flagship facing is, it loads with a shape, touching
        // where it isn't black.
        var (frame, png) = sheets["graphics.orbitbackground"][21];
        Assert.Equal((3, false), (png.BytesPerPixel, png.HasAlpha));
        var flagship = sheets["graphics.flagship"].ToList();
        flagship[3] = (frame, png);
        var changed = new Dictionary<string, IReadOnlyList<(AnimationFrame, Png)>>(sheets) { ["graphics.flagship"] = flagship };
        var mask = ContactShapes.Load(new AnimationSheets(changed)).IpFlagship[3].Mask!;
        for (int i = 0; i < mask.Length; ++i)
            Assert.Equal((png.Pixels[i * 3] | png.Pixels[i * 3 + 1] | png.Pixels[i * 3 + 2]) != 0 ? 1 : 0, mask[i]);
        // A sheet without images is refused.
        changed["graphics.flagship"] = Array.Empty<(AnimationFrame, Png)>();
        var e = Assert.Throws<ContentPackageException>(() => ContactShapes.Load(new AnimationSheets(changed)));
        Assert.Contains("graphics.flagship has no images", e.Message);
    }
}
