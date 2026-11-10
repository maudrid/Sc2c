// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/element.h, units.h,
// src/libs/graphics/pixmap.c: the frame indexing), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Uqm;

namespace Crateris.Core.Combat;

/// <summary>ELEMENT_FLAGS (element.h): what an element is and where it is in the frame's steps.</summary>
[Flags]
public enum ElementFlags : ushort
{
    None = 0,
    /// <summary>A ship a side flies (not a shot, crew, an asteroid...).</summary>
    PlayerShip = 1 << 2,
    /// <summary>New this frame: set up on its first step.</summary>
    Appearing = 1 << 3,
    /// <summary>Gone: removed from the list at the end of the frame.</summary>
    Disappearing = 1 << 4,
    /// <summary>Its frame or place changed.</summary>
    Changing = 1 << 5,
    /// <summary>Touches nothing.</summary>
    NonSolid = 1 << 6,
    /// <summary>Has collided this frame.</summary>
    Collision = 1 << 7,
    /// <summary>Passes through elements of the same parent with this flag too (a ship's own shots).</summary>
    IgnoreSimilar = 1 << 8,
    DefyPhysics = 1 << 9,
    /// <summary>Its life counts down each frame.</summary>
    FiniteLife = 1 << 10,
    /// <summary>Stepped before moving this frame.</summary>
    PreProcess = 1 << 11,
    /// <summary>Stepped after moving this frame.</summary>
    PostProcess = 1 << 12,
    IgnoreVelocity = 1 << 13,
    /// <summary>Crew floating in space.</summary>
    CrewObject = 1 << 14,
    BackgroundObject = 1 << 15,
}

/// <summary>An element's display primitive's type (GRAPHICS_PRIM, prim.h); <see cref="None"/> is NO_PRIM, not drawn.</summary>
public enum PrimType : byte
{
    Point = 0,
    Stamp,
    StampFill,
    Line,
    Text,
    Rect,
    RectFill,
    /// <summary>NO_PRIM (NUM_PRIMS): not drawn, and to TrackShip cloaked.</summary>
    None,
}

/// <summary>
/// A sprite sheet a battle draws or tests (a DRAWABLE): its resource, how
/// many frames it has, and for the big size (the one collisions test,
/// InitIntersectFrame's farray[0]) each frame's shape.
/// </summary>
public sealed class Sheet
{
    public Sheet(string id, int count, IReadOnlyList<IntersectFrame>? shapes = null)
    {
        if (count < 1)
            throw new ArgumentException($"{id} has no frames");
        if (shapes != null && shapes.Count != count)
            throw new ArgumentException($"{id} has {count} frames but {shapes.Count} shapes");
        Id = id;
        Count = count;
        Shapes = shapes;
    }

    public string Id { get; }
    public int Count { get; }
    public IReadOnlyList<IntersectFrame>? Shapes { get; }

    public override string ToString() => Id;
}

/// <summary>
/// A FRAME: a sheet and an index in it, compared as UQM compares the
/// pointers (the same sheet and index); <c>default</c> is the null frame.
/// The indexing is pixmap.c's.
/// </summary>
public readonly record struct Frame(Sheet? Sheet, ushort Index)
{
    public bool IsNull => Sheet is null;

    /// <summary>GetFrameCount: 0 for the null frame.</summary>
    public int Count => Sheet?.Count ?? 0;

    /// <summary>SetAbsFrameIndex: the index taken round the sheet's frames.</summary>
    public Frame SetAbs(int index) => Sheet is null ? this : new Frame(Sheet, (ushort)((ushort)index % Sheet.Count));

    /// <summary>SetRelFrameIndex.</summary>
    public Frame SetRel(int offset)
    {
        if (Sheet is null)
            return this;
        short offs = (short)offset;
        if (offs < 0)
            while ((offs = (short)(offs + Sheet.Count)) < 0)
            {
            }
        return new Frame(Sheet, (ushort)((Index + offs) % Sheet.Count));
    }

    /// <summary>IncFrameIndex: the next frame, the first after the last.</summary>
    public Frame Inc() => Sheet is null ? this : new Frame(Sheet, (ushort)(Index < Sheet.Count - 1 ? Index + 1 : 0));

    /// <summary>DecFrameIndex: the frame before, the last before the first.</summary>
    public Frame Dec() => Sheet is null ? this : new Frame(Sheet, (ushort)(Index > 0 ? Index - 1 : Sheet.Count - 1));

    /// <summary>SetEquFrameIndex: <paramref name="dst"/>'s sheet at <paramref name="src"/>'s index; null if either is.</summary>
    public static Frame SetEqu(Frame dst, Frame src) => dst.IsNull || src.IsNull ? default : dst.SetAbs(src.Index);

    /// <summary>The frame's shape, for a sheet collisions test.</summary>
    public IntersectFrame Shape => Sheet?.Shapes?[Index]
        ?? throw new InvalidOperationException($"{Sheet?.Id ?? "a null frame"} frame {Index} has no shape to test");

    public override string ToString() => Sheet is null ? "null" : $"{Sheet.Id}[{Index}]";
}

/// <summary>
/// A farray: a sheet's three sizes, big, medium and small (the zoom picks
/// one, PostProcessQueue). stars_in_space has the one size: its elements
/// (crew, lasers, ion trails) are points and lines, never stamps.
/// </summary>
public sealed class Views
{
    private readonly Sheet[] _sheets;

    public Views(params Sheet[] sheets)
    {
        if (sheets.Length is not (1 or ShipGraphics.Views))
            throw new ArgumentException("a sheet comes in one size or three");
        _sheets = sheets;
    }

    /// <summary>farray[zoom]: the size's first frame.</summary>
    public Frame this[int zoom] => zoom < _sheets.Length ? new Frame(_sheets[zoom], 0)
        : throw new InvalidOperationException($"{_sheets[0].Id} has one size");

    public int Sizes => _sheets.Length;
}

/// <summary>STATE: a place (world units, which the camera's scroll moves) and an image.</summary>
public struct ElementState
{
    public short X, Y;
    public Frame Frame;
    public Views? FrameArray;
}

/// <summary>UQM's ELEMENT callbacks: before or after moving, and on dying.</summary>
public delegate void ElementProcess(Element element);

/// <summary>UQM's collision callback: each element and where it was when they touched (display units).</summary>
public delegate void ElementCollision(Element element0, (short X, short Y) at0, Element element1, (short X, short Y) at1);

/// <summary>
/// An element of a battle (element.h's ELEMENT): a ship, a shot, an
/// asteroid, the planet, crew floating, an explosion. Its fields are UQM's,
/// with UQM's types: the unions are kept as aliases (<see cref="HitPoints"/>
/// is <see cref="CrewLevel"/>, <see cref="BlastOffset"/> and
/// <see cref="NextTurn"/> are <see cref="ThrustWait"/>). Elements live in
/// a <see cref="DisplayList"/>'s slots and are reused once freed, as UQM's
/// handles are: a stale <see cref="Target"/> may point to a new element.
/// </summary>
public sealed class Element
{
    internal Element(int slot) => Slot = slot;

    /// <summary>The element's slot in its list (UQM's handle), 1 to the list's size.</summary>
    public int Slot { get; }

    internal Element? Pred, Succ;
    internal bool InList;

    public ElementProcess? PreprocessFunc, PostprocessFunc, DeathFunc;
    public ElementCollision? CollisionFunc;

    /// <summary>The side the element is: 0 or 1, or -1 neutral (NEUTRAL_PLAYER_NUM).</summary>
    public short PlayerNr;
    public ElementFlags StateFlags;
    public ushort LifeSpan;

    /// <summary>A ship's crew (crew_level), or anything else's hit points.</summary>
    public ushort CrewLevel;
    public byte MassPoints, TurnWait, ThrustWait, ColorCycleIndex;
    public Velocity Velocity;
    public readonly IntersectControl Intersect = new();
    public PrimType Prim;

    /// <summary>The primitive's colour as UQM's MAKE_RGB15 gives it (red, green, blue, 5 bits each), 0 black.</summary>
    public int PrimColor;
    public ElementState Current, Next;

    /// <summary>The ship the element belongs to (pParent), or none.</summary>
    public Starship? Parent;

    /// <summary>The element it homes in on or follows (hTarget), or none.</summary>
    public Element? Target;

    /// <summary>hit_points: <see cref="CrewLevel"/>'s other name.</summary>
    public ushort HitPoints
    {
        get => CrewLevel;
        set => CrewLevel = value;
    }

    /// <summary>blast_offset: <see cref="ThrustWait"/>'s other name, for a shot.</summary>
    public byte BlastOffset
    {
        get => ThrustWait;
        set => ThrustWait = value;
    }

    /// <summary>next_turn: <see cref="ThrustWait"/>'s other name, for an animation.</summary>
    public byte NextTurn
    {
        get => ThrustWait;
        set => ThrustWait = value;
    }

    /// <summary>The element after this one in its list, or none.</summary>
    public Element? Successor => Succ;

    /// <summary>AllocElement's memset: every field cleared, not drawn.</summary>
    internal void Clear()
    {
        Pred = Succ = null;
        PreprocessFunc = PostprocessFunc = DeathFunc = null;
        CollisionFunc = null;
        PlayerNr = 0;
        StateFlags = ElementFlags.None;
        LifeSpan = 0;
        CrewLevel = 0;
        MassPoints = TurnWait = ThrustWait = ColorCycleIndex = 0;
        Velocity = default;
        Intersect.Clear();
        Prim = PrimType.None;
        Current = Next = default;
        Parent = null;
        Target = null;
    }

    public override string ToString() => $"element {Slot} ({StateFlags}, {Current.Frame})";
}
