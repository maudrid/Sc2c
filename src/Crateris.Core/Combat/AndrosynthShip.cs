// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/androsyn/androsyn.c: init_androsynth, blazer_collision,
// bubble_preprocess, initialize_bubble, androsynth_intelligence,
// androsynth_postprocess, androsynth_preprocess, uninit_androsynth),
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
/// androsyn.c: the Androsynth Guardian. Its weapon is acid bubbles that
/// wander towards the nearest opposing ship (<see cref="Battle.TrackShip"/>,
/// with a random swerve); its special turns the ship into the Blazer, a
/// comet flying at its top speed that rams what it meets, burning energy
/// until it runs out and the ship turns back.
/// <para>
/// Its AI turns into the Blazer to escape shots or to ram a ship it can
/// outlast, and fires bubbles at a ship close or coming its way.
/// </para>
/// </summary>
public sealed class AndrosynthShip : IShipCode
{
    public const string Id = "androsyn";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _bubble, _blazer;

    /// <param name="description">The Androsynth's description (data/ships.json's androsyn).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="bubble">Its bubbles' sheets (ship_data.weapon).</param>
    /// <param name="blazer">The Blazer's sheets (ship_data.special), which the ship takes as it turns.</param>
    public AndrosynthShip(ShipDescription description, Views? hull, Views? bubble, Views? blazer)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _bubble = bubble;
        _blazer = blazer;
    }

    // androsyn.c's #defines its code uses.
    private sealed record Defines(int MaxEnergy, int EnergyRegeneration, int EnergyWait, int ShipMass,
        int MissileSpeed, int TrackWait, int SpecialEnergyCost, int BlazerDegeneration, int BlazerOffset,
        int BlazerThrust, int BlazerTurnWait, int BlazerDamage, int BlazerMass);

    /// <summary>init_androsynth: androsynth_desc with its sheets and androsyn.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _bubble, _blazer);
        var k = new Defines(d.Define("MAX_ENERGY"), d.Define("ENERGY_REGENERATION"), d.Define("ENERGY_WAIT"),
            d.Define("SHIP_MASS"), d.Define("MISSILE_SPEED"), d.Define("TRACK_WAIT"), d.Define("SPECIAL_ENERGY_COST"),
            d.Define("BLAZER_DEGENERATION"), d.Define("BLAZER_OFFSET"), d.Define("BLAZER_THRUST"),
            d.Define("BLAZER_TURN_WAIT"), d.Define("BLAZER_DAMAGE"), d.Define("BLAZER_MASS"));
        var bubble = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("ANDROSYNTH_OFFSET"),
            Speed = (short)k.MissileSpeed, HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => BubblePreprocess(battle, e, k),
        };

        // ANDROSYNTH_DATA: the collision function the ship had before it
        // turned into the Blazer, given back when it turns back.
        ElementCollision? savedCollision = null;
        rd.Preprocess = e => Preprocess(battle, e, k, () => savedCollision);
        rd.Postprocess = e => Postprocess(battle, e, k, c => savedCollision = c);
        rd.InitWeapon = e => InitializeBubble(battle, e, bubble);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, k);
        rd.Uninit = _ => savedCollision = null;
        return rd;
    }

    // blazer_collision: the Blazer does its damage as a shot would (its
    // blast further out), staying as it is, then bumps as a ship.
    private static void BlazerCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines k)
    {
        ushort oldCrewLevel = e0.CrewLevel, oldLife = e0.LifeSpan;
        byte oldOffs = e0.BlastOffset;
        e0.BlastOffset = (byte)k.BlazerOffset;
        e0.MassPoints = (byte)k.BlazerDamage;
        battle.WeaponCollision(e0, at0, e1, at1);
        e0.MassPoints = (byte)k.BlazerMass;
        e0.BlastOffset = oldOffs;
        e0.LifeSpan = oldLife;
        e0.CrewLevel = oldCrewLevel;

        e0.StateFlags &= ~(ElementFlags.Disappearing | ElementFlags.NonSolid);
        battle.Collision(e0, at0, e1, at1);
    }

    // bubble_preprocess: the turn wait's high nibble counts down to its
    // next frame (then a random 0 to 3 more), its low one to its next
    // turn: towards its target with a random swerve of up to half a
    // circle, or anywhere without one.
    private static void BubblePreprocess(Battle battle, Element e, Defines k)
    {
        int thrustWait = e.TurnWait >> 4, turnWait = e.TurnWait & 0xF;
        if (thrustWait > 0)
            --thrustWait;
        else
        {
            e.Next.Frame = e.Current.Frame.Inc();
            e.StateFlags |= ElementFlags.Changing;
            thrustWait = (byte)((ushort)battle.Random.Next("bubble_preprocess") & 3);
        }

        if (turnWait > 0)
            --turnWait;
        else
        {
            const int halfCircleFacing = UqmMath.Facings / 2;
            ushort facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
            int deltaFacing = battle.TrackShip(e, ref facing);
            int f = facing;
            if (deltaFacing == -1)
                f = (ushort)battle.Random.Next("bubble_preprocess");
            else if (deltaFacing <= halfCircleFacing)
                f += (ushort)battle.Random.Next("bubble_preprocess") & (halfCircleFacing - 1);
            else
                f -= (ushort)battle.Random.Next("bubble_preprocess") & (halfCircleFacing - 1);
            e.Velocity.SetVector(k.MissileSpeed, f);
            turnWait = k.TrackWait;
        }

        e.TurnWait = (byte)((turnWait & 0xF) | ((thrustWait & 0xF) << 4));
    }

    // initialize_bubble: a bubble ahead.
    private static IReadOnlyList<Element?> InitializeBubble(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var bubble = battle.InitializeMissile(block);
        if (bubble != null)
            bubble.TurnWait = 0;
        return new[] { bubble };
    }

    // androsynth_intelligence: as the Blazer, no crew to pick up, short-
    // lived shots it's enticed by avoided and others ignored. As the ship,
    // it entices an enemy ship within 16 frames when it can't turn into
    // the Blazer, is low on energy, or the enemy is one to ram; with the
    // special ready, it turns into the Blazer to escape a shot within 4
    // frames, or with a third of its energy against a ship out-ranging it
    // twice, or one within 16 frames it can ram. Not turning, with its
    // weapon ready, it fires at a ship within 4 frames, or within 12 that
    // it isn't pursuing and that comes its way.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Defines k)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;

        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (ReferenceEquals(ship.Next.FrameArray, rd.Special))
        {
            // In Blazer form.
            concerns[Battle.CrewObjectIndex].Object = null;
            if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
            {
                if ((weapon.Object.StateFlags & ElementFlags.FiniteLife) != 0
                    && (weapon.Object.StateFlags & ElementFlags.CrewObject) == 0)
                    weapon.MoveState = MoveState.Avoid;
                else
                    weapon.Object = null;
            }

            battle.ShipIntelligence(ship, concerns, concernCounter);
            return;
        }

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        RaceDesc? enemyRd = null;
        if (enemy.Object != null)
        {
            enemyRd = enemy.Object.Parent!.RaceDesc!;
            if (enemy.WhichTurn <= 16
                && (starShip.SpecialCounter > 0
                    || rd.EnergyLevel < k.MaxEnergy / 3
                    || (enemyRd.WeaponRange <= Battle.CloseRangeWeapon
                        && enemy.Object.CrewLevel > k.BlazerDamage)
                    || (enemy.Object.CrewLevel > k.BlazerDamage * 3
                        && enemyRd.ManeuverabilityIndex > Battle.SlowShip)))
                enemy.MoveState = MoveState.Entice;
        }

        battle.ShipIntelligence(ship, concerns, concernCounter);

        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            if ((weapon.Object != null && weapon.WhichTurn <= 4)
                || (enemy.Object != null
                    && rd.EnergyLevel >= k.MaxEnergy / 3
                    && (enemyRd!.WeaponRange >= rd.WeaponRange << 1
                        || (enemy.WhichTurn < 16
                            && (enemyRd.WeaponRange > Battle.CloseRangeWeapon
                                || enemy.Object.CrewLevel <= k.BlazerDamage)
                            && (enemy.Object.CrewLevel <= k.BlazerDamage * 3
                                || enemyRd.ManeuverabilityIndex <= Battle.SlowShip)))))
                starShip.ShipInputState |= StatusFlags.Special;
        }

        if ((starShip.ShipInputState & StatusFlags.Special) == 0
            && starShip.WeaponCounter == 0
            && enemy.Object != null)
        {
            if (enemy.WhichTurn <= 4)
                starShip.ShipInputState |= StatusFlags.Weapon;
            else if (enemy.MoveState != MoveState.Pursue && enemy.WhichTurn <= 12)
            {
                var (shipDx, shipDy) = ship.Velocity.Current();
                var (otherDx, otherDy) = enemy.Object.Velocity.Current();
                int travelFacing = UqmMath.ArcTan((short)(shipDx - otherDx), (short)(shipDy - otherDy));

                // Across the seam where contact is tested so (decision 5; androsyn.c:353).
                short deltaX = battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X);
                short deltaY = battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y);
                int directionFacing = UqmMath.ArcTan(deltaX, deltaY);

                if (UqmMath.NormalizeAngle(travelFacing - directionFacing + UqmMath.Quadrant / 2) <= UqmMath.Quadrant)
                    starShip.ShipInputState |= StatusFlags.Weapon;
            }
        }
    }

    // androsynth_postprocess: as the Blazer, the special (or running out of
    // energy) has its energy burn away; the special that turned it (its
    // COMET_ON sound) also gives it the Blazer's turning, mass and ramming,
    // keeping the ship's turn wait (as its special wait) and its collision
    // function. Out of energy, it gets a little back to try to turn back.
    private static void Postprocess(Battle battle, Element e, Defines k, Action<ElementCollision?> saveCollision)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if (!ReferenceEquals(e.Next.FrameArray, rd.Special))
            return;

        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 || rd.EnergyLevel == 0)
        {
            rd.Characteristics.EnergyRegeneration = unchecked((byte)k.BlazerDegeneration);
            starShip.EnergyCounter = (byte)k.EnergyWait;

            if ((starShip.CurStatusFlags & StatusFlags.Special) != 0)
            {
                e.TurnWait = 0;
                e.ThrustWait = 0;
                rd.Characteristics.SpecialWait = rd.Characteristics.TurnWait;
                e.MassPoints = (byte)k.BlazerMass;
                rd.Characteristics.TurnWait = (byte)k.BlazerTurnWait;

                // Keep the collision function: it isn't this code's to know.
                saveCollision(e.CollisionFunc);
                e.CollisionFunc = (e0, at0, e1, at1) => BlazerCollision(battle, e0, at0, e1, at1, k);
            }
        }

        if (rd.EnergyLevel == 0)
        {
            // Out of energy as the Blazer: a little more to try to get out of it.
            battle.DeltaEnergy(e, (short)-k.BlazerDegeneration);
            starShip.EnergyCounter = 1;
        }
    }

    // androsynth_preprocess: the ship turns into the Blazer on the special
    // (with the energy; without, it flashes), not firing. The Blazer takes
    // no keys but turning; its turn wait is kept at the Blazer's (a VUX
    // limpet's slowing added to the special wait it gives back); out of
    // energy it stops and turns back into the ship, with the ship's
    // turning, energy, mass and collision; otherwise it flies at its top
    // speed, turning if it may.
    private static void Preprocess(Battle battle, Element e, Defines k, Func<ElementCollision?> savedCollision)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        var curStatusFlags = starShip.CurStatusFlags;
        if (ReferenceEquals(e.Next.FrameArray, rd.Ship))
        {
            if ((curStatusFlags & StatusFlags.Special) != 0)
            {
                if (rd.EnergyLevel < k.SpecialEnergyCost)
                    battle.DeltaEnergy(e, (short)-k.SpecialEnergyCost);   // so the text flashes
                else
                {
                    curStatusFlags &= ~StatusFlags.Weapon;

                    e.Next.FrameArray = rd.Special;
                    e.Next.Frame = Frame.SetEqu(rd.Special![0], e.Next.Frame);
                    e.StateFlags |= ElementFlags.Changing;
                }
            }
        }
        else
        {
            curStatusFlags &= ~(StatusFlags.Thrust | StatusFlags.Weapon | StatusFlags.Special);

            // Protection against the VUX.
            if (rd.Characteristics.TurnWait > k.BlazerTurnWait)
            {
                rd.Characteristics.SpecialWait += (byte)(rd.Characteristics.TurnWait - k.BlazerTurnWait);
                rd.Characteristics.TurnWait = (byte)k.BlazerTurnWait;
            }

            if (rd.EnergyLevel == 0)
            {
                e.Velocity.Zero();
                curStatusFlags &= ~(StatusFlags.Left | StatusFlags.Right
                                    | StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);

                rd.Characteristics.TurnWait = rd.Characteristics.SpecialWait;
                rd.Characteristics.EnergyRegeneration = (byte)k.EnergyRegeneration;
                e.MassPoints = (byte)k.ShipMass;
                e.CollisionFunc = savedCollision();
                e.Next.FrameArray = rd.Ship;
                e.Next.Frame = Frame.SetEqu(rd.Ship![0], e.Next.Frame);
                e.StateFlags |= ElementFlags.Changing;
            }
            else
            {
                if (e.ThrustWait != 0)
                    --e.ThrustWait;
                else
                {
                    int facing = starShip.ShipFacing;
                    if (e.TurnWait == 0 && (curStatusFlags & (StatusFlags.Left | StatusFlags.Right)) != 0)
                    {
                        if ((curStatusFlags & StatusFlags.Left) != 0)
                            --facing;
                        else
                            ++facing;
                    }

                    e.Velocity.SetVector(k.BlazerThrust, UqmMath.NormalizeFacing(facing));
                    curStatusFlags |= StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed;
                }
            }
        }
        starShip.CurStatusFlags = curStatusFlags;
    }
}
