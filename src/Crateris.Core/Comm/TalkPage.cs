// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: DoTalkSegue's and
// PlayerResponseInput's keys, DoConvSummary), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors, under
// the GNU GPL 2 or later.

namespace Crateris.Core.Comm;

/// <summary>
/// How the replier pages a talk on the communication screen, as UQM's keys
/// do, for everyone watching a story talk to follow (decision 7). Sent as
/// what the key did, not the key: a watcher's screen may be a moment
/// behind, where the same key would do something else.
/// </summary>
public enum TalkPage : byte
{
    /// <summary>The lines skipped to their end (Cancel while they play).</summary>
    Skip,

    /// <summary>The lines played again from the start (Left).</summary>
    Replay,

    /// <summary>The summary of the lines opened (Cancel at the replies).</summary>
    Summary,

    /// <summary>The summary's page turned, or closed after its last (Select, Cancel or Right).</summary>
    SummaryTurn,
}

/// <summary>The replier's paging of a story talk's current lines: <see cref="Key"/> the lines' (<see cref="TalkView.LinesKey"/>).</summary>
public sealed record TalkPaging(string Key, List<TalkPage> Pages);
