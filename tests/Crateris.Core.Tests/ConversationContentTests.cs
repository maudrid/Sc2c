// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The conversations' content (docs/plans/stage-3.md, item 3): each race's
/// lines as UQM's own loader reads them, its description, its frames,
/// colour maps and font.
/// </summary>
[Trait("Topic", "content")]
public class ConversationContentTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);

    private static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();

    private static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public void EveryInputReadsAsUqmsGetConversationDataReadsIt()
    {
        foreach (var c in Golden.Load("conversation-cases").GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            var got = Conversation.Parse(Convert.FromHexString(c.GetProperty("input").GetString()!));
            var want = c.GetProperty("entries").EnumerateArray().ToList();
            Assert.True(want.Count == got.Count, $"{name}: {got.Count} entries, UQM has {want.Count}");
            for (int i = 0; i < want.Count; ++i)
            {
                var w = want[i].EnumerateArray().ToList();
                var e = got[i];
                Assert.True(Hex(Encoding.UTF8.GetBytes(e.Name)) == w[0].GetString(), $"{name} {i}: name {e.Name}");
                Assert.True(w[1].GetInt32() == e.Raw.Length && w[2].GetString() == Hex(e.Raw), $"{name} {i}: text");
                string? clip = w[3].ValueKind == JsonValueKind.Null ? null : Encoding.UTF8.GetString(Convert.FromHexString(w[3].GetString()!));
                Assert.True(clip == e.Clip, $"{name} {i}: clip {e.Clip}, UQM's {clip}");
            }
        }
    }

    [Fact]
    public void UqmsNameIndexKeepsTheFirstOfANameAndEntriesStopAtMaxStrings()
    {
        var c = Conversation.Parse("#(A)\nfirst\n#(A)\nsecond\n#(B)\nb\n"u8);
        Assert.Equal(0, c.IndexOf("A"));
        Assert.Equal(2, c.IndexOf("B"));
        Assert.Equal(-1, c.IndexOf("C"));
        Assert.Equal("first", c[0].Text);
    }

    [ContentFact]
    public void EveryConversationInThePackageIsUqms()
    {
        var want = Golden.Load("conversations").GetProperty("conversations");
        TestContent.With(package =>
        {
            int seen = 0;
            foreach (var kv in want.EnumerateObject())
            {
                var got = Conversation.Load(package, kv.Name);
                var entries = kv.Value.GetProperty("entries").EnumerateArray().ToList();
                Assert.True(entries.Count == got.Count, $"{kv.Name}: {got.Count} entries, UQM has {entries.Count}");
                for (int i = 0; i < entries.Count; ++i)
                {
                    var w = entries[i].EnumerateArray().ToList();
                    var e = got[i];
                    Assert.Equal(w[0].GetString(), e.Name);
                    Assert.True(w[1].GetInt32() == e.Raw.Length && w[2].GetString() == Sha(e.Raw), $"{kv.Name} {e.Name}: text");
                    Assert.Equal(w[3].ValueKind == JsonValueKind.Null ? null : w[3].GetString(), e.Clip);
                }
                ++seen;
            }
            Assert.Equal(27, seen);
        });
    }

    [Fact]
    public void EveryScriptAndConversationIdIsThere()
    {
        Assert.Equal(27, Scripts.All.Count);
        Assert.Equal(new[] { "comandr", "starbas" }, Scripts.For("COMMANDER_CONVERSATION").Select(s => s.Name));
        Assert.Equal(new[] { "ilwrath" }, Scripts.For("ILWRATH_CONVERSATION").Select(s => s.Name));
        Assert.Equal(new[] { "chmmr" }, Scripts.For("URQUAN_DRONE_CONVERSATION").Select(s => s.Name));   // init_race's default
        var commander = Scripts["comandr"];
        Assert.Equal(4, commander.PhraseNumber("ARE_YOU_SUPPLY_SHIP"));         // strings.h: NULL_PHRASE, GLAD_..., GIVE_FUEL, GIVE_FUEL_AGAIN, ARE_YOU_...
        Assert.Equal("init_commander_comm", commander.Init);
        var d = commander.Description;
        Assert.Equal("comm.commander.graphics", d.Frames);
        Assert.Equal(new CommAnimation(1, 3, CommAnimationFlags.YoYo, 56, 0, 0, 6720, 0), d.Ambient[0]);   // the blink
        Assert.Equal(CommAnimationFlags.Random | CommAnimationFlags.PauseTalking, d.Ambient[2].Flags);   // the colour cycle
        Assert.Equal(new Rgba(255, 255, 255, 255), d.TextFore);
        Assert.Throws<KeyNotFoundException>(() => commander.PhraseNumber("NO_SUCH_PHRASE"));
    }

    [ContentFact]
    public void EachScriptsPhrasesAreItsConversationsEntriesInOrder()
    {
        // The scripts call lines by number (NPCPhrase (n) is entry n - 1), so
        // strings.h and the content must agree name for name.
        TestContent.With(package =>
        {
            foreach (var script in Scripts.All)
            {
                var c = Conversation.Load(package, script.Description.Phrases);
                Assert.Equal(script.Phrases, c.Entries.Select(e => e.Name));
            }
        });
    }

    [ContentFact]
    public void EveryDescriptionsContentIsThereAndItsAnimationsFit()
    {
        TestContent.With(package =>
        {
            foreach (var script in Scripts.All)
            {
                var d = script.Description;
                int frames = Animation.LoadImages(package, d.Frames).Count;
                int maps = BinaryTable.Load(package, d.ColorMap).Count;
                Assert.NotNull(UqmFont.Load(package, d.Font));
                Assert.Equal("MUSICRES", package.Resource(d.Song).Type);
                if (d.AltSong != "")
                    Assert.Equal("MUSICRES", package.Resource(d.AltSong).Type);
                Assert.True(maps > 0, script.Name);
                foreach (var a in d.Ambient.Append(d.Transition).Append(d.Talk).Where(a => a.NumFrames > 0))
                {
                    // A colour animation plays colour maps, wrapping round the table as UQM's
                    // SetAbsStringTableIndex does (the Talking Pet's strobe runs 30 over its 2);
                    // any other plays frames, which must all be there.
                    bool colours = (a.Flags & CommAnimationFlags.PauseTalking) != 0 && d.Ambient.Contains(a);
                    if (!colours)
                        Assert.True(a.StartIndex + a.NumFrames <= frames,
                            $"{script.Name}: an animation from {a.StartIndex}, {a.NumFrames} long, past its {frames} frames");
                }
            }
        });
    }

    [ContentFact]
    public void EveryConversationsFramesAreUqms()
    {
        var want = Golden.Load("comm-graphics").GetProperty("sheets");
        TestContent.With(package =>
        {
            foreach (var kv in want.EnumerateObject())
            {
                var got = Animation.LoadImages(package, kv.Name);
                var frames = kv.Value.GetProperty("frames").EnumerateArray().ToList();
                Assert.True(frames.Count == got.Count, $"{kv.Name}: {got.Count} frames, UQM has {frames.Count}");
                for (int i = 0; i < frames.Count; ++i)
                {
                    var w = frames[i].EnumerateArray().ToList();
                    var (f, _) = got[i];
                    Assert.Equal(w[0].GetString(), f.Path);
                    Assert.Equal((w[1].GetInt32(), w[2].GetInt32(), w[3].GetInt32(), w[4].GetInt32()),
                        (f.TransparentIndex, f.ColorMapIndex, f.HotSpotX, f.HotSpotY));
                    Assert.True(w[5].GetString() == FontTests.Hash(FontTests.ImageJson(package.ReadFile(f.Path))), $"{kv.Name} {i}: {f.Path}");
                }
            }
            // Every description's frames are among them.
            foreach (var script in Scripts.All)
                Assert.True(want.TryGetProperty(script.Description.Frames, out _), script.Description.Frames);
        });
    }
}
