// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Net;

// The snapshot's containers (stage 3c item 5, protocol 36): every leaver's
// container in space, its number, leaver's name, place and how many
// devices it holds, sent whole when they change.
public static partial class SnapshotCodec
{
    private const int PartContainers = 512;

    /// <summary>The most containers a snapshot holds (one per player who left with devices).</summary>
    public const int MostContainers = 1024;

    private static bool SameContainers(IReadOnlyList<ContainerSnapshot>? a, IReadOnlyList<ContainerSnapshot>? b)
    {
        int n = a?.Count ?? 0;
        if (n != (b?.Count ?? 0))
            return false;
        for (int i = 0; i < n; ++i)
            if (a![i] != b![i])
                return false;
        return true;
    }

    private static void WriteContainers(WireWriter w, IReadOnlyList<ContainerSnapshot>? containers)
    {
        int count = Math.Min(containers?.Count ?? 0, MostContainers);
        w.UInt((ulong)count);
        for (int i = 0; i < count; ++i)
        {
            var c = containers![i];
            w.UInt((ulong)c.Id);
            w.String(c.OwnerName);
            w.Int(Optional(c.Star));
            w.Int(Optional(c.InnerPlanet));
            w.Int(c.X);
            w.Int(c.Y);
            w.UInt((ulong)c.Devices);
        }
    }

    private static IReadOnlyList<ContainerSnapshot> ReadContainers(ref WireReader r)
    {
        var containers = new ContainerSnapshot[Count(r.UInt(), MostContainers)];
        for (int i = 0; i < containers.Length; ++i)
        {
            int id = ToInt((long)Math.Min(r.UInt(), int.MaxValue));
            string name = r.String() ?? throw new InvalidDataException("A container has no leaver's name.");
            containers[i] = new ContainerSnapshot(id, name, ToOptional(r.Int()), ToOptional(r.Int()), ToInt(r.Int()), ToInt(r.Int()),
                Count(r.UInt(), MostDevices));
        }
        return containers;
    }
}
