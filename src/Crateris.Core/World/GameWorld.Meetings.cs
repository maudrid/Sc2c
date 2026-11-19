// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/comm.c: RaceCommunication,
// InitCommunication; src/uqm/encount.c: BuildBattle, EncounterBattle,
// UninitEncounter; src/uqm/hyper.c: encounter_collision, init_transition,
// unhyper_transition, cleanup_hyperspace, hyper_transition;
// src/uqm/starcon.c: the main loop's return to hyperspace after an
// encounter), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Core.World;

/// <summary>
/// Meeting a group (docs/plans/stage-3b.md, item 5): the player whose
/// ship touches a group meets it alone (decision 5), in RaceCommunication's
/// order (<see cref="Meeting"/>).
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// The talk of a race met in space (by races.h's number), with the
    /// answerer's ship, new and not yet loaded; null while the race's talk
    /// isn't ported (all of them until items 6 to 8; docs/differences.md row
    /// 68): the meeting then shows the encounter screen, a hail ending at
    /// once as a talk ending in peace. Tests give their own.
    /// </summary>
    internal Func<Player, int, uint, Talk?>? MeetingTalks { get; set; }

    // Who has been told that another player is talking to a race (decision
    // 5): told once while that player holds the lock.
    private readonly HashSet<(string Player, string Holder, string Conversation)> _toldTalking = new();

    // The conversation a group of a race starts, if any.
    private string? ConversationOf(int race) =>
        Story is { } story ? DialogueLocks?.ConversationOf(story.Rules.Races[race]) : null;

    /// <summary>
    /// Whether a player may meet a group of a race now: not while another
    /// player holds the race's dialogue lock (decision 5: the group passes
    /// through them, and they're told who's talking).
    /// </summary>
    internal bool MayMeet(Player player, int race) =>
        ConversationOf(race) is not { } c || DialogueLocks!.Holder(c) is not { } holder || holder == player.Id;

    // The player is told once who is talking to the race of a group passing through them.
    private void TellWhoIsTalking(Player player, int race)
    {
        if (ConversationOf(race) is not { } c || DialogueLocks!.Holder(c) is not { } holder || holder == player.Id)
            return;
        if (!_toldTalking.Add((player.Id, holder, c)))
            return;
        string who = Find(holder)?.Name ?? holder;
        _battleNotices.Add((player.Id, $"{who} is talking to the {Meeting.RaceName(Story!.Rules.Races[race])}."));
    }

    // Forget who was told about locks let go since.
    private void ForgetToldTalking() =>
        _toldTalking.RemoveWhere(t => DialogueLocks?.Holder(t.Conversation) != t.Holder);

    // A group's ships as the battle takes them: the race's ship with each one's crew (and captain).
    private IReadOnlyList<EnemyFleetShip> FleetShips(int race, IEnumerable<(int Crew, int? Captain)> ships)
    {
        string ship = Story!.Rules.RaceShips[race].Ship;
        return ships.Select(s => new EnemyFleetShip(ship, s.Crew, s.Captain)).ToList();
    }

    // ---- hyperspace

    /// <summary>
    /// A player's ship touched a group in hyperspace (encounter_collision):
    /// the group stops where it is, the player's meeting begins, and the
    /// ship spins into it (init_transition), still in hyperspace for the
    /// encounters until the spin's last frame (as into a star:
    /// <see cref="PlayerShip.Spin"/>). The player's dialogue lock for the
    /// race is taken now, so the race's groups pass through others meanwhile.
    /// </summary>
    private void MeetInHyperspace(Player player, HyperEncounter met)
    {
        // encounter_collision: every group's element made non-solid (each
        // on a screen is solid again on the next frame, tested from where
        // it is then), the one met stopped and marked.
        foreach (var e in Encounters!.List)
            if (e.Element is { } other)
                other.Nonsolid = true;
        var el = met.Element!;
        el.Met = player.Id;
        el.Velocity.Zero();
        met.MeetingPlayer = player.Id;
        HyperspaceMeetings.Add((Frame, player.Id, met.Race));
        var ship = player.Ship;
        int frames = Shapes!.HyperFlagship.Count;
        ship.Spin = StarEntry.Spin(ship.Flight, el.NextX, el.NextY, frames);
        var m = new Meeting(player, met.Race, Story!.Rules.Races[met.Race],
            FleetShips(met.Race, met.Ships.Select(c => (c, (int?)null))), NextRound())
        {
            Encounter = met,
            Step = MeetingStep.Spin,
            SpinEnds = Frame + StarEntry.Turns(ship.Flight.Facing, frames) + 1,
        };
        TakeMeetingLock(m);
        ship.Meeting = m;
        HoldInput(ship, m.SpinEnds + Settings.InputDelayFrames);
    }

    // A frame of the ship's spin into the group: the drift, or on its last
    // frame (unhyper_transition's cleanup_hyperspace) out of hyperspace where
    // the spin took it, the group met at the head of the list (for
    // RaceCommunication), and the meeting goes on.
    private void SpinIntoGroup(Player player, Meeting m)
    {
        var ship = player.Ship;
        if (Frame < m.SpinEnds)
        {
            ship.Spin!.Move();
            return;
        }
        ship.Flight.TakePlaceOf(ship.Spin!);
        ship.Spin = null;
        if (Encounters is { } encounters && MetEncounter(m) is { } e)
        {
            encounters.List.Remove(e);
            encounters.List.Insert(0, e);
        }
        BeginTalk(m);
    }

    // The group a hyperspace meeting met (a list taken back keeps it as a copy).
    private HyperEncounter? MetEncounter(Meeting m) =>
        Encounters is not { } encounters ? null
        : encounters.List.Contains(m.Encounter!) ? m.Encounter
        : encounters.List.FirstOrDefault(e => e.MeetingPlayer == m.Player.Id);

    /// <summary>
    /// hyper_transition's frames as hyperspace starts again without a loaded
    /// game: the flagship spins back in from its last frame to its facing,
    /// unsteered, still and untouchable, before it flies.
    /// </summary>
    internal static int AppearingFrames(int facing, int frameCount) =>
        frameCount - UqmMath.Facings + 2 + UqmMath.NormalizeFacing(UqmMath.Facings - facing);

    // ---- systems

    // A player's ship touched a group in a system (ip_group_collision: the
    // touch, the re-forming and the standing down done): the meeting begins
    // at once (UninitSolarSys' GetGroupInfo, BuildBattle's escape counter).
    private void MeetInSystem(SystemGroups sg, SystemDraw d, SystemFlyer f)
    {
        var g = f.Met!;
        var player = Find(f.Id)!;
        SystemMeetings.Add((Frame, f.Id, d.Star, g.Race, g.GroupId));
        var ships = sg.StartMeeting(d, g, f.Id);
        var m = new Meeting(player, g.Race, Story!.Rules.Races[g.Race],
            FleetShips(g.Race, ships is null ? Array.Empty<(int, int?)>()
                : ships.Ships.Zip(ships.Captains, (c, n) => (c, (int?)n))), NextRound())
        {
            Star = d.Star,
            Group = g,
        };
        if (f.Flight.Orbit is null)
            f.Flight.EscapeCounter = IpFlight.EscapeFrames;
        TakeMeetingLock(m);
        player.Ship.Meeting = m;
        BeginTalk(m);
    }

    // ---- the talk

    private void TakeMeetingLock(Meeting m)
    {
        if (ConversationOf(m.Race) is { } c && DialogueLocks!.TryTake(c, m.Player.Id, out _))
            m.Conversation = c;
    }

    private void ReleaseMeetingLock(Meeting m)
    {
        if (m.Conversation is { } c)
            DialogueLocks?.Release(c, m.Player.Id);
        m.Conversation = null;
    }

    // InitCommunication: the race's sphere tracked (not the drone's), the
    // talk loaded (its init sets whether it's hostile), then the encounter
    // screen if it is, else the hail at once.
    private void BeginTalk(Meeting m)
    {
        if (m.RaceId != "URQUAN_DRONE_SHIP")
            Story!.StartSphereTracking(m.RaceId);
        uint seed = unchecked((uint)Frame * 2_654_435_761u + (uint)m.Race * 40_503u + 0x4D454554u);
        foreach (char ch in m.Player.Id)
            seed = unchecked(seed * 31 + ch);
        // The talk's own random seed (decision 12), as a visit's talks have.
        var talk = MeetingTalks?.Invoke(m.Player, m.Race, seed);
        m.Talk = talk;
        talk?.Load();
        if (talk is null || talk.OffersAttack)
        {
            m.Step = MeetingStep.Encounter;
            return;
        }
        m.Step = MeetingStep.Talk;
        m.Hailed = true;
        talk.Hail();
        if (talk.Ended)
            TalkOver(m);
    }

    /// <summary>
    /// The encounter screen's choice (InitEncounter's DoSelectAction): hail,
    /// and the race's talk runs (under its lock); attack, and the battle
    /// starts against the group's ships. False, changing nothing, if the
    /// player isn't at the encounter screen, or the game is paused or over.
    /// </summary>
    public bool EncounterChoice(string id, EncounterAction action)
    {
        if (Held || StoryPaused || Find(id) is not { Present: true, Ship.Meeting: { Step: MeetingStep.Encounter } m }
            || !Enum.IsDefined(action))
            return false;
        if (action == EncounterAction.Hail)
        {
            m.Hailed = true;
            m.Step = MeetingStep.Talk;
            if (m.Talk is { } talk)
            {
                talk.Hail();
                if (!talk.Ended)
                    return true;
            }
        }
        else
            m.Talk?.Attack();
        TalkOver(m);
        return true;
    }

    // A meeting's talk chooses a reply: false if none of that round is offered.
    private bool ChooseMeetingReply(Meeting m, int index, int? round)
    {
        if (m.Talk is not { Ended: false } talk || round is int r && r != m.Round
            || index < 0 || index >= talk.Replies.Count)
            return false;
        if (HostPaused)
        {
            _heldReplies.RemoveAll(h => h.Id == m.Player.Id);
            _heldReplies.Add((m.Player.Id, index, m.Round));
            return true;
        }
        talk.Choose(index);
        _rounds = Math.Max(_rounds, m.Round);
        if (talk.Ended)
            TalkOver(m);
        return true;
    }

    // The talk is over (InitCommunication after it): its lock let go; hostile
    // (an attack, or a talk ending in battle) and with ships to fight, the
    // battle (BuildBattle, EncounterBattle); else the meeting ends as a talk
    // without a battle does.
    private void TalkOver(Meeting m)
    {
        _rounds = Math.Max(_rounds, m.Round);
        ReleaseMeetingLock(m);
        bool hostile = m.Talk is { } talk ? talk.Segue is Segue.Hostile or Segue.Victory : !m.Hailed;
        if (hostile && m.Ships.Count > 0 && BattleKit is not null
            && m.Ships.All(s => BattleKit.Enemies.Contains(s.Ship)) && m.Player.Present && !GameOver)
        {
            m.Step = MeetingStep.Battle;
            m.Battle = StartBattle(m.Player.Id, new EnemyFleet(m.Ships));
            return;
        }
        EndMeeting(m, fought: false);
    }

    // ---- the end

    /// <summary>
    /// After the battles' frame: a meeting's player back in flight from its
    /// battle (retreated, or the battle over) spins back into hyperspace; a
    /// meeting whose battle is over ends (UninitEncounter: the ships that
    /// died gone, every one on a win, the rest with the crew they had left).
    /// </summary>
    private void AfterMeetingBattles()
    {
        foreach (var p in _players)
        {
            if (p.Ship.Meeting is not { Step: MeetingStep.Battle, Battle: { } wb } m)
                continue;
            if (!m.Returned && p.Ship.Battle != wb)
                ReturnToFlight(m);
            if (wb.Outcome is not null)
                EndMeeting(m, fought: true);
        }
    }

    // The meeting's player out of it, back in flight where they met the
    // group: in hyperspace stopped (starcon.c's ZeroVelocityComponents) and
    // spinning back in (hyper_transition); in a system its element made
    // again with the escape counter (a battle's BackInFlight does that).
    private void ReturnToFlight(Meeting m)
    {
        m.Returned = true;
        var ship = m.Player.Ship;
        if (!m.Player.Present || ship.Destroyed || ship.Battle is not null)
            return;
        if (m.Star is null)
        {
            if (ship.SystemStar is null && Shapes is { } shapes)
            {
                ship.Flight.Stop();
                // Its first frame back is this one if the players' flights
                // have been flown already (back from a battle, or a talk that
                // ended as its spin did), else the next.
                ship.AppearFrames = AppearingFrames(ship.Flight.Facing, shapes.HyperFlagship.Count)
                                    - (_playersStepped ? 1 : 0);
            }
            return;
        }
        if (m.Battle is null && ship.Ip is { Orbit: null } ip)
        {
            ip.EscapeCounter = IpFlight.EscapeFrames;
            ip.Reenter();
        }
    }

    // RaceCommunication's end: the group written back with the ships left
    // (a battle's: UninitEncounter's; a talk's: all of them), re-forming, and
    // one-shot if no battle was fought (hyperspace); in a system as the game
    // comes back from it (reinitNpcs), a battle's race turning the race's
    // groups on the players (DoMissions).
    private void EndMeeting(Meeting m, bool fought)
    {
        ReleaseMeetingLock(m);
        List<(int Crew, int Captain)> left;
        if (fought && m.Battle is { } wb)
        {
            // UninitEncounter: on a win every ship is gone; else those whose crew ran out.
            left = new();
            if (wb.Outcome != BattleOutcome.Won)
                for (int i = 0; i < wb.Enemies.Count; ++i)
                    if (wb.Enemies[i].Starship.CrewLevel > 0)
                        left.Add((wb.Enemies[i].Starship.CrewLevel, wb.Enemies[i].Starship.CaptainsNameIndex));
        }
        else
            left = m.Ships.Select(s => (s.Crew ?? 0, s.Captain ?? 0)).ToList();
        m.Left = left;
        m.Step = MeetingStep.Done;
        var player = m.Player;
        if (player.Ship.Meeting == m)
            player.Ship.Meeting = null;

        if (m.Encounter is not null)
        {
            if (MetEncounter(m) is { } e)
            {
                e.MeetingPlayer = null;
                e.Ships.Clear();
                e.Ships.AddRange(left.Select(s => s.Crew));
                e.Flags |= HyperEncounter.Reforming;
                if (!fought)
                    e.Flags |= HyperEncounter.OneShot;
                e.Element = null;   // made afresh (AddEncounterElement: re-forming, or gone)
            }
        }
        else if (SystemGroups is { } sg && sg.Systems.TryGetValue(m.Star!.Value, out var d))
        {
            sg.EndMeeting(d, m.Group!, fought ? left : null, fought ? m.Race : -1);
            if (fought)
                d.Random = NewSystemRandom(d.Star);   // Battle seeds TFB_Random afresh
            // ResetSolarSys: with nobody else flying there, every element is
            // made again (InitDisplayList, DoMissions).
            if (!_players.Any(p => p != player && p.Ship.SystemStar == d.Star && p.Present
                                   && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem))
                d.Elements = null;
        }
        if (!m.Returned)
            ReturnToFlight(m);
    }

    /// <summary>
    /// A player leaving mid-meeting (before its battle): the meeting is
    /// dropped as a game aborted in it (RaceCommunication returns without
    /// writing the group back): the group goes on as it was, its lock let go,
    /// and a ship spinning into it stops where the spin took it.
    /// </summary>
    private void AbandonMeeting(Player player)
    {
        if (player.Ship.Meeting is not { } m || m.Step == MeetingStep.Battle)
            return;
        ReleaseMeetingLock(m);
        if (m.Step == MeetingStep.Spin && player.Ship.Spin is { } spin)
        {
            player.Ship.Flight.TakePlaceOf(spin);
            player.Ship.Spin = null;
        }
        if (m.Encounter is not null && MetEncounter(m) is { } e)
        {
            e.MeetingPlayer = null;
            if (e.Element is { } el && el.Met == player.Id)
                el.Met = null;
        }
        if (m.Group is { } g)
            g.InMeeting = null;
        m.Step = MeetingStep.Done;
        player.Ship.Meeting = null;
    }

    /// <summary>
    /// Testing (--start-encounter): a group of a race (races.h's number) of
    /// <paramref name="ships"/> ships, each with its race's starting crew,
    /// appears by a player's ship and flies at them: in hyperspace a screen
    /// away to the east, coming in through its vortex, in a sphere of its own
    /// round the player (so it doesn't leave it); in a system in the
    /// player's view, 600 units east, intercepting. It flies whether or not
    /// the race's talk is ported (decision 2 drops only the groups the
    /// game's draws make). Throws if the player isn't flying in hyperspace
    /// or a system.
    /// </summary>
    internal string AddEncounterNear(string id, int race, int ships)
    {
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        var rules = Story?.Rules ?? throw new InvalidOperationException("a group needs a story");
        if (ships < 1)
            throw new ArgumentOutOfRangeException(nameof(ships), "a group has a ship at least");
        int crew = rules.RaceShips[race].Crew;
        var ship = player.Ship;
        string name = Meeting.RaceName(rules.Races[race]);
        if (ship.Activity == Activity.Hyperspace && Encounters is { } encounters)
        {
            if (ships > HyperEncounter.MaxShips)
                throw new ArgumentOutOfRangeException(nameof(ships), $"a group in hyperspace has {HyperEncounter.MaxShips} ships at most");
            if (encounters.List.Count >= encounters.Cap)
                throw new InvalidOperationException("hyperspace has as many groups as it holds");
            var f = ship.Flight;
            short x = (short)Math.Min(f.UniverseX + HyperEncounters.ScreenX, Units.MaxXUniverse);
            var e = new HyperEncounter
            {
                Race = race, Roller = id, OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
                LocX = x, LocY = f.UniverseY, LogX = Units.UniverseToLogX(x), LogY = Units.UniverseToLogY(f.UniverseY),
                TransitionState = 9,
            };
            for (int i = 0; i < ships; ++i)
                e.Ships.Add(crew);
            encounters.List.Add(e);
            return $"A group of {ships} {name} ship{(ships == 1 ? "" : "s")} comes in by the host in hyperspace";
        }
        if (ship.Activity is not (Activity.OuterSystem or Activity.InnerSystem) || ship.SystemStar is not int star
            || SystemGroups is not { } sg || Systems is null)
            throw new InvalidOperationException("the player isn't flying in hyperspace or a system");
        SyncSystemPlayers();   // the system's draw made, if the player has only just come in
        var d = sg.Systems[star];
        var list = d.Defined is { } special ? sg.Defined[special] : d.RandomGroups ??= new();
        int groupId = list.Count == 0 ? 1 : list.Max(g => g.Id) + 1;
        list.Add(new GroupShips(groupId, race, Enumerable.Repeat(crew, ships)));
        var ip = ship.Ip!;
        d.Groups.Add(new IpGroup
        {
            GroupId = groupId, Race = race, SysLoc = ip.View, Task = IpGroup.OnStation, DestLoc = IpGroup.InterceptPlayer,
            LocX = (short)(ip.Location.X + 600), LocY = ip.Location.Y,
        });
        d.Elements = null;   // made again with it (DoMissions)
        return $"A group of {ships} {name} ship{(ships == 1 ? "" : "s")} appears by the host in the system";
    }

    /// <summary>The meeting a player is in, or null.</summary>
    public Meeting? MeetingOf(string id) => Find(id)?.Ship.Meeting;
}
