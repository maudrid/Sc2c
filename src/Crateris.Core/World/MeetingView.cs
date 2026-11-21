// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/encount.c: InitEncounter, the
// encounter screen: where, the group's ships, Hail or Attack), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;

namespace Crateris.Core.World;

/// <summary>
/// A player's meeting with a group as their client is shown it (stage
/// 3b-1 item 10; item 11 draws UQM's encounter screen from it): which
/// meeting (<see cref="Id"/>, which the encounter screen's choice is sent
/// back with), its step (<see cref="MeetingStep"/>: the spin into the
/// group, the encounter screen offering Hail or Attack, the talk, the
/// battle), the race met (<see cref="Race"/>, data/races.json's id), the
/// ships the screen shows (npc_built_ship_q: each one's ship and crew, null
/// for its race's own), whether they're an infinite fleet (a race's world's:
/// the screen repeats its one ship), and where (InitEncounter's "ENCOUNTER
/// IN DEEP SPACE" in hyperspace, <see cref="Star"/> null; "ENCOUNTER AT" a
/// system, and at a race's world, <see cref="World"/>). The talk itself
/// goes as a talk's view (<see cref="Comm.TalkView.Of(Meeting?)"/>), the
/// battle in the snapshot, its tally in the battle's notice. Sent to the
/// player met when it changes, as a reliable message;
/// <see cref="Protocol.NoView"/> means no meeting.
/// </summary>
public sealed record MeetingView(int Id, MeetingStep Step, string Race, IReadOnlyList<EnemyFleetShip> Ships, bool Infinite = false,
    int? Star = null, int? Planet = null, int? Moon = null)
{
    /// <summary>The most ships a view carries (a group in hyperspace has 7 at most, a system's no more).</summary>
    public const int MostShips = 64;

    /// <summary>A meeting as its player is shown it: null for none, or one that's over.</summary>
    public static MeetingView? Of(Meeting? m) =>
        m is null || m.Step == MeetingStep.Done ? null
        : new MeetingView(m.Id, m.Step, m.RaceId, m.Ships.Select(s => new EnemyFleetShip(s.Ship, s.Crew)).ToList(), m.Infinite,
            m.Star, m.World?.Planet, m.World?.Moon);

    /// <summary>Whether the encounter screen is up, offering Hail or Attack.</summary>
    public bool OffersChoice => Step == MeetingStep.Encounter;

    public byte[] Encode()
    {
        var w = new WireWriter();
        w.UInt((ulong)Id);
        w.UInt((ulong)Step + 1);
        w.String(Race);
        int count = Math.Min(Ships.Count, MostShips);
        w.UInt((ulong)count);
        for (int i = 0; i < count; ++i)
        {
            w.String(Ships[i].Ship);
            w.UInt(Ships[i].Crew is int c ? (ulong)c + 1 : 0);
        }
        w.Byte(Infinite ? (byte)1 : (byte)0);
        w.UInt(Star is int s ? (ulong)s + 1 : 0);
        w.UInt(Planet is int p ? (ulong)p + 1 : 0);
        w.UInt(Moon is int mo ? (ulong)mo + 1 : 0);
        return w.Written.ToArray();
    }

    /// <summary>A view from the host; null for none. A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static MeetingView? Decode(ReadOnlySpan<byte> data)
    {
        if (Protocol.IsNoView(data))
            return null;
        var r = new WireReader(data);
        ulong id = r.UInt(), step = r.UInt();
        if (id == 0 || id > int.MaxValue || step == 0 || step - 1 > (ulong)MeetingStep.Battle)
            throw new InvalidDataException("Bad meeting.");
        string race = r.String() is { Length: > 0 } rc ? rc : throw new InvalidDataException("A meeting has no race.");
        ulong count = r.UInt();
        if (count > MostShips)
            throw new InvalidDataException($"A meeting has more than {MostShips} ships.");
        var ships = new List<EnemyFleetShip>((int)count);
        for (ulong i = 0; i < count; ++i)
        {
            string ship = r.String() is { Length: > 0 } sh ? sh : throw new InvalidDataException("A meeting's ship has no kind.");
            ulong crew = r.UInt();
            if (crew > ushort.MaxValue)
                throw new InvalidDataException("Bad meeting ship crew.");
            ships.Add(new EnemyFleetShip(ship, crew == 0 ? null : (int)crew - 1));
        }
        bool infinite = r.Byte() switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Bad meeting fleet."),
        };
        int? Optional(ref WireReader r)
        {
            ulong v = r.UInt();
            return v == 0 ? null : v - 1 <= int.MaxValue ? (int)(v - 1) : throw new InvalidDataException("Bad meeting place.");
        }
        int? star = Optional(ref r), planet = Optional(ref r), moon = Optional(ref r);
        if (!r.AtEnd)
            throw new InvalidDataException("A meeting has bytes after its end.");
        return new MeetingView((int)id, (MeetingStep)(int)(step - 1), race, ships, infinite, star, planet, moon);
    }

    /// <summary>Whether two views show the same (records compare their lists by reference).</summary>
    public bool Same(MeetingView? other) =>
        other is not null && Id == other.Id && Step == other.Step && Race == other.Race && Ships.SequenceEqual(other.Ships)
        && Infinite == other.Infinite && Star == other.Star && Planet == other.Planet && Moon == other.Moon;
}
