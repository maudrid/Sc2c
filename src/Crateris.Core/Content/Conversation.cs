// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/libs/strings/getstr.c
// _GetConversationData), Copyright Paul Reiche, Fred Ford and Toys for
// Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;

namespace Crateris.Core.Content;

/// <summary>One entry of a conversation: its name (the script's phrase id), its text, and its voice clip's file name, if any.</summary>
public sealed class ConversationEntry
{
    private readonly byte[] _text;

    internal ConversationEntry(string name, byte[] text, string? clip)
    {
        Name = name;
        _text = text;
        Clip = clip;
    }

    /// <summary>The name in its <c>#(NAME)</c> header: an alien's line in capitals, a player's phrase in lower case.</summary>
    public string Name { get; }

    /// <summary>The text's bytes as UQM keeps them: with a terminating NUL, or nothing for an entry with no lines.</summary>
    public ReadOnlySpan<byte> Raw => _text;

    /// <summary>The text (UTF-8, without the NUL).</summary>
    public string Text => _text.Length == 0 ? "" : Encoding.UTF8.GetString(_text, 0, _text.Length - 1);

    /// <summary>The voice clip named after the header (<c>commander-000.ogg</c>); no voices are played yet.</summary>
    public string? Clip { get; }
}

/// <summary>
/// A race's conversation (a CONVERSATION resource such as
/// <c>base/comm/commander/commander.txt</c>), read exactly as UQM's
/// <c>_GetConversationData</c> reads it. Entry <c>n</c> is the script's
/// phrase <c>n + 1</c> (its strings.h enum, which starts with NULL_PHRASE
/// at 0); data/conversations.json lists each script's phrases in that
/// order.
/// </summary>
public sealed class Conversation
{
    /// <summary>MAX_STRINGS: UQM stops reading at this many entries.</summary>
    public const int MaxStrings = StringTable.MaxStrings;

    private const int LineBuffer = 1024;

    private readonly ConversationEntry[] _entries;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    private Conversation(ConversationEntry[] entries)
    {
        _entries = entries;
        // UQM's name index keeps the first entry of a name (StringHashTable_add refuses a second).
        for (int i = 0; i < entries.Length; ++i)
            _byName.TryAdd(entries[i].Name, i);
    }

    public int Count => _entries.Length;

    public ConversationEntry this[int index] => _entries[index];

    public IReadOnlyList<ConversationEntry> Entries => _entries;

    /// <summary>The entry with this name (GetStringByName), or -1.</summary>
    public int IndexOf(string name) => _byName.TryGetValue(name, out int i) ? i : -1;

    /// <summary>
    /// Reads a conversation as _GetConversationData does. Lines are read as
    /// fgets reads them (at most 1,023 bytes at a time, up to and including
    /// a newline). A line starting with '#' whose text after it (to any NUL)
    /// has a token between '(' and ')' delimiters (strtok) starts the next
    /// entry, named by it; the next token, up to a space, tab, newline or
    /// ')', is its voice clip; other '#' lines are dropped. Other lines are
    /// added to the current entry up to any NUL in them (lines before the
    /// first entry are dropped). Newlines and carriage returns at the end of
    /// each entry are trimmed. Reading stops at <see cref="MaxStrings"/>.
    /// </summary>
    public static Conversation Parse(ReadOnlySpan<byte> data)
    {
        var names = new List<string>();
        var clips = new List<string?>();
        var texts = new List<List<byte>?>();
        int pos = 0;
        while (pos < data.Length)
        {
            int max = Math.Min(LineBuffer - 1, data.Length - pos);
            int nl = data.Slice(pos, max).IndexOf((byte)'\n');
            int take = nl >= 0 ? nl + 1 : max;
            ReadOnlySpan<byte> chunk = data.Slice(pos, take);
            pos += take;

            if (texts.Count - 1 >= MaxStrings - 1)
                break;   // too many strings

            int nul = chunk.IndexOf((byte)0);
            ReadOnlySpan<byte> line = nul >= 0 ? chunk[..nul] : chunk;

            if (chunk[0] == '#')
            {
                // strcpy (CopyLine, CurrentLine); name = strtok (&CopyLine[1], "()");
                int at = 1;
                var name = Token(line, ref at, "()"u8);
                if (name is null)
                    continue;
                if (texts.Count > 0)
                    Trim(texts[^1]);
                names.Add(Encoding.UTF8.GetString(name.Value.Span));
                texts.Add(null);
                // ts = strtok (NULL, " \t\r\n)");
                var clip = Token(line, ref at, " \t\r\n)"u8);
                clips.Add(clip is { } c ? Encoding.UTF8.GetString(c.Span) : null);
            }
            else if (texts.Count > 0)
            {
                var s = texts[^1] ??= new List<byte>();
                if (s.Count > 0)
                    s.RemoveAt(s.Count - 1);
                foreach (byte b in line)
                    s.Add(b);
                s.Add(0);
            }
        }
        if (texts.Count > 0)
            Trim(texts[^1]);
        return new Conversation(names.Select((n, i) =>
            new ConversationEntry(n, texts[i]?.ToArray() ?? Array.Empty<byte>(), clips[i])).ToArray());
    }

    /// <summary>
    /// strtok over a line: skips the delimiters at <paramref name="at"/>,
    /// takes what follows up to the next delimiter (or the end), and moves
    /// past that delimiter, as strtok overwrites it and resumes after it.
    /// </summary>
    private static ReadOnlyMemory<byte>? Token(ReadOnlySpan<byte> line, ref int at, ReadOnlySpan<byte> delimiters)
    {
        while (at < line.Length && delimiters.IndexOf(line[at]) >= 0)
            ++at;
        if (at >= line.Length)
            return null;
        int start = at;
        while (at < line.Length && delimiters.IndexOf(line[at]) < 0)
            ++at;
        var token = line[start..at].ToArray();
        if (at < line.Length)
            ++at;
        return token;
    }

    private static void Trim(List<byte>? s)
    {
        if (s is null)
            return;
        while (s.Count > 1 && s[^2] is (byte)'\n' or (byte)'\r')
            s.RemoveAt(s.Count - 2);
    }

    /// <summary>Reads a CONVERSATION resource from the content package (the base content's, which names no voice folder).</summary>
    public static Conversation Load(ContentPackage package, string resourceId)
    {
        var entry = package.Resource(resourceId);
        if (entry.Type != "CONVERSATION")
            throw new ContentPackageException($"{resourceId} is {entry.Type}, not a conversation");
        // CONVERSATION:file[:voice folder[:timestamps]]; the base content gives only the file.
        string file = entry.Value.Split(':')[0];
        return Parse(package.ReadFile(file));
    }
}
