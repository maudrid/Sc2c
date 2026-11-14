// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/chmmr/chmmr.c: init_chmmr, animate, laser_death,
// initialize_megawatt_laser, chmmr_intelligence, chmmr_postprocess,
// satellite_preprocess, spawn_point_defense, satellite_postprocess,
// satellite_collision, satellite_death, spawn_satellites,
// chmmr_preprocess), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// chmmr.c: the Chmmr Avatar. Its weapon is a megawatt laser, fired every
/// frame it's held, its colour cycling, with a muzzle flash and ion spots
/// along it; its special is a tractor beam pulling the ship it tracks
/// (<see cref="Battle.TrackShip"/>: the nearest opposing ship, kept once
/// chosen; with several players one whose crew is gone is dropped for the
/// nearest with crew, <see cref="Battle.DropDeadTarget"/>) towards a point
/// ahead of it. Three satellites circle it, each zapping the weakest
/// (then nearest) opposing element within reach; while they live it has
/// point defence. The satellites pass its teammates and their shots (they
/// only take other sides' elements, as UQM's; and no friendly fire).
/// <para>
/// Its AI pulls with the tractor beam an enemy beyond 12 frames that
/// flies away from it, when it isn't firing. The satellites are its
/// SEEKING_SPECIAL elements: they take the very sheets in <see cref="RaceDesc.Special"/>.
/// </para>
/// </summary>
public sealed class ChmmrShip : IShipCode
{
    public const string Id = "chmmr";

    private static readonly int[] LaserCycle =
    {
        Battle.Rgb15(0x17, 0x00, 0x00), Battle.Rgb15(0x1F, 0x03, 0x00),
        Battle.Rgb15(0x1F, 0x11, 0x00), Battle.Rgb15(0x1F, 0x03, 0x00),
    };

    private static readonly int[] ShadowColors =
    {
        Battle.Rgb15(0x00, 0x00, 0x10), Battle.Rgb15(0x00, 0x00, 0x0E), Battle.Rgb15(0x00, 0x00, 0x0C),
        Battle.Rgb15(0x00, 0x00, 0x09), Battle.Rgb15(0x00, 0x00, 0x07),
    };

    private static readonly short[] ShadowOffsets =
    {
        Battle.DisplayToWorld(8), Battle.DisplayToWorld(8 + 9), Battle.DisplayToWorld(8 + 9 + 11),
        Battle.DisplayToWorld(8 + 9 + 11 + 14), Battle.DisplayToWorld(8 + 9 + 11 + 14 + 18),
    };

    private static readonly int SatelliteLaserColor = Battle.Rgb15(0x00, 0x01, 0x1F);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _muzzle, _satellite;

    /// <param name="description">The Chmmr's description (data/ships.json's chmmr).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="muzzle">Its laser's sheets (ship_data.weapon: where the beam starts, the muzzle flash, the ion spots).</param>
    /// <param name="satellite">The satellites' sheets (ship_data.special), the very instance they take (SEEKING_SPECIAL).</param>
    public ChmmrShip(ShipDescription description, Views? hull, Views? muzzle, Views? satellite)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _muzzle = muzzle;
        _satellite = satellite;
    }

    // chmmr.c's #defines.
    private sealed record Defines(int ChmmrOffset, int LaserRange, int NumCycles, int SpecialEnergyCost,
        int NumSatellites, int SatelliteOffset, int SatelliteHitPoints, int SatelliteMass, int DefenseRange,
        int DefenseWait);

    /// <summary>init_chmmr: chmmr_desc with its sheets and chmmr.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _muzzle, _satellite);
        var f = new Defines(d.Define("CHMMR_OFFSET"), d.Define("LASER_RANGE"), d.Define("NUM_CYCLES"),
            d.Define("SPECIAL_ENERGY_COST"), d.Define("NUM_SATELLITES"), d.Define("SATELLITE_OFFSET"),
            d.Define("SATELLITE_HITPOINTS"), d.Define("SATELLITE_MASS"), d.Define("DEFENSE_RANGE"),
            d.Define("DEFENSE_WAIT"));
        rd.Preprocess = e => Preprocess(battle, e, f);
        rd.Postprocess = e => Postprocess(battle, e, f);
        rd.InitWeapon = e => InitializeMegawattLaser(battle, e, f);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // animate: the next frame every next_turn + 1 frames.
    private static void Animate(Element e) => Battle.AnimationPreprocess(e);

    // laser_death: the laser's colour step goes back to the ship; seven
    // times in eight, an ion spot somewhere along the beam beyond the
    // ship's nose.
    private static void LaserDeath(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        starShip.SpecialCounter = e.TurnWait;

        if (starShip.Ship is { } ship)
        {
            // Across the seam where contact is tested so (decision 5).
            short dx = battle.SeamDeltaX(e.Current.X - ship.Current.X);
            short dy = battle.SeamDeltaY(e.Current.Y - ship.Current.Y);
            long dist;
            long nose = Battle.DisplayToWorld(f.ChmmrOffset + 10);
            Element? ion;
            if (((byte)battle.Random.Next("laser_death") & 0x07) != 0
                && (dist = (long)dx * dx + (long)dy * dy) >= nose * nose
                && (ion = battle.List.Alloc()) != null)
            {
                ion.PlayerNr = e.PlayerNr;
                ion.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                 | ElementFlags.Appearing;
                ion.TurnWait = ion.NextTurn = 0;
                ion.LifeSpan = 9;

                int angle = UqmMath.ArcTan(dx, dy);
                int magnitude = (ushort)((ushort)battle.Random.Next("laser_death")
                                         % (UqmMath.SquareRoot((uint)dist) + 1 - nose) + nose);
                ion.Current.X = (short)(ship.Current.X + UqmMath.Cosine(angle, magnitude));
                ion.Current.Y = (short)(ship.Current.Y + UqmMath.Sine(angle, magnitude));
                var weapon = starShip.RaceDesc!.Weapon!;
                ion.Current.FrameArray = weapon;
                ion.Current.Frame = weapon[0].SetAbs(UqmMath.AngleToFacing(UqmMath.FullCircle) << 1);
                ion.PreprocessFunc = Animate;
                ion.Parent = starShip;
                ion.Prim = PrimType.Stamp;
                battle.List.Put(ion);
            }
        }
    }

    // initialize_megawatt_laser: a laser LASER_RANGE long from the ship's
    // nose (the weapon sheet's facing frame's hot spot), its colour the
    // cycle's step (kept in the special's counter), doing 2 damage.
    private static IReadOnlyList<Element?> InitializeMegawattLaser(Battle battle, Element ship, Defines f)
    {
        var starShip = ship.Parent!;
        var block = new LaserBlock { Face = starShip.ShipFacing };
        var r = starShip.RaceDesc!.Weapon![0].SetAbs(block.Face).Rect;
        block.Cx = (short)(Battle.DisplayAlign(ship.Next.X) + Battle.DisplayToWorld(-r.HotSpotX));
        block.Cy = (short)(Battle.DisplayAlign(ship.Next.Y) + Battle.DisplayToWorld(-r.HotSpotY));
        block.Ex = UqmMath.Cosine(UqmMath.FacingToAngle(block.Face), f.LaserRange);
        block.Ey = UqmMath.Sine(UqmMath.FacingToAngle(block.Face), f.LaserRange);
        block.Sender = ship.PlayerNr;
        block.Flags = ElementFlags.IgnoreSimilar;
        block.PixOffs = 0;
        block.Color = LaserCycle[starShip.SpecialCounter];
        var laser = battle.InitializeLaser(block);
        if (laser != null)
        {
            laser.MassPoints = 2;
            laser.DeathFunc = e => LaserDeath(battle, e, f);
            laser.TurnWait = (byte)((starShip.SpecialCounter + 1) % f.NumCycles);
        }
        return new[] { laser };
    }

    // chmmr_intelligence: the stock AI; the tractor beam when ready, not
    // firing, on an enemy beyond 12 frames flying away from it.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        starShip.ShipInputState &= ~StatusFlags.Special;
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter == 0
            && enemy.Object != null
            && (starShip.ShipInputState & StatusFlags.Weapon) == 0
            && enemy.WhichTurn > 12
            && UqmMath.NormalizeAngle(ship.Velocity.TravelAngle
                                      - (enemy.Object.Velocity.TravelAngle + UqmMath.HalfCircle)
                                      + UqmMath.Quadrant) > UqmMath.HalfCircle)
            starShip.ShipInputState |= StatusFlags.Special;
    }

    // chmmr_postprocess: firing, a muzzle flash on the laser just fired
    // (every other cycle step); the tractor beam, paid for each frame,
    // pulling the ship it tracks (a planet's mass aside) towards a point a
    // third of the laser's reach ahead, setting its speed flags, its five
    // shadows trailing; the special's counter back to the cycle's start.
    private static void Postprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;

        if ((starShip.CurStatusFlags & StatusFlags.Weapon) != 0)
        {
            var tail = battle.List.Tail!;
            Element? flash;
            if (tail != e
                && tail.PlayerNr == e.PlayerNr
                // Co-op: a teammate's laser fired this frame isn't its own (UQM's takes
                // any of its side's, one without a ship among them).
                && (tail.Parent == null || tail.Parent == starShip)
                && (tail.StateFlags & ElementFlags.Appearing) != 0
                && tail.Prim == PrimType.Line
                && (starShip.SpecialCounter & 1) == 0
                && (flash = battle.List.Alloc()) != null)
            {
                flash.PlayerNr = e.PlayerNr;
                flash.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                   | ElementFlags.Appearing;
                flash.LifeSpan = 1;
                flash.Current = e.Next;
                flash.Current.FrameArray = rd.Weapon;
                flash.Current.Frame = rd.Weapon![0].SetAbs(starShip.ShipFacing + UqmMath.AngleToFacing(UqmMath.FullCircle));
                flash.Parent = starShip;
                flash.Prim = PrimType.Stamp;
                battle.List.Put(flash);
            }
        }

        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && battle.DeltaEnergy(e, (short)-f.SpecialEnergyCost))
        {
            // The tractor beam's sound.
            ushort facing = 0;
            // Co-op: a kept target whose crew is gone gives way to the nearest with crew (decision 6).
            battle.DropDeadTarget(e);
            if (battle.TrackShip(e, ref facing) >= 0)
            {
                var target = e.Target!;
                if (!Battle.GravityMass(target.MassPoints + 1))
                {
                    int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
                    int reach = f.LaserRange / 3 + Battle.DisplayToWorld(f.ChmmrOffset);
                    // Across the seam where contact is tested so (decision 5).
                    short dx = battle.SeamDeltaX(e.Next.X + UqmMath.Cosine(angle, reach) - target.Next.X);
                    short dy = battle.SeamDeltaY(e.Next.Y + UqmMath.Sine(angle, reach) - target.Next.Y);
                    angle = UqmMath.ArcTan(dx, dy);
                    int magnitude = (ushort)(Velocity.WorldToVelocity(12) / target.MassPoints);
                    target.Velocity.Delta(UqmMath.Cosine(angle, magnitude), UqmMath.Sine(angle, magnitude));

                    (dx, dy) = target.Velocity.Current();
                    var enemyStarShip = target.Parent!;
                    uint currentSpeed = (uint)((long)dx * dx + (long)dy * dy);
                    long maxV = Velocity.WorldToVelocity(enemyStarShip.RaceDesc!.Characteristics.MaxThrust);
                    uint maxSpeed = (uint)(maxV * maxV);
                    enemyStarShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
                    if (currentSpeed > maxSpeed)
                        enemyStarShip.CurStatusFlags |= StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed;
                    else if (currentSpeed == maxSpeed)
                        enemyStarShip.CurStatusFlags |= StatusFlags.ShipAtMaxSpeed;

                    for (int i = 0; i < ShadowOffsets.Length; ++i)
                    {
                        var shadow = battle.List.Alloc();
                        if (shadow == null)
                            continue;
                        shadow.PlayerNr = target.PlayerNr;
                        shadow.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                            | ElementFlags.PostProcess;
                        shadow.LifeSpan = 1;
                        shadow.Current = target.Next;
                        shadow.Current.X += UqmMath.Cosine(angle, ShadowOffsets[i]);
                        shadow.Current.Y += UqmMath.Sine(angle, ShadowOffsets[i]);
                        shadow.Next = shadow.Current;
                        shadow.Parent = enemyStarShip;
                        shadow.Velocity.SetComponents(dx, dy);
                        shadow.Prim = PrimType.StampFill;
                        shadow.PrimColor = ShadowColors[i];
                        battle.List.Insert(shadow, battle.List.Head);
                    }
                }
            }
        }

        starShip.SpecialCounter = 0;
    }

    // satellite_preprocess: a satellite lives on, spinning, a step round
    // its circle each frame; while its ship lives, the ship has point
    // defence and the satellite flies to its place on the circle (at most
    // 20 pixels a frame).
    private static void SatellitePreprocess(Battle battle, Element e, Defines f)
    {
        ++e.LifeSpan;
        e.Next.Frame = e.Current.Frame.SetAbs((e.Current.Frame.Index + 1) & 7);
        e.StateFlags |= ElementFlags.Changing;
        e.TurnWait = (byte)UqmMath.NormalizeAngle(e.TurnWait + 1);

        var starShip = e.Parent!;
        if (starShip.Ship is { } ship)
        {
            starShip.RaceDesc!.ShipFlags |= ShipFlags.PointDefense;
            short dx = (short)(ship.Next.X + UqmMath.Cosine(e.TurnWait, f.SatelliteOffset) - e.Current.X);
            short dy = (short)(ship.Next.Y + UqmMath.Sine(e.TurnWait, f.SatelliteOffset) - e.Current.Y);
            dx = battle.WrapDeltaX(dx);
            dy = battle.WrapDeltaY(dy);
            long step = Battle.DisplayToWorld(20);
            if ((long)dx * dx + (long)dy * dy <= step * step)
                e.Velocity.SetComponents((short)Velocity.WorldToVelocity(dx), (short)Velocity.WorldToVelocity(dy));
            else
            {
                int angle = UqmMath.ArcTan(dx, dy);
                int speed = Velocity.WorldToVelocity(Battle.DisplayToWorld(20));
                e.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));
            }
        }
    }

    // spawn_point_defense: in the helper's death, after every element
    // before it has moved: the satellite zaps the weakest (then nearest)
    // element of another side within reach that could hit its ship, and
    // waits DEFENSE_WAIT frames.
    private static void SpawnPointDefense(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        var ship = starShip.Ship!;
        var satellite = e.Target!;
        Element? best = null;
        int bestDist = f.DefenseRange + 1;
        int weakest = 255;
        int range = f.DefenseRange;
        for (Element? o = e.Predecessor, next; o != null; o = next)
        {
            next = o.Predecessor;
            if (o.PlayerNr != ship.PlayerNr
                && o.PlayerNr != Battle.NeutralPlayerNum
                && Battle.CollisionPossible(o, ship)
                && !Battle.ObjectCloaked(o))
            {
                // Across the seam where contact is tested so (decision 5).
                short dx = battle.SeamDeltaX(o.Next.X - satellite.Next.X);
                short dy = battle.SeamDeltaY(o.Next.Y - satellite.Next.Y);
                if (dx < 0)
                    dx = (short)-dx;
                if (dy < 0)
                    dy = (short)-dy;
                dx = Battle.WorldToDisplay(dx);
                dy = Battle.WorldToDisplay(dy);
                int dist;
                if ((ushort)dx <= range && (ushort)dy <= range
                    && (dist = (ushort)((ushort)dx * (ushort)dx + (ushort)dy * (ushort)dy)) <= range * range
                    && (o.HitPoints < weakest || (o.HitPoints == weakest && dist < bestDist)))
                {
                    best = o;
                    bestDist = dist;
                    weakest = (byte)o.HitPoints;
                }
            }
        }

        if (best != null)
        {
            var block = new LaserBlock
            {
                Cx = satellite.Next.X, Cy = satellite.Next.Y, Face = 0,
                Ex = battle.SeamDeltaX(best.Next.X - satellite.Next.X),
                Ey = battle.SeamDeltaY(best.Next.Y - satellite.Next.Y),
                Sender = satellite.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = 0,
                Color = SatelliteLaserColor,
            };
            var laser = battle.InitializeLaser(block);
            if (laser != null)
            {
                laser.Parent = starShip;
                laser.Target = null;
                battle.List.Put(laser);
                satellite.ThrustWait = (byte)f.DefenseWait;
            }
        }
    }

    // satellite_postprocess: when its wait is up, a helper that dies this
    // frame, its target the satellite, to zap from it.
    private static void SatellitePostprocess(Battle battle, Element e, Defines f)
    {
        if (e.ThrustWait != 0 || e.LifeSpan == 0)
            --e.ThrustWait;
        else
        {
            var defense = battle.List.Alloc();
            if (defense != null)
            {
                battle.List.Put(defense);
                defense.PlayerNr = e.PlayerNr;
                defense.StateFlags = ElementFlags.Appearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
                defense.Target = e.Successor!.Predecessor;
                defense.DeathFunc = x => SpawnPointDefense(battle, x, f);
                defense.Parent = e.Parent;
            }
        }
    }

    // satellite_death: the ship loses point defence (until another
    // satellite's step); the satellite breaks up over four frames.
    private static void SatelliteDeath(Element e)
    {
        e.Parent!.RaceDesc!.ShipFlags &= ~ShipFlags.PointDefense;

        e.StateFlags &= ~ElementFlags.Disappearing;
        e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife | ElementFlags.Changing;
        e.LifeSpan = 4;
        e.TurnWait = 1;
        e.NextTurn = 0;
        e.Next.Frame = e.Current.Frame.SetAbs(8);

        e.PreprocessFunc = Animate;
        e.DeathFunc = null;
        e.PostprocessFunc = null;
        e.CollisionFunc = null;
    }

    // spawn_satellites: three satellites about the ship, a third of a
    // circle apart, each its spin's frame at random.
    private static void SpawnSatellites(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if (starShip.Ship is not { } ship)
            return;
        for (int i = 0; i < f.NumSatellites; ++i)
        {
            var satellite = battle.List.Alloc();
            if (satellite == null)
                continue;
            satellite.PlayerNr = ship.PlayerNr;
            satellite.StateFlags = ElementFlags.IgnoreSimilar | ElementFlags.Appearing | ElementFlags.FiniteLife;
            satellite.LifeSpan = Battle.NormalLife + 1;
            satellite.HitPoints = (ushort)f.SatelliteHitPoints;
            satellite.MassPoints = (byte)f.SatelliteMass;

            int angle = (i * UqmMath.FullCircle + (f.NumSatellites >> 1)) / f.NumSatellites;
            satellite.TurnWait = (byte)angle;
            satellite.Current.X = (short)(ship.Next.X + UqmMath.Cosine(angle, f.SatelliteOffset));
            satellite.Current.Y = (short)(ship.Next.Y + UqmMath.Sine(angle, f.SatelliteOffset));
            var special = starShip.RaceDesc!.Special!;
            satellite.Current.FrameArray = special;
            satellite.Current.Frame = special[0].SetAbs((ushort)battle.Random.Next("spawn_satellites") & 0x07);

            satellite.PreprocessFunc = x => SatellitePreprocess(battle, x, f);
            satellite.PostprocessFunc = x => SatellitePostprocess(battle, x, f);
            satellite.DeathFunc = SatelliteDeath;
            satellite.CollisionFunc = static (_, _, _, _) => { };

            satellite.Parent = starShip;
            satellite.Prim = PrimType.Stamp;
            battle.List.Put(satellite);
        }
    }

    // chmmr_preprocess: on warping in, a helper (not drawn) that dies as
    // the ship has warped in, launching the satellites; then no more.
    private static void Preprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        var helper = battle.List.Alloc();
        if (helper != null)
        {
            helper.PlayerNr = e.PlayerNr;
            helper.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                | ElementFlags.Appearing;
            helper.LifeSpan = Battle.HyperjumpLife + 1;
            helper.DeathFunc = x => SpawnSatellites(battle, x, f);
            helper.Parent = starShip;
            helper.Prim = PrimType.None;
            battle.List.Put(helper);
        }
        starShip.RaceDesc!.Preprocess = null;
    }
}
