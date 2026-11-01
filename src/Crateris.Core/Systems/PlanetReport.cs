// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/scan.c PrintCoarseScanPC,
// PrintCoarseScan3DO and MakeScanValue), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Globalization;

namespace Crateris.Core.Systems;

/// <summary>How the orbit screen shows a world's atmosphere.</summary>
public enum AtmosphereReading { Value, Vacuum, SuperThick }

/// <summary>
/// The numbers on the orbit screen's planet report (UQM's coarse scan), in
/// the units UQM shows them. Values marked ×100 are hundredths: 152 is
/// 1.52. Labels and unit words are content strings, added by the client.
/// Both of UQM's report styles (PC text and 3DO) show these same numbers.
/// </summary>
public sealed record PlanetReport(
    int OrbitAu100,
    AtmosphereReading Atmosphere,
    int Atmospheres100,
    int TemperatureC,
    int? WeatherClass,
    int? TectonicsClass,
    int EarthMasses100,
    int EarthRadii100,
    int Gravity100,
    int Days100,
    int TiltDegrees)
{
    private const int EarthRadius = WorldTypes.EarthRadius;
    private const int EarthAtmosphere = 50;

    public static PlanetReport From(PlanetInfo info, bool gasGiant)
    {
        int orbit = (int)((info.PlanetToSunDist * 100L + (EarthRadius >> 1)) / EarthRadius);

        AtmosphereReading atmo = info.AtmoDensity switch
        {
            PlanetInfo.GasGiantAtmosphere => AtmosphereReading.SuperThick,
            0 => AtmosphereReading.Vacuum,
            _ => AtmosphereReading.Value,
        };
        int atm = atmo == AtmosphereReading.Value
            ? (info.AtmoDensity * 100 + (EarthAtmosphere >> 1)) / EarthAtmosphere : 0;

        // DWORD cube, then 64-bit arithmetic (UQM's long, as on Linux).
        uint r = info.PlanetRadius;
        uint cube = unchecked(r * r * r);
        long mass = ((long)cube / 100L * info.PlanetDensity + ((100L * 100L) >> 1)) / (100L * 100L);
        if (mass == 0)
            mass = 1;

        int gravity = info.SurfaceGravity == 0 ? 1 : info.SurfaceGravity;

        return new PlanetReport(
            orbit, atmo, atm, info.SurfaceTemperature,
            info.AtmoDensity == 0 ? null : info.Weather + 1,
            gasGiant ? null : info.Tectonics + 1,
            (int)mass, info.PlanetRadius, gravity,
            info.RotationPeriod * 10 / 24,
            Math.Abs((int)info.AxialTilt));
    }

    /// <summary>MakeScanValue's number: two decimals below 10, one from 10 up ("0.39", "243.0").</summary>
    public static string ScanValue(long hundredths) => hundredths >= 10 * 100
        ? string.Create(CultureInfo.InvariantCulture, $"{hundredths / 100}.{hundredths / 10 % 10}")
        : string.Create(CultureInfo.InvariantCulture, $"{hundredths / 100}.{hundredths % 100:D2}");
}
