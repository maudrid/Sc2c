// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/human/human.c: init_human, nuke_preprocess,
// spawn_point_defense, initialize_nuke, human_intelligence,
// human_postprocess), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>
/// human.c: the Earthling Cruiser. Its weapon is a nuclear missile that
/// homes on the nearest opposing ship (<see cref="Battle.TrackShip"/>),
/// speeding up as it flies; its special is point defence: a laser at
/// every solid, uncloaked element within reach, paid for once, which
/// leaves its teammates and their shots be (<see cref="Battle.Teammates"/>,
/// stage 4b's co-op rule, as the flagship's).
/// <para>
/// Its AI fires point defence at a shot two frames away or a ship four
/// away, and the missile when it isn't turning or the enemy is within 12
/// frames.
/// </para>
/// </summary>
public sealed class HumanShip : IShipCode
{
    public const string Id = "human";

    private static readonly int PointDefenseColor = Battle.Rgb15(0x1F, 0x1F, 0x1F);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _nuke;

    /// <param name="description">The Earthling's description (data/ships.json's human).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="nuke">Its missile's sheets (ship_data.weapon).</param>
    public HumanShip(ShipDescription description, Views? hull, Views? nuke)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _nuke = nuke;
    }

    // human.c's #defines.
    private sealed record Defines(int MissileSpeed, int MaxMissileSpeed, int ThrustScale, int MissileLife,
        int TrackWait, int SpecialEnergyCost, int LaserRange);

    /// <summary>init_human: human_desc with its sheets and human.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _nuke, null);
        var f = new Defines(d.Define("MISSILE_SPEED"), d.Define("MAX_MISSILE_SPEED"), d.Define("THRUST_SCALE"),
            d.Define("MISSILE_LIFE"), d.Define("TRACK_WAIT"), d.Define("SPECIAL_ENERGY_COST"), d.Define("LASER_RANGE"));
        var nuke = new MissileBlock
        {
            Flags = ElementFlags.None, PixOffs = (short)d.Define("HUMAN_OFFSET"), Speed = (short)f.MissileSpeed,
            HitPoints = (short)d.Define("MISSILE_HITS"), Damage = (short)d.Define("MISSILE_DAMAGE"),
            Life = (ushort)f.MissileLife, PreprocessFunc = e => NukePreprocess(battle, e, f),
            BlastOffs = (short)d.Define("NUKE_OFFSET"),
        };
        rd.InitWeapon = e => InitializeNuke(battle, e, nuke, f);
        rd.Postprocess = e => Postprocess(battle, e, f);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // nuke_preprocess: every TRACK_WAIT + 1 frames a turn towards its
    // target; its speed grows a world unit a frame, up to its top speed.
    private static void NukePreprocess(Battle battle, Element e, Defines f)
    {
        ushort facing = e.Next.Frame.Index;
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            if (battle.TrackShip(e, ref facing) > 0)
            {
                e.Next.Frame = e.Next.Frame.SetAbs(facing);
                e.StateFlags |= ElementFlags.Changing;
            }
            e.TurnWait = (byte)f.TrackWait;
        }

        int speed = f.MissileSpeed + (f.MissileLife - e.LifeSpan) * f.ThrustScale;
        if (speed > f.MaxMissileSpeed)
            speed = f.MaxMissileSpeed;
        e.Velocity.SetVector(speed, facing);
    }

    // spawn_point_defense: on the ship, a helper element that dies this
    // frame, and in its death, after every element has moved: a laser at
    // each solid, uncloaked element within reach but the ship itself, its
    // teammates' and their shots (the co-op rule), the energy paid once.
    private static void SpawnPointDefense(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var defense = battle.List.Alloc();
            if (defense != null)
            {
                defense.PlayerNr = e.PlayerNr;
                defense.StateFlags = ElementFlags.Appearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
                defense.DeathFunc = d => SpawnPointDefense(battle, d, f);
                defense.Parent = starShip;
                battle.List.Put(defense);
            }
            return;
        }

        bool paidFor = false;
        var ship = starShip.Ship!;
        int range = f.LaserRange;
        for (Element? o = battle.List.Tail, next; o != null; o = next)
        {
            next = o.Predecessor;
            if (o == ship || !Battle.Colliding(o) || Battle.ObjectCloaked(o) || Battle.Teammates(o, ship))
                continue;
            // Across the seam where contact is tested so (decision 5).
            short ox = battle.SeamDeltaX(o.Next.X - ship.Next.X), oy = battle.SeamDeltaY(o.Next.Y - ship.Next.Y);
            short dx = ox, dy = oy;
            if (dx < 0)
                dx = (short)-dx;
            if (dy < 0)
                dy = (short)-dy;
            dx = Battle.WorldToDisplay(dx);
            dy = Battle.WorldToDisplay(dy);
            if ((ushort)dx > range || (ushort)dy > range
                || (ushort)dx * (ushort)dx + (ushort)dy * (ushort)dy > range * range)
                continue;

            if (!paidFor)
            {
                if (!battle.DeltaEnergy(ship, (short)-f.SpecialEnergyCost))
                    break;
                // The point defence's sound.
                starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
                paidFor = true;
            }

            var block = new LaserBlock
            {
                Cx = ship.Next.X, Cy = ship.Next.Y, Face = 0, Ex = ox, Ey = oy,
                Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Color = PointDefenseColor,
            };
            var laser = battle.InitializeLaser(block);
            if (laser != null)
            {
                laser.Parent = starShip;
                laser.Target = null;
                battle.List.Put(laser);
            }
        }
    }

    // initialize_nuke: a missile ahead, waiting TRACK_WAIT frames to home.
    private static IReadOnlyList<Element?> InitializeNuke(Battle battle, Element ship, MissileBlock block, Defines f)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var nuke = battle.InitializeMissile(block);
        if (nuke != null)
            nuke.TurnWait = (byte)f.TrackWait;
        return new[] { nuke };
    }

    // human_intelligence: point defence at a shot within 2 frames or the
    // enemy within 4, the shot not dodged; the missile, once ready, at an
    // enemy while not turning or within 12 frames.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter == 0
            && ((weapon.Object != null && weapon.WhichTurn <= 2)
                || (enemy.Object != null && enemy.WhichTurn <= 4)))
            starShip.ShipInputState |= StatusFlags.Special;
        else
            starShip.ShipInputState &= ~StatusFlags.Special;
        weapon.Object = null;

        battle.ShipIntelligence(ship, concerns, concernCounter);

        if (starShip.WeaponCounter == 0
            && enemy.Object != null
            && ((starShip.ShipInputState & (StatusFlags.Left | StatusFlags.Right)) == 0 || enemy.WhichTurn <= 12))
            starShip.ShipInputState |= StatusFlags.Weapon;
    }

    // human_postprocess: point defence when pressed and ready.
    private static void Postprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 && starShip.SpecialCounter == 0)
            SpawnPointDefense(battle, e, f);
    }
}
