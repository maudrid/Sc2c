// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>The groups of alien ships in the snapshot (stage 3b-1 item 10).</summary>
public sealed partial class GameWorld
{
    // The last number given an encounter sent (HyperEncounter.SnapshotId).
    private int _encounterIds;

    // The groups where the present players fly, as the snapshot sends them
    // (each player is sent those it sees, GroupSnapshot.SeenBy): every
    // hyperspace encounter with an element (while a player is in
    // hyperspace), hidden while its element isn't drawn (off every screen,
    // or in its vortex's first frames: on the radar only), and in each
    // system a present player is in, every group with an element (in a view
    // someone flies in), a re-forming one hidden on the frames it isn't
    // drawn. Null for none.
    private List<GroupSnapshot>? GroupsSnapshot(List<Player> present)
    {
        List<GroupSnapshot>? groups = null;
        if (Encounters is { } encounters)
            foreach (var e in encounters.List)
            {
                if (e.Element is not { } el)
                    continue;
                if (e.SnapshotId == 0)
                    e.SnapshotId = ++_encounterIds;
                (groups ??= new()).Add(new GroupSnapshot(e.SnapshotId, null, e.Race, 0, e.LogX, e.LogY, el.Frame, e.TransitionState,
                    (el.Drawn ? GroupSnapshotFlags.None : GroupSnapshotFlags.Hidden)
                    | (e.MeetingPlayer is null ? GroupSnapshotFlags.None : GroupSnapshotFlags.Met)));
            }
        if (SystemGroups is { } sg)
            foreach (int star in present.Select(p => p.Ship.SystemStar).OfType<int>().Distinct().Order())
            {
                if (!sg.Systems.TryGetValue(star, out var d))
                    continue;
                HashSet<int>? ids = null;
                foreach (var g in d.Groups)
                {
                    // One group a number (a draw's are numbered apart: the codec refuses two alike).
                    if (!g.InSystem || g.Element is not { } el || !(ids ??= new()).Add(g.GroupId))
                        continue;
                    (groups ??= new()).Add(new GroupSnapshot(g.GroupId, star, g.Race, g.SysLoc, g.LocX, g.LocY, 0, 0,
                        (el.Drawn ? GroupSnapshotFlags.None : GroupSnapshotFlags.Hidden)
                        | (g.InMeeting is null ? GroupSnapshotFlags.None : GroupSnapshotFlags.Met)));
                }
            }
        return groups;
    }
}
