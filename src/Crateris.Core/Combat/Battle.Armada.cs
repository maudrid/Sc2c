// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/pickship.c:
// GetEncounterStarShip, GetArmadaStarShip; src/uqm/encount.c: BuildBattle's
// race_q[0]; src/uqm/tactrans.c: new_ship, RecordShipDeath), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>
/// One player's ships in a battle, picked one at a time (stage 4b item 8):
/// UQM's race_q[0] in the full game, BuildBattle's escorts in the fleet's
/// order and the flagship last (its tail). Only the host has one (the
/// fleet ships and the flagship, stage 4b decisions 1 and 3); a player
/// with no fleet ships comes in with their own ship alone, as before. Its
/// ships are counted among side 0's ships able to fight
/// (battle_counter[0]) until they die or run away; while it has any
/// counted, its player is in the battle, on the pick screen or flying one
/// (decision 2). When the ship flown dies or warps out, its player picks
/// the next (<see cref="Picking"/>, GetArmadaStarShip), unless the enemy
/// has no ship left; the flagship picked, it's the last choice
/// (battle_counter = 1).
/// </summary>
public sealed class Armada
{
    private readonly List<Starship> _counted = new();

    /// <summary>The ships, each made for the battle on side 0: the escorts in order, the flagship last.</summary>
    public Armada(IReadOnlyList<Starship> escorts, Starship flagship)
    {
        if (escorts.Count == 0)
            throw new ArgumentException("an armada has an escort at least: without, the flagship comes in by itself");
        Ships = escorts.Append(flagship).ToList();
        if (Ships.Distinct().Count() != Ships.Count)
            throw new ArgumentException("a ship is in the armada twice");
        foreach (var s in Ships)
        {
            if (s.PlayerNr != 0)
                throw new ArgumentException("an armada's ships are side 0's");
            if (s.Armada is not null)
                throw new ArgumentException("a ship is in an armada already");
        }
        foreach (var s in Ships)
            s.Armada = this;
    }

    /// <summary>race_q[0]: the escorts, then the flagship.</summary>
    public IReadOnlyList<Starship> Ships { get; }

    /// <summary>The flagship: race_q[0]'s tail, picked last.</summary>
    public Starship Flagship => Ships[^1];

    /// <summary>The escorts (every ship but the flagship).</summary>
    public IEnumerable<Starship> Escorts => Ships.Take(Ships.Count - 1);

    /// <summary>
    /// Its ships still counted among side 0's ships able to fight
    /// (battle_counter[0]'s part for it): those not yet dead or run away,
    /// waiting or flying; once the flagship is picked, it alone.
    /// </summary>
    public IReadOnlyList<Starship> Counted => _counted;

    /// <summary>
    /// Whether its player is to pick its next ship (GetArmadaStarShip's
    /// pick screen): as it joins, and as the ship flown is freed (dead or
    /// warped out) while it has ships counted and the enemy has ships left.
    /// </summary>
    public bool Picking { get; internal set; }

    /// <summary>Whether the flagship has been picked: the last choice (battle_counter = 1).</summary>
    public bool FlagshipChosen { get; internal set; }

    /// <summary>The ship its player flies now (its element in the battle), or null (on the pick screen).</summary>
    public Starship? Flying => Ships.FirstOrDefault(s => s.Ship != null);

    /// <summary>
    /// Whether a ship of it may be picked now: its player picking, the ship
    /// not dead, run away or taken out (DoPickBattleShip's SpeciesID !=
    /// NO_ID: <see cref="Starship.Available"/>) nor in the battle already.
    /// (Once the flagship is picked its player never picks again: its
    /// escorts uncounted, none is.)
    /// </summary>
    public bool CanPick(Starship ship) => Picking && Ships.Contains(ship) && ship.Ship == null && ship.RaceDesc == null
        && ship.Available;

    internal void Count(Starship ship) => _counted.Add(ship);

    internal bool Uncount(Starship ship) => _counted.Remove(ship);

    internal void CountOnly(Starship ship)
    {
        _counted.Clear();
        _counted.Add(ship);
    }
}

// The armada's part of a battle (stage 4b item 8).
public sealed partial class Battle
{
    /// <summary>
    /// A player's armada joining the battle (decision 6), between frames:
    /// its ships added to side 0's fleet and counted among its ships able
    /// to fight (BuildBattle's race_q[0], CountLinks), none of them in the
    /// battle yet: its player picks the first (<see cref="Armada.Picking"/>,
    /// GetArmadaStarShip). Refused as <see cref="Join"/> is (a battle over
    /// or decided, during a frame, a ship in it or spent); it needs no
    /// element until a ship is picked, so it can't find the list full.
    /// </summary>
    public void JoinArmada(Armada armada)
    {
        if (_stepping)
            throw new InvalidOperationException("an armada joins between frames");
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        if (_fleets[0].Contains(armada.Ships[0]) || armada.Counted.Count > 0)
            throw new ArgumentException("the armada is already in the battle");
        if (armada.Ships.Any(s => !s.Available || s.Ship != null || s.RaceDesc != null))
            throw new ArgumentException("an armada's ship isn't free to fight: in a battle or spent by one");
        if (BattleCounter[0] + armada.Ships.Count > byte.MaxValue)
            throw new InvalidOperationException("the side has as many ships as it can count");
        if (BattleCounter[1] == 0)
            throw new InvalidOperationException("the battle is decided: the other side has no ship left to fight");
        AddArmada(armada);
    }

    // Its ships into side 0's fleet, counted, its player picking.
    private void AddArmada(Armada armada)
    {
        foreach (var s in armada.Ships)
        {
            if (!_fleets[0].Contains(s))
                _fleets[0].Add(s);
            armada.Count(s);
        }
        BattleCounter[0] += (byte)armada.Ships.Count;
        armada.Picking = true;
    }

    /// <summary>
    /// The ship its player picked on the pick screen comes in, between
    /// frames (GetArmadaStarShip's choice, GetNextStarShip's spawn_ship):
    /// warping in at a random place as any ship does; the flagship picked
    /// makes it the last choice (battle_counter[0] counts it alone of the
    /// armada's ships). Returns false, changing nothing, when the list is
    /// full (no element for it: UQM gives it the dead ship's; here it waits
    /// for one). A ship the armada can't pick now (<see cref="Armada.CanPick"/>),
    /// or a battle over, decided (UQM's pick screen isn't shown once the
    /// enemy has no ships left) or mid-frame, is refused.
    /// </summary>
    public bool Pick(Starship ship)
    {
        if (_stepping)
            throw new InvalidOperationException("a ship is picked between frames");
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        if (ship.Armada is not { } armada || !_fleets[0].Contains(ship) || !armada.CanPick(ship))
            throw new ArgumentException("the ship can't be picked now");
        if (BattleCounter[1] == 0)
            throw new InvalidOperationException("the battle is decided: the other side has no ship left to fight");
        if (List.Full)
            return false;
        SpawnShip(ship);   // with a slot free, it can't fail
        armada.Picking = false;
        if (ship == armada.Flagship)
        {
            // GetArmadaStarShip: "Player chose SIS. There will be no more choices."
            BattleCounter[0] -= (byte)(armada.Counted.Count - 1);
            armada.CountOnly(ship);
            armada.FlagshipChosen = true;
        }
        return true;
    }

    /// <summary>
    /// A player's armada leaving the battle at once (a disconnect, stage 4
    /// decision 10), between frames: the ships waiting go uncounted and
    /// can't fight again in this battle, the pick screen closes, and the
    /// ship flown, if it has crew, leaves as <see cref="Leave"/> takes a
    /// ship out (one dying stays: its death stands). With no ship of a side
    /// left able to fight, the battle ends (one dying ends it as new_ship
    /// frees it).
    /// </summary>
    public void LeaveArmada(Armada armada)
    {
        if (_stepping)
            throw new InvalidOperationException("an armada leaves between frames");
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        if (!_fleets[0].Contains(armada.Ships[0]))
            throw new ArgumentException("the armada isn't in the battle");
        var flying = armada.Flying;
        foreach (var s in armada.Ships.Where(s => s != flying).ToList())
        {
            if (armada.Uncount(s))
                BattleCounter[0]--;
            if (s.Ship == null)
                s.Available = false;
        }
        armada.Picking = false;
        if (flying is { RaceDesc.CrewLevel: > 0 })
            LeaveNow(flying);
        else if (flying is null && (BattleCounter[0] == 0 || BattleCounter[1] == 0))
            InBattle = false;
    }

    // A ship out of side 0's count (RecordShipDeath, DoRunAway, a ship
    // leaving): battle_counter, and its armada's part of it.
    private void Uncount(Starship ship)
    {
        BattleCounter[ship.PlayerNr]--;
        ship.Armada?.Uncount(ship);
    }

    // GetEncounterStarShip for an armada's ship freed (dead or warped out):
    // with ships of its own counted and the enemy's left, its player picks
    // the next (GetArmadaStarShip, which UQM runs here, stopping the
    // battle; here the pick comes between later frames, Pick); with none,
    // none comes (its flagship was the last: the world settles it). Its
    // element goes (UQM hands it to the ship picked).
    private static void NextOfArmada(Starship last, Armada armada, bool enemyLeft)
    {
        last.Ship = null;
        if (armada.Counted.Count > 0 && enemyLeft)
            armada.Picking = true;
    }
}
