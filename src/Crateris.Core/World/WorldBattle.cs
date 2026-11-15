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
    /// against (<c>--start-battle</c>): by their ids in data/ships.json
    /// (<see cref="Combat.RaceShips.Ported"/>: the Ilwrath and stage 4b's, each
    /// of items 2 to 4 adding its races).
    /// </summary>
    public static IReadOnlyList<string> Enemies => Combat.RaceShips.Ported;

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
    /// race's RACE_SHIP_COST / 8 resource units (an Ilwrath, 1,000, pays
    /// 125). A ship of no race of the full game's (not in avail_race_q: the
    /// Androsynth, Chenjesu and Mmrnmhrm) pays none, as UQM suppresses the
    /// tally and salvage for a battle whose encounter race isn't one
    /// (encount.c's VictoryState = -1); any other ship without a cost is
    /// an error in the data.
    /// </summary>
    public int Salvage(ShipDescription ship)
    {
        string race = RaceOf(ship) ?? (NoRace.Contains(ship.Species ?? "") ? "" : throw new ArgumentException($"{ship.Id} has no race"));
        if (race.Length == 0)
            return 0;
        return _shipCosts.TryGetValue(race, out int cost) ? cost >> 3
            : throw new KeyNotFoundException($"no ship cost for {race}");
    }

    /// <summary>
    /// A ship's race in UQM's race enum (races.h: ARILOU_SHIP, ...), which
    /// data/races.json names, from its species (ships.json's SPECIES_ID):
    /// the species' name but for three (the Earthling's HUMAN_SHIP, the
    /// Ur-Quan's URQUAN_SHIP, the Kohr-Ah's BLACK_URQUAN_SHIP); null for a
    /// ship of no race of the full game's (<see cref="NoRace"/>, the
    /// flagship, the Sa-Matra, the probe).
    /// </summary>
    public static string? RaceOf(ShipDescription ship) => ship.Species switch
    {
        "EARTHLING_ID" => "HUMAN_SHIP",
        "UR_QUAN_ID" => "URQUAN_SHIP",
        "KOHR_AH_ID" => "BLACK_URQUAN_SHIP",
        "SIS_SHIP_ID" or "SA_MATRA_ID" or "UR_QUAN_PROBE_ID" or null => null,
        { } s when NoRace.Contains(s) => null,
        { } s when s.EndsWith("_ID", StringComparison.Ordinal) => s[..^3] + "_SHIP",
        _ => null,
    };

    /// <summary>The species that fight but have no race in the full game (no fleet in avail_race_q, no ship cost).</summary>
    public static readonly IReadOnlySet<string> NoRace = new HashSet<string> { "ANDROSYNTH_ID", "CHENJESU_ID", "MMRNMHRM_ID" };

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
/// One ship of an enemy fleet (a SHIP_FRAGMENT of npc_built_ship_q): its
/// ship's id in data/ships.json and its crew (null: its race's starting
/// crew, as CloneShipFragment's 0).
/// </summary>
public sealed record EnemyFleetShip(string Ship, int? Crew = null);

/// <summary>
/// The enemy's side of a battle (npc_built_ship_q, stage 4b item 5): its
/// ships, fought one at a time in this order, the next warping in when one
/// dies; or, infinite (FleetIsInfinite: a homeworld's, its one ship's crew
/// INFINITE_FLEET), one ship brought back with full crew and a new captain
/// each time it dies, so it can't be beaten.
/// </summary>
public sealed record EnemyFleet(IReadOnlyList<EnemyFleetShip> Ships, bool Infinite = false)
{
    /// <summary>MAX_SHIPS_PER_SIDE: the most ships a fleet in a battle has.</summary>
    public const int MostShips = 14;

    private readonly IReadOnlyList<EnemyFleetShip> _ships = Ships.ToArray();

    /// <summary>The ships, copied (made or set with <c>with</c>): a list changed later doesn't change the fleet.</summary>
    public IReadOnlyList<EnemyFleetShip> Ships { get => _ships; init => _ships = value.ToArray(); }

    /// <summary>A fleet of one ship.</summary>
    public static EnemyFleet Of(string ship, int? crew = null) => new(new[] { new EnemyFleetShip(ship, crew) });

    /// <summary>A fleet of these ships, each with its race's starting crew.</summary>
    public static EnemyFleet Of(params string[] ships) => new(ships.Select(s => new EnemyFleetShip(s)).ToList());

    /// <summary>An infinite fleet of this ship (CloneShipFragment's INFINITE_FLEET): each one with full crew.</summary>
    public static EnemyFleet InfiniteOf(string ship) => new(new[] { new EnemyFleetShip(ship) }, Infinite: true);

    public bool Equals(EnemyFleet? other) => other is not null && Infinite == other.Infinite && Ships.SequenceEqual(other.Ships);

    public override int GetHashCode() => Ships.Aggregate(Infinite.GetHashCode(), (h, s) => HashCode.Combine(h, s));
}

/// <summary>
/// How a battle starts (<see cref="GameWorld.StartBattle"/>): the enemy's
/// crew for a battle against one ship (null: its race's starting crew; a
/// fleet's crews are its ships', <see cref="EnemyFleet"/>), whether an Ilwrath may cloak
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
/// A player in a battle: their own ship's battle code and
/// <see cref="Starship"/>, new for each battle, and what it had when it
/// joined (a save during the battle is of the ship as it was then,
/// decision 14). The host with fleet ships brings them in too (stage 4b
/// item 8): the host's <see cref="Armada"/>, each ship picked in turn on
/// the pick screen (<see cref="Pick"/>), the flagship last.
/// </summary>
public sealed class BattleFighter
{
    private readonly List<BattleFleetShip> _fleetShips;

    internal BattleFighter(Player player, SisShip code, Starship starship, HoldState hold, uint fuel, long frame,
        IReadOnlyList<BattleFleetShip>? fleetShips = null)
    {
        Player = player;
        Code = code;
        OwnStarship = Starship = starship;
        JoinedHold = hold;
        JoinedFuel = fuel;
        JoinedFrame = frame;
        _fleetShips = fleetShips?.ToList() ?? new List<BattleFleetShip>();
        if (_fleetShips.Count > 0)
            Armada = new Armada(_fleetShips.Select(f => f.Starship).ToList(), starship);
    }

    public Player Player { get; }

    /// <summary>The player's own ship's battle code, built from its slots and crew (its <see cref="SisShip.Result"/> once freed).</summary>
    public SisShip Code { get; }

    /// <summary>
    /// The ship the player flies in the battle now, or flew last: their own
    /// ship (<see cref="OwnStarship"/>), or a fleet ship of the host's; on
    /// the pick screen the one flown last (their own before any).
    /// </summary>
    public Starship Starship { get; internal set; }

    /// <summary>The player's own ship's (the flagship's, for the host).</summary>
    public Starship OwnStarship { get; }

    /// <summary>
    /// Whether the ship flown now (or last) is the player's own: always
    /// without fleet ships; with them, once the flagship is picked.
    /// </summary>
    public bool FlyingOwnShip => Starship == OwnStarship && (Armada is null || Armada.FlagshipChosen);

    /// <summary>
    /// The ship flown now's (or last's) id in data/ships.json: the
    /// flagship's (which escorts fly too, stage 4 decision 3), or a fleet
    /// ship's race's.
    /// </summary>
    public string ShipId => FleetShipOf(Starship)?.Ship.Id ?? ShipDescriptions.Flagship;

    /// <summary>The host's fleet ships brought into the battle (built_ship_q's, as they joined), in the fleet's order; none for a guest.</summary>
    public IReadOnlyList<BattleFleetShip> FleetShips => _fleetShips;

    /// <summary>The fleet ship flying as this <see cref="Combat.Starship"/>, or null.</summary>
    public BattleFleetShip? FleetShipOf(Starship starship) => _fleetShips.FirstOrDefault(f => f.Starship == starship);

    /// <summary>
    /// The player's ships picked one at a time (race_q[0]: the fleet ships,
    /// then the flagship), or null: a player with no fleet ships comes in
    /// with their own ship alone, as in stage 4a.
    /// </summary>
    public Armada? Armada { get; }

    /// <summary>
    /// The pick screen, open while the player chooses their next ship
    /// (GetArmadaStarShip): as they join, and when the ship flown dies or
    /// retreats, unless the enemy has no ship left; null otherwise.
    /// </summary>
    public Systems.PickShipMenu? Pick { get; internal set; }

    /// <summary>A ship picked that waits for room in the battle's list (UQM gives it the dead ship's element), or null.</summary>
    internal Starship? PickedWaiting { get; set; }

    /// <summary>The hold as the ship joined.</summary>
    public HoldState JoinedHold { get; }

    /// <summary>The fuel as the ship joined.</summary>
    public uint JoinedFuel { get; }

    /// <summary>The world frame it joined in.</summary>
    public long JoinedFrame { get; }

    /// <summary>
    /// How it came out of the battle, once it has: destroyed as its own
    /// ship's crew runs out (it watches the battle on). A fleet ship's
    /// death or retreat isn't the player's (<see cref="BattleFleetShip.Exit"/>).
    /// </summary>
    public BattleExit? Exit { get; internal set; }

    /// <summary>The crew its own ship came out with (without the captain).</summary>
    public int CrewLeft { get; internal set; }

    /// <summary>Its share of the salvage, on a win (one per player, whatever they flew: stage 4b decision 5).</summary>
    public uint Salvage { get; internal set; }

    // Its ship's last place in the battle (the snapshot's, for a ship gone once its explosion is over).
    internal short LastX, LastY;
}

/// <summary>
/// One of the host's fleet ships in a battle (stage 4b item 8): the fleet
/// ship as it joined (its race, crew, captain and slot), its ship's
/// description and its <see cref="Combat.Starship"/>, and how it came out.
/// </summary>
public sealed class BattleFleetShip
{
    internal BattleFleetShip(FleetShip joined, ShipDescription ship, Starship starship)
    {
        Joined = joined;
        Ship = ship;
        Starship = starship;
    }

    /// <summary>The fleet ship as it joined the battle.</summary>
    public FleetShip Joined { get; }

    /// <summary>Its ship's description.</summary>
    public ShipDescription Ship { get; }

    public Starship Starship { get; }

    /// <summary>
    /// How it came out, once it has: destroyed (gone from the fleet,
    /// decision 4), retreated (warped out, or taken out as the host
    /// disconnected: its crew kept, out for this battle), or fought (in it
    /// with crew at the end); null for one never flown, or still flying.
    /// </summary>
    public BattleExit? Exit { get; internal set; }

    /// <summary>The crew it came out with (its crew_level then, UninitShips' or new_ship's).</summary>
    public int CrewLeft { get; internal set; }

    /// <summary>Whether it has been picked and come into the battle.</summary>
    public bool Flown { get; internal set; }
}

/// <summary>
/// A battle in the world (stage 4a item 6): the enemy's fleet (one ship at
/// a time, stage 4b item 5) against the players who fly into it. It has a marker where it started
/// (<see cref="Place"/>), shown to the players there, which any of them
/// may fly into to join (decision 6), and runs on the host at 24 frames a
/// second while the world does (a story talk freezes it, decision 11, but
/// for the flagship's as it explodes, item 7). It ends when one side has
/// no ship left able to fight: won, the salvage split between the players
/// still in it (decision 9; the host choosing the next ship on the pick
/// screen is in it, stage 4b decision 2), or lost.
/// </summary>
public sealed class WorldBattle
{
    private readonly List<BattleFighter> _fighters = new();
    private readonly HashSet<string> _left = new();
    private readonly IReadOnlyList<(Starship Starship, ShipDescription Ship)> _enemies;
    private Starship _lastEnemy;

    internal WorldBattle(int id, BattlePlace place, byte battlePlanet, Battle battle,
        IReadOnlyList<(Starship Starship, ShipDescription Ship)> enemies, EnemyFleet fleet, BattleStart start,
        string startedBy, long frame)
    {
        Id = id;
        Place = place;
        BattlePlanet = battlePlanet;
        Battle = battle;
        _enemies = enemies;
        _lastEnemy = enemies[0].Starship;
        EnemyFleet = fleet;
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

    /// <summary>
    /// The enemy's ship now: the one fighting, or exploding, or (between
    /// one's death and the next's coming in, and once the last is gone)
    /// the last that was.
    /// </summary>
    public Starship Enemy
    {
        get
        {
            foreach (var (starship, _) in _enemies)
                if (starship.Ship is not null)
                    return _lastEnemy = starship;
            return _lastEnemy;
        }
    }

    /// <summary>The enemy's ship now's description (<see cref="Enemy"/>).</summary>
    public ShipDescription EnemyShipNow => EnemyShipOf(Enemy);

    /// <summary>
    /// The encounter's race (EncounterRace): the enemy fleet's first ship's,
    /// whose bounty the salvage pays for each ship destroyed, and which
    /// the battle is named by.
    /// </summary>
    public ShipDescription EnemyShip => _enemies[0].Ship;

    /// <summary>The enemy's ships (race_q[1]), in the order they come in, each with its description.</summary>
    public IReadOnlyList<(Starship Starship, ShipDescription Ship)> Enemies => _enemies;

    /// <summary>An enemy ship's description.</summary>
    public ShipDescription EnemyShipOf(Starship starship) => _enemies.First(e => e.Starship == starship).Ship;

    /// <summary>The enemy's fleet as the battle started.</summary>
    public EnemyFleet EnemyFleet { get; }

    /// <summary>
    /// The enemy's fleet once the battle is over (null before), for 3b's
    /// encounters to keep: UninitEncounter's npc_built_ship_q, each ship's
    /// crew recorded as it died or warped out or the battle ended
    /// (UpdateShipFragCrew), those destroyed taken out, and on a win none
    /// left; an infinite fleet stays as it was.
    /// </summary>
    public EnemyFleet? EnemyFleetLeft { get; internal set; }

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
