// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>PLANSIZE of a planet type.</summary>
public enum PlanetSize { SmallRocky = 0, LargeRocky = 1, GasGiant = 2 }

/// <summary>
/// One of a planet type's eight mineral slots (ELEMENT_ENTRY). Element is the
/// element id (data/elements.json); empty slots (NOTHING) have quantity 0.
/// </summary>
public readonly record struct ElementSlot(int Element, int QuantityClass, int QualityClass);

/// <summary>
/// One planet type (UQM's PlanetFrame), from data/planet-types.json.
/// DensityClass and AtmosphereClass are the low and high nibble of
/// AtmoAndDensity. <see cref="Elements"/> always has the eight slots of
/// UsefulElements, because mineral generation draws a random number for
/// every slot, empty or not.
/// </summary>
public sealed record PlanetType(int Index, string Name, PlanetSize Size, int BaseTectonics,
    int DensityClass, int AtmosphereClass, IReadOnlyList<ElementSlot> Elements)
{
    public const int UsefulElementSlots = 8;   // NUM_USEFUL_ELEMENTS
}

/// <summary>All planet types, indexed like UQM's PlanData.</summary>
public sealed class PlanetTypes
{
    private readonly PlanetType[] _types;

    private PlanetTypes(PlanetType[] types)
    {
        _types = types;
    }

    public int Count => _types.Length;

    public PlanetType this[int index] => _types[index];

    public int IndexOf(string name) => Array.FindIndex(_types, t => t.Name == name) is var i and >= 0
        ? i : throw new KeyNotFoundException(name);

    public static PlanetTypes Load(string dataDirectory)
    {
        using var elementsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "elements.json")));
        var elementIds = elementsDoc.RootElement.GetProperty("elements").EnumerateArray()
            .ToDictionary(e => e.GetProperty("element").GetString()!, e => e.GetProperty("id").GetInt32());

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "planet-types.json")));
        var list = new List<PlanetType>();
        foreach (var t in doc.RootElement.GetProperty("planet_types").EnumerateArray())
        {
            var slots = t.GetProperty("useful_elements").EnumerateArray()
                .Select(e => new ElementSlot(elementIds[e.GetProperty("element").GetString()!],
                    e.GetProperty("quantity_class").GetInt32(), e.GetProperty("quality_class").GetInt32()))
                .ToList();
            if (slots.Count > PlanetType.UsefulElementSlots)
                throw new InvalidDataException("too many element slots");
            while (slots.Count < PlanetType.UsefulElementSlots)
                slots.Add(new ElementSlot(-1, 0, 0));   // NOTHING, NO_DEPOSIT
            list.Add(new PlanetType(
                list.Count,
                t.GetProperty("type").GetString()!,
                t.GetProperty("size").GetString() switch
                {
                    "SMALL_ROCKY_WORLD" => PlanetSize.SmallRocky,
                    "LARGE_ROCKY_WORLD" => PlanetSize.LargeRocky,
                    "GAS_GIANT" => PlanetSize.GasGiant,
                    var s => throw new InvalidDataException($"unknown planet size {s}"),
                },
                t.GetProperty("base_tectonics").GetInt32(),
                t.GetProperty("density_class").GetInt32(),
                t.GetProperty("atmosphere_class").GetInt32(),
                slots));
        }
        return new PlanetTypes(list.ToArray());
    }
}
