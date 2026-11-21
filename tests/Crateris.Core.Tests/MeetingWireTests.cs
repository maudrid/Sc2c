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
            world.MeetingTalks = (_, _, seed) =>
            {
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
    }

    private static Client NewClient(long peer) => new()
    {
        Peer = peer,
        Session = new PlayerSession(SystemGroupTests.Systems.Value, null, $"g{peer}", TestContent.Shapes.HyperFlagship.Count),
    };

    // One host frame as the game runs it: the host's step, then each guest
    // sent its snapshot (a delta from the one it said it has), its meeting
    // and talk if they changed, and its notices; then what each sends back
    // (its encounter screen's choice, its reply).
    private static WorldSnapshot Frame(HostSession host, params Client[] clients)
    {
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
        }
        return snapshot;
    }

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
        int spathi = world.Story!.Rules.RaceIndex("SPATHI_SHIP");
        var last = Frame(host, g1, g2);
        for (int i = 0; i < 20 && e.Element is not { Drawn: true }; ++i)
            last = Frame(host, g1, g2);
        Assert.True(e.Element!.Drawn);
        int deltas = g1.Session.SnapshotsDelta;
        for (int i = 0; i < 6; ++i)
        {
            // g1 is sent the group on its screen, as the world has it; g2, 1200 units off, and the host nothing.
            var seen = Assert.Single(g1.Session.Snapshot!.Groups!);
            Assert.Equal(new GroupSnapshot(e.SnapshotId, null, spathi, 0, e.Element.NextX, e.Element.NextY, e.Element.Frame,
                e.TransitionState), seen);
            Assert.Equal(GroupSnapshot.SeenBy(last, "g1"), g1.Session.Snapshot.Groups);
            Assert.Equal(g1.Session.Snapshot.Groups, g1.Session.Groups);
            Assert.Null(g2.Session.Snapshot!.Groups);
            Assert.Empty(GroupSnapshot.SeenBy(last, "h"));
            Assert.Equal(seen, Assert.Single(last.Groups!));
            last = Frame(host, g1, g2);
        }
        Assert.True(g1.Session.SnapshotsDelta > deltas);   // sent as deltas, the group's moves with them
        Assert.NotEqual(0, e.SnapshotId);
        // It flies at g1 and meets it: g1 then sees no groups (it's at the encounter screen).
        FramesUntil(host, () => world.MeetingOf("g1") is { Step: MeetingStep.Encounter }, 300, g1, g2);
        Frame(host, g1, g2);
        Assert.Null(g1.Session.Snapshot!.Groups);
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
        var far = new GroupSnapshot(5, null, 3, 0, Log(5200), Uqm.Units.UniverseToLogY(5000), 31, 0);
        var inView = new GroupSnapshot(1, 7, 9, 3, -1200, 800, Flags: GroupSnapshotFlags.Met);
        var outer = new GroupSnapshot(2, 7, 9, 0, 300, 300, Flags: GroupSnapshotFlags.Hidden);
        var first = new WorldSnapshot(10, 2155, 2, 17, ships, Groups: new[] { near, far, inView, outer });
        var writer = new WireWriter();

        // Each viewer is sent what it sees: a ship in hyperspace those on its screen, one in a system those in its view, one in orbit none.
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
        var come = new GroupSnapshot(6, null, 5, 0, Log(4990), Uqm.Units.UniverseToLogY(5010), 0, 9);
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
        Assert.Equal(26, Protocol.Version);   // groups and meetings (stage 3b-1 item 10)
        var game = NewGame(guests: 0);
        var refused = game.Host.Hello(9, "old", "Old", 25);
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

        // The keys go round Hail and Attack; Enter chooses, once.
        g1.Session.PressMenu(MenuKey.Down);
        Assert.Equal(EncounterAction.Attack, g1.Session.EncounterCursor);
        Assert.Contains("  Hail   > Attack <", g1.Session.StatusLines());
        g1.Session.PressMenu(MenuKey.Up);
        g1.Session.PressMenu(MenuKey.Select);
        g1.Session.PressMenu(MenuKey.Down);   // chosen: the keys do nothing more
        Assert.Equal(EncounterAction.Hail, g1.Session.EncounterCursor);
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

        // Over in peace: no meeting; the closing lines (since the reply) a notice, the screen back to hyperspace.
        Assert.Null(world.MeetingOf("g1"));
        Frame(host, g1, g2);
        Assert.Null(g1.Session.Meeting);
        Assert.Null(g1.Meetings[^1]);
        var closing = g1.Talks[^1]!;
        Assert.Equal((VisitStep.Done, "What?"), (closing.Step, closing.Said));
        Assert.Equal(new[] { "Bye" }, closing.Lines);
        Assert.Empty(closing.Replies);
        Assert.Null(g1.Session.Talk);
        Assert.Contains("Bye", g1.Session.Notice);
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
        Frame(host, g1, g2);
        var probe = world.Story!.Rules.RaceShips[world.Story.Rules.RaceIndex("SLYLANDRO_SHIP")];
        uint bounty = (uint)(probe.Cost >> 3);
        Assert.NotEqual(0u, bounty);
        var won = g1.Notices.Single(n => n.Kind == BattleNoticeKind.Ended);
        Assert.Equal(new BattleNotice(BattleNoticeKind.Ended, 0, probe.Ship, BattleOutcome.Won, BattleExit.Fought, 3 * bounty,
            world.Find("g1")!.Ship.Hold.Crew, Destroyed: 3, Bounty: bounty), won);
        Assert.Empty(g2.Notices.Where(n => n.Kind == BattleNoticeKind.Ended));
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
        // Begun and over in one frame (its message, no screen, no replies): only its lines are sent, as a talk's closing lines.
        Assert.Empty(g1.Meetings);
        var message = Assert.Single(g1.Talks)!;
        Assert.Equal(VisitStep.Done, message.Step);
        Assert.NotEmpty(message.Lines);
        Assert.Equal(host.TalkFor("g1"), message, TalkViewSame.Instance);
        Assert.Contains(said, l => l.Contains(OneLine(message.Lines[0])));   // a notice, a page at a time
        Assert.NotEqual("", g1.Session.Notice);
        Assert.Null(g1.Session.Talk);
        Assert.Equal(1, world.Story!.Get("PROBE_MESSAGE_DELIVERED"));
    }

    private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    private sealed class TalkViewSame : IEqualityComparer<TalkView?>
    {
        public static readonly TalkViewSame Instance = new();
        public bool Equals(TalkView? a, TalkView? b) => a is null ? b is null : a.Same(b);
        public int GetHashCode(TalkView? v) => 0;
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
            me.Observe(host.Step(ShipInput.None));
            me.ShowMeeting(host.MeetingFor("h"));
            me.ShowTalk(host.TalkFor("h"));
            foreach (var n in host.TakeBattleNotices().Host)
                me.ShowBattleNotice(n);
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
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(me.Meeting);
        Assert.Contains("Bye", me.Notice);
        Assert.Contains("h: hail", game.Log);
        Assert.Contains("h: reply 0", game.Log);
        // Alone, told nothing of it in words (item 9's notices are for the others).
        Assert.Empty(host.TakeBattleNotices().Host);
    }
}
