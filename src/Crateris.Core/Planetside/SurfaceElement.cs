// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (uqm/element.h's ELEMENT as
// planets/lander.c and planets/scan.c use it on the surface),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Planetside;

/// <summary>What a surface element is.</summary>
public enum SurfaceElementKind
{
    /// <summary>A lander's stun bolt (PS_HUMAN_PLAYER, FINITE_LIFE).</summary>
    Bolt,
    /// <summary>A mineral deposit (PS_NON_PLAYER, a node of the mineral scan).</summary>
    Mineral,
}

/// <summary>
/// Something on the surface (an ELEMENT in UQM's display queue): the
/// landers' bolts and the nodes. Its place is in planetside map units.
/// </summary>
public sealed class SurfaceElement
{
    public SurfaceElementKind Kind { get; init; }

    public int X { get; internal set; }
    public int Y { get; internal set; }

    /// <summary>Frames left (life_span) for a bolt; at 0 it's removed on the next frame.</summary>
    public int LifeSpan { get; internal set; }

    /// <summary>Whether its life counts down (FINITE_LIFE).</summary>
    public bool FiniteLife => Kind == SurfaceElementKind.Bolt;

    /// <summary>A lander's shot (PS_HUMAN_PLAYER): it isn't stopped at the poles, and can't be picked up.</summary>
    public bool LanderShot => Kind == SurfaceElementKind.Bolt;

    /// <summary>Taken or spent this frame (DISAPPEARING): it goes at the start of the next.</summary>
    public bool Disappearing { get; internal set; }

    /// <summary>For a node: its scan and its number in that scan (scan_node).</summary>
    public ScanType Scan { get; init; }

    /// <inheritdoc cref="Scan"/>
    public int Node { get; init; }

    /// <summary>For a mineral: the element (turn_wait), and how much the deposit holds (mass_points).</summary>
    public int Element { get; init; }

    /// <inheritdoc cref="Element"/>
    public int Amount { get; init; }

    /// <summary>For a mineral, its element's category (ElementCategory).</summary>
    public int Category { get; init; }

    /// <summary>
    /// The frame it's drawn and touched with: a bolt its lander's shot
    /// image for its facing, in the lander's sheet; a mineral its image
    /// in the scan dots' sheet (MiscDataFrame).
    /// </summary>
    public int Frame { get; init; }

    internal Lander? Owner { get; init; }

    /// <summary>The lander that took it (or whose bolt did), when <see cref="Disappearing"/>.</summary>
    internal Lander? TakenBy { get; set; }

    internal Velocity Velocity;
}

/// <summary>A mineral node as GeneratePlanetSide puts it on the surface: its number, its place on the scan map, its element and density (MAKE_WORD (gross size, quantity)).</summary>
public readonly record struct SurfaceMineral(int Node, int X, int Y, int Element, int Density);
