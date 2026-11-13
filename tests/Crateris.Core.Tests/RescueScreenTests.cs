// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Xunit;

namespace Crateris.Core.Tests;

/// <summary>
/// The rescue screen (stage 4a item 12): opened as a rescue is offered,
/// choosing the wreck among those in the same place and the crew within
/// what may be given, sending the revival and closing; and the session's
/// battle and game over screens.
/// </summary>
[Trait("Topic", "combat")]
public class RescueScreenTests
{
    // A ship in hyperspace (or a system's outer view), with its crew and kit crew (its crew capacity, no pods).
    private static ShipSnapshot Ship(string id, int crew, int room, bool wreck = false, int? star = null) =>
        new ShipSnapshot(id, id, false, ShipKind.Escort, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false,
            star is null ? Activity.Hyperspace : Activity.OuterSystem, star, null, 0, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0)
        {
            Wrecked = wreck,
            Hold = new HoldSnapshot(crew, 0, 0, new int[ShipHold.Categories], 0),
            ShipName = id + "'s ship",
            CaptainName = "Captain " + id,
            Modules = new ShipModules(Enumerable.Repeat(ShipModules.EmptyDrive, ShipModules.DriveSlotCount).ToArray(),
                Enumerable.Repeat(ShipModules.EmptyJet, ShipModules.JetSlotCount).ToArray(),
                Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray(), room).Save(),
        };

    private static WorldSnapshot World(long frame, params ShipSnapshot[] ships) => new(frame, 2155, 1, 1, ships);

    [ContentFact]
    public void ARescueOfferOpensTheScreenWhichGivesCrewAndCloses()
    {
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "me", TestContent.Shapes.HyperFlagship.Count);
        var world = World(1, Ship("me", 10, 0), Ship("w1", 0, 6, wreck: true), Ship("w2", 0, 20, wreck: true),
            Ship("far", 0, 20, wreck: true, star: 3));
        session.Observe(world);
        Assert.Null(session.Rescue);
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.RescueOffer, Other: "w1"));
        var rescue = Assert.IsType<RescueScreen>(session.Rescue);
        Assert.Equal(new[] { "w1", "w2" }, RescueScreen.WrecksHere(world, "me").Select(s => s.Id));   // not the one in a system

        // The ship waits while it's open.
        Assert.Equal(ShipInput.None, session.NextInput(ShipInput.Thrust));

        var shown = rescue.Follow(world, "me");
        Assert.Equal((RescueScreen.CrewRow, "w1", "w1's ship", "Captain w1", 2, 1, 6, 10, 6),
            (shown.Row, shown.Wreck, shown.WreckName, shown.CaptainName, shown.WrecksHere, shown.Crew, shown.Most, shown.Have, shown.Room));
        // Up to what the wreck holds.
        session.PressMenu(MenuKey.PageUp);
        Assert.Equal(6, rescue.Crew);
        // The other wreck: room for 20, but the rescuer keeps 1 of its 10.
        session.PressMenu(MenuKey.Up);
        session.PressMenu(MenuKey.Right);
        Assert.Equal("w2", rescue.Wreck);
        session.PressMenu(MenuKey.Down);
        session.PressMenu(MenuKey.PageUp);
        Assert.Equal(9, rescue.Follow(world, "me").Most);
        Assert.Equal(9, rescue.Crew);
        session.PressMenu(MenuKey.Left);
        session.PressMenu(MenuKey.Select);
        Assert.Equal(("w2", 8), session.TakeRevive());
        Assert.Null(session.Rescue);   // given: closed (a refusal comes as a notice)

        // Esc closes it; it opens again at the next offer.
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.RescueOffer, Other: "w1"));
        Assert.NotNull(session.Rescue);
        session.PressMenu(MenuKey.Cancel);
        Assert.Null(session.Rescue);
        Assert.Null(session.TakeRevive());
    }

    [ContentFact]
    public void TheRescueScreenClosesWithNothingLeftToRescue()
    {
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "me", TestContent.Shapes.HyperFlagship.Count);
        session.Observe(World(1, Ship("me", 10, 0), Ship("w1", 0, 6, wreck: true)));
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.RescueOffer, Other: "w1"));
        Assert.NotNull(session.Rescue);
        session.Observe(World(2, Ship("me", 10, 0), Ship("w1", 3, 6)));   // revived by another
        Assert.Null(session.Rescue);
        // Nor does giving open over it.
        session.Observe(World(3, Ship("me", 10, 0), Ship("w1", 0, 6, wreck: true)));
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.RescueOffer, Other: "w1"));
        session.ToggleTrade();
        Assert.Null(session.Trade);
        Assert.Equal("Close the rescue first.", session.Notice);
        // The game over closes it, and is the screen.
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.GameOver));
        Assert.Null(session.Rescue);
        Assert.Equal(PlayerScreen.GameOver, session.Screen);
    }

    [ContentFact]
    public void APlayerInABattleSeesIt()
    {
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "me", TestContent.Shapes.HyperFlagship.Count);
        var me = Ship("me", 10, 0) with { Activity = Activity.Battle };
        session.Observe(World(1, me));
        Assert.Equal(PlayerScreen.Hyperspace, session.Screen);   // its battle not sent yet: the place's
        var status = new BattleShipStatus("sis_ship", 10, 10, 5, 10);
        var battle = new BattleSnapshot(1, 8192, 7680, status with { Ship = "ilwrath" }, 0, 0,
            new[] { new BattleFighterSnapshot("me", status, 100, 100, true) }, Array.Empty<BattleElementSnapshot>());
        session.Observe(World(2, me) with { Battles = new[] { battle } });
        Assert.Equal(PlayerScreen.Battle, session.Screen);
    }

    [Fact]
    public void TheRescueScreenIsDrawnInTheStatusArea()
    {
        var canvas = new UqmCanvas(_ => 1, (_, _) => default, new Dictionary<string, int> { ["starcon"] = 11, ["tiny"] = 10 });
        RescueDrawing.Draw(canvas, new RescueShown(RescueScreen.CrewRow, "w1", "Wayfarer", "Captain Q", 2, 4, 9, 10, 20), s => s.Length * 4);
        Assert.All(canvas.Drawn, d => Assert.Equal(canvas.Status.Clip!.Value, d.Clip));
        var texts = canvas.Drawn.Where(d => d.Kind == UqmDrawKind.Text).Select(d => d.Text).ToList();
        Assert.Equal(new[] { RescueDrawing.Title, "<Wayfarer>", "Captain Q", "Have", "10", "Room", "20", "Crew", "4" }, texts);
        Assert.All(canvas.Drawn, d => Assert.True(d.Y >= RescueDrawing.Area.Y && d.Y < RescueDrawing.Area.Y + RescueDrawing.Area.H,
            $"{d} outside the box"));
    }
}
