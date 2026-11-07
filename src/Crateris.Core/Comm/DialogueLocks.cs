// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Comm;

/// <summary>
/// The dialogue lock (docs/differences.md row 17): an ordinary
/// conversation is one player's at a time per alien race, so two players
/// can't change one race's state at once. A conversation's race is UQM's
/// own (RACE_SHIP_FOR_COMM, data/races.json's conversation_races): so the
/// Yehat rebels lock with the Yehat, the Slylandro homeworld with the
/// Slylandro, the Talking Pet with the Umgah, the starbase commander as
/// the humans. One change: the Ur-Quan drone locks with the Ur-Quan, as
/// InitCommunication runs the drone's talk with the Ur-Quan's script and
/// story. A lock is let go when its talk ends or its player leaves; a
/// player is in one talk at a time, so taking a lock twice is still one.
/// Story talks don't take it: they pause the game for everyone (row 2),
/// and one won't start while another player holds that race's lock (the
/// talks that take locks come with item 8 and 3b, which check it). Not
/// saved: talks aren't.
/// </summary>
public sealed class DialogueLocks
{
    /// <summary>The one deliberate change to UQM's races: the drone talks as the Ur-Quan.</summary>
    private static readonly Dictionary<string, string> Changes = new()
    {
        ["URQUAN_DRONE_CONVERSATION"] = "URQUAN_SHIP",
    };

    private readonly IReadOnlyDictionary<string, string> _raceOf;
    private readonly Dictionary<string, string> _held = new();   // race -> player

    public DialogueLocks(IReadOnlyDictionary<string, string> raceOf) => _raceOf = raceOf;

    /// <summary>Each conversation id's race, from data/races.json (INVALID_CONVERSATION isn't one).</summary>
    public static DialogueLocks Load(string dataDirectory)
    {
        using var races = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "races.json")));
        var raceOf = new Dictionary<string, string>();
        foreach (var c in races.RootElement.GetProperty("conversation_races").EnumerateArray())
        {
            string conversation = c.GetProperty("conversation").GetString()!;
            if (conversation != "INVALID_CONVERSATION")
                raceOf[conversation] = Changes.GetValueOrDefault(conversation) ?? c.GetProperty("race").GetString()!;
        }
        return new DialogueLocks(raceOf);
    }

    /// <summary>The race a conversation is locked by (KeyNotFoundException for an id that isn't a conversation).</summary>
    public string RaceOf(string conversation) =>
        _raceOf.TryGetValue(conversation, out var race) ? race : throw new KeyNotFoundException($"no race for {conversation}");

    /// <summary>The player talking to a conversation's race now, or null.</summary>
    public string? Holder(string conversation) => _held.GetValueOrDefault(RaceOf(conversation));

    /// <summary>
    /// A player starts an ordinary conversation: theirs if no one else is
    /// talking to that race (or they already are). False, with who is, if
    /// someone else is (decision 8: they're told).
    /// </summary>
    public bool TryTake(string conversation, string playerId, out string? holder)
    {
        string race = RaceOf(conversation);
        if (_held.TryGetValue(race, out holder) && holder != playerId)
            return false;
        _held[race] = playerId;
        holder = playerId;
        return true;
    }

    /// <summary>The player's talk with a conversation's race ended.</summary>
    public void Release(string conversation, string playerId)
    {
        string race = RaceOf(conversation);
        if (_held.GetValueOrDefault(race) == playerId)
            _held.Remove(race);
    }

    /// <summary>The player left: every lock of theirs is let go.</summary>
    public void ReleaseAll(string playerId)
    {
        foreach (var race in _held.Where(h => h.Value == playerId).Select(h => h.Key).ToList())
            _held.Remove(race);
    }

    /// <summary>The races being talked to, and by whom.</summary>
    public IReadOnlyDictionary<string, string> Held => _held;
}
