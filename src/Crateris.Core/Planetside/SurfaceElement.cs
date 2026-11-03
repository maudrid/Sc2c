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
    /// <summary>A creature, alive or canned (PS_NON_PLAYER, a node of the biological scan).</summary>
    Creature,
    /// <summary>A lander blowing up (PS_HUMAN_PLAYER, FINITE_LIFE, mass_points DEATH_EXPLOSION).</summary>
    Explosion,
}

/// <summary>The sheet a surface element is drawn and touched with.</summary>
public enum SurfaceSheet
{
    /// <summary>graphics.lander (LanderFrame[0]): the lander, its shots, its explosion.</summary>
    Lander,
    /// <summary>graphics.miscdata (MiscDataFrame): the mineral nodes.</summary>
    MiscData,
    /// <summary>graphics.lifecan (PlanetSideFrame[0]): a canned creature.</summary>
    Canister,
    /// <summary>graphics.life.N (load_life_form): a creature of kind N.</summary>
    Life,
}

/// <summary>
/// Something on the surface (an ELEMENT in UQM's display queue): the
/// landers' bolts and explosions, and the nodes. Its place is in
/// planetside map units. A creature keeps UQM's fields as UQM uses them.
/// </summary>
public sealed class SurfaceElement
{
    /// <summary>CREATURE_AWARE: in a creature's mass_points, it has noticed a lander.</summary>
    public const int Aware = 0x80;

    public SurfaceElementKind Kind { get; init; }

    public int X { get; internal set; }
    public int Y { get; internal set; }

    /// <summary>Frames left (life_span) for a bolt or an explosion; at 0 it's removed on the next frame.</summary>
    public int LifeSpan { get; internal set; }

    /// <summary>Whether its life counts down (FINITE_LIFE).</summary>
    public bool FiniteLife => Kind is SurfaceElementKind.Bolt or SurfaceElementKind.Explosion;

    /// <summary>A lander's (PS_HUMAN_PLAYER): it isn't stopped at the poles, and can't be picked up.</summary>
    public bool LanderShot => Kind is SurfaceElementKind.Bolt or SurfaceElementKind.Explosion;

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

    /// <summary>The sheet it's drawn and touched with, and the frame in it.</summary>
    public SurfaceSheet Sheet { get; internal set; }

    /// <inheritdoc cref="Sheet"/>
    public int Frame { get; internal set; }

    /// <summary>
    /// A creature's mass_points: its kind, with <see cref="Aware"/> once it
    /// has noticed a lander; once canned, its bio value instead.
    /// </summary>
    public int MassPoints { get; internal set; }

    /// <summary>A creature's stun hits left (hit_points); 0 once canned.</summary>
    public int HitPoints { get; internal set; }

    /// <summary>For a creature, the kind its sheet is (the sheet stays when its mass_points become its value).</summary>
    public int LifeKind { get; init; }

    /// <summary>turn_wait: the animation's countdown (low nibble) and its reload (high nibble).</summary>
    internal int TurnWait { get; set; }

    /// <summary>thrust_wait: frames before a creature next picks a heading.</summary>
    public int ThrustWait { get; internal set; }

    /// <summary>
    /// Flashing after a hit (STAMPFILL_PRIM): the damage colour, 0 (white)
    /// when just hit, then 6 down to 1; null when drawn plainly.
    /// </summary>
    public int? Flash { get; internal set; }

    internal Lander? Owner { get; init; }

    /// <summary>The lander that took it (or whose bolt did), when <see cref="Disappearing"/>.</summary>
    internal Lander? TakenBy { get; set; }

    internal Velocity Velocity;
}

/// <summary>A mineral node as GeneratePlanetSide puts it on the surface: its number, its place on the scan map, its element and density (MAKE_WORD (gross size, quantity)).</summary>
public readonly record struct SurfaceMineral(int Node, int X, int Y, int Element, int Density);

/// <summary>A life node: its number, its place on the scan map (where a still creature stays; a moving one is put somewhere at random) and its kind.</summary>
public readonly record struct SurfaceCreature(int Node, int X, int Y, int Kind);
