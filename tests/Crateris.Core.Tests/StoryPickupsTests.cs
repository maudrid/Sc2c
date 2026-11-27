// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Planetside;
using Crateris.Core.Session;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The story worlds' single-node pickups and their reports (the stage 3c
/// plan's item 5, part 2; its question 10): each world's node, sheet,
/// report and story against UQM's own generators
/// (tests/golden/story-nodes.json); the ship's place in a world's report;
/// the nodes on the scan following the story; a pickup's device going to
/// the finder's ship, its report to the finder and word of it to the other
/// landers there; the reports' entries on the client.
/// </summary>
[Trait("Topic", "story")]
public class StoryPickupsTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static SystemCatalog Systems => SystemGroupTests.Systems.Value;

    // ------------------------------------------------------------ against UQM

    // The recorded fields the port's StoryWorlds doesn't set: TAALO_UNPROTECTED, which genorz.c's
    // generateOrbital sets as it falls through to the orbit, the port's world talks set (GameWorld.WorldTalks,
    // WorldTalkTests).
    private static readonly HashSet<string> NotStoryWorlds = new() { "TAALO_UNPROTECTED" };

    [Fact]
    public void EveryWorldsNodeSheetReportAndPickupIsUqms()
    {
        var golden = Golden.Load("story-nodes");
        var fields = golden.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToList();
        var cases = golden.GetProperty("cases").EnumerateArray().ToList();
        Assert.Equal(26, cases.Count);
        var seen = new HashSet<StoryNode>();
        foreach (var c in cases)
        {
            string name = c.GetProperty("name").GetString()!;
            int star = c.GetProperty("star").GetInt32(), planet = c.GetProperty("planet").GetInt32();
            int? moon = c.GetProperty("moon").GetInt32() is var m and >= 0 ? m : null;
            var world = new WorldId(star, planet, moon);
            string? special = Systems.Map.Stars[star].Special;
            var story = StoryState.NewGame(Rules, new GameClock(2155, 3, 4));
            uint retrieved = 0;
            foreach (string assignment in c.GetProperty("state").GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var (field, value) = (assignment.Split('=')[0], int.Parse(assignment.Split('=')[1]));
                if (field == "RETRIEVED")
                    retrieved = (uint)value;
                else
                    story.Set(field, value);
            }
            Assert.Equal(1, c.GetProperty("entered").GetInt32());
            var orbit = Systems[star].EnterOrbit(planet, moon);

            // generateOrbital: the report's place, the sheet loaded, the story it sets.
            var place = StoryWorlds.Discovery(special, world, story, retrieved);
            Assert.True(Place(c.GetProperty("discovery")) == Of(place), name);
            var sheet = StoryWorlds.EnergySheet(special, world, story, retrieved);
            var graphics = c.GetProperty("graphics").EnumerateArray().Select(g => g.GetString()).ToList();
            if (name.StartsWith("pluto"))
                Assert.Null(sheet);   // Fwiffo's sheet is put on the surface with his story (stage 3c item 7)
            else
            {
                Assert.True((c.GetProperty("sheet").GetInt32() == 1) == (sheet is not null), name);
                Assert.Equal(sheet is { } s ? new[] { ContactShapes.EnergyResources[s] } : Array.Empty<string>(), graphics);
            }
            StoryWorlds.Orbited(Systems.Map, world, story);
            Fields(c.GetProperty("orbit_fields"), story, null, name + " orbit");

            // generateEnergy and generateLife: the nodes on the scan, by the story and the taken nodes.
            int energy = PlanetWorldState.Remaining(retrieved | (StoryWorlds.EnergyHidden(special, world, story) ? uint.MaxValue : 0),
                orbit.Nodes!.Energy).Count;
            Assert.True(c.GetProperty("energy").GetInt32() == energy, name);
            Assert.Equal(c.GetProperty("life").GetInt32(), orbit.Nodes.Life.Count);

            if (!c.TryGetProperty("pickup", out var pickup))
                continue;
            var finder = new Finder(place);
            int node = pickup.GetProperty("node").GetInt32();
            bool taken = true;
            if (pickup.GetProperty("scan").GetInt32() == (int)ScanType.Energy)
                taken = StoryWorlds.PickupEnergy(special, world, story, finder, node);
            else
                StoryWorlds.PickupCreature(special, world, story, finder, node);   // every creature is taken
            Assert.True((pickup.GetProperty("taken").GetInt32() == 1) == taken, name);
            Assert.True((pickup.GetProperty("takeoffs").GetInt32() > 0) == finder.Takeoff, name);
            var reports = pickup.GetProperty("reports").EnumerateArray().Select(Place).ToList();
            Assert.Equal(reports, finder.Report is { } r ? new[] { ((string, int)?)(r.Resource, r.Entry) } : Array.Empty<(string, int)?>());
            Assert.True(Place(pickup.GetProperty("discovery")) == Of(finder.Place), name);
            Fields(pickup.GetProperty("fields"), story, finder, name + " pickup");
            if (StoryWorlds.Nodes.FirstOrDefault(n => n.On(special, world) && n.Node == node) is { } storyNode && finder.Takeoff)
                seen.Add(storyNode);
        }
        // Every single node taken in the recording.
        Assert.Equal(StoryWorlds.Nodes.ToHashSet(), seen);

        // The recorded fields: the shared ones the story's, the held devices the finder's ship's.
        void Fields(JsonElement values, StoryState story, Finder? finder, string what)
        {
            var v = values.Ints();
            for (int i = 0; i < fields.Count; ++i)
            {
                if (NotStoryWorlds.Contains(fields[i]))
                    continue;
                int port = DeviceTable.HeldByShips(fields[i]) ? (finder?.Devices.Contains(fields[i]) == true ? 1 : 0)
                    : story.Get(fields[i]);
                Assert.True(v[i] == port, $"{what}: {fields[i]} is {port}, UQM's {v[i]}");
            }
        }
    }

    private static (string, int)? Of(ReportPlace? p) => p is null ? null : (p.Resource, p.Entry);

    private static (string, int)? Place(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null ? null : (e[0].GetString()!, e[1].GetInt32());

    [ContentFact]
    public void EachReportsEntriesAreTheContents()
    {
        foreach (var (resource, entries) in StoryWorlds.ReportEntries)
            Assert.Equal(entries, TestContent.With(p => StringTable.Load(p, resource)).Count);
        // Every single node's report is one of them.
        Assert.All(StoryWorlds.Nodes, n => Assert.Contains(n.Report, StoryWorlds.ReportEntries.Keys));
    }

    [ContentFact]
    public void EveryEnergyNodesSheetIsLoadedAndDrawn()
    {
        foreach (var (sheet, resource) in ContactShapes.EnergyResources)
        {
            Assert.NotEmpty(TestContent.Shapes.EnergySheets[sheet]);
            Assert.Contains((PlanetsideScreen.EnergySheet(sheet), resource), PlanetsideSprites.Resources());
        }
    }

    [Fact]
    public void ACyclingReportGoesRoundItsEntriesOnceAndAShownOneIsGone()
    {
        // GenerateDefault_landerReportCycle: each entry once, then the text is gone (stage 3c item 6's ruins build on it).
        var world = new WorldId(1, 0, null);
        var finder = new Finder(new ReportPlace(world, StoryWorlds.UmgahCasterReport, 0));
        finder.LanderReportCycle();
        Assert.Equal(new LanderReport(StoryWorlds.UmgahCasterReport, 0), finder.Report);
        Assert.Equal(new ReportPlace(world, StoryWorlds.UmgahCasterReport, 1), finder.Place);
        finder = new Finder(finder.Place);
        finder.LanderReportCycle();
        Assert.Equal(new LanderReport(StoryWorlds.UmgahCasterReport, 1), finder.Report);
        Assert.Null(finder.Place);
        finder = new Finder(finder.Place);
        finder.LanderReportCycle();
        Assert.Null(finder.Report);
        Assert.False(finder.Takeoff);
        // GenerateDefault_landerReport: shown once, from where the place is, then gone.
        finder = new Finder(new ReportPlace(world, StoryWorlds.UmgahCasterReport, 1));
        finder.LanderReport();
        Assert.Equal(new LanderReport(StoryWorlds.UmgahCasterReport, 1), finder.Report);
        Assert.Null(finder.Place);
    }

    // ------------------------------------------------------------ in the game

    private static GameWorld NewGame(int guests = 1)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems,
            TestContent.Shapes);
        world.EncounterRaces = new HashSet<string>();   // no groups in the systems
        world.Clock.FramesIntoDay = -100000;   // no day's events meanwhile
        for (int i = 1; i <= guests; ++i)
        {
            string id = guests == 1 ? "guest" : $"g{i}";
            Assert.Equal(JoinResult.Joined, world.Join(id, id));
            world.UndockShown(id);
        }
        return world;
    }

    private static int Star(GameWorld world, string special) => world.Systems!.Map.Stars.First(s => s.Special == special).Number;

    private static int DeviceOf(string field) => Rules.Devices.All.First(d => d.Listed && d.Field == field).Id;

    private static void Frames(GameWorld world, int frames, string? who = null, ShipInput keys = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == who ? keys : ShipInput.None);
            world.Step();
        }
    }

    // Lands a player's lander (at the node's site, or elsewhere) and waits for it on the ground.
    private static Lander Land(GameWorld world, string id, int x, int y)
    {
        world.Find(id)!.Ship.Hold.Upgrades = LanderUpgrades.All;   // shielded: the hot worlds' hazards would kill it on the way
        Assert.Equal(DispatchRefusal.None, world.Dispatch(id, x, y));
        var lander = world.Find(id)!.Ship.Lander!;
        for (int i = 0; i < 400 && lander.Phase != LanderPhase.Ground; ++i)
            Frames(world, 1);
        Assert.Equal(LanderPhase.Ground, lander.Phase);
        return lander;
    }

    // Drives a lander onto a surface element until its report shows (the world's pickup).
    private static void DriveOnto(GameWorld world, string id, SurfaceElement target)
    {
        var lander = world.Find(id)!.Ship.Lander!;
        for (int i = 0; i < 3000 && lander.Report is null; ++i)
        {
            ShipInput keys = ShipInput.None;
            if (lander.Phase == LanderPhase.Ground)
            {
                int dx = target.X - lander.X, dy = target.Y - lander.Y;
                if (dx > PlanetsideMap.Width / 2)
                    dx -= PlanetsideMap.Width;
                else if (dx < -PlanetsideMap.Width / 2)
                    dx += PlanetsideMap.Width;
                int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)dx, (short)dy));
                int turn = (want - lander.Facing) & (Uqm.UqmMath.Facings - 1);
                keys = turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right;
            }
            Frames(world, 1, id, keys);
        }
        Assert.True(lander.Report is not null, $"{lander.Phase} crew {lander.Crew} at {lander.X},{lander.Y}, target {target.X},{target.Y} {target.Disappearing}");
    }

    private static SurfaceElement Node(GameWorld world, WorldId at) =>
        world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Energy);

    public static IEnumerable<object[]> EnergyNodes() =>
        StoryWorlds.Nodes.Where(n => n.Scan == ScanType.Energy && n.Special != "SOL_DEFINED").Select(n => new object[] { n.Special });

    [ContentTheory]
    [MemberData(nameof(EnergyNodes))]
    public void ANodeTakenIsTheFindersShipsTheirLanderGoesUpWithItsReportAndTheOtherLanderIsTold(string special)
    {
        var node = StoryWorlds.Nodes.Single(n => n.Special == special);
        var world = NewGame();
        var at = new WorldId(Star(world, special), node.Planet, node.Moon);
        world.Story!.Set("DRUUGE_VISITS", 3);
        world.Story.Set("HELIX_VISITS", 2);
        world.Story.Set("MYCON_VISITS", 4);
        world.PutInOrbit("guest", at.Star, at.Planet, at.Moon);
        world.PutInOrbit("host", at.Star, at.Planet, at.Moon);
        Assert.Equal(at, world.Find("guest")!.Ship.OrbitedWorld);
        // Each ship's own place in the world's report (genXXX.c's generateOrbital).
        Assert.Equal(new ReportPlace(at, node.Report, node.Report == StoryWorlds.UmgahCasterReport ? 1 : 0),
            world.Find("guest")!.Ship.Discovery);
        Assert.Equal(world.Find("guest")!.Ship.Discovery, world.Host.Ship.Discovery);
        var other = Land(world, "host", 10, 10);
        var surface = world.SurfaceOf(at)!;
        var site = world.Find("guest")!.Ship.Orbit!.Nodes!.Energy[0];
        var guestLander = Land(world, "guest", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        var target = Node(world, at);
        Assert.Equal(node.Sheet, target.Sheet);
        DriveOnto(world, "guest", target);

        var guest = world.Find("guest")!;
        var report = new LanderReport(node.Report, node.Report == StoryWorlds.UmgahCasterReport ? 1 : 0);
        Assert.Equal(report, guestLander.Report);
        Assert.True(guestLander.InTransit);
        Assert.Equal(report with { FoundBy = "guest" }, other.Report);
        Assert.Contains(("guest", report), world.TakeReports());
        // The device is the finder's ship's; the story's found, as the generator has it.
        Assert.Contains(DeviceOf(node.Device), guest.Ship.Hold.Devices);
        Assert.DoesNotContain(DeviceOf(node.Device), world.Host.Ship.Hold.Devices);
        if (node.Found is { } found)
            Assert.Equal(1, world.Story.Get(found));
        foreach (var (field, value) in node.Also)
            Assert.Equal(value, world.Story.Get(field));
        Assert.True(world.Planets.IsRetrieved(at, ScanType.Energy, 0));
        // The finder's place in the text is gone; the other's stays (each ship's own).
        Assert.Null(guest.Ship.Discovery);
        Assert.NotNull(world.Host.Ship.Discovery);
        // The finder's lander goes up and is back aboard; the other stays on the ground.
        for (int i = 0; i < 400 && guest.Ship.Lander is not null; ++i)
            Frames(world, 1);
        Assert.Null(guest.Ship.Lander);
        Assert.Equal(LanderPhase.Ground, other.Phase);
        Assert.Contains(other, surface.Landers);
        // The scan shows it no more, nor does the next landing find it.
        Assert.Equal(node.Found is null ? 1u : uint.MaxValue, world.Snapshot().OrbitedWorlds!.Single(w => w.Star == at.Star).EnergyTaken);
    }

    [ContentTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheUmgahBroadcastersReportIsChosenAsTheShipGoesIntoOrbit(int shielded)
    {
        // genspa.c:166-174: the first entry speaks of the Spathi's shield; the second is shown while they haven't
        // shielded themselves. Each ship's place is chosen as it goes into orbit.
        var world = NewGame(guests: 0);
        int star = Star(world, "SPATHI_DEFINED");
        var moon = new WorldId(star, 0, 0);
        world.Story!.Set("SPATHI_SHIELDED_SELVES", shielded);
        world.PutInOrbit("host", star, 0, 0);
        Assert.Equal(new ReportPlace(moon, StoryWorlds.UmgahCasterReport, 1 - shielded), world.Host.Ship.Discovery);
        // The shield raised meanwhile leaves the place as it was (UQM's DiscoveryString lives for the orbit).
        world.Story.Set("SPATHI_SHIELDED_SELVES", 1);
        Land(world, "host", 10, 10);
        DriveOnto(world, "host", Node(world, moon));
        Assert.Equal(new LanderReport(StoryWorlds.UmgahCasterReport, 1 - shielded), world.Host.Ship.Lander!.Report);
    }

    // A stand-in for stage 3c item 6's nodes that stay, on the Taalo protector's moon: the host landed at the node,
    // the guest elsewhere on the moon, the host's place in a two-entry report.
    private static (GameWorld World, WorldId At, Lander Host, Lander Guest, SurfaceElement Node) NodeThatStays(
        GameWorld.EnergyPickup pickup)
    {
        var world = NewGame();
        world.StoryEnergyPickup = pickup;
        var at = new WorldId(Star(world, "TAALO_PROTECTOR_DEFINED"), 1, 2);
        world.PutInOrbit("guest", at.Star, at.Planet, at.Moon);
        world.PutInOrbit("host", at.Star, at.Planet, at.Moon);
        world.Host.Ship.Discovery = new ReportPlace(at, StoryWorlds.UmgahCasterReport, 0);
        var guest = Land(world, "guest", 10, 60);
        var site = world.Host.Ship.Orbit!.Nodes!.Energy[0];
        var host = Land(world, "host", site.X << PlanetsideMap.MagShift, site.Y << PlanetsideMap.MagShift);
        return (world, at, host, guest, Node(world, at));
    }

    // Keeps a node under a lander for some frames, as a lander parked on it would.
    private static void StayOn(GameWorld world, Lander lander, SurfaceElement node, int frames)
    {
        for (int i = 0; i < frames && lander.Phase == LanderPhase.Ground; ++i)
        {
            node.X = lander.X;
            node.Y = lander.Y;
            Frames(world, 1);
        }
    }

    [ContentFact]
    public void ANodeThatStaysShowsItsReportsToTheFinderAloneWithoutATakeoff()
    {
        // GenerateDefault_landerReportCycle on a node that stays (the ruins, item 6): each touch the next entry, the
        // lander staying down, nobody else told, the node still there; once the entries have gone round, nothing.
        var (world, at, host, guest, node) = NodeThatStays((_, _, _, finder, _) =>
        {
            finder.LanderReportCycle();
            return false;
        });
        world.TakeReports();
        StayOn(world, host, node, 10);
        Assert.Equal(new (string, LanderReport)[]
        {
            ("host", new LanderReport(StoryWorlds.UmgahCasterReport, 0)),
            ("host", new LanderReport(StoryWorlds.UmgahCasterReport, 1)),
        }, world.TakeReports());
        Assert.False(host.InTransit);
        Assert.Equal(LanderPhase.Ground, host.Phase);
        Assert.Null(guest.Report);
        Assert.Null(world.Host.Ship.Discovery);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 0));
        Assert.Contains(node, world.SurfaceOf(at)!.Elements);
        Assert.False(node.Disappearing);
    }

    [ContentFact]
    public void ANodeThatStaysCanSendTheLanderUpWithItsReport()
    {
        // The wreck's and the unlocked vault's way (item 6): a report and a take-off, the node staying; the other
        // landers there are told, and stay.
        var (world, at, host, guest, node) = NodeThatStays((_, _, _, finder, _) =>
        {
            finder.LanderReport();
            finder.SetTakeoff();
            return false;
        });
        StayOn(world, host, node, 3);
        var report = new LanderReport(StoryWorlds.UmgahCasterReport, 0);
        Assert.Equal(report, host.Report);
        Assert.True(host.InTransit);
        Assert.Equal(report with { FoundBy = "host" }, guest.Report);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 0));
        for (int i = 0; i < 400 && world.Host.Ship.Lander is not null; ++i)
            Frames(world, 1);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Contains(node, world.SurfaceOf(at)!.Elements);
        Assert.Equal(LanderPhase.Ground, guest.Phase);
    }

    [ContentTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void AFullLanderLeavesZexsBeautyAndOneWithAnyRoomTakesIt(int room)
    {
        // pickupBioNode refuses only a full lander; with any room it takes what fits, and pickupLife comes after it.
        var world = NewGame(guests: 0);
        int star = Star(world, "VUX_BEAST_DEFINED");
        var at = new WorldId(star, 0, null);
        world.PutInOrbit("host", star, 0);
        var lander = Land(world, "host", 120, 40);
        Assert.True(lander.PickUpBioData(ShipHold.MaxScrounged - room));
        var beast = world.SurfaceOf(at)!.Elements.Single(e => e.Kind == SurfaceElementKind.Creature && e.Node == 0);
        beast.HitPoints = 0;
        beast.MassPoints = 15;
        beast.Sheet = SurfaceSheet.Canister;
        beast.Frame = 0;
        StayOn(world, lander, beast, 20);
        Assert.Equal(ShipHold.MaxScrounged, lander.BioData);
        Assert.Equal(room, world.Story!.Get("VUX_BEAST"));
        Assert.Equal(room == 1, world.Planets.IsRetrieved(at, ScanType.Biological, 0));
        Assert.Equal(room == 1, world.Host.Ship.Hold.Devices.Contains(DeviceOf("VUX_BEAST_ON_SHIP")));
        Assert.Equal(room == 1 ? new LanderReport("text.vuxbeast") : null, lander.Report);
        Assert.Equal(room == 1, lander.InTransit);
    }

    [ContentFact]
    public void TheShipsPlaceIsMadeAfreshOnALoadInOrbitAndOnGoingIntoOrbitAgain()
    {
        // Reading (o): the place is the visit's, made from the story as the ship goes into orbit, as UQM's
        // generateOrbital loads the text, and so on a load in orbit too.
        var world = NewGame(guests: 0);
        int star = Star(world, "SPATHI_DEFINED");
        var moon = new WorldId(star, 0, 0);
        world.PutInOrbit("host", star, 0, 0);
        Assert.Equal(new ReportPlace(moon, StoryWorlds.UmgahCasterReport, 1), world.Host.Ship.Discovery);
        // Its text used up by a report, a load in orbit gives it back.
        world.Host.Ship.Discovery = null;
        var loaded = HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes);
        Assert.Equal(moon, loaded.Host.Ship.OrbitedWorld);
        Assert.Equal(new ReportPlace(moon, StoryWorlds.UmgahCasterReport, 1), loaded.Host.Ship.Discovery);
        // The broadcaster found (by someone else) during the visit: this visit's place stays; a load in orbit
        // makes none.
        world.PutInOrbit("host", star, 0, 0);
        world.Story!.Set("UMGAH_BROADCASTERS", 1);
        Assert.NotNull(world.Host.Ship.Discovery);
        Assert.Null(HostSave.FromJson(HostSave.ToJson(world), Systems, TestContent.Shapes).Host.Ship.Discovery);
        // The Spathi shield themselves while the ship orbits their moon: the place keeps the entry it was made with;
        // going into orbit again makes it afresh, the first entry now.
        world.Story.Set("UMGAH_BROADCASTERS", 0);
        world.PutInOrbit("host", star, 0, 0);
        world.Story.Set("SPATHI_SHIELDED_SELVES", 1);
        Assert.Equal(1, world.Host.Ship.Discovery!.Entry);
        world.PutInOrbit("host", star, 0, 0);
        Assert.Equal(new ReportPlace(moon, StoryWorlds.UmgahCasterReport, 0), world.Host.Ship.Discovery);
    }

    [ContentFact]
    public void ZexsBeautyTakenIsTheFindersAndSendsTheLanderUp()
    {
        var world = NewGame();
        int star = Star(world, "VUX_BEAST_DEFINED");
        var at = new WorldId(star, 0, null);
        world.PutInOrbit("guest", star, 0);
        world.PutInOrbit("host", star, 0);
        var other = Land(world, "host", 30, 30);
        var lander = Land(world, "guest", 120, 40);
        var surface = world.SurfaceOf(at)!;
        var beast = surface.Elements.Single(e => e.Kind == SurfaceElementKind.Creature && e.Node == 0);
        Assert.Equal(25, beast.LifeKind);   // NUM_CREATURE_TYPES + 2: Zex's Beauty
        // Canned (shotCreature: its value, 15, its mass_points) and under the finder's lander.
        beast.HitPoints = 0;
        beast.MassPoints = 15;
        beast.Sheet = SurfaceSheet.Canister;
        beast.Frame = 0;
        int bio = lander.BioData;
        for (int i = 0; i < 20 && lander.Report is null; ++i)
        {
            beast.X = lander.X;
            beast.Y = lander.Y;
            Frames(world, 1);
        }
        Assert.Equal(new LanderReport("text.vuxbeast"), lander.Report);
        Assert.True(lander.InTransit);
        Assert.Equal(bio + 15, lander.BioData);   // its bio data too, as any creature's
        Assert.Equal(new LanderReport("text.vuxbeast", FoundBy: "guest"), other.Report);
        Assert.Equal(1, world.Story!.Get("VUX_BEAST"));
        Assert.Contains(DeviceOf("VUX_BEAST_ON_SHIP"), world.Find("guest")!.Ship.Hold.Devices);
        Assert.True(world.Planets.IsRetrieved(at, ScanType.Biological, 0));
        // Another creature there is only a creature.
        var bush = surface.Elements.First(e => e.Kind == SurfaceElementKind.Creature && e.Node != 0);
        Assert.NotEqual(25, bush.LifeKind);
    }

    [ContentFact]
    public void ANodeTheStoryHasFoundIsOffTheScanAndTheSurface()
    {
        // The Maidens found (here by a set-up's field, no node taken): generateEnergy gives none, so the scan
        // shows none and the surface has none; Pluto's once Fwiffo's found (FOUND_PLUTO_SPATHI, no retrieved bit).
        var world = NewGame(guests: 0);
        int star = Star(world, "MAIDENS_DEFINED");
        var at = new WorldId(star, 0, null);
        world.PutInOrbit("host", star, 0);
        Assert.Equal(0u, world.Snapshot().OrbitedWorlds!.Single().EnergyTaken);
        world.Story!.Set("SHOFIXTI_MAIDENS", 1);
        Assert.Equal(uint.MaxValue, world.Snapshot().OrbitedWorlds!.Single().EnergyTaken);
        Land(world, "host", 10, 10);
        Assert.DoesNotContain(world.SurfaceOf(at)!.Elements, e => e.Kind == SurfaceElementKind.Energy);
        Assert.False(world.Planets.IsRetrieved(at, ScanType.Energy, 0));
        // Pluto.
        var pluto = new WorldId(82, 8, null);
        var p2 = NewGame(guests: 0);
        p2.PutInOrbit("host", 82, 8);
        Assert.Equal(0u, p2.Snapshot().OrbitedWorlds!.Single().EnergyTaken);
        p2.Story!.Set("FOUND_PLUTO_SPATHI", 1);
        Assert.Equal(uint.MaxValue, p2.Snapshot().OrbitedWorlds!.Single(w => w.Planet == pluto.Planet).EnergyTaken);
    }

    [ContentFact]
    public void AnEggCaseTakenIsGoneForEveryoneAndItsWorldHasNoReportAfter()
    {
        var world = NewGame();
        int star = Star(world, "EGG_CASE1_DEFINED");
        var at = new WorldId(star, 0, null);
        world.PutInOrbit("host", star, 0);
        Land(world, "host", 10, 10);
        DriveOnto(world, "host", Node(world, at));
        Assert.Contains(DeviceOf("EGG_CASE1_ON_SHIP"), world.Host.Ship.Hold.Devices);
        // Coming into orbit afterwards: no place in its report (genmyc.c loads it only while the node's there).
        world.PutInOrbit("guest", star, 0);
        Assert.Null(world.Find("guest")!.Ship.Discovery);
        Assert.Equal(1u, world.Snapshot().OrbitedWorlds!.Single(w => w.Star == star).EnergyTaken);
    }

    [ContentFact]
    public void ADeviceAShipHoldsAlreadyIsntGivenTwice()
    {
        // --start-devices puts the Maidens aboard the host's ship without SHOFIXTI_MAIDENS: their node is still
        // there. A guest taking it gets the story and the report, not a second Maidens.
        var session = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host", Systems,
            TestContent.Shapes);
        var world = session.World;
        world.EncounterRaces = new HashSet<string>();
        world.Clock.FramesIntoDay = -100000;
        Assert.Equal(JoinResult.Joined, world.Join("guest", "guest"));
        world.UndockShown("guest");
        session.StartWithDevices(new[] { Rules.Devices.All[DeviceOf("MAIDENS_ON_SHIP")].Name });
        Assert.Contains(DeviceOf("MAIDENS_ON_SHIP"), world.Host.Ship.Hold.Devices);
        int star = Star(world, "MAIDENS_DEFINED");
        var at = new WorldId(star, 0, null);
        world.PutInOrbit("guest", star, 0);
        Land(world, "guest", 10, 10);
        DriveOnto(world, "guest", Node(world, at));
        Assert.Equal(1, world.Story!.Get("SHOFIXTI_MAIDENS"));
        Assert.DoesNotContain(DeviceOf("MAIDENS_ON_SHIP"), world.Find("guest")!.Ship.Hold.Devices);
        Assert.Contains(DeviceOf("MAIDENS_ON_SHIP"), world.Host.Ship.Hold.Devices);
        // And so with it in a leaver's container.
        var again = NewGame(guests: 2);
        again.Find("g1")!.Ship.Hold.AddDevice(DeviceOf("TAALO_PROTECTOR_ON_SHIP"));
        again.Leave("g1");
        Assert.Single(again.Containers);
        int taalo = Star(again, "TAALO_PROTECTOR_DEFINED");
        again.PutInOrbit("g2", taalo, 1, 2);
        Land(again, "g2", 10, 10);
        DriveOnto(again, "g2", Node(again, new WorldId(taalo, 1, 2)));
        Assert.Equal(1, again.Story!.Get("TAALO_PROTECTOR"));
        Assert.DoesNotContain(DeviceOf("TAALO_PROTECTOR_ON_SHIP"), again.Find("g2")!.Ship.Hold.Devices);
    }

    [Fact]
    public void TheLandingSiteCanStartWhereASetUpPutsIt()
    {
        // --start-site (testing: a script can't move the site): the scan map pixel given, in planetside units.
        var site = new LandingSite((80, 46));
        Assert.Equal((320, 184), (site.X, site.Y));
        Assert.Equal((121 << PlanetsideMap.MagShift, 37 << PlanetsideMap.MagShift), (new LandingSite().X, new LandingSite().Y));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandingSite((SurfaceMap.Width, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandingSite((0, -1)));
        var menu = new OrbitMenu(new WorldId(82, 2, 1), Systems[82].EnterOrbit(2, 1)) { SiteStart = (10, 20) };
        menu.Press(MenuKey.Select, _ => 0);   // Scan
        Assert.Equal((10, 20), menu.Site!.Cursor);
    }

    [ContentFact]
    public void TheClientShowsTheReportsEntry()
    {
        var table = TestContent.With(p => StringTable.Load(p, StoryWorlds.UmgahCasterReport));
        Assert.Equal(2, table.Count);
        IReadOnlyList<string> Tables(string id) => id == StoryWorlds.UmgahCasterReport ? new[] { table[0], table[1] } : Array.Empty<string>();
        var finder = new PlayerSession(Systems, null, "host", TestContent.Shapes.HyperFlagship.Count) { StringTables = Tables };
        finder.ShowReport(new LanderReport(StoryWorlds.UmgahCasterReport, 1));
        Assert.Equal(table[1], finder.TakeOwnReport());
        finder.ShowReport(new LanderReport(StoryWorlds.UmgahCasterReport));
        Assert.Equal(table[0], finder.TakeOwnReport());
        // Told another's: the entry they found.
        var other = new PlayerSession(Systems, null, "guest", TestContent.Shapes.HyperFlagship.Count) { StringTables = Tables };
        other.ShowReport(new LanderReport(StoryWorlds.UmgahCasterReport, 1, "host"));
        Assert.StartsWith("host's lander found it:\n" + table[1][..20], other.Notice);
        // An entry the table hasn't (a bad one from the wire) shows nothing.
        finder.ShowReport(new LanderReport(StoryWorlds.UmgahCasterReport, 2));
        Assert.Equal("", finder.TakeOwnReport());
    }
}
