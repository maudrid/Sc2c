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

    /// <summary>
    /// The talk over, its closing lines on the player's communication screen
    /// (the alien's lines after the last reply, the drone's message whole:
    /// DoCommunication's last replay, num_responses 0), the meeting waiting
    /// for that player alone until they've gone through them, under the
    /// race's lock still, before its battle or flight (the owner,
    /// 2026-11-21, question 10; stage 3b-1 item 11).
    /// </summary>
    Closing,

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
/// The encounter screen's entries as a player's cursor goes over them:
/// Hail and Attack (<see cref="EncounterAction"/>), and for the host the
/// game menu (DoSelectAction's ATTACK + 1, GameOptions; the owner,
/// 2026-11-21, question 9), which comes in a later stage as orbit's does.
/// </summary>
public enum EncounterEntry
{
    Hail,
    Attack,
    GameMenu,
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

    /// <summary>
    /// The meeting's number in the game (from 1, not saved), which the
    /// encounter screen's choice is sent with (stage 3b-1 item 10), so a
    /// choice meant for one meeting can't choose in the next.
    /// </summary>
    public int Id { get; internal init; }

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

    /// <summary>
    /// The battle's planet (BATTLE_PLANET's world, a data_index; in
    /// hyperspace a random type), chosen as the meeting's talk loads, as
    /// BuildBattle loads the gravity well before the encounter screen
    /// draws it: its battle's arena has the same.
    /// </summary>
    public byte BattlePlanet { get; internal set; }

    /// <summary>
    /// In a system (not at a world), the planet the encounter screen names
    /// (GLOBAL_SIS (PlanetName) as generateName sets it: the inner
    /// system's, or the closest in the outer system), or null.
    /// </summary>
    public int? NamedPlanet { get; internal set; }

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
    /// Whether the talk ended in instant victory (setSegue (Segue_victory):
    /// the Slylandro probe's destruct code), no battle fought: the group's
    /// ships destroyed as on a battle won, <see cref="Salvage"/> paid to the
    /// player; while the Ur-Quan guard the Sa-Matra it wins nothing (no
    /// ship destroyed, no salvage: <see cref="Left"/> holds them all).
    /// </summary>
    public bool InstantVictory { get; internal set; }

    /// <summary>The resource units an instant victory paid the player (UninitEncounter's bounty for each ship).</summary>
    public uint Salvage { get; internal set; }

    /// <summary>
    /// The ships left once it's over (UninitEncounter's npc_built_ship_q):
    /// each ship's crew and captain, in the group's order; all of them after a
    /// talk without a battle, none after a battle won.
    /// </summary>
    public IReadOnlyList<(int Crew, int Captain)>? Left { get; internal set; }

    // The player's ship back in flight from the meeting (after its battle, or as it ended).
    internal bool Returned;

    // How a saved copy's stand-in for a meeting at its closing lines ended its talk (no Talk there: question 13).
    internal Segue? EndedAs;

    /// <summary>How many sets of replies the game's talks offered before this meeting's talk.</summary>
    public int FirstRound { get; }

    /// <summary>
    /// The set of replies offered now: a round names one set of replies in
    /// the whole game (the game's one counter numbers every set in every
    /// talk, stage 3b-1 item 10, so a story's and a meeting's running side
    /// by side never share one); <see cref="FirstRound"/> before any.
    /// </summary>
    public int Round => Talk is { Offers: > 0 } t ? t.RoundOf(t.Offers - 1, FirstRound) : FirstRound;

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
