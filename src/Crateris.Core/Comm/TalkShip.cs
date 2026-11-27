// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Comm;

/// <summary>
/// The ship a talk reads and changes, where UQM's scripts use the
/// flagship's (GLOBAL_SIS): the ship of whoever answers the talk (decision 4,
/// and the owner's rule of 2026-11-12). Its
/// names are the talk's to speak; the rest the scripts may change, as the
/// commander's gifts do.
/// </summary>
public interface ITalkShip
{
    /// <summary>The captain's name (CommanderName).</summary>
    string CaptainName { get; }

    /// <summary>The ship's name (ShipName).</summary>
    string ShipName { get; }

    /// <summary>Crew aboard (CrewEnlisted).</summary>
    int Crew { get; set; }

    /// <summary>The crew the crew pods hold (GetCrewPodCapacity).</summary>
    int CrewCapacity { get; }

    /// <summary>Fuel in 1/100 units (FuelOnBoard).</summary>
    uint Fuel { get; set; }

    /// <summary>The fuel the tanks hold, in 1/100 units (GetFuelTankCapacity).</summary>
    uint FuelCapacity { get; }

    /// <summary>Landers aboard (NumLanders).</summary>
    int Landers { get; set; }

    /// <summary>Resource units (ResUnits).</summary>
    uint ResourceUnits { get; set; }

    /// <summary>The minerals in the hold by element category (ElementAmounts); a script may change them (selling them).</summary>
    IList<int> Minerals { get; }

    /// <summary>
    /// The bio data aboard (TotalBioMass), which the cargo display shows as
    /// the minerals are sold; a script may change it (the Umgah's reward
    /// adds to it, the Melnorme buy it): 0 or more. A COUNT in UQM, which
    /// would wrap at 65,536; the port's doesn't, as the hold keeps bio data
    /// with no upper limit (row 22).
    /// </summary>
    int BioData { get; set; }

    /// <summary>The minerals the storage holds (GetStorageBayCapacity): the ship's own, as its hold is.</summary>
    int StorageCapacity => ModuleSlots.Count(m => m == "STORAGE_BAY") * 500;

    /// <summary>
    /// Where the ship is on the star map (GLOBAL_SIS (log_x), log_y): in
    /// hyperspace its own place, in a system the star's (ExploreSolarSys
    /// sets it so); a new game's at Sol unless the ship says otherwise.
    /// </summary>
    (int X, int Y) LogPlace => (Uqm.Units.UniverseToLogX(1752), Uqm.Units.UniverseToLogY(1450));

    /// <summary>Drive slots with a thruster (DriveSlots below EMPTY_SLOT); the flagship's, in the game (decision 4).</summary>
    int Thrusters { get; }

    /// <summary>Jet slots with turning jets (JetSlots below EMPTY_SLOT); the flagship's, in the game.</summary>
    int Jets { get; }

    /// <summary>The module slots (ModuleSlots), each its module's name as sis.h has it (CREW_POD and so on), null if empty; the flagship's, in the game.</summary>
    IReadOnlyList<string?> ModuleSlots { get; }

    /// <summary>What the escort fleet's ships are worth (CalculateEscortsWorth).</summary>
    int EscortsWorth { get; }

    /// <summary>
    /// The escort fleet's ships' races (races.h's numbers), in
    /// built_ship_q's order: what CountEscortShips, HaveEscortShip and
    /// RemoveEscortShips look through, and GetHeadLink sees as empty or not.
    /// </summary>
    IReadOnlyList<int> EscortRaces { get; }

    /// <summary>How many more ships the escort fleet has room for (EscortFeasibilityStudy).</summary>
    int EscortRoom { get; }

    /// <summary>
    /// Ships of a race (races.h's number) join the escort fleet, as many as
    /// it has room for; returns how many did (AddEscortShips). Their
    /// captains' names are drawn from <paramref name="random"/> (TFB_Random:
    /// the talk's generator).
    /// </summary>
    int AddEscorts(int race, int count, Func<uint> random);

    /// <summary>
    /// Up to <paramref name="count"/> ships of a race (races.h's number)
    /// leave the escort fleet, the first in its order first; returns how
    /// many did (RemoveSomeEscortShips).
    /// </summary>
    int RemoveEscorts(int race, int count);

    /// <summary>
    /// The first escort of a race (races.h's number) whose crew is its
    /// race's starting crew gets <paramref name="crew"/> crew and the
    /// captain <paramref name="captain"/>, if there's one
    /// (SetEscortCrewComplement).
    /// </summary>
    void SetEscortCrewComplement(int race, int crew, int captain);

    // ---- what the Melnorme read and change (stage 3b-2 item 4): the answering ship's own

    /// <summary>
    /// The answerer's Melnorme credits (MELNORME_CREDIT0/1, each player's
    /// own: ShipHold.Credits), read and written live, so a gift that reaches
    /// them mid-talk counts (the stage 3b-2 plan's item 4).
    /// </summary>
    uint Credits { get => 0; set { } }

    /// <summary>The answering ship's lander upgrades and shields (IMPROVED_LANDER_*, LANDER_SHIELDS: each ship's own).</summary>
    World.LanderUpgrades Upgrades { get => World.LanderUpgrades.None; set { } }

    /// <summary>MELNORME_RESCUE_REFUSED: whether the answerer refused the Melnorme's rescue last time (each player's own).</summary>
    int RescueRefused { get => 0; set { } }

    /// <summary>MELNORME_RESCUE_COUNT: how many times the Melnorme have rescued the answerer, as their line counts it (each player's own).</summary>
    int RescueCount { get => 0; set { } }

    /// <summary>
    /// The answering ship's own drive slots (sis.h's numbers: FUSION_THRUSTER
    /// or EMPTY_SLOT + 0), which the Melnorme value and strip, where the
    /// commander grades the flagship's (<see cref="Thrusters"/>): a guest's
    /// escort its own (stage 4 item 8's note). Writable.
    /// </summary>
    IList<byte> OwnDriveSlots => Array.Empty<byte>();

    /// <summary>The answering ship's own jet slots (TURNING_JETS or EMPTY_SLOT + 1), writable.</summary>
    IList<byte> OwnJetSlots => Array.Empty<byte>();

    /// <summary>The answering ship's own module slots (CREW_POD and after, or EMPTY_SLOT + 2), writable.</summary>
    IList<byte> OwnModuleSlots => Array.Empty<byte>();

    /// <summary>The crew the answering ship holds beside its crew pods (an escort's kit's; a flagship's none).</summary>
    int KitCrew => 0;

    // ---- the devices (stage 3c item 5): the answering ship's own

    /// <summary>
    /// The value a device held by ships has on the answering ship
    /// (<see cref="Story.DeviceTable.HeldFields"/>: 1 if it holds it, the
    /// Ultron's repair level for ULTRON_CONDITION, 0 if not), read live, as
    /// UQM's scripts read the one ship's GameState (the owner's answer to
    /// the stage 3c plan's question 5: a talk reads what the answering ship holds).
    /// </summary>
    int Device(string field);

    /// <summary>
    /// A talk gives the answering ship a device held by ships, or takes it
    /// (SET_GAME_STATE on its field: the value as <see cref="Device"/> reads it, 0 to take it).
    /// </summary>
    void SetDevice(string field, int value);

    /// <summary>Whether the answering ship is the host's (the bomb fits only the host's ship, question 5).</summary>
    bool IsHost { get; }
}
