// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The conversation engine and the ported scripts (docs/plans/stage-3.md,
/// item 4), against every path UQM's own scripts take from the recorded
/// starts (tests/golden/talks.json).
/// </summary>
[Trait("Topic", "talk")]
public class TalkTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);
    private static readonly JsonElement Recorded = Golden.Load("talks").GetProperty("talks");

    /// <summary>A flagship as the recordings give it, with the capacities its modules give (sis.c).</summary>
    internal sealed class TestShip : ITalkShip
    {
        public string CaptainName { get; init; } = "Zelnick";
        public string ShipName { get; init; } = "Vindicator";
        public int Crew { get; set; }
        public int CrewCapacity { get; init; }
        public uint Fuel { get; set; }
        public uint FuelCapacity { get; init; }
        public int Landers { get; set; }
        public uint ResourceUnits { get; set; }
        public IList<int> Minerals { get; init; } = new int[8];

        public static TestShip From(JsonElement s)
        {
            var modules = s.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ToList();
            return new TestShip
            {
                CaptainName = s.GetProperty("captain").GetString()!,
                ShipName = s.GetProperty("ship").GetString()!,
                Crew = s.GetProperty("crew").GetInt32(),
                CrewCapacity = 50 * modules.Count(m => m == "CREW_POD"),
                Fuel = s.GetProperty("fuel").GetUInt32(),
                FuelCapacity = Hyperspace.Fuel.TankCapacity(modules.Count(m => m == "FUEL_TANK"), modules.Count(m => m == "HIGHEFF_FUELSYS")),
                Landers = s.GetProperty("landers").GetInt32(),
                ResourceUnits = s.GetProperty("ru").GetUInt32(),
                Minerals = s.GetProperty("elements").Ints(),
            };
        }
    }

    private static readonly Dictionary<string, Func<ConversationScript, CommScript>> Ported = new()
    {
        ["comandr"] = p => new CommanderScript(p),
    };

    private static Talk Start(ContentPackage package, string script, JsonElement start)
    {
        var s = Scripts[script];
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        var own = new Dictionary<string, int>();
        foreach (var f in start.GetProperty("state").EnumerateObject())
        {
            if (StoryState.NotShared.ContainsKey(f.Name))
                own[f.Name] = f.Value.GetInt32();
            else
                story.Set(f.Name, f.Value.GetInt32());
        }
        uint seed = start.TryGetProperty("seed", out var sd) ? sd.GetUInt32() : 12345;
        var talk = new Talk(Ported[script](s), s.Description, Conversation.Load(package, s.Description.Phrases),
            BinaryTable.Load(package, s.Description.ColorMap).Count, story, own, TestShip.From(start.GetProperty("ship")), seed);
        talk.Start();
        return talk;
    }

    private static string Hash(byte[] text) => Convert.ToHexString(SHA256.HashData(text)).ToLowerInvariant()[..16];

    /// <summary>An event as the recording program writes it.</summary>
    internal static JsonObject Json(TalkEvent e) => e switch
    {
        LineQueued { Clip: { } clip } l => new() { ["track"] = l.Phrase, ["clip"] = clip, ["text"] = Hash(l.TextBytes) },
        LineQueued l => new() { ["append"] = l.Phrase, ["text"] = Hash(l.TextBytes) },
        MultiTrackQueued m => new() { ["multi"] = m.Clips, ["text"] = Hash(m.TextBytes) },
        TalkWait w => new() { ["wait"] = w.Track },
        ColorTransform x => new() { ["xform"] = x.Map, ["time"] = x.Time },
        ScreenChanged s => new() { ["screen"] = s.ColorMap, ["ambient"] = new JsonArray(s.Ambient.Select(a => (JsonNode)(int)a).ToArray()) },
        SongChosen s => new() { ["song"] = s.Resource },
        StateSet s => new() { ["set"] = s.Field, ["value"] = s.Value },
        ShipChanged s => new()
        {
            ["ship"] = new JsonArray(new JsonNode[] { s.Crew, s.Fuel, s.Landers, s.ResourceUnits }
                .Concat(s.Minerals.Select(m => (JsonNode)m)).ToArray()),
        },
        GaugesChanged g => new() { ["gauges"] = new JsonArray(g.Crew, g.Fuel, g.ResourceUnits) },
        RandomDrawn r => new() { ["random"] = r.Value },
        ReplyChosen r => new() { ["reply"] = r.Index },
        RepliesOffered o => new()
        {
            ["offer"] = new JsonArray(o.Replies.Select(r => (JsonNode)new JsonArray(r.Phrase, r.Function, Hash(r.TextBytes))).ToArray()),
        },
        TalkEnded t => new() { ["end"] = (int)t.Segue },
        _ => throw new ArgumentException($"no recording for {e}"),
    };

    public static IEnumerable<object[]> PortedScripts() => Ported.Keys.Select(k => new object[] { k });

    [ContentTheory]
    [MemberData(nameof(PortedScripts))]
    public void EveryRecordedPathIsUqms(string script)
    {
        var starts = Recorded.GetProperty(script).EnumerateArray().ToList().Sample();
        Assert.NotEmpty(starts);
        TestContent.With(package =>
        {
            foreach (var start in starts)
            {
                string name = start.GetProperty("name").GetString()!;
                foreach (var step in start.GetProperty("steps").EnumerateArray())
                {
                    var replies = step.GetProperty("replies").Ints();
                    var talk = Start(package, script, start);
                    int from = 0;
                    foreach (int r in replies)
                    {
                        from = talk.Events.Count;
                        talk.Choose(r);
                    }
                    var want = step.GetProperty("events").EnumerateArray().Select(e =>
                    {
                        var o = JsonNode.Parse(e.GetRawText())!.AsObject();
                        o.Remove("seen");
                        return o.ToJsonString();
                    }).ToList();
                    var got = talk.Events.Skip(from).Select(e => Json(e).ToJsonString()).ToList();
                    Assert.True(want.SequenceEqual(got),
                        $"{script} {name} after [{string.Join(", ", replies)}]:\n  UQM: {string.Join("\n       ", want)}\n  port: {string.Join("\n       ", got)}");
                }
            }
            return 0;
        });
    }

    // ------------------------------------------------------------ the engine itself

    /// <summary>A script for the engine's own tests: its intro and replies are what the test gives.</summary>
    private sealed class ProbeScript : CommScript
    {
        public ProbeScript(ConversationScript phrases, Action<ProbeScript> intro) : base(phrases) => IntroAction = intro;

        public Action<ProbeScript> IntroAction { get; }

        public Talk Talk => T;

        public int PostCalls, UninitCalls;

        protected internal override void Init() => T.SetSegue(Segue.Peace);

        protected internal override void Intro() => IntroAction(this);

        protected internal override void PostEncounter() => PostCalls++;

        protected internal override void Uninit() => UninitCalls++;

        public void Said(int r) => T.NPCPhrase("BYE");
    }

    private static readonly CommDescription Plain = Scripts["comandr"].Description with { Ambient = Array.Empty<CommAnimation>() };

    private static (Talk Talk, ProbeScript Script) Probe(Action<ProbeScript> intro, TestShip? ship = null,
        Dictionary<string, int>? own = null, uint seed = 1)
    {
        var phrases = new ConversationScript("probe", "init_probe_comm", Plain,
            new[] { "HELLO", "BYE", "ask", "name_1", "name_2", "name_3", "name_4", "name_end" });
        var lines = Conversation.Parse(
            "#(HELLO) h.ogg\nHello\n#(BYE) b.ogg\nBye\n#(ask)\nWhat?\n#(name_1)\nThe One\n#(name_2)\nThe Two\n#(name_3)\nThe Three\n#(name_4)\nThe Empire of \n#(name_end)\n!\n"u8);
        var script = new ProbeScript(phrases, intro);
        var talk = new Talk(script, Plain, lines, 2, StoryState.NewGame(Rules, new GameClock(2155, 2, 17)),
            own ?? new Dictionary<string, int>(), ship ?? new TestShip { Crew = 10, CrewCapacity = 50, Fuel = 100, FuelCapacity = 6000, Landers = 1 }, seed);
        return (talk, script);
    }

    [Fact]
    public void ATalkWithoutRepliesEndsAtOnceAndRunsTheEndingFunctions()
    {
        var (talk, script) = Probe(s => s.Talk.NPCPhrase("HELLO"));
        talk.Start();
        Assert.True(talk.Ended);
        Assert.Equal((1, 1), (script.PostCalls, script.UninitCalls));
        Assert.Equal(new TalkEnded(Segue.Peace), talk.Events[^1]);
        var line = Assert.IsType<LineQueued>(talk.Events.Single(e => e is LineQueued));
        Assert.Equal((1, "h.ogg", "Hello"), (line.Phrase, line.Clip, line.Text));
        Assert.Throws<InvalidOperationException>(() => talk.Choose(0));
    }

    [Fact]
    public void AReplyRunsItsFunctionWithItsPhrase()
    {
        int said = -1;
        var (talk, _) = Probe(s => s.Talk.Response("ask", r => said = r));
        talk.Start();
        var offer = Assert.IsType<RepliesOffered>(talk.Events[^1]);
        Assert.Equal("What?", Assert.Single(offer.Replies).Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => talk.Choose(1));
        talk.Choose(0);
        Assert.Equal(3, said);
        Assert.True(talk.Ended);
    }

    [Fact]
    public void AtMostEightRepliesAreOffered()
    {
        var (talk, _) = Probe(s =>
        {
            for (int i = 0; i < 9; ++i)
                s.Talk.Response("ask", s.Said);
        });
        Assert.Throws<InvalidOperationException>(() => talk.Start());
    }

    [Fact]
    public void ARepliesTextIsReadWhenShown()
    {
        // comm.c keeps a pointer to the text: a phrase blanked after it's
        // offered shows blank, and shared_phrase_buf as it is at the end.
        var (talk, _) = Probe(s =>
        {
            s.Talk.Response("ask", s.Said);
            s.Talk.ConstructResponse("name_1", "X", "name_end");
            s.Talk.DoResponsePhrase("name_1", s.Said, Talk.SharedPhraseBuffer);
            s.Talk.ConstructResponse("name_2", "Y", "name_end");
            s.Talk.DoResponsePhrase("name_2", s.Said, Talk.SharedPhraseBuffer);
            s.Talk.DisablePhrase("ask");
        });
        talk.Start();
        var offer = Assert.IsType<RepliesOffered>(talk.Events[^1]);
        Assert.Equal(new[] { "", "The TwoY!", "The TwoY!" }, offer.Replies.Select(r => r.Text));
        Assert.Equal(new[] { 3, 4, 5 }, offer.Replies.Select(r => r.Phrase));
        Assert.Equal(nameof(ProbeScript.Said), offer.Replies[0].Function);
    }

    [Fact]
    public void NamesNumbersAndTheAllianceAreSplicedIn()
    {
        var ship = new TestShip { CaptainName = "Zoë", ShipName = "Ñandú", CrewCapacity = 50, FuelCapacity = 1000 };
        var (talk, _) = Probe(s =>
        {
            s.Talk.NPCPhrase("HELLO");
            s.Talk.NPCPhrase(Talk.GlobalPlayerName);
            s.Talk.NPCPhrase(Talk.GlobalShipName);
            s.Talk.NPCNumber(2155);
            s.Talk.NPCPhrase(0);
            s.Talk.Set("NEW_ALLIANCE_NAME", 1);
            s.Talk.NPCPhrase(Talk.GlobalAllianceName + s.Talk.Phrase("name_1"));
            s.Talk.Set("NEW_ALLIANCE_NAME", 3);
            s.Talk.NPCPhrase(Talk.GlobalAllianceName + s.Talk.Phrase("name_1"));
        }, ship);
        talk.Start();
        var lines = talk.Events.OfType<LineQueued>().Select(l => (l.Phrase, l.Clip, l.Text)).ToList();
        Assert.Equal(new (int, string?, string)[]
        {
            (1, "h.ogg", "Hello"), (0, null, "Zoë"), (0, null, "Ñandú"), (0, null, "2155"),
            (0, null, "The Two"), (0, null, "The Empire of Zoë"),
        }, lines);
    }

    [Fact]
    public void TheFieldsThatArentSharedAreTheTalksOwn()
    {
        var own = new Dictionary<string, int> { ["STARBASE_VISITED"] = 1 };
        var (talk, _) = Probe(s =>
        {
            Assert.Equal(1, s.Talk.Get("STARBASE_VISITED"));
            s.Talk.SetSegue(Segue.Hostile);
            s.Talk.Set("STARBASE_VISITED", 0);
            s.Talk.Set("RADIOACTIVES_PROVIDED", 1);
        }, own: own);
        talk.Start();
        Assert.Equal(new Dictionary<string, int> { ["STARBASE_VISITED"] = 0, ["BATTLE_SEGUE"] = 1 }, own);
        Assert.Equal(1, talk.Story.Get("RADIOACTIVES_PROVIDED"));
        Assert.Equal(Segue.Hostile, talk.Segue);
        Assert.Equal(new TalkEnded(Segue.Hostile), talk.Events[^1]);
        Assert.Throws<ArgumentException>(() => Probe(_ => { }, own: new() { ["RADIOACTIVES_PROVIDED"] = 1 }));
    }

    [Theory]
    [InlineData(Segue.Peace, Segue.Peace)]
    [InlineData(Segue.Hostile, Segue.Hostile)]
    [InlineData(Segue.Victory, Segue.Victory)]
    [InlineData(Segue.Defeat, Segue.Defeat)]
    public void TheSegueIsGetSeguesAnswer(Segue set, Segue got)
    {
        var (talk, _) = Probe(s => s.Talk.SetSegue(set));
        talk.Start();
        Assert.Equal(got, talk.Segue);
    }

    [Fact]
    public void TheGaugesKeepTheShipWithinWhatItHolds()
    {
        var ship = new TestShip { Crew = 48, CrewCapacity = 50, Fuel = 5900, FuelCapacity = 6000, ResourceUnits = 3 };
        var (talk, _) = Probe(s =>
        {
            s.Talk.DeltaSISGauges(4, 500, -5);
            s.Talk.DeltaSISGauges(-60, -7000, 9);
            s.Talk.DeltaSISGauges(Talk.UndefinedDelta, Talk.UndefinedDelta, Talk.UndefinedDelta);
        }, ship);
        talk.Start();
        Assert.Equal((0, 0u, 9u), (ship.Crew, ship.Fuel, ship.ResourceUnits));
        Assert.Equal(new[] { new ShipChanged(50, 6000, 0, 0, new int[8]), new ShipChanged(0, 0, 0, 9, new int[8]) }
            .Select(e => Json(e).ToJsonString()), talk.Events.OfType<ShipChanged>().Select(e => Json(e).ToJsonString()));
    }

    [Fact]
    public void TheScriptsRandomNumbersAreTfbRandomsFromTheTalksSeed()
    {
        var want = new Uqm.TfbRandom(77);
        var (talk, _) = Probe(s => s.Talk.Random(), seed: 77);
        talk.Start();
        Assert.Equal(want.Next(), Assert.Single(talk.Events.OfType<RandomDrawn>()).Value);
    }

    [Fact]
    public void ColourMapsWrapAtTheTablesSize()
    {
        var (talk, _) = Probe(s =>
        {
            s.Talk.SetColorMap(3);
            s.Talk.XFormColorMap(2, 420);
        });
        talk.Start();
        Assert.Equal(1, talk.ColorMap);
        Assert.Equal(new ColorTransform(0, 420), talk.Events.OfType<ColorTransform>().Single());
    }
}
