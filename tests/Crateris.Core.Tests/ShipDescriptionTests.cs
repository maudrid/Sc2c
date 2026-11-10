// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Data;

namespace Crateris.Core.Tests;

/// <summary>The ships' descriptions (UQM's RACE_DESCs, data/ships.json) and the battle's content by resource id.</summary>
[Trait("Topic", "combat")]
public class ShipDescriptionTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly Lazy<ShipDescriptions> Ships = new(() => ShipDescriptions.Load(Data));

    [Fact]
    public void EveryShipIsRead()
    {
        Assert.Equal(28, Ships.Value.All.Count);
        Assert.All(Ships.Value.All, s => Assert.NotNull(s.Species));
        Assert.Same(Ships.Value["syreen"], Ships.Value.BySpecies("SYREEN_ID"));
        Assert.Same(Ships.Value[ShipDescriptions.Flagship], Ships.Value.BySpecies("SIS_SHIP_ID"));
        Assert.Throws<KeyNotFoundException>(() => Ships.Value["nobody"]);
    }

    [Fact]
    public void TheFlagshipStartsFromSisDesc()
    {
        // sis_desc, which init_sis builds on from the ship's modules.
        var s = Ships.Value[ShipDescriptions.Flagship];
        Assert.Equal((ShipFlags.None, 16, 42, 42, 42, 42), (s.Flags, s.Cost, s.Crew, s.MaxCrew, s.Energy, s.MaxEnergy));
        Assert.Equal(new ShipCharacteristics(10, 4, 1, 1, 0, 10, 17, 6, 6, 9, 10), s.Characteristics);
        // WeaponRange: BLASTER_SPEED * BLASTER_LIFE.
        Assert.Equal(new ShipIntel(0, 96 * 12), s.Intel);
        Assert.Equal((96, 12, 2), (s.Define("BLASTER_SPEED"), s.Define("BLASTER_LIFE"), s.Define("BLASTER_DAMAGE")));
        Assert.Null(s.RaceStrings);
        Assert.Throws<KeyNotFoundException>(() => s.Define("NO_SUCH_DEFINE"));
    }

    [Fact]
    public void TheIlwrathIsIlwrathDesc()
    {
        var s = Ships.Value[ShipDescriptions.Ilwrath];
        Assert.Equal((ShipFlags.FiresFore, 10, 22, 22, 16, 16), (s.Flags, s.Cost, s.Crew, s.MaxCrew, s.Energy, s.MaxEnergy));
        Assert.Equal(new ShipCharacteristics(25, 5, 4, 1, 3, 4, 2, 0, 0, 13, 7), s.Characteristics);
        // CLOSE_RANGE_WEAPON; SoI 1410 / SPHERE_RADIUS_INCREMENT * 2 round (48, 1700).
        Assert.Equal(new ShipIntel(0, 200), s.Intel);
        Assert.Equal(new ShipFleet(256, 48, 1700), s.Fleet);
        Assert.Equal((8, 25, 1, 29), (s.Define("MISSILE_LIFE"), s.Define("MISSILE_SPEED"), s.Define("MISSILE_DAMAGE"), s.Define("ILWRATH_OFFSET")));
        Assert.Equal("ship.ilwrath.text", s.RaceStrings);
    }

    [Fact]
    public void CrewIsTheStartingCrew()
    {
        // MAX_CREW is the crew a ship starts with: the Syreen 12 of 42.
        var syreen = Ships.Value["syreen"];
        Assert.Equal((12, 42, 12), (syreen.Crew, syreen.MaxCrew, syreen.Define("MAX_CREW")));
        foreach (var s in Ships.Value.All.Where(s => s.Defines.ContainsKey("MAX_CREW")))
            Assert.Equal(s.Define("MAX_CREW"), s.Crew);
    }

    [Fact]
    public void FlagsAreUqmsBits()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "ships.json")));
        var bits = doc.RootElement.GetProperty("flag_values").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32());
        Assert.Equal(12, bits.Count);
        Assert.Equal((int)ShipFlags.FiresFore, bits["FIRES_FORE"]);
        Assert.Equal((int)ShipFlags.ShieldDefense, bits["SHIELD_DEFENSE"]);
        Assert.Equal((int)ShipFlags.PlayerCaptain, bits["PLAYER_CAPTAIN"]);
        // Each ship's flags as its RACE_DESC sets them, SHIELD_DEFENSE too.
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.PointDefense | ShipFlags.ShieldDefense, Ships.Value["utwig"].Flags);
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.ShieldDefense, Ships.Value["yehat"].Flags);
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.SeekingWeapon, Ships.Value["androsyn"].Flags);
        Assert.Equal(ShipFlags.FiresFore | ShipFlags.SeekingWeapon | ShipFlags.PointDefense, Ships.Value["human"].Flags);
    }

    [Fact]
    public void AFlagThePortDoesntKnowIsRefused()
    {
        string dir = Directory.CreateTempSubdirectory("crateris-ships-").FullName;
        try
        {
            string json = File.ReadAllText(Path.Combine(Data, "ships.json"));
            void Write(Action<System.Text.Json.Nodes.JsonNode> change)
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                change(root);
                File.WriteAllText(Path.Combine(dir, "ships.json"), root.ToJsonString());
            }
            Write(_ => { });
            Assert.Equal(28, ShipDescriptions.Load(dir).All.Count);
            // UQM's bit for a flag isn't the port's.
            Write(r => r["flag_values"]!["DONT_CHASE"] = 2);
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
            // A flag UQM has and the port doesn't.
            Write(r => r["flag_values"]!["NEW_FLAG"] = 1 << 14);
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
            // A ship with a flag of no known name.
            Write(r => r["ships"]![0]!["flags"]![0] = "POINT_DEFENCE");
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
            // A define named twice, or a ship without an id.
            File.WriteAllText(Path.Combine(dir, "ships.json"), json.Replace("\"MAX_ENERGY\": 24,", "\"MAX_ENERGY\": 24, \"MAX_ENERGY\": 25,"));
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
            Write(r => r["ships"]![0]!["id"] = null);
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
            // Graphics not at three sizes.
            Write(r => r["ships"]![0]!["graphics"]!["ship"]!.AsArray().RemoveAt(2));
            Assert.Throws<InvalidDataException>(() => ShipDescriptions.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ShipsNameTheirGraphics()
    {
        var flagship = Ships.Value[ShipDescriptions.Flagship].Graphics;
        Assert.Equal(new[] { "large", "medium", "small" }.Select(v => $"ship.flagship.graphics.flagship.{v}"), flagship.Ship);
        Assert.Equal(new[] { "large", "medium", "small" }.Select(v => $"ship.flagship.graphics.blaster.{v}"), flagship.Weapon);
        Assert.Null(ShipGraphics.Sheets(flagship.Special));
        Assert.Equal(("ship.flagship.graphics.captain", "ship.flagship.icons", (string?)null),
            (flagship.Captain, flagship.Icons, flagship.MeleeIcon));
        var ilwrath = Ships.Value[ShipDescriptions.Ilwrath].Graphics;
        Assert.Equal(new[] { "large", "medium", "small" }.Select(v => $"ship.ilwrath.graphics.avenger.{v}"), ilwrath.Ship);
        Assert.Equal(new[] { "large", "medium", "small" }.Select(v => $"ship.ilwrath.graphics.fire.{v}"), ilwrath.Weapon);
        Assert.Equal(("ship.ilwrath.graphics.captain", "ship.ilwrath.icons", "ship.ilwrath.meleeicons"),
            (ilwrath.Captain, ilwrath.Icons, ilwrath.MeleeIcon));
        // A battle draws the sheets at three zooms, the captain and the icons, not the melee icon (which load_ship loads, for systems and the shipyard).
        Assert.Equal(8, ilwrath.BattleResources().Count());
        Assert.DoesNotContain("ship.ilwrath.meleeicons", ilwrath.BattleResources());
        // load_animation reuses the size before for a size without a resource: the Umgah's spritz has one.
        Assert.Equal(Enumerable.Repeat("ship.umgah.graphics.spritz", 3), ShipGraphics.Sheets(Ships.Value["umgah"].Graphics.Weapon));
        Assert.Equal(3 + 1 + 3 + 2, Ships.Value["umgah"].Graphics.BattleResources().Count());
    }

    [Fact]
    public void TheBattleNamesItsContent()
    {
        var planets = PlanetTypes.Load(Data);
        var ships = new[] { Ships.Value[ShipDescriptions.Flagship], Ships.Value[ShipDescriptions.Ilwrath] };
        // InitSpace's sheets, the status panel, and 59 planet types and a shielded world at three sizes.
        var arena = BattleResources.Arena(planets).ToList();
        Assert.Equal(2 + 3 * 3 + 60 * 3, arena.Count);
        Assert.Contains("planet.water.large", arena);
        Assert.Contains("planet.slaveshield.small", arena);
        Assert.Contains("graphics.boom.medium", arena);
        Assert.Equal(arena.Count + 8 + 8, BattleResources.All(ships, planets).Count);
        // Contact takes the big size only: the ships' hulls and guns, the arena's four, the 60 planets.
        var contact = BattleResources.ContactSheets(ships, planets);
        Assert.Equal(4 + 4 + 60, contact.Count);
        Assert.Contains("ship.ilwrath.graphics.fire.large", contact);
        Assert.Contains("graphics.stars", contact);
        Assert.DoesNotContain(contact, id => id.EndsWith(".medium") || id.EndsWith(".small"));
    }
}
