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
/// The Melnorme bought the replier's bio data (melnorm.c's DoSell,
/// sell_life_data; stage 3b-2 item 4): the bio data as it was before the
/// sale, which the seller's client counts down a unit at a time on the
/// cargo display, two credits each, the status message showing the
/// credits (<see cref="Credits"/>, the seller's before the sale). The host
/// sells it all at once; the counting is the client's to draw.
/// <see cref="Amounts"/>, <see cref="Worth"/> and
/// <see cref="StorageCapacity"/> are what the cargo display shows beside
/// it (the hold's minerals, each category's worth, the storage).
/// </summary>
public sealed record BioSale(IReadOnlyList<int> Amounts, IReadOnlyList<int> Worth, int BioData, int StorageCapacity, uint Credits)
{
    /// <summary>BIO_CREDIT_VALUE: the credits a unit of bio data fetches.</summary>
    public const int CreditValue = 2;

    public bool Equals(BioSale? other) =>
        other is not null && Amounts.SequenceEqual(other.Amounts) && Worth.SequenceEqual(other.Worth) && BioData == other.BioData
        && StorageCapacity == other.StorageCapacity && Credits == other.Credits;

    public override int GetHashCode() => HashCode.Combine(BioData, StorageCapacity, Credits);

    internal void Encode(WireWriter w)
    {
        if (Amounts.Count != MineralSale.Categories || Worth.Count != MineralSale.Categories)
            throw new ArgumentException($"A sale has {MineralSale.Categories} element categories.");
        foreach (int a in Amounts)
            w.UInt((ulong)a);
        foreach (int v in Worth)
            w.UInt((ulong)v);
        w.UInt((ulong)BioData);
        w.UInt((ulong)StorageCapacity);
        w.UInt(Credits);
    }

    internal static BioSale Decode(ref WireReader r)
    {
        var amounts = new int[MineralSale.Categories];
        var worth = new int[MineralSale.Categories];
        for (int i = 0; i < amounts.Length; ++i)
            amounts[i] = Small(r.UInt(), int.MaxValue, "amount");
        for (int i = 0; i < worth.Length; ++i)
            worth[i] = Small(r.UInt(), ushort.MaxValue, "worth");
        int bio = Small(r.UInt(), int.MaxValue, "bio data");
        int capacity = Small(r.UInt(), int.MaxValue, "storage");
        ulong credits = r.UInt();
        if (credits > uint.MaxValue)
            throw new InvalidDataException("A sale's credits are out of range.");
        return new BioSale(amounts, worth, bio, capacity, (uint)credits);
    }

    private static int Small(ulong v, int most, string what) =>
        v <= (ulong)most ? (int)v : throw new InvalidDataException($"A bio sale's {what} is out of range.");
}
