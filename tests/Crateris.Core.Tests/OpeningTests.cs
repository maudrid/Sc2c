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
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// UQM's opening at Earth (docs/plans/stage-3.md, item 5): the Lunar base
/// on Luna (gensol.c), and the visit to the starbase that runs the
/// commander, the Ilwrath and the alliance (starbase.c's VisitStarBase).
/// </summary>
[Trait("Topic", "opening")]
public class OpeningTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int Sol = 82, Earth = 2, Luna = 1;
    private static readonly WorldId Moon = new(Sol, Earth, Luna);

    // The Lunar base's place on the planetside map (GenerateSol_generateEnergy, at MAG_SHIFT).
    private const int BaseX = SurfaceMap.Width * 3 / 4 << PlanetsideMap.MagShift, BaseY = SurfaceMap.Height / 4 << PlanetsideMap.MagShift;

    private static GameWorld NewWorld(bool guest = false)
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        if (guest)
        {
            Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
            world.UndockShown("guest");
        }
        return world;
    }

    private static void Run(GameWorld world, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, ShipInput.None);
            world.Step();
        }
    }

    // Lands and drives a player's lander onto the Lunar base (it comes down
    // a little off its site, as UQM's does), until its report shows.
    private static void DriveToTheBase(GameWorld world, string id)
    {
        var lander = world.Find(id)!.Ship.Lander!;
        for (int i = 0; i < 2000 && lander.Report is null; ++i)
        {
            ShipInput keys = ShipInput.None;
            if (lander.Phase == LanderPhase.Ground)
            {
                int dx = BaseX - lander.X, dy = BaseY - lander.Y;
                int want = Uqm.UqmMath.AngleToFacing(Uqm.UqmMath.ArcTan((short)dx, (short)dy));
                int turn = (want - lander.Facing) & (Uqm.UqmMath.Facings - 1);
                keys = turn == 0 ? ShipInput.Thrust : turn > Uqm.UqmMath.Facings / 2 ? ShipInput.Left : ShipInput.Right;
            }
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, p.Id == id ? keys : ShipInput.None);
            world.Step();
        }
    }

    [ContentFact]
    public void TheLunarBaseIsOnLunaUntilTaken()
    {
        var world = NewWorld();
        world.PutInOrbit("host", Sol, Earth, Luna);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 10, 10));
        var surface = world.SurfaceOf(Moon)!;
        var node = Assert.Single(surface.Elements, e => e.Kind == SurfaceElementKind.Energy);
        Assert.Equal((BaseX, BaseY, SurfaceSheet.Moonbase, ScanType.Energy, 0), (node.X, node.Y, node.Sheet, node.Scan, node.Node));
        // GeneratePlanetSide's order: the creatures, the base, the minerals.
        var kinds = surface.Elements.Select(e => e.Kind).ToList();
        Assert.True(kinds.LastIndexOf(SurfaceElementKind.Creature) < kinds.IndexOf(SurfaceElementKind.Energy)
            && kinds.IndexOf(SurfaceElementKind.Energy) < kinds.IndexOf(SurfaceElementKind.Mineral));
        // It animates through its sheet as a creature does, a frame every five.
        int before = node.Frame;
        Run(world, 10);
        Assert.NotEqual(before, node.Frame);
    }

    [ContentFact]
    public void TakingTheLunarBaseShowsItsReportAndSendsTheLanderUp()
    {
        var world = NewWorld();
        world.PutInOrbit("host", Sol, Earth, Luna);
        var hold = world.Host.Ship.Hold;
        int crew = hold.Crew;
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", BaseX, BaseY));
        var lander = world.Host.Ship.Lander!;
        DriveToTheBase(world, "host");
        Assert.Equal(new LanderReport(StoryWorlds.MoonbaseReport), lander.Report);
        Assert.Equal((1, 1), (world.Story!.Get("MOONBASE_DESTROYED"), world.Story.Get("MOONBASE_ON_SHIP")));
        Assert.True(world.Planets.IsRetrieved(Moon, ScanType.Energy, 0));
        // It goes up by itself and comes back aboard with its crew.
        for (int i = 0; i < 400 && world.Host.Ship.Lander is not null; ++i)
            Run(world, 1);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Equal(crew, hold.Crew);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        // The next landing finds no base.
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", 10, 10));
        Assert.DoesNotContain(world.SurfaceOf(Moon)!.Elements, e => e.Kind == SurfaceElementKind.Energy);
    }

    [ContentFact]
    public void AnotherLanderOnLunaStaysAndIsTold()
    {
        var world = NewWorld(guest: true);
        world.PutInOrbit("host", Sol, Earth, Luna);
        world.PutInOrbit("guest", Sol, Earth, Luna);
        Assert.Equal(DispatchRefusal.None, world.Dispatch("guest", 40, 40));
        Assert.Equal(DispatchRefusal.None, world.Dispatch("host", BaseX, BaseY));
        var host = world.Host.Ship.Lander!;
        var guest = world.Find("guest")!.Ship.Lander!;
        DriveToTheBase(world, "host");
        Assert.Equal(new LanderReport(StoryWorlds.MoonbaseReport, "host"), guest.Report);
        Run(world, 100);
        Assert.Null(world.Host.Ship.Lander);
        Assert.Equal(LanderPhase.Ground, guest.Phase);
    }
}
