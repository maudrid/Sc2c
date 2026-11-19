// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Meeting a group (docs/plans/stage-3b.md, item 5): RaceCommunication's
/// order for the player a group touches (decision 5), the talk under the
/// race's lock, the battle from the group's ships, the group written back,
/// and the co-op rules for several players (our own; UQM's one player's
/// frames are HyperEncounterTests' and SystemGroupTests' recordings).
/// </summary>
[Trait("Topic", "world")]
public class MeetingTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly Lazy<SystemCatalog> Systems = new(() => SystemCatalog.Load(DataDir));
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A race's talk for the tests: its init sets how it would end (hostile:
    // the encounter screen), its intro one line and one reply, which ends it
    // as given.
    private sealed class TestScript : CommScript
    {
        private readonly Segue _init, _end;

        public TestScript(ConversationScript phrases, Segue init, Segue end) : base(phrases) => (_init, _end) = (init, end);

        public int Posts, Uninits;

        protected internal override void Init() => T.SetSegue(_init);

        protected internal override void Intro()
        {
            NPCPhrase("HELLO");
            Response("ask", Said);
        }

        private void Said(int r)
        {
            NPCPhrase("BYE");
            T.SetSegue(_end);
        }

        protected internal override void PostEncounter() => Posts++;

        protected internal override void Uninit() => Uninits++;
    }

    private static readonly CommDescription Plain = Scripts["comandr"].Description with { Ambient = Array.Empty<CommAnimation>() };

    private static (Talk Talk, TestScript Script) TestTalk(StoryState story, Segue init, Segue end, uint seed)
    {
        var phrases = new ConversationScript("probe", "init_probe_comm", Plain, new[] { "HELLO", "BYE", "ask" });
        var lines = Conversation.Parse("#(HELLO)\nHello\n#(BYE)\nBye\n#(ask)\nWhat?\n"u8);
        var script = new TestScript(phrases, init, end);
        var talk = new Talk(script, Plain, lines, 2, story, new Dictionary<string, int>(),
            new TalkTests.TestShip { Crew = 10, CrewCapacity = 50, Fuel = 100, FuelCapacity = 6000, Landers = 1 },
            new GameClock(2155, 2, 17), seed, _ => true);
        return (talk, script);
    }

    // A game in hyperspace: the host at (5800, 6100) in the Ur-Quan's
    // sphere and guests far off, every race's groups kept, no day's roll,
    // the battles' content and the dialogue locks; each race's talk the
    // test's (init and end), the last made kept in Scripts.
    private sealed class Game
    {
        public required GameWorld World;
        public readonly List<TestScript> Made = new();
        public Segue Init = Segue.Hostile, End = Segue.Peace;
    }

    private static Game NewGame(params string[] guests)
    {
        var systems = Systems.Value;
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        var game = new Game { World = world };
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        world.EncounterRaces = world.Story!.Rules.Races.ToHashSet();
        world.Story.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        world.BattleKit = WorldBattleTests.Kit();
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.MeetingTalks = (_, _, seed) =>
        {
            var (talk, script) = TestTalk(world.Story!, game.Init, game.End, seed);
            game.Made.Add(script);
            return talk;
        };
        short x = 5800;
        foreach (var p in world.Players)
        {
            p.Ship.Flight.PlaceAtUniverse(x, 6100);
            p.Ship.Flight.StarJustLeft = null;
            x += 600;
        }
        return game;
    }

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    // A group of a race right by a player (as one kept from before, its element made on the next frame).
    private static HyperEncounter GroupBy(GameWorld world, string id, string race, int ships = 2, short dx = 10)
    {
        var f = world.Find(id)!.Ship.Flight;
        var fleet = world.Story!.Fleet(race);
        var e = new HyperEncounter
        {
            Race = Race(world, race), Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + dx), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + dx)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        _ = fleet;
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story.Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    private static void Frames(GameWorld world, int frames, ShipInput input = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, input);
            world.Step();
        }
    }

    // Steps until the player's meeting is past its spin (or 200 frames).
    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 200 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    [ContentFact]
    public void AHostileTalkShowsTheEncounterScreenAndAHailRunsItUnderTheRacesLock()
    {
        var game = NewGame();
        var world = game.World;
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        Assert.Equal(0, world.Story!.Fleet("URQUAN_SHIP").KnownStrength);
        var m = Meet(world, "h");
        Assert.Equal((MeetingStep.Encounter, Race(world, "URQUAN_SHIP")), (m.Step, m.Race));
        Assert.Equal(Activity.Talk, world.Host.Ship.Activity);
        // The lock is the meeting's from the touch; the race's sphere is tracked (InitCommunication).
        Assert.Equal("h", world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        Assert.NotEqual(0, world.Story.Fleet("URQUAN_SHIP").KnownStrength);
        Assert.True(m.Talk!.OffersAttack);
        // The group's ships, with their crew, are the battle's to be.
        Assert.Equal(new[] { ("urquan", (int?)42), ("urquan", (int?)42) }, m.Ships.Select(s => (s.Ship, s.Crew)));
        Assert.False(world.ChooseReply("h", 0));   // no talk yet
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.False(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.Contains(m.Talk.Events, e => e is StateSet { Field: "BATTLE_SEGUE", Value: 0 });
        Assert.False(world.ChooseReply("h", 0, m.Round + 1));
        Assert.False(world.ChooseReply("h", 1, m.Round));
        Assert.True(world.ChooseReply("h", 0, m.Round));
        // Peace: the lock let go, the group re-forming one-shot (gone as its
        // element is made again), the host back in hyperspace spinning in.
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(world.DialogueLocks.Holder("URQUAN_CONVERSATION"));
        Assert.Equal(HyperEncounter.Reforming | HyperEncounter.OneShot, group.Flags & (HyperEncounter.Reforming | HyperEncounter.OneShot));
        Assert.Equal(new[] { 42, 42 }, group.Ships);
        Assert.Null(group.MeetingPlayer);
        Assert.Equal(Activity.Hyperspace, world.Host.Ship.Activity);
        Assert.True(world.Host.Ship.AppearFrames > 0);
        Frames(world, 1);
        Assert.DoesNotContain(group, world.Encounters!.List);
        Assert.Equal((1, 1), (game.Made.Single().Posts, game.Made.Single().Uninits));
    }

    [ContentFact]
    public void ATalkThatIsntHostileStartsAtOnceAndOneEndingHostileFightsTheGroupsShips()
    {
        var game = NewGame();
        game.Init = Segue.Peace;
        game.End = Segue.Hostile;
        var world = game.World;
        var group = GroupBy(world, "h", "SPATHI_SHIP", ships: 3);
        group.Ships[1] = 17;
        var m = Meet(world, "h");
        Assert.Equal(MeetingStep.Talk, m.Step);
        // Only the init set the segue: no screen's hail set it again.
        Assert.Single(m.Talk!.Events, e => e is StateSet { Field: "BATTLE_SEGUE" });
        Assert.False(world.EncounterChoice("h", EncounterAction.Attack));
        var at = (world.Host.Ship.Flight.LogX, world.Host.Ship.Flight.LogY);
        Assert.True(world.ChooseReply("h", 0));
        // The battle: the group's ships with their crew, its marker where they met, the lock let go.
        Assert.Equal(MeetingStep.Battle, m.Step);
        var wb = m.Battle!;
        Assert.Same(wb, world.Host.Ship.Battle);
        Assert.Equal(new[] { 30, 17, 30 }, wb.EnemyFleet.Ships.Select(s => s.Crew!.Value));
        Assert.True(wb.Start.PaysSalvage && wb.Start.RetreatAllowed);
        Assert.Equal((at.LogX, at.LogY), ((int)wb.Place.X, (int)wb.Place.Y));
        Assert.Null(world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        Assert.Equal("h", group.MeetingPlayer);   // the group stays where it met them
        // The second ship hurt further, then the host retreats: the group
        // re-forms (not one-shot) with the crews the battle left it.
        wb.Enemies[1].Starship.CrewLevel = 9;
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
            Frames(world, 1, ShipInput.TakeOff);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(BattleOutcome.Lost, wb.Outcome);
        Assert.Equal(new[] { 30, 9, 30 }, group.Ships);
        // Back in hyperspace that frame, its element made again: re-forming (100 steps), not one-shot.
        Assert.Equal(0, group.Flags & HyperEncounter.OneShot);
        Assert.Equal((short)100, group.TransitionState);
        Assert.Contains(group, world.Encounters!.List);
        Assert.Null(group.MeetingPlayer);
        Assert.Equal(Activity.Hyperspace, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void ABattleWonLeavesTheGroupNoShipsAndItsGone()
    {
        var game = NewGame();
        var world = game.World;
        var group = GroupBy(world, "h", "SPATHI_SHIP", ships: 1);
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var wb = m.Battle!;
        for (int i = 0; i < 400 && wb.Enemy.RaceDesc is null; ++i)
            Frames(world, 1);
        wb.Enemy.RaceDesc!.CrewLevel = 0;   // the Eluder's crew gone
        for (int i = 0; i < 400 && m.Step == MeetingStep.Battle; ++i)
            Frames(world, 1);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        Assert.Empty(m.Left!);
        Assert.Empty(group.Ships);
        Assert.DoesNotContain(group, world.Encounters!.List);   // gone as its element was to be made again
        Assert.Equal(world.BattleKit!.Salvage(world.BattleKit.Ships["spathi"]), (int)wb.SalvagePaid);
    }

    [ContentFact]
    public void AnAttackEndsTheTalkUnsaidAndFights()
    {
        var game = NewGame();
        var world = game.World;
        GroupBy(world, "h", "ILWRATH_SHIP");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var script = game.Made.Single();
        Assert.Equal((1, 1), (script.Posts, script.Uninits));
        Assert.True(m.Talk!.Ended);
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
        Assert.DoesNotContain(m.Talk.Events, e => e is LineQueued);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal(new[] { "ilwrath", "ilwrath" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
    }

    [ContentFact]
    public void WithoutTheRacesTalkAHailEndsInPeaceAndAnAttackFights()
    {
        var game = NewGame();
        var world = game.World;
        world.MeetingTalks = null;   // no race's talk ported yet (row 68)
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        var m = Meet(world, "h");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.Null(m.Talk);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal(HyperEncounter.OneShot, group.Flags & HyperEncounter.OneShot);
        Frames(world, 40);
        GroupBy(world, "h", "BLACK_URQUAN_SHIP");
        m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("blackurq", m.Battle!.EnemyShip.Id);
    }

    [ContentFact]
    public void AGroupWhoseRaceAnotherPlayerIsTalkingToPassesThroughThemAndTheyreToldOnce()
    {
        var game = NewGame("g");
        var world = game.World;
        GroupBy(world, "h", "URQUAN_SHIP");
        Meet(world, "h");
        Assert.Equal("h", world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        world.TakeBattleNotices();
        // An Ur-Quan group by the guest passes through them, who's told who's talking, once.
        var passing = GroupBy(world, "g", "URQUAN_SHIP", dx: 0);
        Frames(world, 30);
        Assert.Null(world.MeetingOf("g"));
        Assert.Null(passing.MeetingPlayer);
        var told = world.TakeBattleNotices().Where(n => n.PlayerId == "g").ToList();
        Assert.Equal("Host is talking to the Ur-Quan.", Assert.Single(told).Text);
        // A Kohr-Ah group meets them.
        GroupBy(world, "g", "BLACK_URQUAN_SHIP", dx: 0);
        Assert.Equal(Race(world, "BLACK_URQUAN_SHIP"), Meet(world, "g").Race);
        // The host's talk over, the Ur-Quan's lock is free.
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", 0));
        Assert.Null(world.DialogueLocks.Holder("URQUAN_CONVERSATION"));
    }

    [ContentFact]
    public void TheGroupMetStaysWhereItMetThePlayerAndIsntMetByAnother()
    {
        var game = NewGame("g");
        var world = game.World;
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        Meet(world, "h");
        // The guest flies on in hyperspace: the group keeps an element, stopped, the host's.
        var at = (group.LocX, group.LocY);
        world.Find("g")!.Ship.Flight.PlaceAtUniverse((short)(group.LocX - 30), group.LocY);
        Frames(world, 80);
        Assert.Equal(at, (group.LocX, group.LocY));
        Assert.Equal("h", group.Element!.Met);
        Assert.Null(world.MeetingOf("g"));
        Assert.Contains(group, world.Encounters!.List);
        // Its meeting over in peace, it re-forms and goes (one-shot).
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", 0));
        Frames(world, 1);
        Assert.DoesNotContain(group, world.Encounters.List);
    }

    [ContentFact]
    public void AGroupInAMeetingGetsItsElementBackAsThePlayersWhenSomeoneComesIntoHyperspace()
    {
        var game = NewGame("g");
        var world = game.World;
        world.PutInSystem("g", 82);   // the guest out of hyperspace: nobody's there once the host meets
        var group = GroupBy(world, "h", "URQUAN_SHIP");
        Meet(world, "h");
        Frames(world, 1);
        Assert.Null(group.Element);
        world.PutInHyperspace("g");
        world.Find("g")!.Ship.Flight.PlaceAtUniverse((short)(group.LocX - 20), group.LocY);
        var at = (group.LocX, group.LocY);
        Frames(world, 60);
        // Made again, it's the host's still: unsteered, untouchable.
        Assert.Equal("h", group.Element!.Met);
        Assert.Equal(at, (group.LocX, group.LocY));
        Assert.Null(world.MeetingOf("g"));
    }

    [ContentFact]
    public void TheSpinIntoAGroupBurnsItsDriftsFuelAndLeavesTheShipWhereItTookIt()
    {
        var game = NewGame();
        var world = game.World;
        GroupBy(world, "h", "URQUAN_SHIP", dx: 60);
        var ship = world.Host.Ship;
        for (int i = 0; i < 200 && ship.Meeting is null; ++i)
            Frames(world, 1);
        HyperFlight? spin = null;
        while (ship.Meeting is { Step: MeetingStep.Spin })
        {
            spin = ship.Spin!.Copy();
            Frames(world, 1);
        }
        Assert.NotNull(spin);
        Assert.Equal((spin!.LogX, spin.LogY, spin.Fuel, spin.FuelDistX, spin.FuelDistY),
            (ship.Flight.LogX, ship.Flight.LogY, ship.Flight.Fuel, ship.Flight.FuelDistX, ship.Flight.FuelDistY));
        Assert.Equal(HyperFlight.CenterX, ship.Flight.ViewX);
    }

    [ContentFact]
    public void APlayerOutOfTheirMeetingsBattleWhileAnotherFightsOnMeetsNoGroup()
    {
        var game = NewGame("g");
        var world = game.World;
        var group = GroupBy(world, "h", "SPATHI_SHIP");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        // The guest flies into the battle's marker and fights on as the host retreats.
        var guest = world.Find("g")!;
        guest.Ship.Flight.PlaceAtUniverse(world.Host.Ship.Flight.UniverseX, world.Host.Ship.Flight.UniverseY);
        Assert.True(world.LetIntoBattle("g"));
        Frames(world, 2);
        Assert.Same(m.Battle, guest.Ship.Battle);
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            world.Step();
        }
        Assert.Null(world.Host.Ship.Battle);
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("h", group.MeetingPlayer);   // written back as the battle ends
        // A group by the host passes through them meanwhile.
        GroupBy(world, "h", "BLACK_URQUAN_SHIP", dx: 0);
        Frames(world, 60);
        Assert.Single(world.HyperspaceMeetings);
        Assert.Same(m, world.MeetingOf("h"));
    }

    [ContentFact]
    public void APauseDuringTheSpinIntoAGroupDoesntShortenIt()
    {
        // Review round 1: the spin counts its own frames, as the spin back
        // in does, so a host's pause holds it where it is.
        (long X, long Y, int Frames, long Held, long End) Run(bool pause)
        {
            var game = NewGame();
            var world = game.World;
            GroupBy(world, "h", "URQUAN_SHIP");
            for (int i = 0; i < 200 && world.MeetingOf("h") is null; ++i)
                Frames(world, 1);
            var m = world.MeetingOf("h")!;
            Assert.Equal(MeetingStep.Spin, m.Step);
            int frames = 0;
            Frames(world, 2);
            frames += 2;
            if (pause)
            {
                var spin = world.Host.Ship.Spin!;
                var at = (spin.LogX, spin.LogY);
                world.SetHostPause(true);
                Frames(world, 40);
                world.SetHostPause(false);
                Assert.Equal(MeetingStep.Spin, m.Step);
                Assert.Equal(at, (world.Host.Ship.Spin!.LogX, world.Host.Ship.Spin.LogY));
            }
            for (; frames < 100 && m.Step == MeetingStep.Spin; ++frames)
                Frames(world, 1);
            return (world.Host.Ship.Flight.LogX, world.Host.Ship.Flight.LogY, frames,
                world.Host.Ship.InputHeldUntil - world.Frame, world.Frame);
        }
        var plain = Run(false);
        var paused = Run(true);
        Assert.Equal((plain.X, plain.Y, plain.Frames), (paused.X, paused.Y, paused.Frames));
        Assert.Equal(plain.End + 40, paused.End);
        // Input read during the spin is dropped up to its end as played.
        Assert.Equal(plain.Held, paused.Held);
    }

    [ContentFact]
    public void NoBattleMarkerIsShownOrJoinedWhileTheShipSpinsBackIn()
    {
        var game = NewGame("g");
        var world = game.World;
        GroupBy(world, "h", "URQUAN_SHIP");
        Meet(world, "h");
        // A battle of the guest's where the host is, the host counted as having been out of its reach.
        var guest = world.Find("g")!;
        guest.Ship.Flight.PlaceAtUniverse(world.Host.Ship.Flight.UniverseX, world.Host.Ship.Flight.UniverseY);
        world.StartBattle("g", "spathi");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", 0));
        Assert.True(world.LetIntoBattle("h"));
        Assert.Empty(world.MarkersFor("h"));
        Frames(world, 3);
        Assert.Null(world.Host.Ship.Battle);
        Assert.Empty(world.MarkersFor("h"));
        Frames(world, world.Host.Ship.AppearFrames + 1);
        Assert.NotEmpty(world.MarkersFor("h"));
    }

    [ContentFact]
    public void APlayerLeavingMidMeetingDropsIt()
    {
        var game = NewGame("g");
        var world = game.World;
        var group = GroupBy(world, "g", "URQUAN_SHIP");
        Meet(world, "g");
        world.Leave("g");
        Assert.Null(world.Find("g")!.Ship.Meeting);
        Assert.Null(world.DialogueLocks!.Holder("URQUAN_CONVERSATION"));
        Assert.Null(group.MeetingPlayer);
        Assert.Equal(0, group.Flags & (HyperEncounter.Reforming | HyperEncounter.OneShot));
        Assert.Equal(new[] { 42, 42 }, group.Ships);
    }

    [ContentFact]
    public void APlayerLeavingAsTheirShipSpinsIntoAGroupStopsWhereTheSpinTookIt()
    {
        var game = NewGame("g");
        var world = game.World;
        var group = GroupBy(world, "g", "URQUAN_SHIP", dx: 40);
        var guest = world.Find("g")!;
        for (int i = 0; i < 200 && guest.Ship.Meeting is null; ++i)
            Frames(world, 1);
        Frames(world, 4);
        Assert.Equal(MeetingStep.Spin, guest.Ship.Meeting!.Step);
        var spun = (guest.Ship.Spin!.LogX, guest.Ship.Spin.LogY);
        world.Leave("g");
        Assert.Null(guest.Ship.Spin);
        Assert.Equal(spun, (guest.Ship.Flight.LogX, guest.Ship.Flight.LogY));
        Assert.Null(group.Element?.Met);
    }

    [ContentFact]
    public void ARepliesChosenDuringTheHostsPauseAreChosenAsItLifts()
    {
        var game = NewGame();
        var world = game.World;
        GroupBy(world, "h", "URQUAN_SHIP");
        var m = Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.SetHostPause(true));
        Assert.True(world.ChooseReply("h", 0, m.Round));
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(world.SetHostPause(false));
        Assert.Equal(MeetingStep.Done, m.Step);
    }

    [ContentFact]
    public void TheShipSpinsBackInStillAndUnsteeredAfterAMeeting()
    {
        var game = NewGame();
        var world = game.World;
        GroupBy(world, "h", "URQUAN_SHIP");
        Meet(world, "h");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", 0));
        var f = world.Host.Ship.Flight;
        int frames = GameWorld.AppearingFrames(f.Facing, TestContent.Shapes!.HyperFlagship.Count);
        Assert.Equal(frames, world.Host.Ship.AppearFrames);
        var at = (f.LogX, f.LogY, f.Facing);
        Frames(world, frames, ShipInput.Thrust | ShipInput.Left);
        Assert.Equal(at, (f.LogX, f.LogY, f.Facing));
        Frames(world, 2, ShipInput.Thrust | ShipInput.Left);
        Assert.NotEqual(at, (f.LogX, f.LogY, f.Facing));
    }

    // ---- the session's testing start

    private static HostSession Session()
    {
        var systems = Systems.Value;
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        host.World.BattleKit = WorldBattleTests.Kit();
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        return host;
    }

    [ContentFact]
    public void StartEncounterPutsAGroupByTheHostInHyperspaceThatFliesAtThem()
    {
        var host = Session();
        Assert.Throws<ArgumentException>(() => host.StartEncounter("probe"));
        Assert.Throws<ArgumentException>(() => host.StartEncounter("spathi:0"));
        Assert.Throws<ArgumentException>(() => host.StartEncounter("spathi:8"));
        host.StartAt(5800, 6100);
        Assert.Equal("A group of 3 Spathi ships comes in by the host in hyperspace", host.StartEncounter("spathi:3"));
        Assert.Equal(1, host.World.Story!.Get("STARBASE_AVAILABLE"));
        var group = host.World.Encounters!.List.Single();
        Assert.Equal((host.World.Story.Rules.RaceIndex("SPATHI_SHIP"), 3), (group.Race, group.Ships.Count));
        host.EncounterChoice = EncounterAction.Attack;
        for (int i = 0; i < 600 && host.World.MeetingOf("h") is not { Step: MeetingStep.Battle }; ++i)
            host.Step(ShipInput.None);
        Assert.Equal(MeetingStep.Battle, host.World.MeetingOf("h")!.Step);
        Assert.Equal(3, host.World.MeetingOf("h")!.Battle!.EnemyFleet.Ships.Count);
    }

    [ContentFact]
    public void StartEncounterAfterTheOpeningPutsAGroupInEarthsSystem()
    {
        var host = Session();
        Assert.Equal("A group of 1 Ilwrath ship appears by the host in the system", host.StartEncounter("ilwrath:1"));
        var d = host.World.SystemGroups!.Systems[host.World.Host.Ship.SystemStar!.Value];
        var g = d.Groups.Single(g => g.Race == host.World.Story!.Rules.RaceIndex("ILWRATH_SHIP"));
        Assert.Equal((host.World.Host.Ship.Ip!.View, IpGroup.InterceptPlayer), (g.SysLoc, g.DestLoc));
        Assert.Equal(new[] { 22 }, host.World.SystemGroups.ShipsOf(d, g)!.Ships);
        host.EncounterChoice = EncounterAction.Hail;
        for (int i = 0; i < 2000 && host.World.SystemMeetings.Count == 0; ++i)
            host.Step(ShipInput.None);
        Assert.Single(host.World.SystemMeetings);
        Assert.Null(host.World.MeetingOf("h"));   // hailed at once: the race's talk not ported, ended in peace
        Assert.True((g.Task & IpGroup.IgnoreFlagship) != 0);
    }
}
