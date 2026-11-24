// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/generate/gensol.c's
// GenerateSol_generateEnergy and GenerateSol_pickupEnergy, genspa.c's
// GenerateSpathi_pickupLife, genrain.c's
// GenerateRainbowWorld_generateOrbital, genmyc.c's
// GenerateMycon_generateOrbital for the shattered worlds), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or
// later.

using Crateris.Core.Data;
using Crateris.Core.Planetside;
using Crateris.Core.World;

namespace Crateris.Core.Story;

/// <summary>
/// The story worlds' energy nodes on the surface, as their generators have
/// them: so far the Lunar base on Luna (stage 3a). The rest (Fwiffo on
/// Pluto, the ruins, the artifacts) come with their stories in 3c; until
/// then their nodes show on the scan but aren't on the surface. And the
/// story worlds' life: the Evil Ones on Spathiwa (the stage 3b-2 plan's
/// question 2), which the Spathi at home ask about. And the orbits that
/// mark a world: the rainbow worlds (the plan's question 2), which the
/// Melnorme buy.
/// </summary>
public static class StoryWorlds
{
    /// <summary>The Lunar base's string table, its lander report.</summary>
    public const string MoonbaseReport = "text.moonbase";

    /// <summary>Every lander report a story world can show (their string tables).</summary>
    public static readonly IReadOnlyList<string> Reports = new[] { MoonbaseReport };

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

    /// <summary>NUM_EVIL_ONES (genspa.c): the Evil Ones on Spathiwa.</summary>
    public const int EvilOnes = 32;

    private static bool IsSpathiwa(string? special, WorldId world) =>
        special == "SPATHI_DEFINED" && world.Planet == 0 && world.Moon is null;

    /// <summary>
    /// GenerateSpathi_pickupLife for Spathiwa, as a creature there is taken
    /// (before its node is marked retrieved; <paramref name="retrievedBefore"/>
    /// is countNodesRetrieved's count until then): SPATHI_CREATURES_EXAMINED,
    /// and SPATHI_CREATURES_ELIMINATED with the last of the 32 Evil Ones.
    /// Both are shared story fields, as the world's taken nodes are, so
    /// whichever players take them, the last one taken eliminates them.
    /// Other worlds' life is the default generator's: nothing more.
    /// </summary>
    public static void PickupLife(string? special, WorldId world, StoryState story, int retrievedBefore)
    {
        if (!IsSpathiwa(special, world))
            return;
        story.Set("SPATHI_CREATURES_EXAMINED", 1);
        if (retrievedBefore + 1 == EvilOnes)
            story.Set("SPATHI_CREATURES_ELIMINATED", 1);   // the last creature picked up
    }

    /// <summary>RAINBOW_DEFINED: the special generator of the stars whose first planet is a rainbow world.</summary>
    public const string RainbowSpecial = "RAINBOW_DEFINED";

    /// <summary>
    /// The rainbow world's bit in RAINBOW_WORLD0/1 for a star
    /// (GenerateRainbowWorld_generateOrbital's which_rainbow): the number
    /// of RAINBOW_DEFINED stars before it in the map's order; null for a
    /// star that isn't one.
    /// </summary>
    public static int? RainbowIndex(StarMap map, int star) =>
        map.Stars[star].Special == RainbowSpecial
            ? map.Stars.Take(star).Count(s => s.Special == RainbowSpecial)
            : null;

    /// <summary>
    /// A ship goes into orbit around a world (the generator's
    /// generateOrbital, as UQM's orbit runs it, a saved game loaded in
    /// orbit too): around a rainbow world (planet 0 of a RAINBOW_DEFINED
    /// star) its bit is set in RAINBOW_WORLD0/1 (genrain.c), the group's
    /// one story field, so whoever orbits it, the group has found it, and
    /// the Melnorme pay for it once (the plan's question 6); around a
    /// shattered world (planet 0 of an egg case's system, genmyc.c) the
    /// players have seen one (KNOW_ABOUT_SHATTERED 1 from 0), which the
    /// Mycon can then be asked about.
    /// </summary>
    public static void Orbited(StarMap map, WorldId world, StoryState story)
    {
        if (world.Planet != 0 || world.Moon is not null)
            return;
        // genmyc.c:164-168 (GenerateMycon_generateOrbital): the first
        // planet of an egg case's system is a shattered world; seeing one
        // lets the players ask the Mycon about them (KNOW_ABOUT_SHATTERED
        // 1, unless they know more already: 2 once told), the group's, as
        // every story field (the stage 3b-2 plan's question 2's kind of
        // pickup: no device, read by a talk).
        if (map.Stars[world.Star].Special is "EGG_CASE0_DEFINED" or "EGG_CASE1_DEFINED" or "EGG_CASE2_DEFINED"
            && story.Get("KNOW_ABOUT_SHATTERED") == 0)
            story.Set("KNOW_ABOUT_SHATTERED", 1);
        if (RainbowIndex(map, world.Star) is not int which)
            return;
        int mask = story.Get("RAINBOW_WORLD0") | (story.Get("RAINBOW_WORLD1") << 8);
        mask |= 1 << which;
        story.Set("RAINBOW_WORLD0", mask & 0xFF);
        story.Set("RAINBOW_WORLD1", mask >> 8);
    }
}
