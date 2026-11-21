// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// Notices of what other players set off (docs/plans/stage-3b.md, item 9,
/// decision 11): each event told once to each player near it (not the one
/// who set it off), as a <see cref="WorldNotice"/>. With one player nobody
/// is told anything new: the game plays as UQM's.
/// </summary>
public sealed partial class GameWorld
{
    private readonly List<WorldNotice> _worldNotices = new();

    // A battle's race to turn on the players in a system (DoMissions, as
    // its EncounterRace) and who fought it (not told: their own doing).
    // Not saved: after a load the turning tells nobody.
    private readonly Dictionary<int, (int Race, HashSet<string> Fighters)> _turnOnAfter = new();

    /// <summary>The notices since the last call, each for one player, in the order they came.</summary>
    public List<WorldNotice> TakeNotices()
    {
        var all = _worldNotices.ToList();
        _worldNotices.Clear();
        return all;
    }

    /// <summary>
    /// The players near a place, but one (who set the event off): present,
    /// not wrecks; at a star, everyone whose ship is in its system, in any
    /// view (the race's standing there is the system's, decision 8); in
    /// hyperspace (<paramref name="star"/> null), everyone in hyperspace
    /// within the span of (<paramref name="x"/>, <paramref name="y"/>)
    /// (universe units): hyper.c's XOFFS and YOFFS, the span a player keeps
    /// the groups round them by (<see cref="HyperEncounters.SpanX"/>).
    /// </summary>
    internal IEnumerable<Player> NearOthers(string but, int? star, short x, short y) =>
        _players.Where(p => p.Id != but && p.Present && !p.Ship.Destroyed
                            && (star is int s
                                ? p.Ship.SystemStar == s
                                : InHyperspace(p) && Math.Abs(HyperspaceFlightOf(p).UniverseX - x) <= HyperEncounters.SpanX
                                                  && Math.Abs(HyperspaceFlightOf(p).UniverseY - y) <= HyperEncounters.SpanY));

    // A player's name as notices give it ("Someone" for nobody's: a story talk's).
    private string NameOf(string id) => Find(id)?.Name ?? (id.Length == 0 ? "Someone" : id);

    private string RaceNameOf(int race) => Meeting.RaceName(Story!.Rules.Races[race]);

    private void Notice(Player to, NoticeEvent ev, string by, string race) =>
        _worldNotices.Add(new WorldNotice(to.Id, ev, NameOf(by), race));

    // An event told to everyone near a meeting's player (where they met the group).
    private List<Player> NearMeeting(Meeting m)
    {
        var f = m.Player.Ship.Flight;
        return NearOthers(m.Player.Id, m.Star, f.UniverseX, f.UniverseY).ToList();
    }

    // A meeting begins: in a system the race's groups stand down for
    // everyone (decision 8), and the others there are told; in hyperspace
    // the others near are told who's meeting them (the race's groups pass
    // through them meanwhile, decision 5).
    private void TellMeetingBegun(Meeting m)
    {
        var ev = m.Star is null ? NoticeEvent.Meeting : NoticeEvent.HoldOff;
        foreach (var p in NearMeeting(m))
            Notice(p, ev, m.Player.Id, RaceNameOf(m.Race));
    }

    // A meeting ends (RaceCommunication's end): the others near are told how
    // (peace, a surrender, the group destroyed at once; a battle's start is
    // told as the battle starts, StartBattle). Those the group had flown at,
    // near still and not in its battle, are told instead if it goes: talked
    // away (in hyperspace one-shot after a talk; in a system sent off, the
    // drone) or destroyed (none of its ships left).
    private void TellMeetingEnded(Meeting m, bool fought, bool gone, IReadOnlyCollection<string> chased)
    {
        bool surrendered = m.Talk is { Segue: Comm.Segue.Defeat };
        NoticeEvent? ended = surrendered ? NoticeEvent.Surrendered
            : m.InstantVictory ? (gone ? NoticeEvent.Destroyed : NoticeEvent.Peace)
            : fought ? null
            : NoticeEvent.Peace;
        var fighters = m.Battle?.Fighters.Select(f => f.Player.Id).ToHashSet() ?? new HashSet<string>();
        string race = RaceNameOf(m.Race);
        foreach (var p in NearMeeting(m))
        {
            if (gone && !surrendered && chased.Contains(p.Id) && !fighters.Contains(p.Id))
                Notice(p, fought ? NoticeEvent.ChaserDestroyed : NoticeEvent.ChaserTalkedAway, m.Player.Id, race);
            else if (ended is { } ev)
                Notice(p, ev, m.Player.Id, race);
        }
        // A battle's race turns on the players in the system as it comes
        // back (DoMissions): told then to those not in the battle.
        if (fought && m.Star is int star)
        {
            fighters.Add(m.Player.Id);
            _turnOnAfter[star] = (m.Race, fighters);
        }
    }

    // A battle starts where a player's ship is: the others near are told
    // who's fighting whom (a meeting's ending in a battle, or any other).
    private void TellBattleStarted(Player player, EnemyFleet fleet)
    {
        string race = player.Ship.Meeting is { } m ? RaceNameOf(m.Race)
            : Story?.Rules.RaceOfShip(fleet.Ships[0].Ship) is { } r ? Meeting.RaceName(r.Race)
            : fleet.Ships[0].Ship;
        var f = player.Ship.Flight;
        foreach (var p in NearOthers(player.Id, player.Ship.SystemStar, f.UniverseX, f.UniverseY).ToList())
            Notice(p, NoticeEvent.Fighting, player.Id, race);
    }

    // After a system's frame, before its meetings: the races its groups
    // turned on the players (SystemDraw.Turned) and still turned: on
    // spotting a player, the others there told who
    // was spotted; after a battle (DoMissions), those there who weren't in
    // it told the race's ships turn on them.
    private void TellTurned(SystemDraw d)
    {
        foreach (var (race, spotted) in d.Turned)
        {
            // Only a turning that stands at the frame's end (a touch later in
            // the frame may have stood the race down again: told as that).
            if (!d.Groups.Any(g => g.Race == race && (g.Task & IpGroup.IgnoreFlagship) == 0
                                   && g.DestLoc == IpGroup.InterceptPlayer))
                continue;
            if (spotted is not null)
            {
                foreach (var p in NearOthers(spotted, d.Star, 0, 0).ToList())
                    Notice(p, NoticeEvent.Spotted, spotted, RaceNameOf(race));
            }
            else if (_turnOnAfter.Remove(d.Star, out var after) && after.Race == race)
            {
                foreach (var p in NearOthers("", d.Star, 0, 0).Where(p => !after.Fighters.Contains(p.Id)).ToList())
                    Notice(p, NoticeEvent.TurnOnYou, "", RaceNameOf(race));
            }
        }
        d.Turned.Clear();
    }
}
