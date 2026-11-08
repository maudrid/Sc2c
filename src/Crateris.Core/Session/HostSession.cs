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

    public string SaveJson() => HostSave.ToJson(World);

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
    /// --start-visit (testing): the host's ship by Earth's starbase, in the
    /// visit's first talk, as if it had just flown into it. Needs the talks'
    /// content (InvalidOperationException).
    /// </summary>
    public string StartVisit()
    {
        if (World.TalkLibrary is null || World.Systems is null)
            throw new InvalidOperationException("the talks need the content");
        int sol = World.Systems.Map.Stars.Single(s => s.Special == "SOL_DEFINED").Number;
        World.PutInOrbit(HostId, sol, 2, 1);
        World.Host.Ship.Ip!.ReturnFromOrbit();   // in Earth's inner system, by the starbase
        World.VisitStarbase(World.Host);
        return World.Visit is { } v ? $"Starting in the {v.Step} talk at Earth's starbase" : "Nothing to tell at Earth's starbase";
    }

    /// <summary>
    /// --guests-in-orbit (testing): each guest joining for the first time is
    /// put in orbit where the host is as it joins (its undocking scene still
    /// shows), rather than in the inner system beside it.
    /// </summary>
    public bool GuestsStartInOrbit { get; set; }

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
        if (Protocol.Accepted(result))
        {
            _peers[peer] = playerId;
            _snapshots.Forget(peer);   // a new guest starts from a full snapshot
        }
        return new Protocol.Welcome(result, answered, World.Settings.InputDelayFrames, message);
    }

    /// <summary>
    /// A guest's input for a frame (only the six input bits count), with
    /// the frame of the newest snapshot it has (<paramref name="has"/>),
    /// which the host's next snapshots for it are deltas from.
    /// </summary>
    public void GuestInput(long peer, long frame, int input, long has)
    {
        if (_peers.TryGetValue(peer, out var id))
        {
            World.SubmitInput(id, frame, (ShipInput)(input & 63));
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
            updates.Add((peer, view?.Encode() ?? Array.Empty<byte>()));
        }
        return updates;
    }

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
            updates.Add((peer, view?.Encode() ?? Array.Empty<byte>()));
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

    /// <summary>One world frame: the host's input (with the game's input delay), the step, and the snapshot for everyone.</summary>
    public WorldSnapshot Step(ShipInput hostInput)
    {
        World.SubmitInput(HostId, World.Frame + World.Settings.InputDelayFrames, hostInput);
        World.Step();
        var snapshot = World.Snapshot();
        _snapshots.Record(snapshot);
        return snapshot;
    }

    /// <summary>
    /// The latest snapshot as the message for one guest: a delta from the
    /// newest one it's known to have, or in full (<see cref="SnapshotSender"/>).
    /// </summary>
    public byte[] SnapshotFor(long peer) => _snapshots.For(peer, _peers.GetValueOrDefault(peer));
}
