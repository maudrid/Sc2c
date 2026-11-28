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
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.StoryPickupsTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Using the devices in the game (the stage 3c plan's item 8; its questions
/// 5 and 6): only the holder uses a device, the bomb only aboard the
/// host's ship (the game over for everyone); the Ultron mended aboard the
/// ship that holds it and the piece; the broadcaster's call (the user's own
/// USED_BROADCASTER) and its talks with the Ilwrath (a meeting with no group
/// and no ships, under their lock); the Sun Device's flash; the Pet's, the
/// spawner's and the Chmmr's halves left for later items, writing nothing.
/// What each use does by where the ship is is UQM's own
/// (DeviceUseTests, tests/golden/device-uses.json).
/// </summary>
[Trait("Topic", "story")]
public class DeviceUsesInGameTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    private static GameWorld NewGame(int guests = 1)
    {
        var world = StoryPickupsTests.NewGame(guests);
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.BattleKit = WorldBattleTests.Kit();
        return world;
    }

    private static int Id(string device) => Rules.Devices.All.Single(d => d.Name == device).Id;

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    // ------------------------------------------------------------ who may use one

    [Fact]
    public void OnlyTheHolderUsesADeviceAndOnlyWhileFlyingOrInOrbit()
    {
        var world = NewGame();
        world.SetDevice("guest", "SUN_DEVICE_ON_SHIP");
        int sun = Id("SUN_EFFICIENCY_DEVICE");
        // The host doesn't hold it: nothing happens; nor a device nobody has, nor one that isn't one.
        Assert.Equal(DeviceOutcome.NotHeld, world.UseDevice("host", sun));
        Assert.Equal(DeviceOutcome.NotHeld, world.UseDevice("guest", Id("ROSY_SPHERE_DEVICE")));
        Assert.Equal(DeviceOutcome.NotHeld, world.UseDevice("guest", 99));
        Assert.Equal(DeviceOutcome.NotHeld, world.UseDevice("guest", -1));
        // The holder in hyperspace: UQM's failure (the Sun Device works in orbit only).
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("guest", sun));
        // Not while the game is paused, nor for a player away.
        world.SetHostPause(true);
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("guest", sun));
        world.SetHostPause(false);
        world.Leave("guest");
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("guest", sun));
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("nobody", sun));
    }

    [ContentFact]
    public void NoDeviceIsUsedWithTheLanderDown()
    {
        var world = NewGame();
        int sol = Star(world, "SOL_DEFINED");
        world.SetDevice("host", "SUN_DEVICE_ON_SHIP");
        world.PutInOrbit("host", sol, 3);   // Mars
        Land(world, "host", 100, 100);
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("host", Id("SUN_EFFICIENCY_DEVICE")));
    }

    // ------------------------------------------------------------ the Ultron

    [Fact]
    public void ThePiecesMendTheUltronAboardTheShipThatHoldsBoth()
    {
        var world = NewGame();
        var story = world.Story!;
        world.SetDevice("guest", "ULTRON_CONDITION", 1);
        world.SetDevice("guest", "ROSY_SPHERE_ON_SHIP");
        world.SetDevice("guest", "AQUA_HELIX_ON_SHIP");
        world.SetDevice("host", "CLEAR_SPINDLE_ON_SHIP");
        story.Set("DISCUSSED_ULTRON", 1);
        story.Set("SUPOX_ULTRON_HELP", 1);
        // The Spindle is aboard the host's ship, the Ultron the guest's: it mends nothing (UQM's failure).
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("CLEAR_SPINDLE_DEVICE")));
        Assert.Equal(1, world.Device("host", "CLEAR_SPINDLE_ON_SHIP"));
        // The Sphere aboard the guest's: one piece more mended, the Sphere gone, the talks' "discussed" put back.
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("guest", Id("ROSY_SPHERE_DEVICE")));
        Assert.Equal((2, 0), (world.Device("guest", "ULTRON_CONDITION"), world.Device("guest", "ROSY_SPHERE_ON_SHIP")));
        Assert.Equal(2, story.Get("ULTRON_CONDITION"));   // the story's record follows
        Assert.Equal((0, 0), (story.Get("DISCUSSED_ULTRON"), story.Get("SUPOX_ULTRON_HELP")));
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("guest", Id("AQUA_HELIX_DEVICE")));
        Assert.Equal(3, world.Device("guest", "ULTRON_CONDITION"));
        Assert.Equal(Id("ULTRON_2_DEVICE"), world.Find("guest")!.Ship.Hold.Devices.Single());
        // Handed the Ultron, the host mends it with the Spindle: whole.
        world.SetDevice("guest", "ULTRON_CONDITION", 0);
        world.SetDevice("host", "ULTRON_CONDITION", 3);
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("host", Id("CLEAR_SPINDLE_DEVICE")));
        Assert.Equal(new[] { Id("ULTRON_3_DEVICE") }, world.Find("host")!.Ship.Hold.Devices);
        Assert.Equal(4, story.Get("ULTRON_CONDITION"));
    }

    [Fact]
    public void APieceWithTheUltronGivenBackMendsNothing()
    {
        // Given back to the Utwig (5), the Ultron is no ship's: UQM's would count on to 6; here it isn't aboard.
        var world = NewGame();
        world.Story!.Set("ULTRON_CONDITION", 5);
        world.SetDevice("host", "ROSY_SPHERE_ON_SHIP");
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("ROSY_SPHERE_DEVICE")));
        Assert.Equal(5, world.Story.Get("ULTRON_CONDITION"));
        Assert.Equal(1, world.Device("host", "ROSY_SPHERE_ON_SHIP"));
    }

    // ------------------------------------------------------------ the broadcasters

    [Fact]
    public void ABroadcasterInHyperspaceCallsTheMelnormeForItsUserAlone()
    {
        var world = NewGame();
        world.SetDevice("guest", "UMGAH_BROADCASTERS_ON_SHIP");
        world.SetDevice("host", "BURV_BROADCASTERS_ON_SHIP");
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("guest", Id("UMGAH_HYPERWAVE_DEVICE")));
        Assert.True(world.Find("guest")!.Ship.UsedBroadcaster);
        Assert.False(world.Host.Ship.UsedBroadcaster);
        Assert.Equal(1, world.Device("guest", "UMGAH_BROADCASTERS_ON_SHIP"));   // never used up
        // In QuasiSpace (the Arilou's side), nothing.
        world.Host.Ship.ArilouSpaceSide = 2;
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("BURVIX_HYPERWAVE_DEVICE")));
        Assert.False(world.Host.Ship.UsedBroadcaster);
    }

    [ContentFact]
    public void ABroadcasterElsewhereInASystemDoesNothing()
    {
        var world = NewGame();
        world.SetDevice("host", "UMGAH_BROADCASTERS_ON_SHIP");
        world.PutInSystem("host", Star(world, "SOL_DEFINED"));
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        Assert.False(world.Host.Ship.UsedBroadcaster);
        Assert.Null(world.MeetingOf("host"));
    }

    [ContentFact]
    public void ABroadcasterInTheIlwrathsHomeSystemSpeaksAsTheirGodsAndNoBattleFollows()
    {
        var world = NewGame();
        int star = Star(world, "ILWRATH_DEFINED");
        world.SetDevice("guest", "BURV_BROADCASTERS_ON_SHIP");
        world.PutInSystem("guest", star);
        world.PutInSystem("host", star);
        world.TakeNotices();
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("guest", Id("BURVIX_HYPERWAVE_DEVICE")));
        var m = world.MeetingOf("guest")!;
        Assert.Equal(16, m.CasterFlags);
        Assert.Empty(m.Ships);
        Assert.Null(m.Group);
        Assert.Null(m.World);
        Assert.Equal((int?)star, m.Star);
        Assert.Equal("guest", world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        Assert.Equal(1, world.Story!.Fleet("ILWRATH_SHIP").KnownStrength);   // StartSphereTracking
        // The others in the system are told of the meeting, not that the Ilwrath hold off (no group stands down).
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "host" && n.Event == NoticeEvent.Meeting && n.By == "guest");
        // At peace from the start: the gods speak at once (GodsSpeak).
        var talk = m.Talk!;
        Assert.IsType<IlwrathScript>(talk.Script);
        Assert.Equal(16, talk.OwnFields["GLOBAL_FLAGS_AND_DATA"]);
        Assert.Equal(MeetingStep.Talk, m.Step);
        Assert.True(Said(talk, "ILWRATH_BELIEVE"));
        Assert.Equal(1, world.Story.Get("ILWRATH_GODS_SPOKEN"));
        // Sent against the Thraddash: their mission starts at once (stage 3c item 3).
        Assert.True(world.ChooseReply("guest", Reply(talk, "go_kill_thraddash"), m.Round));
        Assert.Equal(1, world.Story.Get("ILWRATH_DECEIVED"));
        Assert.True(world.ChooseReply("guest", Reply(talk, "bye_gods"), m.Round));
        // "Goodbye, gods" ends it at peace; nor would a hostile end fight: there are no ships to (UQM's empty
        // npc_built_ship_q).
        MeetingTests.HearClosing(world, "guest");
        Assert.Null(world.MeetingOf("guest"));
        Assert.Null(m.Battle);
        Assert.Null(world.DialogueLocks.Holder("ILWRATH_CONVERSATION"));
        Assert.Equal(Activity.OuterSystem, world.Find("guest")!.Ship.Activity);
    }

    [ContentFact]
    public void ABroadcasterInTheIlwrathsHomeSystemOnceTheyreDeadDoesNothing()
    {
        var world = NewGame();
        int star = Star(world, "ILWRATH_DEFINED");
        world.Story!.SetFleet("ILWRATH_SHIP", world.Story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        world.Story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        world.SetDevice("host", "BURV_BROADCASTERS_ON_SHIP");
        world.PutInSystem("host", star);
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("BURVIX_HYPERWAVE_DEVICE")));
        Assert.Null(world.MeetingOf("host"));
    }

    [ContentFact]
    public void ABroadcasterInOrbitInTheIlwrathsSystemKeepsTheShipInOrbitThroughTheirTalk()
    {
        var world = NewGame();
        int star = Star(world, "ILWRATH_DEFINED");
        world.SetDevice("host", "UMGAH_BROADCASTERS_ON_SHIP");
        int planet = Enumerable.Range(1, world.Systems![star].Planets.Count - 1).First();
        world.PutInOrbit("host", star, planet);
        Assert.Null(world.MeetingOf("host"));
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        var m = world.MeetingOf("host")!;
        Assert.Equal(((int, int?)?)(planet, null), m.World);
        Assert.Equal(Activity.Talk, world.Host.Ship.Activity);
        Assert.NotNull(world.Host.Ship.Orbit);
        var talk = m.Talk!;
        Assert.True(world.ChooseReply("host", Reply(talk, "bye_gods"), m.Round));
        MeetingTests.HearClosing(world, "host");
        Assert.Null(world.MeetingOf("host"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void ABroadcasterReachesAnIlwrathShipFirstAmongASystemsGroups()
    {
        var world = NewGame();
        int sol = Star(world, "SOL_DEFINED");
        world.SetDevice("host", "UMGAH_BROADCASTERS_ON_SHIP");
        world.PutInSystem("host", sol);
        Frames(world, 1);   // in the system: its draw made (Arrive)
        var d = world.SystemGroups!.Systems[sol];
        int ilwrath = Rules.RaceIndex("ILWRATH_SHIP");
        // Another race's group first: nothing.
        var spathi = new IpGroup { GroupId = 90, Race = Rules.RaceIndex("SPATHI_SHIP") };
        d.Groups.Insert(0, spathi);
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        d.Groups.Remove(spathi);
        // The Ilwrath's first (the head of ip_group_q): that ship's talk (bit 5), at peace, grunting.
        d.Groups.Insert(0, new IpGroup { GroupId = 91, Race = ilwrath });
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        var m = world.MeetingOf("host")!;
        Assert.Equal(32, m.CasterFlags);
        Assert.Null(m.Group);
        Assert.True(Said(m.Talk!, "JUST_GRUNTS"));
        Assert.Equal(Segue.Peace, m.Talk!.Segue);
    }

    [ContentFact]
    public void ABroadcastersIlwrathTalkWaitsForTheirLock()
    {
        var world = NewGame();
        int star = Star(world, "ILWRATH_DEFINED");
        world.SetDevice("host", "UMGAH_BROADCASTERS_ON_SHIP");
        world.PutInSystem("host", star);
        Assert.True(world.DialogueLocks!.TryTake("ILWRATH_CONVERSATION", "guest", out _));
        world.TakeNotices();
        Assert.Equal(DeviceOutcome.Talking, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        Assert.Null(world.MeetingOf("host"));
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "host" && n.Event == NoticeEvent.Talking && n.By == "guest");
        world.DialogueLocks.Release("ILWRATH_CONVERSATION", "guest");
        Assert.Equal(DeviceOutcome.Used, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        // Not while it's in that talk.
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
    }

    [ContentFact]
    public void ABroadcasterInOrbitOfTheChmmrsWorldIsItem11s()
    {
        var world = NewGame();
        world.SetDevice("host", "UMGAH_BROADCASTERS_ON_SHIP");
        world.PutInOrbit("host", Star(world, "CHMMR_DEFINED"), 1);
        Assert.Null(world.MeetingOf("host"));
        Assert.Equal(DeviceOutcome.Later, world.UseDevice("host", Id("UMGAH_HYPERWAVE_DEVICE")));
        Assert.Null(world.MeetingOf("host"));
    }

    // ------------------------------------------------------------ the Sun Device

    [ContentFact]
    public void TheSunDeviceFlashesInOrbitAndAtTheChmmrsWorldLeavesTheRestToItem11()
    {
        var world = NewGame();
        world.SetDevice("host", "SUN_DEVICE_ON_SHIP");
        int sol = Star(world, "SOL_DEFINED");
        world.PutInSystem("host", sol);
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", Id("SUN_EFFICIENCY_DEVICE")));
        world.PutInOrbit("host", sol, 3);
        Assert.Equal(DeviceOutcome.Flash, world.UseDevice("host", Id("SUN_EFFICIENCY_DEVICE")));
        Assert.Equal(1, world.Device("host", "SUN_DEVICE_ON_SHIP"));   // never used up
        // Procyon's Ilwrath guard meet a ship with the Sun Device aboard while they live (genchmmr.c): gone.
        world.Story!.SetFleet("ILWRATH_SHIP", world.Story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        world.Story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        world.PutInOrbit("host", Star(world, "CHMMR_DEFINED"), 1);
        Assert.Null(world.MeetingOf("host"));
        Assert.Equal(DeviceOutcome.FlashChmmrLater, world.UseDevice("host", Id("SUN_EFFICIENCY_DEVICE")));
        Assert.Equal(0, world.Story.Get("CHMMR_EMERGING"));
        Assert.Null(world.MeetingOf("host"));
    }

    // ------------------------------------------------------------ the bomb

    [Fact]
    public void TheBombAboardAGuestsShipCantBeSetOff()
    {
        var world = NewGame();
        world.SetDevice("guest", "UTWIG_BOMB_ON_SHIP");
        world.Story!.Set("UTWIG_BOMB", 1);
        Assert.Equal(DeviceOutcome.HostShipOnly, world.UseDevice("guest", Id("UTWIG_BOMB_DEVICE")));
        Assert.False(world.GameOver);
        Assert.Equal(1, world.Device("guest", "UTWIG_BOMB_ON_SHIP"));
        Assert.Equal(1, world.Story.Get("UTWIG_BOMB"));
    }

    [Fact]
    public void TheBombSetOffAboardTheHostsShipIsTheGameOverForEveryone()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, SystemGroupTests.Systems.Value.Map), "host", "Host", SystemGroupTests.Systems.Value,
            TestContent.Shapes);
        var world = host.World;
        world.SetDevice("host", "UTWIG_BOMB_ON_SHIP");
        world.Story!.Set("UTWIG_BOMB", 1);
        Assert.Equal(DeviceOutcome.Used, host.UseDevice("host", Id("UTWIG_BOMB_DEVICE")));
        Assert.True(world.GameOver);
        Assert.True(world.BombSetOff);
        Assert.Equal(GameLoss.UtwigBomb, world.Loss);
        Assert.True(world.Host.Ship.Destroyed);
        Assert.Equal(0, world.Host.Ship.Hold.Crew);
        Assert.Equal(0, world.Story.Get("UTWIG_BOMB"));
        Assert.Equal(1, world.Device("host", "UTWIG_BOMB_ON_SHIP"));   // RestartMenu reads it for the flash
        // Everyone is told the game is over, and how; nothing more is used.
        host.Step(ShipInput.None);
        var (own, guests) = host.TakeBattleNotices();
        Assert.Contains(own, n => n.Kind == BattleNoticeKind.GameOver && BattleNotice.LossOf(n.Text) == GameLoss.UtwigBomb);
        Assert.Equal("Game over: the flagship set off the Utwig bomb.",
            own.First(n => n.Kind == BattleNoticeKind.GameOver).Describe());
        Assert.Equal(DeviceOutcome.NotNow, world.UseDevice("host", Id("UTWIG_BOMB_DEVICE")));
    }

    // ------------------------------------------------------------ later items'

    [ContentFact]
    public void ThePetsAndTheSpawnersUsesAreLaterItemsAndWriteNothing()
    {
        var world = NewGame();
        world.SetDevice("host", "TALKING_PET_ON_SHIP");
        world.Story!.Set("PORTAL_SPAWNER_ON_SHIP", 1);   // everyone's
        uint fuel = world.Host.Ship.Flight.Fuel;
        Assert.Equal(DeviceOutcome.Later, world.UseDevice("host", Id("TALKING_PET_DEVICE")));
        Assert.Equal(DeviceOutcome.Later, world.UseDevice("guest", Id("PORTAL_SPAWNER_DEVICE")));
        Assert.Equal(0, world.Story.Get("PORTAL_COUNTER"));
        Assert.Equal(fuel, world.Host.Ship.Flight.Fuel);
        Assert.Null(world.MeetingOf("host"));
        // Without the fuel, UQM's spawner fails.
        world.Find("guest")!.Ship.Flight.Fuel = 999;
        Assert.Equal(DeviceOutcome.Failed, world.UseDevice("guest", Id("PORTAL_SPAWNER_DEVICE")));
        // In a system the Pet's talk is item 10's too.
        world.PutInSystem("host", Star(world, "SOL_DEFINED"));
        Assert.Equal(DeviceOutcome.Later, world.UseDevice("host", Id("TALKING_PET_DEVICE")));
        Assert.Equal(0, world.Story.Get("SHIP_TO_COMPEL"));
    }

    [Fact]
    public void TheDevicesWithNoUseDoNothing()
    {
        var world = NewGame();
        foreach (var d in Rules.Devices.All.Where(d => d.Listed && d.Field is { } f && DeviceTable.HeldByShips(f)
                     && d.Name is not ("TALKING_PET_DEVICE" or "UTWIG_BOMB_DEVICE" or "SUN_EFFICIENCY_DEVICE"
                         or "ROSY_SPHERE_DEVICE" or "AQUA_HELIX_DEVICE" or "CLEAR_SPINDLE_DEVICE"
                         or "UMGAH_HYPERWAVE_DEVICE" or "BURVIX_HYPERWAVE_DEVICE")))
        {
            var held = world.Host.Ship.Hold.Devices.ToList();
            world.Host.Ship.Hold.SetDevices(Rules.Devices.WithValue(Array.Empty<int>(), d.Field!, d.Value ?? 1));
            Assert.Equal(DeviceOutcome.Failed, world.UseDevice("host", d.Id));
            Assert.Equal(new[] { d.Id }, world.Host.Ship.Hold.Devices);
        }
    }
}
