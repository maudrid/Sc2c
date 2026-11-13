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
    /// 10: snapshots say when the game is paused for a story talk, and who
    /// replies in it; everyone is sent a story talk, with its replier and
    /// the reply chosen. (The reply's round and the lander reports came in
    /// 9's stage, item 5, after it was numbered.)
    /// 11: the allied starbase (stage 3a, item 8): a ship can be docked at
    /// its menu (Activity.Starbase); ships carry their slots; a docked
    /// player's keys on the starbase's screens go to the host, which runs
    /// them, and a rename, each a reliable message; the host sends the
    /// player their screen (StarbaseView, saying who's talking to the
    /// commander when Talk was refused) as one when it changes.
    /// 12: a player gives another resource units, minerals or bio data
    /// (stage 3a, item 9): the gift goes to the host as a reliable message,
    /// refused with a reason, and its giver and receiver are told of it.
    /// 13: "no view" (no talk, not docked) is one zero byte
    /// (<see cref="NoView"/>), as Godot's RPC doesn't deliver an empty one.
    /// 14: the shipyard's screen as UQM's keys move round it (stage 3a,
    /// item 11): StarbaseView carries its mode (the menu, the cursor on the
    /// flagship or the hangar's slots, the crew) and the cursor.
    /// 15: the starbase's screens drawn (stage 3a, item 11): the host
    /// sends a docked player what their screen draws (StarbaseDrawing, a
    /// reliable message before their StarbaseView), each key's with the
    /// ship as that key left it; a client without a picture asks for the
    /// screen drawn afresh.
    /// 16: a drawing can redraw the shipyard's blueprint (ScreenOpKind.Blueprint,
    /// for minerals another player gives while it's open).
    /// 17: the replier of a story talk sends how they page it (TalkPage:
    /// skipped, played again, the summary, with the lines it was for), and the talk sent to everyone
    /// carries it for watchers to follow (decision 7).
    /// 18: the talk carries the minerals its replier's last reply sold
    /// (MineralSale: the hold before the sale, the worths, the bio data,
    /// the storage, the resource units and the bomb's state), which the
    /// replier's client counts down before the commander's reply.
    /// 19: a ship's hold says the room its lander that is out keeps
    /// (HoldSnapshot.LanderRoom), so the giving screen shows the room the
    /// host would take for a gift of minerals.
    /// 20: the snapshot says whether the host has paused the game
    /// (WorldSnapshot.HostPaused, stage 4a item 10); gifts and landings may
    /// be refused for it (TradeRefusal.HostPaused, DispatchRefusal.HostPaused;
    /// RescueRefusal.HostPaused once revivals are sent). Also new since 19,
    /// from stage 4a item 7: the welcome may be JoinResult.GameOver (a guest
    /// joining or coming back after the game over, its connection closed),
    /// and a gift may be refused as TradeRefusal.Wrecked or GameOver.
    /// 21: battles (stage 4a item 11): the snapshot carries the viewer's own
    /// battle (BattleSnapshot: the enemy's and every player ship's status
    /// and place, and the elements near the viewer's ship; a part sent
    /// every frame it's in one) and every battle's marker (a part sent when
    /// they change); a ship's flags carry its activity in four bits and
    /// whether it's a wreck; a snapshot holds up to 1024 ships; a guest's
    /// input keeps its Special key (64); the battle notices (ShowBattleNotice:
    /// a battle joined or ended, a wreck, a battle too full, a rescue offered
    /// or made, the game over) and a guest's revival (Revive, refused with a
    /// RescueRefusal) are reliable messages.
    /// 22: a battle carries what drawing it needs (stage 4a item 12): its
    /// camera this frame and which player ship it followed, its stars'
    /// seed, and each ship's captain, captain's-window keys and element's
    /// crew and life for the status panel.
    public const int Version = 22;
    public const int DefaultPort = 24642;

    /// <summary>
    /// "None" for a view sent by RPC (no talk, not docked): one zero byte,
    /// as Godot's RPC doesn't deliver an empty byte array (the call fails
    /// on arrival, "Method not found"). No view's encoding starts with 0.
    /// </summary>
    public static byte[] NoView => new byte[] { 0 };

    /// <summary>Whether an RPC's view is <see cref="NoView"/> (or empty).</summary>
    public static bool IsNoView(ReadOnlySpan<byte> data) => data.Length == 0 || data is [0];

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
        JoinResult.GameOver => "The game is over: the flagship was lost.",
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
