// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;

namespace Crateris.Core.Net;

/// <summary>
/// The story part of a snapshot (docs/plans/stage-3.md, item 1): sent only
/// when the story state changed from the base's. The game-state bits go
/// as the bytes that differ from the base's (from zeros without one), each
/// as its distance from the last and its value; the prices and the races'
/// standing go whole, and in a delta only if they changed.
/// </summary>
public static partial class SnapshotCodec
{
    private const byte PartStory = 8;
    // The story part's first byte: none, or which of its parts follow.
    private const byte StoryNone = 0, StoryFromBase = 1, StoryPrices = 2, StoryAlliances = 4, StoryPresent = 0x10;
    private const byte StoryKinds = StoryPresent | StoryFromBase | StoryPrices | StoryAlliances;
    private const int MostStoryBytes = 1024, MostModules = 64, MostRaces = 64;

    private static bool StoryChanged(StorySnapshot? now, StorySnapshot? was) =>
        now is null ? was is not null : !now.SameAs(was);

    private static void WriteStory(WireWriter w, StorySnapshot? story, StorySnapshot? baseStory)
    {
        if (story is null)
        {
            w.Byte(StoryNone);
            return;
        }
        var bits = story.Bits;
        bool fromBase = baseStory is not null && baseStory.Bits.Length == bits.Length;
        var was = fromBase ? baseStory!.Bits : ReadOnlySpan<byte>.Empty;
        bool prices = !fromBase || story.CrewCost != baseStory!.CrewCost || story.FuelCost != baseStory.FuelCost
            || !story.ModuleCostSpan.SequenceEqual(baseStory.ModuleCostSpan);
        bool alliances = !fromBase || !story.AlliancesSpan.SequenceEqual(baseStory!.AlliancesSpan);
        w.Byte((byte)(StoryPresent | (fromBase ? StoryFromBase : 0) | (prices ? StoryPrices : 0) | (alliances ? StoryAlliances : 0)));
        w.UInt((ulong)bits.Length);
        int changed = 0;
        for (int i = 0; i < bits.Length; ++i)
        {
            if (bits[i] != (fromBase ? was[i] : 0))
                ++changed;
        }
        w.UInt((ulong)changed);
        int last = -1;
        for (int i = 0; i < bits.Length; ++i)
        {
            if (bits[i] == (fromBase ? was[i] : 0))
                continue;
            w.UInt((ulong)(i - last - 1));
            w.Byte(bits[i]);
            last = i;
        }
        if (prices)
        {
            w.Int(story.CrewCost);
            w.Int(story.FuelCost);
            var costs = story.ModuleCostSpan;
            w.UInt((ulong)costs.Length);
            foreach (int c in costs)
                w.Int(c);
        }
        if (alliances)
        {
            var standing = story.AlliancesSpan;
            w.UInt((ulong)standing.Length);
            foreach (var a in standing)
                w.Byte((byte)a);
        }
    }

    private static StorySnapshot? ReadStory(ref WireReader r, StorySnapshot? baseStory)
    {
        byte mode = r.Byte();
        if (mode == StoryNone)
            return null;
        bool fromBase = (mode & StoryFromBase) != 0;
        if ((mode & ~StoryKinds) != 0 || (mode & StoryPresent) == 0
                || (!fromBase && (mode & (StoryPrices | StoryAlliances)) != (StoryPrices | StoryAlliances)))
            throw new InvalidDataException($"Unknown story kind {mode}.");
        int length = Count(r.UInt(), MostStoryBytes);
        byte[] bits;
        if (fromBase)
        {
            if (baseStory is null || baseStory.Bits.Length != length)
                throw new InvalidDataException("A story delta doesn't fit its base.");
            bits = baseStory.Bits.ToArray();
        }
        else
            bits = new byte[length];
        int changed = Count(r.UInt(), length);
        int at = -1;
        for (int i = 0; i < changed; ++i)
        {
            ulong gap = r.UInt();
            if (gap >= (ulong)(length - at - 1))
                throw new InvalidDataException("A story byte is past the end.");
            at += (int)gap + 1;
            bits[at] = r.Byte();
        }
        int crew, fuel;
        int[] modules;
        if ((mode & StoryPrices) != 0)
        {
            crew = Price(r.Int());
            fuel = Price(r.Int());
            modules = new int[Count(r.UInt(), MostModules)];
            for (int i = 0; i < modules.Length; ++i)
                modules[i] = Price(r.Int());
        }
        else
            (crew, fuel, modules) = (baseStory!.CrewCost, baseStory.FuelCost, baseStory.ModuleCost.ToArray());
        Allied[] alliances;
        if ((mode & StoryAlliances) != 0)
        {
            alliances = new Allied[Count(r.UInt(), MostRaces)];
            for (int i = 0; i < alliances.Length; ++i)
            {
                byte a = r.Byte();
                if (a > (byte)Allied.Bad)
                    throw new InvalidDataException($"A race's standing is {a}.");
                alliances[i] = (Allied)a;
            }
        }
        else
            alliances = baseStory!.Alliances.ToArray();
        return new StorySnapshot(bits, crew, fuel, modules, alliances);

        static int Price(long v) => v is >= 0 and <= byte.MaxValue ? (int)v : throw new InvalidDataException("Bad price.");
    }
}
