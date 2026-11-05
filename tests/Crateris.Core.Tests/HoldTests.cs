// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Data;
using Crateris.Core.Net;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// Each ship's landing kit and hold (stage 2b, item 1): crew, landers,
/// storage, minerals and bio data, with UQM's limits, sent and saved.
/// </summary>
[Trait("Topic", "world")]
public class HoldTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static GameWorld NewWorld() =>
        GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");

    private static int[] Minerals(params (int Category, int Amount)[] amounts)
    {
        var m = new int[ShipHold.Categories];
        foreach (var (c, a) in amounts)
            m[c] = a;
        return m;
    }

    [Fact]
    public void ANewFlagshipAndAnEscortHaveUqmsLandingKit()
    {
        // InitGameStructures: 50 crew in one crew pod, one lander, one storage bay.
        var world = NewWorld();
        var flagship = world.Host.Ship.Hold;
        Assert.Equal((50, 1, 500, 0, 0), (flagship.Crew, flagship.Landers, flagship.StorageCapacity,
            flagship.MineralMass, flagship.BioData));
        Assert.Equal(LanderUpgrades.None, flagship.Upgrades);
        world.Join("guest", "Guest");
        var escort = world.Find("guest")!.Ship.Hold;
        Assert.Equal((10, 1, 500), (escort.Crew, escort.Landers, escort.StorageCapacity));
    }

    [Fact]
    public void ALanderHoldsFiftyOrAHundredAndNoMoreThanTheBaysHaveRoomFor()
    {
        var hold = new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000));
        Assert.Equal(50, hold.LanderMineralCapacity);
        hold.Upgrades = LanderUpgrades.Cargo;
        Assert.Equal(100, hold.LanderMineralCapacity);
        // Fill the bay to 470: only 30 more fit.
        for (int i = 0; i < 4; ++i)
        {
            hold.LanderLeaves();
            hold.LanderReturns(12, Minerals((0, 100)), 0);
        }
        hold.LanderLeaves();
        hold.LanderReturns(12, Minerals((7, 70)), 3);
        Assert.Equal((470, 30), (hold.MineralMass, hold.FreeStorage));
        Assert.Equal(30, hold.LanderMineralCapacity);
        Assert.Equal(new[] { 400, 0, 0, 0, 0, 0, 0, 70 }, hold.Minerals);
        Assert.Equal(3, hold.BioData);
        // A lander brings back no more than it had room for when it left, and 50 bio data.
        hold.LanderLeaves();
        Assert.Equal((12, 30), hold.LanderOut);
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.LanderReturns(12, Minerals((1, 31)), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.LanderReturns(12, Minerals(), 51));
        hold.LanderReturns(12, Minerals((1, 30)), 50);
        Assert.Equal((500, 53), (hold.MineralMass, hold.BioData));
        // Without the upgrade, 50 at most however much room there is.
        var plain = new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000));
        plain.LanderLeaves();
        Assert.Throws<ArgumentOutOfRangeException>(() => plain.LanderReturns(12, Minerals((0, 400)), 0));
        // No storage bay: nothing fits.
        Assert.Equal(0, new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000, StorageBays: 0)).LanderMineralCapacity);
    }

    [Fact]
    public void ALanderTakesUpToTwelveCrewAndIsLostWithThemAll()
    {
        var hold = new ShipHold(new ShipLoadout(2, 2, 1, 0, 14, 1000, Landers: 2));
        Assert.Equal(12, hold.LanderLeaves());
        Assert.Equal(2, hold.Crew);
        Assert.Throws<InvalidOperationException>(() => hold.LanderLeaves());   // one at a time
        // Five come back with what they found; no more than went down.
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.LanderReturns(13, Minerals(), 0));
        hold.LanderReturns(5, Minerals((2, 20)), 4);
        Assert.Equal((7, 2, 20, 4), (hold.Crew, hold.Landers, hold.MineralMass, hold.BioData));
        Assert.Null(hold.LanderOut);
        Assert.Throws<InvalidOperationException>(() => hold.LanderReturns(5, Minerals(), 0));   // none is out
        // All die: the lander and what it carried are lost.
        Assert.Equal(7, hold.LanderLeaves());
        hold.LanderReturns(0, Minerals((2, 50)), 9);
        Assert.Equal((0, 1, 20, 4), (hold.Crew, hold.Landers, hold.MineralMass, hold.BioData));
        // No crew left: no lander goes down.
        Assert.Throws<InvalidOperationException>(() => hold.LanderLeaves());
        // No lander aboard: none goes down, so none can be lost.
        var none = new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000, Landers: 0));
        Assert.Throws<InvalidOperationException>(() => none.LanderLeaves());
        Assert.Throws<InvalidOperationException>(() => none.LanderReturns(0, Minerals(), 0));
        Assert.Equal(0, none.Landers);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipHold(new ShipLoadout(2, 2, 1, 0, 1, 0, Landers: 11)));
    }

    [Fact]
    public void ALanderOutIsSavedAsBackAboardWithItsCrew()
    {
        var world = NewWorld();
        world.Host.Ship.Hold.LanderLeaves();
        var loaded = HostSave.FromJson(HostSave.ToJson(world)).Host.Ship.Hold;
        Assert.Equal((50, 1), (loaded.Crew, loaded.Landers));
        Assert.Null(loaded.LanderOut);
    }

    [Fact]
    public void TheSnapshotIsMadeAgainOnlyWhenTheHoldChanges()
    {
        var hold = new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000));
        var first = hold.Snapshot();
        Assert.Same(first, hold.Snapshot());
        hold.LanderLeaves();
        var out_ = hold.Snapshot();
        Assert.NotSame(first, out_);
        Assert.Equal(38, out_.Crew);
        hold.LanderReturns(12, Minerals((4, 8)), 0);
        Assert.Equal(8, hold.Snapshot().MineralMass);
        hold.Upgrades = LanderUpgrades.Shot;
        Assert.Equal(LanderUpgrades.Shot, hold.Snapshot().Upgrades);
    }

    [Fact]
    public void TheHoldIsSavedAndSent()
    {
        var world = NewWorld();
        var hold = world.Host.Ship.Hold;
        hold.Upgrades = LanderUpgrades.Speed | LanderUpgrades.LavaShield;
        hold.LanderLeaves();
        hold.LanderReturns(9, Minerals((3, 33), (5, 6)), 7);

        var loaded = HostSave.FromJson(HostSave.ToJson(world)).Host.Ship.Hold;
        Assert.Equal((47, 1, 39, 7, hold.Upgrades), (loaded.Crew, loaded.Landers, loaded.MineralMass, loaded.BioData, loaded.Upgrades));
        Assert.Equal(hold.Minerals, loaded.Minerals);

        var writer = new WireWriter();
        SnapshotCodec.Encode(writer, world.Snapshot());
        var sent = SnapshotCodec.Decode(writer.Written).Ships[0].Hold!;
        Assert.Equal((47, 1, 500, 39, 7, hold.Upgrades), (sent.Crew, sent.Landers, sent.StorageCapacity,
            sent.MineralMass, sent.BioData, sent.Upgrades));
        Assert.Equal(hold.Minerals, sent.Minerals);
    }

    [Fact]
    public void ASaveFromBeforeHoldsLoadsWithItsCrewOneLanderAndOneBay()
    {
        var world = NewWorld();
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 4;
        foreach (var p in node["players"]!.AsArray())
        {
            p!.AsObject().Remove("hold");
            p["loadout"]!.AsObject().Remove("landers");
            p["loadout"]!.AsObject().Remove("storage_bays");
        }
        node["settings"]!["escort_loadout"]!.AsObject().Remove("landers");
        var loaded = HostSave.FromJson(node.ToJsonString());
        var hold = loaded.Host.Ship.Hold;
        Assert.Equal((50, 1, 500, 0, 0), (hold.Crew, hold.Landers, hold.StorageCapacity, hold.MineralMass, hold.BioData));
        Assert.Equal(1, loaded.Settings.EscortLoadout.Landers);
    }

    [Theory]
    [InlineData("crew", -1)]
    [InlineData("landers", 11)]
    [InlineData("bio_data", -3)]
    [InlineData("upgrades", 128)]
    [InlineData("minerals", 501)]
    [InlineData("no minerals", 0)]
    public void ABadHoldIsRefused(string field, int value)
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        var hold = node["players"]![0]!["hold"]!;
        if (field == "minerals")
            hold["minerals"] = new JsonArray(value, 0, 0, 0, 0, 0, 0, 0);
        else if (field == "no minerals")
            hold.AsObject().Remove("minerals");
        else
            hold[field] = value;
        var e = Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
        Assert.Contains("host", e.Message);
        if (field == "no minerals")
            Assert.Contains("has no minerals", e.Message);
    }

    [Theory]
    [InlineData("landers", 11)]
    [InlineData("storage_bays", -1)]
    [InlineData("crew", -5)]
    public void ABadEscortLoadoutIsRefusedWhenTheSaveLoads(string field, int value)
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        node["settings"]!["escort_loadout"]![field] = value;
        var e = Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
        Assert.Contains("escort loadout", e.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameWorld.NewGame(
            new GameSettings { EscortLoadout = new GameSettings().EscortLoadout with { Landers = 11 } },
            NewGameData.Load(DataDir), "host", "Host"));
    }
}
