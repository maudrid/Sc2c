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

    /// <summary>The minerals in the hold by element category (ElementAmounts); a script may change them.</summary>
    IList<int> Minerals { get; }
}
