// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/encount.c: BuildBattle,
// EncounterBattle, UninitEncounter; src/uqm/comm.c: InitCommunication's
// BuildBattle; src/uqm/battle.c: RunAwayAllowed; src/uqm/pickship.c: the
// retreat's fuel; src/uqm/planets/solarsys.c: BATTLE_PLANET by the closest
// planet), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Core.World;

// Battles in the world (stage 4a item 6).
public sealed partial class GameWorld
{
    /// <summary>RUN_AWAY_FUEL_COST: 5 units of fuel, in hundredths.</summary>
    public const uint RetreatFuelCost = 5 * 100;

    private readonly List<WorldBattle> _battles = new();
    private readonly List<WorldBattle> _endedBattles = new();
    private readonly Dictionary<string, ShipInput> _battleInput = new();
    private readonly List<(string PlayerId, string Text)> _battleNotices = new();
    private readonly HashSet<(string PlayerId, int Battle)> _toldFull = new();
    private int _battleIds;

    /// <summary>
    /// The ships' descriptions and the battle's sheets, without which no
    /// battle can start (the game gives them as the content is opened; some
    /// tests give sheets of their own, some none).
    /// </summary>
    public BattleKit? BattleKit { get; set; }

    /// <summary>The battles going on, in the order they started; each has a marker where it started.</summary>
    public IReadOnlyList<WorldBattle> Battles => _battles;

    /// <summary>
    /// The flagship was lost (decision 12): the game is over for the group,
    /// and the world stops (nothing steps, joins or changes any more: a
    /// guest is refused, <see cref="JoinResult.GameOver"/>). As UQM's: its
    /// crew run out, it explodes, and once the explosion is over (and the
    /// enemy's victory ditty has played at least MIN_DITTY_FRAME_COUNT's 72
    /// frames: UQM waits for the ditty to end too, the engine here for no
    /// music) new_ship finds its side out of ships (the flagship picked
    /// leaves battle_counter at 1, pickship.c; IN_BATTLE cleared,
    /// tactrans.c) and the battle ends, the game over (CrewEnlisted = ~0:
    /// UninitEncounter skipped, starcon.c's loop left). That the other
    /// players' battle stops with it, unfinished, whoever is still
    /// fighting, and every other battle with the world, is this port's
    /// (UQM has only the one ship); at its battle's end if that comes
    /// first. Item 11 tells every player; item 12 shows UQM's
    /// game over and ends the host's game. From the frame its crew ran out
    /// (<see cref="FlagshipLost"/>) no save is made, so the host's save is
    /// kept as it was before the battle.
    /// </summary>
    public bool GameOver { get; private set; }

    /// <summary>
    /// The flagship's crew has run out in a battle (it's exploding, or the
    /// game is over): from this frame no ship docks at the starbase or
    /// starts a visit there, so no story pause starts (it would hold off the
    /// game over; one started in the same frame doesn't: the flagship's
    /// battle runs on through it to the game over), and no save is made
    /// (<see cref="Session.HostSession.SaveJson"/>; <see cref="HostSave.ToJson"/>
    /// refuses), so loading the host's save goes back to before the battle
    /// (decision 12), not to the flagship as it joined the battle it's
    /// lost in (decision 14).
    /// </summary>
    public bool FlagshipLost { get; private set; }

    /// <summary>
    /// The battles whose markers a player is shown (and may fly into): those
    /// where the player's ship is flying, in hyperspace or the same part
    /// (outer or inner) of the same system (joining one also needs it open to
    /// the player: not left by them, not decided, <see cref="JoinBattles"/>).
    /// None for a player away, in no flight (in orbit, a talk, docked,
    /// entering a star), in a battle, or destroyed.
    /// </summary>
    public IReadOnlyList<WorldBattle> MarkersFor(string id)
    {
        if (Find(id) is not { } player)
            return Array.Empty<WorldBattle>();
        if (!Flying(player))
            return Array.Empty<WorldBattle>();
        return _battles.Where(b => InPlace(b.Place, player.Ship)).ToList();
    }

    /// <summary>The battles that ended since the last call, each with its outcome and how each ship came out.</summary>
    public List<WorldBattle> TakeEndedBattles()
    {
        var all = _endedBattles.ToList();
        _endedBattles.Clear();
        return all;
    }

    /// <summary>What players are to be told about battles since the last call (a battle too full to join).</summary>
    public List<(string PlayerId, string Text)> TakeBattleNotices()
    {
        var all = _battleNotices.ToList();
        _battleNotices.Clear();
        return all;
    }

    /// <summary>
    /// A player's ship starts a battle where it is, in flight, against an
    /// enemy ship of a ported race (<see cref="BattleKit.Enemies"/>), as
    /// UQM's EncounterBattle does after a talk that ends hostile (3b's
    /// talks and item 9's Ilwrath call it; --start-battle does now). The
    /// ship stops where it is, which is the battle's marker; the arena's
    /// planet is BATTLE_PLANET's (the inner system's planet, or in the outer
    /// system the planet closest on the screen, as UQM names the place of
    /// an encounter), in hyperspace a random type's, as BuildBattle's; the
    /// arena's size and the list's cap are the settings'. The ship warps in
    /// as the enemy does, the enemy first. Each battle is seeded by its
    /// frame and number (UQM seeds from the clock). Refused (an
    /// <see cref="InvalidOperationException"/>) without the battle's content,
    /// during a story talk, after the game is over, or for a ship not
    /// present and in flight, or already in a battle, or destroyed.
    /// </summary>
    public WorldBattle StartBattle(string id, string enemy, BattleStart? start = null)
    {
        start ??= new BattleStart();
        var kit = BattleKit ?? throw new InvalidOperationException("battles need the battle's content");
        if (!BattleKit.Enemies.Contains(enemy))
            throw new ArgumentException($"{enemy} isn't a ship that can be fought (ported: {string.Join(", ", BattleKit.Enemies)})");
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        var ship = player.Ship;
        if (GameOver || StoryPaused)
            throw new InvalidOperationException("the game is paused or over");
        if (!player.Present || ship.Destroyed || ship.Battle is not null || ship.Entry is not null
            || ship.Activity is not (Activity.Hyperspace or Activity.OuterSystem or Activity.InnerSystem))
            throw new InvalidOperationException($"{id}'s ship isn't in flight, free to fight ({ship.Activity})");
        var description = kit.Ships[enemy];
        int crew = start.EnemyCrew ?? description.Crew;
        if (crew < 1 || crew > description.MaxCrew)
            throw new ArgumentOutOfRangeException(nameof(start), $"{crew} crew for a ship of {description.MaxCrew}");

        int number = ++_battleIds;
        uint seed = unchecked((uint)(Frame * 1_000_003 + number * 7919));
        BattlePlace place;
        byte battlePlanet;
        if (ship.Ip is { } ip)
        {
            ip.Stop();
            place = new BattlePlace(ship.SystemStar, ip.InnerPlanet, ip.Location.X, ip.Location.Y);
            battlePlanet = BattlePlanetAt(ship, seed);
        }
        else
        {
            ship.Flight.Stop();
            place = new BattlePlace(null, null, ship.Flight.LogX, ship.Flight.LogY);
            battlePlanet = RandomPlanetType(seed);
        }

        var enemyShip = new Starship(new IlwrathShip(description, kit.Graphics.Views(description.Graphics.Ship),
            kit.Graphics.Views(description.Graphics.Weapon), start.NoCloak), 1, (ushort)crew, (ushort)description.MaxCrew);
        var fighter = NewFighter(player);
        // Retreat is the world's to allow, per player and frame (RetreatAllowed): the battle lets side 0 run.
        var battle = new Battle(kit.Graphics.Arena(battlePlanet), new[] { new[] { fighter.Starship }, new[] { enemyShip } },
            seed, runAwayAllowed: true, size: ArenaSize.Scaled(Settings.BattleArenaScale), maxElements: Settings.BattleMaxElements);
        var wb = new WorldBattle(number, place, battlePlanet, battle, enemyShip, description, start, id, Frame)
        {
            PlaceIp = ship.Ip is { } at ? IpFlight.AtRest(at.Save()) : null,
            PlaceStarJustLeft = ship.Ip is null ? ship.Flight.StarJustLeft : null,
        };
        wb.Add(fighter);
        // Those there already join only once they've been out of its reach and come back (JoinBattles).
        _battles.Add(wb);
        ship.Battle = wb;
        return wb;
    }

    // BATTLE_PLANET in a system: the inner system's planet, or in the
    // outer system the planet whose image is closest to the ship on the
    // screen (closestPlanetInterPlanetary); a system without planets (UQM
    // keeps the last BATTLE_PLANET) takes a random type.
    private byte BattlePlanetAt(PlayerShip ship, uint seed)
    {
        var ip = ship.Ip!;
        var system = Systems![ship.SystemStar!.Value];
        if (ip.InnerPlanet is int inner)
            return system.Planets[inner].DataIndex;
        WorldDesc? best = null;
        long bestDistance = long.MaxValue;
        foreach (var planet in system.Planets)
        {
            var at = SystemLayout.OuterPlanet(planet, ip.ZoomRadius);
            long dx = ip.Screen.X - at.X, dy = ip.Screen.Y - at.Y;
            if (dx * dx + dy * dy < bestDistance)
            {
                bestDistance = dx * dx + dy * dy;
                best = planet;
            }
        }
        return best?.DataIndex ?? RandomPlanetType(seed);
    }

    // BuildBattle in hyperspace: (COUNT)TFB_Random () % NUMBER_OF_PLANET_TYPES,
    // drawn here from a generator of the battle's own.
    private static byte RandomPlanetType(uint seed) =>
        (byte)((ushort)new TfbRandom(~seed).Next() % WorldTypes.NumberOfPlanetTypes);

    // A player's ship for a battle: the flagship's battle code with this
    // ship's slots and crew (decision 3), the bomb aboard the flagship from
    // CHMMR_BOMB_STATE 3, and a Starship with no crew of its own, new for
    // each battle (SisShip.Load); what it had as it joined, for a save.
    private BattleFighter NewFighter(Player player)
    {
        var kit = BattleKit!;
        var ship = player.Ship;
        var sis = kit.Ships[ShipDescriptions.Flagship];
        bool bomb = ship.Kind == ShipKind.Flagship && Story?.Get("CHMMR_BOMB_STATE") == 3;
        var code = new SisShip(sis, kit.Graphics.Views(sis.Graphics.Ship), kit.Graphics.Views(sis.Graphics.Weapon),
            SisLoadout.From(ship.Kind, ship.Modules, ship.Hold.Crew, bomb));
        return new BattleFighter(player, code, new Starship(code, 0, 0, 0), ship.Hold.Save(), ship.Flight.Fuel, Frame);
    }

    /// <summary>
    /// After every ship's flight this frame, before the battles step (so
    /// ships join between battle frames): a ship that flies into the marker
    /// of a battle in its place (decision 6: from outside the settings'
    /// reach of it on its screen to inside) joins, warping in where UQM's
    /// spawn_ship puts a ship, and waits stopped there. Only entering
    /// counts: a ship first seen inside the reach (there as the battle
    /// started, a new guest put by the host there, row 14, or a ship back in
    /// flight there from a talk, a dock or an entry) doesn't join until it
    /// has been out of the reach and comes back, so it can fly away (one
    /// without fuel, in hyperspace, can't come back: as a star just left).
    /// A battle it has left (retreated or disconnected), a destroyed ship, a
    /// battle decided (the enemy has no ship left able to fight) or over
    /// can't be joined; one whose list is full can't now, which its player
    /// is told once, and is tried again while the ship stays in it.
    /// </summary>
    private void JoinBattles()
    {
        if (_battles.Count == 0 || BattleKit is null)
            return;
        foreach (var player in _players)
        {
            var ship = player.Ship;
            bool flying = Flying(player);
            foreach (var wb in _battles)
            {
                // Out of flight or out of the place: not known to have been out of its reach.
                if (!flying || !InPlace(wb.Place, ship))
                {
                    wb.SeenOutOfReach.Remove(player.Id);
                    continue;
                }
                if (!InReach(wb.Place, ship))
                {
                    wb.SeenOutOfReach.Add(player.Id);
                    continue;
                }
                var battle = wb.Battle;
                if (!wb.SeenOutOfReach.Contains(player.Id) || wb.Left.Contains(player.Id) || !battle.InBattle
                    || battle.BattleCounter[1] == 0 || battle.BattleCounter[0] == byte.MaxValue)
                    continue;
                var fighter = NewFighter(player);
                if (!battle.Join(fighter.Starship))
                {
                    // Still seen out of its reach: tried again next frame.
                    if (_toldFull.Add((player.Id, wb.Id)))
                        _battleNotices.Add((player.Id, "The battle is too crowded to join just now."));
                    continue;
                }
                wb.SeenOutOfReach.Remove(player.Id);
                wb.Add(fighter);
                ship.Battle = wb;
                ship.Flight.Stop();
                ship.Ip?.Stop();
                break;
            }
        }
    }

    // A present player's ship flying (in hyperspace or a system, not entering
    // a star, not in a battle, not destroyed): one that may fly into a marker.
    private static bool Flying(Player player) => player.Present && !player.Ship.Destroyed && player.Ship.Battle is null
        && player.Ship.Entry is null && player.Ship.Activity is Activity.Hyperspace or Activity.OuterSystem or Activity.InnerSystem;

    // Whether a ship is in a battle's place: hyperspace, or the same part of the same system.
    private static bool InPlace(BattlePlace place, PlayerShip ship) => place.InHyperspace
        ? ship.Ip is null
        : ship.SystemStar == place.Star && ship.Ip is { } ip && ip.InnerPlanet == place.InnerPlanet;

    // Whether a ship is within reach of a battle's marker, on its screen:
    // in hyperspace a pixel is 4 logical units (WORLD_TO_DISPLAY, at 1x);
    // in a system, the view's own at its zoom. The ship must be in the
    // battle's place (InPlace).
    internal bool InReach(BattlePlace place, PlayerShip ship)
    {
        long reach = Settings.BattleMarkerReach;
        long dx, dy;
        if (place.InHyperspace)
        {
            var (x, y) = StarContact.SpritePosition(ship.Flight);
            dx = (x - place.X) >> Units.OneShift;
            dy = (y - place.Y) >> Units.OneShift;
        }
        else
        {
            var ip = ship.Ip!;
            int radius = ip.InnerPlanet is null ? ip.ZoomRadius : IpGeometry.MaxZoomRadius;
            var at = IpGeometry.LocationToDisplay(new IpPoint((short)place.X, (short)place.Y), radius);
            dx = ip.Screen.X - at.X;
            dy = ip.Screen.Y - at.Y;
        }
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>
    /// Each battle's frame, after every ship's flight (so ships join between
    /// battle frames): each player's ship takes its player's keys (Fire the
    /// weapon, Special the special, Escape, the key taking off uses, a
    /// retreat where allowed), the enemy UQM's AI at its full-game rating
    /// (COMPUTER_CONTROL | AWESOME_RATING). Then who is out: a ship whose
    /// crew has run out is destroyed (its player watches the battle on, to
    /// its end), one that has warped out has retreated and is back in
    /// flight; and a battle over ends.
    /// </summary>
    private void StepBattles()
    {
        foreach (var wb in _battles.ToList())
        {
            if (GameOver)
                break;   // the world stops at once
            StepBattle(wb);
        }
        _battleInput.Clear();
    }

    private void StepBattle(WorldBattle wb)
    {
        var battle = wb.Battle;
        if (battle.InBattle)
            battle.Step(ship => ship.PlayerNr == 0 ? PlayerBattleInput(wb, ship) : battle.ComputerIntelligence(ship));
        AfterBattleFrame(wb);
    }

    // The battle whose flagship's crew has run out (it's exploding), if any.
    private WorldBattle? FlagshipDying() => _battles.FirstOrDefault(wb => wb.Fighters.Any(f =>
        f.Player.Ship.Kind == ShipKind.Flagship && f.Player.Ship.Battle == wb
        && (f.Exit == BattleExit.Destroyed || f.Starship.RaceDesc is { CrewLevel: 0 })));

    private BattleInput PlayerBattleInput(WorldBattle wb, Starship ship)
    {
        var fighter = wb.Fighters.First(f => f.Starship == ship);
        if (!fighter.Player.Present || !_battleInput.TryGetValue(fighter.Player.Id, out var input))
            return BattleInput.None;
        return ToBattleInput(input, RetreatAllowed(wb, fighter));
    }

    /// <summary>
    /// RunAwayAllowed, for one player's ship (read each frame, as UQM's is):
    /// a battle that allows it (an encounter: the Earth Ilwrath's doesn't),
    /// once the starbase is available (a world without a story, some
    /// tests', has no rule against it), and not the bomb's carrier. UQM's
    /// BOMB_CARRIER is the game's, and stops the one ship there is from
    /// running; here it stops only the flagship carrying the bomb, so the
    /// flagship joining doesn't keep its teammates from retreating
    /// (stage 4a item 6). A disconnect leaves all the same (decision 10).
    /// </summary>
    private bool RetreatAllowed(WorldBattle wb, BattleFighter fighter) =>
        wb.Start.RetreatAllowed && (Story is null || Story.Get("STARBASE_AVAILABLE") != 0) && !fighter.Code.BombCarrier;

    // The keys in battle: turn, thrust, the weapon (Fire), the special,
    // and Escape (the key taking off uses) where a retreat is allowed.
    private static BattleInput ToBattleInput(ShipInput input, bool retreat) =>
        (BattleInput)(int)(input & (ShipInput.Left | ShipInput.Right | ShipInput.Thrust))
        | ((input & ShipInput.Fire) != 0 ? BattleInput.Weapon : 0)
        | ((input & ShipInput.Special) != 0 ? BattleInput.Special : 0)
        | (retreat && (input & ShipInput.TakeOff) != 0 ? BattleInput.Escape : 0);

    private void AfterBattleFrame(WorldBattle wb)
    {
        foreach (var fighter in wb.Fighters.Where(f => f.Exit is null).ToList())
        {
            var starship = fighter.Starship;
            if (starship.RaceDesc is { CrewLevel: 0 })
                FighterDestroyed(fighter);   // out once its crew is 0, watching on
            else if (starship.RaceDesc is null && fighter.Code.Result is { } freed)
            {
                // Warped out: new_ship freed it with its crew.
                if (freed.Destroyed)
                    FighterDestroyed(fighter);
                else
                    LeaveBattle(wb, fighter, BattleExit.Retreated, freed.CrewLeft);
            }
        }
        // The flagship's explosion over, new_ship has freed it: in UQM its
        // side would be out of ships (the flagship its last, CrewEnlisted =
        // ~0) and the battle over; here others may still fight in it, and
        // the battle stops with the world, unfinished (this port's).
        if (wb.Fighters.FirstOrDefault(f => f.Exit == BattleExit.Destroyed && f.Player.Ship.Kind == ShipKind.Flagship
                && f.Starship.RaceDesc is null && f.Player.Ship.Battle == wb) is { } flagship && wb.Battle.InBattle)
        {
            EndGame(flagship);
            return;
        }
        if (!wb.Battle.InBattle)
            EndBattle(wb);
    }

    // A player's ship whose crew has run out: it watches the battle on, and
    // a wreck at its end (item 7); the flagship's is the game lost.
    private void FighterDestroyed(BattleFighter fighter)
    {
        fighter.Exit = BattleExit.Destroyed;
        if (fighter.Player.Ship.Kind == ShipKind.Flagship)
            FlagshipLost = true;
    }

    // The game over (decision 12): the flagship lost, with no crew; the world stops.
    private void EndGame(BattleFighter flagship)
    {
        flagship.CrewLeft = 0;
        flagship.Player.Ship.Hold.SetCrew(0);
        flagship.Player.Ship.Destroyed = true;
        FlagshipLost = true;
        GameOver = true;
    }

    // A player's ship leaving a battle mid-battle with its crew (a retreat,
    // or a disconnect): its crew back aboard, 5 units of fuel paid (its
    // last ship: in 4a, its only one), and back in flight where it joined.
    private void LeaveBattle(WorldBattle wb, BattleFighter fighter, BattleExit exit, int crew)
    {
        fighter.Exit = exit;
        fighter.CrewLeft = crew;
        wb.Leaves(fighter.Player.Id);
        var ship = fighter.Player.Ship;
        ship.Hold.SetCrew(crew);
        // pickship.c: down by RUN_AWAY_FUEL_COST, to no lower than 0.
        ship.Flight.Fuel = ship.Flight.Fuel > RetreatFuelCost ? ship.Flight.Fuel - RetreatFuelCost : 0;
        BackInFlight(ship);
    }

    // Out of a battle, alive: in flight where it joined, and in a system
    // unable to touch anything for ESCAPE_COUNTER's 110 frames, so it can
    // fly clear (UQM sets it as the battle is built; here every ship
    // leaving a battle in a system has it, joiners too).
    private static void BackInFlight(PlayerShip ship)
    {
        ship.Battle = null;
        if (ship.Ip is { } ip)
            ip.EscapeCounter = IpFlight.EscapeFrames;
    }

    /// <summary>
    /// A player disconnecting mid-battle (decision 10): their ship leaves at
    /// once, keeping its crew, paying the retreat's fuel even where a
    /// retreat isn't allowed, and is back in flight; a ship already
    /// destroyed stays destroyed (its player's back watching if they return
    /// before the battle ends). The battle ends if no one is left in it.
    /// </summary>
    private void DisconnectFromBattle(Player player)
    {
        if (player.Ship.Battle is not { } wb || wb.FighterOf(player.Id) is not { Exit: null } fighter)
            return;
        wb.Battle.Leave(fighter.Starship);
        LeaveBattle(wb, fighter, BattleExit.Disconnected, fighter.Code.Result!.Value.CrewLeft);
        if (!wb.Battle.InBattle)
            EndBattle(wb);
    }

    /// <summary>
    /// A battle over (one side has no ship left able to fight): UninitShips
    /// (the floating crew to the ships left, each ship freed), then each
    /// player's ship still in it: destroyed (its crew 0: a wreck at the
    /// battle's place, item 7; the flagship's is the game over), still
    /// running away (cut short: retreated, its fuel paid, item 5's rule),
    /// or alive, its crew back aboard. Won if the
    /// enemy is out and a player's ship still in it (decision 9), and a
    /// battle that pays salvage pays RACE_SHIP_COST / 8 for each enemy ship,
    /// split equally between those ships, the remainder to the one that
    /// joined first, as stage 3's decision 5 split the starbase's gift.
    /// The marker goes, and those still in it are back in flight.
    /// </summary>
    private void EndBattle(WorldBattle wb)
    {
        var battle = wb.Battle;
        var runningAway = wb.Fighters.Where(f => f.Exit is null && battle.RunningAway(f.Starship)).ToList();
        battle.UninitShips();
        var survivors = new List<BattleFighter>();
        foreach (var fighter in wb.Fighters.Where(f => f.Exit is null || f.Exit == BattleExit.Destroyed && f.Player.Ship.Battle == wb).ToList())
        {
            var ship = fighter.Player.Ship;
            var freed = fighter.Code.Result ?? throw new InvalidOperationException("a ship in the battle wasn't freed");
            if (fighter.Exit == BattleExit.Destroyed || freed.Destroyed)
            {
                FighterDestroyed(fighter);
                fighter.CrewLeft = 0;
                ship.Battle = null;
                if (ship.Kind == ShipKind.Flagship)
                    EndGame(fighter);
                else
                    Wreck(wb, ship);
            }
            else if (runningAway.Contains(fighter))
                LeaveBattle(wb, fighter, BattleExit.Retreated, freed.CrewLeft);
            else
            {
                fighter.Exit = BattleExit.Fought;
                fighter.CrewLeft = freed.CrewLeft;
                ship.Hold.SetCrew(freed.CrewLeft);
                BackInFlight(ship);
                survivors.Add(fighter);
            }
        }

        bool won = survivors.Count > 0 && battle.BattleCounter[1] == 0;
        wb.Outcome = won ? BattleOutcome.Won : BattleOutcome.Lost;
        if (won && wb.Start.PaysSalvage)
        {
            // Every enemy ship counts on a victory (UninitEncounter): the enemy's one.
            uint salvage = (uint)(battle.Fleet(1).Count * BattleKit!.Salvage(wb.EnemyShip));
            wb.SalvagePaid = salvage;
            uint share = salvage / (uint)survivors.Count, remainder = salvage % (uint)survivors.Count;
            for (int i = 0; i < survivors.Count; ++i)
            {
                var fighter = survivors[i];
                fighter.Salvage = share + (i == 0 ? remainder : 0);
                var hold = fighter.Player.Ship.Hold;
                hold.ResourceUnits = (uint)Math.Min(uint.MaxValue, (ulong)hold.ResourceUnits + fighter.Salvage);
            }
        }
        _battles.Remove(wb);
        _toldFull.RemoveWhere(t => t.Battle == wb.Id);
        _endedBattles.Add(wb);
    }
}
