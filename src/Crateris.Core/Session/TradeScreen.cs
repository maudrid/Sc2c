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
/// What the giving screen shows (<see cref="TradeScreen.Follow"/>): the row
/// the cursor is on; the player to give to (their id and name, null and ""
/// with no one else in the game but wrecks); whether they're in the giver's solar
/// system, where alone cargo can go; the room in their storage bays for
/// minerals, as the host takes it (null if the snapshot hasn't their hold);
/// and, by <see cref="TradeScreen"/>'s rows, what the giver has and the
/// amount chosen to give (the receiver's row's unused).
/// </summary>
public sealed record TradeShown(int Row, string? To, string ToName, bool Here, int? Room, IReadOnlyList<long> Have,
    IReadOnlyList<int> Give);

/// <summary>
/// The screen for giving another player resource units, minerals or bio
/// data (stage 3a, item 9), the player's own: its rows are the player to
/// give to, the resource units, each element category and the bio data.
/// Up and down choose the row; left and right choose the player, or the
/// amount (by 1; page up and down by 10, the units by 100); select gives
/// the row's amount; cancel closes it. A gift goes to the host, which
/// refuses it with a reason if it can't go. It's drawn in the status area
/// as the Manifest's cargo display is (item 11: Presentation's
/// GivingScreen, from <see cref="Follow"/>).
/// </summary>
public sealed class TradeScreen
{
    /// <summary>The rows: the player, the units, the 8 categories, the bio data.</summary>
    public const int PlayerRow = 0, UnitsRow = 1, BioRow = 2 + ShipHold.Categories, Rows = BioRow + 1;

    public int Row { get; private set; } = UnitsRow;

    /// <summary>The player to give to (by id), among the others in the game.</summary>
    public string? To { get; private set; }

    /// <summary>The amount on each row (the player row's unused).</summary>
    public int[] Amounts { get; } = new int[Rows];

    /// <summary>A key; returns the gift to send if it gave one.</summary>
    public Gift? Press(MenuKey key, WorldSnapshot? snapshot, string me)
    {
        var others = Others(snapshot, me);
        var mine = Follow(snapshot, me, others);
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

    // The others who can take a gift: not wrecks (no one is aboard; an
    // away player's stays in the snapshot, stage 4a item 7).
    internal static List<string> Others(WorldSnapshot? snapshot, string me) =>
        snapshot?.Ships.Where(s => s.Id != me && !s.Wrecked).Select(s => s.Id).ToList() ?? new List<string>();

    // The screen follows the game: the receiver is one of the others in it
    // (the first, until one is chosen, or if theirs has left), and no
    // amount is more than the player has now.
    private HoldSnapshot? Follow(WorldSnapshot? snapshot, string me, List<string> others)
    {
        if (To is null || !others.Contains(To))
            To = others.FirstOrDefault();
        var mine = snapshot?.Ships.FirstOrDefault(s => s.Id == me)?.Hold;
        for (int row = UnitsRow; row < Rows; ++row)
            Amounts[row] = Math.Min(Amounts[row], Have(mine, row));
        return mine;
    }

    // What the player has of a row's kind, as the host last said.
    private static int Have(HoldSnapshot? hold, int row) => hold is null ? 0 : row switch
    {
        UnitsRow => (int)Math.Min(hold.ResourceUnits, int.MaxValue),
        BioRow => hold.BioData,
        _ => hold.Minerals[row - 2],
    };

    /// <summary>
    /// The screen as it stands with <paramref name="snapshot"/>, followed as
    /// a key would follow it (the receiver one of the others in the game,
    /// no amount more than the player has).
    /// </summary>
    public TradeShown Follow(WorldSnapshot? snapshot, string me)
    {
        var mine = Follow(snapshot, me, Others(snapshot, me));
        var giver = snapshot?.Ships.FirstOrDefault(s => s.Id == me);
        var taker = To is null ? null : snapshot?.Ships.FirstOrDefault(s => s.Id == To);
        var have = new long[Rows];
        if (mine is not null)
        {
            have[UnitsRow] = mine.ResourceUnits;
            for (int i = 0; i < ShipHold.Categories; ++i)
                have[2 + i] = mine.Minerals[i];
            have[BioRow] = mine.BioData;
        }
        bool here = giver?.SystemStar is int star && taker?.SystemStar == star;
        return new TradeShown(Row, taker?.Id, taker?.Name ?? To ?? "", here, taker is null ? null : RoomFor(taker), have,
            Amounts.ToArray());
    }

    /// <summary>
    /// The minerals another player may give this ship, as the host takes it
    /// (<see cref="ShipHold.RoomForCargo"/>: its free storage, less what its
    /// lander that is out may still bring up), from what a snapshot shows;
    /// null without its hold.
    /// </summary>
    public static int? RoomFor(ShipSnapshot ship) => ship.Hold?.RoomForCargo;
}
