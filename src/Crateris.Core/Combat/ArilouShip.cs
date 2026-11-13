// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/arilou/arilou.c: init_arilou, initialize_autoaim_laser,
// arilou_intelligence, arilou_preprocess), Copyright Paul Reiche, Fred
// Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// arilou.c: the Arilou Lalee'lay Skiff. It flies without inertia (it
/// stops when it stops thrusting); its weapon is a laser that aims itself a
/// step towards the nearest opposing ship (<see cref="Battle.TrackShip"/>);
/// its special teleports it: non-solid for a few frames, drawn with its
/// warp sheet, to a random place across the whole arena
/// (<see cref="Battle.RandomX"/>, the arena's size, as UQM's across its
/// own).
/// <para>
/// Its AI entices, always thrusting, and teleports from a shot about to hit
/// one time in four.
/// </para>
/// </summary>
public sealed class ArilouShip : IShipCode
{
    public const string Id = "arilou";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _warp;

    /// <param name="description">The Arilou's description (data/ships.json's arilou).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="warp">
    /// Its teleport's sheets (ship_data.special), the very instance the
    /// teleporting ship takes: its SEEKING_SPECIAL flag makes the AI tell
    /// special elements by it (<see cref="RaceDesc.Special"/>).
    /// </param>
    public ArilouShip(ShipDescription description, Views? hull, Views? warp)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _warp = warp;
    }

    /// <summary>init_arilou: arilou_desc with its sheets and arilou.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        // Its laser draws no sheets (ship_data.weapon is NULL_RESOURCE).
        var rd = RaceShips.NewRaceDesc(d, _hull, null, _warp);
        int offset = d.Define("ARILOU_OFFSET"), range = d.Define("LASER_RANGE"),
            specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"), hyperLife = d.Define("HYPER_LIFE");
        rd.Preprocess = e => Preprocess(battle, e, specialEnergyCost, hyperLife);
        rd.InitWeapon = e => InitializeAutoaimLaser(battle, e, offset, range);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, specialEnergyCost);
        return rd;
    }

    // initialize_autoaim_laser: a laser a facing nearer the nearest opposing ship.
    private static IReadOnlyList<Element?> InitializeAutoaimLaser(Battle battle, Element ship, int offset, int range)
    {
        var starShip = ship.Parent!;
        ushort origFacing = starShip.ShipFacing, face = origFacing;
        short deltaFacing = battle.TrackShip(ship, ref face);
        if (deltaFacing > 0)
            face = UqmMath.NormalizeFacing(origFacing + deltaFacing);
        ship.Target = null;

        int angle = UqmMath.FacingToAngle(face);
        var block = new LaserBlock
        {
            Face = face, Cx = ship.Next.X, Cy = ship.Next.Y,
            Ex = UqmMath.Cosine(angle, range), Ey = UqmMath.Sine(angle, range),
            Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)offset,
            Color = Battle.Rgb15(0x1F, 0x1F, 0x0A),
        };
        return new[] { battle.InitializeLaser(block) };
    }

    // arilou_intelligence: always thrusting, enticing the enemy; with its
    // special ready, a teleport one time in four from a shot about to hit
    // (an immediate weapon, a homing shot about to, fighters, or one
    // PlotIntercept says will within 3 frames); no firing when low on energy.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        int specialEnergyCost)
    {
        var starShip = ship.Parent!;
        starShip.ShipInputState |= StatusFlags.Thrust;

        concerns[Battle.EnemyShipIndex].MoveState = MoveState.Entice;
        battle.ShipIntelligence(ship, concerns, concernCounter);

        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;

            ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
            if (weapon.Object != null && weapon.WhichTurn <= 6)
            {
                var o = weapon.Object;
                var enemyRd = o.Parent!.RaceDesc!;
                bool isTrackingWeapon =
                    ((enemyRd.ShipFlags & ShipFlags.SeekingWeapon) != 0 && ReferenceEquals(o.Next.FrameArray, enemyRd.Weapon))
                    || ((enemyRd.ShipFlags & ShipFlags.SeekingSpecial) != 0 && ReferenceEquals(o.Next.FrameArray, enemyRd.Special));

                // A ship means an immediate weapon; a crew object homing, fighters.
                if (((o.StateFlags & ElementFlags.PlayerShip) != 0
                     || (isTrackingWeapon && (weapon.WhichTurn == 1 || (o.StateFlags & ElementFlags.CrewObject) != 0))
                     || battle.PlotIntercept(o, ship, 3, 0) != 0)
                    && (battle.Random.Next("arilou_intelligence") & 3) == 0)
                {
                    starShip.ShipInputState &= ~(StatusFlags.Left | StatusFlags.Right | StatusFlags.Thrust | StatusFlags.Weapon);
                    starShip.ShipInputState |= StatusFlags.Special;
                }
            }
        }
        if (starShip.RaceDesc!.EnergyLevel <= specialEnergyCost << 1)
            starShip.ShipInputState &= ~StatusFlags.Weapon;
    }

    // arilou_preprocess: no inertia (it stops when its thrust's wait is
    // over); the special starts a teleport: non-solid and finite-lived,
    // drawn with the warp sheet, whose frames go up, the ship moved to a
    // random place halfway, then down, and it appears there whole again.
    // While teleporting its keys are last frame's and its counters wait.
    private static void Preprocess(Battle battle, Element e, int specialEnergyCost, int hyperLife)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if ((e.StateFlags & ElementFlags.NonSolid) == 0)
        {
            if (e.ThrustWait == 0)
            {
                e.Velocity.Zero();
                starShip.CurStatusFlags &= ~StatusFlags.ShipAtMaxSpeed;
            }

            if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
                && starShip.SpecialCounter == 0
                && battle.DeltaEnergy(e, (short)-specialEnergyCost))
            {
                // Start the teleport (its HYPERJUMP sound).
                e.Velocity.Zero();
                starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.Left | StatusFlags.Right
                                             | StatusFlags.Thrust | StatusFlags.Weapon);
                e.StateFlags |= ElementFlags.NonSolid | ElementFlags.FiniteLife | ElementFlags.Changing;
                e.LifeSpan = (ushort)hyperLife;
                e.Next.FrameArray = rd.Special;
                e.Next.Frame = rd.Special![0];
                starShip.SpecialCounter = rd.Characteristics.SpecialWait;
            }
        }
        else if (ReferenceEquals(e.Next.FrameArray, rd.Special))
        {
            const StatusFlags keys = StatusFlags.Keys;
            starShip.CurStatusFlags = (starShip.CurStatusFlags & ~keys) | (starShip.OldStatusFlags & keys);
            ++starShip.WeaponCounter;
            ++starShip.SpecialCounter;
            ++starShip.EnergyCounter;
            ++e.TurnWait;
            ++e.ThrustWait;

            int lifeSpan = e.LifeSpan;
            if (lifeSpan == Battle.NormalLife)
            {
                // The teleport ends.
                e.StateFlags &= ~(ElementFlags.NonSolid | ElementFlags.FiniteLife);
                e.StateFlags |= ElementFlags.Appearing;
                e.Current.FrameArray = e.Next.FrameArray = rd.Ship;
                e.Current.Frame = e.Next.Frame = rd.Ship![0].SetAbs(starShip.ShipFacing);
                Battle.InitIntersectStartPoint(e);
            }
            else
            {
                // Teleporting.
                --lifeSpan;
                if (lifeSpan != 2)
                    e.Next.Frame = lifeSpan < 2 ? e.Next.Frame.Dec() : e.Next.Frame.Inc();
                else
                {
                    // Anywhere in the arena: the battle's own size (decision 4), UQM's in UQM's.
                    e.Next.X = battle.RandomX("arilou_preprocess");
                    e.Next.Y = battle.RandomY("arilou_preprocess");
                }
            }

            e.StateFlags |= ElementFlags.Changing;
        }
    }
}
