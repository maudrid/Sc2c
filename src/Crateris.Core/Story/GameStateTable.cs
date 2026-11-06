// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (globdata.h, ADD_GAME_STATE),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Story;

/// <summary>One of UQM's game-state fields: its bits, StartBit to EndBit (ADD_GAME_STATE).</summary>
public sealed record GameStateField(string Name, int Index, int StartBit, int Bits)
{
    /// <summary>END_name: the field's last bit.</summary>
    public int EndBit => StartBit + Bits - 1;

    /// <summary>The largest value the field holds.</summary>
    public int Max => (1 << Bits) - 1;
}

/// <summary>
/// UQM's game-state fields, from data/game-state.json: 447 fields packed
/// in 1,238 bits, in UQM's order. Every field is 1 to 8 bits (a BYTE); the
/// 32-bit values UQM keeps (GET_GAME_STATE_32) are four 8-bit fields in a
/// row, the first named for them.
/// </summary>
public sealed class GameStateTable
{
    private readonly Dictionary<string, GameStateField> _byName;

    public IReadOnlyList<GameStateField> Fields { get; }

    /// <summary>NUM_GAME_STATE_BITS.</summary>
    public int TotalBits { get; }

    /// <summary>The bytes the bits are packed in (GameState[(NUM_GAME_STATE_BITS + 7) >> 3]).</summary>
    public int Bytes => (TotalBits + 7) >> 3;

    public GameStateTable(IReadOnlyList<GameStateField> fields, int totalBits)
    {
        int next = 0;
        _byName = new Dictionary<string, GameStateField>(fields.Count);
        for (int i = 0; i < fields.Count; ++i)
        {
            var f = fields[i];
            if (f.Index != i || f.StartBit != next || f.Bits < 1 || f.Bits > 8)
                throw new InvalidDataException($"game state field {f.Name}: not where it should be, or not 1 to 8 bits");
            if (!_byName.TryAdd(f.Name, f))
                throw new InvalidDataException($"game state field {f.Name} is listed twice");
            next += f.Bits;
        }
        if (next != totalBits)
            throw new InvalidDataException($"game state fields take {next} bits, not {totalBits}");
        Fields = fields;
        TotalBits = totalBits;
    }

    public static GameStateTable Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "game-state.json")));
        var root = doc.RootElement;
        var fields = root.GetProperty("states").EnumerateArray()
            .Select((s, i) => new GameStateField(s.GetProperty("name").GetString()!, i,
                s.GetProperty("start_bit").GetInt32(), s.GetProperty("bits").GetInt32()))
            .ToList();
        return new GameStateTable(fields, root.GetProperty("total_bits").GetInt32());
    }

    public GameStateField this[string name] =>
        _byName.TryGetValue(name, out var f) ? f : throw new KeyNotFoundException($"no game state field {name}");

    public bool Contains(string name) => _byName.ContainsKey(name);
}
