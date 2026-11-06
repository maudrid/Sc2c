// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm.Scripts;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Comm;

/// <summary>
/// What the host needs from the content to run talks: each ported
/// script's lines (its CONVERSATION resource) and the size of its colour
/// table, the music that loads, and the game strings (the flagship's own
/// names). Read once from the package, so a talk starts without touching
/// it.
/// </summary>
public sealed class TalkLibrary
{
    private static readonly Dictionary<string, Func<ConversationScript, CommScript>> PortedScripts = new()
    {
        ["comandr"] = p => new CommanderScript(p),
        ["ilwrath"] = p => new IlwrathScript(p),
        ["starbas"] = p => new StarbaseScript(p),
    };

    private readonly Dictionary<string, (Conversation Lines, int ColorMaps)> _content;
    private readonly HashSet<string> _music;

    private TalkLibrary(ConversationScripts scripts, Dictionary<string, (Conversation, int)> content,
        HashSet<string> music, GameStrings strings)
    {
        Scripts = scripts;
        _content = content;
        _music = music;
        Strings = strings;
    }

    public ConversationScripts Scripts { get; }

    public GameStrings Strings { get; }

    /// <summary>The scripts ported so far, by folder name.</summary>
    public static IReadOnlyCollection<string> Ported => PortedScripts.Keys;

    /// <summary>Reads the ported scripts' content; anything missing is a <see cref="ContentPackageException"/>.</summary>
    public static TalkLibrary Load(ContentPackage package, ConversationScripts scripts)
    {
        var content = new Dictionary<string, (Conversation, int)>();
        foreach (var name in PortedScripts.Keys)
        {
            var d = scripts[name].Description;
            content[name] = (Conversation.Load(package, d.Phrases), BinaryTable.Load(package, d.ColorMap).Count);
        }
        var music = package.Resources.Values
            .Where(r => r.Type == "MUSICRES" && package.HasFile(r.Value)).Select(r => r.Id).ToHashSet();
        return new TalkLibrary(scripts, content, music, GameStrings.Load(package));
    }

    /// <summary>A new talk with a ported script, not yet started.</summary>
    public Talk Create(string script, StoryState story, Dictionary<string, int> ownFields, ITalkShip ship,
        GameClock clock, uint seed)
    {
        if (!PortedScripts.TryGetValue(script, out var make))
            throw new NotSupportedException($"the {script} script isn't ported yet");
        var s = Scripts[script];
        var (lines, colorMaps) = _content[script];
        return new Talk(make(s), s.Description, lines, colorMaps, story, ownFields, ship, clock, seed, _music.Contains);
    }
}
