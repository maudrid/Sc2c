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
using Crateris.Core.Session;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Druuge and the Mycon in the world (the stage 3b-2 plan's item 8):
/// the Druuge's trade world, at peace and never orbited while they live,
/// hostile once DRUUGE_MANNER is set; the trade in the answering ship's
/// crew: a Mauler to the host's fleet while it has room, fuel into the
/// answering ship's own tanks, the artifacts as the group's story flags,
/// the crew sold by every player counted together and the crew's price
/// raised at the starbase for everyone (the shipyard's price, the
/// commander's bulletin), the goods sold for ships and fuel or for the
/// Rosy Sphere, a ship left with no crew but its captain; the lock; in
/// space the salvage fight and their anger; their 5 ships at the bomb's
/// moon once the Utwig have their Ultron. The Mycon: a group in space,
/// whose replies fight or go on as the talk's own draw says; their
/// homeworld's endless fleet; the Sun Device's world, guarded without end
/// and then by the 5 that fell for the ambush; the trick at their
/// homeworld and its event set aside until 3c; the thief's greeting; the
/// shattered worlds seen from orbit and asked about. A Druuge talk left or
/// saved before its goodbye counting the crew sold all the same. Each
/// expectation is UQM's (druugec.c, myconc.c, gendru.c, genmyc.c,
/// genutw.c); the scripts' every path is TalkTests' recordings.
/// </summary>
[Trait("Topic", "world")]
public class DruugeMyconTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // A system of random groups only (an Ur-Quan system), cleared for the group brought.
    private const int GroupStar = 311;
    private const int Sol = 82, Earth = 2, Luna = 1;

    // The bomb's moon (genutw.c:107: matchWorld (5, 1)).
    private const int BombPlanet = 5, BombMoon = 1;

    private static GameWorld NewGame(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.BattleKit = WorldBattleTests.Kit();
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        return world;
    }

    private static int Star(GameWorld world, string special) =>
        world.Systems!.Map.Stars.Single(s => s.Special == special).Number;

    private static int Race(GameWorld world, string race) => world.Story!.Rules.RaceIndex(race);

    private static void Frames(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 600 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    // A group of a race brought to the player in a system of no groups of its own, and met.
    private static Meeting MeetInSystem(GameWorld world, string id, string race, int ships = 1)
    {
        world.PutInSystem(id, GroupStar);
        Frames(world, 1);
        world.SystemGroups!.Systems[GroupStar].Groups.Clear();
        world.AddEncounterNear(id, Race(world, race), ships);
        return Meet(world, id);
    }

    // A player's ship sent off to Sol, out of the way of another's meetings and battles.
    private static void Away(GameWorld world, string id)
    {
        world.PutInSystem(id, Sol);
        Frames(world, 2);   // the system left is dropped with nobody in it
    }

    // The player's ship orbits a world, its last meeting's escape counter run out: the meeting that starts.
    private static Meeting Orbit(GameWorld world, string id, int star, int planet, int? moon = null)
    {
        var ship = world.Find(id)!.Ship;
        if (ship.Ip is { } ip)
        {
            if (ip.Orbit is not null)
                ip.ReturnFromOrbit();
            ip.EscapeCounter = 0;
        }
        world.PutInOrbit(id, star, planet, moon);
        return Assert.IsType<Meeting>(world.MeetingOf(id));
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Offered(Talk talk, string phrase) =>
        talk.Replies.Any(r => r.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    private static void Choose(GameWorld world, string id, string phrase) =>
        Assert.True(world.ChooseReply(id, Reply(world.MeetingOf(id)!.Talk!, phrase)));

    private static void Choose(GameWorld world, string id, params string[] phrases)
    {
        foreach (var phrase in phrases)
            Choose(world, id, phrase);
    }

    private static bool Said(Talk talk, string phrase) => Count(talk, phrase) > 0;

    // How many times the talk has said a line.
    private static int Count(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Count(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The player retreats from their meeting's battle, their ship kept whole meanwhile (the tests here
    // aren't about who wins).
    private static void Retreat(GameWorld world, string id)
    {
        var ship = world.Find(id)!.Ship;
        for (int i = 0; i < 600 && ship.Battle is { } wb; ++i)
        {
            foreach (var f in wb.Fighters)
                if (f.Starship.RaceDesc is { } rd)
                    rd.CrewLevel = rd.MaxCrew;
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? ShipInput.TakeOff : ShipInput.None);
            world.Step();
        }
        Assert.Null(ship.Battle);
        Assert.False(ship.Destroyed);
    }

    // Crew pods in a ship's free module slots (DeltaSISGauges keeps the crew within what the ship holds), and its crew.
    private static void Crew(PlayerShip ship, int crew, int pods = 0)
    {
        int[] free = ship.Modules.ModuleSlots.Select((m, i) => (m, i)).Where(x => x.m == ShipModules.EmptyModule).Select(x => x.i).ToArray();
        foreach (int slot in free.Take(pods))
            ship.Modules.SetModule(slot, ShipModules.CrewPod);
        ship.Refit();
        Assert.True(ship.Hold.CrewCapacity >= crew, $"room for {ship.Hold.CrewCapacity} crew, not {crew}");
        ship.Hold.SetCrew(crew);
    }

    // A ship's weapons taken out of its module slots.
    private static void Disarm(PlayerShip ship)
    {
        for (int i = 0; i < ship.Modules.ModuleSlots.Count; ++i)
            if (ShipModules.IsWeapon(ship.Modules.ModuleSlots[i]))
                ship.Modules.SetModule(i, ShipModules.EmptyModule);
        ship.Refit();
        Assert.DoesNotContain(ship.Modules.ModuleSlots, ShipModules.IsWeapon);
    }

    private static int CrewSold(Story.StoryState story) =>
        story.Get("CREW_SOLD_TO_DRUUGE0") | story.Get("CREW_SOLD_TO_DRUUGE1") << 8;

    // ------------------------------------------------------------ the Druuge's trade world

    [ContentFact]
    public void TheTradeWorldTalksAtPeaceAndIsNeverOrbited()
    {
        // gendru.c:87-110: the Druuge alive, their trade world starts their talk at bit 7 with their
        // groups stood down (NotifyOthers) and an infinite fleet, and returns whatever the talk: no orbit.
        // init_druuge_comm: not hostile for good and at the trade world, at peace: no encounter screen.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "DRUUGE_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        var m = Orbit(world, "h", star, 0);
        Assert.IsType<DruugeScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Talk), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.Equal("h", world.DialogueLocks!.Holder("DRUUGE_CONVERSATION"));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "The Druuge hold off: Host is meeting them."));
        Assert.True(Said(m.Talk, "INITIAL_TRADE_WORLD_HELLO"));
        Assert.Equal(1, story.Get("DRUUGE_HOME_VISITS"));
        // Their news, once a talk (DISABLE_PHRASE), the next the next time (DRUUGE_HOME_INFO).
        Choose(world, "h", "whats_up_at_trade_world");
        Assert.True(Said(m.Talk, "GEN_INFO_AT_TRADE_WORLD_1"));
        Assert.False(Offered(m.Talk, "whats_up_at_trade_world"));
        Assert.Equal(1, story.Get("DRUUGE_HOME_INFO"));
        // Slave trading explained before the first purchase (KNOW_DRUUGE_SLAVERS).
        Choose(world, "h", "want_to_buy");
        Assert.True(Said(m.Talk, "WE_SELL_FOR_CREW"));
        Choose(world, "h", "isnt_this_slave_trading", "what_do_with_crew", "i_will_never_trade_crew");
        Assert.True(Said(m.Talk, "YOUR_LOSS"));
        Assert.Equal(2, story.Get("KNOW_DRUUGE_SLAVERS"));
        Choose(world, "h", "bye");
        Assert.True(Said(m.Talk, "BYE_FROM_TRADE_WORLD_1"));   // the first visit's
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        // Nothing was sold: no count, no price change.
        Assert.Equal((0, 3), (CrewSold(story), story.CrewCost));

        // The next visit, by the guest: the second greeting and the later goodbye.
        m = Orbit(world, "g", star, 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "SSQ_TRADE_WORLD_HELLO_1"));
        Assert.True(Offered(m.Talk!, "whats_up_at_trade_world"));
        Choose(world, "g", "whats_up_at_trade_world");
        Assert.True(Said(m.Talk!, "GEN_INFO_AT_TRADE_WORLD_2"));
        Choose(world, "g", "bye");
        Assert.True(Said(m.Talk!, "BYE_FROM_TRADE_WORLD_2"));
        MeetingTests.HearClosing(world, "g");
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
    }

    [ContentFact]
    public void HostileForGoodTheTradeWorldFightsWithItsEndlessFleet()
    {
        // DRUUGE_MANNER (set as the bomb is taken, genutw.c:260): init_druuge_comm leaves the talk
        // hostile at the trade world too (the encounter screen); Intro's first branch greets and fights.
        var world = NewGame();
        var story = world.Story!;
        story.Set("DRUUGE_MANNER", 1);
        int star = Star(world, "DRUUGE_DEFINED");
        var m = Orbit(world, "h", star, 0);
        Assert.Equal((MeetingStep.Encounter, true), (m.Step, m.Infinite));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "HSTL_TRADE_WORLD_HELLO_1"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.Empty(m.Talk.Replies);
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal("druuge", m.Battle.EnemyFleet.Ships[0].Ship);
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        // post_druuge_enc: hostile for good already, so not "attacked" (their space greetings as they were).
        Assert.Equal((0, 1), (story.Get("ATTACKED_DRUUGE"), story.Get("DRUUGE_HOME_VISITS")));
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "HSTL_TRADE_WORLD_HELLO_2"));
    }

    [ContentFact]
    public void AMaulerIsBoughtOnlyWhileTheHostsFleetHasRoomAndFuelFillsTheAnsweringShipsTanks()
    {
        // druugec.c's Buy: EscortFeasibilityStudy (DRUUGE_SHIP) == 0 says NOT_ENOUGH_ROOM and takes no
        // crew (the host's fleet, whoever answers: question 4); buy_fuel takes 10 crew and gives 10 units
        // (DeltaSISGauges: as much as the answering ship's own tanks hold, the crew taken all the same).
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        var guest = world.Find("g")!.Ship;
        Crew(guest, 130, pods: 3);
        world.AddFleetShips("SPATHI_SHIP", HostFleet.MaxShips);
        Assert.Equal(0, world.Fleet.Room);
        uint capacity = guest.Flight.FuelCapacity, hostFuel = world.Host.Ship.Flight.Fuel;
        guest.Flight.Fuel = capacity - 300;
        var m = Orbit(world, "g", Star(world, "DRUUGE_DEFINED"), 0);
        Choose(world, "g", "want_to_buy", "buy_druuge_ship");
        Assert.True(Said(m.Talk!, "NOT_ENOUGH_ROOM"));
        Assert.False(Said(m.Talk!, "BOUGHT_SHIP"));
        Assert.Equal((130, 0), (guest.Hold.Crew, world.Fleet.CountOf("DRUUGE_SHIP")));
        Assert.DoesNotContain(m.Talk!.Events, e => e is EscortsAdded);
        // Fuel: 10 crew for 10 units, the guest's tanks filled to their brim and no further.
        Choose(world, "g", "buy_fuel");
        Assert.True(Said(m.Talk, "BOUGHT_FUEL"));
        Assert.Equal((120, capacity, hostFuel), (guest.Hold.Crew, guest.Flight.Fuel, world.Host.Ship.Flight.Fuel));
        Choose(world, "g", "buy_fuel");   // full already: the crew go all the same, as UQM's
        Assert.Equal((110, capacity), (guest.Hold.Crew, guest.Flight.Fuel));
        // Room made: the Mauler joins the host's fleet.
        world.Fleet.RemoveSome("SPATHI_SHIP", 1);
        world.TakeNotices();
        Choose(world, "g", "buy_druuge_ship");
        Assert.True(Said(m.Talk, "BOUGHT_SHIP"));
        Assert.Equal((10, 1, 0), (guest.Hold.Crew, world.Fleet.CountOf("DRUUGE_SHIP"), world.Fleet.Room));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "1 Druuge ship joined the fleet (g's talk)."));
        Choose(world, "g", "buy_druuge_ship");
        Assert.Equal(1, Count(m.Talk, "NOT_ENOUGH_CREW"));   // the crew is asked first, then the room
        Choose(world, "g", "done_buying", "bye");
        // 120 sold in this talk, none before: past 100, the price up by 7 (druugec.c:189).
        Assert.Equal((120, 10), (CrewSold(story), story.CrewCost));
    }

    [ContentFact]
    public void TheArtifactsBoughtAreTheGroupsAndThreeHundredCrewSoldAtOnceRaiseThePriceByTwentyTwo()
    {
        // Buy: 100 crew each for the Rosy Sphere (ROSY_SPHERE_ON_SHIP and ROSY_SPHERE) and their two
        // fakes (ARTIFACT_2_ON_SHIP, ARTIFACT_3_ON_SHIP), story flags: each offered until the group has
        // it. ExitConversation: more than 250 sold with 100 or fewer before, the price up by 22.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        var guest = world.Find("g")!.Ship;
        Crew(guest, 310, pods: 6);
        var m = Orbit(world, "g", Star(world, "DRUUGE_DEFINED"), 0);
        Choose(world, "g", "want_to_buy");
        Assert.True(Said(m.Talk!, "HAVE_SPHERE") && Said(m.Talk!, "HAVE_ART_1") && Said(m.Talk!, "HAVE_ART_2"));
        Choose(world, "g", "buy_rosy_sphere");
        Assert.True(Said(m.Talk!, "BOUGHT_SPHERE"));
        Assert.Equal((1, 1, 210), (story.Get("ROSY_SPHERE"), story.Get("ROSY_SPHERE_ON_SHIP"), guest.Hold.Crew));
        Assert.False(Offered(m.Talk!, "buy_rosy_sphere"));
        Choose(world, "g", "buy_art_1");
        Assert.True(Said(m.Talk!, "BOUGHT_ART_1"));
        Assert.Equal((1, 0, 110), (story.Get("ARTIFACT_2_ON_SHIP"), story.Get("ARTIFACT_3_ON_SHIP"), guest.Hold.Crew));
        Choose(world, "g", "buy_art_2");
        Assert.True(Said(m.Talk!, "BOUGHT_ART_2"));
        Assert.Equal((1, 10), (story.Get("ARTIFACT_3_ON_SHIP"), guest.Hold.Crew));
        Assert.False(Offered(m.Talk!, "buy_art_1") || Offered(m.Talk!, "buy_art_2"));
        Assert.True(Offered(m.Talk!, "buy_druuge_ship") && Offered(m.Talk!, "buy_fuel"));
        world.TakeNotices();
        Choose(world, "g", "done_buying", "bye");
        Assert.Equal((300, 25), (CrewSold(story), story.CrewCost));
        Assert.Equal((44, 1), (story.Get("CREW_SOLD_TO_DRUUGE0"), story.Get("CREW_SOLD_TO_DRUUGE1")));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "g sold crew to the Druuge: crew cost 25 RU each at the starbase now."));
        MeetingTests.HearClosing(world, "g");

        // The group's: the host, who bought none of them, is offered none, nor told of them again.
        m = Orbit(world, "h", Star(world, "DRUUGE_DEFINED"), 0);
        Choose(world, "h", "want_to_buy");
        Assert.Equal(1, Count(m.Talk!, "READY_TO_SELL"));
        Assert.False(Said(m.Talk!, "HAVE_SPHERE") || Said(m.Talk!, "HAVE_ART_1") || Said(m.Talk!, "HAVE_ART_2"));
        Assert.False(Offered(m.Talk!, "buy_rosy_sphere") || Offered(m.Talk!, "buy_art_1") || Offered(m.Talk!, "buy_art_2"));
    }

    [ContentFact]
    public void TwoPlayersSalesAddUpAndTheStarbaseChargesTheNewPriceAndHasTheNews()
    {
        // ExitConversation (druugec.c:171-194): the crew sold in the talk is added to
        // CREW_SOLD_TO_DRUUGE0/1 as it says goodbye at the trade world; crossing 100 in all raises
        // GLOBAL (CrewCost) by 7. The count and the price are the group's: the host's 60 and the guest's
        // 50 cross it together. shipyard.c:739 charges CrewCost for a crew member; starbas.c:1540-1554
        // has the commander's bulletin 28 once more than 100 (and no more than 250) were sold.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        int star = Star(world, "DRUUGE_DEFINED");
        var host = world.Host.Ship;
        var guest = world.Find("g")!.Ship;
        Crew(host, 70, pods: 1);
        Crew(guest, 60, pods: 1);
        var m = Orbit(world, "h", star, 0);
        Choose(world, "h", "want_to_buy");
        for (int i = 0; i < 6; ++i)
            Choose(world, "h", "buy_fuel");
        Assert.Equal((10, 0), (host.Hold.Crew, CrewSold(story)));   // counted at the goodbye
        world.TakeNotices();
        Choose(world, "h", "done_buying", "bye");
        MeetingTests.HearClosing(world, "h");
        Assert.Equal((60, 3), (CrewSold(story), story.CrewCost));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.CrewPrice);

        m = Orbit(world, "g", star, 0);
        Choose(world, "g", "want_to_buy");
        for (int i = 0; i < 5; ++i)
            Choose(world, "g", "buy_fuel");
        Choose(world, "g", "done_buying", "bye");
        MeetingTests.HearClosing(world, "g");
        Assert.Equal((110, 10, 10), (CrewSold(story), story.CrewCost, guest.Hold.Crew));
        Assert.Contains(world.TakeNotices(), n => n is { PlayerId: "h", Event: NoticeEvent.CrewPrice, Count: 10, By: "g" });

        // At the starbase: the commander's news of it, and a crew member hired for 10 RU.
        foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", 3);
        story.Set("STARBASE_DAY", 3);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF & ~(1u << 27));   // only this news left to give
        world.CommanderMet = true;
        host.Ip!.EscapeCounter = 0;
        world.PutInOrbit("h", Sol, Earth, Luna);
        host.Ip!.ReturnFromOrbit();
        host.Hold.ResourceUnits = 100;
        world.VisitStarbase(world.Host);
        Assert.Equal(Activity.Starbase, host.Activity);
        world.StarbaseKey("h", MenuKey.Select, out _);   // Talk
        var talk = host.Visit!.Talk!;
        Assert.IsType<StarbaseScript>(talk.Script);
        Assert.True(Said(talk, "STARBASE_BULLETIN_28"));
        Assert.False(Said(talk, "STARBASE_BULLETIN_27") || Said(talk, "STARBASE_BULLETIN_30"));
        Assert.True(world.ChooseReply("h", Reply(talk, "goodbye_commander"), host.Visit.Round));
        // The shipyard's crew: one hired.
        foreach (var k in new[] { MenuKey.Down, MenuKey.Down, MenuKey.Select, MenuKey.Select, MenuKey.Select, MenuKey.Up })
            world.StarbaseKey("h", k, out _);
        Assert.Equal((11, 90u), (host.Hold.Crew, host.Hold.ResourceUnits));
    }

    [ContentFact]
    public void TheMaidensAreSoldForSixMaulersOrAsManyAsTheHostsFleetHasRoomFor()
    {
        // DoTransaction: MAIDENS_ON_SHIP cleared, AddEscortShips (DRUUGE_SHIP, 6), and their line by the
        // room there was (EscortFeasibilityStudy before): all 6, fewer, or none. The Maidens are the
        // group's (a story flag): a guest sells them, and the ships join the host's fleet.
        foreach (var (room, line) in new[] { (12, "DEAL_FOR_STATED_SHIPS"), (4, "DEAL_FOR_LESS_SHIPS"), (0, "DEAL_FOR_NO_SHIPS") })
        {
            var world = NewGame("g");
            var story = world.Story!;
            story.Set("MAIDENS_ON_SHIP", 1);
            story.Set("ROSY_SPHERE", 1);   // the regular deal
            if (room < HostFleet.MaxShips)
                world.AddFleetShips("SPATHI_SHIP", HostFleet.MaxShips - room);
            world.TakeNotices();
            var m = Orbit(world, "g", Star(world, "DRUUGE_DEFINED"), 0);
            Assert.True(Said(m.Talk!, "SCAN_MAIDENS"));
            Assert.Equal(1, story.Get("SCANNED_MAIDENS"));
            Choose(world, "g", "want_to_sell", "sell_maidens");
            Assert.True(Said(m.Talk!, "BOUGHT_MAIDENS") && Said(m.Talk!, "YOU_GET") && Said(m.Talk!, line));
            int joined = Math.Min(room, 6);
            Assert.Equal((0, joined), (story.Get("MAIDENS_ON_SHIP"), world.Fleet.CountOf("DRUUGE_SHIP")));
            Assert.Equal(new EscortsAdded(Race(world, "DRUUGE_SHIP"), 6, joined), m.Talk!.Events.OfType<EscortsAdded>().Single());
            Assert.False(Offered(m.Talk, "sell_maidens"));
            var told = world.TakeNotices().Where(n => n.Event == NoticeEvent.ShipsGiven).ToList();
            if (joined == 0)
                Assert.Empty(told);
            else
                Assert.Contains(told, n => n is { PlayerId: "h", Count: var c, Race: "Druuge" } && c == joined);
            // No crew was sold: the goodbye counts none.
            Choose(world, "g", "done_selling", "bye");
            Assert.Equal((0, 3), (CrewSold(story), story.CrewCost));
        }
    }

    [ContentFact]
    public void TheBroadcasterIsSoldForFuelToTheBrimOfTheSellersOwnTanks()
    {
        // DoTransaction's trade_gas (druugec.c:432-476): the capacity by the ship's own module slots,
        // less the fuel aboard, given through DeltaSISGauges, the amount said in units and the deal's
        // line by it; no ships for the broadcaster. The seller's tanks: the guest's, the host's untouched.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("BURV_BROADCASTERS_ON_SHIP", 1);
        story.Set("ROSY_SPHERE", 1);
        var guest = world.Find("g")!.Ship;
        uint capacity = guest.Flight.FuelCapacity, hostFuel = world.Host.Ship.Flight.Fuel;
        guest.Flight.Fuel = capacity - 1260;   // 13 units short (rounded to the nearest): a "good deal" (10 to 49)
        var m = Orbit(world, "g", Star(world, "DRUUGE_DEFINED"), 0);
        Assert.True(Said(m.Talk!, "SCAN_DRUUGE_CASTER"));
        Choose(world, "g", "want_to_sell", "sell_caster");
        Assert.True(Said(m.Talk!, "BOUGHT_CASTER") && Said(m.Talk!, "YOU_GET") && Said(m.Talk!, "FUEL0") && Said(m.Talk!, "FUEL1"));
        Assert.True(Said(m.Talk!, "GOOD_DEAL"));
        Assert.Contains(m.Talk!.Events, e => e is LineQueued { Phrase: 0, Text: "13" });
        Assert.Equal(new GaugesChanged(0, 1260, 0), m.Talk.Events.OfType<GaugesChanged>().Single());
        Assert.Equal((capacity, hostFuel, 0), (guest.Flight.Fuel, world.Host.Ship.Flight.Fuel, story.Get("BURV_BROADCASTERS_ON_SHIP")));
        Assert.DoesNotContain(m.Talk.Events, e => e is EscortsAdded);
        Assert.False(Said(m.Talk, "YOU_ALSO_GET"));
        Assert.Empty(world.Fleet.Ships);
    }

    [ContentFact]
    public void WithoutTheRosySphereTheGoodsAreOfferedForItFirst()
    {
        // Sell: while !ROSY_SPHERE each sale goes to Trade (TRADE_FOR_SPHERE; "what is it again?" once);
        // "way" gives the sphere (ROSY_SPHERE_ON_SHIP, then DoTransaction's ROSY_SPHERE) and nothing
        // else; "no way" is the regular deal, the sphere still theirs to offer for the next.
        var world = NewGame();
        var story = world.Story!;
        story.Set("MAIDENS_ON_SHIP", 1);
        story.Set("PORTAL_SPAWNER_ON_SHIP", 1);
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        var host = world.Host.Ship;
        host.Flight.Fuel = host.Flight.FuelCapacity;
        var m = Orbit(world, "h", Star(world, "DRUUGE_DEFINED"), 0);
        Choose(world, "h", "want_to_sell", "sell_spawner");
        Assert.True(Said(m.Talk!, "TRADE_FOR_SPHERE"));
        Assert.Equal(1, story.Get("PORTAL_SPAWNER_ON_SHIP"));   // nothing sold yet
        Choose(world, "h", "whats_the_sphere_again");
        Assert.True(Said(m.Talk!, "SPHERE_IS"));
        Assert.False(Offered(m.Talk!, "whats_the_sphere_again"));
        Choose(world, "h", "no_way");
        Assert.True(Said(m.Talk!, "OK_REGULAR_DEAL") && Said(m.Talk!, "BOUGHT_SPAWNER") && Said(m.Talk!, "DEAL_FOR_STATED_SHIPS"));
        Assert.True(Said(m.Talk!, "YOU_ALSO_GET") && Said(m.Talk!, "FINE_DEAL"));   // 3 ships, and fuel: none lacking
        Assert.Equal((0, 0, 3), (story.Get("PORTAL_SPAWNER_ON_SHIP"), story.Get("ROSY_SPHERE"), world.Fleet.CountOf("DRUUGE_SHIP")));
        // The Maidens for the sphere.
        Choose(world, "h", "sell_maidens");
        Assert.Equal(2, Count(m.Talk!, "TRADE_FOR_SPHERE"));
        Assert.False(Offered(m.Talk!, "whats_the_sphere_again"));   // asked once a talk
        Choose(world, "h", "way");
        Assert.True(Said(m.Talk!, "OK_HERES_SPHERE"));
        Assert.False(Said(m.Talk!, "BOUGHT_MAIDENS"));
        Assert.Equal((0, 1, 1, 3), (story.Get("MAIDENS_ON_SHIP"), story.Get("ROSY_SPHERE"), story.Get("ROSY_SPHERE_ON_SHIP"),
            world.Fleet.CountOf("DRUUGE_SHIP")));
        // Theirs no longer: not for sale.
        Choose(world, "h", "done_selling", "want_to_buy");
        Assert.False(Said(m.Talk!, "HAVE_SPHERE"));
        Assert.False(Offered(m.Talk!, "buy_rosy_sphere"));
    }

    [ContentFact]
    public void AShipWhoseEveryCrewMemberIsSoldFliesOnWithItsCaptainAlone()
    {
        // UQM: CrewEnlisted 0 is no game over (only (COUNT)~0 is: starcon.c:295); the flagship flies on,
        // can't land (planets/scan.c:1064), and fights with its captain alone (sis_ship.c:978,
        // crew_level = CrewEnlisted + 1). The same for the host's flagship and a guest's ship here.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        int star = Star(world, "DRUUGE_DEFINED");
        var host = world.Host.Ship;
        var guest = world.Find("g")!.Ship;
        Crew(guest, 100, pods: 2);
        Assert.Equal(50, host.Hold.Crew);
        var m = Orbit(world, "h", star, 0);
        Choose(world, "h", "want_to_buy");
        for (int i = 0; i < 5; ++i)
            Choose(world, "h", "buy_fuel");
        Assert.Equal(0, host.Hold.Crew);
        Choose(world, "h", "buy_fuel");
        Assert.True(Said(m.Talk!, "NOT_ENOUGH_CREW"));
        Choose(world, "h", "done_buying", "bye");
        MeetingTests.HearClosing(world, "h");
        m = Orbit(world, "g", star, 0);
        Choose(world, "g", "want_to_buy", "buy_druuge_ship");   // exactly its 100
        Assert.Equal((0, 1), (guest.Hold.Crew, world.Fleet.CountOf("DRUUGE_SHIP")));
        Choose(world, "g", "done_buying", "bye");
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(150, CrewSold(story));
        foreach (var ship in new[] { host, guest })
        {
            Assert.False(ship.Destroyed);
            Assert.Contains(ship.Activity, new[] { Activity.OuterSystem, Activity.InnerSystem });   // flying on
        }
        Assert.False(world.GameOver);
        // The next battle, each its own: the ship comes in with its captain alone (the Mauler left at
        // home: no ship to pick first), one hit from its end, as UQM's flagship would.
        world.Fleet.Remove("DRUUGE_SHIP");
        Away(world, "h");
        foreach (var id in new[] { "h", "g" })
        {
            var battle = world.StartBattle(id, "druuge");
            var fighter = battle.Fighters.Single(f => f.Player.Id == id);
            for (int i = 0; i < 300 && fighter.Starship.RaceDesc is null; ++i)
                Frames(world, 1);
            Assert.Equal((ushort)1, fighter.Starship.RaceDesc!.CrewLevel);
        }
    }

    [ContentFact]
    public void AGuestsShipAsItStartsCanOnlyBuyFuelOnce()
    {
        // A guest's ship as it joins holds 10 crew (the escort's kit): a Mauler and the artifacts are
        // out of its reach (NOT_ENOUGH_CREW), fuel takes all 10 (FUEL_CREW_COST) and leaves it with its
        // captain alone; the 10 are counted at the goodbye.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        var guest = world.Find("g")!.Ship;
        Assert.Equal((10, 10), (guest.Hold.Crew, guest.Hold.CrewCapacity));
        uint fuel = guest.Flight.Fuel, hostFuel = world.Host.Ship.Flight.Fuel;
        Assert.True(fuel + 1000 <= guest.Flight.FuelCapacity);
        var m = Orbit(world, "g", Star(world, "DRUUGE_DEFINED"), 0);
        Choose(world, "g", "want_to_buy", "buy_druuge_ship", "buy_rosy_sphere", "buy_art_1", "buy_art_2");
        Assert.Equal(4, Count(m.Talk!, "NOT_ENOUGH_CREW"));
        Assert.Equal((10, 0, 0), (guest.Hold.Crew, world.Fleet.Count, story.Get("ROSY_SPHERE")));
        Choose(world, "g", "buy_fuel");
        Assert.True(Said(m.Talk!, "BOUGHT_FUEL"));
        Assert.Equal((0, fuel + 1000, hostFuel), (guest.Hold.Crew, guest.Flight.Fuel, world.Host.Ship.Flight.Fuel));
        Choose(world, "g", "buy_fuel");
        Assert.Equal(5, Count(m.Talk!, "NOT_ENOUGH_CREW"));
        Assert.Equal(fuel + 1000, guest.Flight.Fuel);
        Choose(world, "g", "done_buying", "bye");
        MeetingTests.HearClosing(world, "g");
        Assert.Equal((10, 3), (CrewSold(story), story.CrewCost));
        Assert.False(guest.Destroyed);
        Assert.Contains(guest.Activity, new[] { Activity.OuterSystem, Activity.InnerSystem });
    }

    [ContentFact]
    public void CrewSoldInATalkLeftBeforeItsGoodbyeIsCountedAsItsPlayerLeaves()
    {
        // UQM's talk is left only by its goodbye, which counts the crew sold (druugec.c:171-194); here a
        // player can leave the game mid-talk, the Maulers bought already the host's: the count and the
        // price are settled then (CommScript.DroppedUnfinished; review round 1), the others told.
        var world = NewGame("g", "k");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        int star = Star(world, "DRUUGE_DEFINED");
        var guest = world.Find("g")!.Ship;
        Crew(guest, 300, pods: 6);
        var m = Orbit(world, "g", star, 0);
        Choose(world, "g", "want_to_buy", "buy_druuge_ship", "buy_druuge_ship", "buy_druuge_ship");
        Assert.Equal((0, 3, 0, 3), (guest.Hold.Crew, world.Fleet.CountOf("DRUUGE_SHIP"), CrewSold(story), story.CrewCost));
        world.TakeNotices();
        world.Leave("g");
        Assert.Equal((300, 25), (CrewSold(story), story.CrewCost));
        var told = world.TakeNotices().Where(n => n.Event == NoticeEvent.CrewPrice).ToList();
        Assert.Equal(new[] { ("h", "g", 25), ("k", "g", 25) }, told.Select(n => (n.PlayerId, n.By, n.Count)).OrderBy(n => n.PlayerId));
        Assert.Null(world.MeetingOf("g"));
        Assert.Null(world.DialogueLocks!.Holder("DRUUGE_CONVERSATION"));
        Assert.Equal(3, world.Fleet.CountOf("DRUUGE_SHIP"));

        // Nothing sold, nothing counted: "k" asks the price and leaves.
        m = Orbit(world, "k", star, 0);
        Choose(world, "k", "want_to_buy");
        world.Leave("k");
        Assert.Equal((300, 25), (CrewSold(story), story.CrewCost));
        Assert.DoesNotContain(world.TakeNotices(), n => n.Event == NoticeEvent.CrewPrice);

        // Left at the goodbye's closing lines: counted by the goodbye, once.
        var host = world.Host.Ship;
        m = Orbit(world, "h", star, 0);
        Choose(world, "h", "want_to_buy", "buy_fuel", "done_buying", "bye");
        Assert.Equal((MeetingStep.Closing, 310), (m.Step, CrewSold(story)));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal((310, 25, 40), (CrewSold(story), story.CrewCost, host.Hold.Crew));
    }

    [ContentFact]
    public void ASaveMadeMidTalkCarriesTheCrewSoldAndTheLiveTalkStillCountsItOnce()
    {
        // A meeting isn't saved: its ship is saved as it is (the crew gone, the fuel aboard), so the save
        // counts the crew sold so far as leaving would (Talk.SettleDroppedIn on the saved copy), while
        // the live talk goes on and counts it at its goodbye, once.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("KNOW_DRUUGE_SLAVERS", 3);
        story.Set("CREW_SOLD_TO_DRUUGE0", 95);
        int star = Star(world, "DRUUGE_DEFINED");
        var host = world.Host.Ship;
        var m = Orbit(world, "h", star, 0);
        string before = HostSave.ToJson(world);
        Choose(world, "h", "want_to_buy", "buy_fuel");
        Assert.Equal((40, 95, 3), (host.Hold.Crew, CrewSold(story), story.CrewCost));
        string json = HostSave.ToJson(world);
        // The live game: untouched by the save.
        Assert.Equal((95, 3, MeetingStep.Talk), (CrewSold(story), story.CrewCost, m.Step));
        Assert.DoesNotContain(m.Talk!.Events, e => e is CrewCostChanged);
        // The save: the 10 counted (105: past 100, the price up by 7), the crew as sold; before any sale, as it was.
        var loaded = HostSave.FromJson(json, world.Systems!, TestContent.Shapes);
        Assert.Equal((105, 10, 40), (CrewSold(loaded.Story!), loaded.Story!.CrewCost, loaded.Host.Ship.Hold.Crew));
        Assert.Null(loaded.MeetingOf("h"));
        var early = HostSave.FromJson(before, world.Systems!, TestContent.Shapes);
        Assert.Equal((95, 3, 50), (CrewSold(early.Story!), early.Story!.CrewCost, early.Host.Ship.Hold.Crew));
        // Saved again from the loaded game: counted once.
        Assert.Equal(105, CrewSold(HostSave.FromJson(HostSave.ToJson(loaded), world.Systems!, TestContent.Shapes).Story!));

        // The live talk goes on: more fuel, another save (20 by now), then its goodbye counts the 20 once.
        Choose(world, "h", "buy_fuel");
        Assert.Equal(115, CrewSold(HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes).Story!));
        Assert.Equal((95, 3), (CrewSold(story), story.CrewCost));
        Choose(world, "h", "done_buying", "bye");
        Assert.Equal((115, 10, 30), (CrewSold(story), story.CrewCost, host.Hold.Crew));
        Assert.Equal(new CrewCostChanged(10, 3), m.Talk.Events.OfType<CrewCostChanged>().Single());
        // At its closing lines and after: the saves have the goodbye's count, no more.
        Assert.Equal(115, CrewSold(HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes).Story!));
        MeetingTests.HearClosing(world, "h");
        var after = HostSave.FromJson(HostSave.ToJson(world), world.Systems!, TestContent.Shapes);
        Assert.Equal((115, 10), (CrewSold(after.Story!), after.Story!.CrewCost));
    }

    [ContentFact]
    public void OnePlayerTradesAtATimeAndTheOtherIsToldWhoIsTalking()
    {
        // The Druuge's lock (decision 5): while the guest trades, the host orbiting the trade world
        // starts no talk and no orbit (UQM never orbits it while its talk would start) and is told.
        var world = NewGame("g");
        int star = Star(world, "DRUUGE_DEFINED");
        var m = Orbit(world, "g", star, 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        world.TakeNotices();
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("h", "g is talking to the Druuge."));
        // Free again once the guest's talk and its closing lines are over.
        Choose(world, "g", "bye");
        MeetingTests.HearClosing(world, "g");
        var again = Orbit(world, "h", star, 0);
        Assert.Equal("h", world.DialogueLocks!.Holder("DRUUGE_CONVERSATION"));
        Assert.True(Said(again.Talk!, "SSQ_TRADE_WORLD_HELLO_1"));
    }

    // ------------------------------------------------------------ the Druuge in space

    [ContentFact]
    public void AnUnarmedShipWithNoEscortsIsTakenForSalvageWhichIsNoAttackOnThem()
    {
        // Intro's last branch (druugec.c:820-856): GetHeadLink (&built_ship_q) == 0 (the host's fleet,
        // whoever answers: question 4) and no GUN_WEAPON to CANNON_WEAPON in the module slots (the
        // answering ship's own): SALVAGE_YOUR_SHIP, a fight, AttemptedSalvage, so post_druuge_enc sets
        // no ATTACKED_DRUUGE.
        var world = NewGame("g");
        var story = world.Story!;
        var guest = world.Find("g")!.Ship;
        Disarm(guest);
        Assert.Contains(world.Host.Ship.Modules.ModuleSlots, ShipModules.IsWeapon);   // the host's armed: it's the answerer's that count
        var m = MeetInSystem(world, "g", "DRUUGE_SHIP");
        Assert.IsType<DruugeScript>(m.Talk!.Script);
        Assert.Equal(MeetingStep.Encounter, m.Step);   // in space: hostile until hailed
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "SALVAGE_YOUR_SHIP_1"));
        Assert.Empty(m.Talk.Replies);
        Assert.Equal((Segue.Hostile, 1), (m.Talk.Segue, story.Get("DRUUGE_SALVAGE")));
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "g");
        Assert.Equal((0, 0), (story.Get("ATTACKED_DRUUGE"), story.Get("DRUUGE_VISITS")));
        // Again: their second line.
        m = MeetInSystem(world, "g", "DRUUGE_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "SALVAGE_YOUR_SHIP_2"));
        MeetingTests.HearClosing(world, "g");
        Retreat(world, "g");
        Assert.Equal(0, story.Get("ATTACKED_DRUUGE"));

        // The armed host isn't salvaged: their news in space, and a peaceful goodbye.
        Away(world, "g");
        m = MeetInSystem(world, "h", "DRUUGE_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "INIT_SPACE_HELLO"));
        Choose(world, "h", "whats_up_in_space");
        Assert.True(Said(m.Talk!, "GENERAL_INFO_IN_SPACE_1"));
        Choose(world, "h", "bye");
        Assert.True(Said(m.Talk!, "GOODBYE_FROM_SPACE"));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal((1, 1), (story.Get("DRUUGE_VISITS"), story.Get("DRUUGE_SPACE_INFO")));

        // With an escort in the host's fleet the unarmed guest isn't salvaged either.
        world.AddFleetShips("SPATHI_SHIP", 1);
        Away(world, "h");
        m = MeetInSystem(world, "g", "DRUUGE_SHIP");
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "SUBSEQUENT_SPACE_HELLO"));
        Assert.False(Said(m.Talk!, "SALVAGE_YOUR_SHIP_2"));
        Assert.True(Offered(m.Talk!, "bye"));
    }

    [ContentFact]
    public void AttackingTheDruugeInSpaceMakesThemHostileThereButTheyStillTrade()
    {
        // comm.c's InitCommunication: Attack from the encounter screen runs post_encounter_func with no
        // talk: hostile, no salvage tried, not hostile for good: ATTACKED_DRUUGE, DRUUGE_VISITS 0. From
        // then on HOSTILE_SPACE_HELLO in space; their trade world still trades (the disclaimer noted).
        var world = NewGame();
        var story = world.Story!;
        story.Set("DRUUGE_VISITS", 1);
        var m = MeetInSystem(world, "h", "DRUUGE_SHIP");
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal((1, 0), (story.Get("ATTACKED_DRUUGE"), story.Get("DRUUGE_VISITS")));
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "h");
        m = MeetInSystem(world, "h", "DRUUGE_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "HOSTILE_SPACE_HELLO_1"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.Empty(m.Talk.Replies);
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "h");
        Assert.Equal(1, story.Get("DRUUGE_VISITS"));
        // The trade world: at peace all the same.
        m = Orbit(world, "h", Star(world, "DRUUGE_DEFINED"), 0);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(m.Talk!, "INITIAL_TRADE_WORLD_HELLO"));
        Assert.Equal(1, story.Get("DRUUGE_DISCLAIMER"));
        Assert.True(Offered(m.Talk!, "want_to_buy"));
    }

    // ------------------------------------------------------------ the bomb's moon

    [ContentFact]
    public void FiveDruugeGuardTheBombsMoonAndFightForIt()
    {
        // genutw.c:142-178: once the Utwig have their Ultron, the bomb's moon is the Druuge's: 5 ships
        // (not infinite), bit 6, their groups not stood down (no NotifyOthers); with any left after the
        // talk the ship stays out of orbit. druugec.c's BombAmbush: the warnings by BOMB_VISITS, then
        // "then we take it": FIGHT_FOR_BOMB, a fight that is an attack on them (post_druuge_enc).
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("UTWIG_HAVE_ULTRON", 1);
        int star = Star(world, "BOMB_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        var m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.IsType<DruugeScript>(m.Talk!.Script);
        Assert.Equal((1 << 6, false, 5), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Ships.Count));
        Assert.Equal(MeetingStep.Encounter, m.Step);   // init_druuge_comm: at peace only at the trade world
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "Host is meeting the Druuge."));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "INIT_BOMB_WORLD_HELLO"));
        Assert.Equal(1, story.Get("BOMB_VISITS"));
        // A peaceful goodbye: the 5 are there still, so no orbit and no flag.
        Choose(world, "h", "bye");
        Assert.True(Said(m.Talk, "GOODBYE_FROM_BOMB_PLANET"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 0), (story.Get("BOMB_UNPROTECTED"), story.Get("ATTACKED_DRUUGE")));

        // Again: the warnings, then the fight for it, against all 5 once more.
        story.Set("DRUUGE_VISITS", 1);
        m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "SUBSEQ_BOMB_WORLD_HELLO"));
        Choose(world, "h", "whats_up_at_bomb_planet");
        Assert.True(Said(m.Talk!, "GEN_INFO_AT_BOMB_PLANET"));
        Choose(world, "h", "we_get_bomb");
        Assert.True(Said(m.Talk!, "NOT_GET_BOMB"));
        Assert.Equal(3, story.Get("BOMB_VISITS"));
        Choose(world, "h", "then_we_take_bomb");
        Assert.True(Said(m.Talk!, "FIGHT_FOR_BOMB"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        Assert.Equal((1, 0), (story.Get("ATTACKED_DRUUGE"), story.Get("DRUUGE_VISITS")));
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.False(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal(5, m.Battle.EnemyFleet.Ships.Count);
        Assert.All(m.Battle.EnemyFleet.Ships, s => Assert.Equal("druuge", s.Ship));
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);   // survivors: out of orbit
        Assert.Equal(0, story.Get("BOMB_UNPROTECTED"));
    }

    [ContentFact]
    public void DestroyingAllFiveDruugeOpensTheBombsMoon()
    {
        // genutw.c:167-178: none left after the talk's battle: BOMB_UNPROTECTED and the orbit.
        var world = NewGame();
        var story = world.Story!;
        story.Set("UTWIG_HAVE_ULTRON", 1);
        int star = Star(world, "BOMB_DEFINED");
        var m = Orbit(world, "h", star, BombPlanet, BombMoon);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(1, story.Get("ATTACKED_DRUUGE"));
        Assert.Equal(5, m.Battle!.EnemyFleet.Ships.Count);
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 1);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Empty(m.Left!);
        Assert.Equal(1, story.Get("BOMB_UNPROTECTED"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(((int, int?)?)(BombPlanet, BombMoon), world.Host.Ship.Ip!.Orbit);
        // Orbited again: no guard, no talk.
        world.Host.Ship.Ip.ReturnFromOrbit();
        world.PutInOrbit("h", star, BombPlanet, BombMoon);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    // ------------------------------------------------------------ the Mycon

    [ContentFact]
    public void AMyconGroupsRepliesFightOrGoOnAsTheTalksOwnDrawSays()
    {
        // myconc.c:377: each time NormalMycon offers its replies, (BYTE) TFB_Random () < 256 * 30 / 100
        // makes them CombatIsInevitable's (the ramble, BYE_AND_DIE, a fight), else NormalMycon's (the
        // ramble, RAMBLE_TAIL, the reply blanked, a new draw). The talk's own generator: a talk's seed
        // is by its frame, so the meetings here differ; each is followed as its draws say, until both
        // ways have been seen.
        var world = NewGame();
        var story = world.Story!;
        bool fought = false, talked = false;
        for (int i = 0; i < 30 && !(fought && talked); ++i)
        {
            Frames(world, 7);
            var m = MeetInSystem(world, "h", "MYCON_SHIP");
            Assert.IsType<MyconScript>(m.Talk!.Script);
            Assert.Equal(MeetingStep.Encounter, m.Step);   // init_mycon_comm: hostile from the start
            int visits = story.Get("MYCON_VISITS");
            Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
            Assert.True(Said(m.Talk, $"HELLO_SPACE_{visits + 1}"));
            foreach (var reply in new[] { "come_in_peace", "gonna_die" })
            {
                var draws = m.Talk.Events.OfType<RandomDrawn>().ToList();
                bool fight = (byte)draws[^1].Value < 256 * 30 / 100;
                string function = fight ? "CombatIsInevitable" : "NormalMycon";
                Assert.Equal(function, m.Talk.Replies[Reply(m.Talk, reply)].Function);
                Assert.Equal(function, m.Talk.Replies[Reply(m.Talk, $"insult_{story.Get("MYCON_INSULTS") + 1}")].Function);
                Assert.Equal("CombatIsInevitable", m.Talk.Replies[Reply(m.Talk, "bye_space")].Function);
                int ramble = story.Get("MYCON_RAMBLE"), tails = Count(m.Talk, "RAMBLE_TAIL");
                Choose(world, "h", reply);
                Assert.True(Said(m.Talk, $"RAMBLE_{ramble + 1}"));
                Assert.Equal((ramble + 1) % 32, story.Get("MYCON_RAMBLE"));
                if (fight)
                {
                    fought = true;
                    Assert.True(Said(m.Talk, "BYE_AND_DIE_SPACE"));
                    Assert.Equal(tails, Count(m.Talk, "RAMBLE_TAIL"));
                    Assert.True(m.Talk.Ended);
                    break;
                }
                talked = true;
                Assert.Equal(tails + 1, Count(m.Talk, "RAMBLE_TAIL"));
                Assert.False(m.Talk.Ended);
                Assert.False(Offered(m.Talk, reply));
                Assert.Equal(draws.Count + 1, m.Talk.Events.OfType<RandomDrawn>().Count());
            }
            if (!m.Talk.Ended)
                Choose(world, "h", "bye_space");
            Assert.Equal(Segue.Hostile, m.Talk.Segue);
            MeetingTests.HearClosing(world, "h");
            Assert.Equal(MeetingStep.Battle, m.Step);
            Retreat(world, "h");
        }
        Assert.True(fought && talked, $"fought {fought}, talked {talked}");
    }

    [ContentFact]
    public void TheMyconHomeworldTalksAndFightsWithItsEndlessFleet()
    {
        // genmyc.c:90-144 at MYCON_DEFINED: an infinite fleet whatever the ambush, bit 7, their groups
        // stood down; always survivors, so never an orbit and never SUN_DEVICE_UNGUARDED.
        var world = NewGame("g");
        var story = world.Story!;
        story.Set("MYCON_FELL_FOR_AMBUSH", 1);
        int star = Star(world, "MYCON_DEFINED");
        world.PutInSystem("g", star);
        world.TakeNotices();
        var m = Orbit(world, "h", star, 0);
        Assert.IsType<MyconScript>(m.Talk!.Script);
        Assert.Equal((1 << 7, true, MeetingStep.Encounter), (m.Talk.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.Contains(world.TakeNotices(), n => (n.PlayerId, n.Text) == ("g", "The Mycon hold off: Host is meeting them."));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "HELLO_HOMEWORLD_1"));
        Assert.Equal(1, story.Get("MYCON_HOME_VISITS"));
        // The homeworld's replies: a question (MYCON_INFO) and the three others; no insult, no "we come in peace".
        Assert.True(Offered(m.Talk, "question_1") && Offered(m.Talk, "lets_be_friends") && Offered(m.Talk, "came_to_homeworld")
                    && Offered(m.Talk, "submit_to_us"));
        Assert.False(Offered(m.Talk, "come_in_peace") || Offered(m.Talk, "insult_1") || Offered(m.Talk, "bye_space"));
        Choose(world, "h", "bye_homeworld");
        Assert.True(Said(m.Talk, "BYE_AND_DIE_HOMEWORLD"));
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal("mycon", m.Battle.EnemyFleet.Ships[0].Ship);
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(0, story.Get("SUN_DEVICE_UNGUARDED"));
    }

    [ContentFact]
    public void TheSunDevicesWorldIsLeftInPeaceWithItsGuardStillThere()
    {
        // genmyc.c at SUN_DEVICE_DEFINED before the ambush: an infinite fleet, bit 6. myconc.c's
        // SunDevice: their news, the trick refused there (NO_TRICK_AT_SUN), and the one farewell that
        // isn't a fight (bye_sun_device). MyconSurvivors: no orbit, no SUN_DEVICE_UNGUARDED.
        var world = NewGame();
        var story = world.Story!;
        story.Set("MYCON_AMBUSH", 1);
        int star = Star(world, "SUN_DEVICE_DEFINED");
        var m = Orbit(world, "h", star, 0);
        Assert.Equal((1 << 6, true, MeetingStep.Encounter), (m.Talk!.Get("GLOBAL_FLAGS_AND_DATA"), m.Infinite, m.Step));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk, "HELLO_SUN_DEVICE_WORLD_1"));
        Assert.Empty(m.Talk.Events.OfType<RandomDrawn>());   // no draw here
        Assert.False(Offered(m.Talk, "like_to_land") || Offered(m.Talk, "how_goes_implanting"));
        Choose(world, "h", "whats_up_sun_device");
        Assert.True(Said(m.Talk, "GENERAL_INFO_SUN_DEVICE"));
        Assert.False(Offered(m.Talk, "whats_up_sun_device"));
        Choose(world, "h", "i_have_a_cunning_plan");
        Assert.True(Said(m.Talk, "WONT_FALL_FOR_TRICK"));
        Assert.Equal((1, 0), (story.Get("NO_TRICK_AT_SUN"), story.Get("MYCON_FELL_FOR_AMBUSH")));
        Assert.False(Offered(m.Talk, "i_have_a_cunning_plan"));
        Choose(world, "h", "bye_sun_device");
        Assert.True(Said(m.Talk, "GOODBYE_SUN_DEVICE"));
        Assert.Equal(Segue.Peace, m.Talk.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal((0, 1), (story.Get("SUN_DEVICE_UNGUARDED"), story.Get("MYCON_SUN_VISITS")));
        // The next visit: their second greeting; the trick isn't offered here again.
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "HELLO_SUN_DEVICE_WORLD_2"));
        Assert.False(Offered(m.Talk!, "i_have_a_cunning_plan"));
    }

    [ContentFact]
    public void OnceTheMyconFellForTheAmbushFiveGuardTheSunDeviceUntilNoneIsLeft()
    {
        // genmyc.c:102-113: 5 ships once MYCON_FELL_FOR_AMBUSH; :134-144 with any left the ship stays
        // out of orbit, else SUN_DEVICE_UNGUARDED and the orbit. SunDevice: the implanting asked about,
        // and "we'd like to land" a fight.
        var world = NewGame();
        var story = world.Story!;
        story.Set("MYCON_FELL_FOR_AMBUSH", 1);
        int star = Star(world, "SUN_DEVICE_DEFINED");
        var m = Orbit(world, "h", star, 0);
        Assert.Equal((false, 5), (m.Infinite, m.Ships.Count));
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Choose(world, "h", "how_goes_implanting");
        Assert.True(Said(m.Talk!, "UNFORSEEN_DELAYS"));
        Assert.False(Offered(m.Talk!, "how_goes_implanting"));
        Choose(world, "h", "like_to_land");
        Assert.True(Said(m.Talk!, "NEVER_LET_LAND"));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.False(m.Battle!.EnemyFleet.Infinite);
        Assert.Equal(5, m.Battle.EnemyFleet.Ships.Count);
        Assert.All(m.Battle.EnemyFleet.Ships, s => Assert.Equal("mycon", s.Ship));
        Retreat(world, "h");
        Assert.NotEqual(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(0, story.Get("SUN_DEVICE_UNGUARDED"));

        // All 5 again, and destroyed: the world is open.
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(5, m.Battle!.EnemyFleet.Ships.Count);
        WorldTalkTests.WinTheBattle(world, m);
        Frames(world, 1);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(1, story.Get("SUN_DEVICE_UNGUARDED"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        Assert.Equal(((int, int?)?)(0, null), world.Host.Ship.Ip!.Orbit);
        world.Host.Ship.Ip.ReturnFromOrbit();
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void TheMyconFallForTheTrickAtTheirHomeworldAndItsEventIsSetAside()
    {
        // NormalMycon offers the trick at home once MYCON_AMBUSH is set (the Syreen's, 3c), whatever
        // the draw; TrickMycon: the three clues in any order, then WE_GO_TO_IMPLANT, peace,
        // MYCON_FELL_FOR_AMBUSH and AddEvent (RELATIVE_EVENT, 0, 0, 0, ADVANCE_MYCON_MISSION), due at
        // once: set aside until stage 3c runs the calendar (the plan's question 3). (The talk is at peace
        // from the hail already, comm.c's InitCommunication clearing BATTLE_SEGUE for it, and nothing on
        // the trick's way sets it hostile: TrickMycon's own setSegue is pinned by TalkTests' recordings.)
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "MYCON_DEFINED");
        var m = Orbit(world, "g", star, 0);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.False(Offered(m.Talk!, "i_have_a_cunning_plan"));   // no ambush set: no trick
        Choose(world, "g", "bye_homeworld");
        MeetingTests.HearClosing(world, "g");
        Retreat(world, "g");

        story.Set("MYCON_AMBUSH", 1);
        m = Orbit(world, "g", star, 0);
        Assert.True(world.EncounterChoice("g", EncounterAction.Hail));
        Assert.Equal("TrickMycon", m.Talk!.Replies[Reply(m.Talk, "i_have_a_cunning_plan")].Function);
        Choose(world, "g", "i_have_a_cunning_plan");
        Assert.True(Said(m.Talk, "TELL_US_ABOUT_WORLD") && Said(m.Talk, "AMBUSH_TAIL"));
        Assert.Equal(new[] { "clue_1", "clue_2", "clue_3" }, m.Talk.Replies.Select(r => Scripts["mycon"].Phrases[r.Phrase - 1]));
        Choose(world, "g", "clue_3");
        Assert.True(Said(m.Talk, "RESPONSE_3"));
        Choose(world, "g", "clue_1");
        Assert.True(Said(m.Talk, "RESPONSE_1"));
        Assert.Equal(0, story.Get("MYCON_FELL_FOR_AMBUSH"));
        Assert.Equal(3, Count(m.Talk, "AMBUSH_TAIL"));
        Choose(world, "g", "clue_2");
        Assert.True(Said(m.Talk, "RESPONSE_2") && Said(m.Talk, "WE_GO_TO_IMPLANT"));
        Assert.Equal(3, Count(m.Talk, "AMBUSH_TAIL"));
        Assert.True(m.Talk.Ended);
        Assert.Equal((Segue.Peace, 1), (m.Talk.Segue, story.Get("MYCON_FELL_FOR_AMBUSH")));
        Assert.Contains(m.Talk.Events, e => e is EventAdded { Relative: true, Months: 0, Days: 0, Years: 0, Event: "ADVANCE_MYCON_MISSION" });
        Assert.Contains(story.Events.SetAside, e => e.Event == "ADVANCE_MYCON_MISSION");
        Assert.DoesNotContain(story.Events, e => e.Event == "ADVANCE_MYCON_MISSION");
        MeetingTests.HearClosing(world, "g");
        Assert.Null(m.Battle);
        Assert.NotEqual(Activity.Orbit, world.Find("g")!.Ship.Activity);
        // Fallen for it: no trick again at home, and the Sun Device's world is down to 5.
        Away(world, "g");
        m = Orbit(world, "h", star, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.False(Offered(m.Talk!, "i_have_a_cunning_plan"));
        Choose(world, "h", "bye_homeworld");
        MeetingTests.HearClosing(world, "h");
        Retreat(world, "h");
        m = Orbit(world, "h", Star(world, "SUN_DEVICE_DEFINED"), 0);
        Assert.Equal((false, 5), (m.Infinite, m.Ships.Count));
    }

    [ContentFact]
    public void OrbitingAShatteredWorldLetsThePlayersAskTheMyconAboutThem()
    {
        // genmyc.c:164-168: orbiting the first planet of an egg case's system (a shattered world) sets
        // KNOW_ABOUT_SHATTERED 1 while it's 0; myconc.c's NormalMycon offers "what about the shattered
        // worlds?" at their homeworld only at exactly 1, and its answer makes it 2. The group's: a
        // guest sees the world, the host asks.
        var world = NewGame("g");
        var story = world.Story!;
        foreach (var special in new[] { "EGG_CASE0_DEFINED", "EGG_CASE1_DEFINED", "EGG_CASE2_DEFINED" })
        {
            int egg = Star(world, special);
            story.Set("KNOW_ABOUT_SHATTERED", 0);
            if (world.Systems!.HasWorld(egg, 1, null))
            {
                world.PutInOrbit("g", egg, 1);   // another planet of the system: nothing
                Assert.Equal(0, story.Get("KNOW_ABOUT_SHATTERED"));
                world.Find("g")!.Ship.Ip!.ReturnFromOrbit();
            }
            world.PutInOrbit("g", egg, 0);
            Assert.Equal(Activity.Orbit, world.Find("g")!.Ship.Activity);
            Assert.Equal(1, story.Get("KNOW_ABOUT_SHATTERED"));
            world.Find("g")!.Ship.Ip!.ReturnFromOrbit();
            // Knowing more already (2 once told, by the Mycon, the Druuge's news or a ramble): left as it is.
            foreach (int known in new[] { 2, 3 })
            {
                story.Set("KNOW_ABOUT_SHATTERED", known);
                world.PutInOrbit("g", egg, 0);
                Assert.Equal(known, story.Get("KNOW_ABOUT_SHATTERED"));
                world.Find("g")!.Ship.Ip!.ReturnFromOrbit();
            }
        }
        // Not at the Mycon's own worlds (their talks start there), nor any other star's first planet.
        story.Set("KNOW_ABOUT_SHATTERED", 0);
        world.PutInOrbit("g", Sol, 0);
        Assert.Equal(0, story.Get("KNOW_ABOUT_SHATTERED"));
        Away(world, "g");

        // Not seen: not offered. Seen: offered, whatever the draw, answered, and not offered again.
        int home = Star(world, "MYCON_DEFINED");
        var m = Orbit(world, "h", home, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.False(Offered(m.Talk!, "what_about_shattered"));
        Choose(world, "h", "bye_homeworld");
        MeetingTests.HearClosing(world, "h");
        Retreat(world, "h");
        world.PutInOrbit("g", Star(world, "EGG_CASE1_DEFINED"), 0);
        Assert.Equal(1, story.Get("KNOW_ABOUT_SHATTERED"));
        Away(world, "g");
        m = Orbit(world, "h", home, 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.Equal("NormalMycon", m.Talk!.Replies[Reply(m.Talk, "what_about_shattered")].Function);
        int ramble = story.Get("MYCON_RAMBLE");
        Choose(world, "h", "what_about_shattered");
        Assert.True(Said(m.Talk, "ABOUT_SHATTERED"));
        Assert.Equal((2, ramble), (story.Get("KNOW_ABOUT_SHATTERED"), story.Get("MYCON_RAMBLE")));   // no ramble for it
        Assert.False(Offered(m.Talk, "what_about_shattered"));
    }

    [ContentFact]
    public void WithTheSunDeviceTakenTheMyconOnlyThreaten()
    {
        // Intro's first branch: SUN_DEVICE (3c's pickup, which also resets MYCON_VISITS, genmyc.c:240):
        // DIE_THIEF, then DIE_THIEF_AGAIN from then on, hostile with no replies, wherever they're met.
        var world = NewGame();
        var story = world.Story!;
        story.Set("SUN_DEVICE", 1);
        var m = MeetInSystem(world, "h", "MYCON_SHIP");
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "DIE_THIEF"));
        Assert.Empty(m.Talk!.Replies);
        Assert.Equal(Segue.Hostile, m.Talk.Segue);
        Assert.Empty(m.Talk.Events.OfType<RandomDrawn>());
        MeetingTests.HearClosing(world, "h");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Retreat(world, "h");
        m = Orbit(world, "h", Star(world, "MYCON_DEFINED"), 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(m.Talk!, "DIE_THIEF_AGAIN"));
        Assert.False(Said(m.Talk!, "HELLO_HOMEWORLD_1"));
        Assert.Equal((1, 0), (story.Get("MYCON_VISITS"), story.Get("MYCON_HOME_VISITS")));
    }
}
