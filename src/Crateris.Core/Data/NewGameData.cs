// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>
/// A ship's modules and what it starts with: drives and jets, fuel tanks,
/// its starting crew and fuel, its starting landers and its storage bays
/// (STORAGE_BAY modules, 500 units of minerals each). Landers and storage
/// bays default to 1 each, what both a new flagship and an escort get, so
/// saves from before stage 2b load with them. CrewCapacity is the crew it
/// can hold (GetCrewPodCapacity: 50 a crew pod; an escort's kit holds 10,
/// stage 3, decision 10); 50, a new flagship's one pod, if not given.
/// Guns is an escort's kit's guns (GUN_WEAPON, stage 4, decision 3), in
/// the weapon slots in the order UQM's flagship fires from them (module
/// slots 15, 14, 13, then 0; at most 4); none if not given, as in saves
/// from before stage 4a item 8. A flagship's one gun is UQM's own
/// (InitGameStructures' in slot 15), not its loadout's: a flagship's
/// loadout has none.
/// </summary>
public sealed record ShipLoadout(int Thrusters, int Jets, int FuelTanks, int HighEfficiencyTanks, int Crew, uint Fuel,
    int Landers = 1, int StorageBays = 1, int CrewCapacity = ShipLoadout.CrewPodCapacity, int Guns = 0)
{
    /// <summary>CREW_POD_CAPACITY (sis.h).</summary>
    public const int CrewPodCapacity = 50;
}

/// <summary>Starting values for a new game, from data/new-game.json.</summary>
public sealed record NewGameData(
    int Year, int Month, int Day,
    short StartUniverseX, short StartUniverseY,
    ShipLoadout Flagship,
    int? StartStar = null)
{
    /// <summary>
    /// Loads data/new-game.json. The flagship starts on its start star
    /// (start_star, Sol), which it counts as having just left so it doesn't
    /// fall straight in; pass the star map if it's already loaded.
    /// </summary>
    public static NewGameData Load(string dataDirectory, StarMap? map = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "new-game.json")));
        var root = doc.RootElement;
        var date = root.GetProperty("start_date");
        var ship = root.GetProperty("flagship");
        var slots = ship.GetProperty("slots");

        static int Count(JsonElement slots, string kind, string module) =>
            slots.GetProperty(kind).EnumerateObject().Count(p => p.Value.GetString() == module);

        var loadout = new ShipLoadout(
            Thrusters: Count(slots, "drive", "FUSION_THRUSTER"),
            Jets: Count(slots, "jet", "TURNING_JETS"),
            FuelTanks: Count(slots, "module", "FUEL_TANK"),
            HighEfficiencyTanks: Count(slots, "module", "HIGHEFF_FUELSYS"),
            Crew: ship.GetProperty("crew").GetInt32(),
            Fuel: ship.GetProperty("fuel").GetUInt32(),
            Landers: ship.GetProperty("landers").GetInt32(),
            StorageBays: Count(slots, "module", "STORAGE_BAY"),
            CrewCapacity: Count(slots, "module", "CREW_POD") * ShipLoadout.CrewPodCapacity);

        short sx = (short)ship.GetProperty("start_universe_x").GetInt32();
        short sy = (short)ship.GetProperty("start_universe_y").GetInt32();
        string startName = ship.GetProperty("start_star").GetString()!;
        map ??= StarMap.Load(dataDirectory);
        Star startStar = map.Stars.SingleOrDefault(s => s.Special == startName + "_DEFINED")
            ?? throw new InvalidDataException($"new-game.json: no star for start_star {startName}");
        if (startStar.X != sx || startStar.Y != sy)
            throw new InvalidDataException(
                $"new-game.json: start ({sx}, {sy}) is not on {startName} at ({startStar.X}, {startStar.Y})");

        return new NewGameData(
            date.GetProperty("year").GetInt32(), date.GetProperty("month").GetInt32(),
            date.GetProperty("day").GetInt32(),
            sx, sy,
            loadout,
            startStar.Number);
    }
}
