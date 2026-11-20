// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: RaceCommunication,
// InitCommunication; src/uqm/encount.c: InitEncounter, DoSelectAction,
// BuildBattle, EncounterBattle, UninitEncounter), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>Where a meeting with a group is (RaceCommunication's order).</summary>
public enum MeetingStep
{
    /// <summary>In hyperspace, the ship spinning into the group it touched (encounter_collision's RANDOM_ENCOUNTER_TRANSITION), still in hyperspace until the spin's last frame.</summary>
    Spin,

    /// <summary>The encounter screen (InitEncounter): the group's ships, Hail or Attack.</summary>
    Encounter,

    /// <summary>The race's talk (HailAlien), under its dialogue lock.</summary>
    Talk,

    /// <summary>The battle against the group's ships (EncounterBattle), its marker where they met.</summary>
    Battle,

    /// <summary>Over: the group has re-formed or gone.</summary>
    Done,
}

/// <summary>The encounter screen's choices (DoSelectAction's HAIL and ATTACK).</summary>
public enum EncounterAction
{
    Hail,
    Attack,
}

/// <summary>
/// One player's meeting with a group of alien ships it touched
/// (docs/plans/stage-3b.md, item 5; decision 5: the player it touches
/// meets it alone): in hyperspace the ship's spin into the group, then
/// RaceCommunication's order: the race's talk loaded (its init sets
/// whether it's hostile), the encounter screen if it is (Hail or Attack),
/// the talk (an ordinary one, under the race's dialogue lock), the battle
/// against the group's ships with their crew if the talk ends hostile or
/// the player attacks (at a race's world, its infinite fleet: <see cref="World"/>),
/// and the group written back: re-forming with the
/// ships left (one-shot after a talk without a battle), or gone. The group
/// stays where it met the player meanwhile. Not saved, as talks and battles
/// aren't.
/// </summary>
public sealed class Meeting
{
    internal Meeting(Player player, int race, string raceId, IReadOnlyList<EnemyFleetShip> ships, int firstRound)
    {
        Player = player;
        Race = race;
        RaceId = raceId;
        Ships = ships.ToArray();
        FirstRound = firstRound;
    }

    /// <summary>The player meeting the group.</summary>
    public Player Player { get; }

    /// <summary>The group's race (races.h's number; the first ship's, which picks the talk).</summary>
    public int Race { get; }

    /// <summary>The group's race by name (data/races.json's).</summary>
    public string RaceId { get; }

    /// <summary>The group's ships as met (npc_built_ship_q): each ship, its crew and captain.</summary>
    public IReadOnlyList<EnemyFleetShip> Ships { get; }

    /// <summary>The hyperspace group met, or null for a system's.</summary>
    public HyperEncounter? Encounter { get; internal init; }

    /// <summary>The system of the group met, or null in hyperspace.</summary>
    public int? Star { get; internal init; }

    /// <summary>The system's group met, or null in hyperspace or at a world.</summary>
    public IpGroup? Group { get; internal init; }

    /// <summary>
    /// The world whose orbit started the meeting (the Ilwrath homeworld:
    /// genilw.c's generateOrbital), its planet and moon, or null for a
    /// group met. Its talk knows it's at the race's world
    /// (GLOBAL_FLAGS_AND_DATA bit 7), and its fleet is infinite.
    /// </summary>
    public (int Planet, int? Moon)? World { get; internal init; }

    /// <summary>
    /// Whether the ships are an infinite fleet (CloneShipFragment's
    /// INFINITE_FLEET: a race's world's), their one ship brought back with
    /// full crew each time it dies.
    /// </summary>
    public bool Infinite { get; internal init; }

    public MeetingStep Step { get; internal set; }

    /// <summary>
    /// The frames of the ship's spin into the group still to come, its
    /// last frame (out of hyperspace) among them (hyperspace). Counted in
    /// the spin's own frames, as the spin back in is
    /// (<see cref="PlayerShip.AppearFrames"/>), so a pause doesn't shorten it.
    /// </summary>
    public int SpinFrames { get; internal set; }

    /// <summary>The conversation whose dialogue lock the meeting holds until its talk is over, or null (none, or no locks).</summary>
    public string? Conversation { get; internal set; }

    /// <summary>
    /// The race's talk, or null while the race's talk isn't ported (stage
    /// 3b-1 items 6 to 8 port theirs, 3b-2 the rest; docs/differences.md row
    /// 68): the encounter screen is shown all the same, a hail ends at once
    /// as a talk ending in peace, an attack fights.
    /// </summary>
    public Talk? Talk { get; internal set; }

    /// <summary>Whether the player hailed (from the screen, or at once: the talk's init wasn't hostile).</summary>
    public bool Hailed { get; internal set; }

    /// <summary>The battle against the group, once the talk ended hostile or the player attacked.</summary>
    public WorldBattle? Battle { get; internal set; }

    /// <summary>
    /// The ships left once it's over (UninitEncounter's npc_built_ship_q):
    /// each ship's crew and captain, in the group's order; all of them after a
    /// talk without a battle, none after a battle won.
    /// </summary>
    public IReadOnlyList<(int Crew, int Captain)>? Left { get; internal set; }

    // The player's ship back in flight from the meeting (after its battle, or as it ended).
    internal bool Returned;

    /// <summary>How many sets of replies the game's talks offered before this meeting's talk.</summary>
    public int FirstRound { get; }

    /// <summary>The set of replies offered now: a round names one set of replies in the whole game.</summary>
    public int Round => FirstRound + (Talk?.Events.Count(e => e is RepliesOffered) ?? 0);

    /// <summary>A race's name for the players' notices.</summary>
    public static string RaceName(string race) => race switch
    {
        "URQUAN_SHIP" or "URQUAN_DRONE_SHIP" => "Ur-Quan",
        "BLACK_URQUAN_SHIP" => "Kohr-Ah",
        "ZOQFOTPIK_SHIP" => "Zoq-Fot-Pik",
        "YEHAT_REBEL_SHIP" => "Yehat",
        "HUMAN_SHIP" => "Earthlings",
        _ => race.EndsWith("_SHIP", StringComparison.Ordinal)
            ? race[0] + race[1..^5].ToLowerInvariant()
            : race,
    };
}
