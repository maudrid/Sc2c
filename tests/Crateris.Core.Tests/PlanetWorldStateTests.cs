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

/// <summary>Taken nodes: shared world state, kept in the host save.</summary>
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
        var map = StarMap.Load(DataDir);
        var sol = new SystemGenerator(PlanetTypes.Load(DataDir)).Generate(map.At(1752, 1450)!);
        var life = sol.EnterOrbit(2, 1).Nodes!.Life;   // Luna's Brainbox Bulldozers
        int before = world.Planets.Remaining(Luna, ScanType.Biological, life).Count;

        // One player takes a node; anyone scanning afterwards sees one fewer.
        // There is one world state for the game, not one per player.
        world.Planets.SetRetrieved(Luna, ScanType.Biological, 2);
        Assert.Equal(before - 1, world.Planets.Remaining(Luna, ScanType.Biological, life).Count);
    }

    [Fact]
    public void TheHostSaveKeepsWorldState()
    {
        var world = NewWorld();
        world.Planets.SetRetrieved(Luna, ScanType.Mineral, 5);
        world.Planets.SetRetrieved(Luna, ScanType.Biological, 31);
        world.Planets.SetRetrieved(new WorldId(0, 3), ScanType.Energy, 15);

        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json);
        Assert.Equal(1u << 5, loaded.Planets.RetrieveMask(Luna, ScanType.Mineral));
        Assert.Equal(1u << 31, loaded.Planets.RetrieveMask(Luna, ScanType.Biological));
        Assert.True(loaded.Planets.IsRetrieved(new WorldId(0, 3), ScanType.Energy, 15));
        Assert.Equal(2, loaded.Planets.Worlds.Count);
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
    [InlineData("{\"star\": 502, \"planet\": 0, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}")]
    [InlineData("{\"star\": 0, \"planet\": 16, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}")]
    [InlineData("{\"star\": 0, \"planet\": 0, \"moon\": 7, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}")]
    public void ASaveWithABadWorldIsRefused(string worldJson)
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        node["worlds"] = new JsonArray(JsonNode.Parse(worldJson));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void AVersion3SaveWrittenWithScanFlagsStillLoads()
    {
        // The first item 8 build also saved "scanned" per world (withdrawn).
        var world = NewWorld();
        world.Planets.SetRetrieved(Luna, ScanType.Mineral, 1);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 3;
        foreach (var w in node["worlds"]!.AsArray())
            w!["scanned"] = 5;
        var loaded = HostSave.FromJson(node.ToJsonString());
        Assert.True(loaded.Planets.IsRetrieved(Luna, ScanType.Mineral, 1));
    }

    [Fact]
    public void WithItsSystemsOnlyRealWorldsAreAccepted()
    {
        var systems = SystemCatalog.Load(DataDir);
        var state = new PlanetWorldState(systems);
        state.SetRetrieved(Luna, ScanType.Mineral, 0);                       // Sol has Earth and its Moon
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetRetrieved(new WorldId(82, 9), ScanType.Mineral, 0));     // Sol has 9 planets: 0..8
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetRetrieved(new WorldId(82, 2, 2), ScanType.Mineral, 0));  // Earth has 2 moons
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetRetrieved(new WorldId(82, 0, 0), ScanType.Mineral, 0));  // Mercury has none

        // And a save naming a world its system doesn't have is refused.
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems);
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["worlds"] = new JsonArray(JsonNode.Parse(
            "{\"star\": 82, \"planet\": 0, \"moon\": 0, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}"));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), systems));
    }

    [Fact]
    public void ASaveWithAWorldTwiceIsRefusedEvenIfOneCopyIsEmpty()
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        const string empty = "{\"star\": 0, \"planet\": 0, \"moon\": null, \"mineral_taken\": 0, \"energy_taken\": 0, \"biological_taken\": 0}";
        const string taken = "{\"star\": 0, \"planet\": 0, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}";
        node["worlds"] = new JsonArray(JsonNode.Parse(empty), JsonNode.Parse(taken));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void NodesPastThirtyTwoCountAsThereInsteadOfThrowing()
    {
        var state = new PlanetWorldState();
        var nodes = Enumerable.Range(0, 34).Select(i => new SurfaceNode((short)i, 0, 0, 0)).ToList();
        state.SetRetrieved(Luna, ScanType.Energy, 31);
        var left = state.Remaining(Luna, ScanType.Energy, nodes);
        Assert.Equal(33, left.Count);
        Assert.Contains(left, n => n.Node == 33);
    }

    [Fact]
    public void ARecordCanBeReadButNotChangedFromOutside()
    {
        var state = new PlanetWorldState();
        state.SetRetrieved(Luna, ScanType.Biological, 6);
        var record = state.Worlds[Luna];
        Assert.Equal(1u << 6, record[ScanType.Biological]);
        Assert.Null(typeof(PlanetWorldState.Record).GetProperty("Item")!.SetMethod);
        Assert.DoesNotContain(typeof(PlanetWorldState.Record).GetMembers(), m => m.Name == "Retrieved" && m is System.Reflection.PropertyInfo or System.Reflection.FieldInfo { IsPublic: true });
    }

    [Fact]
    public void ASaveWithAWorldTwiceIsRefused()
    {
        var node = JsonNode.Parse(HostSave.ToJson(NewWorld()))!;
        const string w = "{\"star\": 0, \"planet\": 0, \"moon\": null, \"mineral_taken\": 1, \"energy_taken\": 0, \"biological_taken\": 0}";
        node["worlds"] = new JsonArray(JsonNode.Parse(w), JsonNode.Parse(w));
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }
}
