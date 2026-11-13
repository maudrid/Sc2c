// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/umgah/umgah.c: init_umgah, cone_preprocess,
// cone_collision, umgah_intelligence, initialize_cone, umgah_postprocess,
// umgah_preprocess, uninit_umgah), Copyright Paul Reiche, Fred Ford and
// Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// umgah.c: the Umgah Drone. Its weapon is an antimatter cone held before
/// the ship for as long as the key is, every frame a new one (its frames
/// animating through the cone's sheet, a set of 16 facings at a time), free
/// of energy; its special, retropropulsion, a jump backwards at once.
/// <para>
/// Its AI holds the cone near the enemy ship and zips backwards away from
/// one behind it.
/// </para>
/// </summary>
public sealed class UmgahShip : IShipCode
{
    public const string Id = "umgah";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _spritz, _cone;

    /// <param name="description">The Umgah's description (data/ships.json's umgah).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="spritz">Its weapon sheets (ship_data.weapon, one size; its cone is drawn from its special's).</param>
    /// <param name="cone">Its cone's sheets (ship_data.special).</param>
    public UmgahShip(ShipDescription description, Views? hull, Views? spritz, Views? cone)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _spritz = spritz;
        _cone = cone;
    }

    // A ship's own state as UQM keeps it: ship_data.special[0] (the RACE_DESC's
    // first special frame, which the cone's code moves through the sheet: the
    // farray's sheets are shared, the frame is this ship's) and UMGAH_DATA's
    // prevFacing (none: no data).
    private sealed class State
    {
        public Frame Special0;
        public ushort? PrevFacing;
    }

    /// <summary>init_umgah: umgah_desc with its sheets and umgah.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _spritz, _cone);
        var state = new State { Special0 = _cone![0] };
        var cone = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("UMGAH_OFFSET"),
            Speed = (short)d.Define("CONE_SPEED"), HitPoints = (short)d.Define("CONE_HITS"),
            Damage = (short)d.Define("CONE_DAMAGE"), Life = (ushort)d.Define("CONE_LIFE"),
            BlastOffs = (short)d.Define("CONE_OFFSET"),
            PreprocessFunc = e => ConePreprocess(e, state),
        };
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"), jumpDist = d.Define("JUMP_DIST"),
            specialWait = d.Define("SPECIAL_WAIT");
        rd.Uninit = _ => state.PrevFacing = null;
        rd.Preprocess = e => Preprocess(battle, e, state, specialEnergyCost, jumpDist, specialWait);
        rd.Postprocess = Postprocess;
        rd.InitWeapon = e => InitializeCone(battle, e, cone, state);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, jumpDist, specialEnergyCost);
        return rd;
    }

    // cone_preprocess: the cone's frame a set of facings on, the cone itself appearing afresh.
    private static void ConePreprocess(Element e, State state)
    {
        state.Special0 = state.Special0.SetRel(UqmMath.AngleToFacing(UqmMath.FullCircle));
        e.StateFlags |= ElementFlags.Appearing;
    }

    // cone_collision: the hit without a blast, the cone held on.
    private static void ConeCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        var blast = battle.WeaponCollision(e0, at0, e1, at1);
        if (blast != null)
        {
            battle.List.Remove(blast);
            battle.List.Free(blast);
            e0.StateFlags &= ~ElementFlags.Disappearing;
        }
    }

    // umgah_intelligence: a shot it's enticed by is let be when it's over
    // 3 frames off or the ship just zipped, avoided if short-lived and not
    // crew, pursued otherwise. With its special waiting, the planet near, or
    // no enemy ship, it pursues at close range and holds the cone within 16
    // frames. Otherwise it plans a zip (a long range for the stock AI) with
    // the energy to cover the distance, when zipping already, the enemy is
    // behind, a slow enemy is over 6 frames off, or it's 16 to 24 frames
    // off; zipping, it doesn't thrust and keeps zipping while the enemy is
    // within the frames it last zipped, or starts with the enemy behind and
    // lined up or near (turning a random way when very near and lined up),
    // and stops zipping to thrust. It holds the cone within 16 frames with
    // the enemy not behind.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        int jumpDist, int specialEnergyCost)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
        {
            if (weapon.WhichTurn > 3 || (starShip.OldStatusFlags & StatusFlags.Special) != 0)
                weapon.Object = null;
            else if ((weapon.Object.StateFlags & ElementFlags.FiniteLife) != 0
                     && (weapon.Object.StateFlags & ElementFlags.CrewObject) == 0)
                weapon.MoveState = MoveState.Avoid;
            else
                weapon.MoveState = MoveState.Pursue;
        }

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter != 0 || concerns[Battle.GravityMassIndex].Object != null || enemy.Object == null)
        {
            rd.WeaponRange = Battle.CloseRangeWeapon;
            battle.ShipIntelligence(ship, concerns, concernCounter);

            if (enemy.WhichTurn < 16)
                starShip.ShipInputState |= StatusFlags.Weapon;
            starShip.ShipInputState &= ~StatusFlags.Special;
        }
        else
        {
            byte thisTurn = enemy.WhichTurn >= 0xFF + 1 ? (byte)0xFF : (byte)enemy.WhichTurn;

            bool enoughJuice = Battle.WorldToTurn(jumpDist * rd.EnergyLevel / specialEnergyCost) > thisTurn;
            // Across the seam where contact is tested so (decision 5; umgah.c:240).
            short deltaX = battle.SeamDeltaX(enemy.Object.Next.X - ship.Next.X);
            short deltaY = battle.SeamDeltaY(enemy.Object.Next.Y - ship.Next.Y);
            const int behind = Octant + (Octant >> 2);
            bool enemyBehind = UqmMath.NormalizeAngle(UqmMath.ArcTan(deltaX, deltaY)
                                                      - (UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle)
                                                      + behind) <= behind << 1;

            var enemyRd = enemy.Object.Parent!.RaceDesc!;
            if (enoughJuice
                && ((starShip.OldStatusFlags & StatusFlags.Special) != 0
                    || enemyBehind
                    || (thisTurn > 6 && enemyRd.ManeuverabilityIndex <= Battle.SlowShip)
                    || (thisTurn >= 16 && thisTurn <= 24)))
                rd.WeaponRange = unchecked((ushort)(Battle.LongRangeWeapon << 3));
            else
                rd.WeaponRange = Battle.CloseRangeWeapon;

            battle.ShipIntelligence(ship, concerns, concernCounter);

            if (rd.WeaponRange == Battle.CloseRangeWeapon)
                starShip.ShipInputState &= ~StatusFlags.Special;
            else
            {
                starShip.ShipInputState &= ~StatusFlags.Thrust;
                bool linedUp = ship.TurnWait == 0
                               && (starShip.OldStatusFlags & (StatusFlags.Left | StatusFlags.Right)) == 0;
                if (((starShip.OldStatusFlags & StatusFlags.Special) != 0
                     && thisTurn <= rd.Characteristics.SpecialWait)
                    || ((starShip.OldStatusFlags & StatusFlags.Special) == 0
                        && enemyBehind && (linedUp || thisTurn < 16)))
                {
                    starShip.ShipInputState |= StatusFlags.Special;
                    rd.Characteristics.SpecialWait = thisTurn;

                    // Don't back straight into the ship.
                    if (thisTurn <= 8 && linedUp)
                    {
                        if ((battle.Random.Next("umgah_intelligence") & 1) != 0)
                            starShip.ShipInputState |= StatusFlags.Left;
                        else
                            starShip.ShipInputState |= StatusFlags.Right;
                    }
                }
                else if ((starShip.OldStatusFlags & StatusFlags.Special) != 0)
                {
                    starShip.ShipInputState &= ~(StatusFlags.Special | StatusFlags.Left | StatusFlags.Right);
                    starShip.ShipInputState |= StatusFlags.Thrust;
                }
            }

            if (thisTurn < 16 && !enemyBehind)
                starShip.ShipInputState |= StatusFlags.Weapon;
        }

        if ((starShip.ShipInputState & StatusFlags.Special) == 0)
            rd.Characteristics.SpecialWait = 0xFF;
    }

    private const int Octant = UqmMath.Quadrant >> 1;

    // initialize_cone: a cone at the ship for its facing, in the frame the
    // cone has got to (kept while the facing is, put back to the facing's
    // first when it turns); set up for contact at once.
    private static IReadOnlyList<Element?> InitializeCone(Battle battle, Element ship, MissileBlock block, State state)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Special!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;

        // Called every frame the weapon is held: the cone's frame isn't put back each time.
        if (state.PrevFacing is not { } prev || starShip.ShipFacing != prev)
        {
            state.PrevFacing = starShip.ShipFacing;
            state.Special0 = state.Special0.SetAbs(starShip.ShipFacing);
        }

        block.Index = state.Special0.Index;
        var cone = battle.InitializeMissile(block);
        if (cone != null)
        {
            cone.CollisionFunc = (e0, at0, e1, at1) => ConeCollision(battle, e0, at0, e1, at1);
            cone.StateFlags &= ~ElementFlags.Appearing;
            cone.Next = cone.Current;
            Battle.InitIntersectStartPoint(cone);
            Battle.InitIntersectEndPoint(cone);
            cone.IntersectStamp = state.Special0;
            cone.Intersect.Frame = state.Special0.Shape;
        }
        return new[] { cone };
    }

    // umgah_postprocess: a zip lasts a frame: then the ship stops.
    private static void Postprocess(Element e)
    {
        var starShip = e.Parent!;
        if (starShip.SpecialCounter > 0)
        {
            starShip.SpecialCounter = 0;
            e.Velocity.Zero();
        }
    }

    // umgah_preprocess: warping in, its data cleared; then, with the
    // special held, its thrust's wait over and the energy paid, a jump
    // backwards (its ZIP_BACKWARDS sound).
    private static void Preprocess(Battle battle, Element e, State state, int specialEnergyCost, int jumpDist, int specialWait)
    {
        var starShip = e.Parent!;
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
        {
            state.PrevFacing = null;   // reset, just in case
            return;
        }
        if (e.ThrustWait == 0
            && (starShip.CurStatusFlags & StatusFlags.Special) != 0
            && battle.DeltaEnergy(e, (short)-specialEnergyCost))
        {
            int facing = UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle;
            int jump = Velocity.WorldToVelocity(jumpDist);
            e.Velocity.Delta(UqmMath.Cosine(facing, jump), UqmMath.Sine(facing, jump));
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
            starShip.SpecialCounter = (byte)specialWait;
        }
    }
}
