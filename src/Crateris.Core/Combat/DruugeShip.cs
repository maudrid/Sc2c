// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/druuge/druuge.c: init_druuge, cannon_collision,
// initialize_cannon, druuge_intelligence, druuge_postprocess,
// druuge_preprocess), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// druuge.c: the Druuge Mauler. Its weapon, a mass driver, kicks the ship
/// back as it fires and the ship it hits along; its special, the furnace,
/// burns a crew member for energy.
/// <para>
/// Its AI pursues within the cannon's reach as if the enemy couldn't fire
/// back, fires when near or a shot is, and burns crew when it's out of
/// energy to fire.
/// </para>
/// </summary>
public sealed class DruugeShip : IShipCode
{
    public const string Id = "druuge";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _cannon;

    /// <param name="description">The Druuge's description (data/ships.json's druuge).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="cannon">Its mass driver's sheets (ship_data.weapon).</param>
    public DruugeShip(ShipDescription description, Views? hull, Views? cannon)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _cannon = cannon;
    }

    /// <summary>init_druuge: druuge_desc with its sheets and druuge.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        // No special sheets (the furnace draws none).
        var rd = RaceShips.NewRaceDesc(d, _hull, _cannon, null);
        int recoil = d.Define("RECOIL_VELOCITY"), maxRecoil = d.Define("MAX_RECOIL_VELOCITY");
        var cannon = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("DRUUGE_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        int weaponEnergyCost = d.Define("WEAPON_ENERGY_COST"), weaponWait = d.Define("WEAPON_WAIT"),
            specialEnergyCost = d.Define("SPECIAL_ENERGY_COST");
        int reachTurn = Battle.WorldToTurn(d.Define("MISSILE_RANGE") * 3 / 4);
        rd.Preprocess = e => Preprocess(battle, e, specialEnergyCost);
        rd.Postprocess = e => Postprocess(e, recoil, maxRecoil);
        rd.InitWeapon = e => InitializeCannon(battle, e, cannon, recoil, maxRecoil);
        rd.Intelligence = (ship, concerns, count) =>
            Intelligence(battle, ship, concerns, count, reachTurn, weaponEnergyCost, weaponWait);
        return rd;
    }

    // The recoil: a kick of RECOIL_VELOCITY along an angle, the speed then
    // held to MAX_RECOIL_VELOCITY.
    private static void Kick(Element e, int angle, int recoil, int maxRecoil)
    {
        e.Velocity.Delta(UqmMath.Cosine(angle, recoil), UqmMath.Sine(angle, recoil));
        var (dx, dy) = e.Velocity.Current();
        if ((long)dx * dx + (long)dy * dy > (long)maxRecoil * maxRecoil)
        {
            angle = UqmMath.ArcTan(dx, dy);
            e.Velocity.SetComponents(UqmMath.Cosine(angle, maxRecoil), UqmMath.Sine(angle, maxRecoil));
        }
    }

    // cannon_collision: the hit, and a ship with crew that isn't a planet knocked along the shot's way.
    private static void CannonCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1,
        int recoil, int maxRecoil)
    {
        battle.WeaponCollision(e0, at0, e1, at1);

        if ((e1.StateFlags & ElementFlags.PlayerShip) != 0
            && e1.CrewLevel != 0
            && !Battle.GravityMass(e1.MassPoints + 1))
        {
            var starShip = e1.Parent!;
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
            Kick(e1, UqmMath.FacingToAngle(e0.Next.Frame.Index), recoil, maxRecoil);
        }
    }

    // initialize_cannon: a slug ahead.
    private static IReadOnlyList<Element?> InitializeCannon(Battle battle, Element ship, MissileBlock block,
        int recoil, int maxRecoil)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var cannon = battle.InitializeMissile(block);
        if (cannon != null)
            cannon.CollisionFunc = (e0, at0, e1, at1) => CannonCollision(battle, e0, at0, e1, at1, recoil, maxRecoil);
        return new[] { cannon };
    }

    // druuge_intelligence: enticing while beyond its top speed; within
    // three quarters of the cannon's reach, pursuing (its thrust waiting a
    // frame) with the enemy's guns taken off for the stock AI's look; then
    // firing when the enemy ship is within 12 frames or a shot within 6,
    // and burning crew for the energy to fire.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        int reachTurn, int weaponEnergyCost, int weaponWait)
    {
        var starShip = ship.Parent!;
        const ShipFlags guns = ShipFlags.FiresFore | ShipFlags.FiresRight | ShipFlags.FiresAft | ShipFlags.FiresLeft;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        RaceDesc? enemyRd = null;
        var shipFlags = ShipFlags.None;
        if ((starShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) != 0)
            enemy.MoveState = MoveState.Entice;
        else if (enemy.Object != null && enemy.WhichTurn <= reachTurn)
        {
            enemyRd = enemy.Object.Parent!.RaceDesc!;
            shipFlags = enemyRd.ShipFlags;
            enemyRd.ShipFlags &= ~guns;

            enemy.MoveState = MoveState.Pursue;
            if (ship.ThrustWait == 0)
                ++ship.ThrustWait;
        }
        battle.ShipIntelligence(ship, concerns, concernCounter);
        if (enemyRd != null)
            enemyRd.ShipFlags = shipFlags;

        if ((starShip.CurStatusFlags & StatusFlags.ShipBeyondMaxSpeed) == 0
            && (enemy.WhichTurn <= 12
                || (concerns[Battle.EnemyWeaponIndex].Object != null && concerns[Battle.EnemyWeaponIndex].WhichTurn <= 6)))
        {
            starShip.ShipInputState |= StatusFlags.Weapon;
            if (ship.ThrustWait < weaponWait + 1)
                ship.ThrustWait = (byte)(weaponWait + 1);
        }

        if ((starShip.ShipInputState & StatusFlags.Weapon) != 0
            && starShip.RaceDesc!.EnergyLevel < weaponEnergyCost
            && ship.CrewLevel > 1)
            starShip.ShipInputState |= StatusFlags.Special;
        else
            starShip.ShipInputState &= ~StatusFlags.Special;
    }

    // druuge_postprocess: the cannon fired this frame kicks the ship backwards.
    private static void Postprocess(Element e, int recoil, int maxRecoil)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Weapon) != 0
            && starShip.WeaponCounter == starShip.RaceDesc!.Characteristics.WeaponWait)
        {
            starShip.CurStatusFlags &= ~StatusFlags.ShipAtMaxSpeed;
            Kick(e, UqmMath.FacingToAngle(starShip.ShipFacing) + UqmMath.HalfCircle, recoil, maxRecoil);
        }
    }

    // druuge_preprocess: the furnace, when ready, with more than one crew
    // and energy not full, burns a crew member for its energy.
    private static void Preprocess(Battle battle, Element e, int specialEnergyCost)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) == 0)
            return;
        var rd = starShip.RaceDesc!;
        if (starShip.SpecialCounter != 0 || e.CrewLevel == 1 || rd.EnergyLevel == rd.MaxEnergy)
            starShip.CurStatusFlags &= ~StatusFlags.Special;
        else
        {
            // Its BURN UP CREW sound.
            battle.DeltaCrew(e, -1);
            battle.DeltaEnergy(e, (short)specialEnergyCost);
            starShip.SpecialCounter = rd.Characteristics.SpecialWait;
        }
    }
}
