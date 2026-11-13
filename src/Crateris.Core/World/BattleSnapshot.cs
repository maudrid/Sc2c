// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;

namespace Crateris.Core.World;

/// <summary>
/// A battle's marker where it's fought (stage 4a item 6): its place, for
/// the players flying there to see and fly into. Every battle's is in the
/// snapshot; a client shows those at its own place (the host's rule for
/// which may be flown into is <see cref="GameWorld.MarkersFor"/>).
/// </summary>
public sealed record BattleMarkerSnapshot(int Id, int? Star, int? InnerPlanet, int X, int Y);

/// <summary>A ship's status panel as UQM's shows it: its kind (a ship description's id), crew and energy, each with its most.</summary>
public sealed record BattleShipStatus(string Ship, int Crew, int MaxCrew, int Energy, int MaxEnergy);

/// <summary>
/// One player's ship in a battle: its status, its place (world units, as
/// the elements'), and whether it's still in it (fighting, or destroyed
/// in a battle still running); a ship that left (retreated, disconnected)
/// isn't listed.
/// </summary>
public sealed record BattleFighterSnapshot(string PlayerId, BattleShipStatus Status, short X, short Y);

/// <summary>
/// One of a battle's elements as it's drawn this frame (the element's
/// current state, after the frame): what it is (<see cref="PrimType"/>),
/// where (world units, which wrap at the arena's size), a line's other
/// end (its next place, <see cref="X2"/>, <see cref="Y2"/>), a stamp's
/// sheet (the big size's, the client picking its zoom's size) and frame,
/// and a point's, line's or filled stamp's colour. A cloaked or undrawn
/// element (no prim) isn't sent.
/// </summary>
public sealed record BattleElementSnapshot(PrimType Prim, short X, short Y, short X2, short Y2, string Sheet, ushort Frame, int Color);

/// <summary>
/// A battle as the snapshot holds it (stage 4a item 11): its arena's size,
/// the enemy's ship and status and place, each player's ship in it, and its
/// elements. The host's world gives every element; a guest is sent its
/// own battle only, with only the elements near its ship (within UQM's
/// widest view of it, so any camera framing it and the enemy has what it
/// draws), and every ship's place and crew for the minimap.
/// </summary>
public sealed record BattleSnapshot(int Id, int Width, int Height, BattleShipStatus Enemy, short EnemyX, short EnemyY,
    IReadOnlyList<BattleFighterSnapshot> Fighters, IReadOnlyList<BattleElementSnapshot> Elements)
{
    /// <summary>The player's own ship in it, or null.</summary>
    public BattleFighterSnapshot? FighterOf(string id) => Fighters.FirstOrDefault(f => f.PlayerId == id);

    /// <summary>UQM's widest view each way, in world units (its four-times zoom of the space window): how far round a ship its player is sent the elements.</summary>
    public const int ViewWidth = (Uqm.Units.SpaceWidth << Uqm.Units.OneShift) << 2, ViewHeight = (Uqm.Units.SpaceHeight << Uqm.Units.OneShift) << 2;

    /// <summary>A delta across the arena's seam, the shorter way round.</summary>
    public static int Wrap(int d, int size)
    {
        d %= size;
        if (d > size / 2)
            d -= size;
        else if (d < -size / 2)
            d += size;
        return d;
    }

    /// <summary>The battle as one player is sent it: every ship, and the elements within <see cref="ViewWidth"/> and <see cref="ViewHeight"/> of their own (of the enemy's for a player not in it).</summary>
    public BattleSnapshot SeenBy(string id)
    {
        var me = FighterOf(id);
        short cx = me?.X ?? EnemyX, cy = me?.Y ?? EnemyY;
        return this with
        {
            Elements = Elements.Where(e => Math.Abs(Wrap(e.X - cx, Width)) <= ViewWidth && Math.Abs(Wrap(e.Y - cy, Height)) <= ViewHeight)
                .ToList(),
        };
    }

    public bool Equals(BattleSnapshot? other) =>
        other is not null && (Id, Width, Height, Enemy, EnemyX, EnemyY) == (other.Id, other.Width, other.Height, other.Enemy, other.EnemyX, other.EnemyY)
        && Fighters.SequenceEqual(other.Fighters) && Elements.SequenceEqual(other.Elements);

    public override int GetHashCode() => HashCode.Combine(Id, Fighters.Count, Elements.Count);
}
