// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text.Json;

namespace Crateris.Core.Tests;

/// <summary>Reference files recorded from UQM's own code (tests/golden).</summary>
internal static class Golden
{
    public static JsonElement Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "golden", name + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    public static int[] Ints(this JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();

    public static long[] Longs(this JsonElement e) => e.EnumerateArray().Select(x => x.GetInt64()).ToArray();

    /// <summary>
    /// Whether the tests run to map which source files each topic runs
    /// (tools/run_tests.py --map, with coverage): the every-world loops
    /// then take a few worlds (<see cref="Sample{T}"/>), as only which code
    /// runs matters, not the results.
    /// </summary>
    public static bool Mapping => Environment.GetEnvironmentVariable("CRATERIS_TOPIC_MAP") == "1";

    /// <summary>All the cases, or a few of them while mapping (<see cref="Mapping"/>).</summary>
    public static List<T> Sample<T>(this IEnumerable<T> all) => Mapping ? all.Take(24).ToList() : all.ToList();

    /// <summary>The SHA-256 of some bytes in lower-case hex, as the reference files give content.</summary>
    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
