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
using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// System groups (docs/plans/stage-3b.md, item 4), against UQM's own for
/// one player (tests/golden/system-groups.json): the special systems'
/// groups and the random draws, kept with the system, expired and drawn
/// again, each group's flight, detection, interception and standing down,
/// and the one the ship touches, frame by frame, with every random number
/// in UQM's order.
/// </summary>
[Trait("Topic", "systems")]
public class SystemGroupTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly Lazy<SystemCatalog> Systems = new(() => SystemCatalog.Load(DataDir));

    // A frame's fields (golden/groups.c).
    private const int FKeys = 0, FFacing = 1, FIpX = 2, FIpY = 3, FScreenX = 4, FScreenY = 5, FZoom = 6, FInner = 7,
        FEvent = 8, FDraws = 9, FSeed = 10, FOrder = 11, FGroups = 12, FMet = 13;
    private const int EvMet = 1, EvLeft = 2, EvOrbit = 3;

    // UQM's ELEMENT state flags a group's element shows, and the stamp primitive.
    private const int NonsolidFlag = 1 << 6, DisappearingFlag = 1 << 4, StampPrim = 1;

    internal static GameWorld NewWorld() =>
        GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "h", "Host", Systems.Value,
            TestContent.Shapes);

    private sealed class Replay
    {
        public required StoryState Story;
        public required SystemGroups Groups;
        public GameDate Date;
    }

    // A scenario's game as golden/groups.c makes it.
    private static Replay Start(JsonElement input)
    {
        var world = NewWorld();
        var story = world.Story!;
        var groups = new SystemGroups(story.Rules) { Ported = story.Rules.Races.ToHashSet() };
        foreach (var st in input.GetProperty("states").EnumerateArray())
        {
            var v = st.Ints();
            story.Set(v[0] switch
            {
                0 => "PROBE_MESSAGE_DELIVERED", 1 => "SLYLANDRO_MULTIPLIER", 2 => "UTWIG_SUPOX_MISSION",
                _ => "STARBASE_AVAILABLE",
            }, v[1]);
        }
        foreach (var f in input.GetProperty("fleets").EnumerateArray())
        {
            var v = f.Ints();
            string race = story.Rules.Races[v[0]];
            story.SetFleet(race, story.Fleet(race) with { ActualStrength = v[1], LocX = v[2], LocY = v[3] });
        }
        if (input.GetProperty("drone_dead").GetInt32() != 0)
            groups.Defined["SOL_DEFINED"] = new() { new GroupShips(1, story.Rules.RaceIndex("URQUAN_DRONE_SHIP"), Array.Empty<int>()) };
        var d = input.GetProperty("date").Ints();
        return new Replay { Story = story, Groups = groups, Date = new GameDate(d[0], d[1], d[2]) };
    }

    // The port's state against a frame of UQM's.
    private static void Match(IpFlight f, SystemDraw d, JsonElement rec, string where, ref int drawn)
    {
        Assert.True(((int)f.Facing, f.Location.X, f.Location.Y, f.Screen.X, f.Screen.Y, f.ZoomRadius, f.InnerPlanet ?? -1) ==
                    (rec[FFacing].GetInt32(), (short)rec[FIpX].GetInt32(), (short)rec[FIpY].GetInt32(),
                        (short)rec[FScreenX].GetInt32(), (short)rec[FScreenY].GetInt32(), (short)rec[FZoom].GetInt32(),
                        rec[FInner].GetInt32()),
            $"{where}: the flagship {(f.Facing, f.Location, f.Screen, f.ZoomRadius, f.InnerPlanet)}");
        var places = rec[FDraws].EnumerateArray().Select(p => p.GetString()!).ToList();
        var random = d.Random!;
        Assert.True(places.SequenceEqual(random.Places!.Skip(drawn)),
            $"{where}: drew {string.Join(",", random.Places!.Skip(drawn))}, UQM {string.Join(",", places)}");
        drawn = random.Places!.Count;
        Assert.True(random.Seed == rec[FSeed].GetUInt32(), $"{where}: the seed");
        var order = (d.Elements ?? new()).Select(g => g.GroupId).ToArray();
        Assert.True(order.SequenceEqual(rec[FOrder].Ints()), $"{where}: the elements' order {string.Join(",", order)}");
        var uqm = rec[FGroups].EnumerateArray().ToList();
        Assert.True(uqm.Count == d.Groups.Count, $"{where}: {d.Groups.Count} groups, UQM {uqm.Count}");
        for (int i = 0; i < uqm.Count; ++i)
        {
            var u = uqm[i];
            var g = d.Groups[i];
            string at = $"{where}, group {i}";
            var v = Enumerable.Range(0, 10).Select(k => u[k].GetInt32()).ToArray();
            Assert.True((g.GroupId, g.Race, (int)g.SysLoc, (int)g.Task, (int)g.DestLoc, (int)g.OrbitPos, (int)g.GroupCounter,
                            g.InSystem ? 1 : 0, (int)g.LocX, (int)g.LocY)
                        == (v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9]),
                $"{at}: {(g.GroupId, g.Race, g.SysLoc, g.Task, g.DestLoc, g.OrbitPos, g.GroupCounter, g.InSystem, g.LocX, g.LocY)}, UQM {string.Join(",", v)}");
            var ue = u[10];
            Assert.True((ue.ValueKind == JsonValueKind.Null) == (g.Element is null), $"{at}: element");
            if (g.Element is not { } el)
                continue;
            var e = ue.Ints();
            Assert.True(((e[0] & NonsolidFlag) != 0, (e[0] & DisappearingFlag) != 0, e[1], e[2], e[16] == StampPrim) ==
                        ((el.Flags & ElementFlags.NonSolid) != 0, (el.Flags & ElementFlags.Disappearing) != 0,
                            el.LifeSpan, (int)el.ThrustWait, el.Drawn),
                $"{at}: element {string.Join(",", e)}, port {(el.Flags, el.LifeSpan, el.ThrustWait, el.Drawn)}");
            Assert.True(e[3..12].SequenceEqual(el.Velocity.ToArray()), $"{at}: velocity {string.Join(",", el.Velocity.ToArray())}");
            Assert.True((el.Cur.X, el.Cur.Y, el.Next.X, el.Next.Y) == (e[12], e[13], e[14], e[15]),
                $"{at}: on the screen {(el.Cur, el.Next)}");
        }
    }

    [ContentFact]
    public void GroupsFlyAsUqmsForOnePlayer()
    {
        var golden = Golden.Load("system-groups");
        var shapes = TestContent.Shapes!;
        foreach (var scenario in golden.GetProperty("scenarios").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var input = scenario.GetProperty("input");
            var game = Start(input);
            int thrusters = input.GetProperty("thrusters").GetInt32(), jets = input.GetProperty("jets").GetInt32();
            var visits = input.GetProperty("visits").EnumerateArray().ToList();
            var recorded = scenario.GetProperty("visits").EnumerateArray().ToList();
            for (int v = 0; v < visits.Count; ++v)
            {
                int star = visits[v][0].GetInt32();
                uint seed = visits[v][1].GetUInt32();
                game.Date = game.Date.Plus(visits[v][2].GetInt32());
                var system = Systems.Value[star];
                // Arriving from hyperspace with the drive and jets of the scenario.
                var flight = new IpFlight(system, SystemTransition.Arrive(system.Star), thrusters, jets, shapes);
                var d = game.Groups.Arrive(system, "h", flight, game.Date, game.Story,
                    () => new HyperspaceRandom(seed) { Places = new List<string>() });
                var flyers = new[] { new SystemFlyer("h", flight) };
                var frames = recorded[v][1].EnumerateArray().ToList();
                var meetings = visits[v].GetArrayLength() > 4 ? visits[v][4].EnumerateArray().ToList() : new();
                int meetingNo = 0;
                int drawn = 0;
                bool left = false;
                for (int k = 0; k < frames.Count; ++k)
                {
                    string where = $"{name}, visit {v + 1}, frame {k + 1}";
                    var ev = flight.Step((ShipInput)frames[k][FKeys].GetInt32());
                    game.Groups.Frame(d, system, flyers, shapes);
                    Match(flight, d, frames[k], where, ref drawn);
                    int uqmEvent = frames[k][FEvent].GetInt32();
                    Assert.True((ev == IpEvent.LeftSystem, ev == IpEvent.EnteredOrbit, flyers[0].Met is not null) ==
                                (uqmEvent == EvLeft, uqmEvent == EvOrbit, uqmEvent == EvMet), $"{where}: event {ev}, met {flyers[0].Met?.GroupId}");
                    if (flyers[0].Met is { } met)
                    {
                        Assert.Equal(frames[k][FMet].GetInt32(), met.GroupId);
                        if (meetingNo < meetings.Count && meetings[meetingNo][0].GetInt32() == 1)
                        {
                            // Back from a battle (GameWorld.EndMeeting): the
                            // group's ships as the battle left them, the
                            // race's groups to turn on the flagship, the
                            // numbers seeded afresh (Battle's).
                            var ships = game.Groups.StartMeeting(d, met, "h")!;
                            var crews = meetings[meetingNo][2].Ints();
                            var after = ships.Ships.Select((c, i) => (Crew: i < crews.Length && crews[i] >= 0 ? crews[i] : c,
                                Captain: ships.Captains[i])).Where(x => x.Crew > 0).ToList();
                            game.Groups.EndMeeting(d, met, after, met.Race);
                            Assert.Equal(met.Race, d.EncounterRace);
                            d.Random = new HyperspaceRandom(meetings[meetingNo][1].GetUInt32()) { Places = new List<string>() };
                            drawn = 0;
                        }
                        else
                        {
                            // Back as from a talk without a battle (GameWorld.EndMeeting).
                            game.Groups.AfterMeeting(d, met);
                        }
                        ++meetingNo;
                        flight.EscapeCounter = IpFlight.EscapeFrames;
                        flight.Reenter();
                        d.Elements = null;
                    }
                    if (ev == IpEvent.EnteredOrbit)
                    {
                        flight.ReturnFromOrbit();
                        d.Elements = null;
                    }
                    if (ev == IpEvent.LeftSystem)
                        left = true;
                }
                _ = left;
                game.Groups.Depart(star, "h", game.Date);
            }
        }
    }

    // ---- The co-op game's rules (our own) ----

    private const int UrQuanStar = 311, UrQuanStar2 = 321, SpathiStar = 336, SolStar = 82;

    // A world whose every race's groups fly, the players' numbers traced.
    private static GameWorld CoopWorld(params string[] guests)
    {
        var world = NewWorld();
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.TraceSystemRandom = true;
        world.SystemSeeds = star => 1000u + (uint)star;
        foreach (var g in guests)
            world.Join(g, g);
        return world;
    }

    private static void Run(GameWorld world, int frames = 1)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    private static SystemDraw DrawOf(GameWorld world, int star) => world.SystemGroups!.Systems[star];

    // The places a system's numbers were drawn since its draw was set going.
    private static List<string> Drawn(GameWorld world, int star) => DrawOf(world, star).Random!.Places!;

    [ContentFact]
    public void ADrawIsKeptUntilEveryPlayerWhoWasInTheSystemHasEnteredAnother()
    {
        var world = CoopWorld("g");
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        var draw = DrawOf(world, UrQuanStar);
        var groups = draw.RandomGroups!.Select(g => (g.Id, g.Race, g.Ships.Count)).ToList();
        Assert.NotEmpty(groups);
        Assert.Contains("BuildGroups", Drawn(world, UrQuanStar));
        var random = draw.Random;
        // The guest comes in while the host is there: the groups fly on, no new tasks.
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        Assert.Same(random, draw.Random);
        Assert.Equal(new[] { "g", "h" }, draw.Present.OrderBy(x => x));
        // Both leave; the host enters another system: kept, as the guest was there.
        world.PutInHyperspace("h");
        world.PutInHyperspace("g");
        Run(world);
        Assert.Empty(draw.Present);
        Assert.Null(draw.Random);
        Assert.Empty(draw.Groups);
        world.PutInSystem("h", UrQuanStar2);
        Run(world);
        Assert.Same(draw, DrawOf(world, UrQuanStar));
        Assert.Equal(new[] { "g" }, draw.Visitors);
        // The guest back within the week: the same draw, new tasks.
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        Assert.Equal(groups, DrawOf(world, UrQuanStar).RandomGroups!.Select(g => (g.Id, g.Race, g.Ships.Count)));
        Assert.DoesNotContain("BuildGroups", Drawn(world, UrQuanStar));
        Assert.Contains("GetGroupInfo", Drawn(world, UrQuanStar));
        // The guest enters a third system: nobody who was there is left, and it goes.
        world.PutInHyperspace("g");
        Run(world);
        world.PutInSystem("g", SpathiStar);
        Run(world);
        Assert.False(world.SystemGroups!.Systems.ContainsKey(UrQuanStar));
        Assert.True(world.SystemGroups.Systems.ContainsKey(UrQuanStar2));   // the host's still there
        // Coming back: a new draw.
        world.PutInHyperspace("g");
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, UrQuanStar));
    }

    [ContentFact]
    public void ADrawIsKeptForAWeekAfterTheLastOneLeftEvenWithNoGroups()
    {
        var world = CoopWorld();
        // The probes, met anywhere, draw at every system; this one is in no
        // race's sphere, and its draw gives no groups.
        world.Story!.Set("SLYLANDRO_MULTIPLIER", 1);
        world.SystemSeeds = _ => 1;
        int star = EmptyStar(world);
        world.PutInSystem("h", star);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, star));
        Assert.Empty(DrawOf(world, star).RandomGroups!);
        world.PutInHyperspace("h");
        Run(world);
        var left = new GameDate(world.Clock.Year, world.Clock.Month, world.Clock.Day);
        Assert.Equal(left.Plus(7), DrawOf(world, star).Expires);
        world.MoveClockDays(7);
        world.PutInSystem("h", star);
        Run(world);
        Assert.DoesNotContain("BuildGroups", Drawn(world, star));   // kept: a week on is still good
        world.PutInHyperspace("h");
        Run(world);
        world.MoveClockDays(8);
        world.PutInSystem("h", star);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, star));   // gone a day after the week
    }

    // A system in no race's sphere (but the races met anywhere), of no special.
    private static int EmptyStar(GameWorld world)
    {
        var story = world.Story!;
        var rules = story.Rules;
        return Systems.Value.Map.Stars.First(s => s.Special is null && story.Fleets.Select((f, i) => (f, i)).All(x =>
            x.f.ActualStrength == 0 || x.f.ActualStrength == rules.InfiniteRadius
            || Math.Pow(s.X - x.f.LocX, 2) + Math.Pow(s.Y - x.f.LocY, 2)
            > Math.Pow((x.f.ActualStrength * rules.SphereRadiusIncrement) >> 1, 2) + 1)).Number;
    }

    [ContentFact]
    public void SolWithoutItsDroneIsDrawnAfreshOnEveryEntryWhileNobodysThere()
    {
        var world = CoopWorld("g");
        var story = world.Story!;
        // The Ilwrath's sphere over Sol, so a draw gives groups; the drone gone.
        story.SetFleet("ILWRATH_SHIP", story.Fleet("ILWRATH_SHIP") with { ActualStrength = 256, LocX = 1752, LocY = 1450 });
        world.SystemGroups!.Defined["SOL_DEFINED"] = new() { new GroupShips(1, story.Rules.RaceIndex("URQUAN_DRONE_SHIP"), Array.Empty<int>()) };
        world.PutInSystem("h", SolStar);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, SolStar));
        var first = DrawOf(world, SolStar).Random;
        // The guest comes in: no new draw while the host is there.
        world.PutInSystem("g", SolStar);
        Run(world);
        Assert.Same(first, DrawOf(world, SolStar).Random);
        world.PutInHyperspace("h");
        world.PutInHyperspace("g");
        Run(world);
        // Back the next frame: a new draw all the same.
        world.PutInSystem("h", SolStar);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, SolStar));
        Assert.Null(DrawOf(world, SolStar).Defined);
    }

    [ContentFact]
    public void TheDroneOrbitsEarthAndTheColonysGuardItsWorld()
    {
        var world = CoopWorld();
        world.PutInSystem("h", SolStar);
        Run(world);
        var drone = DrawOf(world, SolStar).Groups.Single();
        Assert.Equal((world.Story!.Rules.RaceIndex("URQUAN_DRONE_SHIP"), (byte)3, IpGroup.InOrbit), (drone.Race, drone.DestLoc, drone.Task));
        Assert.Equal("SOL_DEFINED", DrawOf(world, SolStar).Defined);
        Assert.Null(DrawOf(world, SolStar).RandomGroups);
        // The drone's race not yet ported: kept, but not flown.
        var unported = CoopWorld();
        unported.EncounterRaces = new HashSet<string>();
        unported.PutInSystem("h", SolStar);
        Run(unported);
        Assert.Empty(DrawOf(unported, SolStar).Groups);
        Assert.Single(unported.SystemGroups!.Defined["SOL_DEFINED"].Single().Ships);

        int colony = Systems.Value.Map.Stars.Single(s => s.Special == "START_COLONY_DEFINED").Number;
        world.PutInSystem("h", colony);
        Run(world);
        var guard = DrawOf(world, colony).Groups.Single();
        Assert.Equal((world.Story.Rules.RaceIndex("URQUAN_SHIP"), (byte)1, IpGroup.InOrbit), (guard.Race, guard.DestLoc, guard.Task));
    }

    [ContentFact]
    public void RacesNotPortedAreDrawnButDontFly()
    {
        var world = CoopWorld();
        world.EncounterRaces = new HashSet<string>();
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        Assert.NotEmpty(DrawOf(world, UrQuanStar).RandomGroups!);
        Assert.Empty(DrawOf(world, UrQuanStar).Groups);
        Assert.Contains("GetGroupInfo", Drawn(world, UrQuanStar));   // their tasks drawn all the same
    }

    // A flight at a place in a system's outer view (or a planet's inner), at
    // rest, its first frame flown.
    // At a zoom of its own (an outer view's zoom changes only as the ship
    // leaves the screen or comes into its zoom-in box, so ships at one
    // place can be at different zooms).
    private static IpFlight FlightAt(SolarSystem system, int x, int y, int? inner = null, int? zoom = null)
    {
        var arrive = new IpFlight(system, SystemTransition.Arrive(system.Star), 2, 2, TestContent.Shapes!).Save();
        if (zoom is int z)
            arrive = arrive with { ZoomRadius = z };
        var screen = IpGeometry.LocationToDisplay(new IpPoint((short)x, (short)y), inner is null ? arrive.ZoomRadius : 8192);
        var state = arrive with
        {
            LocationX = x, LocationY = y, ScreenX = screen.X, ScreenY = screen.Y, InnerPlanet = inner,
            SunLocationX = inner is int p ? SystemGroups.PlanetOuterLocation(system, p, arrive.ZoomRadius).X : 0,
            SunLocationY = inner is int q ? SystemGroups.PlanetOuterLocation(system, q, arrive.ZoomRadius).Y : 0,
        };
        var flight = IpFlight.Restore(system, state, 2, 2, TestContent.Shapes!);
        flight.Step(ShipInput.None);   // its first frame: the view's zoom found for where it is
        return flight;
    }

    private static IpGroup Group(SystemGroups sg, SystemDraw d, string race, int id, int x, int y, byte task = IpGroup.InOrbit, byte dest = 1)
    {
        var g = new IpGroup { GroupId = id, Race = 0, LocX = (short)x, LocY = (short)y, Task = task, DestLoc = dest, SysLoc = 0 };
        g.Race = RaceIndexOf(race);
        d.Groups.Add(g);
        return g;
    }

    private static int RaceIndexOf(string race) => NewWorldRules.Value.RaceIndex(race);
    private static readonly Lazy<StoryRules> NewWorldRules = new(() => NewWorld().Story!.Rules);

    private static (SystemGroups, SystemDraw, SolarSystem) Bare(int star)
    {
        var sg = new SystemGroups(NewWorldRules.Value) { Ported = NewWorldRules.Value.Races.ToHashSet() };
        var d = new SystemDraw(star) { Random = new HyperspaceRandom(9) { Places = new() } };
        sg.Systems[star] = d;
        return (sg, d, Systems.Value[star]);
    }

    [ContentFact]
    public void AGroupInterceptsTheNearestPlayerInItsView()
    {
        var (sg, d, system) = Bare(UrQuanStar);
        var g = Group(sg, d, "URQUAN_SHIP", 1, 0, -6000, dest: IpGroup.InterceptPlayer);
        var far = new SystemFlyer("far", FlightAt(system, 6000, -6000));
        var near = new SystemFlyer("near", FlightAt(system, -5000, -6000));
        for (int i = 0; i < 3; ++i)
        {
            far.Flight.Step(ShipInput.None);
            near.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { far, near }, TestContent.Shapes!);
        }
        Assert.True(g.Element!.Velocity.Current().dx < 0, "flies at the nearer, to the west");
        // The nearer one in an inner system: the one in its view.
        var inside = new SystemFlyer("near", FlightAt(system, -4000, -4000, inner: 0));
        for (int i = 0; i < 8; ++i)
        {
            far.Flight.Step(ShipInput.None);
            inside.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { far, inside }, TestContent.Shapes!);
        }
        Assert.True(g.Element!.Velocity.Current().dx > 0, "flies at the one in its view, to the east");
    }

    [ContentFact]
    public void MeetingAGroupStandsItsRaceDownForEveryPlayerWhoCanStillFlyIntoOne()
    {
        var (sg, d, system) = Bare(UrQuanStar);
        var met = Group(sg, d, "URQUAN_SHIP", 1, 6000, 0, task: IpGroup.OnStation);
        var other = Group(sg, d, "URQUAN_SHIP", 2, -6000, 0, task: IpGroup.OnStation);
        var a = new SystemFlyer("a", FlightAt(system, 6000, 0));
        var b = new SystemFlyer("b", FlightAt(system, -6000 + 600, 0));
        a.Flight.Step(ShipInput.None);
        b.Flight.Step(ShipInput.None);
        sg.Frame(d, system, new[] { a, b }, TestContent.Shapes!);
        // A touched the first: they meet, it re-forms, and the race stands down for everyone.
        Assert.Same(met, a.Met);
        Assert.Null(b.Met);
        Assert.Equal(100, met.GroupCounter);
        Assert.True((met.Task & IpGroup.ReformGroup) != 0);
        Assert.True((other.Task & IpGroup.IgnoreFlagship) != 0);
        // The other, near B, doesn't detect B now; but B flying into it
        // meets it (here, as it goes by, or put on it).
        bool intoIt = false;
        for (int i = 0; i < 20 && !intoIt; ++i)
        {
            b.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { b }, TestContent.Shapes!);
            Assert.NotEqual(IpGroup.InterceptPlayer, other.DestLoc);
            intoIt = b.Met == other;
        }
        if (!intoIt)
        {
            var into = new SystemFlyer("b", FlightAt(system, other.LocX, other.LocY));
            into.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { into }, TestContent.Shapes!);
            intoIt = into.Met == other;
        }
        Assert.True(intoIt);
    }

    [ContentFact]
    public void ABattlesRaceTurnsOnThePlayersAsTheSystemComesBack()
    {
        var (sg, d, system) = Bare(UrQuanStar);
        var g = Group(sg, d, "URQUAN_SHIP", 1, 5000, 5000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var spathi = Group(sg, d, "SPATHI_SHIP", 2, -5000, 5000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        d.EncounterRace = g.Race;
        var a = new SystemFlyer("a", FlightAt(system, -9000, -9000));
        a.Flight.Reenter();   // back from the battle: its element appears, DoMissions
        a.Flight.Step(ShipInput.None);
        sg.Frame(d, system, new[] { a }, TestContent.Shapes!);
        Assert.Equal((IpGroup.InterceptPlayer, 0), (g.DestLoc, g.Task & IpGroup.IgnoreFlagship));
        Assert.Equal(2, spathi.DestLoc);
        Assert.Equal(-1, d.EncounterRace);
    }

    [ContentFact]
    public void TheGroupsWaitWithoutElementsWhileNobodyFliesAndOnePlayerComingBackLeavesTheOthers()
    {
        var world = CoopWorld("g");
        world.PutInSystem("h", UrQuanStar);
        world.PutInSystem("g", UrQuanStar);
        Run(world, 3);
        var d = DrawOf(world, UrQuanStar);
        var elements = d.Elements!;
        var first = d.Groups.First(g => g.Element is not null).Element;
        // The host into orbit: the guest flies on, the elements as they were.
        int planet = 0;
        world.PutInOrbit("h", UrQuanStar, planet);
        Run(world, 2);
        Assert.Same(elements, d.Elements);
        Assert.Same(first, d.Groups.First(g => g.Element is not null).Element);
        // The guest away too: nobody flies, no elements, nobody moves.
        world.PutInHyperspace("g");
        Run(world);
        Assert.Null(d.Elements);
        var at = d.Groups.Select(g => (g.LocX, g.LocY)).ToList();
        Run(world, 10);
        Assert.Equal(at, d.Groups.Select(g => (g.LocX, g.LocY)));
    }

    [ContentFact]
    public void AMeetingInASystemIsNotedAndTheShipFliesOnUntouchable()
    {
        var world = CoopWorld();
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        var d = DrawOf(world, UrQuanStar);
        var target = d.Groups.First(g => g.SysLoc == 0);
        var system = Systems.Value[UrQuanStar];
        world.Host.Ship.Ip = FlightAt(system, target.LocX, target.LocY);
        Run(world, 2);
        var meeting = Assert.Single(world.SystemMeetings);
        Assert.Equal(("h", UrQuanStar), (meeting.Player, meeting.Star));
        Assert.Equal(d.Groups.Single(g => g.GroupId == meeting.Group).Race, meeting.Race);
        Assert.True((d.Groups.Single(g => g.GroupId == meeting.Group).Task & IpGroup.ReformGroup) != 0);
        // The meeting (stage 3b-1 item 5): the encounter screen, the ship
        // waiting where it met the group, the escape counter set
        // (BuildBattle's); a hail (no race's talk ported yet) ends it in
        // peace, and the ship flies on, untouchable for the counter's frames.
        Assert.Equal(MeetingStep.Encounter, world.MeetingOf("h")!.Step);
        Assert.Equal(Activity.Talk, world.Host.Ship.Activity);
        Assert.Equal(IpFlight.EscapeFrames, world.Host.Ship.Ip!.EscapeCounter);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Null(world.MeetingOf("h"));
        Run(world, 2);
        Assert.Single(world.SystemMeetings);
        Assert.InRange(world.Host.Ship.Ip!.EscapeCounter, IpFlight.EscapeFrames - 3, IpFlight.EscapeFrames - 1);
    }

    // A world whose host has met a group in the Ur-Quan's system (its encounter screen up).
    private static (GameWorld World, SystemDraw Draw, IpGroup Met) MeetInUrQuanSystem(params string[] guests)
    {
        var world = CoopWorld(guests);
        world.Story!.Set("STARBASE_AVAILABLE", 1);   // a retreat allowed
        world.BattleKit = WorldBattleTests.Kit();
        world.DialogueLocks = Comm.DialogueLocks.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        var d = DrawOf(world, UrQuanStar);
        var target = d.Groups.First(g => g.SysLoc == 0 && world.SystemGroups!.ShipsOf(d, g)!.Ships.Count >= 2);
        world.Host.Ship.Ip = FlightAt(Systems.Value[UrQuanStar], target.LocX, target.LocY);
        Run(world, 2);
        var m = world.MeetingOf("h")!;
        Assert.Equal(MeetingStep.Encounter, m.Step);
        return (world, d, d.Groups.Single(g => g.GroupId == world.SystemMeetings.Single().Group));
    }

    [ContentFact]
    public void AMeetingInASystemFoughtWritesItsShipsBackAndTurnsTheRaceOnThePlayers()
    {
        var (world, d, met) = MeetInUrQuanSystem();
        var sg = world.SystemGroups!;
        var ships = sg.ShipsOf(d, met)!;
        var captains = ships.Captains.ToList();
        var m = world.MeetingOf("h")!;
        // The group's ships with their crews and captains (the group file's).
        Assert.Equal(ships.Ships.Zip(captains, (c, n) => (c, n)), m.Ships.Select(s => (s.Crew!.Value, s.Captain!.Value)));
        Assert.Equal(met.LocX, met.LocX);
        Assert.Equal("h", met.InMeeting);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var wb = m.Battle!;
        Assert.Equal(captains, wb.Enemies.Select(e => (int)e.Starship.CaptainsNameIndex));
        Assert.Equal((UrQuanStar, (short)world.Host.Ship.Ip!.Location.X), (wb.Place.Star!.Value, (short)wb.Place.X));
        wb.Enemies[1].Starship.CrewLevel = 5;
        var random = d.Random;
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.Step();
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        // UninitEncounter's ships left written back (reinitNpcs), the group
        // still in the system, its race to turn on the players
        // (EncounterRace, DoMissions as the ship comes back), the numbers
        // seeded afresh (Battle's).
        var expected = ships.Ships.Select((c, i) => i == 1 ? 5 : c).ToList();
        Assert.Equal(expected, sg.ShipsOf(d, met)!.Ships);
        Assert.Equal(captains, sg.ShipsOf(d, met)!.Captains);
        Assert.Contains(met, d.Groups);
        Assert.Null(met.InMeeting);
        Assert.NotSame(random, d.Random);
        Run(world, 2);
        Assert.Equal(-1, d.EncounterRace);
        Assert.All(d.Groups.Where(g => g.Race == met.Race), g => Assert.Equal(IpGroup.InterceptPlayer, g.DestLoc));
        Assert.All(d.Groups.Where(g => g.Race == met.Race), g => Assert.Equal(0, g.Task & IpGroup.IgnoreFlagship));
    }

    [ContentFact]
    public void TheGroupMetInASystemStaysWhereItIsWhileAnotherFliesThereAndIsntMetByThem()
    {
        var (world, d, met) = MeetInUrQuanSystem("g");
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        var at = (met.LocX, met.LocY, met.Task, met.GroupCounter);
        // The guest flies onto it: it passes through them.
        world.Find("g")!.Ship.Ip = FlightAt(Systems.Value[UrQuanStar], met.LocX, met.LocY);
        Run(world, 30);
        Assert.Equal(at, (met.LocX, met.LocY, met.Task, met.GroupCounter));
        Assert.Single(world.SystemMeetings);
        Assert.Null(world.MeetingOf("g"));
        Assert.True((met.Element!.Flags & ElementFlags.NonSolid) != 0);
        // The meeting over in peace: it flies (re-forms) again, the lock free.
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Null(met.InMeeting);
        Run(world, 2);
        Assert.NotEqual(at.GroupCounter, met.GroupCounter);
    }

    [ContentFact]
    public void APlayerOutOfTheirMeetingsBattleWhileAnotherFightsOnMeetsNoGroupInASystem()
    {
        var (world, d, met) = MeetInUrQuanSystem("g");
        var m = world.MeetingOf("h")!;
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        world.Find("g")!.Ship.Ip = FlightAt(Systems.Value[UrQuanStar], world.Host.Ship.Ip!.Location.X, world.Host.Ship.Ip.Location.Y);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.True(world.LetIntoBattle("g"));
        Run(world, 2);
        Assert.Same(m.Battle, world.Find("g")!.Ship.Battle);
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            if (m.Battle!.Enemy.RaceDesc is { } enemy)
                enemy.EnergyLevel = 0;   // the Ur-Quan can't fire: the guest holds out
            world.Step();
        }
        Assert.Equal(MeetingStep.Battle, m.Step);
        // Flown onto another group meanwhile: it passes through.
        var other = d.Groups.First(g => g != met && g.SysLoc == 0);
        world.Host.Ship.Ip = FlightAt(Systems.Value[UrQuanStar], other.LocX, other.LocY);
        Run(world, 10);
        Assert.Single(world.SystemMeetings);
        Assert.Same(m, world.MeetingOf("h"));
    }

    [ContentFact]
    public void AGroupWhoseRaceAnotherPlayerIsTalkingToPassesThroughInASystem()
    {
        var (world, d, met) = MeetInUrQuanSystem("g");
        world.PutInSystem("g", UrQuanStar);
        Run(world);
        world.TakeBattleNotices();
        var other = d.Groups.First(g => g != met && g.Race == met.Race && g.SysLoc == 0);
        world.Find("g")!.Ship.Ip = FlightAt(Systems.Value[UrQuanStar], other.LocX, other.LocY);
        Run(world, 10);
        Assert.Null(world.MeetingOf("g"));
        Assert.Single(world.SystemMeetings);
        Assert.Contains(world.TakeBattleNotices(), n => n.PlayerId == "g" && n.Text == "Host is talking to the Ur-Quan.");
    }

    [ContentFact]
    public void TheGroupsAreSavedWithTheirSystems()
    {
        var systems = Systems.Value;
        var world = CoopWorld("g");
        world.PutInSystem("h", UrQuanStar);
        world.PutInSystem("g", SolStar);
        Run(world, 5);
        world.PutInHyperspace("g");
        Run(world);
        var d = DrawOf(world, UrQuanStar);
        d.EncounterRace = 18;
        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, systems, TestContent.Shapes);
        loaded.EncounterRaces = world.EncounterRaces;
        loaded.Join("h", "Host");   // loaded, everyone's away
        var sg = loaded.SystemGroups!;
        var l = sg.Systems[UrQuanStar];
        Assert.Equal(d.Groups.Select(g => (g.GroupId, g.Race, g.GroupCounter, g.SysLoc, g.Task, g.DestLoc, g.OrbitPos, g.InSystem, g.LocX, g.LocY)),
            l.Groups.Select(g => (g.GroupId, g.Race, g.GroupCounter, g.SysLoc, g.Task, g.DestLoc, g.OrbitPos, g.InSystem, g.LocX, g.LocY)));
        Assert.Equal(d.RandomGroups!.Select(g => (g.Id, g.Race, string.Join(",", g.Ships), string.Join(",", g.Captains))),
            l.RandomGroups!.Select(g => (g.Id, g.Race, string.Join(",", g.Ships), string.Join(",", g.Captains))));
        Assert.Equal((d.Expires, d.Defined, 18), (l.Expires, l.Defined, l.EncounterRace));
        Assert.Equal(new[] { "h" }, l.Present);
        var sol = sg.Systems[SolStar];
        Assert.Empty(sol.Present);
        Assert.Equal(new[] { "g" }, sol.Visitors);
        Assert.Single(sg.Defined["SOL_DEFINED"]);
        // The host flies on among the same groups: no new draw or tasks.
        loaded.TraceSystemRandom = true;
        Run(loaded);
        Assert.DoesNotContain(l.Random?.Places ?? new(), p => p is "BuildGroups" or "GetGroupInfo");
        Assert.NotNull(l.Elements);

        // None: left out.
        var none = CoopWorld();
        Assert.Null(System.Text.Json.Nodes.JsonNode.Parse(HostSave.ToJson(none))!["system_groups"]);
        // An older save with them, or a bad one, is refused.
        void Refused(Action<System.Text.Json.Nodes.JsonNode> change, string why)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            change(node);
            Assert.Contains(why, Assert.Throws<InvalidDataException>(
                () => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes)).Message);
        }
        System.Text.Json.Nodes.JsonNode Ur(System.Text.Json.Nodes.JsonNode n) =>
            n["system_groups"]!["systems"]!.AsArray().First(s => s!["star"]!.GetValue<int>() == UrQuanStar)!;
        Refused(n =>
        {
            n["version"] = 12;
            n["story"]!.AsObject().Remove("fleets");
            n["story"]!.AsObject().Remove("set_aside");
        }, "system groups");
        Refused(n => Ur(n)["star"] = 9999, "star 9999");
        Refused(n => Ur(n)["groups"]![0]!["race"] = "NOBODY_SHIP", "race NOBODY_SHIP");
        Refused(n => Ur(n)["groups"]![0]!["task"] = 16, "task 16");
        Refused(n => Ur(n)["groups"]![0]!["sys_loc"] = 40, "place 40");
        Refused(n => Ur(n)["visitors"]!.AsArray().Add("nobody"), "visitor");
        Refused(n => Ur(n)["expires"] = new System.Text.Json.Nodes.JsonArray(2155, 2, 30), "date");
        Refused(n => Ur(n)["defined"] = "SOL_DEFINED", "own groups");
        Refused(n => Ur(n)["random"]![0]!["ships"]!.AsArray().Add(1), "bad group");
        Refused(n => n["system_groups"]!["defined"]![0]!["special"] = "VUX_DEFINED", "VUX_DEFINED");
    }

    [ContentFact]
    public void TheOtherSpecialSystemsKeepTheirOwnGroupsOrNoneAsUqms()
    {
        int Star(string special) => Systems.Value.Map.Stars.First(s => s.Special == special).Number;
        SystemDraw Enter(GameWorld w, string special)
        {
            w.PutInSystem("h", Star(special));
            Run(w);
            var d = DrawOf(w, Star(special));
            w.PutInHyperspace("h");
            Run(w);
            return d;
        }
        var world = CoopWorld();
        var rules = world.Story!.Rules;
        // The Melnorme's trader at each of their stars: no random draw.
        var mel = Enter(world, "MELNORME3_DEFINED");
        Assert.Equal(rules.RaceIndex("MELNORME_SHIP"), world.SystemGroups!.Defined["MELNORME3_DEFINED"].Single().Race);
        Assert.Null(mel.RandomGroups is { Count: > 0 } ? mel.RandomGroups : null);
        // Tanaka: one ship of one crew, his name, while he's neither recruited nor lost.
        Enter(world, "SHOFIXTI_DEFINED");
        var tanaka = world.SystemGroups.Defined["SHOFIXTI_DEFINED"].Single();
        Assert.Equal((rules.RaceIndex("SHOFIXTI_SHIP"), 1, 5 + 16), (tanaka.Race, tanaka.Ships.Single(), tanaka.Captains.Single()));
        // The Zoq-Fot-Pik scout until they're met.
        Enter(world, "ZOQ_SCOUT_DEFINED");
        Assert.Equal(rules.RaceIndex("ZOQFOTPIK_SHIP"), world.SystemGroups.Defined["ZOQ_SCOUT_DEFINED"].Single().Race);
        var met = CoopWorld();
        met.Story!.Set("MET_ZOQFOT", 1);
        met.Story.Set("SHOFIXTI_RECRUITED", 1);
        Enter(met, "ZOQ_SCOUT_DEFINED");
        Enter(met, "SHOFIXTI_DEFINED");
        Assert.False(met.SystemGroups!.Defined.ContainsKey("ZOQ_SCOUT_DEFINED"));
        Assert.False(met.SystemGroups.Defined.ContainsKey("SHOFIXTI_DEFINED"));
        // No groups and no draw at the bomb's system before the bomb's taken,
        // at the Zoq-Fot-Pik's world in their distress, or at the Sa-Matra's.
        world.Story.Set("ZOQFOT_DISTRESS", 1);
        foreach (var special in new[] { "BOMB_DEFINED", "ZOQFOT_DEFINED", "SAMATRA_DEFINED" })
        {
            world.PutInSystem("h", Star(special));
            Run(world);
            Assert.True(DrawOf(world, Star(special)).Random!.Places!.Count == 0, special);
            Assert.Empty(DrawOf(world, Star(special)).Groups);
            world.PutInHyperspace("h");
            Run(world);
        }
        // The bomb taken, its system is drawn as any.
        world.Story.Set("UTWIG_BOMB", 1);
        world.PutInSystem("h", Star("BOMB_DEFINED"));
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, Star("BOMB_DEFINED")));
    }

    [ContentFact]
    public void TheUtwigAndSupoxAwayOnTheirMissionAreNotAlwaysHome()
    {
        // A race is always at its homeworld, five groups or more, wherever
        // its sphere is (here moved far off); the Supox not while they're
        // away with the Utwig (UTWIG_SUPOX_MISSION 2 to 4).
        int supox = Systems.Value.Map.Stars.First(s => s.Special == "SUPOX_DEFINED").Number;
        GameWorld At(int mission)
        {
            var w = CoopWorld();
            var story = w.Story!;
            story.SetFleet("SUPOX_SHIP", story.Fleet("SUPOX_SHIP") with { LocX = 100, LocY = 100 });
            story.Set("UTWIG_SUPOX_MISSION", mission);
            w.PutInSystem("h", supox);
            Run(w);
            return w;
        }
        var home = At(1);
        var d = DrawOf(home, supox);
        Assert.True(d.RandomGroups!.Count >= 5);
        Assert.All(d.RandomGroups, g => Assert.Equal(home.Story!.Rules.RaceIndex("SUPOX_SHIP"), g.Race));
        Assert.Empty(DrawOf(At(3), supox).RandomGroups!);
        Assert.True(DrawOf(At(5), supox).RandomGroups!.Count >= 5);
    }

    [ContentFact]
    public void AGroupGoesByAPlayerInItsViewBeforeANearerOneInAnother()
    {
        // A group by a planet in the outer view: the player in that planet's
        // inner system is nearer, measured in the outer system, but the one
        // flying in the group's own view is the one it intercepts.
        var (sg, d, system) = Bare(UrQuanStar);
        var planet = SystemGroups.PlanetOuterLocation(system, 0, IpGeometry.MaxZoomRadius);
        // The planet on one side of the group, the outer player on the other.
        int side = planet.X - 7500 >= -14000 ? 1 : -1;
        int gx = planet.X - 1500 * side, gy = planet.Y;
        int ax = gx - 6000 * side;
        var g = Group(sg, d, "URQUAN_SHIP", 1, gx, gy, dest: IpGroup.InterceptPlayer);
        var inner = new SystemFlyer("inner", FlightAt(system, -4000, -4000, inner: 0));
        var outer = new SystemFlyer("outer", FlightAt(system, ax, gy));
        for (int i = 0; i < 8; ++i)
        {
            inner.Flight.Step(ShipInput.None);
            outer.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { inner, outer }, TestContent.Shapes!);
        }
        Assert.Equal(0, g.SysLoc);
        Assert.True(Math.Sign(g.Element!.Velocity.Current().dx) == Math.Sign(ax - gx), "flies at the one in its view");
    }

    [ContentFact]
    public void TwoPlayersTouchingAGroupTogetherOneMeetsItAndTheOtherTheNext()
    {
        // Two groups at one place, and two players on it: the first in the
        // game's order meets the first group; the second passes through
        // that one, met by another (decision 5), and meets the second.
        var (sg, d, system) = Bare(UrQuanStar);
        var first = Group(sg, d, "URQUAN_SHIP", 1, 6000, 0, task: IpGroup.OnStation);
        var second = Group(sg, d, "URQUAN_SHIP", 2, 6000, 0, task: IpGroup.OnStation);
        var a = new SystemFlyer("a", FlightAt(system, 6000, 0));
        var b = new SystemFlyer("b", FlightAt(system, 6000, 0));
        a.Flight.Step(ShipInput.None);
        b.Flight.Step(ShipInput.None);
        sg.Frame(d, system, new[] { a, b }, TestContent.Shapes!);
        Assert.Same(first, a.Met);
        Assert.Same(second, b.Met);
    }

    [ContentFact]
    public void AShipCantTouchTheGroupsOnTheFrameItsViewChanges()
    {
        // flag_ship_preprocess: an element whose view has just changed is
        // NONSOLID that frame (no collisions during a transition).
        var system = Systems.Value[UrQuanStar];
        var at = SystemGroups.PlanetOuterLocation(system, 0, IpGeometry.MaxZoomRadius);
        var f = FlightAt(system, at.X, at.Y);
        for (int i = 0; i < 30 && f.InnerPlanet is null; ++i)
            f.Step(ShipInput.None);
        Assert.Equal(0, f.InnerPlanet);
        // Into the inner system on its first frame, as it appeared: its
        // element made in the outer view sees the change on the next.
        if (f.ContactAppearing)
            f.Step(ShipInput.None);
        Assert.False(f.ContactAppearing);
        Assert.False(f.ContactSolid);
        f.Step(ShipInput.None);
        Assert.True(f.ContactSolid);
    }

    [ContentFact]
    public void TheElementsOrderTurnsEvery42FramesAsTheFlagshipJugglesIt()
    {
        // flag_ship_preprocess: every FLIP_WAIT frames the element after the
        // flagship goes to the end of the list, if it isn't the end already.
        var (sg, d, system) = Bare(UrQuanStar);
        Group(sg, d, "URQUAN_SHIP", 1, -9000, -9000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        Group(sg, d, "URQUAN_SHIP", 2, 9000, -9000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var a = new SystemFlyer("a", FlightAt(system, 0, 9000));
        int[] Order() => d.Elements!.Select(g => g.GroupId).ToArray();
        for (int i = 0; i < 42; ++i)
        {
            a.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { a }, TestContent.Shapes!);
            Assert.Equal(i < 41 ? new[] { 1, 2 } : new[] { 2, 1 }, Order());
        }
    }

    [ContentFact]
    public void AShipTouchesAGroupOnlyWhereItIsOnItsOwnScreen()
    {
        // Two players in the outer view at different zooms: the group goes
        // by the one in its view nearest it, but each ship is judged on its
        // own screen, the group's point at that ship's zoom.
        var (sg, d, system) = Bare(UrQuanStar);
        var g = Group(sg, d, "URQUAN_SHIP", 1, 6952, 338, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var far = new SystemFlyer("far", FlightAt(system, 7300, 0));
        var sun = new SystemFlyer("sun", FlightAt(system, 1737, 73));
        Assert.Equal((4096, 1024), ((int)far.Flight.ZoomRadius, (int)sun.Flight.ZoomRadius));
        // At far's zoom the group's on the screen where sun's ship is at its own.
        var groupOnFars = IpGeometry.LocationToDisplay(new IpPoint(6952, 338), 4096);
        Assert.True(Math.Abs(groupOnFars.X - sun.Flight.Screen.X) <= 2 && Math.Abs(groupOnFars.Y - sun.Flight.Screen.Y) <= 2);
        for (int i = 0; i < 3; ++i)
        {
            far.Flight.Step(ShipInput.None);
            sun.Flight.Step(ShipInput.None);
            sg.Frame(d, system, new[] { far, sun }, TestContent.Shapes!);
            Assert.Null(sun.Met);
            Assert.Null(far.Met);
        }
        Assert.Equal(0, (g.Task & IpGroup.ReformGroup));

        // The other way: a ship on the group at another zoom than the
        // player it goes by (on the group too, but untouchable) meets it.
        // (A place where a ship can be at either zoom: on the screen at the
        // nearer, outside the wider's zoom-in box.)
        int x = Enumerable.Range(0, 60).Select(k => 2000 + 50 * k).First(x =>
            FlightAt(system, x, 300, zoom: 4096).ZoomRadius == 4096 && FlightAt(system, x, 300, zoom: 2048).ZoomRadius == 2048);
        (sg, d, system) = Bare(UrQuanStar);
        g = Group(sg, d, "URQUAN_SHIP", 1, x, 300, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var by = new SystemFlyer("by", FlightAt(system, x, 300, zoom: 4096));
        var on = new SystemFlyer("on", FlightAt(system, x, 300, zoom: 2048));
        Assert.Equal((4096, 2048), ((int)by.Flight.ZoomRadius, (int)on.Flight.ZoomRadius));
        by.Flight.EscapeCounter = 10;
        by.Flight.Step(ShipInput.None);
        on.Flight.Step(ShipInput.None);
        Assert.False(by.Flight.ContactSolid);
        sg.Frame(d, system, new[] { by, on }, TestContent.Shapes!);
        Assert.Null(by.Met);
        Assert.Same(g, on.Met);
        // A ship at the edge of touching: as with the group going by it
        // alone, whatever zoom the player the group goes by is at.
        var seen = new HashSet<bool>();
        for (int k = -48; k <= 48; ++k)
        {
            int sx = x + 5 * k;
            if (FlightAt(system, sx, 300, zoom: 2048).ZoomRadius != 2048)
                continue;
            bool Meets(bool withBy)
            {
                var (sg3, d3, _) = Bare(UrQuanStar);
                // Fleeing: its flight doesn't hang on the player it goes by.
                Group(sg3, d3, "URQUAN_SHIP", 1, x, 300, task: IpGroup.Flee, dest: 1);
                var ship = new SystemFlyer("on", FlightAt(system, sx, 300, zoom: 2048));
                var flyers = new List<SystemFlyer>();
                if (withBy)
                {
                    var other = new SystemFlyer("by", FlightAt(system, x, 300, zoom: 4096));
                    other.Flight.EscapeCounter = 10;
                    other.Flight.Step(ShipInput.None);
                    flyers.Add(other);
                }
                ship.Flight.Step(ShipInput.None);
                flyers.Add(ship);
                sg3.Frame(d3, system, flyers, TestContent.Shapes!);
                return ship.Met is not null;
            }
            bool alone = Meets(false);
            seen.Add(alone);
            Assert.True(alone == Meets(true), $"a ship {5 * k} east of the group");
        }
        Assert.Equal(2, seen.Count);   // the edge is among them
    }

    [ContentFact]
    public void AShipTouchesOnlyTheGroupsInItsOwnView()
    {
        // A group in the outer view, drawn there for the player flying in
        // it, at the screen point where another player's ship is in a
        // planet's inner system: that ship doesn't meet it.
        var (sg, d, system) = Bare(UrQuanStar);
        var g = Group(sg, d, "URQUAN_SHIP", 1, 3000, 3000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var outer = new SystemFlyer("outer", FlightAt(system, -12000, -12000));
        Assert.Equal(8192, (int)outer.Flight.ZoomRadius);
        var at = IpGeometry.LocationToDisplay(new IpPoint(3000, 3000), 8192);
        var place = IpGeometry.DisplayToLocation(at, 8192);
        var inner = new SystemFlyer("inner", FlightAt(system, place.X, place.Y, inner: 0));
        for (int i = 0; i < 3; ++i)
        {
            outer.Flight.Step(ShipInput.None);
            inner.Flight.Step(ShipInput.None);
            Assert.Equal(1, inner.Flight.View);
            sg.Frame(d, system, new[] { outer, inner }, TestContent.Shapes!);
            Assert.Null(inner.Met);
        }
        Assert.Equal(0, (g.Task & IpGroup.ReformGroup));
        // In its view, the same ship at that place meets it.
        var (sg2, d2, _) = Bare(UrQuanStar);
        var g2 = Group(sg2, d2, "URQUAN_SHIP", 1, 3000, 3000, task: IpGroup.InOrbit | IpGroup.IgnoreFlagship, dest: 2);
        var there = new SystemFlyer("there", FlightAt(system, 3000, 3000, zoom: 8192));
        there.Flight.Step(ShipInput.None);
        sg2.Frame(d2, system, new[] { there }, TestContent.Shapes!);
        Assert.Same(g2, there.Met);
    }

    [ContentFact]
    public void GroupsBumpOnlyGroupsOnTheirOwnScreen()
    {
        // ip_group_collision keeps two groups on one screen from running
        // into one icon: the first in the list stops where it was. Each
        // group here flies at the player it goes by, from a pixel's edge,
        // so it moves on the screen. Groups in different views, or in the
        // outer view at different zooms, don't bump.
        var system = Systems.Value[UrQuanStar];
        bool BothMoved((int X, int Y, byte View)[] groups, SystemFlyer[] flyers, Action<List<IpGroup>>? check = null)
        {
            var (sg, d, _) = Bare(UrQuanStar);
            var made = groups.Select((g, i) =>
            {
                var group = Group(sg, d, "URQUAN_SHIP", i + 1, g.X, g.Y, dest: IpGroup.InterceptPlayer);
                group.SysLoc = g.View;
                return group;
            }).ToList();
            foreach (var f in flyers)
                f.Flight.Step(ShipInput.None);
            sg.Frame(d, system, flyers, TestContent.Shapes!);
            check?.Invoke(made);
            return made.Zip(groups).All(x => (x.First.LocX, x.First.LocY) != (x.Second.X, x.Second.Y));
        }
        SystemFlyer At(string id, int x, int y, int? inner = null, int? zoom = null)
        {
            var f = new SystemFlyer(id, FlightAt(system, x, y, inner, zoom));
            if (zoom is int z)
                Assert.Equal(z, (int)f.Flight.ZoomRadius);
            return f;
        }
        // One screen (the outer view at the widest zoom), flying west: they bump.
        Assert.False(BothMoved(new[] { (2926, 0, (byte)0), (2926, 0, (byte)0) },
            new[] { At("o", -12000, 0, zoom: 8192) }));
        // The outer view and a planet's inner system, at the same screen point: no bump.
        Assert.True(BothMoved(new[] { (2926, 0, (byte)0), (2926, 0, (byte)1) },
            new[] { At("o", -12000, 0, zoom: 8192), At("i", -9000, 0, inner: 0) }));
        // The outer view at one zoom, each group going by another player
        // (each flying west at the one nearest it), in one pixel: they bump.
        Assert.Equal(IpGeometry.LocationToDisplay(new IpPoint(2926, -10), 8192), IpGeometry.LocationToDisplay(new IpPoint(2926, 130), 8192));
        Assert.False(BothMoved(new[] { (2926, -10, (byte)0), (2926, 130, (byte)0) },
            new[] { At("p", -12000, -3000, zoom: 8192), At("q", -12000, 3000, zoom: 8192) },
            made => Assert.True(made[0].Element!.Velocity.Current().dy < 0 && made[1].Element!.Velocity.Current().dy > 0,
                "each flies at the player nearest it")));
        // The outer view at two zooms, each group flying east at the player
        // nearest it, at the same screen point at its own zoom: no bump.
        Assert.Equal(IpGeometry.LocationToDisplay(new IpPoint(2889, 0), 2048), IpGeometry.LocationToDisplay(new IpPoint(5777, 0), 4096));
        Assert.True(BothMoved(new[] { (2889, 0, (byte)0), (5777, 0, (byte)0) },
            new[] { At("a", 3500, 0, zoom: 2048), At("b", 7000, 0, zoom: 4096) }));
    }

    [ContentFact]
    public void APlayerLoadedInASystemWithoutItsDrawGetsANewArrivalsDraw()
    {
        // A version-13 save from before the system groups: the player in a
        // system finds it drawn as on arriving.
        var world = CoopWorld();
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        var node = System.Text.Json.Nodes.JsonNode.Parse(HostSave.ToJson(world))!;
        node.AsObject().Remove("system_groups");
        var loaded = HostSave.FromJson(node.ToJsonString(), Systems.Value, TestContent.Shapes);
        loaded.EncounterRaces = world.EncounterRaces;
        loaded.TraceSystemRandom = true;
        loaded.SystemSeeds = star => 1000u + (uint)star;
        loaded.Join("h", "Host");
        Run(loaded);
        var d = DrawOf(loaded, UrQuanStar);
        Assert.Contains("BuildGroups", Drawn(loaded, UrQuanStar));
        Assert.Equal(DrawOf(world, UrQuanStar).RandomGroups!.Select(g => (g.Id, g.Race, g.Ships.Count)),
            d.RandomGroups!.Select(g => (g.Id, g.Race, g.Ships.Count)));
        Assert.NotEmpty(d.Groups);
        Assert.Equal(new[] { "h" }, d.Present);
    }

    [ContentFact]
    public void ADrawWhoseGroupsAllFlewOutIsKeptWithinTheWeek()
    {
        var world = CoopWorld();
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        var d = DrawOf(world, UrQuanStar);
        Assert.NotEmpty(d.Groups);
        foreach (var g in d.Groups)
            g.InSystem = false;   // every one flown out of the system
        world.PutInHyperspace("h");
        Run(world);
        Assert.Empty(d.RandomGroups!);
        world.MoveClockDays(3);
        world.PutInSystem("h", UrQuanStar);
        Run(world);
        Assert.Same(d, DrawOf(world, UrQuanStar));
        Assert.DoesNotContain("BuildGroups", Drawn(world, UrQuanStar));
        Assert.Empty(d.Groups);
    }

    [ContentFact]
    public void TheColonyAfterItsGuardIsDrawnAfreshOnEveryEntry()
    {
        var world = CoopWorld();
        // The probes, met anywhere, draw at every system.
        world.Story!.Set("SLYLANDRO_MULTIPLIER", 1);
        int colony = Systems.Value.Map.Stars.Single(s => s.Special == "START_COLONY_DEFINED").Number;
        world.SystemGroups!.Defined["START_COLONY_DEFINED"] = new() { new GroupShips(1, world.Story.Rules.RaceIndex("URQUAN_SHIP"), Array.Empty<int>()) };
        world.PutInSystem("h", colony);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, colony));
        Assert.Null(DrawOf(world, colony).Defined);
        world.PutInHyperspace("h");
        Run(world);
        world.PutInSystem("h", colony);
        Run(world);
        Assert.Contains("BuildGroups", Drawn(world, colony));
        Assert.Null(DrawOf(world, colony).Defined);
    }
}
