// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/lander.c CreatureData,
// planets/lifeform.h), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>How a creature reacts once it notices a lander (BEHAVIOR_*).</summary>
public enum CreatureBehavior { Hunt = 0, Flee = 1, Unpredictable = 2 }

/// <summary>How a creature moves (SPEED_*): motionless creatures stay at their nodes.</summary>
public enum CreatureSpeed { Motionless = 0, Slow = 1, Medium = 2, Fast = 3 }

/// <summary>A kind of creature, as UQM's CreatureData has it.</summary>
public sealed record Lifeform(int Index, string Label, CreatureBehavior Behavior, int Awareness,
    CreatureSpeed Speed, int Danger, int BioValue, int HitPoints)
{
    /// <summary>
    /// Out of 100, the chance each look that a creature within a screen's
    /// width notices the lander: (awareness + 1) * (30 / 6) in object_animation.
    /// </summary>
    public int DetectPercent => (Awareness + 1) * (30 / 6);

    /// <summary>Out of 128, the chance that touching it costs the lander a crew member (danger_vals in CheckObjectCollision).</summary>
    public int DangerChance => Danger switch { 0 => 0, 1 => 6, 2 => 13, _ => 26 };
}

/// <summary>Every kind of creature (data/lifeforms.json, from lander.c's CreatureData).</summary>
public sealed class Lifeforms
{
    private readonly Lifeform[] _kinds;

    private Lifeforms(Lifeform[] kinds) => _kinds = kinds;

    /// <summary>MAX_LIFE_VARIATION: the most kinds a world's surface has sheets for.</summary>
    public const int MaxVariation = 3;

    public int Count => _kinds.Length;

    public Lifeform this[int index] => _kinds[index];

    public static Lifeforms Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "lifeforms.json")));
        var kinds = new List<Lifeform>();
        foreach (var l in doc.RootElement.GetProperty("lifeforms").EnumerateArray())
        {
            int index = l.GetProperty("index").GetInt32();
            if (index != kinds.Count)
                throw new InvalidDataException("lifeforms out of order");
            kinds.Add(new Lifeform(index, l.GetProperty("label").GetString()!,
                Enum.Parse<CreatureBehavior>(l.GetProperty("behavior").GetString()!, true),
                Level(l.GetProperty("awareness").GetString()!, "LOW", "MEDIUM", "HIGH"),
                l.GetProperty("speed").GetString() switch
                {
                    "MOTIONLESS" => CreatureSpeed.Motionless,
                    "SLOW" => CreatureSpeed.Slow,
                    "MEDIUM" => CreatureSpeed.Medium,
                    "FAST" => CreatureSpeed.Fast,
                    var s => throw new InvalidDataException($"unknown creature speed {s}"),
                },
                Level(l.GetProperty("danger").GetString()!, "HARMLESS", "WEAK", "NORMAL", "MONSTROUS"),
                l.GetProperty("bio_value").GetInt32(), l.GetProperty("hit_points").GetInt32()));
        }
        return new Lifeforms(kinds.ToArray());
    }

    private static int Level(string value, params string[] levels) =>
        Array.IndexOf(levels, value) is var i and >= 0 ? i : throw new InvalidDataException($"unknown level {value}");
}
