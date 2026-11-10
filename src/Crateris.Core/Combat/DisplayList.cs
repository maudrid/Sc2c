// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/displist.c: the queue
// with its table of links, QUEUE_TABLE; src/uqm/process.c: AllocElement,
// FreeElement; src/uqm/element.h: MAX_DISPLAY_ELEMENTS), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>
/// The battle's display list (disp_q): every element in the order the
/// frame steps them, which decides which of two collisions happens first.
/// It holds at most <see cref="Capacity"/> elements; allocating one more
/// fails (null), and the caller does without, as UQM's do. Freed slots are
/// reused last freed first, as UQM's free list hands them out.
/// </summary>
public sealed class DisplayList
{
    /// <summary>MAX_DISPLAY_ELEMENTS.</summary>
    public const int MaxDisplayElements = 150;

    private readonly Element[] _slots;
    private readonly Stack<Element> _free = new();

    public DisplayList(int capacity = MaxDisplayElements)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _slots = new Element[capacity];
        for (int i = 0; i < capacity; ++i)
            _slots[i] = new Element(i + 1);
        Reinit();
    }

    public int Capacity => _slots.Length;

    /// <summary>The first element (GetHeadElement), or none.</summary>
    public Element? Head { get; private set; }

    /// <summary>The last element (GetTailElement), or none.</summary>
    public Element? Tail { get; private set; }

    /// <summary>The elements from the head.</summary>
    public IEnumerable<Element> Elements
    {
        get
        {
            for (var e = Head; e != null; e = e.Succ)
                yield return e;
        }
    }

    public int Count => Elements.Count();

    /// <summary>Whether every slot is taken (<see cref="Alloc"/> would fail).</summary>
    public bool Full => _free.Count == 0;

    /// <summary>ReinitQueue: empty, every slot free, the first slot handed out first.</summary>
    public void Reinit()
    {
        Head = Tail = null;
        _free.Clear();
        for (int i = _slots.Length; i > 0; --i)
        {
            _slots[i - 1].InList = false;
            _free.Push(_slots[i - 1]);
        }
    }

    /// <summary>AllocElement: a cleared element not yet in the list, or null when every slot is taken.</summary>
    public Element? Alloc()
    {
        if (_free.Count == 0)
            return null;
        var e = _free.Pop();
        e.Clear();
        return e;
    }

    /// <summary>FreeElement: the slot is free again.</summary>
    public void Free(Element e)
    {
        if (e.InList)
            throw new InvalidOperationException($"{e} is freed while in the list");
        _free.Push(e);
    }

    /// <summary>PutElement (PutQueue): at the tail.</summary>
    public void Put(Element e)
    {
        CheckOut(e);
        if (Head == null)
            Head = e;
        else
            Tail!.Succ = e;
        e.Pred = Tail;
        e.Succ = null;
        Tail = e;
        e.InList = true;
    }

    /// <summary>InsertElement (InsertQueue): before <paramref name="before"/>, or at the tail if none.</summary>
    public void Insert(Element e, Element? before)
    {
        if (before == null)
        {
            Put(e);
            return;
        }
        CheckOut(e);
        e.Pred = before.Pred;
        before.Pred = e;
        e.Succ = before;
        if (Head == before)
            Head = e;
        else
            e.Pred!.Succ = e;
        e.InList = true;
    }

    /// <summary>RemoveElement (RemoveQueue). The element keeps its links, as UQM's does, until freed.</summary>
    public void Remove(Element e)
    {
        if (!e.InList)
            throw new InvalidOperationException($"{e} isn't in the list");
        if (Head == e)
            Head = e.Succ;
        else
            e.Pred!.Succ = e.Succ;
        if (Tail == e)
            Tail = e.Pred;
        else
            e.Succ!.Pred = e.Pred;
        e.InList = false;
    }

    private static void CheckOut(Element e)
    {
        if (e.InList)
            throw new InvalidOperationException($"{e} is already in the list");
    }
}
