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
/// flagship's (GLOBAL_SIS): the talk's starter's ship (decision 4). Its
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

    /// <summary>The bio data aboard (TotalBioMass), which the cargo display shows as the minerals are sold.</summary>
    int BioData => 0;

    /// <summary>The minerals the storage holds (GetStorageBayCapacity): the ship's own, as its hold is.</summary>
    int StorageCapacity => ModuleSlots.Count(m => m == "STORAGE_BAY") * 500;

    /// <summary>Drive slots with a thruster (DriveSlots below EMPTY_SLOT); the flagship's, in the game (decision 4).</summary>
    int Thrusters { get; }

    /// <summary>Jet slots with turning jets (JetSlots below EMPTY_SLOT); the flagship's, in the game.</summary>
    int Jets { get; }

    /// <summary>The module slots (ModuleSlots), each its module's name as sis.h has it (CREW_POD and so on), null if empty; the flagship's, in the game.</summary>
    IReadOnlyList<string?> ModuleSlots { get; }

    /// <summary>What the escort fleet's ships are worth (CalculateEscortsWorth).</summary>
    int EscortsWorth { get; }

    /// <summary>How many more ships the escort fleet has room for (EscortFeasibilityStudy).</summary>
    int EscortRoom { get; }

    /// <summary>Ships of a race (races.h's number) join the escort fleet, as many as it has room for; returns how many did (AddEscortShips).</summary>
    int AddEscorts(int race, int count);
}
