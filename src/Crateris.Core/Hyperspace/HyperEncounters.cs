// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0: check_hyperspace_encounter,
// AddEncounterElement, ProcessEncounter, ProcessEncounters,
// encounter_transition, encounter_collision and cleanup_hyperspace
// (src/uqm/hyper.c), and the encounter elements' part of PreProcess and
// PostProcess (src/uqm/process.c). Copyright Paul Reiche, Fred Ford and
// Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Story;
using Crateris.Core.Uqm;

namespace Crateris.Core.Hyperspace;

/// <summary>
/// UQM's random numbers for one player in hyperspace (TFB_Random, which
/// Battle seeds on every entry into hyperspace): their daily rolls, the
/// groups their rolls make and the ambient stars round them draw on it.
/// <see cref="Places"/>, if set, lists where each number was drawn (tests).
/// </summary>
public sealed class HyperspaceRandom
{
    private readonly TfbRandom _random;

    public HyperspaceRandom(uint seed) => _random = new TfbRandom(seed);

    public uint Seed => _random.Seed;

    /// <summary>
    /// The frames since the player came into hyperspace (none until its
    /// first frame's encounters are done): the first frame's view is set
    /// up after it, which draws a number for each of the background's 90
    /// near stars (MoveGalaxy's VIEW_CHANGE).
    /// </summary>
    public int Frames { get; internal set; }

    public List<string>? Places { get; set; }

    /// <summary>A copy, drawing the same numbers from here on.</summary>
    public HyperspaceRandom Clone() =>
        new(Seed) { Frames = Frames, Places = Places is null ? null : new List<string>(Places) };

    public uint Next(string place)
    {
        Places?.Add(place);
        return _random.Next();
    }
}

/// <summary>A player in hyperspace, as the encounters see them: where they are (universe units).</summary>
public readonly record struct HyperspacePlayer(string Id, short X, short Y);

/// <summary>
/// The hyperspace encounters the players share (docs/plans/stage-3b.md,
/// item 3): UQM's encounter_q, at most <see cref="Cap"/> (UQM's 16, a
/// setting), rolled each day for each player in hyperspace
/// (<see cref="Roll"/>: check_hyperspace_encounter, decision 3), each
/// given an element while any player is in hyperspace, near the player
/// whose roll made it, and flying at the nearest player in hyperspace
/// (<see cref="Process"/>: ProcessEncounters, decision 4); a group of a
/// race whose talk isn't ported is dropped once its element's first made
/// (decision 2). One goes when it leaves its sphere, or when it's outside
/// the span of every player in hyperspace and of every player it's kept
/// for (<see cref="HyperEncounter.KeptFor"/>), measured from where they
/// left. <see cref="Touch"/> finds the one a player's ship touches.
/// </summary>
public sealed class HyperEncounters
{
    // hyper.c's XOFFS and YOFFS: half the radar's span and two screens,
    // in universe units (a player's span).
    public const int SpanX = ((63 * 12) + (63 << 2)) >> 1;   // 504
    public const int SpanY = ((50 * 12) + (50 << 2)) >> 1;   // 400

    // UNIT_SCREEN_WIDTH, UNIT_SCREEN_HEIGHT: a screen in universe units.
    public const int ScreenX = 63;
    public const int ScreenY = 50;

    private const int GridOffset = 200;
    private const int VortexTransitions = 9;
    private const int VortexWait = 1;
    private const int TrackWait = 3;

    /// <summary>The frames of graphics.hyperstars in UQM 0.8.0's content: the vortex's are its last 9, the globe its last.</summary>
    public const int DefaultFrames = 32;

    private readonly StoryRules _rules;
    private readonly int _slylandro, _melnorme;

    public HyperEncounters(StoryRules rules, int cap, int frames = DefaultFrames)
    {
        _rules = rules;
        Cap = cap;
        Frames = frames;
        _slylandro = rules.RaceIndex("SLYLANDRO_SHIP");
        _melnorme = rules.RaceIndex("MELNORME_SHIP");
    }

    /// <summary>The most encounters at once (UQM's MAX_ENCOUNTERS, 16).</summary>
    public int Cap { get; }

    /// <summary>graphics.hyperstars' frame count.</summary>
    public int Frames { get; }

    /// <summary>The encounters, in encounter_q's order.</summary>
    public List<HyperEncounter> List { get; private set; } = new();

    /// <summary>The list as it is now, for taking back (each encounter a copy).</summary>
    public List<HyperEncounter> Copy() => List.Select(e => e.Clone()).ToList();

    /// <summary>Puts back a list from <see cref="Copy"/>.</summary>
    public void Restore(List<HyperEncounter> list) => List = list.Select(e => e.Clone()).ToList();

    /// <summary>
    /// check_hyperspace_encounter for one player in hyperspace, at
    /// (<paramref name="x"/>, <paramref name="y"/>) with
    /// <paramref name="fuel"/> aboard: for each race in avail_race_q's
    /// order whose sphere holds them, a number against its chance; a hit
    /// adds a group of the race (if the list has room). The races met
    /// anywhere (the Slylandro, the Melnorme) have their own chances, and
    /// only one of each at a time for each player (decision 3).
    /// </summary>
    public void Roll(StoryState story, string player, short x, short y, uint fuel, HyperspaceRandom random)
    {
        var fleets = story.Fleets;
        for (int type = 0; type < fleets.Count; ++type)
        {
            var fleet = fleets[type];
            ushort radius = (ushort)fleet.ActualStrength;
            if (radius == 0)
                continue;
            byte flags = 0;
            int percent = _rules.Fleets[type].HyperspacePercent;
            if (radius != _rules.InfiniteRadius)
                radius = (ushort)((radius * _rules.SphereRadiusIncrement) >> 1);
            else
            {
                radius = (Units.MaxXUniverse + 1) << 1;
                if (type == _slylandro)
                {
                    flags = HyperEncounter.OneShot;
                    if (story.Get("STARBASE_AVAILABLE") == 0)
                        percent = 100;
                    else
                        percent = (ushort)(percent * story.Get("SLYLANDRO_MULTIPLIER"));
                }
                else if (type == _melnorme
                         && (fuel == 0 || story.Get("USED_BROADCASTER") != 0)
                         && story.Get("MELNORME_ANGER") < 3)
                {
                    percent = story.Get("USED_BROADCASTER") == 0 ? 30 : 100;
                    flags = HyperEncounter.OneShot;
                }

                // "There can be only one!": of this player's.
                if (List.Any(e => e.Race == type && e.Roller == player))
                    percent = 0;

                if (percent == 100 && type == _melnorme)
                    story.Set("BROADCASTER_RESPONSE", 1);
            }

            int dx = Math.Abs(x - fleet.LocX), dy = Math.Abs(y - fleet.LocY);
            if ((ushort)dx < radius && (ushort)dy < radius
                && (uint)dx * (uint)dx + (uint)dy * (uint)dy < (uint)radius * radius
                && (ushort)random.Next("check_hyperspace_encounter") % 100 < percent)
            {
                if (List.Count < Cap)
                {
                    List.Add(new HyperEncounter
                    {
                        OriginX = (short)fleet.LocX, OriginY = (short)fleet.LocY, Radius = radius,
                        Flags = flags, Race = type, Roller = player,
                    });
                }
            }
        }
        story.Set("USED_BROADCASTER", 0);
    }

    /// <summary>
    /// The encounters' elements' part of the frame UQM runs after the
    /// encounters move (RedrawQueue: PreProcess, PostProcess): each
    /// element's vortex steps (encounter_transition) and its life runs
    /// down, and where it was tested from becomes where it is.
    /// </summary>
    public void ElementsFrame()
    {
        foreach (var e in List)
        {
            if (e.Element is not { } el)
                continue;
            if (el.LifeSpan == 0 && el.InVortex)
            {
                // encounter_transition, as the element would die.
                el.LifeSpan = 1;
                if (el.TurnWait > 0)
                    --el.TurnWait;
                else
                {
                    if (el.VortexOut)
                        el.Frame = el.Frame > 0 ? el.Frame - 1 : Frames - 1;
                    else
                    {
                        int f = el.Frame < Frames - 1 ? el.Frame + 1 : 0;
                        if (f != 0)
                            el.Frame = f;
                        else
                            el.InVortex = false;
                    }
                    el.TurnWait = VortexWait;
                }
            }
            if (el.LifeSpan > 0)
                --el.LifeSpan;
            el.CurX = el.NextX;
            el.CurY = el.NextY;
        }
    }

    /// <summary>
    /// ProcessEncounters for the players in hyperspace (none: the elements
    /// go, cleanup_hyperspace, and nothing moves): each encounter's element
    /// made if it has none (near the player whose roll made it, a new one
    /// at <paramref name="rollerAt"/>'s place, with that player's random
    /// numbers), then each steered at the nearest player and checked; those
    /// that go are taken out. A new group of a race not in
    /// <paramref name="ported"/> is dropped once made (decision 2).
    /// QuasiSpace isn't ported (3c's), where UQM makes no element
    /// (ARILOU_SPACE_SIDE 2 or more) and the groups go.
    /// </summary>
    public void Process(IReadOnlyList<HyperspacePlayer> players,
        Func<string, (short X, short Y)> rollerAt, Func<string, HyperspaceRandom> randomOf,
        IReadOnlySet<string> ported)
    {
        if (players.Count == 0)
        {
            foreach (var e in List)
                e.Element = null;
            return;
        }
        for (int i = 0; i < List.Count;)
        {
            if (ProcessOne(List[i], players, rollerAt, randomOf, ported))
                ++i;
            else
                List.RemoveAt(i);
        }
    }

    // ProcessEncounter: false if the encounter is to go.
    private bool ProcessOne(HyperEncounter e, IReadOnlyList<HyperspacePlayer> players,
        Func<string, (short X, short Y)> rollerAt, Func<string, HyperspaceRandom> randomOf,
        IReadOnlySet<string> ported)
    {
        if (e.Element is null)
        {
            if (!AddElement(e, rollerAt, randomOf, out bool isNew))
                return false;
            if (isNew && !ported.Contains(_rules.Races[e.Race]))
                return false;   // decision 2: made as UQM makes it, then dropped
        }
        var el = e.Element!;
        if (el.InVortex)
        {
            if (e.TransitionState != 0 && el.TurnWait == 0)
            {
                --e.TransitionState;
                if (e.TransitionState >= VortexTransitions)
                    ++el.TurnWait;
                else if (e.TransitionState == -VortexTransitions)
                {
                    el.InVortex = false;
                    return false;
                }
                else
                    el.Drawn = true;
            }
        }
        else
        {
            el.LifeSpan = 1;
            var (dx, dy) = el.Velocity.Next(1);
            if (el.ThrustWait > 0)
                --el.ThrustWait;
            else if (el.Met is null)
            {
                // Steered at the nearest player in hyperspace (decision 4).
                var target = Nearest(players, e.LocX, e.LocY);
                int curFacing = UqmMath.AngleToFacing(el.Velocity.TravelAngle);
                int deltaFacing = UqmMath.NormalizeFacing(curFacing - UqmMath.AngleToFacing(
                    UqmMath.ArcTan((short)(target.X - e.LocX), (short)(target.Y - e.LocY))));
                if (deltaFacing != 0 || (dx == 0 && dy == 0))
                {
                    int speed = _rules.Fleets[e.Race].HyperSpeed;
                    const int half = UqmMath.Facings / 2;
                    if (deltaFacing < half)
                        --curFacing;
                    else
                        ++curFacing;
                    if (UqmMath.NormalizeFacing(deltaFacing + UqmMath.Facings / 8) > UqmMath.Facings / 4)
                    {
                        if (deltaFacing < half)
                            --curFacing;
                        else
                            ++curFacing;
                        speed >>= 1;
                    }
                    int angle = UqmMath.FacingToAngle(curFacing);
                    el.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));
                    (dx, dy) = el.Velocity.Next(1);
                    el.ThrustWait = TrackWait;
                }
            }
            e.LogX += dx;
            e.LogY -= dy;
            e.LocX = Units.LogXToUniverse(e.LogX);
            e.LocY = Units.LogYToUniverse(e.LogY);

            ushort reach = (ushort)(e.Radius + (GridOffset >> 1));
            short ox = (short)Math.Abs(e.LocX - e.OriginX), oy = (short)Math.Abs(e.LocY - e.OriginY);
            if ((ushort)ox >= reach || (ushort)oy >= reach
                || (uint)ox * (uint)ox + (uint)oy * (uint)oy >= (uint)reach * reach)
            {
                // Out of its sphere: it goes, through its vortex if it isn't in one.
                el.Nonsolid = true;
                el.LifeSpan = 0;
                if (e.TransitionState == 0)
                {
                    el.InVortex = true;
                    e.TransitionState = -1;
                    el.VortexOut = true;
                }
                else
                {
                    el.InVortex = false;
                    return false;
                }
            }
        }

        // On a player's screen it can be touched (once out of its vortex);
        // off every screen it can't, and it goes once it's out of every
        // span (UQM's flagship's: each player's in hyperspace, and each
        // player's it's kept for, from where they left).
        if (players.Any(p => OnScreen(e, p.X, p.Y)))
        {
            el.NextX = e.LogX;
            el.NextY = e.LogY;
            if (el.Nonsolid && e.TransitionState == 0)
            {
                el.CurX = el.NextX;
                el.CurY = el.NextY;
                el.Drawn = true;
                if (!el.InVortex)
                    el.Nonsolid = false;
            }
        }
        else
        {
            el.Nonsolid = true;
            if (!players.Any(p => InSpan(e, p.X, p.Y)) && !e.KeptFor.Values.Any(k => InSpan(e, k.X, k.Y)))
            {
                el.LifeSpan = 0;
                el.InVortex = false;
                return false;
            }
            el.Drawn = false;
        }
        return true;
    }

    private static bool OnScreen(HyperEncounter e, short x, short y) =>
        e.LocX - x >= -ScreenX && e.LocX - x <= ScreenX && e.LocY - y >= -ScreenY && e.LocY - y <= ScreenY;

    /// <summary>Whether an encounter is within the span of a player at (x, y): hyper.c's XOFFS, YOFFS.</summary>
    public static bool InSpan(HyperEncounter e, short x, short y) =>
        !(e.LocX - x < -SpanX || e.LocX - x > SpanX || e.LocY - y < -SpanY || e.LocY - y > SpanY);

    /// <summary>The nearest player to a place (the first, of those as near).</summary>
    public static HyperspacePlayer Nearest(IReadOnlyList<HyperspacePlayer> players, short x, short y)
    {
        var best = players[0];
        long bestD = long.MaxValue;
        foreach (var p in players)
        {
            long dx = p.X - x, dy = p.Y - y, d = dx * dx + dy * dy;
            if (d < bestD)
            {
                best = p;
                bestD = d;
            }
        }
        return best;
    }

    // AddEncounterElement: false if no element is made (a group re-forming
    // after a talk, or with no ships left: it goes).
    private bool AddElement(HyperEncounter e, Func<string, (short X, short Y)> rollerAt,
        Func<string, HyperspaceRandom> randomOf, out bool isNew)
    {
        isNew = false;
        if ((e.Flags & HyperEncounter.Reforming) != 0)
        {
            e.Flags &= unchecked((byte)~HyperEncounter.Reforming);
            e.TransitionState = 100;
            if ((e.Flags & HyperEncounter.OneShot) != 0 || e.Ships.Count == 0)
                return false;
        }

        if (e.Ships.Count == 0)
        {
            isNew = true;
            var random = randomOf(e.Roller);
            var (ux, uy) = rollerAt(e.Roller);
            uint radiusSquared = (uint)e.Radius * e.Radius;
            var rules = _rules.Fleets[e.Race];
            int ships = rules.MakeupMin;
            for (int i = rules.MakeupMax - ships; i > 0; --i)
            {
                if ((ushort)random.Next("AddEncounterElement") % 100 < 50)
                    ++ships;
            }
            if (ships > HyperEncounter.MaxShips)
                ships = HyperEncounter.MaxShips;
            int crew = _rules.RaceShips[e.Race].Crew;
            for (int i = 0; i < ships; ++i)
                e.Ships.Add(crew);

            short x, y;
            int dx, dy;
            do
            {
                uint r = random.Next("AddEncounterElement");
                x = (short)(ux + (ushort)r % (SpanX << 1) - SpanX);
                x = Math.Clamp(x, (short)0, (short)Units.MaxXUniverse);
                y = (short)(uy + (ushort)(r >> 16) % (SpanY << 1) - SpanY);
                y = Math.Clamp(y, (short)0, (short)Units.MaxYUniverse);
                dx = (short)(x - e.OriginX);
                dy = (short)(y - e.OriginY);
            } while ((uint)((long)dx * dx + (long)dy * dy) > radiusSquared);
            e.LocX = x;
            e.LocY = y;
            e.LogX = Units.UniverseToLogX(x);
            e.LogY = Units.UniverseToLogY(y);
        }

        var el = new EncounterElement { CurX = e.LogX, CurY = e.LogY, NextX = e.LogX, NextY = e.LogY };
        int vortex = e.TransitionState;
        if (vortex != 0 || isNew)
        {
            if (vortex < 0)
            {
                vortex = -vortex;
                el.VortexOut = true;
            }
            if (vortex == 0 || vortex > VortexTransitions)
                vortex = VortexTransitions;
            el.Frame = Frames - vortex;
            el.InVortex = true;
        }
        else
            el.Frame = Frames - 1;
        el.TurnWait = VortexWait;
        e.Element = el;
        return true;
    }

    /// <summary>
    /// The encounter a ship in hyperspace touches this frame, if any: each
    /// that can be touched (its element on a screen, out of its vortex, met
    /// by no one), a point moving from where it was to where it is, against
    /// the ship's frame moving from <paramref name="before"/> to where its
    /// sprite is now, as process.c tests them (the soonest touch; the first
    /// in the list of those as soon).
    /// </summary>
    public HyperEncounter? Touch(HyperFlight ship, ContactShapes shapes, (long X, long Y) before)
    {
        var now = StarContact.SpritePosition(ship);
        IntersectFrame shipFrame = shapes.HyperFlagship[ship.Facing];
        HyperEncounter? best = null;
        int bestTime = int.MaxValue;
        foreach (var e in List)
        {
            if (e.Element is not { Nonsolid: false, Met: null } el)
                continue;
            var shipControl = new IntersectControl(shipFrame, Display(before.X - now.X + ship.ViewX), Display(before.Y - now.Y + ship.ViewY))
            {
                EndX = Display(ship.ViewX),
                EndY = Display(ship.ViewY),
            };
            long baseX = (Units.LogSpaceWidth >> 1) - (long)ship.LogX, baseY = (Units.LogSpaceHeight >> 1) - (long)ship.LogY;
            var point = new IntersectControl(shapes.HyperPoint, Display(el.CurX + baseX), Display(el.CurY + baseY))
            {
                EndX = Display(el.NextX + baseX),
                EndY = Display(el.NextY + baseY),
            };
            int time = Intersection.DrawablesIntersect(shipControl, point, Intersection.MaxTimeValue);
            if (time != 0 && time < bestTime)
            {
                best = e;
                bestTime = time;
            }
        }
        return best;
    }

    private static short Display(long logical) => unchecked((short)(logical >> Units.OneShift));
}
