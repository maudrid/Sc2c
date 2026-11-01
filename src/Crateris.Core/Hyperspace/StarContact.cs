// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (hyper.c hyper_collision, hyper_death
// and AddStars), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

/// <summary>
/// The sizes used to decide when the flagship touches a star, in display
/// pixels of UQM's 320x240 hyperspace view (32 logical units each).
/// UQM tests sprite masks from the content package: the flagship's
/// hyperspace sprite against a single point at the star's centre, and,
/// for the star just left, against the star's whole image. Until the
/// content stage reads those masks, circles of about the sprites' size
/// stand in for them.
/// </summary>
public sealed record StarContactShape(
    int ShipRadius = 6,
    int DwarfRadius = 4,
    int GiantRadius = 6,
    int SuperGiantRadius = 8)
{
    public static readonly StarContactShape Provisional = new();

    public int StarRadius(StarSize size) => size switch
    {
        StarSize.Dwarf => DwarfRadius,
        StarSize.Giant => GiantRadius,
        _ => SuperGiantRadius,
    };
}

/// <summary>When a ship in hyperspace flies into a star, as hyper_collision decides it.</summary>
public static class StarContact
{
    private const int LogUnitsPerPixel = 32;   // LOG_SPACE_WIDTH / SPACE_WIDTH

    /// <summary>
    /// Call once per hyperspace frame, after the ship has moved. Returns the
    /// star the ship enters, or null. A ship only enters a star while moving
    /// or steering; a ship at rest on a star stays in hyperspace. The star
    /// just left can't be entered until the ship has cleared its image once.
    /// </summary>
    public static Star? Check(HyperFlight ship, StarMap map, StarContactShape? shape = null)
    {
        shape ??= StarContactShape.Provisional;

        if (ship.StarJustLeft is int left)
        {
            // hyper_death: once the ship no longer overlaps the star it
            // left, that star can be entered again.
            Star s = map.Stars[left];
            if (!Within(ship, s, shape.ShipRadius + shape.StarRadius(s.Size)))
                ship.StarJustLeft = null;
        }

        var (dx, dy) = ship.Velocity.Current();
        bool active = dx != 0 || dy != 0
            || (ship.Status & (HyperFlight.StatusLeft | HyperFlight.StatusRight | HyperFlight.StatusThrust
                               | HyperFlight.StatusWeapon | HyperFlight.StatusSpecial)) != 0;
        if (!active)
            return null;

        Star? best = null;
        long bestDist = long.MaxValue;
        foreach (Star s in map.Stars)
        {
            if (s.Number == ship.StarJustLeft)
                continue;
            long d = DistanceSquared(ship, s);
            long r = (long)shape.ShipRadius * LogUnitsPerPixel;
            if (d <= r * r && d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }
        return best;
    }

    private static bool Within(HyperFlight ship, Star s, int radiusPixels)
    {
        long r = (long)radiusPixels * LogUnitsPerPixel;
        return DistanceSquared(ship, s) <= r * r;
    }

    private static long DistanceSquared(HyperFlight ship, Star s)
    {
        long dx = Units.UniverseToLogX(s.X) - (long)ship.LogX;
        long dy = Units.UniverseToLogY(s.Y) - (long)ship.LogY;
        return dx * dx + dy * dy;
    }
}
