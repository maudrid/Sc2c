// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Net;

/// <summary>
/// Snapshots on the wire (docs/plans/stage-2b.md, item 4 and decision 9):
/// a binary format, written either in full or as a delta from an earlier
/// snapshot the receiver has (its base).
///
/// A message is: its kind (full or delta); the frame; for a delta, how many
/// frames back its base is; a byte of which world parts changed (the date,
/// the orbited worlds, the surface, the story state), then those parts;
/// then the ships. Each ship is the
/// place of its base ship in the base's list plus one (0 for a ship new
/// since the base), a bitmask of the fields that changed, and those fields.
/// Numbers are written as their difference from the base ship's (so a ship
/// moving a little costs a byte or two a field, and one standing still
/// nothing but its mask); the id, the name, the star entry, the hold and
/// the ship's and captain's names whole. A full snapshot is the same, measured from a base of zeros, with
/// every field written. Ships whose fields didn't change are the base's own
/// records when decoded, so a delta builds little.
/// </summary>
public static partial class SnapshotCodec
{
    private const byte KindFull = 0, KindDelta = 1;
    private const byte PartDate = 1, PartOrbited = 2, PartSurface = 4, PartPause = 16;

    // The ship's numbers, in the order of the mask's bits.
    private const int Flags = 0, LogX = 1, LogY = 2, UniverseX = 3, UniverseY = 4, Facing = 5,
        VelocityX = 6, VelocityY = 7, Fuel = 8, FuelCapacity = 9, SystemStar = 10, InnerPlanet = 11,
        IpX = 12, IpY = 13, ScreenX = 14, ScreenY = 15, IpFacing = 16, ZoomRadius = 17,
        OrbitPlanet = 18, OrbitMoon = 19, IpVelocityX = 20, IpVelocityY = 21, ViewX = 22, ViewY = 23;
    private const int Numbers = 24;

    // The fields written whole.
    private const int BitId = Numbers, BitName = Numbers + 1, BitEntry = Numbers + 2, BitHold = Numbers + 3,
        BitNames = Numbers + 4, BitModules = Numbers + 5;
    private const ulong AllFields = (1UL << (Numbers + 6)) - 1;

    /// <summary>
    /// Writes <paramref name="snapshot"/> into <paramref name="writer"/>
    /// (cleared first): as a delta from <paramref name="baseSnapshot"/>, an
    /// earlier snapshot the receiver has, or in full without one. Of the
    /// surfaces, only <paramref name="viewer"/>'s goes: the one its own
    /// lander is on (none for no viewer), as a delta from the one it was
    /// sent with the base, if it's the same world's.
    /// Doesn't allocate once the writer's buffer is big enough.
    /// </summary>
    public static void Encode(WireWriter writer, WorldSnapshot snapshot, WorldSnapshot? baseSnapshot = null, string? viewer = null)
    {
        if (baseSnapshot is not null && baseSnapshot.Frame >= snapshot.Frame)
            throw new ArgumentException("a delta's base must be an earlier snapshot", nameof(baseSnapshot));
        writer.Clear();
        writer.Byte(baseSnapshot is null ? KindFull : KindDelta);
        writer.UInt((ulong)snapshot.Frame);
        if (baseSnapshot is not null)
            writer.UInt((ulong)(snapshot.Frame - baseSnapshot.Frame));

        bool dateChanged = baseSnapshot is null
            || (snapshot.Year, snapshot.Month, snapshot.Day) != (baseSnapshot.Year, baseSnapshot.Month, baseSnapshot.Day);
        bool orbitedChanged = baseSnapshot is null || !SameWorlds(snapshot.OrbitedWorlds, baseSnapshot.OrbitedWorlds);
        var surface = SurfaceSeen(snapshot, viewer);
        var baseSurface = SurfaceSeen(baseSnapshot, viewer);
        bool surfaceChanged = surface is not null || baseSurface is not null;
        bool storyChanged = StoryChanged(snapshot.Story, baseSnapshot?.Story);
        bool pauseChanged = snapshot.StoryPause != (baseSnapshot?.StoryPause);
        writer.Byte((byte)((dateChanged ? PartDate : 0) | (orbitedChanged ? PartOrbited : 0) | (surfaceChanged ? PartSurface : 0)
            | (storyChanged ? PartStory : 0) | (pauseChanged ? PartPause : 0)));
        if (dateChanged)
        {
            writer.Int((long)snapshot.Year - (baseSnapshot?.Year ?? 0));
            writer.Int((long)snapshot.Month - (baseSnapshot?.Month ?? 0));
            writer.Int((long)snapshot.Day - (baseSnapshot?.Day ?? 0));
        }
        if (orbitedChanged)
            WriteWorlds(writer, snapshot.OrbitedWorlds);
        if (surfaceChanged)
            WriteSurface(writer, surface, baseSurface);
        if (storyChanged)
            WriteStory(writer, snapshot.Story, baseSnapshot?.Story);
        if (pauseChanged)
            writer.String(snapshot.StoryPause);

        writer.UInt((ulong)snapshot.Ships.Count);
        Span<long> now = stackalloc long[Numbers];
        Span<long> was = stackalloc long[Numbers];
        for (int i = 0; i < snapshot.Ships.Count; ++i)
        {
            var ship = snapshot.Ships[i];
            int at = baseSnapshot is null ? -1 : IndexOf(baseSnapshot.Ships, ship.Id);
            var old = at < 0 ? null : baseSnapshot!.Ships[at];
            writer.UInt((ulong)(at + 1));
            ToNumbers(ship, now);
            if (old is null)
                was.Clear();
            else
                ToNumbers(old, was);
            ulong mask = 0;
            if (old is null)
                mask = AllFields;
            else
            {
                for (int f = 0; f < Numbers; ++f)
                {
                    if (now[f] != was[f])
                        mask |= 1UL << f;
                }
                if (ship.Name != old.Name)
                    mask |= 1UL << BitName;
                if (!Equals(ship.Entry, old.Entry))
                    mask |= 1UL << BitEntry;
                if (!Equals(ship.Hold, old.Hold))
                    mask |= 1UL << BitHold;
                if (ship.ShipName != old.ShipName || ship.CaptainName != old.CaptainName)
                    mask |= 1UL << BitNames;
                if (!Equals(ship.Modules, old.Modules))
                    mask |= 1UL << BitModules;
            }
            writer.UInt(mask);
            for (int f = 0; f < Numbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    writer.Int(now[f] - was[f]);
            }
            if ((mask & (1UL << BitId)) != 0)
                writer.String(ship.Id);
            if ((mask & (1UL << BitName)) != 0)
                writer.String(ship.Name);
            if ((mask & (1UL << BitEntry)) != 0)
                WriteEntry(writer, ship.Entry);
            if ((mask & (1UL << BitHold)) != 0)
                WriteHold(writer, ship.Hold);
            if ((mask & (1UL << BitNames)) != 0)
            {
                writer.String(ship.ShipName);
                writer.String(ship.CaptainName);
            }
            if ((mask & (1UL << BitModules)) != 0)
                WriteModules(writer, ship.Modules);
        }
    }

    // A ship's slots: none, or its drive, jet and module slots and its kit's crew.
    private static void WriteModules(WireWriter w, ModulesState? m)
    {
        if (m is null)
        {
            w.UInt(0);
            return;
        }
        w.UInt(1);
        foreach (byte b in m.Drives!)
            w.UInt(b);
        foreach (byte b in m.Jets!)
            w.UInt(b);
        foreach (byte b in m.Modules!)
            w.UInt(b);
        w.UInt((ulong)m.KitCrew);
    }

    private static ModulesState? ReadModules(ref WireReader r)
    {
        ulong present = r.UInt();
        if (present == 0)
            return null;
        if (present != 1)
            throw new InvalidDataException("Bad ship slots.");
        // Each slot holds what its kind may (ShipModules' rules), or is empty.
        static byte[] Slots(ref WireReader r, int n, byte low, byte high, byte empty)
        {
            var b = new byte[n];
            for (int i = 0; i < n; ++i)
            {
                ulong v = r.UInt();
                if (v != empty && (v < low || v > high))
                    throw new InvalidDataException("Bad ship slot.");
                b[i] = (byte)v;
            }
            return b;
        }
        var drives = Slots(ref r, ShipModules.DriveSlotCount, ShipModules.FusionThruster, ShipModules.FusionThruster,
            ShipModules.EmptyDrive);
        var jets = Slots(ref r, ShipModules.JetSlotCount, ShipModules.TurningJets, ShipModules.TurningJets, ShipModules.EmptyJet);
        var modules = Slots(ref r, ShipModules.ModuleSlotCount, ShipModules.CrewPod, ShipModules.Modules - 1,
            ShipModules.EmptyModule);
        ulong kit = r.UInt();
        if (kit > 1000)
            throw new InvalidDataException("Bad kit crew.");
        return new ModulesState(drives, jets, modules, (int)kit);
    }

    /// <summary>The frame a message is for, and the frame of its base (null for a full snapshot), without decoding it.</summary>
    public static (long Frame, long? Base) Peek(ReadOnlySpan<byte> data)
    {
        var r = new WireReader(data);
        long frame = Header(ref r, out long? baseFrame);
        return (frame, baseFrame);
    }

    private static long Header(ref WireReader r, out long? baseFrame)
    {
        byte kind = r.Byte();
        long frame = Frame(r.UInt());
        baseFrame = null;
        if (kind == KindFull)
            return frame;
        if (kind != KindDelta)
            throw new InvalidDataException($"Unknown snapshot kind {kind}.");
        ulong back = r.UInt();
        if (back == 0 || back > (ulong)frame)
            throw new InvalidDataException("A delta's base must be an earlier frame.");
        baseFrame = frame - (long)back;
        return frame;
    }

    /// <summary>
    /// Reads a message. A delta needs its base: <paramref name="baseSnapshot"/>
    /// must be the snapshot of the frame <see cref="Peek"/> names.
    /// </summary>
    public static WorldSnapshot Decode(ReadOnlySpan<byte> data, WorldSnapshot? baseSnapshot = null) =>
        Decode(data, frame => baseSnapshot?.Frame == frame ? baseSnapshot : null, out _)
        ?? throw new ArgumentException($"this delta needs the snapshot of frame {Peek(data).Base}", nameof(baseSnapshot));

    /// <summary>
    /// Reads a message in one pass, asking <paramref name="baseOf"/> for a
    /// delta's base by its frame (<paramref name="baseFrame"/>, null for a
    /// full snapshot); null if it hasn't got it.
    /// </summary>
    public static WorldSnapshot? Decode(ReadOnlySpan<byte> data, Func<long, WorldSnapshot?> baseOf, out long? baseFrame)
    {
        var r = new WireReader(data);
        long frame = Header(ref r, out baseFrame);
        WorldSnapshot? baseSnapshot = null;
        if (baseFrame is long bf)
        {
            baseSnapshot = baseOf(bf);
            if (baseSnapshot is null)
                return null;
            if (baseSnapshot.Frame != bf)
                throw new ArgumentException($"the base given for frame {bf} is frame {baseSnapshot.Frame}", nameof(baseOf));
        }
        byte parts = r.Byte();
        if ((parts & ~(PartDate | PartOrbited | PartSurface | PartStory | PartPause)) != 0
                || (baseSnapshot is null && (parts & (PartDate | PartOrbited)) != (PartDate | PartOrbited)))
            throw new InvalidDataException("Bad snapshot parts.");
        int year = baseSnapshot?.Year ?? 0, month = baseSnapshot?.Month ?? 0, day = baseSnapshot?.Day ?? 0;
        if ((parts & PartDate) != 0)
        {
            year = ToInt(year + r.Int());
            month = ToInt(month + r.Int());
            day = ToInt(day + r.Int());
        }
        var worlds = (parts & PartOrbited) != 0 ? ReadWorlds(ref r) : baseSnapshot!.OrbitedWorlds;
        var surfaces = (parts & PartSurface) != 0 ? ReadSurface(ref r, baseSnapshot?.Surfaces?.FirstOrDefault())
            : baseSnapshot?.Surfaces;
        var story = (parts & PartStory) != 0 ? ReadStory(ref r, baseSnapshot?.Story) : baseSnapshot?.Story;
        var pause = (parts & PartPause) != 0 ? r.String() : baseSnapshot?.StoryPause;

        int count = Count(r.UInt(), 64);
        var ships = new ShipSnapshot[count];
        Span<long> v = stackalloc long[Numbers];
        for (int i = 0; i < count; ++i)
        {
            ulong at = r.UInt();
            ShipSnapshot? old = null;
            if (at != 0)
            {
                if (baseSnapshot is null || at > (ulong)baseSnapshot.Ships.Count)
                    throw new InvalidDataException("A ship's base isn't in the base snapshot.");
                old = baseSnapshot.Ships[(int)at - 1];
            }
            ulong mask = r.UInt();
            // A new ship has every field; a ship from the base keeps its id
            // (ships are matched by id).
            if ((mask & ~AllFields) != 0 || (old is null ? mask != AllFields : (mask & (1UL << BitId)) != 0))
                throw new InvalidDataException("Bad ship fields.");
            if (old is not null && mask == 0)
            {
                ships[i] = old;
                continue;
            }
            if (old is null)
                v.Clear();
            else
                ToNumbers(old, v);
            for (int f = 0; f < Numbers; ++f)
            {
                if ((mask & (1UL << f)) != 0)
                    v[f] = Add(v[f], r.Int());
            }
            string id = (mask & (1UL << BitId)) != 0 ? r.String() ?? throw new InvalidDataException("A ship has no id.") : old!.Id;
            string name = (mask & (1UL << BitName)) != 0 ? r.String() ?? throw new InvalidDataException("A ship has no name.") : old!.Name;
            var entry = (mask & (1UL << BitEntry)) != 0 ? ReadEntry(ref r) : old!.Entry;
            var hold = (mask & (1UL << BitHold)) != 0 ? ReadHold(ref r) : old!.Hold;
            string? shipName = old?.ShipName, captainName = old?.CaptainName;
            if ((mask & (1UL << BitNames)) != 0)
            {
                shipName = Name(r.String());
                captainName = Name(r.String());
            }
            var modules = (mask & (1UL << BitModules)) != 0 ? ReadModules(ref r) : old?.Modules;
            ships[i] = FromNumbers(v, id, name, entry, hold, shipName, captainName, modules);
        }
        for (int i = 1; i < count; ++i)
        {
            for (int j = 0; j < i; ++j)
            {
                if (ships[i].Id == ships[j].Id)
                    throw new InvalidDataException($"Two ships are {ships[i].Id}.");
            }
        }
        if (!r.AtEnd)
            throw new InvalidDataException("The snapshot has bytes left over.");
        return new WorldSnapshot(frame, year, month, day, ships, worlds, surfaces, story, pause);
    }

    private static void ToNumbers(ShipSnapshot s, Span<long> v)
    {
        v[Flags] = (s.IsHost ? 1 : 0) | (s.Thrusting ? 2 : 0) | (s.UndockPending ? 4 : 0)
            | ((int)s.Kind << 3) | ((int)s.Activity << 5);
        v[LogX] = s.LogX;
        v[LogY] = s.LogY;
        v[UniverseX] = s.UniverseX;
        v[UniverseY] = s.UniverseY;
        v[Facing] = s.Facing;
        v[VelocityX] = s.VelocityX;
        v[VelocityY] = s.VelocityY;
        v[Fuel] = s.Fuel;
        v[FuelCapacity] = s.FuelCapacity;
        v[SystemStar] = Optional(s.SystemStar);
        v[InnerPlanet] = Optional(s.InnerPlanet);
        v[IpX] = s.IpX;
        v[IpY] = s.IpY;
        v[ScreenX] = s.ScreenX;
        v[ScreenY] = s.ScreenY;
        v[IpFacing] = s.IpFacing;
        v[ZoomRadius] = s.ZoomRadius;
        v[OrbitPlanet] = Optional(s.OrbitPlanet);
        v[OrbitMoon] = Optional(s.OrbitMoon);
        v[IpVelocityX] = s.IpVelocityX;
        v[IpVelocityY] = s.IpVelocityY;
        v[ViewX] = s.ViewX;
        v[ViewY] = s.ViewY;
    }

    private static ShipSnapshot FromNumbers(ReadOnlySpan<long> v, string id, string name, StarEntryState? entry, HoldSnapshot? hold,
        string? shipName = null, string? captainName = null, ModulesState? modules = null)
    {
        long flags = v[Flags];
        if (flags < 0 || flags >= 1 << 8)
            throw new InvalidDataException("Bad ship flags.");
        var kind = (ShipKind)((flags >> 3) & 3);
        var activity = (Activity)(flags >> 5);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(activity))
            throw new InvalidDataException("Bad ship kind or activity.");
        return new ShipSnapshot(id, name, (flags & 1) != 0, kind,
            ToInt(v[LogX]), ToInt(v[LogY]), ToInt(v[UniverseX]), ToInt(v[UniverseY]),
            ToInt(v[Facing]), ToInt(v[VelocityX]), ToInt(v[VelocityY]),
            ToUInt(v[Fuel]), ToUInt(v[FuelCapacity]), (flags & 2) != 0, (flags & 4) != 0,
            activity, ToOptional(v[SystemStar]), ToOptional(v[InnerPlanet]),
            ToInt(v[IpX]), ToInt(v[IpY]), ToInt(v[ScreenX]), ToInt(v[ScreenY]), ToInt(v[IpFacing]), ToInt(v[ZoomRadius]),
            ToOptional(v[OrbitPlanet]), ToOptional(v[OrbitMoon]), ToInt(v[IpVelocityX]), ToInt(v[IpVelocityY]),
            ToShort(v[ViewX]), ToShort(v[ViewY]), entry, hold, shipName, captainName, modules);
    }

    private static void WriteEntry(WireWriter w, StarEntryState? e)
    {
        if (e is null)
        {
            w.Byte(0);
            return;
        }
        w.Byte(1);
        w.Int(e.ContactFrame);
        w.Int(e.LogX);
        w.Int(e.LogY);
        w.Int(e.ViewX);
        w.Int(e.ViewY);
        w.Int(e.Facing);
        w.Int(e.Star);
        w.Int(e.Steps);
        w.String(e.Following);
    }

    private static StarEntryState? ReadEntry(ref WireReader r) => r.Byte() switch
    {
        0 => null,
        1 => new StarEntryState(r.Int(), ToInt(r.Int()), ToInt(r.Int()), ToShort(r.Int()), ToShort(r.Int()),
            ToInt(r.Int()), ToInt(r.Int()), ToInt(r.Int()), r.String()),
        _ => throw new InvalidDataException("Bad star entry."),
    };

    private static void WriteHold(WireWriter w, HoldSnapshot? h)
    {
        if (h is null)
        {
            w.UInt(0);
            return;
        }
        w.UInt((ulong)h.Minerals.Count + 1);
        for (int i = 0; i < h.Minerals.Count; ++i)
            w.Int(h.Minerals[i]);
        w.Int(h.Crew);
        w.Int(h.Landers);
        w.Int(h.StorageCapacity);
        w.Int(h.BioData);
        w.Int((long)h.Upgrades);
        w.UInt(h.ResourceUnits);
        w.Int(h.LanderRoom);
    }

    private static HoldSnapshot? ReadHold(ref WireReader r)
    {
        ulong n = r.UInt();
        if (n == 0)
            return null;
        var minerals = new int[Count(n - 1, 64)];
        for (int i = 0; i < minerals.Length; ++i)
            minerals[i] = ToInt(r.Int());
        var hold = new HoldSnapshot(ToInt(r.Int()), ToInt(r.Int()), ToInt(r.Int()), minerals, ToInt(r.Int()),
            (LanderUpgrades)ToInt(r.Int()), ToUInt(r.UInt()), ToInt(r.Int()));
        if ((hold.Upgrades & ~LanderUpgrades.All) != 0)
            throw new InvalidDataException("Bad lander upgrades.");
        if (hold.LanderRoom < 0)
            throw new InvalidDataException("Bad lander room.");
        return hold;
    }

    private static void WriteWorlds(WireWriter w, IReadOnlyList<OrbitedWorldSnapshot>? worlds)
    {
        if (worlds is null)
        {
            w.UInt(0);
            return;
        }
        w.UInt((ulong)worlds.Count + 1);
        for (int i = 0; i < worlds.Count; ++i)
        {
            var o = worlds[i];
            w.Int(o.Star);
            w.Int(o.Planet);
            w.Int(Optional(o.Moon));
            w.UInt(o.MineralTaken);
            w.UInt(o.EnergyTaken);
            w.UInt(o.BiologicalTaken);
        }
    }

    private static IReadOnlyList<OrbitedWorldSnapshot>? ReadWorlds(ref WireReader r)
    {
        ulong n = r.UInt();
        if (n == 0)
            return null;
        var worlds = new OrbitedWorldSnapshot[Count(n - 1, 1024)];
        for (int i = 0; i < worlds.Length; ++i)
            worlds[i] = new OrbitedWorldSnapshot(ToInt(r.Int()), ToInt(r.Int()), ToOptional(r.Int()),
                ToUInt(r.UInt()), ToUInt(r.UInt()), ToUInt(r.UInt()));
        return worlds;
    }

    private static bool SameWorlds(IReadOnlyList<OrbitedWorldSnapshot>? a, IReadOnlyList<OrbitedWorldSnapshot>? b) =>
        a is null || b is null ? a == b : a.SequenceEqual(b);

    private static int IndexOf(IReadOnlyList<ShipSnapshot> ships, string id)
    {
        for (int i = 0; i < ships.Count; ++i)
        {
            if (ships[i].Id == id)
                return i;
        }
        return -1;
    }

    // A nullable number: 0 for null, a value from 0 up as itself plus one, a
    // negative one as itself.
    private static long Optional(int? value) => value switch
    {
        null => 0,
        >= 0 and int v => (long)v + 1,
        int v => v,
    };

    private static int? ToOptional(long v) => v switch
    {
        0 => null,
        > 0 => ToInt(v - 1),
        _ => ToInt(v),
    };

    private static long Add(long a, long b)
    {
        long sum = unchecked(a + b);
        if (((a ^ sum) & (b ^ sum)) < 0)
            throw new InvalidDataException("A number in the snapshot is out of range.");
        return sum;
    }

    // A ship's or captain's name: none (the default), or one UQM could hold (ShipNames.Fit).
    private static string? Name(string? name) =>
        name is null || World.ShipNames.Fit(name) == name
            ? name : throw new InvalidDataException("A ship's or captain's name isn't one UQM could hold.");

    private static long Frame(ulong v) => v <= long.MaxValue ? (long)v : throw new InvalidDataException("Bad frame.");

    private static int Count(ulong n, int most) =>
        n <= (ulong)most ? (int)n : throw new InvalidDataException($"A list in the snapshot is longer than {most}.");

    private static int ToInt(long v) =>
        v is >= int.MinValue and <= int.MaxValue ? (int)v : throw new InvalidDataException("A number in the snapshot is out of range.");

    private static uint ToUInt(long v) =>
        v is >= 0 and <= uint.MaxValue ? (uint)v : throw new InvalidDataException("A number in the snapshot is out of range.");

    private static uint ToUInt(ulong v) =>
        v <= uint.MaxValue ? (uint)v : throw new InvalidDataException("A number in the snapshot is out of range.");

    private static short ToShort(long v) =>
        v is >= short.MinValue and <= short.MaxValue ? (short)v : throw new InvalidDataException("A number in the snapshot is out of range.");
}
