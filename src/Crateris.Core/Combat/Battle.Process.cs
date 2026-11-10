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

    private static bool CollisionPossible(Element e0, Element e1) =>
        Colliding(e0)
        && (e1.StateFlags & e0.StateFlags & ElementFlags.Collision) == 0
        && ((e1.StateFlags & e0.StateFlags & ElementFlags.IgnoreSimilar) == 0 || e1.Parent != e0.Parent)
        && (e1.MassPoints != 0 || e0.MassPoints != 0);

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
    private int CalcReduction(short dx, short dy)
    {
        int nextReduction;
        if (Zoom == BattleZoom.Step)
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
            if (nextReduction < ZoomOut && ZoomOut <= MaxVisReduction)
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
        short dx = (short)((short)(LogSpaceWidth >> 1) - originX);
        short dy = (short)((short)(LogSpaceHeight >> 1) - originY);
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
            if (Zoom == BattleZoom.Step)
            {
                SpaceOrgX = (short)((LogSpaceWidth >> 1) - (LogSpaceWidth >> (MaxReduction + 1 - nextReduction)));
                SpaceOrgY = (short)((LogSpaceHeight >> 1) - (LogSpaceHeight >> (MaxReduction + 1 - nextReduction)));
            }
            else
            {
                const int zoomJump = (1 << ZoomShift) >> 3;
                if (shipsAlive == 1 && ZoomOut > nextReduction && ZoomOut <= MaxZoomOut
                    && ZoomOut - nextReduction > zoomJump)
                    nextReduction = (short)(ZoomOut - zoomJump);
                // Always on a whole pixel, to reduce the jitter.
                SpaceOrgX = DisplayAlign((LogSpaceWidth >> 1) - LogSpaceWidth * nextReduction / (MaxZoomOut << 2));
                SpaceOrgY = DisplayAlign((LogSpaceHeight >> 1) - LogSpaceHeight * nextReduction / (MaxZoomOut << 2));
            }
            ZoomOut = nextReduction;
            viewState = 2;   // VIEW_CHANGE
        }
        // MoveGalaxy scrolls the stars: drawing, the client's.
        pdx = dx;
        pdy = dy;
        return viewState;
    }

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
                while ((timeVal = Intersection.DrawablesIntersect(e.Intersect, test.Intersect, minTime)) == 1
                       && ((stateFlags | testStateFlags) & ElementFlags.FiniteLife) == 0)
                {
                    if ((stateFlags & ElementFlags.Collision) != 0)
                    {
                        InitIntersectEndPoint(test);
                        test.Intersect.OriginX = test.Intersect.EndX;
                        test.Intersect.OriginY = test.Intersect.EndY;
                        timeVal = Intersection.DrawablesIntersect(e.Intersect, test.Intersect, 1);
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

    // PreProcessQueue: every element's step before moving and its
    // collisions, and the camera from the ships.
    private int PreProcessQueue(out short scrollX, out short scrollY)
    {
        int sidesActive = (BattleCounter[0] != 0 ? 1 : 0) + (BattleCounter[1] != 0 ? 1 : 0);
        short minReduction, maxReduction;
        if (Zoom == BattleZoom.Step)
            minReduction = maxReduction = MaxVisReduction + 1;
        else
            minReduction = maxReduction = MaxZoomOut + (1 << ZoomShift);

        short originX = LogSpaceWidth >> 1, originY = LogSpaceHeight >> 1;
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

            if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
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
                if (viewState != 0 || (stateFlags & (ElementFlags.Appearing | ElementFlags.Changing)) != 0)
                {
                    short nextX, nextY;
                    if (objType == PrimType.Line)
                    {
                        short dx = (short)(e.Next.X - e.Current.X);
                        short dy = (short)(e.Next.Y - e.Current.Y);
                        nextX = (short)WrapX(e.Current.X + deltaX);
                        nextY = (short)WrapY(e.Current.Y + deltaY);
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
