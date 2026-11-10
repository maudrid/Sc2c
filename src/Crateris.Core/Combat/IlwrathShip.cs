// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/ilwrath/ilwrath.c: init_ilwrath, ilwrath_preprocess,
// initialize_flame, flame_preprocess, flame_collision,
// ilwrath_intelligence), Copyright Paul
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
/// ilwrath.c: the Ilwrath Avenger. Its weapon is a short-lived spout of
/// hellfire, carried along by the ship's own speed, that burns through
/// what it hits (non-solid after the first hit, not gone). Its Special
/// cloaks it: the ship fades to black over a few frames (a filled stamp,
/// which <see cref="Battle.ObjectCloaked"/> and so homing and point
/// defence pass over). Firing, or the Special again, uncloaks it, fading
/// back in; firing while cloaked first turns it to where the ship it
/// tracks will be in four frames.
/// <para>
/// Its AI (ilwrath_intelligence) closes in and flames within reach, and
/// cloaks when it can, but for the rule UQM reads from globals, made an
/// input here: <see cref="NoCloak"/>.
/// </para>
/// </summary>
public sealed class IlwrathShip : IShipCode
{
    // The cloak's colours, from the ship as it is to black.
    private static readonly int White = Battle.Rgb15(0x1F, 0x1F, 0x1F),
        Cyan = Battle.Rgb15(0x0A, 0x1F, 0x1F),
        DarkCyan = Battle.Rgb15(0x00, 0x14, 0x14),
        Blue = Battle.Rgb15(0x0A, 0x0A, 0x1F),
        DarkBlue = Battle.Rgb15(0x00, 0x00, 0x14);

    private const int Black = 0;

    private readonly ShipDescription _description;
    private readonly Views? _hull, _fire;

    /// <param name="description">The Ilwrath's description (data/ships.json's ilwrath: ilwrath_desc and its #defines).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="fire">Its hellfire's sheets (ship_data.weapon).</param>
    /// <param name="noCloak">
    /// Whether its AI never cloaks: UQM's ilwrath_intelligence doesn't in an
    /// encounter while PROBE_ILWRATH_ENCOUNTER is set, which is the Earth
    /// fight's (stage 4 item 4: the Earth battle's own rule, set by the
    /// visit that starts it, not the story field). Keys still cloak it.
    /// </param>
    public IlwrathShip(ShipDescription description, Views? hull, Views? fire, bool noCloak = false)
    {
        if (description.Id != ShipDescriptions.Ilwrath)
            throw new ArgumentException($"{description.Id} isn't the Ilwrath's description", nameof(description));
        _description = description;
        _hull = hull;
        _fire = fire;
        NoCloak = noCloak;
    }

    /// <summary>Whether its AI never cloaks (the Earth fight).</summary>
    public bool NoCloak { get; }

    /// <summary>init_ilwrath: ilwrath_desc with its sheets and ilwrath.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = new RaceDesc
        {
            ShipFlags = d.Flags, CrewLevel = (ushort)d.Crew, MaxCrew = (ushort)d.MaxCrew,
            EnergyLevel = (byte)d.Energy, MaxEnergy = (byte)d.MaxEnergy,
            Characteristics = Characteristics.From(d.Characteristics),
            Ship = _hull, Weapon = _fire,
            // No Special: ilwrath_desc's special sheets are NULL_RESOURCE (the cloak draws none).
        };
        var flame = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("ILWRATH_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            PreprocessFunc = FlamePreprocess, BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        int weaponEnergyCost = d.Define("WEAPON_ENERGY_COST"), specialEnergyCost = d.Define("SPECIAL_ENERGY_COST"),
            lookAhead = d.Define("LOOK_AHEAD");
        rd.Preprocess = e => Preprocess(battle, e, weaponEnergyCost, specialEnergyCost, lookAhead);
        rd.InitWeapon = e => InitializeFlame(battle, e, flame);
        rd.ManeuverabilityIndex = (ushort)d.Intel.ManeuverabilityIndex;
        rd.WeaponRange = (ushort)d.Intel.WeaponRange;
        bool noCloak = NoCloak;
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, noCloak);
        return rd;
    }

    // ilwrath_intelligence: always after the enemy ship, not dodging when
    // it could be flaming; within 6 frames of it (or 10 with a shot as
    // near), flame, cloaked or not (a cloaked ship thrusting); otherwise
    // cloak when it may.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        bool noCloak)
    {
        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        enemy.MoveState = MoveState.Pursue;
        if (enemy.Object != null && enemy.WhichTurn <= 10)
            concerns[Battle.EnemyWeaponIndex].Object = null;   // don't dodge when you could be flaming

        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        if (enemy.Object != null
            && (enemy.WhichTurn <= 6
                || (enemy.WhichTurn <= 10 && concerns[Battle.EnemyWeaponIndex].WhichTurn <= 10)))
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            if (Battle.ObjectCloaked(ship))
            {
                // UQM's: ~LEFT | RIGHT clears only LEFT.
                starShip.ShipInputState &= ~StatusFlags.Left | StatusFlags.Right;
                starShip.ShipInputState |= StatusFlags.Thrust;
            }
            starShip.ShipInputState |= StatusFlags.Weapon;
        }
        else if (starShip.SpecialCounter == 0 && !noCloak)
        {
            starShip.ShipInputState &= ~StatusFlags.Special;
            if (!Battle.ObjectCloaked(ship) && (starShip.ShipInputState & StatusFlags.Weapon) == 0)
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // flame_preprocess: the next frame every next_turn + 1 frames.
    private static void FlamePreprocess(Element e)
    {
        if (e.TurnWait > 0)
            --e.TurnWait;
        else
        {
            e.Next.Frame = e.Current.Frame.Inc();
            e.StateFlags |= ElementFlags.Changing;
            e.TurnWait = e.NextTurn;
        }
    }

    // flame_collision: the hit, and the flame burns on, touching nothing more.
    private static void FlameCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1, (short X, short Y) at1)
    {
        battle.WeaponCollision(e0, at0, e1, at1);
        e0.StateFlags &= ~ElementFlags.Disappearing;
        e0.StateFlags |= ElementFlags.NonSolid;
    }

    // initialize_flame: a flame ahead, with the ship's own velocity added.
    private static IReadOnlyList<Element?> InitializeFlame(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var flame = battle.InitializeMissile(block);
        if (flame != null)
        {
            var (dx, dy) = ship.Velocity.Current();
            flame.Velocity.Delta(dx, dy);
            flame.Current.X -= (short)Velocity.VelocityToWorld(dx);
            flame.Current.Y -= (short)Velocity.VelocityToWorld(dy);
            flame.CollisionFunc = (e0, at0, e1, at1) => FlameCollision(battle, e0, at0, e1, at1);
            flame.TurnWait = 0;
        }
        return new[] { flame };
    }

    // ilwrath_preprocess: the cloak. A cloaked or fading ship is a filled
    // stamp: firing (with the energy to), or the Special when it may,
    // fades it back in a colour a frame (white, then the plain stamp),
    // firing from black first turning it towards where its target will be;
    // otherwise it fades on to black, where nothing can target it. The
    // Special, when it may and with the energy, cloaks it, starting white.
    private static void Preprocess(Battle battle, Element e, int weaponEnergyCost, int specialEnergyCost, int lookAhead)
    {
        var starShip = e.Parent!;
        var statusFlags = starShip.CurStatusFlags;
        if (e.Prim == PrimType.StampFill)
        {
            int color = e.PrimColor;
            bool weaponDischarge = (statusFlags & StatusFlags.Weapon) != 0
                                   && starShip.RaceDesc!.EnergyLevel >= weaponEnergyCost;
            if (weaponDischarge
                || (starShip.SpecialCounter == 0 && ((statusFlags & StatusFlags.Special) != 0 || color != Black)))
            {
                if (color == White)
                    e.Prim = PrimType.Stamp;
                else if (color == Cyan)
                    e.PrimColor = White;
                else if (color == DarkCyan)
                    e.PrimColor = Cyan;
                else if (color == Blue)
                    e.PrimColor = DarkCyan;
                else if (color == DarkBlue)
                    e.PrimColor = Blue;
                else
                {
                    // The cloak's off sound.
                    e.PrimColor = DarkBlue;
                    if (weaponDischarge)
                    {
                        ushort facing = starShip.ShipFacing;
                        if (battle.TrackShip(e, ref facing) >= 0)
                        {
                            var target = e.Target!;
                            var v = target.Velocity;
                            var (dx0, dy0) = v.Next((ushort)lookAhead);
                            v = e.Velocity;
                            var (dx1, dy1) = v.Next((ushort)lookAhead);
                            // Across the seam where contact is tested so (decision 5).
                            short dx = battle.SeamDeltaX((target.Current.X + dx0) - (e.Current.X + dx1));
                            short dy = battle.SeamDeltaY((target.Current.Y + dy0) - (e.Current.Y + dy1));
                            starShip.ShipFacing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(UqmMath.ArcTan(dx, dy)));
                            if (e.TurnWait == 0)
                                ++e.TurnWait;
                            e.Next.Frame = e.Next.Frame.SetAbs(starShip.ShipFacing);
                        }
                        e.Target = null;
                    }
                }

                e.StateFlags |= ElementFlags.Changing;
                statusFlags &= ~StatusFlags.Special;
                starShip.SpecialCounter = 0;
            }
            else if (color != Black)
            {
                if (color == DarkBlue)
                {
                    e.PrimColor = Black;
                    battle.Untarget(e);
                }
                else if (color == Blue)
                    e.PrimColor = DarkBlue;
                else if (color == DarkCyan)
                    e.PrimColor = Blue;
                else if (color == Cyan)
                    e.PrimColor = DarkCyan;
                else
                    e.PrimColor = Cyan;

                e.StateFlags |= ElementFlags.Changing;
            }
        }

        if ((statusFlags & StatusFlags.Special) != 0 && starShip.SpecialCounter == 0
                                                     && battle.DeltaEnergy(e, (short)-specialEnergyCost))
        {
            e.PrimColor = White;
            e.Prim = PrimType.StampFill;
            // The cloak's on sound.
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
            e.StateFlags |= ElementFlags.Changing;
        }
    }
}
