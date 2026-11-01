// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using Crateris.Core.Content;

namespace Crateris.Core.Tests;

/// <summary>
/// String tables against what UQM's own _GetStringData reads: inputs of
/// our own byte for byte (tests/golden/string-table-cases.json), every
/// string table of the real package as hashes (string-tables.json), and
/// the numbering of gamestr.h (string-numbering.json).
/// </summary>
public class StringTableTests
{
    [Fact]
    public void TablesAreReadAsUqmReadsThem()
    {
        foreach (var c in Golden.Load("string-table-cases").GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            var table = StringTable.Parse(Convert.FromHexString(c.GetProperty("input").GetString()!));
            var want = c.GetProperty("strings").EnumerateArray().ToList();
            Assert.True(want.Count == table.Count, $"{name}: {table.Count} strings, UQM {want.Count}");
            for (int i = 0; i < want.Count; ++i)
            {
                int length = want[i][0].GetInt32();
                string hex = want[i][1].GetString()!;
                Assert.True(length == table.Raw(i).Length && hex == Convert.ToHexString(table.Raw(i)).ToLowerInvariant(),
                    $"{name} string {i}: [{table.Raw(i).Length}, {Convert.ToHexString(table.Raw(i))}], UQM [{length}, {hex}]");
            }
        }
    }

    [Fact]
    public void StringsReadAsText()
    {
        var table = StringTable.Parse(Encoding.UTF8.GetBytes("#(A)\nTemp: \n#(B)\n#(C)\nCafé\nau lait\n"));
        Assert.Equal("Temp: ", table[0]);
        Assert.Equal("", table[1]);
        Assert.Equal("Café\nau lait", table[2]);
    }

    [Fact]
    public void TheNumberingIsGamestrH()
    {
        var golden = Golden.Load("string-numbering");
        Assert.Equal(GameStrings.Count, golden.GetProperty("GAMESTR_COUNT").GetInt32());
        var want = golden.GetProperty("ranges").EnumerateArray()
            .Select(r => (r[0].GetString()!, r[1].GetInt32(), r[2].GetInt32())).ToList();
        Assert.Equal(want, GameStrings.Ranges);
    }

    [ContentFact]
    public void EveryStringTableInThePackageIsReadAsUqmReadsIt()
    {
        TestContent.With(package =>
        {
            var golden = Golden.Load("string-tables").GetProperty("tables");
            int tables = 0, strings = 0;
            foreach (var t in golden.EnumerateObject())
            {
                var table = StringTable.Load(package, t.Name);
                var want = t.Value.GetProperty("strings").EnumerateArray().ToList();
                Assert.True(want.Count == table.Count, $"{t.Name}: {table.Count} strings, UQM {want.Count}");
                for (int i = 0; i < want.Count; ++i)
                {
                    string hash = Convert.ToHexString(SHA256.HashData(table.Raw(i))).ToLowerInvariant();
                    Assert.True(want[i][0].GetInt32() == table.Raw(i).Length && want[i][1].GetString() == hash,
                        $"{t.Name} string {i} differs from UQM's");
                }
                tables++;
                strings += table.Count;
            }
            Assert.Equal(package.Resources.Values.Count(r => r.Type == "STRTAB"), tables);
            Assert.True(strings > 1000, $"only {strings} strings");
        });
    }

    [ContentFact]
    public void AFewGameStringsReadAsInTheGame()
    {
        var game = TestContent.With(GameStrings.Load);
        Assert.Equal("Vega", game[GameStrings.StarBase]);
        Assert.Equal("Orbit: ", game[GameStrings.OrbitScanBase]);
        Assert.Equal("Earth", game[GameStrings.PlanetNumberBase + 2]);
        Assert.Equal("Luna", game[GameStrings.PlanetNumberBase + 9]);
    }
}
