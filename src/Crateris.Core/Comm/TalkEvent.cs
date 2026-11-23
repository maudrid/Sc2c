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

/// <summary>Where a talk is, as the scripts read it (globdata.h's activities: LOBYTE (GLOBAL (CurrentActivity))).</summary>
public enum TalkActivity
{
    SuperMelee,
    InLastBattle,
    InEncounter,
    InHyperspace,
    InInterplanetary,
    WonLastBattle,
}

/// <summary>How a talk's screen comes in (comm.h's CommIntroMode).</summary>
public enum CommIntroMode
{
    CrossfadeSpace,
    CrossfadeWindow,
    CrossfadeScreen,
    FadeInScreen,
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
/// phrase table. <see cref="Callback"/> names the script's function
/// NPCPhrase_cb gave it, which runs as the line plays
/// (<see cref="CallbackRun"/>): the Zoq-Fot-Pik's switch of speaker, which
/// a client follows to show the line as the right one's; null for none.
/// </summary>
public sealed record LineQueued(int Phrase, string? Clip, byte[] TextBytes, string? Callback = null) : TalkEvent
{
    public string Text => Encoding.UTF8.GetString(TextBytes);
}

/// <summary>Several voice clips joined into one with this text (SpliceMultiTrack).</summary>
public sealed record MultiTrackQueued(IReadOnlyList<string> ClipNames, byte[] TextBytes) : TalkEvent
{
    /// <summary>How many voice clips (SpliceMultiTrack's TrackNames, to the first missing).</summary>
    public int Clips => ClipNames.Count;
}

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

/// <summary>The alien's mouth moves with its lines, or stops (EnableTalkingAnim).</summary>
public sealed record TalkingAnimation(bool Enabled) : TalkEvent;

/// <summary>How the talk's screen comes in (SetCommIntroMode), over <see cref="Time"/> (UQM's 1/840 seconds).</summary>
public sealed record IntroModeSet(CommIntroMode Mode, int Time) : TalkEvent;

/// <summary>Ships of a race (races.h's number) asked to join the escort fleet, and those that did (AddEscortShips).</summary>
public sealed record EscortsAdded(int Race, int Count, int Added) : TalkEvent;

/// <summary>
/// The first escort of a race (races.h's number) with its race's starting
/// crew given <see cref="Crew"/> crew and the captain <see cref="Captain"/>
/// (SetEscortCrewComplement: the Spathi's Fwiffo alone in his Eluder).
/// </summary>
public sealed record EscortCrewSet(int Race, int Crew, int Captain) : TalkEvent;

/// <summary>
/// A clock event the script added (AddEvent: <see cref="Relative"/> to
/// today or on that date, by gameev.h's name), which the story queues, runs
/// or sets aside (StoryState.AddEvent).
/// </summary>
public sealed record EventAdded(bool Relative, int Months, int Days, int Years, string Event) : TalkEvent;

/// <summary>
/// A line's callback (NPCPhrase_cb) ran: playback reached a chunk of the
/// line (each page of it is one, each running it), as
/// <see cref="Talk.AlienTalkSegue"/> or the play-out after a script
/// function plays the lines; named by the script's function.
/// </summary>
public sealed record CallbackRun(string Callback) : TalkEvent;

/// <summary>The bio data aboard changed (TotalBioMass): the Umgah's reward, the Melnorme's buying.</summary>
public sealed record BioChanged(int BioData) : TalkEvent;

/// <summary>
/// Ships of a race (races.h's number) asked to leave the escort fleet,
/// up to <see cref="Count"/> (65535 from RemoveEscortShips: every one),
/// and those that did (RemoveSomeEscortShips: the Orz's, as their alliance
/// breaks).
/// </summary>
public sealed record EscortsRemoved(int Race, int Count, int Removed) : TalkEvent;

/// <summary>
/// A race's alliance made (<see cref="Flag"/>) or ended (SetRaceAllied):
/// <see cref="Result"/> 1, or 0 for no such race. <see cref="Changed"/>
/// (not UQM's, nor recorded) whether its standing changed: not for a dead
/// race, which stays dead, nor one already so.
/// </summary>
public sealed record RaceAllied(int Race, bool Flag, int Result, bool Changed) : TalkEvent;

/// <summary>
/// A race's sphere to be shown on the starmap from now on
/// (StartSphereTracking, from a script): <see cref="Result"/> the race's
/// number, or 0 for an extinct race or no such race (and for the Arilou,
/// whose number is 0).
/// </summary>
public sealed record SphereTracked(int Race, int Result) : TalkEvent;

/// <summary>
/// The answering ship's own slots changed (the Melnorme's strip, stage
/// 3b-2 item 4): its drive, jet and module slots, each sis.h's module
/// number or its kind's empty mark, as the recording's "slots" gives them.
/// </summary>
public sealed record SlotsChanged(byte[] Drives, byte[] Jets, byte[] Modules) : TalkEvent
{
    public bool Equals(SlotsChanged? other) =>
        other is not null && Drives.SequenceEqual(other.Drives) && Jets.SequenceEqual(other.Jets) && Modules.SequenceEqual(other.Modules);

    public override int GetHashCode() => HashCode.Combine(Drives.Length, Jets.Length, Modules.Length);
}

/// <summary>
/// The modules' prices changed (GLOBAL (ModuleCost), shared: a module
/// bought from the Melnorme goes on sale for everyone, stage 3b-2 item 4):
/// each purchasable module's, PLANET_LANDER to ANTIMISSILE_DEFENSE, in
/// units of MODULE_COST_SCALE, as the recording's "costs" gives them.
/// </summary>
public sealed record CostsChanged(int[] Costs) : TalkEvent
{
    /// <summary>The prices before (not UQM's, nor recorded): what the change made for sale.</summary>
    public int[] Was { get; init; } = Array.Empty<int>();

    public bool Equals(CostsChanged? other) => other is not null && Costs.SequenceEqual(other.Costs);

    public override int GetHashCode() => Costs.Length;
}

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
