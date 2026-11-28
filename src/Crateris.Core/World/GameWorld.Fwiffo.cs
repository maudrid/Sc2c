// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/generate/gensol.c:
// GenerateSol_pickupEnergy; src/uqm/planets/lander.c: KillLanderCrewSeq;
// src/uqm/planets/scan.c: spawnFwiffo, DispatchLander; src/uqm/comm.c:
// RaceCommunication's fake load; src/uqm/planets/solarsys.c: the ship back
// in orbit after it), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Planetside;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// Fwiffo on Pluto (the stage 3c plan's item 7; its question 10): the
/// lander that touches him loses 8 crew one by one while the world goes on
/// (a lander of 8 or fewer dies, and nothing happens); living through it,
/// he's found (FOUND_PLUTO_SPATHI 1, shared), his report shown and the
/// lander sent up, and the Spathi's lock is the finder's from the touch, so
/// no other Spathi talk (which would be his while the field is 1) can start
/// meanwhile. When the finder's lander is back aboard, his talk starts: a
/// meeting with no group in the system, his one ship (crew 1, his own
/// captain's name), the ship kept in orbit, as UQM's spawnFwiffo and the
/// fake load after DispatchLander have it. If his talk can't come to the
/// finder (they leave, the game is loaded from a save made before it, the
/// talk is dropped), the field stays 1 and the next lander back aboard any
/// player's ship brings it to that ship, taking the Spathi's lock or
/// waiting for it, dropped if the field passes 1 meanwhile.
/// </summary>
public sealed partial class GameWorld
{
    // The player Fwiffo's talk is coming to (not saved): the finder from the
    // pickup, holding the Spathi's lock; or the player whose lander came back
    // aboard while the field was 1 and no talk was coming to anyone.
    private string? _fwiffoFor;

    /// <summary>The player Fwiffo's talk is coming to, or null (for tests and the host's log).</summary>
    internal string? FwiffoFor => _fwiffoFor;

    private int SpathiRace => Story!.Rules.RaceIndex("SPATHI_SHIP");

    // A lander touched Fwiffo (StoryWorlds.PickupEnergy asked for its crew
    // loss): the Spathi's lock taken for the finder (the touch passing over
    // him while another player holds it, who's talking told once), and the
    // lander held while its crew are lost; then, if it lives, the rest of
    // the pickup.
    private void PickupFwiffo(Player finder, WorldId world, PlanetsideSurface surface, Lander lander,
        (int Count, int PeriodTicks) loss)
    {
        int race = SpathiRace;
        string? conversation = DialogueLocks is null ? null : ConversationOf(race);
        if (conversation is not null && !DialogueLocks!.TryTake(conversation, finder.Id, out _))
        {
            TellWhoIsTalking(finder, conversation, race);
            return;
        }
        lander.LoseCrew(loss.Count, loss.PeriodTicks, lived => FwiffoCrewLost(finder, world, surface, lander, lived, conversation));
    }

    // The end of the crew loss: the lander dead (or the story or the lander
    // gone meanwhile), the lock let go and nothing more; living, Fwiffo is
    // found (GenerateSol_pickupEnergy's rest, made as any pickup's: the
    // report at the finder's place, the take-off, word to the other landers
    // there) and his talk is coming to the finder, who keeps the lock.
    private void FwiffoCrewLost(Player finder, WorldId world, PlanetsideSurface surface, Lander lander, bool lived,
        string? conversation)
    {
        if (!lived || Story is not { } story || story.Get(StoryWorlds.FwiffoFoundField) != 0
            || !_landerPlayers.ContainsKey(lander) || !finder.Present)
        {
            if (conversation is not null && _fwiffoFor != finder.Id && finder.Ship.Meeting?.Conversation != conversation)
                DialogueLocks?.Release(conversation, finder.Id);
            return;
        }
        var found = new Finder(PlaceIn(finder.Ship, world), field => PlayerTalkShip.DeviceOf(this, finder, field),
            Planets.RetrieveMask(world, ScanType.Energy));
        StoryWorlds.FwiffoFound(story, found);
        Found(finder, world, surface, lander, found);
        _fwiffoFor = finder.Id;
    }

    // A lander's visit is over (back aboard after its take-off, or lost),
    // not one taken aboard as its player leaves (DispatchLander's check
    // after PlanetSide, which a lost lander reaches too): while Fwiffo's
    // field is 1, his talk comes to this ship if it's coming to nobody.
    private void FwiffoLanderBack(Player player)
    {
        if (Story is not { } story || story.Get(StoryWorlds.FwiffoFoundField) != 1)
            return;
        _fwiffoFor ??= player.Id;
    }

    /// <summary>
    /// Each frame after the surfaces: Fwiffo's talk, if it's coming to a
    /// player, starts once their ship is in orbit with its lander aboard
    /// and nothing else going on (no meeting, visit, battle or entry), and
    /// the Spathi's lock is free or theirs (waiting meanwhile); it's
    /// dropped (the lock let go) if the field isn't 1 any more (another
    /// player's Spathi talk was his), its player has gone, or their ship has
    /// left orbit or is a wreck: the field, if still 1, waits for the next
    /// lander back aboard.
    /// </summary>
    private void StepFwiffo()
    {
        if (_fwiffoFor is not { } id)
            return;
        var player = Find(id);
        if (Story is not { } story || story.Get(StoryWorlds.FwiffoFoundField) != 1 || player is not { Present: true }
            || player.Ship.Orbit is null || player.Ship.Destroyed)
        {
            DropFwiffo();
            return;
        }
        var ship = player.Ship;
        if (ship.Lander is not null || ship.Hold.LanderOut is not null || ship.Meeting is not null || ship.Visit is not null
            || ship.Battle is not null || ship.Entry is not null)
            return;
        MeetFwiffo(player);
    }

    // Fwiffo's talk no longer coming: the lock let go if it's held for it.
    private void DropFwiffo()
    {
        if (_fwiffoFor is not { } id)
            return;
        _fwiffoFor = null;
        if (DialogueLocks is { } locks && ConversationOf(SpathiRace) is { } c && locks.Holder(c) == id
            && Find(id)?.Ship.Meeting?.Conversation != c)
            locks.Release(c, id);
    }

    // The player leaves: his talk isn't coming to them (their locks go with them).
    private void FwiffoPlayerLeft(string id)
    {
        if (_fwiffoFor == id)
            _fwiffoFor = null;
    }

    /// <summary>
    /// Fwiffo's talk (scan.c's spawnFwiffo and DispatchLander's fake load,
    /// comm.c's SPATHI_CONVERSATION): a meeting with no group, at the world
    /// the ship orbits, the system's groups put aside for it (PutGroupInfo
    /// GROUP_SAVE_IP) and none standing down, his one ship (CloneShipFragment
    /// with crew 1, captains_name_index NAME_OFFSET + NUM_CAPTAINS_NAMES: his
    /// own name), under the Spathi's lock (or the player told who holds it,
    /// the talk waiting). The race's ordinary talk, which reads the field and
    /// is his while it's 1; its end sets it to 2. After it (and its battle,
    /// if he fights at Pluto) the ship is still in orbit, as UQM's
    /// SaveSolarSysLocation keeps it (in_orbit), and the groups back
    /// (reinitNpcs). His ship is nobody's group: never saved, gone with the
    /// meeting. Without a talk to make (no content), nothing starts.
    /// </summary>
    private void MeetFwiffo(Player player)
    {
        var rules = Story!.Rules;
        int race = SpathiRace;
        if (!HasTalk(race) || ConversationOf(race) is not { } conversation || player.Ship.OrbitedWorld is not { } world)
            return;
        if (!DialogueLocks!.TryTake(conversation, player.Id, out _))
        {
            TellWhoIsTalking(player, conversation, race);
            return;
        }
        _fwiffoFor = null;
        if (SystemGroups is { } sg && sg.Systems.TryGetValue(world.Star, out var d))
            sg.StartWorldMeeting(d, -1);
        var fwiffo = new EnemyFleetShip(rules.RaceShips[race].Ship, 1, Battle.CaptainNameOffset + Battle.NumCaptainsNames);
        var m = new Meeting(player, race, rules.Races[race], new[] { fwiffo }, NextRound())
        {
            Id = ++_meetingIds,
            Star = world.Star,
            World = (world.Planet, world.Moon),
            Conversation = conversation,
            Fwiffo = true,
        };
        player.Ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        BeginTalk(m);
    }
}
