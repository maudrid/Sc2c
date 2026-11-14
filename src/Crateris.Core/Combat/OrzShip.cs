// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/orz/orz.c: init_orz, howitzer_collision,
// initialize_turret_missile, count_marines, orz_intelligence,
// ion_preprocess, intruder_preprocess, spawn_marine_ion_trail,
// marine_preprocess, marine_collision, animate, turret_postprocess,
// orz_preprocess), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// orz.c: the Orz Nemesis. Its weapon is a howitzer fired from a turret,
/// which the special with left or right turns; the weapon and special
/// together launch a space marine (a crewman), which flies at the nearest
/// opposing ship, boards it and fights its crew (shown on that ship's
/// captain's window, <see cref="Battle.ModifySilhouette"/>) until it dies
/// or the ship does, then flies back to its own ship to come aboard again.
/// <para>
/// With several players (stage 4b decision 6): UQM's marine takes the
/// last opposing ship in the list (its loop keeps overwriting it); here it
/// takes the nearest (by TrackShip's measure). A marine aboard stays with
/// the ship it boarded (UQM's finds the one opposing ship in the list
/// each frame) and goes on killing its crew whatever its teammates can or
/// can't do to it; one flying back takes only its own ship, not a
/// teammate's. Its count of marines leaves out a teammate Orz's (UQM's
/// counts every marine with its sheets).
/// </para>
/// <para>
/// Its AI turns the turret to the enemy with the special while it can,
/// and launches marines at an enemy without point defence while it has a
/// quarter of its crew. The turret, the turret's flash and the marines
/// are its SEEKING_SPECIAL elements: they take the very sheets in
/// <see cref="RaceDesc.Special"/>.
/// </para>
/// </summary>
public sealed class OrzShip : IShipCode
{
    public const string Id = "orz";

    private static readonly int[] IonColors =
    {
        Battle.Rgb15(0x1F, 0x15, 0x00), Battle.Rgb15(0x1F, 0x0E, 0x00), Battle.Rgb15(0x1F, 0x03, 0x00),
        Battle.Rgb15(0x1B, 0x00, 0x00), Battle.Rgb15(0x13, 0x00, 0x00),
    };

    private static readonly int StartIonColor = Battle.Rgb15(0x1F, 0x15, 0x00);

    // SHIP_BEYOND_MAX_SPEED, SHIP_AT_MAX_SPEED and SHIP_IN_GRAVITY_WELL as
    // a marine keeps them in its thrust_wait (shifted down 6).
    private const byte BeyondMaxSpeed = 1, AtMaxSpeed = 2, InGravityWell = 4;

    private readonly ShipDescription _description;
    private readonly Views? _hull, _howitzer, _turret;

    /// <param name="description">The Orz's description (data/ships.json's orz).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="howitzer">Its howitzer's sheets (ship_data.weapon).</param>
    /// <param name="turret">The turret's and marines' sheets (ship_data.special), the very instance they take (SEEKING_SPECIAL).</param>
    public OrzShip(ShipDescription description, Views? hull, Views? howitzer, Views? turret)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _howitzer = howitzer;
        _turret = turret;
    }

    /// <summary>
    /// ORZ_MARINE (shofixti.c): a marine aboard a ship (intruder_preprocess
    /// with marine_collision), which the Shofixti's Glory Device kills
    /// though it's non-solid.
    /// </summary>
    internal static bool IsMarineAboard(Element e) => e.Tag is Aboard;

    // A marine aboard a ship: the ship it boarded (Element.Tag).
    private sealed record Aboard(Element Ship);

    // orz.c's #defines.
    private sealed record Defines(int TurretOffset, int TurretWait, int MarineMaxThrust, int MarineThrustIncrement,
        int MarineHitPoints, int MarineMassPoints, int MaxMarines, int MarineWait, int IonLife);

    /// <summary>init_orz: orz_desc with its sheets and orz.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _howitzer, _turret);
        var f = new Defines(d.Define("TURRET_OFFSET"), d.Define("TURRET_WAIT"), d.Define("MARINE_MAX_THRUST"),
            d.Define("MARINE_THRUST_INCREMENT"), d.Define("MARINE_HIT_POINTS"), d.Define("MARINE_MASS_POINTS"),
            d.Define("MAX_MARINES"), d.Define("MARINE_WAIT"), d.Define("ION_LIFE"));
        var howitzer = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)f.TurretOffset,
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        rd.Preprocess = e => Preprocess(battle, e, f);
        rd.InitWeapon = e => InitializeTurretMissile(battle, e, howitzer, f);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, f);
        return rd;
    }

    // howitzer_collision: it hits only another side's things.
    private static void HowitzerCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1)
    {
        if (e0.PlayerNr != e1.PlayerNr)
            battle.WeaponCollision(e0, at0, e1, at1);
    }

    // initialize_turret_missile: the turret (the ship's successor) turns a
    // step with the special and left or right, when it may; a howitzer
    // shell from it, its way.
    private static IReadOnlyList<Element?> InitializeTurretMissile(Battle battle, Element ship, MissileBlock block,
        Defines f)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;

        var turret = ship.Successor!;
        if (turret.TurnWait == 0
            && (starShip.CurStatusFlags & StatusFlags.Special) != 0
            && (starShip.CurStatusFlags & (StatusFlags.Left | StatusFlags.Right)) != 0)
        {
            if ((starShip.CurStatusFlags & StatusFlags.Right) != 0)
                ++turret.ThrustWait;
            else
                --turret.ThrustWait;
            turret.TurnWait = (byte)(f.TurretWait + 1);
        }
        block.Face = block.Index = (ushort)UqmMath.NormalizeFacing(starShip.ShipFacing + turret.ThrustWait);

        block.Sender = ship.PlayerNr;
        var shell = battle.InitializeMissile(block);
        if (shell != null)
            shell.CollisionFunc = (e0, at0, e1, at1) => HowitzerCollision(battle, e0, at0, e1, at1);
        return new[] { shell };
    }

    // count_marines: the ship's marines out (up to MAX_MARINES), counted
    // from the list's tail; with findSpot, the first slot on a captain's
    // window none of those aboard a ship holds. A teammate Orz's marines
    // aren't counted (UQM's counts every marine of its sheets).
    private static int CountMarines(Battle battle, Starship starShip, bool findSpot, Defines f)
    {
        var idUse = new bool[f.MaxMarines];
        int numMarines = 0;
        var ship = starShip.Ship;
        for (Element? e = battle.List.Tail, next; e != null; e = next)
        {
            next = e.Predecessor;
            if (ReferenceEquals(e.Current.FrameArray, starShip.RaceDesc!.Special)
                && e.LifeSpan != 0
                && (e.StateFlags & (ElementFlags.FiniteLife | ElementFlags.Disappearing)) == 0
                && !(ship != null && Battle.Teammates(e, ship)))
            {
                if ((e.StateFlags & ElementFlags.NonSolid) != 0)
                    idUse[e.TurnWait] = true;
                if (++numMarines == f.MaxMarines)
                    next = null;
            }
        }

        if (findSpot)
        {
            numMarines = 0;
            while (idUse[numMarines])
                ++numMarines;
        }
        return numMarines;
    }

    // orz_intelligence: the stock AI with the turret held still; with an
    // enemy ship it may stop turning while drifting at speed away from an
    // enemy it entices; it turns the turret towards the enemy (special
    // with left or right) when nothing else turns; and launches marines
    // at an enemy without point defence while it has a quarter of its crew,
    // the enemy slow, near or with fewer than 2 marines out.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Defines f)
    {
        var turret = ship.Successor!;

        ++turret.TurnWait;
        battle.ShipIntelligence(ship, concerns, concernCounter);
        --turret.TurnWait;

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object == null)
            starShip.ShipInputState &= ~StatusFlags.Special;
        else if (starShip.SpecialCounter != 1)
        {
            const int Octant = UqmMath.Quadrant >> 1;
            if (ship.TurnWait == 0
                && enemy.MoveState == MoveState.Entice
                && enemy.WhichTurn < 24
                && (starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) != 0
                && (starShip.ShipInputState & StatusFlags.Thrust) == 0
                // Across the seam where contact is tested so (decision 5).
                && UqmMath.NormalizeAngle(ship.Velocity.TravelAngle
                                          - UqmMath.ArcTan(battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X),
                                              battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y))
                                          + (UqmMath.Quadrant - (Octant >> 1)))
                >= (UqmMath.Quadrant - (Octant >> 1)) << 1)
                starShip.ShipInputState &= ~(StatusFlags.Left | StatusFlags.Right);

            starShip.ShipInputState &= ~StatusFlags.Special;
            if (ship.TurnWait == 0
                && (starShip.ShipInputState & (StatusFlags.Left | StatusFlags.Right | StatusFlags.Weapon)) == 0
                && turret.TurnWait == 0)
            {
                ushort facing = (ushort)UqmMath.NormalizeFacing(starShip.ShipFacing + turret.ThrustWait);
                short deltaFacing;
                if ((deltaFacing = battle.TrackShip(turret, ref facing)) > 0)
                {
                    starShip.ShipInputState |= StatusFlags.Special;
                    const int halfCircleFacing = UqmMath.Facings / 2;
                    if (deltaFacing == halfCircleFacing)
                        deltaFacing += (short)((((byte)battle.Random.Next("orz_intelligence") & 1) << 1) - 1);

                    if (deltaFacing < halfCircleFacing)
                        starShip.ShipInputState |= StatusFlags.Right;
                    else
                        starShip.ShipInputState |= StatusFlags.Left;
                }
            }

            var enemyRd = enemy.Object.Parent!.RaceDesc!;
            if (starShip.SpecialCounter == 0
                && (starShip.ShipInputState & StatusFlags.Weapon) == 0
                && rd.CrewLevel > (byte)(rd.MaxCrew >> 2)
                && (enemyRd.ShipFlags & ShipFlags.PointDefense) == 0
                && (enemyRd.ManeuverabilityIndex < Battle.SlowShip
                    || enemy.WhichTurn <= 12
                    || CountMarines(battle, starShip, false, f) < 2))
                starShip.ShipInputState |= StatusFlags.Weapon | StatusFlags.Special;
        }
    }

    // ion_preprocess: a marine's ion trail fades through its colours.
    private static void IonPreprocess(Element e)
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

    // A slot's place on a captain's window.
    private static (short X, short Y) Slot(int slot) => ((short)(16 + (slot & 3) * 9), (short)(14 + (slot >> 2) * 11));

    // intruder_preprocess: a marine aboard dies with its own ship; it
    // follows the ship it boarded; it shows itself back from the fight
    // (its window's frame) the frame after it fought; it leaves (its slot
    // erased from the window) when the ship has no crew, flying free again;
    // every MARINE_WAIT frames it fights: one time in 16 it dies (its
    // sound), else half the time the ship loses a crewman (dying with
    // none), the fight shown on its window (its sound).
    private static void IntruderPreprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if (starShip.Ship is { } own
            && own.CrewLevel == 0
            && own.LifeSpan == 1
            && (own.StateFlags & (ElementFlags.FiniteLife | ElementFlags.NonSolid))
            == (ElementFlags.FiniteLife | ElementFlags.NonSolid))
        {
            e.LifeSpan = 0;
            e.StateFlags |= ElementFlags.Disappearing;
        }

        if (e.ThrustWait != 0)
            --e.ThrustWait;

        // UQM's: the first opposing ship in the list (the one there is);
        // the co-op rule: the ship it boarded, while it's in the battle.
        var ship = ((Aboard)e.Tag!).Ship;
        bool aboard = ship.InList && (ship.StateFlags & ElementFlags.PlayerShip) != 0 && ship.PlayerNr != e.PlayerNr;
        if (aboard)
        {
            var (x, y) = Slot(e.TurnWait);
            var marines = e.Next.FrameArray![0];
            if (e.ThrustWait == f.MarineWait)
            {
                --e.ThrustWait;
                battle.ModifySilhouette(ship, marines.SetAbs(marines.Count - 2), x, y);
            }

            e.Next.X = ship.Next.X;
            e.Next.Y = ship.Next.Y;

            if (ship.CrewLevel == 0 || e.LifeSpan == 0)
            {
                aboard = false;
                battle.ModifySilhouette(ship, e.Next.Frame, x, y, erase: true);
            }
            else if (e.ThrustWait == 0)
            {
                e.ThrustWait = (byte)f.MarineWait;

                byte randval = (byte)battle.Random.Next("intruder_preprocess");
                if (randval < 0x0100 / 16)
                {
                    e.LifeSpan = 0;
                    e.StateFlags |= ElementFlags.Disappearing;
                    // The marine's death sound.
                    battle.ModifySilhouette(ship, e.Next.Frame, x, y, erase: true);
                }
                else if (randval < 0x0100 / 2 + 0x0100 / 16)
                {
                    if (!battle.DeltaCrew(ship, -1))
                        ship.LifeSpan = 0;

                    ++e.ThrustWait;
                    battle.ModifySilhouette(ship, marines.SetAbs(marines.Count - 1), x, y);
                    // The fight's sound.
                }
            }
        }

        if (!aboard && e.LifeSpan != 0)
        {
            e.Tag = null;
            e.StateFlags &= ~ElementFlags.NonSolid;
            e.StateFlags |= ElementFlags.Changing | ElementFlags.CrewObject;
            e.Prim = PrimType.Stamp;

            e.Current.Frame = e.Next.Frame = starShip.RaceDesc!.Special![0].SetAbs(21);
            e.ThrustWait = 0;
            e.TurnWait = MakeByte(0, UqmMath.NormalizeFacing((byte)battle.Random.Next("intruder_preprocess")));
            e.PreprocessFunc = x => MarinePreprocess(battle, x, f);
        }
    }

    private static byte MakeByte(int lo, int hi) => (byte)(((byte)hi << 4) | (byte)lo);

    private static byte LoNibble(byte x) => (byte)(x & 0x0F);

    private static byte HiNibble(byte x) => (byte)(x >> 4);

    // spawn_marine_ion_trail: a dot behind a thrusting marine, at the
    // list's head (stepped already, as it may never be).
    private static void SpawnMarineIonTrail(Battle battle, Element e, Starship starShip, int facing, Defines f)
    {
        var ion = battle.List.Alloc();
        if (ion == null)
            return;
        int angle = UqmMath.FacingToAngle(facing) + UqmMath.HalfCircle;

        battle.List.Insert(ion, battle.List.Head);
        ion.PlayerNr = Battle.NeutralPlayerNum;
        ion.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
        ion.ThrustWait = (byte)f.IonLife;
        ion.LifeSpan = ion.ThrustWait;
        ion.Prim = PrimType.Point;
        ion.PrimColor = StartIonColor;
        ion.ColorCycleIndex = 0;
        ion.Current.X = (short)(e.Current.X + UqmMath.Cosine(angle, Battle.DisplayToWorld(2)));
        ion.Current.Y = (short)(e.Current.Y + UqmMath.Sine(angle, Battle.DisplayToWorld(2)));
        ion.DeathFunc = IonPreprocess;
        ion.Parent = starShip;

        // normally done during preprocess, but at the head it may never be.
        ion.Next = ion.Current;
        --ion.LifeSpan;
        ion.StateFlags |= ElementFlags.PreProcess;
    }

    // Whether an element is a ship of another side than the marine's.
    private static bool Opposing(Element ship, Element marine) =>
        (ship.StateFlags & ElementFlags.PlayerShip) != 0 && ship.PlayerNr != marine.PlayerNr;

    // TrackShip's measure: |dx| + |dy|, the shorter way round.
    private static int Distance(Battle battle, Element a, Element b) =>
        Math.Abs((int)battle.WrapDeltaX(a.Current.X - b.Current.X)) + Math.Abs((int)battle.WrapDeltaY(a.Current.Y - b.Current.Y));

    // marine_preprocess: a free marine dies with its ship; after a bump it
    // waits; else it steers a step: away from the planet when about to
    // fall in (taking the planet when it's ahead), at the nearest opposing
    // ship with crew while outbound (UQM's: the last in the list), or
    // home to its own ship (taking it, its frame turned, when nothing
    // else is left), leading the ship it flies at by its speed; and
    // thrusts (as a ship of its own speed would) when on course, or when
    // it's beyond its top speed and not in a gravity well, leaving a trail.
    private static void MarinePreprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if (starShip.Ship is { } own
            && own.CrewLevel == 0
            && own.LifeSpan == 1
            && (own.StateFlags & (ElementFlags.FiniteLife | ElementFlags.NonSolid))
            == (ElementFlags.FiniteLife | ElementFlags.NonSolid))
        {
            e.LifeSpan = 0;
            e.StateFlags |= ElementFlags.Disappearing | ElementFlags.NonSolid;
            e.TurnWait = 1;
        }

        if (LoNibble(e.TurnWait) != 0)
        {
            --e.TurnWait;
            return;
        }

        int pfacing = 0, deltaFacing;
        e.ThrustWait &= unchecked((byte)~InGravityWell);

        Element? target = null;
        for (Element? o = battle.List.Head, next; o != null; o = next)
        {
            next = o.Successor;
            if (Battle.GravityMass(o.MassPoints))
            {
                short dx = battle.WrapDeltaX(o.Current.X - e.Current.X);
                short dy = battle.WrapDeltaY(o.Current.Y - e.Current.Y);
                long threshold = Battle.DisplayToWorld(255);   // GRAVITY_THRESHOLD
                if ((long)dx * dx + (long)dy * dy <= threshold * threshold)
                {
                    pfacing = UqmMath.AngleToFacing(UqmMath.ArcTan(dx, dy));
                    deltaFacing = UqmMath.NormalizeFacing(pfacing - UqmMath.AngleToFacing(e.Velocity.TravelAngle)
                                                          + UqmMath.AngleToFacing(UqmMath.Quadrant >> 1));
                    if (deltaFacing <= UqmMath.AngleToFacing(UqmMath.Quadrant))
                    {
                        target = o;
                        next = null;
                    }
                    e.ThrustWait |= InGravityWell;
                }
            }
            else if ((o.StateFlags & ElementFlags.PlayerShip) != 0 && o.CrewLevel != 0 && !Battle.ObjectCloaked(o))
            {
                if (o.PlayerNr != e.PlayerNr)
                {
                    // Co-op: the nearest opposing ship, not the last in the list (decision 6).
                    if ((e.StateFlags & ElementFlags.IgnoreSimilar) != 0
                        && (target == null || !Opposing(target, e) || Distance(battle, o, e) < Distance(battle, target, e)))
                        target = o;
                }
                // Co-op: home is its own ship, not a teammate's.
                else if (target == null && o.Parent == starShip)
                    target = o;
            }
        }

        int facing = HiNibble(e.TurnWait);
        if (target == null)
            deltaFacing = -1;
        else
        {
            short dx = battle.WrapDeltaX(target.Current.X - e.Current.X);
            short dy = battle.WrapDeltaY(target.Current.Y - e.Current.Y);
            if (Battle.GravityMass(target.MassPoints))
            {
                deltaFacing = UqmMath.NormalizeFacing(pfacing - facing + UqmMath.AngleToFacing(UqmMath.Quadrant >> 1));

                if (deltaFacing > UqmMath.AngleToFacing(UqmMath.Quadrant))
                    deltaFacing = 0;
                else if (deltaFacing == UqmMath.AngleToFacing(UqmMath.Quadrant >> 1))
                    facing += (((short)battle.Random.Next("marine_preprocess") & 1) << 1) - 1;
                else if (deltaFacing < UqmMath.AngleToFacing(UqmMath.Quadrant >> 1))
                    ++facing;
                else
                    --facing;
            }
            else
            {
                if (target.PlayerNr == e.PlayerNr && (e.StateFlags & ElementFlags.IgnoreSimilar) != 0)
                {
                    e.Next.Frame = starShip.RaceDesc!.Special![0].SetAbs(21);
                    e.StateFlags &= ~ElementFlags.IgnoreSimilar;
                    e.StateFlags |= ElementFlags.Changing;
                }

                ushort numFrames = Battle.WorldToTurn(UqmMath.SquareRoot((uint)((long)dx * dx + (long)dy * dy)));
                if (numFrames == 0)
                    numFrames = 1;

                var shipVelocity = target.Velocity;
                (dx, dy) = shipVelocity.Next(numFrames);

                // Across the seam where contact is tested so (decision 5).
                dx = battle.SeamDeltaX(target.Current.X + dx - e.Current.X);
                dy = battle.SeamDeltaY(target.Current.Y + dy - e.Current.Y);

                deltaFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(dx, dy)) - facing);

                if (deltaFacing > 0)
                {
                    const int halfCircleFacing = UqmMath.Facings / 2;
                    if (deltaFacing == halfCircleFacing)
                        facing += (((byte)battle.Random.Next("marine_preprocess") & 1) << 1) - 1;
                    else if (deltaFacing < halfCircleFacing)
                        ++facing;
                    else
                        --facing;
                }
            }
        }

        e.TurnWait = MakeByte(0, UqmMath.NormalizeFacing(facing));
        if (deltaFacing == 0
            || ((e.ThrustWait & BeyondMaxSpeed) != 0 && (e.ThrustWait & InGravityWell) == 0))
        {
            // UQM's: its ship's STARSHIP lent to inertial_thrust for the marine.
            var rd = starShip.RaceDesc!;
            ushort oldFacing = starShip.ShipFacing;
            var oldStatus = starShip.CurStatusFlags;
            ushort oldIncrement = rd.Characteristics.ThrustIncrement, oldThrust = rd.Characteristics.MaxThrust;

            starShip.ShipFacing = (ushort)facing;
            starShip.CurStatusFlags = (StatusFlags)(e.ThrustWait << 6);
            rd.Characteristics.ThrustIncrement = (ushort)f.MarineThrustIncrement;
            rd.Characteristics.MaxThrust = (ushort)f.MarineMaxThrust;

            var thrustStatus = Battle.InertialThrust(e);

            rd.Characteristics.MaxThrust = oldThrust;
            rd.Characteristics.ThrustIncrement = oldIncrement;
            starShip.CurStatusFlags = oldStatus;
            starShip.ShipFacing = oldFacing;

            if ((e.ThrustWait & InGravityWell) != 0
                || deltaFacing != 0
                || (thrustStatus & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) == 0)
                SpawnMarineIonTrail(battle, e, starShip, facing, f);

            e.ThrustWait = (byte)((int)thrustStatus >> 6);
        }
    }

    // marine_collision: a free marine bumping something solid of another
    // side is stunned 5 frames and slowed; the planet kills it; touching
    // its own ship it comes aboard; touching another ship with crew while
    // outbound, it boards it (killing its last crewman, or taking a slot
    // on its captain's window and fighting there; its sound).
    private static void MarineCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines f)
    {
        if (e0.LifeSpan != 0
            && (e0.StateFlags & (ElementFlags.NonSolid | ElementFlags.Collision)) == 0
            && (e1.StateFlags & ElementFlags.FiniteLife) == 0)
        {
            if (e0.PlayerNr != e1.PlayerNr)
            {
                e0.TurnWait = MakeByte(5, HiNibble(e0.TurnWait));
                e0.ThrustWait &= unchecked((byte)~(AtMaxSpeed | BeyondMaxSpeed));
                e0.StateFlags |= ElementFlags.Collision;
            }

            if (Battle.GravityMass(e1.MassPoints))
            {
                e0.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife;
                e0.HitPoints = 0;
                e0.LifeSpan = 0;
            }
            else if ((e1.StateFlags & ElementFlags.PlayerShip) != 0
                     && ((e1.StateFlags & ElementFlags.FiniteLife) != 0 || e1.LifeSpan == Battle.NormalLife))
            {
                e1.StateFlags &= ~ElementFlags.Collision;

                if ((e0.StateFlags & ElementFlags.Collision) == 0)
                {
                    battle.DeltaCrew(e1, 1);

                    e0.StateFlags |= ElementFlags.Disappearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
                    e0.HitPoints = 0;
                    e0.LifeSpan = 0;
                }
                else if ((e0.StateFlags & ElementFlags.IgnoreSimilar) != 0 && e1.CrewLevel != 0)
                {
                    var starShip = e0.Parent!;
                    if (!battle.DeltaCrew(e1, -1))
                        e1.LifeSpan = 0;
                    else
                    {
                        e0.TurnWait = (byte)CountMarines(battle, starShip, true, f);
                        e0.ThrustWait = (byte)f.MarineWait;
                        e0.Next.Frame = e0.Next.FrameArray![0].SetAbs(22 + e0.TurnWait);
                        e0.StateFlags |= ElementFlags.NonSolid;
                        e0.StateFlags &= ~ElementFlags.CrewObject;
                        e0.Prim = PrimType.None;
                        e0.PreprocessFunc = x => IntruderPreprocess(battle, x, f);
                        e0.Tag = new Aboard(e1);

                        var (x, y) = Slot(e0.TurnWait);
                        battle.ModifySilhouette(e1, e0.Next.Frame, x, y);
                    }
                    // The boarding's sound.
                }

                e0.StateFlags &= ~ElementFlags.Collision;
            }
        }
    }

    // turret_postprocess: the turret, a new element each frame, dying as
    // the next is made behind it: at the ship's place, turning a step with
    // the special and left or right when it may; the ship firing to its
    // side (its FIRES flags); firing, a flash at its muzzle (the turret
    // drawn back with the recoil), only for the ship's own shell just
    // fired; drawn as the ship is. With the weapon and special together,
    // ready, with crew to spare, fewer than MAX_MARINES out and an
    // opposing ship to go for: a marine (a crewman) out of its back.
    private static void TurretPostprocess(Battle battle, Element e, Defines f)
    {
        if (e.LifeSpan != 0)
            return;

        e.Prim = PrimType.None;

        var starShip = e.Parent!;
        if (starShip.Ship is not { } ship)
            return;

        var rd = starShip.RaceDesc!;
        int facing = 0;
        var turret = battle.List.Alloc();
        if (turret != null)
        {
            turret.PlayerNr = e.PlayerNr;
            turret.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                | ElementFlags.Changing | ElementFlags.PreProcess | ElementFlags.PostProcess;
            turret.LifeSpan = 1;
            turret.Current.Frame = e.Current.Frame;
            turret.Current.FrameArray = e.Current.FrameArray;
            turret.Current.X = ship.Next.X;
            turret.Current.Y = ship.Next.Y;
            turret.TurnWait = e.TurnWait;
            turret.ThrustWait = e.ThrustWait;

            if (turret.TurnWait != 0)
                --turret.TurnWait;
            else if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
                     && (starShip.CurStatusFlags & (StatusFlags.Left | StatusFlags.Right)) != 0)
            {
                if ((starShip.CurStatusFlags & StatusFlags.Right) != 0)
                    ++turret.ThrustWait;
                else
                    --turret.ThrustWait;
                turret.TurnWait = (byte)f.TurretWait;
            }
            facing = UqmMath.NormalizeFacing(starShip.ShipFacing + turret.ThrustWait);
            rd.ShipFlags &= ~(ShipFlags.FiresFore | ShipFlags.FiresRight | ShipFlags.FiresAft | ShipFlags.FiresLeft);
            rd.ShipFlags |= (ShipFlags)((int)ShipFlags.FiresFore
                                        << (UqmMath.NormalizeFacing(facing + UqmMath.AngleToFacing(UqmMath.Quadrant >> 1))
                                            / UqmMath.AngleToFacing(UqmMath.Quadrant)));
            turret.Current.Frame = turret.Current.Frame.SetAbs(facing);
            facing = UqmMath.FacingToAngle(facing);
            if ((starShip.CurStatusFlags & StatusFlags.Weapon) != 0)
            {
                var tail = battle.List.Tail!;
                Element? effect;
                if (tail != e
                    && tail.PlayerNr == e.PlayerNr
                    // Co-op: a teammate's shell fired this frame isn't its own (UQM's takes
                    // any of its side's, a blast without a ship among them).
                    && (tail.Parent == null || tail.Parent == starShip)
                    && (tail.StateFlags & ElementFlags.Appearing) != 0
                    && tail.Prim == PrimType.Stamp
                    && (effect = battle.List.Alloc()) != null)
                {
                    turret.Current.X -= UqmMath.Cosine(facing, Battle.DisplayToWorld(2));
                    turret.Current.Y -= UqmMath.Sine(facing, Battle.DisplayToWorld(2));

                    effect.PlayerNr = e.PlayerNr;
                    effect.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar
                                        | ElementFlags.Appearing;
                    effect.LifeSpan = 4;
                    effect.Current.X = (short)(turret.Current.X + UqmMath.Cosine(facing, Battle.DisplayToWorld(f.TurretOffset)));
                    effect.Current.Y = (short)(turret.Current.Y + UqmMath.Sine(facing, Battle.DisplayToWorld(f.TurretOffset)));
                    var special = rd.Special!;
                    effect.Current.FrameArray = special;
                    effect.Current.Frame = special[0].SetAbs(UqmMath.AngleToFacing(UqmMath.FullCircle));
                    effect.PreprocessFunc = Battle.AnimationPreprocess;
                    effect.Parent = starShip;
                    effect.Prim = PrimType.Stamp;
                    battle.List.Put(effect);
                }
            }
            turret.Next = turret.Current;

            turret.Prim = ship.Prim;
            turret.PrimColor = ship.PrimColor;

            turret.PostprocessFunc = e.PostprocessFunc;
            turret.Parent = starShip;
            battle.List.Insert(turret, e.Successor);
        }

        // TrackShip turns UQM's facing, here the turret's angle (or nothing set, without a new turret).
        ushort trackFacing = (ushort)facing;
        Element? marine = null;
        if (starShip.SpecialCounter == 0
            && (starShip.CurStatusFlags & StatusFlags.Special) != 0
            && (starShip.CurStatusFlags & StatusFlags.Weapon) != 0
            && ship.CrewLevel > 1
            && CountMarines(battle, starShip, false, f) < f.MaxMarines)
        {
            // Co-op: a kept target whose crew is gone gives way to the nearest with crew (decision 6).
            battle.DropDeadTarget(ship);
            if (battle.TrackShip(ship, ref trackFacing) >= 0)
                marine = battle.List.Alloc();
        }
        if (marine != null)
        {
            marine.PlayerNr = e.PlayerNr;
            marine.StateFlags = ElementFlags.IgnoreSimilar | ElementFlags.Appearing | ElementFlags.CrewObject;
            marine.LifeSpan = Battle.NormalLife;
            marine.HitPoints = (ushort)f.MarineHitPoints;
            marine.MassPoints = (byte)f.MarineMassPoints;

            int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
            marine.Current.X = (short)(ship.Current.X - UqmMath.Cosine(angle, Battle.DisplayToWorld(f.TurretOffset)));
            marine.Current.Y = (short)(ship.Current.Y - UqmMath.Sine(angle, Battle.DisplayToWorld(f.TurretOffset)));
            var special = rd.Special!;
            marine.Current.FrameArray = special;
            marine.Current.Frame = special[0].SetAbs(20);

            marine.TurnWait = MakeByte(0, UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle + UqmMath.HalfCircle)));
            marine.PreprocessFunc = x => MarinePreprocess(battle, x, f);
            marine.CollisionFunc = (e0, at0, e1, at1) => MarineCollision(battle, e0, at0, e1, at1, f);

            marine.Parent = starShip;
            marine.Prim = PrimType.Stamp;
            battle.List.Put(marine);

            battle.DeltaCrew(ship, -1);
            // The marine's launch sound.

            starShip.SpecialCounter = rd.Characteristics.SpecialWait;
        }
    }

    // orz_preprocess: on warping in, the turret just after the ship in
    // the list; then each frame, the special and left or right turn the
    // turret, not the ship (its turn waits), and the weapon and special
    // together launch a marine, not a shell (its weapon waits).
    private static void Preprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if ((e.StateFlags & ElementFlags.Appearing) == 0)
        {
            if (((starShip.CurStatusFlags | starShip.OldStatusFlags) & StatusFlags.Special) != 0
                && (starShip.CurStatusFlags & (StatusFlags.Left | StatusFlags.Right)) != 0
                && e.TurnWait == 0)
                ++e.TurnWait;

            if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
                && (starShip.CurStatusFlags & StatusFlags.Weapon) != 0
                && starShip.WeaponCounter == 0)
                ++starShip.WeaponCounter;
        }
        else
        {
            var turret = battle.List.Alloc();
            if (turret != null)
            {
                turret.PlayerNr = e.PlayerNr;
                turret.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.IgnoreSimilar;
                turret.LifeSpan = 1;
                var special = starShip.RaceDesc!.Special!;
                turret.Current.FrameArray = special;
                turret.Current.Frame = special[0].SetAbs(starShip.ShipFacing);
                turret.PostprocessFunc = x => TurretPostprocess(battle, x, f);
                turret.Parent = starShip;
                battle.List.Insert(turret, e.Successor);
            }
        }
    }
}
