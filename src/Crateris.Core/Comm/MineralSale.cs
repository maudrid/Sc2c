// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;

namespace Crateris.Core.Comm;

/// <summary>
/// The allied commander bought the replier's minerals (starbas.c's
/// SellMinerals): the hold as it was before the sale, which the seller's
/// client counts down a unit at a time in the status area before the
/// commander's reply plays (stage 3a, item 11). The host sells it all at
/// once; the counting is the client's to draw. <see cref="Amounts"/> are
/// the element amounts sold (all the hold had), <see cref="Worth"/> each
/// category's worth a unit (ElementWorth), <see cref="BioData"/> and
/// <see cref="StorageCapacity"/> what the cargo display shows beside them,
/// <see cref="ResourceUnits"/> the seller's before the sale and
/// <see cref="BombState"/> CHMMR_BOMB_STATE (from 2 the status message
/// says the resource units are unlimited).
/// </summary>
public sealed record MineralSale(IReadOnlyList<int> Amounts, IReadOnlyList<int> Worth, int BioData, int StorageCapacity,
    uint ResourceUnits, int BombState)
{
    /// <summary>The element categories (NUM_ELEMENT_CATEGORIES).</summary>
    public const int Categories = 8;

    /// <summary>The minerals' mass before the sale (TotalElementMass).</summary>
    public int Mass => Amounts.Sum();

    /// <summary>What the minerals were worth.</summary>
    public long Total => Amounts.Select((a, i) => (long)a * Worth[i]).Sum();

    public bool Equals(MineralSale? other) =>
        other is not null && Amounts.SequenceEqual(other.Amounts) && Worth.SequenceEqual(other.Worth) && BioData == other.BioData
        && StorageCapacity == other.StorageCapacity && ResourceUnits == other.ResourceUnits && BombState == other.BombState;

    public override int GetHashCode() => HashCode.Combine(Mass, BioData, StorageCapacity, ResourceUnits, BombState);

    internal void Encode(WireWriter w)
    {
        if (Amounts.Count != Categories || Worth.Count != Categories)
            throw new ArgumentException($"A sale has {Categories} element categories.");
        foreach (int a in Amounts)
            w.UInt((ulong)a);
        foreach (int v in Worth)
            w.UInt((ulong)v);
        w.UInt((ulong)BioData);
        w.UInt((ulong)StorageCapacity);
        w.UInt(ResourceUnits);
        w.UInt((ulong)BombState);
    }

    internal static MineralSale Decode(ref WireReader r)
    {
        var amounts = new int[Categories];
        var worth = new int[Categories];
        for (int i = 0; i < Categories; ++i)
            amounts[i] = Small(r.UInt(), ushort.MaxValue, "amount");
        for (int i = 0; i < Categories; ++i)
            worth[i] = Small(r.UInt(), ushort.MaxValue, "worth");
        int bio = Small(r.UInt(), int.MaxValue, "bio data");
        int capacity = Small(r.UInt(), int.MaxValue, "storage");
        ulong ru = r.UInt();
        if (ru > uint.MaxValue)
            throw new InvalidDataException("A sale's resource units are out of range.");
        int bomb = Small(r.UInt(), byte.MaxValue, "bomb state");
        return new MineralSale(amounts, worth, bio, capacity, (uint)ru, bomb);
    }

    private static int Small(ulong v, int most, string what) =>
        v <= (ulong)most ? (int)v : throw new InvalidDataException($"A sale's {what} is out of range.");
}
