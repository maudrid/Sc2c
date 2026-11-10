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
/// The AI (<see cref="Battle.TacticalIntelligence"/>, cyborg.c and
/// intel.c; the Ilwrath's and the flagship's own, ilwrath_intelligence and
/// sis_intelligence) against UQM's own (tests/golden/battle-ai.json,
/// recorded by tools/extract/golden/battle.c running UQM's intel.c,
/// cyborg.c, ilwrath.c and sis_ship.c): the Ilwrath flown by the AI
/// against a scripted flagship, at Earth (no cloak,
/// <see cref="IlwrathShip.NoCloak"/>) and elsewhere, at each rating; the
/// flagship flown by the AI; each battle replayed with every side's input
/// asked in ProcessInput's order, and every frame and each side's input
/// compared, the AI's random draws among them.
/// </summary>
[Trait("Topic", "combat")]
public class BattleAiTests
{
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle-ai"));

    public static IEnumerable<object[]> Battles() => BattleReplay.Names("battle-ai");

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheAiIsUqms(string name)
    {
        var recorded = BattleReplay.Recorded(Reference.Value, name);
        var input = recorded.GetProperty("input");
        bool earth = input.TryGetProperty("earth", out var e) && e.GetBoolean();
        var ai = input.GetProperty("ai").EnumerateArray().Select(a => a.ValueKind == JsonValueKind.Null
            ? (CyborgRating?)null
            : a.GetString() switch
            {
                "standard" => CyborgRating.Standard,
                "good" => CyborgRating.Good,
                "awesome" => CyborgRating.Awesome,
                var other => throw new InvalidDataException($"a rating {other}"),
            }).ToList();
        Assert.Contains(ai, a => a != null);

        var ships = input.GetProperty("ships").EnumerateArray().ToList();
        var sis = new List<SisShip>();
        var fleets = ships.Select((s, side) =>
        {
            var (code, crew) = ShipBattleTests.Ship(s, noCloak: earth);
            if (code is SisShip flagship)
                sis.Add(flagship);
            return (IReadOnlyList<Starship>)new[]
            {
                new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) },
            };
        }).ToList();

        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean() && !sis.Any(s => s.BombCarrier);
        var battle = BattleReplay.Run(Reference.Value, name, fleets, runaway, colours: true, ai);
        // No crew floats at the end (UninitShips' handing it out is item 6's).
        Assert.DoesNotContain(battle.List.Elements, x => (x.StateFlags & ElementFlags.CrewObject) != 0);
        ShipBattleTests.CheckFreed(recorded, fleets, sis);
    }
}
