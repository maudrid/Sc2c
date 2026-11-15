// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/pickship.c's
// DoPickBattleShip and GetArmadaStarShip), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Systems;

/// <summary>What a key did on the pick screen.</summary>
public enum PickOutcome
{
    /// <summary>Nothing: a key it ignores (cancel, the page keys), or select where no ship can be picked.</summary>
    None,

    /// <summary>The cursor moved (MENU_SOUND_MOVE).</summary>
    Moved,

    /// <summary>The ship under the cursor is picked (MENU_SOUND_SUCCESS): the screen is done.</summary>
    Chosen,
}

/// <summary>
/// The pick screen's cursor (stage 4b item 8; pickship.c's
/// DoPickBattleShip, which GetArmadaStarShip runs): the hangar's twelve
/// slots in two rows of six, the flagship in the middle between the
/// third and fourth columns, so the cursor's column runs from 0 to 6 with
/// the flagship's at 3 (on it, the row is kept but doesn't matter). It
/// starts on the flagship; left and right move across, wrapping from one
/// end to the other; up and down move between the rows, wrapping. Select
/// picks the ship under the cursor if it can be picked now (a fleet ship
/// in that slot still able to fight, or the flagship); cancel does
/// nothing, as UQM's. What can be picked is the world's; what's drawn
/// (DrawArmadaPickShip, the flash, the name and crew) the client's
/// (stage 4b item 10).
/// </summary>
public sealed class PickShipMenu
{
    /// <summary>NUM_PICK_SHIP_ROWS and NUM_PICK_SHIP_COLUMNS: the hangar's slots, two rows of six.</summary>
    public const int Rows = 2, Columns = 6;

    /// <summary>The flagship's column (NUM_PICK_SHIP_COLUMNS &gt;&gt; 1): between the slots' third and fourth.</summary>
    public const int FlagshipColumn = Columns >> 1;

    /// <summary>The cursor's column (first_item.x): 0 to <see cref="Columns"/>, <see cref="FlagshipColumn"/> the flagship.</summary>
    public int Column { get; private set; } = FlagshipColumn;

    /// <summary>The cursor's row (first_item.y): 0 or 1, kept on the flagship's column.</summary>
    public int Row { get; private set; }

    /// <summary>Whether the cursor is on the flagship.</summary>
    public bool OnFlagship => Column == FlagshipColumn;

    /// <summary>
    /// The hangar slot under the cursor (ship_index: the row's six, the
    /// columns past the flagship's one less), or null on the flagship.
    /// </summary>
    public int? Slot => SlotAt(Row, Column);

    /// <summary>The hangar slot at a row and column of the pick screen, or null on the flagship's column.</summary>
    public static int? SlotAt(int row, int column) =>
        column == FlagshipColumn ? null : row * Columns + (column > FlagshipColumn ? column - 1 : column);

    /// <summary>
    /// A key: the arrows move the cursor (DoPickBattleShip's dx and dy, the
    /// column wrapping from -1 to 6 and from 7 to 0, the row from -1 to 1
    /// and from 2 to 0); select picks what's under it if
    /// <paramref name="canPick"/> says it can be picked now (given the
    /// <see cref="Slot"/>, null for the flagship: CurFrame non-zero);
    /// anything else does nothing.
    /// </summary>
    public PickOutcome Press(MenuKey key, Func<int?, bool> canPick)
    {
        int dx = 0, dy = 0;
        switch (key)
        {
            case MenuKey.Select:
                return canPick(Slot) ? PickOutcome.Chosen : PickOutcome.None;
            case MenuKey.Right:
                dx = 1;
                break;
            case MenuKey.Left:
                dx = -1;
                break;
            case MenuKey.Up:
                dy = -1;
                break;
            case MenuKey.Down:
                dy = 1;
                break;
            default:
                return PickOutcome.None;
        }
        int column = Column + dx, row = Row + dy;
        if (column < 0)
            column = Columns;
        else if (column > Columns)
            column = 0;
        if (row < 0)
            row = Rows - 1;
        else if (row == Rows)
            row = 0;
        Column = column;
        Row = row;
        return PickOutcome.Moved;
    }
}

/// <summary>
/// What the pick screen shows under its cursor (DoPickBattleShip's name
/// and crew): the ship's id in data/ships.json, its captain's name's index
/// in its race's strings (0 for the flagship: the commander's name), its
/// crew and most (shown as the crew alone when it's at its most, else
/// crew/most), and its hangar slot (null for the flagship).
/// </summary>
public sealed record PickLit(string Ship, int Captain, int Crew, int MaxCrew, int? Slot);

/// <summary>
/// The host's pick screen as the host's own session sees it (stage 4b
/// item 8): the host's world runs it (the host plays locally); guests have
/// no fleet (stage 4b decision 3), so no pick screen.
/// </summary>
public interface IPickShipScreen
{
    /// <summary>The pick screen, open while the host chooses their next ship; null otherwise.</summary>
    PickShipMenu? Menu { get; }

    /// <summary>The ship under the cursor that can be picked, or null.</summary>
    PickLit? Lit { get; }

    /// <summary>A key on it (<see cref="PickShipMenu.Press"/>, the world's pick).</summary>
    PickOutcome Press(MenuKey key);
}
