// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (solarsys.c's InitSolarSys,
// IP_frame and UninitSolarSys around the system's groups: initNpcs,
// RedrawQueue, uninitNpcs, and the meeting's GetGroupInfo and
// reinitNpcs), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// System groups (docs/plans/stage-3b.md, item 4): the players coming
/// into and leaving systems (each system's draw kept for those who were
/// in it, decision 7), and each system frame's groups after the flights
/// of the players flying there.
/// </summary>
public sealed partial class GameWorld
{
    private SystemGroups? _systemGroups;
    private StoryState? _systemGroupsOf;
    private Dictionary<string, int> _inSystemBefore = new();
    private readonly Dictionary<string, ShipInput> _systemInput = new();

    /// <summary>The groups kept with the systems, or null without a story.</summary>
    public SystemGroups? SystemGroups
    {
        get
        {
            if (Story is null)
                return null;
            if (_systemGroupsOf != Story)
            {
                _systemGroups = new SystemGroups(Story.Rules);
                _systemGroupsOf = Story;
            }
            _systemGroups!.Ported = EncounterRaces;
            return _systemGroups;
        }
    }

    /// <summary>
    /// The seed of a system's random numbers as a player comes into it with
    /// nobody there (UQM's TFB_Random goes on from hyperspace's): from the
    /// frame and the star. Tests set their own.
    /// </summary>
    internal Func<int, uint>? SystemSeeds { get; set; }

    /// <summary>Whether each system's random numbers note where each is drawn (tests).</summary>
    internal bool TraceSystemRandom { get; set; }

    /// <summary>
    /// The meetings in systems so far: who touched which group (by its race
    /// and number in the draw), in which system, on which frame (item 5's
    /// encounter screen and talk start from here). Until then a meeting ends
    /// at once as a talk without a battle does: the group re-forms where it
    /// was and its race's groups stand down (decision 8), and the player
    /// flies on, unable to touch anything for the escape counter's frames.
    /// </summary>
    internal List<(long Frame, string Player, int Star, int Race, int Group)> SystemMeetings { get; } = new();

    /// <summary>Called after each system frame's groups, with the system's star (tests).</summary>
    internal Action<int>? AfterSystemFrame { get; set; }

    private HyperspaceRandom NewSystemRandom(int star)
    {
        uint seed = SystemSeeds?.Invoke(star) ?? unchecked((uint)Frame * 2246822519u + (uint)star * 3266489917u + 0x49504752u);
        var r = new HyperspaceRandom(seed);
        if (TraceSystemRandom)
            r.Places = new List<string>();
        return r;
    }

    private GameDate Today => new(Clock.Year, Clock.Month, Clock.Day);

    // Who is in which system for its groups: a ship there whose entry has
    // played out, present or away, a wreck too.
    private Dictionary<string, int> InSystems() =>
        _players.Where(p => p.Ship.SystemStar is not null && p.Ship.Entry is null && p.Ship.Ip is not null)
            .ToDictionary(p => p.Id, p => p.Ship.SystemStar!.Value);

    // Players leaving systems (the last out keeps the draw a week), then
    // those coming into one (InitSolarSys's initNpcs, if nobody's there).
    private void SyncSystemPlayers()
    {
        if (SystemGroups is not { } sg || Systems is null)
            return;
        var now = InSystems();
        foreach (var (id, star) in _inSystemBefore)
        {
            if (!now.TryGetValue(id, out int s) || s != star)
                sg.Depart(star, id, Today);
        }
        foreach (var (id, star) in now)
        {
            if (_inSystemBefore.TryGetValue(id, out int s) && s == star)
                continue;
            var ship = Find(id)!.Ship;
            sg.Arrive(Systems[star], id, ship.Ip!, Today, Story!, () => NewSystemRandom(star));
        }
        _inSystemBefore = now;
    }

    /// <summary>
    /// The players' flights in systems this world frame, a system frame at a
    /// time: each player flying there steps, then each system's groups fly
    /// for those who flew in it (with nobody flying there, the groups wait
    /// without elements), and those who met a group meet it.
    /// </summary>
    private void StepSystems(int systemFrames)
    {
        SyncSystemPlayers();
        var sg = SystemGroups;
        for (int i = 0; i < systemFrames; ++i)
        {
            var flying = _players.Where(p => _systemInput.ContainsKey(p.Id)
                                             && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem)
                .Select(p => (Player: p, Star: p.Ship.SystemStar!.Value, Flight: p.Ship.Ip!)).ToList();
            if (flying.Count == 0)
            {
                // Nobody flies in any system: the groups keep no elements.
                foreach (var d in sg?.Systems.Values ?? Enumerable.Empty<SystemDraw>())
                    d.Elements = null;
                break;
            }
            foreach (var (p, _, _) in flying)
                StepSystem(p.Ship, _systemInput[p.Id]);
            if (sg is null || Systems is null || Shapes is null)
                continue;
            foreach (var d in sg.Systems.Values.ToList())
            {
                var flyers = flying.Where(f => f.Star == d.Star)
                    .Select(f => new SystemFlyer(f.Player.Id, f.Flight)).ToList();
                if (flyers.Count == 0 || !d.Present.Overlaps(flyers.Select(f => f.Id)))
                {
                    d.Elements = null;
                    continue;
                }
                sg.Frame(d, Systems[d.Star], flyers, Shapes);
                foreach (var f in flyers.Where(f => f.Met is not null))
                    MeetInSystem(sg, d, f);
                AfterSystemFrame?.Invoke(d.Star);
            }
        }
        _systemInput.Clear();
    }

    // A player's ship touched a group (decision 5): item 5 runs the
    // meeting; until then it ends at once without a battle, as UQM's game
    // comes back from such a talk: the group met re-forms where it is, the
    // others stand down (as the touch left them), and the player's ship
    // flies on from where it was, its element made again and unable to
    // touch anything for the escape counter's frames (BuildBattle's).
    private void MeetInSystem(SystemGroups sg, SystemDraw d, SystemFlyer f)
    {
        var g = f.Met!;
        SystemMeetings.Add((Frame, f.Id, d.Star, g.Race, g.GroupId));
        sg.AfterMeeting(d, g);
        var ip = f.Flight;
        if (ip.Orbit is null)
        {
            ip.EscapeCounter = IpFlight.EscapeFrames;
            ip.Reenter();
        }
        // ResetSolarSys: with nobody else flying there, every element is
        // made again (InitDisplayList, DoMissions).
        if (!_players.Any(p => p.Id != f.Id && p.Ship.SystemStar == d.Star && _systemInput.ContainsKey(p.Id)
                               && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem))
            d.Elements = null;
    }

    /// <summary>
    /// Puts a player in a system as a ship arriving from hyperspace has
    /// it, its entry played (for testing and debugging); out of any other
    /// system first.
    /// </summary>
    internal void PutInSystem(string id, int star)
    {
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        var ship = player.Ship;
        EndSpin(player);
        ship.Entry = null;
        ship.Ip = null;
        EnterSystem(ship, Systems!.Map.Stars[star]);
    }

    /// <summary>Takes a player's ship out of its system into hyperspace, as flying off its edge does (for testing).</summary>
    internal void PutInHyperspace(string id)
    {
        var ship = (Find(id) ?? throw new ArgumentException($"no player {id}")).Ship;
        if (ship.Ip is not { } ip)
            return;
        SystemTransition.Leave(ship.Flight, Systems!.Map.Stars[ship.SystemStar!.Value], ip.Facing);
        ship.Ip = null;
        ship.SystemStar = null;
    }

    /// <summary>A loaded game: the players in systems were there as it was saved; their systems' groups go on as saved.</summary>
    internal void LoadedInSystems()
    {
        _inSystemBefore = InSystems();
        if (SystemGroups is not { } sg)
            return;
        foreach (var (id, star) in _inSystemBefore)
        {
            if (!sg.Systems.TryGetValue(star, out var d))
                sg.Systems[star] = d = new SystemDraw(star);
            d.Present.Add(id);
            d.Visitors.Add(id);
        }
    }
}
