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

    // UQM's START_INTERPLANETARY: CurrentActivity's after a run that went into a star.
    private const int StartInterplanetary = 1 << 11;

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

    private static int KeysAt(JsonElement steps, int frame)
    {
        int at = 0;
        foreach (var step in steps.EnumerateArray())
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
        // Spinning into a star, the flagship is where its spin has taken it.
        var f = world.Host.Ship.Spin ?? world.Host.Ship.Flight;
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
        int followed = 0;
        foreach (var scenario in golden.GetProperty("scenarios").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var input = scenario.GetProperty("input");
            var world = UqmScenario(input, out _, scenario);
            world.BattleKit = WorldBattleTests.Kit();
            var frames = scenario.GetProperty("frames").EnumerateArray().ToList();
            int drawn = 0;
            world.EndOfFrame();
            Match(world, frames[0], $"{name}, frame 1", ref drawn);
            var end = Run(world, input.GetProperty("steps"), frames, name, 1, ref drawn);
            if ((scenario.GetProperty("end")[1].GetInt32() & StartInterplanetary) != 0)
                Assert.True(end == RunEnd.IntoAStar, $"{name}: UQM's flagship went into a star, the port's didn't");
            if (end != RunEnd.Met)
                continue;
            // The spin's last frame (unhyper_transition's cleanup_hyperspace):
            // out of hyperspace, the group met first in the list, the
            // meeting at its encounter screen (no race's talk ported yet).
            // UQM's clock stops from here (the encounter); the port's runs
            // (docs/differences.md row 1): put back below.
            var clock = (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay);
            world.Step();
            var meeting = world.MeetingOf("h")!;
            Assert.Equal(MeetingStep.Encounter, meeting.Step);
            Assert.Null(world.Host.Ship.Spin);
            Assert.Same(meeting.Encounter, world.Encounters!.List[0]);
            Assert.All(world.Encounters.List, e => Assert.Null(e.Element));
            if (!input.TryGetProperty("after", out var after))
                continue;
            ++followed;
            // RaceCommunication's end, as the scenario has it: a talk
            // without a battle (a hail, the race's talk not ported), or a
            // battle (an attack) the player retreats from, the group's ships
            // left with the crews given.
            var crews = after.GetProperty("crews").Ints();
            // Back in hyperspace: Battle seeds the numbers afresh.
            uint seed = scenario.GetProperty("after_start_seed").GetUInt32();
            world.HyperspaceSeeds = _ => seed;
            var afterFrames = scenario.GetProperty("after").EnumerateArray().ToList();
            drawn = 0;
            int first = 0;
            if (after.GetProperty("status").GetInt32() == 0)
            {
                Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
                (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay) = clock;
            }
            else
            {
                // No day ends in the battle (its events would run, as UQM's
                // stopped clock doesn't).
                world.Clock.FramesIntoDay = -100000;
                Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
                var wb = meeting.Battle!;
                for (int i = 0; i < crews.Length; ++i)
                    wb.Enemies[i].Starship.CrewLevel = (ushort)crews[i];
                for (int i = 0; i < 400 && meeting.Step == MeetingStep.Battle; ++i)
                {
                    world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
                    world.Step();
                }
                // The battle's last frame was hyperspace's first back (the
                // player out of the battle, its frame's encounters flown):
                // the clock as UQM's, a frame on from the meeting's.
                Assert.True(clock.FramesIntoDay + 1 < input.GetProperty("day_ticks").GetInt32(), $"{name}: a day ends as it's back");
                (world.Clock.Year, world.Clock.Month, world.Clock.Day, world.Clock.FramesIntoDay) =
                    (clock.Year, clock.Month, clock.Day, clock.FramesIntoDay + 1);
                Match(world, afterFrames[0], $"{name}, back, frame 1", ref drawn);
                first = 1;
            }
            Assert.Equal(MeetingStep.Done, meeting.Step);
            Assert.Equal(crews, meeting.Left!.Select(l => l.Crew));
            Run(world, after.GetProperty("steps"), afterFrames, $"{name}, back", first, ref drawn);
        }
        Assert.Equal(2, followed);
    }

    private enum RunEnd { Done, Met, IntoAStar }

    // The port's frames against UQM's from frames[first] (each a world
    // frame with the steps' keys), through a meeting's spin, or into a star.
    private static RunEnd Run(GameWorld world, JsonElement steps, List<JsonElement> frames, string name, int first,
        ref int drawn)
    {
        int meetings = world.HyperspaceMeetings.Count;
        bool met = false;
        for (int k = first; k < frames.Count; ++k)
        {
            // The flagship's first frame, as it appears, takes no keys (ship_preprocess).
            world.SubmitInput("h", world.Frame + 1, k == 1 ? ShipInput.None : (ShipInput)KeysAt(steps, k - 1));
            world.Step();
            string where = $"{name}, frame {k + 1}";
            bool meeting = frames[k][FEncounters].EnumerateArray().Any(e => e[12].ValueKind != JsonValueKind.Null && e[12][16].GetInt32() != 0);
            if (frames[k][FMet].GetInt32() == 1 && !meeting)
            {
                // Spinning into a star: still in hyperspace for the
                // encounters until the spin's last frame (IN_BATTLE).
                Assert.True(world.Host.Ship.Spin is not null && world.Host.Ship.Entry is not null, $"{where}: not spinning");
                Match(world, frames[k], where, ref drawn);
                if (k == frames.Count - 1)
                {
                    // The spin's last frame (cleanup_hyperspace): out of
                    // hyperspace, in the star's system, the groups kept
                    // without elements.
                    world.Step();
                    Assert.Null(world.Host.Ship.Spin);
                    Assert.Equal(Activity.OuterSystem, world.Host.Ship.Activity);
                    Assert.Equal(frames[k][FEncounters].GetArrayLength(), world.Encounters!.List.Count);
                    Assert.All(world.Encounters.List, e => Assert.Null(e.Element));
                    return RunEnd.IntoAStar;
                }
                continue;
            }
            if (frames[k][FMet].GetInt32() == 1)
            {
                // UQM's flagship met a group in that frame's flight (or
                // spins into it since): the port's met the same one, and
                // spins into it, still in hyperspace for the encounters.
                var m = frames[k][FEncounters].EnumerateArray().Single(e => e[12].ValueKind != JsonValueKind.Null && e[12][16].GetInt32() != 0);
                Assert.True(world.HyperspaceMeetings.Count == meetings + 1, $"{where}: no meeting");
                Assert.Equal(("h", m[0].GetInt32()), (world.HyperspaceMeetings[^1].Player, world.HyperspaceMeetings[^1].Race));
                Assert.True(world.Host.Ship.Meeting is { Step: MeetingStep.Spin } && world.Host.Ship.Spin is not null, $"{where}: not spinning");
                Match(world, frames[k], where, ref drawn);
                met = true;
                continue;
            }
            Assert.True(!met && world.HyperspaceMeetings.Count == meetings, $"{where}: met too soon, or the spin ended early");
            // Spinning back in after a meeting (hyper_transition: its first
            // frame, APPEARING, sets it going).
            if (k > 0)
                Assert.True((frames[k][FMet].GetInt32() == 2) == (world.Host.Ship.AppearFrames > 0),
                    $"{where}: spinning back in {world.Host.Ship.AppearFrames}");
            Match(world, frames[k], where, ref drawn);
        }
        return met ? RunEnd.Met : RunEnd.Done;
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
        world.EndOfFrame();
        var numbers = world.HyperspaceRandomOf("h");
        for (int i = 0; i < 400 && world.HyperspaceMeetings.Count == 0; ++i)
        {
            foreach (var p in world.Players)
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
        Assert.Equal(("h", Race("URQUAN_SHIP")), (world.HyperspaceMeetings.Single().Player, world.HyperspaceMeetings.Single().Race));
        // The player's numbers made afresh, as Battle seeds them again after a meeting.
        Assert.NotSame(numbers, world.HyperspaceRandomOf("h"));
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

    [Fact]
    public void EachStayInHyperspaceGetsFreshNumbers()
    {
        // Out of every sphere: only the day's anywhere races roll.
        var world = Players(null, ("a", 9000, 300));
        uint next = 500;
        world.HyperspaceSeeds = _ => next++;
        Frames(world, 3);
        var first = world.HyperspaceRandomOf("a");
        Assert.Equal(3, first.Frames);
        // Away and back (Battle seeds TFB_Random again): new numbers, their
        // frames counted afresh, the first frame's view set up again.
        var a = world.Find("a")!;
        a.Present = false;
        world.Step();
        a.Present = true;
        world.Step();
        var second = world.HyperspaceRandomOf("a");
        Assert.NotSame(first, second);
        Assert.Equal(1, second.Frames);
        // Seeded afresh (the next seed): its numbers are a new TFB_Random's.
        var fresh = new Uqm.TfbRandom(501);
        foreach (var _ in second.Places!)
            fresh.Next();
        Assert.Equal(fresh.Seed, second.Seed);
        Assert.DoesNotContain("MoveGalaxy", second.Places!);
        // Its second frame begins with the view's set-up: 90 numbers.
        int before = second.Places!.Count;
        world.Step();
        Assert.Equal(Enumerable.Repeat("MoveGalaxy", 90), second.Places!.Skip(before).Take(90));
        Assert.Equal(90, second.Places!.Count(p => p == "MoveGalaxy"));
    }

    [Fact]
    public void AWreckInHyperspaceDoesntRollIsntFlownAtAndKeepsNoGroups()
    {
        // In the Ur-Quan's sphere: a flying, b a wreck nearer the group.
        var world = Players(null, ("a", 5200, 6100), ("b", 5800, 6100));
        world.Find("b")!.Ship.Destroyed = true;
        var between = Group(world, "URQUAN_SHIP", 5600, 6100, "a");
        var byTheWreck = Group(world, "URQUAN_SHIP", 6200, 6100, "a");   // in b's span alone
        world.Step();
        Assert.DoesNotContain(byTheWreck, world.Encounters!.List);
        Frames(world, 60);
        Assert.True(between.LocX < 5600, $"at {between.LocX}: flying at the wreck, not at a");
        for (int day = 0; day < 5; ++day)
            world.MoveClockDays(1);
        Assert.DoesNotContain("check_hyperspace_encounter", world.HyperspaceRandomOf("b").Places!);
        Assert.Contains("check_hyperspace_encounter", world.HyperspaceRandomOf("a").Places!);
        Assert.DoesNotContain(world.Encounters.List, e => e.Roller == "b");
    }

    [Fact]
    public void EachPlayersOwnFuelGivesTheMelnormeTheirChance()
    {
        // Out of every sphere: the host with fuel, a guest with none.
        var world = Players(null, ("a", 9000, 300), ("b", 9500, 800));
        world.Find("a")!.Ship.Flight.Fuel = 1000;
        world.Find("b")!.Ship.Flight.Fuel = 0;
        for (int day = 0; day < 30; ++day)
            world.MoveClockDays(1);
        int melnorme = Race("MELNORME_SHIP");
        Assert.Contains(world.Encounters!.List, e => e.Race == melnorme && e.Roller == "b");
        Assert.DoesNotContain(world.Encounters.List, e => e.Race == melnorme && e.Roller == "a");
    }

    // A game with the host flying into the star (6166, 6000) and touching
    // it, the guest far off, and a group in the host's span alone.
    private static (GameWorld World, HyperEncounter Group) IntoAStar()
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        Assert.Equal(JoinResult.Joined, world.Join("g", "Guest"));
        world.UndockShown("g");
        world.TraceHyperspaceRandom = true;
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.Story.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        var f = world.Host.Ship.Flight;
        f.PlaceAtUniverse(6126, 6000);
        f.StarJustLeft = null;
        f.Facing = 4;
        var guest = world.Find("g")!.Ship.Flight;
        guest.PlaceAtUniverse(7400, 6000);
        guest.StarJustLeft = null;
        var e = Group(world, "URQUAN_SHIP", 6166 - 200, 6000, "h");
        for (int i = 0; i < 200 && world.Host.Ship.Entry is null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            world.Step();
        }
        Assert.Equal(systems.Map.Stars.Single(s => (s.X, s.Y) == (6166, 6000)).Number, world.Host.Ship.SystemStar);
        return (world, e);
    }

    [ContentFact]
    public void AShipSpinningIntoAStarIsInHyperspaceForTheEncountersUntilTheSpinsLastFrame()
    {
        var (world, e) = IntoAStar();
        var host = world.Host;
        var spin = host.Ship.Spin!;
        int steps = host.Ship.Entry!.Steps;
        // Spinning: in hyperspace for the encounters, its numbers kept, the
        // group flying at it (not kept for it as one gone, nor at the guest),
        // its place drifting from where it touched the star.
        var numbers = world.HyperspaceRandomOf("h");
        Assert.NotEqual((host.Ship.Flight.UniverseX, host.Ship.Flight.UniverseY), (spin.UniverseX, spin.UniverseY));
        for (int i = 0; i < steps; ++i)
        {
            Assert.True(GameWorld.InHyperspace(host));
            Assert.NotNull(e.Element);
            Assert.DoesNotContain("h", e.KeptFor.Keys);
            if (i == 2)
            {
                // A save during the spin keeps the group for the host from
                // where the spin has taken it.
                var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems.Value, TestContent.Shapes);
                Assert.Equal((spin.UniverseX, spin.UniverseY), loaded.Encounters!.List.Single().KeptFor["h"]);
                Assert.Empty(e.KeptFor);
            }
            world.Step();
        }
        Assert.Same(numbers, world.HyperspaceRandomOf("h"));
        Assert.True(e.LocX > 6166 - 200, $"at {e.LocX}: not flying at the host");
        Assert.Empty(world.HyperspaceMeetings);
        // The spin's last frame: out of hyperspace, the group kept for the
        // host from where the spin took it.
        (short X, short Y) spunTo = (spin.UniverseX, spin.UniverseY);
        world.Step();
        Assert.Null(host.Ship.Spin);
        Assert.False(GameWorld.InHyperspace(host));
        Assert.Equal(spunTo, e.KeptFor["h"]);
        Assert.Contains(e, world.Encounters!.List);
    }

    [ContentFact]
    public void ADayEndingInTheSpinRollsWhereTheSpinHasTakenTheShip()
    {
        // The Spathi's sphere moved (as 3c's events move fleets) so that its
        // edge lies between where the spin has taken the host and the star:
        // the day's roll draws for the Spathi only from the spin's place.
        int Draws(bool edgeBetween)
        {
            var (world, _) = IntoAStar();
            world.Step();
            var spin = world.Host.Ship.Spin!;
            Assert.True(spin.UniverseX < 6166, $"spun to {spin.UniverseX}");
            var story = world.Story!;
            int strength = 10;
            int radius = (strength * Rules.SphereRadiusIncrement) >> 1;
            var spathi = story.Fleet("SPATHI_SHIP");
            story.SetFleet("SPATHI_SHIP", spathi with
            {
                ActualStrength = strength, LocY = spin.UniverseY,
                LocX = edgeBetween ? spin.UniverseX - radius + 1 : 1000,
            });
            var random = world.HyperspaceRandomOf("h");
            int before = random.Places!.Count(p => p == "check_hyperspace_encounter");
            world.MoveClockDays(1);
            return random.Places!.Count(p => p == "check_hyperspace_encounter") - before;
        }
        Assert.Equal(Draws(false) + 1, Draws(true));
    }

    [ContentFact]
    public void APlayerLeavingAsTheirShipSpinsIntoAStarLeavesHyperspaceFromWhereItHadSpun()
    {
        var (world, e) = IntoAStar();
        var host = world.Host;
        var spin = host.Ship.Spin!;
        world.Step();
        (short X, short Y) spunTo = (spin.UniverseX, spin.UniverseY);
        Assert.NotEqual((host.Ship.Flight.UniverseX, host.Ship.Flight.UniverseY), spunTo);
        world.Leave("h");
        world.Step();
        Assert.Null(host.Ship.Spin);
        Assert.Equal(spunTo, e.KeptFor["h"]);
    }

    [ContentFact]
    public void AShipPutInOrbitAsItSpinsIntoAStarIsOutOfHyperspace()
    {
        var (world, e) = IntoAStar();
        world.Step();
        var star = world.Host.Ship.SystemStar!.Value;
        world.PutInOrbit("h", star, 0);
        Assert.False(GameWorld.InHyperspace(world.Host));
        world.Step();
        Assert.Contains("h", e.KeptFor.Keys);
    }

    // A seed whose first number, as a roll takes it ((COUNT) % 100), is n.
    private static uint SeedRolling(int n)
    {
        for (uint seed = 1; ; ++seed)
            if ((ushort)new Uqm.TfbRandom(seed).Next() % 100 == n)
                return seed;
    }

    [Fact]
    public void TheMelnormeComeAtThirtyInAHundredToAShipWithNoFuelAndAtOnceToTheBroadcaster()
    {
        // Out of every sphere: the Melnorme's roll is the day's first.
        var clock = new GameClock(2155, 2, 17);
        var story = StoryState.NewGame(Rules, clock);
        story.Set("STARBASE_AVAILABLE", 1);
        story.Set("SLYLANDRO_MULTIPLIER", 0);
        int melnorme = Race("MELNORME_SHIP");
        int Rolled(int n, uint fuel)
        {
            var encounters = new HyperEncounters(Rules, 16);
            encounters.Roll(story, "a", 9000, 300, fuel, new HyperspaceRandom(SeedRolling(n)));
            return encounters.List.Count(e => e.Race == melnorme);
        }
        Assert.Equal((1, 0, 0), (Rolled(29, 0), Rolled(30, 0), Rolled(0, 100)));
        // The broadcaster used: 100%, its response set, and its use spent.
        story.Set("USED_BROADCASTER", 1);
        Assert.Equal(1, Rolled(99, 100));
        Assert.Equal((1, 0), (story.Get("BROADCASTER_RESPONSE"), story.Get("USED_BROADCASTER")));
        Assert.Equal(0, Rolled(0, 100));
        // Angered (MELNORME_ANGER 3), they don't come.
        story.Set("MELNORME_ANGER", 3);
        Assert.Equal(0, Rolled(0, 0));
    }

    [Fact]
    public void APlayersSpanIsHalfTheRadarsAndTwoScreensEachWay()
    {
        var e = new HyperEncounter { LocX = 1000, LocY = 1000 };
        Assert.True(HyperEncounters.InSpan(e, (short)(1000 - HyperEncounters.SpanX), (short)(1000 + HyperEncounters.SpanY)));
        Assert.False(HyperEncounters.InSpan(e, (short)(1000 - HyperEncounters.SpanX - 1), 1000));
        Assert.False(HyperEncounters.InSpan(e, 1000, (short)(1000 + HyperEncounters.SpanY + 1)));
        Assert.Equal((504, 400), (HyperEncounters.SpanX, HyperEncounters.SpanY));
    }

    [ContentFact]
    public void OfTwoGroupsTouchedInAFrameTheSoonestIsMet()
    {
        var encounters = new HyperEncounters(Rules, 16);
        var ship = new HyperFlight(ShipDrive.ForHyperspace(2, 2), 10000);
        ship.PlaceAtUniverse(5000, 5000);
        ship.Facing = 4;   // right
        HyperEncounter At(int dx)
        {
            var e = new HyperEncounter { Race = Race("URQUAN_SHIP") };
            int x = ship.LogX + dx;
            e.Element = new EncounterElement { Nonsolid = false, CurX = x, CurY = ship.LogY, NextX = x, NextY = ship.LogY };
            encounters.List.Add(e);
            return e;
        }
        // The ship's sprite swept 400 units right this frame: the later group listed first.
        var later = At(-20);
        var sooner = At(-300);
        Assert.Same(sooner, encounters.Touch(ship, TestContent.Shapes, (ship.LogX - 400, ship.LogY)));
        Assert.Same(later, encounters.Touch(ship, TestContent.Shapes, (ship.LogX - 100, ship.LogY)));
        // A group not to be touched: in its vortex (not solid), or met.
        later.Element!.Nonsolid = true;
        sooner.Element!.Met = "b";
        Assert.Null(encounters.Touch(ship, TestContent.Shapes, (ship.LogX - 400, ship.LogY)));
    }

    [ContentFact]
    public void AStarTouchedInTheSameFrameAsAGroupComesFirst()
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        var star = systems.Map.Stars.First(s => s.Special is null);
        var f = world.Host.Ship.Flight;
        f.PlaceAtUniverse(star.X, star.Y);
        f.LogX -= 40;
        f.StarJustLeft = null;
        f.Facing = 4;
        f.Velocity.SetComponents(40 << 5, 0);
        var e = Group(world, "URQUAN_SHIP", star.X, star.Y, "h");
        e.Element = new EncounterElement
        {
            Nonsolid = false, InVortex = false, ThrustWait = 100, Frame = 31,
            CurX = e.LogX, CurY = e.LogY, NextX = e.LogX, NextY = e.LogY,
        };
        world.SubmitInput("h", world.Frame + 1, ShipInput.Thrust);
        world.Step();
        Assert.Equal(star.Number, world.Host.Ship.SystemStar);
        Assert.Empty(world.HyperspaceMeetings);
        Assert.Contains(e, world.Encounters!.List);
    }
}
