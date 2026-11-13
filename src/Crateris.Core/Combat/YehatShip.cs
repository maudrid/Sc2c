// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/yehat/yehat.c: init_yehat, initialize_standard_missiles,
// yehat_intelligence, yehat_postprocess, yehat_preprocess), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// yehat.c: the Yehat Terminator. Its weapon is twin pulse cannons; its
/// special, a force shield: for a few frames the ship is drawn with its
/// shield sheet and takes no damage (its life span above NORMAL_LIFE, which
/// <see cref="Battle.WeaponCollision"/> spares), paying energy each frame.
/// <para>
/// Its AI shields itself from a shot or ship about to hit, three times in
/// four, and pursues an enemy without an immediate weapon.
/// </para>
/// </summary>
public sealed class YehatShip : IShipCode
{
    public const string Id = "yehat";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _missile, _shield;

    /// <param name="description">The Yehat's description (data/ships.json's yehat).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="missile">Its cannons' sheets (ship_data.weapon).</param>
    /// <param name="shield">Its shielded ship's sheets (ship_data.special).</param>
    public YehatShip(ShipDescription description, Views? hull, Views? missile, Views? shield)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _missile = missile;
        _shield = shield;
    }

    /// <summary>init_yehat: yehat_desc with its sheets and yehat.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _missile, _shield);
        var missile = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("YEHAT_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        int launchOffs = d.Define("LAUNCH_OFFS"), specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"),
            shieldLife = d.Define("SHIELD_LIFE");
        rd.Preprocess = e => Preprocess(battle, e, specialEnergyCost, shieldLife);
        rd.Postprocess = e => Postprocess(battle, e, specialEnergyCost);
        rd.InitWeapon = e => InitializeStandardMissiles(battle, e, missile, launchOffs);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // initialize_standard_missiles: two missiles ahead, either side of the nose.
    private static IReadOnlyList<Element?> InitializeStandardMissiles(Battle battle, Element ship, MissileBlock block,
        int launchOffs)
    {
        var starShip = ship.Parent!;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;

        int angle = UqmMath.FacingToAngle(block.Face);
        short offsX = (short)-UqmMath.Sine(angle, launchOffs);
        short offsY = UqmMath.Cosine(angle, launchOffs);

        block.Cx = (short)(ship.Next.X + offsX);
        block.Cy = (short)(ship.Next.Y + offsY);
        var first = battle.InitializeMissile(block);

        block.Cx = (short)(ship.Next.X - offsX);
        block.Cy = (short)(ship.Next.Y - offsY);
        var second = battle.InitializeMissile(block);
        return new[] { first, second };
    }

    // yehat_intelligence: a shot or ship it's enticed by (one it could
    // shield from) is pursued if it isn't short-lived; a massive or crew
    // one counts double the frames away if it lasts, half if it doesn't
    // (and a short-lived one with mass is dropped, crew pursued). With the
    // shield ready, it shields three times in four when such a thing is
    // within 2 frames (a ship meaning an immediate weapon, or a shot
    // PlotIntercept says will hit within 2) and the ship isn't shielded;
    // then it no longer dodges it. It pursues an enemy ship without an
    // immediate weapon.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        int shieldStatus = -1;
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
        {
            shieldStatus = 0;
            if ((weapon.Object.StateFlags & (ElementFlags.FiniteLife | ElementFlags.CrewObject)) == 0)
                weapon.MoveState = MoveState.Pursue;
            else if (weapon.Object.MassPoints != 0 || (weapon.Object.StateFlags & ElementFlags.CrewObject) != 0)
            {
                if ((weapon.Object.StateFlags & ElementFlags.FiniteLife) == 0)
                    weapon.WhichTurn <<= 1;
                else
                {
                    if ((weapon.WhichTurn >>= 1) == 0)
                        weapon.WhichTurn = 1;

                    if (weapon.Object.MassPoints != 0)
                        weapon.Object = null;
                    else
                        weapon.MoveState = MoveState.Pursue;
                }
                shieldStatus = 1;
            }
        }

        var starShip = ship.Parent!;
        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            if (shieldStatus != 0)
            {
                if (ship.LifeSpan <= Battle.NormalLife + 1
                    && (shieldStatus > 0 || weapon.Object != null)
                    && weapon.WhichTurn <= 2
                    && (shieldStatus > 0
                        // A ship means an immediate weapon.
                        || (weapon.Object!.StateFlags & ElementFlags.PlayerShip) != 0
                        || battle.PlotIntercept(weapon.Object, ship, 2, 0) != 0)
                    && (battle.Random.Next("yehat_intelligence") & 3) != 0)
                    starShip.ShipInputState |= StatusFlags.Special;

                if (weapon.Object != null && (weapon.Object.StateFlags & ElementFlags.CrewObject) == 0)
                    weapon.Object = null;
            }
        }

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object != null
            && (enemy.Object.Parent!.RaceDesc!.ShipFlags & ShipFlags.ImmediateWeapon) == 0)
            enemy.MoveState = MoveState.Pursue;
        battle.ShipIntelligence(ship, concerns, concernCounter);
    }

    // yehat_postprocess: each frame of the shield costs its energy; the
    // shield's last frame ends the special's wait.
    private static void Postprocess(Battle battle, Element e, int specialEnergyCost)
    {
        if ((e.StateFlags & ElementFlags.NonSolid) != 0)
            return;
        var starShip = e.Parent!;
        if (starShip.SpecialCounter > 0)
        {
            if (e.LifeSpan == Battle.NormalLife)
                starShip.SpecialCounter = 0;
            else
            {
                // Its YEHAT_SHIELD_ON sound.
                battle.DeltaEnergy(e, (short)-specialEnergyCost);
            }
        }
    }

    // yehat_preprocess: the shield's frames count down its life span, and
    // when it's over the ship is drawn with its hull again; the special,
    // when ready and with the energy, raises the shield (without the
    // energy it only flashes LOW_ON_ENERGY).
    private static void Preprocess(Battle battle, Element e, int specialEnergyCost, int shieldLife)
    {
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
            return;
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if ((e.LifeSpan > Battle.NormalLife && --e.LifeSpan == Battle.NormalLife)
            || (e.LifeSpan == Battle.NormalLife && ReferenceEquals(e.Next.FrameArray, rd.Special)))
        {
            e.Next.FrameArray = rd.Ship;
            e.Next.Frame = Frame.SetEqu(rd.Ship![0], e.Next.Frame);
            e.StateFlags |= ElementFlags.Changing;
        }

        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 && starShip.SpecialCounter == 0)
        {
            if (rd.EnergyLevel < specialEnergyCost)
                battle.DeltaEnergy(e, (short)-specialEnergyCost);   // so the text flashes
            else
            {
                e.LifeSpan = (ushort)(shieldLife + Battle.NormalLife);
                e.Next.FrameArray = rd.Special;
                e.Next.Frame = Frame.SetEqu(rd.Special![0], e.Next.Frame);
                e.StateFlags |= ElementFlags.Changing;
                starShip.SpecialCounter = rd.Characteristics.SpecialWait;
            }
        }
    }
}
