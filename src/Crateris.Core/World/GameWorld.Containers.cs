// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.World;

/// <summary>
/// A leaver's cargo container (stage 3c item 5; the owner's answer to the
/// stage 3c plan's question 5): the devices their ship held, left where it
/// was as they left (or were disconnected), at <see cref="Place"/>: in
/// hyperspace (logical units), or in a system's outer view or a planet's
/// inner one (interplanetary units), as a wreck's place is given. Any other
/// player flying into it takes what's in it; if its leaver rejoins while it's
/// still there, it goes back to their ship. Saved.
/// </summary>
public sealed class DeviceContainer
{
    private readonly SortedSet<int> _devices;

    internal DeviceContainer(int id, string owner, string ownerName, BattlePlace place, IEnumerable<int> devices)
    {
        Id = id;
        Owner = owner;
        OwnerName = ownerName;
        Place = place;
        _devices = new SortedSet<int>(devices);
    }

    /// <summary>Its number in the game, from 1, never used twice in a game (not saved: a loaded game numbers its own).</summary>
    public int Id { get; }

    /// <summary>The leaver's player id, whose ship takes it back on rejoining.</summary>
    public string Owner { get; }

    /// <summary>The leaver's name as they left, for the status panel and the markers.</summary>
    public string OwnerName { get; }

    /// <summary>Where it is.</summary>
    public BattlePlace Place { get; }

    /// <summary>The devices in it, by globdata.h's numbers.</summary>
    public IReadOnlyCollection<int> Devices => _devices;
}

/// <summary>
/// A container as snapshots send it: its number, its leaver's name, its
/// place (a star and planet or none, and the place in that view's units)
/// and how many devices it holds.
/// </summary>
public sealed record ContainerSnapshot(int Id, string OwnerName, int? Star, int? InnerPlanet, int X, int Y, int Devices);

// The leavers' containers (stage 3c item 5).
public sealed partial class GameWorld
{
    private readonly List<DeviceContainer> _containers = new();
    private int _nextContainer = 1;
    // (player, container): the player was last seen flying in the container's place out of its reach.
    private readonly HashSet<(string Player, int Container)> _seenOutOfContainer = new();

    /// <summary>The devices' names in words (by number), for the notices: the content's, if the host has it.</summary>
    public Func<int, string?>? DeviceNames { get; set; }

    /// <summary>The leavers' containers in space.</summary>
    public IReadOnlyList<DeviceContainer> Containers => _containers;

    /// <summary>
    /// A player's devices left behind in a container where their ship is
    /// (the plan's readings of question 5): as they leave, after the
    /// leaving has put their ship where it waits (a lander taken back up, a
    /// meeting or battle left where the ship was before it, a docked ship by
    /// the starbase), and for every guest away as a save loads. A ship in
    /// orbit leaves it beside its world, in the planet's inner view where
    /// the ship is; a wreck where it lies. Nothing for a ship holding no
    /// device, nor for the host, whose leaving ends the game (its devices
    /// stay aboard, saved).
    /// </summary>
    /// <param name="tell">Whether to tell the players present (as a player leaves; not as a save loads).</param>
    internal void LeaveDevicesBehind(Player player, bool tell = false)
    {
        if (player.IsHost || player.Ship.Hold.Devices.Count == 0)
            return;
        int count = player.Ship.Hold.Devices.Count;
        AddContainer(player.Id, player.Name, PlaceOf(player.Ship), player.Ship.Hold.Devices.ToList());
        player.Ship.Hold.SetDevices(Array.Empty<int>());
        if (tell)
            foreach (var p in Others(player.Id))
                Notice(p, NoticeEvent.ContainerLeft, player.Id, "", count);
    }

    /// <summary>A container put in space (a leaver's, or a save's).</summary>
    internal DeviceContainer AddContainer(string owner, string ownerName, BattlePlace place, IReadOnlyCollection<int> devices)
    {
        var container = new DeviceContainer(_nextContainer++, owner, ownerName, place, devices);
        _containers.Add(container);
        return container;
    }

    /// <summary>
    /// A rejoining player's container, if it's still there: its devices go
    /// back to their ship (question 5), and it's gone.
    /// </summary>
    private void TakeBackContainer(Player player)
    {
        foreach (var c in _containers.Where(c => c.Owner == player.Id).ToList())
        {
            foreach (int d in c.Devices)
                player.Ship.Hold.AddDevice(d);
            RemoveContainer(c);
        }
    }

    private void RemoveContainer(DeviceContainer c)
    {
        _containers.Remove(c);
        _seenOutOfContainer.RemoveWhere(t => t.Container == c.Id);
    }

    /// <summary>
    /// After the wrecks' rescues: a ship flying free (not a wreck, not in a
    /// battle, a talk, orbit or docked, not entering a star) flying into a
    /// container, from outside its reach to inside (the battle markers'
    /// reach, as a wreck's), takes what's in it, and it's gone. As with a
    /// wreck, a ship first seen inside its reach (the container left where
    /// it was) must fly out of it and back. Not while the game waits for a
    /// story talk or the host's pause (nothing flies then).
    /// </summary>
    private void FlyIntoContainers()
    {
        if (_containers.Count == 0)
        {
            _seenOutOfContainer.Clear();
            return;
        }
        foreach (var player in _players)
        {
            bool flying = Flying(player);
            foreach (var c in _containers.ToList())
            {
                var key = (player.Id, c.Id);
                if (!flying || !InPlace(c.Place, player.Ship))
                {
                    _seenOutOfContainer.Remove(key);
                    continue;
                }
                if (!InReach(c.Place, player.Ship))
                {
                    _seenOutOfContainer.Add(key);
                    continue;
                }
                if (!_seenOutOfContainer.Remove(key))
                    continue;
                foreach (int d in c.Devices)
                    player.Ship.Hold.AddDevice(d);
                RemoveContainer(c);
                // Told to the taker, the devices named (by the content's names, if the host has them), and to the others.
                var names = c.Devices.Select(d => DeviceNames?.Invoke(d)).ToList();
                Notice(player, NoticeEvent.ContainerYours, player.Id, c.OwnerName, c.Devices.Count,
                    names.All(n => n is { Length: > 0 }) ? string.Join(", ", names.Select(n => "the " + n)) : "");
                foreach (var p in Others(player.Id))
                    Notice(p, NoticeEvent.ContainerTaken, player.Id, c.OwnerName);
            }
        }
    }

    // The containers for the snapshot, each with its leaver's name and place.
    private List<ContainerSnapshot> ContainersSnapshot() => _containers
        .Select(c => new ContainerSnapshot(c.Id, c.OwnerName, c.Place.Star, c.Place.InnerPlanet, c.Place.X, c.Place.Y, c.Devices.Count))
        .ToList();
}
