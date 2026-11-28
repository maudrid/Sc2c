// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The host's fleet (stage 4b item 6): each race's ship in the story's
/// rules, build.c's functions on the fleet (adding into the first free
/// slots, naming captains, counting, its worth and room, removing), the
/// talks' ship giving the host's fleet whoever answers, the Arilou's gift
/// from the commander's bulletin, saves, and --start-fleet and
/// --start-allied. Most without the content; the talks, saves with
/// systems and the host's testing starts with it.
/// </summary>
[Trait("Topic", "fleet")]
public class FleetTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private const string Arilou = "ARILOU_SHIP", Spathi = "SPATHI_SHIP", Yehat = "YEHAT_SHIP", Rebel = "YEHAT_REBEL_SHIP";

    // A generator drawing 0, 1, 2, ... (each name the next of the 16).
    private static Func<uint> Counting(uint from = 0)
    {
        uint n = from;
        return () => n++;
    }

    // A generator drawing these, then failing.
    private static Func<uint> Drawing(params uint[] values)
    {
        int i = 0;
        return () => i < values.Length ? values[i++] : throw new InvalidOperationException("drawn too often");
    }

    [Fact]
    public void EachRaceHasItsShipItsCostAndItsCrew()
    {
        // races.h's order, RACE_SHIP_COST, and the ships' crews as ships.json has them.
        Assert.Equal(Rules.Races, Rules.RaceShips.Select(r => r.Race));
        Assert.Equal(new RaceShip(Arilou, "arilou", 1600, 6, 6), Rules.RaceShip(Arilou));
        Assert.Equal(new RaceShip("HUMAN_SHIP", "human", 1100, 18, 18), Rules.RaceShip("HUMAN_SHIP"));
        Assert.Equal(new RaceShip("URQUAN_SHIP", "urquan", 3000, 42, 42), Rules.RaceShip("URQUAN_SHIP"));
        Assert.Equal(new RaceShip("BLACK_URQUAN_SHIP", "blackurq", 3000, 42, 42), Rules.RaceShip("BLACK_URQUAN_SHIP"));
        Assert.Equal(new RaceShip("SYREEN_SHIP", "syreen", 1300, 12, 42), Rules.RaceShip("SYREEN_SHIP"));
        // InitGameStructures: the rebels fly the Yehat's ship, the drone the probe (and costs nothing).
        Assert.Equal(new RaceShip(Rebel, "yehat", 2300, 20, 20), Rules.RaceShip(Rebel));
        Assert.Equal(("probe", 0), (Rules.RaceShip("URQUAN_DRONE_SHIP").Ship, Rules.RaceShip("URQUAN_DRONE_SHIP").Cost));
        // By ship: the race that can have a fleet, the Yehat's not the rebels'; none for ships of no such race.
        Assert.Equal(Yehat, Rules.RaceOfShip("yehat")!.Race);
        Assert.Equal("ZOQFOTPIK_SHIP", Rules.RaceOfShip("zoqfot")!.Race);
        Assert.All(new[] { "sis_ship", "androsyn", "chenjesu", "mmrnmhrm", "lastbat", "probe", "nothing" },
            s => Assert.Null(Rules.RaceOfShip(s)));
    }

    [Fact]
    public void ShipsJoinTheFirstFreeSlotsWithTheirRacesCrewAndCaptainsOfTheirOwn()
    {
        var fleet = new HostFleet();
        Assert.Equal((0, 12), (fleet.Count, fleet.Room));
        Assert.Equal(3, fleet.Add(Rules, Arilou, 3, Counting()));
        Assert.Equal(new[] { new FleetShip(Arilou, 6, 5, 0), new FleetShip(Arilou, 6, 6, 1), new FleetShip(Arilou, 6, 7, 2) }, fleet.Ships);
        Assert.Equal(2, fleet.Add(Rules, Spathi, 2, Counting()));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, fleet.Ships.Select(s => s.Slot));
        Assert.Equal((byte)5, fleet.InSlot(3)!.Captain);   // another race may have the same name
        Assert.Equal(30, fleet.InSlot(4)!.Crew);
        Assert.Null(fleet.InSlot(5));

        // A gap (slot 1 freed) is filled first, then the next past the end; the queue kept by slot.
        Assert.Equal(0, fleet.RemoveSome(Arilou, 1));   // the first by slot
        Assert.Equal(new[] { 1, 2, 3, 4 }, fleet.Ships.Select(s => s.Slot));
        fleet.Restore(Rules, fleet.Ships.Where(s => s.Slot != 3));   // slots 1, 2, 4
        Assert.Equal(2, fleet.Add(Rules, Yehat, 2, Counting()));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, fleet.Ships.Select(s => s.Slot));
        Assert.Equal((Yehat, Yehat), (fleet.InSlot(0)!.Race, fleet.InSlot(3)!.Race));
        Assert.Equal(1, fleet.Add(Rules, Yehat, 1, Counting()));
        Assert.Equal((Yehat, 5), (fleet.Ships[^1].Race, fleet.Ships[^1].Slot));
        Assert.Equal(3, fleet.CountOf(Yehat));

        // A count below 1, a race that isn't one, the drone: refused.
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.Add(Rules, Arilou, 0, Counting()));
        Assert.Throws<ArgumentException>(() => fleet.Add(Rules, "NOT_A_SHIP", 1, Counting()));
        Assert.Throws<ArgumentException>(() => fleet.Add(Rules, "URQUAN_DRONE_SHIP", 1, Counting()));
    }

    [Fact]
    public void NameCaptainDrawsAgainWhileAShipOfItsSpeciesHasTheName()
    {
        var fleet = new HostFleet();
        fleet.Add(Rules, Yehat, 1, Drawing(0));                 // name 5
        // A rebel is the Yehat's species: 5 again is drawn again, 16 (& 15 = 0) too, then 1 (6).
        fleet.Add(Rules, Rebel, 1, Drawing(0, 16, 1));
        Assert.Equal((byte)6, fleet.Ships[^1].Captain);
        // Another species' name is free.
        fleet.Add(Rules, Spathi, 1, Drawing(16));
        Assert.Equal((byte)5, fleet.Ships[^1].Captain);
        Assert.Equal((byte)7, fleet.NameCaptain(Rules, Yehat, Drawing(0, 1, 2)));
    }

    [Fact]
    public void AFullFleetTakesNoMoreButDrawsANameAsUqmsDoes()
    {
        var fleet = new HostFleet();
        int draws = 0;
        uint n = 0;
        uint Random() { ++draws; return n++; }
        Assert.Equal(10, fleet.Add(Rules, Spathi, 10, Random));
        Assert.Equal(10, draws);
        // Room for 2 of 5 asked: 2 join, and CloneShipFragment names a third before finding the queue full.
        Assert.Equal(2, fleet.Add(Rules, Arilou, 5, Random));
        Assert.Equal((12, 0, 13), (fleet.Count, fleet.Room, draws));
        Assert.Equal(0, fleet.Add(Rules, Arilou, 1, Random));
        Assert.Equal(14, draws);
    }

    [Fact]
    public void TheFleetIsWorthItsShipsCostsAndShipsLeaveByRace()
    {
        var fleet = new HostFleet();
        Assert.Equal(0, fleet.Worth(Rules));
        fleet.Add(Rules, Arilou, 3, Counting());
        fleet.Add(Rules, Spathi, 2, Counting());
        fleet.Add(Rules, Arilou, 1, Counting(10));
        Assert.Equal(4 * 1600 + 2 * 1800, fleet.Worth(Rules));
        Assert.Equal((4, 2, 0), (fleet.CountOf(Arilou), fleet.CountOf(Spathi), fleet.CountOf(Yehat)));
        // RemoveSomeEscortShips returns the count it couldn't remove (UQM's, whatever its comment says).
        Assert.Equal(0, fleet.RemoveSome(Arilou, 0));
        Assert.Equal(0, fleet.RemoveSome(Arilou, -1));   // UQM's count is unsigned: none asked for, none left
        Assert.Equal(6, fleet.Count);
        Assert.Equal(0, fleet.RemoveSome(Arilou, 2));
        Assert.Equal(new[] { 2, 3, 4, 5 }, fleet.Ships.Select(s => s.Slot));
        Assert.Equal(1, fleet.RemoveSome(Spathi, 3));
        Assert.Equal(0, fleet.CountOf(Spathi));
        fleet.Remove(Arilou);
        Assert.Equal(0, fleet.Count);
    }

    [Fact]
    public void AFleetShipsCrewIsSetWithinItsMost()
    {
        var fleet = new HostFleet();
        fleet.Add(Rules, "SYREEN_SHIP", 1, Counting());
        Assert.Equal(12, fleet.Ships[0].Crew);   // the Syreen's starting crew, not its most
        fleet.SetCrew(Rules, 0, 42);
        fleet.SetCrew(Rules, 0, 0);              // the shipyard's, while its crew is edited
        Assert.Equal(0, fleet.Ships[0].Crew);
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.SetCrew(Rules, 0, 43));
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.SetCrew(Rules, 0, -1));
        Assert.Throws<ArgumentException>(() => fleet.SetCrew(Rules, 1, 1));
    }

    [Fact]
    public void ARestoredFleetMustBeOne()
    {
        var fleet = new HostFleet();
        fleet.Add(Rules, Arilou, 2, Counting());
        var before = fleet.Ships.ToList();
        void Refused(params FleetShip[] ships)
        {
            Assert.Throws<ArgumentException>(() => fleet.Restore(Rules, ships));
            Assert.Equal(before, fleet.Ships);   // left as it was
        }
        Refused(new FleetShip("NOT_A_SHIP", 1, 5, 0));
        Refused(new FleetShip("URQUAN_DRONE_SHIP", 1, 5, 0));
        Refused(new FleetShip(Arilou, -1, 5, 0));
        Refused(new FleetShip(Arilou, 7, 5, 0));
        Refused(new FleetShip(Arilou, 6, 4, 0));
        Refused(new FleetShip(Arilou, 6, 21, 0));   // the name after the 16 is only Fwiffo's, a Spathi's
        Refused(new FleetShip(Spathi, 1, 22, 0));
        Refused(new FleetShip(Arilou, 6, 5, -1));
        Refused(new FleetShip(Arilou, 6, 5, 12));
        Refused(new FleetShip(Arilou, 6, 5, 3), new FleetShip(Spathi, 6, 5, 3));
        Refused(Enumerable.Range(0, 13).Select(i => new FleetShip(Arilou, 6, 5, i % 12)).ToArray());
        // In any order, kept by slot.
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 30, 20, 11), new FleetShip(Arilou, 0, 5, 4) });
        Assert.Equal(new[] { 4, 11 }, fleet.Ships.Select(s => s.Slot));
        Assert.Equal(0, fleet.InSlot(4)!.Crew);   // a ship whose crew the shipyard sent away stays
        // Fwiffo alone in his Eluder (spathic.c's SetEscortCrewComplement: NAME_OFFSET + NUM_CAPTAINS_NAMES).
        fleet.Restore(Rules, new[] { new FleetShip(Spathi, 1, 21, 0) });
        Assert.True(HostFleet.IsFwiffo(fleet.Ships.Single()));
    }

    // A world of hyperspace only with a story (the fleet's rules), the host and guests g1, ...
    private static GameWorld World(int players = 1)
    {
        var world = WorldBattleTests.NewWorld(players);
        world.Story = StoryState.NewGame(Rules, world.Clock);
        return world;
    }

    [ContentFact]
    public void TheTalksShipIsTheHostsFleetGivenWhoeverAnswers()
    {
        // Decision 3: only the host has a fleet; ships a talk gives join it whoever answers.
        var world = World(2);
        world.AddFleetShips(Spathi, 2);
        var guest = world.Find("g1")!;
        var strings = Library.Value.Strings;
        var hostShip = new PlayerTalkShip(world.Host, strings, world);
        var guestShip = new PlayerTalkShip(guest, strings, world);
        Assert.Equal((3600, 10), (hostShip.EscortsWorth, hostShip.EscortRoom));
        Assert.Equal((0, 10), (guestShip.EscortsWorth, guestShip.EscortRoom));   // a guest's fleet is worth nothing: they have none
        int arilou = Rules.RaceIndex(Arilou);
        Assert.Equal(3, guestShip.AddEscorts(arilou, 3, Counting()));
        Assert.Equal(3, world.Fleet.CountOf(Arilou));
        Assert.Equal((3600 + 3 * 1600, 7), (hostShip.EscortsWorth, guestShip.EscortRoom));
        // A race that isn't one of avail_race_q's adds none (AddEscortShips' GetStarShipFromIndex).
        Assert.Equal(0, hostShip.AddEscorts(Rules.Races.Count, 1, Counting()));
        Assert.Equal(0, hostShip.AddEscorts(-1, 1, Counting()));

        // The answerer's ship: the same, by who answers now.
        var answerer = world.Host;
        var talks = new AnswererTalkShip(() => answerer, strings, world);
        Assert.Equal(3600 + 3 * 1600, talks.EscortsWorth);
        answerer = guest;
        Assert.Equal((0, 7), (talks.EscortsWorth, talks.EscortRoom));
        Assert.Equal(0, talks.AddEscorts(-1, 1, Counting()));
        Assert.Equal(0, talks.AddEscorts(Rules.Races.Count, 1, Counting()));
        Assert.Equal(7, talks.AddEscorts(Rules.RaceIndex(Yehat), 9, Counting()));
        Assert.Equal((12, 0), (world.Fleet.Count, talks.EscortRoom));
    }

    [ContentFact]
    public void TheTalksShipLooksInAndTakesFromTheHostsFleetWhoeverAnswers()
    {
        // The stage 3b-2 plan's question 4: CountEscortShips, HaveEscortShip,
        // GetHeadLink and RemoveEscortShips read and change the host's fleet,
        // a guest's talk too (a guest sees the host's escorts).
        var world = World(2);
        world.AddFleetShips(Spathi, 1);
        world.AddFleetShips(Arilou, 2);
        world.AddFleetShips(Spathi, 1);
        var strings = Library.Value.Strings;
        var guestShip = new PlayerTalkShip(world.Find("g1")!, strings, world);
        int spathi = Rules.RaceIndex(Spathi), arilou = Rules.RaceIndex(Arilou);
        Assert.Equal(new[] { spathi, arilou, arilou, spathi }, guestShip.EscortRaces);
        Assert.Equal(1, guestShip.RemoveEscorts(spathi, 1));   // the first first
        Assert.Equal(new[] { Arilou, Arilou, Spathi }, world.Fleet.Ships.Select(s => s.Race));
        Assert.Equal(0, guestShip.RemoveEscorts(Rules.Races.Count, 5));   // no such race
        Assert.Equal(0, guestShip.RemoveEscorts(-1, 5));
        Assert.Equal(2, guestShip.RemoveEscorts(arilou, 0xFFFF));
        Assert.Equal(new[] { spathi }, new PlayerTalkShip(world.Host, strings, world).EscortRaces);

        // The answerer's ship: a ship the talks gave and took back again isn't
        // among those an opening taken back would take from the fleet.
        var answerer = world.Find("g1")!;
        var talks = new AnswererTalkShip(() => answerer, strings, world);
        Assert.Equal(2, talks.AddEscorts(arilou, 2, Counting()));
        Assert.Equal(2, talks.EscortsAdded.Count);
        Assert.Equal(new[] { arilou, arilou, spathi }, talks.EscortRaces);   // into the free slots, before the Spathi's
        Assert.Equal(1, talks.RemoveEscorts(spathi, 0xFFFF));   // not one the talks gave
        Assert.Equal(2, talks.EscortsAdded.Count);
        // One whose crew changed since (a battle, the roster) is still told by its slot, race and captain.
        world.Fleet.SetCrew(Rules, talks.EscortsAdded[0].Slot, 2);
        Assert.Equal(2, talks.RemoveEscorts(arilou, 0xFFFF));
        Assert.Empty(talks.EscortsAdded);
        Assert.Empty(talks.EscortRaces);
    }

    [ContentFact]
    public void TheTalksShipsBioDataIsTheAnswerersAndTakenBackWithTheirChanges()
    {
        // ITalkShip.BioData is writable (the Umgah's reward, the Melnorme's
        // buying): the answerer's hold's, and an opening taken back takes the
        // talks' change back from each ship.
        var world = World(2);
        var strings = Library.Value.Strings;
        var host = world.Host;
        var guest = world.Find("g1")!;
        host.Ship.Hold.SetBioData(10);
        new PlayerTalkShip(guest, strings, world).BioData = 5;
        Assert.Equal(5, guest.Ship.Hold.BioData);
        var answerer = host;
        var talks = new AnswererTalkShip(() => answerer, strings, world);
        talks.BioData += 200;
        answerer = guest;
        talks.BioData = 0;
        Assert.Equal((210, 0), (host.Ship.Hold.BioData, guest.Ship.Hold.BioData));
        Assert.Equal(200, talks.Changes[host].BioData);
        Assert.Equal(-5, talks.Changes[guest].BioData);
        // Without the talks (a save during an opening), and taken back.
        Assert.Equal(10, talks.Changes[host].Without(host.Ship.Hold.Save(), 0, 50).Hold.BioData);
        host.Ship.Hold.SetBioData(150);   // sold meanwhile, say: no lower than 0
        talks.Changes[host].TakeBackFrom(host.Ship);
        talks.Changes[guest].TakeBackFrom(guest.Ship);
        Assert.Equal((0, 5), (host.Ship.Hold.BioData, guest.Ship.Hold.BioData));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Ship.Hold.SetBioData(-1));
    }

    [ContentFact]
    public void BioDataPastWhatAUqmCountHoldsDoesntStopATalk()
    {
        // Review round 1: the hold keeps bio data with no upper limit (gifts,
        // the lander), so a talk adding to more than 65,535 goes on (UQM's
        // COUNT would wrap; the port's doesn't), and its change comes back off.
        var world = World(2);
        var host = world.Host;
        host.Ship.Hold.MoveCargo(new int[ShipHold.Categories], 65_530, +1);
        var answerer = host;
        var talks = new AnswererTalkShip(() => answerer, Library.Value.Strings, world);
        talks.BioData += 500;
        Assert.Equal(66_030, host.Ship.Hold.BioData);
        new PlayerTalkShip(host, Library.Value.Strings, world).BioData = 70_000;
        Assert.Equal(70_000 - 500, talks.Changes[host].Without(host.Ship.Hold.Save(), 0, 50).Hold.BioData);
        talks.Changes[host].TakeBackFrom(host.Ship);
        Assert.Equal(69_500, host.Ship.Hold.BioData);
    }

    [Fact]
    public void AddingOutsideATalkNeedsTheStorysRules()
    {
        var world = WorldBattleTests.NewWorld();
        Assert.Throws<InvalidOperationException>(() => world.AddFleetShips(Arilou, 1));
        world = World();
        Assert.Equal(2, world.AddFleetShips(Arilou, 2));
        Assert.Equal(2, world.Fleet.Ships.Select(s => s.Captain).Distinct().Count());
    }

    // ---------------------------------------------------------------- with the content

    private static readonly Lazy<SystemCatalog> Systems = WorldBattleTests.Systems;
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));
    private const int Sol = 82, Earth = 2, Luna = 1;

    // An allied game by the starbase, as StarbaseTests' (the host and a guest), with the Arilou's
    // bulletin due: the portal spawner found, every other bulletin given.
    private static GameWorld AlliedWithTheSpawner()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "host", "Host",
            Systems.Value, TestContent.Shapes);
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        world.UndockShown("guest");
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "PORTAL_SPAWNER" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", 3);
        story.Set("STARBASE_DAY", 3);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF & ~(1u << 5));   // only the Arilou's news
        world.CommanderMet = true;
        foreach (var p in world.Players.Where(p => p.Present))
        {
            world.PutInOrbit(p.Id, Sol, Earth, Luna);
            p.Ship.Ip!.ReturnFromOrbit();
        }
        return world;
    }

    // A player docks and talks to the commander: the talk's events.
    private static IReadOnlyList<TalkEvent> TalkToTheCommander(GameWorld world, string id)
    {
        var p = world.Find(id)!;
        world.VisitStarbase(p);
        Assert.Equal(Activity.Starbase, p.Ship.Activity);
        world.StarbaseKey(id, MenuKey.Select, out _);   // Talk
        return p.Ship.Visit!.Talk!.Events;
    }

    [ContentFact]
    public void TheCommandersBulletinBringsThreeArilouToTheHostsFleetWhoeverHearsIt()
    {
        var world = AlliedWithTheSpawner();
        var events = TalkToTheCommander(world, "guest");
        var added = Assert.Single(events.OfType<EscortsAdded>());
        Assert.Equal(new EscortsAdded(Rules.RaceIndex(Arilou), 3, 3), added);
        Assert.Equal(new[] { Arilou, Arilou, Arilou }, world.Fleet.Ships.Select(s => s.Race));
        Assert.Equal(new[] { 0, 1, 2 }, world.Fleet.Ships.Select(s => s.Slot));
        Assert.All(world.Fleet.Ships, s => Assert.Equal(6, s.Crew));
        // The menu's talk is the guest's alone (not story): the host is told (the stage 3b-2 plan's item 1).
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "host" && n.Text == "3 Arilou ships joined the fleet (Guest's talk).");
        // The captains are named from the talk's own generator (NameCaptain's TFB_Random), drawn just before.
        int at = events.ToList().IndexOf(added);
        var drawn = events.Take(at).Reverse().TakeWhile(e => e is RandomDrawn).Reverse().Cast<RandomDrawn>().Select(r => r.Value).ToList();
        Assert.True(drawn.Count >= 3, $"{drawn.Count} names drawn");
        var replay = new HostFleet();
        replay.Add(Rules, Arilou, 3, Drawing(drawn.ToArray()));
        Assert.Equal(replay.Ships, world.Fleet.Ships);
        // Given once the talk is over: a second talk gives none.
        var visit = world.Find("guest")!.Ship.Visit!;
        int bye = visit.Talk!.Replies.Select(r => r.Phrase).ToList().IndexOf(Scripts["starbas"].PhraseNumber("goodbye_commander"));
        Assert.True(world.ChooseReply("guest", bye, visit.Round));
        Assert.Equal(1u << 5, world.Story!.Get32("STARBASE_BULLETS0") & (1u << 5));
        world.StarbaseKey("guest", MenuKey.Select, out _);
        Assert.Empty(visit.Talk!.Events.OfType<EscortsAdded>());
        Assert.Equal(3, world.Fleet.Count);
    }

    [ContentFact]
    public void AStoryTalksGiftIsShownToEveryoneNotToldAgain()
    {
        // Review round 1: the allied commander's talk as the Chmmr's bomb is
        // installed (CHMMR_BOMB_STATE 2: time passes, then his talk, story,
        // shown to every player) brings the Arilou with its bulletins; nobody
        // is told so in a notice as well (GameWorld.TellTalkEffects skips a
        // story talk), not as it runs nor as its replies go on.
        var world = AlliedWithTheSpawner();
        world.Story!.Set("CHMMR_BOMB_STATE", 2);
        world.TakeNotices();
        var host = world.Host;
        world.VisitStarbase(host);
        var visit = host.Ship.Visit!;
        Assert.Equal(VisitStep.TimePassing, visit.Step);
        for (int i = 0; i < 1000 && visit.Step == VisitStep.TimePassing; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, Hyperspace.ShipInput.None);
            world.Step();
        }
        Assert.Equal(VisitStep.StarbaseReady, visit.Step);
        Assert.True(visit.Story);
        Assert.Equal(new EscortsAdded(Rules.RaceIndex(Arilou), 3, 3), Assert.Single(visit.Talk!.Events.OfType<EscortsAdded>()));
        Assert.Equal(3, world.Fleet.CountOf(Arilou));
        int bye = visit.Talk!.Replies.Select(r => r.Phrase).ToList().IndexOf(Scripts["starbas"].PhraseNumber("goodbye_commander"));
        Assert.True(world.ChooseReply("host", bye, visit.Round));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event is NoticeEvent.ShipsGiven);
    }

    [ContentFact]
    public void AFullFleetHearsNoArilouBulletin()
    {
        // EscortFeasibilityStudy: no room, the news waits (STARBASE_BULLETIN_6 only with room, or repeated).
        var world = AlliedWithTheSpawner();
        world.AddFleetShips(Spathi, 12);
        var events = TalkToTheCommander(world, "host");
        Assert.Empty(events.OfType<EscortsAdded>());
        Assert.Equal(0u, world.Story!.Get32("STARBASE_BULLETS0") & (1u << 5));
        Assert.Equal(0, world.Fleet.CountOf(Arilou));
    }

    [ContentFact]
    public void TheCommandersAdviceCountsTheHostsFleetForTheHost()
    {
        // CalculateEscortsWorth: the host's fleet for the host, nothing for a guest (no fleet of their own).
        var world = AlliedWithTheSpawner();
        world.Story!.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);
        world.AddFleetShips("CHMMR_SHIP", 2);
        var host = new PlayerTalkShip(world.Host, Library.Value.Strings, world);
        var guest = new PlayerTalkShip(world.Find("guest")!, Library.Value.Strings, world);
        Assert.Equal((6000, 0), (host.EscortsWorth, guest.EscortsWorth));
    }

    [ContentFact]
    public void TheArilouJoinAsFarAsTheFleetHasRoomAndTheNewsIsGiven()
    {
        // AddEscortShips takes what room there is: one of the three; the bulletin is told all the same.
        var world = AlliedWithTheSpawner();
        world.AddFleetShips(Spathi, 11);
        var events = TalkToTheCommander(world, "host");
        Assert.Equal(new EscortsAdded(Rules.RaceIndex(Arilou), 3, 1), Assert.Single(events.OfType<EscortsAdded>()));
        Assert.Equal((12, 1), (world.Fleet.Count, world.Fleet.CountOf(Arilou)));
        Assert.Equal(Arilou, world.Fleet.InSlot(11)!.Race);
        var visit = world.Host.Ship.Visit!;
        int bye = visit.Talk!.Replies.Select(r => r.Phrase).ToList().IndexOf(Scripts["starbas"].PhraseNumber("goodbye_commander"));
        Assert.True(world.ChooseReply("host", bye, visit.Round));
        Assert.Equal(1u << 5, world.Story!.Get32("STARBASE_BULLETS0") & (1u << 5));
    }

    [ContentFact]
    public void AFleetIsSavedAndLoadedAndAnOlderSaveHasNone()
    {
        var world = AlliedWithTheSpawner();
        world.AddFleetShips(Arilou, 2);
        world.AddFleetShips("SYREEN_SHIP", 1);
        world.Fleet.SetCrew(Rules, 2, 30);
        world.Fleet.SetCrew(Rules, 1, 1);
        string json = HostSave.ToJson(world);
        var node = JsonNode.Parse(json)!;
        Assert.Equal(HostSave.Version, node["version"]!.GetValue<int>());   // the fleet since 12
        Assert.Equal(3, node["fleet"]!.AsArray().Count);
        Assert.Equal("ARILOU_SHIP", node["fleet"]![0]!["race"]!.GetValue<string>());
        var loaded = HostSave.FromJson(json, Systems.Value, TestContent.Shapes);
        Assert.Equal(world.Fleet.Ships, loaded.Fleet.Ships);
        // A ship of no crew (the shipyard's, while its crew is edited) is saved scrapped, as leaving would scrap it
        // (stage 4b item 7; ShipyardTests).
        world.Fleet.SetCrew(Rules, 1, 0);
        Assert.Equal(new[] { 0, 2 }, HostSave.FromJson(HostSave.ToJson(world), Systems.Value, TestContent.Shapes).Fleet.Ships
            .Select(s => s.Slot));

        // An older save with one, refused; an older save loads with none.
        var old = JsonNode.Parse(json)!;
        old["version"] = 11;
        old["story"]!.AsObject().Remove("fleets");   // saves before 13 have no races' fleets
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(old.ToJsonString(), Systems.Value, TestContent.Shapes));
        old["fleet"] = new JsonArray();
        Assert.Empty(HostSave.FromJson(old.ToJsonString(), Systems.Value, TestContent.Shapes).Fleet.Ships);
        old.AsObject().Remove("fleet");
        Assert.Empty(HostSave.FromJson(old.ToJsonString(), Systems.Value, TestContent.Shapes).Fleet.Ships);

        // A bad fleet is refused.
        foreach (var (field, value) in new (string, JsonNode?)[] { ("crew", -1), ("captain", 30), ("slot", 12), ("race", "NOT_A_SHIP"), ("race", null) })
        {
            var bad = JsonNode.Parse(json)!;
            bad["fleet"]![0]![field] = value?.DeepClone();
            Assert.Throws<InvalidDataException>(() => HostSave.FromJson(bad.ToJsonString(), Systems.Value, TestContent.Shapes));
        }
        var nullShip = JsonNode.Parse(json)!;
        nullShip["fleet"]![0] = null;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(nullShip.ToJsonString(), Systems.Value, TestContent.Shapes));
    }

    [Fact]
    public void ASaveWithAFleetNeedsTheDataToCheckItBy()
    {
        // A hyperspace world's save without its story loads without systems; with a fleet it can't be checked: refused.
        var world = World();
        world.AddFleetShips(Arilou, 1);
        var n = JsonNode.Parse(HostSave.ToJson(world))!.AsObject();
        n.Remove("story");
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(n.ToJsonString()));
        n["fleet"] = new JsonArray();
        Assert.Empty(HostSave.FromJson(n.ToJsonString()).Fleet.Ships);
    }

    [ContentFact]
    public void TheOpeningVisitIsSavedAndTakenBackWithoutTheShipsItsTalksGave()
    {
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "host", "Host",
            Systems.Value, TestContent.Shapes);
        host.World.TalkLibrary = Library.Value;
        host.World.DialogueLocks = DialogueLocks.Load(DataDir);
        host.World.AddFleetShips(Spathi, 1);
        var before = host.World.Fleet.Ships.ToList();
        host.StartVisit();
        var visit = host.World.Visit!;
        Assert.True(visit.Opening);
        var rules = host.World.Story!.Rules;
        Assert.Equal(2, visit.Talk!.AddEscortShips(rules.Races.ToList().IndexOf(Arilou), 2));   // as a talk in it might
        // What else changes the fleet meanwhile isn't the visit's: kept (a guest's talk elsewhere, a battle, the roster).
        host.World.AddFleetShips(Spathi, 1);
        host.World.Fleet.SetCrew(rules, 0, 7);
        var kept = host.World.Fleet.Ships.Where(s => s.Race == Spathi).ToList();
        Assert.Equal(new[] { 0, 3 }, kept.Select(s => s.Slot));
        var saved = HostSave.FromJson(HostSave.ToJson(host.World), Systems.Value, TestContent.Shapes);
        Assert.Equal(kept, saved.Fleet.Ships);
        visit.Undo();
        Assert.Equal(kept, host.World.Fleet.Ships);
        Assert.NotEqual(before, kept);
    }

    [ContentFact]
    public void TheHostsTestingStartsSetTheFleetAndTheAlliances()
    {
        var host = Session.HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Value.Map), "host", "Host",
            Systems.Value, TestContent.Shapes);
        Assert.Equal("Starting with a fleet of 3 (arilou, spathi, arilou)", host.StartWithFleet(new[] { "arilou", "spathi:5", "arilou:6" }));
        Assert.Equal(new[] { (Arilou, 6, 0), (Spathi, 5, 1), (Arilou, 6, 2) },
            host.World.Fleet.Ships.Select(s => (s.Race, s.Crew, s.Slot)));
        Assert.NotEqual(host.World.Fleet.Ships[0].Captain, host.World.Fleet.Ships[2].Captain);
        // Again: the fleet is these ships, whatever there was.
        host.StartWithFleet(new[] { "yehat" });
        Assert.Equal(Yehat, Assert.Single(host.World.Fleet.Ships).Race);
        // Refused, changing nothing: a ship of no fleet race, a crew out of range, more than 12.
        foreach (var bad in new[] { new[] { "androsyn" }, new[] { "sis_ship" }, new[] { "probe" }, new[] { "spathi:0" }, new[] { "spathi:31" },
                     new[] { "spathi:x" }, new[] { "spathi:1:2" }, Enumerable.Repeat("arilou", 13).ToArray() })
        {
            Assert.Throws<ArgumentException>(() => host.StartWithFleet(bad));
            Assert.Equal(Yehat, Assert.Single(host.World.Fleet.Ships).Race);
        }
        Assert.Equal("Starting with a fleet of 0", host.StartWithFleet(Array.Empty<string>()));
        Assert.Empty(host.World.Fleet.Ships);

        Assert.Equal(Allied.Bad, host.World.Story!.Alliance(Spathi));
        Assert.Equal("Starting allied with spathi, chmmr", host.StartAllied(new[] { "spathi", "chmmr" }));
        Assert.Equal((Allied.Good, Allied.Good), (host.World.Story.Alliance(Spathi), host.World.Story.Alliance("CHMMR_SHIP")));
        Assert.Throws<ArgumentException>(() => host.StartAllied(new[] { "orz", "androsyn" }));
        Assert.Equal(Allied.Bad, host.World.Story.Alliance("ORZ_SHIP"));   // nothing changed
    }
}
