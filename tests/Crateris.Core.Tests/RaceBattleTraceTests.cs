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
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Stage 4b's race ships' traces (tests/golden/battle-races-1.json to 3.json,
/// recorded by tools/extract/golden/battle.c with each ship's own UQM code):
/// each race ship has its two battles in its part's file, a duel against
/// the flagship by keys and one flown by UQM's AI against the flagship's AI,
/// in the shape <see cref="BattleReplay"/> replays, with sheets the port
/// loads. The recorder checks they're deterministic and use each ship's
/// weapon and special; stage 4b's items 2 to 4 match them as each part's
/// ships are ported.
/// </summary>
[Trait("Topic", "combat")]
public class RaceBattleTraceTests
{
    private const int Weapon = 8, Special = 16;

    /// <summary>The plan's three parts of ships (stage 4b items 2, 3 and 4), each in battle-races-N.json.</summary>
    public static readonly string[][] Parts =
    {
        new[] { "arilou", "spathi", "supox", "druuge", "yehat", "zoqfot", "thradd", "umgah" },
        new[] { "utwig", "mycon", "androsyn", "mmrnmhrm", "chenjesu", "slylandr", "urquan", "blackurq" },
        new[] { "human", "chmmr", "shofixti", "syreen", "orz", "melnorme", "vux", "pkunk" },
    };

    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Data);

    private static IEnumerable<(int Part, JsonElement File)> Files() =>
        Enumerable.Range(1, Parts.Length).Select(n => (n, Golden.Load($"battle-races-{n}")));

    [Fact]
    public void ThePartsAreEveryRaceShipButTheIlwrath()
    {
        var all = Parts.SelectMany(p => p).ToList();
        Assert.Equal(24, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(BattleKit.RaceShips(Ships).Select(s => s.Id).Where(id => id != ShipDescriptions.Ilwrath).OrderBy(id => id),
            all.OrderBy(id => id));
    }

    [Fact]
    public void EachRaceShipHasItsTwoBattlesInItsPartsFile()
    {
        foreach (var (n, file) in Files())
        {
            var names = file.GetProperty("battles").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
            var want = Parts[n - 1].SelectMany(s => new[] { $"{s}-keys", $"{s}-ai" }).ToList();
            if (n == 3)
                want.Add("pkunk-rebirth");   // the Pkunk dying and reborn
            Assert.Equal(want, names);
            foreach (var battle in file.GetProperty("battles").EnumerateArray())
            {
                string name = battle.GetProperty("name").GetString()!;
                var input = battle.GetProperty("input");
                var ships = input.GetProperty("ships").EnumerateArray().ToList();
                Assert.Equal(2, ships.Count);
                // The flagship on side 0, the race ship on side 1 with its crew.
                Assert.Equal("sis", ships[0].GetProperty("kind").GetString());
                Assert.Equal("race", ships[1].GetProperty("kind").GetString());
                var race = Ships[ships[1].GetProperty("ship").GetString()!];
                Assert.StartsWith(race.Id + "-", name);
                int crew = ships[1].GetProperty("crew").GetInt32();
                Assert.InRange(crew, 1, race.MaxCrew);
                bool ai = input.TryGetProperty("ai", out var sides);
                Assert.Equal(name.EndsWith("-ai"), ai);
                if (ai)
                    Assert.Equal(new[] { "awesome", "awesome" }, sides.EnumerateArray().Select(s => s.GetString()));
                // The recorded Shofixti is the recruited one (not Tanaka's scout).
                Assert.Equal(race.Id == "shofixti", input.TryGetProperty("shofixti_recruited", out var r) && r.GetBoolean());
            }
        }
    }

    [Fact]
    public void TheTracesAreInTheShapeTheReplayTakes()
    {
        foreach (var (n, file) in Files())
        {
            var sheets = file.GetProperty("sheets").EnumerateArray().Select(s => s.GetString()!).ToList();
            foreach (var battle in file.GetProperty("battles").EnumerateArray())
            {
                string name = battle.GetProperty("name").GetString()!;
                var input = battle.GetProperty("input");
                var race = Ships[input.GetProperty("ships")[1].GetProperty("ship").GetString()!];
                int steps = input.GetProperty("steps").EnumerateArray().Sum(s => s[0].GetInt32());
                var frames = battle.GetProperty("frames").EnumerateArray().ToList();
                int full = battle.GetProperty("full").GetInt32();
                // A frame before the first, then one for each frame run, all
                // the steps' unless the battle ended.
                Assert.True(battle.GetProperty("ended").GetBoolean() ? frames.Count <= steps + 1 : frames.Count == steps + 1,
                    $"{name}: {frames.Count} frames for {steps} steps");
                Assert.Equal(frames.Count, battle.GetProperty("inputs").GetArrayLength());
                // The first frames whole, the rest hashed (BattleReplay.Check).
                Assert.All(frames.Take(full + 1), f => Assert.Equal(10, f.GetArrayLength()));
                Assert.All(frames.Skip(full + 1), f => Assert.Equal(9, f.GetArrayLength()));
                // The race ship warps in flying its own hull (the elements' sheet numbers name the file's sheets).
                var hulls = frames.Take(full + 1).SelectMany(f => f[8].EnumerateArray())
                    .Where(e => (e[0].GetInt32() & (int)ElementFlags.PlayerShip) != 0 && e[21].GetInt32() == 1)
                    .Select(e => sheets[e[19].GetInt32()]).Distinct().ToList();
                Assert.Contains(ShipGraphics.Sheets(race.Graphics.Ship)![0], hulls);
            }
        }
    }

    [Fact]
    public void TheirSheetsAreOnesThePortLoads()
    {
        var planets = PlanetTypes.Load(Data);
        var all = BattleResources.All(BattleKit.Fighters(Ships), planets).ToHashSet();
        foreach (var (n, file) in Files())
            Assert.All(file.GetProperty("sheets").EnumerateArray(), s => Assert.Contains(s.GetString()!, all));
    }

    [Fact]
    public void TheRaceShipUsesItsWeaponAndSpecial()
    {
        // The recorder found the frame each first had an effect (the battle
        // without those presses first differs there: golden_uqm.py's
        // key_effect), never frame 0 or 1 (frame 1's input comes as the ship
        // warps in, to no effect); the race ship pressed it, after frame 1,
        // by then (the weapon without the special; the Orz's both are its marines).
        foreach (var (n, file) in Files())
            foreach (var battle in file.GetProperty("battles").EnumerateArray())
            {
                string name = battle.GetProperty("name").GetString()!;
                if (name == "pkunk-rebirth")
                {
                    Assert.False(battle.TryGetProperty("uses", out _));
                    continue;
                }
                var inputs = battle.GetProperty("inputs").EnumerateArray().Select(i => i[1].GetInt32()).ToList();
                var uses = battle.GetProperty("uses");
                foreach (var (what, key, without) in new[] { ("weapon", Weapon, Special), ("special", Special, 0) })
                {
                    int at = uses.GetProperty(what).GetInt32();
                    Assert.InRange(at, 2, inputs.Count - 1);
                    Assert.True(Enumerable.Range(2, at - 1).Any(t => inputs[t] > 0 && (inputs[t] & key) != 0 && (inputs[t] & without) == 0),
                        $"{name}: its {what} had an effect at frame {at}, but wasn't pressed after frame 1 by then");
                }
            }
    }
}
