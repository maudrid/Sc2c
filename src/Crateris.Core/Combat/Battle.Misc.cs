// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/misc.c), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

// misc.c: the planet, asteroids, damage and floating crew.
public sealed partial class Battle
{
    private const int CrewLife = 300;
    private static readonly int CrewColorLowIntensity = Rgb15(0x00, 0x14, 0x00);
    private static readonly int CrewColorHighIntensity = Rgb15(0x0A, 0x1E, 0x0A);

    // spawn_planet: somewhere nothing else is.
    private void SpawnPlanet()
    {
        var planet = List.Alloc();
        if (planet == null)
            return;
        planet.PlayerNr = NeutralPlayerNum;
        planet.HitPoints = 200;
        planet.StateFlags = ElementFlags.Appearing;
        planet.LifeSpan = NormalLife + 1;
        planet.Prim = PrimType.Stamp;
        planet.Current.FrameArray = Arena.Planet;
        planet.Current.Frame = Arena.Planet[0];
        planet.CollisionFunc = Collision;
        planet.PostprocessFunc = e => CalculateGravity(e);
        planet.Velocity.Zero();
        do
        {
            planet.Current.X = RandomX("spawn_planet");
            planet.Current.Y = RandomY("spawn_planet");
        } while (CalculateGravity(planet) || TimeSpaceMatterConflict(planet));
        planet.MassPoints = (byte)planet.HitPoints;
        List.Put(planet);
    }

    // spawn_rubble: an asteroid breaking up; when it's done, a new asteroid.
    private void SpawnRubble(Element asteroid)
    {
        var rubble = List.Alloc();
        if (rubble == null)
            return;
        List.Put(rubble);
        rubble.PlayerNr = asteroid.PlayerNr;
        rubble.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
        rubble.LifeSpan = 5;
        rubble.TurnWait = rubble.NextTurn = 0;
        rubble.Prim = PrimType.Stamp;
        rubble.Current.FrameArray = Arena.Asteroid;
        rubble.Current.Frame = Arena.Asteroid[0].SetAbs(UqmMath.AngleToFacing(UqmMath.FullCircle));
        rubble.Current.X = asteroid.Current.X;
        rubble.Current.Y = asteroid.Current.Y;
        rubble.PreprocessFunc = AnimationPreprocess;
        rubble.DeathFunc = SpawnAsteroid;
    }

    // asteroid_preprocess: its spin.
    private static void AsteroidPreprocess(Element e)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            int frameIndex = e.Current.Frame.Index;
            if ((e.ThrustWait & (1 << 7)) != 0)
                --frameIndex;
            else
                ++frameIndex;
            e.Next.Frame = e.Current.Frame.SetAbs(UqmMath.NormalizeFacing(frameIndex));
            e.StateFlags |= ElementFlags.Changing;
            e.TurnWait = (byte)(e.ThrustWait & ((1 << 7) - 1));
        }
    }

    // spawn_asteroid: on the seam (x or y 0), drifting and spinning. When
    // the list is full, the rubble it comes from stays (unseen) and tries again.
    private void SpawnAsteroid(Element? e)
    {
        var asteroid = List.Alloc();
        if (asteroid == null)
        {
            if (e != null)
            {
                e.StateFlags &= ~ElementFlags.Disappearing;
                e.Prim = PrimType.None;
                e.LifeSpan = 1;
            }
            return;
        }

        asteroid.PlayerNr = NeutralPlayerNum;
        asteroid.HitPoints = 1;
        asteroid.MassPoints = 3;
        asteroid.StateFlags = ElementFlags.Appearing;
        asteroid.LifeSpan = NormalLife;
        asteroid.Prim = PrimType.Stamp;
        ushort val = (ushort)Random.Next("spawn_asteroid");
        if ((val & (1 << 0)) != 0)
        {
            asteroid.Current.X = (short)((val & (1 << 1)) == 0 ? 0 : LogSpaceWidth);
            asteroid.Current.Y = RandomY("spawn_asteroid");
        }
        else
        {
            asteroid.Current.X = RandomX("spawn_asteroid");
            asteroid.Current.Y = (short)((val & (1 << 1)) == 0 ? 0 : LogSpaceHeight);
        }

        short magnitude = DisplayToWorld(((short)Random.Next("spawn_asteroid") & 7) + 4);
        ushort facing = (ushort)Random.Next("spawn_asteroid");
        asteroid.Velocity.SetVector(magnitude, facing);
        asteroid.Current.FrameArray = Arena.Asteroid;
        asteroid.Current.Frame = Arena.Asteroid[0].SetAbs(UqmMath.NormalizeFacing((int)Random.Next("spawn_asteroid")));
        asteroid.TurnWait = asteroid.ThrustWait = (byte)((byte)Random.Next("spawn_asteroid") & ((1 << 2) - 1));
        asteroid.ThrustWait |= (byte)((byte)Random.Next("spawn_asteroid") & (1 << 7));
        asteroid.PreprocessFunc = AsteroidPreprocess;
        asteroid.DeathFunc = SpawnRubble;
        asteroid.CollisionFunc = Collision;
        List.Put(asteroid);
    }

    /// <summary>do_damage: a ship loses crew (dying at none); anything else hit points; a planet nothing.</summary>
    public void DoDamage(Element e, short damage)
    {
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            if (!DeltaCrew(e, (short)-damage))
            {
                e.LifeSpan = 0;
                e.StateFlags |= ElementFlags.NonSolid;
            }
        }
        else if (!GravityMass(e.MassPoints))
        {
            if ((byte)damage < e.HitPoints)
                e.HitPoints -= (byte)damage;
            else
            {
                e.HitPoints = 0;
                e.LifeSpan = 0;
                e.StateFlags |= ElementFlags.NonSolid;
            }
        }
    }

    // crew_preprocess: crew blink and drift towards their ship, or the nearest.
    private void CrewPreprocess(Element e)
    {
        e.PrimColor = e.PrimColor == CrewColorLowIntensity ? CrewColorHighIntensity : CrewColorLowIntensity;
        e.StateFlags |= ElementFlags.Changing;

        var target = e.Target;
        if (target == null)
        {
            var starShip = e.Parent;
            if (starShip != null && starShip.RaceDesc!.CrewLevel != 0)
                e.Target = starShip.Ship;
            else
            {
                ushort facing = 0;
                TrackShip(e, ref facing);
            }
        }

        if (target != null)
        {
            const int crewDelta = ScaledOne;
            short delta = (short)(target.Current.X - e.Current.X);
            delta = WrapDeltaX(delta);
            if (delta > 0)
                e.Next.X += crewDelta;
            else if (delta < 0)
                e.Next.X -= crewDelta;

            delta = (short)(target.Current.Y - e.Current.Y);
            delta = WrapDeltaY(delta);
            if (delta > 0)
                e.Next.Y += crewDelta;
            else if (delta < 0)
                e.Next.Y -= crewDelta;
        }
    }

    // crew_collision: a ship picks up the crew (unless it's crew-immune).
    private void CrewCollision(Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        if ((e1.StateFlags & ElementFlags.PlayerShip) != 0 && e1.LifeSpan >= NormalLife && e0.HitPoints > 0)
        {
            if ((e1.Parent!.RaceDesc!.ShipFlags & ShipFlags.CrewImmune) == 0)
                DeltaCrew(e1, 1);
        }
        e0.HitPoints = 0;
        e0.LifeSpan = 0;
        e0.StateFlags |= ElementFlags.Collision | ElementFlags.Disappearing | ElementFlags.NonSolid;
    }

    /// <summary>
    /// AbandonShip: <paramref name="crewLoss"/> of the ship's crew float
    /// away behind it, each heading for <paramref name="target"/>'s ship
    /// (the Syreen's song).
    /// </summary>
    public void AbandonShip(Element ship, Element target, int crewLoss)
    {
        var starShip = ship.Parent!;
        if ((starShip.RaceDesc!.ShipFlags & ShipFlags.CrewImmune) != 0)
            return;

        var shipIntersect = ship.Intersect.Copy();
        var r = shipIntersect.Frame;   // GetFrameRect: its size and hot spot
        short dx, dy;
        ushort direction = ship.Velocity.TravelAngle;
        if (direction == UqmMath.FullCircle)
            dx = dy = 0;
        else
        {
            const int moreThanEnough = 100;
            direction += UqmMath.HalfCircle;
            dx = Cosine(direction, moreThanEnough);
            dy = Sine(direction, moreThanEnough);
        }

        ushort loss = (ushort)crewLoss;
        Element? crew;
        while (loss-- != 0 && (crew = List.Alloc()) != null)
        {
            DeltaCrew(ship, -1);
            List.Put(crew);
            crew.PlayerNr = NeutralPlayerNum;
            crew.HitPoints = 1;
            crew.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.CrewObject;
            crew.LifeSpan = CrewLife;
            crew.Prim = PrimType.Point;
            crew.PrimColor = CrewColorLowIntensity;
            crew.Current.Frame = Arena.StarsInSpace.Dec();
            crew.Current.FrameArray = Arena.Stars;
            crew.PreprocessFunc = CrewPreprocess;
            crew.CollisionFunc = CrewCollision;
            crew.Parent = starShip;
            // UQM's starShip now becomes the target's, so the crew after the first belong to it.
            starShip = target.Parent!;
            crew.Target = starShip.Ship;

            shipIntersect.OriginX = ship.Intersect.EndX;
            shipIntersect.OriginY = ship.Intersect.EndY;
            short w = (short)((ushort)Random.Next("AbandonShip") % r.Width);
            short h = (short)((ushort)Random.Next("AbandonShip") % r.Height);
            var crewIntersect = new IntersectControl
            {
                EndX = shipIntersect.EndX, EndY = shipIntersect.EndY, Frame = crew.Current.Frame.Shape,
            };
            if (dx == 0 && dy == 0)
            {
                crewIntersect.EndX += (short)(w - (r.Width >> 1));
                crewIntersect.EndY += (short)(h - (r.Height >> 1));
                crewIntersect.OriginX = target.Intersect.EndX;
                crewIntersect.OriginY = target.Intersect.EndY;
            }
            else
            {
                if (dx == 0)
                    crewIntersect.EndX += (short)(w - (r.Width >> 1));
                else if (dx > 0)
                    crewIntersect.EndX += w;
                else
                    crewIntersect.EndX -= w;
                if (dy == 0)
                    crewIntersect.EndY += (short)(h - (r.Height >> 1));
                else if (dy > 0)
                    crewIntersect.EndY += h;
                else
                    crewIntersect.EndY -= h;
                crewIntersect.OriginX = (short)(crewIntersect.EndX + dx);
                crewIntersect.OriginY = (short)(crewIntersect.EndY + dy);
            }

            Intersection.DrawablesIntersect(crewIntersect, shipIntersect, MaxTimeValue);
            crew.Current.X = DisplayToWorld(crewIntersect.EndX);
            crew.Current.Y = DisplayToWorld(crewIntersect.EndY);
        }
    }
}
