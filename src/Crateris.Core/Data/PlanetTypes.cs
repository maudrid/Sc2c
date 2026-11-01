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

/// <summary>How a planet type's surface map is made (TOPO_ALGO, CRATERED_ALGO, GAS_GIANT_ALGO).</summary>
public enum MapAlgorithm { Topo = 0, Cratered = 1, GasGiant = 2 }

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
/// <summary>
/// One of UQM's planet types (PlanData). BodyColor is the colour family
/// UQM draws the planet in (PLANCOLOR: BLUE_BODY, RED_BODY and so on).
/// </summary>
public sealed record PlanetType(int Index, string Name, PlanetSize Size, int BaseTectonics,
    int DensityClass, int AtmosphereClass, IReadOnlyList<ElementSlot> Elements, string BodyColor = "")
{
    /// <summary>How its surface map is made (PLANALGO).</summary>
    public MapAlgorithm Algorithm { get; init; }

    /// <summary>The surface generator's parameters (PlanetFrame: num_faults, fault_depth, num_blemishes, base_elevation).</summary>
    public int NumFaults { get; init; }
    public int FaultDepth { get; init; }
    public int NumBlemishes { get; init; }
    public int BaseElevation { get; init; }

    /// <summary>The content's colour map and translate table for its surface (BINTAB resource ids).</summary>
    public string ColorMapResource { get; init; } = "";
    public string TranslateTableResource { get; init; } = "";

    public const int UsefulElementSlots = 8;   // NUM_USEFUL_ELEMENTS
}

/// <summary>All planet types, indexed like UQM's PlanData.</summary>
public sealed class PlanetTypes
{
    private readonly PlanetType[] _types;

    private readonly int[] _elementCategory;

    private PlanetTypes(PlanetType[] types, int[] elementCategory)
    {
        _types = types;
        _elementCategory = elementCategory;
    }

    /// <summary>The names of UQM's element categories, in order (elemdata.h: COMMON, CORROSIVE and so on).</summary>
    public static readonly IReadOnlyList<string> ElementCategoryNames = new[]
    {
        "COMMON", "CORROSIVE", "BASE_METAL", "NOBLE", "RARE_EARTH", "PRECIOUS", "RADIOACTIVE", "EXOTIC",
    };

    /// <summary>ElementCategory: an element's category, 0..7 (an index into <see cref="ElementCategoryNames"/>).</summary>
    public int ElementCategory(int element) => _elementCategory[element];

    public int Count => _types.Length;

    public PlanetType this[int index] => _types[index];

    public int IndexOf(string name) => Array.FindIndex(_types, t => t.Name == name) is var i and >= 0
        ? i : throw new KeyNotFoundException(name);

    public static PlanetTypes Load(string dataDirectory)
    {
        using var elementsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "elements.json")));
        var elements = elementsDoc.RootElement.GetProperty("elements").EnumerateArray().ToList();
        var elementIds = elements
            .ToDictionary(e => e.GetProperty("element").GetString()!, e => e.GetProperty("id").GetInt32());
        var categories = new int[elements.Count];
        foreach (var e in elements)
        {
            int id = e.GetProperty("id").GetInt32();
            string category = e.GetProperty("category").GetString()!;
            if (id < 0 || id >= categories.Length)
                throw new InvalidDataException($"element id {id} out of range");
            categories[id] = ElementCategoryNames.ToList().IndexOf(category) is var c and >= 0
                ? c : throw new InvalidDataException($"unknown element category {category}");
        }

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
                slots,
                t.GetProperty("color").GetString()!)
            {
                Algorithm = t.GetProperty("map_algorithm").GetString() switch
                {
                    "TOPO_ALGO" => MapAlgorithm.Topo,
                    "CRATERED_ALGO" => MapAlgorithm.Cratered,
                    "GAS_GIANT_ALGO" => MapAlgorithm.GasGiant,
                    var a => throw new InvalidDataException($"unknown map algorithm {a}"),
                },
                NumFaults = t.GetProperty("num_faults").GetInt32(),
                FaultDepth = t.GetProperty("fault_depth").GetInt32(),
                NumBlemishes = t.GetProperty("num_blemishes").GetInt32(),
                BaseElevation = t.GetProperty("base_elevation").GetInt32(),
                ColorMapResource = t.GetProperty("color_map_resource").GetString() ?? "",
                TranslateTableResource = t.GetProperty("color_table_resource").GetString() ?? "",
            });
        }
        return new PlanetTypes(list.ToArray(), categories);
    }
}
