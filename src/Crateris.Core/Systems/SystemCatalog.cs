// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.Systems;

/// <summary>
/// The star map and the systems generated from it, each generated once on
/// first use. Systems are deterministic, so a system is the same whoever
/// asks and whenever.
/// </summary>
public sealed class SystemCatalog
{
    private readonly SolarSystem?[] _systems;

    public SystemCatalog(StarMap map, SystemGenerator generator)
    {
        Map = map;
        Generator = generator;
        _systems = new SolarSystem?[map.Stars.Count];
    }

    public StarMap Map { get; }

    public SystemGenerator Generator { get; }

    public static SystemCatalog Load(string dataDirectory, SystemStoryState? story = null) =>
        new(StarMap.Load(dataDirectory), new SystemGenerator(PlanetTypes.Load(dataDirectory), story));

    /// <summary>The system of a star (map number).</summary>
    public SolarSystem this[int star] => _systems[star] ??= Generator.Generate(Map.Stars[star]);

    /// <summary>Whether the star has this planet, and, if a moon is given, the planet has that moon.</summary>
    public bool HasWorld(int star, int planet, int? moon)
    {
        if ((uint)star >= (uint)_systems.Length)
            return false;
        SolarSystem sys = this[star];
        if ((uint)planet >= (uint)sys.Planets.Count)
            return false;
        return moon is not int m || (uint)m < sys.Planets[planet].NumPlanets;
    }
}
