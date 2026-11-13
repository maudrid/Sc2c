// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.World;
using Xunit;
using static Crateris.Core.Tests.WorldBattleTests;

namespace Crateris.Core.Tests;

/// <summary>
/// Battles on the wire (docs/plans/stage-4.md, item 11): each player's
/// battle in its snapshot, the markers, the battle notices sent reliably,
/// a guest's Special key and its revival request.
/// </summary>
[Trait("Topic", "combat")]
public class BattleWireTests
{
    private static HostSession Host()
    {
        var host = HostSession.New(new GameSettings(), NewGameData.Load(DataDir, WorldBattleTests.Systems.Value.Map), "host", "Host",
            WorldBattleTests.Systems.Value, TestContent.Shapes);
        host.World.BattleKit = Kit();
        host.StartBattle(Ilwrath);
        host.Hello(1, "g1", "Guest 1", Protocol.Version);
        host.Hello(2, "g2", "Guest 2", Protocol.Version);
        return host;
    }

    [ContentFact]
    public void EachPlayerIsSentItsOwnBattleAndEveryoneTheMarkers()
    {
        var host = Host();
        var wb = WreckTests.JoinedInSol(host, "g1");
        var last = host.Step(ShipInput.None);
        var g1 = SnapshotCodec.Decode(host.SnapshotFor(1)!);
        var g2 = SnapshotCodec.Decode(host.SnapshotFor(2)!);
        var seen = Assert.Single(g1.Battles!);
        Assert.Equal(wb.Id, seen.Id);
        Assert.Equal(new[] { "host", "g1" }, seen.Fighters.Select(f => f.PlayerId));
        Assert.Equal((Ilwrath, 22), (seen.Enemy.Ship, seen.Enemy.MaxCrew));
        var mine = seen.FighterOf("g1")!;
        var ship = wb.FighterOf("g1")!.Starship.Ship!;
        Assert.Equal((ship.Current.X, ship.Current.Y), (mine.X, mine.Y));
        Assert.Contains(seen.Elements, e => e.Prim == Combat.PrimType.Stamp && e.Sheet.Contains("flagship") && (e.X, e.Y) == (mine.X, mine.Y));
        // The world's battle holds every element; g1 is sent those near its ship.
        Assert.Equal(last.Battles!.Single().SeenBy("g1").Elements, seen.Elements);
        Assert.Null(g2.Battles);   // by the marker, not in it (its message isn't g1's)
        Assert.Equal(new BattleMarkerSnapshot(wb.Id, wb.Place.Star, wb.Place.InnerPlanet, wb.Place.X, wb.Place.Y),
            Assert.Single(g2.Markers!));
        // The status lines say it, until item 12 draws it.
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "g1", TestContent.Shapes.HyperFlagship.Count);
        session.Observe(g1);
        Assert.Same(g1.Battles![0], session.Battle);
        Assert.Contains(session.StatusLines(), l => l.StartsWith($"In battle against the {Ilwrath}: crew "));
    }

    [ContentFact]
    public void ABattlesStartEndAndWrecksAreToldToItsPlayers()
    {
        var host = Host();
        var wb = WreckTests.JoinedInSol(host, "g1");
        var (own, guests) = host.TakeBattleNotices();
        Assert.Equal(new BattleNotice(BattleNoticeKind.Joined, wb.Id, Ilwrath), Assert.Single(own));
        Assert.Equal((1L, new BattleNotice(BattleNoticeKind.Joined, wb.Id, Ilwrath)), Assert.Single(guests));
        // g1 destroyed, then the Ilwrath: won by the host, g1 a wreck.
        Kill(wb.Battle, wb.FighterOf("g1")!.Starship);
        for (int i = 0; i < 60; ++i)
        {
            wb.Enemy.WeaponCounter = wb.Enemy.SpecialCounter = 2;   // disarmed: nothing it does decides the test
            host.Step(ShipInput.None);
        }
        Assert.Null(wb.Outcome);
        Kill(wb.Battle, wb.Enemy);
        for (int i = 0; i < 600 && wb.Outcome is null; ++i)
            host.Step(ShipInput.None);
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
        (own, guests) = host.TakeBattleNotices();
        var hostEnd = Assert.Single(own);
        Assert.Equal((BattleNoticeKind.Ended, BattleOutcome.Won, BattleExit.Fought), (hostEnd.Kind, hostEnd.Outcome, hostEnd.Exit));
        Assert.Equal(wb.FighterOf("host")!.Salvage, hostEnd.Salvage);
        Assert.Equal(new[] { BattleNoticeKind.Ended, BattleNoticeKind.Wrecked }, guests.Select(g => g.Notice.Kind));
        Assert.All(guests, g => Assert.Equal(1L, g.Peer));
        Assert.Equal(BattleExit.Destroyed, guests[0].Notice.Exit);
        // Nothing new is told again.
        host.Step(ShipInput.None);
        Assert.Empty(host.TakeBattleNotices().Guests);
    }

    [ContentFact]
    public void TheGameOverIsToldToEveryone()
    {
        var host = Host();
        var wb = WreckTests.JoinedInSol(host, "g1");
        host.TakeBattleNotices();
        Kill(wb.Battle, wb.FighterOf("host")!.Starship);
        host.Step(ShipInput.None);
        host.GuestLeft(1);
        host.Step(ShipInput.None);
        Assert.True(host.World.GameOver);
        var (own, guests) = host.TakeBattleNotices();
        Assert.Contains(own, n => n.Kind == BattleNoticeKind.GameOver);
        Assert.Equal(new[] { (2L, BattleNoticeKind.GameOver) }, guests.Select(g => (g.Peer, g.Notice.Kind)));
        host.Step(ShipInput.None);
        Assert.Empty(host.TakeBattleNotices().Host);   // once
    }

    [Fact]
    public void NoticesRoundTripAndBadOnesAreRefused()
    {
        var notices = new[]
        {
            new BattleNotice(BattleNoticeKind.Joined, 4, "ilwrath"),
            new BattleNotice(BattleNoticeKind.Ended, 4, "ilwrath", BattleOutcome.Won, BattleExit.Fought, 125, 30),
            new BattleNotice(BattleNoticeKind.Ended, 4, "ilwrath", BattleOutcome.Lost, BattleExit.Disconnected, 0, 7),
            new BattleNotice(BattleNoticeKind.Wrecked),
            new BattleNotice(BattleNoticeKind.Full, Text: "The battle is too crowded to join just now."),
            new BattleNotice(BattleNoticeKind.RescueOffer, Other: "g2"),
            new BattleNotice(BattleNoticeKind.Revived, Crew: 5, Other: "g2", Text: BattleNotice.Rescuer),
            new BattleNotice(BattleNoticeKind.GameOver),
        };
        foreach (var n in notices)
        {
            Assert.Equal(n, BattleNotice.Decode(n.Encode()));
            Assert.NotEqual("", n.Describe());
        }
        var bytes = notices[1].Encode();
        Assert.Throws<InvalidDataException>(() => BattleNotice.Decode(bytes.Append((byte)0).ToArray()));
        Assert.ThrowsAny<Exception>(() => BattleNotice.Decode(bytes[..^1]));
        var bad = (byte[])bytes.Clone();
        bad[0] = 99;
        Assert.Throws<InvalidDataException>(() => BattleNotice.Decode(bad));
    }

    [ContentFact]
    public void ASessionKeepsTheRescueOfferAndTheGameOverAndAGuestsSpecialKeyGoesThrough()
    {
        var session = new PlayerSession(WorldBattleTests.Systems.Value, null, "g1", TestContent.Shapes.HyperFlagship.Count);
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.RescueOffer, Other: "g2"));
        Assert.Equal("g2", session.RescueOffer);
        Assert.Equal("You're at g2's wreck: you can give it crew.", session.Notice);
        session.Revive("g2", 3);
        Assert.Equal(("g2", 3), session.TakeRevive());
        Assert.Null(session.TakeRevive());
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.Revived, Crew: 3, Other: "g2", Text: BattleNotice.Rescuer));
        Assert.Null(session.RescueOffer);
        session.RescueRefused(RescueRefusal.NotHere);
        Assert.Equal("You aren't at the wreck.", session.Notice);
        session.ShowBattleNotice(new BattleNotice(BattleNoticeKind.GameOver));
        Assert.True(session.GameOver);
        Assert.Equal(3, session.BattleNotices.Count);

        // --script's special, and a guest's Special through the host.
        Assert.Equal(ShipInput.Special | ShipInput.Fire, InputScript.Parse("special+fire:1").Steps[0].Input);
        var host = Host();
        var wb = WreckTests.JoinedInSol(host, "g1");
        var ilwrathCrew = wb.Enemy.RaceDesc!.CrewLevel;
        host.GuestInput(1, host.World.Frame + 1, (int)ShipInput.Special, 0);
        host.Step(ShipInput.None);
        Assert.NotEqual(0, (int)(wb.FighterOf("g1")!.Starship.ShipInputState & Combat.StatusFlags.Special));
        Assert.True(ilwrathCrew > 0);
    }
}
