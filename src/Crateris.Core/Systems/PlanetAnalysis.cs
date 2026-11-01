// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/calc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>
/// A world's planet data (UQM's PLANET_INFO), as shown in the planet report.
/// Units as in UQM: temperature in °C, gravity and radius in 1/100 of
/// Earth's, density in 1/100 of Earth's, atmosphere in 1/50 of Earth's
/// (0xFFFF for gas giants), rotation in 1/10 hours, tectonics and weather
/// as classes 0..7.
/// </summary>
public sealed record PlanetInfo(
    short AxialTilt,
    ushort Tectonics,
    ushort Weather,
    ushort PlanetDensity,
    ushort PlanetRadius,
    ushort SurfaceGravity,
    short SurfaceTemperature,
    ushort RotationPeriod,
    ushort AtmoDensity,
    short LifeChance,
    ushort PlanetToSunDist,
    uint SeedAfter)
{
    /// <summary>THIN_ATMOSPHERE and so on (plandata.h).</summary>
    public const ushort GasGiantAtmosphere = 0xFFFF;
}

/// <summary>
/// DoPlanetaryAnalysis from calc.c: a world's planet data from its seed,
/// its planet type and the star.
/// </summary>
public static class PlanetAnalysis
{
    private const int ColdThreshold = -40;
    private const int HotThreshold = 100;
    private const int Light = 0, Medium = 1, Heavy = 2;   // atmosphere classes

    /// <summary>
    /// Analyses a world. <paramref name="sunDistance"/> is the planet's orbit
    /// radius (for a moon, its planet's). UQM calls this two ways: with
    /// <paramref name="greenhouse"/> false when drawing orbit colours, and
    /// true on entering orbit (the planet report), where a greenhouse bonus
    /// is added to the temperature. The two make different RNG calls, so
    /// every value after the temperature can differ between them.
    /// </summary>
    public static PlanetInfo Analyse(PlanetTypes types, Star star, uint randSeed, byte dataIndex,
        short sunDistance, bool greenhouse)
    {
        if (!WorldTypes.HasPlanetData(dataIndex))
            throw new ArgumentException($"world type {dataIndex} has no planet data", nameof(dataIndex));

        var rng = new SysRandom(randSeed);
        PlanetType type = types[WorldTypes.PlanetType(dataIndex)];

        // CalcSysInfo
        int intensity = star.Color switch
        {
            StarColor.Red => 0,
            StarColor.Orange => 1,
            StarColor.Yellow => 2,
            StarColor.Green => 3,
            StarColor.Blue => 4,
            _ => 5, // white
        };
        int starEnergy = (intensity + 1) * star.Size switch
        {
            StarSize.Dwarf => 1,
            StarSize.Giant => 5,
            _ => 20,
        };

        short radius = sunDistance;
        short temperature = CalcTemp(rng, starEnergy, radius, greenhouse, type);

        int density = type.DensityClass switch
        {
            0 => 20,   // GAS_DENSITY
            1 => 33,   // LIGHT_DENSITY
            2 => 60,   // LOW_DENSITY
            3 => 100,  // NORMAL_DENSITY
            4 => 150,  // HIGH_DENSITY
            _ => 200,  // SUPER_DENSITY
        };
        density = (ushort)(density + (density / 20) - (rng.RandomWord() % (density / 10)));

        ushort planetRadius = type.Size switch
        {
            PlanetSize.SmallRocky => HalfBaseVariance(rng, 25),
            PlanetSize.LargeRocky => HalfBaseVariance(rng, 75),
            _ => FromBase(rng, 300, 1500 - 300),
        };

        // CalcRotation
        ushort rotation;
        if (type.Size == PlanetSize.GasGiant)
            rotation = FromBase(rng, 80, 80);
        else if ((byte)rng.Random() % 10 == 0)
            rotation = FromBase(rng, 50 * 240, 200 * 240);
        else
            rotation = FromBase(rng, 150, 150);

        ushort gravity = (ushort)((uint)density * planetRadius / 100);

        // CalcTilt: ten tosses for a bell curve.
        short tilt = -(180 / 2);
        for (int i = 0; i < 10; ++i)
            tilt = (short)(tilt + rng.RandomWord() % ((180 / 10) + 1));

        // CalcTectonics
        int tectonics = FromBase(rng, (ushort)type.BaseTectonics, 3 << 5);
        if (tectonics > 255)
            tectonics = 255;

        // GeneratePlanetComposition
        ushort weather;
        ushort atmo;
        if (type.Size == PlanetSize.GasGiant)
        {
            weather = 7 << 5;
            atmo = PlanetInfo.GasGiantAtmosphere;
        }
        else
        {
            weather = 0;
            atmo = 0;
            byte range = (byte)type.AtmosphereClass;
            if (range <= Heavy)
            {
                if (temperature < ColdThreshold)
                    --range;   // BYTE: LIGHT becomes 255, so no atmosphere
                else if (temperature > HotThreshold)
                    ++range;

                if (range <= Heavy + 1)
                {
                    switch (range)
                    {
                        case Light:
                            atmo = 10;   // THIN_ATMOSPHERE
                            weather = 1 << 5;
                            break;
                        case Medium:
                            atmo = 75;   // NORMAL_ATMOSPHERE
                            weather = 2 << 5;
                            break;
                        case Heavy:
                            atmo = 200;  // THICK_ATMOSPHERE
                            weather = 4 << 5;
                            break;
                        default:
                            atmo = 2500; // SUPER_THICK_ATMOSPHERE
                            weather = 6 << 5;
                            break;
                    }

                    int r = radius / WorldTypes.EarthRadius;
                    if (r < 2)
                        weather += 1 << 5;
                    else if (r > 10)
                        weather -= 1 << 5;
                    atmo = HalfBaseVariance(rng, atmo);
                }
            }
        }

        ushort tect = (ushort)(tectonics >> 5);
        weather >>= 5;

        short life = CalcLifeChance(type, temperature, atmo);

        return new PlanetInfo(tilt, tect, weather, (ushort)density, planetRadius, gravity,
            temperature, rotation, atmo, life, (ushort)radius, rng.Seed);
    }

    /// <summary>
    /// The orbit colour LoadSolarSys gives a planet: 0..4, cold to hot,
    /// from the temperature without the greenhouse bonus.
    /// </summary>
    public static int TemperatureColorIndex(short temperature)
    {
        // COUNT index = (temp + 250) / 100: unsigned, so very cold worlds
        // (below -349) wrap round and get the hottest colour, as in UQM.
        ushort index = (ushort)((temperature + 250) / 100);
        return index >= 5 ? 4 : index;
    }

    private static short CalcTemp(SysRandom rng, int starEnergy, short radius, bool greenhouse, PlanetType type)
    {
        const uint albedo = 100 - 33;
        uint alb = (uint)UqmMath.SquareRoot(UqmMath.SquareRoot(albedo * 100 * 10000))
                   * 277u * (uint)starEnergy / ((2 + 1) * 1);
        short centigrade = (short)((short)(alb / UqmMath.SquareRoot((uint)(radius * 10000L / WorldTypes.EarthRadius))) - 273);

        short bonus = 0;
        if (greenhouse && type.AtmosphereClass <= Heavy)
        {
            if (centigrade >= HotThreshold)
                bonus = 200;
            else if (centigrade >= ColdThreshold)
                bonus = 20;

            bonus = (short)(bonus << type.AtmosphereClass);
            bonus = (short)HalfBaseVariance(rng, (ushort)bonus);
        }

        return (short)(centigrade + bonus);
    }

    private static short CalcLifeChance(PlanetType type, short temp, ushort atmo)
    {
        if (type.Size == PlanetSize.GasGiant)
            return -1;

        int life = 0;
        if (temp < -151) life -= 300;
        else if (temp < -51) life -= 100;
        else if (temp < 0) life += 100;
        else if (temp < 50) life += 300;
        else if (temp < 150) life += 50;
        else if (temp < 250) life -= 100;
        else if (temp < 500) life -= 400;
        else life -= 800;

        if (atmo == 0) life -= 1000;
        else if (atmo < 15) life += 100;
        else if (atmo < 30) life += 200;
        else if (atmo < 100) life += 300;
        else if (atmo < 1000) life += 150;
        else if (atmo < 2500) { }
        else life -= 100;

        life += 200 + 80 + 80;
        return (short)life;
    }

    private static ushort FromBase(SysRandom rng, ushort baseValue, ushort variance) =>
        (ushort)(baseValue + rng.RandomWord() % variance);

    private static ushort HalfBaseVariance(SysRandom rng, ushort baseValue) =>
        FromBase(rng, baseValue, (ushort)((baseValue >> 1) + 1));
}
