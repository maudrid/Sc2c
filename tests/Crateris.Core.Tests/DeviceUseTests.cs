// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Story;

namespace Crateris.Core.Tests;

/// <summary>
/// What using each device does (the stage 3c plan's item 8), as UQM's own
/// InvokeDevice has it (tests/golden/device-uses.json, from planets/
/// devices.c): <see cref="DeviceUses.Invoke"/> by where the ship is and
/// the story. The game's side (who may use one, and what the use starts)
/// is DeviceUsesInGameTests'.
/// </summary>
[Trait("Topic", "story")]
public class DeviceUseTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly JsonElement Recorded = Golden.Load("device-uses").GetProperty("scenarios");

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! }).Sample();

    [Fact]
    public void EveryDeviceIsRecordedInEveryKindOfPlace()
    {
        var names = Recorded.EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var d in Rules.Devices.All)
            foreach (var place in new[] { "hyperspace", "quasispace", "system", "orbit" })
                Assert.Contains($"{d.Name.ToLowerInvariant()}-{place}", names);
        Assert.True(names.Count > 200);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TheUseIsUqms(string name)
    {
        var sc = Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var p = sc.GetProperty("place");
        var place = new DevicePlace(
            p.GetProperty("activity").GetString() == "hyperspace",
            p.GetProperty("orbit").GetInt32() == 1,
            p.GetProperty("star").ValueKind == JsonValueKind.Null ? null : p.GetProperty("star").GetString(),
            p.GetProperty("planet").GetInt32(),
            p.GetProperty("moon").GetInt32() is var moon && moon >= 0 ? moon : null,
            p.GetProperty("first_group").ValueKind == JsonValueKind.Null ? null : p.GetProperty("first_group").GetString(),
            p.GetProperty("encounter").GetInt32() == 1,
            (uint)p.GetProperty("fuel").GetInt32());
        var state = sc.GetProperty("state").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32());
        var before = new Dictionary<string, int>(state);
        bool alive = p.GetProperty("ilwrath_alive").GetInt32() == 1;
        var use = DeviceUses.Invoke(Rules.Devices, sc.GetProperty("device").GetInt32(), place,
            f => state.GetValueOrDefault(f),
            (f, v) => state[f] = v & Rules.Table[f].Max,   // SET_GAME_STATE keeps the field's bits
            () => alive);

        Assert.Equal(sc.GetProperty("status").GetInt32(), (int)use.Status);
        var writes = state.Where(kv => before.GetValueOrDefault(kv.Key) != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        var recorded = sc.GetProperty("writes").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32());
        Assert.Equal(recorded.OrderBy(kv => kv.Key), writes.OrderBy(kv => kv.Key));
        Assert.Equal(sc.GetProperty("encounter").GetInt32() == 1, use.StartEncounter);
        Assert.Equal(sc.GetProperty("load").GetInt32() == 1, use.FakeLoad);
        Assert.Equal(sc.GetProperty("battle").GetInt32() == 0, use.LeftBattle);
        Assert.Equal(sc.GetProperty("crew").GetInt32() == 0xFFFF, use.CrewGone);
        Assert.Equal((uint)p.GetProperty("fuel").GetInt32() - (uint)sc.GetProperty("fuel").GetInt32(), use.FuelCost);
        var calls = sc.GetProperty("calls").EnumerateArray().Select(c => c[0].GetString()! switch
        {
            "sound" => new DeviceCall("sound", c[1].GetInt32()),
            "fade" => new DeviceCall("fade", c[1].GetInt32(), c[2].GetInt32()),
            "put" => new DeviceCall("put", c[1].GetInt32(), c[2].GetInt32()),
            "sphere" or "clone" => new DeviceCall(c[0].GetString()!, Race: c[1].GetString()),
            var k => new DeviceCall(k),
        }).ToList();
        Assert.Equal(calls, use.Calls);
        // What it sets going follows from what it did.
        Assert.Equal(use.Status == DeviceStatus.Failure, use.Action == DeviceAction.None);
    }
}
