// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.MeetingTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Groups of alien ships and meetings with them on the wire
/// (docs/plans/stage-3b.md, item 10): each player sent the groups it sees
/// in its snapshots, as deltas; a guest's meeting sent to it (the encounter
/// screen's state), its choice sent back, the meeting's talk and its
/// closing lines (the drone's message) sent as a talk's, its replies back;
/// the battle-won screen's tally told those it's for; the protocol's version.
/// </summary>
[Trait("Topic", "world")]
public class MeetingWireTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private const int SolStar = 82, Earth = 2;

    // A host session with two guests (peers 1 and 2) in hyperspace, the
    // host at (5800, 6100) and each guest 600 units further east, every
    // race's groups kept, no day's roll, the battles' content and the
    // dialogue locks; each race's talk the test's (MeetingTests.TestTalk:
    // a line, one reply, then its end), the last made kept.
    private sealed class Game
    {
        public required HostSession Host;
        public readonly List<TestScript> Made = new();
        public Segue Init = Segue.Hostile, End = Segue.Peace;
        public readonly List<string> Log = new();
        // A race's talk of the test's own (by race id), instead of TestTalk's.
        public readonly Dictionary<string, Func<uint, Talk>> Own = new();
        public GameWorld World => Host.World;
    }

    private static Game NewGame(int guests = 2, bool testTalks = true)
    {
        var systems = SystemGroupTests.Systems.Value;
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        var game = new Game { Host = host };
        host.Logged += game.Log.Add;
        var world = host.World;
        for (int i = 1; i <= guests; ++i)
        {
            Assert.Equal(JoinResult.Joined, host.Hello(i, $"g{i}", $"Guest {i}", Protocol.Version).Result);
            world.UndockShown($"g{i}");
        }
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        world.BattleKit = WorldBattleTests.Kit();
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        if (testTalks)
        {
            world.EncounterRaces = world.Story.Rules.Races.ToHashSet();
            world.MeetingTalks = (_, race, seed) =>
            {
                if (game.Own.TryGetValue(world.Story!.Rules.Races[race], out var own))
                    return own(seed);
                var (talk, script) = TestTalk(world.Story!, game.Init, game.End, seed);
                game.Made.Add(script);
                return talk;
            };
        }
        short x = 5800;
        foreach (var p in world.Players)
        {
            p.Ship.Flight.PlaceAtUniverse(x, 6100);
            p.Ship.Flight.Stop();
            p.Ship.Flight.StarJustLeft = null;
            x += 600;
        }
        return game;
    }

    // MeetingTests.TestTalk with voice clips, as the content's lines have
    // (a line without one is spliced into the one before).
    private static (Talk Talk, TestScript Script) TestTalk(Story.StoryState story, Segue init, Segue end, uint seed)
    {
        var plain = ConversationScripts.Load(DataDir)["comandr"].Description with { Ambient = Array.Empty<CommAnimation>() };
        var phrases = new ConversationScript("probe", "init_probe_comm", plain, new[] { "HELLO", "BYE", "ask" });
        var lines = Content.Conversation.Parse("#(HELLO) hello.ogg\nHello\n#(BYE) bye.ogg\nBye\n#(ask)\nWhat?\n"u8);
        var script = new TestScript(phrases, init, end);
        var talk = new Talk(script, plain, lines, 2, story, new Dictionary<string, int>(),
            new TalkTests.TestShip { Crew = 10, CrewCapacity = 50, Fuel = 100, FuelCapacity = 6000, Landers = 1 },
            new GameClock(2155, 2, 17), seed, _ => true);
        return (talk, script);
    }

    // A talk offering a set of replies again and again (sets of them), then over in peace.
    private sealed class RepeatScript : CommScript
    {
        private int _left;

        public RepeatScript(ConversationScript phrases, int sets) : base(phrases) => _left = sets;

        protected internal override void Init() => T.SetSegue(Segue.Peace);

        protected internal override void Intro() => Again(0);

        private void Again(int r)
        {
            if (_left-- > 0)
            {
                NPCPhrase("HELLO");
                Response("ask", Again);
            }
            else
                NPCPhrase("BYE");
        }
    }

    private static Talk RepeatTalk(Story.StoryState story, int sets, uint seed)
    {
        var plain = ConversationScripts.Load(DataDir)["comandr"].Description with { Ambient = Array.Empty<CommAnimation>() };
        var phrases = new ConversationScript("probe", "init_probe_comm", plain, new[] { "HELLO", "BYE", "ask" });
        var lines = Content.Conversation.Parse("#(HELLO) hello.ogg\nHello\n#(BYE) bye.ogg\nBye\n#(ask)\nWhat?\n"u8);
        return new Talk(new RepeatScript(phrases, sets), plain, lines, 2, story, new Dictionary<string, int>(),
            new TalkTests.TestShip { Crew = 10, CrewCapacity = 50, Fuel = 100, FuelCapacity = 6000, Landers = 1 },
            new GameClock(2155, 2, 17), seed, _ => true);
    }

    // Every set of replies a meeting's talk has offered, by its number in the game.
    private static IEnumerable<int> Sets(Meeting m) => Enumerable.Range(0, m.Talk!.Offers).Select(i => m.Talk.RoundOf(i, m.FirstRound));

    [ContentFact]
    public void EverySetOfRepliesInTheGameHasANumberOfItsOwn()
    {
        // Two meetings' talks side by side, each offering set after set: once
        // each counted on from its own first round, so they shared numbers.
        var game = NewGame(guests: 1);
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        game.Own["SPATHI_SHIP"] = seed => RepeatTalk(world.Story!, 6, seed);
        game.Own["ZOQFOTPIK_SHIP"] = seed => RepeatTalk(world.Story!, 6, seed);
        GroupBy(world, "h", "SPATHI_SHIP");
        FramesUntil(host, () => world.MeetingOf("h") is { Step: MeetingStep.Talk }, 300, g1);
        GroupBy(world, "g1", "ZOQFOTPIK_SHIP");
        FramesUntil(host, () => world.MeetingOf("g1") is { Step: MeetingStep.Talk }, 300, g1);
        var mh = world.MeetingOf("h")!;
        var mg = world.MeetingOf("g1")!;
        for (int i = 0; i < 6; ++i)
        {
            Assert.True(host.ChooseReply("h", mh.Round, 0));
            g1.Session.PressMenu(MenuKey.Select);   // as its client says it, the round it was shown
            Frame(host, g1);
            Frame(host, g1);
        }
        var all = Sets(mh).Concat(Sets(mg)).ToList();
        Assert.Equal(12, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(all.Count - 1, all.Max() - all.Min());   // one after another: the counter skips none
        // g1's replies all went to its own talk, by its rounds: over, its closing lines on its screen (item 11).
        Assert.Equal(MeetingStep.Closing, world.MeetingOf("g1")?.Step);
        Assert.True(mg.Talk!.Ended);
    }

    // A group of a race in hyperspace by a player (as one kept from before,
    // its element made on the next frame), dx universe units east.
    private static HyperEncounter GroupBy(GameWorld world, string id, string race, int ships = 2, short dx = 10)
    {
        var f = world.Find(id)!.Ship.Flight;
        var e = new HyperEncounter
        {
            Race = world.Story!.Rules.RaceIndex(race), Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + dx), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + dx)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story.Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    // A guest's side: its session, fed what the host sends its peer.
    private sealed class Client
    {
        public required long Peer;
        public required PlayerSession Session;
        public readonly List<MeetingView?> Meetings = new();
        public readonly List<TalkView?> Talks = new();
        public readonly List<BattleNotice> Notices = new();
        public readonly List<string> Said = new();   // the session's log
    }

    private static Client NewClient(long peer)
    {
        var c = new Client
        {
            Peer = peer,
            Session = new PlayerSession(SystemGroupTests.Systems.Value, null, $"g{peer}", TestContent.Shapes.HyperFlagship.Count),
        };
        c.Session.Logged += c.Said.Add;
        return c;
    }

    // One host frame as the game runs it: the host's step (its own closing
    // lines gone through at once, as with no screen to play them on), then
    // each guest sent its snapshot (a delta from the one it said it has),
    // its meeting and talk if they changed, and its notices; then what each
    // sends back (its encounter screen's choice, its reply, its closing
    // lines gone through: a frame's time passing on a screen that doesn't
    // play them, so after the last replay's three seconds).
    private static WorldSnapshot Frame(HostSession host, params Client[] clients)
    {
        if (host.World.MeetingOf(host.HostId) is { Step: MeetingStep.Closing } own)
            host.ClosingDone(host.HostId, own.Id);
        var snapshot = host.Step(ShipInput.None);
        foreach (var c in clients)
        {
            if (host.SnapshotFor(c.Peer) is { } data)
                c.Session.ReceiveSnapshot(data);
            host.GuestInput(c.Peer, snapshot.Frame + 1, 0, c.Session.Snapshot?.Frame ?? -1);
        }
        Client Of(long peer) => clients.Single(c => c.Peer == peer);
        foreach (var (peer, data) in host.MeetingUpdates())
            if (clients.Any(c => c.Peer == peer))
            {
                var view = MeetingView.Decode(data);
                Of(peer).Meetings.Add(view);
                Of(peer).Session.ShowMeeting(view);
            }
        foreach (var (peer, data) in host.TalkUpdates())
            if (clients.Any(c => c.Peer == peer))
            {
                var view = TalkView.Decode(data);
                Of(peer).Talks.Add(view);
                Of(peer).Session.ShowTalk(view);
            }
        foreach (var (peer, notice) in host.TakeBattleNotices().Guests)
            if (clients.Any(c => c.Peer == peer))
            {
                var sent = BattleNotice.Decode(notice.Encode());
                Of(peer).Notices.Add(sent);
                Of(peer).Session.ShowBattleNotice(sent);
            }
        foreach (var c in clients)
        {
            if (c.Session.TakeEncounterChoice() is var (meeting, action))
                host.GuestEncounterChoice(c.Peer, meeting, (int)action);
            if (c.Session.TakeReply() is var (round, index))
                host.GuestChooseReply(c.Peer, round, index);
            c.Session.FollowClosing(false, FrameSeconds);
            if (c.Session.TakeClosingDone() is int closed)
                host.GuestClosingDone(c.Peer, closed);
        }
        return snapshot;
    }

    // A world frame's time on a client (60 a second).
    private const double FrameSeconds = 1.0 / 60;

    private static void FramesUntil(HostSession host, Func<bool> done, int most, params Client[] clients)
    {
        for (int i = 0; i < most && !done(); ++i)
            Frame(host, clients);
        Assert.True(done(), "it didn't come to that");
    }

    // ---- groups in the snapshot

    [ContentFact]
    public void EachPlayerIsSentTheGroupsOnItsScreenInHyperspaceAsDeltas()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        var e = GroupBy(world, "g1", "SPATHI_SHIP", dx: 55);
        var radar = GroupBy(world, "g2", "SPATHI_SHIP", dx: 300);   // in g2's span, on no one's screen
        int spathi = world.Story!.Rules.RaceIndex("SPATHI_SHIP");
        var last = Frame(host, g1, g2);
        for (int i = 0; i < 20 && e.Element is not { Drawn: true }; ++i)
            last = Frame(host, g1, g2);
        Assert.True(e.Element!.Drawn);
        int deltas = g1.Session.SnapshotsDelta;
        for (int i = 0; i < 6; ++i)
        {
            // g1 is sent the group on its screen, as the world has it; g2,
            // 600 units off, the other, in its span but on no one's screen
            // (its radar's only: hidden); the host, 655 off the first,
            // nothing.
            var seen = Assert.Single(g1.Session.Snapshot!.Groups!);
            Assert.Equal((e.LogX, e.LogY), (e.Element.NextX, e.Element.NextY));
            Assert.Equal(new GroupSnapshot(e.SnapshotId, null, spathi, 0, e.LogX, e.LogY, e.Element.Frame, e.TransitionState), seen);
            Assert.Equal(GroupSnapshot.SeenBy(last, "g1"), g1.Session.Snapshot.Groups);
            Assert.Equal(g1.Session.Snapshot.Groups, g1.Session.Groups);
            Assert.Equal(new GroupSnapshot(radar.SnapshotId, null, spathi, 0, radar.LogX, radar.LogY, radar.Element!.Frame,
                radar.TransitionState, GroupSnapshotFlags.Hidden), Assert.Single(g2.Session.Snapshot!.Groups!));
            Assert.Empty(GroupSnapshot.SeenBy(last, "h"));
            Assert.Equal(new[] { seen, g2.Session.Snapshot.Groups![0] }, last.Groups!);
            last = Frame(host, g1, g2);
        }
        Assert.True(g1.Session.SnapshotsDelta > deltas);   // sent as deltas, the group's moves with them
        Assert.NotEqual(0, e.SnapshotId);
        // It flies at g1 and meets it: g1 then sees no groups (it's at the encounter screen).
        FramesUntil(host, () => world.MeetingOf("g1") is { Step: MeetingStep.Encounter }, 300, g1, g2);
        Frame(host, g1, g2);
        Assert.Null(g1.Session.Snapshot!.Groups);
        Assert.Empty(g1.Session.Groups);
    }

    [ContentFact]
    public void APlayerFlyingInASystemIsSentTheGroupsInItsView()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        world.PutInOrbit("g1", SolStar, 3);
        world.Find("g1")!.Ship.Ip!.ReturnFromOrbit();   // in Mars's inner system (Earth's has the drone)
        world.PutInOrbit("g2", SolStar, 4);
        world.Find("g2")!.Ship.Ip!.ReturnFromOrbit();   // in Jupiter's
        world.PutInOrbit("h", SolStar, 3);
        world.Host.Ship.Ip!.ReturnFromOrbit();   // the host in Mars's too
        Frame(host, g1, g2);
        world.AddEncounterNear("g1", world.Story!.Rules.RaceIndex("SPATHI_SHIP"), 1);
        var d = world.SystemGroups!.Systems[SolStar];
        var spathi = d.Groups.Single(g => g.Race == world.Story.Rules.RaceIndex("SPATHI_SHIP"));
        spathi.LocX += 3000;   // further off, so it flies a while before it meets g1
        var last = Frame(host, g1, g2);
        last = Frame(host, g1, g2);
        Assert.Equal(Activity.InnerSystem, world.Find("g1")!.Ship.Activity);
        var view = world.Find("g1")!.Ship.Ip!.View;
        var seen = g1.Session.Snapshot!.Groups!.Single(g => g.Id == spathi.GroupId);
        Assert.Equal(new GroupSnapshot(spathi.GroupId, SolStar, spathi.Race, view, spathi.LocX, spathi.LocY), seen);
        // Every group sent is in g1's view of Sol; g2, in Jupiter's, sees none of them.
        Assert.All(g1.Session.Snapshot.Groups!, g => Assert.Equal((SolStar, (int)view), (g.Star!.Value, g.View)));
        Assert.Equal(GroupSnapshot.SeenBy(last, "g1"), g1.Session.Snapshot.Groups);
        Assert.DoesNotContain(g2.Session.Snapshot!.Groups ?? Array.Empty<GroupSnapshot>(), g => g.View == view);
        // Flying, it moves on g1's screen from frame to frame, sent as deltas.
        var before = seen;
        for (int i = 0; i < 5; ++i)
            Frame(host, g1, g2);
        var after = g1.Session.Snapshot!.Groups!.Single(g => g.Id == spathi.GroupId);
        Assert.NotEqual((before.X, before.Y), (after.X, after.Y));
        Assert.Equal((spathi.LocX, spathi.LocY), ((short)after.X, (short)after.Y));
        // It meets one of the two in Mars's view; the other sees it met, held where it is.
        FramesUntil(host, () => world.MeetingOf("g1") is not null || world.MeetingOf("h") is not null, 2000, g1, g2);
        string other = world.MeetingOf("g1") is not null ? "h" : "g1";
        last = Frame(host, g1, g2);
        var met = GroupSnapshot.SeenBy(last, other).Single(g => g.Id == spathi.GroupId);
        Assert.Equal(GroupSnapshotFlags.Met, met.Flags & GroupSnapshotFlags.Met);
    }

    [Fact]
    public void GroupsRoundTripInFullAndAsDeltasAndBadOnesAreRefused()
    {
        static ShipSnapshot Ship(string id, Activity activity, int ux, int uy, int? star = null, int? inner = null) =>
            new(id, id, id == "a", ShipKind.Escort, 0, 0, ux, uy, 0, 0, 0, 100, 100, false, false, activity, star, inner);
        var ships = new[]
        {
            Ship("a", Activity.Hyperspace, 5000, 5000), Ship("b", Activity.InnerSystem, 0, 0, 7, 2),
            Ship("c", Activity.Orbit, 0, 0, 7, 2),
        };
        int Log(int u) => Uqm.Units.UniverseToLogX((short)u);
        var near = new GroupSnapshot(4, null, 3, 0, Log(5030), Uqm.Units.UniverseToLogY(5000), 31, 0);
        var far = new GroupSnapshot(5, null, 3, 0, Log(5600), Uqm.Units.UniverseToLogY(5000), 31, 0);   // out of a's span
        var inView = new GroupSnapshot(1, 7, 9, 3, -1200, 800, Flags: GroupSnapshotFlags.Met);
        var outer = new GroupSnapshot(2, 7, 9, 0, 300, 300, Flags: GroupSnapshotFlags.Hidden);
        var first = new WorldSnapshot(10, 2155, 2, 17, ships, Groups: new[] { near, far, inView, outer });
        var writer = new WireWriter();

        // Each viewer is sent what it sees: a ship in hyperspace those in its span, one in a system those in its view, one in orbit none.
        SnapshotCodec.Encode(writer, first, null, "a");
        var a1 = SnapshotCodec.Decode(writer.Written);
        Assert.Equal(new[] { near }, a1.Groups);
        SnapshotCodec.Encode(writer, first, null, "b");
        var b1 = SnapshotCodec.Decode(writer.Written);
        Assert.Equal(new[] { inView }, b1.Groups);
        SnapshotCodec.Encode(writer, first, null, "c");
        Assert.Null(SnapshotCodec.Decode(writer.Written).Groups);
        SnapshotCodec.Encode(writer, first);
        Assert.Null(SnapshotCodec.Decode(writer.Written).Groups);

        // A delta: the group seen moved, another new, one gone; decoded
        // from the base the viewer was sent or from the host's whole one.
        var moved = near with { X = near.X - 40, Frame = 30 };
        var come = new GroupSnapshot(6, null, 5, 0, Log(4600), Uqm.Units.UniverseToLogY(5300), 0, 9, GroupSnapshotFlags.Hidden);
        var second = first with { Frame = 11, Groups = new[] { come, moved, far, outer } };
        SnapshotCodec.Encode(writer, second, first, "a");
        Assert.Equal(new[] { come, moved }, SnapshotCodec.Decode(writer.Written, a1).Groups);
        Assert.Equal(new[] { come, moved }, SnapshotCodec.Decode(writer.Written, first).Groups);
        SnapshotCodec.Encode(writer, second, first, "b");
        var b2 = SnapshotCodec.Decode(writer.Written, b1);
        Assert.Null(b2.Groups);   // gone from b's view
        // Unchanged, the groups aren't sent again: the base's stand.
        var third = second with { Frame = 12 };
        var a2 = SnapshotCodec.Decode(Encoded(writer, second, first, "a"), a1);
        SnapshotCodec.Encode(writer, third, second, "a");
        int unchanged = writer.Length;
        Assert.Same(a2.Groups, SnapshotCodec.Decode(writer.Written, a2).Groups);
        SnapshotCodec.Encode(writer, third with { Groups = null }, second, "a");
        Assert.True(unchanged < writer.Length);   // the part says they're gone
        Assert.Null(SnapshotCodec.Decode(writer.Written, a2).Groups);

        // Bad groups are refused.
        SnapshotCodec.Encode(writer, first with { Groups = new[] { near with { Flags = (GroupSnapshotFlags)4 } } }, null, "a");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written));
        SnapshotCodec.Encode(writer, first with { Groups = new[] { near, near with { X = near.X + 1 } } }, null, "a");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written));
        SnapshotCodec.Encode(writer, first with { Groups = new[] { near with { Frame = -1 } } }, null, "a");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written));
        SnapshotCodec.Encode(writer, first with { Groups = new[] { near with { Transition = short.MaxValue + 1 } } }, null, "a");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written));
        Assert.Equal(new[] { near with { Transition = -3 } },   // a vortex going out: transition_state below 0
            SnapshotCodec.Decode(Encoded(writer, first with { Groups = new[] { near with { Transition = -3 } } }, null, "a")).Groups);
        // A group in a system numbered below 0 (seen by a ship said to be there).
        var odd = first with { Ships = new[] { Ship("b", Activity.InnerSystem, 0, 0, -5, 2) }, Groups = new[] { inView with { Star = -5 } } };
        SnapshotCodec.Encode(writer, odd, null, "b");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written));
        // A delta naming a base group the base hasn't.
        SnapshotCodec.Encode(writer, second, first, "a");
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(writer.Written, first with { Groups = null }));
    }

    private static byte[] Encoded(WireWriter writer, WorldSnapshot snapshot, WorldSnapshot? baseSnapshot, string viewer)
    {
        SnapshotCodec.Encode(writer, snapshot, baseSnapshot, viewer);
        return writer.Written.ToArray();
    }

    // ---- the meeting's messages

    [Fact]
    public void MeetingsTalksAndTalliesRoundTripAndBadOnesAreRefused()
    {
        var view = new MeetingView(3, MeetingStep.Encounter, "URQUAN_SHIP",
            new[] { new EnemyFleetShip("urquan", 42), new EnemyFleetShip("urquan", 7) }, false, 82, 2, null);
        Assert.True(view.Same(MeetingView.Decode(view.Encode())));
        var world = view with { Step = MeetingStep.Talk, Ships = new[] { new EnemyFleetShip("ilwrath") }, Infinite = true, Planet = 0, Moon = 1 };
        var back = MeetingView.Decode(world.Encode())!;
        Assert.True(world.Same(back));
        Assert.Null(back.Ships[0].Crew);
        Assert.Null(MeetingView.Decode(Protocol.NoView));
        Assert.False(view.Same(view with { Step = MeetingStep.Talk }));
        Assert.Throws<InvalidDataException>(() => MeetingView.Decode(new byte[] { 3, 9 }));   // no such step
        Assert.Throws<InvalidDataException>(() => MeetingView.Decode(view.Encode().Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => MeetingView.Decode(view.Encode()[..^2]));
        // Since item 11: the closing lines' step, the battle's planet and the planet named.
        var closing = view with { Step = MeetingStep.Closing, Well = 0x2C, Named = 3 };
        var again = MeetingView.Decode(closing.Encode())!;
        Assert.True(closing.Same(again));
        Assert.Equal((MeetingStep.Closing, (byte)0x2C, (int?)3), (again.Step, again.Well, again.Named));
        Assert.False(closing.Same(closing with { Well = 1 }));
        Assert.False(closing.Same(closing with { Named = null }));
        // A planet named on no star, or not on the star named, is refused by the client.
        var client = new PlayerSession(SystemGroupTests.Systems.Value, null, "g1", TestContent.Shapes.HyperFlagship.Count);
        Assert.Throws<InvalidDataException>(() => client.ShowMeeting(closing with { Star = null, Planet = null, Moon = null }));
        Assert.Throws<InvalidDataException>(() => client.ShowMeeting(closing with { Planet = null, Moon = null, Named = 99 }));

        var talk = new TalkView(VisitStep.Meeting, "urquan", new[] { "Hello" }, new[] { "Hi", "Bye" }, 5, "g1", "");
        Assert.True(talk.Same(TalkView.Decode(talk.Encode())));
        var bad = talk.Encode();
        bad[0] = (byte)VisitStep.IlwrathFight + 1;   // never sent
        Assert.Throws<InvalidDataException>(() => TalkView.Decode(bad));

        var tally = new BattleNotice(BattleNoticeKind.Ended, 4, "spathi", BattleOutcome.Won, BattleExit.Fought, 93, 12, Destroyed: 3, Bounty: 62);
        Assert.Equal(tally, BattleNotice.Decode(tally.Encode()));
        Assert.Equal("The spathi battle group is destroyed: 3 ships, 93 RU of salvage.", tally.Describe());
        Assert.Equal("The spathi is destroyed: 93 RU of salvage.", (tally with { Destroyed = 1 }).Describe());
        Assert.Throws<InvalidDataException>(() =>
            BattleNotice.Decode((tally with { Destroyed = BattleNotice.MostDestroyed + 1 }).Encode()));
    }

    [ContentFact]
    public void AnOldClientIsRefused()
    {
        Assert.Equal(27, Protocol.Version);   // a meeting's closing lines (stage 3b-1 item 11)
        var game = NewGame(guests: 0);
        var refused = game.Host.Hello(9, "old", "Old", 26);
        Assert.False(Protocol.Accepted(refused.Result));
        Assert.Contains("Version mismatch", refused.Message);
    }

    // ---- a guest's meeting

    [ContentFact]
    public void AGuestMeetsAGroupChoosesAtItsScreenRepliesAndIsShownTheClosingLines()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1, g2);
        var m = world.MeetingOf("g1")!;
        // The encounter screen's state: the race's ships, where, Hail or Attack (said in words until item 11 draws it).
        var shown = g1.Session.Meeting!;
        Assert.Equal((m.Id, "SPATHI_SHIP", false, (int?)null), (shown.Id, shown.Race, shown.Infinite, shown.Star));
        Assert.Equal(new[] { ("spathi", (int?)30), ("spathi", (int?)30) }, shown.Ships.Select(s => (s.Ship, s.Crew)));
        // A spin first (sent as it began), then the screen.
        Assert.Equal(new[] { MeetingStep.Spin, MeetingStep.Encounter }, g1.Meetings.Select(v => v!.Step));
        Assert.Equal(PlayerScreen.Encounter, g1.Session.Screen);
        var lines = g1.Session.StatusLines();
        Assert.Contains("Encounter: 2 Spathi ships in deep space", lines);
        Assert.Contains("> Hail <   Attack", lines);
        Assert.True(g1.Session.IgnoresRepeatedSelect);
        // Neither the host nor the other guest is sent it.
        Assert.Null(host.MeetingFor("h"));
        Assert.Empty(g2.Meetings);
        Assert.Empty(g2.Talks);

        // A choice for another meeting changes nothing, the screen up.
        host.GuestEncounterChoice(1, m.Id + 1, (int)EncounterAction.Attack);
        Frame(host, g1, g2);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.DoesNotContain("g1: attack", game.Log);
        // The keys go round Hail and Attack; Enter chooses, once.
        g1.Session.PressMenu(MenuKey.Down);
        Assert.Equal(EncounterEntry.Attack, g1.Session.EncounterCursor);
        Assert.Contains("  Hail   > Attack <", g1.Session.StatusLines());
        g1.Session.PressMenu(MenuKey.Up);
        g1.Session.PressMenu(MenuKey.Select);
        g1.Session.PressMenu(MenuKey.Down);   // chosen: the keys do nothing more
        Assert.Equal(EncounterEntry.Hail, g1.Session.EncounterCursor);
        Frame(host, g1, g2);
        Assert.Contains("g1: hail", game.Log);
        Assert.Equal(MeetingStep.Talk, m.Step);
        // A choice sent again, or for another meeting, changes nothing.
        host.GuestEncounterChoice(1, m.Id, (int)EncounterAction.Attack);
        host.GuestEncounterChoice(1, m.Id + 1, (int)EncounterAction.Attack);
        Assert.Equal(MeetingStep.Talk, m.Step);

        // The talk, as the commander's is sent: the lines, the replies, g1 replying.
        Frame(host, g1, g2);
        var talk = g1.Session.Talk!;
        Assert.Equal((VisitStep.Meeting, "probe", "g1"), (talk.Step, talk.Script, talk.Replier));
        Assert.Equal(new[] { "Hello" }, talk.Lines);
        Assert.Equal(new[] { "What?" }, talk.Replies);
        Assert.Equal(m.Round, talk.Round);
        Assert.True(g1.Session.TalkShown && g1.Session.Replying);
        Assert.Equal(PlayerScreen.Comm, g1.Session.Screen);
        Assert.Equal(MeetingStep.Talk, g1.Session.Meeting!.Step);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1, g2);
        Assert.Contains("g1: reply 0", game.Log);

        // The talk over in peace: the closing lines (since the reply) on the communication screen, the meeting
        // waiting for g1 alone at its new step (item 11; the owner, question 10), under the race's lock still.
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.Equal("g1", world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        Frame(host, g1, g2);
        Assert.Equal(MeetingStep.Closing, g1.Session.Meeting!.Step);
        var closing = g1.Talks[^1]!;
        Assert.Equal((VisitStep.Meeting, "What?", "g1"), (closing.Step, closing.Said, closing.Replier));
        Assert.Equal(new[] { "Bye" }, closing.Lines);
        Assert.Empty(closing.Replies);
        Assert.Same(closing, g1.Session.Talk);
        Assert.True(g1.Session.AtClosingLines);
        Assert.DoesNotContain("Bye", g1.Session.Notice);
        Assert.Equal(PlayerScreen.Comm, g1.Session.Screen);
        // Another player goes on meanwhile (the world doesn't wait): g2 flies.
        long frame = world.Frame;
        // Gone through once played and the last replay's three seconds are up (DoLastReplay): then the meeting is over.
        int waited = 0;
        while (world.MeetingOf("g1") is not null && waited < 400)
        {
            Frame(host, g1, g2);
            ++waited;
        }
        Assert.InRange(waited, (int)(PlayerSession.LastReplaySeconds * 60) - 2, (int)(PlayerSession.LastReplaySeconds * 60) + 2);
        Assert.Equal(frame + waited, world.Frame);
        Assert.Contains("g1: the closing lines heard", game.Log);
        Assert.Null(world.DialogueLocks.Holder("SPATHI_CONVERSATION"));
        Frame(host, g1, g2);
        Assert.Null(g1.Session.Meeting);
        Assert.Null(g1.Meetings[^1]);
        Assert.Null(g1.Talks[^1]);
        Assert.Null(g1.Session.Talk);
        Assert.NotEqual(PlayerScreen.Comm, g1.Session.Screen);
        // Nothing more is sent for it; g2 was told of it only in words (item 9's notices).
        int talks = g1.Talks.Count;
        Frame(host, g1, g2);
        Assert.Equal(talks, g1.Talks.Count);
        Assert.Empty(g2.Meetings);
        Assert.Empty(g2.Talks);
        Assert.Contains(g2.Notices, n => n.Kind == BattleNoticeKind.Told && n.Text == "Guest 1 is meeting the Spathi.");
        Assert.Contains(g2.Notices, n => n.Kind == BattleNoticeKind.Told && n.Text == "Guest 1 made peace with the Spathi.");
    }

    [ContentFact]
    public void AGuestsAttackFightsAndTheBattleWonTallyIsTheirs()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        GroupBy(world, "g1", "SPATHI_SHIP", ships: 2);
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1, g2);
        var m = world.MeetingOf("g1")!;
        g1.Session.PressMenu(MenuKey.Down);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1, g2);
        Assert.Contains("g1: attack", game.Log);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Frame(host, g1, g2);
        Assert.Equal(MeetingStep.Battle, g1.Session.Meeting!.Step);
        Assert.Null(g1.Session.Talk);   // attacked: no talk, no lines
        // Both Spathi destroyed, each once it's in and fighting.
        var wb = m.Battle!;
        // The arena's planet is the gravity well the encounter screen showed (BuildBattle's, item 11).
        Assert.Equal(m.BattlePlanet, wb.BattlePlanet);
        Assert.Equal(m.BattlePlanet, g1.Meetings.First(v => v!.Step == MeetingStep.Encounter)!.Well);
        int inFor = 0;
        for (int i = 0; i < 3000 && wb.Outcome is null; ++i)
        {
            inFor = wb.Enemy.Ship is not null && wb.Enemy.RaceDesc is { CrewLevel: > 0 } ? inFor + 1 : 0;
            if (inFor > 30)
            {
                WorldBattleTests.Kill(wb.Battle, wb.Enemy);
                inFor = 0;
            }
            Frame(host, g1, g2);
        }
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Frame(host, g1, g2);
        // The battle-won screen's tally: the ships destroyed, each's bounty, g1's salvage (all of it: alone in the battle).
        int bounty = world.BattleKit!.Salvage(world.BattleKit.Ships["spathi"]);
        var end = g1.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal((BattleOutcome.Won, BattleExit.Fought, 2, (uint)bounty, (uint)(2 * bounty)),
            (end.Outcome, end.Exit, end.Destroyed, end.Bounty, end.Salvage));
        Assert.Contains($"The spathi battle group is destroyed: 2 ships, {2 * bounty} RU of salvage.", g1.Session.Notice);
        Assert.DoesNotContain(g2.Notices, n => n.Kind == BattleNoticeKind.Ended);
        Assert.Null(g1.Session.Meeting);
        // The battle-won screen (item 11) once g1 is out of the battle, the ship waiting meanwhile; g2 none.
        Assert.Same(end, g1.Session.Tally);
        Assert.Null(g2.Session.Tally);
        FramesUntil(host, () => g1.Session.Me?.Activity == Activity.Hyperspace, 300, g1, g2);
        Assert.Equal(PlayerScreen.BattleWon, g1.Session.Screen);
        Assert.Equal(ShipInput.None, g1.Session.NextInput(ShipInput.Thrust));
        Assert.Contains(g1.Session.StatusLines(), l => l.StartsWith("Battle won: 2 ships destroyed", StringComparison.Ordinal));
        g1.Session.CloseTally();   // the screen has run
        Assert.Equal(PlayerScreen.Hyperspace, g1.Session.Screen);
        Assert.Equal(ShipInput.Thrust, g1.Session.NextInput(ShipInput.Thrust));
        // A tally not yet shown goes when a talk or battle comes first, once the player's out of the battle.
        g1.Session.ShowBattleNotice(end);
        Frame(host, g1, g2);
        var wb2 = world.StartBattle("g1", "spathi");
        Frame(host, g1, g2);
        Frame(host, g1, g2);
        Assert.Null(g1.Session.Tally);
    }

    [ContentFact]
    public void AnInstantVictoryIsToldAsABattleWon()
    {
        var game = NewGame();
        game.Init = Segue.Peace;   // a talk at once, no encounter screen
        game.End = Segue.Victory;
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        GroupBy(world, "g1", "SLYLANDRO_SHIP", ships: 3);
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 300, g1, g2);
        // Never at the screen: the talk at once.
        Assert.DoesNotContain(g1.Meetings, v => v is { Step: MeetingStep.Encounter });
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1, g2);
        // Its closing lines first (item 11), then the victory.
        Assert.Equal(MeetingStep.Closing, world.MeetingOf("g1")?.Step);
        Assert.Empty(g1.Notices.Where(n => n.Kind == BattleNoticeKind.Ended));
        FramesUntil(host, () => world.MeetingOf("g1") is null, 400, g1, g2);
        Frame(host, g1, g2);
        var probe = world.Story!.Rules.RaceShips[world.Story.Rules.RaceIndex("SLYLANDRO_SHIP")];
        uint bounty = (uint)(probe.Cost >> 3);
        Assert.NotEqual(0u, bounty);
        var won = g1.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal(new BattleNotice(BattleNoticeKind.Ended, 0, probe.Ship, BattleOutcome.Won, BattleExit.Fought, 3 * bounty,
            world.Find("g1")!.Ship.Hold.Crew, Destroyed: 3, Bounty: bounty), won);
        Assert.Empty(g2.Notices.Where(n => n.Kind == BattleNoticeKind.Ended));
        // Its client plays the battle-won screen (item 11), there being no battle to come out of; g2 none.
        Assert.Same(won, g1.Session.Tally);
        Assert.Equal(PlayerScreen.BattleWon, g1.Session.Screen);
        Assert.NotEqual(PlayerScreen.BattleWon, g2.Session.Screen);
    }

    [ContentFact]
    public void AChoiceMadeDuringAPauseIsMadeAsItEnds()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1);
        var m = world.MeetingOf("g1")!;
        Assert.True(host.TogglePause());
        Frame(host, g1);
        g1.Session.PressMenu(MenuKey.Select);   // sent, as the guest can't tell it'd be refused
        Frame(host, g1);
        Frame(host, g1);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.Contains("g1: hail (made as the pause lifts)", game.Log);
        Assert.True(host.TogglePause());
        Frame(host, g1);
        Assert.Equal(MeetingStep.Talk, m.Step);
    }

    [ContentFact]
    public void ClosingLinesGoneThroughDuringAPauseGoOnAsItEndsAndASaveThenIsWithoutTheMeeting()
    {
        // The owner's answer to question 10 (item 11): the meeting waits at its closing lines for its player
        // alone; one gone through during the host's pause goes on as it lifts (question 11's rule).
        var game = NewGame();
        game.End = Segue.Hostile;   // a battle after the closing lines
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        var group = GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1, g2);
        var m = world.MeetingOf("g1")!;
        g1.Session.PressMenu(MenuKey.Select);
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 10, g1, g2);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1, g2);
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.Null(m.Battle);
        // A save now is an ordinary one: the meeting isn't in it (the group as before it, not met).
        var loaded = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Null(loaded.MeetingOf("g1"));
        var kept = loaded.Encounters!.List.Single(e => e.Race == m.Race);
        Assert.Null(kept.MeetingPlayer);
        Assert.Equal(new[] { 30, 30 }, kept.Ships);
        Assert.Equal(0, kept.Flags & (HyperEncounter.OneShot | HyperEncounter.Reforming));
        // The host pauses; the closing lines are gone through meanwhile: kept, the battle starting as it lifts.
        Assert.True(host.TogglePause());
        FramesUntil(host, () => game.Log.Contains("g1: the closing lines heard (the meeting goes on as the pause lifts)"), 400, g1, g2);
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.Equal("g1", world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        Assert.True(host.TogglePause());
        Frame(host, g1, g2);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.NotNull(m.Battle);
        Assert.Null(world.DialogueLocks.Holder("SPATHI_CONVERSATION"));
        // A done sent again changes nothing.
        Assert.False(host.ClosingDone("g1", m.Id));
        Assert.Same(group, world.Encounters!.List.Single(e => e.Race == m.Race));
    }

    [ContentFact]
    public void ClosingLinesHeldThroughAPauseGoWithAPlayerWhoLeaves()
    {
        var game = NewGame(guests: 1);
        game.End = Segue.Hostile;
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var group = GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1);
        g1.Session.PressMenu(MenuKey.Select);
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 10, g1);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1);
        var m = world.MeetingOf("g1")!;
        Assert.True(host.TogglePause());
        Assert.True(host.ClosingDone("g1", m.Id));   // held for the pause's end
        Assert.Equal(new[] { ("g1", m.Id) }, world.HeldClosings);
        host.GuestLeft(1);
        Assert.Empty(world.HeldClosings);
        Assert.Null(world.MeetingOf("g1"));
        Assert.True(host.TogglePause());
        host.Step(ShipInput.None);
        Assert.Empty(world.Battles);   // nothing made as the pause lifts: no battle for the player gone
        Assert.Null(group.MeetingPlayer);
    }

    [Fact]
    public void ClosingLinesShownAgainAfterSomethingElsePlayFromTheStart()
    {
        // Item 11, review round 1: a story's closing lines shown over the meeting's, then these again: their wait
        // starts afresh (three seconds once played), not where it was.
        var s = new PlayerSession(SystemGroupTests.Systems.Value, null, "g1", TestContent.Shapes.HyperFlagship.Count);
        var meeting = new MeetingView(4, MeetingStep.Closing, "SPATHI_SHIP", new[] { new EnemyFleetShip("spathi", 30) });
        var lines = new TalkView(VisitStep.Meeting, "probe", new[] { "Bye" }, Array.Empty<string>(), 2, "g1", "What?");
        s.ShowMeeting(meeting);
        s.ShowTalk(lines);
        Assert.True(s.AtClosingLines);
        s.FollowClosing(false, PlayerSession.LastReplaySeconds - 0.5);
        Assert.Null(s.TakeClosingDone());
        // The story's closing lines come (a notice: no talk shown), then the meeting's again.
        s.ShowTalk(new TalkView(VisitStep.Done, "ilwrath", new[] { "Die!" }, Array.Empty<string>(), 1, "h", ""));
        Assert.False(s.AtClosingLines);
        s.FollowClosing(false, 1.0 / 60);
        s.ShowTalk(lines);
        Assert.True(s.AtClosingLines);
        s.FollowClosing(false, 1.0);
        Assert.Null(s.TakeClosingDone());   // would have run out had the wait gone on
        s.FollowClosing(false, PlayerSession.LastReplaySeconds - 1.0);
        Assert.Equal(4, s.TakeClosingDone());
    }

    [ContentFact]
    public void LeavingAtTheClosingLinesMakesTheTalksEndButStartsNoBattle()
    {
        // Item 11, review round 1: a player leaving at their meeting's closing lines doesn't undo the talk's end.
        var game = NewGame(guests: 1);
        game.Init = Segue.Peace;
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var group = GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 300, g1);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1);
        Assert.Equal(MeetingStep.Closing, world.MeetingOf("g1")?.Step);
        host.GuestLeft(1);
        // In peace: written back as after the talk (re-forming, one-shot), the lock let go.
        Assert.Null(world.MeetingOf("g1"));
        Assert.Null(group.MeetingPlayer);
        Assert.Equal(HyperEncounter.OneShot | HyperEncounter.Reforming, group.Flags & (HyperEncounter.OneShot | HyperEncounter.Reforming));
        Assert.Null(world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        Assert.Empty(world.Battles);

        // An instant victory: the leaver is paid, and its tally waits for its return (the client opening on the
        // battle-won screen then).
        LeaveAtClosingAndComeBack(Segue.Victory, "SLYLANDRO_SHIP", (game, group) =>
        {
            // Written back with no ships, re-forming (gone on the next frame's encounters), the lock let go.
            Assert.Empty(group.Ships);
            Assert.Equal(HyperEncounter.Reforming, group.Flags & HyperEncounter.Reforming);
            Assert.Null(group.MeetingPlayer);
            Assert.Null(game.World.DialogueLocks!.Holder("SLYLANDRO_CONVERSATION"));
        }, (game, again, units, group) =>
        {
            Assert.DoesNotContain(group, game.World.Encounters!.List);   // gone
            var probe = game.World.Story!.Rules.RaceShip("SLYLANDRO_SHIP");
            uint bounty = (uint)(probe.Cost >> 3);
            Assert.Equal(units + 2 * bounty, game.World.Find("g1")!.Ship.Hold.ResourceUnits);
            var won = again.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
            Assert.Equal((BattleOutcome.Won, 2, bounty, 2 * bounty), (won.Outcome, won.Destroyed, won.Bounty, won.Salvage));
            Assert.Same(won, again.Session.Tally);
            Assert.Equal(PlayerScreen.BattleWon, again.Session.Screen);
        });
        // A surrender: the leaver's ship a wreck, told so as it's back; no game over (a guest's ship).
        LeaveAtClosingAndComeBack(Segue.Defeat, "URQUAN_SHIP", (game, group) =>
        {
            // The group as after a talk without a battle: its ships, re-forming and one-shot; the lock let go.
            Assert.Equal(2, group.Ships.Count);
            Assert.Equal(HyperEncounter.OneShot | HyperEncounter.Reforming, group.Flags & (HyperEncounter.OneShot | HyperEncounter.Reforming));
            Assert.Null(group.MeetingPlayer);
            Assert.Null(game.World.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        }, (game, again, _, _) =>
        {
            Assert.False(game.World.GameOver);
            var ship = game.World.Find("g1")!.Ship;
            Assert.True(ship.Destroyed);
            Assert.Equal(0, ship.Hold.Crew);
            Assert.Contains(again.Notices, n => n.Kind == BattleNoticeKind.Wrecked);
            Assert.DoesNotContain(again.Notices, n => n.Kind == BattleNoticeKind.GameOver);
        });

        // The drone at Earth: its message given, the guest leaving at it, it flies off all the same (check_probe).
        var drone = NewGame(guests: 1, testTalks: false);
        drone.World.TalkLibrary = UrquanMeetingTests.Library.Value;
        var d1 = NewClient(1);
        drone.World.PutInOrbit("g1", SolStar, Earth);
        drone.World.Find("g1")!.Ship.Ip!.ReturnFromOrbit();
        FramesUntil(drone.Host, () => drone.World.SystemMeetings.Count > 0, 600, d1);
        var met = drone.World.SystemMeetings[0];
        Assert.Equal(MeetingStep.Closing, drone.World.MeetingOf("g1")?.Step);
        drone.Host.GuestLeft(1);
        Assert.Null(drone.World.MeetingOf("g1"));
        Assert.Equal(1, drone.World.Story!.Get("PROBE_MESSAGE_DELIVERED"));
        var probe = drone.World.SystemGroups!.Systems[SolStar].Groups.Single(g => g.Race == met.Race);
        Assert.Equal((IpGroup.Flee | IpGroup.IgnoreFlagship, (byte)0), (probe.Task, probe.DestLoc));
    }

    // A guest at its meeting's closing lines (a talk at once, ending as given) leaves, a frame passes, and it comes
    // back on a new connection and is sent what waited for it; then the checks (with its resource units before
    // and the group it met).
    // atLeave: checks as it leaves (the group as the leaving wrote it back, before frames move it on).
    private static void LeaveAtClosingAndComeBack(Segue end, string race, Action<Game, HyperEncounter> atLeave,
        Action<Game, Client, uint, HyperEncounter> check)
    {
        var game = NewGame(guests: 1);
        game.Init = Segue.Peace;
        game.End = end;
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var group = GroupBy(world, "g1", race);
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 300, g1);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1);
        Assert.Equal(MeetingStep.Closing, world.MeetingOf("g1")?.Step);
        uint units = world.Find("g1")!.Ship.Hold.ResourceUnits;
        host.GuestLeft(1);
        atLeave(game, group);
        Assert.Null(world.MeetingOf("g1"));
        Assert.Empty(world.Battles);
        Frame(host, g1);
        Frame(host, g1);
        Assert.Equal(JoinResult.Rejoined, host.Hello(3, "g1", "Guest 1", Protocol.Version).Result);
        var again = NewClient(3);
        again.Session.PlayerId = "g1";
        Frame(host, again);
        Frame(host, again);
        check(game, again, units, group);
    }

    [ContentFact]
    public void TheDronesMessageIsShownToTheGuestItMet()
    {
        var game = NewGame(guests: 1, testTalks: false);
        var host = game.Host;
        var world = game.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var g1 = NewClient(1);
        var said = new List<string>();
        g1.Session.Logged += said.Add;
        world.PutInOrbit("g1", SolStar, Earth);
        world.Find("g1")!.Ship.Ip!.ReturnFromOrbit();   // in Earth's inner system, the drone orbiting there
        FramesUntil(host, () => world.SystemMeetings.Count > 0, 600, g1);
        Frame(host, g1);
        // Its talk begun and over in one frame (its message, no screen, no replies): the message whole on the
        // communication screen as the meeting's closing lines, the meeting waiting for g1 (item 11; question 10).
        var m = world.MeetingOf("g1")!;
        Assert.Equal(MeetingStep.Closing, m.Step);
        Assert.Equal(MeetingStep.Closing, Assert.Single(g1.Meetings)!.Step);
        var message = Assert.Single(g1.Talks)!;
        Assert.Equal((VisitStep.Meeting, "g1", ""), (message.Step, message.Replier, message.Said));
        Assert.NotEmpty(message.Lines);
        Assert.Empty(message.Replies);
        Assert.Equal(host.TalkFor("g1"), message, TalkViewSame.Instance);
        Assert.DoesNotContain(said, l => l.Contains(OneLine(message.Lines[0])));   // not a notice now
        Assert.Same(message, g1.Session.Talk);
        Assert.Equal(PlayerScreen.Comm, g1.Session.Screen);
        Assert.Equal(1, world.Story!.Get("PROBE_MESSAGE_DELIVERED"));
        // The lines playing, or heard again, or the summary open (the screen busy), hold it; once they've
        // played the last replay's three seconds run, two after a replay (DoLastReplay).
        // (The frames above had it played already: its three seconds were running.)
        for (int i = 0; i < 600; ++i)
            g1.Session.FollowClosing(true, FrameSeconds);
        Assert.Null(g1.Session.TakeClosingDone());
        g1.Session.FollowClosing(false, PlayerSession.AfterReplaySeconds - 0.5);
        g1.Session.FollowClosing(true, FrameSeconds);   // heard again
        g1.Session.FollowClosing(false, PlayerSession.AfterReplaySeconds - 0.25);
        Assert.Null(g1.Session.TakeClosingDone());
        Assert.Equal(MeetingStep.Closing, m.Step);
        // A done for another meeting changes nothing.
        host.GuestClosingDone(1, m.Id + 1);
        Assert.Equal(MeetingStep.Closing, m.Step);
        g1.Session.FollowClosing(false, 0.5);
        Assert.Equal(m.Id, g1.Session.TakeClosingDone());
        host.GuestClosingDone(1, m.Id);
        Assert.Null(world.MeetingOf("g1"));   // over: the drone flies off
        Assert.Null(g1.Session.TakeClosingDone());
        g1.Session.FollowClosing(false, 10);
        Assert.Null(g1.Session.TakeClosingDone());   // said once
        Frame(host, g1);
        Assert.Null(g1.Session.Meeting);
        Assert.Null(g1.Session.Talk);
        Assert.NotEqual(PlayerScreen.Comm, g1.Session.Screen);
    }

    private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    private sealed class TalkViewSame : IEqualityComparer<TalkView?>
    {
        public static readonly TalkViewSame Instance = new();
        public bool Equals(TalkView? a, TalkView? b) => a is null ? b is null : a.Same(b);
        public int GetHashCode(TalkView? v) => 0;
    }

    [Fact]
    public void TheHostsEncounterScreenHasTheGameMenuWhichComesLaterAGuestsHasHailAndAttackOnly()
    {
        // The owner, 2026-11-21 (question 9): UQM's third entry, the game
        // menu, for the host; it says it comes later, as orbit's does, and
        // chooses nothing.
        PlayerSession At(string id, bool host)
        {
            var s = new PlayerSession(SystemGroupTests.Systems.Value, null, id, 32);
            s.Observe(new WorldSnapshot(1, 2155, 2, 17, new[]
            {
                new ShipSnapshot(id, id, host, host ? ShipKind.Flagship : ShipKind.Escort, 0, 0, 5000, 5000, 0, 0, 0, 100, 100, false, false, Activity.Talk),
            }));
            s.ShowMeeting(new MeetingView(1, MeetingStep.Encounter, "SPATHI_SHIP", new[] { new EnemyFleetShip("spathi", 30) }));
            return s;
        }
        var h = At("h", true);
        Assert.Contains("> Hail <   Attack     Game menu", h.StatusLines());
        h.PressMenu(MenuKey.Up);   // round, from Hail to the last
        Assert.Equal(EncounterEntry.GameMenu, h.EncounterCursor);
        Assert.Contains("  Hail     Attack   > Game menu <", h.StatusLines());
        h.PressMenu(MenuKey.Select);
        Assert.Equal("Game menu comes in a later stage.", h.Notice);
        Assert.Null(h.TakeEncounterChoice());
        h.PressMenu(MenuKey.Down);   // the screen still up: round to Hail
        Assert.Equal(EncounterEntry.Hail, h.EncounterCursor);
        h.PressMenu(MenuKey.Down);
        h.PressMenu(MenuKey.Select);
        Assert.Equal((1, EncounterAction.Attack), h.TakeEncounterChoice());

        // On the game menu as the player is no longer the host (review): the
        // cursor shown and pressed from Hail, Select choosing Hail.
        var was = At("h", true);
        was.PressMenu(MenuKey.Up);
        Assert.Equal(EncounterEntry.GameMenu, was.EncounterCursor);
        was.Observe(new WorldSnapshot(2, 2155, 2, 17, new[]
        {
            new ShipSnapshot("h", "h", false, ShipKind.Escort, 0, 0, 5000, 5000, 0, 0, 0, 100, 100, false, false, Activity.Talk),
        }));
        Assert.Contains("> Hail <   Attack", was.StatusLines());
        was.PressMenu(MenuKey.Select);
        Assert.Equal((1, EncounterAction.Hail), was.TakeEncounterChoice());

        var g = At("g1", false);
        Assert.Contains("> Hail <   Attack", g.StatusLines());
        Assert.DoesNotContain(g.StatusLines(), l => l.Contains("Game menu"));
        g.PressMenu(MenuKey.Up);
        Assert.Equal(EncounterEntry.Attack, g.EncounterCursor);
        g.PressMenu(MenuKey.Up);
        Assert.Equal(EncounterEntry.Hail, g.EncounterCursor);
    }

    [Fact]
    public void ANewMeetingsScreenStartsOnHailAndOneOffTheStarMapIsRefused()
    {
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, "g1", 32);
        me.Observe(new WorldSnapshot(1, 2155, 2, 17, new[]
        {
            new ShipSnapshot("g1", "g1", false, ShipKind.Escort, 0, 0, 5000, 5000, 0, 0, 0, 100, 100, false, false, Activity.Talk),
        }));
        var first = new MeetingView(1, MeetingStep.Encounter, "SPATHI_SHIP", new[] { new EnemyFleetShip("spathi", 30) });
        me.ShowMeeting(first);
        me.PressMenu(MenuKey.Down);
        Assert.Equal(EncounterEntry.Attack, me.EncounterCursor);
        me.ShowMeeting(first with { Step = MeetingStep.Talk });   // the same meeting on: where it was
        Assert.Equal(EncounterEntry.Attack, me.EncounterCursor);
        me.ShowMeeting(first with { Id = 2 });   // the next meeting's screen: on Hail, as UQM's starts
        Assert.Equal(EncounterEntry.Hail, me.EncounterCursor);
        me.PressMenu(MenuKey.Select);
        Assert.Equal((2, EncounterAction.Hail), me.TakeEncounterChoice());
        // A place not on this player's star map is refused, changing nothing.
        int stars = SystemGroupTests.Systems.Value.Map.Stars.Count;
        Assert.Throws<InvalidDataException>(() => me.ShowMeeting(first with { Id = 3, Star = stars }));
        Assert.Throws<InvalidDataException>(() => me.ShowMeeting(first with { Id = 3, Star = SolStar, Planet = 40 }));
        Assert.Throws<InvalidDataException>(() => me.ShowMeeting(first with { Id = 3, Planet = 2 }));
        Assert.Equal(2, me.Meeting!.Id);
        me.ShowMeeting(first with { Id = 3, Star = SolStar, Planet = Earth, Moon = 0 });
        Assert.Contains(me.StatusLines(), l => l.StartsWith("Encounter: 1 Spathi ship at "));
    }

    [ContentFact]
    public void AWinPayingNoSalvageTellsNoTallyAndNorDoesTheSaMatrasGuard()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        // An Androsynth: of no race of the full game's, it pays nothing (UQM's VictoryState -1: no tally).
        var wb = world.StartBattle("g1", Combat.AndrosynthShip.Id);
        Win(host, wb, g1, g2);
        var end = g1.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal((BattleOutcome.Won, 0u, 0, 0u), (end.Outcome, end.Salvage, end.Destroyed, end.Bounty));
        // The instant victory while the Ur-Quan guard the Sa-Matra wins nothing: no tally either.
        world.Story!.Set("URQUAN_PROTECTING_SAMATRA", 1);
        game.Init = Segue.Peace;
        game.End = Segue.Victory;
        GroupBy(world, "g2", "SLYLANDRO_SHIP", ships: 2);
        FramesUntil(host, () => g2.Session.Talk is { Replies.Count: > 0 }, 300, g1, g2);
        g2.Session.PressMenu(MenuKey.Select);
        FramesUntil(host, () => world.MeetingOf("g2") is null, 400, g1, g2);   // its closing lines gone through (item 11)
        Frame(host, g1, g2);
        Assert.Contains(game.Log, l => l.Contains("an instant victory that wins nothing"));
        Assert.DoesNotContain(g2.Notices, n => n.Kind == BattleNoticeKind.Ended);
    }

    // A battle won: each enemy ship destroyed once it's in and fighting.
    private static void Win(HostSession host, WorldBattle wb, params Client[] clients)
    {
        int inFor = 0;
        for (int i = 0; i < 3000 && wb.Outcome is null; ++i)
        {
            inFor = wb.Enemy.Ship is not null && wb.Enemy.RaceDesc is { CrewLevel: > 0 } ? inFor + 1 : 0;
            if (inFor > 30)
            {
                WorldBattleTests.Kill(wb.Battle, wb.Enemy);
                inFor = 0;
            }
            else if (wb.Outcome is null)
                wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;   // disarmed: nothing it does decides the test
            Frame(host, clients);
        }
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Frame(host, clients);
    }

    [ContentFact]
    public void APlayerDestroyedBeforeTheWinIsToldNoTally()
    {
        var game = NewGame();
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        GroupBy(world, "g1", "SPATHI_SHIP", ships: 1);
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1, g2);
        var m = world.MeetingOf("g1")!;
        g1.Session.PressMenu(MenuKey.Down);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1, g2);
        var wb = m.Battle!;
        // g2 flies into its marker and joins; destroyed once in, before the win.
        var at = world.Find("g1")!.Ship.Flight;
        world.Find("g2")!.Ship.Flight.PlaceAtUniverse(at.UniverseX, at.UniverseY);
        Assert.True(world.LetIntoBattle("g2"));
        FramesUntil(host, () => wb.FighterOf("g2")?.Starship.Ship is not null, 300, g1, g2);
        for (int i = 0; i < 30; ++i)
            Frame(host, g1, g2);
        WorldBattleTests.Kill(wb.Battle, wb.FighterOf("g2")!.Starship);
        FramesUntil(host, () => wb.FighterOf("g2")!.Exit is not null, 600, g1, g2);
        Win(host, wb, g1, g2);
        Assert.Equal(BattleExit.Destroyed, wb.FighterOf("g2")!.Exit);
        var mine = g1.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal((BattleOutcome.Won, 1), (mine.Outcome, mine.Destroyed));
        var theirs = g2.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal((BattleOutcome.Won, BattleExit.Destroyed, 0, 0u), (theirs.Outcome, theirs.Exit, theirs.Destroyed, theirs.Bounty));
    }

    [ContentFact]
    public void ALeaversMeetingAndClosingLinesArentSentAgainToItsNewConnection()
    {
        var game = NewGame(guests: 1);
        var host = game.Host;
        var world = game.World;
        var g1 = NewClient(1);
        // A first meeting's talk at once, ended in peace: its closing lines shown to g1, then gone through.
        game.Init = Segue.Peace;
        var first = GroupBy(world, "g1", "SPATHI_SHIP");
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 300, g1);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1);
        Frame(host, g1);
        Assert.Equal((VisitStep.Meeting, MeetingStep.Closing), (host.TalkFor("g1")!.Step, world.MeetingOf("g1")!.Step));
        FramesUntil(host, () => world.MeetingOf("g1") is null, 400, g1);
        Assert.Null(host.TalkFor("g1"));
        Assert.Equal(HyperEncounter.OneShot, first.Flags & HyperEncounter.OneShot);   // a talk without a battle: one-shot
        // The next meeting's talk, ended hostile: g1 leaves at its closing lines and comes back on a new connection.
        game.Init = Segue.Hostile;
        game.End = Segue.Hostile;
        var second = GroupBy(world, "g1", "ZOQFOTPIK_SHIP");
        FramesUntil(host, () => world.MeetingOf("g1") is { Step: MeetingStep.Encounter }, 300, g1);
        Assert.Null(host.TalkFor("g1"));
        g1.Session.PressMenu(MenuKey.Select);
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 }, 10, g1);
        g1.Session.PressMenu(MenuKey.Select);
        Frame(host, g1);
        Frame(host, g1);
        Assert.Equal(new[] { "Bye" }, host.TalkFor("g1")!.Lines);
        Assert.Equal("g1", world.DialogueLocks!.Holder("ZOQFOTPIK_CONVERSATION"));
        host.GuestLeft(1);
        // No battle starts (review round 1: the talk's end made, but no battle for a player gone): the group as it
        // was, as when everyone retreats before it (not one-shot), the lock let go.
        Assert.Null(world.MeetingOf("g1"));
        Assert.Null(world.DialogueLocks.Holder("ZOQFOTPIK_CONVERSATION"));
        Assert.Equal(0, second.Flags & HyperEncounter.OneShot);
        Assert.Null(second.MeetingPlayer);
        Assert.Empty(world.Battles);
        Assert.Equal(JoinResult.Rejoined, host.Hello(3, "g1", "Guest 1", Protocol.Version).Result);
        var again = NewClient(3);
        again.Session.PlayerId = "g1";
        Frame(host, again);
        Assert.Null(host.TalkFor("g1"));
        Assert.Empty(again.Talks);
        Assert.Empty(again.Meetings);
        // At a third meeting's screen it leaves again (the meeting dropped) and comes back: nothing is sent of it
        // (flying on, it may meet the group again: a new meeting, sent as any).
        GroupBy(world, "g1", "UMGAH_SHIP");
        FramesUntil(host, () => again.Session.Meeting is { OffersChoice: true }, 300, again);
        host.GuestLeft(3);
        Assert.Equal(JoinResult.Rejoined, host.Hello(4, "g1", "Guest 1", Protocol.Version).Result);
        Assert.Null(world.MeetingOf("g1"));
        Assert.DoesNotContain(host.MeetingUpdates(), u => u.Peer == 4);
        Assert.DoesNotContain(host.TalkUpdates(), u => u.Peer == 4);
    }

    [ContentFact]
    public void ARepliesAndAChoiceSentDuringAStoryTalkAreMadeAfterItAndItsClosingLinesShown()
    {
        var game = NewGame(guests: 3);
        var host = game.Host;
        var world = game.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var g1 = NewClient(1);
        var g2 = NewClient(2);
        var g3 = NewClient(3);
        var all = new[] { g1, g2, g3 };
        game.Own["ZOQFOTPIK_SHIP"] = seed => RepeatTalk(world.Story!, 20, seed);   // g2's: set after set
        // g1 and g2 in their meetings' talks (at once), g3 at its encounter screen.
        game.Init = Segue.Peace;
        GroupBy(world, "g1", "SPATHI_SHIP");
        GroupBy(world, "g2", "ZOQFOTPIK_SHIP");
        FramesUntil(host, () => g1.Session.Talk is { Replies.Count: > 0 } && g2.Session.Talk is { Replies.Count: > 0 }, 300, all);
        game.Init = Segue.Hostile;
        GroupBy(world, "g3", "UMGAH_SHIP");
        FramesUntil(host, () => g3.Session.Meeting is { OffersChoice: true }, 300, all);
        var m1 = world.MeetingOf("g1")!;
        var m3 = world.MeetingOf("g3")!;

        // g1 replies and g3 chooses as the host's opening visit with the Lunar base reported starts: a story talk.
        g1.Session.PressMenu(MenuKey.Select);
        g3.Session.PressMenu(MenuKey.Select);
        foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP" })
            world.Story!.Set(f, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 0);
        world.CommanderMet = true;
        world.VisitStarbase(world.Host);
        Assert.True(world.StoryPaused);
        Frame(host, all);
        Frame(host, all);
        // Kept, not refused: the talk and the screen wait for the story.
        Assert.Contains("g1: reply 0", game.Log);
        Assert.Contains("g3: hail (made as the pause lifts)", game.Log);
        Assert.Equal((MeetingStep.Talk, false), (m1.Step, m1.Talk!.Ended));
        Assert.Equal(MeetingStep.Encounter, m3.Step);
        Assert.True(g1.Session.TalkShown && !g1.Session.Replying);   // shown the story's

        // The commander's talk, then the Ilwrath's, end in its fight (no story pause): both made then.
        Say(host, "comandr", "base_was_abandoned");
        Say(host, "ilwrath", "surrender");
        Assert.False(world.StoryPaused);
        var wb = world.Visit!.Fight!;
        Frame(host, all);
        Assert.Equal(MeetingStep.Closing, m1.Step);   // its closing lines on its screen (item 11)
        Assert.Equal(MeetingStep.Talk, m3.Step);
        Frame(host, all);
        Assert.Equal(VisitStep.Meeting, g1.Talks[^1]!.Step);
        Assert.Equal(new[] { "Bye" }, g1.Talks[^1]!.Lines);
        // g1 was shown the Ilwrath's closing lines first, as everyone (its reply ended its talk as they came).
        Assert.Contains(g1.Talks, t => t is { Step: VisitStep.Done, Script: "ilwrath" });
        Assert.Equal((VisitStep.Done, "ilwrath"), (g1.Talks[^2]!.Step, g1.Talks[^2]!.Script));
        // g2, still in its meeting's talk, is shown the Ilwrath's closing lines as everyone is, then its talk again.
        var ilwrathLines = g2.Talks[^2]!;
        Assert.Equal((VisitStep.Done, "ilwrath"), (ilwrathLines.Step, ilwrathLines.Script));
        Assert.Equal(VisitStep.Meeting, g2.Talks[^1]!.Step);
        Assert.Equal(VisitStep.Meeting, g2.Session.Talk!.Step);
        // g1 goes through its own closing lines and its meeting is over: the Ilwrath's lines kept for it aren't
        // sent again (item 11, review round 1: they were, as a second notice), nor said again.
        int ilwrathShown = g1.Talks.Count(t => t is { Step: VisitStep.Done, Script: "ilwrath" });
        int said = g1.Said.Count;
        Assert.True(host.ClosingDone("g1", m1.Id));   // as its client says it (quicker than its wait: the fight goes on)
        Frame(host, all);
        Frame(host, all);
        Assert.Null(world.MeetingOf("g1"));
        Assert.Equal(ilwrathShown, g1.Talks.Count(t => t is { Step: VisitStep.Done, Script: "ilwrath" }));
        Assert.Null(g1.Talks[^1]);
        Assert.Null(host.TalkFor("g1"));
        string ilwrathLine = OneLine(g1.Talks.First(t => t is { Step: VisitStep.Done, Script: "ilwrath" })!.Lines[0]);
        Assert.DoesNotContain(g1.Said.Skip(said), l => l.Contains(ilwrathLine, StringComparison.Ordinal));
        // g2 answers set after set in its talk during the fight, while the story's waits.
        var m2 = world.MeetingOf("g2")!;
        for (int i = 0; i < 4; ++i)
        {
            g2.Session.PressMenu(MenuKey.Select);
            Frame(host, all);
            Frame(host, all);
        }
        Assert.Equal(5, m2.Talk!.Offers);

        // The fight won, the alliance's story talk to its end.
        var visit = world.Visit!;
        Win(host, wb, all);
        Say(host, "comandr", "cook_their_butts");
        Say(host, "comandr", "name_2");
        for (int i = 0; i < StarbaseVisit.TimePassingFrames + 2; ++i)
            Frame(host, all);
        Say(host, "starbas", "goodbye_commander");
        Assert.Null(world.Visit);
        // No set of the story's shares its number with one of g2's meeting's (each set numbered by the game's one counter).
        var storySets = visit.Rounds().ToList();
        Assert.True(storySets.Count >= 4);
        Assert.Empty(storySets.Intersect(Sets(m2)));
        // g1, whose meeting closed during the fight, is shown the story's closing lines, not its meeting's again.
        var storyEnd = host.TalkFor("g1")!;
        Assert.Equal((VisitStep.Done, "starbas"), (storyEnd.Step, storyEnd.Script));
        Frame(host, all);
        Assert.Equal("starbas", g1.Talks[^1]!.Script);
        // g2, in its meeting's talk all along, gets them too (a frame), then its talk.
        Frame(host, all);
        Assert.Equal((VisitStep.Done, "starbas"), (g2.Talks[^2]!.Step, g2.Talks[^2]!.Script));
        Assert.Equal(VisitStep.Meeting, g2.Talks[^1]!.Step);
        Assert.Equal(VisitStep.Meeting, g2.Session.Talk!.Step);
    }

    [ContentFact]
    public void AnAttackHeldThroughAStoryTalkAndMadeInItsClosingWindowLeavesTheStorysLinesShown()
    {
        // Item 11, review round 5: a meeting's talk over in the story's closing window (here an attack held
        // through the story, made as it ends) keeps the story's closing lines for its player (AfterTalk drops
        // them only once the window has passed).
        var game = NewGame(guests: 1);
        var host = game.Host;
        var world = game.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var g1 = NewClient(1);
        GroupBy(world, "g1", "UMGAH_SHIP");
        FramesUntil(host, () => g1.Session.Meeting is { OffersChoice: true }, 300, g1);
        var m = world.MeetingOf("g1")!;
        foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP" })
            world.Story!.Set(f, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 0);
        world.CommanderMet = true;
        world.VisitStarbase(world.Host);
        Assert.True(world.StoryPaused);
        g1.Session.PressMenu(MenuKey.Down);
        g1.Session.PressMenu(MenuKey.Select);   // Attack, held through the story
        Frame(host, g1);
        Assert.Contains("g1: attack (made as the pause lifts)", game.Log);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        // The commander's talk, then the Ilwrath's, end in its fight: the story's closing lines kept for g1, and its
        // attack made in the next frame, inside their window.
        Say(host, "comandr", "base_was_abandoned");
        Say(host, "ilwrath", "surrender");
        Assert.False(world.StoryPaused);
        int shown = g1.Talks.Count(t => t is { Step: VisitStep.Done, Script: "ilwrath" });
        Frame(host, g1);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(shown + 1, g1.Talks.Count(t => t is { Step: VisitStep.Done, Script: "ilwrath" }));
    }

    [ContentFact]
    public void AStoryTalksRepliesPassedToAPlayerInTheirMeetingsTalkAreTheirsToSay()
    {
        var game = NewGame(guests: 1);
        var host = game.Host;
        var world = game.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var g1 = NewClient(1);
        game.Init = Segue.Peace;
        GroupBy(world, "h", "SPATHI_SHIP");
        FramesUntil(host, () => world.MeetingOf("h") is { Step: MeetingStep.Talk, Talk.Replies.Count: > 0 }, 300, g1);
        var m = world.MeetingOf("h")!;
        // g1 starts the opening's story talk; the host's --meeting-replies wait for it, not held one after another.
        foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP" })
            world.Story!.Set(f, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 0);
        world.CommanderMet = true;
        world.VisitStarbase(world.Find("g1")!);
        Assert.True(world.StoryPaused);
        host.MeetingReplies.Enqueue(0);
        for (int i = 0; i < 3; ++i)
            Frame(host, g1);
        Assert.Single(host.MeetingReplies);
        Assert.Equal(MeetingStep.Talk, m.Step);
        host.MeetingReplies.Clear();   // the host replies by hand from here
        // g1 leaves: the story's replies pass to the host, in its meeting's talk, who is shown the story and says it.
        host.GuestLeft(1);
        Assert.Same(world.Host, world.Visit!.Replier);
        var shown = host.TalkFor("h")!;
        Assert.Equal(("comandr", "h"), (shown.Script, shown.Replier));
        // During the host's pause it says the story's reply and its meeting's: each held, neither replacing the other.
        int meetingRound = m.Round, sets = m.Talk!.Offers;
        Assert.NotEqual(meetingRound, world.Visit.Round);
        Assert.True(host.TogglePause());
        Say(host, "comandr", "base_was_abandoned");
        Assert.True(host.ChooseReply("h", meetingRound, 0));
        Assert.Equal(2, world.HeldReplies.Count(h => h.Id == "h"));   // the meeting's beside the story's
        Say(host, "comandr", "base_was_abandoned");   // said again: it replaces its own, not the meeting's
        Assert.Equal(new[] { world.Visit.Round, meetingRound }.Order(), world.HeldReplies.Where(h => h.Id == "h").Select(h => h.Round!.Value).Order());
        Assert.Equal("comandr", host.TalkFor("h")!.Script);
        Assert.True(host.TogglePause());
        // The story's made as the pause lifts; the meeting's waits for the story's end.
        Assert.Equal("ilwrath", host.TalkFor("h")!.Script);
        Assert.Equal((MeetingStep.Talk, sets), (m.Step, m.Talk.Offers));
        // The Ilwrath's answered: its fight has no starter (g1 gone), so the
        // opening is taken back, the story over, and the meeting's reply made.
        Say(host, "ilwrath", "surrender");
        Frame(host);
        Assert.False(world.StoryPaused);
        Assert.Contains(m.Talk.Events, e => e is ReplyChosen);
    }

    [ContentFact]
    public void AStoryRepliersMeetingReplyGoesToTheMeetingThoughItsSetCameAsTheStorysNumberOnceWould()
    {
        // The host in a meeting's talk offering set after set; g1 starts the
        // opening's story and fights its Ilwrath, the host answering its
        // meeting meanwhile (no story pause then); after the fight g1 leaves,
        // so the alliance's replies pass to the host. Counting on from each
        // talk's first round, the meeting's set and the story's came to the
        // same number here, and the meeting's reply went to the story.
        var game = NewGame(guests: 1);
        var host = game.Host;
        var world = game.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        var g1 = NewClient(1);
        game.Own["SPATHI_SHIP"] = seed => RepeatTalk(world.Story!, 30, seed);
        GroupBy(world, "h", "SPATHI_SHIP");
        FramesUntil(host, () => world.MeetingOf("h") is { Step: MeetingStep.Talk, Talk.Offers: > 0 }, 300, g1);
        var m = world.MeetingOf("h")!;
        foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP" })
            world.Story!.Set(f, 1);
        world.Story!.Set("STARBASE_AVAILABLE", 0);
        world.CommanderMet = true;
        world.VisitStarbase(world.Find("g1")!);
        var visit = world.Visit!;
        Say(host, "comandr", "base_was_abandoned");
        Say(host, "ilwrath", "surrender");
        var wb = visit.Fight!;
        for (int i = 0; i < Answers; ++i)
            Assert.True(host.ChooseReply("h", m.Round, 0));   // no pause in the fight: made at once
        Win(host, wb, g1);
        host.GuestLeft(1);
        Assert.Same(world.Host, visit.Replier);
        Assert.True(world.StoryPaused);
        int storyRound = visit.Round, meetingRound = m.Round, meetingSets = m.Talk!.Offers, storyEvents = visit.Talk!.Events.Count;
        // The meeting's reply (for its set) is the meeting's: held through the story, not said in it.
        Assert.True(host.ChooseReply("h", meetingRound, 0));
        Assert.Equal(storyEvents, visit.Talk.Events.Count);
        Assert.Contains(world.HeldReplies, h => h.Id == "h" && h.Round == meetingRound);
        // The story's reply is the story's.
        Say(host, "comandr", "cook_their_butts");
        Assert.Equal(meetingSets, m.Talk.Offers);
        Assert.NotEqual(storyRound, visit.Round);
        Assert.NotEqual(storyRound, meetingRound);   // the game's numbers: the two sets never one
    }

    // The meeting's sets the host answers during the fight: with three, the
    // meeting's set and the story's came to one number (4) as each talk
    // counted on from its own first round.
    private const int Answers = 3;

    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // The story talk's replier (the host) says the reply with this phrase id.
    private static void Say(HostSession host, string script, string phrase)
    {
        var talk = host.World.Visit!.Talk!;
        int n = Scripts[script].PhraseNumber(phrase);
        int i = talk.Replies.Select(r => r.Phrase).ToList().IndexOf(n);
        Assert.True(i >= 0, $"{phrase} isn't offered");
        Assert.True(host.ChooseReply(host.World.Visit.Replier.Id, host.World.Visit.Round, i));
    }

    // ---- one player, as before

    [ContentFact]
    public void TheHostAloneMeetsThroughItsOwnSession()
    {
        var game = NewGame(guests: 0);
        var host = game.Host;
        var world = game.World;
        var me = new PlayerSession(SystemGroupTests.Systems.Value, null, "h", TestContent.Shapes.HyperFlagship.Count);
        GroupBy(world, "h", "SPATHI_SHIP");
        // The host's game: its own world's meeting and talk shown each frame, its choice and reply made at once.
        void HostFrame()
        {
            if (me.TakeReply() is var (round, index))
                host.ChooseReply("h", round, index);
            if (me.TakeEncounterChoice() is var (meeting, action))
                host.ChooseEncounter("h", meeting, action);
            if (me.TakeClosingDone() is int closed)
                host.ClosingDone("h", closed);
            me.Observe(host.Step(ShipInput.None));
            me.ShowMeeting(host.MeetingFor("h"));
            me.ShowTalk(host.TalkFor("h"));
            foreach (var n in host.TakeBattleNotices().Host)
                me.ShowBattleNotice(n);
            me.FollowClosing(false, FrameSeconds);
        }
        for (int i = 0; i < 300 && me.Meeting is not { OffersChoice: true }; ++i)
            HostFrame();
        Assert.Equal(PlayerScreen.Encounter, me.Screen);
        me.PressMenu(MenuKey.Select);
        HostFrame();
        Assert.Equal(PlayerScreen.Comm, me.Screen);
        Assert.Equal(new[] { "What?" }, me.Talk!.Replies);
        me.PressMenu(MenuKey.Select);
        HostFrame();
        HostFrame();
        // The closing lines on the communication screen as UQM's (item 11), the game going on once they've played.
        Assert.Equal(MeetingStep.Closing, world.MeetingOf("h")!.Step);
        Assert.Equal(PlayerScreen.Comm, me.Screen);
        Assert.Equal(new[] { "Bye" }, me.Talk!.Lines);
        Assert.True(me.AtClosingLines);
        for (int i = 0; i < 400 && world.MeetingOf("h") is not null; ++i)
            HostFrame();
        HostFrame();
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(me.Meeting);
        Assert.Null(me.Talk);
        Assert.Contains("h: the closing lines heard", game.Log);
        Assert.Contains("h: hail", game.Log);
        Assert.Contains("h: reply 0", game.Log);
        // Alone, told nothing of it in words (item 9's notices are for the others).
        Assert.Empty(host.TakeBattleNotices().Host);
    }
}
