// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/solarsys.c, gendef.c and
// planets/generate/*.c), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;
using static Crateris.Core.Systems.WorldTypes;

namespace Crateris.Core.Systems;

/// <summary>
/// The story flags the system layouts read. All false at the start of a
/// new game. Stage 3 connects these to the real story state.
/// </summary>
public sealed record SystemStoryState(bool ChmmrUnleashed = false, bool SpathiShieldedSelves = false)
{
    public static readonly SystemStoryState NewGame = new();
}

/// <summary>A generated star system: the sun and its planets, as LoadSolarSys leaves them.</summary>
public sealed class SolarSystem
{
    internal SolarSystem(SystemGenerator generator, Star star, uint sunSeed, WorldDesc[] planets)
    {
        Generator = generator;
        Star = star;
        SunSeed = sunSeed;
        Planets = planets;
    }

    public SystemGenerator Generator { get; }

    public Star Star { get; }

    /// <summary>The sun's rand_seed.</summary>
    public uint SunSeed { get; }

    /// <summary>The sun's data_index: its size.</summary>
    public int SunType => (int)Star.Size;

    public IReadOnlyList<WorldDesc> Planets { get; }

    /// <summary>The moons of a planet, as GenerateMoons makes them on entering its inner system.</summary>
    public WorldDesc[] Moons(int planet) => Generator.GenerateMoons(this, planet);

    /// <summary>A planet's data as the planet report shows it (with the greenhouse bonus).</summary>
    public PlanetInfo PlanetInfo(int planet) => Analyse(Planets[planet], Planets[planet].Radius, true);

    /// <summary>A moon's data as the planet report shows it.</summary>
    public PlanetInfo MoonInfo(int planet, WorldDesc moon) => Analyse(moon, Planets[planet].Radius, true);

    /// <summary>Enters orbit around a planet, or one of its moons: report, scan seeds and surface nodes.</summary>
    public OrbitData EnterOrbit(int planet, int? moon = null) => Surface.EnterOrbit(this, planet, moon);

    /// <summary>A planet's orbit colour, 0..4 (cold to hot), as LoadSolarSys gives it.</summary>
    public int OrbitColor(int planet) =>
        PlanetAnalysis.TemperatureColorIndex(Analyse(Planets[planet], Planets[planet].Radius, false).SurfaceTemperature);

    /// <summary>The temperature without the greenhouse bonus, used for orbit colours.</summary>
    public short OrbitTemperature(WorldDesc world, short sunDistance) =>
        Analyse(world, sunDistance, false).SurfaceTemperature;

    private PlanetInfo Analyse(WorldDesc world, short sunDistance, bool greenhouse) =>
        PlanetAnalysis.Analyse(Generator.Types, Star, world.RandSeed, world.DataIndex, sunDistance, greenhouse);
}

/// <summary>
/// Generates star systems the way UQM 0.8.0 does: LoadSolarSys and
/// GenerateMoons with the default generator (gendefault.c) or a star's
/// special generator. Only the layout parts of the special generators are
/// here (planets, moons, their types and orbits); their story hooks come in
/// later stages.
/// </summary>
public sealed class SystemGenerator
{
    public SystemGenerator(PlanetTypes types, SystemStoryState? story = null)
    {
        Types = types;
        Story = story ?? SystemStoryState.NewGame;
    }

    public PlanetTypes Types { get; }

    public SystemStoryState Story { get; }

    /// <summary>LoadSolarSys: the sun's seed and the planets of a star.</summary>
    public SolarSystem Generate(Star star)
    {
        var rng = new SysRandom(star.Seed);
        uint sunSeed = rng.Random();
        var planets = new WorldDesc[MaxPlanets];
        int count = GeneratePlanets(rng, star, planets);
        return new SolarSystem(this, star, sunSeed, planets[..count]);
    }

    /// <summary>GenerateMoons: seeded with the planet's seed, then the generator's generateMoons.</summary>
    internal WorldDesc[] GenerateMoons(SolarSystem system, int planet)
    {
        WorldDesc p = system.Planets[planet];
        var rng = new SysRandom(p.RandSeed);
        var moons = new WorldDesc[MaxMoons];
        Star star = system.Star;

        // GenerateDefault_generateMoons
        Orbits.Fill(rng, star, moons, 0, p.NumPlanets, false, moons: true);

        switch (star.Special)
        {
            case "SOL_DEFINED":
                SolMoons(rng, planet, moons);
                break;
            case "BURVIXESE_DEFINED" when planet == 0:
                moons[0].DataIndex = SelenicWorld;
                moons[0].Radius = MinMoonRadius + (MaxMoons - 1) * MoonDelta;
                PlaceAtRandomAngle(rng, ref moons[0]);
                break;
            case "CHMMR_DEFINED" when planet == 1:
                moons[0].DataIndex = HierarchyStarbase;
                moons[0].Radius = MinMoonRadius;
                PlaceAtRandomAngle(rng, ref moons[0]);
                break;
            case "SAMATRA_DEFINED" when planet == 4:
                moons[0].DataIndex = SaMatra;
                moons[0].Radius = MinMoonRadius + 2 * MoonDelta;
                PlaceAtRandomAngle(rng, ref moons[0]);
                break;
            case "SPATHI_DEFINED" when planet == 0:
                moons[0].DataIndex = PellucidWorld;
                moons[0].Radius = MinMoonRadius + MoonDelta;
                PlaceAtRandomAngle(rng, ref moons[0]);
                break;
            case "SYREEN_DEFINED" when planet == 0:
                moons[0].DataIndex = HierarchyStarbase;
                moons[0].Radius = MinMoonRadius;
                PlaceAt(ref moons[0], UqmMath.Quadrant);
                break;
        }

        return moons[..p.NumPlanets];
    }

    private int GeneratePlanets(SysRandom rng, Star star, WorldDesc[] p)
    {
        int n;
        switch (star.Special)
        {
            case "SOL_DEFINED":
                return SolPlanets(p);

            case "SHOFIXTI_DEFINED":
                for (int i = 0; i < 6; ++i)
                {
                    p[i].NumPlanets = 0;
                    p[i].DataIndex = i < 3 ? SelenicWorld : MetalWorld;
                }
                return Orbits.Fill(rng, star, p, 0, 6, true, moons: false);

            case "START_COLONY_DEFINED":
                n = Orbits.Fill(rng, star, p, 0, null, false, moons: false);
                Resize(ref p[0], EarthRadius * 115 / 100);
                p[0].DataIndex = WaterWorld | Shielded;
                return n;

            case "SPATHI_DEFINED":
                n = Orbits.Fill(rng, star, p, 0, 1, false, moons: false);
                Resize(ref p[0], EarthRadius * 1150 / 100);
                p[0].DataIndex = WaterWorld;
                if (Story.SpathiShieldedSelves)
                    p[0].DataIndex |= Shielded;
                p[0].NumPlanets = 1;
                return n;

            case "ANDROSYNTH_DEFINED":
                n = Default(rng, star, p);
                p[1].DataIndex = TelluricWorld;
                Resize(ref p[1], EarthRadius * 204 / 100);
                return n;

            case "BURVIXESE_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = ReduxWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 39 / 100);
                return n;

            case "CHMMR_DEFINED":
                n = Default(rng, star, p);
                p[1].DataIndex = SapphireWorld;
                if (!Story.ChmmrUnleashed)
                    p[1].DataIndex |= Shielded;
                p[1].NumPlanets = 1;
                return n;

            case "DRUUGE_DEFINED":
                n = Default(rng, star, p);
                Array.Copy(p, 0, p, 1, n);
                ++n;
                p[0].DataIndex = DustWorld;
                p[0].Radius = EarthRadius * 50 / 100;
                p[0].NumPlanets = 0;
                PlaceAt(ref p[0], UqmMath.HalfCircle - Octant);
                p[0].RandSeed = Orbits.MakeDword(p[0].X, p[0].Y);
                return n;

            case "ILWRATH_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = PrimordialWorld;
                Resize(ref p[0], EarthRadius * 204 / 100);
                return n;

            case "SUN_DEVICE_DEFINED":
            case "MYCON_DEFINED":
            case "EGG_CASE0_DEFINED":
            case "EGG_CASE1_DEFINED":
            case "EGG_CASE2_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = ShatteredWorld;
                p[0].Radius = EarthRadius * 80 / 100;
                if (p[0].NumPlanets > 2)
                    p[0].NumPlanets = 2;
                Resize(ref p[0], p[0].Radius);
                return n;

            case "ORZ_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 0;
                Resize(ref p[0], EarthRadius * 156 / 100);
                return n;

            case "TALKING_PET_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = TelluricWorld;
                Resize(ref p[0], EarthRadius * 204 / 100);
                return n;

            case "PKUNK_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 104 / 100);
                return n;

            case "RAINBOW_DEFINED":
            {
                n = Default(rng, star, p);
                p[0].DataIndex = RainbowWorld;
                p[0].NumPlanets = 0;
                p[0].Radius = EarthRadius * 50 / 100;
                int angle = UqmMath.ArcTan(p[0].X, p[0].Y);
                if (angle <= UqmMath.Quadrant)
                    angle += UqmMath.Quadrant;
                else if (angle >= UqmMath.FullCircle - UqmMath.Quadrant)
                    angle -= UqmMath.Quadrant;
                PlaceAt(ref p[0], angle);
                return n;
            }

            case "SAMATRA_DEFINED":
                n = Default(rng, star, p);
                p[4].NumPlanets = 1;
                return n;

            case "SLYLANDRO_DEFINED":
                n = Default(rng, star, p);
                p[3].DataIndex = RedGasGiant;
                p[3].NumPlanets = 1;
                return n;

            case "SUPOX_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 2;
                Resize(ref p[0], EarthRadius * 152 / 100);
                return n;

            case "SYREEN_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld | Shielded;
                p[0].NumPlanets = 1;
                return n;

            case "AQUA_HELIX_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = PrimordialWorld;
                Resize(ref p[0], EarthRadius * 65 / 100);
                return n;

            case "THRADD_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 0;
                Resize(ref p[0], EarthRadius * 98 / 100);
                return n;

            case "MYCON_TRAP_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = TelluricWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 203 / 100);
                return n;

            case "UTWIG_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 174 / 100);
                return n;

            case "MAIDENS_DEFINED":
                // UQM runs the default generator twice here (see genvux.c).
                Default(rng, star, p);
                n = Default(rng, star, p);
                p[0].DataIndex = ReduxWorld;
                Resize(ref p[0], EarthRadius * 212 / 100);
                return n;

            case "VUX_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = ReduxWorld;
                p[0].NumPlanets = 1;
                p[0].Radius = EarthRadius * 42 / 100;
                PlaceAt(ref p[0], UqmMath.HalfCircle + Octant);
                p[0].RandSeed = Orbits.MakeDword(p[0].X, p[0].Y);
                return n;

            case "VUX_BEAST_DEFINED":
                n = Default(rng, star, p);
                Array.Copy(p, 0, p, 1, n);
                ++n;
                p[0].DataIndex = WaterWorld;
                p[0].Radius = EarthRadius * 110 / 100;
                p[0].NumPlanets = 0;
                PlaceAt(ref p[0], UqmMath.HalfCircle - Octant);
                p[0].RandSeed = Orbits.MakeDword(p[0].X, p[0].Y);
                return n;

            case "YEHAT_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = WaterWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 106 / 100);
                return n;

            case "ZOQFOT_DEFINED":
                n = Default(rng, star, p);
                p[0].DataIndex = ReduxWorld;
                p[0].NumPlanets = 1;
                Resize(ref p[0], EarthRadius * 138 / 100);
                return n;

            default:
                // The default generator, and the special generators that keep
                // its layout: the Melnorme, the Ur-Quan wreck, the ship vault,
                // the Zoq-Fot-Pik scout, the Taalo protector and the bomb.
                return Default(rng, star, p);
        }
    }

    private const int Octant = UqmMath.FullCircle >> 3;

    /// <summary>GenerateDefault_generatePlanets: FillOrbits, then moon counts.</summary>
    private int Default(SysRandom rng, Star star, WorldDesc[] p)
    {
        int n = Orbits.Fill(rng, star, p, 0, null, false, moons: false);
        for (int i = 0; i < n; ++i)
        {
            byte b = (byte)rng.Random();
            int moons = 0;
            switch (Types[PlanetType(p[i].DataIndex)].Size)
            {
                case PlanetSize.LargeRocky:
                    if (b < 0xFF * 25 / 100)
                    {
                        if (b < 0xFF * 5 / 100)
                            ++moons;
                        ++moons;
                    }
                    break;
                case PlanetSize.GasGiant:
                    if (b < 0xFF * 90 / 100)
                    {
                        if (b < 0xFF * 75 / 100)
                        {
                            if (b < 0xFF * 50 / 100)
                            {
                                if (b < 0xFF * 25 / 100)
                                    ++moons;
                                ++moons;
                            }
                            ++moons;
                        }
                        ++moons;
                    }
                    break;
            }
            p[i].NumPlanets = (byte)moons;
        }
        return n;
    }

    private static int SolPlanets(WorldDesc[] p)
    {
        var rng = new SysRandom(334241042);   // SOL_SEED
        (byte Type, int Radius, byte Moons)[] sol =
        {
            (MetalWorld, EarthRadius * 39 / 100, 0),          // Mercury
            (PrimordialWorld, EarthRadius * 72 / 100, 0),     // Venus
            (WaterWorld | Shielded, EarthRadius, 2),          // Earth
            (DustWorld, EarthRadius * 152 / 100, 0),          // Mars
            (RedGasGiant, EarthRadius * 500 / 100, 4),        // Jupiter
            (OraGasGiant, EarthRadius * 750 / 100, 1),        // Saturn
            (GrnGasGiant, EarthRadius * 1000 / 100, 0),       // Uranus
            (BluGasGiant, EarthRadius * 1250 / 100, 1),       // Neptune
            (PellucidWorld, EarthRadius * 1550 / 100, 0),     // Pluto
        };
        for (int i = 0; i < sol.Length; ++i)
        {
            p[i].RandSeed = rng.Random();
            int angle = UqmMath.NormalizeAngle((byte)((ushort)p[i].RandSeed >> 8));
            if (i == 1)
                angle = UqmMath.NormalizeAngle(UqmMath.FullCircle - angle);
            else if (i == 8)
                angle = UqmMath.FullCircle - Octant;
            p[i].DataIndex = sol[i].Type;
            p[i].Radius = (short)sol[i].Radius;
            p[i].NumPlanets = sol[i].Moons;
            PlaceAt(ref p[i], angle);
        }
        return sol.Length;
    }

    private static void SolMoons(SysRandom rng, int planet, WorldDesc[] m)
    {
        switch (planet)
        {
            case 2: // Earth: the starbase and Luna
                m[0].DataIndex = HierarchyStarbase;
                m[0].Radius = MinMoonRadius;
                PlaceAt(ref m[0], UqmMath.HalfCircle + UqmMath.Quadrant);
                m[1].DataIndex = SelenicWorld;
                m[1].Radius = MinMoonRadius + (MaxMoons - 1) * MoonDelta;
                PlaceAtRandomAngle(rng, ref m[1]);
                break;
            case 4: // Jupiter: Io, Europa, Ganymede, Callisto
                m[0].DataIndex = RadioactiveWorld;
                m[1].DataIndex = HalideWorld;
                m[2].DataIndex = CyanicWorld;
                m[3].DataIndex = PellucidWorld;
                break;
            case 5: // Saturn: Titan
                m[0].DataIndex = AlkaliWorld;
                break;
            case 7: // Neptune: Triton
                m[0].DataIndex = VinylogousWorld;
                break;
        }
    }

    /// <summary>Sets a new orbit radius and moves the world out along its old direction (seed unchanged).</summary>
    private static void Resize(ref WorldDesc w, int radius)
    {
        int angle = UqmMath.ArcTan(w.X, w.Y);
        w.Radius = (short)radius;
        PlaceAt(ref w, angle);
    }

    private static void PlaceAt(ref WorldDesc w, int angle)
    {
        w.X = UqmMath.Cosine(angle, w.Radius);
        w.Y = UqmMath.Sine(angle, w.Radius);
    }

    private static void PlaceAtRandomAngle(SysRandom rng, ref WorldDesc w) =>
        PlaceAt(ref w, UqmMath.NormalizeAngle(rng.RandomWord()));
}
