// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/chenjesu/chenjesu.c: init_chenjesu, crystal_postprocess,
// crystal_preprocess, animate, crystal_collision, doggy_preprocess,
// doggy_death, doggy_collision, spawn_doggy, chenjesu_intelligence,
// initialize_crystal, chenjesu_postprocess, chenjesu_preprocess),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// chenjesu.c: the Chenjesu Broodhome. Its weapon is a photon crystal that
/// flies on while the key is held and, let go, bursts into eight shards;
/// its special releases a DOGI, up to four at a time, which chases the
/// nearest opposing ship (<see cref="Battle.TrackShip"/>), dodging its
/// nose, and drains its energy on contact.
/// <para>
/// Its AI holds the crystal until it would burst on the enemy ship, and
/// releases DOGI while low on energy. The DOGI are its SEEKING_SPECIAL
/// shots: they take the very sheets in <see cref="RaceDesc.Special"/>.
/// </para>
/// </summary>
public sealed class ChenjesuShip : IShipCode
{
    public const string Id = "chenjesu";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _crystal, _doggy;

    /// <param name="description">The Chenjesu's description (data/ships.json's chenjesu).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="crystal">Its crystal's sheets (ship_data.weapon): the crystal, its shards, its sparkles.</param>
    /// <param name="doggy">
    /// The DOGI's sheets (ship_data.special), the very instance the DOGI
    /// take: its SEEKING_SPECIAL flag makes the AI tell them by it.
    /// </param>
    public ChenjesuShip(ShipDescription description, Views? hull, Views? crystal, Views? doggy)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _crystal = crystal;
        _doggy = doggy;
    }

    // chenjesu.c's #defines its code uses.
    private sealed record Defines(int ChenjesuOffset, int NumSparkles, int FragmentRange, int SpecialEnergyCost,
        int DoggyOffset, int DoggySpeed, int EnergyDrain, int MaxDoggies, int DoggyHits, int DoggyMass);

    /// <summary>init_chenjesu: chenjesu_desc with its sheets and chenjesu.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _crystal, _doggy);
        var k = new Defines(d.Define("CHENJESU_OFFSET"), d.Define("NUM_SPARKLES"), d.Define("FRAGMENT_RANGE"),
            d.Define("SPECIAL_ENERGY_COST"), d.Define("DOGGY_OFFSET"), d.Define("DOGGY_SPEED"), d.Define("ENERGY_DRAIN"),
            d.Define("MAX_DOGGIES"), d.Define("DOGGY_HITS"), d.Define("DOGGY_MASS"));
        var fragment = new MissileBlock
        {
            Index = 1, Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Speed = (short)d.Define("FRAGMENT_SPEED"),
            HitPoints = (short)d.Define("FRAGMENT_HITS"), Damage = (short)d.Define("FRAGMENT_DAMAGE"),
            Life = (ushort)d.Define("FRAGMENT_LIFE"), BlastOffs = (short)d.Define("FRAGMENT_OFFSET"),
        };
        int numFragments = d.Define("NUM_FRAGMENTS");
        var crystal = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)k.ChenjesuOffset,
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => CrystalPreprocess(battle, e, fragment, numFragments),
        };
        rd.Preprocess = e => Preprocess(e, k);
        rd.Postprocess = e => Postprocess(battle, e, k);
        rd.InitWeapon = e => InitializeCrystal(battle, e, crystal, k);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, k);
        return rd;
    }

    // crystal_postprocess: the crystal bursts into its shards, one every
    // so many facings round (their CRYSTAL_FRAGMENTS sound).
    private static void CrystalPostprocess(Battle battle, Element e, MissileBlock block, int numFragments)
    {
        var starShip = e.Parent!;
        block.Cx = e.Next.X;
        block.Cy = e.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Sender = e.PlayerNr;
        for (block.Face = 0; block.Face < UqmMath.Facings; block.Face += (ushort)(UqmMath.Facings / numFragments))
        {
            var shard = battle.InitializeMissile(block);
            if (shard != null)
            {
                shard.Parent = starShip;
                battle.List.Put(shard);
            }
        }
    }

    // crystal_preprocess: the crystal lives on while the weapon is held;
    // let go, it bursts after this frame.
    private static void CrystalPreprocess(Battle battle, Element e, MissileBlock fragment, int numFragments)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Weapon) != 0)
            ++e.LifeSpan;   // keep it going while the key is pressed
        else
        {
            e.LifeSpan = 1;
            e.PostprocessFunc = c => CrystalPostprocess(battle, c, fragment, numFragments);
        }
    }

    // animate: the next frame every next_turn + 1 frames.
    private static void Animate(Element e)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            e.Next.Frame = e.Current.Frame.Inc();
            e.StateFlags |= ElementFlags.Changing;
            e.TurnWait = e.NextTurn;
        }
    }

    // crystal_collision: the hit, and its blast is the crystal's sparkles
    // at the hit thing's place.
    private static void CrystalCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, int numSparkles)
    {
        var blast = battle.WeaponCollision(e0, at0, e1, at1);
        if (blast == null)
            return;
        blast.Current.X = e1.Current.X;
        blast.Current.Y = e1.Current.Y;
        blast.LifeSpan = (ushort)numSparkles;
        blast.TurnWait = blast.NextTurn = 0;
        blast.PreprocessFunc = Animate;
        blast.Current.FrameArray = e0.Next.FrameArray;
        blast.Current.Frame = blast.Current.FrameArray![0].SetAbs(2);   // skip the stones
    }

    // doggy_preprocess: each DOGI counts itself in the ship's special
    // counter; unless knocked back by a collision, it heads for its
    // target, sidestepping a quarter circle when the target is facing it,
    // or anywhere without one.
    private static void DoggyPreprocess(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        ++starShip.SpecialCounter;
        if (e.ThrustWait > 0)   // could be non-zero after a collision
            --e.ThrustWait;
        else
        {
            ushort facing, origFacing;
            facing = origFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
            int f;
            if (battle.TrackShip(e, ref facing) < 0)
                f = UqmMath.NormalizeFacing((int)battle.Random.Next("doggy_preprocess"));
            else
            {
                var ship = e.Target!;
                // Across the seam where contact is tested so (decision 5).
                f = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(
                    battle.SeamDeltaX(ship.Current.X - e.Current.X),
                    battle.SeamDeltaY(ship.Current.Y - e.Current.Y))));
                int deltaFacing = UqmMath.NormalizeFacing(f - ship.Current.Frame.Index);

                const int octant = UqmMath.Quadrant / 2;
                if (deltaFacing > UqmMath.AngleToFacing(UqmMath.HalfCircle - octant)
                    && deltaFacing < UqmMath.AngleToFacing(UqmMath.HalfCircle + octant))
                {
                    if (deltaFacing >= UqmMath.AngleToFacing(UqmMath.HalfCircle))
                        f -= UqmMath.AngleToFacing(UqmMath.Quadrant);
                    else
                        f += UqmMath.AngleToFacing(UqmMath.Quadrant);
                }

                f = UqmMath.NormalizeFacing(f);
            }

            if (f != origFacing)
                e.Velocity.SetVector(k.DoggySpeed, f);
        }
    }

    // doggy_death: the DOGI dies over six frames of its sheet (its
    // DOGGY_DIES sound), still and touching nothing.
    private static void DoggyDeath(Element e)
    {
        e.StateFlags &= ~ElementFlags.Disappearing;
        e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife;
        e.LifeSpan = 6;
        e.PreprocessFunc = Animate;
        e.DeathFunc = null;
        e.CollisionFunc = null;
        e.Velocity.Zero();
        e.TurnWait = e.NextTurn = 0;
    }

    // doggy_collision: a DOGI bumps as a ship does; touching an opposing
    // ship it drains its energy (its DOGGY_STEALS_ENERGY sound); either
    // way it's knocked off course for a few frames.
    private static void DoggyCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines k)
    {
        battle.Collision(e0, at0, e1, at1);
        if ((e1.StateFlags & ElementFlags.PlayerShip) != 0 && e0.PlayerNr != e1.PlayerNr)
        {
            var rd = e1.Parent!.RaceDesc!;
            if (rd.EnergyLevel < k.EnergyDrain)
                battle.DeltaEnergy(e1, (short)-rd.EnergyLevel);
            else
                battle.DeltaEnergy(e1, (short)-k.EnergyDrain);
        }
        if (e0.ThrustWait <= Battle.CollisionThrustWait)
            e0.ThrustWait += Battle.CollisionThrustWait << 1;
    }

    // spawn_doggy: a DOGI out of the ship's back (the ship defies physics
    // for the frame), flying away from it (its RELEASE_DOGGY sound).
    private static void SpawnDoggy(Battle battle, Element e, Defines k)
    {
        var doggy = battle.List.Alloc();
        if (doggy == null)
            return;
        e.StateFlags |= ElementFlags.DefyPhysics;

        battle.List.Put(doggy);
        doggy.HitPoints = (ushort)k.DoggyHits;
        doggy.MassPoints = (byte)k.DoggyMass;
        doggy.ThrustWait = 0;
        doggy.PlayerNr = e.PlayerNr;
        doggy.StateFlags = ElementFlags.Appearing;
        doggy.LifeSpan = Battle.NormalLife;
        doggy.Prim = PrimType.Stamp;
        doggy.PreprocessFunc = d => DoggyPreprocess(battle, d, k);
        doggy.PostprocessFunc = null;
        doggy.CollisionFunc = (e0, at0, e1, at1) => DoggyCollision(battle, e0, at0, e1, at1, k);
        doggy.DeathFunc = DoggyDeath;

        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        int angle = UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle;
        doggy.Current.X = (short)(e.Next.X + UqmMath.Cosine(angle, Battle.DisplayToWorld(k.ChenjesuOffset + k.DoggyOffset)));
        doggy.Current.Y = (short)(e.Next.Y + UqmMath.Sine(angle, Battle.DisplayToWorld(k.ChenjesuOffset + k.DoggyOffset)));
        doggy.Current.FrameArray = rd.Special;
        doggy.Current.Frame = rd.Special![0];

        doggy.Velocity.SetVector(k.DoggySpeed, UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle)));

        doggy.Parent = starShip;
    }

    // chenjesu_intelligence: it pursues an enemy ship within 16 frames that
    // turns well, or a slow, far-reaching one with a seeking weapon. A
    // DOGI just out (the counter 1) has it look at a shot it's enticed by
    // within 8 frames instead. With a crystal of its own out (the weapon
    // held), it lets go when the crystal's burst would reach the enemy
    // ship, holding it otherwise; with none, it fires when the burst would
    // reach. Low on energy and not firing, it releases a DOGI.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Defines k)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        starShip.ShipInputState &= ~StatusFlags.Special;

        int eval = Battle.EnemyShipIndex;
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object != null)
        {
            var enemyRd = enemy.Object.Parent!.RaceDesc!;
            if ((enemy.WhichTurn <= 16 && enemyRd.ManeuverabilityIndex >= Battle.MediumShip)
                || (enemyRd.ManeuverabilityIndex <= Battle.SlowShip
                    && enemyRd.WeaponRange >= Battle.LongRangeWeapon * 3 / 4
                    && (enemyRd.ShipFlags & ShipFlags.SeekingWeapon) != 0))
                enemy.MoveState = MoveState.Pursue;
        }

        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (starShip.SpecialCounter == 1
            && weapon.Object != null
            && weapon.MoveState == MoveState.Entice
            && weapon.WhichTurn <= 8)
            eval = Battle.EnemyWeaponIndex;

        battle.ShipIntelligence(ship, concerns, concernCounter);

        if (concerns[eval].Object != null)
        {
            Element? h = (starShip.OldStatusFlags & StatusFlags.Weapon) != 0 ? battle.List.Tail : null;
            while (h != null)
            {
                var crystal = h;
                var next = crystal.Predecessor;
                // Its own crystals only: a teammate's (of its side, another
                // ship's) aren't, as UQM's side has one ship.
                if ((crystal.StateFlags & ElementFlags.NonSolid) == 0
                    && ReferenceEquals(crystal.Next.FrameArray, rd.Weapon)
                    && crystal.PreprocessFunc != null
                    && crystal.LifeSpan > 0
                    && crystal.PlayerNr == ship.PlayerNr && !Battle.Teammates(crystal, ship))
                {
                    var enemyShip = concerns[Battle.EnemyShipIndex].Object;
                    if (enemyShip != null)
                    {
                        ushort whichTurn = battle.PlotIntercept(crystal, enemyShip, crystal.LifeSpan,
                            (ushort)(k.FragmentRange / 2));
                        if (whichTurn == 0
                            || (whichTurn == 1 && battle.PlotIntercept(crystal, enemyShip, crystal.LifeSpan, 0) == 0))
                            starShip.ShipInputState &= ~StatusFlags.Weapon;
                        else if (starShip.WeaponCounter == 0)
                        {
                            starShip.ShipInputState |= StatusFlags.Weapon;
                            eval = Battle.EnemyShipIndex;
                        }
                        break;
                    }
                    next = null;
                }
                h = next;
            }

            if (h == null)
            {
                if ((starShip.OldStatusFlags & StatusFlags.Weapon) != 0)
                {
                    starShip.ShipInputState &= ~StatusFlags.Weapon;
                    if (eval == Battle.EnemyWeaponIndex)
                        starShip.WeaponCounter = 3;
                }
                else if (starShip.WeaponCounter == 0
                         && battle.ShipWeapons(ship, concerns[eval].Object!, (ushort)(k.FragmentRange / 2)))
                    starShip.ShipInputState |= StatusFlags.Weapon;
            }
        }

        if (starShip.SpecialCounter < k.MaxDoggies)
        {
            if (concerns[eval].Object != null
                && rd.EnergyLevel <= k.SpecialEnergyCost
                && (starShip.ShipInputState & StatusFlags.Weapon) == 0)
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // initialize_crystal: a crystal ahead.
    private static IReadOnlyList<Element?> InitializeCrystal(Battle battle, Element ship, MissileBlock block, Defines k)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var crystal = battle.InitializeMissile(block);
        if (crystal != null)
        {
            int numSparkles = k.NumSparkles;
            crystal.CollisionFunc = (e0, at0, e1, at1) => CrystalCollision(battle, e0, at0, e1, at1, numSparkles);
        }
        return new[] { crystal };
    }

    // chenjesu_postprocess: the special, with fewer than MAX_DOGGIES out
    // and paid for, releases a DOGI; the counter is set to 1 (as if one
    // were out: ship_postprocess counts it down), and the DOGI out count
    // themselves into it as they move.
    private static void Postprocess(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter < k.MaxDoggies
            && battle.DeltaEnergy(e, (short)-k.SpecialEnergyCost))
            SpawnDoggy(battle, e, k);

        starShip.SpecialCounter = 1;
    }

    // chenjesu_preprocess: a counter above 1 here (only with UQM's standard
    // computer opponent) is pushed past the DOGI's most; the weapon held
    // from the frame before holds its wait.
    private static void Preprocess(Element e, Defines k)
    {
        var starShip = e.Parent!;
        if (starShip.SpecialCounter > 1)   // only when the STANDARD computer opponent
            starShip.SpecialCounter = (byte)(starShip.SpecialCounter + k.MaxDoggies);
        if ((starShip.CurStatusFlags & starShip.OldStatusFlags & StatusFlags.Weapon) != 0)
            ++starShip.WeaponCounter;
    }
}
