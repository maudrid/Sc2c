// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (hyper.c's encounters' elements, drawn
// from graphics.hyperstars' frames; ipdisp.c's spawn_ip_group, a group drawn
// as its ship's melee icon), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;

namespace Crateris.Core.World;

/// <summary>What a <see cref="GroupSnapshot"/> says of its group beyond its place.</summary>
[Flags]
public enum GroupSnapshotFlags : byte
{
    None = 0,

    /// <summary>Not drawn this frame: a system's group re-forming after a meeting flashes, drawn every other frame (ip_group_preprocess).</summary>
    Hidden = 1,

    /// <summary>Being met by a player (stage 3b-1 item 5): it stays where it met them, untouchable, until the meeting is over.</summary>
    Met = 2,
}

/// <summary>
/// A group of alien ships as a client draws it (stage 3b-1 item 10; item
/// 11 draws it as UQM does): in hyperspace (<see cref="Star"/> null) an
/// encounter's element, at its place in hyperspace's logical units
/// (<see cref="X"/>, <see cref="Y"/>: log_x, log_y, as a ship's LogX and
/// LogY), drawn as its frame of graphics.hyperstars (<see cref="Frame"/>:
/// the vortex's frames, then the globe) with its vortex's state
/// (<see cref="Transition"/>: transition_state); in a system a group's
/// element, in its view (<see cref="View"/>: 0 the outer system, else 1 +
/// the planet whose inner system, a ship's IpFlight.View), at its place
/// there (<see cref="X"/>, <see cref="Y"/>: the group's loc, in the view's
/// units, which the viewer's own zoom puts on its screen), drawn as its
/// race's ship's melee icon (frame 1; <see cref="Frame"/> and
/// <see cref="Transition"/> 0). <see cref="Id"/> tells the groups apart
/// from one frame to the next: in a system the group's group_id, in
/// hyperspace a number the world gives each encounter it sends.
/// <see cref="Race"/> is races.h's number (the story's race order).
/// </summary>
public sealed record GroupSnapshot(int Id, int? Star, int Race, int View, int X, int Y, int Frame = 0, int Transition = 0,
    GroupSnapshotFlags Flags = GroupSnapshotFlags.None)
{
    /// <summary>
    /// The groups a player sees (the snapshot sends them only these): in
    /// hyperspace, the encounters drawn on their screen (hyper.c's: within
    /// a screen of their ship, <see cref="HyperEncounters.ScreenX"/> and
    /// <see cref="HyperEncounters.ScreenY"/> universe units); flying in a
    /// system, the groups in their view (the outer system or the inner
    /// system they're in); anywhere else (orbit, a talk, a battle, the
    /// starbase) none, as UQM draws none there. Empty for a player not in
    /// the snapshot.
    /// </summary>
    public static IReadOnlyList<GroupSnapshot> SeenBy(WorldSnapshot? snapshot, string? viewer)
    {
        if (viewer is null || snapshot?.Groups is not { Count: > 0 } groups)
            return Array.Empty<GroupSnapshot>();
        ShipSnapshot? me = null;
        foreach (var s in snapshot.Ships)
            if (s.Id == viewer)
            {
                me = s;
                break;
            }
        if (me is null)
            return Array.Empty<GroupSnapshot>();
        List<GroupSnapshot>? seen = null;
        foreach (var g in groups)
        {
            if (!Sees(me, g))
                continue;
            (seen ??= new List<GroupSnapshot>()).Add(g);
        }
        return seen is null ? Array.Empty<GroupSnapshot>() : seen;
    }

    /// <summary>Whether a ship sees a group (<see cref="SeenBy"/>).</summary>
    public static bool Sees(ShipSnapshot me, GroupSnapshot g)
    {
        if (g.Star is null)
            return me.Activity == Activity.Hyperspace && me.SystemStar is null
                   && Math.Abs(Units.LogXToUniverse(g.X) - me.UniverseX) <= HyperEncounters.ScreenX
                   && Math.Abs(Units.LogYToUniverse(g.Y) - me.UniverseY) <= HyperEncounters.ScreenY;
        return me.Activity is Activity.OuterSystem or Activity.InnerSystem && me.SystemStar == g.Star
               && g.View == (me.InnerPlanet is int p ? p + 1 : 0);
    }
}
