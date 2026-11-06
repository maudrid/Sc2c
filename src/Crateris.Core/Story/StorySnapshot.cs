// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Serialization;

namespace Crateris.Core.Story;

/// <summary>
/// The story state as clients are sent it: the packed game-state bits,
/// the prices and each race's standing (the events stay on the host).
/// Immutable; the host makes one only when the story changes, so a
/// snapshot with nothing new shares its base's.
/// </summary>
public sealed class StorySnapshot
{
    private readonly byte[] _bits;
    private readonly int[] _moduleCost;
    private readonly Allied[] _alliances;

    public StorySnapshot(ReadOnlySpan<byte> bits, int crewCost, int fuelCost, ReadOnlySpan<int> moduleCost, ReadOnlySpan<Allied> alliances)
    {
        _bits = bits.ToArray();
        CrewCost = crewCost;
        FuelCost = fuelCost;
        _moduleCost = moduleCost.ToArray();
        _alliances = alliances.ToArray();
    }

    /// <summary>The story state now, module prices in <see cref="StoryRules.Modules"/>'s order.</summary>
    public static StorySnapshot Of(StoryState story) =>
        new(story.Bits, story.CrewCost, story.FuelCost,
            story.Rules.Modules.Select(story.ModuleCost).ToArray(), story.Alliances.ToArray());

    [JsonIgnore]
    public ReadOnlySpan<byte> Bits => _bits;

    /// <summary>The module prices and the standing as spans, for the codec (no enumerators).</summary>
    internal ReadOnlySpan<int> ModuleCostSpan => _moduleCost;

    internal ReadOnlySpan<Allied> AlliancesSpan => _alliances;

    public int CrewCost { get; }

    public int FuelCost { get; }

    public IReadOnlyList<int> ModuleCost => _moduleCost;

    public IReadOnlyList<Allied> Alliances => _alliances;

    /// <summary>A field's value, by the table's place for it (as GET_GAME_STATE reads it).</summary>
    public int Get(GameStateTable table, string name)
    {
        var f = table[name];
        if (f.EndBit >= _bits.Length * 8)
            throw new ArgumentException($"{name} is past this snapshot's bits", nameof(name));
        return StoryState.GetBits(_bits, f.StartBit, f.EndBit);
    }

    public bool SameAs(StorySnapshot? other) =>
        other is not null && (ReferenceEquals(this, other)
            || (CrewCost == other.CrewCost && FuelCost == other.FuelCost
                && _bits.AsSpan().SequenceEqual(other._bits)
                && _moduleCost.AsSpan().SequenceEqual(other._moduleCost)
                && _alliances.AsSpan().SequenceEqual(other._alliances)));
}
