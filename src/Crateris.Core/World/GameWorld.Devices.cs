// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/devices.c:
// DoManipulateDevices, InvokeDevice, UseCaster; src/uqm/comm.c:
// RaceCommunication's fake load; src/uqm/restart.c: RestartMenu's bomb),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>What a player's use of a device came to (the Devices list's select, stage 3c item 8).</summary>
public enum DeviceOutcome : byte
{
    /// <summary>Used (DEVICE_SUCCESS): the list closes. The Ultron mended, the Melnorme called, a talk begun, the bomb set off.</summary>
    Used,

    /// <summary>The Sun Device used in orbit (DEVICE_SUCCESS_NO_SOUND): the list closes and the user's screen flashes white and comes back.</summary>
    Flash,

    /// <summary>
    /// The Sun Device used in orbit of the Chmmr's world: the flash as
    /// <see cref="Flash"/>, the Chmmr's emerging left for stage 3c item 11.
    /// </summary>
    FlashChmmrLater,

    /// <summary>DEVICE_FAILURE: nothing happens here; the list stays open.</summary>
    Failed,

    /// <summary>The ship doesn't hold it (a gift or a talk took it meanwhile).</summary>
    NotHeld,

    /// <summary>The Utwig bomb aboard a guest's ship: it can be set off only aboard the host's (the stage 3c plan's questions 5 and 6).</summary>
    HostShipOnly,

    /// <summary>Its use here comes with a later item: the Talking Pet's (item 10), the portal spawner's (item 9), the broadcaster's shielded talk with the Chmmr (item 11).</summary>
    Later,

    /// <summary>A broadcaster's talk with the Ilwrath while another player talks to them (their lock): the list stays open.</summary>
    Talking,

    /// <summary>Not now: the ship is in a talk, a battle, docked, entering a star, a wreck, its lander down, or the game is paused or over.</summary>
    NotNow,
}

public sealed partial class GameWorld
{
    /// <summary>
    /// A present player uses a device from their Devices list (devices.c's
    /// DoManipulateDevices on select, InvokeDevice: <see cref="DeviceUses"/>),
    /// with the co-op rules (the stage 3c plan's questions 5 and 6): only a
    /// device their ship holds (or a shared one: the portal spawner, the
    /// Lunar base), the Utwig bomb only aboard the host's ship, whose
    /// setting off is the game over for everyone. What it does here: the
    /// Ultron's pieces mend it aboard the same ship; a broadcaster calls the
    /// Melnorme in hyperspace (the user's own USED_BROADCASTER) or, in a
    /// system, starts the Ilwrath's talk (their "gods" in their home system
    /// while they live, or the ship first among the system's groups; a
    /// meeting with no group and no ships, under the Ilwrath's lock); the
    /// Sun Device flashes the user's screen in orbit; the bomb ends the
    /// game. The Pet's (item 10), the spawner's (item 9) and the Chmmr's
    /// halves of the broadcaster and the Sun Device (item 11) come later:
    /// nothing they'd write is written. Otherwise nothing happens, as UQM's
    /// failure.
    /// </summary>
    public DeviceOutcome UseDevice(string id, int device)
    {
        if (Story is not { } story || Find(id) is not { Present: true } player)
            return DeviceOutcome.NotNow;
        var ship = player.Ship;
        if (StoryPaused || Held || ship.Destroyed || ship.Meeting is not null || ship.Visit is not null || ship.Battle is not null
            || ship.Entry is not null || ship.Lander is not null || ship.Hold.LanderOut is not null || ship.AppearFrames > 0)
            return DeviceOutcome.NotNow;
        var table = story.Rules.Devices;
        if (!table.Inventory(story.Get, ship.Hold.Devices).Contains(device))
            return DeviceOutcome.NotHeld;
        string name = table.All[device].Name;
        if (name == "UTWIG_BOMB_DEVICE" && !player.IsHost)
            return DeviceOutcome.HostShipOnly;

        // InvokeDevice on what UQM's one ship would see: the user's ship's
        // devices, its own USED_BROADCASTER and ARILOU_SPACE_SIDE, the shared
        // story; the writes held until what it set going is known.
        var writes = new Dictionary<string, int>();
        int Get(string field) => writes.TryGetValue(field, out int v) ? v : field switch
        {
            "ARILOU_SPACE_SIDE" => ship.ArilouSpaceSide,
            "USED_BROADCASTER" => ship.UsedBroadcaster ? 1 : 0,
            "GLOBAL_FLAGS_AND_DATA" => 0,
            // The Ultron mended needs it aboard the same ship as the piece
            // (the plan's readings of question 5): the ship's own, never the
            // story's record (given back, 5, it isn't aboard).
            _ when DeviceTable.HeldByShips(field) => PlayerTalkShip.DeviceOf(this, player, field),
            _ => story.Get(field),
        };
        void Set(string field, int value) => writes[field] = value & story.Rules.Table[field].Max;
        var use = DeviceUses.Invoke(table, device, PlaceOf(player), Get, Set,
            () => story.StartSphereTracking("ILWRATH_SHIP"));
        switch (use.Action)
        {
            case DeviceAction.None:
                return DeviceOutcome.Failed;
            case DeviceAction.PetTalk or DeviceAction.Portal or DeviceAction.ChmmrTalk:
                return DeviceOutcome.Later;
            case DeviceAction.ChmmrEmerging:
                return DeviceOutcome.FlashChmmrLater;
            case DeviceAction.SunFlash:
                return DeviceOutcome.Flash;
            case DeviceAction.IlwrathHomeTalk or DeviceAction.IlwrathShipTalk:
                return MeetIlwrathByCaster(player, writes["GLOBAL_FLAGS_AND_DATA"]) ? DeviceOutcome.Used : DeviceOutcome.Talking;
            case DeviceAction.CalledMelnorme:
                ship.UsedBroadcaster = true;
                return DeviceOutcome.Used;
            case DeviceAction.Bomb:
                SetOffBomb(player, writes);
                return DeviceOutcome.Used;
            default:   // Mended
                foreach (var (field, value) in writes)
                {
                    if (DeviceTable.HeldByShips(field))
                    {
                        PlayerTalkShip.SetDeviceOf(this, player, field, value);
                        if (DeviceTable.RecordedFields.Contains(field))
                            story.Set(field, value);
                    }
                    else
                        story.Set(field, value);
                }
                return DeviceOutcome.Used;
        }
    }

    // Where the user's ship is, as InvokeDevice reads it: in hyperspace or
    // QuasiSpace (IN_HYPERSPACE) or in a system, in orbit, the system's
    // generator, the world orbited, the race of the system's first group (the
    // head of ip_group_q) and the fuel. Whether a hyperspace group is near
    // is the Pet's alone (SHIP_TO_COMPEL, item 10): not looked for.
    private DevicePlace PlaceOf(Player player)
    {
        var ship = player.Ship;
        if (ship.SystemStar is not int star)
            return new DevicePlace(InHqSpace: true, Fuel: ship.Flight.Fuel);
        var world = ship.OrbitedWorld;
        string? first = SystemGroups is { } sg && sg.Systems.TryGetValue(star, out var d) && d.Groups.FirstOrDefault() is { } g
            ? Story!.Rules.Races[g.Race] : null;
        return new DevicePlace(false, world is not null, Systems?.Map.Stars[star].Special, world?.Planet ?? 0, world?.Moon,
            first, false, ship.Flight.Fuel);
    }

    /// <summary>
    /// A broadcaster's talk with the Ilwrath (UseCaster's fake load, comm.c's
    /// ILWRATH_CONVERSATION by GLOBAL_FLAGS_AND_DATA bits 4 and 5): a meeting
    /// with no group and no ships (npc_built_ship_q is empty: no battle
    /// follows, whatever the talk says), in the system where the ship is (in
    /// flight, or in orbit, where it stays), under the Ilwrath's lock (false,
    /// nothing begun, the user told who holds it, while another player talks
    /// to them), the system's groups put aside for it (PutGroupInfo
    /// GROUP_SAVE_IP, ReinitQueue) and none standing down. Without the
    /// content's talk, nothing begins (false).
    /// </summary>
    private bool MeetIlwrathByCaster(Player player, int flags)
    {
        var rules = Story!.Rules;
        int race = rules.RaceIndex("ILWRATH_SHIP");
        if (!HasTalk(race) || ConversationOf(race) is not { } conversation || player.Ship.SystemStar is not int star)
            return false;
        if (!DialogueLocks!.TryTake(conversation, player.Id, out _))
        {
            TellWhoIsTalking(player, conversation, race);
            return false;
        }
        if (SystemGroups is { } sg && sg.Systems.TryGetValue(star, out var d))
            sg.StartWorldMeeting(d, -1);
        var world = player.Ship.OrbitedWorld;
        var m = new Meeting(player, race, rules.Races[race], Array.Empty<EnemyFleetShip>(), NextRound())
        {
            Id = ++_meetingIds,
            Star = star,
            World = world is { } w ? (w.Planet, w.Moon) : null,
            Conversation = conversation,
            CasterFlags = flags,
        };
        player.Ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        BeginTalk(m);
        return true;
    }

    /// <summary>The host's ship set off the Utwig bomb (for the game over's look: the flash, everyone's).</summary>
    public bool BombSetOff => Loss == GameLoss.UtwigBomb;

    // The Utwig bomb set off aboard the host's ship (InvokeDevice: UTWIG_BOMB
    // 0, CrewEnlisted ~0): the ship gone and the game over for everyone, as
    // any loss of the host's ship is (the stage 3c plan's question 6). The
    // bomb stays aboard (RestartMenu reads UTWIG_BOMB_ON_SHIP for its flash);
    // nothing is saved after.
    private void SetOffBomb(Player host, Dictionary<string, int> writes)
    {
        foreach (var (field, value) in writes)
            Story!.Set(field, value);
        host.Ship.Hold.SetCrew(0);
        host.Ship.Destroyed = true;
        LoseTheGame(GameLoss.UtwigBomb);
    }

    // After a broadcaster's talk with the Ilwrath: a ship still in orbit
    // there has the orbit's start done afresh (UQM's fake load comes back
    // through the world's generateOrbital, SaveSolarSysLocation having kept
    // it in orbit), as after Fwiffo's (AfterFwiffo).
    private void AfterCaster(Meeting m) => AfterFwiffo(m);
}
