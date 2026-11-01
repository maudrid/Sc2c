// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (state.c GetPlanetInfo and
// PutPlanetInfo, scan.c isNodeRetrieved, setNodeRetrieved,
// setNodeNotRetrieved and countNodesRetrieved),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>The three kinds of scan and node (UQM's MINERAL_SCAN, ENERGY_SCAN, BIOLOGICAL_SCAN).</summary>
public enum ScanType { Mineral = 0, Energy = 1, Biological = 2 }

/// <summary>
/// One planet or moon: the star (map number), the planet (0-based) and the
/// moon (null for the planet itself), as UQM's star info file addresses
/// worlds (moon_index 0 for the planet, 1 and up for moons).
/// </summary>
public readonly record struct WorldId(int Star, int Planet, int? Moon = null)
{
    public override string ToString() => Moon is int m ? $"star {Star} planet {Planet} moon {m}" : $"star {Star} planet {Planet}";

    /// <summary>Throws if this can't be a world: star outside the map, planet or moon number out of UQM's range.</summary>
    public void Validate()
    {
        if (Star < 0 || Star >= StarMap.SolarSystemCount)
            throw new ArgumentOutOfRangeException(nameof(Star), $"{this}: no such star");
        if (Planet < 0 || Planet >= WorldTypes.MaxPlanets)
            throw new ArgumentOutOfRangeException(nameof(Planet), $"{this}: no such planet");
        if (Moon is int m && (m < 0 || m >= WorldTypes.MaxMoons))
            throw new ArgumentOutOfRangeException(nameof(Moon), $"{this}: no such moon");
    }
}

/// <summary>
/// The shared state of planets and moons: which nodes have been taken from
/// each world (UQM's ScanRetrieveMask, one 32-bit mask per scan type, bit n
/// for node n), and, for Crateris, which worlds someone has scanned. One
/// instance per game, the same for every player: a node one player takes
/// is gone for everyone (docs/porting-inventory.md, "Orbit and scanning").
/// Worlds nobody has touched aren't stored.
/// </summary>
public sealed class PlanetWorldState
{
    /// <summary>Nodes per scan type a world can have (sizeof (DWORD) * 8).</summary>
    public const int MaxNodes = 32;

    private readonly Dictionary<WorldId, Record> _worlds = new();

    /// <summary>What is stored for one world.</summary>
    public sealed class Record
    {
        /// <summary>Taken nodes, indexed by <see cref="ScanType"/>.</summary>
        public uint[] Retrieved { get; } = new uint[3];

        /// <summary>Scan types someone has run on this world (bit per <see cref="ScanType"/>).</summary>
        public byte Scanned { get; set; }

        internal bool IsEmpty => Scanned == 0 && Retrieved[0] == 0 && Retrieved[1] == 0 && Retrieved[2] == 0;
    }

    /// <summary>Every world with stored state.</summary>
    public IReadOnlyDictionary<WorldId, Record> Worlds => _worlds;

    /// <summary>The taken-node mask for one scan type (ScanRetrieveMask).</summary>
    public uint RetrieveMask(WorldId world, ScanType scan) =>
        _worlds.TryGetValue(world, out var r) ? r.Retrieved[(int)scan] : 0;

    /// <summary>isNodeRetrieved.</summary>
    public bool IsRetrieved(WorldId world, ScanType scan, int node) =>
        (RetrieveMask(world, scan) & Bit(node)) != 0;

    /// <summary>setNodeRetrieved: the node has been taken, for everyone.</summary>
    public void SetRetrieved(WorldId world, ScanType scan, int node) =>
        Get(world).Retrieved[(int)scan] |= Bit(node);

    /// <summary>setNodeNotRetrieved: the node is back. UQM defines it but nothing calls it yet; kept for stage 3's story nodes.</summary>
    public void ClearRetrieved(WorldId world, ScanType scan, int node)
    {
        uint bit = Bit(node);
        world.Validate();
        if (_worlds.TryGetValue(world, out var r))
        {
            r.Retrieved[(int)scan] &= ~bit;
            Forget(world, r);
        }
    }

    /// <summary>countNodesRetrieved.</summary>
    public int CountRetrieved(WorldId world, ScanType scan) =>
        System.Numerics.BitOperations.PopCount(RetrieveMask(world, scan));

    public bool IsScanned(WorldId world, ScanType scan) =>
        _worlds.TryGetValue(world, out var r) && (r.Scanned & (1 << (int)scan)) != 0;

    /// <summary>Someone ran this scan on this world.</summary>
    public void MarkScanned(WorldId world, ScanType scan) =>
        Get(world).Scanned |= (byte)(1 << (int)scan);

    /// <summary>
    /// The nodes still on a world's surface, with their node numbers, as
    /// GeneratePlanetSurface in scan.c leaves out the retrieved ones.
    /// </summary>
    public IReadOnlyList<(int Node, SurfaceNode Info)> Remaining(WorldId world, ScanType scan, IReadOnlyList<SurfaceNode> nodes)
    {
        uint mask = RetrieveMask(world, scan);
        var left = new List<(int, SurfaceNode)>();
        for (int i = 0; i < nodes.Count; ++i)
        {
            if ((mask & Bit(i)) == 0)
                left.Add((i, nodes[i]));
        }
        return left;
    }

    /// <summary>Restores one world's stored state (from a save).</summary>
    public void Restore(WorldId world, uint mineral, uint energy, uint biological, byte scanned)
    {
        world.Validate();
        if (scanned > 7)
            throw new ArgumentOutOfRangeException(nameof(scanned), $"{world}: scanned flags {scanned}");
        var r = new Record { Scanned = scanned };
        r.Retrieved[0] = mineral;
        r.Retrieved[1] = energy;
        r.Retrieved[2] = biological;
        if (!r.IsEmpty)
            _worlds[world] = r;
    }

    private Record Get(WorldId world)
    {
        world.Validate();
        if (!_worlds.TryGetValue(world, out var r))
            _worlds[world] = r = new Record();
        return r;
    }

    private void Forget(WorldId world, Record r)
    {
        if (r.IsEmpty)
            _worlds.Remove(world);
    }

    private static uint Bit(int node)
    {
        if ((uint)node >= MaxNodes)
            throw new ArgumentOutOfRangeException(nameof(node), $"node {node}: a world has at most {MaxNodes} per scan");
        return 1u << node;
    }
}
