// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Planetside;

namespace Crateris.Core.World;

/// <summary>
/// A lander on a surface as clients see it: whose it is, its place
/// (planetside map units), facing, height above the ground (as
/// <see cref="Lander.Height"/>), phase, crew, how it was drawn (its flash,
/// whether at all), its damage and explosion counters, its cargo and the
/// mineral text after a pickup.
/// </summary>
public sealed record LanderSnapshot(string PlayerId, int X, int Y, int Facing, int Height, LanderPhase Phase, int Crew,
    LanderFlash Flash, bool Drawn, int DamageIndex, int ExplosionIndex, int MineralMass, int BioData, MineralText? Text)
{
    public static LanderSnapshot Of(string playerId, Lander l) =>
        new(playerId, l.X, l.Y, l.Facing, l.Height, l.FramePhase, l.Crew, l.Flash, l.Drawn, l.DamageIndex,
            l.ExplosionIndex, l.MineralMass, l.BioData, l.Text);
}

/// <summary>
/// Something moving or changing on a surface, as clients draw it: its
/// number on the surface (<see cref="SurfaceElement.Id"/>), what it is, its
/// place, its sheet and frame, a creature's kind (its sheet's), its flash,
/// and a bolt of lightning's life left and at the start, for its fade (the
/// rest's lives aren't drawn, so aren't sent). Mineral deposits aren't
/// sent: clients have the world's nodes, and the taken ones in the
/// snapshot's orbited worlds.
/// </summary>
public sealed record SurfaceElementSnapshot(int Id, SurfaceElementKind Kind, int X, int Y, SurfaceSheet Sheet, int Frame,
    int LifeKind, int? Flash, int LifeSpan, int Cycle)
{
    public static SurfaceElementSnapshot Of(SurfaceElement e) =>
        new(e.Id, e.Kind, e.X, e.Y, e.Sheet, e.Frame, e.LifeKind, e.Flash,
            e.Kind == SurfaceElementKind.Lightning ? e.LifeSpan : 0, e.Cycle);
}

/// <summary>A world's surface: its landers in the order they came, and what's on it in UQM's queue order but the deposits.</summary>
public sealed record SurfaceSnapshot(int Star, int Planet, int? Moon, IReadOnlyList<LanderSnapshot> Landers,
    IReadOnlyList<SurfaceElementSnapshot> Elements)
{
    public WorldId World => new(Star, Planet, Moon);
}
