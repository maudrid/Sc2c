// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0, src/uqm/planets/lander.c
// (BuildObjectList, CheckObjectCollision, pickupMineralNode, pickupBioNode)
// and src/uqm/planets/scan.c (GeneratePlanetSide),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Planetside;

/// <summary>
/// A planet's surface while landers are on it: its random generator
/// (TFB_Random), its landers and what's on it, in UQM's display queue
/// order. Each frame (<see cref="Step"/>) every lander moves and fires;
/// then each draws one random number for the hazards around it
/// (BuildObjectList's roll, per lander as decision 3 has it; the hazards
/// themselves are item 8); then everything on the surface moves, once;
/// then each lander on the ground picks up what it touches, and its bolts
/// hit what they touch (CheckObjectCollision). With one lander that's
/// UQM's frame. Only mineral nodes are on it so far: energy nodes (which
/// stop bolts in UQM, and do nothing else yet, decision 4) and creatures
/// come with item 7.
/// </summary>
public sealed class PlanetsideSurface
{
    /// <summary>SURFACE_WIDTH and SURFACE_HEIGHT: the surface view, the lander at its middle.</summary>
    public const int ViewWidth = 242, ViewHeight = 147;

    private readonly List<SurfaceElement> _elements = new();
    private readonly List<Lander> _landers = new();
    private readonly List<(ScanType Scan, int Node)> _taken = new();
    private readonly ContactShapes? _shapes;

    /// <summary>
    /// A surface with its mineral nodes put on it as GeneratePlanetSide
    /// puts them (last node first, at four times their place on the scan
    /// map); the caller leaves out those already taken (the world's
    /// taken-node mask, isNodeRetrieved). Touching needs the sprites'
    /// shapes (<paramref name="shapes"/>), so a surface with nodes must
    /// have them; <paramref name="elementCategory"/> gives an element's
    /// category (<see cref="Data.PlanetTypes.ElementCategory"/>).
    /// </summary>
    public PlanetsideSurface(TfbRandom random, IEnumerable<SurfaceMineral>? minerals = null,
        Func<int, int>? elementCategory = null, ContactShapes? shapes = null)
    {
        Random = random;
        _shapes = shapes;
        var nodes = (minerals ?? Array.Empty<SurfaceMineral>()).OrderByDescending(m => m.Node).ToList();
        if (nodes.Count > 0 && (shapes is null || elementCategory is null))
            throw new ArgumentException("a surface with nodes needs the shapes and the element categories");
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
                Frame = ContactShapes.FirstMineralFrame + category * ContactShapes.MineralFramesPerCategory
                    + (m.Density & 0xFF) + 2,
                X = m.X << PlanetsideMap.MagShift,
                Y = m.Y << PlanetsideMap.MagShift,
            });
        }
    }

    public TfbRandom Random { get; }

    /// <summary>What's on the surface, head of the queue first.</summary>
    public IReadOnlyList<SurfaceElement> Elements => _elements;

    /// <summary>The landers on the surface or coming down, in the order they came.</summary>
    public IReadOnlyList<Lander> Landers => _landers;

    /// <summary>
    /// The nodes taken in the last frame, in order (setNodeRetrieved):
    /// picked up, or hit by a bolt. A node can be listed twice, as UQM
    /// retrieves it twice, when a lander's bolt and the lander both touch
    /// it in one frame.
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
        foreach (var _ in landers)
            Random.Next();   // the hazards' roll around each lander: lava, quakes and lightning (item 8)
        MoveElements();
        foreach (var lander in landers)
        {
            // ScrollPlanetSide: contact on the ground only, while it has crew.
            // Bolts alone touch nothing (they're the lander's own drawable),
            // so with nothing else on the surface there's nothing to test.
            if (lander.FramePhase == LanderPhase.Ground && lander.Crew > 0
                    && _elements.Any(e => e.Kind != SurfaceElementKind.Bolt))
                CheckObjectCollision(lander, null);
            lander.TextFrame();
        }
        foreach (var lander in landers)
        {
            if (lander.Phase != LanderPhase.Gone)
                continue;
            _landers.Remove(lander);
            lander.BackAboard();
        }
    }

    /// <summary>Puts an element at the head of the queue (InsertElement before GetHeadElement).</summary>
    internal void AddFirst(SurfaceElement element) => _elements.Insert(0, element);

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

    private IntersectFrame FrameOf(SurfaceElement e) => e.Kind == SurfaceElementKind.Bolt
        ? _shapes!.Lander[e.Frame]
        : _shapes!.MiscData[e.Frame];

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
                int retrieved;
                if (bolt is null)
                    retrieved = e.Amount;   // a deposit the lander touched
                else
                    retrieved = 0;          // a stun bolt: the deposit is gone, nothing picked up
                if (retrieved > 0 && !lander.PickUpMinerals(e, retrieved,
                        control.EndX - landerControl.EndX, control.EndY - landerControl.EndY))
                    continue;               // the lander is full: it stays
                // The default generator's pickupMinerals keeps nothing back.
                _taken.Add((e.Scan, e.Node));
                e.Disappearing = true;
                e.TakenBy = lander;
            }
        }
    }
}
