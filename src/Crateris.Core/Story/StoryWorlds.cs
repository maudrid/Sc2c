// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/generate/gensol.c's
// GenerateSol_generateEnergy and GenerateSol_pickupEnergy), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Planetside;
using Crateris.Core.World;

namespace Crateris.Core.Story;

/// <summary>
/// The story worlds' energy nodes on the surface, as their generators have
/// them: so far the Lunar base on Luna (stage 3a). The rest (Fwiffo on
/// Pluto, the ruins, the artifacts) come with their stories in 3c; until
/// then their nodes show on the scan but aren't on the surface.
/// </summary>
public static class StoryWorlds
{
    /// <summary>The Lunar base's string table, its lander report.</summary>
    public const string MoonbaseReport = "text.moonbase";

    private static bool IsLuna(string? special, WorldId world) =>
        special == "SOL_DEFINED" && world.Planet == 2 && world.Moon == 1;

    /// <summary>
    /// The sheet a world's energy nodes are drawn and touched with on its
    /// surface (the generator's PlanetSideFrame[1]), or null if they aren't
    /// put there yet. The Lunar base is there until it's been taken
    /// (MOONBASE_DESTROYED; the node's retrieved bit says so too).
    /// </summary>
    public static SurfaceSheet? EnergySheet(string? special, WorldId world, StoryState? story) =>
        IsLuna(special, world) && story is not null && story.Get("MOONBASE_DESTROYED") == 0
            ? SurfaceSheet.Moonbase : null;

    /// <summary>
    /// GenerateSol_pickupEnergy for Luna: the Lunar base's report is shown
    /// and the lander goes up (SetLanderTakeoff), and the base is the
    /// group's (MOONBASE_DESTROYED, MOONBASE_ON_SHIP). True if the node is
    /// taken.
    /// </summary>
    public static bool PickupEnergy(string? special, WorldId world, StoryState story, Lander lander, int node)
    {
        if (!IsLuna(special, world) || node != 0 || story.Get("MOONBASE_DESTROYED") != 0)
            return false;
        lander.Takeoff(new LanderReport(MoonbaseReport));
        story.Set("MOONBASE_DESTROYED", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        return true;
    }
}
