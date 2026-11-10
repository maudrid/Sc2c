// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/battle.c: Battle,
// DoBattle, ProcessInput, DoRunAway; src/uqm/init.c: InitShips;
// src/uqm/galaxy.c: InitGalaxy's stars; src/uqm/units.h), Copyright Paul
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
/// The sheets a battle's arena uses (InitSpace, load_gravity_well): the
/// stars (whose last frame is floating crew, lasers and ion trails),
/// asteroids, blasts, explosions, and the planet.
/// </summary>
public sealed record BattleArena(Views Stars, Views Asteroid, Views Blast, Views Explosion, Views Planet)
{
    /// <summary>stars_in_space: the stars sheet's first frame.</summary>
    public Frame StarsInSpace => Stars[0];
}

/// <summary>How the battle's view zooms (optMeleeScale): UQM's steps of 1x, 2x and 4x, or its smooth zoom (the default).</summary>
public enum BattleZoom
{
    /// <summary>TFB_SCALE_STEP.</summary>
    Step,
    /// <summary>The smooth zoom (TFB_SCALE_TRILINEAR, UQM's default).</summary>
    Smooth,
}

/// <summary>
/// UQM's random generator for a battle (TFB_Random, seeded as Battle seeds
/// it), noting where each number is drawn when asked to (the traces
/// compare the places, by the name of UQM's function that draws).
/// </summary>
public sealed class BattleRandom
{
    private readonly TfbRandom _random;

    public BattleRandom(uint seed) => _random = new TfbRandom(seed);

    /// <summary>The generator's seed now: the last number drawn.</summary>
    public uint Seed => _random.Seed;

    /// <summary>When set, each draw adds its place here.</summary>
    public List<string>? Places { get; set; }

    /// <summary>TFB_Random, drawn by UQM's function <paramref name="place"/>.</summary>
    public uint Next(string place)
    {
        Places?.Add(place);
        return _random.Next();
    }
}

/// <summary>
/// A battle as UQM runs one in an encounter: the arena (stars, five
/// asteroids, a planet), each side's ships, and DoBattle's frames
/// (<see cref="Step"/>). Its coordinates move with the camera: each frame
/// the scroll is added to every element, so the view's centre stays the
/// arena's (<see cref="ScrollX"/>, <see cref="ScrollY"/>). Nothing here
/// draws: what UQM draws from inside the simulation (the status panel's
/// crew and energy) is kept as state on the ships.
/// </summary>
public sealed partial class Battle
{
    public const int LogSpaceWidth = Units.LogSpaceWidth;
    public const int LogSpaceHeight = Units.LogSpaceHeight;
    public const int NormalLife = 1;
    public const int HyperjumpLife = 15;
    public const int NumExplosionFrames = 12;
    public const short NeutralPlayerNum = -1;
    public const int NumSides = 2;
    public const int NumViews = ShipGraphics.Views;

    /// <summary>The planet's mass and the threshold above which a mass pulls (GRAVITY_MASS: more than MAX_SHIP_MASS * 10).</summary>
    public const int MaxShipMass = 10;

    private const int OneShift = Units.OneShift;
    private const int ScaledOne = 1 << OneShift;

    public static bool GravityMass(int m) => m > MaxShipMass * 10;

    public static short DisplayToWorld(int x) => (short)(x << OneShift);

    public static short WorldToDisplay(short x) => (short)(x >> OneShift);

    public static short DisplayAlign(int x) => (short)((short)x & ~(ScaledOne - 1));

    private static short DisplayAlignX(uint x) => (short)((short)((ushort)x % LogSpaceWidth) & ~(ScaledOne - 1));

    private static short DisplayAlignY(uint y) => (short)((short)((ushort)y % LogSpaceHeight) & ~(ScaledOne - 1));

    /// <summary>
    /// WRAP_X (DISPLAY_ALIGN_X (TFB_Random ())): a random place across the
    /// arena. WRAP_X is a macro that reads its argument up to three times,
    /// and each read draws a number: UQM draws three and keeps the last.
    /// </summary>
    private short RandomX(string place) => WrapRandom(() => DisplayAlignX(Random.Next(place)), LogSpaceWidth);

    /// <summary>WRAP_Y (DISPLAY_ALIGN_Y (TFB_Random ())), drawing as <see cref="RandomX"/> does.</summary>
    private short RandomY(string place) => WrapRandom(() => DisplayAlignY(Random.Next(place)), LogSpaceHeight);

    // WRAP_VAL (v, w) with v read afresh each time.
    private static short WrapRandom(Func<short> v, int w)
    {
        if (v() < 0)
            return (short)(ushort)(v() + w);
        if (v() >= w)
            return (short)(ushort)(v() - w);
        return (short)(ushort)v();
    }

    public static ushort WrapX(int x) => (ushort)(x < 0 ? x + LogSpaceWidth : x >= LogSpaceWidth ? x - LogSpaceWidth : x);

    public static ushort WrapY(int y) => (ushort)(y < 0 ? y + LogSpaceHeight : y >= LogSpaceHeight ? y - LogSpaceHeight : y);

    public static short WrapDeltaX(int dx) => (short)Units.WrapDeltaX(dx);

    public static short WrapDeltaY(int dy) => (short)Units.WrapDeltaY(dy);

    private static short Sine(int angle, int magnitude) => UqmMath.Sine(angle, magnitude);

    private static short Cosine(int angle, int magnitude) => UqmMath.Cosine(angle, magnitude);

    private readonly List<Starship>[] _fleets;
    private readonly bool _runAwayAllowed;

    /// <summary>
    /// Battle: the generator seeded (UQM seeds it from the clock), the arena
    /// set up (InitShips: the stars, five asteroids, the planet), and each
    /// side's first ship warping in, the enemy's first (GetInitialStarShips).
    /// </summary>
    /// <param name="arena">The arena's sheets.</param>
    /// <param name="fleets">Each side's ships, side 0 the player's (RPG_PLAYER_NUM), 1 the enemy's.</param>
    /// <param name="seed">TFB_SeedRandom's seed.</param>
    /// <param name="zoom">How the view zooms.</param>
    /// <param name="runAwayAllowed">Whether side 0 can retreat (RunAwayAllowed: an encounter after the starbase is available, without the bomb).</param>
    /// <param name="randomPlaces">Where to note each random number's place, or null.</param>
    public Battle(BattleArena arena, IReadOnlyList<IReadOnlyList<Starship>> fleets, uint seed,
        BattleZoom zoom = BattleZoom.Smooth, bool runAwayAllowed = false, List<string>? randomPlaces = null)
    {
        if (fleets.Count != NumSides)
            throw new ArgumentException("a battle has two sides");
        Arena = arena;
        Zoom = zoom;
        _runAwayAllowed = runAwayAllowed;
        _fleets = fleets.Select(f => f.ToList()).ToArray();
        for (int side = 0; side < NumSides; ++side)
            if (_fleets[side].Any(s => s.PlayerNr != side))
                throw new ArgumentException($"a ship of side {side} isn't that side's");
        Random = new BattleRandom(seed) { Places = randomPlaces };

        InitShips();
        InBattle = true;
        for (int side = 0; side < NumSides; ++side)
            BattleCounter[side] = (byte)_fleets[side].Count;
        // GetInitialStarShips outside Super Melee: the last side first.
        for (int side = NumSides; side > 0; --side)
            if (!GetNextStarShip(null, side - 1))
                throw new InvalidOperationException($"side {side - 1} has no ship to fight with");
    }

    public BattleArena Arena { get; }
    public BattleZoom Zoom { get; }
    public DisplayList List { get; } = new();
    public BattleRandom Random { get; }

    /// <summary>Each side's ships (race_q), in the order they're picked.</summary>
    public IReadOnlyList<Starship> Fleet(int side) => _fleets[side];

    /// <summary>battle_counter: each side's ships still able to fight.</summary>
    public byte[] BattleCounter { get; } = new byte[NumSides];

    /// <summary>Whether the battle goes on (IN_BATTLE): cleared when a side has no ship left.</summary>
    public bool InBattle { get; private set; }

    /// <summary>The last frame's camera scroll, added to every element (CalcView's dx and dy).</summary>
    public short ScrollX { get; private set; }

    /// <inheritdoc cref="ScrollX"/>
    public short ScrollY { get; private set; }

    /// <summary>The last frame's view: 0 stable, 1 scrolled, 2 zoomed (VIEW_STATE).</summary>
    public int ViewState { get; private set; } = -1;

    /// <summary>zoom_out: the step zoom's reduction (0 to 2), or the smooth zoom's (256 is 1x, 1024 4x).</summary>
    public int ZoomOut { get; private set; }

    /// <summary>SpaceOrg: the view's top left corner, in world units.</summary>
    public short SpaceOrgX { get; private set; }

    /// <inheritdoc cref="SpaceOrgX"/>
    public short SpaceOrgY { get; private set; }

    /// <summary>InitGalaxy's stars, big, medium and small, each block sorted by y (log_star_array).</summary>
    public IReadOnlyList<(short X, short Y)> Stars => _stars;

    private readonly (short X, short Y)[] _stars = new (short, short)[BigStarCount + MedStarCount + SmlStarCount];
    private const int BigStarCount = 30, MedStarCount = 60, SmlStarCount = 90;

    /// <summary>
    /// DoBattle with one key state a side, every ship of a side taking its
    /// side's keys: <see cref="Step(Func{Starship, BattleInput})"/>.
    /// </summary>
    public bool Step(IReadOnlyList<BattleInput> keys)
    {
        if (keys.Count != NumSides)
            throw new ArgumentException("a key state for each side");
        return Step(ship => keys[ship.PlayerNr]);
    }

    /// <summary>
    /// DoBattle: each ship's input (ProcessInput), then the list stepped
    /// (RedrawQueue). Returns whether the battle goes on.
    /// <paramref name="input"/> gives a ship's input for the frame (its
    /// player's keys, or <see cref="ComputerIntelligence"/>'s): it is
    /// asked in ProcessInput's order, as UQM asks each side's frameInput,
    /// side 0's ships first, each side's in its fleet's order, for every
    /// ship with an element in the battle, its crew or not; each ship's
    /// input takes effect before the next is asked (a retreat, DoRunAway,
    /// changes the ship at once), so the AI flying side 1 sees that frame's
    /// retreats.
    /// </summary>
    public bool Step(Func<Starship, BattleInput> input)
    {
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        ProcessInput(input);
        RedrawQueue();
        return InBattle;
    }

    // battle.c's ProcessInput.
    private void ProcessInput(Func<Starship, BattleInput> keys)
    {
        for (int side = 0; side < NumSides; ++side)
        {
            foreach (var ship in _fleets[side])
            {
                if (ship.Ship == null)
                    continue;
                var input = keys(ship);
                ship.ShipInputState = StatusFlags.None;
                if (ship.RaceDesc!.CrewLevel == 0)
                    continue;
                if ((input & BattleInput.Left) != 0)
                    ship.ShipInputState |= StatusFlags.Left;
                else if ((input & BattleInput.Right) != 0)
                    ship.ShipInputState |= StatusFlags.Right;
                if ((input & BattleInput.Thrust) != 0)
                    ship.ShipInputState |= StatusFlags.Thrust;
                if ((input & BattleInput.Weapon) != 0)
                    ship.ShipInputState |= StatusFlags.Weapon;
                if ((input & BattleInput.Special) != 0)
                    ship.ShipInputState |= StatusFlags.Special;
                if (_runAwayAllowed && side == 0 && (input & BattleInput.Escape) != 0)
                    DoRunAway(ship);
            }
        }
    }

    // battle.c's DoRunAway: the ship flashes red, then warps out (flee_preprocess).
    private void DoRunAway(Starship starShip)
    {
        var e = starShip.Ship!;
        if (e.Prim == PrimType.Stamp && e.LifeSpan == NormalLife && (e.StateFlags & ElementFlags.FiniteLife) == 0
            && e.MassPoints != MaxShipMass * 10 && (e.StateFlags & ElementFlags.Appearing) == 0)
        {
            BattleCounter[0]--;
            e.TurnWait = 3;
            e.ThrustWait = 4;
            e.ColorCycleIndex = 0;
            e.PreprocessFunc = FleePreprocess;
            e.MassPoints = MaxShipMass * 10;
            e.Velocity.Zero();
            starShip.CurStatusFlags &= ~(StatusFlags.ShipAtMaxSpeed | StatusFlags.ShipBeyondMaxSpeed);
            e.PrimColor = Rgb15(0x0B, 0x00, 0x00);
            e.Prim = PrimType.StampFill;
            starShip.ShipInputState = StatusFlags.None;
        }
    }

    /// <summary>MAKE_RGB15: a colour as <see cref="Element.PrimColor"/> keeps it.</summary>
    public static int Rgb15(int r, int g, int b) => (r << 10) | (g << 5) | b;

    // init.c's InitShips, for a battle outside hyperspace and the last battle.
    private void InitShips()
    {
        InitDisplayList();
        InitGalaxy();
        const int numAsteroids = 5;
        for (int i = 0; i < numAsteroids; ++i)
            SpawnAsteroid(null);
        SpawnPlanet();
    }

    // galaxy.c's InitGalaxy: where the stars are (their drawing is the client's).
    private void InitGalaxy()
    {
        int factor = OneShift + Units.MaxReduction + (3 - 3);   // BACKGROUND_SHIFT - 3
        for (int i = 0; i < _stars.Length; ++i)
        {
            if (i == BigStarCount || i == BigStarCount + MedStarCount)
                ++factor;
            short x = (short)((ushort)Random.Next("InitGalaxy") % Units.SpaceWidth << factor);
            short y = (short)((ushort)Random.Next("InitGalaxy") % Units.SpaceHeight << factor);
            _stars[i] = (x, y);
        }
        SortStarBlock(0, BigStarCount);
        SortStarBlock(BigStarCount, MedStarCount);
        SortStarBlock(BigStarCount + MedStarCount, SmlStarCount);
    }

    private void SortStarBlock(int start, int count)
    {
        for (int i = 0; i < count; ++i)
            for (int j = count - 1; j > i; --j)
                if (_stars[start + i].Y > _stars[start + j].Y)
                    (_stars[start + i], _stars[start + j]) = (_stars[start + j], _stars[start + i]);
    }

    // ship.c's GetNextStarShip: the side's next ship warps in, taking the
    // last one's element. pickship.c's GetEncounterStarShip is a menu for
    // the player; here each side takes its first ship still able to fight.
    private bool GetNextStarShip(Starship? last, int side)
    {
        var next = _fleets[side].FirstOrDefault(s => s.Available);
        if (next != null)
        {
            if (last != null)
            {
                if (next == last)
                    last = null;   // recycled (infinite fleets)
                else
                    next.Ship = last.Ship;
            }
            if (!SpawnShip(next))
                return false;
        }
        if (last != null)
            last.Ship = null;
        return next != null;
    }
}
