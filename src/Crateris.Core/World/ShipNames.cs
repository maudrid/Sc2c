// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (globdata.c InitGameStructures,
// gameopt.c NameCaptainOrShip, sis.h SIS_NAME_SIZE),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Content;

namespace Crateris.Core.World;

/// <summary>
/// Each player's captain and ship names (docs/plans/stage-3.md, item 2 and
/// decision 10), which UQM's talks speak (CommanderName, ShipName).
///
/// The flagship's are UQM's until renamed: the content's game strings
/// (NAMING_STRING_BASE + 2 and + 3, as InitGameStructures sets them). An
/// escort's ship is named from the host's setting when the escort is made
/// (<see cref="GameSettings.EscortShipName"/>); a guest's captain is the
/// name they join with. Only a name that isn't one of these is stored,
/// so the world needs no content to hold them.
/// </summary>
public static class ShipNames
{
    /// <summary>SIS_NAME_SIZE less its terminating zero: the longest name, in UTF-8 bytes.</summary>
    public const int MaxBytes = 15;

    /// <summary>
    /// A name as UQM keeps it (utf8StringCopy into SIS_NAME_SIZE): cut to
    /// <see cref="MaxBytes"/> UTF-8 bytes, never in the middle of a
    /// character. An empty name is a name, as UQM's text entry allows.
    /// </summary>
    public static string Fit(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Encoding.UTF8.GetByteCount(name) <= MaxBytes)
            return name;
        var fitted = new StringBuilder();
        int bytes = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxBytes)
                break;
            bytes += rune.Utf8SequenceLength;
            fitted.Append(rune.ToString());
        }
        return fitted.ToString();
    }

    /// <summary>The flagship's ship name before it's renamed (GAME_STRING (NAMING_STRING_BASE + 2)).</summary>
    public static string FlagshipDefault(GameStrings strings) => Fit(strings[GameStrings.NamingBase + 2]);

    /// <summary>The flagship's captain before renaming (GAME_STRING (NAMING_STRING_BASE + 3)).</summary>
    public static string CommanderDefault(GameStrings strings) => Fit(strings[GameStrings.NamingBase + 3]);

    /// <summary>A ship's name: its own, or for the flagship UQM's.</summary>
    public static string Ship(ShipKind kind, string? name, GameStrings strings) =>
        name ?? (kind == ShipKind.Flagship ? FlagshipDefault(strings) : "");

    /// <summary>A captain's name: their own, or the flagship's UQM's, or a guest's the name they joined with.</summary>
    public static string Captain(ShipKind kind, string? name, string playerName, GameStrings strings) =>
        name ?? (kind == ShipKind.Flagship ? CommanderDefault(strings) : Fit(playerName));
}
