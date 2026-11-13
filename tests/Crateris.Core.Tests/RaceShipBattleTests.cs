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
/// Stage 4b's race ships against UQM's own (tests/golden/battle-races-1.json
/// to 3.json, recorded by tools/extract/golden/battle.c with each ship's own
/// UQM code): each ship ported (<see cref="RaceShips.Ported"/>) replayed in
/// its two battles, by keys against the flagship and flown by the AI
/// against the flagship's AI, every frame compared (the elements' colours,
/// the random draws under UQM's names, each side's input) and the crew
/// each side had left when the battle ended.
/// </summary>
[Trait("Topic", "combat")]
public class RaceShipBattleTests
{
    private static readonly Lazy<JsonElement>[] Parts = Enumerable.Range(1, RaceBattleTraceTests.Parts.Length)
        .Select(n => new Lazy<JsonElement>(() => Golden.Load($"battle-races-{n}"))).ToArray();

    /// <summary>Each ported ship's battles, by its part's file.</summary>
    public static IEnumerable<object[]> Battles() =>
        Enumerable.Range(0, RaceBattleTraceTests.Parts.Length).SelectMany(part =>
            BattleReplay.Names($"battle-races-{part + 1}")
                .Select(n => (string)n[0])
                .Where(name => RaceShips.Ported.Contains(name[..name.IndexOf('-')]))
                .Select(name => new object[] { part + 1, name }));

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheBattleIsUqms(int part, string name)
    {
        var reference = Parts[part - 1].Value;
        var recorded = BattleReplay.Recorded(reference, name);
        var input = recorded.GetProperty("input");
        var ai = input.TryGetProperty("ai", out var sides)
            ? sides.EnumerateArray().Select(a => a.GetString() == "awesome" ? CyborgRating.Awesome
                : throw new InvalidDataException($"a rating {a}")).Select(r => (CyborgRating?)r).ToList()
            : null;
        int[] fleetSizes = input.TryGetProperty("fleet", out var fleet) ? fleet.Ints() : new[] { 1, 1 };
        var sis = new List<SisShip>();
        var fleets = input.GetProperty("ships").EnumerateArray().Select((s, side) =>
            (IReadOnlyList<Starship>)Enumerable.Range(0, fleetSizes[side]).Select(_ =>
            {
                var (code, crew) = ShipBattleTests.Ship(s);
                if (code is SisShip flagship)
                    sis.Add(flagship);
                return new Starship(code, (short)side, crew, 0) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) };
            }).ToList()).ToList();

        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean() && !sis.Any(s => s.BombCarrier);
        var battle = BattleReplay.Run(reference, name, fleets, runaway, colours: true, ai);
        ShipBattleTests.CheckFreed(reference, name, battle, sis);
    }
}
