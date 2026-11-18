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
                        // Back as from a talk without a battle (GameWorld.MeetInSystem).
                        game.Groups.AfterMeeting(d, met);
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
}
