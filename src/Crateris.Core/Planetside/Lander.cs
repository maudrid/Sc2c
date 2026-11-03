// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0, src/uqm/planets/lander.c
// (PlanetSide, InitPlanetSide, LandingTakeoffSequence, IdlePlanetSide,
// DoPlanetSide, ScrollPlanetSide, BuildObjectList, LanderFire),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Planetside;

/// <summary>A lander's keys for one surface frame (the recording's flags).</summary>
[Flags]
public enum LanderInput
{
    None = 0,
    Left = 1,
    Right = 2,
    Thrust = 4,
    Fire = 8,
    /// <summary>Escape or the special key: take off.</summary>
    TakeOff = 16,
}

/// <summary>Where a lander is in its visit to the surface.</summary>
public enum LanderPhase
{
    /// <summary>Waiting a second over the site, then coming down (LandingTakeoffSequence).</summary>
    Landing,
    /// <summary>On the ground, driven by its player (DoPlanetSide).</summary>
    Ground,
    /// <summary>Going up, then waiting half a second (LandingTakeoffSequence).</summary>
    TakingOff,
    /// <summary>Back in orbit.</summary>
    Gone,
}

/// <summary>
/// Something on the surface that the frame moves: so far the lander's
/// bolts (an ELEMENT in UQM's display queue).
/// </summary>
public sealed class SurfaceElement
{
    public int X { get; internal set; }
    public int Y { get; internal set; }

    /// <summary>Frames left (life_span); at 0 it's removed on the next frame.</summary>
    public int LifeSpan { get; internal set; }

    /// <summary>Whether its life counts down (FINITE_LIFE).</summary>
    public bool FiniteLife { get; init; }

    /// <summary>A lander's shot (PS_HUMAN_PLAYER): it isn't stopped at the poles.</summary>
    public bool LanderShot { get; init; }

    internal Velocity Velocity;
}

/// <summary>
/// A planet's surface while landers are on it: its random generator
/// (TFB_Random), its landers and what's on it, in UQM's display queue
/// order. Each frame (<see cref="Step"/>) every lander moves and fires;
/// then each draws one random number for the hazards around it
/// (BuildObjectList's roll, per lander as decision 3 has it; the hazards
/// themselves are item 8); then everything on the surface moves, once.
/// With one lander that's UQM's frame.
/// </summary>
public sealed class PlanetsideSurface
{
    private readonly List<SurfaceElement> _elements = new();
    private readonly List<Lander> _landers = new();

    public PlanetsideSurface(TfbRandom random) => Random = random;

    public TfbRandom Random { get; }

    /// <summary>What's on the surface, head of the queue first.</summary>
    public IReadOnlyList<SurfaceElement> Elements => _elements;

    /// <summary>The landers on the surface or coming down, in the order they came.</summary>
    public IReadOnlyList<Lander> Landers => _landers;

    /// <summary>
    /// A ship sends its lander down at a site (planetside map units): the
    /// crew go aboard from the ship's hold, up to 12 (AnimateLanderWarmup),
    /// and the lander comes down (<see cref="Lander"/>). The fuel is the
    /// host's to take (<see cref="GameWorld.Dispatch"/>).
    /// </summary>
    public Lander Land(ShipHold hold, int siteX, int siteY)
    {
        if (siteX is < 0 or >= PlanetsideMap.Width || siteY is < 0 or >= PlanetsideMap.Height)
            throw new ArgumentOutOfRangeException(nameof(siteX), $"the site ({siteX}, {siteY}) is off the map");
        var lander = new Lander(this, hold, siteX, siteY);
        _landers.Add(lander);
        return lander;
    }

    /// <summary>
    /// One surface frame, 35 a second, with each lander's keys. A lander
    /// whose take-off ends goes back to its ship, with its crew, and leaves
    /// the surface.
    /// </summary>
    public void Step(Func<Lander, LanderInput> keys)
    {
        foreach (var lander in _landers)
            lander.Frame(keys(lander));
        foreach (var _ in _landers)
            Random.Next();   // the hazards' roll: lava, quakes and lightning (item 8)
        MoveElements();
        for (int i = 0; i < _landers.Count;)
        {
            if (_landers[i].Phase == LanderPhase.Gone)
            {
                _landers[i].BackAboard();
                _landers.RemoveAt(i);
            }
            else
                i++;
        }
    }

    /// <summary>Puts an element at the head of the queue (InsertElement before GetHeadElement).</summary>
    internal void AddFirst(SurfaceElement element) => _elements.Insert(0, element);

    /// <summary>
    /// BuildObjectList after the roll: each element in order is removed if
    /// its life is over, or has its life counted down and moves by its
    /// velocity, wrapping east and west; all but a lander's shots stop at
    /// the poles.
    /// </summary>
    private void MoveElements()
    {
        for (int i = 0; i < _elements.Count;)
        {
            var e = _elements[i];
            if (e.LifeSpan == 0)
            {
                _elements.RemoveAt(i);
                continue;
            }
            if (e.FiniteLife)
                e.LifeSpan--;
            var (dx, dy) = e.Velocity.Next(1);
            if (dx != 0 || dy != 0)
            {
                int x = (short)(e.X + dx), y = (short)(e.Y + dy);
                if (!e.LanderShot)
                    y = Math.Clamp(y, 0, PlanetsideMap.Height - 1);
                x = x < 0 ? x + PlanetsideMap.Width : x % PlanetsideMap.Width;
                e.X = x;
                e.Y = y;
            }
            i++;
        }
    }
}

/// <summary>
/// One lander's visit to a planet's surface, as UQM's PlanetSide runs it,
/// at 35 frames a second: it comes down near the picked site facing a
/// random way, is driven while on the ground (turning one facing every 3
/// frames, moving at one speed while thrust is held, firing a bolt every
/// 16 frames, or 8 with the shot upgrade), and takes off again. The
/// surface wraps east and west; at a pole the lander stops, and keeps
/// still under thrust until it turns. Its surface steps it
/// (<see cref="PlanetsideSurface.Step"/>). The crew go aboard from the
/// ship's hold as it leaves, and back when it has taken off; UQM's
/// animations of that, and of the launch from the ship, take no frames
/// here.
/// </summary>
public sealed class Lander
{
    /// <summary>RANDOM_MISS: the lander comes down up to this far from the site, each way.</summary>
    public const int RandomMiss = 64;
    /// <summary>The frames waiting over the site before coming down: a second (IdlePlanetSide).</summary>
    public const int LandingWaitFrames = 35;
    /// <summary>The frames waiting after going up: half a second, 420 of UQM's ticks at 24 a frame.</summary>
    public const int TakeOffWaitFrames = 18;
    /// <summary>DISTANCE_COVERED: how far above its place the lander is when it's off the screen (SURFACE_HEIGHT / 2 + 10).</summary>
    public const int OffScreen = 83;
    /// <summary>SHUTTLE_TURN_WAIT: frames between turns.</summary>
    public const int TurnWait = 2;
    /// <summary>SHUTTLE_FIRE_WAIT: frames to wait after a bolt before the next (halved with the shot upgrade), so a bolt every 16 frames, or 8.</summary>
    public const int FireWait = 15;
    /// <summary>The lander's speed in world units, over <see cref="SpeedDenominator"/>: 16, or 28 upgraded.</summary>
    public const int Speed = 2 * 8, FastSpeed = 2 * 14, SpeedDenominator = 10;
    /// <summary>A bolt's speed in world units (2 * 3) and its life in frames.</summary>
    public const int BoltSpeed = 2 * 3, BoltLife = 12;

    /// <summary>
    /// How far above its place the lander is drawn at each step of coming
    /// down or going up: 1, 3, 6, ... until past <see cref="OffScreen"/>
    /// (LandingTakeoffSequence's landingOfs, negated).
    /// </summary>
    public static readonly IReadOnlyList<int> Heights = MakeHeights();

    private readonly PlanetsideSurface _surface;
    private readonly ShipHold _hold;
    private readonly bool _fast, _quickShot;
    private Velocity _velocity;
    private int _turnWait, _weaponWait, _phaseFrame;

    /// <summary>
    /// The lander leaves its ship with up to 12 crew and the ship's lander
    /// upgrades, and comes down: it faces a random way (PlanetSide), then
    /// lands up to <see cref="RandomMiss"/> from the site each way,
    /// wrapping east and west and kept off the poles (InitPlanetSide).
    /// </summary>
    internal Lander(PlanetsideSurface surface, ShipHold hold, int siteX, int siteY)
    {
        _surface = surface;
        _hold = hold;
        Crew = hold.LanderLeaves();
        _fast = (hold.Upgrades & LanderUpgrades.Speed) != 0;
        _quickShot = (hold.Upgrades & LanderUpgrades.Shot) != 0;
        Facing = UqmMath.NormalizeFacing((int)surface.Random.Next());
        int x = siteX - (RandomMiss - (int)(surface.Random.Next() % (RandomMiss << 1)));
        if (x < 0)
            x += PlanetsideMap.Width;
        else if (x >= PlanetsideMap.Width)
            x -= PlanetsideMap.Width;
        int y = siteY - (RandomMiss - (int)(surface.Random.Next() % (RandomMiss << 1)));
        X = x;
        Y = Math.Clamp(y, 0, PlanetsideMap.Height - 1);
    }

    /// <summary>The lander's place (curLanderLoc), in planetside map units.</summary>
    public int X { get; private set; }

    /// <inheritdoc cref="X"/>
    public int Y { get; private set; }

    /// <summary>The way it faces, 0 (north) to 15 clockwise.</summary>
    public int Facing { get; private set; }

    /// <summary>The crew aboard.</summary>
    public int Crew { get; private set; }

    public LanderPhase Phase { get; private set; } = LanderPhase.Landing;

    /// <summary>The phase the last frame was drawn in: a frame that lands is the landing's last, the one that ends the take-off its last.</summary>
    public LanderPhase FramePhase { get; private set; } = LanderPhase.Landing;

    /// <summary>
    /// How far above its place the lander was drawn in the last frame
    /// (landingOffset, as a height): 0 on the ground, <see cref="OffScreen"/>
    /// while it waits above the screen.
    /// </summary>
    public int Height { get; private set; } = OffScreen;

    /// <summary>
    /// The lander's part of a surface frame: it moves and fires. Keys count
    /// only on the ground; take-off there starts going up in the same frame
    /// (DoPlanetSide stops, and the take-off's first frame comes in its place).
    /// </summary>
    internal void Frame(LanderInput input)
    {
        if (Phase == LanderPhase.Ground && (input & LanderInput.TakeOff) != 0)
        {
            Phase = LanderPhase.TakingOff;
            _phaseFrame = 0;
        }
        FramePhase = Phase;
        switch (Phase)
        {
            case LanderPhase.Landing:
                Height = _phaseFrame < LandingWaitFrames
                    ? OffScreen
                    : Heights[Heights.Count - 1 - (_phaseFrame - LandingWaitFrames)];
                Scroll(0, 0);
                if (++_phaseFrame == LandingWaitFrames + Heights.Count)
                    Land();
                break;
            case LanderPhase.Ground:
                Drive(input);
                break;
            case LanderPhase.TakingOff:
                Height = _phaseFrame < Heights.Count ? Heights[_phaseFrame] : OffScreen;
                Scroll(0, 0);
                if (++_phaseFrame == Heights.Count + TakeOffWaitFrames)
                    Phase = LanderPhase.Gone;
                break;
            case LanderPhase.Gone:
                throw new InvalidOperationException("the lander has gone back to orbit");
        }
    }

    // PlanetSide after the take-off: the crew go back to the ship (cargo is item 6).
    internal void BackAboard() => _hold.LanderReturns(Crew, new int[ShipHold.Categories], 0);

    // DoPlanetSide's first call: the lander sets off the way it faces.
    private void Land()
    {
        Phase = LanderPhase.Ground;
        _turnWait = 0;
        _weaponWait = 0;
        SetHeading();
    }

    private void Drive(LanderInput input)
    {
        Height = 0;
        if (_turnWait > 0)
            _turnWait--;
        else if ((input & (LanderInput.Left | LanderInput.Right)) != 0)
        {
            Facing = UqmMath.NormalizeFacing(Facing + ((input & LanderInput.Left) != 0 ? -1 : 1));
            SetHeading();
            _turnWait = TurnWait;
        }
        int dx = 0, dy = 0;
        bool thrust = (input & LanderInput.Thrust) != 0;
        if (thrust)
            (dx, dy) = _velocity.Next(1);
        if (_weaponWait > 0)
            _weaponWait--;
        else if ((input & LanderInput.Fire) != 0)
        {
            Fire(thrust);
            _weaponWait = _quickShot ? FireWait >> 1 : FireWait;
        }
        Scroll(dx, dy);
    }

    private void SetHeading()
    {
        int angle = UqmMath.FacingToAngle(Facing);
        int speed = Velocity.WorldToVelocity(_fast ? FastSpeed : Speed);
        _velocity.SetComponents((short)(UqmMath.Cosine(angle, speed) / SpeedDenominator),
            (short)(UqmMath.Sine(angle, speed) / SpeedDenominator));
    }

    // LanderFire: a bolt from where the lander is, at its own speed plus
    // the lander's while thrust is held.
    private void Fire(bool thrust)
    {
        var (wdx, wdy) = thrust ? _velocity.Current() : ((short)0, (short)0);
        int angle = UqmMath.FacingToAngle(Facing);
        var bolt = new SurfaceElement { X = X, Y = Y, LifeSpan = BoltLife, FiniteLife = true, LanderShot = true };
        bolt.Velocity.SetComponents((short)(UqmMath.Cosine(angle, Velocity.WorldToVelocity(BoltSpeed)) + wdx),
            (short)(UqmMath.Sine(angle, Velocity.WorldToVelocity(BoltSpeed)) + wdy));
        _surface.AddFirst(bolt);
    }

    // ScrollPlanetSide: the lander moves, stopping at a pole (where its
    // velocity is lost, and with it any move east or west), wrapping east
    // and west. The rest of the frame is the surface's (PlanetsideSurface.Step).
    private void Scroll(int dx, int dy)
    {
        int y = Y + dy;
        if (y < 0 || y > PlanetsideMap.Height - 1)
        {
            y = Math.Clamp(y, 0, PlanetsideMap.Height - 1);
            dx = 0;
            _velocity.Zero();
        }
        int x = X + dx;
        if (x < 0)
            x += PlanetsideMap.Width;
        else if (x >= PlanetsideMap.Width)
            x -= PlanetsideMap.Width;
        X = x;
        Y = y;
    }

    private static int[] MakeHeights()
    {
        var heights = new List<int>();
        for (int sum = 0, i = 1; sum < OffScreen; ++i)
            heights.Add(sum += i);
        return heights.ToArray();
    }
}
