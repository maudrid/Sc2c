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
/// The devices (stage 3a, item 10), as data/devices.json has them from
/// devices.c, and who holds them (stage 3c item 5; the owner's answer to
/// the stage 3c plan's question 5): devices are held by ships, not by the
/// group. The portal spawner is every player's once anyone has it, and the
/// Lunar base is the opening's: both stay shared story fields
/// (<see cref="SharedFields"/>). Every other device is held by one ship at
/// a time (<see cref="World.ShipHold.Devices"/>, by its id), its story
/// field kept out of the shared story, but for the three whose field is
/// also the story's only record that the thing was found, sold or given
/// (<see cref="RecordedFields"/>): those keep their shared field as that
/// record, apart from the holding. Each player's Manifest lists the
/// shared devices and their own ship's. Using them is stage 3c item 8.
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

    /// <summary>The story fields that are things the players carry, shared or held by ships.</summary>
    public IEnumerable<string> Fields => All.Where(d => d.Listed).Select(d => d.Field!).Distinct();

    /// <summary>
    /// The devices' fields that stay shared story (the plan's question 5):
    /// the portal spawner, every player's once anyone has it, and the Lunar
    /// base, the opening's (with the destruct code, which devices.c never
    /// lists: knowledge, not an object).
    /// </summary>
    public static readonly IReadOnlySet<string> SharedFields = new HashSet<string> { "PORTAL_SPAWNER_ON_SHIP", "MOONBASE_ON_SHIP" };

    /// <summary>
    /// The held devices' fields whose shared field stays as the story's
    /// record that the thing was found, sold or given (the plan's readings
    /// of question 5): ULTRON_CONDITION (the Supox's and their ruins'
    /// "already given", 5 once given back to the Utwig) and the Druuge's
    /// Trident and Rod (their only "already sold"). The holder carries the
    /// device itself (the Ultron with its repair level).
    /// </summary>
    public static readonly IReadOnlySet<string> RecordedFields = new HashSet<string>
    {
        "ULTRON_CONDITION", "ARTIFACT_2_ON_SHIP", "ARTIFACT_3_ON_SHIP",
    };

    /// <summary>
    /// Every device field held by ships rather than shared (each a listed
    /// device's, not <see cref="SharedFields"/>), as devices.json has them;
    /// those not <see cref="RecordedFields"/> are kept out of the shared
    /// story altogether (<see cref="StoryState.NotShared"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> HeldFields = new[]
    {
        "TALKING_PET_ON_SHIP", "UTWIG_BOMB_ON_SHIP", "SUN_DEVICE_ON_SHIP", "ROSY_SPHERE_ON_SHIP", "AQUA_HELIX_ON_SHIP",
        "CLEAR_SPINDLE_ON_SHIP", "ULTRON_CONDITION", "MAIDENS_ON_SHIP", "UMGAH_BROADCASTERS_ON_SHIP",
        "BURV_BROADCASTERS_ON_SHIP", "TAALO_PROTECTOR_ON_SHIP", "EGG_CASE0_ON_SHIP", "EGG_CASE1_ON_SHIP",
        "EGG_CASE2_ON_SHIP", "SYREEN_SHUTTLE_ON_SHIP", "VUX_BEAST_ON_SHIP", "PORTAL_KEY_ON_SHIP",
        "ARTIFACT_2_ON_SHIP", "ARTIFACT_3_ON_SHIP",
    };

    private static readonly HashSet<string> Held = new(HeldFields);

    /// <summary>Whether a story field is a device held by ships (<see cref="HeldFields"/>).</summary>
    public static bool HeldByShips(string field) => Held.Contains(field);

    /// <summary>Whether a device (by id) is held by ships: listed, its field not shared.</summary>
    public bool IsHeld(int device) =>
        device >= 0 && device < All.Count && All[device] is { Listed: true, Field: { } f } && Held.Contains(f);

    /// <summary>
    /// The value a ship's holding gives a held device's field (by the
    /// devices' ids it holds): the held device's value (the Ultron's
    /// repairs, 1 to 4), or 1; 0 if it holds none of that field's.
    /// </summary>
    public int ValueOf(IEnumerable<int> held, string field)
    {
        foreach (int id in held)
            if (id >= 0 && id < All.Count && All[id].Field == field)
                return All[id].Value ?? 1;
        return 0;
    }

    /// <summary>
    /// The devices a ship holds once a held field is set to
    /// <paramref name="value"/> (SET_GAME_STATE on the holder): its devices
    /// of that field gone, and the one the value says added (none for 0, or
    /// for a value no device of the field has: the Ultron's 5, given back).
    /// </summary>
    public SortedSet<int> WithValue(IEnumerable<int> held, string field, int value)
    {
        var set = new SortedSet<int>(held.Where(id => id < 0 || id >= All.Count || All[id].Field != field));
        if (value != 0 && All.FirstOrDefault(d => d.Listed && d.Field == field && (d.Value is not int v || v == value)) is { } added)
            set.Add(added.Id);
        return set;
    }

    /// <summary>
    /// A ship's Devices list (InventoryDevices): the shared devices the
    /// story has (<paramref name="shared"/>, by field) and the held ones the
    /// ship holds, in globdata.h's order.
    /// </summary>
    public List<int> Inventory(Func<string, int> shared, IReadOnlyCollection<int> held) =>
        All.Where(d => d.Listed && (Held.Contains(d.Field!) ? held.Contains(d.Id)
            : d.Value is int v ? shared(d.Field!) == v : shared(d.Field!) != 0)).Select(d => d.Id).ToList();

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

    /// <summary>
    /// InventoryDevices as UQM's one ship has it: the devices aboard, in
    /// order, every one by its story field's value (UQM's GameState, as the
    /// recording has it).
    /// </summary>
    public List<int> Inventory(Func<string, int> get) =>
        All.Where(d => d.Listed && (d.Value is int v ? get(d.Field!) == v : get(d.Field!) != 0)).Select(d => d.Id).ToList();

    /// <summary>A device's name in the game strings (GAME_STRING (DEVICE_STRING_BASE + 1 + device)).</summary>
    public static int NameString(int device) => Content.GameStrings.DeviceBase + 1 + device;
}
