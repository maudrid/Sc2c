// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/encount.c: BuildBattle,
// EncounterBattle, UninitEncounter's salvage; src/uqm/pickship.c: the
// retreat's fuel), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;

namespace Crateris.Core.World;

/// <summary>
/// What a world needs to run battles (stage 4a item 6): the ships'
/// descriptions (data/ships.json), the battle's sheets (the content's, or a
/// test's own) and each race's ship cost, which the salvage is paid from
/// (RACE_SHIP_COST, data/races.json). Only the races whose code is ported
/// can be fought (<see cref="Enemies"/>); every ship's shapes are made
/// (<see cref="Fighters"/>).
/// </summary>
public sealed class BattleKit
{
    private readonly IReadOnlyDictionary<string, int> _shipCosts;

    /// <param name="ships">Every ship's description.</param>
    /// <param name="graphics">The battle's sheets.</param>
    /// <param name="shipCosts">RACE_SHIP_COST by race (ILWRATH_SHIP and so on: <see cref="LoadShipCosts"/>).</param>
    public BattleKit(ShipDescriptions ships, IBattleGraphics graphics, IReadOnlyDictionary<string, int> shipCosts)
    {
        Ships = ships;
        Graphics = graphics;
        _shipCosts = shipCosts;
    }

    public ShipDescriptions Ships { get; }
    public IBattleGraphics Graphics { get; }

    /// <summary>
    /// The race ships whose code is ported, which a battle can be fought
    /// against (<c>--start-battle</c>): by their ids in data/ships.json.
    /// Stage 4b's items 2 to 4 each add their races.
    /// </summary>
    public static readonly IReadOnlyList<string> Enemies = new[] { ShipDescriptions.Ilwrath };

    /// <summary>
    /// The ships in data/ships.json no battle of the port has: the Sa-Matra
    /// (lastbat, the endgame's last battle, not a ship of 16 facings) and
    /// the Ur-Quan drone (probe, which never fights and has no battle graphics).
    /// </summary>
    public static readonly IReadOnlyList<string> NotFought = new[] { "lastbat", "probe" };

    /// <summary>
    /// Every race ship, in data/ships.json's order: every ship but the
    /// flagship and <see cref="NotFought"/>'s, the Ilwrath and stage 4b's 24.
    /// </summary>
    public static IEnumerable<ShipDescription> RaceShips(ShipDescriptions ships) =>
        ships.All.Where(s => s.Id != ShipDescriptions.Flagship && !NotFought.Contains(s.Id));

    /// <summary>
    /// Every ship a battle can have, its code ported yet or not: the
    /// flagship (which escorts fly too, stage 4 decision 3) and every race
    /// ship (<see cref="RaceShips"/>). The game makes their shapes as the
    /// content is opened, headless too, and with a window their sprites, so
    /// a broken sheet of any of them is found then, not in a battle.
    /// </summary>
    public static IEnumerable<ShipDescription> Fighters(ShipDescriptions ships) =>
        RaceShips(ships).Prepend(ships[ShipDescriptions.Flagship]);

    /// <summary>
    /// The salvage for a ship destroyed (UninitEncounter's bounty): its
    /// race's RACE_SHIP_COST / 8 resource units (an Ilwrath, 1,000, pays 125).
    /// </summary>
    public int Salvage(ShipDescription ship)
    {
        string race = ship.Species is { } species && species.EndsWith("_ID", StringComparison.Ordinal)
            ? species[..^3] + "_SHIP"
            : throw new ArgumentException($"{ship.Id} has no race");
        return _shipCosts.TryGetValue(race, out int cost) ? cost >> 3
            : throw new KeyNotFoundException($"no ship cost for {race}");
    }

    /// <summary>RACE_SHIP_COST by race, from data/races.json's ship_cost.</summary>
    public static IReadOnlyDictionary<string, int> LoadShipCosts(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "races.json")));
        var costs = new Dictionary<string, int>();
        foreach (var race in doc.RootElement.GetProperty("races").EnumerateArray())
            if (race.TryGetProperty("ship_cost", out var cost))
                costs[race.GetProperty("race").GetString()!] = cost.GetInt32();
        return costs;
    }

    /// <summary>The kit from data/ (the descriptions and costs) and the battle's sheets.</summary>
    public static BattleKit Load(string dataDirectory, ShipDescriptions ships, IBattleGraphics graphics) =>
        new(ships, graphics, LoadShipCosts(dataDirectory));
}

/// <summary>
/// How a battle starts (<see cref="GameWorld.StartBattle"/>): the enemy's
/// crew (null: its race's starting crew), whether an Ilwrath may cloak
/// (<see cref="IlwrathShip.NoCloak"/>, the Earth fight's rule), whether it
/// pays salvage (UQM marks the Earth Ilwrath's as paying none, race_id ~0),
/// and whether it may be retreated from at all (in an encounter, as UQM's
/// RunAwayAllowed: the starbase must also be available, and the bomb's
/// carrier can't).
/// </summary>
public sealed record BattleStart(int? EnemyCrew = null, bool NoCloak = false, bool PaysSalvage = true,
    bool RetreatAllowed = true);

/// <summary>
/// Where a battle started, and its marker is: in hyperspace (no star; X and
/// Y the ship's hyperspace place, in logical units), or a system's outer
/// view (the star, no inner planet) or a planet's inner system (both), X
/// and Y its place there (ip_location).
/// </summary>
public readonly record struct BattlePlace(int? Star, int? InnerPlanet, int X, int Y)
{
    public bool InHyperspace => Star is null;
}

/// <summary>How a player's ship came out of a battle.</summary>
public enum BattleExit
{
    /// <summary>Still in it, alive, at its end.</summary>
    Fought,

    /// <summary>Ran away (UQM's retreat), or was still running away at the end.</summary>
    Retreated,

    /// <summary>Its player disconnected: it left at once, as a retreat (decision 10).</summary>
    Disconnected,

    /// <summary>Its crew ran out.</summary>
    Destroyed,
}

/// <summary>How a battle ended (decision 9).</summary>
public enum BattleOutcome
{
    /// <summary>The enemy destroyed with a player ship still in it.</summary>
    Won,

    /// <summary>No player ship left in it: every one destroyed, retreated or disconnected.</summary>
    Lost,
}

/// <summary>
/// A player's ship in a battle: its battle code and <see cref="Starship"/>,
/// new for each battle, and what it had when it joined (a save during the
/// battle is of the ship as it was then, decision 14).
/// </summary>
public sealed class BattleFighter
{
    internal BattleFighter(Player player, SisShip code, Starship starship, HoldState hold, uint fuel, long frame)
    {
        Player = player;
        Code = code;
        Starship = starship;
        JoinedHold = hold;
        JoinedFuel = fuel;
        JoinedFrame = frame;
    }

    public Player Player { get; }

    /// <summary>The ship's battle code, built from its slots and crew (its <see cref="SisShip.Result"/> once freed).</summary>
    public SisShip Code { get; }

    public Starship Starship { get; }

    /// <summary>The hold as the ship joined.</summary>
    public HoldState JoinedHold { get; }

    /// <summary>The fuel as the ship joined.</summary>
    public uint JoinedFuel { get; }

    /// <summary>The world frame it joined in.</summary>
    public long JoinedFrame { get; }

    /// <summary>How it came out of the battle, once it has: destroyed as its crew runs out (it watches the battle on).</summary>
    public BattleExit? Exit { get; internal set; }

    /// <summary>The crew it came out with (without the captain).</summary>
    public int CrewLeft { get; internal set; }

    /// <summary>Its share of the salvage, on a win.</summary>
    public uint Salvage { get; internal set; }

    // Its ship's last place in the battle (the snapshot's, for a ship gone once its explosion is over).
    internal short LastX, LastY;
}

/// <summary>
/// A battle in the world (stage 4a item 6): one enemy ship against the
/// players who fly into it. It has a marker where it started
/// (<see cref="Place"/>), shown to the players there, which any of them
/// may fly into to join (decision 6), and runs on the host at 24 frames a
/// second while the world does (a story talk freezes it, decision 11, but
/// for the flagship's as it explodes, item 7). It ends when one side has
/// no ship left able to fight: won, the salvage split between the players
/// still in it (decision 9), or lost.
/// </summary>
public sealed class WorldBattle
{
    private readonly List<BattleFighter> _fighters = new();
    private readonly HashSet<string> _left = new();

    internal WorldBattle(int id, BattlePlace place, byte battlePlanet, Battle battle, Starship enemy,
        ShipDescription enemyShip, BattleStart start, string startedBy, long frame)
    {
        Id = id;
        Place = place;
        BattlePlanet = battlePlanet;
        Battle = battle;
        Enemy = enemy;
        EnemyShip = enemyShip;
        Start = start;
        StartedBy = startedBy;
        StartFrame = frame;
    }

    public int Id { get; }

    /// <summary>Where it started: its marker's place.</summary>
    public BattlePlace Place { get; }

    /// <summary>BATTLE_PLANET: the world (a data_index) whose planet is in the arena.</summary>
    public byte BattlePlanet { get; }

    /// <summary>The battle itself.</summary>
    public Battle Battle { get; }

    /// <summary>The enemy's ship.</summary>
    public Starship Enemy { get; }

    public ShipDescription EnemyShip { get; }

    public BattleStart Start { get; }

    /// <summary>The player whose ship started it.</summary>
    public string StartedBy { get; }

    /// <summary>The world frame it started in.</summary>
    public long StartFrame { get; }

    /// <summary>
    /// The starter's flight at its place as the battle started, at rest: in
    /// a system its system flight (null in hyperspace), and in hyperspace
    /// the star it had just left there; a wreck is put there as the battle
    /// ends (stage 4a item 7), as a guest is put at the host's place.
    /// </summary>
    internal Systems.IpFlightState? PlaceIp { get; init; }

    /// <inheritdoc cref="PlaceIp"/>
    internal int? PlaceStarJustLeft { get; init; }

    /// <summary>Every player's ship that has been in it, in the order they joined.</summary>
    public IReadOnlyList<BattleFighter> Fighters => _fighters;

    /// <summary>The players who left it (retreated or disconnected): they can't join it again (decision 6).</summary>
    public IReadOnlySet<string> Left => _left;

    /// <summary>How it ended, once it has.</summary>
    public BattleOutcome? Outcome { get; internal set; }

    /// <summary>The salvage paid on a win, before it's split (0 for a battle that pays none).</summary>
    public uint SalvagePaid { get; internal set; }

    /// <summary>A player's ship in it (out of it or not), or null.</summary>
    public BattleFighter? FighterOf(string id) => _fighters.FirstOrDefault(f => f.Player.Id == id);

    internal void Add(BattleFighter fighter) => _fighters.Add(fighter);

    /// <summary>
    /// The players whose ships were last seen flying in its place out of
    /// its marker's reach: only they join, by coming within it (a ship first
    /// seen within it doesn't join until it has been out of it).
    /// </summary>
    internal HashSet<string> SeenOutOfReach { get; } = new();

    internal void Leaves(string id) => _left.Add(id);

    // The enemy ship's last place (the snapshot's, once it's gone).
    internal short EnemyLastX, EnemyLastY;
}
