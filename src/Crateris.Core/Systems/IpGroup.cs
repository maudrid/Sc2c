// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (grpinfo.h's IP_GROUP, the ELEMENT
// ipdisp.c's spawn_ip_group gives each, and the group files of grpinfo.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>
/// A group of alien ships flying in a system (UQM's IP_GROUP, in
/// ip_group_q; docs/plans/stage-3b.md, item 4): its group in the system's
/// draw (<see cref="GroupId"/>, group_id), its race (races.h's number),
/// where it is (<see cref="SysLoc"/>: 0 the outer system, else 1 + the
/// planet whose inner system; <see cref="LocX"/>, <see cref="LocY"/> in
/// that view's units), its task (<see cref="InOrbit"/>,
/// <see cref="Explore"/>, <see cref="Flee"/> or <see cref="OnStation"/>,
/// with <see cref="IgnoreFlagship"/>: standing down, and
/// <see cref="ReformGroup"/>: re-forming after a meeting), where it's
/// going (<see cref="DestLoc"/>: a world's 1 + planet, or
/// <see cref="InterceptPlayer"/>), its place in its orbit
/// (<see cref="OrbitPos"/>, a facing; while intercepting, the destination
/// it had), its counter (revolutions left, or the re-forming's frames),
/// and whether it's still in the system. While a player flies in the
/// system it has an element (<see cref="Element"/>).
/// </summary>
public sealed class IpGroup
{
    public const byte InOrbit = 0, Explore = 1, Flee = 2, OnStation = 3;
    public const byte IgnoreFlagship = 1 << 2, ReformGroup = 1 << 3;

    /// <summary>IPNL_INTERCEPT_PLAYER: a destination that's the player (and a fleeing group's, out of the system).</summary>
    public const byte InterceptPlayer = 0;

    /// <summary>IPNL_ALL_CLEAR: NotifyOthers' standing down.</summary>
    public const byte AllClear = 255;

    public int GroupId { get; internal set; }
    public int Race { get; internal set; }
    public ushort GroupCounter { get; internal set; }
    public byte SysLoc { get; internal set; }
    public byte Task { get; internal set; }
    public byte DestLoc { get; internal set; }
    public byte OrbitPos { get; internal set; }
    public bool InSystem { get; internal set; } = true;
    public short LocX { get; internal set; }
    public short LocY { get; internal set; }

    /// <summary>Its element while a player flies in the system, or null.</summary>
    public IpGroupElement? Element { get; internal set; }

    // The flyer who met it this frame (decision 5: it meets one player).
    internal object? MetBy;

    public IpGroup Clone()
    {
        var c = (IpGroup)MemberwiseClone();
        c.Element = Element?.Clone();
        c.MetBy = null;
        return c;
    }
}

/// <summary>
/// The element spawn_ip_group gives a group (process.c's ELEMENT, the
/// parts a group's has): its state flags (UQM's, <see cref="ElementFlags"/>),
/// its life span, its steering's wait (thrust_wait), its velocity, where
/// it is on the screen of its view (<see cref="Cur"/>: current.location,
/// <see cref="Next"/>: next.location, display units without the space's
/// offset), where its group was as the frame began (the display
/// primitive's point, which a group stopped by another goes back to), and
/// whether it's drawn (its primitive a stamp).
/// </summary>
public sealed class IpGroupElement
{
    public ElementFlags Flags;
    public int LifeSpan;
    public byte ThrustWait;
    public Velocity Velocity;
    public IpPoint Cur, Next, Point;
    public bool Drawn = true;
    internal IntersectControl Control = new();

    /// <summary>
    /// The zoom radius its screen points are at this frame
    /// (zoomRadiusForLocation for the player it goes by).
    /// </summary>
    internal int Radius = IpGeometry.MaxZoomRadius;

    public IpGroupElement Clone()
    {
        var c = (IpGroupElement)MemberwiseClone();
        c.Control = Control.Copy();
        return c;
    }
}

/// <summary>
/// A group as a system's draw keeps it (a group of grpinfo.c's files):
/// its number in the draw, its race and each ship's crew and captain
/// (SHIP_FRAGMENT's crew_level and captains_name_index). None left is a
/// group that's dead.
/// </summary>
public sealed class GroupShips
{
    public GroupShips(int id, int race, IEnumerable<int> ships, IEnumerable<int>? captains = null)
    {
        Id = id;
        Race = race;
        Ships = ships.ToList();
        Captains = captains?.ToList() ?? Ships.Select(_ => 0).ToList();
        if (Captains.Count != Ships.Count)
            throw new ArgumentException("a captain for each ship");
    }

    public int Id { get; }
    public int Race { get; }
    public List<int> Ships { get; private set; }
    public List<int> Captains { get; private set; }

    /// <summary>No ships left: the group is dead.</summary>
    public void Kill()
    {
        Ships.Clear();
        Captains.Clear();
    }

    public GroupShips Clone() => new(Id, Race, Ships, Captains);
}

/// <summary>A date of the game's calendar (the clock's).</summary>
public readonly record struct GameDate(int Year, int Month, int Day) : IComparable<GameDate>
{
    public int CompareTo(GameDate o) => (Year, Month, Day).CompareTo((o.Year, o.Month, o.Day));

    /// <summary>The date some days on (ValidateEvent's RELATIVE_EVENT).</summary>
    public GameDate Plus(int days)
    {
        int y = Year, m = Month, d = Day + days;
        while (d > World.GameClock.DaysInMonth(m, y))
        {
            d -= World.GameClock.DaysInMonth(m, y);
            if (++m > 12)
            {
                m = 1;
                ++y;
            }
        }
        return new GameDate(y, m, d);
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2}";
}
