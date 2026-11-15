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

    /// <summary>
    /// A crew member moved (DoModifyRoster's up and down, DeltaSupportCrew):
    /// <paramref name="delta"/> 1 from the flagship to the ship at
    /// <paramref name="index"/> in the queue, -1 back; false, changing
    /// nothing, if refused.
    /// </summary>
    bool MoveCrew(int index, int delta);
}

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
    public int Selected => Array.IndexOf(ShipPoints.ToArray(), Point);

    /// <summary>Cancel left the roster (DoModifyRoster gave FALSE).</summary>
    public bool Left { get; private set; }

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
            return MenuOutcome.Moved;
        }
        if (select || cancel)
        {
            ModifyingCrew = !ModifyingCrew;
            return MenuOutcome.Moved;
        }
        if (ModifyingCrew)
        {
            // Up: a crew member from the flagship to the ship; down: one back. The flagship's crew and room, and the
            // ship's (at least one left, no more than its race's starting crew), are the world's to check.
            if (!up && !down)
                return MenuOutcome.None;
            return fleet.MoveCrew(Selected, up ? 1 : -1) ? MenuOutcome.Moved : MenuOutcome.Refused;
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
        if (next == now)
            return MenuOutcome.None;
        State = next;
        return MenuOutcome.Moved;
    }
}
