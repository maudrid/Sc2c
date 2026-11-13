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

/// <summary>
/// The arena's size in world units (decision 4): UQM's, LOG_SPACE_WIDTH by
/// LOG_SPACE_HEIGHT (8,192 by 7,680), or bigger, UQM's times a scale
/// (<see cref="Scaled"/>; the game's is <c>GameSettings.BattleArenaScale</c>).
/// Only the arena grows: the camera's zoom stays UQM's, out to its 4x,
/// which shows half UQM's arena each way. Places are 16-bit, as UQM's
/// COORDs: an arena of at most <see cref="MaxScale"/> times UQM's keeps
/// every place and every difference of two places in range, with room for
/// the elements past an edge before they're wrapped. A place moved by the
/// arena's size to test contact across the seam can pass a COORD's 32,767
/// in an arena over about 2.67 times UQM's: what does so works in ints
/// (<see cref="PlotIntercept"/>'s places) or display units (the contact
/// tests' controls), never in a COORD. Each side is a
/// multiple of 8, so the arena's centre is on a whole pixel, as UQM's is.
/// </summary>
public sealed record ArenaSize
{
    /// <summary>The most the arena may be scaled each way.</summary>
    public const double MaxScale = 3;

    /// <summary>UQM's arena: LOG_SPACE_WIDTH by LOG_SPACE_HEIGHT.</summary>
    public static readonly ArenaSize Uqm = new(Units.LogSpaceWidth, Units.LogSpaceHeight);

    public ArenaSize(int width, int height)
    {
        if (width < Units.LogSpaceWidth || height < Units.LogSpaceHeight)
            throw new ArgumentOutOfRangeException(nameof(width), $"the arena {width} by {height} is smaller than UQM's");
        if (width > Units.LogSpaceWidth * MaxScale || height > Units.LogSpaceHeight * MaxScale)
            throw new ArgumentOutOfRangeException(nameof(width), $"the arena {width} by {height} is more than {MaxScale} times UQM's");
        if (width % 8 != 0 || height % 8 != 0)
            throw new ArgumentException($"the arena {width} by {height} isn't a multiple of 8 each way");
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>UQM's arena times <paramref name="scale"/> each way (1 to <see cref="MaxScale"/>), each side rounded to a multiple of 8.</summary>
    public static ArenaSize Scaled(double scale)
    {
        if (!(scale >= 1 && scale <= MaxScale))
            throw new ArgumentOutOfRangeException(nameof(scale), $"the arena's scale {scale} isn't from 1 to {MaxScale}");
        return new ArenaSize(Round(Units.LogSpaceWidth * scale), Round(Units.LogSpaceHeight * scale));
    }

    private static int Round(double v) => (int)Math.Round(v / 8) * 8;
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
/// <para>
/// Co-op (stage 4a item 5): side 0 is the players', any number of ships
/// fighting at once, each joining when it will (<see cref="Join"/>); side 1
/// the enemy's. Teammates don't hurt or bump each other
/// (<see cref="Teammates"/>, decision 2). The arena may be bigger than UQM's
/// (<see cref="ArenaSize"/>, decision 4), and then contact is tested across
/// the seam (<see cref="AcrossSeam"/>, decision 5). The camera, UQM's moving
/// frame, follows one ship a side (<see cref="FollowedElement"/>). With one ship a
/// side and UQM's arena, the battle is UQM's, frame for frame.
/// </para>
/// </summary>
public sealed partial class Battle
{
    /// <summary>LOG_SPACE_WIDTH: UQM's arena, which the camera's zoom is scaled to (the arena's own size is <see cref="Size"/>).</summary>
    public const int LogSpaceWidth = Units.LogSpaceWidth;

    /// <inheritdoc cref="LogSpaceWidth"/>
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

    private short DisplayAlignX(uint x) => (short)((short)((ushort)x % Size.Width) & ~(ScaledOne - 1));

    private short DisplayAlignY(uint y) => (short)((short)((ushort)y % Size.Height) & ~(ScaledOne - 1));

    /// <summary>
    /// WRAP_X (DISPLAY_ALIGN_X (TFB_Random ())): a random place across the
    /// arena. WRAP_X is a macro that reads its argument up to three times,
    /// and each read draws a number: UQM draws three and keeps the last.
    /// </summary>
    internal short RandomX(string place) => WrapRandom(() => DisplayAlignX(Random.Next(place)), Size.Width);

    /// <summary>WRAP_Y (DISPLAY_ALIGN_Y (TFB_Random ())), drawing as <see cref="RandomX"/> does.</summary>
    internal short RandomY(string place) => WrapRandom(() => DisplayAlignY(Random.Next(place)), Size.Height);

    // WRAP_VAL (v, w) with v read afresh each time.
    private static short WrapRandom(Func<short> v, int w)
    {
        if (v() < 0)
            return (short)(ushort)(v() + w);
        if (v() >= w)
            return (short)(ushort)(v() - w);
        return (short)(ushort)v();
    }

    /// <summary>WRAP_X: a place taken round the arena (once).</summary>
    public ushort WrapX(int x) => (ushort)(x < 0 ? x + Size.Width : x >= Size.Width ? x - Size.Width : x);

    /// <summary>WRAP_Y.</summary>
    public ushort WrapY(int y) => (ushort)(y < 0 ? y + Size.Height : y >= Size.Height ? y - Size.Height : y);

    /// <summary>WRAP_DELTA_X: the shorter way between two places, across the seam or not.</summary>
    public short WrapDeltaX(int dx) => (short)WrapDelta(dx, Size.Width);

    /// <summary>WRAP_DELTA_Y.</summary>
    public short WrapDeltaY(int dy) => (short)WrapDelta(dy, Size.Height);

    /// <summary>WRAP_DELTA across an arena <paramref name="w"/> wide: the shorter way round.</summary>
    public static int WrapDelta(int d, int w) => d < 0
        ? (-d <= w >> 1 ? d : w + d)
        : (d <= w >> 1 ? d : d - w);

    private static short Sine(int angle, int magnitude) => UqmMath.Sine(angle, magnitude);

    private static short Cosine(int angle, int magnitude) => UqmMath.Cosine(angle, magnitude);

    private readonly List<Starship>[] _fleets;
    private readonly bool _runAwayAllowed;

    // Whether a frame is being stepped: ships join between frames.
    private bool _stepping;

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
    /// <param name="size">The arena's size: UQM's (the default), or bigger (decision 4).</param>
    /// <param name="maxElements">The most elements the list holds: UQM's 150 (MAX_DISPLAY_ELEMENTS) by default, more for more players.</param>
    public Battle(BattleArena arena, IReadOnlyList<IReadOnlyList<Starship>> fleets, uint seed,
        BattleZoom zoom = BattleZoom.Smooth, bool runAwayAllowed = false, List<string>? randomPlaces = null,
        ArenaSize? size = null, int maxElements = DisplayList.MaxDisplayElements)
    {
        if (fleets.Count != NumSides)
            throw new ArgumentException("a battle has two sides");
        Arena = arena;
        Zoom = zoom;
        Size = size ?? ArenaSize.Uqm;
        AcrossSeam = Size != ArenaSize.Uqm;
        List = new DisplayList(maxElements);
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

    /// <summary>The arena's size: where places wrap, the random places, the asteroids' seam.</summary>
    public ArenaSize Size { get; }

    public DisplayList List { get; }
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
        if (_stepping)
            throw new InvalidOperationException("a frame is being stepped");
        _stepping = true;
        try
        {
            ProcessInput(input);
            RedrawQueue();
        }
        finally
        {
            _stepping = false;
        }
        return InBattle;
    }

    /// <summary>
    /// A ship joining the battle (decision 6), between frames: it's added to
    /// its side's fleet, counted among the side's ships able to fight
    /// (battle_counter), and warps in as UQM's spawn_ship brings in a ship,
    /// at a random place clear of the planet and of everything solid and
    /// every ship, facing a random way, its element at the list's head,
    /// drawing from the battle's random numbers. Returns false, changing
    /// nothing, when the list is full (no element for it). A ship already in
    /// a battle, or spent by one (no longer available), is refused, as is
    /// joining a battle that's over, or decided (the other side has no ship
    /// left able to fight, its last one exploding: the battle ends once
    /// it's gone), or during a frame (from a ship's input). A ship its own
    /// code refuses (<see cref="IShipCode.Load"/> throwing) changes nothing.
    /// </summary>
    public bool Join(Starship ship)
    {
        if (_stepping)
            throw new InvalidOperationException("a ship joins between frames");
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        if (ship.PlayerNr is < 0 or >= NumSides)
            throw new ArgumentException($"a ship of side {ship.PlayerNr} can't join");
        if (_fleets.Any(f => f.Contains(ship)))
            throw new ArgumentException("the ship is already in the battle");
        if (!ship.Available || ship.Ship != null || ship.RaceDesc != null)
            throw new ArgumentException("the ship isn't free to fight: in a battle or spent by one");
        if (BattleCounter[ship.PlayerNr] == byte.MaxValue)
            throw new InvalidOperationException("the side has as many ships as it can count");
        if (BattleCounter[1 - ship.PlayerNr] == 0)
            throw new InvalidOperationException("the battle is decided: the other side has no ship left to fight");
        if (List.Full)
            return false;
        // The ship first (its code may refuse it, changing nothing), then its side's.
        SpawnShip(ship);   // with a slot free, it can't fail
        _fleets[ship.PlayerNr].Add(ship);
        BattleCounter[ship.PlayerNr]++;
        return true;
    }

    /// <summary>
    /// Whether a ship is running away (DoRunAway): flashing red, warping
    /// out, or gone and waiting for new_ship, with its crew. A ship running
    /// away has been counted out of its side's ships able to fight; it can
    /// still be hit until it's gone, and dies if its crew runs out.
    /// </summary>
    public bool RunningAway(Starship ship) =>
        ship.RaceDesc is { CrewLevel: > 0 } && ship.Ship is { } e && e.MassPoints == MaxShipMass * 10;

    /// <summary>
    /// Whether a ship is fighting: its element in the battle, with crew,
    /// not running away.
    /// </summary>
    public bool Fighting(Starship ship) =>
        ship.RaceDesc is { CrewLevel: > 0 } && ship.Ship is { } e && e.MassPoints != MaxShipMass * 10;

    /// <summary>
    /// A ship leaving the battle at once, between frames, with the crew it
    /// has (a disconnect, stage 4 decision 10: no red flashes, no warp-out,
    /// so it can't be hit on the way out): what cleanup_dead_ship and
    /// new_ship do for a ship that has warped out, done now. Every element
    /// it owns goes but its floating crew (which stay, as cleanup_dead_ship
    /// leaves them), nothing homes in on it any more, its description is
    /// freed (<see cref="RaceDesc.Uninit"/>: the flagship's crew left), and
    /// it can't fight again in this battle. Its side's count goes down unless
    /// it was running away (already counted out), and its side's next ship
    /// comes in, if it has one; with none, a side with no ship left able to
    /// fight ends the battle, as new_ship ends it. A ship dying (no crew) or
    /// gone already, or not in the battle, is refused: its death stands.
    /// </summary>
    public void Leave(Starship ship)
    {
        if (_stepping)
            throw new InvalidOperationException("a ship leaves between frames");
        if (!InBattle)
            throw new InvalidOperationException("the battle is over");
        if (ship.PlayerNr is < 0 or >= NumSides || !_fleets[ship.PlayerNr].Contains(ship))
            throw new ArgumentException("the ship isn't in the battle");
        if (ship.Ship is not { } element || ship.RaceDesc is not { CrewLevel: > 0 } rd)
            throw new InvalidOperationException("the ship isn't in the battle with crew: dying, or gone");

        if (element.MassPoints != MaxShipMass * 10)
            BattleCounter[ship.PlayerNr]--;   // a ship running away was counted when it ran
        ship.CrewLevel = rd.CrewLevel;   // cleanup_dead_ship: the crew left after the battle
        Untarget(element);
        for (var e = List.Head; e != null; e = e.Succ)
        {
            if (e.Parent != ship)
                continue;
            e.Parent = null;
            if ((e.StateFlags & ElementFlags.CrewObject) == 0 || e.PreprocessFunc != (ElementProcess)CrewPreprocess)
            {
                e.Prim = PrimType.None;
                e.LifeSpan = 0;
                e.StateFlags = ElementFlags.NonSolid | ElementFlags.Disappearing | ElementFlags.FiniteLife;
                e.PreprocessFunc = e.PostprocessFunc = e.DeathFunc = null;
                e.CollisionFunc = null;
            }
        }
        ship.CurStatusFlags &= ~StatusFlags.PlayVictoryDitty;
        if (_winner == ship)
            _winner = null;

        // new_ship, without the element: it goes with the rest.
        rd.Uninit?.Invoke(rd);
        ship.RaceDesc = null;
        ship.Available = false;
        ship.Ship = null;
        if (!GetNextStarShip(null, ship.PlayerNr) && (BattleCounter[0] == 0 || BattleCounter[1] == 0))
            InBattle = false;
    }

    /// <summary>
    /// init.c's UninitShips, once the battle is over (or given up): the crew
    /// still floating in space goes to the ships left with crew, each ship's
    /// crew is recorded to its <see cref="Starship.CrewLevel"/>, and each
    /// ship still in the battle (fighting, running away, or dead and waiting
    /// for new_ship) is freed (<see cref="RaceDesc.Uninit"/>, as new_ship
    /// frees a dead one: the flagship's crew left). UQM gives all the
    /// floating crew to every ship with crew, up to its most, assuming one
    /// is left ("There should only be one ship left in battle"), which with
    /// one ship a side gives each side all of it. Here each side is given
    /// all of it, as UQM's: one ship of a side left with crew takes it as
    /// UQM's does; several (stage 4a's players) share it, each crew going to
    /// one ship only, dealt one at a time, in turn, in the fleet's order (the
    /// order the players joined), to those with room, so the first to join
    /// takes what doesn't share out. With one ship a side that's UQM's.
    /// UpdateShipFragCrew (the enemy's crew left, for its encounter) is the
    /// world's, from each Starship's crew. The battle is over after it.
    /// </summary>
    public void UninitShips()
    {
        if (_stepping)
            throw new InvalidOperationException("the ships are freed between frames");
        int crewRetrieved = List.Elements.Count(e => (e.StateFlags & ElementFlags.CrewObject) != 0);

        // Every ship still in the battle, in the list's order, as UQM goes through them.
        var left = new List<Starship>();
        foreach (var e in List.Elements)
        {
            if (((e.StateFlags & ElementFlags.PlayerShip) != 0 || e.DeathFunc == (ElementProcess)NewShip)
                && e.Parent is { RaceDesc: not null } s && !left.Contains(s))
                left.Add(s);
        }

        // The floating crew to those with crew: all of it to each side, as
        // UQM gives it to each ship left (one a side in UQM), shared between
        // a side's ships in turn, each up to its most.
        foreach (var fleet in _fleets)
        {
            var takers = fleet.Where(s => left.Contains(s) && s.RaceDesc!.CrewLevel != 0).ToList();
            if (takers.Count == 1)
            {
                var rd = takers[0].RaceDesc!;
                rd.CrewLevel = crewRetrieved >= rd.MaxCrew - rd.CrewLevel ? rd.MaxCrew : (ushort)(rd.CrewLevel + crewRetrieved);
                continue;
            }
            int crew = crewRetrieved;
            while (crew > 0 && takers.Any(s => s.RaceDesc!.CrewLevel < s.RaceDesc.MaxCrew))
            {
                foreach (var s in takers)
                {
                    if (crew == 0)
                        break;
                    if (s.RaceDesc!.CrewLevel >= s.RaceDesc.MaxCrew)
                        continue;
                    s.RaceDesc.CrewLevel++;
                    crew--;
                }
            }
        }

        foreach (var s in left)
        {
            var rd = s.RaceDesc!;
            s.CrewLevel = rd.CrewLevel;   // the crew left after the battle
            rd.Uninit?.Invoke(rd);   // free_ship
            s.RaceDesc = null;
        }
        InBattle = false;
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
        StarSeed = Random.Seed;
        Galaxy(() => Random.Next("InitGalaxy")).CopyTo(_stars, 0);
    }

    /// <summary>The generator's seed as InitGalaxy started: the stars come from it alone (<see cref="Galaxy(uint)"/>), so a client makes them from it.</summary>
    public uint StarSeed { get; private set; }

    /// <summary>InitGalaxy's stars drawn from a generator seeded with <paramref name="seed"/> (<see cref="StarSeed"/>).</summary>
    public static (short X, short Y)[] Galaxy(uint seed)
    {
        var random = new TfbRandom(seed);
        return Galaxy(random.Next);
    }

    private static (short X, short Y)[] Galaxy(Func<uint> next)
    {
        var stars = new (short X, short Y)[BigStarCount + MedStarCount + SmlStarCount];
        int factor = OneShift + Units.MaxReduction + (3 - 3);   // BACKGROUND_SHIFT - 3
        for (int i = 0; i < stars.Length; ++i)
        {
            if (i == BigStarCount || i == BigStarCount + MedStarCount)
                ++factor;
            short x = (short)((ushort)next() % Units.SpaceWidth << factor);
            short y = (short)((ushort)next() % Units.SpaceHeight << factor);
            stars[i] = (x, y);
        }
        SortStarBlock(stars, 0, BigStarCount);
        SortStarBlock(stars, BigStarCount, MedStarCount);
        SortStarBlock(stars, BigStarCount + MedStarCount, SmlStarCount);
        return stars;
    }

    private static void SortStarBlock((short X, short Y)[] stars, int start, int count)
    {
        for (int i = 0; i < count; ++i)
            for (int j = count - 1; j > i; --j)
                if (stars[start + i].Y > stars[start + j].Y)
                    (stars[start + i], stars[start + j]) = (stars[start + j], stars[start + i]);
    }

    // ship.c's GetNextStarShip: the side's next ship warps in, taking the
    // last one's element. pickship.c's GetEncounterStarShip is a menu for
    // the player; here each side takes its first ship still able to fight
    // and not fighting already (with several ships of a side at once, a
    // teammate in the battle isn't the next ship).
    private bool GetNextStarShip(Starship? last, int side)
    {
        // (UQM recycles a ship of an infinite fleet, picking the dead one
        // again; new_ship here always makes the dead ship unavailable first,
        // so it never comes up: 4b's infinite fleets must bring it back.)
        var next = _fleets[side].FirstOrDefault(s => s.Available && s.Ship == null);
        if (next != null)
        {
            if (last != null)
                next.Ship = last.Ship;
            if (!SpawnShip(next))
                return false;
        }
        if (last != null)
            last.Ship = null;
        return next != null;
    }
}
