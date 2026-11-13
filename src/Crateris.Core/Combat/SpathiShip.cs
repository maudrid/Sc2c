// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/spathi/spathi.c: init_spathi, butt_missile_preprocess,
// spawn_butt_missile, spathi_intelligence, initialize_standard_missile,
// spathi_postprocess), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// spathi.c: the Spathi Eluder. Its weapon is a missile ahead; its special,
/// the B.U.T.T., a slow missile launched backwards that homes in on the
/// nearest opposing ship (<see cref="Battle.TrackShip"/>).
/// <para>
/// Its AI launches the B.U.T.T. when the enemy ship is behind it and near,
/// or coming its way.
/// </para>
/// </summary>
public sealed class SpathiShip : IShipCode
{
    public const string Id = "spathi";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _missile, _butt;

    /// <param name="description">The Spathi's description (data/ships.json's spathi).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="missile">Its forward gun's sheets (ship_data.weapon).</param>
    /// <param name="butt">
    /// The B.U.T.T.'s sheets (ship_data.special), the very instance its
    /// missiles take: its SEEKING_SPECIAL flag makes the AI tell them by it
    /// (<see cref="RaceDesc.Special"/>).
    /// </param>
    public SpathiShip(ShipDescription description, Views? hull, Views? missile, Views? butt)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _missile = missile;
        _butt = butt;
    }

    /// <summary>init_spathi: spathi_desc with its sheets and spathi.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _missile, _butt);
        var missile = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("SPATHI_FORWARD_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        int discriminatorSpeed = d.Define("DISCRIMINATOR_SPEED"), trackWait = d.Define("TRACK_WAIT");
        var butt = new MissileBlock
        {
            Flags = ElementFlags.None, PixOffs = (short)d.Define("SPATHI_REAR_OFFSET"),
            Speed = (short)discriminatorSpeed, HitPoints = (short)d.Define("DISCRIMINATOR_HITS"),
            Damage = (short)d.Define("DISCRIMINATOR_DAMAGE"), Life = (ushort)d.Define("DISCRIMINATOR_LIFE"),
            BlastOffs = (short)d.Define("DISCRIMINATOR_OFFSET"),
        };
        butt.PreprocessFunc = e => ButtMissilePreprocess(battle, e, discriminatorSpeed, trackWait);
        int specialEnergyCost = d.Define("SPECIAL_ENERGY_COST");
        rd.InitWeapon = e => InitializeStandardMissile(battle, e, missile);
        rd.Postprocess = e => Postprocess(battle, e, butt, specialEnergyCost, trackWait);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // butt_missile_preprocess: every TRACK_WAIT + 1 frames, a step towards its target.
    private static void ButtMissilePreprocess(Battle battle, Element e, int speed, int trackWait)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            ushort facing = e.Next.Frame.Index;
            if (battle.TrackShip(e, ref facing) > 0)
            {
                e.Next.Frame = e.Next.Frame.SetAbs(facing);
                e.StateFlags |= ElementFlags.Changing;
                e.Velocity.SetVector(speed, facing);
            }
            e.TurnWait = (byte)trackWait;
        }
    }

    // spawn_butt_missile: a B.U.T.T. out of the ship's back.
    private static void SpawnButtMissile(Battle battle, Element ship, MissileBlock block, int trackWait)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Special!;
        block.Face = block.Index = UqmMath.NormalizeFacing(starShip.ShipFacing + UqmMath.AngleToFacing(UqmMath.HalfCircle));
        block.Sender = ship.PlayerNr;
        var butt = battle.InitializeMissile(block);
        if (butt != null)
        {
            butt.TurnWait = (byte)trackWait;
            butt.Parent = starShip;
            // Its LAUNCH_BUTT_MISSILE sound.
            battle.List.Put(butt);
        }
    }

    // spathi_intelligence: the stock AI, and the B.U.T.T. when the enemy
    // ship is within 24 frames, behind the ship, and near (8) or flying
    // towards it, unless the ship is beyond its top speed or in a gravity
    // well and not heading away from it.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        starShip.ShipInputState &= ~StatusFlags.Special;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (starShip.SpecialCounter == 0 && enemy.Object != null && enemy.WhichTurn <= 24)
        {
            const int half = UqmMath.Facings / 2, quadrant = UqmMath.Facings / 4;
            int travelFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(ship.Velocity.TravelAngle + UqmMath.HalfCircle));
            // Across the seam where contact is tested so (decision 5).
            short deltaX = battle.SeamDeltaX(enemy.Object.Current.X - ship.Current.X);
            short deltaY = battle.SeamDeltaY(enemy.Object.Current.Y - ship.Current.Y);
            int directionFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(deltaX, deltaY)));

            if (UqmMath.NormalizeFacing(directionFacing - (starShip.ShipFacing + half) + quadrant) <= half
                && (enemy.WhichTurn <= 8
                    || UqmMath.NormalizeFacing(directionFacing + half
                                               - UqmMath.AngleToFacing(enemy.Object.Velocity.TravelAngle) + quadrant) <= half)
                && ((starShip.CurStatusFlags & (StatusFlags.ShipBeyondMaxSpeed | StatusFlags.ShipInGravityWell)) == 0
                    || UqmMath.NormalizeFacing(directionFacing - travelFacing + quadrant) <= half))
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // initialize_standard_missile: a missile ahead.
    private static IReadOnlyList<Element?> InitializeStandardMissile(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        return new[] { battle.InitializeMissile(block) };
    }

    // spathi_postprocess: the special, when ready and paid for, launches a B.U.T.T.
    private static void Postprocess(Battle battle, Element e, MissileBlock butt, int specialEnergyCost, int trackWait)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-specialEnergyCost))
        {
            SpawnButtMissile(battle, e, butt, trackWait);
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
        }
    }
}
