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

    // ---- several players (decisions 2 to 5)

    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    // UQM's races by their number.
    private static int Race(string race) => Rules.RaceIndex(race);

    // A world of players in hyperspace (no systems: nothing to touch), each
    // at a universe place, every race kept (decision 2 tested on its own).
    private static GameWorld Players(GameSettings? settings = null, params (string Id, short X, short Y)[] players)
    {
        var clock = new GameClock(2155, 2, 17);
        var world = new GameWorld(settings ?? new GameSettings(), clock) { Story = StoryState.NewGame(Rules, clock) };
        world.Story!.Set("STARBASE_AVAILABLE", 1);   // no daily probe
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.EncounterRaces = Rules.Races.ToHashSet();
        world.TraceHyperspaceRandom = true;
        uint n = 0;
        var seeds = new Dictionary<string, uint>();
        foreach (var (id, x, y) in players)
        {
            seeds[id] = 1000 + ++n * 7919;
            var ship = n == 1 ? new PlayerShip(ShipKind.Flagship, NewGameData.Load(DataDir).Flagship)
                : new PlayerShip(ShipKind.Escort, world.Settings.EscortLoadout);
            ship.Flight.PlaceAtUniverse(x, y);
            world.AddPlayer(new Player(id, id, n == 1, ship) { Present = true });
        }
        world.HyperspaceSeeds = id => seeds[id];
        return world;
    }

    private static void Frames(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
            world.Step();
    }

    // A group of a race at a place, its element not yet made (as one kept from before).
    private static HyperEncounter Group(GameWorld world, string race, short x, short y, string roller, int ships = 2)
    {
        var fleet = world.Story!.Fleet(race);
        var e = new HyperEncounter
        {
            Race = Race(race), OriginX = (short)fleet.LocX, OriginY = (short)fleet.LocY,
            Radius = (ushort)((fleet.ActualStrength * Rules.SphereRadiusIncrement) >> 1),
            LocX = x, LocY = y, LogX = Uqm.Units.UniverseToLogX(x), LogY = Uqm.Units.UniverseToLogY(y), Roller = roller,
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    private static int Distance(HyperEncounter e, Player p) =>
        Math.Abs(e.LocX - p.Ship.Flight.UniverseX) + Math.Abs(e.LocY - p.Ship.Flight.UniverseY);

    [Fact]
    public void EachPlayerInHyperspaceRollsWithTheirOwnNumbersAndTheirGroupsComeInRoundThem()
    {
        // Two players in the Ur-Quan's and the Kohr-Ah's spheres, far apart; a third away.
        var world = Players(null, ("a", 5800, 6100), ("b", 6800, 7200), ("c", 5900, 6200));
        world.Find("c")!.Present = false;
        world.EndOfFrame();
        for (int day = 0; day < 20 && world.Encounters!.List.Select(e => e.Roller).Distinct().Count() < 2; ++day)
            world.MoveClockDays(1);
        var list = world.Encounters!.List;
        Assert.Contains(list, e => e.Roller == "a");
        Assert.Contains(list, e => e.Roller == "b");
        Assert.DoesNotContain(list, e => e.Roller == "c");
        // Each drew on their own numbers: a roll for each race whose sphere
        // holds them (the Ur-Quan, the Kohr-Ah, and the races met anywhere).
        var rolls = world.HyperspaceRandomOf("a").Places!.Count(p => p == "check_hyperspace_encounter");
        Assert.Equal(rolls, world.HyperspaceRandomOf("b").Places!.Count(p => p == "check_hyperspace_encounter"));
        Assert.True(rolls >= world.HyperspaceEncounterDays * 3, $"{rolls} rolls in {world.HyperspaceEncounterDays} days");
        Assert.DoesNotContain(world.HyperspaceRandomOf("c").Places!, p => p == "check_hyperspace_encounter");
        // Their elements made: each round the player whose roll made it.
        world.Step();
        foreach (var e in world.Encounters.List)
        {
            var p = world.Find(e.Roller)!;
            Assert.True(HyperEncounters.InSpan(e, p.Ship.Flight.UniverseX, p.Ship.Flight.UniverseY), $"{e.Roller}'s group isn't round them");
            Assert.True(e.Ships.Count is >= 1 and <= HyperEncounter.MaxShips);
        }
        Assert.Contains("AddEncounterElement", world.HyperspaceRandomOf("a").Places!);
    }

    [Fact]
    public void TheListsCapIsASetting()
    {
        var world = Players(new GameSettings { MaxHyperspaceEncounters = 3 }, ("a", 5800, 6100), ("b", 6000, 6300));
        world.EncounterRaces = new HashSet<string>();   // not made yet: rolled only
        for (int day = 0; day < 30; ++day)
            world.MoveClockDays(1);
        Assert.Equal(3, world.Encounters!.List.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameWorld(new GameSettings { MaxHyperspaceEncounters = 0 }, new GameClock(2155, 2, 17)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameWorld(
            new GameSettings { MaxHyperspaceEncounters = GameSettings.MostHyperspaceEncounters + 1 }, new GameClock(2155, 2, 17)));
    }

    [Fact]
    public void AGroupOfARaceWhoseTalkIsntPortedIsMadeAsUqmsThenDropped()
    {
        var world = Players(null, ("a", 5800, 6100));
        world.EncounterRaces = new HashSet<string> { "SPATHI_SHIP" };
        world.EndOfFrame();
        while (world.Encounters!.List.Count == 0)
            world.MoveClockDays(1);
        var random = world.HyperspaceRandomOf("a");
        int before = random.Places!.Count;
        world.Step();
        // Its makeup and place drawn (AddEncounterElement), then dropped.
        Assert.Empty(world.Encounters.List);
        Assert.Contains("AddEncounterElement", random.Places.Skip(before));
        // The game's own: no race's talk ported yet (items 6 to 8 add them).
        Assert.Empty(new GameWorld(new GameSettings(), new GameClock(2155, 2, 17)).EncounterRaces);
    }

    [Fact]
    public void AGroupFliesAtTheNearestPlayerInHyperspace()
    {
        var world = Players(null, ("a", 5600, 6000), ("b", 6100, 6000));
        var e = Group(world, "URQUAN_SHIP", 5900, 6000, "a");
        Frames(world, 60);
        Assert.True(e.LocX > 5930, $"at {e.LocX}: not flying at b, the nearer");
        // b gone into a talk (away): it turns on a.
        world.Find("b")!.Present = false;
        int x = e.LocX;
        Frames(world, 200);
        Assert.True(e.LocX < x - 50, $"at {e.LocX}: not flying at a");
    }

    [Fact]
    public void AGroupGoesOutOfEverySpanButIsKeptForAPlayerWhoLeftWithItInTheirs()
    {
        var world = Players(null, ("a", 5600, 6000), ("b", 7000, 6000));
        var near = Group(world, "URQUAN_SHIP", 5650, 6020, "a");
        var far = Group(world, "URQUAN_SHIP", 3000, 6000, "a");
        world.Step();
        Assert.DoesNotContain(far, world.Encounters!.List);   // out of both spans
        Assert.NotNull(near.Element);
        // a leaves hyperspace (away): the group is kept for a, from where a left.
        var a = world.Find("a")!;
        a.Present = false;
        world.Step();
        Assert.Equal((a.Ship.Flight.UniverseX, a.Ship.Flight.UniverseY), near.KeptFor["a"]);
        // It flies at b, far off, and goes once out of a's span too.
        for (int i = 0; i < 2000 && world.Encounters.List.Contains(near); ++i)
            world.Step();
        Assert.DoesNotContain(near, world.Encounters.List);
        Assert.True(Distance(near, a) > HyperEncounters.SpanX, $"gone {Distance(near, a)} from a");
        Assert.True(Distance(near, world.Find("b")!) > HyperEncounters.SpanX);
    }

    [Fact]
    public void WithNobodyInHyperspaceTheGroupsWaitAndComeBackWhereTheyWereWithoutAVortex()
    {
        var world = Players(null, ("a", 5600, 6000));
        var e = Group(world, "URQUAN_SHIP", 5700, 6050, "a");
        Frames(world, 5);
        var a = world.Find("a")!;
        a.Present = false;
        world.Step();
        Assert.Null(e.Element);   // cleanup_hyperspace
        var (x, y) = (e.LocX, e.LocY);
        Frames(world, 100);
        Assert.Equal((x, y), (e.LocX, e.LocY));
        Assert.Contains(e, world.Encounters!.List);
        // Back: placed where it was, as a globe (no vortex), and checked.
        a.Present = true;
        world.Step();
        Assert.Equal((false, world.Encounters.Frames - 1), (e.Element!.InVortex, e.Element.Frame));
        Assert.DoesNotContain("a", e.KeptFor.Keys);
        // One kept for a player who comes back far from it goes.
        a.Present = false;
        world.Step();
        a.Ship.Flight.PlaceAtUniverse(2000, 2000);
        a.Present = true;
        world.Step();
        Assert.DoesNotContain(e, world.Encounters.List);
    }

    [Fact]
    public void TheProbesAreOneAtATimeForEachPlayer()
    {
        // Out of every sphere but the races met anywhere.
        var world = Players(null, ("a", 9000, 300), ("b", 9500, 800));
        world.Story!.Set("STARBASE_AVAILABLE", 0);   // a probe every day
        world.EncounterRaces = new HashSet<string>();
        for (int day = 0; day < 5; ++day)
            world.MoveClockDays(1);
        int probe = Race("SLYLANDRO_SHIP");
        Assert.Equal(new[] { "a", "b" }, world.Encounters!.List.Where(e => e.Race == probe).Select(e => e.Roller).OrderBy(r => r));
        Assert.All(world.Encounters.List, e => Assert.Equal(HyperEncounter.OneShot, e.Flags));
    }

    [ContentFact]
    public void TheGroupATouchingPlayerMeetsIsTheirsAlone()
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, world.Join("g", "Guest"));
        world.UndockShown("g");
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.Story.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        var host = world.Host.Ship.Flight;
        host.PlaceAtUniverse(5800, 6100);
        host.StarJustLeft = null;
        var guest = world.Find("g")!.Ship.Flight;
        guest.PlaceAtUniverse(6600, 6600);
        guest.StarJustLeft = null;
        var met = Group(world, "URQUAN_SHIP", 5810, 6100, "h");
        var other = Group(world, "BLACK_URQUAN_SHIP", 6650, 6700, "g");
        for (int i = 0; i < 400 && world.HyperspaceMeetings.Count == 0; ++i)
        {
            foreach (var p in world.Players)
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
        Assert.Equal(("h", Race("URQUAN_SHIP")), (world.HyperspaceMeetings.Single().Player, world.HyperspaceMeetings.Single().Race));
        // Until item 5's meeting it ends at once, no battle: the group
        // re-forms one-shot, and goes as its element is next made (the
        // same frame's: AddEncounterElement took its re-forming flag).
        Assert.Equal((HyperEncounter.OneShot, (short)100), (met.Flags, met.TransitionState));
        Assert.DoesNotContain(met, world.Encounters!.List);
        Assert.Contains(other, world.Encounters.List);
        Assert.Null(other.Element!.Met);
    }

    [ContentFact]
    public void TheEncountersAreSavedAndThePlayersInHyperspaceKeepThoseRoundThem()
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        var host = world.Host.Ship.Flight;
        host.PlaceAtUniverse(5800, 6100);
        host.StarJustLeft = null;
        var e = Group(world, "URQUAN_SHIP", 5900, 6150, "h", ships: 3);
        e.KeptFor["h"] = (1, 2);
        e.Flags = HyperEncounter.OneShot;
        e.TransitionState = -3;
        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, systems, TestContent.Shapes);
        var l = loaded.Encounters!.List.Single();
        Assert.Equal((e.Race, e.Flags, e.OriginX, e.OriginY, e.Radius, e.LocX, e.LocY, e.LogX, e.LogY, e.TransitionState, e.Roller),
            (l.Race, l.Flags, l.OriginX, l.OriginY, l.Radius, l.LocX, l.LocY, l.LogX, l.LogY, l.TransitionState, l.Roller));
        Assert.Equal(e.Ships, l.Ships);
        Assert.Equal(e.KeptFor, l.KeptFor);
        Assert.Null(l.Element);
        // Loaded, everyone's away: the host, in hyperspace as it was saved,
        // keeps the groups in its span, from where it was.
        loaded.Encounters.List[0].KeptFor.Clear();
        loaded.EndOfFrame();
        Assert.Equal((host.UniverseX, host.UniverseY), loaded.Encounters.List[0].KeptFor["h"]);

        // None: left out, and a save without them has none.
        world.Encounters!.List.Clear();
        var none = System.Text.Json.Nodes.JsonNode.Parse(HostSave.ToJson(world))!;
        Assert.Null(none["encounters"]);
        Assert.Empty(HostSave.FromJson(none.ToJsonString(), systems, TestContent.Shapes).Encounters!.List);
        // An older save with them, or a bad one, is refused.
        void Refused(Action<System.Text.Json.Nodes.JsonNode> change, string why)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            change(node);
            Assert.Contains(why, Assert.Throws<InvalidDataException>(
                () => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes)).Message);
        }
        System.Text.Json.Nodes.JsonNode First(System.Text.Json.Nodes.JsonNode n) => n["encounters"]![0]!;
        Refused(n =>
        {
            n["version"] = 12;
            n["story"]!.AsObject().Remove("fleets");
            n["story"]!.AsObject().Remove("set_aside");
        }, "encounters");
        Refused(n => First(n)["race"] = 99, "race 99");
        Refused(n => First(n)["flags"] = 1, "flags");
        Refused(n => First(n)["roller"] = "nobody", "roller");
        Refused(n => First(n)["ships"] = new System.Text.Json.Nodes.JsonArray(1, 2, 3, 4, 5, 6, 7, 8), "ships");
        Refused(n => First(n)["loc_x"] = 40000, "COORD");
        Refused(n => First(n)["kept_for"]!.AsArray().Add(First(n)["kept_for"]![0]!.DeepClone()), "twice");
        Refused(n => First(n)["kept_for"]![0]!["player"] = "nobody", "kept for");
        Refused(n => n["settings"]!["max_hyperspace_encounters"] = 0, "encounters");
    }
}
