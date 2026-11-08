// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/devices.c's
// InventoryDevices), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using System.Text.Json;

namespace Crateris.Core.Story;

/// <summary>
/// A device (globdata.h's device enum): the story field that says it's
/// aboard (aboard when it isn't 0, or, with a value, when it's that value:
/// the Ultron's repairs), and whether the Manifest ever lists it.
/// </summary>
public sealed record Device(string Name, int Id, string? Field, int? Value, bool Listed);

/// <summary>
/// The group inventory (stage 3a, item 10): the things the group carries,
/// as data/devices.json has them from devices.c. They're story fields,
/// shared by the whole group (docs/differences.md row 10), so every
/// player's Manifest lists the same devices. Using them is stage 3c.
/// </summary>
public sealed class DeviceTable
{
    /// <summary>MAX_VIS_DEVICES: the rows the Devices list shows at once.</summary>
    public const int VisibleRows = 5;

    /// <summary>The first device icon in MiscDataFrame (DrawDevicesDisplay's 77 + device).</summary>
    public const int FirstIconFrame = 77;

    public DeviceTable(IReadOnlyList<Device> devices) => All = devices;

    /// <summary>Every device, in globdata.h's order.</summary>
    public IReadOnlyList<Device> All { get; }

    /// <summary>The story fields that are things the group carries.</summary>
    public IEnumerable<string> Fields => All.Where(d => d.Listed).Select(d => d.Field!).Distinct();

    public static DeviceTable Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "devices.json")));
        var devices = doc.RootElement.GetProperty("devices").EnumerateArray().Select(d => new Device(
            d.GetProperty("device").GetString()!, d.GetProperty("id").GetInt32(),
            d.TryGetProperty("field", out var f) ? f.GetString() : null,
            d.TryGetProperty("value", out var v) ? v.GetInt32() : null,
            d.GetProperty("listed").GetBoolean())).ToList();
        if (devices.Where((d, i) => d.Id != i).Any())
            throw new InvalidDataException("devices.json: the devices aren't numbered in order from 0");
        return new DeviceTable(devices);
    }

    /// <summary>InventoryDevices: the devices aboard, in order, by a story field's value.</summary>
    public List<int> Inventory(Func<string, int> get) =>
        All.Where(d => d.Listed && (d.Value is int v ? get(d.Field!) == v : get(d.Field!) != 0)).Select(d => d.Id).ToList();

    /// <summary>A device's name in the game strings (GAME_STRING (DEVICE_STRING_BASE + 1 + device)).</summary>
    public static int NameString(int device) => Content.GameStrings.DeviceBase + 1 + device;
}
