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
            hold.LanderLeaves(hold.CrewForLander);
            hold.LanderReturns(12, Minerals((0, 100)), 0);
        }
        hold.LanderLeaves(12);
        hold.LanderReturns(12, Minerals((7, 70)), 3);
        Assert.Equal((470, 30), (hold.MineralMass, hold.FreeStorage));
        Assert.Equal(30, hold.LanderMineralCapacity);
        Assert.Equal(new[] { 400, 0, 0, 0, 0, 0, 0, 70 }, hold.Minerals);
        Assert.Equal(3, hold.BioData);
        hold.LanderLeaves(12);
        Assert.Throws<InvalidOperationException>(() => hold.LanderReturns(12, Minerals((1, 31)), 0));
        // No storage bay: nothing fits.
        Assert.Equal(0, new ShipHold(new ShipLoadout(2, 2, 1, 0, 50, 1000, StorageBays: 0)).LanderMineralCapacity);
    }

    [Fact]
    public void ALanderTakesUpToTwelveCrewAndIsLostWithThemAll()
    {
        var hold = new ShipHold(new ShipLoadout(2, 2, 1, 0, 14, 1000, Landers: 2));
        Assert.Equal(12, hold.CrewForLander);
        hold.LanderLeaves(12);
        Assert.Equal(2, hold.Crew);
        Assert.Equal(2, hold.CrewForLander);
        // Five come back with what they found.
        hold.LanderReturns(5, Minerals((2, 20)), 4);
        Assert.Equal((7, 2, 20, 4), (hold.Crew, hold.Landers, hold.MineralMass, hold.BioData));
        // All die: the lander and what it carried are lost.
        hold.LanderLeaves(7);
        hold.LanderReturns(0, Minerals((2, 50)), 9);
        Assert.Equal((0, 1, 20, 4), (hold.Crew, hold.Landers, hold.MineralMass, hold.BioData));
        Assert.Equal(0, hold.CrewForLander);
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.LanderLeaves(1));
        hold.LanderLeaves(0);
        hold.LanderReturns(0, Minerals(), 0);
        Assert.Throws<InvalidOperationException>(() => hold.LanderLeaves(0));   // no lander left
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShipHold(new ShipLoadout(2, 2, 1, 0, 1, 0, Landers: 11)));
    }

    [Fact]
    public void TheHoldIsSavedAndSent()
    {
        var world = NewWorld();
        var hold = world.Host.Ship.Hold;
        hold.Upgrades = LanderUpgrades.Speed | LanderUpgrades.LavaShield;
        hold.LanderLeaves(12);
        hold.LanderReturns(9, Minerals((3, 33), (5, 6)), 7);

        var loaded = HostSave.FromJson(HostSave.ToJson(world)).Host.Ship.Hold;
        Assert.Equal((47, 1, 39, 7, hold.Upgrades), (loaded.Crew, loaded.Landers, loaded.MineralMass, loaded.BioData, loaded.Upgrades));
        Assert.Equal(hold.Minerals, loaded.Minerals);

        var sent = Protocol.Decode<WorldSnapshot>(Protocol.Encode(world.Snapshot())).Ships[0].Hold!;
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
    public void ABadHoldIsRefused(string field, int value)
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        var hold = node["players"]![0]!["hold"]!;
        if (field == "minerals")
            hold["minerals"] = new JsonArray(value, 0, 0, 0, 0, 0, 0, 0);
        else
            hold[field] = value;
        var e = Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
        Assert.Contains("host", e.Message);
    }
}
