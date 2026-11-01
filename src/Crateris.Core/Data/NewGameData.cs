// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>What a ship carries that matters in hyperspace.</summary>
public sealed record ShipLoadout(int Thrusters, int Jets, int FuelTanks, int HighEfficiencyTanks, int Crew, uint Fuel);

/// <summary>Starting values for a new game, from data/new-game.json.</summary>
public sealed record NewGameData(
    int Year, int Month, int Day,
    short StartUniverseX, short StartUniverseY,
    ShipLoadout Flagship,
    int? StartStar = null)
{
    public static NewGameData Load(string dataDirectory)
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
            Fuel: ship.GetProperty("fuel").GetUInt32());

        // The game starts at a star (Sol). The ship counts as having just
        // left it, so it doesn't fall straight in (StarContact).
        short sx = (short)ship.GetProperty("start_universe_x").GetInt32();
        short sy = (short)ship.GetProperty("start_universe_y").GetInt32();
        Star? startStar = StarMap.Load(dataDirectory).At(sx, sy);

        return new NewGameData(
            date.GetProperty("year").GetInt32(), date.GetProperty("month").GetInt32(),
            date.GetProperty("day").GetInt32(),
            (short)ship.GetProperty("start_universe_x").GetInt32(),
            (short)ship.GetProperty("start_universe_y").GetInt32(),
            loadout,
            startStar?.Number);
    }
}
