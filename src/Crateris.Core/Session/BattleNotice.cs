// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>What a <see cref="BattleNotice"/> tells.</summary>
public enum BattleNoticeKind : byte
{
    /// <summary>The player's ship is in a battle: flown into its marker, started by a talk or an encounter.</summary>
    Joined = 1,

    /// <summary>
    /// The player's battle is over for it: its outcome, how the player's
    /// ship came out, its crew and salvage; or, out of a battle still going
    /// on (retreated, or disconnected and back), how it left, with no outcome.
    /// </summary>
    Ended,

    /// <summary>The player's ship is a wreck (destroyed in a battle, its crew 0): it waits for a rescuer.</summary>
    Wrecked,

    /// <summary>A battle the player flew into was too crowded to join just then (<see cref="BattleNotice.Text"/> says so).</summary>
    Full,

    /// <summary>The player flew into a wreck it can revive (<see cref="BattleNotice.Other"/>: the wreck's player): item 12's rescue screen opens.</summary>
    RescueOffer,

    /// <summary>A wreck was revived: sent to the rescuer and the wreck's player (<see cref="BattleNotice.Other"/>: the other one), with the crew given.</summary>
    Revived,

    /// <summary>The game is over: the flagship was lost (decision 12). The world has stopped.</summary>
    GameOver,
}

/// <summary>
/// A battle's news for one player, sent reliably (stage 4a item 11): its
/// start, end and wreck, a battle too full to join, a rescue offered or
/// made, and the game over. A guest's client keeps it to show (item 12's
/// screens) and says it in words meanwhile (<see cref="Describe"/>).
/// </summary>
public sealed record BattleNotice(BattleNoticeKind Kind, int Battle = 0, string Enemy = "", BattleOutcome? Outcome = null,
    BattleExit? Exit = null, uint Salvage = 0, int Crew = 0, string Other = "", string Text = "")
{
    /// <summary>A revival's <see cref="Text"/>: the notice is for the rescuer (<see cref="Other"/> the wreck's player), or for the wreck's player (<see cref="Other"/> the rescuer).</summary>
    public const string Rescuer = "rescuer", Rescued = "rescued";

    public byte[] Encode()
    {
        var w = new WireWriter(32);
        w.Byte((byte)Kind);
        w.UInt((ulong)Battle);
        w.String(Enemy);
        w.Byte(Outcome is { } o ? (byte)(o + 1) : (byte)0);
        w.Byte(Exit is { } x ? (byte)(x + 1) : (byte)0);
        w.UInt(Salvage);
        w.UInt((ulong)Crew);
        w.String(Other);
        w.String(Text);
        return w.Written.ToArray();
    }

    public static BattleNotice Decode(ReadOnlySpan<byte> data)
    {
        var r = new WireReader(data);
        var kind = (BattleNoticeKind)r.Byte();
        if (!Enum.IsDefined(kind))
            throw new InvalidDataException("Bad battle notice.");
        ulong battle = r.UInt();
        string enemy = r.String() ?? "";
        byte outcome = r.Byte(), exit = r.Byte();
        if (outcome > 2 || exit > 4)
            throw new InvalidDataException("Bad battle outcome or exit.");
        ulong salvage = r.UInt(), crew = r.UInt();
        string other = r.String() ?? "", text = r.String() ?? "";
        if (battle > int.MaxValue || salvage > uint.MaxValue || crew > int.MaxValue || !r.AtEnd)
            throw new InvalidDataException("Bad battle notice.");
        return new BattleNotice(kind, (int)battle, enemy, outcome == 0 ? null : (BattleOutcome)(outcome - 1),
            exit == 0 ? null : (BattleExit)(exit - 1), (uint)salvage, (int)crew, other, text);
    }

    /// <summary>In words, for the player it's for.</summary>
    public string Describe() => Kind switch
    {
        BattleNoticeKind.Joined => $"In battle against the {Enemy}.",
        BattleNoticeKind.Ended => (Exit, Outcome) switch
        {
            (BattleExit.Destroyed, _) => $"Your ship was destroyed fighting the {Enemy}.",
            (BattleExit.Retreated, _) => "You got away.",
            (BattleExit.Disconnected, _) => "You left the battle.",
            (_, BattleOutcome.Won) => Salvage > 0 ? $"The {Enemy} is destroyed: {Salvage} RU of salvage." : $"The {Enemy} is destroyed.",
            _ => $"The battle against the {Enemy} is lost.",
        },
        BattleNoticeKind.Wrecked => "Your ship is a wreck: a friend can bring it crew.",
        BattleNoticeKind.Full => Text,
        BattleNoticeKind.RescueOffer => $"You're at {Other}'s wreck: you can give it crew.",
        BattleNoticeKind.Revived => Text == Rescued ? $"{Other} gave your ship {Crew} crew: it flies again."
            : $"You gave {Other}'s wreck {Crew} crew: it flies again.",
        BattleNoticeKind.GameOver => "Game over: the flagship was lost.",
        _ => "",
    };
}
