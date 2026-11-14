// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/melnorme/melnorme.c: init_melnorme, pump_up_preprocess,
// pump_up_postprocess, animate, pump_up_collision, initialize_pump_up,
// confuse_preprocess, confusion_collision, initialize_confusion,
// initialize_test_pump_up, melnorme_intelligence, melnorme_postprocess),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>
/// melnorme.c: the Melnorme Trader. Its weapon is a blaster that charges
/// while held (a new shot at its nose each frame, doubling its damage
/// every LEVEL_COUNTER frames up to four levels) and flies when let go;
/// its special is a confusion shot, which sticks to the ship it hits and
/// turns its keys at random for 400 frames. With several players, a
/// confusion shot confuses the ship it hits: one already confusing another
/// player isn't taken from it (UQM's reuses its one confusion, whatever
/// ship it's on: with one ship a side that's the same ship).
/// <para>
/// Its AI aims its blaster as if it flew at once (a test shot), fires
/// confusion when its special is ready and the shot would hit (its
/// confusion aimed with the ship held still), and holds off firing when
/// short of energy.
/// </para>
/// </summary>
public sealed class MelnormeShip : IShipCode
{
    public const string Id = "melnorme";

    private const int ReverseDir = 128;

    private readonly ShipDescription _description;
    private readonly Views? _hull, _pumpUp, _confuse;

    /// <param name="description">The Melnorme's description (data/ships.json's melnorme).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="pumpUp">Its blaster's sheets (ship_data.weapon).</param>
    /// <param name="confuse">The confusion's sheets (ship_data.special).</param>
    public MelnormeShip(ShipDescription description, Views? hull, Views? pumpUp, Views? confuse)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _pumpUp = pumpUp;
        _confuse = confuse;
    }

    // melnorme.c's #defines and its three weapon functions' blocks.
    private sealed class Defines
    {
        public required int WeaponWait, LevelCounter, MaxPump, PumpUpSpeed, PumpUpLife, PumpUpDamage,
            MinPumpitudeAnims, NumPumpAnims, SpecialEnergyCost, WeaponEnergyCost;

        public MissileBlock PumpUp, TestPumpUp, Confusion;
    }

    /// <summary>init_melnorme: melnorme_desc with its sheets and melnorme.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _pumpUp, _confuse);
        var f = new Defines
        {
            WeaponWait = d.Define("WEAPON_WAIT"), LevelCounter = d.Define("LEVEL_COUNTER"), MaxPump = d.Define("MAX_PUMP"),
            PumpUpSpeed = d.Define("PUMPUP_SPEED"), PumpUpLife = d.Define("PUMPUP_LIFE"),
            PumpUpDamage = d.Define("PUMPUP_DAMAGE"), MinPumpitudeAnims = d.Define("MIN_PUMPITUDE_ANIMS"),
            NumPumpAnims = d.Define("NUM_PUMP_ANIMS"), SpecialEnergyCost = d.Define("SPECIAL_ENERGY_COST"),
            WeaponEnergyCost = d.Define("WEAPON_ENERGY_COST"),
        };
        short offset = (short)d.Define("MELNORME_OFFSET");
        f.PumpUp = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = offset, Speed = Battle.DisplayToWorld(offset),
            HitPoints = (short)f.PumpUpDamage, Damage = (short)f.PumpUpDamage, Life = 2, BlastOffs = 0,
        };
        f.TestPumpUp = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = offset, Speed = (short)f.PumpUpSpeed,
            HitPoints = (short)f.PumpUpDamage, Damage = (short)f.PumpUpDamage, Life = (ushort)f.PumpUpLife, BlastOffs = 0,
        };
        f.Confusion = new MissileBlock
        {
            Index = 0, Flags = ElementFlags.IgnoreSimilar, PixOffs = offset, Speed = (short)d.Define("CMISSILE_SPEED"),
            HitPoints = (short)d.Define("CMISSILE_HITS"), Damage = (short)d.Define("CMISSILE_DAMAGE"),
            Life = (ushort)d.Define("CMISSILE_LIFE"), PreprocessFunc = e => ConfusePreprocess(battle, e),
            BlastOffs = (short)d.Define("CMISSILE_OFFSET"),
        };
        rd.InitWeapon = e => InitializePumpUp(battle, e, f);
        rd.Postprocess = e => Postprocess(battle, e, f);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count, f);
        return rd;
    }

    // The shot's frame steps through its level's animation and back.
    private static int Pumped(Element e, Frame frame, int numPumpAnims)
    {
        int frameIndex = frame.Index;
        if (((e.TurnWait & ReverseDir) != 0 && frameIndex % numPumpAnims != 0)
            || ((e.TurnWait & ReverseDir) == 0 && (frameIndex + 1) % numPumpAnims == 0))
        {
            --frameIndex;
            e.TurnWait |= ReverseDir;
        }
        else
        {
            ++frameIndex;
            e.TurnWait &= unchecked((byte)~ReverseDir);
        }
        return frameIndex;
    }

    // pump_up_preprocess: a flying shot animates every other frame.
    private static void PumpUpPreprocess(Element e, Defines f)
    {
        if ((--e.ThrustWait & 1) != 0)
        {
            e.Next.Frame = e.Current.Frame.SetAbs(Pumped(e, e.Current.Frame, f.NumPumpAnims));
            e.StateFlags |= ElementFlags.Changing;
        }
    }

    // pump_up_postprocess: a shot just made stays still; one already
    // there is replaced by a new one at the ship's nose, which keeps its
    // charge, its frame and its count, a level up (its sound) every
    // LEVEL_COUNTER frames; its damage that of the one it replaces. Held,
    // the weapon waits; let go, the new shot flies (its sound). The old
    // one is drawn no more and touches nothing.
    private static void PumpUpPostprocess(Battle battle, Element e, Defines f)
    {
        if ((e.StateFlags & ElementFlags.Appearing) != 0)
        {
            e.Velocity.Zero();
            return;
        }

        var starShip = e.Parent!;
        var ship = starShip.Ship!;
        var pumpUp = InitializePumpUp(battle, ship, f)[0];
        battle.DeltaEnergy(ship, 0);
        if (pumpUp != null)
        {
            pumpUp.Current.Frame = e.Current.Frame;
            pumpUp.TurnWait = e.TurnWait;
            pumpUp.ThrustWait = e.ThrustWait;
            if (--pumpUp.ThrustWait == 0)
            {
                if ((pumpUp.TurnWait & ~ReverseDir) < f.MaxPump - 1)
                {
                    ++pumpUp.TurnWait;
                    pumpUp.Current.Frame = pumpUp.Current.Frame.SetRel(f.NumPumpAnims);
                }
                pumpUp.ThrustWait = (byte)f.LevelCounter;
            }

            pumpUp.MassPoints = (byte)(f.PumpUpDamage << (e.TurnWait & ~ReverseDir));
            pumpUp.HitPoints = pumpUp.MassPoints;
            pumpUp.Parent = starShip;

            if ((pumpUp.ThrustWait & 1) != 0)
                pumpUp.Current.Frame = pumpUp.Current.Frame.SetAbs(Pumped(pumpUp, pumpUp.Current.Frame, f.NumPumpAnims));

            if ((starShip.CurStatusFlags & starShip.OldStatusFlags & StatusFlags.Weapon) != 0)
                starShip.WeaponCounter = (byte)f.WeaponWait;
            else
            {
                pumpUp.LifeSpan = (ushort)f.PumpUpLife;
                pumpUp.PreprocessFunc = x => PumpUpPreprocess(x, f);
                pumpUp.PostprocessFunc = null;
                int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
                int speed = Velocity.WorldToVelocity(f.PumpUpSpeed);
                pumpUp.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));
            }

            battle.List.Put(pumpUp);
        }

        e.Prim = PrimType.None;
        e.StateFlags |= ElementFlags.NonSolid;
    }

    // pump_up_collision: the hit's blast half the shot's width ahead, then
    // the blast animates longer the more the shot was charged.
    private static void PumpUpCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1, Defines f)
    {
        var r = e0.Next.Frame.Rect;
        byte oldThrustWait = e0.ThrustWait;
        e0.BlastOffset = (byte)(r.Width >> 1);
        var blast = battle.WeaponCollision(e0, at0, e1, at1);
        e0.ThrustWait = oldThrustWait;

        if (blast != null)
        {
            blast.LifeSpan = (ushort)(f.MinPumpitudeAnims + (e0.TurnWait & ~ReverseDir));
            blast.TurnWait = blast.NextTurn = 0;
            blast.PreprocessFunc = Battle.AnimationPreprocess;
            blast.Current.FrameArray = e0.Next.FrameArray;
            blast.Current.Frame = blast.Current.FrameArray![0].SetAbs(f.MaxPump * f.NumPumpAnims);
        }
    }

    // initialize_pump_up: a shot at the ship's nose, which its
    // postprocess replaces while the key is held.
    private static IReadOnlyList<Element?> InitializePumpUp(Battle battle, Element ship, Defines f)
    {
        var starShip = ship.Parent!;
        var block = f.PumpUp;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var pumpUp = battle.InitializeMissile(block);
        if (pumpUp != null)
        {
            pumpUp.PostprocessFunc = x => PumpUpPostprocess(battle, x, f);
            pumpUp.CollisionFunc = (e0, at0, e1, at1) => PumpUpCollision(battle, e0, at0, e1, at1, f);
            pumpUp.ThrustWait = (byte)f.LevelCounter;
        }
        return new[] { pumpUp };
    }

    // confuse_preprocess: the confusion shot spins as it flies; stuck on a
    // ship (non-solid), it follows it, and while it's confusing (its turn
    // wait the left or right key), it spins through its other frames, sets
    // the ship's keys to turn that way without its special, and leaves an
    // image of itself a frame. One whose ship is gone goes.
    private static void ConfusePreprocess(Battle battle, Element e)
    {
        if ((e.StateFlags & ElementFlags.NonSolid) == 0)
        {
            e.Next.Frame = e.Current.Frame.SetAbs((e.Current.Frame.Index + 1) & 7);
            e.StateFlags |= ElementFlags.Changing;
        }
        else if (e.Target == null)
        {
            e.LifeSpan = 0;
            e.StateFlags |= ElementFlags.Disappearing;
        }
        else
        {
            var target = e.Target;
            e.Next.X = target.Next.X;
            e.Next.Y = target.Next.Y;

            if (e.TurnWait != 0)
            {
                e.Next.Frame = e.Current.Frame.Inc();
                if (e.Next.Frame.Index == 0)
                    e.Next.Frame = e.Next.Frame.SetRel(-8);

                if (target.Parent is { } confused)
                    confused.ShipInputState = (confused.ShipInputState
                                               & ~(StatusFlags.Left | StatusFlags.Right | StatusFlags.Special))
                                              | (StatusFlags)e.TurnWait;

                var effect = battle.List.Alloc();
                if (effect != null)
                {
                    effect.PlayerNr = e.PlayerNr;
                    effect.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.Changing;
                    effect.LifeSpan = 1;
                    effect.Current = effect.Next = e.Next;
                    effect.PreprocessFunc = x => ConfusePreprocess(battle, x);
                    effect.Prim = PrimType.Stamp;
                    effect.Parent = e.Parent;
                    effect.Target = e.Target;
                    battle.List.Put(effect);
                }
            }
        }
    }

    // confusion_collision: a ship hit is confused: the Melnorme's
    // confusion already on it is renewed, or a new one (not drawn, at the
    // list's head) put on it; 400 frames turning left or right, at random.
    // The shot is gone whatever it hit.
    private static void ConfusionCollision(Battle battle, Element e0, (short X, short Y) at0, Element e1,
        (short X, short Y) at1)
    {
        if ((e1.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var starShip = e0.Parent!;
            var hitShip = e1.Parent!.Ship;
            Element? confusion;
            for (confusion = battle.List.Head; confusion != null; confusion = confusion.Successor)
            {
                if (confusion.PlayerNr == e0.PlayerNr
                    && ReferenceEquals(confusion.Current.FrameArray, starShip.RaceDesc!.Special)
                    && (confusion.StateFlags & ElementFlags.NonSolid) != 0
                    // Co-op: its own confusion on the ship hit, not one on another player's.
                    && confusion.Parent == starShip && confusion.Target == hitShip)
                    break;
            }

            if (confusion != null || (confusion = battle.List.Alloc()) != null)
            {
                if (confusion.StateFlags == ElementFlags.None)
                {
                    // not allocated before
                    battle.List.Insert(confusion, battle.List.Head);
                    confusion.Current = e0.Next;
                    confusion.Current.Frame = confusion.Current.Frame.SetAbs(8);
                    confusion.Next = confusion.Current;
                    confusion.PlayerNr = e0.PlayerNr;
                    confusion.StateFlags = ElementFlags.FiniteLife | ElementFlags.NonSolid | ElementFlags.Changing;
                    confusion.PreprocessFunc = x => ConfusePreprocess(battle, x);
                    confusion.Prim = PrimType.None;
                    confusion.Parent = starShip;
                    confusion.Target = hitShip;
                }

                confusion.LifeSpan = 400;
                confusion.TurnWait = (byte)(1 << ((byte)battle.Random.Next("confusion_collision") & 1));   // LEFT or RIGHT
            }

            e0.HitPoints = 0;
            e0.LifeSpan = 0;
            e0.StateFlags |= ElementFlags.Disappearing | ElementFlags.Collision | ElementFlags.NonSolid;
        }
    }

    // initialize_confusion: a confusion shot ahead.
    private static Element? InitializeConfusion(Battle battle, Element ship, Defines f)
    {
        var starShip = ship.Parent!;
        var block = f.Confusion;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Special!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        var confusion = battle.InitializeMissile(block);
        if (confusion != null)
        {
            confusion.CollisionFunc = (e0, at0, e1, at1) => ConfusionCollision(battle, e0, at0, e1, at1);
            confusion.Parent = starShip;
        }
        return confusion;
    }

    // initialize_test_pump_up: the AI's stand-in for the blaster: a shot
    // flying at once, as it would when let go.
    private static IReadOnlyList<Element?> InitializeTestPumpUp(Battle battle, Element ship, Defines f)
    {
        var starShip = ship.Parent!;
        var block = f.TestPumpUp;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        return new[] { battle.InitializeMissile(block) };
    }

    // melnorme_intelligence: the stock AI aims a test shot (and fires
    // while the weapon waits on its hold); short of energy for both shots
    // and not firing, it entices the enemy, else pursues one without an
    // immediate weapon. The key held is let go when the shot would hit (or
    // pressed again once ready, with the energy). With the special ready
    // and paid for, the stock AI's weapon test with the confusion shot and
    // the ship held still decides the special.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter,
        Defines f)
    {
        var starShip = ship.Parent!;
        var rd = starShip.RaceDesc!;

        rd.InitWeapon = e => InitializeTestPumpUp(battle, e, f);
        byte oldCount = starShip.WeaponCounter;

        if (starShip.WeaponCounter == f.WeaponWait)
            starShip.WeaponCounter = 0;

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object != null)
        {
            if (rd.EnergyLevel < f.SpecialEnergyCost + f.WeaponEnergyCost
                && (starShip.OldStatusFlags & StatusFlags.Weapon) == 0)
                enemy.MoveState = MoveState.Entice;
            else if ((enemy.Object.Parent!.RaceDesc!.ShipFlags & ShipFlags.ImmediateWeapon) == 0)
                enemy.MoveState = MoveState.Pursue;
        }
        battle.ShipIntelligence(ship, concerns, concernCounter);

        if (starShip.WeaponCounter == 0
            && (oldCount != 0
                || ((starShip.SpecialCounter != 0 || rd.EnergyLevel >= f.SpecialEnergyCost + f.WeaponEnergyCost)
                    && (starShip.ShipInputState & StatusFlags.Weapon) == 0)))
            starShip.ShipInputState ^= StatusFlags.Weapon;

        starShip.ShipInputState &= ~StatusFlags.Special;
        if (starShip.SpecialCounter == 0 && rd.EnergyLevel >= f.SpecialEnergyCost)
        {
            var oldInputState = starShip.ShipInputState;

            rd.InitWeapon = e => new[] { InitializeConfusion(battle, e, f) };

            ++ship.TurnWait;
            ++ship.ThrustWait;
            battle.ShipIntelligence(ship, concerns, Battle.EnemyShipIndex + 1);
            --ship.ThrustWait;
            --ship.TurnWait;

            if ((starShip.ShipInputState & StatusFlags.Weapon) != 0)
            {
                starShip.ShipInputState &= ~StatusFlags.Weapon;
                starShip.ShipInputState |= StatusFlags.Special;
            }

            starShip.ShipInputState = (StatusFlags)(byte)(oldInputState | (starShip.ShipInputState & StatusFlags.Special));
        }

        starShip.WeaponCounter = oldCount;

        rd.InitWeapon = e => InitializePumpUp(battle, e, f);
    }

    // melnorme_postprocess: the confusion shot when pressed, ready and
    // paid for (its sound), the special waiting only if it was made.
    private static void Postprocess(Battle battle, Element e, Defines f)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-f.SpecialEnergyCost))
        {
            var confusion = InitializeConfusion(battle, e, f);
            if (confusion != null)
            {
                battle.List.Put(confusion);
                starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
            }
        }
    }
}
