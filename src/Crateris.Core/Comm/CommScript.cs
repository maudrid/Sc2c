// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.Comm;

/// <summary>
/// A conversation script ported from UQM (src/uqm/comm/NAME/), new for
/// each talk: its init function's changes to the race's description
/// (<see cref="Init"/>), its intro (init_encounter_func), and its
/// post-encounter and clean-up functions. Its reply functions take the
/// reply chosen, as UQM's RESPONSE_FUNCs do, and keep UQM's names, which
/// the recordings give.
/// </summary>
public abstract class CommScript
{
    private Talk? _talk;

    protected CommScript(ConversationScript phrases) => Phrases = phrases;

    /// <summary>The script's phrase ids (strings.h).</summary>
    public ConversationScript Phrases { get; }

    /// <summary>The talk this script runs in.</summary>
    protected Talk T => _talk ?? throw new InvalidOperationException("the script hasn't started");

    internal void Bind(Talk talk)
    {
        if (_talk is not null)
            throw new InvalidOperationException("a script runs one talk");
        _talk = talk;
    }

    /// <summary>init_NAME_comm: the description's changes for the story as it stands, and the segue.</summary>
    protected internal abstract void Init();

    /// <summary>init_encounter_func.</summary>
    protected internal abstract void Intro();

    /// <summary>
    /// The race's number speech (LOCDATA's AlienNumberSpeech), for a race
    /// whose description has it (data/conversations.json's number_speech);
    /// null for the rest.
    /// </summary>
    protected internal virtual NumberSpeech? NumberSpeech => null;

    /// <summary>post_encounter_func: after a talk that wasn't aborted.</summary>
    protected internal virtual void PostEncounter()
    {
    }

    /// <summary>uninit_encounter_func.</summary>
    protected internal virtual void Uninit()
    {
    }

    // UQM's script calls, as the scripts write them.
    protected void NPCPhrase(string id) => T.NPCPhrase(id);

    protected void NPCPhrase(int index) => T.NPCPhrase(index);

    protected void Response(string id, Action<int> func) => T.Response(id, func);

    protected bool PlayerSaid(int r, string id) => T.PlayerSaid(r, id);

    protected bool PhraseEnabled(string id) => T.PhraseEnabled(id);

    protected void DisablePhrase(string id) => T.DisablePhrase(id);

    protected void DisablePhrase(int r) => T.DisablePhrase(r);

    protected int GetGameState(string field) => T.Get(field);

    protected void SetGameState(string field, int value) => T.Set(field, value);

    protected ITalkShip Sis => T.Ship;
}

/// <summary>
/// A race's number speech (comm.h's NUMBER_SPEECH_DESC): how a number is
/// spoken, its digits from the largest.
/// </summary>
public sealed record NumberSpeech(IReadOnlyList<SpeechDigit> Digits);

/// <summary>
/// One digit of a race's number speech (SPEECH_DIGIT): the number divided
/// by <paramref name="Divider"/>, less <paramref name="Subtrahend"/>, is
/// spoken as the phrase <paramref name="Words"/> has for it (null for one
/// that isn't spoken), or, without words, as a number of its own; then its
/// name, the first of <paramref name="Names"/> whose divider leaves at most
/// its remainder (SPEECH_DIGITNAME), or else <paramref name="CommonName"/>
/// ("hundred").
/// </summary>
public sealed record SpeechDigit(int Divider, int Subtrahend, IReadOnlyList<string?>? Words,
    IReadOnlyList<(int Divider, int MaxRemainder, string Phrase)>? Names, string? CommonName);

