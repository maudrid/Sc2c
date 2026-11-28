// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/planets/devices.c's
// InvokeDevice and UseCaster), Copyright Paul Reiche, Fred Ford and Toys
// for Bob, Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2
// or later.

namespace Crateris.Core.Story;

/// <summary>What a device's use came to (devices.c's DeviceStatus).</summary>
public enum DeviceStatus
{
    /// <summary>DEVICE_FAILURE: nothing happens, the failure sound, the list stays open.</summary>
    Failure = 0,

    /// <summary>DEVICE_SUCCESS: the invoked sound, the list closes.</summary>
    Success = 1,

    /// <summary>DEVICE_SUCCESS_NO_SOUND: the list closes (the Sun Device made its own sound).</summary>
    SuccessNoSound = 2,
}

/// <summary>What a device's use sets going, beyond its story writes.</summary>
public enum DeviceAction
{
    /// <summary>Nothing more (a failure, or the story writes alone).</summary>
    None,

    /// <summary>One more piece of the Ultron mended (ULTRON_CONDITION one up, the piece gone).</summary>
    Mended,

    /// <summary>A broadcaster used in hyperspace: the Melnorme called (USED_BROADCASTER).</summary>
    CalledMelnorme,

    /// <summary>A broadcaster in orbit of the Chmmr's world before they're freed: their shielded talk (GLOBAL_FLAGS_AND_DATA bit 7).</summary>
    ChmmrTalk,

    /// <summary>A broadcaster in the Ilwrath's home system: their "gods" talk (bit 4).</summary>
    IlwrathHomeTalk,

    /// <summary>A broadcaster in a system whose first group is the Ilwrath's: that ship's talk (bit 5).</summary>
    IlwrathShipTalk,

    /// <summary>The Sun Device in orbit: the white flash, then the screen back.</summary>
    SunFlash,

    /// <summary>The Sun Device in orbit of the Chmmr's world: the white flash, and the Chmmr emerging (CHMMR_EMERGING, their ship and talk).</summary>
    ChmmrEmerging,

    /// <summary>The Utwig bomb: set off (UTWIG_BOMB 0, CrewEnlisted ~0: the game over).</summary>
    Bomb,

    /// <summary>The Talking Pet: its talk (GLOBAL_FLAGS_AND_DATA 0).</summary>
    PetTalk,

    /// <summary>The portal spawner in hyperspace: its 10 units of fuel paid and the portal opening (PORTAL_COUNTER 1).</summary>
    Portal,
}

/// <summary>A call InvokeDevice makes, as tests/golden/device-uses.json records them.</summary>
/// <param name="Kind">sound, fade, sleep, sphere, put, reinit, clone, savesys or savehyper.</param>
/// <param name="A">PlayMenuSound's sound, FadeScreen's kind, PutGroupInfo's offset.</param>
/// <param name="B">FadeScreen's time (ticks of 840 a second), PutGroupInfo's mode.</param>
/// <param name="Race">StartSphereTracking's and CloneShipFragment's race.</param>
public readonly record struct DeviceCall(string Kind, int A = 0, int B = 0, string? Race = null);

/// <summary>
/// Where the ship is as a device is used (what InvokeDevice reads besides
/// the story): in hyperspace or QuasiSpace (<see cref="InHqSpace"/>:
/// IN_HYPERSPACE), else in a system (IN_INTERPLANETARY, the solar system
/// loaded); in a world's orbit; the system's generator (CurStarDescPtr's
/// Index, by gendef.h's name: CHMMR_DEFINED, ILWRATH_DEFINED,
/// SAMATRA_DEFINED); the world orbited (its planet, and its moon or null
/// for the planet itself: matchWorld); the race of the system's first group
/// (the head of ip_group_q), or null; whether a hyperspace group is near
/// (the head of encounter_q); and the fuel aboard (1/100 units).
/// </summary>
public sealed record DevicePlace(bool InHqSpace, bool InOrbit = false, string? Star = null, int Planet = 0, int? Moon = null,
    string? FirstGroupRace = null, bool EncounterNear = false, uint Fuel = 0);

/// <summary>What a device's use did (InvokeDevice's effects).</summary>
public sealed class DeviceUse
{
    public DeviceStatus Status { get; internal set; }

    public DeviceAction Action { get; internal set; }

    /// <summary>START_ENCOUNTER set: a talk starts (the Pet's, the Chmmr's, the Ilwrath's).</summary>
    public bool StartEncounter { get; internal set; }

    /// <summary>NextActivity's CHECK_LOAD: the device's talk comes by a fake load.</summary>
    public bool FakeLoad { get; internal set; }

    /// <summary>IN_BATTLE taken off CurrentActivity (the Pet in hyperspace, the bomb).</summary>
    public bool LeftBattle { get; internal set; }

    /// <summary>CrewEnlisted (COUNT)~0: the ship gone (the bomb), the game over.</summary>
    public bool CrewGone { get; internal set; }

    /// <summary>The fuel it cost (1/100 units: the spawner's 10 units).</summary>
    public uint FuelCost { get; internal set; }

    /// <summary>The calls made, in order.</summary>
    public List<DeviceCall> Calls { get; } = new();
}

/// <summary>
/// InvokeDevice (devices.c) and UseCaster, as UQM's one ship has them:
/// what using a device does, given where the ship is and the story (read
/// and written through <c>get</c> and <c>set</c>: the user's ship's
/// devices among them, as a talk reads them). Pure: the game applies what
/// it says (<see cref="World.GameWorld"/>'s UseDevice, with the co-op rules
/// of the stage 3c plan's questions 5 and 6). Checked against UQM's own
/// (tests/golden/device-uses.json).
/// </summary>
public static class DeviceUses
{
    /// <summary>PORTAL_FUEL_COST: 10 units of fuel (FUEL_TANK_SCALE 100).</summary>
    public const uint PortalFuelCost = 10 * 100;

    /// <summary>MENU_SOUND_INVOKED (sounds.h).</summary>
    public const int SoundInvoked = 3;

    /// <summary>ScreenFadeType's FadeAllToWhite and FadeAllToColor (gfxlib.h).</summary>
    public const int FadeAllToWhite = 250, FadeAllToColor = 253;

    /// <summary>ONE_SECOND (840 ticks).</summary>
    public const int OneSecond = 840;

    /// <summary>PutGroupInfo (GROUPS_RANDOM, GROUP_SAVE_IP): offset 0, mode ~0.</summary>
    private static readonly DeviceCall SaveGroups = new("put", 0, 255);

    /// <param name="device">globdata.h's device number.</param>
    /// <param name="ilwrathAlive">StartSphereTracking (ILWRATH_SHIP), asked only in the Ilwrath's home system.</param>
    public static DeviceUse Invoke(DeviceTable table, int device, DevicePlace place, Func<string, int> get,
        Action<string, int> set, Func<bool> ilwrathAlive)
    {
        var use = new DeviceUse();
        string name = device >= 0 && device < table.All.Count ? table.All[device].Name : "";
        bool inHyperSpace = place.InHqSpace && get("ARILOU_SPACE_SIDE") <= 1;
        switch (name)
        {
            case "ROSY_SPHERE_DEVICE":
                Mend(use, "ROSY_SPHERE_ON_SHIP", get, set);
                break;
            case "AQUA_HELIX_DEVICE":
                Mend(use, "AQUA_HELIX_ON_SHIP", get, set);
                break;
            case "CLEAR_SPINDLE_DEVICE":
                Mend(use, "CLEAR_SPINDLE_ON_SHIP", get, set);
                break;
            case "SUN_EFFICIENCY_DEVICE":
                if (!place.InHqSpace && place.InOrbit)
                {
                    use.Calls.Add(new("sound", SoundInvoked));
                    use.Calls.Add(new("fade", FadeAllToWhite, OneSecond));
                    use.Calls.Add(new("sleep"));
                    if (place.Star != "CHMMR_DEFINED" || !AtPlanetOne(place))
                    {
                        use.Calls.Add(new("fade", FadeAllToColor, OneSecond * 2));
                        use.Action = DeviceAction.SunFlash;
                    }
                    else
                    {
                        set("CHMMR_EMERGING", 1);
                        use.StartEncounter = true;
                        use.Calls.Add(SaveGroups);
                        use.Calls.Add(new("reinit"));
                        use.Calls.Add(new("clone", Race: "CHMMR_SHIP"));
                        use.Action = DeviceAction.ChmmrEmerging;
                    }
                    use.Status = DeviceStatus.SuccessNoSound;
                }
                break;
            case "UTWIG_BOMB_DEVICE":
                set("UTWIG_BOMB", 0);
                use.LeftBattle = true;
                use.CrewGone = true;
                use.Action = DeviceAction.Bomb;
                use.Status = DeviceStatus.Success;
                break;
            case "TALKING_PET_DEVICE":
                use.FakeLoad = true;
                use.StartEncounter = true;
                set("GLOBAL_FLAGS_AND_DATA", 0);
                if (place.InHqSpace)
                {
                    if (place.EncounterNear)
                        set("SHIP_TO_COMPEL", 1);
                    use.LeftBattle = true;
                    use.Calls.Add(new("savehyper"));
                }
                else
                {
                    if (place.FirstGroupRace is not null)
                    {
                        set("SHIP_TO_COMPEL", 1);
                        use.Calls.Add(SaveGroups);
                        use.Calls.Add(new("reinit"));
                    }
                    if (place.Star == "SAMATRA_DEFINED")
                        set("READY_TO_CONFUSE_URQUAN", 1);
                    if (place.InOrbit)
                        use.Calls.Add(new("savesys"));
                }
                use.Action = DeviceAction.PetTalk;
                use.Status = DeviceStatus.Success;
                break;
            case "UMGAH_HYPERWAVE_DEVICE":
            case "BURVIX_HYPERWAVE_DEVICE":
                if (UseCaster(use, place, get, set, ilwrathAlive))
                    use.Status = DeviceStatus.Success;
                break;
            case "PORTAL_SPAWNER_DEVICE":
                if (inHyperSpace && place.Fuel >= PortalFuelCost)
                {
                    use.FuelCost = PortalFuelCost;
                    set("PORTAL_COUNTER", 1);
                    use.Action = DeviceAction.Portal;
                    use.Status = DeviceStatus.Success;
                }
                break;
        }
        return use;
    }

    // ROSY_SPHERE_DEVICE, AQUA_HELIX_DEVICE, CLEAR_SPINDLE_DEVICE: with the
    // Ultron aboard in any condition, one more piece of it mended.
    private static void Mend(DeviceUse use, string piece, Func<string, int> get, Action<string, int> set)
    {
        int val = get("ULTRON_CONDITION");
        if (val == 0)
            return;
        set("ULTRON_CONDITION", val + 1);
        set(piece, 0);
        set("DISCUSSED_ULTRON", 0);
        set("SUPOX_ULTRON_HELP", 0);
        use.Action = DeviceAction.Mended;
        use.Status = DeviceStatus.Success;
    }

    // matchWorld (pSolarSysState, pOrbitalDesc, 1, MATCH_PLANET): the second planet itself.
    private static bool AtPlanetOne(DevicePlace place) => place.Planet == 1 && place.Moon is null;

    // UseCaster: true if the broadcaster did something.
    private static bool UseCaster(DeviceUse use, DevicePlace place, Func<string, int> get, Action<string, int> set,
        Func<bool> ilwrathAlive)
    {
        if (place.InHqSpace)
        {
            if (get("ARILOU_SPACE_SIDE") > 1)
                return false;
            set("USED_BROADCASTER", 1);
            use.Action = DeviceAction.CalledMelnorme;
            return true;
        }
        if (place.InOrbit && AtPlanetOne(place) && place.Star == "CHMMR_DEFINED" && get("CHMMR_UNLEASHED") == 0)
        {
            // In orbit around the Chenjesu/Mmrnmhrm home planet.
            use.FakeLoad = true;
            use.StartEncounter = true;
            use.Calls.Add(SaveGroups);
            use.Calls.Add(new("reinit"));
            set("GLOBAL_FLAGS_AND_DATA", 1 << 7);
            use.Calls.Add(new("savesys"));
            use.Action = DeviceAction.ChmmrTalk;
            return true;
        }
        bool home = place.Star == "ILWRATH_DEFINED";
        bool found = false;
        if (home)
        {
            use.Calls.Add(new("sphere", Race: "ILWRATH_SHIP"));
            found = ilwrathAlive();
        }
        if (!found && place.FirstGroupRace is { } race)
            found = race == "ILWRATH_SHIP";
        if (!found)
            return false;
        use.FakeLoad = true;
        use.StartEncounter = true;
        use.Calls.Add(SaveGroups);
        use.Calls.Add(new("reinit"));
        set("GLOBAL_FLAGS_AND_DATA", home ? 1 << 4 : 1 << 5);
        use.Action = home ? DeviceAction.IlwrathHomeTalk : DeviceAction.IlwrathShipTalk;
        if (place.InOrbit)
            use.Calls.Add(new("savesys"));
        return true;
    }
}
