// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/surface.c, scan.c and the
// generateOrbital, generateEnergy and generateLife functions of
// planets/generate/*.c), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.Systems;

/// <summary>
/// A mineral, energy or life node on a planet's surface map (NODE_INFO).
/// For minerals, Type is the element id and Density is MAKE_WORD (gross
/// size 0..2, quantity); for life, Type is the creature (23 an Evil One,
/// 24 a Brainbox Bulldozer, 25 Zex's Beauty); energy nodes have neither.
/// </summary>
public readonly record struct SurfaceNode(short X, short Y, int Type, ushort Density);

/// <summary>A world's nodes, in node order (the order of UQM's node numbers).</summary>
public sealed record SurfaceNodes(
    IReadOnlyList<SurfaceNode> Minerals,
    IReadOnlyList<SurfaceNode> Energy,
    IReadOnlyList<SurfaceNode> Life);

/// <summary>
/// The result of entering orbit around a world: whether orbit is reached,
/// the scan seeds, the planet report and the surface nodes (null for a
/// shielded world, which can't be scanned).
/// </summary>
public sealed record OrbitData(
    bool Entered,
    uint MineralSeed,
    uint EnergySeed,
    uint BiologicalSeed,
    PlanetInfo? Report,
    SurfaceNodes? Nodes,
    PlanetReport? Scan = null)
{
    public static readonly OrbitData NotEntered = new(false, 0, 0, 0, null, null);
}

/// <summary>
/// Entering orbit and generating surface nodes, as UQM 0.8.0 does at the
/// start of a new game with no alien ships present. Homeworld
/// conversations, story pickups and changing story flags are later stages:
/// here a world is orbited as when its owners are gone, and nothing has
/// been picked up.
/// </summary>
public static class Surface
{
    public const int MapWidth = 242;    // MAP_WIDTH (SIS_SCREEN_WIDTH at 320x240)
    public const int MapHeight = 75;    // MAP_HEIGHT

    private const int NumCreatureTypes = 23;
    private const int MaxLifeVariation = 3;
    private const int NumRaceRuins = 16;
    private const int MaxNodes = 32;    // sizeof (DWORD) * 8
    private const int EarthAtmosphere = 50;

    /// <summary>
    /// Enters orbit around a planet (<paramref name="moon"/> null) or one of
    /// its moons: the generator's generateOrbital, then the nodes scan.c
    /// would show.
    /// </summary>
    public static OrbitData EnterOrbit(SolarSystem system, int planet, int? moon)
    {
        WorldDesc world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        if (!WorldTypes.HasPlanetData(world.DataIndex))
            return OrbitData.NotEntered;   // starbases and the Sa-Matra

        string? special = system.Star.Special;
        bool IsPlanet(int i) => planet == i && moon is null;
        bool IsMoon(int i, int j) => planet == i && moon == j;

        // Worlds whose generateOrbital always starts a conversation.
        if (special == "TALKING_PET_DEFINED" && IsPlanet(0))
            return OrbitData.NotEntered;   // new game: TALKING_PET not yet set
        if (special == "SLYLANDRO_DEFINED" && IsPlanet(3))
            return OrbitData.NotEntered;

        short sunDistance = moon is null ? world.Radius : system.Planets[planet].Radius;
        PlanetInfo info = PlanetAnalysis.Analyse(system.Generator.Types, system.Star, world.RandSeed,
            world.DataIndex, sunDistance, greenhouse: true);
        PlanetType type = system.Generator.Types[WorldTypes.PlanetType(world.DataIndex)];
        int starSize = system.SunType;
        uint mineralSeed, energySeed, bioSeed;

        if (special == "SOL_DEFINED")
        {
            mineralSeed = info.SeedAfter;
            uint after = SeedAfterMinerals(type, starSize, mineralSeed);
            energySeed = 0;
            bioSeed = IsMoon(2, 1) ? after : 0;
            info = SolReport(info, planet, moon);
        }
        else if (special == "SPATHI_DEFINED" && IsPlanet(0))
        {
            mineralSeed = info.SeedAfter;
            bioSeed = SeedAfterMinerals(type, starSize, mineralSeed);
            energySeed = 0;
            info = info with { PlanetRadius = 120, Weather = 0, Tectonics = 0, SurfaceTemperature = 31 };
            info = info with { SurfaceGravity = Gravity(info) };
        }
        else if (special == "START_COLONY_DEFINED" && IsPlanet(0))
        {
            mineralSeed = energySeed = bioSeed = 0;
            info = info with
            {
                AtmoDensity = EarthAtmosphere * 98 / 100, Weather = 0, Tectonics = 0, SurfaceTemperature = 28,
            };
        }
        else
        {
            // GenerateDefault_generateOrbital (also Burvixese and the Spathi moon).
            bioSeed = info.SeedAfter;
            mineralSeed = SeedAfterLife(type, info.LifeChance, bioSeed);
            energySeed = mineralSeed;
            info = ReportOverrides(info, special, IsPlanet, IsMoon);
        }

        SurfaceNodes? nodes = null;
        if ((world.DataIndex & WorldTypes.Shielded) == 0)
        {
            var w = new World(type, starSize, info.LifeChance, mineralSeed, energySeed, bioSeed);
            nodes = new SurfaceNodes(
                Minerals(w),
                EnergyNodes(w, special, IsPlanet, IsMoon),
                LifeNodes(w, special, IsPlanet, IsMoon));
        }

        return new OrbitData(true, mineralSeed, energySeed, bioSeed, info, nodes,
            PlanetReport.From(info, type.Size == PlanetSize.GasGiant));
    }

    private readonly record struct World(PlanetType Type, int StarSize, short LifeChance,
        uint MineralSeed, uint EnergySeed, uint BioSeed);

    private static ushort Gravity(PlanetInfo i) => (ushort)((uint)i.PlanetDensity * i.PlanetRadius / 100);

    private static PlanetInfo ReportOverrides(PlanetInfo info, string? special, Func<int, bool> isPlanet,
        Func<int, int, bool> isMoon) => special switch
    {
        "ANDROSYNTH_DEFINED" when isPlanet(1) => info with
        {
            AtmoDensity = EarthAtmosphere * 144 / 100, SurfaceTemperature = 28, Weather = 1, Tectonics = 1,
        },
        "BURVIXESE_DEFINED" when isPlanet(0) => info with { Weather = 0, Tectonics = 0 },
        "ILWRATH_DEFINED" when isPlanet(0) => info with { Weather = 2, Tectonics = 3 },
        "SPATHI_DEFINED" when isMoon(0, 0) => info with { Weather = 0, Tectonics = 0, SurfaceTemperature = 28 },
        "SYREEN_DEFINED" when isPlanet(0) => info with
        {
            SurfaceTemperature = 19, Tectonics = 0, Weather = 0, AtmoDensity = EarthAtmosphere * 9 / 10,
        },
        "MYCON_TRAP_DEFINED" when isPlanet(0) => info with
        {
            AtmoDensity = EarthAtmosphere * 2, SurfaceTemperature = 35, Weather = 3, Tectonics = 1,
        },
        "UTWIG_DEFINED" when isPlanet(0) => info with { Weather = 1, Tectonics = 1 },
        "VUX_DEFINED" or "MAIDENS_DEFINED" or "VUX_BEAST_DEFINED" when isPlanet(0) =>
            info with { Weather = 2, Tectonics = 0 },
        _ => info,
    };

    // GenerateSol_generateOrbital: the real values of Sol's worlds.
    private static PlanetInfo SolReport(PlanetInfo i, int planet, int? moon)
    {
        const int er = WorldTypes.EarthRadius;
        if (moon is int m)
        {
            i = i with { AxialTilt = 0, AtmoDensity = 0, Weather = 0 };
            i = planet switch
            {
                2 => i with { PlanetDensity = 60, PlanetRadius = 25, Tectonics = 0, RotationPeriod = 240 * 29, SurfaceTemperature = -18 },
                4 => (i with { PlanetToSunDist = er * 520 / 100 }) is var j ? m switch
                {
                    0 => j with { PlanetDensity = 69, PlanetRadius = 25, Tectonics = 3, RotationPeriod = 390, SurfaceTemperature = -163 },
                    1 => j with { PlanetDensity = 54, PlanetRadius = 25, Tectonics = 1, RotationPeriod = 840, SurfaceTemperature = -161 },
                    2 => j with { PlanetDensity = 35, PlanetRadius = 41, Tectonics = 0, RotationPeriod = 1728, SurfaceTemperature = -164 },
                    3 => j with { PlanetDensity = 35, PlanetRadius = 38, Tectonics = 1, RotationPeriod = 4008, SurfaceTemperature = -167 },
                    _ => j,
                } : i,
                5 => i with { PlanetToSunDist = er * 952 / 100, AtmoDensity = 160, Weather = 2, PlanetDensity = 34, PlanetRadius = 40, Tectonics = 1, RotationPeriod = 3816, SurfaceTemperature = -178 },
                7 => i with { PlanetToSunDist = er * 2999 / 100, AtmoDensity = 10, Weather = 1, PlanetDensity = 95, PlanetRadius = 27, Tectonics = 0, RotationPeriod = 4300, SurfaceTemperature = -216 },
                _ => i,
            };
        }
        else
        {
            i = planet switch
            {
                0 => i with { AtmoDensity = 0, PlanetDensity = 98, PlanetRadius = 38, AxialTilt = 3, Weather = 0, Tectonics = 2, RotationPeriod = 59 * 240, SurfaceTemperature = 165 },
                1 => i with { AtmoDensity = 90 * EarthAtmosphere, PlanetDensity = 95, PlanetRadius = 95, AxialTilt = 177, Weather = 7, Tectonics = 1, RotationPeriod = 243 * 240, SurfaceTemperature = 457 },
                2 => i with { AtmoDensity = EarthAtmosphere, PlanetDensity = 100, PlanetRadius = 100, AxialTilt = 23, Weather = 1, Tectonics = 1, RotationPeriod = 240, SurfaceTemperature = 22 },
                3 => i with { AtmoDensity = 1, PlanetDensity = 72, PlanetRadius = 53, AxialTilt = 24, Weather = 1, Tectonics = 1, RotationPeriod = 246, SurfaceTemperature = -53 },
                4 => i with { AtmoDensity = PlanetInfo.GasGiantAtmosphere, PlanetDensity = 24, PlanetRadius = 1120, AxialTilt = 3, Weather = 7, Tectonics = 0, RotationPeriod = 98, SurfaceTemperature = -143, PlanetToSunDist = er * 520 / 100 },
                5 => i with { AtmoDensity = PlanetInfo.GasGiantAtmosphere, PlanetDensity = 13, PlanetRadius = 945, AxialTilt = 27, Weather = 7, Tectonics = 0, RotationPeriod = 102, SurfaceTemperature = -197, PlanetToSunDist = er * 952 / 100 },
                6 => i with { AtmoDensity = PlanetInfo.GasGiantAtmosphere, PlanetDensity = 21, PlanetRadius = 411, AxialTilt = 98, Weather = 7, Tectonics = 0, RotationPeriod = 172, SurfaceTemperature = -217, PlanetToSunDist = er * 1916 / 100 },
                7 => i with { AtmoDensity = PlanetInfo.GasGiantAtmosphere, PlanetDensity = 28, PlanetRadius = 396, AxialTilt = 30, Weather = 7, Tectonics = 0, RotationPeriod = 182, SurfaceTemperature = -229, PlanetToSunDist = er * 2999 / 100 },
                8 => i with { AtmoDensity = 0, PlanetDensity = 33, PlanetRadius = 18, AxialTilt = 119, Weather = 0, Tectonics = 0, RotationPeriod = 1533, SurfaceTemperature = -235, PlanetToSunDist = er * 3937 / 100 },
                _ => i,
            };
        }
        return i with { SurfaceGravity = Gravity(i) };
    }

    // ---- surface.c ----

    private static SurfaceNode RandomLocation(SysRandom rng, int type = 0, ushort density = 0)
    {
        ushort r = (ushort)rng.Random();
        return new SurfaceNode(
            (short)(8 + (byte)r % (MapWidth - (8 << 1))),
            (short)(8 + (byte)(r >> 8) % (MapHeight - (8 << 1))),
            type, density);
    }

    /// <summary>CalcMineralDeposits, all nodes (up to 32).</summary>
    private static List<SurfaceNode> CalcMinerals(PlanetType type, int starSize, SysRandom rng)
    {
        var nodes = new List<SurfaceNode>();
        foreach (ElementSlot slot in type.Elements)
        {
            int possible = (byte)rng.Random() % (slot.QuantityClass + 1);
            while (possible-- > 0)
            {
                int fine = (ushort)rng.Random() % 100 + (slot.QualityClass + starSize) * 50;
                int gross = fine < 150 ? 0 : fine < 225 ? 1 : 2;
                SurfaceNode loc = RandomLocation(rng);
                nodes.Add(loc with { Type = slot.Element, Density = (ushort)(gross | ((fine / 10 + 1) << 8)) });
                if (nodes.Count == MaxNodes)
                    return nodes;
            }
        }
        return nodes;
    }

    /// <summary>CalcLifeForms, all nodes (up to 32).</summary>
    private static List<SurfaceNode> CalcLife(PlanetType type, short lifeChance, SysRandom rng)
    {
        var nodes = new List<SurfaceNode>();
        if (type.Size == PlanetSize.GasGiant)
            return nodes;

        const int minLifeChance = 10;
        short lifeVar = (short)(rng.Random() & 1023);
        if (lifeVar < lifeChance || (lifeChance < minLifeChance && lifeVar < minLifeChance))
        {
            int numTypes = 1 + (byte)rng.Random() % MaxLifeVariation;
            do
            {
                ushort r = (ushort)rng.Random();
                int index = (byte)r % NumCreatureTypes;
                int numCreatures = 1 + (byte)(r >> 8) % 10;
                do
                {
                    nodes.Add(RandomLocation(rng, index));
                    if (nodes.Count == MaxNodes)
                        return nodes;
                } while (--numCreatures > 0);
            } while (--numTypes > 0);
        }
        return nodes;
    }

    private static uint SeedAfterLife(PlanetType type, short lifeChance, uint bioSeed)
    {
        var rng = new SysRandom(bioSeed);
        CalcLife(type, lifeChance, rng);
        return rng.Seed;
    }

    private static uint SeedAfterMinerals(PlanetType type, int starSize, uint mineralSeed)
    {
        var rng = new SysRandom(mineralSeed);
        CalcMinerals(type, starSize, rng);
        return rng.Seed;
    }

    private static List<SurfaceNode> Minerals(World w) =>
        CalcMinerals(w.Type, w.StarSize, new SysRandom(w.MineralSeed));

    /// <summary>GenerateRandomNodes.</summary>
    private static List<SurfaceNode> RandomNodes(uint seed, int count, int type)
    {
        var rng = new SysRandom(seed);
        var nodes = new List<SurfaceNode>();
        for (int i = 0; i < count; ++i)
            nodes.Add(RandomLocation(rng, type));
        return nodes;
    }

    /// <summary>GeneratePresetLife.</summary>
    private static List<SurfaceNode> PresetLife(uint seed, int[] types)
    {
        var rng = new SysRandom(seed);
        return types.Select(t => RandomLocation(rng, t)).ToList();
    }

    private static readonly SurfaceNode[] None = Array.Empty<SurfaceNode>();

    // The special generators' generateEnergy, with a new game's story state
    // (nothing picked up yet). Ruins and artifacts are energy nodes.
    private static IReadOnlyList<SurfaceNode> EnergyNodes(World w, string? special, Func<int, bool> isPlanet,
        Func<int, int, bool> isMoon)
    {
        List<SurfaceNode> Ruins() => RandomNodes(w.EnergySeed, NumRaceRuins, 0);
        List<SurfaceNode> Artifact() => RandomNodes(w.EnergySeed, 1, 0);

        switch (special)
        {
            case "ANDROSYNTH_DEFINED" when isPlanet(1):
            case "DRUUGE_DEFINED" when isPlanet(0):
            case "ILWRATH_DEFINED" when isPlanet(0):
            case "ORZ_DEFINED" when isPlanet(0):
            case "TALKING_PET_DEFINED" when isPlanet(0):
            case "PKUNK_DEFINED" when isPlanet(0):
            case "SUPOX_DEFINED" when isPlanet(0):
            case "THRADD_DEFINED" when isPlanet(0):
            case "UTWIG_DEFINED" when isPlanet(0):
            case "VUX_DEFINED" when isPlanet(0):
            case "YEHAT_DEFINED" when isPlanet(0):
            case "ZOQFOT_DEFINED" when isPlanet(0):
            case "BURVIXESE_DEFINED" when isPlanet(0):
                return Ruins();
            case "BURVIXESE_DEFINED" when isMoon(0, 0):       // broadcasters
            case "SUN_DEVICE_DEFINED" when isPlanet(0):
            case "EGG_CASE0_DEFINED" when isPlanet(0):
            case "EGG_CASE1_DEFINED" when isPlanet(0):
            case "EGG_CASE2_DEFINED" when isPlanet(0):
            case "TAALO_PROTECTOR_DEFINED" when isMoon(1, 2):
            case "SPATHI_DEFINED" when isMoon(0, 0):           // Umgah broadcasters
            case "AQUA_HELIX_DEFINED" when isPlanet(0):
            case "BOMB_DEFINED" when isMoon(5, 1):
            case "SHIP_VAULT_DEFINED" when isMoon(0, 0):
            case "URQUAN_WRECK_DEFINED" when isPlanet(6):
                return Artifact();
            case "SOL_DEFINED" when isPlanet(8):                // the Spathi on Pluto (SPAPLUTO)
                return new[] { new SurfaceNode(20, MapHeight - 8, 0, 0) };
            case "SOL_DEFINED" when isMoon(2, 1):               // the Moon base
                return new[] { new SurfaceNode(MapWidth * 3 / 4, MapHeight * 1 / 4, 0, 0) };
            case "MAIDENS_DEFINED" when isPlanet(0):            // the Shofixti maidens
                return new[] { new SurfaceNode(MapWidth / 3, MapHeight * 5 / 8, 0, 0) };
            default:
                return None;
        }
    }

    private static readonly int[] MaidensLife = { 9, 9, 9, 9, 14, 14, 14, 14, 18, 18, 18, 18 };
    private static readonly int[] VuxBeastLife = { NumCreatureTypes + 2, 3, 3, 3, 3, 3, 8, 8, 8, 8, 8 };

    // The special generators' generateLife.
    private static IReadOnlyList<SurfaceNode> LifeNodes(World w, string? special, Func<int, bool> isPlanet,
        Func<int, int, bool> isMoon)
    {
        switch (special)
        {
            case "SOL_DEFINED":
                // Only the Moon has life: the base's Brainbox Bulldozers.
                return isMoon(2, 1) ? RandomNodes(w.BioSeed, 10, NumCreatureTypes + 1) : None;
            case "SPATHI_DEFINED":
                // Only Spathiwa has life: the Evil Ones.
                return isPlanet(0) ? RandomNodes(w.BioSeed, 32, NumCreatureTypes) : None;
            case "SUN_DEVICE_DEFINED":
            case "MYCON_DEFINED":
            case "EGG_CASE0_DEFINED":
            case "EGG_CASE1_DEFINED":
            case "EGG_CASE2_DEFINED":
                return None;   // no life in Mycon systems
            case "MAIDENS_DEFINED" when isPlanet(0):
                return PresetLife(w.BioSeed, MaidensLife);
            case "VUX_BEAST_DEFINED" when isPlanet(0):
                return PresetLife(w.BioSeed, VuxBeastLife);
            default:
                return CalcLife(w.Type, w.LifeChance, new SysRandom(w.BioSeed));
        }
    }
}
