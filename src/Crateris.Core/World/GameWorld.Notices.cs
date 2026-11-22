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
/// decision 11): each event told once to every player present but the one
/// who set it off (the owner, 2026-11-21: no nearness, to be added if it
/// proves too much), as a <see cref="WorldNotice"/>; those it touches (<see
/// cref="IsNear"/>) in words of their own ("turn on you", "chasing you"),
/// the others in words for anyone. With one player nobody is told anything
/// new: the game plays as UQM's.
/// </summary>
public sealed partial class GameWorld
{
    private readonly List<WorldNotice> _worldNotices = new();

    // A battle's race to turn on the players in a system (DoMissions, as
    // its EncounterRace), whose meeting it was and who fought it (not told:
    // their own doing). Not saved: after a load the turning tells nobody.
    private readonly Dictionary<int, (int Race, string By, HashSet<string> Fighters)> _turnOnAfter = new();

    /// <summary>The notices since the last call, each for one player, in the order they came.</summary>
    public List<WorldNotice> TakeNotices()
    {
        var all = _worldNotices.ToList();
        _worldNotices.Clear();
        return all;
    }

    /// <summary>
    /// Every player told of an event (the owner, 2026-11-21: everyone, no
    /// nearness): present (a player away isn't told: by the time they're
    /// back it's stale), wrecks too, but the one who set it off.
    /// </summary>
    internal IEnumerable<Player> Others(string but) =>
        _players.Where(p => p.Id != but && p.Present).ToList();

    /// <summary>
    /// Whether an event touches a player, who is then told it in words of
    /// their own: not a wreck (it doesn't fly: told as anyone), and at a
    /// star, their ship is in its system, in any view (the
    /// race's standing there is the system's, decision 8); in hyperspace
    /// (<paramref name="star"/> null), they're in hyperspace within the span
    /// of (<paramref name="x"/>, <paramref name="y"/>) (universe units):
    /// hyper.c's XOFFS and YOFFS, the span a player keeps the groups round
    /// them by (<see cref="HyperEncounters.SpanX"/>).
    /// </summary>
    internal bool IsNear(Player p, int? star, short x, short y) =>
        !p.Ship.Destroyed
        && (star is int s
            ? p.Ship.SystemStar == s
            : InHyperspace(p) && Math.Abs(HyperspaceFlightOf(p).UniverseX - x) <= HyperEncounters.SpanX
                              && Math.Abs(HyperspaceFlightOf(p).UniverseY - y) <= HyperEncounters.SpanY);

    // A player's name as notices give it ("Someone" for nobody's: a story talk's).
    private string NameOf(string id) => Find(id)?.Name ?? (id.Length == 0 ? "Someone" : id);

    private string RaceNameOf(int race) => Meeting.RaceName(Story!.Rules.Races[race]);

    private void Notice(Player to, NoticeEvent ev, string by, string race, int count = 0) =>
        _worldNotices.Add(new WorldNotice(to.Id, ev, NameOf(by), race, count));

    // How many of each talk's events the others have been told of (TellTalkEffects).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Comm.Talk, System.Runtime.CompilerServices.StrongBox<int>> _talkTold = new();

    /// <summary>
    /// What a talk did to the group (the stage 3b-2 plan's item 1, 3b-1's
    /// decision 11), told to every player but its talker as it runs, once
    /// each: an alliance made or ended (SetRaceAllied, where the standing
    /// changed), ships given to the host's fleet (AddEscortShips) or taken
    /// from it (RemoveEscortShips). Called after each step of a meeting's
    /// talk and a visit's ordinary talks; a story talk (the opening's, the
    /// allied commander's on arrival) is shown to everyone already, so
    /// nobody is told it again.
    /// </summary>
    internal void TellTalkEffects(Player talker, Comm.Talk talk)
    {
        var told = _talkTold.GetValue(talk, _ => new System.Runtime.CompilerServices.StrongBox<int>(0));
        var events = talk.Events;
        for (; told.Value < events.Count; ++told.Value)
        {
            (NoticeEvent Event, int Race, int Count)? notice = events[told.Value] switch
            {
                Comm.RaceAllied { Changed: true } a => (a.Flag ? NoticeEvent.AllianceMade : NoticeEvent.AllianceBroken, a.Race, 0),
                Comm.EscortsAdded { Added: > 0 } a => (NoticeEvent.ShipsGiven, a.Race, a.Added),
                Comm.EscortsRemoved { Removed: > 0 } r => (NoticeEvent.ShipsTaken, r.Race, r.Removed),
                _ => null,
            };
            if (notice is not { } n)
                continue;
            foreach (var p in Others(talker.Id))
                Notice(p, n.Event, talker.Id, RaceNameOf(n.Race), n.Count);
        }
    }

    // Whether a meeting's event touches a player (near where they met the group).
    private bool NearMeeting(Meeting m, Player p)
    {
        var f = m.Player.Ship.Flight;
        return IsNear(p, m.Star, f.UniverseX, f.UniverseY);
    }

    // A meeting begins: in a system the race's groups stand down for
    // everyone (decision 8), and the others there are told so (not at a
    // world whose generator doesn't stand them down); everyone else is
    // told who's meeting them (the race's groups pass through them
    // meanwhile, decision 5).
    private void TellMeetingBegun(Meeting m)
    {
        bool standDown = m.Star is not null && m.WorldTalk is not { StandDown: false };
        foreach (var p in Others(m.Player.Id))
            Notice(p, standDown && NearMeeting(m, p) ? NoticeEvent.HoldOff : NoticeEvent.Meeting,
                m.Player.Id, RaceNameOf(m.Race));
    }

    // A meeting ends (RaceCommunication's end): the others are told how
    // it ended: a surrender; the group destroyed at once (the instant
    // victory: one that wins nothing, while the Ur-Quan guard the Sa-Matra,
    // as peace); a talk ending hostile with no battle to fight (the game
    // over, no battle's content, ships not fought) as over; else peace; a
    // battle's start is told as the battle starts (StartBattle), its end to
    // nobody. Those the group had flown at, near it still and not in its
    // battle, are told instead if it goes (<paramref name="gone"/>: in
    // hyperspace written back one-shot or with no ships; in a system none
    // left, or sent off by the talk, the drone): destroyed in the battle,
    // destroyed at once by the talk, gone after a battle it survived (a
    // group one-shot from its roll: the probes), gone after a meeting ending
    // hostile with no battle (told over to the others), or talked away;
    // after a surrender they're told the surrender as everyone else is.
    private void TellMeetingEnded(Meeting m, bool fought, bool gone, IReadOnlyCollection<string> chased)
    {
        bool surrendered = m.Talk is { Segue: Comm.Segue.Defeat };
        bool hostile = m.Talk is { } talk ? talk.Segue is Comm.Segue.Hostile : !m.Hailed;
        bool noneLeft = m.Left is { Count: 0 };
        NoticeEvent? ended = surrendered ? NoticeEvent.Surrendered
            : m.InstantVictory ? (noneLeft ? NoticeEvent.Destroyed : NoticeEvent.Peace)
            : fought ? null
            : hostile ? NoticeEvent.MeetingOver
            : NoticeEvent.Peace;
        NoticeEvent chaser = m.Battle is not null
            ? (noneLeft ? NoticeEvent.ChaserDestroyedInBattle : NoticeEvent.ChaserGoneAfterBattle)
            : m.InstantVictory ? (noneLeft ? NoticeEvent.ChaserDestroyed : NoticeEvent.ChaserTalkedAway)
            : ended is NoticeEvent.MeetingOver ? NoticeEvent.ChaserGoneAfterMeeting
            : NoticeEvent.ChaserTalkedAway;
        var fighters = m.Battle?.Fighters.Select(f => f.Player.Id).ToHashSet() ?? new HashSet<string>();
        string race = RaceNameOf(m.Race);
        foreach (var p in Others(m.Player.Id))
        {
            if (gone && !surrendered && chased.Contains(p.Id) && !fighters.Contains(p.Id) && NearMeeting(m, p))
                Notice(p, chaser, m.Player.Id, race);
            else if (ended is { } ev)
                Notice(p, ev, m.Player.Id, race);
        }
        // A battle's race turns on the players in the system as it comes
        // back (DoMissions): told then to those not in the battle.
        if (fought && m.Star is int star)
        {
            fighters.Add(m.Player.Id);
            _turnOnAfter[star] = (m.Race, m.Player.Id, fighters);
        }
    }

    // A battle starts where a player's ship is: the others are told who's
    // fighting whom (a meeting's ending in a battle, or any other).
    private void TellBattleStarted(Player player, EnemyFleet fleet)
    {
        string race = player.Ship.Meeting is { } m ? RaceNameOf(m.Race)
            : Story?.Rules.RaceOfShip(fleet.Ships[0].Ship) is { } r ? Meeting.RaceName(r.Race)
            : fleet.Ships[0].Ship;
        foreach (var p in Others(player.Id))
            Notice(p, NoticeEvent.Fighting, player.Id, race);
    }

    // After a system's frame, before its meetings: the races its groups
    // turned on the players (SystemDraw.Turned) and still turned: on
    // spotting a player, the others told who was spotted (those there that
    // the race turns on them); after a battle (DoMissions), those who
    // weren't in it told the race's ships turn on them (there) or on the
    // players there (elsewhere).
    private void TellTurned(SystemDraw d)
    {
        foreach (var (race, spotted) in d.Turned)
        {
            // After a battle: who fought it, taken whether it's told or not.
            (int Race, string By, HashSet<string> Fighters)? battle = null;
            if (spotted is null && _turnOnAfter.Remove(d.Star, out var a))
                battle = a;
            // Only a turning that stands at the frame's end (a touch later in
            // the frame may have stood the race down again: told as that).
            if (!d.Groups.Any(g => g.Race == race && (g.Task & IpGroup.IgnoreFlagship) == 0
                                   && g.DestLoc == IpGroup.InterceptPlayer))
                continue;
            if (spotted is not null)
            {
                foreach (var p in Others(spotted))
                    Notice(p, IsNear(p, d.Star, 0, 0) ? NoticeEvent.Spotted : NoticeEvent.SpottedThere, spotted, RaceNameOf(race));
            }
            else if (battle is { } after && after.Race == race)
            {
                foreach (var p in Others("").Where(p => !after.Fighters.Contains(p.Id)))
                    Notice(p, IsNear(p, d.Star, 0, 0) ? NoticeEvent.TurnOnYou : NoticeEvent.TurnOnThem, after.By, RaceNameOf(race));
            }
        }
        d.Turned.Clear();
    }

    // A battle's turning that won't come: its system's draw no longer
    // waits to turn the race (EncounterRace cleared as the last player
    // left, or a new draw), or the system's gone. Before the systems' frames.
    private void ForgetTurnings()
    {
        foreach (int star in _turnOnAfter.Keys.ToList())
        {
            if (SystemGroups is not { } sg || !sg.Systems.TryGetValue(star, out var d) || d.EncounterRace < 0)
                _turnOnAfter.Remove(star);
        }
    }
}
