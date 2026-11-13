// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/mycon/mycon.c: init_mycon, plasma_preprocess,
// plasma_blast_preprocess, plasma_collision, mycon_intelligence,
// initialize_plasma, mycon_postprocess), Copyright Paul Reiche, Fred Ford
// and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// mycon.c: the Mycon Podship. Its weapon is a slow plasmoid that homes in
/// on the nearest opposing ship (<see cref="Battle.TrackShip"/>), fading
/// as it goes: its damage and hit points are what's left of its life, and
/// its blast, as big as the damage it had, follows the ship it hit. Its
/// special spends all its energy to grow back crew.
/// <para>
/// Its AI fires when the enemy ship is ahead and near (or the ship has
/// all its crew), keeps clear of short-lived shots, doesn't thrust after
/// homing ones, and regrows crew whenever it can.
/// </para>
/// </summary>
public sealed class MyconShip : IShipCode
{
    public const string Id = "mycon";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _plasma;

    /// <param name="description">The Mycon's description (data/ships.json's mycon).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="plasma">Its plasmoid's sheets (ship_data.weapon), its blast's frames after its own.</param>
    public MyconShip(ShipDescription description, Views? hull, Views? plasma)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _plasma = plasma;
    }

    // mycon.c's #defines the plasmoid's code uses.
    private sealed record Plasma(int NumPlasmas, int NumGloballs, int PlasmaDuration, int MissileLife,
        int MissileSpeed, int MissileDamage, int TrackWait);

    /// <summary>init_mycon: mycon_desc with its sheets and mycon.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _plasma, null);
        var p = new Plasma(d.Define("NUM_PLASMAS"), d.Define("NUM_GLOBALLS"), d.Define("PLASMA_DURATION"),
            d.Define("MISSILE_LIFE"), d.Define("MISSILE_SPEED"), d.Define("MISSILE_DAMAGE"), d.Define("TRACK_WAIT"));
        var plasma = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.None, PixOffs = (short)d.Define("MYCON_OFFSET"),
            Speed = (short)p.MissileSpeed, HitPoints = (short)p.MissileDamage, Damage = (short)p.MissileDamage,
            Life = (ushort)p.MissileLife, BlastOffs = (short)d.Define("MISSILE_OFFSET"),
            PreprocessFunc = e => PlasmaPreprocess(battle, e, p),
        };
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"), regeneration = d.Define("REGENERATION_AMOUNT");
        rd.InitWeapon = e => InitializePlasma(battle, e, plasma, p);
        rd.Postprocess = e => Postprocess(battle, e, specialEnergyCost, regeneration);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, p, specialEnergyCost);
        return rd;
    }

    // plasma_preprocess: its hit points (cut by a hit, which shortens its
    // life to match) or its life left (which sets them) make its damage
    // and its frame; every TRACK_WAIT + 1 frames, a step towards its
    // target.
    private static void PlasmaPreprocess(Battle battle, Element e, Plasma p)
    {
        if (e.MassPoints > e.HitPoints)
            e.LifeSpan = (ushort)(e.HitPoints * p.PlasmaDuration);
        else
            e.HitPoints = (byte)((e.LifeSpan * p.MissileDamage + (p.MissileLife - 1)) / p.MissileLife);
        e.MassPoints = (byte)e.HitPoints;
        int plasmaIndex = p.NumPlasmas - (e.LifeSpan + (p.PlasmaDuration - 1)) / p.PlasmaDuration;
        if (plasmaIndex != e.Next.Frame.Index)
        {
            e.Next.Frame = e.Next.Frame.SetAbs(plasmaIndex);
            e.StateFlags |= ElementFlags.Changing;
        }

        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            ushort facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
            if (battle.TrackShip(e, ref facing) > 0)
                e.Velocity.SetVector(p.MissileSpeed, facing);

            e.TurnWait = (byte)p.TrackWait;
        }
    }

    // plasma_blast_preprocess: the blast grows for half its life, then
    // shrinks, following the ship it hit.
    private static void PlasmaBlastPreprocess(Element e)
    {
        e.Next.Frame = e.LifeSpan >= e.ThrustWait ? e.Next.Frame.Inc() : e.Next.Frame.Dec();
        if (e.Target != null)
        {
            e.Next.X = e.Target.Next.X;
            e.Next.Y = e.Target.Next.Y;
        }

        e.StateFlags |= ElementFlags.Changing;
    }

    // plasma_collision: a hit on anything but the ship's own shots (its
    // own ship too); the blast, at the hit thing's place, grows and
    // shrinks over frames as many as the plasmoid's damage in globs, and
    // follows a ship it hit.
    private static void PlasmaCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Plasma p)
    {
        int oldMass = e0.MassPoints;
        if (e0.Parent != e1.Parent || (e1.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var blast = battle.WeaponCollision(e0, at0, e1, at1);
            if (blast == null)
                return;
            blast.Parent = e0.Parent;
            blast.Target = (e1.StateFlags & ElementFlags.PlayerShip) == 0 ? null : e1.Parent!.Ship;
            blast.Current.X = e1.Current.X;
            blast.Current.Y = e1.Current.Y;

            int numAnimations = (oldMass * p.NumGloballs + (p.MissileDamage - 1)) / p.MissileDamage;
            if (numAnimations == 0)
                numAnimations = 1;

            blast.ThrustWait = (byte)numAnimations;
            blast.LifeSpan = (ushort)((numAnimations << 1) - 1);
            blast.PreprocessFunc = PlasmaBlastPreprocess;
            blast.Current.FrameArray = e0.Next.FrameArray;
            blast.Current.Frame = blast.Current.FrameArray![0].SetAbs(p.NumPlasmas);
        }
    }

    // mycon_intelligence: a short-lived shot it's enticed by is avoided,
    // anything else pursued, but it doesn't thrust after what it pursues
    // (homing shots). With its weapon ready, it fires when the enemy ship
    // is within 16 frames (or the ship has all its crew) and ahead, unless
    // the ship is beyond its top speed or in a gravity well and not
    // heading that way. With the special ready, the plasmoid's reach is
    // its life's flight while the ship lacks crew (and it regrows crew
    // with all its energy, not firing), 800 pixels otherwise.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Plasma p, int specialEnergyCost)
    {
        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null && weapon.MoveState == MoveState.Entice)
        {
            weapon.MoveState = (weapon.Object.StateFlags & ElementFlags.FiniteLife) != 0
                               && (weapon.Object.StateFlags & ElementFlags.CrewObject) == 0
                ? MoveState.Avoid
                : MoveState.Pursue;
        }

        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;
        if (concerns[Battle.EnemyWeaponIndex].MoveState == MoveState.Pursue)
            starShip.ShipInputState &= ~StatusFlags.Thrust;   // don't pursue seekers

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.WeaponCounter == 0
            && enemy.Object != null
            && (enemy.WhichTurn <= 16 || ship.CrewLevel == rd.MaxCrew))
        {
            const int half = UqmMath.Facings / 2, quadrant = UqmMath.Facings / 4, octant = UqmMath.Facings / 8;
            int travelFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(ship.Velocity.TravelAngle + UqmMath.HalfCircle));
            // Across the seam where contact is tested so (decision 5).
            short deltaX = battle.SeamDeltaX(enemy.Object.Current.X - ship.Current.X);
            short deltaY = battle.SeamDeltaY(enemy.Object.Current.Y - ship.Current.Y);
            int directionFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(deltaX, deltaY)));

            if (UqmMath.NormalizeFacing(directionFacing - starShip.ShipFacing + quadrant) <= half
                && ((starShip.CurStatusFlags & (StatusFlags.ShipBeyondMaxSpeed | StatusFlags.ShipInGravityWell)) == 0
                    || UqmMath.NormalizeFacing(directionFacing - travelFacing + octant) <= quadrant))
                starShip.ShipInputState |= StatusFlags.Weapon;
        }

        if (starShip.SpecialCounter == 0)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            rd.WeaponRange = (ushort)Battle.DisplayToWorld(800);
            if (ship.CrewLevel < rd.MaxCrew)
            {
                rd.WeaponRange = (ushort)(p.MissileSpeed * p.MissileLife);
                if (rd.EnergyLevel >= specialEnergyCost && (starShip.ShipInputState & StatusFlags.Weapon) == 0)
                    starShip.ShipInputState |= StatusFlags.Special;
            }
        }
    }

    // initialize_plasma: a plasmoid ahead, its first track two frames late.
    private static IReadOnlyList<Element?> InitializePlasma(Battle battle, Element ship, MissileBlock block, Plasma p)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var plasma = battle.InitializeMissile(block);
        if (plasma != null)
        {
            plasma.CollisionFunc = (e0, at0, e1, at1) => PlasmaCollision(battle, e0, at0, e1, at1, p);
            plasma.TurnWait = (byte)(p.TrackWait + 2);
        }
        return new[] { plasma };
    }

    // mycon_postprocess: the special, when ready, the ship short of crew,
    // and paid for (all its energy), grows back up to REGENERATION_AMOUNT
    // crew (its GROW_NEW_CREW sound).
    private static void Postprocess(Battle battle, Element e, int specialEnergyCost, int regeneration)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && e.CrewLevel != rd.MaxCrew
            && battle.DeltaEnergy(e, (short)-specialEnergyCost))
        {
            int addCrew = regeneration;
            if (addCrew > rd.MaxCrew - e.CrewLevel)
                addCrew = rd.MaxCrew - e.CrewLevel;
            battle.DeltaCrew(e, (short)addCrew);

            starShip.SpecialCounter = rd.Characteristics.SpecialWait;
        }
    }
}
