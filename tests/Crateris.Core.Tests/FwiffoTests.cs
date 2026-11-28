// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Planetside;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;
using static Crateris.Core.Tests.StoryPickupsTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Fwiffo on Pluto (the stage 3c plan's item 7; its question 10): the
/// finder's lander losing 8 crew one by one while the world goes on
/// (KillLanderCrewSeq, against UQM's own: tests/golden/lander-killcrew.json),
/// a lander of 8 or fewer dying with nothing found; FOUND_PLUTO_SPATHI set
/// at the pickup and the Spathi's lock the finder's from the touch; his
/// talk as the finder's lander comes back aboard, a meeting with no group
/// and his one ship (crew 1, his name), the ship kept in orbit; his joining
/// the host's fleet, flying off with no room, or his battle; and his talk
/// waiting when it can't come to the finder. The pickup's story against
/// gensol.c is StoryPickupsTests' EveryWorldsNodeSheetReportAndPickupIsUqms
/// (tests/golden/story-nodes.json).
/// </summary>
[Trait("Topic", "story")]
public class FwiffoTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private const string Spathi = "SPATHI_CONVERSATION";

    // ------------------------------------------------------------ the crew loss, against UQM

    [Fact]
    public void LanderCrewLossIsUqms()
    {
        // lander.c's KillLanderCrewSeq: a crew member lost at once, then one every period ticks, each as
        // DeltaLanderCrew (-1, LANDER_INJURED), which no shield stops and which draws nothing at random; it
        // ends after the last wait with crew left, or with none. The port loses each on the first surface
        // frame (24 ticks each) that begins at or after its tick, the lander held meanwhile while the world
        // goes on; a lander killed ends it at once.
        var runs = Golden.Load("lander-killcrew").GetProperty("runs").EnumerateArray().ToList();
        Assert.Equal(8, runs.Count);
        foreach (var run in runs)
        {
            int crew = run.GetProperty("crew").GetInt32(), shields = run.GetProperty("shields").GetInt32();
            int count = run.GetProperty("count").GetInt32(), period = run.GetProperty("period").GetInt32();
            string what = $"{crew} crew, shields {shields}, {count} every {period}";
            Assert.Equal(1, run.GetProperty("seed_same").GetInt32());
            var (surface, lander) = Landed(crew, shields == 15);
            var (twin, twinLander) = Landed(crew, shields == 15);
            (bool Lived, int Frame)? end = null;
            int frame = 0;
            lander.LoseCrew(count, period, lived => end = (lived, frame));
            Assert.True(lander.Seized || end is not null, what);
            // Each death's frame and the crew it leaves (the clock as UQM's sleep is called, after the death).
            var deaths = run.GetProperty("deaths").EnumerateArray()
                .Select(d => (Frame: Ceil(d[0].GetInt32()), Crew: d[1].GetInt32(), Until: d[2].GetInt32())).ToList();
            int CrewAt(int k) => deaths.Where(d => d.Frame <= k).Select(d => d.Crew).DefaultIfEmpty(crew).Last();
            Assert.Equal((0, deaths[0].Crew), (deaths[0].Frame, lander.Crew));   // the first at once
            for (frame = 0; frame < 40 && end is null; ++frame)
            {
                surface.Step(_ => LanderInput.Thrust | LanderInput.TakeOff);   // held: no keys read
                twin.Step(_ => LanderInput.None);
                if (end is null)
                    Assert.True(CrewAt(frame) == lander.Crew, $"{what}: frame {frame}");
                Assert.Equal((twinLander.X, twinLander.Y), (lander.X, lander.Y));
            }
            bool lived = run.GetProperty("lived").GetInt32() == 1;
            Assert.Equal(run.GetProperty("crew_left").GetInt32(), lander.Crew);
            // The end: after the last wait with crew left, or as the last of them dies.
            int endFrame = lived ? Ceil(deaths[^1].Until) : deaths.First(d => d.Crew == 0).Frame;
            Assert.Equal((lived, endFrame), end);
            Assert.False(lander.Seized);
            Assert.Equal(LanderPhase.Ground, lander.Phase);   // never taken off by the keys while held
            // Nothing drawn at random: the twin's surface, with no crew loss, has drawn as many.
            Assert.Equal(twin.Random.Seed, surface.Random.Seed);
        }

        static int Ceil(int ticks) => (ticks + Lander.FrameTicks - 1) / Lander.FrameTicks;

        static (PlanetsideSurface, Lander) Landed(int crew, bool shielded)
        {
            var surface = new PlanetsideSurface(new TfbRandom(7));
            var hold = new ShipHold(new ShipLoadout(1, 1, 0, 0, crew, 0))
            {
                Upgrades = shielded ? LanderUpgrades.All : LanderUpgrades.None,
            };
            hold.LanderLeaves();
            var lander = surface.Land(hold, 300, 100);
            while (lander.Phase != LanderPhase.Ground)
                surface.Step(_ => LanderInput.None);
            return (surface, lander);
        }
    }

    // ------------------------------------------------------------ in the game

    private static GameWorld NewGame(int guests = 1)
    {
        var world = StoryPickupsTests.NewGame(guests);
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.BattleKit = WorldBattleTests.Kit();
        return world;
    }

    private static WorldId Pluto(GameWorld world) => new(Star(world, "SOL_DEFINED"), 8, null);

    // A player's ship in orbit around Pluto, with this much crew aboard.
    private static void OrbitPluto(GameWorld world, string id, int crew = 30)
    {
        var at = Pluto(world);
        world.Find(id)!.Ship.Hold.SetCrew(crew);
        world.PutInOrbit(id, at.Star, at.Planet, at.Moon);
    }

    private static SurfaceElement FwiffoNode(GameWorld world) =>
        world.SurfaceOf(Pluto(world))!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy && e.Node == 0);

    // Keeps Fwiffo's node under a lander, frame by frame, until the lander is up or lost, or enough frames.
    private static void OnFwiffo(GameWorld world, Lander lander, int frames = 60)
    {
        var node = FwiffoNode(world);
        for (int i = 0; i < frames && lander.Phase == LanderPhase.Ground && lander.Crew > 0; ++i)
        {
            node.X = lander.X;
            node.Y = lander.Y;
            Frames(world, 1);
        }
    }

    // Waits for a player's lander to be back aboard (or lost).
    private static void Back(GameWorld world, string id)
    {
        for (int i = 0; i < 400 && world.Find(id)!.Ship.Lander is not null; ++i)
            Frames(world, 1);
        Assert.Null(world.Find(id)!.Ship.Lander);
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Offered(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        return talk.Replies.Any(r => r.Phrase == n);
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The host finds Fwiffo: its lander on his node until it's sent up.
    private static Lander FindHim(GameWorld world, string id)
    {
        var at = Pluto(world);
        var site = world.Find(id)!.Ship.Orbit!.Nodes!.Energy[0];
        var lander = Land(world, id, site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        OnFwiffo(world, lander);
        Assert.True(lander.InTransit, "the lander was sent up");
        return lander;
    }

    [ContentFact]
    public void FindingHimCostsTheFindersLanderEightCrewThenHisTalkStartsAsItComesBackAboard()
    {
        var world = NewGame();
        var at = Pluto(world);
        var story = world.Story!;
        OrbitPluto(world, "host");
        OrbitPluto(world, "guest");
        // His node, sheet and report while he isn't found (gensol.c).
        Assert.Equal(new ReportPlace(at, StoryWorlds.FwiffoReport, 0), world.Host.Ship.Discovery);
        var guest = Land(world, "guest", 10, 10);
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[0];
        var lander = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        Assert.Equal(SurfaceSheet.Fwiffo, FwiffoNode(world).Sheet);
        Assert.Equal(12, lander.Crew);
        // The touch: a crew member lost at once (the shields don't help: Land gives every upgrade), the lander held,
        // and the Spathi's lock the finder's.
        var node = FwiffoNode(world);
        world.TakeReports();
        for (int i = 0; i < 40 && !lander.Seized; ++i)
        {
            node.X = lander.X;
            node.Y = lander.Y;
            Frames(world, 1);
        }
        Assert.True(lander.Seized);
        Assert.Equal("host", world.DialogueLocks!.Holder(Spathi));
        Assert.Equal(0, story.Get("FOUND_PLUTO_SPATHI"));
        var lost = new List<int> { lander.Crew };
        int x = lander.X, y = lander.Y;
        for (int i = 0; i < 40 && lander.Seized; ++i)
        {
            Frames(world, 1, "host", ShipInput.Thrust | ShipInput.TakeOff);   // held: it doesn't move or take off
            Assert.Equal((x, y), (lander.X, lander.Y));
            lost.Add(lander.Crew);
        }
        // One by one (no frame loses two: a world frame has one or two surface frames, a loss every 1.75).
        Assert.Equal(12 - 8, lander.Crew);
        Assert.All(lost.Zip(lost.Skip(1)), p => Assert.InRange(p.First - p.Second, 0, 1));
        // Living through it: found, his report shown to the finder and word of it to the guest, the lander up.
        Assert.Equal(1, story.Get("FOUND_PLUTO_SPATHI"));
        Assert.True(lander.InTransit);
        var reports = world.TakeReports();
        Assert.Contains(("host", new LanderReport(StoryWorlds.FwiffoReport)), reports);
        Assert.Contains(("guest", new LanderReport(StoryWorlds.FwiffoReport, FoundBy: "host")), reports);
        Assert.Equal(new LanderReport(StoryWorlds.FwiffoReport, FoundBy: "host"), guest.Report);
        Assert.Null(world.Host.Ship.Discovery);
        Assert.Equal("host", world.FwiffoFor);
        // Hidden from the scan from now on, though the node stays while the lander takes off.
        Assert.True(StoryWorlds.EnergyHidden("SOL_DEFINED", at, story));
        Assert.Null(StoryWorlds.EnergySheet("SOL_DEFINED", at, story));
        Assert.Null(world.Host.Ship.Meeting);
        // Back aboard: his talk, a meeting with no group, his one ship (crew 1, his own name), the ship in orbit.
        Back(world, "host");
        var m = Assert.IsType<Meeting>(world.MeetingOf("host"));
        Assert.True(m.Fwiffo);
        Assert.Equal(new[] { new EnemyFleetShip(world.Story!.Rules.RaceShip("SPATHI_SHIP").Ship, 1, 5 + 16) }, m.Ships);
        Assert.Equal((at.Star, (8, (int?)null)), (m.Star, m.World));
        Assert.Null(m.Group);
        Assert.Equal(Spathi, m.Conversation);
        Assert.IsType<SpathiScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Talk, m.Step);   // at peace from the start: no encounter screen
        Assert.True(Said(m.Talk, "SORRY_ABOUT_THAT"));
        Assert.NotNull(world.Host.Ship.Orbit);
        Assert.Equal(Activity.Talk, world.Host.Ship.Activity);
        Assert.Null(world.FwiffoFor);
        Assert.Equal(30 - 8, world.Host.Ship.Hold.Crew);   // the 8 lost, the rest back aboard
    }

    [ContentFact]
    public void ALanderOfEightOrFewerDiesAndNothingHappens()
    {
        var world = NewGame();
        var story = world.Story!;
        OrbitPluto(world, "host", crew: 8);
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[0];
        var lander = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        Assert.Equal(8, lander.Crew);
        world.TakeReports();
        OnFwiffo(world, lander);
        Assert.Equal(0, lander.Crew);
        Assert.False(lander.Seized);
        Assert.Equal(0, story.Get("FOUND_PLUTO_SPATHI"));
        Assert.Empty(world.TakeReports());
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        Assert.Null(world.FwiffoFor);
        Back(world, "host");
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Equal(new ReportPlace(Pluto(world), StoryWorlds.FwiffoReport, 0), world.Host.Ship.Discovery);   // as before
    }

    [ContentFact]
    public void HeJoinsTheHostsFleetAloneInHisEluderWhoeverFindsHim()
    {
        var world = NewGame();
        OrbitPluto(world, "guest");
        FindHim(world, "guest");
        Back(world, "guest");
        var m = world.MeetingOf("guest")!;
        var talk = m.Talk!;
        Assert.True(world.ChooseReply("guest", Reply(talk, "hi_there")));
        // Asked about himself until the monsters are told, then "join us" ends the talk.
        string[] asks = { "what_doing_on_pluto_1", "what_doing_on_pluto_2", "what_doing_on_pluto_3", "what_about_moonbase",
            "what_about_other_spathi", "how_many_crew", "really_thousands", "full_of_monsters" };
        foreach (string ask in asks)
            Assert.True(world.ChooseReply("guest", Reply(talk, ask)), ask);
        Assert.True(world.ChooseReply("guest", Reply(talk, "join_us")));
        Assert.True(talk.Ended);
        Assert.True(Said(talk, "WILL_JOIN"));
        var ship = Assert.Single(world.Fleet.Ships);
        Assert.Equal(("SPATHI_SHIP", 1, (byte)(5 + 16)), (ship.Race, ship.Crew, ship.Captain));
        Assert.Equal(2, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.True(world.ClosingDone("guest"));
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.MeetingOf("guest"));
        Assert.Equal(Activity.Orbit, world.Find("guest")!.Ship.Activity);
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        // Another lander back now brings nothing: the field has passed 1.
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.Null(world.Host.Ship.Meeting);
    }

    [ContentFact]
    public void WithNoRoomInTheFleetHeFliesOff()
    {
        var world = NewGame();
        uint draws = 0;
        world.Fleet.Add(world.Story!.Rules, "HUMAN_SHIP", HostFleet.MaxShips, () => draws++);
        OrbitPluto(world, "host");
        FindHim(world, "host");
        Back(world, "host");
        var talk = world.MeetingOf("host")!.Talk!;
        Assert.True(world.ChooseReply("host", Reply(talk, "hi_there")));
        foreach (string ask in new[] { "what_doing_on_pluto_1", "what_doing_on_pluto_2", "what_doing_on_pluto_3",
                     "what_about_moonbase", "what_about_other_spathi", "how_many_crew", "really_thousands", "full_of_monsters" })
            Assert.True(world.ChooseReply("host", Reply(talk, ask)), ask);
        Assert.True(world.ChooseReply("host", Reply(talk, "join_us")));
        Assert.True(Said(talk, "TOO_SCARY"));
        Assert.Equal(Segue.Peace, talk.Segue);
        Assert.DoesNotContain(world.Fleet.Ships, s => s.Race == "SPATHI_SHIP");
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(2, world.Story.Get("FOUND_PLUTO_SPATHI"));
    }

    [ContentFact]
    public void HisBattleIsFoughtFromOrbitAndTheShipIsStillInOrbitAfter()
    {
        var world = NewGame();
        OrbitPluto(world, "host");
        FindHim(world, "host");
        Back(world, "host");
        var m = world.MeetingOf("host")!;
        byte escape = world.Host.Ship.Ip!.EscapeCounter;
        Assert.True(world.ChooseReply("host", Reply(m.Talk!, "we_fight_1")));
        Assert.True(Said(m.Talk!, "OK_WE_FIGHT_AT_PLUTO"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        var enemy = Assert.Single(m.Battle!.Enemies);
        Assert.Equal((1, 5 + 16), (enemy.Starship.CrewLevel, (int)enemy.Starship.CaptainsNameIndex));
        Assert.Equal(Activity.Battle, world.Host.Ship.Activity);
        WorldTalkTests.WinTheBattle(world, m);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.NotNull(world.Host.Ship.Orbit);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(2, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        // Still in orbit, the ship keeps the escape counter it had (UQM's fake load saves and restores it;
        // nothing in orbit counts it down): out of orbit, it touches things at once (review round 1).
        Assert.Equal(escape, world.Host.Ship.Ip!.EscapeCounter);
        Frames(world, 3, "host", ShipInput.Action);
        Assert.Null(world.Host.Ship.Orbit);
        Assert.Equal(0, escape);
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);   // no escape counter to wait out: it touches things at once
    }

    [ContentFact]
    public void RetreatingFromHisBattleLeavesTheShipInOrbitWithItsEscapeCounter()
    {
        var world = NewGame();
        world.Story!.Set("STARBASE_AVAILABLE", 1);   // a retreat allowed (RunAwayAllowed)
        OrbitPluto(world, "host");
        FindHim(world, "host");
        Back(world, "host");
        var m = world.MeetingOf("host")!;
        byte escape = world.Host.Ship.Ip!.EscapeCounter;
        Assert.True(world.ChooseReply("host", Reply(m.Talk!, "we_fight_1")));
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            if (m.Battle!.Enemy.RaceDesc is { } rd)
                rd.CrewLevel = rd.MaxCrew;   // he lives: the battle ends by the retreat
            Frames(world, 1, "host", ShipInput.TakeOff);
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(BattleExit.Retreated, m.Battle!.Fighters[0].Exit);
        Assert.False(world.GameOver);
        Assert.NotNull(world.Host.Ship.Orbit);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(escape, world.Host.Ship.Ip!.EscapeCounter);
    }

    [ContentFact]
    public void WhileAnotherPlayerTalksToTheSpathiTheLanderPassesOverHim()
    {
        var world = NewGame();
        Assert.True(world.DialogueLocks!.TryTake(Spathi, "guest", out _));
        OrbitPluto(world, "host");
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[0];
        var lander = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        world.TakeNotices();
        OnFwiffo(world, lander, 30);
        Assert.False(lander.Seized);
        Assert.Equal(12, lander.Crew);
        Assert.Equal(0, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Contains(world.TakeNotices(), n => n is { PlayerId: "host", Event: NoticeEvent.Talking, By: "guest" });
        // The lock let go, the next touch finds him.
        world.DialogueLocks.Release(Spathi, "guest");
        OnFwiffo(world, lander);
        Assert.Equal(1, world.Story.Get("FOUND_PLUTO_SPATHI"));
    }

    [ContentFact]
    public void IfTheFinderLeavesTheNextLanderBackAboardAnywhereBringsHisTalk()
    {
        var world = NewGame();
        OrbitPluto(world, "guest");
        FindHim(world, "guest");
        world.Leave("guest");
        Assert.Equal(1, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        Assert.Null(world.FwiffoFor);
        // The host, orbiting the Moon (Earth's moon 1), lands and comes back: his talk is the host's, at the Moon.
        int sol = Star(world, "SOL_DEFINED");
        world.Host.Ship.Hold.SetCrew(30);
        world.PutInOrbit("host", sol, 2, 1);
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        var m = Assert.IsType<Meeting>(world.MeetingOf("host"));
        Assert.True(m.Fwiffo);
        Assert.Equal((sol, (2, (int?)1)), (m.Star, m.World));
        Assert.True(Said(m.Talk!, "SORRY_ABOUT_THAT"));
        Assert.NotNull(world.Host.Ship.Orbit);
    }

    [ContentFact]
    public void AWaitingTalkWaitsForTheSpathisLockAndIsDroppedOnceTheFieldHasPassed()
    {
        var world = NewGame();
        var story = world.Story!;
        // A save made before his talk: the field 1, no talk coming to anyone.
        story.Set("FOUND_PLUTO_SPATHI", 1);
        Assert.True(world.DialogueLocks!.TryTake(Spathi, "guest", out _));
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        world.TakeNotices();
        Back(world, "host");
        Assert.Equal("host", world.FwiffoFor);
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Contains(world.TakeNotices(), n => n is { PlayerId: "host", Event: NoticeEvent.Talking, By: "guest" });
        // The lock let go: his talk starts.
        world.DialogueLocks.Release(Spathi, "guest");
        Frames(world, 1);
        Assert.True(world.MeetingOf("host") is { Fwiffo: true });

        // Again, but the guest's Spathi talk was his meanwhile (the field passed 1): dropped.
        world = NewGame();
        story = world.Story!;
        story.Set("FOUND_PLUTO_SPATHI", 1);
        Assert.True(world.DialogueLocks!.TryTake(Spathi, "guest", out _));
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.Equal("host", world.FwiffoFor);
        story.Set("FOUND_PLUTO_SPATHI", 2);
        world.DialogueLocks.Release(Spathi, "guest");
        Frames(world, 1);
        Assert.Null(world.FwiffoFor);
        Assert.Null(world.Host.Ship.Meeting);
    }

    [ContentFact]
    public void AWaitingTalkIsDroppedAsItsShipLeavesOrbit()
    {
        var world = NewGame();
        world.Story!.Set("FOUND_PLUTO_SPATHI", 1);
        Assert.True(world.DialogueLocks!.TryTake(Spathi, "guest", out _));
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.Equal("host", world.FwiffoFor);
        Frames(world, 3, "host", ShipInput.Action);   // out of orbit
        Assert.Null(world.Host.Ship.Orbit);
        Assert.Null(world.FwiffoFor);
        world.DialogueLocks.Release(Spathi, "guest");
        Frames(world, 1);
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Equal(1, world.Story.Get("FOUND_PLUTO_SPATHI"));   // the next lander back aboard brings it
    }

    [ContentFact]
    public void ASaveBeforeHisTalkEndsKeepsTheFieldAtOneAndHisShipIsNotInIt()
    {
        var world = NewGame();
        OrbitPluto(world, "host");
        FindHim(world, "host");
        // Saved with the lander still going up (saved as aboard), then during his talk: the field 1 both times.
        var loaded = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Equal(1, loaded.Story!.Get("FOUND_PLUTO_SPATHI"));
        Back(world, "host");
        Assert.NotNull(world.MeetingOf("host"));
        loaded = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Equal(1, loaded.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(loaded.Host.Ship.Meeting);
        Assert.Null(loaded.FwiffoFor);
    }

    // ------------------------------------------------------------ review round 1

    // His talk joined: asked about himself until the monsters are told, then "join us".
    private static void JoinHim(GameWorld world, string id)
    {
        var talk = world.MeetingOf(id)!.Talk!;
        Assert.True(world.ChooseReply(id, Reply(talk, "hi_there")));
        foreach (string ask in new[] { "what_doing_on_pluto_1", "what_doing_on_pluto_2", "what_doing_on_pluto_3",
                     "what_about_moonbase", "what_about_other_spathi", "how_many_crew", "really_thousands", "full_of_monsters" })
            Assert.True(world.ChooseReply(id, Reply(talk, ask)), ask);
        Assert.True(world.ChooseReply(id, Reply(talk, "join_us")));
        Assert.True(talk.Ended);
    }

    // A lander down on his node, its crew being lost.
    private static Lander Seized(GameWorld world, string id)
    {
        var site = world.Find(id)!.Ship.Orbit!.Nodes!.Energy[0];
        var lander = Land(world, id, site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        var node = FwiffoNode(world);
        for (int i = 0; i < 40 && !lander.Seized; ++i)
        {
            node.X = lander.X;
            node.Y = lander.Y;
            Frames(world, 1);
        }
        Assert.True(lander.Seized);
        return lander;
    }

    [ContentFact]
    public void TheFinderLeavingMidCrewLossFindsNothing()
    {
        var world = NewGame();
        OrbitPluto(world, "guest");
        Seized(world, "guest");
        Assert.Equal("guest", world.DialogueLocks!.Holder(Spathi));
        world.TakeReports();
        world.Leave("guest");
        Frames(world, 20);
        Assert.Null(world.DialogueLocks.Holder(Spathi));
        Assert.Equal(0, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.FwiffoFor);
        Assert.Empty(world.TakeReports());
    }

    [ContentFact]
    public void ALanderKilledMidCrewLossByAnythingElseFindsNothingAndLetsTheLockGo()
    {
        var world = NewGame();
        OrbitPluto(world, "host");
        var lander = Seized(world, "host");
        world.TakeReports();
        lander.KillForTest();   // a hazard or a creature's last blow
        Frames(world, 2);
        Assert.False(lander.Seized);
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        Assert.Equal(0, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Empty(world.TakeReports());
        Back(world, "host");
        Assert.Null(world.Host.Ship.Meeting);
    }

    [ContentFact]
    public void OfTwoLandersTouchingHimAtOnceTheFirstHasHimAndTheOtherPassesOverUntilItDies()
    {
        var world = NewGame();
        OrbitPluto(world, "host");
        OrbitPluto(world, "guest");
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[0];
        var host = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        var guest = Land(world, "guest", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        var node = FwiffoNode(world);
        world.TakeNotices();
        for (int i = 0; i < 40 && !host.Seized && !guest.Seized; ++i)
        {
            guest.PlaceForTest(host.X, host.Y);
            node.X = host.X;
            node.Y = host.Y;
            Frames(world, 1);
        }
        // The host's lander landed first, so is first on the surface: it has him; the guest's passes over.
        Assert.True(host.Seized);
        Assert.False(guest.Seized);
        Assert.Equal("host", world.DialogueLocks!.Holder(Spathi));
        Assert.Contains(world.TakeNotices(), n => n is { PlayerId: "guest", Event: NoticeEvent.Talking, By: "Host" });
        Assert.Equal(12, guest.Crew);
        host.KillForTest();
        for (int i = 0; i < 60 && !guest.InTransit && guest.Crew > 0; ++i)
        {
            node.X = guest.X;
            node.Y = guest.Y;
            Frames(world, 1);
        }
        Assert.True(guest.InTransit);
        Assert.Equal(12 - 8, guest.Crew);
        Assert.Equal(1, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Equal("guest", world.FwiffoFor);
    }

    [ContentFact]
    public void ThePlayerLeavingMidTalkLeavesTheFieldAtOneForTheNextLanderBack()
    {
        var world = NewGame();
        OrbitPluto(world, "guest");
        FindHim(world, "guest");
        Back(world, "guest");
        Assert.NotNull(world.MeetingOf("guest"));
        world.Leave("guest");
        Assert.Equal(1, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        Assert.Null(world.FwiffoFor);
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.True(world.MeetingOf("host") is { Fwiffo: true });
    }

    [ContentFact]
    public void ThePlayerLeavingMidBattleLeavesHisTalkOver()
    {
        // His talk's end (post_encounter_func, before the battle as UQM's InitCommunication) set the field 2 and
        // let the lock go: leaving his battle brings no talk again.
        var world = NewGame();
        OrbitPluto(world, "guest");
        FindHim(world, "guest");
        Back(world, "guest");
        var m = world.MeetingOf("guest")!;
        Assert.True(world.ChooseReply("guest", Reply(m.Talk!, "we_fight_1")));
        Assert.True(world.ClosingDone("guest"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(2, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.DialogueLocks!.Holder(Spathi));
        world.Leave("guest");
        Frames(world, 2);
        Assert.Equal(MeetingStep.Done, m.Step);
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Null(world.FwiffoFor);
    }

    [ContentFact]
    public void ASaveAtHisClosingLinesCarriesTheTalksEndAndNoMeeting()
    {
        var world = NewGame();
        OrbitPluto(world, "host");
        FindHim(world, "host");
        Back(world, "host");
        JoinHim(world, "host");
        var m = world.MeetingOf("host")!;
        Assert.Equal(MeetingStep.Closing, m.Step);
        var loaded = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Null(loaded.Host.Ship.Meeting);
        Assert.NotNull(loaded.Host.Ship.Orbit);
        Assert.Equal(new WorldId(Pluto(world).Star, 8, null), loaded.Host.Ship.OrbitedWorld);
        Assert.Equal(2, loaded.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Contains(loaded.Fleet.Ships, s => s.Race == "SPATHI_SHIP" && s.Crew == 1 && s.Captain == 21);
        Assert.Null(loaded.FwiffoFor);
        // The live meeting goes on waiting for its player.
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void AWaitingTalkIsDroppedAsItsPlayerLeaves()
    {
        var world = NewGame();
        world.Story!.Set("FOUND_PLUTO_SPATHI", 1);
        Assert.True(world.DialogueLocks!.TryTake(Spathi, "host", out _));
        OrbitPluto(world, "guest");
        Land(world, "guest", 10, 10);
        Frames(world, 1, "guest", ShipInput.TakeOff);
        Back(world, "guest");
        Assert.Equal("guest", world.FwiffoFor);
        world.Leave("guest");
        Assert.Null(world.FwiffoFor);
        world.DialogueLocks.Release(Spathi, "host");
        Frames(world, 2);
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Null(world.DialogueLocks.Holder(Spathi));
        // The next lander back aboard brings it.
        OrbitPluto(world, "host");
        Land(world, "host", 10, 10);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.True(world.MeetingOf("host") is { Fwiffo: true });
    }

    [ContentFact]
    public void AfterHisTalkTheOrbitsStartIsDoneAfreshAsUqmsReloadInOrbit()
    {
        // UQM's fake load comes back into the system in orbit, running the world's generateOrbital again: a ship
        // orbiting the Ilwrath's ruins, its one report a visit used up, has it again after his talk.
        var world = NewGame();
        var story = world.Story!;
        Extinct(story, "ILWRATH_SHIP");
        story.Set("FOUND_PLUTO_SPATHI", 1);
        var at = new WorldId(Star(world, "ILWRATH_DEFINED"), 0, null);
        world.Host.Ship.Hold.SetCrew(30);
        world.PutInOrbit("host", at.Star, at.Planet, at.Moon);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[3];
        var lander = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        StayOn(world, lander, world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy && e.Node == 3), 5);
        Assert.Null(world.Host.Ship.Discovery);   // the ruins' report shown: gone for the visit
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.True(world.MeetingOf("host") is { Fwiffo: true });
        Assert.Null(world.Host.Ship.Discovery);
        JoinHim(world, "host");
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
    }

    // ------------------------------------------------------------ review round 2

    [ContentFact]
    public void NoLanderGoesDownDuringHisTalkOrHisBattle()
    {
        // His talk and battle keep the ship in orbit, the orbit entered: a landing asked for then is refused
        // (a request that came as they began), so no crew is aboard a lander while the battle keeps the ship's.
        var world = NewGame();
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        OrbitPluto(world, "host");
        FindHim(world, "host");
        Back(world, "host");
        var m = world.MeetingOf("host")!;
        Assert.Equal(DispatchRefusal.InTalk, world.Dispatch("host", 10, 10));
        Assert.Null(world.Host.Ship.Lander);
        int crew = world.Host.Ship.Hold.Crew;
        Assert.True(world.ChooseReply("host", Reply(m.Talk!, "we_fight_1")));
        Assert.Equal(DispatchRefusal.InTalk, world.Dispatch("host", 10, 10));   // at the closing lines
        Assert.True(world.ClosingDone("host"));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(DispatchRefusal.InTalk, world.Dispatch("host", 10, 10));
        Assert.Null(world.Host.Ship.Lander);
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            if (m.Battle!.Enemy.RaceDesc is { } rd)
                rd.CrewLevel = rd.MaxCrew;
            Frames(world, 1, "host", ShipInput.TakeOff);
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(BattleExit.Retreated, m.Battle!.Fighters[0].Exit);
        Assert.Equal(crew, world.Host.Ship.Hold.Crew);   // no crew made twice by a lander out meanwhile
        Assert.Equal("Not now: the ship is in a talk or a battle.", PlayerSession.RefusalMessage(DispatchRefusal.InTalk));
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 10, 10));   // over: the lander goes
    }

    // A ship orbiting the Ilwrath's ruins (their race extinct), its one report of the visit used up, with his talk
    // coming to it (the field 1, its lander back aboard): returns the world and the meeting.
    private static (WorldId At, Meeting M) AtRuinsWithHisTalk(GameWorld world, params string[] alsoOrbiting) =>
        AtRuinsWithHisTalkFor(world, "host", alsoOrbiting);

    // As AtRuinsWithHisTalk, his talk come to the finder named.
    private static (WorldId At, Meeting M) AtRuinsWithHisTalkFor(GameWorld world, string finder, params string[] alsoOrbiting)
    {
        var story = world.Story!;
        Extinct(story, "ILWRATH_SHIP");
        story.Set("STARBASE_AVAILABLE", 1);   // a retreat allowed
        story.Set("FOUND_PLUTO_SPATHI", 1);
        var at = new WorldId(Star(world, "ILWRATH_DEFINED"), 0, null);
        foreach (string id in alsoOrbiting)
            world.PutInOrbit(id, at.Star, at.Planet, at.Moon);
        var ship = world.Find(finder)!.Ship;
        ship.Hold.SetCrew(30);
        world.PutInOrbit(finder, at.Star, at.Planet, at.Moon);
        var site = ship.Orbit!.Nodes!.Energy[3];
        var lander = Land(world, finder, site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        StayOn(world, lander, world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy && e.Node == 3), 5);
        Assert.Null(ship.Discovery);
        Frames(world, 1, finder, ShipInput.TakeOff);
        Back(world, finder);
        var m = Assert.IsType<Meeting>(world.MeetingOf(finder));
        Assert.True(m.Fwiffo);
        Assert.True(world.ChooseReply(finder, Reply(m.Talk!, "we_fight_1")));
        Assert.True(world.ClosingDone(finder));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Null(ship.Discovery);
        return (at, m);
    }

    [ContentFact]
    public void AfterHisBattleWonOrRetreatedFromTheOrbitsStartIsDoneAfresh()
    {
        // Won.
        var world = NewGame();
        var (at, m) = AtRuinsWithHisTalk(world);
        byte escape = world.Host.Ship.Ip!.EscapeCounter;
        WorldTalkTests.WinTheBattle(world, m);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
        Assert.Equal(escape, world.Host.Ship.Ip!.EscapeCounter);

        // Retreated from.
        world = NewGame();
        (at, m) = AtRuinsWithHisTalk(world);
        escape = world.Host.Ship.Ip!.EscapeCounter;
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            if (m.Battle!.Enemy.RaceDesc is { } rd)
                rd.CrewLevel = rd.MaxCrew;
            Frames(world, 1, "host", ShipInput.TakeOff);
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(BattleExit.Retreated, m.Battle!.Fighters[0].Exit);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
        Assert.Equal(escape, world.Host.Ship.Ip!.EscapeCounter);
    }

    [ContentFact]
    public void LeavingHisBattleEarlyWhileAnotherFightsOnLeavesTheShipInOrbitAsItLeaves()
    {
        var world = NewGame();
        var (at, m) = AtRuinsWithHisTalk(world, "guest");
        var wb = m.Battle!;
        byte escape = world.Host.Ship.Ip!.EscapeCounter;
        // The guest leaves orbit there and joins his battle, at the world's place in the planet's inner view.
        Frames(world, 3, "guest", ShipInput.Action);
        Assert.Null(world.Find("guest")!.Ship.Orbit);
        Assert.True(world.LetIntoBattle("guest"));
        for (int i = 0; i < 20 && world.Find("guest")!.Ship.Battle != wb; ++i)
            Frames(world, 1);
        Assert.Equal(wb, world.Find("guest")!.Ship.Battle);
        // The host runs away; the guest fights on, everyone kept alive.
        for (int i = 0; i < 300 && world.Host.Ship.Battle == wb; ++i)
        {
            foreach (var f in wb.Fighters)
                if (f.Starship.RaceDesc is { } own)
                    own.CrewLevel = own.MaxCrew;
            if (wb.Enemy.RaceDesc is { } rd)
                rd.CrewLevel = rd.MaxCrew;
            Frames(world, 1, "host", ShipInput.TakeOff);
        }
        Assert.Null(world.Host.Ship.Battle);
        Assert.Null(wb.Outcome);   // still going, the guest in it
        Assert.True(m.LeftBattleEarly);
        Assert.Equal(MeetingStep.Battle, m.Step);
        // Back in orbit as it leaves: its escape counter as it was, the orbit's start done afresh then.
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(escape, world.Host.Ship.Ip!.EscapeCounter);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
        // Back in orbit, it can land while the guest fights on (review round 3), the lander touching the ruins
        // (the place moved on: the report shown) and coming back with its crew.
        int crew = world.Host.Ship.Hold.Crew;
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[5];
        // (Land asserts the dispatch is accepted: DispatchRefusal.None, not InTalk.)
        var lander = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        Assert.Equal(MeetingStep.Battle, m.Step);
        StayOn(world, lander, world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy && e.Node == 5), 5);
        Assert.Null(world.Host.Ship.Discovery);
        Frames(world, 1, "host", ShipInput.TakeOff);
        Back(world, "host");
        Assert.Equal(crew, world.Host.Ship.Hold.Crew);
        Assert.Equal(MeetingStep.Battle, m.Step);   // still during the guest's battle
        Assert.Equal(2, world.Story!.Get("FOUND_PLUTO_SPATHI"));
        Assert.Null(world.FwiffoFor);   // no new talk brought (the field is 2)
        // The battle's end later doesn't make the place again, nor change the crew.
        WorldTalkTests.WinTheBattle(world, m);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.Host.Ship.Meeting);
        Assert.Null(world.Host.Ship.Discovery);
        Assert.Equal(crew, world.Host.Ship.Hold.Crew);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void AFinderGoneFromHisBattleAloneFindsTheOrbitsStartDoneOnRejoining()
    {
        // Review round 5: alone in his battle, the guest leaving the game ends it; the battle's end does the
        // orbit's start afresh for its ship, kept in orbit, so it has the world's report place on rejoining.
        var world = NewGame();
        var (at, m) = AtRuinsWithHisTalkFor(world, "guest");
        var guest = world.Find("guest")!;
        world.Leave("guest");
        Frames(world, 2);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.False(m.LeftBattleEarly);
        Assert.Equal(at, guest.Ship.OrbitedWorld);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), guest.Ship.Discovery);
        Assert.Equal(JoinResult.Rejoined, world.Join("guest", "Guest"));
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), guest.Ship.Discovery);
    }

    [ContentFact]
    public void AFinderGoneFromHisBattleWhileAnotherFightsOnFindsTheOrbitsStartDoneOnRejoining()
    {
        // Review round 4: the guest's talk, the host joining his battle; the guest leaves the game, the host
        // fighting on. Its ship stays in orbit there, the orbit's start done afresh as it leaves (UQM's reload in
        // orbit), so it has the world's report place on rejoining; the battle's end doesn't do it again.
        var world = NewGame();
        var (at, m) = AtRuinsWithHisTalkFor(world, "guest", "host");
        var wb = m.Battle!;
        var guest = world.Find("guest")!;
        Frames(world, 3, "host", ShipInput.Action);
        Assert.Null(world.Host.Ship.Orbit);
        Assert.True(world.LetIntoBattle("host"));
        for (int i = 0; i < 20 && world.Host.Ship.Battle != wb; ++i)
            Frames(world, 1);
        Assert.Equal(wb, world.Host.Ship.Battle);
        world.Leave("guest");
        Frames(world, 2);
        Assert.Null(wb.Outcome);   // still going, the host in it
        Assert.True(m.LeftBattleEarly);
        Assert.Null(guest.Ship.Battle);
        Assert.Equal(at, guest.Ship.OrbitedWorld);
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), guest.Ship.Discovery);
        WorldTalkTests.WinTheBattle(world, m);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(JoinResult.Rejoined, world.Join("guest", "Guest"));
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), guest.Ship.Discovery);
        Assert.Equal(Activity.Orbit, guest.Ship.Activity);
    }
}
