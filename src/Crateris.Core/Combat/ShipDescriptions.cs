// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/races.h: RACE_DESC and its
// parts; src/uqm/init.c: load_animation), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Combat;

/// <summary>SHIP_INFO.ship_flags: how a ship fires and what the AI should know of it (races.h).</summary>
[Flags]
public enum ShipFlags
{
    None = 0,
    SeekingWeapon = 1 << 2,
    SeekingSpecial = 1 << 3,
    PointDefense = 1 << 4,
    ImmediateWeapon = 1 << 5,
    CrewImmune = 1 << 6,
    FiresFore = 1 << 7,
    FiresRight = 1 << 8,
    FiresAft = 1 << 9,
    FiresLeft = 1 << 10,
    ShieldDefense = 1 << 11,
    DontChase = 1 << 12,
    /// <summary>The captain himself is aboard (init_sis sets it on the flagship outside Super Melee).</summary>
    PlayerCaptain = 1 << 13,
}

/// <summary>CHARACTERISTIC_STUFF: a ship's thrust, turning, energy and waits (in battle frames), and its mass.</summary>
public sealed record ShipCharacteristics(
    int MaxThrust, int ThrustIncrement, int EnergyRegeneration, int WeaponEnergyCost, int SpecialEnergyCost,
    int EnergyWait, int TurnWait, int ThrustWait, int WeaponWait, int SpecialWait, int ShipMass);

/// <summary>INTEL_STUFF: what the combat AI reads of a ship.</summary>
public readonly record struct ShipIntel(int ManeuverabilityIndex, int WeaponRange);

/// <summary>FLEET_STUFF: the starting size and centre of the race's sphere of influence (strength 0: none).</summary>
public readonly record struct ShipFleet(int Strength, int HomeX, int HomeY);

/// <summary>
/// The content a RACE_DESC names (DATA_STUFF and SHIP_INFO), null for
/// NULL_RESOURCE: the ship, its weapon and its special at the three zooms
/// (big, medium, small), its captain's window, the status panel's icons and
/// the icon it flies with in a system (melee_icon).
/// </summary>
public sealed record ShipGraphics(
    IReadOnlyList<string?> Ship, IReadOnlyList<string?> Weapon, IReadOnlyList<string?> Special,
    string? Captain, string? Icons, string? MeleeIcon)
{
    /// <summary>NUM_VIEWS: big, medium and small.</summary>
    public const int Views = 3;

    /// <summary>
    /// The sheet load_animation loads for each zoom, or null without a big
    /// one (NULL_RESOURCE: none at all): a size without a resource of its
    /// own takes the one before (the Umgah's one-size spritz).
    /// </summary>
    public static IReadOnlyList<string>? Sheets(IReadOnlyList<string?> resources)
    {
        if (resources[0] is null)
            return null;
        var sheets = new string[Views];
        for (int v = 0; v < Views; ++v)
            sheets[v] = resources[v] ?? sheets[v - 1];
        return sheets;
    }

    /// <summary>Every GFXRES resource a battle loads for the ship (load_ship with its battle data): its sheets, captain and icons.</summary>
    public IEnumerable<string> BattleResources() =>
        new[] { Sheets(Ship), Sheets(Weapon), Sheets(Special) }
            .Where(s => s != null).SelectMany(s => s!)
            .Concat(new[] { Captain, Icons }.OfType<string>())
            .Distinct();
}

/// <summary>
/// A ship's description, UQM's RACE_DESC as each ship's source file
/// initialises it (data/ships.json). <see cref="Crew"/> is the crew it
/// starts with (crew_level, the MAX_CREW define), not always its most
/// (<see cref="MaxCrew"/>): the Syreen start with 12 of 42. The flagship's
/// (sis_ship) is the start init_sis builds on from its modules.
/// <see cref="Defines"/> are every integer #define in the ship's file, by
/// name: its weapons' speed, life, damage and range, which aren't in the
/// RACE_DESC.
/// </summary>
public sealed record ShipDescription(
    string Id, string? Species, ShipFlags Flags, int Cost,
    int Crew, int MaxCrew, int Energy, int MaxEnergy,
    ShipFleet Fleet, ShipCharacteristics Characteristics, ShipIntel Intel,
    string? RaceStrings, ShipGraphics Graphics, IReadOnlyDictionary<string, int> Defines)
{
    /// <summary>A #define of the ship's file, by name.</summary>
    public int Define(string name) => Defines.TryGetValue(name, out int value)
        ? value : throw new KeyNotFoundException($"{Id}'s source has no #define {name}");
}

/// <summary>Every ship's description (data/ships.json), by its source directory's name.</summary>
public sealed class ShipDescriptions
{
    /// <summary>The flagship's id (ships/sis_ship).</summary>
    public const string Flagship = "sis_ship";

    /// <summary>The Ilwrath Avenger's id (ships/ilwrath).</summary>
    public const string Ilwrath = "ilwrath";

    private readonly Dictionary<string, ShipDescription> _ships;

    private ShipDescriptions(IReadOnlyList<ShipDescription> all)
    {
        All = all;
        _ships = all.ToDictionary(s => s.Id);
    }

    /// <summary>Every ship, in ships.json's order (by directory name).</summary>
    public IReadOnlyList<ShipDescription> All { get; }

    public ShipDescription this[string id] => _ships.TryGetValue(id, out var ship)
        ? ship : throw new KeyNotFoundException($"no ship {id}");

    /// <summary>The ship a SPECIES_ID loads (code_resources in loadship.c).</summary>
    public ShipDescription BySpecies(string species) => All.FirstOrDefault(s => s.Species == species)
        ?? throw new KeyNotFoundException($"no ship for {species}");

    private static readonly IReadOnlyDictionary<string, ShipFlags> FlagNames = new Dictionary<string, ShipFlags>
    {
        ["SEEKING_WEAPON"] = ShipFlags.SeekingWeapon,
        ["SEEKING_SPECIAL"] = ShipFlags.SeekingSpecial,
        ["POINT_DEFENSE"] = ShipFlags.PointDefense,
        ["IMMEDIATE_WEAPON"] = ShipFlags.ImmediateWeapon,
        ["CREW_IMMUNE"] = ShipFlags.CrewImmune,
        ["FIRES_FORE"] = ShipFlags.FiresFore,
        ["FIRES_RIGHT"] = ShipFlags.FiresRight,
        ["FIRES_AFT"] = ShipFlags.FiresAft,
        ["FIRES_LEFT"] = ShipFlags.FiresLeft,
        ["SHIELD_DEFENSE"] = ShipFlags.ShieldDefense,
        ["DONT_CHASE"] = ShipFlags.DontChase,
        ["PLAYER_CAPTAIN"] = ShipFlags.PlayerCaptain,
    };

    /// <summary>
    /// Reads data/ships.json. The flags' bits are checked against UQM's
    /// (flag_values), so a flag the port doesn't know, or knows by another
    /// bit, is an <see cref="InvalidDataException"/>, as is anything else
    /// missing or out of shape.
    /// </summary>
    public static ShipDescriptions Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "ships.json")));
        var root = doc.RootElement;
        try
        {
            foreach (var f in root.GetProperty("flag_values").EnumerateObject())
                if (!FlagNames.TryGetValue(f.Name, out var flag) || (int)flag != f.Value.GetInt32())
                    throw new InvalidDataException($"ship flag {f.Name} is bit {f.Value.GetInt32():x} in UQM, which the port doesn't have");
            var ships = new List<ShipDescription>();
            foreach (var s in root.GetProperty("ships").EnumerateArray())
            {
                string id = s.GetProperty("id").GetString()!;
                var flags = ShipFlags.None;
                foreach (var f in s.GetProperty("flags").EnumerateArray())
                    flags |= FlagNames.TryGetValue(f.GetString()!, out var flag)
                        ? flag : throw new InvalidDataException($"{id} has an unknown flag {f.GetString()}");
                var fleet = s.GetProperty("fleet");
                var ch = s.GetProperty("characteristics");
                int C(string name) => ch.GetProperty(name).GetInt32();
                var ai = s.GetProperty("ai");
                var g = s.GetProperty("graphics");
                string? Str(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();
                IReadOnlyList<string?> Views(string name)
                {
                    var views = g.GetProperty(name).EnumerateArray().Select(Str).ToArray();
                    if (views.Length != ShipGraphics.Views)
                        throw new InvalidDataException($"{id}'s {name} graphics aren't {ShipGraphics.Views} sizes");
                    return views;
                }
                ships.Add(new ShipDescription(
                    id,
                    Str(s.GetProperty("species")),
                    flags,
                    s.GetProperty("cost").GetInt32(),
                    s.GetProperty("crew").GetInt32(),
                    s.GetProperty("max_crew").GetInt32(),
                    s.GetProperty("energy").GetInt32(),
                    s.GetProperty("max_energy").GetInt32(),
                    new ShipFleet(fleet.GetProperty("strength").GetInt32(),
                        fleet.GetProperty("home_x").GetInt32(), fleet.GetProperty("home_y").GetInt32()),
                    new ShipCharacteristics(
                        C("max_thrust"), C("thrust_increment"), C("energy_regeneration"),
                        C("weapon_energy_cost"), C("special_energy_cost"), C("energy_wait"),
                        C("turn_wait"), C("thrust_wait"), C("weapon_wait"), C("special_wait"), C("ship_mass")),
                    new ShipIntel(ai.GetProperty("maneuverability_index").GetInt32(), ai.GetProperty("weapon_range").GetInt32()),
                    Str(s.GetProperty("race_strings_resource")),
                    new ShipGraphics(Views("ship"), Views("weapon"), Views("special"),
                        Str(g.GetProperty("captain")), Str(g.GetProperty("icons")), Str(g.GetProperty("melee_icon"))),
                    s.GetProperty("defines").EnumerateObject().ToDictionary(d => d.Name, d => d.Value.GetInt32())));
            }
            if (ships.Select(s => s.Id).Distinct().Count() != ships.Count)
                throw new InvalidDataException("ships.json names a ship twice");
            return new ShipDescriptions(ships);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"ships.json can't be read: {e.Message}", e);
        }
    }
}
