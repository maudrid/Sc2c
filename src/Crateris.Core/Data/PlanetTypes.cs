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
/// One planet type (UQM's PlanetFrame), from data/planet-types.json.
/// DensityClass and AtmosphereClass are the low and high nibble of
/// AtmoAndDensity.
/// </summary>
public sealed record PlanetType(int Index, string Name, PlanetSize Size, int BaseTectonics,
    int DensityClass, int AtmosphereClass);

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
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "planet-types.json")));
        var list = new List<PlanetType>();
        foreach (var t in doc.RootElement.GetProperty("planet_types").EnumerateArray())
        {
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
                t.GetProperty("atmosphere_class").GetInt32()));
        }
        return new PlanetTypes(list.ToArray());
    }
}
