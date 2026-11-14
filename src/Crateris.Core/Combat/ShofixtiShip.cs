// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/shofixti/shofixti.c: init_shofixti,
// initialize_standard_missile, destruct_preprocess,
// self_destruct_kill_objects, shofixti_destruct_death, self_destruct,
// shofixti_intelligence, shofixti_postprocess), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// shofixti.c: the Shofixti Scout, as the recruited Shofixti fly it
/// (SHOFIXTI_RECRUITED; Tanaka's and Katana's damaged scout is stage 3b's).
/// Its weapon is a dart ahead; its special is the Glory Device, which
/// counts on the captain's window: each press and each release of the key
/// moves its frame on, and at the third from the end the ship blows
/// itself up, taking crew from every ship and hit points from every
/// solid thing in range (more the nearer), its own side's shots among
/// them as UQM's, but no teammate's ship or shot (stage 4b's co-op rule),
/// and any Orz marine aboard a ship in range.
/// <para>
/// Its AI presses and releases the special until the window is near the
/// end, then blows up with the enemy within 4 frames, or a shot about to
/// hit it that would kill it (now and then).
/// </para>
/// </summary>
public sealed class ShofixtiShip : IShipCode
{
    public const string Id = "shofixti";

    private static readonly int White = Battle.Rgb15(0x1F, 0x1F, 0x1F),
        Yellow = Battle.Rgb15(0x1F, 0x1F, 0x0A),
        Red = Battle.Rgb15(0x1F, 0x0A, 0x0A),
        Brown = Battle.Rgb15(0x14, 0x0A, 0x00),
        DarkRed = Battle.Rgb15(0x14, 0x00, 0x00);

    // DESTRUCT_SWITCH: (NUM_EXPLOSION_FRAMES * 3) - 3.
    private const int DestructSwitch = Battle.NumExplosionFrames * 3 - 3;

    private readonly ShipDescription _description;
    private readonly Views? _hull, _dart, _destruct, _captain;

    /// <param name="description">The Shofixti's description (data/ships.json's shofixti).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="dart">Its dart's sheets (ship_data.weapon).</param>
    /// <param name="destruct">The Glory Device's explosion's sheets (ship_data.special).</param>
    /// <param name="captain">
    /// Its captain's window's sheets (captain_control's), whose frame count
    /// the Glory Device counts to (20 in the content); without them, as
    /// UQM's without a captain's window, it never goes off.
    /// </param>
    public ShofixtiShip(ShipDescription description, Views? hull, Views? dart, Views? destruct, Views? captain)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _dart = dart;
        _destruct = destruct;
        _captain = captain;
    }

    // captain_control.special: the captain window's frame the Glory
    // Device counts on (DrawCaptainsWindow sets it to frame 12 as the ship
    // warps in); its sheet's frame count.
    private sealed class Glory
    {
        public required Frame Special;
        public required int DestructRange, MaxDestruction;

        public int Left => Special.Count - Special.Index;
    }

    /// <summary>init_shofixti: shofixti_desc with its sheets and shofixti.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _dart, _destruct);
        var dart = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("SHOFIXTI_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        // DrawCaptainsWindow: background, turn (+1), thrust (+5), weapon (+3), special (+3).
        var g = new Glory
        {
            Special = _captain is { } captain ? captain[0].SetAbs(1 + 5 + 3 + 3) : default,
            DestructRange = d.Define("DESTRUCT_RANGE"), MaxDestruction = d.Define("MAX_DESTRUCTION"),
        };
        rd.InitWeapon = e => InitializeStandardMissile(battle, e, dart);
        rd.Postprocess = e => Postprocess(battle, e, g);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, g);
        return rd;
    }

    // initialize_standard_missile: a dart ahead.
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

    // destruct_preprocess: the ship (its explosion's 36 frames counting
    // down) flashes white, yellow, white for three frames; then it's
    // drawn no more and stays still, and the explosion is an element of
    // its own, which steps through the explosion's frames cycling white,
    // yellow, red, brown to dark red.
    private static void DestructPreprocess(Battle battle, Element e)
    {
        e.StateFlags |= ElementFlags.Changing;
        if (e.LifeSpan > DestructSwitch)
        {
            e.Prim = PrimType.StampFill;
            e.PrimColor = e.LifeSpan == DestructSwitch + 2 ? Yellow : White;
        }
        else if (e.LifeSpan < DestructSwitch)
        {
            int color = e.PrimColor;
            e.Next.Frame = e.Current.Frame.Inc();
            if (color == White)
                e.PrimColor = Yellow;
            else if (color == Yellow)
                e.PrimColor = Red;
            else if (color == Red)
                e.PrimColor = Brown;
            else if (color == Brown)
                e.PrimColor = DarkRed;
        }
        else
        {
            e.Prim = PrimType.None;
            e.PreprocessFunc = null;

            var destruct = battle.List.Alloc();
            if (destruct != null)
            {
                var starShip = e.Parent!;
                battle.List.Put(destruct);
                destruct.Parent = starShip;
                destruct.HitPoints = 0;
                destruct.MassPoints = 0;
                destruct.PlayerNr = Battle.NeutralPlayerNum;
                destruct.StateFlags = ElementFlags.Appearing | ElementFlags.FiniteLife | ElementFlags.NonSolid;
                destruct.Prim = PrimType.StampFill;
                destruct.PrimColor = White;
                var special = starShip.RaceDesc!.Special!;
                destruct.Current.FrameArray = special;
                destruct.Current.Frame = special[0];
                destruct.LifeSpan = (ushort)destruct.Current.Frame.Count;
                destruct.Current.X = e.Current.X;
                destruct.Current.Y = e.Current.Y;
                destruct.PreprocessFunc = x => DestructPreprocess(battle, x);
                destruct.PostprocessFunc = null;
                destruct.DeathFunc = null;
                destruct.Velocity.Zero();
            }
        }
    }

    // self_destruct_kill_objects: in the helper's death, after every
    // element has moved: every solid element (and an Orz marine aboard a
    // ship) within range, but a teammate's, loses 1 + 18 * (180 - its
    // distance) / 180 crew or hit points, a ship left without crew or a
    // thing without hit points dying. The planet is untouched.
    private static void SelfDestructKillObjects(Battle battle, Element e, Glory g)
    {
        int range = g.DestructRange;
        for (Element? o = battle.List.Head, next; o != null; o = next)
        {
            next = o.Successor;
            if (!Battle.Colliding(o) && !OrzShip.IsMarineAboard(o))
                continue;
            // No friendly fire (stage 4b): its teammates' ships and shots are spared.
            if (Battle.Teammates(o, e))
                continue;

            // Across the seam where contact is tested so (decision 5).
            short dx = battle.SeamDeltaX(o.Next.X - e.Next.X);
            if (dx < 0)
                dx = (short)-dx;
            short dy = battle.SeamDeltaY(o.Next.Y - e.Next.Y);
            if (dy < 0)
                dy = (short)-dy;
            dx = Battle.WorldToDisplay(dx);
            dy = Battle.WorldToDisplay(dy);
            uint dist = (uint)(dx * dx + dy * dy);
            if (dx <= range && dy <= range && dist <= (uint)(range * range))
            {
                int destruction = 1 + g.MaxDestruction * (range - UqmMath.SquareRoot(dist)) / range;
                if ((o.StateFlags & ElementFlags.PlayerShip) != 0)
                {
                    if (!battle.DeltaCrew(o, (short)-destruction))
                        o.LifeSpan = 0;
                }
                else if (!Battle.GravityMass(o.MassPoints))
                {
                    if (destruction < o.HitPoints)
                        o.HitPoints -= (ushort)destruction;
                    else
                    {
                        o.HitPoints = 0;
                        o.LifeSpan = 0;
                    }
                }
            }
        }
    }

    // shofixti_destruct_death: the ship blows up in its own way (its
    // explosion's sound); the winner, if none yet, is the other side's
    // ship alive, or with none the Shofixti; a Shofixti that had already
    // won plays no ditty. It's counted dead.
    private static void DestructDeath(Battle battle, Element ship)
    {
        var starShip = ship.Parent!;
        battle.StartShipExplosion(ship);
        ship.PreprocessFunc = e => DestructPreprocess(battle, e);

        var winner = battle.Winner;
        if (winner == null)
            battle.SetWinnerStarShip(battle.FindAliveStarShip(ship) ?? starShip);
        else if (winner == starShip)
            starShip.CurStatusFlags &= ~StatusFlags.PlayVictoryDitty;
        battle.RecordShipDeath(ship);
    }

    // self_destruct: a helper element at the ship, dying this frame (its
    // death, near the end of the frame, does the damage); the ship loses
    // all its crew and dies the next frame, in its own way.
    private static void SelfDestruct(Battle battle, Element e, Glory g)
    {
        var destruct = battle.List.Alloc();
        if (destruct != null)
        {
            destruct.PlayerNr = e.PlayerNr;
            destruct.StateFlags = ElementFlags.Appearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
            destruct.Next.X = e.Next.X;
            destruct.Next.Y = e.Next.Y;
            destruct.LifeSpan = 0;
            destruct.Parent = e.Parent;
            destruct.Target = null;
            destruct.DeathFunc = x => SelfDestructKillObjects(battle, x, g);
            battle.List.Put(destruct);
        }

        battle.DeltaCrew(e, (short)-e.CrewLevel);
        e.StateFlags |= ElementFlags.NonSolid;
        e.LifeSpan = 0;
        e.DeathFunc = x => DestructDeath(battle, x);
    }

    // shofixti_intelligence: the stock AI; then, with the special ready,
    // the key let go if pressed, else pressed while the captain's window
    // is far from the end, or the enemy is within 4 frames, or the enemy
    // ship itself is about to hit it (with one crew left), or a shot that
    // would kill it will hit it within 2 frames (half the time).
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter, Glory g)
    {
        battle.ShipIntelligence(ship, concerns, concernCounter);

        var starShip = ship.Parent!;
        if (starShip.SpecialCounter != 0)
            return;

        if ((starShip.ShipInputState & StatusFlags.Special) != 0)
            starShip.ShipInputState &= ~StatusFlags.Special;
        else
        {
            ref var weapon = ref concerns[Battle.EnemyWeaponIndex];
            ref var enemy = ref concerns[Battle.EnemyShipIndex];
            if (starShip.RaceDesc!.Special != null
                && (g.Left > 5
                    || (enemy.Object != null && enemy.WhichTurn <= 4)
                    || (weapon.Object != null
                        // means IMMEDIATE WEAPON
                        && (((weapon.Object.StateFlags & ElementFlags.PlayerShip) != 0 && ship.CrewLevel == 1)
                            || (battle.PlotIntercept(weapon.Object, ship, 2, 0) != 0
                                && weapon.Object.MassPoints >= ship.CrewLevel
                                && (battle.Random.Next("shofixti_intelligence") & 1) != 0)))))
                starShip.ShipInputState |= StatusFlags.Special;
        }
    }

    // shofixti_postprocess: each press or release of the special moves the
    // captain window's frame on; at the third from its end, the Glory Device.
    private static void Postprocess(Battle battle, Element e, Glory g)
    {
        var starShip = e.Parent!;
        if (((starShip.CurStatusFlags ^ starShip.OldStatusFlags) & StatusFlags.Special) != 0)
        {
            g.Special = g.Special.Inc();
            if (g.Left == 3)
                SelfDestruct(battle, e, g);
        }
    }
}
