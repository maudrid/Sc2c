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
/// A race's sphere of influence as the players know it (stage 3c item 3,
/// the plan's question 8: the status panel's fleet lines): the race
/// (races.h's number), its strength (the sphere's size, in steps of
/// SPHERE_RADIUS_INCREMENT) and centre (universe units), as UQM's star
/// map would show it once opened: each tracked sphere (KnownStrength not
/// 0, StartSphereTracking) brought up to where the fleet is and how strong
/// (pstarmap.c's UpdateMap, which StarMap calls while ARILOU_SPACE_SIDE is 1 or less, sets known_loc and known_strength so), and
/// one shrunk to nothing not shown.
/// </summary>
public readonly record struct KnownFleet(int Race, int Strength, int X, int Y);

/// <summary>
/// The story state as clients are sent it: the packed game-state bits,
/// the prices, each race's standing and the spheres the players know
/// (<see cref="KnownFleet"/>; the events and the rest of the fleets stay
/// on the host). Immutable; the host makes one only when the story
/// changes, so a snapshot with nothing new shares its base's.
/// </summary>
public sealed class StorySnapshot
{
    private readonly byte[] _bits;
    private readonly int[] _moduleCost;
    private readonly Allied[] _alliances;
    private readonly KnownFleet[] _fleets;

    public StorySnapshot(ReadOnlySpan<byte> bits, int crewCost, int fuelCost, ReadOnlySpan<int> moduleCost, ReadOnlySpan<Allied> alliances,
        ReadOnlySpan<KnownFleet> fleets = default)
    {
        _bits = bits.ToArray();
        CrewCost = crewCost;
        FuelCost = fuelCost;
        _moduleCost = moduleCost.ToArray();
        _alliances = alliances.ToArray();
        _fleets = fleets.ToArray();
    }

    /// <summary>The story state now, module prices in <see cref="StoryRules.Modules"/>'s order.</summary>
    public static StorySnapshot Of(StoryState story) =>
        new(story.Bits, story.CrewCost, story.FuelCost,
            story.Rules.Modules.Select(story.ModuleCost).ToArray(), story.Alliances.ToArray(), KnownFleets(story).ToArray());

    /// <summary>
    /// The spheres the players know, in races.h's order: each race whose
    /// sphere is tracked (KnownStrength not 0) and not shrunk to nothing,
    /// at its strength and place now, as the star map shows it once opened
    /// (UQM's map then sets the known strength and place to these: the port
    /// has no map, question 8, so they're read, not written).
    /// </summary>
    public static IEnumerable<KnownFleet> KnownFleets(StoryState story) =>
        story.Fleets.Select((f, i) => (f, i))
            .Where(x => x.f.KnownStrength != 0 && x.f.ActualStrength != 0)
            .Select(x => new KnownFleet(x.i, x.f.ActualStrength, x.f.LocX, x.f.LocY));

    [JsonIgnore]
    public ReadOnlySpan<byte> Bits => _bits;

    /// <summary>The module prices and the standing as spans, for the codec (no enumerators).</summary>
    internal ReadOnlySpan<int> ModuleCostSpan => _moduleCost;

    internal ReadOnlySpan<Allied> AlliancesSpan => _alliances;

    internal ReadOnlySpan<KnownFleet> FleetsSpan => _fleets;

    /// <summary>The spheres the players know (<see cref="KnownFleet"/>), in races.h's order.</summary>
    public IReadOnlyList<KnownFleet> Fleets => _fleets;

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
                && _alliances.AsSpan().SequenceEqual(other._alliances)
                && _fleets.AsSpan().SequenceEqual(other._fleets)));
}
