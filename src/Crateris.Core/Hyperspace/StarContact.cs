// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (hyper.c hyper_collision, hyper_death,
// AddStars and AllocHyperElement; process.c's collision of moving
// elements; collide.h's intersect points), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

/// <summary>
/// When a ship in hyperspace flies into a star, as hyper_collision decides
/// it, by UQM's sprite intersection (<see cref="Intersection"/>): the
/// flagship's hyperspace frame for its facing, moving over the frame from
/// where its sprite was to where it is, against each star near enough to be
/// on its screen (AddStars). A star is tested as a single point at its
/// centre (UQM's one-pixel frame), except the star just left, which is
/// tested with its whole image (hyper_death). Elements are placed in
/// hyperspace's logical space around the ship (AllocHyperElement) and
/// tested in display pixels, a quarter of a logical unit (WORLD_TO_DISPLAY).
/// </summary>
public static class StarContact
{
    /// <summary>How near a star must be for AddStars to put it near a ship: XOFFS and YOFFS over NUM_RADAR_SCREENS, in universe units.</summary>
    public const int ReachX = ((63 * 12 + (63 << 2)) >> 1) / 12;   // 42
    public const int ReachY = ((50 * 12 + (50 << 2)) >> 1) / 12;   // 33

    /// <summary>Whether AddStars puts a star near a ship at a universe position (only those can be touched, or drawn).</summary>
    public static bool InReach(Star star, int universeX, int universeY) =>
        Math.Abs(star.X - universeX) <= ReachX && Math.Abs(star.Y - universeY) <= ReachY;

    /// <summary>
    /// Call once per hyperspace frame, after the ship has moved, with where
    /// its sprite was before the move (<see cref="SpritePosition"/>; the
    /// sprite is taken as still if not given). Returns the star the ship
    /// enters, or null. A ship only enters a star while moving or steering;
    /// a ship at rest on a star stays in hyperspace. The star just left
    /// can't be entered until the ship has cleared its image once.
    /// </summary>
    public static Star? Check(HyperFlight ship, StarMap map, ContactShapes shapes, (long X, long Y)? before = null)
    {
        var now = SpritePosition(ship);
        var was = before ?? now;
        IntersectFrame shipFrame = shapes.HyperFlagship[ship.Facing];

        // The ship's element, in the logical space around it (its place in its
        // view is ViewX), moving from where it was to where it is.
        IntersectControl Ship() => new(shipFrame, Display(was.X - now.X + ship.ViewX), Display(was.Y - now.Y + ship.ViewY))
        {
            EndX = Display(ship.ViewX),
            EndY = Display(ship.ViewY),
        };

        bool Touches(Star s, IntersectFrame frame) => Intersection.DrawablesIntersect(Ship(),
            new IntersectControl(frame,
                Display(Units.UniverseToLogX(s.X) + (Units.LogSpaceWidth >> 1) - (long)ship.LogX),
                Display(Units.UniverseToLogY(s.Y) + (Units.LogSpaceHeight >> 1) - (long)ship.LogY)),
            Intersection.MaxTimeValue) != 0;

        // hyper_death: the star just left stays off limits only while the
        // ship still touches its image. The first frame it doesn't, the
        // star is forgotten, so StarJustLeft always means "still on it".
        if (ship.StarJustLeft is int left)
        {
            if ((uint)left >= (uint)map.Stars.Count)
                ship.StarJustLeft = null;   // not a star of this map
            else
            {
                Star s = map.Stars[left];
                if (!Touches(s, shapes.HyperStars[s.HyperspaceFrame]))
                    ship.StarJustLeft = null;
            }
        }

        var (dx, dy) = ship.Velocity.Current();
        bool active = dx != 0 || dy != 0
            || (ship.Status & (HyperFlight.StatusLeft | HyperFlight.StatusRight | HyperFlight.StatusThrust
                               | HyperFlight.StatusWeapon | HyperFlight.StatusSpecial)) != 0;
        if (!active)
            return null;

        // The stars AddStars puts near the ship; if two are touched at once
        // (none are close enough in 0.8.0's map), the nearest.
        Star? best = null;
        long bestDist = long.MaxValue;
        int ux = ship.UniverseX, uy = ship.UniverseY;
        foreach (Star s in map.InYRange(uy - ReachY, uy + ReachY))
        {
            if (s.Number == ship.StarJustLeft || !InReach(s, ux, uy) || !Touches(s, shapes.HyperPoint))
                continue;
            long ex = Units.UniverseToLogX(s.X) - now.X, ey = Units.UniverseToLogY(s.Y) - now.Y;
            long d = ex * ex + ey * ey;
            if (d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>WORLD_TO_DISPLAY of a logical coordinate.</summary>
    private static short Display(long logical) => checked((short)(logical >> Units.OneShift));

    /// <summary>
    /// Where the ship's sprite is, in hyperspace logical units: its
    /// hyperspace position plus how far it is from the middle of the view.
    /// At high speed the view's scroll (at most ORG_JUMP a frame) lags the
    /// ship, and UQM tests the sprite where it is drawn.
    /// </summary>
    public static (long X, long Y) SpritePosition(HyperFlight ship) =>
        (ship.LogX + (long)(ship.ViewX - HyperFlight.CenterX),
         ship.LogY + (long)(ship.ViewY - HyperFlight.CenterY));
}
