// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>Taken nodes and scans: shared world state, kept in the host save.</summary>
public class PlanetWorldStateTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static GameWorld NewWorld() =>
        GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host-id", "Host");

    private static readonly WorldId Luna = new(82, 2, 1);
    private static readonly WorldId Earth = new(82, 2);

    [Fact]
    public void TakingANodeSetsItsBitLikeUqmsRetrieveMask()
    {
        var state = new PlanetWorldState();
        Assert.False(state.IsRetrieved(Luna, ScanType.Mineral, 3));

        state.SetRetrieved(Luna, ScanType.Mineral, 3);
        state.SetRetrieved(Luna, ScanType.Mineral, 31);
        Assert.Equal((1u << 3) | (1u << 31), state.RetrieveMask(Luna, ScanType.Mineral));
        Assert.True(state.IsRetrieved(Luna, ScanType.Mineral, 31));
        Assert.Equal(2, state.CountRetrieved(Luna, ScanType.Mineral));

        // Other scan types and other worlds are separate.
        Assert.Equal(0u, state.RetrieveMask(Luna, ScanType.Energy));
        Assert.Equal(0u, state.RetrieveMask(Earth, ScanType.Mineral));
        Assert.Equal(0u, state.RetrieveMask(new WorldId(82, 2, 0), ScanType.Mineral));

        state.ClearRetrieved(Luna, ScanType.Mineral, 3);
        state.ClearRetrieved(Luna, ScanType.Mineral, 31);
        Assert.Empty(state.Worlds);   // nothing left to store
    }

    [Fact]
    public void NodeNumbersAndWorldsOutsideUqmsRangeAreRefused()
    {
        var state = new PlanetWorldState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetRetrieved(Luna, ScanType.Energy, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetRetrieved(Luna, ScanType.Energy, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetRetrieved(new WorldId(502, 0), ScanType.Energy, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetRetrieved(new WorldId(0, 16), ScanType.Energy, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetRetrieved(new WorldId(0, 0, 4), ScanType.Energy, 0));
    }

    [Fact]
    public void RemainingLeavesOutTakenNodesAndKeepsNodeNumbers()
    {
        var map = StarMap.Load(DataDir);
        var sol = new SystemGenerator(PlanetTypes.Load(DataDir)).Generate(map.At(1752, 1450)!);
        var nodes = sol.EnterOrbit(2, 1).Nodes!;   // Luna
        var state = new PlanetWorldState();

        state.SetRetrieved(Luna, ScanType.Biological, 0);
        state.SetRetrieved(Luna, ScanType.Biological, 4);
        var left = state.Remaining(Luna, ScanType.Biological, nodes.Life);
        Assert.Equal(nodes.Life.Count - 2, left.Count);
        Assert.DoesNotContain(left, n => n.Node is 0 or 4);
        Assert.All(left, n => Assert.Equal(nodes.Life[n.Node], n.Info));

        Assert.Equal(nodes.Minerals.Count, state.Remaining(Luna, ScanType.Mineral, nodes.Minerals).Count);
    }

    [Fact]
    public void EveryPlayerSeesTheSameWorldState()
    {
        var world = NewWorld();
        world.Join("guest", "Guest");
        world.Planets.SetRetrieved(Luna, ScanType.Energy, 0);   // anyone takes the Moon base node
        world.Planets.MarkScanned(Luna, ScanType.Mineral);

        // There is one world state for the game, not one per player.
        Assert.True(world.Planets.IsRetrieved(Luna, ScanType.Energy, 0));
        Assert.True(world.Planets.IsScanned(Luna, ScanType.Mineral));
        Assert.False(world.Planets.IsScanned(Luna, ScanType.Energy));
    }

    [Fact]
    public void TheHostSaveKeepsWorldState()
    {
        var world = NewWorld();
        world.Planets.SetRetrieved(Luna, ScanType.Mineral, 5);
        world.Planets.SetRetrieved(Luna, ScanType.Biological, 31);
        world.Planets.MarkScanned(Earth, ScanType.Energy);
        world.Planets.SetRetrieved(new WorldId(0, 3), ScanType.Energy, 15);

        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json);
        Assert.Equal(1u << 5, loaded.Planets.RetrieveMask(Luna, ScanType.Mineral));
        Assert.Equal(1u << 31, loaded.Planets.RetrieveMask(Luna, ScanType.Biological));
        Assert.True(loaded.Planets.IsScanned(Earth, ScanType.Energy));
        Assert.True(loaded.Planets.IsRetrieved(new WorldId(0, 3), ScanType.Energy, 15));
        Assert.Equal(3, loaded.Planets.Worlds.Count);
        Assert.Equal(json, HostSave.ToJson(loaded));   // stable order
    }

    [Fact]
    public void OlderSavesLoadWithNoWorldTouched()
    {
        var world = NewWorld();
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 2;
        node.AsObject().Remove("worlds");
        var loaded = HostSave.FromJson(node.ToJsonString());
        Assert.Empty(loaded.Planets.Worlds);
    }

    [Theory]
    [InlineData("{\"star\": 502, \"planet\": 0, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0, \"scanned\": 0}")]
    [InlineData("{\"star\": 0, \"planet\": 16, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0, \"scanned\": 0}")]
    [InlineData("{\"star\": 0, \"planet\": 0, \"moon\": 7, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0, \"scanned\": 0}")]
    [InlineData("{\"star\": 0, \"planet\": 0, \"moon\": null, \"mineral_taken\": 0, \"energy_taken\": 0, \"biological_taken\": 0, \"scanned\": 9}")]
    public void ASaveWithABadWorldIsRefused(string worldJson)
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        node["worlds"] = new JsonArray(JsonNode.Parse(worldJson));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void ASaveWithAWorldTwiceIsRefused()
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        const string w = "{\"star\": 0, \"planet\": 0, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0, \"scanned\": 0}";
        node["worlds"] = new JsonArray(JsonNode.Parse(w), JsonNode.Parse(w));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }
}
