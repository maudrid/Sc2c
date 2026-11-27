// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>
/// What the giving screen shows (<see cref="TradeScreen.Follow"/>): the row
/// the cursor is on; the player to give to (their id and name, null and ""
/// with no one else in the game but wrecks); whether they're in the giver's solar
/// system, where alone cargo can go; the room in their storage bays for
/// minerals, as the host takes it (null if the snapshot hasn't their hold);
/// whether they're close by, where alone fuel can go
/// (<see cref="Gift.CloseBy"/>); the room in their fuel tanks, in whole
/// units (null without their ship); and, by <see cref="TradeScreen"/>'s
/// rows, what the giver has and the amount chosen to give (the receiver's
/// row's unused; the fuel row's in whole units; the device row's how many
/// devices the giver's ship holds, and which of them is chosen, from 1, 0
/// for none); and the device chosen (by globdata.h's number, null for none).
/// </summary>
public sealed record TradeShown(int Row, string? To, string ToName, bool Here, int? Room, IReadOnlyList<long> Have,
    IReadOnlyList<int> Give, bool Near = false, int? TankRoom = null, int? Device = null);

/// <summary>
/// The screen for giving another player resource units, Melnorme credits,
/// fuel, minerals, bio data or a device (stage 3a, item 9; the credits and
/// the fuel since stage 3b-2 item 3, the devices since stage 3c item 5),
/// the player's own: its rows are the player to give to, the resource
/// units, the credits, the fuel (in whole units, up to the receiver's
/// room), each element category, the bio data and the devices their ship
/// holds (one at a time, to a player close by, as fuel goes).
/// Up and down choose the row; left and right choose the player, the
/// device, or the amount (by 1; page up and down by 10, the units and the credits by
/// 100); select gives the row's amount, or the device; cancel closes it. A gift goes to the host, which
/// refuses it with a reason if it can't go. It's drawn in the status area
/// as the Manifest's cargo display is (item 11: Presentation's
/// GivingScreen, from <see cref="Follow"/>).
/// </summary>
public sealed class TradeScreen
{
    /// <summary>The rows: the player, the units, the credits, the fuel, the 8 categories, the bio data, the devices.</summary>
    public const int PlayerRow = 0, UnitsRow = 1, CreditsRow = 2, FuelRow = 3, FirstMineralRow = 4,
        BioRow = FirstMineralRow + ShipHold.Categories, DeviceRow = BioRow + 1, Rows = DeviceRow + 1;

    public int Row { get; private set; } = UnitsRow;

    /// <summary>The player to give to (by id), among the others in the game.</summary>
    public string? To { get; private set; }

    /// <summary>The amount on each row (the player row's unused).</summary>
    public int[] Amounts { get; } = new int[Rows];

    /// <summary>A key; returns the gift to send if it gave one.</summary>
    public Gift? Press(MenuKey key, WorldSnapshot? snapshot, string me)
    {
        var others = Others(snapshot, me);
        var (mine, tankRoom) = Follow(snapshot, me, others);
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
                int step = key is MenuKey.PageUp or MenuKey.PageDown ? (Row is UnitsRow or CreditsRow ? 100 : 10) : 1;
                int sign = key is MenuKey.Left or MenuKey.PageDown ? -1 : 1;
                Amounts[Row] = Math.Clamp(Amounts[Row] + sign * step, 0, Most(mine, Row, tankRoom));
                break;
            case MenuKey.Select when To is not null && Row == DeviceRow && Amounts[Row] > 0:
                var device = new Gift(me, To, 0, new int[ShipHold.Categories], 0,
                    Device: mine!.Hold!.HeldDevices[Amounts[Row] - 1]);
                Amounts[Row] = 0;
                return device;
            case MenuKey.Select when To is not null && Row != PlayerRow && Amounts[Row] > 0:
                var minerals = new int[ShipHold.Categories];
                if (Row >= FirstMineralRow && Row < BioRow)
                    minerals[Row - FirstMineralRow] = Amounts[Row];
                int amount = Amounts[Row];
                var gift = new Gift(me, To, Row == UnitsRow ? (uint)amount : 0, minerals, Row == BioRow ? amount : 0,
                    Row == CreditsRow ? (uint)amount : 0, Row == FuelRow ? (uint)amount * Fuel.Scale : 0);
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
    // (Fuel goes up to the receiver's room too: the room in whole units, null without their ship.)
    private (ShipSnapshot? Mine, int? TankRoom) Follow(WorldSnapshot? snapshot, string me, List<string> others)
    {
        if (To is null || !others.Contains(To))
            To = others.FirstOrDefault();
        var mine = snapshot?.Ships.FirstOrDefault(s => s.Id == me);
        var taker = To is null ? null : snapshot?.Ships.FirstOrDefault(s => s.Id == To);
        int? tankRoom = taker is null ? null : TankRoomOf(taker);
        for (int row = UnitsRow; row < Rows; ++row)
            Amounts[row] = Math.Min(Amounts[row], Most(mine, row, tankRoom));
        return (mine, tankRoom);
    }

    // The most a row can give: what the player has, and for fuel no more than the receiver's room.
    private static int Most(ShipSnapshot? mine, int row, int? tankRoom) =>
        row == FuelRow ? Math.Min(Have(mine, row), tankRoom ?? 0) : Have(mine, row);

    // What the player has of a row's kind, as the host last said (fuel in whole units).
    // (The device row's: how many devices the ship holds, the amount being which of them, from 1.)
    private static int Have(ShipSnapshot? ship, int row) => row == FuelRow
        ? (int)(ship?.Fuel / Fuel.Scale ?? 0)
        : ship?.Hold is not { } hold ? 0 : row switch
        {
            UnitsRow => (int)Math.Min(hold.ResourceUnits, int.MaxValue),
            CreditsRow => (int)Math.Min(hold.Credits, int.MaxValue),
            BioRow => hold.BioData,
            DeviceRow => hold.HeldDevices.Count,
            _ => hold.Minerals[row - FirstMineralRow],
        };

    /// <summary>The room in a ship's fuel tanks, in whole units (a fraction left over can't be given).</summary>
    public static int TankRoomOf(ShipSnapshot ship) =>
        ship.FuelCapacity > ship.Fuel ? (int)((ship.FuelCapacity - ship.Fuel) / Fuel.Scale) : 0;

    /// <summary>
    /// The screen as it stands with <paramref name="snapshot"/>, followed as
    /// a key would follow it (the receiver one of the others in the game,
    /// no amount more than the player has).
    /// </summary>
    public TradeShown Follow(WorldSnapshot? snapshot, string me)
    {
        var (giver, tankRoom) = Follow(snapshot, me, Others(snapshot, me));
        var taker = To is null ? null : snapshot?.Ships.FirstOrDefault(s => s.Id == To);
        var have = new long[Rows];
        if (giver is not null)
            have[FuelRow] = giver.Fuel / Fuel.Scale;
        if (giver?.Hold is { } mine)
        {
            have[UnitsRow] = mine.ResourceUnits;
            have[CreditsRow] = mine.Credits;
            for (int i = 0; i < ShipHold.Categories; ++i)
                have[FirstMineralRow + i] = mine.Minerals[i];
            have[BioRow] = mine.BioData;
            have[DeviceRow] = mine.HeldDevices.Count;
        }
        int? device = Amounts[DeviceRow] > 0 && giver?.Hold is { } h && Amounts[DeviceRow] <= h.HeldDevices.Count
            ? h.HeldDevices[Amounts[DeviceRow] - 1] : null;
        bool here = giver?.SystemStar is int star && taker?.SystemStar == star;
        bool near = giver is not null && taker is not null && Gift.CloseBy(giver.SystemStar, giver.UniverseX, giver.UniverseY,
            taker.SystemStar, taker.UniverseX, taker.UniverseY);
        return new TradeShown(Row, taker?.Id, taker?.Name ?? To ?? "", here, taker is null ? null : RoomFor(taker), have,
            Amounts.ToArray(), near, tankRoom, device);
    }

    /// <summary>
    /// The minerals another player may give this ship, as the host takes it
    /// (<see cref="ShipHold.RoomForCargo"/>: its free storage, less what its
    /// lander that is out may still bring up), from what a snapshot shows;
    /// null without its hold.
    /// </summary>
    public static int? RoomFor(ShipSnapshot ship) => ship.Hold?.RoomForCargo;
}
