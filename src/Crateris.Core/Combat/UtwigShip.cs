// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/utwig/utwig.c: init_utwig, initialize_lance,
// utwig_intelligence, utwig_collision, utwig_preprocess), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// utwig.c: the Utwig Jugger. Its weapon is six lances at once, from
/// either side of its bow; its special, a shield held while the key is,
/// that costs energy every half of its wait and turns what hits it into
/// energy (a shot's damage, through the ship's life span). The ship has no
/// energy of its own coming back: only what the shield takes in.
/// <para>
/// Its AI raises the shield (three times in four) when a shot or ship it
/// could shield from is within 2 frames, and pursues an enemy ship without
/// an immediate weapon while it has energy.
/// </para>
/// </summary>
public sealed class UtwigShip : IShipCode
{
    public const string Id = "utwig";

    // The shield's colours, a frame each in turn (the ship drawn filled).
    private static readonly int[] ShieldColors =
    {
        Battle.Rgb15(0x1F, 0x15, 0x00), Battle.Rgb15(0x1F, 0x0E, 0x00), Battle.Rgb15(0x1F, 0x07, 0x00),
        Battle.Rgb15(0x1F, 0x00, 0x00), Battle.Rgb15(0x17, 0x00, 0x00), Battle.Rgb15(0x13, 0x00, 0x00),
        Battle.Rgb15(0x0F, 0x00, 0x00),
    };

    private static readonly int ShieldOff = Battle.Rgb15(0x1F, 0x1C, 0x00);

    private readonly ShipDescription _description;
    private readonly Views? _hull, _lance;

    /// <param name="description">The Utwig's description (data/ships.json's utwig).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="lance">Its lances' sheets (ship_data.weapon).</param>
    public UtwigShip(ShipDescription description, Views? hull, Views? lance)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _lance = lance;
    }

    /// <summary>init_utwig: utwig_desc with its sheets and utwig.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _lance, null);
        var lance = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = 0,
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        var offsets = new (int X, int Y)[]
        {
            (d.Define("LAUNCH_XOFFS0"), d.Define("LAUNCH_YOFFS0")),
            (d.Define("LAUNCH_XOFFS1"), d.Define("LAUNCH_YOFFS1")),
            (d.Define("LAUNCH_XOFFS2"), d.Define("LAUNCH_YOFFS2")),
        };
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"), specialWait = d.Define("SPECIAL_WAIT");
        rd.Preprocess = e => Preprocess(battle, e, specialEnergyCost, specialWait);
        rd.InitWeapon = e => InitializeLance(battle, e, lance, offsets);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // initialize_lance: three pairs of lances, each pair either side of
    // the bow, the offsets turned to the ship's facing.
    private static IReadOnlyList<Element?> InitializeLance(Battle battle, Element ship, MissileBlock block,
        (int X, int Y)[] offsets)
    {
        var starShip = ship.Parent!;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;

        var lances = new Element?[6];
        for (int i = 0; i < 3; ++i)
        {
            int cos0 = offsets[i].X, sin0 = offsets[i].Y;
            int angle = UqmMath.FacingToAngle(block.Face) + UqmMath.Quadrant;
            short cos1cos0 = UqmMath.Cosine(angle, cos0);
            short sin1sin0 = UqmMath.Sine(angle, sin0);
            short sin1cos0 = UqmMath.Sine(angle, cos0);
            short cos1sin0 = UqmMath.Cosine(angle, sin0);

            short x = (short)(cos1cos0 - sin1sin0), y = (short)(sin1cos0 + cos1sin0);
            block.Cx = (short)(ship.Next.X + x);
            block.Cy = (short)(ship.Next.Y + y);
            lances[i << 1] = battle.InitializeMissile(block);

            x = (short)(-cos1cos0 - sin1sin0);
            y = (short)(-sin1cos0 + cos1sin0);
            block.Cx = (short)(ship.Next.X + x);
            block.Cy = (short)(ship.Next.Y + y);
            lances[(i << 1) + 1] = battle.InitializeMissile(block);
        }
        return lances;
    }

    // utwig_intelligence: with energy, a shot or ship it's enticed by is
    // pursued if it isn't short-lived; a short-lived one with mass or crew
    // counts half the frames away (one with mass then dropped, crew
    // pursued) and makes the shield wanted. With the special ready, the
    // shield (three times in four, its weapon let go) when such a thing is
    // within 2 frames: a ship meaning an immediate weapon, a shot that
    // PlotIntercept says hits within 2; then it no longer dodges it. With
    // energy, it pursues an enemy ship without an immediate weapon.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        int shieldStatus;
        if (rd.EnergyLevel == 0)
            shieldStatus = 0;
        else
        {
            shieldStatus = -1;
            if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
            {
                shieldStatus = 0;
                if ((weapon.Object.StateFlags & ElementFlags.FiniteLife) == 0)
                    weapon.MoveState = MoveState.Pursue;
                else if (weapon.Object.MassPoints != 0 || (weapon.Object.StateFlags & ElementFlags.CrewObject) != 0)
                {
                    if ((weapon.WhichTurn >>= 1) == 0)
                        weapon.WhichTurn = 1;

                    if (weapon.Object.MassPoints != 0)
                        weapon.Object = null;
                    else
                        weapon.MoveState = MoveState.Pursue;
                    shieldStatus = 1;
                }
            }
        }

        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            if (shieldStatus != 0)
            {
                if ((shieldStatus > 0 || weapon.Object != null)
                    && weapon.WhichTurn <= 2
                    && (shieldStatus > 0
                        // A ship means an immediate weapon.
                        || (weapon.Object!.StateFlags & ElementFlags.PlayerShip) != 0
                        || battle.PlotIntercept(weapon.Object, ship, 2, 0) != 0)
                    && (battle.Random.Next("utwig_intelligence") & 3) != 0)
                {
                    starShip.ShipInputState |= StatusFlags.Special;
                    starShip.ShipInputState &= ~StatusFlags.Weapon;
                }

                weapon.Object = null;
            }
        }

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (rd.EnergyLevel != 0 && enemy.Object != null
            && (enemy.Object.Parent!.RaceDesc!.ShipFlags & ShipFlags.ImmediateWeapon) == 0)
            enemy.MoveState = MoveState.Pursue;
        battle.ShipIntelligence(ship, concerns, concernCounter);
    }

    // utwig_collision: shielded, a short-lived thing with mass hitting it
    // adds its mass to the ship's life span (energy, in its preprocess);
    // then the ship's own collision.
    private static void Collision(Battle battle, Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        if (e0.LifeSpan > Battle.NormalLife
            && (e1.StateFlags & ElementFlags.FiniteLife) != 0
            && e1.MassPoints != 0)
            e0.LifeSpan += e1.MassPoints;

        battle.Collision(e0, at0, e1, at1);
    }

    // utwig_preprocess: what the shield took in becomes energy; the shield
    // is held while the special is, its energy paid every half of its wait
    // (without the energy, the ship loses its point and shield defence for
    // the AI); shielded, the ship is drawn filled in turning colours and
    // its life span is one more than a ship's, which shots don't damage.
    private static void Preprocess(Battle battle, Element e, int specialEnergyCost, int specialWait)
    {
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
            e.CollisionFunc = (e0, at0, e1, at1) => Collision(battle, e0, at0, e1, at1);

        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if (e.LifeSpan > Battle.NormalLife + 1)
        {
            battle.DeltaEnergy(e, (short)(e.LifeSpan - (Battle.NormalLife + 1)));
            // Its sound for energy taken in.
        }

        if ((starShip.CurStatusFlags & StatusFlags.Special) == 0)
            starShip.SpecialCounter = 0;
        else if (starShip.SpecialCounter % (specialWait >> 1) == 0)
        {
            if (!battle.DeltaEnergy(e, (short)-specialEnergyCost))
                rd.ShipFlags &= ~(ShipFlags.PointDefense | ShipFlags.ShieldDefense);
            else if (starShip.SpecialCounter == 0)
            {
                starShip.SpecialCounter = rd.Characteristics.SpecialWait;
                // Its shield's sound.
            }
        }

        if (starShip.SpecialCounter == 0)
        {
            // The shield is off.
            e.PrimColor = ShieldOff;
            e.ColorCycleIndex = 0;
            e.LifeSpan = Battle.NormalLife;
            e.Prim = PrimType.Stamp;
        }
        else
        {
            // The shield is on.
            if (starShip.WeaponCounter == 0)
                ++starShip.WeaponCounter;

            // ColorCycleIndex is one more than the colour's place, 0 with the shield off.
            ++e.ColorCycleIndex;
            if (e.ColorCycleIndex == ShieldColors.Length + 1)
                e.ColorCycleIndex = 1;

            e.PrimColor = ShieldColors[e.ColorCycleIndex - 1];
            e.LifeSpan = Battle.NormalLife + 1;
            e.Prim = PrimType.StampFill;
        }
    }
}
