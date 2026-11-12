// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c: VisitStarBase's
// Ilwrath, CloneShipFragment (ILWRATH_SHIP, ..., 7) with race_id ~0;
// src/uqm/comm.c: InitCommunication's EncounterBattle; src/uqm/encount.c:
// UninitEncounter; src/uqm/ships/ilwrath/ilwrath.c: no cloak in the
// encounter while PROBE_ILWRATH_ENCOUNTER is set), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;

namespace Crateris.Core.World;

// The Ilwrath fight in the opening (stage 4a item 9, stage 4 decision 13).
public sealed partial class GameWorld
{
    /// <summary>
    /// How the Earth fight starts (VisitStarBase): one Ilwrath of 7 crew
    /// (CloneShipFragment's crew_level), which doesn't cloak
    /// (PROBE_ILWRATH_ENCOUNTER in an encounter, ilwrath.c), pays no salvage
    /// and shows no tally (race_id ~0), and can't be retreated from (an
    /// encounter before STARBASE_AVAILABLE, RunAwayAllowed).
    /// </summary>
    public static readonly BattleStart IlwrathFightStart =
        new(EnemyCrew: 7, NoCloak: true, PaysSalvage: false, RetreatAllowed: false);

    // The Ilwrath's talk ended hostile (StarbaseVisit, VisitStep.IlwrathFight):
    // the talk hands over to the battle. Its story pause is over (the
    // fight's step is no talk), every player is shown the Ilwrath's closing
    // lines as a talk over, and the starter's ship, out of the visit, is
    // back in flight by the starbase, where the battle starts (its marker,
    // which the others may fly into). A starter gone by then (the replies
    // passed on) can't fight it: no player is in it, and the visit is taken
    // back as when every player leaves the fight unwon.
    private void StartTheFight(StarbaseVisit v)
    {
        if (TalkView.Of(v) is { } closing)
            foreach (var p in _players.Where(p => p.Present))
                _ended[p.Id] = (v, closing with { Step = VisitStep.Done });
        var starter = v.Starter;
        if (starter.Ship.Visit == v)
            starter.Ship.Visit = null;
        if (!starter.Present)
        {
            v.Undo();
            return;   // AfterVisit, which called this, goes on with the visit undone
        }
        v.Fight = StartBattle(starter.Id, Combat.ShipDescriptions.Ilwrath, IlwrathFightStart);
    }

    /// <summary>
    /// Once the Ilwrath's fight has ended (after the battles' frame, and
    /// after a player leaving, with that player gone), the opening goes on
    /// or is taken back (decision 13), unless the game is over (the
    /// flagship lost: nothing acts on the fight's end, and the save is the
    /// one from before the visit).
    /// <para>
    /// The Ilwrath killed (its crew ran out, whether or not a player's ship
    /// was still in it at the end, as UQM's VisitStarBase goes on to the
    /// commander after any end of that battle but the flagship's death),
    /// the commander's alliance talk follows: answered by the starter if
    /// its ship fought to the end, which is back in the visit and docks
    /// after; otherwise (wrecked, or disconnected) by the next player
    /// present in join order, the host first, as a story talk's replies
    /// pass on (stage 3, decision 7), and nobody docks as it ends. With no
    /// one to answer it, it's taken back.
    /// </para>
    /// <para>
    /// The Ilwrath alive with every player out of it (destroyed or
    /// disconnected; it can't be retreated from), the visit is taken back
    /// (<see cref="StarbaseVisit.Undo"/>): what its talks wrote, the
    /// starter's hold and fuel from before it (a wreck staying a wreck), a
    /// starter's disconnect paying its 5 units of fuel after; the Ilwrath
    /// comes again, whole, on the next report to the commander.
    /// </para>
    /// </summary>
    private void AfterTheFight()
    {
        if (GameOver || Visit is not { Step: VisitStep.IlwrathFight, Fight: { Outcome: not null } fight } visit)
            return;
        var starter = visit.Starter;
        bool killed = fight.Enemy.CrewLevel == 0;
        if (killed)
        {
            bool foughtOn = fight.FighterOf(starter.Id) is { Exit: BattleExit.Fought } && starter.Present;
            var replier = foughtOn ? starter
                : _players.FirstOrDefault(p => p.Present && p != starter);
            if (replier is not null)
            {
                if (foughtOn)
                    starter.Ship.Visit = visit;   // talking again, by the starbase
                visit.AfterTheFight(replier);
                AfterVisit(visit);
                return;
            }
        }
        visit.Undo();
        AfterVisit(visit);
    }
}
