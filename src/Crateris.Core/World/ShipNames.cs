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
/// (NAMING_STRING_BASE + 2 and + 3, as InitGameStructures sets them), and
/// those aren't stored, so the world needs no content to hold them. An
/// escort's ship is named from the host's setting when the escort is made
/// (<see cref="GameSettings.EscortShipName"/>) and stored like a rename; a
/// guest's captain is the name they join with, unless renamed.
/// </summary>
public static class ShipNames
{
    /// <summary>SIS_NAME_SIZE less its terminating zero: the longest name, in UTF-8 bytes.</summary>
    public const int MaxBytes = 15;

    /// <summary>
    /// A name as UQM's text entry would take it typed (DoTextEntry,
    /// getchar.c): each character in turn, if it's printable
    /// (UniChar_isPrint) and still fits in <see cref="MaxBytes"/> UTF-8
    /// bytes; one that doesn't is left out and the rest still tried. Text
    /// that isn't valid UTF-16 is left out too. An empty name is a name, as
    /// UQM's text entry allows. (UQM cuts its default names with
    /// utf8StringCopy, which can split a character; the shipped defaults,
    /// "Vindicator" and "Zelnick", fit whole.)
    /// </summary>
    public static string Fit(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var fitted = new StringBuilder(name.Length);
        int bytes = 0;
        var rest = name.AsSpan();
        while (!rest.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(rest, out var rune, out int used);
            rest = rest[used..];
            if (status != System.Buffers.OperationStatus.Done || !IsPrint(rune.Value)
                    || bytes + rune.Utf8SequenceLength > MaxBytes)
                continue;
            bytes += rune.Utf8SequenceLength;
            fitted.Append(rune.ToString());
        }
        return fitted.ToString();
    }

    /// <summary>UniChar_isPrint and UniChar_isGraph (unicode.c): a space, ASCII's graphic characters, and everything past Latin-1's controls but the private use area.</summary>
    private static bool IsPrint(int ch) =>
        ch == 0x20 || (ch > 0x20 && ch < 0x7F) || (ch > 0xA0 && (ch < 0xE000 || ch > 0xF8FF));

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

    /// <summary>A ship's name as a snapshot gives it.</summary>
    public static string Ship(ShipSnapshot ship, GameStrings strings) => Ship(ship.Kind, ship.ShipName, strings);

    /// <summary>A captain's name as a snapshot gives it.</summary>
    public static string Captain(ShipSnapshot ship, GameStrings strings) => Captain(ship.Kind, ship.CaptainName, ship.Name, strings);

    /// <summary>A player's ship's name.</summary>
    public static string Ship(Player player, GameStrings strings) => Ship(player.Ship.Kind, player.Ship.Name, strings);

    /// <summary>A player's captain's name.</summary>
    public static string Captain(Player player, GameStrings strings) => Captain(player.Ship.Kind, player.CaptainName, player.Name, strings);
}
