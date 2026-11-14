// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/vux/vux.c: init_vux, limpet_preprocess, limpet_collision,
// spawn_limpets, initialize_horrific_laser, vux_intelligence,
// vux_postprocess, vux_preprocess), Copyright Paul Reiche, Fred Ford and
// Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// vux.c: the VUX Intruder. Its weapon is a long laser ahead; its
/// special drops a limpet behind it, which homes slowly on the nearest
/// opposing ship (<see cref="Battle.TrackShip"/>) and sticks to the first
/// ship it touches (never a teammate of its own, which it passes: no
/// friendly fire), slowing its turning and thrust for the rest of the
/// battle and leaving a mark on its captain's window
/// (<see cref="Battle.ModifySilhouette"/>, whose place is drawn at random).
/// In an encounter it warps in as any ship (vux_preprocess warps it in
/// near the enemy only in Super Melee).
/// <para>
/// Its AI pursues the enemy ship, avoids its shots (but chases crew), and
/// drops a limpet while turning with the enemy within 12 frames and half
/// its energy. Limpets are its SEEKING_SPECIAL shots: they take the very
/// sheets in <see cref="RaceDesc.Special"/>.
/// </para>
/// </summary>
public sealed class VuxShip : IShipCode
{
    public const string Id = "vux";

    private static readonly int LaserColor = Battle.Rgb15(0x0A, 0x1F, 0x0A);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _slime, _limpet;

    /// <param name="description">The VUX's description (data/ships.json's vux).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="slime">Its weapon's sheets (ship_data.weapon: the slime, big only, whose frames mark a limpet on a ship's captain's window).</param>
    /// <param name="limpet">The limpets' sheets (ship_data.special), the very instance the limpets take (SEEKING_SPECIAL).</param>
    public VuxShip(ShipDescription description, Views? hull, Views? slime, Views? limpet)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _slime = slime;
        _limpet = limpet;
    }

    private sealed record Limpets(int SpecialEnergyCost, int LimpetSpeed, int MinThrustIncrement);

    /// <summary>init_vux: vux_desc with its sheets and vux.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _slime, _limpet);
        var f = new Limpets(d.Define("SPECIAL_ENERGY_COST"), d.Define("LIMPET_SPEED"), d.Define("MIN_THRUST_INCREMENT"));
        var limpet = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("LIMPET_OFFSET"),
            Speed = (short)f.LimpetSpeed, HitPoints = (short)d.Define("LIMPET_HITS"),
            Damage = (short)d.Define("LIMPET_DAMAGE"), Life = (ushort)d.Define("LIMPET_LIFE"),
            PreprocessFunc = e => LimpetPreprocess(battle, e, f), BlastOffs = 0,
        };
        int laserRange = d.Define("LASER_RANGE"), offset = d.Define("VUX_OFFSET");
        rd.Preprocess = e => Preprocess(e);
        rd.InitWeapon = e => InitializeHorrificLaser(battle, e, laserRange, offset);
        rd.Postprocess = e => Postprocess(battle, e, f, limpet);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // limpet_preprocess: it turns straight to its target (no step at a
    // time), keeping its speed, and animates.
    private static void LimpetPreprocess(Battle battle, Element e, Limpets f)
    {
        ushort facing, origFacing;
        facing = origFacing = (ushort)UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
        short deltaFacing;
        if ((deltaFacing = battle.TrackShip(e, ref facing)) > 0)
        {
            facing = (ushort)(origFacing + deltaFacing);
            e.Velocity.SetVector(f.LimpetSpeed, facing);
        }
        e.Next.Frame = e.Next.Frame.Inc();
        e.StateFlags |= ElementFlags.Changing;
    }

    // limpet_collision: on a ship it sticks (its LIMPET_AFFIXES sound),
    // the ship turning and thrusting a frame slower, its thrust a step
    // weaker down to a floor, and the AI working its manoeuvrability out
    // again; a slime frame drawn at random marks its captain's window.
    // The limpet is gone whatever it touched.
    private static void LimpetCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Limpets f)
    {
        if ((e1.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var rd = e1.Parent!.RaceDesc!;
            var c = rd.Characteristics;
            if (++c.TurnWait == 0)
                --c.TurnWait;
            if (++c.ThrustWait == 0)
                --c.ThrustWait;
            if (c.ThrustIncrement <= f.MinThrustIncrement)
                c.MaxThrust = (ushort)(c.ThrustIncrement << 1);
            else
            {
                int numThrusts = c.MaxThrust / c.ThrustIncrement;
                --c.ThrustIncrement;
                c.MaxThrust = (ushort)(c.ThrustIncrement * numThrusts);
            }
            rd.ManeuverabilityIndex = 0;

            var mark = e0.Parent!.RaceDesc!.Weapon![0].SetAbs((ushort)battle.Random.Next("limpet_collision"));
            battle.ModifySilhouette(e1, mark, 0, 0, image: true);
        }

        e0.HitPoints = 0;
        e0.LifeSpan = 0;
        e0.StateFlags |= ElementFlags.Collision | ElementFlags.Disappearing;
    }

    // spawn_limpets: a limpet out of the ship's back (its LAUNCH_LIMPET sound).
    private static void SpawnLimpets(Battle battle, Element e, Limpets f, MissileBlock block)
    {
        var starShip = e.Parent!;
        block.FrameArray = starShip.RaceDesc!.Special!;
        block.Face = (ushort)(starShip.ShipFacing + UqmMath.HalfCircle);
        block.Sender = e.PlayerNr;
        block.Cx = e.Next.X;
        block.Cy = e.Next.Y;
        var limpet = battle.InitializeMissile(block);
        if (limpet != null)
        {
            limpet.CollisionFunc = (e0, at0, e1, at1) => LimpetCollision(battle, e0, at0, e1, at1, f);
            limpet.Parent = starShip;
            battle.List.Put(limpet);
        }
    }

    // initialize_horrific_laser: a laser ahead, LASER_RANGE long.
    private static IReadOnlyList<Element?> InitializeHorrificLaser(Battle battle, Element ship, int laserRange, int offset)
    {
        var starShip = ship.Parent!;
        var block = new LaserBlock
        {
            Face = starShip.ShipFacing, Cx = ship.Next.X, Cy = ship.Next.Y,
            Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)offset, Color = LaserColor,
        };
        block.Ex = UqmMath.Cosine(UqmMath.FacingToAngle(block.Face), laserRange);
        block.Ey = UqmMath.Sine(UqmMath.FacingToAngle(block.Face), laserRange);
        return new[] { battle.InitializeLaser(block) };
    }

    // vux_intelligence: it pursues the enemy ship; an enemy shot it's
    // enticed by it avoids if it's a shot, chases if it's crew; a limpet
    // when ready, the enemy within 12 frames, turning, with half its energy.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        enemy.MoveState = MoveState.Pursue;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
        {
            if ((weapon.Object.StateFlags & ElementFlags.FiniteLife) != 0
                && (weapon.Object.StateFlags & ElementFlags.CrewObject) == 0)
                weapon.MoveState = MoveState.Avoid;
            else
                weapon.MoveState = MoveState.Pursue;
        }

        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        if (starShip.SpecialCounter == 0
            && enemy.Object != null
            && enemy.WhichTurn <= 12
            && (starShip.ShipInputState & (StatusFlags.Left | StatusFlags.Right)) != 0
            && rd.EnergyLevel >= (byte)(rd.MaxEnergy >> 1))
            starShip.ShipInputState |= StatusFlags.Special;
        else
            starShip.ShipInputState &= ~StatusFlags.Special;
    }

    // vux_postprocess: a limpet when pressed, ready and paid for.
    private static void Postprocess(Battle battle, Element e, Limpets f, MissileBlock block)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-f.SpecialEnergyCost))
        {
            SpawnLimpets(battle, e, f, block);
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
        }
    }

    // vux_preprocess: in Super Melee the VUX warps in close to the enemy;
    // every battle here is an encounter (IN_ENCOUNTER), where it warps in
    // as any ship, so its first step only stops its own steps.
    private static void Preprocess(Element e)
    {
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
            e.Parent!.RaceDesc!.Preprocess = null;
    }
}
