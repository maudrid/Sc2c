// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>
/// The host's side of a game, without Godot: the world, the guests by
/// their network peer, their input, saves, and the testing start options.
/// The Godot layer moves messages and files; this decides what they do.
/// The host's own player plays through the same input path, with the same
/// delay (<see cref="Step"/>).
/// </summary>
public sealed class HostSession
{
    private readonly Dictionary<long, string> _peers = new();
    private readonly SnapshotSender _snapshots = new();

    private HostSession(GameWorld world) => World = world;

    public GameWorld World { get; }

    /// <summary>The host's player id (a loaded game's is the save's).</summary>
    public string HostId => World.Host.Id;

    /// <summary>Guests in the game, by peer.</summary>
    public int Guests => _peers.Count;

    /// <summary>The peers of the guests in the game, who are sent snapshots.</summary>
    public IEnumerable<long> GuestPeers => _peers.Keys;

    /// <summary>Whether a peer has a player in the game (a refused hello from it mustn't close its connection).</summary>
    public bool InGame(long peer) => _peers.ContainsKey(peer);

    public event Action<string>? Logged;

    /// <summary>A new game: the host in the flagship, in hyperspace at the start.</summary>
    public static HostSession New(GameSettings settings, NewGameData data, string hostId, string hostName,
        SystemCatalog systems, ContactShapes shapes) =>
        new(GameWorld.NewGame(settings, data, hostId, hostName, systems, shapes));

    /// <summary>A saved game; the host (the save's) joins it under <paramref name="hostName"/>.</summary>
    public static HostSession Load(string json, string hostName, SystemCatalog systems, ContactShapes shapes)
    {
        var world = HostSave.FromJson(json, systems, shapes);
        world.Join(world.Host.Id, hostName);
        return new HostSession(world);
    }

    /// <summary>
    /// The game as the host's save, or null once the flagship is lost (from
    /// the frame its crew ran out, through the game over: decision 12, the
    /// save is kept as it was, so loading it goes back to before the battle
    /// the flagship was lost in), so neither the autosave nor quitting
    /// writes one.
    /// </summary>
    public string? SaveJson() => World.FlagshipLost ? null : HostSave.ToJson(World);

    /// <summary>
    /// --start-at: the host's ship in hyperspace at universe coordinates, at
    /// rest. Only from hyperspace: a game loaded with the host in a system
    /// or in orbit is left where it is (InvalidOperationException).
    /// </summary>
    public string StartAt(short x, short y)
    {
        var host = World.Host.Ship;
        if (host.Activity != Activity.Hyperspace)
            throw new InvalidOperationException($"the host is in {host.Activity}, not hyperspace");
        host.Flight.PlaceAtUniverse(x, y);
        host.Flight.Stop();
        host.Flight.StarJustLeft = null;
        return $"Starting in hyperspace at {x}:{y}";
    }

    /// <summary>--start-orbit: the host's ship in orbit there (ArgumentException if there's no such world).</summary>
    public string StartInOrbit(int star, int planet, int? moon)
    {
        World.PutInOrbit(HostId, star, planet, moon);
        return $"Starting in orbit: star {star} planet {planet}" + (moon is int m ? $" moon {m}" : "");
    }

    /// <summary>
    /// --start-minerals (testing): the host's hold starts with these
    /// minerals (the 8 element categories' amounts), as if mined, to sell
    /// at the starbase (ArgumentException if they're not 8 or don't fit).
    /// </summary>
    public string StartWithMinerals(IReadOnlyList<int> amounts)
    {
        var hold = World.Host.Ship.Hold;
        if (amounts.Count != ShipHold.Categories || amounts.Any(a => a < 0))
            throw new ArgumentException($"{ShipHold.Categories} amounts, none below 0");
        if (amounts.Sum() > hold.FreeStorage)
            throw new ArgumentException($"{amounts.Sum()} units don't fit in {hold.FreeStorage} free");
        hold.MoveCargo(amounts, 0, 1);
        return $"Starting with {amounts.Sum()} units of minerals";
    }

    /// <summary>--start-units (testing): the host's wallet starts with these resource units.</summary>
    public string StartWithUnits(uint units)
    {
        World.Host.Ship.Hold.ResourceUnits = units;
        return $"Starting with {units} resource units";
    }

    /// <summary>
    /// --start-devices (testing): the group carries these devices (by
    /// globdata.h's name, as data/devices.json has them, or number): each
    /// one's story field set (to its value, for the Ultron's states). Ones
    /// the Manifest never lists are refused (ArgumentException), as are
    /// two of the Ultron's states, which share one field.
    /// </summary>
    public string StartWithDevices(IEnumerable<string> devices)
    {
        if (World.Story is not { } story || World.Systems?.StoryRules is not { } rules)
            throw new InvalidOperationException("the story needs the content");
        var table = rules.Devices;
        var chosen = new List<Crateris.Core.Story.Device>();
        foreach (var name in devices)
        {
            var d = (int.TryParse(name, out int n) ? table.All.ElementAtOrDefault(n) : null)
                ?? table.All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"no device {name}");
            if (!d.Listed)
                throw new ArgumentException($"{d.Name} is never listed");
            if (chosen.Any(c => c.Field == d.Field && c.Value != d.Value))
                throw new ArgumentException($"{d.Name} and {chosen.First(c => c.Field == d.Field).Name} are one field's values");
            chosen.Add(d);
        }
        foreach (var d in chosen)
            story.Set(d.Field!, d.Value ?? 1);
        return $"Starting with {chosen.Count} devices aboard";
    }

    /// <summary>
    /// The steps --start-visit takes (testing): <c>base-report</c>, the
    /// commander's talk where the Lunar base is reported, before the
    /// Ilwrath's (<see cref="StartVisit"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> VisitSteps = new[] { "base-report" };

    /// <summary>
    /// --start-visit [STEP] (testing): the host's ship by Earth's starbase,
    /// in the visit's first talk, as if it had just flown into it: with no
    /// step the commander's first talk; <c>base-report</c> the commander's
    /// talk where the Lunar base is reported (<see cref="VisitStep.Commander"/>
    /// with the errands done: the radioactives given, the base taken and
    /// aboard, the commander met), whose replies bring the Ilwrath and its
    /// fight (stage 4a item 9). Needs the talks' content
    /// (InvalidOperationException); an unknown step is an ArgumentException.
    /// </summary>
    public string StartVisit(string? step = null)
    {
        if (step is not null && !VisitSteps.Contains(step))
            throw new ArgumentException($"no visit step {step} (steps: {string.Join(", ", VisitSteps)})");
        if (World.TalkLibrary is null || World.Systems is null || World.Story is null)
            throw new InvalidOperationException("the talks need the content");
        if (step == "base-report")
        {
            foreach (var f in new[] { "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "MOONBASE_ON_SHIP" })
                World.Story.Set(f, 1);
            World.CommanderMet = true;
        }
        int sol = World.Systems.Map.Stars.Single(s => s.Special == "SOL_DEFINED").Number;
        World.PutInOrbit(HostId, sol, 2, 1);
        World.Host.Ship.Ip!.ReturnFromOrbit();   // in Earth's inner system, by the starbase
        World.VisitStarbase(World.Host);
        return World.Visit is { } v ? $"Starting in the {v.Step} talk at Earth's starbase" : "Nothing to tell at Earth's starbase";
    }

    /// <summary>
    /// --start-docked (testing): the host's ship docked at Earth's
    /// starbase's menu, as after the opening: the starbase ready and the
    /// commander met, with no news. Needs the talks' content
    /// (InvalidOperationException).
    /// </summary>
    public string StartDocked()
    {
        if (World.TalkLibrary is null || World.Systems is null || World.Story is null)
            throw new InvalidOperationException("the starbase needs the content");
        AfterTheOpening();
        World.VisitStarbase(World.Host);
        return World.Host.Ship.Visit is { AtMenu: true } ? "Starting docked at Earth's starbase" : "Couldn't dock at Earth's starbase";
    }

    // The opening over (the starbase ready and the commander met, with no
    // news), the host's ship in flight in Earth's inner system, by Luna.
    private void AfterTheOpening()
    {
        var story = World.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", World.Clock.Month);
        story.Set("STARBASE_DAY", World.Clock.Day);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);   // no news
        World.CommanderMet = true;
        int sol = World.Systems!.Map.Stars.Single(s => s.Special == "SOL_DEFINED").Number;
        World.PutInOrbit(HostId, sol, 2, 1);
        World.Host.Ship.Ip!.ReturnFromOrbit();
    }

    /// <summary>
    /// --start-battle RACE (testing): a game after the opening, as
    /// --start-docked leaves it (so a retreat is allowed), with the host's
    /// ship in flight in Earth's inner system, in a battle against a ported
    /// race's ship (<see cref="BattleKit.Enemies"/>: the Ilwrath, which
    /// cloaks here, as everywhere but in the Earth fight), whose marker the
    /// others may fly into. Needs the content (InvalidOperationException);
    /// a race not ported is an ArgumentException.
    /// </summary>
    public string StartBattle(string race)
    {
        if (World.BattleKit is null || World.Systems is null || World.Story is null)
            throw new InvalidOperationException("a battle needs the content");
        if (!BattleKit.Enemies.Contains(race))
            throw new ArgumentException($"{race} isn't a ship that can be fought (ported: {string.Join(", ", BattleKit.Enemies)})");
        AfterTheOpening();
        var battle = World.StartBattle(HostId, race);
        return $"Starting in a battle against the {battle.EnemyShip.Id} by Earth";
    }

    /// <summary>
    /// --guests-in-orbit (testing): each guest joining for the first time is
    /// put in orbit where the host is as it joins (its undocking scene still
    /// shows), rather than in the inner system beside it.
    /// </summary>
    public bool GuestsStartInOrbit { get; set; }

    /// <summary>
    /// --guests-in-battle (testing): each guest joining for the first time,
    /// put beside the host as ever, joins the battle there as if it had
    /// flown into its marker (<see cref="GameWorld.LetIntoBattle"/>).
    /// </summary>
    public bool GuestsJoinBattle { get; set; }

    /// <summary>
    /// A guest says hello: refused if its version differs or its connection
    /// already has a player, otherwise joined (or refused, if the game is full). The welcome goes back to it; an
    /// accepted guest's input and undocking are taken from its peer from now on.
    /// </summary>
    public Protocol.Welcome Hello(long peer, string playerId, string name, int version)
    {
        JoinResult result;
        string message;
        if (_peers.TryGetValue(peer, out var already))
        {
            // One player a connection: a second hello changes nothing, and
            // the player already on it stays until the connection closes.
            result = JoinResult.AlreadyPresent;
            message = $"This connection is already in the game as {already}.";
        }
        else if (version != Protocol.Version)
        {
            result = JoinResult.Full;
            message = $"Version mismatch: host {Protocol.Version}, you {version}.";
        }
        else
        {
            result = World.Join(playerId, name);
            message = Protocol.Describe(result);
        }
        // A repeated hello is answered (and logged) with the player already on the connection.
        string answered = already ?? playerId;
        Logged?.Invoke(already is null
            ? $"{name} ({playerId}): {result}"
            : $"{name} ({playerId}): {result}, peer {peer} is already {already}");
        if (result == JoinResult.Joined && GuestsStartInOrbit && World.Host.Ship.OrbitedWorld is { } w)
            World.PutInOrbit(playerId, w.Star, w.Planet, w.Moon);
        if (result == JoinResult.Joined && GuestsJoinBattle)
            World.LetIntoBattle(playerId);
        if (Protocol.Accepted(result))
        {
            _peers[peer] = playerId;
            _snapshots.Forget(peer);   // a new guest starts from a full snapshot
        }
        return new Protocol.Welcome(result, answered, World.Settings.InputDelayFrames, message);
    }

    /// <summary>
    /// A guest's input for a frame (only the seven input bits count, Special among them), with
    /// the frame of the newest snapshot it has (<paramref name="has"/>),
    /// which the host's next snapshots for it are deltas from.
    /// </summary>
    public void GuestInput(long peer, long frame, int input, long has)
    {
        if (_peers.TryGetValue(peer, out var id))
        {
            World.SubmitInput(id, frame, (ShipInput)(input & 127));   // Special (64) too: the battle's
            _snapshots.Acknowledge(peer, has);
        }
    }

    /// <summary>
    /// A guest sends its lander down at a site it picked (planetside map
    /// units): refused, and logged, if it can't go now (a site off the map,
    /// the ship no longer in orbit, its lander already down).
    /// </summary>
    public DispatchRefusal GuestDispatch(long peer, int siteX, int siteY) =>
        _peers.TryGetValue(peer, out var id) ? Dispatch(id, siteX, siteY) : DispatchRefusal.NotInOrbit;

    /// <summary>A player's lander goes down (the host's own through here too).</summary>
    public DispatchRefusal Dispatch(string id, int siteX, int siteY)
    {
        if (siteX < 0 || siteX >= PlanetsideMap.Width || siteY < 0 || siteY >= PlanetsideMap.Height)
        {
            Logged?.Invoke($"{id}: landing site {siteX}, {siteY} is off the map");
            return DispatchRefusal.OffTheMap;
        }
        var refusal = World.Dispatch(id, siteX, siteY);
        Logged?.Invoke(refusal == DispatchRefusal.None
            ? $"{id}: lander down at {siteX}, {siteY}"
            : $"{id}: lander refused ({refusal})");
        return refusal;
    }

    /// <summary>
    /// A guest gives a wreck crew (item 7; item 11 sends it, from item 12's
    /// rescue screen): refused with a reason, or None. Its rescuer is the
    /// guest, whatever the message says.
    /// </summary>
    public RescueRefusal GuestRevive(long peer, string wreck, int crew) =>
        _peers.TryGetValue(peer, out var id) ? Revive(id, wreck, crew) : RescueRefusal.NoOne;

    /// <summary>A player gives a wreck crew (the host's own through here too), logged.</summary>
    public RescueRefusal Revive(string rescuer, string wreck, int crew)
    {
        var refusal = World.Revive(rescuer, wreck, crew);
        Logged?.Invoke(refusal == RescueRefusal.None ? $"{rescuer} revived {wreck} with {crew} crew"
            : $"{rescuer}'s rescue of {wreck} refused: {refusal}");
        return refusal;
    }

    /// <summary>A guest drops a unit of cargo overboard (anything wrong is ignored).</summary>
    public void GuestDiscard(long peer, int category)
    {
        if (_peers.TryGetValue(peer, out var id))
            World.DiscardCargo(id, category);
    }

    public void GuestUndockShown(long peer)
    {
        if (_peers.TryGetValue(peer, out var id))
            World.UndockShown(id);
    }

    /// <summary>A guest pages the story talk it replies in, in that round (anything wrong is ignored).</summary>
    public void GuestPageTalk(long peer, int round, int lines, int page)
    {
        if (_peers.TryGetValue(peer, out var id) && page is >= 0 and <= (int)TalkPage.SummaryTurn)
            World.PageTalk(id, round, lines, (TalkPage)page);
    }

    /// <summary>The host's own paging of the story talk it replies in (<see cref="GameWorld.PageTalk"/>).</summary>
    public bool PageTalk(string id, int round, int lines, TalkPage page) => World.PageTalk(id, round, lines, page);

    /// <summary>A guest chooses a reply in its talk, from the set offered in that round (anything wrong is ignored).</summary>
    public void GuestChooseReply(long peer, int round, int index)
    {
        if (_peers.TryGetValue(peer, out var id))
            ChooseReply(id, round, index);
    }

    /// <summary>A player chooses a reply in their talk, from the set offered in that round (the host's own through here too).</summary>
    public bool ChooseReply(string id, int round, int index)
    {
        bool chosen = World.ChooseReply(id, index, round);
        if (chosen)
            Logged?.Invoke($"{id}: reply {index}");
        return chosen;
    }

    private readonly Dictionary<string, TalkView?> _talkSent = new();

    /// <summary>A guest gives another player something (item 9): refused with a reason, or None. Its sender is the guest, whatever the message says.</summary>
    public TradeRefusal GuestGive(long peer, byte[] data)
    {
        if (!_peers.TryGetValue(peer, out var id))
            return TradeRefusal.NoOne;
        Gift gift;
        try
        {
            gift = Gift.Decode(data);
        }
        catch (InvalidDataException)
        {
            return TradeRefusal.Nothing;
        }
        return Give(gift with { From = id });
    }

    /// <summary>A player gives another something (the host's own through here too).</summary>
    public TradeRefusal Give(Gift gift)
    {
        var refusal = World.Give(gift);
        Logged?.Invoke(refusal == TradeRefusal.None ? $"{gift.From} gave {gift.To} {gift.Describe()}"
            : $"{gift.From}'s gift to {gift.To} refused: {refusal}");
        return refusal;
    }

    /// <summary>
    /// The gifts given since the last call, to tell their giver and their
    /// receiver: the host's own (the host is either), and each guest's by
    /// its peer.
    /// </summary>
    public (List<Gift> Host, List<(long Peer, Gift Gift)> Guests) TakeGifts()
    {
        var host = new List<Gift>();
        var guests = new List<(long, Gift)>();
        foreach (var gift in World.TakeGifts())
        {
            if (gift.From == HostId || gift.To == HostId)
                host.Add(gift);
            foreach (var (peer, id) in _peers)
                if (id == gift.From || id == gift.To)
                    guests.Add((peer, gift));
        }
        return (host, guests);
    }

    /// <summary>A guest's key on its starbase screen (anything wrong is ignored).</summary>
    public void GuestStarbaseKey(long peer, int key)
    {
        if (_peers.TryGetValue(peer, out var id) && Enum.IsDefined((Systems.MenuKey)key))
            StarbaseKey(id, (Systems.MenuKey)key);
    }

    /// <summary>
    /// A docked player's key on their starbase screen (the host's own
    /// through here too): the host choosing Save game saves
    /// (<see cref="SaveRequested"/>).
    /// </summary>
    public Starbase.OutfitOutcome StarbaseKey(string id, Systems.MenuKey key)
    {
        var outcome = World.StarbaseKey(id, key, out bool save);
        if (save)
            SaveRequested?.Invoke();
        if (outcome is Starbase.OutfitOutcome.Acted or Starbase.OutfitOutcome.Refused)
            Logged?.Invoke($"{id}: starbase {key}: {outcome}");
        return outcome;
    }

    /// <summary>
    /// The host's pause key (stage 4a item 10, decision 15): pauses the
    /// game, or lifts the pause; only the host has it (guests have no
    /// message for it). Every player is shown it through the snapshot
    /// (<see cref="WorldSnapshot.HostPaused"/>). False once the game is over.
    /// </summary>
    public bool TogglePause()
    {
        if (!World.SetHostPause(!World.HostPaused))
            return false;
        Logged?.Invoke(World.HostPaused ? "The host paused the game" : "The host lifted the pause");
        return true;
    }

    /// <summary>The host chose Save game at the starbase.</summary>
    public event Action? SaveRequested;

    /// <summary>A guest renames its ship or captain (empty: unchanged), docked at the starbase.</summary>
    public void GuestRename(long peer, string ship, string captain)
    {
        if (_peers.TryGetValue(peer, out var id))
            Rename(id, ship == "" ? null : ship, captain == "" ? null : captain);
    }

    /// <summary>Renames a docked player's ship or captain (outfitting's naming); false if not docked.</summary>
    public bool Rename(string id, string? ship, string? captain) =>
        !World.StoryPaused && World.Find(id)?.Ship.Activity == Activity.Starbase && World.Rename(id, ship, captain);

    /// <summary>What a docked player's starbase screen is now: null if not docked.</summary>
    public Starbase.StarbaseView? StarbaseFor(string id) => World.StarbaseFor(id);

    private readonly Dictionary<string, Starbase.StarbaseView?> _starbaseSent = new();

    /// <summary>The starbase screens to send the guests: each whose screen has changed since it was last sent (empty for none).</summary>
    public List<(long Peer, byte[] Data)> StarbaseUpdates()
    {
        var updates = new List<(long, byte[])>();
        foreach (var (peer, id) in _peers)
        {
            var view = StarbaseFor(id);
            if (Equals(view, _starbaseSent.GetValueOrDefault(id)) && _starbaseSent.ContainsKey(id))
                continue;
            _starbaseSent[id] = view;
            updates.Add((peer, view?.Encode() ?? Protocol.NoView));
        }
        return updates;
    }

    /// <summary>
    /// The starbase's drawing to send the guests: each docked guest's
    /// since the last call, in order, as one message (reliable, sent
    /// before <see cref="StarbaseUpdates"/>' screens, as each is drawn over
    /// the last); empty for none.
    /// </summary>
    public List<(long Peer, byte[] Data)> StarbaseDrawings()
    {
        var drawings = new List<(long, byte[])>();
        foreach (var (peer, id) in _peers)
        {
            var drawn = World.TakeStarbaseDrawing(id);
            if (drawn.Count != 0)
                drawings.Add((peer, Starbase.StarbaseDrawing.Encode(drawn)));
        }
        return drawings;
    }

    /// <summary>A guest's client has no picture of its starbase screen: it's drawn afresh in the next drawings sent.</summary>
    public void GuestStarbaseRedraw(long peer)
    {
        if (_peers.TryGetValue(peer, out var id))
            World.RedrawStarbase(id);
    }

    /// <summary>The host's own starbase drawing since the last call (see <see cref="StarbaseDrawings"/>).</summary>
    public List<Starbase.StarbaseDrawing> HostStarbaseDrawing() => World.TakeStarbaseDrawing(HostId);

    /// <summary>The lander reports to show (World.TakeReports): the host's own, and each guest's by its peer.</summary>
    public (List<LanderReport> Host, List<(long Peer, LanderReport Report)> Guests) TakeReports()
    {
        var host = new List<LanderReport>();
        var guests = new List<(long, LanderReport)>();
        foreach (var (id, report) in World.TakeReports())
        {
            if (id == HostId)
                host.Add(report);
            else if (_peers.FirstOrDefault(p => p.Value == id) is { Value: not null } peer)
                guests.Add((peer.Key, report));
        }
        return (host, guests);
    }

    /// <summary>What a player in a talk is shown now: null if they're in none.</summary>
    public TalkView? TalkFor(string id) => World.TalkFor(id);

    /// <summary>
    /// The talks to send the guests: each guest whose talk view has changed
    /// since it was last sent (empty for none), as reliable messages.
    /// </summary>
    public List<(long Peer, byte[] Data)> TalkUpdates()
    {
        var updates = new List<(long, byte[])>();
        foreach (var (peer, id) in _peers)
        {
            var view = TalkFor(id);
            var sent = _talkSent.GetValueOrDefault(id);
            if (view is null ? sent is null : view.Same(sent))
                continue;
            _talkSent[id] = view;
            updates.Add((peer, view?.Encode() ?? Protocol.NoView));
        }
        return updates;
    }

    /// <summary>A guest's connection has gone: it leaves the game (its ship is kept for its return).</summary>
    public void GuestLeft(long peer)
    {
        _snapshots.Forget(peer);
        if (_peers.Remove(peer, out var id))
        {
            _talkSent.Remove(id);
            _starbaseSent.Remove(id);
            World.Leave(id);
            Logged?.Invoke($"{id} left");
        }
    }

    private bool _gameOverLogged;
    private readonly Dictionary<string, int> _inBattle = new();
    private readonly HashSet<string> _wrecks = new();
    private readonly List<(string PlayerId, BattleNotice Notice)> _notices = new();
    // Notices for players away when they came, told as they're back (a
    // wreck made, revived, or the game over meanwhile).
    private readonly List<(string PlayerId, BattleNotice Notice)> _awayNotices = new();
    // The fighters told they're out of a battle (left it before its end).
    private readonly HashSet<(int Battle, string Player)> _toldOut = new();

    // The world's battle news since the last frame, as notices for the
    // players present (stage 4a item 11), each logged: a ship joining a
    // battle (seen here, a frame on: the Ilwrath fight starts inside a
    // reply), a battle's end, a ship become a wreck, a battle too full to
    // join, a wreck flown into and one revived, and the game over.
    private void TakeBattleNews()
    {
        void Tell(string id, BattleNotice notice)
        {
            if (World.Find(id) is { Present: true })
                _notices.Add((id, notice));
            else
                _awayNotices.Add((id, notice));
        }
        // Those back since: their notices from while they were away (none
        // of a battle they joined: they left it as they went).
        for (int i = 0; i < _awayNotices.Count; ++i)
            if (World.Find(_awayNotices[i].PlayerId) is { Present: true })
            {
                _notices.Add(_awayNotices[i]);
                _awayNotices.RemoveAt(i--);
            }
        foreach (var p in World.Players)
        {
            if (p.Ship.Battle is { } wb && (!_inBattle.TryGetValue(p.Id, out int was) || was != wb.Id))
            {
                _inBattle[p.Id] = wb.Id;
                Tell(p.Id, new BattleNotice(BattleNoticeKind.Joined, wb.Id, wb.EnemyShip.Id));
            }
            else if (p.Ship.Battle is null)
                _inBattle.Remove(p.Id);
        }
        // A ship out of a battle still going on (retreated, or its player
        // disconnected): told then, with no outcome yet.
        foreach (var wb in World.Battles)
            foreach (var f in wb.Fighters)
                if (f.Exit is BattleExit.Retreated or BattleExit.Disconnected && _toldOut.Add((wb.Id, f.Player.Id)))
                    Tell(f.Player.Id, new BattleNotice(BattleNoticeKind.Ended, wb.Id, wb.EnemyShip.Id, null, f.Exit, 0, f.CrewLeft));
        foreach (var ended in World.TakeEndedBattles())
        {
            Logged?.Invoke($"Battle {ended.Id} against the {ended.EnemyShip.Id} {ended.Outcome?.ToString().ToLowerInvariant()}: "
                + string.Join(", ", ended.Fighters.Select(f => $"{f.Player.Id} {f.Exit?.ToString().ToLowerInvariant()}")));
            foreach (var f in ended.Fighters)
                if (!_toldOut.Remove((ended.Id, f.Player.Id)))
                    Tell(f.Player.Id, new BattleNotice(BattleNoticeKind.Ended, ended.Id, ended.EnemyShip.Id, ended.Outcome, f.Exit,
                        f.Salvage, f.CrewLeft));
        }
        foreach (var p in World.Players)
        {
            if (p.Ship.Wreck && _wrecks.Add(p.Id))
                Tell(p.Id, new BattleNotice(BattleNoticeKind.Wrecked));
            else if (!p.Ship.Wreck)
                _wrecks.Remove(p.Id);
        }
        foreach (var (player, text) in World.TakeBattleNotices())
        {
            Logged?.Invoke($"{player}: {text}");
            Tell(player, new BattleNotice(BattleNoticeKind.Full, Text: text));
        }
        foreach (var (rescuer, wreck) in World.TakeRescueOffers())
        {
            Logged?.Invoke($"{rescuer} is at {wreck}'s wreck");
            Tell(rescuer, new BattleNotice(BattleNoticeKind.RescueOffer, Other: wreck));
        }
        foreach (var revival in World.TakeRevivals())   // Revive logs each
        {
            Tell(revival.Rescuer, new BattleNotice(BattleNoticeKind.Revived, Crew: revival.Crew, Other: revival.Wreck, Text: BattleNotice.Rescuer));
            Tell(revival.Wreck, new BattleNotice(BattleNoticeKind.Revived, Crew: revival.Crew, Other: revival.Rescuer, Text: BattleNotice.Rescued));
        }
        if (World.GameOver && !_gameOverLogged)
        {
            _gameOverLogged = true;
            Logged?.Invoke("Game over: the flagship was lost");
            foreach (var p in World.Players)
                Tell(p.Id, new BattleNotice(BattleNoticeKind.GameOver));
        }
    }

    /// <summary>
    /// The battle notices since last asked: the host's own, and each
    /// guest's by its peer (a notice for a player who's since left is dropped).
    /// </summary>
    public (List<BattleNotice> Host, List<(long Peer, BattleNotice Notice)> Guests) TakeBattleNotices()
    {
        var host = new List<BattleNotice>();
        var guests = new List<(long, BattleNotice)>();
        foreach (var (id, notice) in _notices)
        {
            if (id == HostId)
                host.Add(notice);
            foreach (var (peer, pid) in _peers)
                if (pid == id)
                    guests.Add((peer, notice));
        }
        _notices.Clear();
        return (host, guests);
    }
    private WorldSnapshot? _lastSnapshot;
    private bool _gameOverSnapshot;

    /// <summary>One world frame: the host's input (with the game's input delay), the step, and the snapshot for everyone.</summary>
    public WorldSnapshot Step(ShipInput hostInput)
    {
        World.SubmitInput(HostId, World.Frame + World.Settings.InputDelayFrames, hostInput);
        World.Step();
        TakeBattleNews();
        // Once the game is over the world has stopped (decision 12): its
        // frame no longer moves on, and the snapshot of the world as it
        // stopped stands. That is taken once, the first step the game is
        // seen over, though the world stopped between steps (the last guest
        // leaving the flagship's battle as it explodes ends it): numbered
        // after the last sent then, so the guests take it.
        if (World.GameOver && _gameOverSnapshot && _lastSnapshot is { } last)
            return last;
        var snapshot = World.Snapshot();
        if (_lastSnapshot is { } previous && snapshot.Frame <= previous.Frame)
            snapshot = snapshot with { Frame = previous.Frame + 1 };
        _gameOverSnapshot = World.GameOver;
        _snapshots.Record(snapshot);
        _lastSnapshot = snapshot;
        return snapshot;
    }

    /// <summary>
    /// The latest snapshot as the message for one guest: a delta from the
    /// newest one it's known to have, or in full (<see cref="SnapshotSender"/>);
    /// null, nothing to send, if it has said it has the latest (as after the
    /// game over, when the latest stands for good: not sent again in full
    /// every frame).
    /// </summary>
    public byte[]? SnapshotFor(long peer) =>
        _snapshots.HasLatest(peer) ? null : _snapshots.For(peer, _peers.GetValueOrDefault(peer));
}
