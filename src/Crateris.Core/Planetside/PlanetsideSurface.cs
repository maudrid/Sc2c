// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0, src/uqm/planets/lander.c
// (BuildObjectList, CheckObjectCollision, pickupMineralNode, pickupBioNode,
// object_animation, shotCreature, AddGroundDisaster, AddLightning,
// lightning_process)
// and src/uqm/planets/scan.c (GeneratePlanetSide),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Planetside;

/// <summary>
/// A planet's surface while landers are on it: its random generator
/// (TFB_Random), its landers and what's on it, in UQM's display queue
/// order. Each frame (<see cref="Step"/>) every lander moves and fires;
/// then around each lander quakes, lightning and lava may start, by the
/// world's chances (BuildObjectList's roll, per lander as decision 3 has
/// it); then everything on the surface moves, once; then each lander on
/// the ground picks up what it touches, is hurt by what it touches, and
/// its bolts hit what they touch (CheckObjectCollision), and lightning that
/// struck it this frame takes a crew member. With one lander that's UQM's
/// frame. Creatures notice, hunt and flee the nearest lander
/// (decision 2). Energy nodes are only on story worlds, whose generators
/// give them their images; they come with those worlds in stage 3
/// (decision 4).
/// </summary>
public sealed class PlanetsideSurface
{
    /// <summary>SURFACE_WIDTH and SURFACE_HEIGHT: the surface view, the lander at its middle.</summary>
    public const int ViewWidth = 242, ViewHeight = 147;

    /// <summary>
    /// MAX_DISPLAY_ELEMENTS: how much UQM's display queue holds, nodes and
    /// all. When it's full, no bolt, explosion or hazard can start
    /// (AllocElement fails), and none of their random numbers is drawn. On
    /// a shared surface each lander has UQM's queue to itself (decision 3):
    /// the nodes and creatures, and its own bolts, explosion and hazards.
    /// With one lander that's UQM's queue.
    /// </summary>
    public const int MaxElements = 150;

    private readonly List<SurfaceElement> _elements = new();
    private readonly List<Lander> _landers = new();
    private readonly List<(ScanType Scan, int Node)> _taken = new();
    private readonly ContactShapes? _shapes;
    private readonly Lifeforms? _lifeforms;

    /// <summary>SPEED_SLOW, SPEED_MEDIUM and SPEED_FAST in velocity units (object_animation).</summary>
    public static readonly IReadOnlyList<int> CreatureSpeeds = new[]
    {
        0, Velocity.WorldToVelocity(2 * 1) >> 2, Velocity.WorldToVelocity(2 * 1) >> 1, Velocity.WorldToVelocity(2 * 1) * 9 / 10,
    };

    /// <summary>
    /// A surface with its nodes put on it as GeneratePlanetSide puts them:
    /// the creatures, then the mineral nodes, last node first in each, at
    /// four times their place on the scan map. A moving creature is put at
    /// a random place, and every creature starts on a random frame of its
    /// sheet (generateBioNode's TFB_Random draws, in that order). The caller
    /// leaves out nodes already taken (the world's taken-node masks,
    /// isNodeRetrieved). Touching needs the sprites' shapes
    /// (<paramref name="shapes"/>), so a surface with nodes must have them;
    /// <paramref name="elementCategory"/> gives an element's category
    /// (<see cref="PlanetTypes.ElementCategory"/>), <paramref name="lifeforms"/>
    /// the creatures' kinds, <paramref name="hazards"/> the world's chances
    /// of quakes, lightning and lava (none if not given); hazards need the
    /// shapes too.
    /// </summary>
    public PlanetsideSurface(TfbRandom random, IEnumerable<SurfaceMineral>? minerals = null,
        Func<int, int>? elementCategory = null, ContactShapes? shapes = null,
        IEnumerable<SurfaceCreature>? creatures = null, Lifeforms? lifeforms = null,
        HazardChances hazards = default)
    {
        Random = random;
        _shapes = shapes;
        _lifeforms = lifeforms;
        if (hazards.Tectonics < 0 || hazards.Weather < 0 || hazards.Fire < 0)
            throw new ArgumentOutOfRangeException(nameof(hazards), "a hazard's chance can't be negative");
        if (hazards != HazardChances.None && shapes is null)
            throw new ArgumentException("a surface with hazards needs the shapes");
        Hazards = hazards;
        var nodes = (minerals ?? Array.Empty<SurfaceMineral>()).OrderByDescending(m => m.Node).ToList();
        var life = (creatures ?? Array.Empty<SurfaceCreature>()).OrderByDescending(c => c.Node).ToList();
        if (nodes.Count > 0 && (shapes is null || elementCategory is null))
            throw new ArgumentException("a surface with nodes needs the shapes and the element categories");
        if (life.Count > 0 && (shapes is null || lifeforms is null))
            throw new ArgumentException("a surface with creatures needs the shapes and the lifeforms");
        // UQM skips the nodes it has no room for; no world has so many
        // (a scan's taken nodes are 32 bits), so here it's an error.
        if (nodes.Count + life.Count > MaxElements)
            throw new ArgumentException($"a surface holds {MaxElements} nodes at most");
        if (life.Select(c => c.Kind).Distinct().Count() > Lifeforms.MaxVariation)
            throw new ArgumentException($"a surface has sheets for {Lifeforms.MaxVariation} kinds of creature at most");
        foreach (var c in life)
        {
            var kind = lifeforms![c.Kind];
            int x = c.X, y = c.Y;
            if (kind.Speed != CreatureSpeed.Motionless)
            {
                int r = (ushort)Random.Next();
                x = (r & 0xFF) % (SurfaceMap.Width - (8 << 1)) + 8;
                y = (r >> 8) % (SurfaceMap.Height - (8 << 1)) + 8;
            }
            _elements.Add(new SurfaceElement
            {
                Kind = SurfaceElementKind.Creature,
                Scan = ScanType.Biological,
                Node = c.Node,
                LifeKind = c.Kind,
                MassPoints = c.Kind,
                HitPoints = kind.HitPoints,
                TurnWait = 0x44,   // MAKE_BYTE (4, 4): a new frame every 5
                Sheet = SurfaceSheet.Life,
                Frame = (ushort)Random.Next() % shapes!.Life[c.Kind].Count,
                X = x << PlanetsideMap.MagShift,
                Y = y << PlanetsideMap.MagShift,
            });
        }
        foreach (var m in nodes)
        {
            int category = elementCategory!(m.Element);
            _elements.Add(new SurfaceElement
            {
                Kind = SurfaceElementKind.Mineral,
                Scan = ScanType.Mineral,
                Node = m.Node,
                Element = m.Element,
                Amount = m.Density >> 8,
                Category = category,
                // The scan map's dot for the deposit's size, one bigger.
                Sheet = SurfaceSheet.MiscData,
                Frame = ContactShapes.FirstMineralFrame + category * ContactShapes.MineralFramesPerCategory
                    + (m.Density & 0xFF) + 2,
                X = m.X << PlanetsideMap.MagShift,
                Y = m.Y << PlanetsideMap.MagShift,
            });
        }
    }

    public TfbRandom Random { get; }

    /// <summary>The world's chances of quakes, lightning and lava, out of 256 a frame around each lander.</summary>
    public HazardChances Hazards { get; }

    /// <summary>What's on the surface, head of the queue first.</summary>
    public IReadOnlyList<SurfaceElement> Elements => _elements;

    /// <summary>The landers on the surface or coming down, in the order they came.</summary>
    public IReadOnlyList<Lander> Landers => _landers;

    /// <summary>
    /// The nodes taken in the last frame, in order (setNodeRetrieved):
    /// picked up, or hit by a bolt. A node can be listed more than once, as
    /// UQM retrieves it again, when more than one of a lander's bolts, or a
    /// bolt and the lander, touch it in one frame.
    /// </summary>
    public IReadOnlyList<(ScanType Scan, int Node)> Taken => _taken;

    /// <summary>
    /// A ship's lander, sent out with its crew (<see cref="ShipHold.LanderLeaves"/>,
    /// which <see cref="GameWorld.Dispatch"/> does with the fuel), comes
    /// down at a site (planetside map units), with the ship's lander
    /// upgrades (<see cref="Lander"/>). It starts with the next frame.
    /// </summary>
    public Lander Land(ShipHold hold, int siteX, int siteY)
    {
        if (siteX is < 0 or >= PlanetsideMap.Width || siteY is < 0 or >= PlanetsideMap.Height)
            throw new ArgumentOutOfRangeException(nameof(siteX), $"the site ({siteX}, {siteY}) is off the map");
        if (hold.LanderOut is null)
            throw new InvalidOperationException("the ship hasn't sent a lander out");
        if (_landers.Any(l => l.Hold == hold))
            throw new InvalidOperationException("that ship's lander is already here");
        var lander = new Lander(this, hold, siteX, siteY);
        _landers.Add(lander);
        return lander;
    }

    /// <summary>
    /// One surface frame, 35 a second, with each lander's keys, all asked
    /// for before anything moves (so a callback that throws changes nothing,
    /// and one landing a new lander starts it next frame). A lander whose
    /// take-off ends leaves the surface and goes back to its ship with
    /// what it carries.
    /// </summary>
    public void Step(Func<Lander, LanderInput> keys)
    {
        var landers = _landers.ToArray();
        var input = new LanderInput[landers.Length];
        for (int i = 0; i < landers.Length; ++i)
            input[i] = keys(landers[i]);
        _taken.Clear();
        for (int i = 0; i < landers.Length; ++i)
            landers[i].Frame(input[i]);
        foreach (var lander in landers)
        {
            lander.Struck = false;
            if (lander.FrameDrawn)
                StartHazards(lander);
        }
        MoveElements();
        foreach (var lander in landers)
        {
            if (!lander.FrameDrawn)
                continue;   // the frame that ends a lost lander's wait isn't one
            lander.Draw();
            // ScrollPlanetSide: contact on the ground only, while it has crew.
            // Its own bolts and explosion touch nothing (they're the
            // lander's own drawable), so with only those there's nothing to test.
            if (lander.FramePhase == LanderPhase.Ground && lander.Crew > 0
                    && _elements.Any(e => !e.LanderShot))
                CheckObjectCollision(lander, null);
            lander.TextFrame();
            if (lander.Struck)
                lander.Hurt(LanderDisaster.Lightning);
        }
        foreach (var lander in landers)
        {
            if (lander.Phase != LanderPhase.Gone)
                continue;
            _landers.Remove(lander);
            lander.BackAboard();
        }
    }

    /// <summary>
    /// Whether there's room for one more of a lander's elements
    /// (AllocElement): the nodes and creatures and what's that lander's
    /// under <see cref="MaxElements"/>. Hazards round a lander that has left
    /// count for no one.
    /// </summary>
    internal bool HasRoom(Lander lander) => _elements.Count(e => e.Owner is null || e.Owner == lander) < MaxElements;

    /// <summary>Puts an element at the head of the queue (InsertElement before GetHeadElement).</summary>
    internal void AddFirst(SurfaceElement element) => _elements.Insert(0, element);

    /// <summary>
    /// BuildObjectList's roll around a lander: one random number, whose
    /// third byte starts a lava spot, second an earthquake and first a bolt
    /// of lightning, each if under its chance.
    /// </summary>
    private void StartHazards(Lander lander)
    {
        uint r = Random.Next();
        if (((r >> 16) & 0xFF) < Hazards.Fire)
            AddGroundDisaster(SurfaceElementKind.Lava, lander);
        if (((r >> 8) & 0xFF) < Hazards.Tectonics)
            AddGroundDisaster(SurfaceElementKind.Quake, lander);
        if ((r & 0xFF) < Hazards.Weather)
            AddLightning(lander);
    }

    /// <summary>
    /// AddGroundDisaster: a quake or a lava spot somewhere in the middle
    /// three quarters of the lander's view each way, wrapping round the
    /// map both ways (as UQM's place does, even north and south), at the
    /// end of the queue. A quake shows a new frame every 3, a lava spot
    /// every frame, heading a random way; each lasts as long as its frames.
    /// None if the queue is full.
    /// </summary>
    private SurfaceElement? AddGroundDisaster(SurfaceElementKind kind, Lander lander)
    {
        if (!HasRoom(lander))
            return null;
        uint r = Random.Next();
        var e = new SurfaceElement
        {
            Kind = kind,
            Owner = lander,
            MassPoints = (int)(kind == SurfaceElementKind.Quake ? LanderDisaster.Earthquake : LanderDisaster.Lavaspot),
            X = (int)((lander.X + (PlanetsideMap.Width - ViewWidth * 3 / 8) + (r & 0xFFFF) % (ViewWidth * 3 / 4))
                % PlanetsideMap.Width),
            Y = (int)((lander.Y + (PlanetsideMap.Height - ViewHeight * 3 / 8) + (r >> 16) % (ViewHeight * 3 / 4))
                % PlanetsideMap.Height),
        };
        if (kind == SurfaceElementKind.Quake)
        {
            e.Sheet = SurfaceSheet.Quake;
            e.TurnWait = 0x22;   // MAKE_BYTE (2, 2): a new frame every 3
        }
        else
        {
            e.Sheet = SurfaceSheet.Lava;
            e.Facing = UqmMath.NormalizeFacing((int)Random.Next());
            e.TurnWait = 0;      // a new frame every frame
        }
        e.LifeSpan = FramesIn(e) * ((e.TurnWait & 0xF) + 1) - 1;
        _elements.Add(e);
        return e;
    }

    /// <summary>
    /// AddLightning: a bolt of lightning, harmful 1 time in 4, lasting 11
    /// to 20 frames, somewhere on the lander's view but its 6-unit edges,
    /// at the end of the queue (none if the queue is full).
    /// </summary>
    private SurfaceElement? AddLightning(Lander lander)
    {
        if (!HasRoom(lander))
            return null;
        bool harmful = Random.Next() % 100 < 25;
        uint r = Random.Next();
        int life = 10 + (int)((r >> 16) % 10) + 1;
        var bolt = new SurfaceElement
        {
            Kind = SurfaceElementKind.Lightning,
            Owner = lander,
            MassPoints = harmful ? (int)LanderDisaster.Lightning : 0,
            LifeSpan = life,
            Cycle = life,
            X = (int)((lander.X + (PlanetsideMap.Width - ((ViewWidth >> 1) - 6)) + (r & 0xFF) % (ViewWidth - 12))
                % PlanetsideMap.Width),
            Y = (int)((lander.Y + (PlanetsideMap.Height - ((ViewHeight >> 1) - 6)) + ((r >> 8) & 0xFF) % (ViewHeight - 12))
                % PlanetsideMap.Height),
            Sheet = SurfaceSheet.Lightning,
            Frame = 0,
        };
        _elements.Add(bolt);
        return bolt;
    }

    /// <summary>For tests of rules UQM has no recording for: starts a hazard round a lander, as the roll would.</summary>
    internal SurfaceElement? StartHazardForTest(SurfaceElementKind kind, Lander lander) =>
        kind == SurfaceElementKind.Lightning ? AddLightning(lander) : AddGroundDisaster(kind, lander);

    /// <summary>
    /// lightning_process: each frame a bolt of lightning shows one of its
    /// frames at random; a harmful one keeps to its lander and, while the
    /// lander has crew, strikes it 1 time in 10 when it's on the ground
    /// (KILL_CREW: one crew member as the frame ends, however many strike).
    /// Lightning round a lander that has left stays where it is and strikes
    /// nothing.
    /// </summary>
    private void Lightning(SurfaceElement e)
    {
        if ((e.TurnWait & 0xF) != 0)
        {
            e.TurnWait--;
            return;
        }
        int frames = FramesIn(e) - ContactShapes.LightningStrikeFrames;
        if (e.Frame >= frames)
            e.Frame = e.Frame + 1 < FramesIn(e) ? e.Frame + 1 : 0;   // the strike's frames: never reached in UQM
        else
        {
            var lander = e.Owner!;
            if (e.MassPoints == (int)LanderDisaster.Lightning && _landers.Contains(lander))
            {
                if (lander.Crew > 0 && Random.Next() % 100 < 10 && lander.FramePhase == LanderPhase.Ground)
                    lander.Struck = true;
                e.X = lander.X;
                e.Y = lander.Y;
            }
            e.Frame = (int)(Random.Next() % (uint)frames);
        }
        e.TurnWait += e.TurnWait >> 4;
    }

    /// <summary>
    /// BuildObjectList after the roll: each element in order is removed if
    /// its life is over or it went this frame, or has its life counted down
    /// and moves by its velocity, wrapping east and west; all but a
    /// lander's shots stop at the poles.
    /// </summary>
    private void MoveElements()
    {
        for (int i = 0; i < _elements.Count;)
        {
            var e = _elements[i];
            if ((e.FiniteLife && e.LifeSpan == 0) || e.Disappearing)
            {
                _elements.RemoveAt(i);
                continue;
            }
            if (e.FiniteLife)
                e.LifeSpan--;
            if (e.Kind == SurfaceElementKind.Lightning)
                Lightning(e);
            else if (e.Kind is not (SurfaceElementKind.Bolt or SurfaceElementKind.Mineral))
                Animate(e);
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

    private int FramesIn(SurfaceElement e) => e.Sheet switch
    {
        // Only an explosion animates on it; without the shapes (no contact
        // tested), as far as its frames go.
        SurfaceSheet.Lander => _shapes?.Lander.Count ?? Lander.ExplosionFrame + Lander.ExplosionLife,
        SurfaceSheet.MiscData => _shapes!.MiscData.Count,
        SurfaceSheet.Canister => _shapes!.Canister.Count,
        SurfaceSheet.Quake => _shapes!.Quake.Count,
        SurfaceSheet.Lightning => _shapes!.Lightning.Count,
        SurfaceSheet.Lava => _shapes!.Lava.Count,
        _ => _shapes!.Life[e.LifeKind].Count,
    };

    /// <summary>
    /// object_animation: a creature's flash after a hit runs its colours
    /// back to white (and a canned one then stops, on its canister's first
    /// frame); every turn_wait it shows its next frame, and on every fourth
    /// a creature still standing takes a new heading by its behaviour, from
    /// the nearest lander (decision 2). An explosion shows its frames and
    /// moves its lander's explosion on. A quake and a lava spot show their
    /// frames; on a lava spot's fifth frame it spreads 9 times in 10: a
    /// new spot 4 units on its way, heading up to a facing either side.
    /// </summary>
    private void Animate(SurfaceElement e)
    {
        if (e.Flash is int c)
        {
            // DamageColorCycle (c, 0): white, then 6 down to 1, then white again.
            int next = c == 0 ? 6 : c - 1;
            if (next == 0)
            {
                e.Flash = null;
                if (e.HitPoints == 0)
                {
                    e.Velocity.Zero();
                    e.Frame = 0;
                }
            }
            else
                e.Flash = next;
        }
        int frameIndex = e.Frame + 1;
        if ((e.TurnWait & 0xF) != 0)
        {
            e.TurnWait--;
            return;
        }
        e.TurnWait += e.TurnWait >> 4;
        e.Frame = e.Frame + 1 < FramesIn(e) ? e.Frame + 1 : 0;
        if (e.Kind == SurfaceElementKind.Explosion)
        {
            if (++e.Owner!.ExplosionIndex >= Lander.ExplosionLife)
                e.Frame = e.Frame > 0 ? e.Frame - 1 : FramesIn(e) - 1;   // the last frame is drawn twice
            return;
        }
        if (e.Kind == SurfaceElementKind.Lava && frameIndex == 5 && Random.Next() % 100 < 90)
            SpreadLava(e);
        if (e.Hazard)
            return;
        if ((frameIndex & 3) != 0 || e.HitPoints == 0)
            return;
        var kind = _lifeforms![e.MassPoints & ~SurfaceElement.Aware];
        if (kind.Speed == CreatureSpeed.Motionless)
            return;
        var lander = Nearest(e);
        if (lander is null)
            return;
        int dx = lander.X - e.X;
        if (dx < 0 && dx < -(PlanetsideMap.Width >> 1))
            dx += PlanetsideMap.Width;
        else if (dx > PlanetsideMap.Width >> 1)
            dx -= PlanetsideMap.Width;
        int dy = lander.Y - e.Y;
        int angle = UqmMath.ArcTan((short)dx, (short)dy);
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        if (dx >= ViewWidth || dy >= ViewWidth || dx * dx + dy * dy >= ViewWidth * ViewWidth)
            e.MassPoints &= ~SurfaceElement.Aware;
        else if ((e.MassPoints & SurfaceElement.Aware) == 0 && Random.Next() % 100 < kind.DetectPercent)
        {
            e.ThrustWait = 0;
            e.MassPoints |= SurfaceElement.Aware;
        }
        bool atPole = e.Y == 0 || e.Y == PlanetsideMap.Height - 1;
        if (atPole)
            e.ThrustWait = 0;
        int oldAngle = e.Velocity.TravelAngle;
        if (e.ThrustWait > 0)
        {
            e.ThrustWait--;
            angle = oldAngle;
        }
        else if ((e.MassPoints & SurfaceElement.Aware) == 0 || kind.Behavior == CreatureBehavior.Unpredictable)
        {
            int r = (ushort)Random.Next();
            angle = UqmMath.NormalizeAngle(r & 0xFF);
            e.ThrustWait = (r >> 8 >> 2) + 10;
        }
        else if (kind.Behavior == CreatureBehavior.Flee)
        {
            if (atPole)
            {
                if ((angle & (UqmMath.HalfCircle - 1)) != 0)
                    angle = UqmMath.HalfCircle - angle;
                else if (oldAngle == UqmMath.Quadrant || oldAngle == UqmMath.FullCircle - UqmMath.Quadrant)
                    angle = oldAngle;
                else
                    angle = (int)(Random.Next() & 1) * UqmMath.HalfCircle - UqmMath.Quadrant;
                e.ThrustWait = 5;
            }
            angle = UqmMath.NormalizeAngle(angle + UqmMath.HalfCircle);
        }
        int speed = CreatureSpeeds[(int)kind.Speed];
        e.Velocity.SetComponents(UqmMath.Cosine(angle, speed), UqmMath.Sine(angle, speed));
    }

    /// <summary>
    /// A lava spot spreading: AddGroundDisaster's spot (its random place
    /// thrown away; none if the queue is full) put 4 units the old one's way, kept off the poles and
    /// wrapping east and west, its heading turned by up to a facing.
    /// </summary>
    private void SpreadLava(SurfaceElement e)
    {
        var spot = AddGroundDisaster(SurfaceElementKind.Lava, e.Owner!);
        if (spot is null)
            return;   // the queue is full
        int angle = UqmMath.FacingToAngle(e.Facing);
        int x = (short)(e.X + UqmMath.Cosine(angle, 4));
        int y = (short)(e.Y + UqmMath.Sine(angle, 4));
        spot.Y = Math.Clamp(y, 0, PlanetsideMap.Height - 1);
        spot.X = x < 0 ? x + PlanetsideMap.Width : x % PlanetsideMap.Width;
        spot.Facing = UqmMath.NormalizeFacing(e.Facing + (int)(Random.Next() % 3) - 1);
    }

    /// <summary>
    /// The lander a creature reacts to: in UQM the one lander, wherever
    /// it is, alive or not; here the nearest (decision 2), east and west
    /// the nearer way round, the earlier on the surface if two are as near,
    /// and one with crew before a dead one, so creatures don't hunt or flee
    /// a wreck while a crewed lander is there. With one lander that's UQM's.
    /// </summary>
    internal Lander? Nearest(SurfaceElement e)
    {
        bool anyCrewed = _landers.Any(l => l.Crew > 0);
        Lander? best = null;
        long bestDistance = long.MaxValue;
        // Every lander still on the surface, the one whose take-off ends this
        // frame too (it's there until the frame is over).
        foreach (var l in _landers)
        {
            if (anyCrewed && l.Crew == 0)
                continue;
            long dx = Math.Abs(l.X - e.X);
            dx = Math.Min(dx, PlanetsideMap.Width - dx);
            long dy = l.Y - e.Y;
            long d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                best = l;
                bestDistance = d;
            }
        }
        return best;
    }

    // BuildObjectList's stamp origin: where an element is drawn on the
    // lander's view, the nearer way round east or west.
    private static (short X, short Y) ViewPlace(SurfaceElement e, Lander lander)
    {
        int x = e.X - lander.X + (ViewWidth >> 1);
        const int far = PlanetsideMap.Width - ViewWidth * 3 / 2;
        if (x >= far)
            x -= PlanetsideMap.Width;
        else if (x <= -far)
            x += PlanetsideMap.Width;
        return ((short)x, (short)(e.Y - lander.Y + (ViewHeight >> 1)));
    }

    private IntersectFrame FrameOf(SurfaceElement e) => e.Sheet switch
    {
        SurfaceSheet.Lander => _shapes!.Lander[e.Frame],
        SurfaceSheet.MiscData => _shapes!.MiscData[e.Frame],
        SurfaceSheet.Canister => _shapes!.Canister[e.Frame],
        SurfaceSheet.Quake => _shapes!.Quake[e.Frame],
        SurfaceSheet.Lightning => _shapes!.Lightning[e.Frame],
        SurfaceSheet.Lava => _shapes!.Lava[e.Frame],
        _ => _shapes!.Life[e.LifeKind][e.Frame],
    };

    /// <summary>
    /// CheckObjectCollision: what the lander touches, or (with
    /// <paramref name="bolt"/>) what one of its bolts touches, as UQM walks
    /// its display list (here the queue's order, since BuildObjectList puts
    /// each element in front of the last). A bolt is the lander's own
    /// drawable, so it isn't touched but checked in its turn against what
    /// follows it. On contact, the queue is walked: a bolt that touched
    /// something goes; a mineral the lander touches is picked up, as much
    /// as the lander has room for (none: it stays), and one a bolt touches
    /// is gone with nothing picked up, as in UQM.
    /// </summary>
    private void CheckObjectCollision(Lander lander, int? boltAt)
    {
        IntersectControl landerControl;
        SurfaceElement? bolt = null;
        int index;
        if (boltAt is int b)
        {
            bolt = _elements[b];
            var (bx, by) = ViewPlace(bolt, lander);
            landerControl = new IntersectControl(FrameOf(bolt), bx, by);
            index = b + 1;
        }
        else
        {
            landerControl = new IntersectControl(_shapes?.Lander[lander.Facing]
                    ?? throw new InvalidOperationException("contact needs the sprites' shapes"),
                ViewWidth >> 1, ViewHeight >> 1);
            index = 0;
        }
        var elements = _elements;
        for (; index < elements.Count; ++index)
        {
            var prim = elements[index];
            if (prim.Kind == SurfaceElementKind.Bolt)
            {
                if (prim.Owner == lander)
                    CheckObjectCollision(lander, index);
                continue;   // another lander's bolt: not this lander's to touch (item 9)
            }
            if (prim.Kind == SurfaceElementKind.Explosion)
                continue;   // a lander's own drawable; a dead lander touches nothing
            // Lightning goes at the end of UQM's display list, before
            // everything else in the walk, so no bolt is checked against it;
            // a lander touching it is, but CheckObjectCollision does nothing
            // for lightning (only quakes and lava hurt on touch).
            if (prim.Kind == SurfaceElementKind.Lightning)
                continue;
            // Taken this frame by another lander: it's that lander's (one
            // node, one taker). A lander can touch again what it or its
            // bolt took this frame, as UQM's one lander can.
            if (prim.Disappearing && prim.TakenBy != lander)
                continue;
            var (px, py) = ViewPlace(prim, lander);
            var control = new IntersectControl(FrameOf(prim), px, py);
            if (Intersection.DrawablesIntersect(landerControl, control, Intersection.MaxTimeValue) == 0)
                continue;
            foreach (var e in elements)
            {
                if (e == bolt)
                {
                    e.Disappearing = true;
                    e.TakenBy = lander;
                    continue;
                }
                if (e != prim)
                    continue;
                if (e.Hazard)
                {
                    // A quake or lava hurts the lander 1 time in 4 each frame
                    // it touches it; a bolt that touches one just goes.
                    if (bolt is null)
                    {
                        if (lander.Crew == 0)
                            break;
                        if (Random.Next() % 100 < 25)
                            lander.Hurt((LanderDisaster)e.MassPoints);
                    }
                    continue;
                }
                int retrieved;
                if (bolt is null)
                {
                    if (lander.Crew == 0)
                        break;      // its last crew member died this frame: it touches nothing more
                    if (e.Kind == SurfaceElementKind.Creature && e.HitPoints > 0)
                    {
                        // A live creature: it may hurt the crew, by its danger.
                        var kind = _lifeforms![e.MassPoints & ~SurfaceElement.Aware];
                        if (Random.Next() % 128 < kind.DangerChance)
                            lander.Hurt(LanderDisaster.Biological);
                        continue;
                    }
                    // A deposit, or a canned creature's bio value.
                    retrieved = e.Kind == SurfaceElementKind.Creature ? e.MassPoints : e.Amount;
                }
                else
                {
                    // A stun bolt. UQM reads the creature's kind from its
                    // mass_points, which once it's canned are its value; every
                    // value names a kind worth something, so a canned creature
                    // takes bolts and nothing comes of them. A live creature
                    // worth nothing (a Brainbox Bulldozer) is destroyed by one,
                    // nothing picked up.
                    if (e.Kind == SurfaceElementKind.Creature
                            && _lifeforms![e.MassPoints & ~SurfaceElement.Aware].BioValue is var value and > 0)
                    {
                        Stun(e, value, bolt);
                        break;
                    }
                    retrieved = 0;          // nothing picked up: the node is gone
                }
                if (retrieved > 0 && !(e.Kind == SurfaceElementKind.Creature
                        ? lander.PickUpBioData(retrieved)
                        : lander.PickUpMinerals(e, retrieved,
                            control.EndX - landerControl.EndX, control.EndY - landerControl.EndY)))
                    continue;               // the lander is full: it stays
                // The default generator's pickupMinerals and pickupLife keep nothing back.
                _taken.Add((e.Scan, e.Node));
                e.Disappearing = true;
                e.TakenBy = lander;
            }
        }
    }

    /// <summary>
    /// shotCreature: a hit takes a stun point; at none the creature is
    /// canned (its mass_points become its value, its image the canister);
    /// a moving one hit but still standing is knocked the bolt's way and
    /// notices. Either way it flashes white. One already canned is left.
    /// </summary>
    private void Stun(SurfaceElement e, int value, SurfaceElement bolt)
    {
        if (e.HitPoints == 0)
            return;
        if (--e.HitPoints == 0)
        {
            e.MassPoints = value;
            e.Sheet = SurfaceSheet.Canister;
            e.Frame = 0;
        }
        else if (_lifeforms![e.MassPoints & ~SurfaceElement.Aware].Speed != CreatureSpeed.Motionless)
        {
            int angle = UqmMath.FacingToAngle(bolt.Frame - UqmMath.Facings);
            e.Velocity.Delta(UqmMath.Cosine(angle, Velocity.WorldToVelocity(1)),
                UqmMath.Sine(angle, Velocity.WorldToVelocity(1)));
            e.ThrustWait = 0;
            e.MassPoints |= SurfaceElement.Aware;
        }
        e.Flash = 0;
    }
}
