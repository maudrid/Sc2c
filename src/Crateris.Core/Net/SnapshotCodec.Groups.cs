// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Net;

// The snapshot's groups of alien ships (stage 3b-1 item 10, protocol 26):
// the groups the viewer sees (GroupSnapshot.SeenBy: the encounters on its
// screen in hyperspace, the groups in its view of a system), sent when they
// change, as a delta from those it was sent with the base: each group its id
// and system (which find it in the base), a mask of the numbers that changed,
// with a bit saying whether it's the base's, and those numbers as their
// difference from the base group's (so a group flying costs its key, its
// mask and a byte or two for each of its place's numbers); a group new since
// the base, every number from 0.
public static partial class SnapshotCodec
{
    private const int PartGroups = 256;

    /// <summary>The most groups a snapshot's message holds (hyperspace keeps 16 by default, a system a few).</summary>
    public const int MostGroups = 256;

    // A group's numbers, in the order of the mask's bits; then the bit of a group from the base.
    private const int GroupRace = 0, GroupView = 1, GroupX = 2, GroupY = 3, GroupFrame = 4, GroupTransition = 5, GroupFlags = 6;
    private const int GroupNumbers = 7;
    private const ulong AllGroupNumbers = (1UL << GroupNumbers) - 1, GroupFromBase = 1UL << GroupNumbers;

    /// <summary>Whether a viewer sees any group in the snapshot (its message is then its own).</summary>
    public static bool SeesGroups(WorldSnapshot? snapshot, string? viewer) => GroupSnapshot.SeenBy(snapshot, viewer).Count > 0;

    private static bool SameGroups(IReadOnlyList<GroupSnapshot> a, IReadOnlyList<GroupSnapshot> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; ++i)
            if (a[i] != b[i])
                return false;
        return true;
    }

    private static void GroupToNumbers(GroupSnapshot g, Span<long> v)
    {
        v[GroupRace] = g.Race;
        v[GroupView] = g.View;
        v[GroupX] = g.X;
        v[GroupY] = g.Y;
        v[GroupFrame] = g.Frame;
        v[GroupTransition] = g.Transition;
        v[GroupFlags] = (long)g.Flags;
    }

    // A group by its key (id and system) in a list, or null.
    private static GroupSnapshot? FindGroup(IReadOnlyList<GroupSnapshot>? groups, int id, int? star)
    {
        if (groups is null)
            return null;
        for (int i = 0; i < groups.Count; ++i)
            if (groups[i].Id == id && groups[i].Star == star)
                return groups[i];
        return null;
    }

    private static void WriteGroups(WireWriter w, IReadOnlyList<GroupSnapshot> groups, IReadOnlyList<GroupSnapshot> baseGroups)
    {
        int count = Math.Min(groups.Count, MostGroups);   // the decoder's limit
        w.UInt((ulong)count);
        Span<long> now = stackalloc long[GroupNumbers];
        Span<long> was = stackalloc long[GroupNumbers];
        for (int i = 0; i < count; ++i)
        {
            var g = groups[i];
            var old = FindGroup(baseGroups, g.Id, g.Star);
            w.Int(g.Id);
            w.Int(Optional(g.Star));
            GroupToNumbers(g, now);
            if (old is null)
                was.Clear();
            else
                GroupToNumbers(old, was);
            ulong mask = old is null ? 0 : GroupFromBase;
            for (int f = 0; f < GroupNumbers; ++f)
                if (old is null || now[f] != was[f])
                    mask |= 1UL << f;
            w.UInt(mask);
            for (int f = 0; f < GroupNumbers; ++f)
                if ((mask & (1UL << f)) != 0)
                    w.Int(now[f] - was[f]);
        }
    }

    // The base's groups are those it was sent (or a host's whole list, which
    // holds them: a group is found by its key either way).
    private static IReadOnlyList<GroupSnapshot>? ReadGroups(ref WireReader r, IReadOnlyList<GroupSnapshot>? baseGroups)
    {
        int count = Count(r.UInt(), MostGroups);
        if (count == 0)
            return null;
        var groups = new GroupSnapshot[count];
        Span<long> v = stackalloc long[GroupNumbers];
        for (int i = 0; i < count; ++i)
        {
            int id = ToInt(r.Int());
            int? star = ToOptional(r.Int());
            if (star < 0)
                throw new InvalidDataException("Bad group system.");
            ulong mask = r.UInt();
            GroupSnapshot? old = null;
            if ((mask & GroupFromBase) != 0)
                old = FindGroup(baseGroups, id, star) ?? throw new InvalidDataException("A group's base isn't in the base snapshot.");
            if ((mask & ~(AllGroupNumbers | GroupFromBase)) != 0 || (old is null && mask != AllGroupNumbers))
                throw new InvalidDataException("Bad group fields.");
            if (old is not null && (mask & AllGroupNumbers) == 0)
            {
                groups[i] = old;
                continue;
            }
            if (old is null)
                v.Clear();
            else
                GroupToNumbers(old, v);
            for (int f = 0; f < GroupNumbers; ++f)
                if ((mask & (1UL << f)) != 0)
                    v[f] = Add(v[f], r.Int());
            if (v[GroupRace] < 0 || v[GroupView] is < 0 or > byte.MaxValue
                    || v[GroupFlags] is < 0 or > (long)(GroupSnapshotFlags.Hidden | GroupSnapshotFlags.Met))
                throw new InvalidDataException("Bad group.");
            groups[i] = new GroupSnapshot(id, star, ToInt(v[GroupRace]), ToInt(v[GroupView]),
                ToInt(v[GroupX]), ToInt(v[GroupY]), ToInt(v[GroupFrame]), ToInt(v[GroupTransition]), (GroupSnapshotFlags)v[GroupFlags]);
        }
        for (int i = 1; i < count; ++i)
            for (int j = 0; j < i; ++j)
                if (groups[i].Id == groups[j].Id && groups[i].Star == groups[j].Star)
                    throw new InvalidDataException("Two groups have one id.");
        return groups;
    }
}
