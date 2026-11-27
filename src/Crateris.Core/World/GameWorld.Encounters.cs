// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (hyper.c's encounters, starcon.c's
// on_battle_frame, battle.c's Battle seeding TFB_Random as hyperspace
// starts, galaxy.c's MoveGalaxy), Copyright Paul Reiche, Fred Ford and
// Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>
/// Encounters in hyperspace (docs/plans/stage-3b.md, item 3): the shared
/// list (<see cref="Encounters"/>), each player's random numbers while
/// they're in hyperspace (UQM's TFB_Random, seeded as each stay in
/// hyperspace begins), the daily roll for each player in hyperspace, and
/// each frame's encounters (after the clock, as starcon.c's
/// on_battle_frame runs SeedUniverse after GameClockTick).
/// </summary>
public sealed partial class GameWorld
{
    private HyperEncounters? _encounters;
    private StoryState? _encountersOf;
    private readonly Dictionary<string, HyperspaceRandom> _hyperRandom = new();
    private HashSet<string> _inHyperspaceBefore = new();
    private readonly Dictionary<string, (short X, short Y)> _spinEndedAt = new();

    /// <summary>
    /// The races whose groups fly: every race (races.h's, data/races.json),
    /// as every race's talk is ported since stage 3b-2 item 11 (3b-1 items 6
    /// to 8 and 3b-2 items 4 to 11 brought them in turn: stage 3b, decision
    /// 2). The humans and the Syreen have no talk in space
    /// (RACE_COMMUNICATION's INVALID_CONVERSATION) and no groups either: no
    /// sphere or system draw makes them, and --start-encounter refuses them.
    /// </summary>
    internal static readonly IReadOnlySet<string> TalkingRaces = new HashSet<string>
    {
        "ARILOU_SHIP", "CHMMR_SHIP", "HUMAN_SHIP", "ORZ_SHIP", "PKUNK_SHIP", "SHOFIXTI_SHIP", "SPATHI_SHIP",
        "SUPOX_SHIP", "THRADDASH_SHIP", "UTWIG_SHIP", "VUX_SHIP", "YEHAT_SHIP", "MELNORME_SHIP", "DRUUGE_SHIP",
        "ILWRATH_SHIP", "MYCON_SHIP", "SLYLANDRO_SHIP", "UMGAH_SHIP", "URQUAN_SHIP", "ZOQFOTPIK_SHIP", "SYREEN_SHIP",
        "BLACK_URQUAN_SHIP", "YEHAT_REBEL_SHIP", "URQUAN_DRONE_SHIP",
    };

    /// <summary>
    /// The races whose groups are kept once made (decision 2):
    /// <see cref="TalkingRaces"/>, every race. Tests set their own: every
    /// other race's groups are then made as UQM makes them, and dropped.
    /// </summary>
    internal IReadOnlySet<string> EncounterRaces { get; set; } = TalkingRaces;

    /// <summary>
    /// The hyperspace encounters the players share (UQM's encounter_q), or
    /// null without a story.
    /// </summary>
    public HyperEncounters? Encounters
    {
        get
        {
            if (Story is null)
                return null;
            if (_encountersOf != Story)
            {
                _encounters = new HyperEncounters(Story.Rules, Settings.MaxHyperspaceEncounters,
                    Shapes?.HyperStars.Count ?? HyperEncounters.DefaultFrames);
                _encountersOf = Story;
            }
            return _encounters;
        }
    }

    /// <summary>
    /// The meetings so far: who touched which group, on which frame; each
    /// starts the player's <see cref="Meeting"/> (GameWorld.Meetings.cs).
    /// </summary>
    internal List<(long Frame, string Player, int Race)> HyperspaceMeetings { get; } = new();

    /// <summary>
    /// The seed of a player's random numbers as a stay in hyperspace begins
    /// (UQM's Battle seeds TFB_Random from the time): from the frame and
    /// the player's place in the game. Tests set their own.
    /// </summary>
    internal Func<string, uint>? HyperspaceSeeds { get; set; }

    /// <summary>Whether each player's random numbers note where each is drawn (tests).</summary>
    internal bool TraceHyperspaceRandom { get; set; }

    /// <summary>A player's random numbers in hyperspace, made as their stay begins (or as a day's roll first needs them).</summary>
    internal HyperspaceRandom HyperspaceRandomOf(string id)
    {
        if (!_hyperRandom.TryGetValue(id, out var random))
        {
            int index = _players.FindIndex(p => p.Id == id);
            uint seed = HyperspaceSeeds?.Invoke(id) ?? unchecked((uint)Frame * 2654435761u + (uint)index * 40503u + 0x48595045u);
            random = new HyperspaceRandom(seed);
            if (TraceHyperspaceRandom)
                random.Places = new List<string>();
            _hyperRandom[id] = random;
        }
        return random;
    }

    /// <summary>
    /// Whether a player is in hyperspace for the encounters: present,
    /// flying there (not in a battle, talk, system or at the starbase) or
    /// spinning into a star or a group it met (<see cref="PlayerShip.Spin"/>:
    /// UQM's flagship leaves hyperspace on the spin's last frame), and not a
    /// wreck. Only they roll, are flown at, and keep groups.
    /// </summary>
    internal static bool InHyperspace(Player p) =>
        p.Present && !p.Ship.Destroyed && (p.Ship.Activity == Activity.Hyperspace || p.Ship.Spin is not null);

    // A player's hyperspace flight: their spin's while they spin into a star.
    private static HyperFlight HyperspaceFlightOf(Player p) => p.Ship.Spin ?? p.Ship.Flight;

    private List<HyperspacePlayer> HyperspacePlayers() =>
        _players.Where(InHyperspace).Select(p =>
        {
            var f = HyperspaceFlightOf(p);
            return new HyperspacePlayer(p.Id, f.UniverseX, f.UniverseY);
        }).ToList();

    /// <summary>
    /// A ship's spin into a star is over (its last frame, or it's put
    /// elsewhere, or its player leaves): it's out of hyperspace for the
    /// encounters, from where the spin had taken it.
    /// </summary>
    private void EndSpin(Player p)
    {
        if (p.Ship.Spin is not { } spin)
            return;
        _spinEndedAt[p.Id] = (spin.UniverseX, spin.UniverseY);
        p.Ship.Spin = null;
    }

    /// <summary>
    /// HYPERSPACE_ENCOUNTER_EVENT's check_hyperspace_encounter, called as
    /// the event runs each day: in UQM, the roll for the flagship if it's
    /// in hyperspace; here, for each player in hyperspace, in the game's
    /// order, each with their own numbers (stage 3b, decision 3). During
    /// the starbase's two weeks too, for the players in hyperspace
    /// meanwhile (StarbaseVisit takes them back with the two weeks).
    /// </summary>
    private void HyperspaceEncounterRolls()
    {
        ++HyperspaceEncounterDays;
        if (Encounters is not { } encounters)
            return;
        foreach (var p in _players.Where(InHyperspace))
        {
            var f = HyperspaceFlightOf(p);
            encounters.Roll(Story!, p.Id, f.UniverseX, f.UniverseY, f.Fuel, HyperspaceRandomOf(p.Id), p.Ship.UsedBroadcaster);
            // check_hyperspace_encounter spends the broadcaster's use: the roller's own (stage 3b-2 item 4).
            p.Ship.UsedBroadcaster = false;
        }
    }

    /// <summary>How many times the clock's encounter event has asked for the day's rolls (tests).</summary>
    internal int HyperspaceEncounterDays { get; private set; }

    /// <summary>
    /// The end of a world frame: the encounters' elements' part of the
    /// frame just flown (their vortices, RedrawQueue's), then the clock
    /// (a new day rolls for each player in hyperspace), then the encounters
    /// fly (SeedUniverse's ProcessEncounters) and each player in hyperspace
    /// draws for an ambient star, as UQM's next frame begins. A player
    /// coming into hyperspace gets new numbers; one leaving it keeps the
    /// groups in their span (<see cref="HyperEncounter.KeptFor"/>).
    /// </summary>
    internal void EndOfFrame()
    {
        var encounters = Encounters;
        if (encounters is not null)
            SyncHyperspacePlayers(encounters);
        encounters?.ElementsFrame();
        foreach (var p in _players.Where(InHyperspace))
        {
            // The first frame's view set up (MoveGalaxy's VIEW_CHANGE): a
            // number for each of the background's 90 near stars.
            if (_hyperRandom.TryGetValue(p.Id, out var r) && r.Frames == 1)
            {
                for (int i = 0; i < 90; ++i)
                    r.Next("MoveGalaxy");
            }
        }
        // GameClockTick: a new day runs its events (processClockDayEvents);
        // then checkArilouGate (starcon.c's battle frame): the portal's
        // counter, shared, stepped once a world frame.
        if (Clock.Tick(Settings.SecondsPerGameDay))
            RunDayEvents();
        Story?.StepArilouGate();
        if (encounters is null)
            return;
        var players = HyperspacePlayers();
        encounters.Process(players, RollerAt, HyperspaceRandomOf, EncounterRaces);
        foreach (var p in players)
        {
            // SeedUniverse's ambient star: one in eight frames.
            var r = HyperspaceRandomOf(p.Id);
            if (((ushort)r.Next("SeedUniverse") & 7) == 0)
                r.Next("AddAmbientElement");
            r.Frames++;
        }
    }

    // Where a group a player's roll made comes in: round them (their
    // hyperspace place, kept while they're out of it).
    private (short X, short Y) RollerAt(string id) =>
        Find(id) is { } p ? (HyperspaceFlightOf(p).UniverseX, HyperspaceFlightOf(p).UniverseY) : ((short)0, (short)0);

    // Players coming into hyperspace get their numbers (Battle's seed) and
    // are no longer kept for; those leaving it (into a system, a battle, a
    // talk, a wreck, or away) keep the groups in their span, from where
    // they left, and their numbers go.
    private void SyncHyperspacePlayers(HyperEncounters encounters)
    {
        var now = _players.Where(InHyperspace).Select(p => p.Id).ToHashSet();
        foreach (var id in _inHyperspaceBefore.Where(id => !now.Contains(id)))
        {
            _hyperRandom.Remove(id);
            if (Find(id) is not { } p)
                continue;
            (short X, short Y) at = _spinEndedAt.TryGetValue(id, out var spunTo) ? spunTo
                : (p.Ship.Flight.UniverseX, p.Ship.Flight.UniverseY);
            foreach (var e in encounters.List.Where(e => HyperEncounters.InSpan(e, at.X, at.Y)))
                e.KeptFor[id] = at;
        }
        foreach (var id in now)
        {
            HyperspaceRandomOf(id);
            foreach (var e in encounters.List)
                e.KeptFor.Remove(id);
        }
        _inHyperspaceBefore = now;
        _spinEndedAt.Clear();
    }

    /// <summary>
    /// After a player's hyperspace flight: the group their ship touches, if
    /// any, meets them (decision 5; encounter_collision): their meeting
    /// begins (GameWorld.Meetings.cs). A group another player is meeting, or
    /// whose race another player is talking to (the lock taken), passes
    /// through them, and they're told who's talking.
    /// </summary>
    private void TouchEncounters(Player player, (long X, long Y) before)
    {
        if (Encounters is not { } encounters || Shapes is null)
            return;
        if (player.Ship.Meeting is null
            && encounters.Touch(player.Ship.Flight, Shapes, before,
                e => e.Element is { Nonsolid: false, Met: null } && MayMeetOrRescue(player, e.Race)) is { } met)
        {
            MeetInHyperspace(player, met);
            return;
        }
        if (encounters.Touch(player.Ship.Flight, Shapes, before,
                e => e.Element is { Nonsolid: false } el && (el.Met is { } by && by != player.Id || !MayMeetOrRescue(player, e.Race)))
            is { } passing)
            TellWhoIsTalking(player, passing.Race);
    }

    /// <summary>The encounters and each player's numbers as they are, for taking back (the starbase's two weeks).</summary>
    internal (List<HyperEncounter> List, Dictionary<string, HyperspaceRandom> Random)? EncountersState() =>
        Encounters is { } e ? (e.Copy(), _hyperRandom.ToDictionary(r => r.Key, r => r.Value.Clone())) : null;

    /// <summary>Puts back what <see cref="EncountersState"/> took.</summary>
    internal void RestoreEncounters((List<HyperEncounter> List, Dictionary<string, HyperspaceRandom> Random) state)
    {
        Encounters!.Restore(state.List);
        _hyperRandom.Clear();
        foreach (var (id, r) in state.Random)
            _hyperRandom[id] = r.Clone();
    }

    /// <summary>
    /// The encounters as a save has them: a ship spinning into a star is
    /// saved in its system (its entry isn't saved), so the groups in its
    /// span are kept for it from where its spin has taken it, as when the
    /// spin ends. One spinning into a group it met is kept for the same way,
    /// though it's saved in hyperspace where it met the group (a meeting
    /// isn't saved), so loaded it's back in hyperspace and no longer kept for.
    /// </summary>
    internal IReadOnlyList<HyperEncounter>? EncountersForSave(IReadOnlyList<HyperEncounter>? list)
    {
        var spinning = _players.Where(p => InHyperspace(p) && p.Ship.Spin is not null).ToList();
        if (list is null || spinning.Count == 0)
            return list;
        var copy = list.Select(e => e.Clone()).ToList();
        foreach (var p in spinning)
        {
            var f = p.Ship.Spin!;
            foreach (var e in copy.Where(e => HyperEncounters.InSpan(e, f.UniverseX, f.UniverseY)))
                e.KeptFor[p.Id] = (f.UniverseX, f.UniverseY);
        }
        return copy;
    }

    /// <summary>A loaded game: the players whose ships were in hyperspace left it as it was saved (they're away), keeping the groups round them.</summary>
    internal void LoadedInHyperspace() =>
        _inHyperspaceBefore = _players.Where(p => p.Ship.Activity == Activity.Hyperspace && !p.Ship.Destroyed)
            .Select(p => p.Id).ToHashSet();
}
