// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>The tests' way to the devices held by ships (stage 3c item 5): a player's ship's, by a device's story field.</summary>
internal static class TestDevices
{
    /// <summary>A player's ship holds a device by its field's value (0 none), a recorded one's story record set with it, as the set-up options do.</summary>
    public static void SetDevice(this GameWorld world, string player, string field, int value = 1)
    {
        PlayerTalkShip.SetDeviceOf(world, world.Find(player)!, field, value);
        if (DeviceTable.RecordedFields.Contains(field))
            world.Story!.Set(field, value);
    }

    /// <summary>The value a device's field has on a player's ship (0 if it holds none of it).</summary>
    public static int Device(this GameWorld world, string player, string field) =>
        PlayerTalkShip.DeviceOf(world, world.Find(player)!, field);
}
