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
/// which may be flown into is <see cref="GameWorld.MarkersFor"/>), and the
/// race it's against (stage 4b item 9): the encounter race's ship, the
/// enemy fleet's first ship's id in data/ships.json
/// (<see cref="WorldBattle.EnemyShip"/>), which the marker shows.
/// </summary>
public sealed record BattleMarkerSnapshot(int Id, int? Star, int? InnerPlanet, int X, int Y, string EnemyShip);

/// <summary>
/// A ship's status panel as UQM's shows it: its kind (a ship description's
/// id), crew and energy, each with its most; its captain's name (an index
/// in its race's strings, <see cref="Starship.CaptainsNameIndex"/>); the
/// keys its captain's window shows (<see cref="Starship.CurStatusFlags"/>);
/// and its element's crew and life, or that it's gone (the panel stops as
/// the ship's explosion ends, status.c's). Since stage 4b item 9, what the
/// ship's own code does to its captain's window: the frame its special key
/// shows there where the code keeps its own (<see cref="CaptainSpecial"/>,
/// the Shofixti's Glory Device's cover; 0 for DrawCaptainsWindow's), and
/// of the marks ModifySilhouette made on it since it was drawn, those still
/// showing, in order (<see cref="Marks"/>: <see cref="SilhouetteMarks.Shown"/>,
/// the Orz's marines aboard, the VUX's limpets; on the wire the newest
/// 1,024 at most). Equal by value.
/// </summary>
public sealed record BattleShipStatus(string Ship, int Crew, int MaxCrew, int Energy, int MaxEnergy, int Captain = 0,
    StatusFlags Flags = StatusFlags.None, int ElementCrew = 0, int LifeSpan = 0, bool Gone = false, int CaptainSpecial = 0,
    IReadOnlyList<SilhouetteMarkSnapshot>? Marks = null)
{
    public bool Equals(BattleShipStatus? other) =>
        other is not null && (Ship, Crew, MaxCrew, Energy, MaxEnergy, Captain, Flags, ElementCrew, LifeSpan, Gone, CaptainSpecial)
            == (other.Ship, other.Crew, other.MaxCrew, other.Energy, other.MaxEnergy, other.Captain, other.Flags, other.ElementCrew,
                other.LifeSpan, other.Gone, other.CaptainSpecial)
        && (Marks ?? Array.Empty<SilhouetteMarkSnapshot>()).SequenceEqual(other.Marks ?? Array.Empty<SilhouetteMarkSnapshot>());

    public override int GetHashCode() => HashCode.Combine(Ship, Crew, Energy, Captain, Flags, Marks?.Count ?? 0);
}

/// <summary>
/// A mark ModifySilhouette made on a ship's captain's window
/// (<see cref="SilhouetteMark"/>), as the snapshot sends it: the frame
/// stamped (its sheet and index), its place in the ship's status panel,
/// and whether it's the window drawn afresh within the frame's rectangle
/// (MODIFY_SWAP) instead.
/// </summary>
public sealed record SilhouetteMarkSnapshot(string Sheet, ushort Frame, short X, short Y, bool Erase);

/// <summary>
/// One player's ship in a battle, still in it (fighting, or destroyed in a
/// battle still running: its player watches); a ship that left (retreated,
/// disconnected) isn't listed. Its status and its place (world units, as
/// the elements'; a destroyed ship's last, where it exploded); whether
/// the battle's camera followed it this frame (<see cref="BattleSnapshot.Camera"/>
/// is then its player's own, UQM's exactly). The host with fleet ships in
/// the battle (stage 4b items 8 and 9): their ships to pick from and the
/// pick screen (<see cref="Armada"/>); null for a player who came in with
/// their own ship alone.
/// </summary>
public sealed record BattleFighterSnapshot(string PlayerId, BattleShipStatus Status, short X, short Y, bool Followed = false,
    BattleArmadaSnapshot? Armada = null);

/// <summary>
/// The host's fleet ships in a battle and the pick screen (stage 4b item 9;
/// <see cref="BattleFighter.FleetShips"/>, <see cref="BattleFighter.Pick"/>):
/// each fleet ship as it stands in the battle, in the fleet's order, and
/// the pick screen's cursor while the host chooses (null otherwise). The
/// flagship, always pickable while the host chooses, isn't listed: it's
/// the host's own ship. Equal by value.
/// </summary>
public sealed record BattleArmadaSnapshot(IReadOnlyList<BattleFleetShipSnapshot> Ships, BattlePickSnapshot? Pick)
{
    public bool Equals(BattleArmadaSnapshot? other) =>
        other is not null && Pick == other.Pick && Ships.SequenceEqual(other.Ships);

    public override int GetHashCode() => HashCode.Combine(Ships.Count, Pick);

    /// <summary>The fleet ship in a hangar slot, or null.</summary>
    public BattleFleetShipSnapshot? InSlot(int slot) => Ships.FirstOrDefault(s => s.Slot == slot);
}

/// <summary>
/// One of the host's fleet ships in a battle (<see cref="BattleFleetShip"/>),
/// as the pick screen shows it (DrawArmadaPickShip, DoPickBattleShip): its
/// hangar slot, its ship's id in data/ships.json, its captain's name (an
/// index in its race's strings), its crew now (as it joined, while it
/// waits; as it came out, once out) and most; how it came out
/// (<see cref="BattleFleetShip.Exit"/>: null while it waits or flies),
/// whether it has flown (<see cref="BattleFleetShip.Flown"/>), and whether
/// the host can pick it now (<see cref="Combat.Armada.CanPick"/>, and still
/// in the host's fleet).
/// </summary>
public sealed record BattleFleetShipSnapshot(int Slot, string Ship, int Captain, int Crew, int MaxCrew, BattleExit? Exit,
    bool Flown, bool CanPick);

/// <summary>
/// The pick screen's cursor (<see cref="Systems.PickShipMenu"/>'s
/// <see cref="Systems.PickShipMenu.Row"/> and <see cref="Systems.PickShipMenu.Column"/>).
/// </summary>
public sealed record BattlePickSnapshot(int Row, int Column)
{
    /// <summary>Whether the cursor is on the flagship.</summary>
    public bool OnFlagship => Column == Systems.PickShipMenu.FlagshipColumn;

    /// <summary>The hangar slot under the cursor, or null on the flagship (<see cref="Systems.PickShipMenu.Slot"/>).</summary>
    public int? Slot => Systems.PickShipMenu.SlotAt(Row, Column);
}

/// <summary>
/// One of a battle's elements as it's drawn this frame (the element's
/// current state, after the frame): what it is (<see cref="PrimType"/>),
/// where (world units, which wrap at the arena's size; a line's start as
/// drawn, its end in <see cref="X2"/>, <see cref="Y2"/>), a stamp's
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
/// draws), and every ship's place and crew for the minimap. To draw it
/// (stage 4a item 12): the battle's camera this frame (<see cref="Camera"/>,
/// the one following <see cref="BattleFighterSnapshot.Followed"/>), and the
/// seed its stars come from (<see cref="Battle.Galaxy(uint)"/>).
/// </summary>
public sealed record BattleSnapshot(int Id, int Width, int Height, BattleShipStatus Enemy, short EnemyX, short EnemyY,
    IReadOnlyList<BattleFighterSnapshot> Fighters, IReadOnlyList<BattleElementSnapshot> Elements,
    BattleCamera Camera = default, uint StarSeed = 0)
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
        other is not null && (Id, Width, Height, Enemy, EnemyX, EnemyY, Camera, StarSeed)
        == (other.Id, other.Width, other.Height, other.Enemy, other.EnemyX, other.EnemyY, other.Camera, other.StarSeed)
        && Fighters.SequenceEqual(other.Fighters) && Elements.SequenceEqual(other.Elements);

    public override int GetHashCode() => HashCode.Combine(Id, Fighters.Count, Elements.Count);
}
