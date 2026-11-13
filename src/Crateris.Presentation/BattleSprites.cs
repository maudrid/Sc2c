// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;

namespace Crateris.Presentation;

/// <summary>
/// The battle's sprites (<see cref="BattleResources.All"/>): each ship's
/// sheets at the three zooms, captain's window and icons, and the arena's
/// stars, explosions, blasts, asteroids, status panel and planets, every
/// frame drawn once with UQM's main colour map (the only one a battle has:
/// setup.c sets it at start and nothing in a battle changes it), so
/// anything wrong is found when it's made (the game makes it as the content
/// is opened, with a window, for every ship a battle can have:
/// <see cref="Crateris.Core.World.BattleKit.Fighters"/>). Any failure is a
/// <see cref="ContentPackageException"/>.
/// </summary>
public sealed class BattleSprites
{
    private readonly Dictionary<string, SpriteImage[]> _sheets = new();

    public BattleSprites(ContentPackage package, AnimationSheets sheets, IEnumerable<ShipDescription> ships, PlanetTypes planets)
    {
        try
        {
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            foreach (string resource in BattleResources.All(ships, planets))
            {
                var frames = SpriteSheet.Load(sheets, resource);
                _sheets[resource] = Enumerable.Range(0, frames.Count).Select(f => frames.Render(f, maps)).ToArray();
            }
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the battle's sprites couldn't be read: {e.Message}", e);
        }
    }

    /// <summary>The resources drawn.</summary>
    public IEnumerable<string> Resources => _sheets.Keys;

    /// <summary>A frame of a sheet, its index wrapping as SetAbsFrameIndex's does.</summary>
    public SpriteImage Image(string resource, int frame)
    {
        var images = _sheets.TryGetValue(resource, out var s) ? s : throw new KeyNotFoundException($"no battle sprite {resource}");
        return images[frame % images.Length];
    }

    public int Count(string resource) => _sheets[resource].Length;

    /// <summary>Every frame (to make their textures up front).</summary>
    public IEnumerable<SpriteImage> All => _sheets.Values.SelectMany(s => s);
}
