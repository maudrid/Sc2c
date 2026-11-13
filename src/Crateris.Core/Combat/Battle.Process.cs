// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/process.c,
// src/uqm/collide.h), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

// process.c: a frame's steps, collisions and the camera.
public sealed partial class Battle
{
    private const int MaxReduction = Units.MaxReduction;
    private const int MaxVisReduction = 2;
    private const int ReductionShift = 1;
    private const int ZoomShift = 8;
    private const int MaxZoomOut = 1 << (ZoomShift + MaxReduction - 1);
    private const int TransitionWidth = (Units.SpaceWidth << OneShift) << MaxVisReduction;
    private const int TransitionHeight = (Units.SpaceHeight << OneShift) << MaxVisReduction;
    private const ushort MaxTimeValue = Intersection.MaxTimeValue;

    private int _optMaxZoomOut;

    // collide.h's SKIP_COLLISION, CollidingElement and CollisionPossible.
    private const ElementFlags SkipCollision = ElementFlags.NonSolid | ElementFlags.Disappearing;

    /// <summary>CollidingElement: neither non-solid nor gone.</summary>
    public static bool Colliding(Element e) => (e.StateFlags & SkipCollision) == 0;

    // UQM's, and teammates' elements never (decision 2).
    private static bool CollisionPossible(Element e0, Element e1) =>
        Colliding(e0)
        && (e1.StateFlags & e0.StateFlags & ElementFlags.Collision) == 0
        && ((e1.StateFlags & e0.StateFlags & ElementFlags.IgnoreSimilar) == 0 || e1.Parent != e0.Parent)
        && (e1.MassPoints != 0 || e0.MassPoints != 0)
        && !Teammates(e0, e1);

    /// <summary>
    /// Whether two elements are teammates' (decision 2, no friendly fire):
    /// of one side, but different ships'. They pass through each other, so
    /// a ship's shots pass through its teammates and their shots, and
    /// teammates' ships through each other; point defence leaves them be,
    /// and the AI doesn't see them (it looks only at what its ship can
    /// collide with). A ship's own elements aren't teammates' (UQM's rules
    /// hold between them, IGNORE_SIMILAR's), nor are neutral ones (the
    /// planet, asteroids, crew floating, explosions). With one ship a side
    /// at a time, as in UQM, no two elements are teammates': a side's next
    /// ship warps in once the last one's elements are gone
    /// (cleanup_dead_ship), but for its crew, which are neutral.
    /// </summary>
    public static bool Teammates(Element e0, Element e1) =>
        e0.PlayerNr == e1.PlayerNr && e0.PlayerNr != NeutralPlayerNum && e0.Parent != e1.Parent;

    /// <summary>
    /// Whether contact is tested across the seam (decision 5): in an arena
    /// bigger than UQM's, collisions, the point defence's reach and the AI's
    /// plotting of them measure the shorter way round, as gravity, homing and
    /// the AI's distances do in UQM. In UQM's own arena they don't, as
    /// UQM's don't (its camera keeps the seam off the screen): elements
    /// either side of the seam pass through each other.
    /// </summary>
    public bool AcrossSeam { get; }

    /// <summary>
    /// A difference of two places (world units) as contact measures it: the
    /// shorter way round where contact is tested across the seam
    /// (<see cref="AcrossSeam"/>), as it is in UQM's arena.
    /// </summary>
    public short SeamDeltaX(int dx) => AcrossSeam ? WrapDeltaX(dx) : (short)dx;

    /// <inheritdoc cref="SeamDeltaX"/>
    public short SeamDeltaY(int dy) => AcrossSeam ? WrapDeltaY(dy) : (short)dy;

    // What to add to a place (to) to bring it within half the arena of
    // another (from), where contact is tested across the seam; nothing
    // otherwise. In world units, or display units with shift OneShift.
    private (short X, short Y) SeamShift(int fromX, int fromY, int toX, int toY, int shift = 0)
    {
        if (!AcrossSeam)
            return default;
        return (Shift(toX - fromX, Size.Width >> shift), Shift(toY - fromY, Size.Height >> shift));

        static short Shift(int d, int w) => (short)(d > w >> 1 ? -w : -d > w >> 1 ? w : 0);
    }

    private static void Move(IntersectControl c, short dx, short dy)
    {
        c.OriginX += dx;
        c.OriginY += dy;
        c.EndX += dx;
        c.EndY += dy;
    }

    // DrawablesIntersect, across the seam where contact is tested so: the
    // second control moved by the arena's size to within half the arena
    // of the first for the test, and back (its contact point too, so each
    // control's end is in its own element's places).
    private ushort Intersect(IntersectControl c0, IntersectControl c1, ushort maxTime)
    {
        var (dx, dy) = SeamShift(c0.OriginX, c0.OriginY, c1.OriginX, c1.OriginY, OneShift);
        if (dx == 0 && dy == 0)
            return Intersection.DrawablesIntersect(c0, c1, maxTime);
        Move(c1, dx, dy);
        ushort time = Intersection.DrawablesIntersect(c0, c1, maxTime);
        Move(c1, (short)-dx, (short)-dy);
        return time;
    }

    private static void InitIntersectStartPoint(Element e)
    {
        e.Intersect.OriginX = WorldToDisplay(e.Current.X);
        e.Intersect.OriginY = WorldToDisplay(e.Current.Y);
    }

    private static void InitIntersectEndPoint(Element e)
    {
        e.Intersect.EndX = WorldToDisplay(e.Next.X);
        e.Intersect.EndY = WorldToDisplay(e.Next.Y);
    }

    private static void InitIntersectFrame(Element e) => e.Intersect.Frame = ShapeOf(e.Next);

    /// <summary>SetEquFrameIndex (farray[0], frame): the big size's frame, which collisions test; null without one.</summary>
    private static IntersectFrame ShapeOf(ElementState state)
    {
        var frame = state.FrameArray == null ? default : Frame.SetEqu(state.FrameArray[0], state.Frame);
        return frame.IsNull ? null! : frame.Shape;
    }

    private void InitDisplayList()
    {
        if (Zoom == BattleZoom.Step)
        {
            ZoomOut = MaxVisReduction + 1;
            _optMaxZoomOut = MaxVisReduction;
        }
        else
        {
            ZoomOut = MaxZoomOut + (1 << ZoomShift);
            _optMaxZoomOut = MaxZoomOut;
        }
        List.Reinit();
    }

    // SetUpElement.
    private static void SetUpElement(Element e)
    {
        e.Next = e.Current;
        if (Colliding(e))
        {
            InitIntersectStartPoint(e);
            InitIntersectEndPoint(e);
            InitIntersectFrame(e);
        }
    }

    // PreProcess: dying, its own step, moving.
    private void PreProcess(Element e)
    {
        if (e.LifeSpan == 0)
        {
            if (e.Parent != null)
                Untarget(e);
            e.StateFlags |= ElementFlags.Disappearing;
            e.DeathFunc?.Invoke(e);
        }

        var stateFlags = e.StateFlags;
        if ((stateFlags & ElementFlags.Disappearing) == 0)
        {
            if ((stateFlags & ElementFlags.Appearing) != 0)
            {
                SetUpElement(e);
                if ((stateFlags & ElementFlags.PlayerShip) != 0)
                    stateFlags &= ~ElementFlags.Appearing;   // the ship's own step sets it up
            }

            if (e.PreprocessFunc != null && (stateFlags & ElementFlags.Appearing) == 0)
            {
                e.PreprocessFunc(e);
                stateFlags = e.StateFlags;
                if ((stateFlags & ElementFlags.Changing) != 0 && Colliding(e))
                    InitIntersectFrame(e);
            }

            if ((stateFlags & ElementFlags.IgnoreVelocity) == 0)
            {
                var (dx, dy) = e.Velocity.Next(1);
                if (dx != 0 || dy != 0)
                {
                    stateFlags |= ElementFlags.Changing;
                    e.Next.X += dx;
                    e.Next.Y += dy;
                }
            }

            if (Colliding(e))
                InitIntersectEndPoint(e);

            if ((stateFlags & ElementFlags.FiniteLife) != 0)
                --e.LifeSpan;
        }

        e.StateFlags = (stateFlags & ~(ElementFlags.PostProcess | ElementFlags.Collision)) | ElementFlags.PreProcess;
    }

    // PostProcess: its own step after moving, then its next state becomes its state.
    private static void PostProcess(Element e)
    {
        e.PostprocessFunc?.Invoke(e);
        e.Current = e.Next;
        if (Colliding(e))
        {
            InitIntersectStartPoint(e);
            InitIntersectEndPoint(e);
        }
        e.StateFlags = (e.StateFlags & ~(ElementFlags.PreProcess | ElementFlags.Changing | ElementFlags.Appearing))
                       | ElementFlags.PostProcess;
    }

    // CalcReduction: how far out to zoom for two ships dx and dy apart.
    private int CalcReduction(short dx, short dy) => CalcReduction(Zoom, ZoomOut, dx, dy);

    /// <summary>
    /// CalcReduction: how far out a camera at <paramref name="zoomOut"/>
    /// zooms for two ships <paramref name="dx"/> and <paramref name="dy"/>
    /// apart (each way, not negative). A player's own camera (stage 4a item
    /// 12) zooms as the battle's does.
    /// </summary>
    public static int CalcReduction(BattleZoom zoom, int zoomOut, short dx, short dy)
    {
        int nextReduction;
        if (zoom == BattleZoom.Step)
        {
            short sdx = dx, sdy = dy;
            for (nextReduction = MaxVisReduction;
                 (dx = (short)(dx << ReductionShift)) <= TransitionWidth
                 && (dy = (short)(dy << ReductionShift)) <= TransitionHeight
                 && nextReduction > 0;
                 nextReduction -= ReductionShift)
            {
            }

            // check for "real" zoom in
            if (nextReduction < zoomOut && zoomOut <= MaxVisReduction)
            {
                const int hysteresisX = 24 << OneShift, hysteresisY = 20 << OneShift;
                if (((sdx + hysteresisX) << (MaxVisReduction - nextReduction)) > TransitionWidth
                    || ((sdy + hysteresisY) << (MaxVisReduction - nextReduction)) > TransitionHeight)
                    nextReduction += ReductionShift;   // if we don't zoom in, we want to stay at next+1
            }
        }
        else
        {
            dx = (short)(dx * MaxZoomOut / (LogSpaceWidth >> 2));
            if (dx < 1 << ZoomShift)
                dx = 1 << ZoomShift;
            else if (dx > MaxZoomOut)
                dx = MaxZoomOut;

            dy = (short)(dy * MaxZoomOut / (LogSpaceHeight >> 2));
            if (dy < 1 << ZoomShift)
                dy = 1 << ZoomShift;
            else if (dy > MaxZoomOut)
                dy = MaxZoomOut;

            nextReduction = dy > dx ? dy : dx;
        }
        return (ushort)nextReduction;
    }

    // CalcView: the scroll that centres the camera, and the zoom.
    private int CalcView(short originX, short originY, short nextReduction, int shipsAlive, out short pdx, out short pdy)
    {
        // The view's centre is the arena's; its size, UQM's.
        int centreX = Size.Width >> 1, centreY = Size.Height >> 1;
        short dx = (short)((short)centreX - originX);
        short dy = (short)((short)centreY - originY);
        dx = WrapDeltaX(dx);
        dy = WrapDeltaY(dy);
        if (shipsAlive == 1)
        {
            if (dx > Units.OrgJumpX)
                dx = Units.OrgJumpX;
            else if (dx < -Units.OrgJumpX)
                dx = -Units.OrgJumpX;
            if (dy > Units.OrgJumpY)
                dy = Units.OrgJumpY;
            else if (dy < -Units.OrgJumpY)
                dy = -Units.OrgJumpY;
        }

        int viewState;
        if (ZoomOut == nextReduction)
            viewState = dx == 0 && dy == 0 ? 0 : 1;   // VIEW_STABLE, VIEW_SCROLL
        else
        {
            nextReduction = (short)ZoomTo(Zoom, ZoomOut, nextReduction, shipsAlive);
            (SpaceOrgX, SpaceOrgY) = SpaceOrg(Zoom, Size, nextReduction);
            ZoomOut = nextReduction;
            viewState = 2;   // VIEW_CHANGE
        }
        // MoveGalaxy scrolls the stars: drawing, the client's.
        pdx = dx;
        pdy = dy;
        return viewState;
    }

    /// <summary>CalcView's zoom: the smooth zoom with one ship followed zooms in by at most an eighth a frame.</summary>
    public static int ZoomTo(BattleZoom zoom, int zoomOut, int nextReduction, int shipsAlive)
    {
        const int zoomJump = (1 << ZoomShift) >> 3;
        if (zoom == BattleZoom.Smooth && shipsAlive == 1 && zoomOut > nextReduction && zoomOut <= MaxZoomOut
            && zoomOut - nextReduction > zoomJump)
            nextReduction = zoomOut - zoomJump;
        return nextReduction;
    }

    /// <summary>CalcView's SpaceOrg: the view's corner at a zoom, round the arena's centre (the smooth zoom's on a whole pixel, to reduce the jitter).</summary>
    public static (short X, short Y) SpaceOrg(BattleZoom zoom, ArenaSize size, int reduction)
    {
        int centreX = size.Width >> 1, centreY = size.Height >> 1;
        return zoom == BattleZoom.Step
            ? ((short)(centreX - (LogSpaceWidth >> (MaxReduction + 1 - reduction))),
                (short)(centreY - (LogSpaceHeight >> (MaxReduction + 1 - reduction))))
            : (DisplayAlign(centreX - LogSpaceWidth * reduction / (MaxZoomOut << 2)),
                DisplayAlign(centreY - LogSpaceHeight * reduction / (MaxZoomOut << 2)));
    }

    /// <summary>The camera the battle was last drawn from (stage 4a item 12: sent to the players), with the frames and scroll so far.</summary>
    public BattleCamera Camera => new(ViewState, ScrollX, ScrollY, ZoomOut, SpaceOrgX, SpaceOrgY, Zoom == BattleZoom.Step,
        Frames, ScrolledX, ScrolledY);

    /// <summary>The frames stepped so far (RedrawQueue's): a player's screen draws each once, and sees one missed.</summary>
    public int Frames { get; private set; }

    /// <summary>
    /// Every frame's scroll added up: the stars' places are InitGalaxy's
    /// moved by it, each plane's wrapped round its size, so a player's
    /// screen that missed a frame places them afresh (stage 4a item 12).
    /// </summary>
    public int ScrolledX { get; private set; }

    /// <inheritdoc cref="ScrolledX"/>
    public int ScrolledY { get; private set; }

    // ProcessCollisions: the element against those after it (from hSucc),
    // each pair's time of impact over their shapes; returns COLLISION if
    // the element collided.
    private ElementFlags ProcessCollisions(Element? hSucc, Element e, ushort minTime, ElementFlags processFlags)
    {
        Element? test;
        while ((test = hSucc) != null)
        {
            if ((test.StateFlags & processFlags) == 0)
                PreProcess(test);
            hSucc = test.Succ;

            if (test == e)
                continue;

            if (!CollisionPossible(test, e))
                continue;

            var stateFlags = e.StateFlags;
            var testStateFlags = test.StateFlags;
            ushort timeVal;
            if (((stateFlags | testStateFlags) & ElementFlags.FiniteLife) != 0
                && (((stateFlags & ElementFlags.Appearing) != 0 && e.LifeSpan > 1)
                    || ((testStateFlags & ElementFlags.Appearing) != 0 && test.LifeSpan > 1)))
                timeVal = 0;
            else
            {
                while ((timeVal = Intersect(e.Intersect, test.Intersect, minTime)) == 1
                       && ((stateFlags | testStateFlags) & ElementFlags.FiniteLife) == 0)
                {
                    if ((stateFlags & ElementFlags.Collision) != 0)
                    {
                        InitIntersectEndPoint(test);
                        test.Intersect.OriginX = test.Intersect.EndX;
                        test.Intersect.OriginY = test.Intersect.EndY;
                        timeVal = Intersect(e.Intersect, test.Intersect, 1);
                        InitIntersectStartPoint(test);
                    }

                    if (timeVal == 1)
                    {
                        Frame curFrame = e.Current.Frame, nextFrame = e.Next.Frame;
                        Frame testCurFrame = test.Current.Frame, testNextFrame = test.Next.Frame;
                        if (nextFrame == curFrame && testNextFrame == testCurFrame)
                        {
                            if ((testStateFlags & ElementFlags.Appearing) != 0)
                            {
                                DoDamage(test, (short)test.HitPoints);
                                if (test.Parent != null)
                                    Untarget(test);
                                test.StateFlags |= ElementFlags.Collision | ElementFlags.Disappearing;
                                test.DeathFunc?.Invoke(test);
                            }
                            if ((stateFlags & ElementFlags.Appearing) != 0)
                            {
                                DoDamage(e, (short)e.HitPoints);
                                if (e.Parent != null)
                                    Untarget(e);
                                e.StateFlags |= ElementFlags.Collision | ElementFlags.Disappearing;
                                e.DeathFunc?.Invoke(e);
                                return ElementFlags.Collision;
                            }
                            timeVal = 0;
                        }
                        else
                        {
                            if (curFrame.Index != nextFrame.Index)
                                e.Next.Frame = Frame.SetEqu(nextFrame, curFrame);
                            else if (nextFrame != curFrame)
                            {
                                e.Next.Frame = e.Current.Frame;
                                e.Next.FrameArray = e.Current.FrameArray;
                                if (e.LifeSpan > NormalLife)
                                    e.LifeSpan = NormalLife;
                            }

                            if (testCurFrame.Index != testNextFrame.Index)
                                test.Next.Frame = Frame.SetEqu(testNextFrame, testCurFrame);
                            else if (testNextFrame != testCurFrame)
                            {
                                test.Next.Frame = test.Current.Frame;
                                test.Next.FrameArray = test.Current.FrameArray;
                                if (test.LifeSpan > NormalLife)
                                    test.LifeSpan = NormalLife;
                            }

                            InitIntersectStartPoint(e);
                            InitIntersectEndPoint(e);
                            InitIntersectFrame(e);
                            if ((stateFlags & ElementFlags.PlayerShip) != 0)
                                e.Parent!.ShipFacing = e.Next.Frame.Index;

                            InitIntersectStartPoint(test);
                            InitIntersectEndPoint(test);
                            InitIntersectFrame(test);
                            if ((testStateFlags & ElementFlags.PlayerShip) != 0)
                                test.Parent!.ShipFacing = test.Next.Frame.Index;
                        }
                    }

                    if (timeVal == 0)
                    {
                        InitIntersectEndPoint(e);
                        InitIntersectEndPoint(test);
                        break;
                    }
                }
            }

            if (timeVal > 0)
            {
                (short X, short Y) savePt = (e.Intersect.EndX, e.Intersect.EndY);
                (short X, short Y) testSavePt = (test.Intersect.EndX, test.Intersect.EndY);
                InitIntersectEndPoint(e);
                InitIntersectEndPoint(test);
                if (timeVal == 1
                    || (((stateFlags & ElementFlags.Collision) != 0
                         || ProcessCollisions(hSucc, e, (ushort)(timeVal - 1), processFlags) == 0)
                        && ((testStateFlags & ElementFlags.Collision) != 0
                            || ProcessCollisions((test.StateFlags & ElementFlags.Appearing) == 0 ? e.Succ : List.Head,
                                test, (ushort)(timeVal - 1), processFlags) == 0)))
                {
                    stateFlags = e.StateFlags;
                    testStateFlags = test.StateFlags;

                    if ((testStateFlags & ElementFlags.PlayerShip) != 0)
                    {
                        test.CollisionFunc!(test, testSavePt, e, savePt);
                        e.CollisionFunc!(e, savePt, test, testSavePt);
                    }
                    else
                    {
                        e.CollisionFunc!(e, savePt, test, testSavePt);
                        test.CollisionFunc!(test, testSavePt, e, savePt);
                    }

                    if ((test.StateFlags & ElementFlags.Collision) != 0 && (testStateFlags & ElementFlags.Collision) == 0)
                    {
                        test.Intersect.OriginX = testSavePt.X;
                        test.Intersect.OriginY = testSavePt.Y;
                        test.Next.X = DisplayToWorld(testSavePt.X);
                        test.Next.Y = DisplayToWorld(testSavePt.Y);
                        InitIntersectEndPoint(test);
                    }

                    if ((e.StateFlags & ElementFlags.Collision) != 0)
                    {
                        if ((stateFlags & ElementFlags.Collision) == 0)
                        {
                            e.Intersect.OriginX = savePt.X;
                            e.Intersect.OriginY = savePt.Y;
                            e.Next.X = DisplayToWorld(savePt.X);
                            e.Next.Y = DisplayToWorld(savePt.Y);
                            InitIntersectEndPoint(e);

                            if ((stateFlags & ElementFlags.FiniteLife) == 0 && (testStateFlags & ElementFlags.FiniteLife) == 0)
                            {
                                Collide(e, test);
                                ProcessCollisions(List.Head, e, MaxTimeValue, processFlags);
                                ProcessCollisions(List.Head, test, MaxTimeValue, processFlags);
                            }
                        }
                        return ElementFlags.Collision;
                    }

                    if (!Colliding(e))
                    {
                        e.StateFlags |= ElementFlags.Collision;
                        return ElementFlags.Collision;
                    }
                }
            }
        }

        return e.StateFlags & ElementFlags.Collision;
    }

    /// <summary>
    /// The element of a side's ship the camera follows (decision 5's moving
    /// frame), or null: UQM's follows every ship, one a side; here one ship
    /// a side, the side's first in its fleet still fighting (its element a
    /// ship's, with crew, not running away; for side 0 the battle's first
    /// player still in it), or with none fighting, one whose element is
    /// still a ship's and stays one this frame (UQM's camera follows a ship
    /// exploding or warping out until cleanup_dead_ship): the one followed
    /// last frame while it is one, else the first. With none, null. With one
    /// ship a side, that ship's element, or none in the frame its element
    /// stops being a ship's (as UQM's, which then follows nothing of it).
    /// </summary>
    public Element? FollowedElement(int side)
    {
        Element? fallback = null;
        bool lastStill = false;
        foreach (var s in _fleets[side])
        {
            var e = s.Ship;
            if (e == null || (e.StateFlags & ElementFlags.PlayerShip) == 0 || LeavesThisFrame(e))
                continue;
            if (s.RaceDesc is { CrewLevel: > 0 } && e.MassPoints <= MaxShipMass + 1)
                return e;
            fallback ??= e;
            lastStill |= e == _lastFollowed[side];
        }
        return lastStill ? _lastFollowed[side] : fallback;
    }

    /// <summary>
    /// Whether the camera followed a ship's element in the last frame: it was
    /// its side's <see cref="FollowedElement"/> as the frame started, or the
    /// side had none (then each of its ships is followed, as UQM's are).
    /// </summary>
    public bool CameraFollowed(Element ship) =>
        (ship.StateFlags & ElementFlags.PlayerShip) != 0 && ship.PlayerNr is >= 0 and < NumSides
        && Followed(ship, _lastFollowed[ship.PlayerNr]);

    // The element each side's camera followed last frame.
    private readonly Element?[] _lastFollowed = new Element?[NumSides];

    // A ship's element whose explosion or warp-out ends as this frame
    // starts (cleanup_dead_ship, in PreProcess): it stops being a ship's.
    internal bool LeavesThisFrame(Element e) =>
        e.LifeSpan == 0 && e.DeathFunc == (ElementProcess)CleanupDeadShip;

    /// <summary>
    /// Whether the camera follows a ship's element now: it is its side's
    /// <see cref="FollowedElement"/>, or the side has none (then every one
    /// of its ships is followed, as UQM's are). <see cref="PreProcessQueue"/>
    /// takes each side's element once, as the frame starts, so a ship dying
    /// during the frame is followed to its end, as UQM's is, and while any
    /// ship of a side stays a ship's for the frame, one is followed.
    /// </summary>
    public bool Followed(Element ship) =>
        ship.PlayerNr is < 0 or >= NumSides || Followed(ship, FollowedElement(ship.PlayerNr));

    /// <summary>The ships the camera followed in the last frame (PreProcessQueue's ships_alive).</summary>
    internal int ShipsFollowed { get; private set; }

    private static bool Followed(Element ship, Element? followed) => followed == null || followed == ship;

    // PreProcessQueue: every element's step before moving and its
    // collisions, and the camera from the ships it follows.
    private int PreProcessQueue(out short scrollX, out short scrollY)
    {
        int sidesActive = (BattleCounter[0] != 0 ? 1 : 0) + (BattleCounter[1] != 0 ? 1 : 0);
        short minReduction, maxReduction;
        if (Zoom == BattleZoom.Step)
            minReduction = maxReduction = MaxVisReduction + 1;
        else
            minReduction = maxReduction = MaxZoomOut + (1 << ZoomShift);

        short originX = (short)(Size.Width >> 1), originY = (short)(Size.Height >> 1);
        // The ships the camera follows this frame (one a side), taken before any element steps.
        Element? followed0 = FollowedElement(0), followed1 = FollowedElement(1);
        _lastFollowed[0] = followed0;
        _lastFollowed[1] = followed1;
        int shipsAlive = 0;
        var element = List.Head;
        while (element != null)
        {
            var e = element;
            if ((e.StateFlags & ElementFlags.PreProcess) == 0)
                PreProcess(e);
            var nextElement = e.Succ;

            if (Colliding(e) && (e.StateFlags & ElementFlags.Collision) == 0)
                ProcessCollisions(nextElement, e, MaxTimeValue, ElementFlags.PreProcess);

            if ((e.StateFlags & ElementFlags.PlayerShip) != 0
                && Followed(e, e.PlayerNr == 0 ? followed0 : e.PlayerNr == 1 ? followed1 : null))
            {
                shipsAlive++;
                if (maxReduction > _optMaxZoomOut && minReduction > _optMaxZoomOut)
                {
                    originX = DisplayAlign(e.Next.X);
                    originY = DisplayAlign(e.Next.Y);
                }

                short dx = (short)(DisplayAlign(e.Next.X) - originX);
                dx = WrapDeltaX(dx);
                short dy = (short)(DisplayAlign(e.Next.Y) - originY);
                dy = WrapDeltaY(dy);

                if (sidesActive <= 2 || e.PlayerNr == 0)
                {
                    originX = DisplayAlign(originX + (dx >> 1));
                    originY = DisplayAlign(originY + (dy >> 1));
                    if (dx < 0)
                        dx = (short)-dx;
                    if (dy < 0)
                        dy = (short)-dy;
                    maxReduction = (short)CalcReduction(dx, dy);
                }
                else if (maxReduction > _optMaxZoomOut && minReduction <= _optMaxZoomOut)
                {
                    originX = DisplayAlign(originX + (dx >> 1));
                    originY = DisplayAlign(originY + (dy >> 1));
                    if (dx < 0)
                        dx = (short)-dx;
                    if (dy < 0)
                        dy = (short)-dy;
                    minReduction = (short)CalcReduction(dx, dy);
                }
                else
                {
                    if (dx < 0)
                        dx = (short)-dx;
                    if (dy < 0)
                        dy = (short)-dy;
                    short reduction = (short)CalcReduction((short)(dx << 1), (short)(dy << 1));
                    if (minReduction > _optMaxZoomOut || reduction < minReduction)
                        minReduction = reduction;
                }
            }
            element = nextElement;
        }

        if (minReduction > _optMaxZoomOut || minReduction <= maxReduction)
        {
            minReduction = maxReduction;
            if (minReduction > _optMaxZoomOut)
            {
                minReduction = (short)ZoomOut;
                if (minReduction > _optMaxZoomOut)
                    minReduction = (short)(Zoom == BattleZoom.Step ? 0 : 1 << ZoomShift);
            }
        }

        ShipsFollowed = shipsAlive;
        return CalcView(originX, originY, minReduction, shipsAlive, out scrollX, out scrollY);
    }

    // CALC_ZOOM_STUFF's index: which size the smooth zoom draws.
    private int ZoomIndex => Zoom == BattleZoom.Step ? ZoomOut
        : ZoomOut < 2 << ZoomShift ? 0 : ZoomOut < 4 << ZoomShift ? 1 : 2;

    // PostProcessQueue: elements added this frame stepped and collided,
    // the gone ones removed, the scroll added to the others, the size the
    // zoom draws picked, and each one's step after moving.
    private void PostProcessQueue(int viewState, short scrollX, short scrollY)
    {
        var element = List.Head;
        while (element != null)
        {
            var e = element;
            Element? nextElement;
            short deltaX, deltaY;
            var stateFlags = e.StateFlags;
            if ((stateFlags & ElementFlags.PreProcess) != 0)
            {
                if ((stateFlags & ElementFlags.Collision) == 0)
                    e.StateFlags &= ~ElementFlags.DefyPhysics;
                else
                    e.StateFlags &= ~ElementFlags.Collision;

                if ((stateFlags & ElementFlags.PostProcess) != 0)
                    deltaX = deltaY = 0;
                else
                {
                    deltaX = scrollX;
                    deltaY = scrollY;
                }
            }
            else
            {
                var post = e;
                do
                {
                    if ((post.StateFlags & ElementFlags.PreProcess) == 0)
                        PreProcess(post);
                    var nextPost = post.Succ;
                    if (Colliding(post) && (post.StateFlags & ElementFlags.Collision) == 0)
                        ProcessCollisions(List.Head, post, MaxTimeValue, ElementFlags.PreProcess | ElementFlags.PostProcess);
                    post = nextPost;
                } while (post != null);

                // These are new elements, already in the moved coordinates.
                scrollX = scrollY = 0;
                deltaX = deltaY = 0;
                stateFlags = e.StateFlags;
            }

            if ((stateFlags & ElementFlags.Disappearing) != 0)
            {
                nextElement = e.Succ;
                List.Remove(e);
                List.Free(e);
            }
            else
            {
                var objType = e.Prim;
                // A line is drawn from its place to its next (the display
                // list's line.first and line.second); PostProcess then makes
                // the next its place, so the start is kept here for whoever
                // draws it (stage 4a item 11: the battle sent to the players).
                if (objType == PrimType.Line)
                    (e.LineStartX, e.LineStartY) = (e.Current.X, e.Current.Y);
                if (viewState != 0 || (stateFlags & (ElementFlags.Appearing | ElementFlags.Changing)) != 0)
                {
                    short nextX, nextY;
                    if (objType == PrimType.Line)
                    {
                        short dx = (short)(e.Next.X - e.Current.X);
                        short dy = (short)(e.Next.Y - e.Current.Y);
                        nextX = (short)WrapX(e.Current.X + deltaX);
                        nextY = (short)WrapY(e.Current.Y + deltaY);
                        (e.LineStartX, e.LineStartY) = (nextX, nextY);
                        nextX += dx;
                        nextY += dy;
                    }
                    else
                    {
                        nextX = (short)WrapX(e.Next.X + deltaX);
                        nextY = (short)WrapY(e.Next.Y + deltaY);
                        if ((objType == PrimType.Stamp || objType == PrimType.StampFill)
                            && (viewState == 2 || (stateFlags & (ElementFlags.Appearing | ElementFlags.Changing)) != 0))
                            e.Next.Frame = Frame.SetEqu(e.Next.FrameArray![ZoomIndex], e.Next.Frame);
                    }
                    e.Next.X = nextX;
                    e.Next.Y = nextY;
                }

                PostProcess(e);
                nextElement = e.Succ;
            }

            element = nextElement;
        }
    }

    // RedrawQueue, without the drawing.
    private void RedrawQueue()
    {
        int viewState = PreProcessQueue(out short scrollX, out short scrollY);
        ScrollX = scrollX;
        ScrollY = scrollY;
        ViewState = viewState;
        Frames++;
        ScrolledX += scrollX;
        ScrolledY += scrollY;
        PostProcessQueue(viewState, scrollX, scrollY);
    }

    // Untarget: nothing homes in on or follows this element any more.
    internal void Untarget(Element e)
    {
        foreach (var l in List.Elements)
            if (l.Target == e)
                l.Target = null;
    }
}
