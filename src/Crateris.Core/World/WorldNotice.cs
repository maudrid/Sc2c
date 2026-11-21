// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.World;

/// <summary>
/// What a <see cref="WorldNotice"/> tells: an event another player set off
/// (docs/plans/stage-3b.md, decision 11). Stage 3b-1 brings the first;
/// later stages (3b-2's alliances and ships given, 3c's story events) add
/// theirs here.
/// </summary>
public enum NoticeEvent : byte
{
    /// <summary>A group passed through the player: another player is talking to its race (decision 5's notice).</summary>
    Talking,

    /// <summary>In a system: another player met a group (or orbited the race's world), and the race's groups stand down (decision 8).</summary>
    HoldOff,

    /// <summary>Another player met a group: in hyperspace, or in a system the player isn't in (the race's groups pass through the others while they talk).</summary>
    Meeting,

    /// <summary>Another player's meeting ended without a battle, in peace.</summary>
    Peace,

    /// <summary>Another player's talk ended hostile but no battle could start (the game over, no battle's content, ships that can't be fought).</summary>
    MeetingOver,

    /// <summary>Another player's battle started: a meeting's ending in one, or any other battle.</summary>
    Fighting,

    /// <summary>Another player surrendered at a meeting (the Ur-Quan's).</summary>
    Surrendered,

    /// <summary>Another player's talk destroyed the group at once (the Slylandro probe's destruct code).</summary>
    Destroyed,

    /// <summary>In a system: after another player's battle, the race's groups turn on the players (DoMissions).</summary>
    TurnOnYou,

    /// <summary>In a system: a group of the race spotted another player, and the race's groups turn on the players (NotifyOthers on detection).</summary>
    Spotted,

    /// <summary>In a system the player isn't in: after another player's battle there, the race's groups turn on the players there (DoMissions).</summary>
    TurnOnThem,

    /// <summary>In a system the player isn't in: a group of the race spotted another player, and the race's groups there turn on the players there.</summary>
    SpottedThere,

    /// <summary>A group that had flown at the player went: another player talked it away (one-shot in hyperspace, or sent off: the drone).</summary>
    ChaserTalkedAway,

    /// <summary>A group that had flown at the player went: another player's talk destroyed it at once (the instant victory).</summary>
    ChaserDestroyed,

    /// <summary>A group that had flown at the player went: destroyed in the battle another player's meeting started.</summary>
    ChaserDestroyedInBattle,

    /// <summary>A group that had flown at the player went after the battle another player's meeting started, which it survived (one-shot from its roll: the probes).</summary>
    ChaserGoneAfterBattle,

    /// <summary>A group that had flown at the player went after another player's meeting with it ended hostile with no battle to fight (one-shot, not fought: <see cref="MeetingOver"/>).</summary>
    ChaserGoneAfterMeeting,
}

/// <summary>
/// A notice for one player of what another player set off (stage 3b-1
/// item 9, decision 11): the event, the other player (<see cref="By"/>,
/// their name) and the race it's about (<see cref="Race"/>, as
/// <see cref="Meeting.RaceName"/> names it). The world gathers them
/// (<see cref="GameWorld.TakeNotices"/>), each once for each player it
/// tells; the host's session sends each in words (<see cref="Text"/>) as
/// the battle notices are sent (<c>BattleNoticeKind.Told</c>) to a player
/// present, and drops it for one away (by the time they're back it's stale).
/// </summary>
public sealed record WorldNotice(string PlayerId, NoticeEvent Event, string By, string Race)
{
    /// <summary>In words, for the player it's for.</summary>
    public string Text => Event switch
    {
        NoticeEvent.Talking => $"{By} is talking to the {Race}.",
        NoticeEvent.HoldOff => $"The {Race} hold off: {By} is meeting them.",
        NoticeEvent.Meeting => $"{By} is meeting the {Race}.",
        NoticeEvent.Peace => $"{By} made peace with the {Race}.",
        NoticeEvent.MeetingOver => $"{By}'s meeting with the {Race} is over.",
        NoticeEvent.Fighting => $"{By} is fighting the {Race}.",
        NoticeEvent.Surrendered => $"{By} surrendered to the {Race}.",
        NoticeEvent.Destroyed => $"{By} destroyed the {Race}.",
        NoticeEvent.TurnOnYou => $"The {Race}'s ships turn on you.",
        NoticeEvent.Spotted => $"The {Race} turn on you: they spotted {By}.",
        NoticeEvent.TurnOnThem => $"The {Race}'s ships turn on the players where {By} fought them.",
        NoticeEvent.SpottedThere => $"The {Race} spotted {By} and turn on the players there.",
        NoticeEvent.ChaserTalkedAway => $"The {Race} chasing you are gone: {By} talked them away.",
        NoticeEvent.ChaserDestroyed => $"The {Race} chasing you are gone: {By} destroyed them.",
        NoticeEvent.ChaserDestroyedInBattle => $"The {Race} chasing you were destroyed in the battle {By} started.",
        NoticeEvent.ChaserGoneAfterBattle => $"The {Race} chasing you are gone after the battle {By} started.",
        NoticeEvent.ChaserGoneAfterMeeting => $"The {Race} chasing you are gone after {By}'s meeting with them.",
        _ => "",
    };
}
