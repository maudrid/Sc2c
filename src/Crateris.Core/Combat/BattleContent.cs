// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/init.c: InitSpace;
// src/uqm/cons_res.c: load_gravity_well; src/uqm/setup.c: StatusFrame;
// src/uqm/collide.h: InitIntersectFrame), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// The graphics a battle draws, by resource id: each ship's (as its
/// description names it, <see cref="ShipGraphics.BattleResources"/>) and
/// the arena's: InitSpace's stars, explosions, blasts and asteroids, the
/// status panel's frames (StatusFrame, loaded at start by setup.c) and the
/// planet, any planet type's or a shielded world's (load_gravity_well).
/// </summary>
public static class BattleResources
{
    /// <summary>STAR_MASK_PMAP_ANIM (stars_in_space): the stars, and in its last frame floating crew, lasers and ion trails.</summary>
    public const string Stars = "graphics.stars";

    /// <summary>STATUS_MASK_PMAP_ANIM (StatusFrame): the status panel's frames.</summary>
    public const string Status = "graphics.status";

    /// <summary>BOOM_*_MASK_PMAP_ANIM (explosion): a ship or shot blowing up, big, medium and small.</summary>
    public static readonly IReadOnlyList<string> Explosion = Views("graphics.boom");

    /// <summary>BLAST_*_MASK_PMAP_ANIM (blast): a shot's hit, big, medium and small.</summary>
    public static readonly IReadOnlyList<string> Blast = Views("graphics.blast");

    /// <summary>ASTEROID_*_MASK_PMAP_ANIM (asteroid): an asteroid's spin, big, medium and small.</summary>
    public static readonly IReadOnlyList<string> Asteroid = Views("graphics.asteroid");

    private static string[] Views(string name) => new[] { $"{name}.large", $"{name}.medium", $"{name}.small" };

    /// <summary>Every planet sprite a battle can have, each type's and a shielded world's, big, medium and small.</summary>
    public static IEnumerable<IReadOnlyList<string>> Planets(PlanetTypes planets) =>
        Enumerable.Range(0, planets.Count).Select(i => planets[i].BattleSprites).Append(planets.ShieldedBattleSprites);

    /// <summary>The arena's resources: the stars, explosions, blasts, asteroids, status panel and every planet.</summary>
    public static IEnumerable<string> Arena(PlanetTypes planets) =>
        new[] { Stars, Status }.Concat(Explosion).Concat(Blast).Concat(Asteroid)
            .Concat(Planets(planets).SelectMany(p => p));

    /// <summary>Every graphics resource a battle between <paramref name="ships"/> draws, each once (the ships' race strings, a string table, aside).</summary>
    public static IReadOnlyList<string> All(IEnumerable<ShipDescription> ships, PlanetTypes planets) =>
        ships.SelectMany(s => s.Graphics.BattleResources()).Concat(Arena(planets)).Distinct().ToList();

    /// <summary>
    /// The sheets a battle tests for contact: the big size only, as
    /// InitIntersectFrame takes an element's frame from farray[0]. Each
    /// ship's ship, weapon and special, the asteroids, blasts, explosions
    /// and stars, and every planet.
    /// </summary>
    public static IReadOnlyList<string> ContactSheets(IEnumerable<ShipDescription> ships, PlanetTypes planets)
    {
        var sheets = new List<string>();
        foreach (var ship in ships)
            foreach (var views in new[] { ship.Graphics.Ship, ship.Graphics.Weapon, ship.Graphics.Special })
                if (ShipGraphics.Sheets(views) is { } s)
                    sheets.Add(s[0]);
        sheets.AddRange(new[] { Asteroid[0], Blast[0], Explosion[0], Stars });
        sheets.AddRange(Planets(planets).Select(p => p[0]));
        return sheets.Distinct().ToList();
    }
}

/// <summary>
/// The shapes a battle tests for contact (<see cref="BattleResources.ContactSheets"/>),
/// each frame's mask as UQM's loader and TFB_DrawCanvas_Intersect make it
/// (<see cref="ContactShapes.Frame"/>; the planets, true colour without
/// alpha, as converted to the screen's format).
/// </summary>
public sealed class BattleShapes
{
    private BattleShapes(IReadOnlyDictionary<string, IReadOnlyList<IntersectFrame>> sheets) => Sheets = sheets;

    /// <summary>Every sheet's frames, by resource id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IntersectFrame>> Sheets { get; }

    /// <summary>A sheet's frames, by resource id.</summary>
    public IReadOnlyList<IntersectFrame> this[string resourceId] => Sheets.TryGetValue(resourceId, out var frames)
        ? frames : throw new KeyNotFoundException($"no battle shapes for {resourceId}");

    /// <summary>The shapes for a battle between <paramref name="ships"/>, from sheets decoded once for everything that uses them.</summary>
    public static BattleShapes Load(AnimationSheets decoded, IEnumerable<ShipDescription> ships, PlanetTypes planets)
    {
        var list = ships.ToList();
        var shapes = Load(decoded, BattleResources.ContactSheets(list, planets));
        // A ship turns through 16 facings, a frame each (ship_preprocess).
        foreach (var ship in list)
        {
            string hull = ShipGraphics.Sheets(ship.Graphics.Ship)?[0]
                ?? throw new ContentPackageException($"{ship.Id} has no ship graphics to fight with");
            if (shapes[hull].Count < UqmMath.Facings)
                throw new ContentPackageException($"{hull} has {shapes[hull].Count} frames; a ship needs {UqmMath.Facings}");
        }
        return shapes;
    }

    /// <summary>
    /// The shapes of these sheets. Every frame must have a known mask, and
    /// every sheet a frame; anything wrong is a <see cref="ContentPackageException"/>,
    /// found when it's made rather than in a battle (the game will make it as the
    /// content is opened, headless too, stage 4a item 6; the tests do now).
    /// </summary>
    public static BattleShapes Load(AnimationSheets decoded, IEnumerable<string> sheets)
    {
        var loaded = new Dictionary<string, IReadOnlyList<IntersectFrame>>();
        try
        {
            foreach (string id in sheets)
            {
                var frames = decoded[id].Select(f => ContactShapes.Frame(f.Frame, f.Image)).ToList();
                if (frames.Count == 0)
                    throw new ContentPackageException($"{id} has no images");
                // Every kind of image the loader makes has a shape
                // (ContactShapes.Frame), so this only guards against one that hasn't.
                int unknown = frames.FindIndex(f => f.Mask is null);
                if (unknown >= 0)
                    throw new ContentPackageException($"{id} frame {unknown} is tested for contact, but its shape can't be read");
                loaded[id] = frames;
            }
        }
        catch (Exception e) when (e is not ContentPackageException)
        {
            throw new ContentPackageException($"the battle's shapes couldn't be read: {e.Message}", e);
        }
        return new BattleShapes(loaded);
    }
}

/// <summary>
/// The sheets a battle runs with, from the content: each resource's frames
/// counted (the zoom's sizes take an index round their own count), and for
/// the sheets collisions test (the big size) their shapes from
/// <see cref="BattleShapes"/>.
/// </summary>
public sealed class BattleSheets
{
    private readonly AnimationSheets _decoded;
    private readonly BattleShapes _shapes;
    private readonly Dictionary<string, Sheet> _sheets = new();
    private readonly Dictionary<string, Views> _views = new();

    public BattleSheets(AnimationSheets decoded, BattleShapes shapes)
    {
        _decoded = decoded;
        _shapes = shapes;
    }

    /// <summary>A sheet by resource id.</summary>
    public Sheet this[string resourceId]
    {
        get
        {
            if (!_sheets.TryGetValue(resourceId, out var sheet))
                _sheets[resourceId] = sheet = new Sheet(resourceId, _decoded[resourceId].Count,
                    _shapes.Sheets.TryGetValue(resourceId, out var shapes) ? shapes : null);
            return sheet;
        }
    }

    /// <summary>
    /// A farray: the sheets load_animation loads for a RACE_DESC's three
    /// sizes (<see cref="ShipGraphics.Sheets"/>), or null for none. The same
    /// sheets give the same <see cref="Combat.Views"/>, as UQM's farray is
    /// the one array in the RACE_DESC (the AI tells a ship's special shots
    /// by it: <see cref="RaceDesc.Special"/>).
    /// </summary>
    public Views? Views(IReadOnlyList<string?> resources)
    {
        if (ShipGraphics.Sheets(resources) is not { } sheets)
            return null;
        string key = string.Join("\n", sheets);
        // Asked from several threads at once (the tests replaying battles in parallel).
        lock (_views)
        {
            if (!_views.TryGetValue(key, out var views))
                _views[key] = views = new Views(sheets.Select(s => this[s]).ToArray());
            return views;
        }
    }

    /// <summary>The arena's sheets (InitSpace's), with a planet's battle sprites (load_gravity_well).</summary>
    public BattleArena Arena(IReadOnlyList<string> planet) => new(
        new Views(this[BattleResources.Stars]), Views(BattleResources.Asteroid)!, Views(BattleResources.Blast)!,
        Views(BattleResources.Explosion)!, Views(planet)!);
}
