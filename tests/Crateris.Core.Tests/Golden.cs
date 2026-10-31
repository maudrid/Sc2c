// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

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
}
