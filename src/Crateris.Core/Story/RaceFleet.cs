// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (races.h FLEET_INFO; globdata.c
// InitGameStructures; save.c and load.c's race queue),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Story;

/// <summary>
/// A race's fleet, as its FLEET_INFO in avail_race_q holds it (races.h):
/// the sphere of influence's strength (<see cref="ActualStrength"/>, its
/// radius in steps of SPHERE_RADIUS_INCREMENT; 0 for none, INFINITE_RADIUS
/// for a race met anywhere) and centre (<see cref="LocX"/>,
/// <see cref="LocY"/>, universe units), the sphere as the starmap last
/// showed it (<see cref="KnownStrength"/>, 0 while unknown, and
/// <see cref="KnownLocX"/>, <see cref="KnownLocY"/>; StartSphereTracking),
/// its daily growth (<see cref="Growth"/>, a signed byte, with
/// <see cref="GrowthFract"/> 256ths more and the error term that counts
/// them up, <see cref="GrowthErrTerm"/>; check_race_growth) and where it's
/// heading (<see cref="DestLocX"/>, <see cref="DestLocY"/>,
/// <see cref="DaysLeft"/> days away, with the event that runs on its
/// arrival, <see cref="Arrival"/>: func_index, null for none, ~0 in UQM;
/// SetRaceDest). Its standing (allied_state) is the story state's
/// <see cref="StoryState.Alliance"/>; its ships' crew and energy
/// (crew_level, max_crew, max_energy), never changed, are the race's ship's
/// (<see cref="StoryRules.RaceShips"/>). The fields hold UQM's types'
/// values: strengths are COUNTs (0 to 65,535), places COORDs (−32,768 to
/// 32,767), the rest BYTEs (0 to 255).
/// </summary>
public sealed record RaceFleet(
    int ActualStrength, int KnownStrength, int LocX, int LocY, int KnownLocX, int KnownLocY,
    int Growth, int GrowthFract, int GrowthErrTerm, int DaysLeft, string? Arrival, int DestLocX, int DestLocY)
{
    /// <summary>
    /// A new game's fleet (InitGameStructures): its ship's sphere, unknown,
    /// at its centre, with no growth (the error term at 255 &gt;&gt; 1) and
    /// no heading (dest_loc 0, as the queue's new link leaves it). The
    /// Yehat rebels' has no sphere (InitGameStructures' "Rebel special
    /// case": their ship is the Yehat's, whose sphere they'd otherwise get).
    /// </summary>
    public static RaceFleet NewGame(RaceFleetRules rules) =>
        new(rules.Race == "YEHAT_REBEL_SHIP" ? 0 : rules.Strength, 0, rules.HomeX, rules.HomeY, rules.HomeX, rules.HomeY,
            0, 0, 255 >> 1, 0, null, 0, 0);

    /// <summary>
    /// Why this fleet can't be a FLEET_INFO's (a field outside its type's
    /// range, or an arrival event that doesn't exist), or null if it can.
    /// </summary>
    internal string? Invalid(StoryRules rules)
    {
        foreach (var (name, value, min, max) in new (string, int, int, int)[]
        {
            ("actual strength", ActualStrength, 0, ushort.MaxValue), ("known strength", KnownStrength, 0, ushort.MaxValue),
            ("place's x", LocX, short.MinValue, short.MaxValue), ("place's y", LocY, short.MinValue, short.MaxValue),
            ("known place's x", KnownLocX, short.MinValue, short.MaxValue),
            ("known place's y", KnownLocY, short.MinValue, short.MaxValue),
            ("growth", Growth, 0, byte.MaxValue), ("growth's fraction", GrowthFract, 0, byte.MaxValue),
            ("growth's error term", GrowthErrTerm, 0, byte.MaxValue), ("days left", DaysLeft, 0, byte.MaxValue),
            ("heading's x", DestLocX, short.MinValue, short.MaxValue), ("heading's y", DestLocY, short.MinValue, short.MaxValue),
        })
        {
            if (value < min || value > max)
                return $"its {name} is {value}, not {min} to {max}";
        }
        if (Arrival is not null && !rules.EventIds.ContainsKey(Arrival))
            return $"its arrival runs {Arrival}, which isn't an event";
        return null;
    }
}
