// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/pkunk/pkunk.c: init_pkunk, animate,
// initialize_bug_missile, pkunk_intelligence, new_pkunk,
// pkunk_reincarnation_death, intercept_pkunk_death, spawn_phoenix_trail,
// phoenix_transition, pkunk_preprocess, pkunk_postprocess), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// pkunk.c: the Pkunk Fury. Its weapon fires three bugs, ahead and to
/// either side, carried along by the ship's own speed; its special is an
/// insult (a sound, drawn at random but never the last one twice), which
/// gives it energy. Half the time it warps in (a random draw), a phoenix
/// watches over it: when it dies it blows up as any ship, then is reborn
/// with full crew and energy at a random place across the whole arena
/// (stage 4b's rule for ships that place themselves at random), facing a
/// random way, its four phoenix images flying in.
/// <para>
/// UQM keeps the last insult in a static that <c>init_pkunk</c> sets back
/// to 0 whenever a Pkunk ship is made (each one warping in, not a
/// rebirth); the port keeps it for the battle (<see cref="Battle.ShipState{T}"/>)
/// and sets it back as <see cref="Load"/> makes a Pkunk's description.
/// </para>
/// <para>
/// Its AI insults whenever it isn't at full energy (with its special
/// ready, or now and then); at UQM's standard rating it takes away the phoenix.
/// </para>
/// </summary>
public sealed class PkunkShip : IShipCode
{
    public const string Id = "pkunk";

    /// <summary>
    /// GetSoundCount of ship.pkunk.sounds: the content's (its fury.snd
    /// lists 14 sounds), whose first two (its gun and the rebirth) aren't insults.
    /// </summary>
    public const int SoundCount = 14;

    private static readonly int StartPhoenixColor = Battle.Rgb15(0x1F, 0x15, 0x00);

    private static readonly int[] PhoenixTrailColors =
    {
        Battle.Rgb15(0x1F, 0x15, 0x00), Battle.Rgb15(0x1F, 0x11, 0x00), Battle.Rgb15(0x1F, 0x0E, 0x00),
        Battle.Rgb15(0x1F, 0x0A, 0x00), Battle.Rgb15(0x1F, 0x07, 0x00), Battle.Rgb15(0x1F, 0x03, 0x00),
        Battle.Rgb15(0x1F, 0x00, 0x00), Battle.Rgb15(0x1B, 0x00, 0x00), Battle.Rgb15(0x17, 0x00, 0x00),
        Battle.Rgb15(0x13, 0x00, 0x00), Battle.Rgb15(0x0F, 0x00, 0x00), Battle.Rgb15(0x0B, 0x00, 0x00),
    };

    private readonly ShipDescription _description;
    private readonly Views? _hull, _bug;

    /// <param name="description">The Pkunk's description (data/ships.json's pkunk).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="bug">Its bugs' sheets (ship_data.weapon).</param>
    public PkunkShip(ShipDescription description, Views? hull, Views? bug)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _bug = bug;
    }

    /// <summary>pkunk.c's static LastSound, for the battle.</summary>
    internal sealed class Insults
    {
        public int LastSound;
    }

    // pkunk.c's #defines, and PKUNK_DATA: the phoenix watching over the
    // ship and the ship's element callbacks saved as it dies.
    private sealed class Pkunk
    {
        public required RaceDesc Rd;
        public required ShipDescription D;
        public Element? Phoenix;
        public ElementProcess? SavedPreprocess, SavedPostprocess, SavedDeath;
    }

    /// <summary>init_pkunk: pkunk_desc with its sheets and pkunk.c's battle code; the last insult forgotten.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _bug, null);
        var p = new Pkunk { Rd = rd, D = d };
        var bug = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("PKUNK_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            PreprocessFunc = Battle.AnimationPreprocess, BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        rd.InitWeapon = e => InitializeBugMissile(battle, e, bug);
        rd.Preprocess = e => Preprocess(battle, e, p);
        rd.Postprocess = e => Postprocess(battle, e, p);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, p);
        battle.ShipState<Insults>().LastSound = 0;
        return rd;
    }

    // initialize_bug_missile: three bugs, ahead, right and left, each
    // carried along by the ship's speed, animating.
    private static IReadOnlyList<Element?> InitializeBugMissile(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Sender = ship.PlayerNr;
        var bugs = new Element?[3];
        const int quadrantFacing = UqmMath.Facings / 4;
        for (int i = 0; i < 3; ++i)
        {
            int face = starShip.ShipFacing + quadrantFacing * i;
            if (i == 2)
                face += quadrantFacing;
            block.Face = (ushort)UqmMath.NormalizeFacing(face);
            var bug = bugs[i] = battle.InitializeMissile(block);
            if (bug != null)
            {
                var (dx, dy) = ship.Velocity.Current();
                bug.Velocity.Delta(dx, dy);
                bug.Current.X -= (short)Velocity.VelocityToWorld(dx);
                bug.Current.Y -= (short)Velocity.VelocityToWorld(dy);
            }
        }
        return bugs;
    }

    // pkunk_intelligence: at UQM's standard rating, no phoenix; the insult
    // when short of energy, with the special ready or one time in 13; then
    // the stock AI.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Pkunk p)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        if (p.Phoenix != null && starShip.Rating == CyborgRating.Standard)
        {
            battle.List.Remove(p.Phoenix);
            battle.List.Free(p.Phoenix);
            p.Phoenix = null;
        }

        if (rd.EnergyLevel < rd.MaxEnergy
            && (starShip.SpecialCounter == 0 || (byte)battle.Random.Next("pkunk_intelligence") < 20))
            starShip.ShipInputState |= StatusFlags.Special;
        else
            starShip.ShipInputState &= ~StatusFlags.Special;
        battle.ShipIntelligence(ship, concerns, concernCounter);
    }

    // new_pkunk: the explosion over, the ship is reborn: its callbacks put
    // back, full crew and energy, its characteristics as built (a VUX's
    // limpets shaken off), its keys and counters cleared (a victory ditty
    // kept: a Pkunk reborn wins a battle both sides lose), facing a random
    // way at a random place across the arena clear of the planet and
    // everything else. Its target set tells its first step it's a rebirth.
    private static void NewPkunk(Battle battle, Element e, Pkunk p)
    {
        var starShip = e.Parent!;
        var rd = p.Rd;
        var d = p.D;

        e.StateFlags = ElementFlags.Appearing | ElementFlags.PlayerShip | ElementFlags.IgnoreSimilar;
        e.MassPoints = (byte)d.Define("SHIP_MASS");
        e.PreprocessFunc = p.SavedPreprocess;
        e.PostprocessFunc = p.SavedPostprocess;
        e.DeathFunc = p.SavedDeath;
        rd.Preprocess = x => Preprocess(battle, x, p);
        rd.CrewLevel = (ushort)d.Define("MAX_CREW");
        rd.EnergyLevel = (byte)d.Define("MAX_ENERGY");
        // fix vux impairment
        rd.Characteristics.MaxThrust = (ushort)d.Define("MAX_THRUST");
        rd.Characteristics.ThrustIncrement = (ushort)d.Define("THRUST_INCREMENT");
        rd.Characteristics.TurnWait = (byte)d.Define("TURN_WAIT");
        rd.Characteristics.ThrustWait = (byte)d.Define("THRUST_WAIT");
        rd.Characteristics.SpecialWait = 0;

        starShip.ShipInputState = StatusFlags.None;
        starShip.CurStatusFlags &= StatusFlags.PlayVictoryDitty;
        starShip.OldStatusFlags = StatusFlags.None;
        starShip.EnergyCounter = 0;
        starShip.WeaponCounter = 0;
        starShip.SpecialCounter = 0;
        e.CrewLevel = 0;
        e.TurnWait = 0;
        e.ThrustWait = 0;
        e.LifeSpan = Battle.NormalLife;

        starShip.ShipFacing = (ushort)UqmMath.NormalizeFacing((int)battle.Random.Next("new_pkunk"));
        e.Current.FrameArray = rd.Ship;
        e.Current.Frame = rd.Ship![0].SetAbs(starShip.ShipFacing);
        e.Prim = PrimType.Stamp;

        do
        {
            e.Current.X = battle.RandomX("new_pkunk");
            e.Current.Y = battle.RandomY("new_pkunk");
        } while (battle.CalculateGravity(e) || battle.TimeSpaceMatterConflict(e));

        e.Target = starShip.Ship;
    }

    // pkunk_reincarnation_death: it blows up as a ship dying does, but
    // isn't counted dead and doesn't decide the winner; the explosion over,
    // it's reborn.
    private static void ReincarnationDeath(Battle battle, Element ship, Pkunk p)
    {
        battle.StartShipExplosion(ship);
        ship.DeathFunc = e => NewPkunk(battle, e, p);
    }

    // intercept_pkunk_death: the phoenix lives while the ship has crew;
    // when it has none (and isn't warping out), the ship is marked as
    // reborn for FindAliveStarShip, its callbacks saved, and its death
    // made a rebirth.
    private static void InterceptPkunkDeath(Battle battle, Element phoenix, Pkunk p)
    {
        var starShip = phoenix.Parent!;
        if (starShip.RaceDesc!.CrewLevel != 0)
        {
            phoenix.StateFlags &= ~ElementFlags.Disappearing;
            phoenix.LifeSpan = 1;
            return;
        }

        var ship = starShip.Ship!;
        if (!Battle.GravityMass(ship.MassPoints + 1))
        {
            ship.MassPoints = Battle.MaxShipMass + 1;
            p.SavedPreprocess = ship.PreprocessFunc;
            p.SavedPostprocess = ship.PostprocessFunc;
            p.SavedDeath = ship.DeathFunc;
            ship.DeathFunc = e => ReincarnationDeath(battle, e, p);
        }
    }

    // spawn_phoenix_trail: an image fades through the trail's colours, a frame each, then goes.
    private static void SpawnPhoenixTrail(Element e)
    {
        e.ColorCycleIndex++;
        if (e.ColorCycleIndex != PhoenixTrailColors.Length)
        {
            e.LifeSpan = 1;   // TRANSITION_LIFE
            e.PrimColor = PhoenixTrailColors[e.ColorCycleIndex];
            e.StateFlags &= ~ElementFlags.Disappearing;
            e.StateFlags |= ElementFlags.Changing;
        }
    }

    // phoenix_transition: while the reborn ship is non-solid, an image of
    // it flies in from where it'll be (each image then stepping on along
    // its own angle, kept in its mass).
    private static void PhoenixTransition(Battle battle, Element e)
    {
        var starShip = e.Parent!;
        var shipImage = starShip.Ship!;
        if ((shipImage.StateFlags & ElementFlags.NonSolid) == 0)
        {
            e.PreprocessFunc = null;
            return;
        }
        var image = battle.List.Alloc();
        if (image == null)
            return;
        battle.List.Put(image);
        image.PlayerNr = Battle.NeutralPlayerNum;
        image.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
        image.LifeSpan = 1;   // TRANSITION_LIFE
        image.Prim = PrimType.StampFill;
        image.PrimColor = StartPhoenixColor;
        image.ColorCycleIndex = 0;
        image.Current.Frame = e.Current.Frame;
        image.Current.FrameArray = e.Current.FrameArray;
        image.Current.X = e.Current.X;
        image.Current.Y = e.Current.Y;
        int angle;
        short transitionSpeed = Battle.DisplayToWorld(20);
        if ((e.StateFlags & ElementFlags.PlayerShip) == 0)
        {
            angle = e.MassPoints;
            image.Current.X += UqmMath.Cosine(angle, transitionSpeed);
            image.Current.Y += UqmMath.Sine(angle, transitionSpeed);
            e.PreprocessFunc = null;
        }
        else
        {
            angle = UqmMath.FacingToAngle(starShip.ShipFacing);
            image.Current.X -= (short)(UqmMath.Cosine(angle, transitionSpeed) * (e.LifeSpan - 1));
            image.Current.Y -= (short)(UqmMath.Sine(angle, transitionSpeed) * (e.LifeSpan - 1));
            image.Current.X = (short)battle.WrapX(image.Current.X);
            image.Current.Y = (short)battle.WrapY(image.Current.Y);
        }
        image.MassPoints = (byte)angle;
        image.PreprocessFunc = x => PhoenixTransition(battle, x);
        image.DeathFunc = SpawnPhoenixTrail;
        image.Parent = starShip;
    }

    // pkunk_preprocess: on warping in, half the time a phoenix at the
    // list's head (preprocessed before the ships, so before the dead
    // Pkunk's death); a new ship needs no more of this, a reborn one is
    // non-solid for 12 frames, its four images flying in from the
    // diagonals. Until it's solid again it has no keys.
    private static void Preprocess(Battle battle, Element e, Pkunk p)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
        {
            Element? phoenix = null;
            if ((battle.Random.Next("pkunk_preprocess") & 1) != 0)
                phoenix = battle.List.Alloc();

            if (phoenix != null)
            {
                phoenix.PlayerNr = e.PlayerNr;
                phoenix.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar;
                phoenix.LifeSpan = 1;
                phoenix.DeathFunc = x => InterceptPkunkDeath(battle, x, p);
                phoenix.Parent = starShip;
                battle.List.Insert(phoenix, battle.List.Head);
            }
            p.Phoenix = phoenix;

            if (e.Target == null)
                rd.Preprocess = null;   // a new ship is preprocessed only once
            else
            {
                // The rebirth's sound.
                e.LifeSpan = (ushort)p.D.Define("PHOENIX_LIFE");
                e.Prim = PrimType.None;
                e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife | ElementFlags.Changing;

                ushort facing = starShip.ShipFacing;
                for (int angle = UqmMath.Quadrant >> 1; angle < UqmMath.FullCircle; angle += UqmMath.Quadrant)
                {
                    starShip.ShipFacing = (ushort)UqmMath.NormalizeFacing(facing + UqmMath.AngleToFacing(angle));
                    PhoenixTransition(battle, e);
                }
                starShip.ShipFacing = facing;
            }
        }

        if (rd.Preprocess != null)
        {
            starShip.CurStatusFlags &= ~StatusFlags.Keys;
            if (e.LifeSpan == Battle.NormalLife)
            {
                e.Current.Frame = e.Next.Frame = Frame.SetEqu(e.Current.FrameArray![0], e.Current.Frame);
                e.Prim = PrimType.Stamp;
                Battle.InitIntersectStartPoint(e);
                Battle.InitIntersectEndPoint(e);
                Battle.InitIntersectFrame(e);
                e.Velocity.Zero();
                e.StateFlags &= ~(ElementFlags.NonSolid | ElementFlags.FiniteLife);
                e.StateFlags |= ElementFlags.Changing;
                rd.Preprocess = null;
            }
        }
    }

    // pkunk_postprocess: the special's wait counts in its characteristics;
    // when it's up, the special, short of energy, insults (never the last
    // insult again) for SPECIAL_ENERGY_COST energy.
    private static void Postprocess(Battle battle, Element e, Pkunk p)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if (rd.Characteristics.SpecialWait != 0)
            --rd.Characteristics.SpecialWait;
        else if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 && rd.EnergyLevel < rd.MaxEnergy)
        {
            var insults = battle.ShipState<Insults>();
            int curSound;
            do
            {
                curSound = 2 + (ushort)battle.Random.Next("pkunk_postprocess") % (SoundCount - 2);
            } while (curSound == insults.LastSound);
            // The insult's sound.
            insults.LastSound = curSound;

            battle.DeltaEnergy(e, (short)p.D.Define("SPECIAL_ENERGY_COST"));
            rd.Characteristics.SpecialWait = (byte)p.D.Define("SPECIAL_WAIT");
        }
    }
}
