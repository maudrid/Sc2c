// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Planetside;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using static Crateris.Core.Tests.StoryPickupsTests;

namespace Crateris.Core.Tests;

/// <summary>
/// The ruins, the Ur-Quan wreck, the Syreen's vault and Procyon's starbase
/// in the game (the stage 3c plan's item 6; its question 10): nodes that
/// stay, their reports with a take-off or the finder's alone, the text
/// read from the shared story as a node is touched, the Androsynth's
/// chapters the group's story, the vault read by the finder's ship's
/// shuttle. Against UQM's own generators: StoryPickupsTests'
/// EveryWorldsNodeSheetReportAndPickupIsUqms (tests/golden/story-nodes.json).
/// </summary>
[Trait("Topic", "story")]
public class StoryRuinsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    // A site's world, its ship (or ships) in orbit around it.
    private static (GameWorld World, WorldId At) Orbiting(string special, params string[] ids)
    {
        var world = NewGame(guests: ids.Count(id => id != "host"));
        var site = StoryWorlds.Sites.Single(s => s.Special == special);
        var at = new WorldId(Star(world, special), site.Planet, site.Moon);
        return (world, at);
    }

    private static void Orbit(GameWorld world, WorldId at, string id) => world.PutInOrbit(id, at.Star, at.Planet, at.Moon);

    // A player's lander down on the world's energy node (by its number), on the ground.
    private static Lander LandOn(GameWorld world, WorldId at, string id, int node)
    {
        var site = world.Find(id)!.Ship.Orbit!.Nodes!.Energy[node];
        return Land(world, id, site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
    }

    private static SurfaceElement EnergyNode(GameWorld world, WorldId at, int node) =>
        world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy && e.Node == node);

    // Keeps a node under a lander for some frames (StayOn), and returns the reports shown meanwhile.
    private static List<(string, LanderReport)> Touch(GameWorld world, Lander lander, SurfaceElement node, int frames = 5)
    {
        world.TakeReports();
        StayOn(world, lander, node, frames);
        return world.TakeReports();
    }

    [ContentFact]
    public void AHomeworldsRuinsAreThereOnceItsRaceIsExtinctAndTellTheFinderAloneOnceAVisit()
    {
        var (world, at) = Orbiting("ILWRATH_DEFINED", "host", "guest");
        // While the Ilwrath live, their world isn't the ruins' (UQM's talk comes instead): no text, no sheet.
        Orbit(world, at, "host");
        Assert.Null(world.Host.Ship.Discovery);
        Assert.Null(StoryWorlds.EnergySheet("ILWRATH_DEFINED", at, world.Story));
        Extinct(world.Story!, "ILWRATH_SHIP");
        Orbit(world, at, "host");
        Orbit(world, at, "guest");
        Assert.Equal(new ReportPlace(at, StoryWorlds.RuinsReport, 0), world.Host.Ship.Discovery);
        var guest = Land(world, "guest", 10, 10);
        var lander = LandOn(world, at, "host", 3);
        var ruins = world.SurfaceOf(at)!.Elements.Where(e => e.Kind == SurfaceElementKind.Energy).ToList();
        Assert.Equal(16, ruins.Count);
        Assert.All(ruins, r => Assert.Equal(SurfaceSheet.Ruins, r.Sheet));
        // The report the finder's alone: no take-off, nobody else told, the node staying.
        var shown = Touch(world, lander, EnergyNode(world, at, 3));
        Assert.Equal(new[] { ("host", new LanderReport(StoryWorlds.RuinsReport)) }, shown);
        Assert.False(lander.InTransit);
        Assert.Null(guest.Report);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 3));
        Assert.Contains(EnergyNode(world, at, 3), world.SurfaceOf(at)!.Elements);
        // One a visit: another ruin says nothing more; the guest's own place gives the guest theirs.
        Assert.Empty(Touch(world, lander, EnergyNode(world, at, 4)));
        Assert.Null(world.Host.Ship.Discovery);
        Assert.Equal(new[] { ("guest", new LanderReport(StoryWorlds.RuinsReport)) }, Touch(world, guest, EnergyNode(world, at, 5)));
        Assert.Null(lander.Report!.FoundBy);   // nor the host of the guest's
    }

    [ContentFact]
    public void ARaceMadeExtinctByASetUpLeavesItsRuinsWhereItsTalkWas()
    {
        // --start-extinct: the race's fleet gone and DEAD_GUY, as the calendar's deaths leave it; orbiting its
        // homeworld then is the ruins' orbit, not its talk (gensup.c's StartSphereTracking).
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, SystemGroupTests.Systems.Value.Map), "host",
            "Host", SystemGroupTests.Systems.Value, TestContent.Shapes);
        var world = host.World;
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.DialogueLocks = Comm.DialogueLocks.Load(DataDir);
        int star = Star(world, "SUPOX_DEFINED");
        Assert.NotNull(world.WorldTalkAt(star, 0, null));
        Assert.Equal("Starting with supox extinct", host.StartExtinct(new[] { "supox" }));
        Assert.Equal((Allied.Dead, 0), (world.Story!.Alliance("SUPOX_SHIP"), world.Story.Fleet("SUPOX_SHIP").ActualStrength));
        Assert.Null(world.WorldTalkAt(star, 0, null));
        world.PutInOrbit("host", star, 0);
        Assert.Equal(new ReportPlace(new WorldId(star, 0, null), "text.ultron", 0), world.Host.Ship.Discovery);
        // A ship of no race with a fleet is refused, changing nothing.
        Assert.Throws<ArgumentException>(() => host.StartExtinct(new[] { "druuge", "androsyn" }));
        Assert.NotEqual(Allied.Dead, world.Story.Alliance("DRUUGE_SHIP"));
    }

    [ContentFact]
    public void AnArtifactIsTheFirstFindersAndTheNextReadsItFoundFromTheStory()
    {
        // gensup.c: the Ultron at any ruin the first time; the shared record (ULTRON_CONDITION) is the story's,
        // so a player whose place was made before it was found reads the second entry, not the find again.
        var (world, at) = Orbiting("SUPOX_DEFINED", "host", "guest");
        Extinct(world.Story!, "SUPOX_SHIP");
        Orbit(world, at, "host");
        Orbit(world, at, "guest");
        Assert.Equal(new ReportPlace(at, "text.ultron", 0), world.Host.Ship.Discovery);
        var host = Land(world, "host", 10, 10);
        var guest = LandOn(world, at, "guest", 2);
        StayOn(world, guest, EnergyNode(world, at, 2), 3);
        Assert.Equal(new LanderReport("text.ultron"), guest.Report);
        Assert.True(guest.InTransit);
        Assert.Equal(new LanderReport("text.ultron", FoundBy: "guest"), host.Report);
        Assert.Equal(1, world.Story!.Get("ULTRON_CONDITION"));
        Assert.Contains(DeviceOf("ULTRON_CONDITION"), world.Find("guest")!.Ship.Hold.Devices);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 2));
        // The host's place was made with the Ultron not found: touched now, it reads the second entry, no
        // take-off, no second Ultron.
        Assert.Equal(new ReportPlace(at, "text.ultron", 0), world.Host.Ship.Discovery);
        var shown = Touch(world, host, EnergyNode(world, at, 7));
        Assert.Equal(new[] { ("host", new LanderReport("text.ultron", 1)) }, shown);
        Assert.False(host.InTransit);
        Assert.DoesNotContain(DeviceOf("ULTRON_CONDITION"), world.Host.Ship.Hold.Devices);
        Assert.Null(world.Host.Ship.Discovery);
        // Coming into orbit afterwards: the second entry.
        for (int i = 0; i < 400 && world.Find("guest")!.Ship.Lander is not null; ++i)
            Frames(world, 1);
        Orbit(world, at, "guest");
        Assert.Equal(new ReportPlace(at, "text.ultron", 1), world.Find("guest")!.Ship.Discovery);
    }

    [ContentFact]
    public void TheAndrosynthsChaptersComeInOrderAtNewRuinsAndAreTheGroupsStory()
    {
        var (world, at) = Orbiting("ANDROSYNTH_DEFINED", "host", "g1", "g2");
        Orbit(world, at, "host");
        Orbit(world, at, "g1");
        Assert.Equal(new ReportPlace(at, StoryWorlds.AndrosynthReport, 0), world.Host.Ship.Discovery);
        var host = LandOn(world, at, "host", 3);
        var g1 = Land(world, "g1", 10, 10);
        // A new ruin: the first chapter, the finder's, and word of it to every other player (g2 not landed, nor
        // in orbit); the ruin marked visited, the node staying.
        var shown = Touch(world, host, EnergyNode(world, at, 3));
        var first = new LanderReport(StoryWorlds.AndrosynthReport);
        Assert.Equal(new[] { ("host", first), ("g1", first with { FoundBy = "host" }), ("g2", first with { FoundBy = "host" }) },
            shown);
        Assert.False(host.InTransit);
        Assert.True(world.Planets.IsRetrieved(at, ScanType.Energy, 3 + StoryWorlds.VisitedShift));
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 3));
        Assert.Contains(EnergyNode(world, at, 3), world.SurfaceOf(at)!.Elements);
        // A ruin someone has visited says nothing; a new one, the next chapter, read from the story, though g1's
        // own place still stands at the first.
        Assert.Empty(Touch(world, g1, EnergyNode(world, at, 3)));
        Assert.Equal(new ReportPlace(at, StoryWorlds.AndrosynthReport, 0), world.Find("g1")!.Ship.Discovery);
        var second = new LanderReport(StoryWorlds.AndrosynthReport, 1);
        Assert.Equal(new[] { ("g1", second), ("host", second with { FoundBy = "g1" }), ("g2", second with { FoundBy = "g1" }) },
            Touch(world, g1, EnergyNode(world, at, 9)));
        // Saved and loaded, the ruins visited are kept: coming into orbit, the third chapter is the first unread.
        var loaded = HostSave.FromJson(HostSave.ToJson(world), SystemGroupTests.Systems.Value, TestContent.Shapes);
        Assert.Equal(new ReportPlace(at, StoryWorlds.AndrosynthReport, 2), loaded.Host.Ship.Discovery);
        var third = new LanderReport(StoryWorlds.AndrosynthReport, 2);
        Assert.Equal(("host", third), Touch(world, host, EnergyNode(world, at, 11))[0]);
        // All three told: a new ruin is marked but tells nothing, and a ship coming into orbit has no text.
        Assert.Empty(Touch(world, g1, EnergyNode(world, at, 12)));
        Assert.True(world.Planets.IsRetrieved(at, ScanType.Energy, 12 + StoryWorlds.VisitedShift));
        Orbit(world, at, "g2");
        Assert.Null(world.Find("g2")!.Ship.Discovery);
        // The scan still shows all 16.
        Assert.Equal(16, PlanetWorldState.Remaining(world.Snapshot().OrbitedWorlds!.Single(w => w.Star == at.Star).EnergyTaken,
            world.Host.Ship.Orbit!.Nodes!.Energy).Count);
    }

    [ContentFact]
    public void TheWreckGivesThePortalKeyOnceAndStays()
    {
        var (world, at) = Orbiting("URQUAN_WRECK_DEFINED", "host", "guest");
        Orbit(world, at, "host");
        Orbit(world, at, "guest");
        var other = Land(world, "host", 10, 10);
        var lander = LandOn(world, at, "guest", 0);
        var wreck = EnergyNode(world, at, 0);
        Assert.Equal(SurfaceSheet.UrquanWreck, wreck.Sheet);
        StayOn(world, lander, wreck, 3);
        Assert.Equal(new LanderReport("text.urquanwreck"), lander.Report);
        Assert.True(lander.InTransit);
        Assert.Equal(new LanderReport("text.urquanwreck", FoundBy: "guest"), other.Report);
        Assert.Equal(1, world.Story!.Get("PORTAL_KEY"));
        Assert.Contains(DeviceOf("PORTAL_KEY_ON_SHIP"), world.Find("guest")!.Ship.Hold.Devices);
        Assert.Equal(new ReportPlace(at, "text.urquanwreck", 1), world.Find("guest")!.Ship.Discovery);
        // The host, whose place was made before: the second entry, the finder's alone, no second key.
        Assert.Equal(new[] { ("host", new LanderReport("text.urquanwreck", 1)) }, Touch(world, other, wreck));
        Assert.False(other.InTransit);
        Assert.DoesNotContain(DeviceOf("PORTAL_KEY_ON_SHIP"), world.Host.Ship.Hold.Devices);
        Assert.Contains(wreck, world.SurfaceOf(at)!.Elements);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 0));
    }

    [ContentFact]
    public void TheVaultIsReadByTheFindersShuttle()
    {
        var (world, at) = Orbiting("SHIP_VAULT_DEFINED", "host", "guest");
        world.Find("guest")!.Ship.Hold.AddDevice(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"));
        world.Story!.Set("SYREEN_HOME_VISITS", 3);
        Orbit(world, at, "host");
        Orbit(world, at, "guest");
        // genvault.c:69: each ship's place by its own shuttle.
        Assert.Equal(new ReportPlace(at, StoryWorlds.VaultReport, 0), world.Host.Ship.Discovery);
        Assert.Equal(new ReportPlace(at, StoryWorlds.VaultReport, 1), world.Find("guest")!.Ship.Discovery);
        // Locked, without the shuttle: the first text, the finder's alone, once; the vault known.
        var host = LandOn(world, at, "host", 0);
        var vault = EnergyNode(world, at, 0);
        Assert.Equal(SurfaceSheet.SyreenVault, vault.Sheet);
        Assert.Equal(new[] { ("host", new LanderReport(StoryWorlds.VaultReport)) }, Touch(world, host, vault));
        Assert.False(host.InTransit);
        Assert.Equal(1, world.Story.Get("KNOW_SYREEN_VAULT"));
        Assert.Equal(0, world.Story.Get("SHIP_VAULT_UNLOCKED"));
        Assert.Empty(Touch(world, host, vault));
        // With the shuttle: opened, the guest's lander up, the shuttle used up, the Syreen's visits afresh; the
        // host's lander there told.
        var guest = LandOn(world, at, "guest", 0);
        StayOn(world, guest, vault, 3);
        Assert.Equal(new LanderReport(StoryWorlds.VaultReport, 1), guest.Report);
        Assert.True(guest.InTransit);
        Assert.Equal(new LanderReport(StoryWorlds.VaultReport, 1, "guest"), host.Report);
        Assert.Equal(1, world.Story.Get("SHIP_VAULT_UNLOCKED"));
        Assert.Equal(0, world.Story.Get("SYREEN_HOME_VISITS"));
        Assert.DoesNotContain(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"), world.Find("guest")!.Ship.Hold.Devices);
        Assert.Contains(vault, world.SurfaceOf(at)!.Elements);
        // Coming into orbit afterwards: the last text, "empty", once.
        Orbit(world, at, "host");
        Assert.Equal(new ReportPlace(at, StoryWorlds.VaultReport, 2), world.Host.Ship.Discovery);
    }

    [ContentFact]
    public void TheVaultReadsTheShuttleAsTheNodeIsTouched()
    {
        // The place made without it, the shuttle handed over in orbit: touched, the vault reads the finder's ship
        // as it is then (the text from the story as the node is touched, the plan's question 10).
        var (world, at) = Orbiting("SHIP_VAULT_DEFINED", "host");
        Orbit(world, at, "host");
        Assert.Equal(0, world.Host.Ship.Discovery!.Entry);
        var host = LandOn(world, at, "host", 0);
        world.Host.Ship.Hold.AddDevice(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"));
        StayOn(world, host, EnergyNode(world, at, 0), 3);
        Assert.Equal(new LanderReport(StoryWorlds.VaultReport, 1), host.Report);
        Assert.True(host.InTransit);
        Assert.Equal(1, world.Story!.Get("SHIP_VAULT_UNLOCKED"));
    }

    [ContentFact]
    public void AShuttleHandedOverAfterTheLockedTextOpensTheVaultWithItsText()
    {
        // Review round 1: the locked text shown uses up the visit's text; a shuttle given to the finder on the giving
        // screen while its lander is down, the next touch opens the vault with its own text, the lander up, the
        // others told, never silently.
        var (world, at) = Orbiting("SHIP_VAULT_DEFINED", "host", "guest");
        world.Find("guest")!.Ship.Hold.AddDevice(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"));
        Orbit(world, at, "host");
        Orbit(world, at, "guest");
        var host = LandOn(world, at, "host", 0);
        var guest = Land(world, "guest", 10, 10);
        var vault = EnergyNode(world, at, 0);
        Assert.Equal(new[] { ("host", new LanderReport(StoryWorlds.VaultReport)) }, Touch(world, host, vault));
        Assert.Null(world.Host.Ship.Discovery);
        Assert.Equal(TradeRefusal.None, world.Give(new Gift("guest", "host", 0, new int[ShipHold.Categories], 0,
            Device: DeviceOf("SYREEN_SHUTTLE_ON_SHIP"))));
        Assert.Contains(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"), world.Host.Ship.Hold.Devices);
        var opened = new LanderReport(StoryWorlds.VaultReport, 1);
        Assert.Equal(new[] { ("host", opened), ("guest", opened with { FoundBy = "host" }) }, Touch(world, host, vault));
        Assert.True(host.InTransit);
        Assert.Equal(opened with { FoundBy = "host" }, guest.Report);
        Assert.Equal(1, world.Story!.Get("SHIP_VAULT_UNLOCKED"));
        Assert.DoesNotContain(DeviceOf("SYREEN_SHUTTLE_ON_SHIP"), world.Host.Ship.Hold.Devices);
    }

    [ContentFact]
    public void ProcyonsStarbaseShowsItsReportAsAShipComesToIt()
    {
        // genchmmr.c: the Chmmr's moon, a starbase never entered; its report the player's own, the ship by it.
        var world = NewGame();
        int star = Star(world, "CHMMR_DEFINED");
        Assert.False(world.Systems![star].EnterOrbit(1, 0).Entered);
        world.TakeReports();
        world.PutInOrbit("guest", star, 1, 0);
        Assert.Equal(new[] { ("guest", new LanderReport(StoryWorlds.ChmmrBaseReport)) }, world.TakeReports());
        Assert.Null(world.Find("guest")!.Ship.OrbitedWorld);
        Assert.Equal(star, world.Find("guest")!.Ship.SystemStar);
    }

    // One frame of a player's flight in a system toward a point on the screen: turned to it, thrusting once facing it.
    private static void SteerTo(GameWorld world, string id, IpPoint target)
    {
        var ip = world.Find(id)!.Ship.Ip!;
        int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)(target.X - ip.Screen.X), (short)(target.Y - ip.Screen.Y)));
        int turn = (want - ip.Facing) & (Uqm.UqmMath.Facings - 1);
        Frames(world, 1, id, turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right);
    }

    [ContentFact]
    public void FlyingIntoProcyonsStarbaseShowsItsReportOnceATouch()
    {
        // Review round 1: StepSystem's way in, flying into the moon. By it (as --start-orbit leaves the ship, the
        // contact remembered), staying there shows nothing more; flown away and back into it, the report again, as
        // UQM's each time the ship comes to it.
        var world = NewGame(guests: 0);
        int star = Star(world, "CHMMR_DEFINED");
        world.PutInOrbit("host", star, 1, 0);
        world.TakeReports();
        var ip = world.Host.Ship.Ip!;
        Assert.Equal(1, ip.InnerPlanet);
        Frames(world, 60);
        Assert.Empty(world.TakeReports());
        var moon = ip.MoonScreen[0];
        // Away from it...
        Frames(world, 25, "host", ShipInput.Thrust);
        Assert.Empty(world.TakeReports());
        Assert.NotEqual(moon, ip.Screen);
        // ...and back into it.
        var shown = new List<(string, LanderReport)>();
        for (int i = 0; i < 2000 && shown.Count == 0; ++i)
        {
            SteerTo(world, "host", moon);
            shown.AddRange(world.TakeReports());
        }
        Assert.Equal(new[] { ("host", new LanderReport(StoryWorlds.ChmmrBaseReport)) }, shown);
        Assert.Null(world.Host.Ship.OrbitedWorld);
        Assert.Equal(1, world.Host.Ship.Ip!.InnerPlanet);
        Frames(world, 60);
        Assert.Empty(world.TakeReports());
    }

    [Fact]
    public void ProcyonsReportShowsOnTheSystemScreenAndNoOtherThere()
    {
        // Review round 1: the system screen shows a world's report as a ship comes to it (Procyon's starbase's), as
        // UQM's over its space window; any other own report waiting there is a notice, as before item 6.
        var player = new PlayerSession(SystemGroupTests.Systems.Value, null, "host", TestContent.Shapes.HyperFlagship.Count)
        {
            StringTables = id => new[] { id + " text" },
        };
        Assert.False(PlayerSession.ShowsReports(PlayerScreen.System));
        player.ShowReport(new LanderReport(StoryWorlds.RuinsReport));
        player.ShowReport(new LanderReport(StoryWorlds.ChmmrBaseReport));
        Assert.Null(player.OwnReportFor(PlayerScreen.System, free: false));
        Assert.StartsWith(StoryWorlds.RuinsReport + " text", player.Notice);
        Assert.Equal(StoryWorlds.ChmmrBaseReport + " text", player.OwnReportFor(PlayerScreen.System, free: true));
        Assert.Null(player.TakeOwnReport());
        // Elsewhere (hyperspace) it's a notice, as every report.
        player.ShowReport(new LanderReport(StoryWorlds.ChmmrBaseReport));
        Assert.Null(player.OwnReportFor(PlayerScreen.Hyperspace, free: true));
        Assert.Null(player.TakeOwnReport());
    }
}
