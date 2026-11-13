// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;

namespace Crateris.Core.Tests;

/// <summary>
/// The flagship (<see cref="SisShip"/>, sis_ship.c) built from several
/// layouts, and the Ilwrath (<see cref="IlwrathShip"/>, ilwrath.c), against
/// UQM's own (tests/golden/battle-ships.json, recorded by
/// tools/extract/golden/battle.c with UQM's sis_ship.c and ilwrath.c):
/// each battle replayed from its seed and keys and every frame compared,
/// each element's colour too (the Ilwrath's cloak); and the crew the
/// flagship had left when it was freed, against what uninit_sis wrote.
/// </summary>
[Trait("Topic", "combat")]
public class ShipBattleTests
{
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle-ships"));

    public static IEnumerable<object[]> Battles() => BattleReplay.Names("battle-ships");

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheBattleIsUqms(string name)
    {
        var recorded = BattleReplay.Recorded(Reference.Value, name);
        var input = recorded.GetProperty("input");
        var ships = input.GetProperty("ships").EnumerateArray().ToList();
        int[] fleetSizes = input.TryGetProperty("fleet", out var fleet) ? fleet.Ints() : new[] { 1, 1 };
        var sis = new List<SisShip>();
        var fleets = ships.Select((s, side) => (IReadOnlyList<Starship>)Enumerable.Range(0, fleetSizes[side]).Select(_ =>
        {
            var (code, crew) = Ship(s);
            if (code is SisShip flagship)
                sis.Add(flagship);
            return new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) };
        }).ToList()).ToList();

        // RunAwayAllowed: the starbase available, and not the bomb's carrier (init_sis's BOMB_CARRIER).
        bool bombCarrier = sis.Any(s => s.BombCarrier);
        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean() && !bombCarrier;
        var battle = BattleReplay.Run(Reference.Value, name, fleets, runaway, colours: true);
        Assert.Equal(recorded.GetProperty("bomb_carrier").GetInt32() != 0, bombCarrier);
        CheckFreed(Reference.Value, name, battle, sis);
    }

    /// <summary>
    /// The battle's end, UninitShips (the crew still floating to the ship
    /// left with crew, then the ships still in the battle freed, as UQM's
    /// Battle ends: <see cref="BattleReplay.End"/>, each ship's crew against
    /// UQM's), and the flagship's crew left against what uninit_sis wrote:
    /// the recording program's CrewEnlisted is UQM's after its UninitShips,
    /// floating crew taken.
    /// </summary>
    internal static void CheckFreed(JsonElement reference, string name, Battle battle, IReadOnlyList<SisShip> sis)
    {
        BattleReplay.End(reference, name, battle);
        var recorded = BattleReplay.Recorded(reference, name);

        // uninit_sis's CrewEnlisted: the crew left, ~0 when it died (UQM's
        // game over); the port gives the crew left and whether it died.
        if (sis.Count == 0)
            return;
        int crewEnlisted = recorded.GetProperty("crew_enlisted").GetInt32();
        var freed = Assert.Single(sis).Result;
        Assert.NotNull(freed);
        Assert.Equal(crewEnlisted == 0xFFFF, freed.Value.Destroyed);
        Assert.Equal(freed.Value.Destroyed ? 0 : crewEnlisted, freed.Value.CrewLeft);
    }

    /// <summary>
    /// A recorded ship: the flagship from its slots and crew, or the Ilwrath
    /// (its AI not cloaking with <paramref name="noCloak"/>) or a race ship
    /// (<see cref="RaceShips.Code"/>) with its crew;
    /// and the crew its STARSHIP comes with (0: the flagship's own).
    /// </summary>
    internal static (IShipCode Code, ushort Crew) Ship(JsonElement s, bool noCloak = false)
    {
        var description = BattleReplay.Ships[s.GetProperty("ship").GetString()!];
        var hull = BattleReplay.ViewsOf(description.Graphics.Ship);
        var weapon = BattleReplay.ViewsOf(description.Graphics.Weapon);
        int crew = s.GetProperty("crew").GetInt32();
        switch (s.GetProperty("kind").GetString())
        {
            case "sis":
                static byte[] Slots(JsonElement e) => e.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
                var loadout = new SisLoadout(Slots(s.GetProperty("drives")), Slots(s.GetProperty("jets")),
                    Slots(s.GetProperty("modules")), crew, carriesBomb: s.GetProperty("bomb").GetInt32() == 3);
                return (new SisShip(description, hull, weapon, loadout), 0);
            case "ilwrath":
                return (new IlwrathShip(description, hull, weapon, noCloak), (ushort)crew);
            case "race":
                // Stage 4b's race ships (battle-races-*.json), as the world makes them.
                return (RaceShips.Code(description, r => BattleReplay.Sheets.Value.Views(r), noCloak), (ushort)crew);
            default:
                throw new InvalidDataException($"a ship of kind {s.GetProperty("kind")}");
        }
    }
}
