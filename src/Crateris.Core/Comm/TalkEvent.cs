// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Data;

namespace Crateris.Core.Comm;

/// <summary>How a talk ends (Segue, commglue.h).</summary>
public enum Segue
{
    /// <summary>Depart in peace.</summary>
    Peace,

    /// <summary>Go into battle.</summary>
    Hostile,

    /// <summary>Won at once, without a battle.</summary>
    Victory,

    /// <summary>The game is over.</summary>
    Defeat,
}

/// <summary>
/// What a talk did, in order: what its script asked of UQM's engine
/// (the lines queued, the waits, the screen's changes, the story and ship
/// writes, the random numbers drawn) and the player's part (the replies
/// offered and the one chosen). The recording program records the same
/// events from UQM's own script (tools/extract/golden/comm.c).
/// </summary>
public abstract record TalkEvent;

/// <summary>
/// A line queued (SpliceTrack): with a voice clip, a new track; without
/// one, text added to the last track's last page (a name spliced in, say).
/// <see cref="Phrase"/> is its entry number from 1, 0 for text not in the
/// phrase table.
/// </summary>
public sealed record LineQueued(int Phrase, string? Clip, byte[] TextBytes) : TalkEvent
{
    public string Text => Encoding.UTF8.GetString(TextBytes);
}

/// <summary>Several voice clips joined into one with this text (SpliceMultiTrack).</summary>
public sealed record MultiTrackQueued(int Clips, byte[] TextBytes) : TalkEvent;

/// <summary>The script waits for the lines to play up to track <see cref="Track"/> (AlienTalkSegue); 65535 is all of them.</summary>
public sealed record TalkWait(int Track) : TalkEvent;

/// <summary>The colours fade to colour map <see cref="Map"/> over <see cref="Time"/> (XFormColorMap, in UQM's 1/840 seconds).</summary>
public sealed record ColorTransform(int Map, int Time) : TalkEvent;

/// <summary>The alien's colour map or ambient animations changed (CommData's AlienColorMap and AnimFlags).</summary>
public sealed record ScreenChanged(int ColorMap, IReadOnlyList<CommAnimationFlags> Ambient) : TalkEvent;

/// <summary>The music the talk plays (HailAlien: the alternate song if asked for and it loads, else the song).</summary>
public sealed record SongChosen(string Resource) : TalkEvent;

/// <summary>A story field written (SET_GAME_STATE), with the value as given.</summary>
public sealed record StateSet(string Field, int Value) : TalkEvent;

/// <summary>The ship changed: crew, fuel (1/100 units), landers, resource units, minerals.</summary>
public sealed record ShipChanged(int Crew, uint Fuel, int Landers, uint ResourceUnits, IReadOnlyList<int> Minerals) : TalkEvent;

/// <summary>DeltaSISGauges called with these changes.</summary>
public sealed record GaugesChanged(int Crew, int Fuel, int ResourceUnits) : TalkEvent;

/// <summary>A random number drawn (TFB_Random).</summary>
public sealed record RandomDrawn(uint Value) : TalkEvent;

/// <summary>The player chose the reply at <see cref="Index"/> of those offered.</summary>
public sealed record ReplyChosen(int Index) : TalkEvent;

/// <summary>The replies offered, waiting for the player's choice.</summary>
public sealed record RepliesOffered(IReadOnlyList<TalkReply> Replies) : TalkEvent;

/// <summary>No replies: the talk is over, ending so.</summary>
public sealed record TalkEnded(Segue Segue) : TalkEvent;

/// <summary>A reply offered: its phrase number, the script's function it calls, and its text as shown.</summary>
public sealed record TalkReply(int Phrase, string Function, byte[] TextBytes)
{
    public string Text => Encoding.UTF8.GetString(TextBytes);
}
