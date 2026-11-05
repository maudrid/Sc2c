// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Planetside;
using Crateris.Core.World;

namespace Crateris.Core.Net;

// A surface in a snapshot: none (0), in full with its world (1), or as a
// delta from the one the receiver has (2). Landers are matched by player,
// elements by their ids; each as a mask of the numbers that changed and
// the differences, as ships are.
public static partial class SnapshotCodec
{
    private const byte SurfaceNone = 0, SurfaceFull = 1, SurfaceDelta = 2;

    // A lander's numbers.
    private const int LX = 0, LY = 1, LFacing = 2, LHeight = 3, LPhase = 4, LCrew = 5, LFlash = 6, LDrawn = 7,
        LDamage = 8, LExplosion = 9, LMinerals = 10, LBio = 11, LTextFrames = 12, LTextAmount = 13,
        LTextElement = 14, LTextX = 15, LTextY = 16, LHasText = 17, LCrewTaken = 18;
    private const int LanderNumbers = 19;
    private const ulong AllLanderFields = (1UL << (LanderNumbers + 1)) - 1;   // and the player's id

    // An element's numbers.
    private const int EKind = 0, EX = 1, EY = 2, ESheet = 3, EFrame = 4, ELifeKind = 5, EFill = 6, ELife = 7, ECycle = 8;
    private const int ElementNumbers = 9;
    private const ulong AllElementFields = (1UL << ElementNumbers) - 1;

    /// <summary>
    /// The surface a player sees in a snapshot: the one its ship's lander is
    /// on, if the snapshot has it.
    /// </summary>
    public static SurfaceSnapshot? SurfaceSeen(WorldSnapshot? snapshot, string? viewer)
    {
        if (snapshot?.Surfaces is not { Count: > 0 } surfaces || viewer is null)
            return null;
        ShipSnapshot? ship = null;
        foreach (var s in snapshot.Ships)
        {
            if (s.Id == viewer)
                ship = s;
        }
        if (ship is not { Activity: Activity.Planetside, SystemStar: int star, OrbitPlanet: int planet })
            return null;
        foreach (var s in surfaces)
        {
            if (s.Star == star && s.Planet == planet && s.Moon == ship.OrbitMoon)
                return s;
        }
        return null;
    }

    private static void WriteSurface(WireWriter w, SurfaceSnapshot? surface, SurfaceSnapshot? old)
    {
        if (surface is null)
        {
            w.Byte(SurfaceNone);
            return;
        }
        if (old is not null && (old.Star, old.Planet, old.Moon) != (surface.Star, surface.Planet, surface.Moon))
            old = null;
        if (old is null)
        {
            w.Byte(SurfaceFull);
            w.Int(surface.Star);
            w.Int(surface.Planet);
            w.Int(Optional(surface.Moon));
        }
        else
            w.Byte(SurfaceDelta);

        Span<long> now = stackalloc long[LanderNumbers];
        Span<long> was = stackalloc long[LanderNumbers];
        w.UInt((ulong)surface.Landers.Count);
        foreach (var l in surface.Landers)
        {
            int at = -1;
            for (int i = 0; old is not null && i < old.Landers.Count; ++i)
            {
                if (old.Landers[i].PlayerId == l.PlayerId)
                    at = i;
            }
            w.UInt((ulong)(at + 1));
            ToNumbers(l, now);
            if (at < 0)
                was.Clear();
            else
                ToNumbers(old!.Landers[at], was);
            ulong mask = at < 0 ? AllLanderFields : 0;
            for (int f = 0; at >= 0 && f < LanderNumbers; ++f)
            {
                if (now[f] != was[f])
                    mask |= 1UL << f;
            }
            w.UInt(mask);
            for (int f = 0; f < LanderNumbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    w.Int(now[f] - was[f]);
            }
            if (at < 0)
                w.String(l.PlayerId);
        }

        var taken = surface.DepositsTaken ?? Array.Empty<int>();
        w.UInt((ulong)taken.Count);
        foreach (int node in taken)
            w.UInt((ulong)node);

        Span<long> enow = stackalloc long[ElementNumbers];
        Span<long> ewas = stackalloc long[ElementNumbers];
        w.UInt((ulong)surface.Elements.Count);
        int lastId = 0, cursor = 0;
        foreach (var e in surface.Elements)
        {
            w.Int((long)e.Id - lastId);
            lastId = e.Id;
            var prev = old is null ? null : Find(old.Elements, e.Id, ref cursor);
            ToNumbers(e, enow);
            if (prev is null)
                ewas.Clear();
            else
                ToNumbers(prev, ewas);
            ulong mask = prev is null ? AllElementFields : 0;
            for (int f = 0; prev is not null && f < ElementNumbers; ++f)
            {
                if (enow[f] != ewas[f])
                    mask |= 1UL << f;
            }
            w.UInt(mask);
            for (int f = 0; f < ElementNumbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    w.Int(enow[f] - ewas[f]);
            }
        }
    }

    private static IReadOnlyList<SurfaceSnapshot>? ReadSurface(ref WireReader r, SurfaceSnapshot? old)
    {
        byte mode = r.Byte();
        if (mode == SurfaceNone)
            return null;
        int star, planet;
        int? moon;
        if (mode == SurfaceFull)
        {
            star = ToInt(r.Int());
            planet = ToInt(r.Int());
            moon = ToOptional(r.Int());
            old = null;
        }
        else if (mode == SurfaceDelta && old is not null)
            (star, planet, moon) = (old.Star, old.Planet, old.Moon);
        else
            throw new InvalidDataException("A surface's delta has no surface to go from.");

        var landers = new LanderSnapshot[Count(r.UInt(), 64)];
        Span<long> v = stackalloc long[LanderNumbers];
        for (int i = 0; i < landers.Length; ++i)
        {
            ulong at = r.UInt();
            LanderSnapshot? prev = null;
            if (at != 0)
            {
                if (old is null || at > (ulong)old.Landers.Count)
                    throw new InvalidDataException("A lander's base isn't in the base surface.");
                prev = old.Landers[(int)at - 1];
            }
            ulong mask = r.UInt();
            if (prev is null ? mask != AllLanderFields : (mask & ~((1UL << LanderNumbers) - 1)) != 0)
                throw new InvalidDataException("Bad lander fields.");
            if (prev is not null && mask == 0)
            {
                landers[i] = prev;
                continue;
            }
            if (prev is null)
                v.Clear();
            else
                ToNumbers(prev, v);
            for (int f = 0; f < LanderNumbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    v[f] = Add(v[f], r.Int());
            }
            string id = prev?.PlayerId ?? r.String() ?? throw new InvalidDataException("A lander has no player.");
            landers[i] = LanderFromNumbers(v, id);
        }

        var taken = new int[Count(r.UInt(), 64)];
        for (int i = 0; i < taken.Length; ++i)
            taken[i] = Count(r.UInt(), 1023);

        var elements = new SurfaceElementSnapshot[Count(r.UInt(), 4096)];
        Span<long> ev = stackalloc long[ElementNumbers];
        long lastId = 0;
        int cursor = 0;
        for (int i = 0; i < elements.Length; ++i)
        {
            int id = ToInt(Add(lastId, r.Int()));
            lastId = id;
            var prev = old is null ? null : Find(old.Elements, id, ref cursor);
            ulong mask = r.UInt();
            if (prev is null ? mask != AllElementFields : (mask & ~AllElementFields) != 0)
                throw new InvalidDataException("Bad surface element fields.");
            if (prev is not null && mask == 0)
            {
                elements[i] = prev;
                continue;
            }
            if (prev is null)
                ev.Clear();
            else
                ToNumbers(prev, ev);
            for (int f = 0; f < ElementNumbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    ev[f] = Add(ev[f], r.Int());
            }
            elements[i] = ElementFromNumbers(ev, id);
        }
        for (int i = 1; i < landers.Length; ++i)
        {
            for (int j = 0; j < i; ++j)
            {
                if (landers[i].PlayerId == landers[j].PlayerId)
                    throw new InvalidDataException($"Two landers are {landers[i].PlayerId}'s.");
            }
        }
        return new[] { new SurfaceSnapshot(star, planet, moon, landers, elements, taken) };
    }

    // An element by its id, looking on from where the last one was found:
    // the elements keep their order from frame to frame, so this is quick.
    private static SurfaceElementSnapshot? Find(IReadOnlyList<SurfaceElementSnapshot> elements, int id, ref int cursor)
    {
        int n = elements.Count;
        for (int k = 0; k < n; ++k)
        {
            int i = (cursor + k) % n;
            if (elements[i].Id == id)
            {
                cursor = i + 1;
                return elements[i];
            }
        }
        return null;
    }

    private static void ToNumbers(LanderSnapshot l, Span<long> v)
    {
        v[LX] = l.X;
        v[LY] = l.Y;
        v[LFacing] = l.Facing;
        v[LHeight] = l.Height;
        v[LPhase] = (int)l.Phase;
        v[LCrew] = l.Crew;
        v[LFlash] = (int)l.Flash;
        v[LDrawn] = l.Drawn ? 1 : 0;
        v[LDamage] = l.DamageIndex;
        v[LExplosion] = l.ExplosionIndex;
        v[LMinerals] = l.MineralMass;
        v[LBio] = l.BioData;
        var t = l.Text ?? default;
        v[LTextFrames] = t.Frames;
        v[LTextAmount] = t.Amount;
        v[LTextElement] = t.Element;
        v[LTextX] = t.X;
        v[LTextY] = t.Y;
        v[LHasText] = l.Text is null ? 0 : 1;
        v[LCrewTaken] = l.CrewTaken;
    }

    private static LanderSnapshot LanderFromNumbers(ReadOnlySpan<long> v, string id)
    {
        var phase = (LanderPhase)ToInt(v[LPhase]);
        var flash = (LanderFlash)ToInt(v[LFlash]);
        // What the client draws by: the flash's colour, the text's colour
        // and element name, the gauges' cells.
        if (!Enum.IsDefined(phase) || !Enum.IsDefined(flash) || v[LDrawn] is not (0 or 1) || v[LHasText] is not (0 or 1)
                || v[LFacing] is < 0 or >= Uqm.UqmMath.Facings || v[LDamage] is < 0 or > Lander.DamageCycle || v[LCrewTaken] is < 0 or > Core.World.ShipHold.LanderCrew
                || v[LCrew] < 0 || v[LCrew] > v[LCrewTaken]
                || (v[LHasText] == 1 && (v[LTextFrames] is < 0 or >= Lander.TextFrames
                    || v[LTextElement] is < 0 or >= Core.Content.GameStrings.ElementsCount)))
            throw new InvalidDataException("Bad lander.");
        MineralText? text = v[LHasText] == 1
            ? new MineralText(ToInt(v[LTextFrames]), ToInt(v[LTextAmount]), ToInt(v[LTextElement]), ToInt(v[LTextX]), ToInt(v[LTextY]))
            : null;
        return new LanderSnapshot(id, ToInt(v[LX]), ToInt(v[LY]), ToInt(v[LFacing]), ToInt(v[LHeight]), phase,
            ToInt(v[LCrew]), flash, v[LDrawn] == 1, ToInt(v[LDamage]), ToInt(v[LExplosion]), ToInt(v[LMinerals]),
            ToInt(v[LBio]), text, ToInt(v[LCrewTaken]));
    }

    private static void ToNumbers(SurfaceElementSnapshot e, Span<long> v)
    {
        v[EKind] = (int)e.Kind;
        v[EX] = e.X;
        v[EY] = e.Y;
        v[ESheet] = (int)e.Sheet;
        v[EFrame] = e.Frame;
        v[ELifeKind] = e.LifeKind;
        v[EFill] = e.Fill is { } c ? ((long)c.R << 16 | (long)c.G << 8 | c.B) + 1 : 0;
        v[ELife] = e.LifeSpan;
        v[ECycle] = e.Cycle;
    }

    private static SurfaceElementSnapshot ElementFromNumbers(ReadOnlySpan<long> v, int id)
    {
        var kind = (SurfaceElementKind)ToInt(v[EKind]);
        var sheet = (SurfaceSheet)ToInt(v[ESheet]);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(sheet) || v[EFill] is < 0 or > 0x1000000
                || v[EFrame] < 0 || v[ELifeKind] is < 0 or >= Content.ContactShapes.LifeSheets)
            throw new InvalidDataException("Bad surface element.");
        long fill = v[EFill] - 1;
        Systems.Rgb? color = fill < 0 ? null : new Systems.Rgb((byte)(fill >> 16), (byte)(fill >> 8), (byte)fill);
        return new SurfaceElementSnapshot(id, kind, ToInt(v[EX]), ToInt(v[EY]), sheet, ToInt(v[EFrame]),
            ToInt(v[ELifeKind]), color, ToInt(v[ELife]), ToInt(v[ECycle]));
    }
}
