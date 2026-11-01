// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>Star sizes (STAR_TYPE in plandata.h).</summary>
public enum StarSize { Dwarf = 0, Giant = 1, SuperGiant = 2 }

/// <summary>Star colours (STAR_COLOR in plandata.h).</summary>
public enum StarColor { Blue = 0, Green = 1, Orange = 2, Red = 3, White = 4, Yellow = 5 }

/// <summary>
/// One star of the map (STAR_DESC). <see cref="Special"/> is the name of its
/// special generator (STAR_DESC.Index, for example "SOL_DEFINED"), or null.
/// <see cref="NamePrefix"/> and <see cref="NamePostfix"/> are STAR_DESC's
/// Prefix and Postfix: what its name is made of, in the game strings
/// (<see cref="Crateris.Core.Content.Names.Cluster"/>).
/// </summary>
public sealed record Star(int Number, short X, short Y, StarSize Size, StarColor Color, string? Special,
    int NamePrefix = 0, int NamePostfix = 0)
{
    /// <summary>GetRandomSeedForStar: MAKE_DWORD (x, y).</summary>
    public uint Seed => ((uint)(ushort)Y << 16) | (ushort)X;
}

/// <summary>The stars of the map, from data/starmap.json, in UQM's order.</summary>
public sealed class StarMap
{
    private StarMap(IReadOnlyList<Star> stars)
    {
        Stars = stars;
    }

    public IReadOnlyList<Star> Stars { get; }

    /// <summary>NUM_SOLAR_SYSTEMS: the stars of the hyperspace map.</summary>
    public const int SolarSystemCount = 502;

    public static StarMap Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "starmap.json")));
        var stars = new List<Star>();
        foreach (var s in doc.RootElement.GetProperty("stars").EnumerateArray())
        {
            var special = s.GetProperty("special");
            stars.Add(new Star(
                stars.Count,
                (short)s.GetProperty("x").GetInt32(),
                (short)s.GetProperty("y").GetInt32(),
                ParseSize(s.GetProperty("size").GetString()!),
                ParseColor(s.GetProperty("color").GetString()!),
                special.ValueKind == JsonValueKind.Null ? null : special.GetString(),
                s.GetProperty("name_prefix").GetInt32(),
                s.GetProperty("name_postfix").GetInt32()));
        }
        if (stars.Count != SolarSystemCount)
            throw new InvalidDataException($"starmap.json: {stars.Count} stars, expected {SolarSystemCount}");
        for (int i = 1; i < stars.Count; ++i)
        {
            if (stars[i].Y < stars[i - 1].Y)
                throw new InvalidDataException("starmap.json: stars must be sorted by y");
        }
        return new StarMap(stars);
    }

    /// <summary>Stars with y in [minY, maxY], found by binary search (the map is sorted by y, as UQM's FindStar relies on).</summary>
    public IEnumerable<Star> InYRange(int minY, int maxY)
    {
        int lo = 0, hi = Stars.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Stars[mid].Y < minY)
                lo = mid + 1;
            else
                hi = mid;
        }
        for (int i = lo; i < Stars.Count && Stars[i].Y <= maxY; ++i)
            yield return Stars[i];
    }

    /// <summary>The star at exactly these universe coordinates, or null.</summary>
    public Star? At(short x, short y) => Stars.FirstOrDefault(s => s.X == x && s.Y == y);

    private static StarSize ParseSize(string s) => s switch
    {
        "DWARF_STAR" => StarSize.Dwarf,
        "GIANT_STAR" => StarSize.Giant,
        "SUPER_GIANT_STAR" => StarSize.SuperGiant,
        _ => throw new InvalidDataException($"unknown star size {s}"),
    };

    private static StarColor ParseColor(string s) => s switch
    {
        "BLUE_BODY" => StarColor.Blue,
        "GREEN_BODY" => StarColor.Green,
        "ORANGE_BODY" => StarColor.Orange,
        "RED_BODY" => StarColor.Red,
        "WHITE_BODY" => StarColor.White,
        "YELLOW_BODY" => StarColor.Yellow,
        _ => throw new InvalidDataException($"unknown star colour {s}"),
    };
}
