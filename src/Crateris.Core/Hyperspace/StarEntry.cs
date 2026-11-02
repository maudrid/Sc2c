// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (hyper.c init_transition and
// unhyper_transition), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

/// <summary>
/// The flagship's entry into a star, as UQM plays it before the system
/// loads (init_transition, then unhyper_transition each frame): the ship
/// turns clockwise to facing 0, then plays the frames of its hyperspace
/// sheet after the 16 facings, while drifting onto the star at a steady
/// velocity that brings it there in that many frames. The server puts the
/// ship in the system at once (docs/differences.md row 24); clients play
/// this where the ship was.
/// </summary>
public static class StarEntry
{
    public const int Facings = 1 << UqmMath.FacingShift;   // ANGLE_TO_FACING (FULL_CIRCLE)

    /// <summary>One frame of the entry: the sheet's frame, and where the sprite is in hyperspace's logical units.</summary>
    public readonly record struct Step(int Frame, long X, long Y);

    /// <summary>
    /// The frames after contact, one a hyperspace frame, until the system
    /// loads (the frame after the last). <paramref name="frameCount"/> is
    /// the hyperspace flagship sheet's frame count (34 in 0.8.0).
    /// </summary>
    public static IReadOnlyList<Step> Animate(long spriteX, long spriteY, int facing, Star star, int frameCount)
    {
        if (frameCount <= Facings)
            throw new ArgumentException("the hyperspace flagship has no frames after its facings", nameof(frameCount));
        // init_transition: the velocity that covers the distance in num_turns frames.
        short dx = (short)Velocity.WorldToVelocity((int)(Units.UniverseToLogX(star.X) - spriteX));
        short dy = (short)Velocity.WorldToVelocity((int)(Units.UniverseToLogY(star.Y) - spriteY));
        // At least 1 (frameCount > Facings), so UQM's "if (num_turns == 0)" can't happen.
        int turns = frameCount - Facings + UqmMath.NormalizeFacing(Facings - facing);
        var velocity = default(Velocity);
        velocity.SetComponents((short)(dx / turns), (short)(dy / turns));

        var steps = new List<Step>();
        int frame = facing;
        long x = spriteX, y = spriteY;
        while (true)
        {
            // unhyper_transition, then the move (PreProcess).
            if (frame == 0)
                frame += Facings;
            else if (frame < Facings)
                frame = UqmMath.NormalizeFacing(frame + 1);
            else if (++frame == frameCount)
                return steps;
            var (mx, my) = velocity.Next(1);
            x += mx;
            y += my;
            steps.Add(new Step(frame, x, y));
        }
    }
}
