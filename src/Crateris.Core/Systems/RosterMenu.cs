// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/roster.c's
// RosterMenu, DoModifyRoster, LockSupportShip and compShipPos; sis.h's
// SUPPORT_SHIP_PTS), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>
/// The host's fleet as the roster sees it (stage 4b item 7): how many
/// ships it has, and a crew member moved between the flagship and the
/// ship at a place in the fleet's queue (its slot order). The host's own
/// world does it (the host plays locally); guests have no fleet (stage 4b,
/// decision 3), so no roster.
/// </summary>
public interface IRosterFleet
{
    /// <summary>The fleet ships, in the queue's order (their slots').</summary>
    IReadOnlyList<World.FleetShip> Ships { get; }

    /// <summary>How many (CountLinks (&amp;GLOBAL (built_ship_q))).</summary>
    int Count => Ships.Count;

    /// <summary>The flagship's crew (GLOBAL_SIS (CrewEnlisted)), as a crew move leaves it.</summary>
    int FlagshipCrew { get; }

    /// <summary>
    /// A crew member moved (DoModifyRoster's up and down, DeltaSupportCrew):
    /// <paramref name="delta"/> 1 from the flagship to the ship at
    /// <paramref name="index"/> in the queue, -1 back; false, changing
    /// nothing, if refused.
    /// </summary>
    bool MoveCrew(int index, int delta);
}

/// <summary>
/// What a key did on the roster, as its drawing follows it (stage 4b item
/// 10, review round 3): each step DoModifyRoster drew for, recorded as the
/// key is taken, in order.
/// </summary>
public enum RosterStep
{
    /// <summary>The cursor's keys (flashSupportShip's turn): from <see cref="RosterEvent.From"/> to <see cref="RosterEvent.To"/>, maybe the same.</summary>
    Navigated,

    /// <summary>Select: the crew changed from now (drawModifiedSupportShip, flashSupportShipCrew).</summary>
    ModeOn,

    /// <summary>Select or cancel while the crew is changed: back to the cursor.</summary>
    ModeOff,

    /// <summary>A crew member moved (DeltaSupportCrew): the ship and flagship's crews as it left them.</summary>
    CrewMoved,

    /// <summary>Cancel: the roster left (the lit ship drawn unlit).</summary>
    Left,
}

/// <summary>
/// One step of the roster's (<see cref="RosterStep"/>): the lit ship's
/// point before and after it; for the cursor's, the ship lit after it (its
/// place in the fleet's queue) and its crew then; for a crew move the
/// ship's place, its crew and the flagship's as it left them, and the crew
/// moved to the ship (1, or -1 back).
/// </summary>
public readonly record struct RosterEvent(RosterStep Step, (int X, int Y) From, (int X, int Y) To, int Ship = -1, int Crew = 0,
    int FlagshipCrew = 0, int Delta = 0);

/// <summary>
/// The roster (roster.c), from the Manifest: the fleet ships' icons round
/// the flagship in the status area, the cursor moved among them (up and
/// down along a column, wrapping into the other; left and right across),
/// select (or cancel) to change the lit ship's crew, then up to move a
/// crew member from the flagship to it and down to move one back, cancel
/// leaving. The crew rules are the world's (<see cref="IRosterFleet"/>).
/// What it draws (the icons, the lit ship flashing, its crew) is the
/// client's (stage 4b item 10).
/// </summary>
public sealed class RosterMenu
{
    /// <summary>SUPPORT_SHIP_PTS: where each fleet ship's icon is, by its place in the queue (sis.h).</summary>
    public static readonly IReadOnlyList<(int X, int Y)> ShipPoints = new (int, int)[]
    {
        (3 + 0, 30 + 2 * 16), (3 + 42, 30 + 2 * 16), (3 + 0, 30 + 3 * 16), (3 + 42, 30 + 3 * 16),
        (3 + 0, 30 + 1 * 16), (3 + 42, 30 + 1 * 16), (3 + 0, 30 + 4 * 16), (3 + 42, 30 + 4 * 16),
        (3 + 0, 30 + 0 * 16), (3 + 42, 30 + 0 * 16), (3 + 0, 30 + 5 * 16), (3 + 42, 30 + 5 * 16),
    };

    private readonly (int X, int Y)[] _sorted;

    /// <summary>
    /// RosterMenu with <paramref name="count"/> fleet ships (at least one:
    /// with none UQM's roster doesn't open): their points sorted on X then
    /// Y (compShipPos), the cursor on the first.
    /// </summary>
    public RosterMenu(int count)
    {
        if (count < 1 || count > ShipPoints.Count)
            throw new ArgumentOutOfRangeException(nameof(count), $"{count} fleet ships: 1 to {ShipPoints.Count}");
        Count = count;
        _sorted = ShipPoints.Take(count).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
    }

    /// <summary>The ships counted as it opened (rosterState->count).</summary>
    public int Count { get; }

    /// <summary>The cursor: a place in the sorted points (pMS->CurState).</summary>
    public int State { get; private set; }

    /// <summary>Changing the lit ship's crew (rosterState->modifyingCrew).</summary>
    public bool ModifyingCrew { get; private set; }

    /// <summary>The lit ship's icon's point (curShipPt).</summary>
    public (int X, int Y) Point => _sorted[State];

    /// <summary>The lit ship's place in the fleet's queue (LockSupportShip: its point's place in SUPPORT_SHIP_PTS).</summary>
    public int Selected
    {
        get
        {
            var point = Point;
            for (int i = 0; i < ShipPoints.Count; ++i)
                if (ShipPoints[i] == point)
                    return i;
            return -1;
        }
    }

    /// <summary>Cancel left the roster (DoModifyRoster gave FALSE).</summary>
    public bool Left { get; private set; }

    private readonly List<RosterEvent> _events = new();

    /// <summary>The steps the keys took since the last take, in order, for the drawing (stage 4b item 10).</summary>
    public IReadOnlyList<RosterEvent> TakeEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<RosterEvent>();   // the usual frame: nothing made
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    /// <summary>
    /// A key, as DoModifyRoster takes it: Moved for the cursor or the crew
    /// mode changed, or the roster left (<see cref="Left"/>); Refused when a
    /// crew member couldn't move (MENU_SOUND_FAILURE); None otherwise.
    /// </summary>
    public MenuOutcome Press(MenuKey key, IRosterFleet fleet)
    {
        if (Left)
            return MenuOutcome.None;
        bool select = key == MenuKey.Select, cancel = key == MenuKey.Cancel;
        bool up = key == MenuKey.Up, down = key == MenuKey.Down;
        bool horiz = key is MenuKey.Left or MenuKey.Right;   // two columns: either goes across
        if (cancel && !ModifyingCrew)
        {
            Left = true;
            _events.Add(new(RosterStep.Left, Point, Point));
            return MenuOutcome.Moved;
        }
        if (select || cancel)
        {
            ModifyingCrew = !ModifyingCrew;
            _events.Add(new(ModifyingCrew ? RosterStep.ModeOn : RosterStep.ModeOff, Point, Point));
            return MenuOutcome.Moved;
        }
        if (ModifyingCrew)
        {
            // Up: a crew member from the flagship to the ship; down: one back. The flagship's crew and room, and the
            // ship's (at least one left, no more than its race's starting crew), are the world's to check.
            if (!up && !down)
                return MenuOutcome.None;
            int delta = up ? 1 : -1, ship = Selected;
            if (!fleet.MoveCrew(ship, delta))
                return MenuOutcome.Refused;
            _events.Add(new(RosterStep.CrewMoved, Point, Point, ship, fleet.Ships[ship].Crew, fleet.FlagshipCrew, delta));
            return MenuOutcome.Moved;
        }
        int now = State, next = State;
        int topRight = (Count + 1) >> 1;
        if (Count < 2)
        {
            // no navigation allowed
        }
        else if (horiz)
        {
            if (next == topRight - 1)
                next = Count - 1;
            else if (next >= topRight)
            {
                next -= topRight;
                if (_sorted[next].Y < _sorted[now].Y)
                    ++next;
            }
            else
            {
                next += topRight;
                if (next != topRight && _sorted[next].Y > _sorted[now].Y)
                    --next;
            }
        }
        else if (down)
        {
            ++next;
            if (next == Count)
                next = topRight;
            else if (next == topRight)
                next = 0;
        }
        else if (up)
        {
            if (next == 0)
                next = topRight - 1;
            else if (next == topRight)
                next = Count - 1;
            else
                --next;
        }
        var from = Point;
        State = next;
        // flashSupportShip's turn, the cursor moved or not; the ship lit with its crew then.
        int lit = Selected;
        _events.Add(new(RosterStep.Navigated, from, Point, lit, lit < fleet.Count ? fleet.Ships[lit].Crew : 0));
        return next == now ? MenuOutcome.None : MenuOutcome.Moved;
    }
}
