// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;

namespace Crateris.Core.World;

/// <summary>Why a gift between players was refused (none: it went).</summary>
public enum TradeRefusal : byte
{
    None,

    /// <summary>No such player in the game now (or the giver themself).</summary>
    NoOne,

    /// <summary>Nothing to give, or a bad amount.</summary>
    Nothing,

    /// <summary>The giver hasn't that much.</summary>
    NotEnough,

    /// <summary>Cargo goes only to a player in the same solar system (docs/differences.md row 22).</summary>
    NotHere,

    /// <summary>The receiver's storage bays haven't the room.</summary>
    NoRoom,

    /// <summary>The game waits for a story talk.</summary>
    Paused,

    /// <summary>The receiver's wallet would overflow.</summary>
    TooMuch,

    /// <summary>The giver or the receiver is in a battle.</summary>
    InBattle,

    /// <summary>The giver or the receiver is a wreck (stage 4a item 7): no one is aboard.</summary>
    Wrecked,

    /// <summary>The game is over (the flagship lost, or surrendered).</summary>
    GameOver,

    /// <summary>The host has paused the game (<see cref="GameWorld.HostPaused"/>).</summary>
    HostPaused,

    /// <summary>
    /// Fuel goes only to a player close by (the stage 3b-2 plan's item 3):
    /// within a screen in hyperspace, or in the same system (<see cref="Gift.CloseBy"/>).
    /// </summary>
    TooFar,

    /// <summary>The receiver's fuel tanks haven't the room.</summary>
    NoTankRoom,

    /// <summary>
    /// Minerals don't go to a player the Melnorme offer to strip (stage
    /// 3b-2 item 4): the strip may take storage bays they'd need.
    /// </summary>
    StripOffered,
}

/// <summary>
/// A gift from one player to another (stage 3a, item 9; docs/differences.md
/// rows 11, 19, 22 and 79): resource units and Melnorme credits,
/// anywhere; minerals by element category and bio data, to a player in
/// the same solar system, the minerals up to the room in the receiver's
/// storage bays; fuel (in UQM's 1/100 units, FuelOnBoard's) to a player
/// close by (<see cref="CloseBy"/>), up to the room in the receiver's
/// tanks (the stage 3b-2 plan's item 3).
/// </summary>
public sealed record Gift(string From, string To, uint ResourceUnits, IReadOnlyList<int> Minerals, int BioData,
    uint Credits = 0, uint Fuel = 0)
{
    /// <summary>Whether it carries cargo (minerals or bio data), which goes only within a system.</summary>
    public bool Cargo => Minerals.Any(m => m != 0) || BioData != 0;

    public bool Equals(Gift? other) =>
        other is not null && (From, To, ResourceUnits, BioData, Credits, Fuel)
            == (other.From, other.To, other.ResourceUnits, other.BioData, other.Credits, other.Fuel)
        && Minerals.SequenceEqual(other.Minerals);

    public override int GetHashCode() => HashCode.Combine(From, To, ResourceUnits, BioData, Credits, Fuel);

    /// <summary>
    /// Whether two ships are close enough for fuel to go between them (the
    /// owner, 2026-11-22, the stage 3b-2 plan's question 7): in the same
    /// system (<paramref name="starA"/> and <paramref name="starB"/>, the
    /// stars whose systems they're in), or both in hyperspace (no star)
    /// within a screen of each other (UNIT_SCREEN_WIDTH and
    /// UNIT_SCREEN_HEIGHT, universe units: hyper.c's test for a group on
    /// the flagship's screen), at (<paramref name="ax"/>, <paramref name="ay"/>)
    /// and (<paramref name="bx"/>, <paramref name="by"/>).
    /// </summary>
    public static bool CloseBy(int? starA, int ax, int ay, int? starB, int bx, int by) =>
        starA is int a ? starB == a
        : starB is null && Math.Abs(ax - bx) <= Hyperspace.HyperEncounters.ScreenX
                        && Math.Abs(ay - by) <= Hyperspace.HyperEncounters.ScreenY;

    /// <summary>The gift as a message (from, to, the units, each category's minerals, the bio data, the credits, the fuel).</summary>
    public byte[] Encode()
    {
        var w = new WireWriter();
        w.String(From);
        w.String(To);
        w.UInt(ResourceUnits);
        foreach (int m in Minerals)
            w.UInt((ulong)m);
        w.UInt((ulong)BioData);
        w.UInt(Credits);
        w.UInt(Fuel);
        return w.Written.ToArray();
    }

    /// <summary>A gift from a message; a bad one is an <see cref="InvalidDataException"/>.</summary>
    public static Gift Decode(ReadOnlySpan<byte> data)
    {
        var r = new WireReader(data);
        string from = r.String() ?? "", to = r.String() ?? "";
        if (from.Length > MaxId || to.Length > MaxId)
            throw new InvalidDataException("A gift's player id is too long.");
        ulong units = r.UInt();
        var minerals = new int[ShipHold.Categories];
        for (int i = 0; i < minerals.Length; ++i)
            minerals[i] = Small(r.UInt());
        int bio = Small(r.UInt());
        ulong credits = r.UInt(), fuel = r.UInt();
        if (units > uint.MaxValue)
            throw new InvalidDataException("Bad resource units.");
        if (credits > uint.MaxValue)
            throw new InvalidDataException("Bad credits.");
        if (fuel > uint.MaxValue)
            throw new InvalidDataException("Bad fuel.");
        if (!r.AtEnd)
            throw new InvalidDataException("A gift has bytes after its end.");
        return new Gift(from, to, (uint)units, minerals, bio, (uint)credits, (uint)fuel);
    }

    /// <summary>The longest player id a gift may name (ids are short; a longer one is refused).</summary>
    public const int MaxId = 64;

    private static int Small(ulong n) => n <= 1_000_000 ? (int)n : throw new InvalidDataException("Bad gift amount.");

    /// <summary>What it carries, in words.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (ResourceUnits != 0)
            parts.Add($"{ResourceUnits} RU");
        int minerals = Minerals.Sum();
        if (minerals != 0)
            parts.Add($"{minerals} units of minerals");
        if (BioData != 0)
            parts.Add($"{BioData} bio data");
        if (Credits != 0)
            parts.Add($"{Credits} credits");
        if (Fuel != 0)
            parts.Add($"{FuelUnits(Fuel)} units of fuel");
        return string.Join(", ", parts);
    }

    /// <summary>Fuel (1/100 units) in units, in words: whole, or with its hundredths.</summary>
    public static string FuelUnits(uint fuel) =>
        (fuel / (decimal)Hyperspace.Fuel.Scale).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Why it can't go, in words.</summary>
    public static string Why(TradeRefusal refusal) => refusal switch
    {
        TradeRefusal.NoOne => "There's no one to give it to.",
        TradeRefusal.Nothing => "There's nothing to give.",
        TradeRefusal.NotEnough => "You haven't that much.",
        TradeRefusal.NotHere => "Cargo goes only to a player in the same system.",
        TradeRefusal.NoRoom => "Their storage bays haven't the room.",
        TradeRefusal.Paused => "The game waits while the story's talk goes on.",
        TradeRefusal.TooMuch => "Their wallet can't hold that much.",
        TradeRefusal.InBattle => "Not while in a battle.",
        TradeRefusal.Wrecked => "No one is aboard a wreck to give or take it.",
        TradeRefusal.GameOver => "The game is over.",
        TradeRefusal.HostPaused => "The host has paused the game.",
        TradeRefusal.TooFar => "Fuel goes only to a player close by: within a screen in hyperspace, or in the same system.",
        TradeRefusal.NoTankRoom => "Their fuel tanks haven't the room.",
        TradeRefusal.StripOffered => "Not now: the Melnorme are offering to strip their ship, storage bays and all.",
        _ => "",
    };
}
