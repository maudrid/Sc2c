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
/// payloads that carry structure (welcome, snapshot) are JSON bytes so
/// they can be tested here without Godot.
///
/// Flow: a client connects and sends Hello (stable player id, name,
/// protocol version). The host answers Welcome (or a refusal), then sends a
/// Snapshot every frame to everyone. Clients send Input every frame for
/// "latest snapshot frame + input delay". A client that has shown the
/// undocking scene sends UndockShown.
/// </summary>
public static class Protocol
{
    public const int Version = 1;
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
}
