// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Serialization;
using Crateris.Core.World;

namespace Crateris.Core.Net;

/// <summary>
/// Messages between host and clients. They travel as Godot RPCs; the
/// payloads that carry structure are bytes made here, so they can be
/// tested without Godot: the welcome as JSON, snapshots in a binary format
/// (<see cref="SnapshotCodec"/>).
///
/// Flow: a client connects and sends Hello (stable player id, name,
/// protocol version). The host answers Welcome (or a refusal), then sends
/// each guest in the game a Snapshot every frame: a delta from the newest
/// one the guest is known to have, or in full (<see cref="SnapshotSender"/>).
/// Clients send Input every frame for "latest snapshot frame + input
/// delay", with the latest snapshot frame they have, which the host's next
/// deltas for them are from. A client that has shown the undocking scene
/// sends UndockShown.
/// </summary>
public static class Protocol
{
    /// <summary>
    /// 2: snapshots carry activities (systems, orbit) and the action input bit.
    /// 3: snapshots carry entries into stars, and an escort's entry follows
    /// its host's (only the host's is drawn).
    /// 4: snapshots carry each ship's hold (crew, landers, minerals, bio data).
    /// 5: snapshots are binary, and deltas from what each guest has.
    /// 6: landers on surfaces: a guest's dispatch and its refusal, the fire
    /// and take-off input bits, each guest's own surface in its snapshots
    /// (its landers with the crew they took, what's on it as drawn with its
    /// fill colour, the deposits taken in its last frame), and dropping
    /// cargo overboard.
    /// 7: snapshots carry the story state (the game-state bits that changed,
    /// the prices, the races' standing).
    /// 8: each ship carries its stored ship's and captain's names (none for
    /// the flagship's UQM names and a guest's join name as captain).
    /// 9: a ship's hold carries its resource units; a ship can be in a talk
    /// (Activity.Talk); the talk a player is in is sent to them as a
    /// reliable message when it changes (TalkView), and their reply comes
    /// back as one.
    /// </summary>
    public const int Version = 9;
    public const int DefaultPort = 24642;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public sealed record Welcome(JoinResult Result, string PlayerId, int InputDelayFrames, string Message);

    public static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    public static T Decode<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new InvalidDataException("Empty message.");

    public static string Describe(JoinResult result) => result switch
    {
        JoinResult.Joined => "Joined. Your escort is undocking from the flagship.",
        JoinResult.Rejoined => "Welcome back. Your ship is where you left it.",
        JoinResult.AlreadyPresent => "That player is already connected.",
        JoinResult.Full => "The game is full (4 players).",
        _ => result.ToString(),
    };

    /// <summary>A refused join closes the connection.</summary>
    public static bool Accepted(JoinResult result) => result is JoinResult.Joined or JoinResult.Rejoined;

    /// <summary>What a guest does with a welcome.</summary>
    public enum WelcomeAction { Join, Leave, Ignore }

    /// <summary>
    /// What a guest does with a welcome: an accepted first welcome joins it,
    /// a refused one ends it; once in the game, a later welcome (the answer
    /// to a repeated hello) changes nothing, and the host keeps the player on
    /// the connection.
    /// </summary>
    public static WelcomeAction OnWelcome(Welcome welcome, bool alreadyIn) =>
        alreadyIn ? WelcomeAction.Ignore : Accepted(welcome.Result) ? WelcomeAction.Join : WelcomeAction.Leave;
}
