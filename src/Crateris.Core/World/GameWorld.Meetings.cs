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
    /// answerer's ship, new and not yet loaded (the meeting says where it
    /// is: <see cref="Talk.Activity"/>, <see cref="Talk.BattleGroup"/>);
    /// null while the race's talk isn't ported (docs/differences.md row 68):
    /// the meeting then shows the encounter screen, a hail ending at once as
    /// a talk ending in peace. By default <see cref="RaceTalk"/>; tests give
    /// their own (null puts the default back).
    /// </summary>
    internal Func<Player, int, uint, Talk?> MeetingTalks
    {
        get => _meetingTalks ?? RaceTalk;
        set => _meetingTalks = value;
    }

    private Func<Player, int, uint, Talk?>? _meetingTalks;

    /// <summary>
    /// A race's talk as RaceCommunication picks it (stage 3b-1 item 6): the
    /// race's conversation (RACE_COMMUNICATION by the group's first ship's
    /// race: <see cref="DialogueLocks.ConversationOf"/>; the drone's
    /// URQUAN_DRONE_CONVERSATION runs the Ur-Quan's script, init_race),
    /// from the <see cref="TalkLibrary"/>, with the answerer's ship
    /// (<see cref="PlayerTalkShip"/>: their names, crew, fuel and hold) and
    /// the talk's own fields; null without the content or the locks, or
    /// for a race not yet among <see cref="TalkingRaces"/>. No such talk is
    /// story (stage 3b, decision 6: <see cref="TalkRules"/>).
    /// </summary>
    internal Talk? RaceTalk(Player player, int race, uint seed) => RaceTalk(player, race, seed, 0);

    /// <summary>
    /// <see cref="RaceTalk(Player, int, uint)"/> where the talk is told
    /// where it is by <paramref name="globalFlags"/> (GLOBAL_FLAGS_AND_DATA,
    /// the talk's own: bit 7 at the race's world, stage 3b-1 item 7), which
    /// also picks the script where init_race chooses by it (the Spathi at
    /// Spathiwa run spahome.c, 3b-2's: <see cref="TalkLibrary.ScriptFor"/>);
    /// null while that script isn't ported.
    /// </summary>
    internal Talk? RaceTalk(Player player, int race, uint seed, int globalFlags)
    {
        if (RaceScript(race, globalFlags) is not { } script)
            return null;
        if (TalkRules.IsStory(script, Story!))
            throw new InvalidOperationException($"the {script} talk is story: a race met in space starts an ordinary talk");
        var own = new Dictionary<string, int>();
        if (globalFlags != 0)
            own["GLOBAL_FLAGS_AND_DATA"] = globalFlags;
        return TalkLibrary!.Create(script, Story!, own, new PlayerTalkShip(player, TalkLibrary.Strings, this), Clock, seed);
    }

    // The ported script a race's talk runs (init_race), or null: none
    // without the content, the locks or the race among the talking ones.
    private string? RaceScript(int race, int globalFlags)
    {
        if (TalkLibrary is not { } library || Story is not { } story || !TalkingRaces.Contains(story.Rules.Races[race])
            || ConversationOf(race) is not { } conversation)
            return null;
        string script = TalkLibrary.ScriptFor(library.Scripts, conversation, story, globalFlags);
        return TalkLibrary.Ported.Contains(script) ? script : null;
    }

    /// <summary>
    /// The worlds whose orbit starts a race's talk against an infinite
    /// fleet of its ships (stage 3b-1 item 7), by the special system, the
    /// planet and the moon: the Ilwrath homeworld (genilw.c) and Spathiwa's
    /// moon (genspa.c, while the Spathi haven't shielded themselves).
    /// </summary>
    internal static readonly IReadOnlyList<(string Special, int Planet, int? Moon, string Race)> TalkWorlds = new[]
    {
        ("ILWRATH_DEFINED", 0, (int?)null, "ILWRATH_SHIP"),
        ("SPATHI_DEFINED", 0, (int?)0, "SPATHI_SHIP"),
    };

    /// <summary>
    /// The race whose talk orbiting a world starts now, or -1: one of the
    /// <see cref="TalkWorlds"/> while its race lives (StartSphereTracking,
    /// which tracks its sphere too) and, for Spathiwa's moon, the Spathi
    /// haven't shielded themselves; its talk at the world ported (the
    /// Spathi's there is spahome.c's, 3b-2's: till then the moon is orbited
    /// as any world, docs/differences.md row 27). Else the world is orbited
    /// as UQM's is when no talk starts there (the Ilwrath's ruins).
    /// </summary>
    internal int WorldTalkRace(int star, int planet, int? moon)
    {
        if (Systems is null || Story is not { } story)
            return -1;
        string? special = Systems.Map.Stars[star].Special;
        foreach (var (s, p, m, raceId) in TalkWorlds)
        {
            if (s != special || p != planet || m != moon)
                continue;
            int race = story.Rules.RaceIndex(raceId);
            if (RaceScript(race, 1 << 7) is null
                || raceId == "SPATHI_SHIP" && story.Get("SPATHI_SHIELDED_SELVES") != 0
                || !story.StartSphereTracking(raceId))
                return -1;
            return race;
        }
        return -1;
    }

    /// <summary>
    /// A player's ship orbits a race's world (<see cref="WorldTalkRace"/>;
    /// genilw.c's generateOrbital): no orbit, the ship by the world, and
    /// the player meets the race's infinite fleet there. The race's groups
    /// in the system stand down and are put aside for the talk
    /// (<see cref="SystemGroups.StartWorldMeeting"/>); the talk is told
    /// it's at the world (GLOBAL_FLAGS_AND_DATA 1 &lt;&lt; 7) and, hostile,
    /// shows the encounter screen; a battle is against the infinite fleet.
    /// Under the race's lock, as a group met (decision 5): while another
    /// player holds it, nothing starts and the player is told who's talking.
    /// A player in a meeting (one whose battle others fight on, handoff
    /// question 3), a visit or a battle starts no talk, as such a player
    /// meets no group and starts no starbase visit: the world is an
    /// ordinary fly-by, the ship by it with no orbit (UQM never orbits it
    /// while the race lives).
    /// </summary>
    private void MeetAtWorld(Player player, int star, (int Planet, int? Moon) world, int race)
    {
        if (player.Ship is not { Meeting: null, Visit: null, Battle: null })
            return;
        if (!MayMeet(player, race))
        {
            TellWhoIsTalking(player, race);
            return;
        }
        if (!TakeMeetingLock(player, race, out var conversation))
            return;
        if (SystemGroups is { } sg && sg.Systems.TryGetValue(star, out var d))
            sg.StartWorldMeeting(d, race);
        var m = new Meeting(player, race, Story!.Rules.Races[race],
            new[] { new EnemyFleetShip(Story.Rules.RaceShips[race].Ship) }, NextRound())
        {
            Id = ++_meetingIds,
            Star = star,
            World = world,
            Infinite = true,
            Conversation = conversation,
        };
        // BuildBattle (NPC_PLAYER_NUM) in a system: ESCAPE_COUNTER.
        if (player.Ship.Ip is { Orbit: null } ip)
            ip.EscapeCounter = IpFlight.EscapeFrames;
        player.Ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        BeginTalk(m);
    }

    // The meetings begun since the host's session last asked (its log).
    private readonly List<Meeting> _newMeetings = new();

    // The last meeting's number (Meeting.Id).
    private int _meetingIds;

    // Encounter screens' choices made during a pause, made as it ends (StepFrame).
    private readonly List<(string Id, EncounterAction Action, int Meeting)> _heldChoices = new();

    /// <summary>The meetings begun since last asked (over already, some of them: a talk ending at once), for the host's log.</summary>
    internal IReadOnlyList<Meeting> TakeNewMeetings()
    {
        var begun = _newMeetings.ToList();
        _newMeetings.Clear();
        return begun;
    }

    // Who has been told that another player is talking to a race (decision
    // 5): told once while that player holds the lock.
    private readonly HashSet<(string Player, string Holder, string Conversation)> _toldTalking = new();

    // The conversation a group of a race starts, if any.
    private string? ConversationOf(int race) =>
        Story is { } story ? DialogueLocks?.ConversationOf(story.Rules.Races[race]) : null;

    // The dialogue lock a group of a race comes under (the race its
    // conversation locks), if any.
    private string? LockOf(int race) => ConversationOf(race) is { } c ? DialogueLocks!.RaceOf(c) : null;

    /// <summary>
    /// Whether a player may meet a group of a race now: not while another
    /// player holds the race's dialogue lock (decision 5: the group passes
    /// through them, and they're told who's talking), nor while the race's
    /// talk is story (the opening's Ilwrath at Earth, while
    /// PROBE_ILWRATH_ENCOUNTER is set: TalkRules), which the visit's
    /// starter holds as a lock.
    /// </summary>
    internal bool MayMeet(Player player, int race) => TalkHolder(race) is not { } holder || holder == player.Id;

    // Who holds a race's talk: its lock's holder, or while its talk is
    // story, the story visit's starter ("" with none); null for nobody.
    private string? TalkHolder(int race) => ConversationOf(race) is { } c ? TalkHolder(c) : null;

    private string? TalkHolder(string conversation)
    {
        if (DialogueLocks!.Holder(conversation) is { } holder)
            return holder;
        if (TalkLibrary is not { } library || Story is not { } story)
            return null;
        string script = TalkLibrary.ScriptFor(library.Scripts, conversation, story, 0);
        return TalkRules.Scripts.Contains(script) && TalkRules.IsStory(script, story) ? Visit?.Starter.Id ?? "" : null;
    }

    // The player is told once who is talking to the race of a group passing through them.
    private void TellWhoIsTalking(Player player, int race)
    {
        if (ConversationOf(race) is not { } c || TalkHolder(race) is not { } holder || holder == player.Id)
            return;
        if (!_toldTalking.Add((player.Id, holder, c)))
            return;
        Notice(player, NoticeEvent.Talking, holder, RaceNameOf(race));
    }

    // Forget who was told about locks let go since.
    private void ForgetToldTalking() =>
        _toldTalking.RemoveWhere(t => DialogueLocks is null || TalkHolder(t.Conversation) != t.Holder);

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
        if (!TakeMeetingLock(player, met.Race, out var conversation))
            return;
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
            Id = ++_meetingIds,
            Encounter = met,
            Step = MeetingStep.Spin,
            SpinFrames = StarEntry.Turns(ship.Flight.Facing, frames) + 1,
            Conversation = conversation,
        };
        ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        HoldInput(ship, Frame + m.SpinFrames + Settings.InputDelayFrames);
    }

    // A frame of the ship's spin into the group: the drift, or on its last
    // frame (unhyper_transition's cleanup_hyperspace) out of hyperspace where
    // the spin took it, the group met at the head of the list (for
    // RaceCommunication), and the meeting goes on.
    private void SpinIntoGroup(Player player, Meeting m)
    {
        var ship = player.Ship;
        if (--m.SpinFrames > 0)
        {
            ship.Spin!.Move();
            return;
        }
        ship.Flight.TakePlaceOf(ship.Spin!);
        ship.Spin = null;
        // Input read while it spun is dropped (a pause meanwhile put its end later).
        HoldInput(ship, Math.Max(ship.InputHeldUntil, Frame + Settings.InputDelayFrames));
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
        if (!TakeMeetingLock(player, g.Race, out var conversation))
            return;
        SystemMeetings.Add((Frame, f.Id, d.Star, g.Race, g.GroupId));
        var ships = sg.StartMeeting(d, g, f.Id);
        var m = new Meeting(player, g.Race, Story!.Rules.Races[g.Race],
            FleetShips(g.Race, ships is null ? Array.Empty<(int, int?)>()
                : ships.Ships.Zip(ships.Captains, (c, n) => (c, (int?)n))), NextRound())
        {
            Id = ++_meetingIds,
            Star = d.Star,
            Group = g,
            Conversation = conversation,
        };
        if (f.Flight.Orbit is null)
            f.Flight.EscapeCounter = IpFlight.EscapeFrames;
        player.Ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        BeginTalk(m);
    }

    // ---- the talk

    // A meeting's dialogue lock taken as it begins (the race's conversation,
    // or null if it has none). The touch is checked first (MayMeet; in a
    // system SystemGroups.MayMeet, which also passes through a second touch
    // of the race in one frame), so the take doesn't fail; if it did, no
    // meeting begins (no talk runs without the lock) and the player is told
    // who's talking, as a touch passing through is.
    private bool TakeMeetingLock(Player player, int race, out string? conversation)
    {
        conversation = ConversationOf(race);
        if (conversation is null || DialogueLocks!.TryTake(conversation, player.Id, out _))
            return true;
        conversation = null;
        TellWhoIsTalking(player, race);
        return false;
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
        // The player's last talk's closing lines are no longer shown (TalkFor).
        _ended.Remove(m.Player.Id);
        if (m.RaceId != "URQUAN_DRONE_SHIP")
            Story!.StartSphereTracking(m.RaceId);
        uint seed = unchecked((uint)Frame * 2_654_435_761u + (uint)m.Race * 40_503u + 0x4D454554u);
        foreach (char ch in m.Player.Id)
            seed = unchecked(seed * 31 + ch);
        // The talk's own random seed (decision 12), as a visit's talks have.
        var talk = m.World is not null ? RaceTalk(m.Player, m.Race, seed, 1 << 7) : MeetingTalks(m.Player, m.Race, seed);
        m.Talk = talk;
        if (talk is not null)
        {
            // Where the talk is, as its script reads it: LOBYTE (GLOBAL
            // (CurrentActivity)), and in a system its own groups' offset
            // (BattleGroupRef: Sol's drone, the colony's guard, ...).
            talk.RoundSource = NewRound;   // before its first set of replies (Hail)
            talk.Activity = m.Star is null ? TalkActivity.InHyperspace : TalkActivity.InInterplanetary;
            talk.BattleGroup = m.Star is int star && SystemGroups is { } sg && sg.Systems.TryGetValue(star, out var d)
                ? Crateris.Core.Systems.SystemGroups.OffsetField(d.Defined) : null;
            talk.Load();
        }
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
    /// starts against the group's ships; during the host's pause or a
    /// story talk's, made as the pause ends (true; another choice before
    /// then replaces it). False, changing nothing, if the player isn't at
    /// the encounter screen, or the game is over,
    /// or the choice is for another meeting than theirs
    /// (<paramref name="meeting"/>, a <see cref="Meeting.Id"/>: a guest's,
    /// stage 3b-1 item 10; null for whichever they're in).
    /// </summary>
    public bool EncounterChoice(string id, EncounterAction action, int? meeting = null)
    {
        if (GameOver || Find(id) is not { Present: true, Ship.Meeting: { Step: MeetingStep.Encounter } m }
            || !Enum.IsDefined(action) || meeting is int n && n != m.Id)
            return false;
        if (HostPaused || StoryPaused)
        {
            // Kept for the pause's end (the host's, or a story talk's), as a
            // reply is through the host's: the chooser's client counts a
            // choice sent as made, so one refused would leave them at the
            // screen for good.
            _heldChoices.RemoveAll(h => h.Id == id);
            _heldChoices.Add((id, action, m.Id));
            return true;
        }
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
        if (HostPaused || StoryPaused)
        {
            // Kept for the pause's end (GameWorld.SetHostPause, StepFrame).
            _heldReplies.RemoveAll(h => h.Id == m.Player.Id && h.Round == m.Round);   // the last for this set; a story's kept
            _heldReplies.Add((m.Player.Id, index, m.Round));
            return true;
        }
        talk.Choose(index);
        _rounds = Math.Max(_rounds, m.Round);
        if (talk.Ended)
            TalkOver(m);
        return true;
    }

    // The talk is over (InitCommunication after it). Hailed, with lines
    // since the player's last reply (the alien's after it, the drone's
    // message whole), the meeting waits at its closing lines (Closing) for
    // that player to go through them on their communication screen
    // (DoCommunication's last replay; the owner, 2026-11-21, question 10),
    // the race's lock held meanwhile; then, or at once without them, it
    // goes on (AfterTalk).
    private void TalkOver(Meeting m)
    {
        _rounds = Math.Max(_rounds, m.Round);
        if (m.Hailed && m.Player.Present && !GameOver && TalkView.Of(m) is { Lines.Count: > 0 })
        {
            m.Step = MeetingStep.Closing;
            return;
        }
        AfterTalk(m);
    }

    /// <summary>
    /// The player has gone through their meeting's closing lines
    /// (<see cref="MeetingStep.Closing"/>: their client played them on the
    /// communication screen and the last replay's time ran out): its battle
    /// starts or its ship flies on. During the host's pause or a story
    /// talk's, kept and made as the pause ends (true), as an encounter
    /// choice is. False, changing nothing, if the player's meeting isn't at
    /// its closing lines, or the game is over, or it's for another meeting
    /// (<paramref name="meeting"/>, a <see cref="Meeting.Id"/>; null for
    /// whichever they're in).
    /// </summary>
    public bool ClosingDone(string id, int? meeting = null)
    {
        if (GameOver || Find(id) is not { Present: true, Ship.Meeting: { Step: MeetingStep.Closing } m }
            || meeting is int n && n != m.Id)
            return false;
        if (HostPaused || StoryPaused)
        {
            _heldClosings.RemoveAll(h => h.Id == id);
            _heldClosings.Add((id, m.Id));
            return true;
        }
        AfterTalk(m);
        return true;
    }

    // Closing lines gone through during a pause, made as it ends (StepFrame).
    private readonly List<(string Id, int Meeting)> _heldClosings = new();

    // After the talk and its closing lines: its lock let go; hostile (an
    // attack, or a talk ending in battle) and with ships to fight, the
    // battle (BuildBattle, EncounterBattle); else the meeting ends as a talk
    // without a battle does.
    private void AfterTalk(Meeting m)
    {
        ReleaseMeetingLock(m);
        if (m.Talk is { Segue: Segue.Defeat })
        {
            Surrender(m);
            return;
        }
        if (m.Talk is { Segue: Segue.Victory } && m.Ships.Count > 0)
        {
            InstantVictory(m);
            return;
        }
        bool hostile = m.Talk is { } talk ? talk.Segue is Segue.Hostile : !m.Hailed;
        if (hostile && m.Ships.Count > 0 && BattleKit is not null
            && m.Ships.All(s => BattleKit.Enemies.Contains(s.Ship)) && m.Player.Present && !GameOver)
        {
            m.Step = MeetingStep.Battle;
            m.Battle = StartBattle(m.Player.Id, new EnemyFleet(m.Ships, m.Infinite));
            return;
        }
        EndMeeting(m, fought: false);
    }

    /// <summary>
    /// A talk ending in instant victory (setSegue (Segue_victory): the
    /// Slylandro probe sent the destruct code, stage 3b-1 item 8):
    /// EncounterBattle as UQM's, whose Battle starts no fight (instantVictory:
    /// the player's side left, the other's none), so UninitEncounter tallies
    /// a battle won: every one of the group's ships destroyed, each paying
    /// the race's bounty (RACE_SHIP_COST / 8) to the answerer, the only one
    /// in it (unless the Ur-Quan guard the Sa-Matra: no victory, nothing
    /// destroyed). The meeting then ends as after a battle
    /// (RaceCommunication's status 1): in hyperspace the group re-forms with
    /// no ships (with them all while the Sa-Matra is guarded), and goes,
    /// one-shot as every probe is from its roll; in a system the group is gone, the race's groups
    /// turn on the players (EncounterRace) and the numbers are seeded afresh
    /// (Battle's). comm.c's own clearing of the group for the Slylandro in a
    /// system (status 0) is never reached, as the instant victory's status
    /// is 1 and the probe's talk ends nowhere else in peace.
    /// </summary>
    private void InstantVictory(Meeting m)
    {
        var rules = Story!.Rules;
        bool victory = Story.Get("URQUAN_PROTECTING_SAMATRA") == 0;
        m.InstantVictory = true;
        if (victory)
        {
            uint salvage = (uint)(m.Ships.Count * (rules.RaceShips[m.Race].Cost >> 3));
            m.Salvage = salvage;
            var hold = m.Player.Ship.Hold;
            hold.ResourceUnits = (uint)Math.Min(uint.MaxValue, (ulong)hold.ResourceUnits + salvage);
        }
        EndMeeting(m, fought: true, victory ? new List<(int, int)>() : null);
    }

    /// <summary>
    /// The player surrendered (the Ur-Quan's: setSegue (Segue_defeat), the
    /// crew (COUNT)~0). UQM's game is then over (starcon.c leaves its loop);
    /// here, as a ship's crew run out is (stage 4a, decision 7, and decision
    /// 12): the host's flagship surrendering is the game over for everyone,
    /// another ship (a guest's) a wreck where it is, with no crew, for a
    /// friend to revive. The group goes on as after a talk without a battle
    /// (RaceCommunication's status 0: re-forming, one-shot in hyperspace).
    /// A placeholder for the owner (docs/handoff.md: what a guest's
    /// surrender does).
    /// </summary>
    private void Surrender(Meeting m)
    {
        var ship = m.Player.Ship;
        ship.Hold.SetCrew(0);
        ship.Destroyed = true;   // stopped already (the spin's end), and a wreck doesn't fly
        if (ship.Kind == ShipKind.Flagship)
            LoseTheGame(surrendered: true);
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
    // group: in hyperspace spinning back in (hyper_transition), stopped
    // (starcon.c's ZeroVelocityComponents: the spin's end and the battle's
    // start stopped it already); in a system its element made again with
    // the escape counter (a battle's BackInFlight does that).
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
    private void EndMeeting(Meeting m, bool fought, List<(int Crew, int Captain)>? instantLeft = null)
    {
        ReleaseMeetingLock(m);
        List<(int Crew, int Captain)> left;
        if (instantLeft is not null)
            left = instantLeft;
        else if (fought && m.Battle is { } wb)
        {
            // UninitEncounter: those whose crew ran out are gone (on a win,
            // every one: none is left able to fight), the rest keep theirs.
            left = new();
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

        // Whether the group goes through this meeting (the others it had
        // flown at are told so, decision 11), and whom it had flown at.
        bool gone = false;
        IReadOnlyCollection<string> chased = Array.Empty<string>();
        if (m.Encounter is not null)
        {
            if (MetEncounter(m) is { } e)
            {
                chased = e.Chased.ToList();
                e.MeetingPlayer = null;
                e.Ships.Clear();
                e.Ships.AddRange(left.Select(s => s.Crew));
                e.Flags |= HyperEncounter.Reforming;
                if (!fought)
                    e.Flags |= HyperEncounter.OneShot;
                e.Element = null;   // made afresh (AddEncounterElement: re-forming, or gone)
                // Its fate as AddEncounterElement makes it: one-shot (after a
                // talk, or from its roll: the probes) or with no ships, it goes.
                gone = (e.Flags & HyperEncounter.OneShot) != 0 || e.Ships.Count == 0;
            }
        }
        else if (SystemGroups is { } sg && sg.Systems.TryGetValue(m.Star!.Value, out var d))
        {
            if (m.Group is { } g)
            {
                chased = g.Chased.ToList();
                bool fleeing = IsFleeing(g);
                sg.EndMeeting(d, g, fought ? left : null, fought ? m.Race : -1);
                // Gone: destroyed (none left), or sent off out of the system by the talk (the drone, check_probe).
                gone = fought ? left.Count == 0 : !fleeing && IsFleeing(g);
            }
            else
                sg.EndWorldMeeting(d, fought ? m.Race : -1);   // the race's world's

            if (fought)
                d.Random = NewSystemRandom(d.Star);   // Battle seeds TFB_Random afresh
            // ResetSolarSys: with nobody else flying there, every element is
            // made again (InitDisplayList, DoMissions).
            if (!_players.Any(p => p != player && p.Ship.SystemStar == d.Star && p.Present
                                   && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem))
                d.Elements = null;
        }
        TellMeetingEnded(m, fought, gone, chased);
        if (!m.Returned)
            ReturnToFlight(m);
    }

    private static bool IsFleeing(IpGroup g) => (g.Task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup)) == IpGroup.Flee;

    /// <summary>
    /// A player leaving mid-meeting (before its battle, its closing lines
    /// too): the meeting is dropped as a game aborted in it
    /// (RaceCommunication returns without writing the group back, and an
    /// abort during the last replay fights no battle): the group goes on as
    /// it was, its lock let go, and a ship spinning into it stops where the
    /// spin took it.
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
                Flags = encounters.RolledFlags(Story, race, f.Fuel),   // a real roll's: the probes one-shot
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
