// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.World;

namespace Crateris.Core.Net;

// The snapshot's battle and markers (stage 4a item 11, protocol 21): a
// guest's own battle, sent whole every frame it's in one (its elements move
// every frame), with only the elements near its ship; and every battle's
// marker, sent when they change.
public static partial class SnapshotCodec
{
    private const byte PartBattle = 64, PartMarkers = 128;

    /// <summary>The most elements, ships and sheets a battle message holds (a full display list is 150 to a few hundred).</summary>
    public const int MostBattleElements = 4096, MostBattleShips = 256, MostBattleSheets = 256, MostMarkers = 1024;

    // The battle a viewer is sent: the one its ship is in, as it sees it
    // (without allocating when it's in none, as encoding doesn't).
    private static BattleSnapshot? BattleSeen(WorldSnapshot? snapshot, string? viewer)
    {
        if (viewer is null || snapshot?.Battles is not { } battles)
            return null;
        for (int i = 0; i < battles.Count; ++i)
            for (int j = 0; j < battles[i].Fighters.Count; ++j)
                if (battles[i].Fighters[j].PlayerId == viewer)
                    return battles[i].SeenBy(viewer);
        return null;
    }

    /// <summary>Whether a viewer's ship is in one of the snapshot's battles (its message is then its own).</summary>
    public static bool InBattle(WorldSnapshot? snapshot, string? viewer)
    {
        if (viewer is null || snapshot?.Battles is not { } battles)
            return false;
        for (int i = 0; i < battles.Count; ++i)
            for (int j = 0; j < battles[i].Fighters.Count; ++j)
                if (battles[i].Fighters[j].PlayerId == viewer)
                    return true;
        return false;
    }

    private static bool SameMarkers(IReadOnlyList<BattleMarkerSnapshot>? a, IReadOnlyList<BattleMarkerSnapshot>? b)
    {
        int n = a?.Count ?? 0;
        if (n != (b?.Count ?? 0))
            return false;
        for (int i = 0; i < n; ++i)
            if (a![i] != b![i])
                return false;
        return true;
    }

    private static void WriteMarkers(WireWriter w, IReadOnlyList<BattleMarkerSnapshot>? markers)
    {
        int count = markers?.Count ?? 0;
        w.UInt((ulong)count);
        for (int i = 0; i < count; ++i)
        {
            var m = markers![i];
            w.UInt((ulong)m.Id);
            w.Int(Optional(m.Star));
            w.Int(Optional(m.InnerPlanet));
            w.Int(m.X);
            w.Int(m.Y);
        }
    }

    private static IReadOnlyList<BattleMarkerSnapshot> ReadMarkers(ref WireReader r)
    {
        var markers = new BattleMarkerSnapshot[Count(r.UInt(), MostMarkers)];
        for (int i = 0; i < markers.Length; ++i)
            markers[i] = new BattleMarkerSnapshot(ToInt((long)r.UInt()), ToOptional(r.Int()), ToOptional(r.Int()),
                ToInt(r.Int()), ToInt(r.Int()));
        return markers;
    }

    private static void WriteStatus(WireWriter w, BattleShipStatus s)
    {
        w.String(s.Ship);
        w.UInt((ulong)s.Crew);
        w.UInt((ulong)s.MaxCrew);
        w.UInt((ulong)s.Energy);
        w.UInt((ulong)s.MaxEnergy);
    }

    private static BattleShipStatus ReadStatus(ref WireReader r) =>
        new(r.String() ?? throw new InvalidDataException("A ship's status has no kind."),
            ToInt((long)Math.Min(r.UInt(), int.MaxValue)), ToInt((long)Math.Min(r.UInt(), int.MaxValue)),
            ToInt((long)Math.Min(r.UInt(), int.MaxValue)), ToInt((long)Math.Min(r.UInt(), int.MaxValue)));

    // A battle, whole: 0 for none (the viewer has left it), else 1 and the battle.
    private static void WriteBattle(WireWriter w, BattleSnapshot? b)
    {
        if (b is null)
        {
            w.Byte(0);
            return;
        }
        w.Byte(1);
        w.UInt((ulong)b.Id);
        w.UInt((ulong)b.Width);
        w.UInt((ulong)b.Height);
        WriteStatus(w, b.Enemy);
        w.Int(b.EnemyX);
        w.Int(b.EnemyY);
        w.UInt((ulong)b.Fighters.Count);
        foreach (var f in b.Fighters)
        {
            w.String(f.PlayerId);
            WriteStatus(w, f.Status);
            w.Int(f.X);
            w.Int(f.Y);
        }
        var sheets = b.Elements.Select(e => e.Sheet).Distinct().ToList();
        w.UInt((ulong)sheets.Count);
        foreach (var s in sheets)
            w.String(s);
        w.UInt((ulong)b.Elements.Count);
        foreach (var e in b.Elements)
        {
            w.Byte((byte)e.Prim);
            w.Int(e.X);
            w.Int(e.Y);
            if (e.Prim == PrimType.Line)
            {
                w.Int(e.X2);
                w.Int(e.Y2);
            }
            w.UInt((ulong)sheets.IndexOf(e.Sheet));
            w.UInt(e.Frame);
            w.Int(e.Color);
        }
    }

    private static IReadOnlyList<BattleSnapshot>? ReadBattle(ref WireReader r)
    {
        switch (r.Byte())
        {
            case 0:
                return null;
            case 1:
                break;
            default:
                throw new InvalidDataException("Bad battle.");
        }
        int id = ToInt((long)Math.Min(r.UInt(), int.MaxValue));
        int width = ToInt((long)Math.Min(r.UInt(), int.MaxValue)), height = ToInt((long)Math.Min(r.UInt(), int.MaxValue));
        if (width < 1 || height < 1 || width > ushort.MaxValue || height > ushort.MaxValue)
            throw new InvalidDataException("Bad arena size.");
        var enemy = ReadStatus(ref r);
        short ex = ToShort(r.Int()), ey = ToShort(r.Int());
        var fighters = new BattleFighterSnapshot[Count(r.UInt(), MostBattleShips)];
        for (int i = 0; i < fighters.Length; ++i)
        {
            string pid = r.String() ?? throw new InvalidDataException("A battle's ship has no player.");
            fighters[i] = new BattleFighterSnapshot(pid, ReadStatus(ref r), ToShort(r.Int()), ToShort(r.Int()));
        }
        var sheets = new string[Count(r.UInt(), MostBattleSheets)];
        for (int i = 0; i < sheets.Length; ++i)
            sheets[i] = r.String() ?? throw new InvalidDataException("A battle's sheet has no name.");
        var elements = new BattleElementSnapshot[Count(r.UInt(), MostBattleElements)];
        for (int i = 0; i < elements.Length; ++i)
        {
            var prim = (PrimType)r.Byte();
            if (!Enum.IsDefined(prim) || prim == PrimType.None)
                throw new InvalidDataException("Bad element.");
            short x = ToShort(r.Int()), y = ToShort(r.Int());
            short x2 = 0, y2 = 0;
            if (prim == PrimType.Line)
                (x2, y2) = (ToShort(r.Int()), ToShort(r.Int()));
            ulong sheet = r.UInt();
            if (sheet >= (ulong)sheets.Length)
                throw new InvalidDataException("An element's sheet isn't in the battle's.");
            ulong frame = r.UInt();
            if (frame > ushort.MaxValue)
                throw new InvalidDataException("Bad element frame.");
            elements[i] = new BattleElementSnapshot(prim, x, y, x2, y2, sheets[sheet], (ushort)frame, ToInt(r.Int()));
        }
        return new[] { new BattleSnapshot(id, width, height, enemy, ex, ey, fighters, elements) };
    }
}
