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
        // Stage 3b-1 item 6.
        ["urquan"] = p => new UrquanScript(p),
        ["blackur"] = p => new BlackUrquanScript(p),
        // Stage 3b-1 item 7 (the Ilwrath's whole since then).
        ["spathi"] = p => new SpathiScript(p),
        // Stage 3b-1 item 8.
        ["slyland"] = p => new SlylandroScript(p),
        // Stage 3b-2 item 2.
        ["spahome"] = p => new SpathiHomeScript(p),
        ["slyhome"] = p => new SlylandroHomeScript(p),
        // Stage 3b-2 item 4.
        ["melnorm"] = p => new MelnormeScript(p),
        // Stage 3b-2 item 5.
        ["shofixt"] = p => new ShofixtiScript(p),
        ["zoqfot"] = p => new ZoqFotPikScript(p),
        // Stage 3b-2 item 6.
        ["vux"] = p => new VuxScript(p),
        ["yehat"] = p => new YehatScript(p),
        ["rebel"] = p => new YehatRebelScript(p),
        // Stage 3b-2 item 7.
        ["orz"] = p => new OrzScript(p),
        ["pkunk"] = p => new PkunkScript(p),
        // Stage 3b-2 item 8.
        ["druuge"] = p => new DruugeScript(p),
        ["mycon"] = p => new MyconScript(p),
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

    /// <summary>
    /// init_race (commglue.c): the script a conversation runs, where it
    /// chooses by the story or where the talk is: the commander's, before
    /// the starbase is available (comandr) or after (starbas); the
    /// Spathi's, away from Spathiwa (spathi) or at it
    /// (<paramref name="globalFlags"/>, GLOBAL_FLAGS_AND_DATA, with bit 7:
    /// spahome); every other conversation its one script.
    /// </summary>
    public static string ScriptFor(ConversationScripts scripts, string conversation, StoryState story, int globalFlags)
    {
        var list = scripts.For(conversation);
        if (list.Count == 1)
            return list[0].Name;
        return conversation switch
        {
            "COMMANDER_CONVERSATION" => story.Get("STARBASE_AVAILABLE") == 0 ? "comandr" : "starbas",
            "SPATHI_CONVERSATION" => (globalFlags & (1 << 7)) == 0 ? "spathi" : "spahome",
            _ => throw new InvalidDataException($"init_race has no choice of scripts for {conversation}"),
        };
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
