// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Encounters in hyperspace (docs/plans/stage-3b.md, item 3), against
/// UQM's own for one player (tests/golden/hyperspace-encounters.json):
/// the daily rolls, the groups coming in through their vortices, flying
/// at the flagship, leaving their spheres or the flagship's span, and the
/// one it touches, frame by frame, with every random number in UQM's
/// order.
/// </summary>
[Trait("Topic", "hyperspace")]
public class HyperEncounterTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly Lazy<SystemCatalog> Systems = new(() => SystemCatalog.Load(DataDir));

    // A record's fields (golden/hyperspace.c).
    private const int FLogX = 0, FLogY = 1, FFacing = 2, FMet = 4, FDate = 5, FDraws = 6, FSeed = 7, FEncounters = 8;

    // UQM's ELEMENT state flag NONSOLID, and the stamp primitive (an element drawn).
    private const int NonsolidFlag = 1 << 6, StampPrim = 1;

    internal static GameWorld UqmScenario(JsonElement input, out uint startSeed, JsonElement scenario)
    {
        var systems = Systems.Value;
        int dayTicks = input.GetProperty("day_ticks").GetInt32();
        var settings = new GameSettings { SecondsPerGameDay = dayTicks / 24.0 };
        var data = NewGameData.Load(DataDir, systems.Map);
        var world = GameWorld.NewGame(settings, data, "h", "Host", systems, TestContent.Shapes);
        var story = world.Story!;
        world.EncounterRaces = story.Rules.Races.ToHashSet();
        uint seed = startSeed = scenario.GetProperty("start_seed").GetUInt32();
        world.HyperspaceSeeds = _ => seed;
        world.TraceHyperspaceRandom = true;
        story.Set("STARBASE_AVAILABLE", input.GetProperty("starbase").GetInt32());
        story.Set("SLYLANDRO_MULTIPLIER", input.GetProperty("multiplier").GetInt32());
        story.Set("MELNORME_ANGER", input.GetProperty("anger").GetInt32());
        foreach (var f in input.GetProperty("fleets").EnumerateArray())
        {
            var v = f.Ints();
            string race = story.Rules.Races[v[0]];
            story.SetFleet(race, story.Fleet(race) with { ActualStrength = v[1], LocX = v[2], LocY = v[3] });
        }
        world.Clock.FramesIntoDay = dayTicks - input.GetProperty("tick_count").GetInt32();
        var flight = world.Host.Ship.Flight;
        flight.PlaceAtUniverse((short)input.GetProperty("x").GetInt32(), (short)input.GetProperty("y").GetInt32());
        flight.Facing = (ushort)input.GetProperty("facing").GetInt32();
        flight.Fuel = input.GetProperty("fuel").GetUInt32();
        flight.StarJustLeft = null;
        flight.Drive = ShipDrive.ForHyperspace(input.GetProperty("thrusters").GetInt32(), 2);
        var encounters = world.Encounters!;
        foreach (var e in input.GetProperty("encounters").EnumerateArray())
        {
            var v = e.Ints();
            var enc = new HyperEncounter
            {
                Race = v[0], Flags = (byte)v[1], OriginX = (short)v[3], OriginY = (short)v[4], Radius = (ushort)v[5],
                LocX = (short)v[6], LocY = (short)v[7], LogX = Uqm.Units.UniverseToLogX((short)v[6]),
                LogY = Uqm.Units.UniverseToLogY((short)v[7]), TransitionState = (short)v[8], Roller = "h",
            };
            for (int i = 0; i < v[2]; ++i)
                enc.Ships.Add(story.Rules.RaceShips[v[0]].Crew);
            encounters.List.Add(enc);
        }
        return world;
    }

    private static int KeysAt(JsonElement input, int frame)
    {
        int at = 0;
        foreach (var step in input.GetProperty("steps").EnumerateArray())
        {
            var s = step.Ints();
            if (frame < at + s[0])
                return s[1];
            at += s[0];
        }
        return 0;
    }

    // The port's state against a record of UQM's.
    private static void Match(GameWorld world, JsonElement rec, string where, ref int drawn)
    {
        var f = world.Host.Ship.Flight;
        Assert.True((f.LogX, f.LogY, (int)f.Facing) == (rec[FLogX].GetInt32(), rec[FLogY].GetInt32(), rec[FFacing].GetInt32()),
            $"{where}: the flagship at {(f.LogX, f.LogY, f.Facing)}");
        var date = rec[FDate].Ints();
        Assert.True((world.Clock.Year, world.Clock.Month, world.Clock.Day) == (date[0], date[1], date[2]), $"{where}: the date");
        var random = world.HyperspaceRandomOf("h");
        var places = rec[FDraws].EnumerateArray().Select(p => p.GetString()!)
            .Where(p => p is not ("InitGalaxy" or "spawn_ship")).ToList();
        Assert.True(places.SequenceEqual(random.Places!.Skip(drawn)),
            $"{where}: drew {string.Join(",", random.Places!.Skip(drawn))}, UQM {string.Join(",", places)}");
        drawn = random.Places!.Count;
        Assert.True(random.Seed == rec[FSeed].GetUInt32(), $"{where}: the seed");
        var list = world.Encounters!.List;
        var uqm = rec[FEncounters].EnumerateArray().ToList();
        Assert.True(uqm.Count == list.Count, $"{where}: {list.Count} encounters, UQM {uqm.Count}");
        for (int i = 0; i < uqm.Count; ++i)
        {
            var u = uqm[i];
            var e = list[i];
            string at = $"{where}, encounter {i}";
            Assert.True((e.Race, e.Flags, e.OriginX, e.OriginY, e.Radius) ==
                        (u[0].GetInt32(), (byte)u[1].GetInt32(), (short)u[4].GetInt32(), (short)u[5].GetInt32(), (ushort)u[6].GetInt32()),
                $"{at}: race, flags, sphere");
            Assert.True(e.Ships.SequenceEqual(u[3].Ints()), $"{at}: ships");
            Assert.True((e.LocX, e.LocY, e.LogX, e.LogY, e.TransitionState) ==
                        ((short)u[7].GetInt32(), (short)u[8].GetInt32(), u[9].GetInt32(), u[10].GetInt32(), (short)u[11].GetInt32()),
                $"{at}: at {(e.LocX, e.LocY, e.LogX, e.LogY, e.TransitionState)}");
            var ue = u[12];
            Assert.True((ue.ValueKind == JsonValueKind.Null) == (e.Element is null), $"{at}: element");
            if (e.Element is not { } el)
                continue;
            var v = ue.Ints();
            Assert.True(((v[0] & NonsolidFlag) != 0, v[1], v[2], v[3], v[4], v[5] != 0, v[6] != 0, v[16] != 0, v[21] == StampPrim) ==
                        (el.Nonsolid, el.LifeSpan, el.Frame, el.TurnWait, el.ThrustWait, el.VortexOut, el.InVortex, el.Met is not null, el.Drawn),
                $"{at}: element {string.Join(",", v)}");
            Assert.True(v[7..16].SequenceEqual(el.Velocity.ToArray()), $"{at}: velocity");
            if ((v[0] & NonsolidFlag) == 0)
            {
                // Where it's tested from and to, in the view round the flagship.
                int cx = HyperFlight.CenterX - f.LogX, cy = HyperFlight.CenterY - f.LogY;
                Assert.True((el.CurX + cx, el.CurY + cy, el.NextX + cx, el.NextY + cy) == (v[17], v[18], v[19], v[20]),
                    $"{at}: tested from {(el.CurX + cx, el.CurY + cy)} to {(el.NextX + cx, el.NextY + cy)}");
            }
        }
    }

    [ContentFact]
    public void EncountersFlyAsUqmsForOnePlayer()
    {
        var golden = Golden.Load("hyperspace-encounters");
        foreach (var scenario in golden.GetProperty("scenarios").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var input = scenario.GetProperty("input");
            var world = UqmScenario(input, out _, scenario);
            var frames = scenario.GetProperty("frames").EnumerateArray().ToList();
            int drawn = 0;
            world.EndOfFrame();
            Match(world, frames[0], $"{name}, frame 1", ref drawn);
            for (int k = 1; k < frames.Count; ++k)
            {
                // The flagship's first frame, as it appears, takes no keys (ship_preprocess).
                world.SubmitInput("h", world.Frame + 1, k == 1 ? ShipInput.None : (ShipInput)KeysAt(input, k - 1));
                world.Step();
                if (frames[k][FMet].GetInt32() != 0)
                {
                    // UQM's flagship met a group in that frame's flight: the
                    // port's met the same one.
                    var met = frames[k][FEncounters].EnumerateArray().Single(e => e[12].ValueKind != JsonValueKind.Null && e[12][16].GetInt32() != 0);
                    Assert.True(world.HyperspaceMeetings.Count == 1, $"{name}, frame {k + 1}: no meeting");
                    Assert.Equal(("h", met[0].GetInt32()), (world.HyperspaceMeetings[0].Player, world.HyperspaceMeetings[0].Race));
                    break;
                }
                Assert.True(world.HyperspaceMeetings.Count == 0, $"{name}, frame {k + 1}: met too soon");
                Match(world, frames[k], $"{name}, frame {k + 1}", ref drawn);
            }
        }
    }
}
