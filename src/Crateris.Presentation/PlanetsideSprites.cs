// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;

namespace Crateris.Presentation;

/// <summary>
/// The sprites of the planet's surface, its scan map and the lander's
/// gauges (LoadLanderData, MiscDataFrame, the canister and the creatures),
/// every frame drawn once with UQM's main colour map, so anything wrong is
/// found as the content is opened. Any failure is a <see cref="ContentPackageException"/>.
/// </summary>
public sealed class PlanetsideSprites
{
    private readonly Dictionary<(PlanetsideSheet, int), SpriteImage> _images = new();
    private readonly Dictionary<PlanetsideSheet, int> _counts = new();

    /// <summary>Each sheet's resource, by the recordings' numbers.</summary>
    public static IEnumerable<(PlanetsideSheet Sheet, string Resource)> Resources()
    {
        yield return (PlanetsideSheet.Lander, "graphics.lander");
        yield return (PlanetsideSheet.Quake, "graphics.quake");
        yield return (PlanetsideSheet.Lightning, "graphics.lightning");
        yield return (PlanetsideSheet.Lava, "graphics.lavaspot");
        yield return (PlanetsideSheet.Shield, "graphics.landershield");
        yield return (PlanetsideSheet.Launch, "graphics.landerlaunch");
        yield return (PlanetsideSheet.Return, "graphics.landerreturn");
        yield return (PlanetsideSheet.Moonbase, "graphics.moonbase");
        yield return (PlanetsideSheet.MiscData, "graphics.miscdata");
        yield return (PlanetsideSheet.Canister, "graphics.lifecan");
        for (int n = 0; n < ContactShapes.LifeSheets; ++n)
            yield return (PlanetsideSheet.Life + n, $"graphics.life.{n}");
    }

    public PlanetsideSprites(ContentPackage package, AnimationSheets sheets)
    {
        try
        {
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            foreach (var (sheet, resource) in Resources())
            {
                var frames = SpriteSheet.Load(sheets, resource);
                _counts[sheet] = frames.Count;
                for (int f = 0; f < frames.Count; ++f)
                    _images[(sheet, f)] = frames.Render(f, maps);
            }
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the surface's sprites couldn't be read: {e.Message}", e);
        }
    }

    /// <summary>A frame, its index wrapping as SetAbsFrameIndex's does.</summary>
    public SpriteImage Image(PlanetsideSheet sheet, int frame) => _images[(sheet, frame % _counts[sheet])];

    public int Count(PlanetsideSheet sheet) => _counts[sheet];

    /// <summary>Every frame (to make their textures up front).</summary>
    public IEnumerable<SpriteImage> All => _images.Values;
}
