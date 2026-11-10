// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/cyborg.c: PlotIntercept,
// InitCyborg, ship_movement, ship_weapons, ship_intelligence, TurnShip,
// ThrustShip, Pursue, Entice, tactical_intelligence; src/uqm/intel.c:
// computer_intelligence; src/uqm/intel.h, races.h: EVALUATE_DESC),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>How well the AI flies (intel.h's ratings in PlayerControl): the story's enemy is <see cref="Awesome"/>.</summary>
public enum CyborgRating
{
    /// <summary>STANDARD_RATING: aims with a margin of 40 pixels, never uses the special.</summary>
    Standard,
    /// <summary>GOOD_RATING: aims with a margin of 20 pixels.</summary>
    Good,
    /// <summary>AWESOME_RATING: aims at the shapes themselves, and dodges shots.</summary>
    Awesome,
}

/// <summary>MOVEMENT_STATE (races.h): what the AI does about something.</summary>
public enum MoveState : byte
{
    Pursue,
    Avoid,
    Entice,
    NoMovement,
}

/// <summary>
/// EVALUATE_DESC (races.h): something the AI is concerned with, which
/// way it faces or goes, in how many frames it matters, and what to do.
/// </summary>
public struct EvaluateDesc
{
    /// <summary>ObjectPtr: the element, or none.</summary>
    public Element? Object;
    public ushort Facing;
    public ushort WhichTurn;
    public MoveState MoveState;
}

/// <summary>IntelligenceFunc: a ship's own AI (cyborg_control.intelligence_func).</summary>
public delegate void IntelligenceFunc(Element ship, EvaluateDesc[] objectsOfConcern, int concernCounter);

// cyborg.c and intel.c: the computer flying a ship.
public sealed partial class Battle
{
    // intel.h's indices into the objects of concern.
    public const int EnemyShipIndex = 0, CrewObjectIndex = 1, EnemyWeaponIndex = 2, GravityMassIndex = 3,
        FirstEmptyIndex = 4;

    // intel.h.
    public const int CloseRangeWeapon = 50 << OneShift, LongRangeWeapon = 1000 << OneShift;
    public const int FastShip = 150, MediumShip = 45, SlowShip = 25;

    private const int Octant = UqmMath.Quadrant >> 1;

    private static ushort WorldToTurn(int d) => (ushort)(d >> 6);

    private static bool ElementsOfSamePlayer(Element e0, Element e1) => e0.PlayerNr == e1.PlayerNr;

    /// <summary>
    /// computer_intelligence for a ship the computer flies (CYBORG_CONTROL):
    /// <see cref="TacticalIntelligence"/> at <paramref name="rating"/>, or
    /// nothing in the last battle (IN_LAST_BATTLE), where UQM's AI doesn't
    /// fly. For <see cref="Step(Func{Starship, BattleInput})"/>.
    /// </summary>
    public BattleInput ComputerIntelligence(Starship ship, CyborgRating rating = CyborgRating.Awesome,
        bool lastBattle = false)
    {
        ship.Rating = rating;
        return lastBattle ? BattleInput.None : TacticalIntelligence(ship);
    }

    /// <summary>
    /// tactical_intelligence: the AI looks at every element in the battle
    /// and picks what concerns its ship (the nearest enemy ship, crew to
    /// pick up, the enemy shot to dodge first, the planet to keep clear of;
    /// and the nearest asteroid, which only a ship's own AI may act on, as
    /// <see cref="ShipIntelligence"/> stops short of it), then the ship's
    /// own AI decides on the keys,
    /// which come back as the ship's input. It works on a copy of its
    /// ship's element, but the ship's keys (ship_input_state, which it
    /// keeps from frame to frame), its facing (put back after) and, for
    /// <see cref="CyborgRating.Standard"/>, its special's counter are the
    /// ship's own, and it draws from the battle's random numbers.
    /// <para>
    /// UQM chooses the enemy ship as the nearest player ship by
    /// straight-line distance (not TrackShip's |dx| + |dy|), with no side
    /// check: it takes the nearest player ship that CollisionPossible lets
    /// through, which leaves out its own (IGNORE_SIMILAR with the same
    /// STARSHIP) and any non-solid or disappearing, but none by side; with
    /// two sides of one ship each that is the other side's. The port considers only opposing ships
    /// (the co-op rule: with several ships on a side, a teammate isn't an
    /// enemy), which keeps UQM's choice with one ship a side.
    /// </para>
    /// </summary>
    public BattleInput TacticalIntelligence(Starship starShip)
    {
        var rd = starShip.RaceDesc!;
        if (rd.ManeuverabilityIndex == 0)
            InitCyborg(starShip);

        var shipPtr = starShip.Ship!;
        if (rd.CrewLevel == 0 || shipPtr.Prim == PrimType.None)
            return BattleInput.None;

        bool shipMoved = true;
        // The standard AI never uses its special.
        if (starShip.Rating == CyborgRating.Standard)
            ++starShip.SpecialCounter;

        var ship = shipPtr.Copy();
        ushort shipFacing = starShip.ShipFacing;

        var concerns = new EvaluateDesc[FirstEmptyIndex + 1];
        for (int i = 0; i <= FirstEmptyIndex; ++i)
            concerns[i] = new EvaluateDesc { MoveState = MoveState.NoMovement, WhichTurn = ushort.MaxValue };
        const int concernCounter = FirstEmptyIndex;

        var c = rd.Characteristics;
        bool ultraManeuverable = c.ThrustIncrement == c.MaxThrust && rd.ManeuverabilityIndex >= MediumShip;

        if (ship.TurnWait == 0)
        {
            shipMoved = false;
            starShip.ShipInputState &= ~(StatusFlags.Left | StatusFlags.Right);
        }
        if (ship.ThrustWait == 0)
        {
            shipMoved = false;
            starShip.ShipInputState &= ~StatusFlags.Thrust;
        }

        for (var e = List.Head; e != null; e = e.Succ)
        {
            var ed = new EvaluateDesc { Object = e, MoveState = MoveState.NoMovement };
            if (!CollisionPossible(e, ship))
                continue;

            short dx = WrapDeltaX((short)(e.Next.X - ship.Next.X));
            short dy = WrapDeltaY((short)(e.Next.Y - ship.Next.Y));
            ushort Distance() => WorldToTurn(UqmMath.SquareRoot((uint)((long)dx * dx + (long)dy * dy)));

            if (GravityMass(e.MassPoints))
            {
                ushort maneuverTurn = ultraManeuverable ? (ushort)16
                    : rd.ManeuverabilityIndex <= MediumShip ? (ushort)48 : (ushort)32;
                var footprint = FrameZeroShape(ship);
                ushort shipBounds = (ushort)(footprint == null ? 0 : footprint.Width + footprint.Height);

                if (!shipMoved && (ed.WhichTurn = PlotIntercept(e, ship, maneuverTurn,
                        (ushort)DisplayToWorld(30 + shipBounds * 3))) != 0)
                {
                    if (ed.WhichTurn > 1
                        || PlotIntercept(e, ship, 1, (ushort)DisplayToWorld(35 + shipBounds)) != 0
                        || PlotIntercept(e, ship, (ushort)(maneuverTurn << 1), (ushort)DisplayToWorld(40 + shipBounds)) > 1)
                    {
                        ed.Facing = UqmMath.ArcTan((short)-dx, (short)-dy);
                        // Ultra-manoeuvrable ships dodge; the rest try a gravity whip.
                        ed.MoveState = ultraManeuverable ? MoveState.Avoid : MoveState.Entice;
                        concerns[GravityMassIndex] = ed;
                    }
                    else if (!ultraManeuverable && !ship.Velocity.IsZero)
                    {
                        // An orbital insertion: don't thrust.
                        ++ship.ThrustWait;
                        if (ship.TurnWait != 0)
                            shipMoved = true;
                    }
                }
            }
            else if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
            {
                // The co-op side check (UQM has none): a ship of the AI's own side isn't an enemy.
                if (ElementsOfSamePlayer(e, ship))
                    continue;
                var enemy = e.Parent!;
                var enemyRd = enemy.RaceDesc!;
                if (enemyRd.ManeuverabilityIndex == 0)
                    InitCyborg(enemy);

                ed.WhichTurn = Distance();
                if (ed.WhichTurn > concerns[EnemyShipIndex].WhichTurn)
                    continue;
                if (ed.WhichTurn == 0)
                    ed.WhichTurn = 1;

                ref var enemyShip = ref concerns[EnemyShipIndex];
                enemyShip.Object = e;
                enemyShip.Facing = (ushort)UqmMath.FacingToAngle(enemy.ShipFacing);
                enemyShip.WhichTurn = ed.WhichTurn;

                if (shipMoved
                    || e.MassPoints > MaxShipMass
                    || (rd.WeaponRange < LongRangeWeapon
                        && (rd.WeaponRange <= CloseRangeWeapon
                            || (enemyRd.WeaponRange >= LongRangeWeapon
                                && (enemyRd.ShipFlags & ShipFlags.SeekingWeapon) != 0)
                            || (c.MaxThrust < enemyRd.Characteristics.MaxThrust
                                && rd.WeaponRange < enemyRd.WeaponRange))))
                    enemyShip.MoveState = MoveState.Pursue;
                else
                    enemyShip.MoveState = MoveState.Entice;

                if ((enemyRd.ShipFlags & ShipFlags.ImmediateWeapon) != 0 && ShipWeapons(e, ship, 0))
                {
                    ed.WhichTurn = 1;
                    ed.MoveState = MoveState.Avoid;
                    ed.Facing = enemyShip.Facing;
                    concerns[EnemyWeaponIndex] = ed;
                }
            }
            else if (e.Parent == null)
            {
                if ((e.StateFlags & ElementFlags.FiniteLife) == 0)
                {
                    ed.WhichTurn = Distance();
                    if (ed.WhichTurn < concerns[FirstEmptyIndex].WhichTurn)
                    {
                        ed.MoveState = MoveState.Pursue;
                        ed.Facing = e.Velocity.TravelAngle;
                        concerns[FirstEmptyIndex] = ed;
                    }
                }
            }
            else if (!ElementsOfSamePlayer(e, ship)
                     && e.PreprocessFunc != (ElementProcess)CrewPreprocess
                     && concerns[EnemyWeaponIndex].WhichTurn > 1
                     && e.LifeSpan > 0)
            {
                var enemyRd = e.Parent.RaceDesc!;
                bool special = enemyRd.Special != null && ReferenceEquals(e.Next.FrameArray, enemyRd.Special);
                if (((enemyRd.ShipFlags & ShipFlags.SeekingWeapon) != 0 && !special)
                    || ((enemyRd.ShipFlags & ShipFlags.SeekingSpecial) != 0 && special))
                {
                    if (((e.StateFlags & (ElementFlags.FiniteLife | ElementFlags.CrewObject)) == 0
                         && c.MaxThrust > DisplayToWorld(8))
                        || UqmMath.NormalizeAngle(e.Velocity.TravelAngle - UqmMath.ArcTan((short)-dx, (short)-dy)
                                                  + UqmMath.Quadrant) > UqmMath.HalfCircle)
                        ed.WhichTurn = 0;
                    else
                    {
                        ed.WhichTurn = Distance();
                        ed.MoveState = MoveState.Entice;
                        if (ultraManeuverable)
                        {
                            if (ed.WhichTurn == 0)
                                ed.WhichTurn = 1;
                            else if (ed.WhichTurn > 16)
                                ed.WhichTurn = 0;
                        }
                        else if (ed.WhichTurn == 0)
                            ed.WhichTurn = 1;
                        else if (ed.WhichTurn > 16 || (rd.ManeuverabilityIndex > MediumShip && ed.WhichTurn > 8))
                            ed.WhichTurn = 0;
                    }
                }
                else if (starShip.Rating != CyborgRating.Awesome)
                    ed.WhichTurn = 0;
                else
                {
                    ed.WhichTurn = PlotIntercept(e, ship, e.LifeSpan, (ushort)DisplayToWorld(40));
                    ed.MoveState = MoveState.Avoid;
                }

                if (ed.WhichTurn > 0
                    && (ed.WhichTurn < concerns[EnemyWeaponIndex].WhichTurn
                        || (ed.WhichTurn == concerns[EnemyWeaponIndex].WhichTurn && ed.MoveState == MoveState.Avoid)))
                {
                    ed.Facing = e.Velocity.TravelAngle;
                    concerns[EnemyWeaponIndex] = ed;
                }
            }
            else if ((e.StateFlags & ElementFlags.CrewObject) != 0
                     && (((e.StateFlags & ElementFlags.IgnoreSimilar) == 0 && ElementsOfSamePlayer(e, ship))
                         || e.PreprocessFunc == (ElementProcess)CrewPreprocess)
                     && concerns[CrewObjectIndex].WhichTurn > 1)
            {
                ed.WhichTurn = Distance();
                if (ed.WhichTurn == 0)
                    ed.WhichTurn = 1;

                if (concerns[CrewObjectIndex].WhichTurn > ed.WhichTurn
                    && (concerns[EnemyShipIndex].WhichTurn > 32
                        || (concerns[EnemyShipIndex].WhichTurn > 8 && starShip.Ship == e.Target)))
                {
                    ed.MoveState = MoveState.Pursue;
                    ed.Facing = 0;
                    concerns[CrewObjectIndex] = ed;
                }
            }
        }

        var intelligence = rd.Intelligence
                           ?? throw new InvalidOperationException("the AI can't fly a ship without an intelligence_func");
        intelligence(ship, concerns, concernCounter);

        starShip.ShipFacing = shipFacing;
        var input = BattleInput.None;
        if ((starShip.ShipInputState & StatusFlags.Left) != 0)
            input |= BattleInput.Left;
        else if ((starShip.ShipInputState & StatusFlags.Right) != 0)
            input |= BattleInput.Right;
        if ((starShip.ShipInputState & StatusFlags.Thrust) != 0)
            input |= BattleInput.Thrust;
        if ((starShip.ShipInputState & StatusFlags.Weapon) != 0)
            input |= BattleInput.Weapon;
        if ((starShip.ShipInputState & StatusFlags.Special) != 0)
            input |= BattleInput.Special;
        return input;
    }

    // GetFrameRect (SetAbsFrameIndex (IntersectStamp.frame, 0)): the
    // footprint of the first frame of the sheet the ship's intersection
    // tests, none before it has one. InitIntersectFrame takes that frame
    // from next.image.farray[0]: the ship's hull for the ships ported (a
    // ship changing its farray, as the Mmrnmhrm transforming, would need
    // the intersection's own sheet).
    private static IntersectFrame? FrameZeroShape(Element ship) =>
        ship.Intersect.HasFrame && ship.Next.FrameArray != null ? ship.Next.FrameArray[0].Shape : null;

    // InitCyborg: the ship's manoeuvrability, its top speed over its waits.
    private static void InitCyborg(Starship starShip)
    {
        var rd = starShip.RaceDesc!;
        var c = rd.Characteristics;
        ushort index = (ushort)(c.MaxThrust * c.ThrustIncrement);
        ushort divisor = (ushort)(c.TurnWait + c.ThrustWait);
        if (divisor > 0)
            index = (ushort)(index / divisor);
        else
            index >>= 1;
        rd.ManeuverabilityIndex = index;
    }

    /// <summary>
    /// PlotIntercept: in how many frames, up to <paramref name="maxTurns"/>
    /// (and the life left of a short-lived one), the two elements will
    /// meet flying on as they go, within <paramref name="margin"/> world
    /// units, or, with no margin, their shapes touching; 0 if they won't.
    /// Measured from their places as they are, not across the seam, as
    /// UQM's; across it where contact is tested so (<see cref="AcrossSeam"/>),
    /// as the collisions it foresees are.
    /// </summary>
    public ushort PlotIntercept(Element e0, Element e1, ushort maxTurns, ushort margin)
    {
        if (((e0.StateFlags | e1.StateFlags) & ElementFlags.FiniteLife) != 0)
        {
            if ((e0.StateFlags & ElementFlags.FiniteLife) == 0)
            {
                if (e1.LifeSpan < maxTurns)
                    maxTurns = e1.LifeSpan;
            }
            else if ((e1.StateFlags & ElementFlags.FiniteLife) == 0)
            {
                if (e0.LifeSpan < maxTurns)
                    maxTurns = e0.LifeSpan;
            }
            else
            {
                if (e0.LifeSpan < maxTurns)
                    maxTurns = e0.LifeSpan;
                if (e1.LifeSpan < maxTurns)
                    maxTurns = e1.LifeSpan;
            }
        }

        short dst0X = e0.Current.X, dst0Y = e0.Current.Y;
        var (dx0, dy0) = e0.Velocity.Current();
        dx0 = (short)Velocity.VelocityToWorld((int)((long)dx0 * maxTurns));
        dy0 = (short)Velocity.VelocityToWorld((int)((long)dy0 * maxTurns));

        // The second element the shorter way round, where contact is tested across the seam.
        var (seamX, seamY) = SeamShift(e0.Current.X, e0.Current.Y, e1.Current.X, e1.Current.Y);
        short dst1X = (short)(e1.Current.X + seamX), dst1Y = (short)(e1.Current.Y + seamY);
        var (dx1, dy1) = e1.Velocity.Current();
        dx1 = (short)Velocity.VelocityToWorld((int)((long)dx1 * maxTurns));
        dy1 = (short)Velocity.VelocityToWorld((int)((long)dy1 * maxTurns));

        // The frames' rectangles (GetFrameRect: corner at minus the hot spot), none without a frame.
        var r0 = e0.Intersect.HasFrame ? e0.Intersect.Frame : null;
        var r1 = e1.Intersect.HasFrame ? e1.Intersect.Frame : null;

        short timeY0, timeY1;
        if (margin != 0)
        {
            dst1Y = (short)(dst1Y - margin);
            timeY0 = 1;
            timeY1 = (short)(margin << 1);
        }
        else
        {
            dst0Y = (short)(dst0Y + DisplayToWorld(-(r0?.HotSpotY ?? 0)));
            dst1Y = (short)(dst1Y + DisplayToWorld(-(r1?.HotSpotY ?? 0)));
            timeY0 = DisplayToWorld(r0?.Height ?? 0);
            timeY1 = DisplayToWorld(r1?.Height ?? 0);
        }

        short dy = (short)(dst1Y - dst0Y);
        timeY0 = (short)(dy - timeY0 + 1);
        timeY1 = (short)(dy + timeY1 - 1);
        dy = (short)(dy0 - dy1);

        if ((timeY0 <= 0 && timeY1 >= 0) || (timeY0 > 0 && dy >= timeY0) || (timeY1 < 0 && dy <= timeY1))
        {
            short timeX0, timeX1;
            if (margin != 0)
            {
                dst1X = (short)(dst1X - margin);
                timeX0 = 1;
                timeX1 = (short)(margin << 1);
            }
            else
            {
                dst0X = (short)(dst0X + DisplayToWorld(-(r0?.HotSpotX ?? 0)));
                dst1X = (short)(dst1X + DisplayToWorld(-(r1?.HotSpotX ?? 0)));
                timeX0 = DisplayToWorld(r0?.Width ?? 0);
                timeX1 = DisplayToWorld(r1?.Width ?? 0);
            }

            short dx = (short)(dst1X - dst0X);
            timeX0 = (short)(dx - timeX0 + 1);
            timeX1 = (short)(dx + timeX1 - 1);
            dx = (short)(dx0 - dx1);

            if ((timeX0 <= 0 && timeX1 >= 0) || (timeX0 > 0 && dx >= timeX0) || (timeX1 < 0 && dx <= timeX1))
            {
                if (dx == 0 && dy == 0)
                    timeY0 = timeY1 = 0;
                else
                {
                    short t;
                    long timeBeg, timeEnd, fract;

                    if (timeY1 < 0)
                    {
                        t = timeY0;
                        timeY0 = (short)-timeY1;
                        timeY1 = (short)-t;
                    }
                    else if (timeY0 <= 0)
                    {
                        if (dy < 0)
                            timeY1 = (short)-timeY0;
                        timeY0 = 0;
                    }
                    if (dy < 0)
                        dy = (short)-dy;
                    if (dy < timeY1)
                        timeY1 = dy;

                    if (timeX1 < 0)
                    {
                        t = timeX0;
                        timeX0 = (short)-timeX1;
                        timeX1 = (short)-t;
                    }
                    else if (timeX0 <= 0)
                    {
                        if (dx < 0)
                            timeX1 = (short)-timeX0;
                        timeX0 = 0;
                    }
                    if (dx < 0)
                        dx = (short)-dx;
                    if (dx < timeX1)
                        timeX1 = dx;

                    if (dx == 0)
                    {
                        timeBeg = timeY0;
                        timeEnd = timeY1;
                        fract = dy;
                    }
                    else if (dy == 0)
                    {
                        timeBeg = timeX0;
                        timeEnd = timeX1;
                        fract = dx;
                    }
                    else
                    {
                        long timeX = (long)timeX0 * dy, timeY = (long)timeY0 * dx;
                        timeBeg = timeX < timeY ? timeY : timeX;
                        timeX = (long)timeX1 * dy;
                        timeY = (long)timeY1 * dx;
                        timeEnd = timeX > timeY ? timeY : timeX;
                        fract = (long)dx * dy;
                    }

                    if ((timeBeg *= maxTurns) < fract)
                        timeY0 = 0;
                    else
                        timeY0 = (short)(timeBeg / fract);

                    if (timeEnd >= fract)   // just in case of overflow
                        timeY1 = (short)(maxTurns - 1);
                    else
                        timeY1 = (short)(timeEnd * maxTurns / fract);
                }

                if (timeY0 <= timeY1)
                {
                    if (margin != 0)
                        return (ushort)(timeY0 + 1);

                    short pt0X = e0.Current.X, pt0Y = e0.Current.Y;
                    var velocity0 = e0.Velocity;
                    var control0 = e0.Intersect.Copy();
                    short pt1X = (short)(e1.Current.X + seamX), pt1Y = (short)(e1.Current.Y + seamY);
                    var velocity1 = e1.Velocity;
                    var control1 = e1.Intersect.Copy();
                    Move(control1, (short)(seamX >> OneShift), (short)(seamY >> OneShift));

                    if (timeY0 != 0)
                    {
                        (dx0, dy0) = velocity0.Next((ushort)timeY0);
                        pt0X = (short)(pt0X + dx0);
                        pt0Y = (short)(pt0Y + dy0);
                        control0.EndX = WorldToDisplay(pt0X);
                        control0.EndY = WorldToDisplay(pt0Y);

                        (dx1, dy1) = velocity1.Next((ushort)timeY0);
                        pt1X = (short)(pt1X + dx1);
                        pt1Y = (short)(pt1Y + dy1);
                        control1.EndX = WorldToDisplay(pt1X);
                        control1.EndY = WorldToDisplay(pt1Y);
                    }

                    do
                    {
                        ++timeY0;

                        (dx0, dy0) = velocity0.Next(1);
                        pt0X = (short)(pt0X + dx0);
                        pt0Y = (short)(pt0Y + dy0);

                        (dx1, dy1) = velocity1.Next(1);
                        pt1X = (short)(pt1X + dx1);
                        pt1Y = (short)(pt1Y + dy1);

                        control0.OriginX = control0.EndX;
                        control0.OriginY = control0.EndY;
                        control0.EndX = WorldToDisplay(pt0X);
                        control0.EndY = WorldToDisplay(pt0Y);

                        control1.OriginX = control1.EndX;
                        control1.OriginY = control1.EndY;
                        control1.EndX = WorldToDisplay(pt1X);
                        control1.EndY = WorldToDisplay(pt1Y);
                        ushort when = Intersection.DrawablesIntersect(control0, control1, MaxTimeValue);
                        if (when != 0)
                        {
                            if (when == 1 && timeY0 == 1
                                          && ((e0.StateFlags | e1.StateFlags) & ElementFlags.Appearing) != 0)
                            {
                                when = 0;
                                control0.EndX = WorldToDisplay(pt0X);
                                control0.EndY = WorldToDisplay(pt0Y);
                                control1.EndX = WorldToDisplay(pt1X);
                                control1.EndY = WorldToDisplay(pt1Y);
                            }

                            if (when != 0)
                                return (ushort)timeY0;
                        }
                    } while (timeY0 < timeY1);
                }
            }
        }

        return 0;
    }

    // ship_movement: what to do about one concern.
    private void ShipMovement(Element ship, ref EvaluateDesc ed)
    {
        if (ed.WhichTurn == 0)
            ed.WhichTurn = 1;

        switch (ed.MoveState)
        {
            case MoveState.Pursue:
                Pursue(ship, ref ed);
                break;
            case MoveState.Avoid:   // Avoid isn't written (NOTYET): as Entice
            case MoveState.Entice:
                Entice(ship, ref ed);
                break;
        }
    }

    /// <summary>
    /// ship_weapons: whether a shot fired now (the ship's own weapon made,
    /// from where the ship will be next frame) would hit
    /// <paramref name="other"/>, within <paramref name="margin"/> (40 pixels
    /// more for a cloaked one). The shots made to see are freed again.
    /// </summary>
    public bool ShipWeapons(Element shipPtr, Element other, ushort margin)
    {
        if (ObjectCloaked(other))
            margin = (ushort)(margin + DisplayToWorld(40));

        var ship = shipPtr.Copy();
        var (dx, dy) = ship.Velocity.Next(1);
        ship.Next.X = (short)(ship.Current.X + dx);
        ship.Next.Y = (short)(ship.Current.Y + dy);
        ship.Current.X = ship.Next.X;
        ship.Current.Y = ship.Next.Y;

        var weapons = ship.Parent!.RaceDesc!.InitWeapon!(ship);
        int hit = -1;
        for (int i = 0; i < weapons.Count; ++i)
        {
            var w = weapons[i];
            if (w == null)
                continue;
            if ((w.StateFlags & ElementFlags.Appearing) != 0)
            {
                w.Next = w.Current;
                InitIntersectStartPoint(w);
                InitIntersectEndPoint(w);
                InitIntersectFrame(w);
            }
            if (PlotIntercept(w, other, w.LifeSpan, margin) != 0)
            {
                hit = i;
                break;
            }
            List.Free(w);
        }
        if (hit < 0)
            return false;
        for (int i = hit; i < weapons.Count; ++i)
            if (weapons[i] is { } w)
                List.Free(w);
        return true;
    }

    /// <summary>
    /// ship_intelligence: in the concerns' order from the planet down
    /// (the planet, the enemy shot, crew, the enemy ship; not the asteroid
    /// at <see cref="FirstEmptyIndex"/>), the ship moves about the first it
    /// can (once a frame) and fires at the
    /// enemy ship, or a shot it isn't dodging, when its weapon would hit.
    /// Ships' own AIs (<see cref="RaceDesc.Intelligence"/>) call it.
    /// </summary>
    public void ShipIntelligence(Element ship, EvaluateDesc[] objectsOfConcern, int concernCounter)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;

        bool shipMoved = true;
        if (ship.TurnWait == 0)
            shipMoved = false;
        if (ship.ThrustWait == 0)
            shipMoved = false;

        bool shipFired = true;
        if (starShip.WeaponCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Weapon;
            if ((rd.ShipFlags & ShipFlags.SeekingWeapon) == 0)
                shipFired = false;
        }

        ushort margin = starShip.Rating switch
        {
            CyborgRating.Awesome => 0,
            CyborgRating.Good => (ushort)DisplayToWorld(20),
            _ => (ushort)DisplayToWorld(40),
        };

        while (concernCounter-- > 0)
        {
            ref var concern = ref objectsOfConcern[concernCounter];
            if (concern.Object == null)
                continue;
            if (!shipMoved
                && (concernCounter != EnemyWeaponIndex
                    || concern.MoveState == MoveState.Pursue
                    || (concern.Object.StateFlags & ElementFlags.CrewObject) != 0
                    || rd.ManeuverabilityIndex >= MediumShip))
            {
                ShipMovement(ship, ref concern);
                shipMoved = true;
            }
            if (!shipFired
                && (concernCounter == EnemyShipIndex
                    || (concernCounter == EnemyWeaponIndex && concern.MoveState != MoveState.Avoid)))
            {
                shipFired = ShipWeapons(ship, concern.Object!, margin);
                if (shipFired)
                    starShip.ShipInputState |= StatusFlags.Weapon;
            }
        }
    }

    /// <summary>
    /// TurnShip: a facing towards <paramref name="angle"/> (the left or
    /// right key, and the ship's facing and next frame turned); exactly
    /// behind, a random turn either way. Whether it turns.
    /// </summary>
    public bool TurnShip(Element ship, int angle)
    {
        var starShip = ship.Parent!;
        int f = starShip.ShipFacing;
        int halfCircleFacing = UqmMath.AngleToFacing(UqmMath.HalfCircle);
        int shipDeltaFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle) - f);
        if (shipDeltaFacing == 0)
            return false;

        if (shipDeltaFacing == halfCircleFacing)
            shipDeltaFacing = UqmMath.NormalizeFacing(shipDeltaFacing
                + ((Random.Next("TurnShip") & 1) != 0
                    ? UqmMath.AngleToFacing(Octant >> 1)
                    : -UqmMath.AngleToFacing(Octant >> 1)));

        if (shipDeltaFacing < halfCircleFacing)
        {
            starShip.ShipInputState |= StatusFlags.Right;
            ++f;
            ship.Next.Frame = ship.Current.Frame.Inc();
        }
        else
        {
            starShip.ShipInputState |= StatusFlags.Left;
            --f;
            ship.Next.Frame = ship.Current.Frame.Dec();
        }
        starShip.ShipFacing = UqmMath.NormalizeFacing(f);
        return true;
    }

    /// <summary>
    /// ThrustShip: thrust (the key, and the ship's velocity changed) if it
    /// was thrusting, or isn't already going that way at its top speed and
    /// faces <paramref name="angle"/> (or, beyond its top speed, faces
    /// within a half circle of it). Whether it thrusts.
    /// </summary>
    public bool ThrustShip(Element ship, int angle)
    {
        var starShip = ship.Parent!;
        bool shouldThrust;
        if ((starShip.ShipInputState & StatusFlags.Thrust) != 0)
            shouldThrust = true;
        else if (UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle)
                                         - UqmMath.AngleToFacing(ship.Velocity.TravelAngle)) == 0
                 && (starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) != 0
                 && (starShip.CurStatusFlags & StatusFlags.ShipInGravityWell) == 0)
            shouldThrust = false;
        else
        {
            int shipDeltaFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle) - starShip.ShipFacing
                                                          + UqmMath.AngleToFacing(UqmMath.Quadrant));
            shouldThrust = shipDeltaFacing == UqmMath.AngleToFacing(UqmMath.Quadrant)
                           || ((starShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) != 0
                               && shipDeltaFacing <= UqmMath.AngleToFacing(UqmMath.HalfCircle));
        }

        if (shouldThrust)
        {
            InertialThrust(ship);
            starShip.ShipInputState |= StatusFlags.Thrust;
        }
        return shouldThrust;
    }

    // Pursue: head for where the concern will be, catching an enemy ship
    // on its back side when it's dangerous ahead.
    private void Pursue(Element ship, ref EvaluateDesc ed)
    {
        var shipVelocity = ship.Velocity;
        var (shipDx, shipDy) = shipVelocity.Next(ed.WhichTurn);
        ship.Next.X = (short)(ship.Current.X + shipDx);
        ship.Next.Y = (short)(ship.Current.Y + shipDy);

        var other = ed.Object!;
        var otherVelocity = other.Velocity;
        var (otherDx, otherDy) = otherVelocity.Next(ed.WhichTurn);

        short deltaX = WrapDeltaX((short)(other.Current.X + otherDx - ship.Next.X));
        short deltaY = WrapDeltaY((short)(other.Current.Y + otherDy - ship.Next.Y));
        int desiredThrustAngle = UqmMath.ArcTan(deltaX, deltaY);

        var maneuverState = StatusFlags.None;
        if (ship.TurnWait == 0)
            maneuverState |= StatusFlags.Left | StatusFlags.Right;
        if (ship.ThrustWait == 0
            && ((other.StateFlags & ElementFlags.PlayerShip) != 0
                || ElementsOfSamePlayer(other, ship)
                || other.PreprocessFunc == (ElementProcess)CrewPreprocess))
            maneuverState |= StatusFlags.Thrust;

        int desiredTurnAngle = UqmMath.NormalizeAngle(desiredThrustAngle + UqmMath.HalfCircle);
        // the other player's ship
        if ((other.StateFlags & ElementFlags.PlayerShip) != 0 && other.MassPoints <= MaxShipMass)
        {
            var starShip = ship.Parent!;
            var enemyStarShip = other.Parent!;
            var rd = starShip.RaceDesc!;
            var enemyRd = enemyStarShip.RaceDesc!;
            if ((rd.ManeuverabilityIndex >= FastShip && rd.WeaponRange > CloseRangeWeapon)
                || (ed.WhichTurn >= 24
                    && (rd.Characteristics.MaxThrust * 2 / 3 < enemyRd.Characteristics.MaxThrust
                        || (enemyStarShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) != 0)))
            {
                var shipFlags = enemyRd.ShipFlags;
                // you're manoeuvrable
                if (rd.ManeuverabilityIndex >= MediumShip)
                {
                    int facing = ed.Facing;
                    for (var fireFlags = ShipFlags.FiresFore; fireFlags <= ShipFlags.FiresLeft;
                         fireFlags = (ShipFlags)((int)fireFlags << 1), facing += UqmMath.Quadrant)
                    {
                        if ((shipFlags & fireFlags) != 0   // he's dangerous in this direction
                            // he's facing the way you want to go
                            && UqmMath.NormalizeAngle(desiredTurnAngle - facing + Octant) <= UqmMath.Quadrant
                            // he's moving
                            && (otherDx != 0 || otherDy != 0)
                            // he's coasting backwards
                            && UqmMath.NormalizeAngle(otherVelocity.TravelAngle + UqmMath.HalfCircle - facing
                                                      + (Octant + (Octant >> 1))) <= (Octant + (Octant >> 1)) << 1)
                        {
                            // catch him on the back side
                            desiredThrustAngle = desiredTurnAngle;
                            break;
                        }
                    }
                }

                if (desiredThrustAngle != desiredTurnAngle
                    && (otherDx != 0 || otherDy != 0)
                    && ed.WhichTurn >= 24
                    && UqmMath.NormalizeAngle(desiredThrustAngle - otherVelocity.TravelAngle + Octant) <= UqmMath.Quadrant
                    && ((UqmMath.NormalizeAngle(otherVelocity.TravelAngle - shipVelocity.TravelAngle + Octant)
                         <= UqmMath.Quadrant
                         && (((starShip.CurStatusFlags & StatusFlags.ShipAtMaxSpeed) != 0
                              && (starShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) == 0)
                             || (shipFlags & ShipFlags.DontChase) != 0))
                        || UqmMath.NormalizeAngle(desiredTurnAngle - UqmMath.FacingToAngle(starShip.ShipFacing) + Octant)
                        <= UqmMath.Quadrant))
                    desiredThrustAngle = desiredTurnAngle;
            }
        }

        if ((maneuverState & (StatusFlags.Left | StatusFlags.Right)) != 0)
            TurnShip(ship, desiredThrustAngle);
        if ((maneuverState & StatusFlags.Thrust) != 0)
            ThrustShip(ship, desiredThrustAngle);
    }

    // Entice: keep at the weapon's range, flank or dodge a shot, whip round
    // or keep clear of the planet.
    private void Entice(Element ship, ref EvaluateDesc ed)
    {
        var shipVelocity = ship.Velocity;
        var (shipDx, shipDy) = shipVelocity.Next(ed.WhichTurn);
        ship.Next.X = (short)(ship.Current.X + shipDx);
        ship.Next.Y = (short)(ship.Current.Y + shipDy);

        var other = ed.Object!;
        var otherVelocity = other.Velocity;
        var (otherDx, otherDy) = otherVelocity.Next(ed.WhichTurn);

        short deltaX = WrapDeltaX((short)(other.Current.X + otherDx - ship.Next.X));
        short deltaY = WrapDeltaY((short)(other.Current.Y + otherDy - ship.Next.Y));
        int desiredThrustAngle = UqmMath.ArcTan(deltaX, deltaY);

        var maneuverState = StatusFlags.None;
        if (ship.TurnWait == 0)
            maneuverState |= StatusFlags.Left | StatusFlags.Right;
        if (ship.ThrustWait == 0)
            maneuverState |= StatusFlags.Thrust;

        deltaX = (short)(shipDx - otherDx);
        deltaY = (short)(shipDy - otherDy);
        int travelAngle = UqmMath.ArcTan(deltaX, deltaY);
        int desiredTurnAngle = UqmMath.NormalizeAngle(desiredThrustAngle + UqmMath.HalfCircle);

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        int coneOfFire;
        if (ed.MoveState == MoveState.Avoid)
        {
            desiredTurnAngle = UqmMath.NormalizeAngle(desiredTurnAngle - ed.Facing);

            // desired_thrust_angle holds which way to flank for a moment: LEFT or RIGHT.
            if (UqmMath.NormalizeFacing(UqmMath.AngleToFacing(desiredTurnAngle)) != 0)
                desiredThrustAngle = desiredTurnAngle <= UqmMath.HalfCircle ? (int)StatusFlags.Right : (int)StatusFlags.Left;
            else
            {
                desiredTurnAngle = UqmMath.NormalizeAngle(UqmMath.FacingToAngle(starShip.ShipFacing) - ed.Facing);
                if ((desiredTurnAngle & (UqmMath.HalfCircle - 1)) == 0)
                    desiredThrustAngle = (Random.Next("Entice") & 1) != 0 ? (int)StatusFlags.Right : (int)StatusFlags.Left;
                else
                    desiredThrustAngle = desiredTurnAngle < UqmMath.HalfCircle ? (int)StatusFlags.Right : (int)StatusFlags.Left;
            }

            const int flankLeft = -UqmMath.Quadrant, shipLeft = -Octant, flankRight = UqmMath.Quadrant, shipRight = Octant;
            if (desiredThrustAngle == (int)StatusFlags.Left)
                desiredThrustAngle = ed.Facing + flankLeft - (shipLeft >> 1);
            else
                desiredThrustAngle = ed.Facing + flankRight - (shipRight >> 1);

            desiredThrustAngle = UqmMath.NormalizeAngle(desiredThrustAngle);
        }
        else if (GravityMass(other.MassPoints))
        {
            int planetFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(desiredThrustAngle));
            coneOfFire = UqmMath.NormalizeFacing(planetFacing - starShip.ShipFacing
                                                 + UqmMath.AngleToFacing(UqmMath.Quadrant));

            if (rd.Characteristics.ThrustIncrement != rd.Characteristics.MaxThrust)
                maneuverState &= ~StatusFlags.Thrust;

            // if not pointing towards the planet
            if (coneOfFire > UqmMath.AngleToFacing(UqmMath.Quadrant << 1))
                desiredTurnAngle = desiredThrustAngle;
            // if pointing straight at the planet
            else if (coneOfFire == UqmMath.AngleToFacing(UqmMath.Quadrant)
                     && UqmMath.NormalizeFacing(UqmMath.AngleToFacing(travelAngle)) != planetFacing)
                desiredTurnAngle = travelAngle;
            else if (coneOfFire == 0
                     || coneOfFire == UqmMath.AngleToFacing(UqmMath.Quadrant << 1)
                     || ((maneuverState & StatusFlags.Thrust) == 0
                         && (coneOfFire < UqmMath.AngleToFacing(Octant)
                             || coneOfFire > UqmMath.AngleToFacing((UqmMath.Quadrant << 1) - Octant))))
            {
                desiredTurnAngle = UqmMath.FacingToAngle(starShip.ShipFacing);
                if (UqmMath.NormalizeAngle(desiredTurnAngle - travelAngle + UqmMath.Quadrant) > UqmMath.HalfCircle)
                    desiredTurnAngle = travelAngle;
                if (ship.ThrustWait == 0)
                    maneuverState |= StatusFlags.Thrust;
            }

            desiredThrustAngle = desiredTurnAngle;
        }
        else
        {
            int wRange = rd.WeaponRange;

            coneOfFire = UqmMath.NormalizeAngle(desiredTurnAngle - ed.Facing + Octant);
            if ((other.StateFlags & ElementFlags.PlayerShip) != 0)
            {
                var enemyStarShip = other.Parent!;
                var shipFlags = enemyStarShip.RaceDesc!.ShipFlags;
                int facing = ed.Facing;
                for (var fireFlags = ShipFlags.FiresFore; fireFlags <= ShipFlags.FiresLeft;
                     fireFlags = (ShipFlags)((int)fireFlags << 1), facing += UqmMath.Quadrant)
                {
                    if ((shipFlags & fireFlags) != 0   // he's dangerous in this direction
                        // he's facing the way you want to go
                        && (coneOfFire = UqmMath.NormalizeAngle(desiredTurnAngle - facing + Octant)) <= UqmMath.Quadrant
                        // he's moving
                        && (otherDx != 0 || otherDy != 0)
                        // he's coasting backwards
                        && UqmMath.NormalizeAngle(otherVelocity.TravelAngle + UqmMath.HalfCircle - facing + Octant)
                        <= UqmMath.Quadrant)
                    {
                        // need to be close for a kill
                        if (wRange < LongRangeWeapon && ed.WhichTurn <= 32)
                        {
                            // catch him on the back side
                            desiredThrustAngle = desiredTurnAngle;
                            goto DoManeuver;
                        }
                        break;
                    }
                }

                if (ed.WhichTurn <= 8
                    && rd.Characteristics.MaxThrust <= enemyStarShip.RaceDesc.Characteristics.MaxThrust)
                    goto DoManeuver;
            }

            if ((starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) == 0   // not at full speed
                && PlotIntercept(ship, other, 40, (ushort)(CloseRangeWeapon << 1)) != 0)
            {
                // pointed straight at him, or not exposed to the business end
                if (UqmMath.NormalizeAngle(desiredThrustAngle - UqmMath.FacingToAngle(starShip.ShipFacing) + Octant)
                    <= UqmMath.Quadrant
                    || coneOfFire > UqmMath.Quadrant)
                    desiredThrustAngle = desiredTurnAngle;
                else
                {
                    desiredThrustAngle = UqmMath.FacingToAngle(starShip.ShipFacing);
                    desiredTurnAngle = desiredThrustAngle;
                }
            }
            else if ((coneOfFire = PlotIntercept(ship, other, 10, (ushort)(wRange - (wRange >> 2)))) != 0)
            {
                if (rd.Characteristics.ThrustIncrement != rd.Characteristics.MaxThrust
                    // and already at full speed
                    && (starShip.CurStatusFlags & (StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed)) != 0
                    // and facing away from the enemy
                    && (UqmMath.NormalizeAngle(desiredTurnAngle - UqmMath.ArcTan(shipDx, shipDy) + (Octant + 2))
                        <= (Octant + 2) << 1
                        // or not on a collision course
                        || PlotIntercept(ship, other, 30, (ushort)(CloseRangeWeapon << 1)) == 0))
                    maneuverState &= ~StatusFlags.Thrust;
                // veer off
                else if (coneOfFire == 1 || rd.Characteristics.ThrustIncrement != rd.Characteristics.MaxThrust)
                {
                    if ((maneuverState & (StatusFlags.Left | StatusFlags.Right)) != 0)
                    {
                        TurnShip(ship, desiredTurnAngle);
                        maneuverState &= ~(StatusFlags.Left | StatusFlags.Right);
                    }

                    if (UqmMath.NormalizeAngle(desiredThrustAngle - UqmMath.ArcTan(shipDx, shipDy) + (Octant + 2))
                        <= (Octant + 2) << 1)
                        desiredThrustAngle = UqmMath.FacingToAngle(starShip.ShipFacing);
                    else
                        desiredThrustAngle = desiredTurnAngle;
                }
            }
        }

        DoManeuver:
        if ((maneuverState & (StatusFlags.Left | StatusFlags.Right)) != 0)
            TurnShip(ship, desiredThrustAngle);
        if ((maneuverState & StatusFlags.Thrust) != 0)
            ThrustShip(ship, desiredThrustAngle);
    }
}
