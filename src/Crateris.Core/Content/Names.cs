// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/starmap.c GetClusterName;
// src/uqm/planets/solarsys.c GetNamedPlanetaryBody, GetPlanetOrMoonName;
// src/uqm/planets/scan.c GetPlanetTitle; planets/generate/gendefault.c and
// gensol.c generateName),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Core.Content;

/// <summary>
/// The names of stars, planets and moons, made from the game strings as
/// UQM makes them. Planets and moons are named as seen from their planet's
/// inner system or orbit, the only places UQM names them. Names are built
/// as bytes and cut where UQM's buffers cut them, then read as UTF-8.
/// </summary>
public sealed class Names
{
    /// <summary>SIS_NAME_SIZE: the room for GLOBAL_SIS (PlanetName), and for the save summary's name.</summary>
    public const int SisNameSize = 16;

    /// <summary>The size of GetPlanetTitle's buffer in PrintCoarseScanPC.</summary>
    private const int TitleBufferSize = 200;

    private readonly StringTable _strings;

    public Names(GameStrings strings) => _strings = strings.Table;

    public static Names Load(ContentPackage package) => new(GameStrings.Load(package));

    /// <summary>GetClusterName: the star's name, its prefix (Alpha, Beta…) if it has one, then its constellation.</summary>
    public string Cluster(Star star)
    {
        var buf = new List<byte>();
        if (star.NamePrefix != 0 && String(GameStrings.StarNumberBase + star.NamePrefix - 1) is byte[] prefix)
        {
            buf.AddRange(prefix);
            buf.Add((byte)' ');
        }
        if (String(star.NamePostfix) is byte[] postfix)
            buf.AddRange(postfix);
        return Text(buf.ToArray());
    }

    /// <summary>
    /// GetNamedPlanetaryBody: the name a world has of its own (Sol's planets
    /// and named moons, the starbase, the Sa-Matra), or null.
    /// </summary>
    public string? Named(Star star, int planet, int? moon) => NamedBytes(star, planet, moon) is byte[] b ? Text(b) : null;

    /// <summary>
    /// GLOBAL_SIS (PlanetName) as the system's generateName leaves it on
    /// entering the planet's inner system: Sol's planets by name, others
    /// "Planet I", "Planet II"… UQM's title over the inner system and orbit.
    /// </summary>
    public string PlanetName(Star star, int planet) => Text(PlanetNameBytes(star, planet));

    /// <summary>
    /// GetPlanetTitle: the orbit screen's title, the world's own name or its
    /// type ("Water World", "Gas Giant").
    /// </summary>
    public string Title(SolarSystem system, int planet, int? moon)
    {
        if (NamedBytes(system.Star, planet, moon) is byte[] named)
            return Text(Copy(named, TitleBufferSize));
        var world = moon is int m ? system.Moons(planet)[m] : system.Planets[planet];
        int type = world.DataIndex & ~WorldTypes.Shielded;
        if (type >= WorldTypes.FirstGasGiant)
            return Text(String(GameStrings.ScanBase + 4 + 51) ?? Array.Empty<byte>());   // Gas Giant
        return Text([.. String(GameStrings.ScanBase + 4 + type) ?? Array.Empty<byte>(), (byte)' ',
            .. String(GameStrings.ScanBase + 4 + 50) ?? Array.Empty<byte>()]);              // World
    }

    /// <summary>
    /// GetPlanetOrMoonName: the world's own name, or the planet's name, with
    /// "-A", "-B"… after it for a moon; cut to <paramref name="bufferSize"/>
    /// bytes with the terminating NUL (the save summary gives it <see cref="SisNameSize"/>).
    /// </summary>
    public string PlanetOrMoon(Star star, int planet, int? moon, int bufferSize = SisNameSize)
    {
        if (NamedBytes(star, planet, moon) is byte[] named)
            return Text(Copy(named, bufferSize));
        var buf = new List<byte>(Copy(PlanetNameBytes(star, planet), bufferSize));
        if (moon is int m && bufferSize - buf.Count >= 3)
        {
            buf.Add((byte)'-');
            buf.Add((byte)('A' + m));
        }
        return Text(buf.ToArray());
    }

    private byte[]? NamedBytes(Star star, int planet, int? moon)
    {
        switch (star.Special)
        {
            case "SOL_DEFINED":
                if (moon is not int m)
                    return String(GameStrings.PlanetNumberBase + planet);
                return (planet, m) switch
                {
                    (2, 0) => String(GameStrings.StarbaseBase + 0),        // Starbase
                    (2, 1) => String(GameStrings.PlanetNumberBase + 9),    // Luna
                    (4, 0) => String(GameStrings.PlanetNumberBase + 10),   // Io
                    (4, 1) => String(GameStrings.PlanetNumberBase + 11),   // Europa
                    (4, 2) => String(GameStrings.PlanetNumberBase + 12),   // Ganymede
                    (4, 3) => String(GameStrings.PlanetNumberBase + 13),   // Callisto
                    (5, 0) => String(GameStrings.PlanetNumberBase + 14),   // Titan
                    (7, 0) => String(GameStrings.PlanetNumberBase + 15),   // Triton
                    _ => null,
                };
            case "SAMATRA_DEFINED" when planet == 4 && moon == 0:
                return String(GameStrings.PlanetNumberBase + 32);          // Sa-Matra
            default:
                // Spathiwa is named only under NOTYET in UQM 0.8.0.
                return null;
        }
    }

    private byte[] PlanetNameBytes(Star star, int planet)
    {
        int index = star.Special == "SOL_DEFINED"
            ? GameStrings.PlanetNumberBase + planet                    // GenerateSol_generateName
            : GameStrings.PlanetNumberBase + (9 + 7) + planet;         // GenerateDefault_generateName
        return Copy(String(index) ?? Array.Empty<byte>(), SisNameSize);
    }

    // GAME_STRING (index) without its NUL, or null where UQM has none.
    private byte[]? String(int index)
    {
        if (index < 0 || index >= _strings.Count)
            return null;
        var raw = _strings.Raw(index);
        return raw.Length == 0 ? null : raw[..^1].ToArray();
    }

    // utf8StringCopy: strncpy into size bytes, the last one a NUL.
    private static byte[] Copy(byte[] s, int size)
    {
        if (size <= 0)
            return Array.Empty<byte>();
        int end = Array.IndexOf(s, (byte)0);
        int length = Math.Min(end >= 0 ? end : s.Length, size - 1);
        return s[..length];
    }

    private static string Text(byte[] b) => Encoding.UTF8.GetString(b);
}
