// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/blackurq/blackurq.c: init_black_urquan, spin_preprocess,
// buzztrack_preprocess, decelerate_preprocess, splinter_preprocess,
// buzzsaw_collision, buzzsaw_preprocess, buzzsaw_postprocess,
// initialize_buzzsaw, black_urquan_intelligence, gas_cloud_preprocess,
// gas_cloud_collision, spawn_gas_cloud, black_urquan_postprocess,
// black_urquan_preprocess), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// blackurq.c: the Kohr-Ah Marauder. Its weapon is a spinning blade that
/// flies while the key is held, then slows to a stop and drifts towards
/// the nearest opposing ship (<see cref="Battle.TrackShip"/>) within
/// reach, up to eight at once; its special, the F.R.I.E.D., a ring of
/// burning gas round the ship.
/// <para>
/// Its AI lets a blade go when it would pass the enemy ship, and burns
/// the gas when a shot or the enemy ship is near.
/// </para>
/// </summary>
public sealed class KohrAhShip : IShipCode
{
    public const string Id = "blackurq";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _saw, _gas;

    /// <param name="description">The Kohr-Ah's description (data/ships.json's blackurq).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="saw">Its blades' sheets (ship_data.weapon): spinning, then splintering.</param>
    /// <param name="gas">Its gas clouds' sheets (ship_data.special).</param>
    public KohrAhShip(ShipDescription description, Views? hull, Views? saw, Views? gas)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _saw = saw;
        _gas = gas;
    }

    // blackurq.c's #defines its code uses.
    private sealed record Defines(int MissileLife, int SawRate, int MaxSaws, int ActivateRange, int TrackWait,
        int FragmentRange, int SpecialEnergyCost, int SpecialWait, int GasOffset, int GasSpeed, int GasRate, int GasHits,
        int GasDamage, int GasAltDamage, int NumGasClouds, int LastSpinIndex);

    /// <summary>init_black_urquan: black_urquan_desc with its sheets and blackurq.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _saw, _gas);
        var k = new Defines(d.Define("MISSILE_LIFE"), d.Define("SAW_RATE"), d.Define("MAX_SAWS"),
            d.Define("ACTIVATE_RANGE"), d.Define("TRACK_WAIT"), d.Define("FRAGMENT_RANGE"), d.Define("SPECIAL_ENERGY_COST"),
            d.Define("SPECIAL_WAIT"), d.Define("GAS_OFFSET"), d.Define("GAS_SPEED"), d.Define("GAS_RATE"),
            d.Define("GAS_HITS"), d.Define("GAS_DAMAGE"), d.Define("GAS_ALT_DAMAGE"), d.Define("NUM_GAS_CLOUDS"),
            d.Define("LAST_SPIN_INDEX"));
        var saw = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("KOHR_AH_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)k.MissileLife,
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => BuzzsawPreprocess(battle, e, k),
        };
        rd.Preprocess = Preprocess;
        rd.Postprocess = e => Postprocess(battle, e, k);
        rd.InitWeapon = e => InitializeBuzzsaw(battle, e, saw, k);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, k);
        return rd;
    }

    // spin_preprocess: each blade counts itself in the special wait (while
    // the ship has crew), and those past the MAX_SAWS-th go; the rest live
    // on and spin.
    private static void SpinPreprocess(Element e, Defines k)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        var ship = starShip.Ship;
        if (ship != null && ship.CrewLevel != 0 && ++rd.Characteristics.SpecialWait > k.MaxSaws)
        {
            e.LifeSpan = 1;
            e.StateFlags |= ElementFlags.Disappearing;
        }
        else
        {
            ++e.LifeSpan;
            if (e.TurnWait != 0)
                --e.TurnWait;
            else
            {
                e.Next.Frame = e.Current.Frame.Index < k.LastSpinIndex
                    ? e.Current.Frame.Inc()
                    : e.Current.Frame.SetAbs(0);
                e.StateFlags |= ElementFlags.Changing;

                e.TurnWait = (byte)k.SawRate;
            }
        }
    }

    // buzztrack_preprocess: a stopped blade, every TRACK_WAIT + 1 frames,
    // drifts towards its target (kept once found, the nearest opposing
    // ship) if it's within ACTIVATE_RANGE pixels, and is still otherwise.
    private static void BuzztrackPreprocess(Battle battle, Element e, Defines k)
    {
        if (e.ThrustWait != 0)
            --e.ThrustWait;
        else
        {
            ushort facing = 0;
            if (e.Target == null && battle.TrackShip(e, ref facing) < 0)
                e.Velocity.Zero();
            else
            {
                var target = e.Target!;
                int dx = battle.WrapDeltaX(target.Current.X - e.Current.X);
                int dy = battle.WrapDeltaY(target.Current.Y - e.Current.Y);
                facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan((short)dx, (short)dy)));

                if (dx < 0)
                    dx = -dx;
                if (dy < 0)
                    dy = -dy;
                dx = Battle.WorldToDisplay((short)dx);
                dy = Battle.WorldToDisplay((short)dy);
                if (dx >= k.ActivateRange
                    || dy >= k.ActivateRange
                    || (uint)((ushort)dx * dx) + (uint)((ushort)dy * dy) >= (uint)(k.ActivateRange * k.ActivateRange))
                    e.Velocity.Zero();
                else
                {
                    e.ThrustWait = (byte)k.TrackWait;
                    e.Velocity.SetVector(Battle.DisplayToWorld(2), facing);
                }
            }
        }

        SpinPreprocess(e, k);
    }

    // decelerate_preprocess: a let-go blade halves its speed each frame,
    // and stopped, starts tracking.
    private static void DeceleratePreprocess(Battle battle, Element e, Defines k)
    {
        var (dx, dy) = e.Velocity.Current();
        dx /= 2;
        dy /= 2;
        e.Velocity.SetComponents(dx, dy);
        if (dx == 0 && dy == 0)
            e.PreprocessFunc = b => BuzztrackPreprocess(battle, b, k);

        SpinPreprocess(e, k);
    }

    // splinter_preprocess: a broken blade's splinters, a frame each frame.
    private static void SplinterPreprocess(Element e)
    {
        e.Next.Frame = e.Current.Frame.Inc();
        e.StateFlags |= ElementFlags.Changing;
    }

    // buzzsaw_collision: the hit; a blade that would go splinters over
    // five frames instead.
    private static void BuzzsawCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1)
    {
        battle.WeaponCollision(e0, at0, e1, at1);

        if ((e0.StateFlags & ElementFlags.Disappearing) != 0)
        {
            e0.StateFlags &= ~ElementFlags.Disappearing;
            e0.StateFlags |= ElementFlags.NonSolid | ElementFlags.Changing;
            e0.LifeSpan = 5;
            e0.Next.Frame = e0.Current.Frame.SetAbs(2);

            e0.PreprocessFunc = SplinterPreprocess;
        }
    }

    // buzzsaw_preprocess: a blade flies on while the weapon is held; let
    // go, half its life is gone and it slows down.
    private static void BuzzsawPreprocess(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Weapon) == 0)
        {
            e.LifeSpan >>= 1;
            e.PreprocessFunc = b => DeceleratePreprocess(battle, b, k);
        }

        SpinPreprocess(e, k);
    }

    // buzzsaw_postprocess: a new blade, once out, goes to just behind its
    // ship in the list (a copy, already moved for the frame, taking its
    // place; the blade itself goes), so the ship's AI finds it there.
    private static void BuzzsawPostprocess(Battle battle, Element e)
    {
        e.PostprocessFunc = null;
        var copy = battle.List.Alloc();
        if (copy == null)
            return;
        copy.CopyFrom(e);
        copy.Current = copy.Next;
        Battle.InitIntersectStartPoint(copy);
        Battle.InitIntersectEndPoint(copy);
        copy.StateFlags = (copy.StateFlags & ~(ElementFlags.PreProcess | ElementFlags.Changing | ElementFlags.Appearing))
                          | ElementFlags.PostProcess;

        var ship = e.Parent!.Ship!;
        battle.List.Insert(copy, ship.Successor);

        e.LifeSpan = 0;
    }

    // initialize_buzzsaw: a blade ahead.
    private static IReadOnlyList<Element?> InitializeBuzzsaw(Battle battle, Element ship, MissileBlock block, Defines k)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var saw = battle.InitializeMissile(block);
        if (saw != null)
        {
            saw.TurnWait = (byte)k.SawRate;
            saw.ThrustWait = 0;
            saw.PostprocessFunc = s => BuzzsawPostprocess(battle, s);
            saw.CollisionFunc = (e0, at0, e1, at1) => BuzzsawCollision(battle, e0, at0, e1, at1);
        }
        return new[] { saw };
    }

    // black_urquan_intelligence: crew within 8 frames that it's enticed by
    // is pursued; with the special ready and the energy, the gas for a
    // shot within 8 frames. With an enemy ship: with a fresh blade of its
    // own out (the weapon held), it lets go when the blade would pass the
    // ship (holding it otherwise); with none, it fires when a blade would
    // reach, and, not firing, burns the gas for a ship within 8 frames
    // while it turns.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Defines k)
    {
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null
            && weapon.MoveState == MoveState.Entice
            && (weapon.Object.StateFlags & ElementFlags.CrewObject) != 0
            && weapon.WhichTurn <= 8)
            weapon.MoveState = MoveState.Pursue;

        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        starShip.ShipInputState &= ~StatusFlags.Special;

        if (starShip.SpecialCounter == 0
            && rd.EnergyLevel >= k.SpecialEnergyCost
            && weapon.Object != null
            && weapon.WhichTurn <= 8)
            starShip.ShipInputState |= StatusFlags.Special;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object == null)
            return;

        Element? h = (starShip.OldStatusFlags & StatusFlags.Weapon) != 0 ? ship.Successor : null;
        while (h != null)
        {
            var saw = h;
            var next = saw.Successor;
            // Its own blades only: a teammate's (of its side, another
            // ship's) aren't, as UQM's side has one ship.
            if ((saw.StateFlags & ElementFlags.NonSolid) == 0
                && ReferenceEquals(saw.Next.FrameArray, rd.Weapon)
                && saw.LifeSpan > k.MissileLife * 3 / 4
                && saw.PlayerNr == ship.PlayerNr && !Battle.Teammates(saw, ship))
            {
                if (battle.PlotIntercept(saw, enemy.Object, saw.LifeSpan, (ushort)(k.FragmentRange / 2)) == 0)
                    starShip.ShipInputState &= ~StatusFlags.Weapon;
                else if (starShip.WeaponCounter == 0)
                    starShip.ShipInputState |= StatusFlags.Weapon;
                break;
            }
            h = next;
        }

        if (h == null)
        {
            if ((starShip.OldStatusFlags & StatusFlags.Weapon) != 0)
                starShip.ShipInputState &= ~StatusFlags.Weapon;
            else if (starShip.WeaponCounter == 0
                     && battle.ShipWeapons(ship, enemy.Object, (ushort)(k.FragmentRange / 2)))
                starShip.ShipInputState |= StatusFlags.Weapon;

            if (starShip.SpecialCounter == 0
                && (starShip.ShipInputState & StatusFlags.Weapon) == 0
                && enemy.WhichTurn <= 8
                && (starShip.ShipInputState & (StatusFlags.Left | StatusFlags.Right)) != 0
                && rd.EnergyLevel >= k.SpecialEnergyCost)
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // gas_cloud_preprocess: the next frame every GAS_RATE + 1 frames.
    private static void GasCloudPreprocess(Element e, int gasRate)
    {
        if (e.TurnWait != 0)
            --e.TurnWait;
        else
        {
            e.Next.Frame = e.Current.Frame.Inc();
            e.StateFlags |= ElementFlags.Changing;

            e.TurnWait = (byte)gasRate;
        }
    }

    // gas_cloud_collision: the gas burns a ship for GAS_DAMAGE, anything
    // else for GAS_ALT_DAMAGE.
    private static void GasCloudCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines k)
    {
        e0.MassPoints = (byte)((e1.StateFlags & ElementFlags.PlayerShip) != 0 ? k.GasDamage : k.GasAltDamage);
        battle.WeaponCollision(e0, at0, e1, at1);
    }

    // spawn_gas_cloud: a ring of gas clouds round the ship, carried along
    // by its speed.
    private static void SpawnGasCloud(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        var special = starShip.RaceDesc!.Special!;
        var block = new MissileBlock
        {
            Cx = e.Next.X, Cy = e.Next.Y, FrameArray = special, Index = 0, Sender = e.PlayerNr,
            Flags = ElementFlags.IgnoreSimilar, PixOffs = 20, Speed = (short)k.GasSpeed, HitPoints = (short)k.GasHits,
            Damage = (short)k.GasDamage, Life = (ushort)(special[0].Count * (k.GasRate + 1) - 1),
            PreprocessFunc = g => GasCloudPreprocess(g, k.GasRate), BlastOffs = (short)k.GasOffset,
        };

        var (dx, dy) = e.Velocity.Current();
        for (block.Face = 0; block.Face < UqmMath.Facings; block.Face += (ushort)(UqmMath.Facings / k.NumGasClouds))
        {
            var cloud = battle.InitializeMissile(block);
            if (cloud != null)
            {
                cloud.Parent = starShip;
                cloud.Target = null;
                cloud.TurnWait = (byte)(k.GasRate - 1);
                cloud.CollisionFunc = (e0, at0, e1, at1) => GasCloudCollision(battle, e0, at0, e1, at1, k);
                cloud.Velocity.Delta(dx, dy);
                battle.List.Put(cloud);
            }
        }
    }

    // black_urquan_postprocess: the special, when ready and paid for,
    // burns the gas (its sound).
    private static void Postprocess(Battle battle, Element e, Defines k)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-k.SpecialEnergyCost))
        {
            SpawnGasCloud(battle, e, k);
            starShip.SpecialCounter = (byte)k.SpecialWait;
        }
    }

    // black_urquan_preprocess: the blades count themselves afresh each
    // frame; the weapon held from the frame before holds its wait.
    private static void Preprocess(Element e)
    {
        var starShip = e.Parent!;
        starShip.RaceDesc!.Characteristics.SpecialWait = 0;   // no spinning disks
        if (starShip.WeaponCounter == 0
            && (starShip.CurStatusFlags & starShip.OldStatusFlags & StatusFlags.Weapon) != 0)
            ++starShip.WeaponCounter;
    }
}
