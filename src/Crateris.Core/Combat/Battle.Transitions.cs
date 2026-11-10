// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/tactrans.c), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

// tactrans.c: ships dying, warping in and out, ion trails.
public sealed partial class Battle
{
    /// <summary>MIN_DITTY_FRAME_COUNT: (ONE_SECOND * 3) / BATTLE_FRAME_RATE.</summary>
    private const int MinDittyFrameCount = 840 * 3 / (840 / 24);
    private const int IonLife = 1;
    private const int TransitionLife = 1;
    private static readonly short TransitionSpeed = DisplayToWorld(40);
    private static readonly int StartIonColor = Rgb15(0x1F, 0x15, 0x00);

    private static readonly int[] IonColors =
    {
        Rgb15(0x1F, 0x15, 0x00), Rgb15(0x1F, 0x11, 0x00), Rgb15(0x1F, 0x0E, 0x00), Rgb15(0x1F, 0x0A, 0x00),
        Rgb15(0x1F, 0x07, 0x00), Rgb15(0x1F, 0x03, 0x00), Rgb15(0x1F, 0x00, 0x00), Rgb15(0x1B, 0x00, 0x00),
        Rgb15(0x17, 0x00, 0x00), Rgb15(0x13, 0x00, 0x00), Rgb15(0x0F, 0x00, 0x00), Rgb15(0x0B, 0x00, 0x00),
    };

    private static readonly int[] FleeColors =
    {
        Rgb15(0x0A, 0x00, 0x00), Rgb15(0x0E, 0x00, 0x00), Rgb15(0x13, 0x00, 0x00), Rgb15(0x17, 0x00, 0x00),
        Rgb15(0x1B, 0x00, 0x00), Rgb15(0x1F, 0x00, 0x00), Rgb15(0x1F, 0x04, 0x04), Rgb15(0x1F, 0x0A, 0x0A),
        Rgb15(0x1F, 0x0F, 0x0F), Rgb15(0x1F, 0x13, 0x13), Rgb15(0x1F, 0x19, 0x19), Rgb15(0x1F, 0x13, 0x13),
        Rgb15(0x1F, 0x0F, 0x0F), Rgb15(0x1F, 0x0A, 0x0A), Rgb15(0x1F, 0x04, 0x04), Rgb15(0x1F, 0x00, 0x00),
        Rgb15(0x1B, 0x00, 0x00), Rgb15(0x17, 0x00, 0x00), Rgb15(0x13, 0x00, 0x00), Rgb15(0x0E, 0x00, 0x00),
    };

    // winnerStarShip: the ship that won, which picks last.
    private Starship? _winner;

    // preprocess_dead_ship: only stops the sounds.
    private static void PreprocessDeadShip(Element e)
    {
    }

    // cleanup_dead_ship: the explosion is over (or the ship has warped
    // out): everything the ship owned goes but its floating crew, and its
    // element waits a frame (longer for a winner's ditty) to pick the next ship.
    private void CleanupDeadShip(Element deadShip)
    {
        var dead = deadShip.Parent!;
        dead.CrewLevel = dead.RaceDesc!.CrewLevel;   // the crew left after the battle

        bool musicStarted = false;
        for (var e = List.Head; e != null; e = e.Succ)
        {
            var starShip = e.Parent;
            if (starShip == dead)
            {
                e.Parent = null;
                if ((e.StateFlags & ElementFlags.CrewObject) == 0 || e.PreprocessFunc != (ElementProcess)CrewPreprocess)
                {
                    e.Prim = PrimType.None;
                    e.LifeSpan = 0;
                    e.StateFlags = ElementFlags.NonSolid | ElementFlags.Disappearing | ElementFlags.FiniteLife;
                    e.PreprocessFunc = e.PostprocessFunc = e.DeathFunc = null;
                    e.CollisionFunc = null;
                }
            }
            if (starShip != null && (starShip.CurStatusFlags & StatusFlags.PlayVictoryDitty) != 0)
            {
                // The ship left: its victory ditty plays (PlayDitty).
                musicStarted = true;
                starShip.CurStatusFlags &= ~StatusFlags.PlayVictoryDitty;
            }
        }

        deadShip.LifeSpan = (ushort)(musicStarted ? MinDittyFrameCount : 1);
        if (dead == _winner)
            deadShip.LifeSpan = MinDittyFrameCount + 1;   // dead but won: it picks last
        deadShip.DeathFunc = NewShip;
        deadShip.PreprocessFunc = PreprocessDeadShip;
        deadShip.StateFlags &= ~ElementFlags.Disappearing;
        ++deadShip.LifeSpan;
        deadShip.Parent = dead;
    }

    // new_ship: the dead ship's side picks its next ship, or the battle is
    // over. UQM first waits for the victory ditty to end (readyForBattleEnd);
    // with no music here, it never waits.
    private void NewShip(Element deadShip)
    {
        var dead = deadShip.Parent!;
        _winner = null;
        deadShip.Parent = null;
        dead.RaceDesc!.Uninit?.Invoke(dead.RaceDesc);   // free_ship
        dead.RaceDesc = null;
        // UpdateShipFragCrew is the world's; the ship can't be picked again.
        dead.Available = false;
        if (GetNextStarShip(dead, dead.PlayerNr))
        {
        }
        else if (BattleCounter[0] == 0 || BattleCounter[1] == 0)
            InBattle = false;   // a side is out of ships
    }

    // explosion_preprocess: the ship blowing up, a few explosions a frame.
    private void ExplosionPreprocess(Element ship)
    {
        byte i = (byte)(NumExplosionFrames * 3 - ship.LifeSpan);
        switch (i)
        {
            case 25:
                ship.PreprocessFunc = null;
                i = 1;
                break;
            case 0:
            case 1:
            case 2:
            case 20:
            case 21:
            case 22:
            case 23:
            case 24:
                i = 1;
                break;
            case 3:
            case 4:
            case 5:
            case 18:
            case 19:
                i = 2;
                break;
            case 15:
                ship.Prim = PrimType.None;
                ship.StateFlags |= ElementFlags.Changing;
                i = 3;
                break;
            default:
                i = 3;
                break;
        }

        do
        {
            var e = List.Alloc();
            if (e == null)
                continue;
            List.Put(e);
            e.PlayerNr = NeutralPlayerNum;
            e.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
            e.LifeSpan = 9;
            e.Prim = PrimType.Stamp;
            e.Current.FrameArray = Arena.Explosion;
            e.Current.Frame = Arena.Explosion[0];
            uint randVal = Random.Next("explosion_preprocess");
            int angle = (byte)(randVal >> 16);
            short dist = DisplayToWorld((byte)randVal % 8);
            if ((byte)(randVal >> 8) < 256 * 1 / 3)
                dist += DisplayToWorld(8);
            e.Current.X = (short)(ship.Current.X + Cosine(angle, dist));
            e.Current.Y = (short)(ship.Current.Y + Sine(angle, dist));
            e.PreprocessFunc = AnimationPreprocess;
            randVal = Random.Next("explosion_preprocess");
            angle = (byte)randVal;
            dist = (short)Velocity.WorldToVelocity(DisplayToWorld((byte)(randVal >> 8) % 5));
            e.Velocity.SetComponents(Cosine(angle, dist), Sine(angle, dist));
        } while (--i != 0);
    }

    // FindAliveStarShip: the other ship, if it's alive and not running away.
    private Starship? FindAliveStarShip(Element deadShip)
    {
        for (var e = List.Head; e != null; e = e.Succ)
        {
            if ((e.StateFlags & ElementFlags.PlayerShip) != 0 && e != deadShip && e.MassPoints <= MaxShipMass + 1)
            {
                var alive = e.Parent!;
                if (alive.RaceDesc!.CrewLevel == 0 && e.MassPoints != MaxShipMass + 1)
                    alive = null;   // dead too (a reincarnating Pkunk isn't)
                return alive;
            }
        }
        return null;
    }

    private void SetWinnerStarShip(Starship? winner)
    {
        if (winner == null)
            return;
        winner.CurStatusFlags |= StatusFlags.PlayVictoryDitty;
        _winner ??= winner;   // the first, if both die
    }

    private void RecordShipDeath(Element deadShip)
    {
        var dead = deadShip.Parent!;
        if (deadShip.MassPoints <= MaxShipMass)
            BattleCounter[dead.PlayerNr]--;   // a ship running away was counted when it ran
    }

    // StartShipExplosion: still, without energy, blowing up for 36 frames.
    private void StartShipExplosion(Element ship)
    {
        var starShip = ship.Parent!;
        ship.Velocity.Zero();
        DeltaEnergy(ship, (short)-starShip.RaceDesc!.EnergyLevel);
        ship.LifeSpan = NumExplosionFrames * 3;
        ship.StateFlags &= ~ElementFlags.Disappearing;
        ship.StateFlags |= ElementFlags.FiniteLife | ElementFlags.NonSolid;
        ship.PreprocessFunc = ExplosionPreprocess;
        ship.PostprocessFunc = PostProcessStatus;
        ship.DeathFunc = CleanupDeadShip;
        ship.Target = null;
    }

    // ship_death: a ship without crew explodes; the other, if alive, has won.
    private void ShipDeath(Element ship)
    {
        var starShip = ship.Parent!;
        starShip.CurStatusFlags &= ~StatusFlags.PlayVictoryDitty;
        StartShipExplosion(ship);
        SetWinnerStarShip(FindAliveStarShip(ship));
        RecordShipDeath(ship);
    }

    // cycle_ion_trail: an ion trail's dot or a warp shadow fades through
    // its colours, a frame each, then goes.
    private static void CycleIonTrail(Element e)
    {
        e.ColorCycleIndex++;
        if (e.ColorCycleIndex != IonColors.Length)
        {
            e.LifeSpan = e.ThrustWait;
            e.PrimColor = IonColors[e.ColorCycleIndex];
            e.StateFlags &= ~ElementFlags.Disappearing;
            e.StateFlags |= ElementFlags.Changing;
        }
    }

    // spawn_ion_trail: a dot behind a thrusting ship, at the head of the list.
    private void SpawnIonTrail(Element ship)
    {
        var ion = List.Alloc();
        if (ion == null)
            return;
        var starShip = ship.Parent!;
        int angle = UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle;
        var frame0 = starShip.RaceDesc!.Ship![0].Shape;
        short height = DisplayToWorld(frame0.Height + -frame0.HotSpotY);

        List.Insert(ion, List.Head);
        ion.PlayerNr = NeutralPlayerNum;
        ion.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
        ion.ThrustWait = IonLife;
        ion.LifeSpan = ion.ThrustWait;
        ion.Prim = PrimType.Point;
        ion.PrimColor = StartIonColor;
        ion.ColorCycleIndex = 0;
        ion.Current.Frame = Arena.StarsInSpace.Dec();
        ion.Current.FrameArray = Arena.Stars;
        ion.Current.X = (short)(ship.Current.X + Cosine(angle, height));
        ion.Current.Y = (short)(ship.Current.Y + Sine(angle, height));
        ion.DeathFunc = CycleIonTrail;
        ion.Parent = starShip;

        // Normally done before moving, but at the head it may never be.
        ion.Next = ion.Current;
        --ion.LifeSpan;
        ion.StateFlags |= ElementFlags.PreProcess;
    }

    // ship_transition: a ship warping in (or out) over 15 frames, its
    // shadows streaking in from behind.
    private void ShipTransition(Element e)
    {
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            if ((e.StateFlags & ElementFlags.Appearing) != 0)
            {
                e.LifeSpan = HyperjumpLife;
                e.PreprocessFunc = ShipTransition;
                e.PostprocessFunc = null;
                e.Prim = PrimType.None;
                e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife | ElementFlags.Changing;
            }
            else if (e.LifeSpan < HyperjumpLife)
            {
                if (e.LifeSpan == NormalLife && e.CrewLevel != 0)
                {
                    e.Current.Frame = e.Next.Frame = Frame.SetEqu(e.Current.FrameArray![0], e.Current.Frame);
                    e.Prim = PrimType.Stamp;
                    InitIntersectStartPoint(e);
                    InitIntersectEndPoint(e);
                    InitIntersectFrame(e);
                    e.Velocity.Zero();
                    e.StateFlags &= ~(ElementFlags.NonSolid | ElementFlags.FiniteLife);
                    e.StateFlags |= ElementFlags.Changing;
                    e.PreprocessFunc = ShipPreprocess;
                    e.PostprocessFunc = ShipPostprocess;
                }
                return;
            }
        }

        var starShip = e.Parent!;
        var shipImage = starShip.Ship!;
        if ((shipImage.StateFlags & ElementFlags.NonSolid) == 0)
        {
            e.PreprocessFunc = null;
            return;
        }
        var image = List.Alloc();
        if (image == null)
            return;
        List.Put(image);
        int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
        image.PlayerNr = NeutralPlayerNum;
        image.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
        image.ThrustWait = TransitionLife;
        image.LifeSpan = image.ThrustWait;
        image.Prim = PrimType.StampFill;
        image.PrimColor = StartIonColor;
        image.ColorCycleIndex = 0;
        image.Current.Frame = e.Current.Frame;
        image.Current.FrameArray = e.Current.FrameArray;
        image.Current.X = e.Current.X;
        image.Current.Y = e.Current.Y;
        if ((e.StateFlags & ElementFlags.PlayerShip) == 0)
        {
            image.Current.X += Cosine(angle, TransitionSpeed);
            image.Current.Y += Sine(angle, TransitionSpeed);
            e.PreprocessFunc = null;
        }
        else if (e.CrewLevel != 0)
        {
            image.Current.X -= (short)(Cosine(angle, TransitionSpeed) * (e.LifeSpan - 1));
            image.Current.Y -= (short)(Sine(angle, TransitionSpeed) * (e.LifeSpan - 1));
            image.Current.X = (short)WrapX(image.Current.X);
            image.Current.Y = (short)WrapY(image.Current.Y);
        }
        image.PreprocessFunc = ShipTransition;
        image.DeathFunc = CycleIonTrail;
        image.Parent = starShip;
    }

    // flee_preprocess: the retreating ship flashes red ever faster, then warps out.
    private void FleePreprocess(Element e)
    {
        if (--e.TurnWait == 0)
        {
            e.ColorCycleIndex++;
            if (e.ColorCycleIndex == FleeColors.Length)
                e.ColorCycleIndex = 0;
            e.PrimColor = FleeColors[e.ColorCycleIndex];
            if (e.ColorCycleIndex == 0)
                --e.ThrustWait;

            e.TurnWait = e.ThrustWait;
            if (e.TurnWait != 0)
                e.TurnWait = (byte)(((e.TurnWait - 1) >> 1) + 1);
            else if (e.ColorCycleIndex != FleeColors.Length / 2)
                e.TurnWait = 1;
            else
            {
                e.DeathFunc = CleanupDeadShip;
                e.CrewLevel = 0;
                e.LifeSpan = HyperjumpLife + 1;
                e.PreprocessFunc = ShipTransition;
                e.PostprocessFunc = null;
                e.Prim = PrimType.None;
                e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife | ElementFlags.Changing;
            }
        }
        e.Parent!.CurStatusFlags &= ~StatusFlags.Keys;   // no keys while fleeing
    }
}
