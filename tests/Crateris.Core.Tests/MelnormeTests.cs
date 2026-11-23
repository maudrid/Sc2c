// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The Melnorme in the world (the stage 3b-2 plan's item 4): their groups
/// flying in hyperspace and at the super giants; the talk trading with the
/// answerer's own credits (live: a gift mid-talk counts), tanks and slots;
/// a module bought on sale for everyone, a lander upgrade the answerer's;
/// the anger told to everyone; the bio data sold and counted down on the
/// seller's screen; the rescue's strip of the answering ship's own slots
/// applied as a change; the strip offer's minerals refusal; the rescue-only
/// talk beside another player's (question 7); the rescue fields and the
/// broadcaster's use each player's own, and saved. The script's every
/// path is TalkTests' recording.
/// </summary>
[Trait("Topic", "world")]
public class MelnormeTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    // The system no race's sphere holds (SlylandroMeetingTests'): no groups of its own with the probes off.
    private const int NoSphereStar = 14;

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

    // Met before and down to business, neither angered nor curious about the purple.
    private static void Business(GameWorld world)
    {
        var story = world.Story!;
        story.Set("MET_MELNORME", 1);
        story.Set("MELNORME_YACK_STACK2", 6);
        story.Set("WHY_MELNORME_PURPLE", 1);
        story.Set("TRADED_WITH_MELNORME", 1);
        story.Set("MELNORME_FUEL_PROCEDURE", 1);
        story.Set("MELNORME_TECH_PROCEDURE", 1);
        story.Set("MELNORME_INFO_PROCEDURE", 1);
    }

    private static void Frames(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    private static int Melnorme(GameWorld world) => world.Story!.Rules.RaceIndex("MELNORME_SHIP");

    // A Melnorme ship in hyperspace right by a player there, with a rolled group's flags.
    private static HyperEncounter MelnormeBy(GameWorld world, string id, short x, short y)
    {
        world.PutInHyperspace(id);
        var ship = world.Find(id)!.Ship;
        var f = ship.Flight;
        f.PlaceAtUniverse(x, y);
        f.Stop();
        f.StarJustLeft = null;
        var e = new HyperEncounter
        {
            Race = Melnorme(world), Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
            Flags = world.Encounters!.RolledFlags(world.Story!, Melnorme(world), f.Fuel, ship.UsedBroadcaster),
        };
        e.Ships.Add(world.Story!.Rules.RaceShips[e.Race].Crew);
        world.Encounters!.List.Add(e);
        return e;
    }

    private static Meeting Meet(GameWorld world, string id)
    {
        for (int i = 0; i < 600 && world.MeetingOf(id) is not { Step: not MeetingStep.Spin }; ++i)
            Frames(world, 1);
        return world.MeetingOf(id) ?? throw new Xunit.Sdk.XunitException($"{id} met nothing");
    }

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts["melnorm"].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static void Choose(GameWorld world, string id, string phrase) =>
        Assert.True(world.ChooseReply(id, Reply(world.MeetingOf(id)!.Talk!, phrase)));

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts["melnorm"].PhraseNumber(phrase));

    private static Gift Credits(string from, string to, uint credits) => new(from, to, 0, new int[8], 0, Credits: credits);

    [ContentFact]
    public void TradingUsesTheAnswerersOwnCreditsLiveAndAModuleBoughtGoesOnSaleForEveryone()
    {
        var world = NewGame("g");
        Business(world);
        var guest = world.Find("g")!;
        guest.Ship.Hold.Credits = 200;
        world.Host.Ship.Hold.Credits = 100;
        MelnormeBy(world, "g", 3000, 8000);
        var m = Meet(world, "g");
        // At peace (init_melnorme_comm): no encounter screen, under the Melnorme's lock.
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.IsType<MelnormeScript>(m.Talk!.Script);
        Assert.Equal("g", world.DialogueLocks!.Holder("MELNORME_CONVERSATION"));
        Assert.Equal(200u, TalkView.Of(m)!.Credits);   // the status area's credits (SMM_CREDITS)
        world.TakeNotices();
        Choose(world, "g", "buy");
        Choose(world, "g", "buy_technology");
        Assert.True(Said(m.Talk, "NEW_TECH_1"));
        Choose(world, "g", "buy_new_tech");
        // The blaster (question 5): its shared price set, on sale for everyone, the others told.
        Assert.Equal(50u, guest.Ship.Hold.Credits);
        Assert.Equal(4000 / 50, world.Story!.ModuleCost("BLASTER_WEAPON"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.ModuleForSale && n.Race == "blaster");
        Assert.True(Said(m.Talk, "NEW_TECH_2"));
        // 50 credits can't buy the lander's speed; the host tops the trader up mid-talk, and it can.
        Assert.Equal(TradeRefusal.None, world.Give(Credits("h", "g", 100)));
        Assert.Equal(150u, TalkView.Of(m)!.Credits);
        Choose(world, "g", "buy_new_tech");
        Assert.False(Said(m.Talk, "NEED_MORE_CREDIT0"));
        // A lander upgrade is the answering ship's own.
        Assert.Equal(0u, guest.Ship.Hold.Credits);
        Assert.Equal(LanderUpgrades.Speed, guest.Ship.Hold.Upgrades);
        Assert.Equal(LanderUpgrades.None, world.Host.Ship.Hold.Upgrades);
        Assert.Equal(0u, world.Host.Ship.Hold.Credits);
        Assert.Equal(1, m.Talk.OwnFields["IMPROVED_LANDER_SPEED"]);
        // The catalog for the host now skips the blaster (anyone's) but not the speed (the guest's).
        Choose(world, "g", "no_buy_new_tech");   // out of credits now: how commerce works (NEED_CREDIT)
        Assert.True(Said(m.Talk, "NEED_CREDIT"));
        Choose(world, "g", "goodbye");
        MeetingTests.HearClosing(world, "g");
        Assert.Null(world.DialogueLocks.Holder("MELNORME_CONVERSATION"));
        world.Host.Ship.Hold.Credits = 150;
        MelnormeBy(world, "h", 9000, 300);
        var hm = Meet(world, "h");
        Choose(world, "h", "buy");
        Choose(world, "h", "buy_technology");
        Assert.True(Said(hm.Talk!, "NEW_TECH_2"));
        Assert.False(Said(hm.Talk!, "NEW_TECH_1"));
    }

    [ContentFact]
    public void FuelIsBoughtUpToTheAnsweringShipsOwnTanks()
    {
        var world = NewGame("g");
        Business(world);
        var guest = world.Find("g")!.Ship;
        guest.Hold.Credits = 1000;
        MelnormeBy(world, "g", 3000, 8000);
        guest.Flight.Fuel = 0;
        var m = Meet(world, "g");
        Choose(world, "g", "buy");
        Choose(world, "g", "buy_fuel");
        Choose(world, "g", "fill_me_up");
        // The escort's own tanks, not the flagship's: filled to its capacity, a credit a unit.
        uint capacity = guest.Modules.FuelCapacity;
        Assert.Equal(capacity, guest.Flight.Fuel);
        Assert.Equal(1000u - capacity / 100, guest.Hold.Credits);
        Assert.Throws<Xunit.Sdk.XunitException>(() => Reply(m.Talk!, "buy_fuel"));
    }

    [ContentFact]
    public void SellingBioDataPaysTheSellerAndIsCountedDownOnTheirScreen()
    {
        var world = NewGame("g");
        Business(world);
        var guest = world.Find("g")!.Ship;
        guest.Hold.SetBioData(7);
        guest.Hold.Credits = 3;
        MelnormeBy(world, "g", 3000, 8000);
        var m = Meet(world, "g");
        Choose(world, "g", "sell");
        Assert.Null(TalkView.Of(m)!.BioSale);
        Choose(world, "g", "sell_life_data");
        Assert.Equal((0, 17u), (guest.Hold.BioData, guest.Hold.Credits));
        var view = TalkView.Of(m)!;
        // The bio data as it was, and the credits before, for the seller's cargo display to count down.
        Assert.Equal((7, 3u), (view.BioSale!.BioData, view.BioSale.Credits));
        Assert.Equal(17u, view.Credits);
        // The view goes over the wire whole.
        Assert.True(view.Same(TalkView.Decode(view.Encode())));
    }

    [ContentFact]
    public void AngeringTheMelnormeTellsEveryoneAndEndsInABattle()
    {
        var world = NewGame("g");
        world.Story!.Set("MET_MELNORME", 1);
        world.Story.Set("MELNORME_YACK_STACK2", 3);   // the bluster next
        MelnormeBy(world, "g", 3000, 8000);
        var m = Meet(world, "g");
        world.TakeNotices();
        Choose(world, "g", "trade_is_for_the_weak");
        Choose(world, "g", "youre_on");
        Assert.Equal(1, world.Story.Get("MELNORME_ANGER"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.Angered && n.Count == 1);
        MeetingTests.HearClosing(world, "g");
        Assert.Equal(MeetingStep.Battle, m.Step);
        Assert.Equal("melnorme", m.Battle!.EnemyShip.Id);
    }

    // The guest stranded (no fuel, bio data, credits or rainbow worlds) by a Melnorme ship, down to business.
    private static (GameWorld World, Meeting Meeting) Stranded(bool inSystem = false)
    {
        var world = NewGame("g");
        Business(world);
        var guest = world.Find("g")!.Ship;
        if (inSystem)
        {
            world.PutInSystem("g", NoSphereStar);
            Frames(world, 1);
            world.AddEncounterNear("g", Melnorme(world), 1);
            guest.Flight.Fuel = 0;
        }
        else
        {
            MelnormeBy(world, "g", 3000, 8000);
            guest.Flight.Fuel = 0;
        }
        var m = Meet(world, "g");
        Assert.True(Said(m.Talk!, "RESCUE_EXPLANATION"));
        return (world, m);
    }

    [ContentFact]
    public void TheRescueStripsTheAnsweringShipsOwnSlotsAsAChangeAndAGiftOfFuelMeanwhileStays()
    {
        var (world, m) = Stranded(inSystem: true);
        var guest = world.Find("g")!.Ship;
        var host = world.Host.Ship;
        var hostSlots = host.Modules.Save();
        var before = guest.Modules.Save();
        int landers = guest.Hold.Landers;
        Assert.Equal(1, guest.MelnormeRescueCount);   // the guest's own count
        Assert.Equal(0, host.MelnormeRescueCount);
        Choose(world, "g", "yes_help");
        Assert.True(Said(m.Talk!, "RESCUE_OFFER"));
        var melnorme = (MelnormeScript)m.Talk!.Script;
        Assert.True(melnorme.StripOffered);
        // Minerals can't go to them while the strip is offered (it may take their bays); fuel can.
        world.PutInSystem("h", NoSphereStar);
        Frames(world, 1);
        host.Hold.MoveCargo(new[] { 10, 0, 0, 0, 0, 0, 0, 0 }, 0, +1);
        Assert.Equal(TradeRefusal.StripOffered,
            world.Give(new Gift("h", "g", 0, new[] { 5, 0, 0, 0, 0, 0, 0, 0 }, 0)));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("h", "g", 0, new int[8], 0, Fuel: 300)));
        Assert.Equal(300u, guest.Flight.Fuel);
        Choose(world, "g", "take_it");
        Assert.True(Said(m.Talk, "HAPPY_TO_HAVE_RESCUED"));
        // The parts the offer named are gone from the escort's own slots; the flagship untouched.
        var after = guest.Modules.Save();
        Assert.NotEqual(before, after);
        Assert.Equal(hostSlots, host.Modules.Save());
        int taken = landers - guest.Hold.Landers
                    + before.Drives!.Zip(after.Drives!).Count(d => d.First != d.Second)
                    + before.Jets!.Zip(after.Jets!).Count(j => j.First != j.Second)
                    + before.Modules!.Zip(after.Modules!).Count(x => x.First != x.Second);
        Assert.True(taken > 0);
        // The fuel given up to the tanks' room, the friend's gift kept.
        uint rescue = m.Talk.Events.OfType<GaugesChanged>().Last().Fuel is var f and > 0 ? (uint)f : 0;
        Assert.Equal(Math.Min(300u + rescue, guest.Flight.FuelCapacity), guest.Flight.Fuel);
        // The offer taken, minerals may go to them again (as far as the bays the strip left hold them).
        Assert.False(melnorme.StripOffered);
        Assert.Equal(guest.Hold.StorageCapacity >= 5 ? TradeRefusal.None : TradeRefusal.NoRoom,
            world.Give(new Gift("h", "g", 0, new[] { 5, 0, 0, 0, 0, 0, 0, 0 }, 0)));
    }

    [ContentFact]
    public void FuelAFriendGivesWhileTheStripIsOfferedStaysWithTheRescuesFuel()
    {
        var world = NewGame("g");
        Business(world);
        var guest = world.Find("g")!.Ship;
        // Near Sol: the rescue's fuel is the way home, short of the tanks.
        MelnormeBy(world, "g", 1800, 1500);
        guest.Flight.Fuel = 0;
        world.PutInHyperspace("h");
        world.Host.Ship.Flight.PlaceAtUniverse(1760, 1500);
        world.Host.Ship.Flight.Stop();
        var m = Meet(world, "g");
        Choose(world, "g", "yes_help");
        Assert.True(Said(m.Talk!, "RESCUE_HOME"));
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("h", "g", 0, new int[8], 0, Fuel: 300)));
        Choose(world, "g", "take_it");
        uint rescue = (uint)m.Talk.Events.OfType<GaugesChanged>().Last().Fuel;
        Assert.True(rescue < guest.Flight.FuelCapacity - 300);
        // Applied as a change (not UQM's restore of the ship as offered): the gift and the rescue's fuel.
        Assert.Equal(300u + rescue, guest.Flight.Fuel);
    }

    [ContentFact]
    public void ARefusedRescueIsThePlayersOwnAndSaved()
    {
        var (world, _) = Stranded();
        Choose(world, "g", "no_help");
        var guest = world.Find("g")!.Ship;
        Assert.Equal((1, 1), (guest.MelnormeRescueRefused, guest.MelnormeRescueCount));
        Assert.Equal((0, 0), (world.Host.Ship.MelnormeRescueRefused, world.Host.Ship.MelnormeRescueCount));
        world.Host.Ship.UsedBroadcaster = true;
        MeetingTests.HearClosing(world, "g");
        // Saved as version 14's optional part, each player's own.
        var json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, world.Systems!, world.Shapes!);
        Assert.Equal((1, 1, false), (loaded.Find("g")!.Ship.MelnormeRescueRefused, loaded.Find("g")!.Ship.MelnormeRescueCount,
            loaded.Find("g")!.Ship.UsedBroadcaster));
        Assert.Equal((0, 0, true), (loaded.Host.Ship.MelnormeRescueRefused, loaded.Host.Ship.MelnormeRescueCount,
            loaded.Host.Ship.UsedBroadcaster));
        // A version 14 save without them has none; an older one with them is refused.
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var p in node["players"]!.AsArray())
            p!.AsObject().Remove("melnorme");
        var none = HostSave.FromJson(node.ToJsonString(), world.Systems!, world.Shapes!);
        Assert.Equal(0, none.Find("g")!.Ship.MelnormeRescueRefused);
        node = JsonNode.Parse(json)!.AsObject();
        node["version"] = 13;
        foreach (var p in node["players"]!.AsArray())
            p!["hold"]!["credits"] = 0;
        var e = Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), world.Systems!, world.Shapes!));
        Assert.Contains("rescue", e.Message);
        // Asked again: "changed your mind?", the guest's own refusal.
        var (again, _) = (world, 0);
        MelnormeBy(again, "g", 3000, 8000);
        var m2 = Meet(again, "g");
        Assert.True(Said(m2.Talk!, "CHANGED_MIND"));
    }

    [ContentFact]
    public void AStrandedPlayerIsRescuedWhileAnotherTradesWithoutTheLockOrTheGreeting()
    {
        var world = NewGame("g");
        Business(world);
        world.Host.Ship.Hold.Credits = 100;
        MelnormeBy(world, "h", 9000, 300);
        var hm = Meet(world, "h");
        Assert.Equal("h", world.DialogueLocks!.Holder("MELNORME_CONVERSATION"));
        var guest = world.Find("g")!.Ship;
        int stack = world.Story!.Get("MELNORME_YACK_STACK2");   // the host's greeting has moved it on
        // Not stranded (fuel aboard): the guest's Melnorme pass through them.
        var passing = MelnormeBy(world, "g", 3000, 8000);
        Frames(world, 60);
        Assert.Null(world.MeetingOf("g"));
        Assert.Contains(passing, world.Encounters!.List);
        // Stranded: the rescue-only talk, beside the host's, without the lock.
        guest.Flight.Fuel = 0;
        var m = Meet(world, "g");
        Assert.True(m.RescueOnly);
        Assert.Equal("h", world.DialogueLocks.Holder("MELNORME_CONVERSATION"));
        var talk = m.Talk!;
        Assert.False(Said(talk, "HELLO_AND_DOWN_TO_BUSINESS_1"));
        Assert.True(Said(talk, "RESCUE_EXPLANATION"));
        Assert.Equal(stack, world.Story.Get("MELNORME_YACK_STACK2"));   // no greeting's write to the shared stack
        // The same as UQM's rescue branch from that state, without the greeting (the recording's first rescue).
        var recorded = Golden.Load("talks-melnorm").GetProperty("starts").EnumerateArray()
            .First(s => s.GetProperty("name").GetString() == "rescue-count-0").GetProperty("steps")[0].GetProperty("events")
            .EnumerateArray().Select(e => JsonNode.Parse(e.GetRawText())!.AsObject()).ToList();
        // Without the greeting: NatureOfConversation's hello line and its write of MELNORME_YACK_STACK2 after it.
        int yack = recorded.FindIndex(e => e["set"] is { } f && (string)f! == "MELNORME_YACK_STACK2");
        Assert.NotNull(recorded[yack - 1]["track"]);
        var want = recorded.Where((_, i) => i != yack && i != yack - 1)
            .Select(e => { e.Remove("key"); return e.ToJsonString(); }).ToList();
        var got = talk.Events.Select(e => TalkTests.Json(e).ToJsonString()).ToList();
        Assert.Equal(want, got);
        // The host angers them meanwhile: the rescue still ends in peace.
        Choose(world, "h", "goodbye");
        world.Story.Set("MELNORME_ANGER", 3);
        Choose(world, "g", "yes_help");
        Choose(world, "g", "take_it");
        Assert.Equal(Segue.Peace, talk.Segue);
        MeetingTests.HearClosing(world, "g");
        Assert.Null(world.MeetingOf("g"));
        Assert.True(guest.Flight.Fuel > 0);
        // Its lock never taken, the host's let go as their talk ended.
        Assert.Equal(MeetingStep.Closing, hm.Step);
    }

    [ContentFact]
    public void AStrandedPlayerWithSomethingToTradeWaitsForTheLock()
    {
        var world = NewGame("g");
        Business(world);
        MelnormeBy(world, "h", 9000, 300);
        Meet(world, "h");
        var guest = world.Find("g")!.Ship;
        guest.Hold.SetBioData(1);   // something to trade: the full talk, which takes the lock
        MelnormeBy(world, "g", 3000, 8000);
        guest.Flight.Fuel = 0;
        Frames(world, 60);
        Assert.Null(world.MeetingOf("g"));
        // Nor with them angered.
        guest.Hold.SetBioData(0);
        world.Story!.Set("MELNORME_ANGER", 1);
        Frames(world, 30);
        Assert.Null(world.MeetingOf("g"));
        world.Story.Set("MELNORME_ANGER", 0);
        // Nor before the players are down to business with them.
        world.Story.Set("MELNORME_YACK_STACK2", 5);
        Frames(world, 30);
        Assert.Null(world.MeetingOf("g"));
        world.Story.Set("MELNORME_YACK_STACK2", 6);
        Assert.True(Meet(world, "g").RescueOnly);
    }

    [ContentFact]
    public void TheBroadcastersUseIsTheUsersOwn()
    {
        var world = NewGame("g");
        world.PutInHyperspace("h");
        world.PutInHyperspace("g");
        world.Find("g")!.Ship.UsedBroadcaster = true;
        // A day's roll: the guest's Melnorme at 100%, the host's not (fuel aboard); each use spent.
        world.Clock.FramesIntoDay = 0;
        int before = world.Encounters!.List.Count(e => e.Race == Melnorme(world) && e.Roller == "g");
        for (int i = 0; i < 400 && world.HyperspaceEncounterDays == 0; ++i)
            Frames(world, 1);
        Assert.True(world.HyperspaceEncounterDays > 0);
        Assert.Equal(before + 1, world.Encounters.List.Count(e => e.Race == Melnorme(world) && e.Roller == "g"));
        Assert.DoesNotContain(world.Encounters.List, e => e.Race == Melnorme(world) && e.Roller == "h");
        Assert.False(world.Find("g")!.Ship.UsedBroadcaster);
        // Entering a system spends it too (InterplanetaryTransition).
        world.Host.Ship.UsedBroadcaster = true;
        world.PutInSystem("h", NoSphereStar);
        Frames(world, 1);
        Assert.False(world.Host.Ship.UsedBroadcaster);
    }

    [ContentFact]
    public void EachSuperGiantHasAMelnormeShipThatFlies()
    {
        var world = NewGame();
        var stars = world.Systems!.Map.Stars.Where(s => s.Special is { } x && x.StartsWith("MELNORME", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, stars.Count);
        foreach (var star in stars.Take(2))
        {
            world.PutInSystem("h", star.Number);
            Frames(world, 1);
            var d = world.SystemGroups!.Systems[star.Number];
            Assert.Contains(d.Groups, g => g.Race == Melnorme(world));
            world.PutInHyperspace("h");
            Frames(world, 1);
        }
    }

    [ContentFact]
    public void OkStripMeStripsAGuestsEscortButTheCrewPodsItsCrewNeeds()
    {
        var world = NewGame("g");
        world.Story!.Set("MET_MELNORME", 1);
        world.Story.Set("MELNORME_ANGER", 2);
        world.Story.Set("MELNORME_YACK_STACK4", 2);
        var guest = world.Find("g")!.Ship;
        // Worth stripping, with two crew pods its crew (beyond the kit's) needs one of.
        guest.Hold.SetLanders(2);
        int[] free = guest.Modules.ModuleSlots.Select((m, i) => (m, i)).Where(x => x.m == ShipModules.EmptyModule).Select(x => x.i).ToArray();
        guest.Modules.SetModule(free[0], ShipModules.CrewPod);
        guest.Modules.SetModule(free[1], ShipModules.CrewPod);
        guest.Refit();
        guest.Hold.SetCrew(guest.Modules.KitCrew + 45);   // one pod beyond the kit (two counting the kit's crew as UQM's would)
        MelnormeBy(world, "g", 3000, 8000);
        var m = Meet(world, "g");
        world.TakeNotices();
        Choose(world, "g", "ok_strip_me");
        Assert.True(Said(m.Talk!, "FAIR_JUSTICE"));
        Assert.Equal(0, world.Story.Get("MELNORME_ANGER"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "h" && n.Event == NoticeEvent.Calmed);
        Assert.Equal(0, guest.Hold.Landers);
        Assert.Equal(0, guest.Modules.Thrusters);
        // The kit's crew is its own: the crew pods kept are those the rest of the crew needs.
        Assert.Equal(1, guest.Modules.Count(ShipModules.CrewPod));
        Assert.Equal(guest.Modules.KitCrew + 45, guest.Hold.Crew);
        Assert.True(guest.Flight.Fuel <= Fuel.Reserve);
    }
}
