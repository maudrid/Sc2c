// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/encount.c: BuildBattle's
// race_q[0], UninitEncounter's built_ship_q; src/uqm/pickship.c:
// GetEncounterStarShip, GetArmadaStarShip, DoPickBattleShip; src/uqm/init.c:
// UninitShips), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

// The host's fleet ships in battle (stage 4b item 8).
public sealed partial class GameWorld
{
    // The host's fleet ships for a battle the host's ship starts or joins
    // (BuildBattle's race_q[0]: built_ship_q's ships in its order, each
    // with its crew, captain and race's ship; the flagship comes last, the
    // fighter's own): none for a guest (stage 4b decision 3), nor without
    // the story's rules.
    private List<BattleFleetShip>? FleetShipsFor(Player player)
    {
        if (!player.IsHost || Story is null || Fleet.Count == 0)
            return null;
        var kit = BattleKit!;
        var ships = new List<BattleFleetShip>();
        foreach (var s in Fleet.Ships)
        {
            string id = FleetRules.RaceShip(s.Race).Ship;
            // A ship of no crew is the shipyard's, never in flight; every race ship's code is ported.
            if (s.Crew <= 0 || !BattleKit.Enemies.Contains(id))
                continue;
            // A Shofixti before they're recruited (only --start-fleet's) flies as Tanaka's damaged scout, as UQM's
            // init_shofixti makes any in an encounter.
            var d = kit.Ships[BattleKit.FlownAs(id, Story)];
            var starship = new Starship(RaceShips.Code(d, kit.Graphics.Views), 0, (ushort)s.Crew, (ushort)d.MaxCrew)
            {
                Icons = RaceShips.Icons(d, kit.Graphics.Views),
                CaptainsNameIndex = s.Captain,
            };
            ships.Add(new BattleFleetShip(s, d, starship));
        }
        return ships;
    }

    /// <summary>
    /// Whether a battle waits for the host's pick (stage 4b decision 2):
    /// the host is on the pick screen and no player's ship with crew is in
    /// it (fighting, warping in or out: the host's own have none in it
    /// then, so it's another player's). Alone, the host gets UQM's
    /// battle, stopped while choosing; with others, it runs on. A guest
    /// flying into it meanwhile joins once the host has chosen.
    /// </summary>
    public static bool WaitsForPick(WorldBattle wb) =>
        wb.Battle.InBattle && wb.Fighters.Any(f => f.Exit is null && f.Pick is not null)
        && !wb.Battle.Fleet(0).Any(s => s.Ship is not null && s.RaceDesc is { CrewLevel: > 0 });

    /// <summary>
    /// A key on the host's pick screen (DoPickBattleShip): the arrows move
    /// the cursor, select picks the ship under it if it can be picked (a
    /// fleet ship still able to fight that's still in the host's fleet, or
    /// the flagship), and it comes in at once, between frames (or once the
    /// battle's list has room). Nothing, <see cref="PickOutcome.None"/>,
    /// with no pick screen open for the player, or while the world waits
    /// (a story talk, the host's pause, the game over).
    /// </summary>
    public PickOutcome PickShipKey(string id, MenuKey key)
    {
        if (GameOver || StoryPaused || HostPaused || Find(id) is not { Present: true, Ship.Battle: { } wb }
                || wb.FighterOf(id) is not { Exit: null, Pick: { } menu, Armada: { } armada } fighter)
            return PickOutcome.None;
        var outcome = menu.Press(key, slot => PickableAt(fighter, slot) is not null);
        if (outcome != PickOutcome.Chosen)
            return outcome;
        fighter.PickedWaiting = PickableAt(fighter, menu.Slot)!;
        fighter.Pick = null;
        PlacePick(wb, fighter);
        return outcome;
    }

    /// <summary>
    /// What the host's pick screen shows under its cursor (DoPickBattleShip's
    /// name and crew): the ship that can be picked there (its id in
    /// data/ships.json, its captain's index, 0 for the flagship, its crew and
    /// most; the flagship's crew is its crew enlisted, its most its pods'),
    /// or null where none can.
    /// </summary>
    public PickLit? PickLitFor(string id)
    {
        if (Find(id) is not { Ship.Battle: { } wb } player || wb.FighterOf(id) is not { Pick: { } menu } fighter)
            return null;
        var ship = PickableAt(fighter, menu.Slot);
        if (ship is null)
            return null;
        if (fighter.FleetShipOf(ship) is { } fleetShip)
            return new PickLit(fleetShip.Ship.Id, fleetShip.Joined.Captain, ship.CrewLevel, ship.MaxCrew, fleetShip.Joined.Slot);
        var hold = player.Ship.Hold;
        return new PickLit(ShipDescriptions.Flagship, 0, hold.Crew, hold.CrewCapacity, null);
    }

    /// <summary>The host's pick screen, for the host's own session (null for a guest: they have no fleet).</summary>
    public IPickShipScreen? PickScreenFor(string id) => Find(id) is { IsHost: true } ? new PickScreen(this, id) : null;

    private sealed class PickScreen : IPickShipScreen
    {
        private readonly GameWorld _world;
        private readonly string _id;

        public PickScreen(GameWorld world, string id) => (_world, _id) = (world, id);

        public PickShipMenu? Menu => _world.Find(_id)?.Ship.Battle?.FighterOf(_id) is { Exit: null } f ? f.Pick : null;

        public PickLit? Lit => _world.PickLitFor(_id);

        public PickOutcome Press(MenuKey key) => _world.PickShipKey(_id, key);
    }

    // The ship of the player's armada that can be picked at a place on the
    // pick screen (DoPickBattleShip's CurFrame): the flagship (null: race_q's
    // tail, always, as UQM's; it's counted while the pick screen is open), or
    // the fleet ship in that hangar slot, if it's still able to fight and
    // still in the host's fleet (a talk may have taken it meanwhile); else null.
    private Starship? PickableAt(BattleFighter fighter, int? slot)
    {
        var armada = fighter.Armada!;
        if (slot is null)
            return armada.Flagship;
        var fleetShip = fighter.FleetShips.FirstOrDefault(f => f.Joined.Slot == slot);
        return fleetShip is not null && armada.CanPick(fleetShip.Starship) && InFleet(fleetShip.Joined) is not null
            ? fleetShip.Starship : null;
    }

    // The fleet ship as it is now that joined a battle as this (by its slot, race and captain), or null.
    private FleetShip? InFleet(FleetShip joined) =>
        Fleet.Ships.FirstOrDefault(s => s.Slot == joined.Slot && s.Race == joined.Race && s.Captain == joined.Captain);

    // The ship picked comes in (Battle.Pick), unless the battle's list is
    // full: then it waits, and is tried again before each of its frames
    // (the battle runs on meanwhile, so room comes). A fleet ship a talk
    // takes from the fleet while it waits doesn't come in: the pick screen
    // opens again after the frame (UpdatePicks), as for a ship freed.
    private void PlacePick(WorldBattle wb, BattleFighter fighter)
    {
        if (fighter.PickedWaiting is not { } ship || !wb.Battle.InBattle)
            return;
        if (wb.Battle.BattleCounter[1] == 0 || !fighter.Armada!.CanPick(ship))
        {
            fighter.PickedWaiting = null;   // the battle is decided: no ship comes in
            return;
        }
        if (fighter.FleetShipOf(ship) is { } chosen && InFleet(chosen.Joined) is null)
        {
            fighter.PickedWaiting = null;   // no longer the host's: they choose again
            return;
        }
        if (!wb.Battle.Pick(ship))
            return;
        fighter.PickedWaiting = null;
        fighter.Starship = ship;
        if (fighter.FleetShipOf(ship) is { } fleetShip)
            fleetShip.Flown = true;
    }

    // Before a battle's frame: the picks waiting for room.
    private void PlaceWaitingPicks(WorldBattle wb)
    {
        foreach (var f in wb.Fighters.Where(f => f.Exit is null && f.PickedWaiting is not null))
            PlacePick(wb, f);
    }

    // The pick screen opens as the player's armada picks (as it joins, and
    // as its ship flown is freed), and closes once the enemy has no ship
    // left (GetArmadaStarShip returns at once then: decision 2's win while
    // choosing) or the player is out.
    private static void UpdatePicks(WorldBattle wb)
    {
        foreach (var f in wb.Fighters.Where(f => f.Armada is not null))
        {
            bool open = f.Exit is null && f.Armada!.Picking && f.PickedWaiting is null && wb.Battle.InBattle
                && wb.Battle.BattleCounter[1] != 0;
            if (!open)
                f.Pick = null;
            else
                f.Pick ??= new PickShipMenu();
        }
    }

    // The fleet ships flown and freed since (new_ship: dead, or warped out
    // with their crew): a dead one is gone from the host's fleet at once
    // (stage 4b decision 4, UninitEncounter's crew_level 0), one warped out
    // keeps its crew (written to the fleet as the host leaves the battle or
    // it ends) and is out for this battle.
    private void SettleFleetShips(WorldBattle wb)
    {
        foreach (var f in wb.Fighters)
            foreach (var fs in f.FleetShips.Where(s => s.Flown && s.Exit is null && s.Starship.RaceDesc is null))
                SettleFleetShip(fs, BattleExit.Retreated);
    }

    // A fleet ship freed: destroyed with no crew (and gone from the fleet), else as given.
    private void SettleFleetShip(BattleFleetShip fs, BattleExit alive)
    {
        fs.CrewLeft = fs.Starship.CrewLevel;
        fs.Exit = fs.CrewLeft == 0 ? BattleExit.Destroyed : alive;
        if (fs.Exit == BattleExit.Destroyed && InFleet(fs.Joined) is { } gone)
            Fleet.Scrap(gone.Slot);
    }

    // The fleet ships' crews as they came out (UninitEncounter keeps each
    // escort's crew_level), written to the host's fleet as the host leaves
    // the battle or it ends; one never flown keeps the crew it had.
    private void WriteBackFleet(BattleFighter fighter)
    {
        foreach (var fs in fighter.FleetShips.Where(s => s.Exit is BattleExit.Retreated or BattleExit.Fought))
            if (InFleet(fs.Joined) is { } ship)
                Fleet.SetCrew(FleetRules, ship.Slot, fs.CrewLeft);
    }

    /// <summary>
    /// The host's fleet as a save holds it while the host is in a battle
    /// (stage 4 decision 14: as it joined): the fleet ships with the crews
    /// they joined with (written back only as the host leaves the battle),
    /// those destroyed gone (decision 4), the one flown now gone too if
    /// it's dying (its crew run out, not a Pkunk being reborn).
    /// </summary>
    internal IEnumerable<FleetShip> FleetForSave()
    {
        var dying = Host.Ship.Battle?.FighterOf(Host.Id) is { Exit: null } f
            && f.FleetShipOf(f.Starship) is { Exit: null, Flown: true } flown
            && flown.Starship.RaceDesc is { CrewLevel: 0 } && flown.Starship.Ship is not { MassPoints: Battle.MaxShipMass + 1 }
                ? InFleet(flown.Joined) : null;
        return Fleet.Ships.Where(s => s != dying);
    }
}
