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
    public void AFrameTestedForContactWithoutAKnownShapeIsAContentError()
    {
        var sheets = TestContent.With(p =>
        {
            var decoded = new AnimationSheets(p);
            return ContactShapes.Resources.Concat(new[] { "graphics.orbitbackground" }).Distinct()
                .ToDictionary(id => id, id => decoded[id]);
        });
        // The orbit background's last frame is a true colour image without
        // alpha: put it where a flagship facing is, and loading refuses it.
        var flagship = sheets["graphics.flagship"].ToList();
        flagship[3] = sheets["graphics.orbitbackground"][21];
        var bad = new Dictionary<string, IReadOnlyList<(AnimationFrame, Png)>>(sheets) { ["graphics.flagship"] = flagship };
        var e = Assert.Throws<ContentPackageException>(() => ContactShapes.Load(new AnimationSheets(bad)));
        Assert.Contains("graphics.flagship frame 3", e.Message);
        // Unchanged, the same sheets load (frame 21 itself is never tested).
        Assert.NotNull(ContactShapes.Load(new AnimationSheets(sheets)));
    }
}
