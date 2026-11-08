// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/libs/graphics/cmap.c:
// XFormPLUT, XFormColorMap_step, finish_colormap_xform), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// Colour maps fading to a colour table entry over time, as UQM's cmap.c
/// fades them (XFormColorMap): each step blends the map from its colours
/// when the fade started toward the entry's first map, and at the end
/// sets the whole entry. A fade started on a map already fading finishes
/// that one first. Times are in ticks (ONE_SECOND 840) of the caller's
/// own clock.
/// </summary>
public sealed class ColorFades
{
    /// <summary>MAX_XFORMS: fades at once; a new one past it finishes the last.</summary>
    public const int MaxFades = 16;

    private sealed class Fade
    {
        public int Map = -1;
        public byte[] Entry = Array.Empty<byte>();
        public Rgb[] Old = Array.Empty<Rgb>();
        public int Ticks;
        public long End;
    }

    private readonly Fade[] _fades = Enumerable.Range(0, MaxFades).Select(_ => new Fade()).ToArray();
    private int _highest = -1;

    public ColorFades(ColorMaps maps) => Maps = maps;

    /// <summary>The colour maps the fades change.</summary>
    public ColorMaps Maps { get; }

    /// <summary>Whether any fade is running.</summary>
    public bool Running => _highest >= 0;

    private void Finish(int which)
    {
        Maps.Set(_fades[which].Entry);
        _fades[which].Map = -1;
        if (which == _highest)
        {
            do
                --which;
            while (which >= 0 && _fades[which].Map == -1);
            _highest = which;
        }
    }

    /// <summary>XFormPLUT: fade to <paramref name="entry"/> (a colour table entry) over <paramref name="ticks"/> from <paramref name="now"/>.</summary>
    public void Start(byte[] entry, int ticks, long now)
    {
        int index = entry[0];
        int firstFree = -1, x;
        for (x = 0; x <= _highest && _fades[x].Map != index; ++x)
            if (firstFree == -1 && _fades[x].Map == -1)
                firstFree = x;
        if (x < MaxFades && _fades[x].Map == index)
            Finish(x);
        else if (firstFree >= 0)
            x = firstFree;
        else if (x >= MaxFades)
        {
            x = _highest;
            Finish(x);
        }
        var old = Maps[index];
        if (old == null)
            return;   // UQM: "no current map"
        var f = _fades[x];
        if (x > _highest)
            _highest = x;
        f.Map = index;
        f.Entry = entry;
        f.Old = (Rgb[])old.Clone();
        f.Ticks = Math.Max(ticks, 0);
        f.End = now + f.Ticks;
    }

    private static byte Blend(byte c1, byte c2, int weight) => (byte)(c1 + (c2 - c1) * weight / 0x10000);

    /// <summary>XFormColorMap_step: the maps as they are at <paramref name="now"/>; whether any changed (so the picture is drawn again).</summary>
    public bool Step(long now)
    {
        bool changed = false;
        for (int x = 0; x <= _highest; ++x)
        {
            var f = _fades[x];
            if (f.Map < 0)
                continue;
            int left = (int)(f.End - now);
            if (Maps[f.Map] == null)
            {
                Finish(x);
                continue;
            }
            if (left > 0)
            {
                int frac = (f.Ticks - left) * 0x10000 / f.Ticks;
                var map = new Rgb[256];
                for (int i = 0, at = 2; i < 256; ++i, at += 3)
                    map[i] = new Rgb(Blend(f.Old[i].R, f.Entry[at], frac), Blend(f.Old[i].G, f.Entry[at + 1], frac),
                        Blend(f.Old[i].B, f.Entry[at + 2], frac));
                Maps.Replace(f.Map, map);
            }
            else
                Finish(x);
            changed = true;
        }
        return changed;
    }
}
