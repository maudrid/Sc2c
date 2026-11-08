// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>
/// The screen for giving another player resource units, minerals or bio
/// data (stage 3a, item 9), the player's own: its rows are the player to
/// give to, the resource units, each element category and the bio data.
/// Up and down choose the row; left and right choose the player, or the
/// amount (by 1; page up and down by 10, the units by 100); select gives
/// the row's amount; cancel closes it. A gift goes to the host, which
/// refuses it with a reason if it can't go. Until item 11 draws it, it's
/// listed in the status panel.
/// </summary>
public sealed class TradeScreen
{
    /// <summary>The rows: the player, the units, the 8 categories, the bio data.</summary>
    public const int PlayerRow = 0, UnitsRow = 1, BioRow = 2 + ShipHold.Categories, Rows = BioRow + 1;

    private static readonly string[] Categories =
        { "common", "corrosive", "base metal", "noble", "rare earth", "precious", "radioactive", "exotic" };

    public int Row { get; private set; } = UnitsRow;

    /// <summary>The player to give to (by id), among the others in the game.</summary>
    public string? To { get; private set; }

    /// <summary>The amount on each row (the player row's unused).</summary>
    public int[] Amounts { get; } = new int[Rows];

    /// <summary>A key; returns the gift to send if it gave one.</summary>
    public Gift? Press(MenuKey key, WorldSnapshot? snapshot, string me)
    {
        var others = snapshot?.Ships.Where(s => s.Id != me).Select(s => s.Id).ToList() ?? new List<string>();
        if (To is null || !others.Contains(To))
            To = others.FirstOrDefault();
        var mine = snapshot?.Ships.FirstOrDefault(s => s.Id == me)?.Hold;
        switch (key)
        {
            case MenuKey.Up:
                Row = (Row + Rows - 1) % Rows;
                break;
            case MenuKey.Down:
                Row = (Row + 1) % Rows;
                break;
            case MenuKey.Left or MenuKey.Right or MenuKey.PageUp or MenuKey.PageDown when Row == PlayerRow:
                if (others.Count > 0)
                {
                    int i = others.IndexOf(To!);
                    int d = key is MenuKey.Left or MenuKey.PageDown ? -1 : 1;
                    To = others[(i + d + others.Count) % others.Count];
                }
                break;
            case MenuKey.Left or MenuKey.Right or MenuKey.PageUp or MenuKey.PageDown:
                int step = key is MenuKey.PageUp or MenuKey.PageDown ? (Row == UnitsRow ? 100 : 10) : 1;
                int sign = key is MenuKey.Left or MenuKey.PageDown ? -1 : 1;
                Amounts[Row] = Math.Clamp(Amounts[Row] + sign * step, 0, Have(mine, Row));
                break;
            case MenuKey.Select when To is not null && Row != PlayerRow && Amounts[Row] > 0:
                var minerals = new int[ShipHold.Categories];
                if (Row >= 2 && Row < BioRow)
                    minerals[Row - 2] = Amounts[Row];
                var gift = new Gift(me, To, Row == UnitsRow ? (uint)Amounts[Row] : 0, minerals, Row == BioRow ? Amounts[Row] : 0);
                Amounts[Row] = 0;
                return gift;
        }
        return null;
    }

    // What the player has of a row's kind, as the host last said.
    private static int Have(HoldSnapshot? hold, int row) => hold is null ? 0 : row switch
    {
        UnitsRow => (int)Math.Min(hold.ResourceUnits, int.MaxValue),
        BioRow => hold.BioData,
        _ => hold.Minerals[row - 2],
    };

    /// <summary>The screen in words.</summary>
    public IEnumerable<string> Lines(WorldSnapshot? snapshot, string me)
    {
        var mine = snapshot?.Ships.FirstOrDefault(s => s.Id == me)?.Hold;
        string who = snapshot?.Ships.FirstOrDefault(s => s.Id == To)?.Name ?? (To is null ? "(no one else here)" : To);
        string Mark(int row) => row == Row ? ">" : " ";
        yield return "Give to another player (T, Esc or Backspace: close):";
        yield return $"{Mark(PlayerRow)} To: {who}";
        yield return $"{Mark(UnitsRow)} Resource units: {Amounts[UnitsRow]} of {Have(mine, UnitsRow)}";
        for (int i = 0; i < ShipHold.Categories; ++i)
            yield return $"{Mark(2 + i)} {Categories[i]}: {Amounts[2 + i]} of {Have(mine, 2 + i)}";
        yield return $"{Mark(BioRow)} Bio data: {Amounts[BioRow]} of {Have(mine, BioRow)}";
        yield return "Up, down: the row; left, right: the player or amount (PgUp, PgDn by more); Enter: give. Cargo goes only within a system.";
    }
}
