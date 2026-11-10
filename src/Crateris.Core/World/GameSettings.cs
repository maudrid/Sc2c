// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;

namespace Crateris.Core.World;

/// <summary>
/// Host settings. Every number the design leaves open is a setting here,
/// with a placeholder default to be tuned by playtesting.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Most players in one game, the host included.</summary>
    public int MaxPlayers { get; init; } = 4;

    /// <summary>
    /// Constant clock rate (docs/differences.md row 1). Placeholder: UQM's
    /// hyperspace rate.
    /// </summary>
    public double SecondsPerGameDay { get; init; } = 5;

    /// <summary>
    /// Frames between a client pressing a key and the host applying it, like
    /// UQM's netplay input delay (default 2 frames, about 83 ms).
    /// </summary>
    public int InputDelayFrames { get; init; } = 2;

    /// <summary>
    /// A new guest's escort. Placeholder until the escort module set is
    /// decided: the flagship's starting drives, one fuel tank, 10 fuel,
    /// 10 crew, and for landing one lander and one storage bay (stage 2b,
    /// decision 6); it holds 10 crew at most (stage 3, decision 10).
    /// </summary>
    public ShipLoadout EscortLoadout { get; init; } = new(Thrusters: 2, Jets: 2, FuelTanks: 1,
        HighEfficiencyTanks: 0, Crew: 10, Fuel: 1000, Landers: 1, StorageBays: 1, CrewCapacity: 10);

    /// <summary>
    /// A new guest's escort's name, until they rename it (stage 3, decision
    /// 10); cut to UQM's 15 bytes. Placeholder.
    /// </summary>
    public string EscortShipName { get; init; } = "Wayfarer";

    /// <summary>
    /// A battle's arena, UQM's times this each way (stage 4, decision 4):
    /// from 1 (UQM's own, as the traces run) to
    /// <see cref="Combat.ArenaSize.MaxScale"/>. Four ships have room at 1.5;
    /// UQM's camera, out to its 4x, then shows a third of the arena each
    /// way. In an arena bigger than UQM's, contact is tested across the
    /// seam (decision 5).
    /// </summary>
    public double BattleArenaScale { get; init; } = 1.5;

    /// <summary>
    /// The most elements a battle holds at once (UQM's MAX_DISPLAY_ELEMENTS
    /// is 150, for two ships): a shot fired with the list full isn't.
    /// At least UQM's. Placeholder: twice UQM's, for up to four players'
    /// ships and the enemy's.
    /// </summary>
    public int BattleMaxElements { get; init; } = 2 * Combat.DisplayList.MaxDisplayElements;

    /// <summary>The most <see cref="BattleMaxElements"/> may be.</summary>
    public const int MostBattleElements = 1000;

    /// <summary>Refuses battle settings out of range (the arena's scale, the list's size).</summary>
    public void CheckBattle()
    {
        _ = Combat.ArenaSize.Scaled(BattleArenaScale);
        if (BattleMaxElements < Combat.DisplayList.MaxDisplayElements || BattleMaxElements > MostBattleElements)
            throw new ArgumentOutOfRangeException(nameof(BattleMaxElements),
                $"a battle's list of {BattleMaxElements} elements isn't from UQM's {Combat.DisplayList.MaxDisplayElements} to {MostBattleElements}");
    }
}
