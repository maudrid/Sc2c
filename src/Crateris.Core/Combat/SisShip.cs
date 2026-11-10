// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/sis_ship/sis_ship.c: init_sis and uninit_sis in battle,
// InitWeaponSlots, InitModuleSlots, InitDriveSlots, InitJetSlots,
// sis_battle_preprocess, sis_battle_postprocess, spawn_point_defense,
// blaster_preprocess, blaster_collision, initialize_blasters,
// sis_intelligence), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Combat;

/// <summary>
/// What a player ship takes into battle: its slots and crew. UQM's init_sis
/// reads them from GLOBAL_SIS, the one flagship's; here each player ship,
/// the flagship or an escort, gives its own (stage 4, decision 3).
/// </summary>
public sealed class SisLoadout
{
    /// <param name="driveSlots">The 11 drive slots (sis.h's module numbers, <see cref="ShipModules"/>'s).</param>
    /// <param name="jetSlots">The 8 jet slots.</param>
    /// <param name="moduleSlots">The 16 module slots.</param>
    /// <param name="crew">The crew aboard, the captain not counted (GLOBAL_SIS's CrewEnlisted).</param>
    /// <param name="kitCrew">Crew the ship holds beside its crew pods (an escort's kit's, <see cref="ShipModules.KitCrew"/>).</param>
    /// <param name="carriesBomb">Whether the Chmmr's bomb is aboard (the flagship's, CHMMR_BOMB_STATE 3).</param>
    public SisLoadout(IReadOnlyList<byte> driveSlots, IReadOnlyList<byte> jetSlots, IReadOnlyList<byte> moduleSlots,
        int crew, int kitCrew = 0, bool carriesBomb = false)
    {
        if (driveSlots.Count != ShipModules.DriveSlotCount || jetSlots.Count != ShipModules.JetSlotCount
            || moduleSlots.Count != ShipModules.ModuleSlotCount)
            throw new ArgumentException($"{ShipModules.DriveSlotCount} drive, {ShipModules.JetSlotCount} jet and "
                                        + $"{ShipModules.ModuleSlotCount} module slots");
        if (crew < 0 || crew >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(crew), $"{crew} crew");
        // The most crew, every module slot a crew pod, the kit's and the captain, is a COUNT.
        if (kitCrew < 0 || kitCrew > ushort.MaxValue - ShipModules.ModuleSlotCount * ShipLoadout.CrewPodCapacity - 1)
            throw new ArgumentOutOfRangeException(nameof(kitCrew), $"{kitCrew} kit crew");
        DriveSlots = driveSlots.ToArray();
        JetSlots = jetSlots.ToArray();
        ModuleSlots = moduleSlots.ToArray();
        Crew = crew;
        KitCrew = kitCrew;
        CarriesBomb = carriesBomb;
    }

    /// <summary>
    /// A player ship's slots and the crew it has aboard. A flagship holds
    /// crew in its crew pods only, as UQM's does (no kit crew); the bomb is
    /// only ever the flagship's.
    /// </summary>
    public static SisLoadout From(ShipKind kind, ShipModules modules, int crew, bool carriesBomb = false)
    {
        if (kind == ShipKind.Flagship && modules.KitCrew != 0)
            throw new ArgumentException($"a flagship holds crew in crew pods only, not {modules.KitCrew} beside them");
        if (kind != ShipKind.Flagship && carriesBomb)
            throw new ArgumentException("only the flagship carries the bomb");
        return new(modules.DriveSlots, modules.JetSlots, modules.ModuleSlots, crew, modules.KitCrew, carriesBomb);
    }

    public IReadOnlyList<byte> DriveSlots { get; }
    public IReadOnlyList<byte> JetSlots { get; }
    public IReadOnlyList<byte> ModuleSlots { get; }
    public int Crew { get; }
    public int KitCrew { get; }
    public bool CarriesBomb { get; }
}

/// <summary>
/// What a player ship had left when its description was freed (uninit_sis):
/// its crew, the captain not counted, and whether it was destroyed (no
/// crew left, the captain lost with them). UQM writes the crew back to
/// GLOBAL_SIS's CrewEnlisted, crew_level - 1, which for a ship destroyed
/// is ~0, UQM's game over; the port leaves it to the world (an escort
/// destroyed is a wreck, stage 4, decision 7).
/// </summary>
public readonly record struct SisBattleResult(int CrewLeft, bool Destroyed);

/// <summary>
/// A player ship's description as init_sis builds it, before the battle
/// binds it (<see cref="SisShip.Describe"/>): the RACE_DESC, the blasters'
/// MISSILE_BLOCKs by the slots they fire from (the nose, the spread pair,
/// the side pair, the tail), and the tracking systems that count
/// (SIS_DATA).
/// </summary>
public sealed record SisDescription(RaceDesc RaceDesc, IReadOnlyList<MissileBlock> Blasters, int Trackers);

/// <summary>
/// sis_ship.c in battle: a player ship (the flagship, or an escort flying
/// the flagship's battle code, stage 4, decision 3) whose description is
/// built from its own slots and crew: guns, blasters and cannons in module
/// slots 15 (the nose), 14 (a spread pair), 13 (both sides) and 0 (the
/// tail), each costing energy by its kind; tracking systems (at most three
/// count) turning its shots towards the nearest enemy and costing energy;
/// point defence lasers (the Special); energy from dynamos (faster) and
/// furnaces (more); thrust from fusion thrusters and turning from jets;
/// crew from crew pods and an escort's kit, the captain on top. The AI can
/// fly it (sis_intelligence, UQM's Cyborg option's), though no player ship
/// is flown so.
/// <para>
/// Where UQM reads globals the inputs are explicit (<see cref="SisLoadout"/>)
/// and the results too: <see cref="BombCarrier"/> for BOMB_CARRIER, which
/// init_sis sets, and <see cref="Result"/> for the crew uninit_sis writes
/// back. A new <see cref="SisShip"/> is made for each battle.
/// </para>
/// </summary>
public sealed class SisShip : IShipCode
{
    // sis_ship.c's point defence lasers' colours, by its energy cost.
    private static readonly int[] ColorRange =
    {
        Battle.Rgb15(0x1F, 0x03, 0x00), Battle.Rgb15(0x1F, 0x07, 0x00), Battle.Rgb15(0x1F, 0x0A, 0x00),
        Battle.Rgb15(0x1F, 0x0E, 0x00), Battle.Rgb15(0x1F, 0x11, 0x00), Battle.Rgb15(0x1F, 0x15, 0x00),
        Battle.Rgb15(0x1F, 0x18, 0x00), Battle.Rgb15(0x1F, 0x1C, 0x00),
    };

    private readonly ShipDescription _description;
    private readonly Views? _hull, _blaster;

    /// <param name="description">The flagship's description (data/ships.json's sis_ship: sis_desc and its #defines).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="blaster">Its shots' sheets (ship_data.weapon).</param>
    /// <param name="loadout">This ship's slots and crew.</param>
    public SisShip(ShipDescription description, Views? hull, Views? blaster, SisLoadout loadout)
    {
        if (description.Id != ShipDescriptions.Flagship)
            throw new ArgumentException($"{description.Id} isn't the flagship's description", nameof(description));
        _description = description;
        _hull = hull;
        _blaster = blaster;
        Loadout = loadout;
    }

    public SisLoadout Loadout { get; }

    /// <summary>
    /// BOMB_CARRIER, which init_sis sets when the bomb is aboard
    /// (CHMMR_BOMB_STATE 3): the world's to set, and a battle with the
    /// bomb's carrier in it can't be retreated from (RunAwayAllowed).
    /// </summary>
    public bool BombCarrier => Loadout.CarriesBomb;

    /// <summary>The crew left when the ship's description was freed (uninit_sis); null until then.</summary>
    public SisBattleResult? Result { get; private set; }

    /// <summary>
    /// init_sis in battle, outside Super Melee: the description from
    /// <paramref name="loadout"/>'s slots (InitModuleSlots, InitWeaponSlots,
    /// InitDriveSlots, InitJetSlots), the captain counted on top of the
    /// crew, full energy. Without sheets or callbacks.
    /// </summary>
    public static SisDescription Describe(ShipDescription description, SisLoadout loadout)
    {
        var d = description;
        var rd = new RaceDesc
        {
            ShipFlags = d.Flags, CrewLevel = (ushort)d.Crew, MaxCrew = (ushort)d.MaxCrew,
            EnergyLevel = (byte)d.Energy, MaxEnergy = (byte)d.MaxEnergy,
            Characteristics = Characteristics.From(d.Characteristics),
        };
        var blasters = new List<MissileBlock>();
        int trackers = InitModuleSlots(d, rd, loadout);
        InitWeaponSlots(d, rd, loadout.ModuleSlots, blasters);
        InitDriveAndJetSlots(rd, loadout);

        // Not Super Melee: count the captain too. An escort's kit holds crew
        // beside its pods (stage 3, decision 10).
        rd.MaxCrew = (ushort)(rd.MaxCrew + loadout.KitCrew + 1);
        rd.CrewLevel = (ushort)(loadout.Crew + 1);
        rd.ShipFlags |= ShipFlags.PlayerCaptain;
        rd.EnergyLevel = rd.MaxEnergy;
        return new SisDescription(rd, blasters, trackers);
    }

    /// <summary>
    /// load_ship: init_sis's description with this ship's sheets and
    /// sis_ship.c's battle code. The ship's <see cref="Starship"/> must be
    /// made with no crew (as UQM's flagship's STARSHIP is): its crew is the
    /// loadout's with the captain on top, and spawn_ship would put a
    /// Starship's own crew in its place, without the captain. After the
    /// battle, the crew left is <see cref="Result"/>'s; the Starship's
    /// (cleanup_dead_ship's record) counts the captain.
    /// </summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        if (starship.CrewLevel != 0)
            throw new InvalidOperationException(
                $"a player ship's Starship has no crew of its own ({starship.CrewLevel}): its crew is its loadout's");
        var (rd, described, trackers) = Describe(_description, Loadout);
        rd.ManeuverabilityIndex = (ushort)_description.Intel.ManeuverabilityIndex;
        rd.WeaponRange = (ushort)_description.Intel.WeaponRange;
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, trackers);
        int blasterSpeed = _description.Define("BLASTER_SPEED");
        var blasters = described.Select(b =>
        {
            b.PreprocessFunc = e => BlasterPreprocess(battle, e, blasterSpeed);
            return b;
        }).ToArray();
        rd.Ship = _hull;
        rd.Weapon = _blaster;
        rd.Preprocess = BattlePreprocess;
        int laserRange = _description.Define("LASER_RANGE");
        rd.Postprocess = e => BattlePostprocess(battle, e, laserRange);
        rd.InitWeapon = e => InitializeBlasters(battle, e, blasters, trackers);
        rd.Uninit = Uninit;
        Result = null;
        return rd;
    }

    // InitModuleSlots: crew pods, tracking systems, point defence,
    // furnaces and dynamos. Returns how many tracking systems count.
    private static int InitModuleSlots(ShipDescription d, RaceDesc rd, SisLoadout loadout)
    {
        var c = rd.Characteristics;
        rd.MaxCrew = 0;
        int numTrackers = 0;
        for (int i = 0; i < ShipModules.ModuleSlotCount; ++i)
        {
            switch (loadout.ModuleSlots[ShipModules.ModuleSlotCount - 1 - i])
            {
                case ShipModules.CrewPod:
                    rd.MaxCrew += ShipLoadout.CrewPodCapacity;
                    break;
                case ShipModules.TrackingSystem:
                    ++numTrackers;
                    break;
                case ShipModules.AntimissileDefense:
                    c.SpecialEnergyCost += (byte)d.Define("ANTIMISSILE_ENERGY_INC");
                    break;
                case ShipModules.ShivaFurnace:
                    c.EnergyRegeneration += (byte)d.Define("SHIVA_ENERGY_REGEN_INC");
                    break;
                case ShipModules.DynamoUnit:
                    c.EnergyWait -= (byte)d.Define("DYNAMO_UNIT_ENERGY_WAIT_DEC");
                    if (c.EnergyWait < d.Define("MIN_ENERGY_WAIT"))
                        c.EnergyWait = (byte)d.Define("MIN_ENERGY_WAIT");
                    break;
            }
        }

        numTrackers = Math.Min(numTrackers, d.Define("MAX_TRACKING"));
        c.WeaponEnergyCost += (byte)(numTrackers * d.Define("TRACKER_ENERGY_COST"));
        if (c.SpecialEnergyCost != 0)
        {
            rd.ShipFlags |= ShipFlags.PointDefense;
            if (c.SpecialEnergyCost > d.Define("MAX_DEFENSE"))
                c.SpecialEnergyCost = (byte)d.Define("MAX_DEFENSE");
        }
        return numTrackers;
    }

    // InitWeaponSlots: a gun (1), blaster (2) or cannon (3) in slot 15 fires
    // ahead, in 14 a pair a facing either side of ahead, in 13 a pair to
    // each side, in 0 behind; each costs twice its kind in energy, does its
    // kind's damage and lives longer by it.
    private static void InitWeaponSlots(ShipDescription d, RaceDesc rd, IReadOnlyList<byte> moduleSlots,
        List<MissileBlock> blasters)
    {
        const int numModuleSlots = ShipModules.ModuleSlotCount;
        int blasterLife = d.Define("BLASTER_LIFE");
        for (int i = 0; i < numModuleSlots; ++i)
        {
            if (i == 3)
                i = numModuleSlots - 1;
            int whichGun = moduleSlots[numModuleSlots - 1 - i];
            if (whichGun < ShipModules.GunWeapon || whichGun > ShipModules.CannonWeapon)
                continue;   // not a gun

            whichGun -= ShipModules.GunWeapon - 1;
            rd.Characteristics.WeaponEnergyCost += (byte)(whichGun * 2);

            var mb = new MissileBlock
            {
                Flags = ElementFlags.IgnoreSimilar, BlastOffs = (short)d.Define("BLASTER_OFFSET"),
                Speed = (short)d.Define("BLASTER_SPEED"), HitPoints = (short)(d.Define("BLASTER_HITS") * whichGun),
                Damage = (short)(d.Define("BLASTER_DAMAGE") * whichGun),
                Life = (ushort)(blasterLife + (blasterLife >> 2) * (whichGun - 1)),
                Index = whichGun switch { 1 => 0, 2 => 9, _ => 16 },
            };
            switch (i)
            {
                case 0:   // NOSE WEAPON
                    rd.ShipFlags |= ShipFlags.FiresFore;
                    mb.PixOffs = (short)d.Define("SIS_VERT_OFFSET");
                    mb.Face = 0;
                    break;
                case 1:   // SPREAD WEAPON
                    rd.ShipFlags |= ShipFlags.FiresFore;
                    mb.PixOffs = (short)d.Define("SIS_VERT_OFFSET");
                    mb.Face = 1;
                    blasters.Add(mb);   // two of them
                    mb.Face = UqmMath.NormalizeFacing(-1);
                    break;
                case 2:   // SIDE WEAPON
                    rd.ShipFlags |= ShipFlags.FiresLeft | ShipFlags.FiresRight;
                    mb.PixOffs = (short)d.Define("SIS_HORZ_OFFSET");
                    mb.Face = (ushort)UqmMath.AngleToFacing(UqmMath.Quadrant);
                    blasters.Add(mb);   // two of them
                    mb.Face = UqmMath.NormalizeFacing(-UqmMath.AngleToFacing(UqmMath.Quadrant));
                    break;
                case numModuleSlots - 1:   // TAIL WEAPON
                    rd.ShipFlags |= ShipFlags.FiresAft;
                    mb.PixOffs = (short)d.Define("SIS_VERT_OFFSET");
                    mb.Face = (ushort)UqmMath.AngleToFacing(UqmMath.HalfCircle);
                    break;
            }
            blasters.Add(mb);
        }
    }

    // InitDriveSlots and InitJetSlots: thrust from the fusion thrusters,
    // turning from the jets (as in hyperspace, without its penalty).
    private static void InitDriveAndJetSlots(RaceDesc rd, SisLoadout loadout)
    {
        var drive = ShipDrive.ForBattle(loadout.DriveSlots.Count(s => s == ShipModules.FusionThruster),
            loadout.JetSlots.Count(s => s == ShipModules.TurningJets));
        var c = rd.Characteristics;
        c.MaxThrust = (ushort)drive.MaxThrust;
        c.ThrustIncrement = (ushort)drive.ThrustIncrement;
        c.ThrustWait = (byte)drive.ThrustWait;
        c.TurnWait = (byte)drive.TurnWait;
    }

    // uninit_sis: the crew left, the captain not counted.
    private void Uninit(RaceDesc rd)
    {
        int crew = rd.CrewLevel;
        if ((rd.ShipFlags & ShipFlags.PlayerCaptain) != 0)
            --crew;
        Result = new SisBattleResult(Math.Max(crew, 0), rd.CrewLevel == 0);
    }

    // sis_battle_preprocess: without point defence the Special does
    // nothing, without guns the weapon.
    private static void BattlePreprocess(Element e)
    {
        var starShip = e.Parent!;
        var rd = starShip.RaceDesc!;
        if (rd.Characteristics.SpecialEnergyCost == 0)
        {
            starShip.CurStatusFlags &= ~StatusFlags.Special;
            starShip.SpecialCounter = 2;
        }
        if ((rd.ShipFlags & (ShipFlags.FiresFore | ShipFlags.FiresRight | ShipFlags.FiresAft | ShipFlags.FiresLeft)) == 0)
        {
            starShip.CurStatusFlags &= ~StatusFlags.Weapon;
            starShip.WeaponCounter = 2;
        }
    }

    // sis_battle_postprocess: the Special fires the point defence.
    private static void BattlePostprocess(Battle battle, Element e, int laserRange)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0 && starShip.SpecialCounter == 0
            && starShip.RaceDesc!.Characteristics.SpecialEnergyCost != 0)
            SpawnPointDefense(battle, e, laserRange);
    }

    // spawn_point_defense: from the ship, an element that dies at once
    // (after the ship has moved); dying, it fires a laser at every element
    // near the ship that can collide and isn't cloaked, the ship's own
    // shots too but not the ship, paying for them all once.
    private static void SpawnPointDefense(Battle battle, Element e, int laserRange)
    {
        var starShip = e.Parent!;
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var defense = battle.List.Alloc();
            if (defense != null)
            {
                defense.PlayerNr = e.PlayerNr;
                defense.StateFlags = ElementFlags.Appearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
                defense.DeathFunc = d => SpawnPointDefense(battle, d, laserRange);
                defense.Parent = starShip;
                battle.List.Put(defense);
            }
            return;
        }

        var rd = starShip.RaceDesc!;
        byte cost = rd.Characteristics.SpecialEnergyCost;
        // UQM's table has 8 colours, for costs 0 to 7; MAX_DEFENSE's 8 reads
        // past its end (undefined). Only the laser's colour; the traces keep
        // to seven point defences at most.
        int laserColor = ColorRange[Math.Min((int)cost, ColorRange.Length - 1)];
        bool paidFor = false;
        var ship = starShip.Ship!;
        for (Element? o = battle.List.Tail, next; o != null; o = next)
        {
            next = o.Predecessor;
            if (o == ship || !Battle.Colliding(o) || Battle.ObjectCloaked(o))
                continue;
            short dx = (short)(o.Next.X - ship.Next.X);
            short dy = (short)(o.Next.Y - ship.Next.Y);
            if (dx < 0)
                dx = (short)-dx;
            if (dy < 0)
                dy = (short)-dy;
            dx = Battle.WorldToDisplay(dx);
            dy = Battle.WorldToDisplay(dy);
            if ((ushort)dx > laserRange || (ushort)dy > laserRange
                || (ushort)dx * (ushort)dx + (ushort)dy * (ushort)dy > laserRange * laserRange)
                continue;

            if (!paidFor)
            {
                if (!battle.DeltaEnergy(ship, (short)-(cost << 2)))
                    break;
                // The point defence's sound.
                starShip.SpecialCounter = rd.Characteristics.SpecialWait;
                paidFor = true;
            }

            var block = new LaserBlock
            {
                Cx = ship.Next.X, Cy = ship.Next.Y, Face = 0,
                Ex = (short)(o.Next.X - ship.Next.X), Ey = (short)(o.Next.Y - ship.Next.Y),
                Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = 0, Color = laserColor,
            };
            var laser = battle.InitializeLaser(block);
            if (laser != null)
            {
                laser.MassPoints = cost;
                laser.Parent = starShip;
                laser.Target = null;
                battle.List.Put(laser);
            }
        }
    }

    // blaster_collision: the hit's blast as the shot's kind has it.
    private static void BlasterCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1)
    {
        var blast = battle.WeaponCollision(e0, at0, e1, at1);
        if (blast == null)
            return;
        switch (e0.MassPoints)
        {
            case 2 * 1:   // BLASTER_DAMAGE * 1
                blast.LifeSpan = 2;
                blast.Current.Frame = e0.Current.Frame.SetAbs(0);
                blast.PreprocessFunc = null;
                break;
            case 2 * 2:
                blast.LifeSpan = 6;
                blast.Current.Frame = e0.Current.Frame.Inc();
                break;
            case 2 * 3:
                blast.LifeSpan = 7;
                blast.Current.Frame = e0.Current.Frame.SetAbs(20);
                break;
        }
    }

    // blaster_preprocess: a gun's shot grows, a cannon's spins; with
    // tracking systems, every few frames a shot turns a facing towards the
    // nearest enemy (turn_wait: the wait left in the low nibble, the wait
    // between turns in the high one).
    private static void BlasterPreprocess(Battle battle, Element e, int blasterSpeed)
    {
        switch (e.MassPoints)
        {
            case 2 * 1:
                if (e.Current.Frame.Index < 8)
                {
                    e.Next.Frame = e.Current.Frame.Inc();
                    e.StateFlags |= ElementFlags.Changing;
                }
                break;
            case 2 * 3:
                e.Next.Frame = e.Current.Frame.Index < 19 ? e.Current.Frame.Inc() : e.Current.Frame.SetAbs(16);
                e.StateFlags |= ElementFlags.Changing;
                break;
        }

        int wait;
        if ((e.TurnWait & 0x0F) != 0)
            --e.TurnWait;
        else if ((wait = e.TurnWait >> 4) != 0)
        {
            ushort facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
            if (battle.TrackShip(e, ref facing) > 0)
                e.Velocity.SetVector(blasterSpeed, facing);
            e.TurnWait = MakeByte(wait, wait);
        }
    }

    // initialize_blasters: a shot from each blaster, the tracking systems'
    // turn wait in it.
    private static IReadOnlyList<Element?> InitializeBlasters(Battle battle, Element ship, MissileBlock[] blasters,
        int trackers)
    {
        var starShip = ship.Parent!;
        int nt = (4 - trackers) & 3;
        var made = new Element?[blasters.Length];
        for (int i = 0; i < blasters.Length; ++i)
        {
            var mb = blasters[i];
            mb.Cx = ship.Next.X;
            mb.Cy = ship.Next.Y;
            mb.FrameArray = starShip.RaceDesc!.Weapon!;
            mb.Sender = ship.PlayerNr;
            mb.Face = UqmMath.NormalizeFacing(starShip.ShipFacing + mb.Face);
            var blaster = made[i] = battle.InitializeMissile(mb);
            if (blaster != null)
            {
                blaster.CollisionFunc = (e0, at0, e1, at1) => BlasterCollision(battle, e0, at0, e1, at1);
                blaster.TurnWait = MakeByte(nt, nt);
            }
        }
        return made;
    }

    // sis_intelligence: with point defence, the Special against a shot
    // within 2 frames or the enemy ship within 4 (and no dodging); without,
    // a slow ship keeps after a shot it would entice; and with tracking,
    // firing when the enemy ship is near and a gun points its way.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        int trackers)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;

        ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
        if (weapon.Object != null)
        {
            if (rd.Characteristics.SpecialEnergyCost != 0)
            {
                if (starShip.SpecialCounter == 0
                    && ((weapon.Object != null && weapon.WhichTurn <= 2)
                        || (concerns[Battle.EnemyShipIndex].Object != null
                            && concerns[Battle.EnemyShipIndex].WhichTurn <= 4)))
                    starShip.ShipInputState |= StatusFlags.Special;
                else
                    starShip.ShipInputState &= ~StatusFlags.Special;
                weapon.Object = null;
            }
            else if (rd.ManeuverabilityIndex < Battle.MediumShip
                     && weapon.MoveState == MoveState.Entice
                     && ((weapon.Object.StateFlags & ElementFlags.CrewObject) == 0 || weapon.WhichTurn <= 8)
                     && ((weapon.Object.StateFlags & ElementFlags.FiniteLife) == 0
                         || (weapon.Object.MassPoints >= 4 && weapon.WhichTurn == 2
                                                           && concerns[Battle.EnemyShipIndex].WhichTurn > 16)))
                weapon.MoveState = MoveState.Pursue;
        }

        battle.ShipIntelligence(ship, concerns, concernCounter);

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (trackers != 0
            && starShip.WeaponCounter == 0
            && (starShip.ShipInputState & StatusFlags.Weapon) == 0
            && enemy.Object != null
            && enemy.WhichTurn <= 16)
        {
            // Not across the seam, as UQM's.
            short deltaX = (short)(enemy.Object.Current.X - ship.Current.X);
            short deltaY = (short)(enemy.Object.Current.Y - ship.Current.Y);
            int directionFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(deltaX, deltaY)));

            var shipFlags = rd.ShipFlags;
            // UQM adds a QUADRANT (an angle) to a facing for each way a
            // gun fires: a whole turn of facings, so every way is tested
            // against the ship's own facing.
            int facing = starShip.ShipFacing;
            for (var fireFlags = ShipFlags.FiresFore; fireFlags <= ShipFlags.FiresLeft;
                 fireFlags = (ShipFlags)((int)fireFlags << 1), facing += UqmMath.Quadrant)
            {
                if ((shipFlags & fireFlags) != 0
                    && UqmMath.NormalizeFacing(directionFacing - facing + UqmMath.AngleToFacing(UqmMath.Quadrant >> 1))
                    <= UqmMath.AngleToFacing(UqmMath.Quadrant))
                {
                    starShip.ShipInputState |= StatusFlags.Weapon;
                    break;
                }
            }
        }
    }

    // MAKE_BYTE (lo, hi).
    private static byte MakeByte(int lo, int hi) => (byte)((hi << 4) | lo);
}
