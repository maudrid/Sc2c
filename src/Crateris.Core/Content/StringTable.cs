// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/libs/strings/getstr.c
// _GetStringData; src/uqm/gamestr.h),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;

namespace Crateris.Core.Content;

/// <summary>
/// A UQM string table (a STRTAB resource such as <c>base/gamestrings.txt</c>),
/// read exactly as UQM's <c>_GetStringData</c> reads it. Strings are
/// numbered from 0; their <c>#(name)</c> headers are only separators.
/// </summary>
public sealed class StringTable
{
    /// <summary>MAX_STRINGS: UQM stops reading a table at this many strings.</summary>
    public const int MaxStrings = 2048;

    /// <summary>The size of UQM's line buffer (CurrentLine): longer lines are read in pieces of one less.</summary>
    private const int LineBuffer = 1024;

    private readonly byte[][] _raw;

    private StringTable(byte[][] raw) => _raw = raw;

    /// <summary>How many strings the table has.</summary>
    public int Count => _raw.Length;

    /// <summary>
    /// A string's bytes as UQM keeps them: the text with a terminating NUL,
    /// or nothing at all for a string with no lines.
    /// </summary>
    public ReadOnlySpan<byte> Raw(int index) => _raw[index];

    /// <summary>A string as text (UTF-8, without UQM's terminating NUL).</summary>
    public string this[int index]
    {
        get
        {
            byte[] b = _raw[index];
            return b.Length == 0 ? "" : Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }
    }

    /// <summary>
    /// Reads a table as _GetStringData does. Lines are read as fgets reads
    /// them (at most 1,023 bytes at a time, up to and including a newline);
    /// a line that starts with '#' and has anything but '(' and ')' after it
    /// starts the next string, and other '#' lines are dropped; other lines
    /// are added to the current string up to any NUL in them (lines before
    /// the first string are dropped). Newlines and carriage returns at the
    /// end of each string are trimmed. Reading stops at <see cref="MaxStrings"/>.
    /// </summary>
    public static StringTable Parse(ReadOnlySpan<byte> data)
    {
        var strings = new List<List<byte>?>();
        int pos = 0;
        while (pos < data.Length)
        {
            // fgets: up to LineBuffer - 1 bytes, ending after a newline.
            int max = Math.Min(LineBuffer - 1, data.Length - pos);
            int nl = data.Slice(pos, max).IndexOf((byte)'\n');
            int take = nl >= 0 ? nl + 1 : max;
            ReadOnlySpan<byte> chunk = data.Slice(pos, take);
            pos += take;

            if (strings.Count - 1 >= MaxStrings - 1)
                break;   // too many strings

            // The C code treats the line as a C string: it ends at a NUL.
            int nul = chunk.IndexOf((byte)0);
            ReadOnlySpan<byte> line = nul >= 0 ? chunk[..nul] : chunk;

            if (chunk[0] == '#')
            {
                // strtok (&line[1], "()") finds a token if anything after
                // the '#' isn't a parenthesis.
                bool named = false;
                foreach (byte b in line[1..])
                {
                    if (b != '(' && b != ')')
                    {
                        named = true;
                        break;
                    }
                }
                if (named)
                {
                    if (strings.Count > 0)
                        Trim(strings[^1]);
                    strings.Add(null);
                }
            }
            else if (strings.Count > 0)
            {
                // The line joins the current string, replacing its NUL.
                var s = strings[^1] ??= new List<byte>();
                if (s.Count > 0)
                    s.RemoveAt(s.Count - 1);
                foreach (byte b in line)
                    s.Add(b);
                s.Add(0);
            }
        }
        if (strings.Count > 0)
            Trim(strings[^1]);
        return new StringTable(strings.Select(s => s?.ToArray() ?? Array.Empty<byte>()).ToArray());
    }

    // Drops newlines and carriage returns before the terminating NUL.
    private static void Trim(List<byte>? s)
    {
        if (s is null)
            return;
        while (s.Count > 1 && s[^2] is (byte)'\n' or (byte)'\r')
            s.RemoveAt(s.Count - 2);
    }

    /// <summary>Reads a STRTAB resource from the content package.</summary>
    public static StringTable Load(ContentPackage package, string resourceId)
    {
        var entry = package.Resource(resourceId);
        if (entry.Type != "STRTAB")
            throw new ContentPackageException($"{resourceId} is {entry.Type}, not a string table");
        return Parse(package.ReadFile(entry.Value));
    }
}

/// <summary>
/// The game strings (<c>text.starcon</c>, <c>base/gamestrings.txt</c>) and
/// their numbering in ranges from <c>src/uqm/gamestr.h</c>: GAME_STRING
/// (base + n) is <c>this[base + n]</c>.
/// </summary>
public sealed class GameStrings
{
    /// <summary>The resource the game strings are in.</summary>
    public const string ResourceId = "text.starcon";

    public const int StarCount = 133, DeviceCount = 29, CargoCount = 10, ElementsCount = 133, ScanCount = 56,
        StarNumberCount = 14, PlanetNumberCount = 33, MonthsCount = 12, FeedbackCount = 2, StarbaseCount = 5,
        EncounterCount = 8, NavigationCount = 6, NamingCount = 4, MeleeCount = 9, SavegameCount = 5,
        OptionCount = 5, QuitMenuCount = 3, StatusCount = 6, FlagshipCount = 13, OrbitScanCount = 19,
        MainMenuCount = 55, NetMeleeCount = 19;

    public const int StarBase = 0;
    public const int DeviceBase = StarBase + StarCount;
    public const int CargoBase = DeviceBase + DeviceCount;
    public const int ElementsBase = CargoBase + CargoCount;
    public const int ScanBase = ElementsBase + ElementsCount;
    public const int StarNumberBase = ScanBase + ScanCount;
    public const int PlanetNumberBase = StarNumberBase + StarNumberCount;
    public const int MonthsBase = PlanetNumberBase + PlanetNumberCount;
    public const int FeedbackBase = MonthsBase + MonthsCount;
    public const int StarbaseBase = FeedbackBase + FeedbackCount;
    public const int EncounterBase = StarbaseBase + StarbaseCount;
    public const int NavigationBase = EncounterBase + EncounterCount;
    public const int NamingBase = NavigationBase + NavigationCount;
    public const int MeleeBase = NamingBase + NamingCount;
    public const int SavegameBase = MeleeBase + MeleeCount;
    public const int OptionBase = SavegameBase + SavegameCount;
    public const int QuitMenuBase = OptionBase + OptionCount;
    public const int StatusBase = QuitMenuBase + QuitMenuCount;
    public const int FlagshipBase = StatusBase + StatusCount;
    public const int OrbitScanBase = FlagshipBase + FlagshipCount;
    public const int MainMenuBase = OrbitScanBase + OrbitScanCount;
    public const int NetMeleeBase = MainMenuBase + MainMenuCount;
    public const int Count = NetMeleeBase + NetMeleeCount;

    /// <summary>Each range as gamestr.h names it, with its base and count.</summary>
    public static readonly IReadOnlyList<(string Name, int Base, int Count)> Ranges = new[]
    {
        ("STAR_STRING_BASE", StarBase, StarCount), ("DEVICE_STRING_BASE", DeviceBase, DeviceCount),
        ("CARGO_STRING_BASE", CargoBase, CargoCount), ("ELEMENTS_STRING_BASE", ElementsBase, ElementsCount),
        ("SCAN_STRING_BASE", ScanBase, ScanCount), ("STAR_NUMBER_BASE", StarNumberBase, StarNumberCount),
        ("PLANET_NUMBER_BASE", PlanetNumberBase, PlanetNumberCount), ("MONTHS_STRING_BASE", MonthsBase, MonthsCount),
        ("FEEDBACK_STRING_BASE", FeedbackBase, FeedbackCount), ("STARBASE_STRING_BASE", StarbaseBase, StarbaseCount),
        ("ENCOUNTER_STRING_BASE", EncounterBase, EncounterCount), ("NAVIGATION_STRING_BASE", NavigationBase, NavigationCount),
        ("NAMING_STRING_BASE", NamingBase, NamingCount), ("MELEE_STRING_BASE", MeleeBase, MeleeCount),
        ("SAVEGAME_STRING_BASE", SavegameBase, SavegameCount), ("OPTION_STRING_BASE", OptionBase, OptionCount),
        ("QUITMENU_STRING_BASE", QuitMenuBase, QuitMenuCount), ("STATUS_STRING_BASE", StatusBase, StatusCount),
        ("FLAGSHIP_STRING_BASE", FlagshipBase, FlagshipCount), ("ORBITSCAN_STRING_BASE", OrbitScanBase, OrbitScanCount),
        ("MAINMENU_STRING_BASE", MainMenuBase, MainMenuCount), ("NETMELEE_STRING_BASE", NetMeleeBase, NetMeleeCount),
    };

    private readonly StringTable _table;

    public GameStrings(StringTable table)
    {
        if (table.Count < Count)
            throw new ContentPackageException($"the game strings have {table.Count} strings, fewer than the {Count} UQM numbers");
        _table = table;
    }

    public static GameStrings Load(ContentPackage package) => new(StringTable.Load(package, ResourceId));

    /// <summary>The whole table, for code that needs the strings as UQM keeps them.</summary>
    public StringTable Table => _table;

    /// <summary>GAME_STRING (index).</summary>
    public string this[int index] => _table[index];
}
