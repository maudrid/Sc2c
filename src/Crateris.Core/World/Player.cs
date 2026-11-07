// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

public enum ShipKind
{
    Flagship,
    Escort,
}

/// <summary>What a player is doing (each runs on the host at its own frame rate).</summary>
public enum Activity
{
    /// <summary>Flying in hyperspace, 24 frames a second.</summary>
    Hyperspace,
    /// <summary>Flying in a system's outer view, 30 frames a second.</summary>
    OuterSystem,
    /// <summary>Flying in a planet's inner system, 30 frames a second.</summary>
    InnerSystem,
    /// <summary>In orbit around a planet or moon; the ship doesn't move.</summary>
    Orbit,
    /// <summary>In orbit, with its lander on the world's surface, 35 frames a second.</summary>
    Planetside,
    /// <summary>
    /// In a talk (or between the talks of a visit, as time passes): the
    /// ship waits in its system where it touched the world.
    /// </summary>
    Talk,
}

/// <summary>A player's ship. The host flies the flagship; guests captain escorts.</summary>
public sealed class PlayerShip
{
    public ShipKind Kind { get; }
    public ShipLoadout Loadout { get; }

    /// <summary>Crew, landers, minerals and bio data aboard, and the lander's upgrades.</summary>
    public ShipHold Hold { get; }

    /// <summary>
    /// The ship's name (ShipName), or null for the flagship's UQM name
    /// (<see cref="ShipNames"/>). An escort's is set when it's made.
    /// </summary>
    public string? Name { get; internal set; }

    /// <summary>
    /// Flight in hyperspace. While in a system it keeps the hyperspace
    /// position the ship returns to: the star's (ExploreSolarSys).
    /// </summary>
    public HyperFlight Flight { get; }

    /// <summary>The star (map number) whose system the ship is in, or null in hyperspace.</summary>
    public int? SystemStar { get; internal set; }

    /// <summary>Flight inside that system, or null in hyperspace.</summary>
    public IpFlight? Ip { get; internal set; }

    /// <summary>
    /// While the ship's entry into a star plays (<see cref="StarEntry"/>):
    /// where and how it touched the star. Its system flight waits until
    /// the entry is over. Not saved: a game loaded mid-entry starts in the
    /// system.
    /// </summary>
    public StarEntryState? Entry { get; internal set; }

    /// <summary>
    /// The last world frame whose input is ignored after an entry into a
    /// star: input for frames up to the entry's end plus the input delay
    /// was read while the entry was still playing on its player's screen.
    /// </summary>
    public long InputHeldUntil { get; internal set; } = -1;

    /// <summary>In orbit: the world's report, scan seeds and nodes (from <see cref="IpFlight.OrbitData"/>).</summary>
    public OrbitData? Orbit => Ip?.OrbitData;

    /// <summary>The world orbited, or null when not in orbit.</summary>
    public WorldId? OrbitedWorld => Ip?.Orbit is { } o ? new WorldId(SystemStar!.Value, o.Planet, o.Moon) : null;

    /// <summary>The ship's lander on the orbited world's surface, while it's there (not saved: a save keeps it in orbit).</summary>
    public Planetside.Lander? Lander { get; internal set; }

    /// <summary>The visit to Earth's starbase this ship is on, while it's on one (not saved).</summary>
    public StarbaseVisit? Visit { get; internal set; }

    public Activity Activity => Visit is not null ? Activity.Talk : Ip switch
    {
        null => Activity.Hyperspace,
        { Orbit: not null } when Lander is not null => Activity.Planetside,
        { Orbit: not null } => Activity.Orbit,
        { InnerPlanet: not null } => Activity.InnerSystem,
        _ => Activity.OuterSystem,
    };

    /// <summary>
    /// The module slots (ModuleSlots, 16 of them), each the module's name
    /// as sis.h has it, null if empty. A flagship's are a new game's
    /// (data/new-game.json) less what its loadout changes; an escort's
    /// its kit's.
    /// </summary>
    public IReadOnlyList<string?> ModuleSlots => _moduleSlots ??= Modules(Kind, Loadout);

    private IReadOnlyList<string?>? _moduleSlots;

    /// <summary>NUM_MODULE_SLOTS (sis.h).</summary>
    public const int ModuleSlotCount = 16;

    // The modules the loadout says the ship has, in the slots a new game's
    // flagship has them in (InitGameStructures: a fuel tank in slot 1,
    // a crew pod in 2, a storage bay in 8 and the gun in 15), more of a
    // kind in the empty slots after.
    private static string?[] Modules(ShipKind kind, ShipLoadout loadout)
    {
        var slots = new string?[ModuleSlotCount];
        void Put(string module, int count, int first)
        {
            for (int i = first; count > 0 && i < ModuleSlotCount + first; ++i)
                if (slots[i % ModuleSlotCount] is null)
                {
                    slots[i % ModuleSlotCount] = module;
                    --count;
                }
        }
        if (kind == ShipKind.Flagship)
            Put("GUN_WEAPON", 1, 15);
        Put("FUEL_TANK", loadout.FuelTanks, 1);
        Put("HIGHEFF_FUELSYS", loadout.HighEfficiencyTanks, 1);
        Put("CREW_POD", loadout.CrewCapacity / ShipLoadout.CrewPodCapacity, 2);
        Put("STORAGE_BAY", loadout.StorageBays, 8);
        return slots;
    }

    public PlayerShip(ShipKind kind, ShipLoadout loadout)
    {
        Kind = kind;
        Loadout = loadout;
        Hold = new ShipHold(loadout);
        Flight = new HyperFlight(ShipDrive.ForHyperspace(loadout.Thrusters, loadout.Jets),
            Hyperspace.Fuel.TankCapacity(loadout.FuelTanks, loadout.HighEfficiencyTanks))
        {
            Fuel = loadout.Fuel,
        };
    }
}

/// <summary>
/// A player in the host's game, kept in the host's save while away.
/// The id is stable per player (the client keeps it), so a returning
/// player gets their own ship back.
/// </summary>
public sealed class Player
{
    public string Id { get; }
    public string Name { get; set; }
    public bool IsHost { get; }
    public PlayerShip Ship { get; }

    /// <summary>Connected and flying. Absent players' ships wait in the save, not in space.</summary>
    public bool Present { get; set; }

    /// <summary>
    /// The captain's name (CommanderName), or null for the default: the
    /// flagship's UQM name, a guest's the name they join with (<see cref="ShipNames"/>).
    /// </summary>
    public string? CaptainName { get; internal set; }

    /// <summary>Set on a guest's first join until their client has shown the undocking scene.</summary>
    public bool UndockPending { get; set; }

    public Player(string id, string name, bool isHost, PlayerShip ship)
    {
        Id = id;
        Name = name;
        IsHost = isHost;
        Ship = ship;
    }
}

/// <summary>
/// A ship's contact with a star, as the entry starts from it: the world
/// frame of the contact, the ship's hyperspace position and its place in
/// its view then (so its sprite was at LogX + ViewX - CenterX), its
/// facing, the star, and how many frames the entry plays after the contact
/// frame (<see cref="StarEntry.Animate"/>'s steps). The system's flight
/// starts on the frame after the last. An escort that joined during its
/// host's entry has the host's, <paramref name="Following"/> the host: it
/// waits with it and arrives with it, but rides in with the flagship rather
/// than being drawn entering itself.
/// </summary>
public sealed record StarEntryState(long ContactFrame, int LogX, int LogY, short ViewX, short ViewY, int Facing,
    int Star, int Steps, string? Following = null)
{
    /// <summary>Where the ship's sprite was at contact, in hyperspace logical units.</summary>
    public (long X, long Y) Sprite => HyperFlight.SpriteAt(LogX, LogY, ViewX, ViewY);

    /// <summary>Whether the entry is still playing at a world frame (the contact frame and the steps after it).</summary>
    public bool PlayingAt(long frame) => frame >= ContactFrame && frame <= ContactFrame + Steps;
}
