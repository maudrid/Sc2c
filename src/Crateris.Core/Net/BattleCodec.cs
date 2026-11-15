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

    /// <summary>The most marks a ship's captain's window holds in a message (each limpet or marine adds one or two until the ship warps in again).</summary>
    public const int MostSilhouetteMarks = 1024;

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
        int count = Math.Min(markers?.Count ?? 0, MostMarkers);   // the decoder's limit (a thousand battles at once won't happen)
        w.UInt((ulong)count);
        for (int i = 0; i < count; ++i)
        {
            var m = markers![i];
            w.UInt((ulong)m.Id);
            w.Int(Optional(m.Star));
            w.Int(Optional(m.InnerPlanet));
            w.Int(m.X);
            w.Int(m.Y);
            w.String(m.EnemyShip);
        }
    }

    private static IReadOnlyList<BattleMarkerSnapshot> ReadMarkers(ref WireReader r)
    {
        var markers = new BattleMarkerSnapshot[Count(r.UInt(), MostMarkers)];
        for (int i = 0; i < markers.Length; ++i)
            markers[i] = new BattleMarkerSnapshot(ToInt((long)r.UInt()), ToOptional(r.Int()), ToOptional(r.Int()),
                ToInt(r.Int()), ToInt(r.Int()), r.String() ?? throw new InvalidDataException("A battle's marker has no race."));
        return markers;
    }

    // A ship's status; its captain's window's marks by their sheets' places
    // in the battle's (those of sheets past its limit left out, none in practice).
    private static void WriteStatus(WireWriter w, BattleShipStatus s, Dictionary<string, int> sheets)
    {
        w.String(s.Ship);
        w.UInt((ulong)s.Crew);
        w.UInt((ulong)s.MaxCrew);
        w.UInt((ulong)s.Energy);
        w.UInt((ulong)s.MaxEnergy);
        w.UInt((ulong)s.Captain);
        w.UInt((ulong)s.Flags);
        w.UInt((ulong)s.ElementCrew);
        w.UInt((ulong)s.LifeSpan);
        w.Byte(s.Gone ? (byte)1 : (byte)0);
        w.UInt((ulong)s.CaptainSpecial);
        var marks = s.Marks ?? Array.Empty<SilhouetteMarkSnapshot>();
        int count = 0;
        foreach (var m in marks)
            if (sheets.ContainsKey(m.Sheet) && count < MostSilhouetteMarks)
                ++count;
        w.UInt((ulong)count);
        foreach (var m in marks)
        {
            if (count == 0)
                break;
            if (!sheets.TryGetValue(m.Sheet, out int sheet))
                continue;
            --count;
            w.UInt((ulong)sheet);
            w.UInt(m.Frame);
            w.Int(m.X);
            w.Int(m.Y);
            w.Byte(m.Erase ? (byte)1 : (byte)0);
        }
    }

    private static BattleShipStatus ReadStatus(ref WireReader r, string[] sheets)
    {
        var status = new BattleShipStatus(r.String() ?? throw new InvalidDataException("A ship's status has no kind."),
            ToInt((long)Math.Min(r.UInt(), int.MaxValue)), ToInt((long)Math.Min(r.UInt(), int.MaxValue)),
            ToInt((long)Math.Min(r.UInt(), int.MaxValue)), ToInt((long)Math.Min(r.UInt(), int.MaxValue)),
            Small(r.UInt(), byte.MaxValue, "captain"), (StatusFlags)Small(r.UInt(), ushort.MaxValue, "status flags"),
            Small(r.UInt(), ushort.MaxValue, "element crew"), Small(r.UInt(), ushort.MaxValue, "life span"), Flag(r.Byte()),
            Small(r.UInt(), ushort.MaxValue, "captain's window frame"));
        var marks = new SilhouetteMarkSnapshot[Count(r.UInt(), MostSilhouetteMarks)];
        for (int i = 0; i < marks.Length; ++i)
        {
            ulong sheet = r.UInt();
            if (sheet >= (ulong)sheets.Length)
                throw new InvalidDataException("A captain's window's mark's sheet isn't in the battle's.");
            marks[i] = new SilhouetteMarkSnapshot(sheets[sheet], (ushort)Small(r.UInt(), ushort.MaxValue, "mark frame"),
                ToShort(r.Int()), ToShort(r.Int()), Flag(r.Byte()));
        }
        return status with { Marks = marks };
    }

    // The host's fleet ships in the battle and the pick screen: 0 for none,
    // else 1, the ships (each its slot, ship, captain, crew and most, how it
    // came out (0 for not yet, else BattleExit's plus one), and a byte of
    // whether it has flown (1) and can be picked now (2)), and the pick
    // screen's cursor (0 for none, else 1, its row and column).
    private static void WriteArmada(WireWriter w, BattleArmadaSnapshot? a)
    {
        if (a is null)
        {
            w.Byte(0);
            return;
        }
        w.Byte(1);
        int count = Math.Min(a.Ships.Count, HostFleet.MaxShips);   // the decoder's limit, the fleet's
        w.UInt((ulong)count);
        foreach (var s in a.Ships.Take(count))
        {
            w.UInt((ulong)s.Slot);
            w.String(s.Ship);
            w.UInt((ulong)s.Captain);
            w.UInt((ulong)s.Crew);
            w.UInt((ulong)s.MaxCrew);
            w.UInt(s.Exit is { } exit ? (ulong)exit + 1 : 0);
            w.Byte((byte)((s.Flown ? 1 : 0) | (s.CanPick ? 2 : 0)));
        }
        if (a.Pick is not { } pick)
            w.Byte(0);
        else
        {
            w.Byte(1);
            w.UInt((ulong)pick.Row);
            w.UInt((ulong)pick.Column);
        }
    }

    private static BattleArmadaSnapshot? ReadArmada(ref WireReader r)
    {
        if (!Flag(r.Byte()))
            return null;
        var ships = new BattleFleetShipSnapshot[Count(r.UInt(), HostFleet.MaxShips)];
        for (int i = 0; i < ships.Length; ++i)
        {
            int slot = Small(r.UInt(), HostFleet.MaxShips - 1, "fleet ship's slot");
            if (i > 0 && slot <= ships[i - 1].Slot)
                throw new InvalidDataException("A battle's fleet ships aren't in their slots' order.");
            string ship = r.String() ?? throw new InvalidDataException("A battle's fleet ship has no kind.");
            int captain = Small(r.UInt(), byte.MaxValue, "captain"), crew = Small(r.UInt(), ushort.MaxValue, "crew"),
                most = Small(r.UInt(), ushort.MaxValue, "most crew");
            ulong exit = r.UInt();
            if (exit > (ulong)BattleExit.Destroyed + 1)
                throw new InvalidDataException("Bad fleet ship's exit.");
            byte flags = r.Byte();
            if (flags > 3)
                throw new InvalidDataException("Bad fleet ship's flags.");
            ships[i] = new BattleFleetShipSnapshot(slot, ship, captain, crew, most, exit == 0 ? null : (BattleExit)(int)(exit - 1),
                (flags & 1) != 0, (flags & 2) != 0);
        }
        BattlePickSnapshot? pick = null;
        if (Flag(r.Byte()))
            pick = new BattlePickSnapshot(Small(r.UInt(), Systems.PickShipMenu.Rows - 1, "pick row"),
                Small(r.UInt(), Systems.PickShipMenu.Columns, "pick column"));
        return new BattleArmadaSnapshot(ships, pick);
    }

    private static int Small(ulong v, int most, string what) =>
        v <= (ulong)most ? (int)v : throw new InvalidDataException($"Bad {what}.");

    private static bool Flag(byte b) => b switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Bad flag.") };

    private static void WriteCamera(WireWriter w, BattleCamera c)
    {
        w.Int(c.ViewState);
        w.Int(c.ScrollX);
        w.Int(c.ScrollY);
        w.Int(c.ZoomOut);
        w.Int(c.OrgX);
        w.Int(c.OrgY);
        w.Byte(c.Step ? (byte)1 : (byte)0);
        w.UInt((ulong)c.Frame);
        w.Int(c.ScrolledX);
        w.Int(c.ScrolledY);
    }

    private static BattleCamera ReadCamera(ref WireReader r)
    {
        long view = r.Int();
        if (view is < -1 or > 2)
            throw new InvalidDataException("Bad view state.");
        short sx = ToShort(r.Int()), sy = ToShort(r.Int()), zoom = ToShort(r.Int()), ox = ToShort(r.Int()), oy = ToShort(r.Int());
        bool step = Flag(r.Byte());
        if (zoom < 0 || (step && zoom > 2))
            throw new InvalidDataException("Bad zoom.");
        ulong frames = r.UInt();
        if (frames > int.MaxValue)
            throw new InvalidDataException("Bad battle frame.");
        int frame = (int)frames;
        long tx = r.Int(), ty = r.Int();
        if (tx is < int.MinValue or > int.MaxValue || ty is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("Bad scroll.");
        return new BattleCamera((int)view, sx, sy, zoom, ox, oy, step, frame, (int)tx, (int)ty);
    }

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
        // Within the decoder's limits: a battle's ships are bounded by the
        // players, its elements by GameSettings.MostBattleElements; sheets
        // past the limit (none in practice) leave their elements and marks out.
        // The sheets first: the elements' and the captain's windows' marks'.
        int fighters = Math.Min(b.Fighters.Count, MostBattleShips);
        var sheets = new Dictionary<string, int>();
        foreach (var e in b.Elements)
            if (sheets.Count < MostBattleSheets)
                sheets.TryAdd(e.Sheet, sheets.Count);
        foreach (var status in b.Fighters.Take(fighters).Select(f => f.Status).Prepend(b.Enemy))
            foreach (var m in status.Marks ?? Array.Empty<SilhouetteMarkSnapshot>())
                if (sheets.Count < MostBattleSheets)
                    sheets.TryAdd(m.Sheet, sheets.Count);
        w.UInt((ulong)sheets.Count);
        foreach (var s in sheets.Keys)
            w.String(s);
        WriteStatus(w, b.Enemy, sheets);
        w.Int(b.EnemyX);
        w.Int(b.EnemyY);
        w.UInt((ulong)fighters);
        foreach (var f in b.Fighters.Take(fighters))
        {
            w.String(f.PlayerId);
            WriteStatus(w, f.Status, sheets);
            w.Int(f.X);
            w.Int(f.Y);
            w.Byte(f.Followed ? (byte)1 : (byte)0);
            WriteArmada(w, f.Armada);
        }
        WriteCamera(w, b.Camera);
        w.UInt(b.StarSeed);
        var sent = b.Elements.Where(e => sheets.ContainsKey(e.Sheet)).Take(MostBattleElements).ToList();
        w.UInt((ulong)sent.Count);
        foreach (var e in sent)
        {
            w.Byte((byte)e.Prim);
            w.Int(e.X);
            w.Int(e.Y);
            if (e.Prim == PrimType.Line)
            {
                w.Int(e.X2);
                w.Int(e.Y2);
            }
            w.UInt((ulong)sheets[e.Sheet]);
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
        var sheets = new string[Count(r.UInt(), MostBattleSheets)];
        for (int i = 0; i < sheets.Length; ++i)
            sheets[i] = r.String() ?? throw new InvalidDataException("A battle's sheet has no name.");
        var enemy = ReadStatus(ref r, sheets);
        short ex = ToShort(r.Int()), ey = ToShort(r.Int());
        var fighters = new BattleFighterSnapshot[Count(r.UInt(), MostBattleShips)];
        for (int i = 0; i < fighters.Length; ++i)
        {
            string pid = r.String() ?? throw new InvalidDataException("A battle's ship has no player.");
            fighters[i] = new BattleFighterSnapshot(pid, ReadStatus(ref r, sheets), ToShort(r.Int()), ToShort(r.Int()), Flag(r.Byte()),
                ReadArmada(ref r));
        }
        var camera = ReadCamera(ref r);
        ulong seed = r.UInt();
        if (seed > uint.MaxValue)
            throw new InvalidDataException("Bad star seed.");
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
        return new[] { new BattleSnapshot(id, width, height, enemy, ex, ey, fighters, elements, camera, (uint)seed) };
    }
}
