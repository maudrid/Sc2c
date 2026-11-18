// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (encount.h's ENCOUNTER and
// BRIEF_SHIP_INFO; the ELEMENT hyper.c gives each, AddEncounterElement),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

/// <summary>
/// A group of alien ships in hyperspace (UQM's ENCOUNTER, in encounter_q;
/// docs/plans/stage-3b.md, item 3): its race (races.h's number, the
/// story's race order), its flags (<see cref="OneShot"/>,
/// <see cref="Reforming"/>), its ships' crews (num_ships and ShipList's
/// crew_level, the one field UQM reads; none until its element is first
/// made, AddEncounterElement), its race's sphere as it was rolled
/// (<see cref="OriginX"/>, <see cref="OriginY"/>, <see cref="Radius"/>),
/// where it is (loc_pt in universe units, log_x and log_y in hyperspace's
/// logical units) and its vortex's state (transition_state). The co-op
/// game adds whose daily roll made it (<see cref="Roller"/>: it comes in
/// near them, and the races met anywhere are one at a time for each
/// player, stage 3b decision 3) and the players out of hyperspace it's
/// kept for (<see cref="KeptFor"/>, with where they left), and has one
/// element (<see cref="Element"/>) while any player is in hyperspace.
/// </summary>
public sealed class HyperEncounter
{
    /// <summary>ONE_SHOT_ENCOUNTER: once met, it doesn't come back.</summary>
    public const byte OneShot = 1 << 7;

    /// <summary>ENCOUNTER_REFORMING: met, and coming back in (or going) when its element is next made.</summary>
    public const byte Reforming = 1 << 6;

    /// <summary>MAX_HYPER_SHIPS: the most ships a group has.</summary>
    public const int MaxShips = 7;

    public int Race { get; internal set; }
    public byte Flags { get; internal set; }

    /// <summary>Each ship's crew (ShipList's crew_level); its count is num_ships.</summary>
    public List<int> Ships { get; private set; } = new();

    public short OriginX { get; internal set; }
    public short OriginY { get; internal set; }
    public ushort Radius { get; internal set; }
    public short LocX { get; internal set; }
    public short LocY { get; internal set; }
    public int LogX { get; internal set; }
    public int LogY { get; internal set; }
    public short TransitionState { get; internal set; }

    /// <summary>The player whose daily roll made it.</summary>
    public string Roller { get; internal set; } = "";

    /// <summary>
    /// The players who left hyperspace with it in their span, by where they
    /// left (universe units): it's kept for them until they're back, and
    /// checked then.
    /// </summary>
    public Dictionary<string, (short X, short Y)> KeptFor { get; private set; } = new();

    /// <summary>Its element while a player is in hyperspace (UQM's hElement), or null.</summary>
    public EncounterElement? Element { get; internal set; }

    public HyperEncounter Clone()
    {
        var c = (HyperEncounter)MemberwiseClone();
        c.Ships = new List<int>(Ships);
        c.KeptFor = new Dictionary<string, (short X, short Y)>(KeptFor);
        c.Element = Element?.Clone();
        return c;
    }
}

/// <summary>
/// The element hyper.c gives an encounter in hyperspace: its frame of
/// graphics.hyperstars (the vortex's frames, then the globe), the waits
/// (turn_wait: the vortex's pace, thrust_wait: the steering's), whether
/// its vortex goes out rather than in (hit_points 1), whether it's in its
/// vortex (death_func encounter_transition), its velocity, whether it can
/// be touched (not NONSOLID), its life_span, the player it met (hTarget),
/// whether it's drawn (its primitive a stamp) and where it is in
/// hyperspace's logical units: <see cref="CurX"/>, <see cref="CurY"/>
/// (current.location: where a touch is tested from) and
/// <see cref="NextX"/>, <see cref="NextY"/> (next.location: to), as UQM's
/// would be with the view's scroll taken out.
/// </summary>
public sealed class EncounterElement
{
    public int Frame;
    public int TurnWait;
    public int ThrustWait;
    public bool VortexOut;
    public bool InVortex;
    public Velocity Velocity;
    public bool Nonsolid = true;
    public int LifeSpan = 1;
    public string? Met;
    public bool Drawn;
    public int CurX, CurY, NextX, NextY;

    public EncounterElement Clone() => (EncounterElement)MemberwiseClone();
}
