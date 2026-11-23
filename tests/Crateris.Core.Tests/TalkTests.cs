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
/// starts (tests/golden/talks-NAME.json, one file per script).
/// </summary>
[Trait("Topic", "talk")]
public class TalkTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

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
        public int BioData { get; set; }
        public List<int> Escorts { get; init; } = new();
        public IReadOnlyList<int> EscortRaces => Escorts;
        public int Thrusters { get; init; } = 2;
        public int Jets { get; init; } = 2;
        public IReadOnlyList<string?> ModuleSlots { get; init; } = new string?[16];
        public int EscortsWorth { get; set; }
        public int EscortRoom { get; set; }
        public (int X, int Y) LogPlace { get; init; } = (Uqm.Units.UniverseToLogX(1752), Uqm.Units.UniverseToLogY(1450));
        public uint Credits { get; set; }
        public LanderUpgrades Upgrades { get; set; }
        public int RescueRefused { get; set; }
        public int RescueCount { get; set; }
        public IList<byte> OwnDriveSlots { get; init; } = Enumerable.Repeat(ShipModules.EmptyDrive, 11).ToArray();
        public IList<byte> OwnJetSlots { get; init; } = Enumerable.Repeat(ShipModules.EmptyJet, 8).ToArray();
        public IList<byte> OwnModuleSlots { get; init; } = Enumerable.Repeat(ShipModules.EmptyModule, 16).ToArray();

        // UQM's recorder stubs AddEscortShips (comm.c): no captains' names drawn; each joins the queue's tail.
        public int AddEscorts(int race, int count, Func<uint> random)
        {
            int added = Math.Min(count, EscortRoom);
            EscortRoom -= added;
            EscortsWorth += added * RaceCosts[race];
            for (int i = 0; i < added && Escorts.Count < 12; ++i)
                Escorts.Add(race);
            return added;
        }

        // The recorder's RemoveSomeEscortShips (comm.c): the first first, their room given back, their worth taken off.
        public int RemoveEscorts(int race, int count)
        {
            int removed = 0;
            for (int i = 0; i < Escorts.Count && removed < count;)
            {
                if (Escorts[i] == race)
                {
                    Escorts.RemoveAt(i);
                    ++removed;
                }
                else
                    ++i;
            }
            EscortRoom += removed;
            if (race >= 0 && race < RaceCosts.Length)
                EscortsWorth -= removed * RaceCosts[race];
            return removed;
        }

        // UQM's recorder stubs SetEscortCrewComplement (comm.c): recorded only.
        public void SetEscortCrewComplement(int race, int crew, int captain)
        {
        }

        private static readonly int[] RaceCosts = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "races.json")))
            .RootElement.GetProperty("races").EnumerateArray().Select(r => r.TryGetProperty("ship_cost", out var c) ? c.GetInt32() : 0).ToArray();   // the drone has none

        public static TestShip From(JsonElement s, JsonElement start)
        {
            var modules = s.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ToList();
            var escorts = start.TryGetProperty("escorts", out var e) ? e.Ints() : new[] { 0, 0 };
            var state = start.GetProperty("state");
            int Field(string name) => state.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            var drives = s.GetProperty("drives").Ints();
            var jets = s.GetProperty("jets").Ints();
            return new TestShip
            {
                Thrusters = s.GetProperty("drives").GetArrayLength(),
                Jets = s.GetProperty("jets").GetArrayLength(),
                ModuleSlots = modules.Select(m => m == "EMPTY" ? null : m).ToList(),
                EscortsWorth = escorts[0],
                EscortRoom = escorts[1],
                CaptainName = s.GetProperty("captain").GetString()!,
                ShipName = s.GetProperty("ship").GetString()!,
                Crew = s.GetProperty("crew").GetInt32(),
                CrewCapacity = 50 * modules.Count(m => m == "CREW_POD"),
                Fuel = s.GetProperty("fuel").GetUInt32(),
                FuelCapacity = Hyperspace.Fuel.TankCapacity(modules.Count(m => m == "FUEL_TANK"), modules.Count(m => m == "HIGHEFF_FUELSYS")),
                Landers = s.GetProperty("landers").GetInt32(),
                ResourceUnits = s.GetProperty("ru").GetUInt32(),
                Minerals = s.GetProperty("elements").Ints(),
                BioData = start.TryGetProperty("bio", out var bio) ? bio.GetInt32() : 0,
                Escorts = start.TryGetProperty("escort_fleet", out var fleet)
                    ? fleet.EnumerateArray().Select(r => Rules.RaceIndex(r.GetString()!)).ToList() : new(),
                LogPlace = start.TryGetProperty("log", out var log) ? (log[0].GetInt32(), log[1].GetInt32())
                    : (Uqm.Units.UniverseToLogX(1752), Uqm.Units.UniverseToLogY(1450)),
                // The Melnorme's: the credits, the lander's upgrades and the rescue's fields from the start's
                // (each player's own) fields, and the ship's own slots.
                Credits = (uint)(Field("MELNORME_CREDIT0") | Field("MELNORME_CREDIT1") << 8),
                Upgrades = (LanderUpgrades)Field("LANDER_SHIELDS")
                           | (Field("IMPROVED_LANDER_SPEED") != 0 ? LanderUpgrades.Speed : 0)
                           | (Field("IMPROVED_LANDER_CARGO") != 0 ? LanderUpgrades.Cargo : 0)
                           | (Field("IMPROVED_LANDER_SHOT") != 0 ? LanderUpgrades.Shot : 0),
                RescueRefused = Field("MELNORME_RESCUE_REFUSED"),
                RescueCount = Field("MELNORME_RESCUE_COUNT"),
                OwnDriveSlots = Enumerable.Range(0, 11).Select(i => drives.Contains(i) ? ShipModules.FusionThruster : ShipModules.EmptyDrive).ToArray(),
                OwnJetSlots = Enumerable.Range(0, 8).Select(i => jets.Contains(i) ? ShipModules.TurningJets : ShipModules.EmptyJet).ToArray(),
                OwnModuleSlots = modules.Select(m => m == "EMPTY" ? ShipModules.EmptyModule
                    : (byte)ShipModules.Names.ToList().IndexOf(m!)).ToArray(),
            };
        }
    }

    // Each ported script, its recording (tests/golden/talks-NAME.json) and how it's made.
    private static readonly Dictionary<string, (string Recording, Func<ConversationScript, CommScript> Make)> Ported = new()
    {
        ["comandr"] = ("talks-comandr", p => new CommanderScript(p)),
        ["ilwrath"] = ("talks-ilwrath", p => new IlwrathScript(p)),
        ["starbas"] = ("talks-starbas", p => new StarbaseScript(p)),
        ["urquan"] = ("talks-urquan", p => new UrquanScript(p)),
        ["blackur"] = ("talks-blackur", p => new BlackUrquanScript(p)),
        ["spathi"] = ("talks-spathi", p => new SpathiScript(p)),
        ["slyland"] = ("talks-slyland", p => new SlylandroScript(p)),
        ["spahome"] = ("talks-spahome", p => new SpathiHomeScript(p)),
        ["slyhome"] = ("talks-slyhome", p => new SlylandroHomeScript(p)),
        ["melnorm"] = ("talks-melnorm", p => new MelnormeScript(p)),
        ["shofixt"] = ("talks-shofixt", p => new ShofixtiScript(p)),
        ["zoqfot"] = ("talks-zoqfot", p => new ZoqFotPikScript(p)),
        ["vux"] = ("talks-vux", p => new VuxScript(p)),
        ["yehat"] = ("talks-yehat", p => new YehatScript(p)),
        ["rebel"] = ("talks-rebel", p => new YehatRebelScript(p)),
    };

    // A script's recorded starts.
    private static List<JsonElement> Recorded(string script)
    {
        var file = Golden.Load(Ported[script].Recording);
        Assert.Equal(script, file.GetProperty("script").GetString());
        return file.GetProperty("starts").EnumerateArray().ToList();
    }

    private static Talk Start(ContentPackage package, string script, JsonElement start)
    {
        var s = Scripts[script];
        var date = start.TryGetProperty("date", out var dt) ? dt.Ints() : new[] { 2155, 2, 17 };
        var clock = new GameClock(date[0], date[1], date[2]);
        var story = StoryState.NewGame(Rules, clock);
        var own = new Dictionary<string, int>();
        foreach (var f in start.GetProperty("state").EnumerateObject())
        {
            if (StoryState.NotShared.ContainsKey(f.Name))
                own[f.Name] = f.Value.GetInt32();
            else
                story.Set(f.Name, f.Value.GetInt32());
        }
        if (start.TryGetProperty("alliances", out var alliances))
            foreach (var a in alliances.EnumerateObject())
                story.SetAlliance(a.Name, a.Value.GetString() switch
                {
                    "good" => Allied.Good,
                    "bad" => Allied.Bad,
                    "dead" => Allied.Dead,
                    var x => throw new InvalidDataException($"alliance {x}"),
                });
        if (start.TryGetProperty("costs", out var costs))
            foreach (var m in ShipModules.Names.Take(Talk.PurchaseModules))
                story.SetModuleCost(m, costs.TryGetProperty(m, out var c) ? c.GetInt32() : 0);
        if (start.TryGetProperty("fleets", out var fleets))
            foreach (var f in fleets.EnumerateObject())
                story.SetFleet(f.Name, story.Fleet(f.Name) with { ActualStrength = f.Value[0].GetInt32(), KnownStrength = f.Value[1].GetInt32() });
        uint seed = start.TryGetProperty("seed", out var sd) ? sd.GetUInt32() : 12345;
        var talk = new Talk(Ported[script].Make(s), s.Description, Conversation.Load(package, s.Description.Phrases),
            BinaryTable.Load(package, s.Description.ColorMap).Count, story, own, TestShip.From(start.GetProperty("ship"), start), clock, seed,
            id => package.Resources.TryGetValue(id, out var r) && r.Type == "MUSICRES" && package.HasFile(r.Value));
        if (start.TryGetProperty("activity", out var activity))
            talk.Activity = activity.GetString() switch
            {
                "SUPER_MELEE" => TalkActivity.SuperMelee,
                "IN_LAST_BATTLE" => TalkActivity.InLastBattle,
                "IN_HYPERSPACE" => TalkActivity.InHyperspace,
                "IN_INTERPLANETARY" => TalkActivity.InInterplanetary,
                "WON_LAST_BATTLE" => TalkActivity.WonLastBattle,
                var a => throw new InvalidDataException($"activity {a}"),
            };
        if (start.TryGetProperty("battle_group", out var group))
            talk.BattleGroup = group.GetString();
        talk.Start();
        return talk;
    }

    private static string Hash(byte[] text) => Convert.ToHexString(SHA256.HashData(text)).ToLowerInvariant()[..16];

    /// <summary>An event as the recording program writes it.</summary>
    internal static JsonObject Json(TalkEvent e) => e switch
    {
        LineQueued { Clip: { } clip } l => WithCallback(new() { ["track"] = l.Phrase, ["clip"] = clip, ["text"] = Hash(l.TextBytes) }, l),
        LineQueued l => WithCallback(new() { ["append"] = l.Phrase, ["text"] = Hash(l.TextBytes) }, l),
        CallbackRun r => new() { ["run"] = r.Callback },
        BioChanged b => new() { ["bio"] = b.BioData },
        EscortsRemoved r => new() { ["removed"] = new JsonArray(r.Race, r.Count, r.Removed) },
        RaceAllied a => new() { ["allied"] = new JsonArray(a.Race, a.Flag ? 1 : 0, a.Result) },
        SphereTracked t => new() { ["sphere"] = new JsonArray(t.Race, t.Result) },
        MultiTrackQueued m => new() { ["multi"] = m.Clips, ["clips"] = new JsonArray(m.ClipNames.Select(c => (JsonNode)c).ToArray()), ["text"] = Hash(m.TextBytes) },
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
        TalkingAnimation t => new() { ["talking"] = t.Enabled ? 1 : 0 },
        IntroModeSet i => new() { ["intro"] = (int)i.Mode, ["time"] = i.Time },
        EscortsAdded a => new() { ["escorts"] = new JsonArray(a.Race, a.Count, a.Added) },
        EscortCrewSet c => new() { ["complement"] = new JsonArray(c.Race, c.Crew, c.Captain) },
        EventAdded a => new()
        {
            ["event"] = new JsonArray(a.Relative ? 1 : 0, a.Months, a.Days, a.Years, Rules.EventIds[a.Event]),
        },
        SlotsChanged s => new() { ["slots"] = new JsonArray(s.Drives.Concat(s.Jets).Concat(s.Modules).Select(b => (JsonNode)(int)b).ToArray()) },
        CostsChanged c => new() { ["costs"] = new JsonArray(c.Costs.Select(v => (JsonNode)v).ToArray()) },
        ReplyChosen r => new() { ["reply"] = r.Index },
        RepliesOffered o => new()
        {
            ["offer"] = new JsonArray(o.Replies.Select(r => (JsonNode)new JsonArray(r.Phrase, r.Function, Hash(r.TextBytes))).ToArray()),
        },
        TalkEnded t => new() { ["end"] = (int)t.Segue },
        _ => throw new ArgumentException($"no recording for {e}"),
    };

    // A line's callback after its text, as the recording program writes it.
    private static JsonObject WithCallback(JsonObject o, LineQueued l)
    {
        if (l.Callback is { } callback)
            o["callback"] = callback;
        return o;
    }

    public static IEnumerable<object[]> PortedScripts() => Ported.Keys.Select(k => new object[] { k });

    [ContentTheory]
    [MemberData(nameof(PortedScripts))]
    public void EveryRecordedPathIsUqms(string script)
    {
        var starts = Recorded(script).Sample();
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
                    for (int k = 0; k < replies.Length; ++k)
                    {
                        from = talk.Events.Count;
                        talk.Choose(replies[k]);
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

        public readonly List<string> Ran = new();

        // Two callbacks (NPCPhrase_cb's), as the Zoq-Fot-Pik's speakers: each
        // notes its run, and Zoq changes the screen (its ambient flags).
        public void Zoq()
        {
            Ran.Add(nameof(Zoq));
            T.Ambient[0] ^= CommAnimationFlags.WaitTalking;
        }

        public void Pik() => Ran.Add(nameof(Pik));
    }

    private static readonly CommDescription Plain = Scripts["comandr"].Description with { Ambient = Array.Empty<CommAnimation>() };

    private static (Talk Talk, ProbeScript Script) Probe(Action<ProbeScript> intro, TestShip? ship = null,
        Dictionary<string, int>? own = null, uint seed = 1, StoryState? story = null, bool paged = false)
    {
        var ids = new List<string> { "HELLO", "BYE", "ask", "name_1", "name_2", "name_3", "name_4", "name_end" };
        string text = "#(HELLO) h.ogg\nHello\n#(BYE) b.ogg\nBye\n#(ask)\nWhat?\n#(name_1)\nThe One\n#(name_2)\nThe Two\n#(name_3)\nThe Three\n#(name_4)\nThe Empire of \n#(name_end)\n!\n";
        if (paged)
        {
            // A line of three pages (SplitSubPages: each run to a line end), and one of two without a clip.
            ids.AddRange(new[] { "PAGED", "paged_append" });
            text += "#(PAGED) p.ogg\nOne\nTwo\r\n\nThree\n#(paged_append)\nFour\nFive\n";
        }
        var phrases = new ConversationScript("probe", "init_probe_comm", Plain with { Ambient = Scripts["comandr"].Description.Ambient.Take(1).ToArray() },
            ids.ToArray());
        var lines = Conversation.Parse(System.Text.Encoding.UTF8.GetBytes(text));
        var script = new ProbeScript(phrases, intro);
        var talk = new Talk(script, phrases.Description, lines, 2, story ?? StoryState.NewGame(Rules, new GameClock(2155, 2, 17)),
            own ?? new Dictionary<string, int>(), ship ?? new TestShip { Crew = 10, CrewCapacity = 50, Fuel = 100, FuelCapacity = 6000, Landers = 1 },
            new GameClock(2155, 2, 17), seed,
            _ => true);
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

    [Fact]
    public void ASplicedLineJoinsThePageBefore()
    {
        var (talk, _) = Probe(s =>
        {
            s.Talk.NPCPhrase("HELLO");
            s.Talk.NPCPhraseSplice("ask");
            s.Talk.NPCPhraseSplice("BYE");
        });
        talk.Start();
        Assert.Equal(new[] { "track 1 Hello", "append 3 What?" }, talk.Events.OfType<LineQueued>()
            .Select(l => $"{(l.Clip is null ? "append" : "track")} {l.Phrase} {l.Text}"));
        // With a voice clip, NPCPhrase_splice joins the clip (SpliceMultiTrack).
        Assert.Equal("Bye", System.Text.Encoding.UTF8.GetString(Assert.Single(talk.Events.OfType<MultiTrackQueued>()).TextBytes));
    }

    [Fact]
    public void PhraseNumbersWrapAsUqmsSixteenBitIndexDoes()
    {
        var (talk, _) = Probe(s =>
        {
            s.Talk.NPCPhrase(9);      // entry 8 % 8: the first
            s.Talk.NPCPhrase(16);     // the eighth
        });
        talk.Start();
        Assert.Equal(new[] { (1, "Hello"), (8, "!") }, talk.Events.OfType<LineQueued>().Select(l => (l.Phrase, l.Text)));
    }

    [Fact]
    public void TheAlternateSongPlaysOnlyIfItLoads()
    {
        foreach (bool loads in new[] { true, false })
        {
            var phrases = Scripts["comandr"];
            var talk = new Talk(new CommanderScript(phrases), phrases.Description,
                Conversation.Parse("#(ARE_YOU_SUPPLY_SHIP) a.ogg\nHi\n"u8), 2,
                StoryState.NewGame(Rules, new GameClock(2155, 2, 17)), new(), new TestShip { CrewCapacity = 50, FuelCapacity = 1000 },
                new GameClock(2155, 2, 17), 1,
                id => loads);
            try
            {
                talk.Start();
            }
            catch (KeyNotFoundException)
            {
                // The one-line conversation hasn't the replies' phrases; the song is chosen before them.
            }
            Assert.Equal(loads ? "comm.commander.lowpower.music" : phrases.Description.Song,
                talk.Events.OfType<SongChosen>().Single().Resource);
        }
    }

    // ------------------------------------------------------------ what talks can do (stage 3b-2 item 1)

    private static int RaceOf(string race) => Rules.RaceIndex(race);

    [Fact]
    public void SetRaceAlliedMakesAndEndsAnAllianceButADeadRaceStaysDead()
    {
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        story.SetAlliance("VUX_SHIP", Allied.Dead);
        int orz = RaceOf("ORZ_SHIP"), vux = RaceOf("VUX_SHIP");
        var results = new List<int>();
        var read = new List<Allied>();
        var (talk, _) = Probe(s =>
        {
            results.Add(s.Talk.SetRaceAllied(orz, true));
            read.Add(s.Talk.CheckAlliance(orz));   // the talk reads its own change
            results.Add(s.Talk.SetRaceAllied(orz, true));   // already allied: unchanged
            results.Add(s.Talk.SetRaceAllied(orz, false));
            read.Add(s.Talk.CheckAlliance(orz));
            results.Add(s.Talk.SetRaceAllied(vux, true));   // "strange request, silently ignore it"
            read.Add(s.Talk.CheckAlliance(vux));
            results.Add(s.Talk.SetRaceAllied(Rules.Races.Count, true));   // no such race: 0
            results.Add(s.Talk.SetRaceAllied(-1, false));
        }, story: story);
        talk.Start();
        Assert.Equal(new[] { 1, 1, 1, 1, 0, 0 }, results);
        Assert.Equal(new[] { Allied.Good, Allied.Bad, Allied.Dead }, read);
        Assert.Equal(new[]
        {
            new RaceAllied(orz, true, 1, true), new RaceAllied(orz, true, 1, false), new RaceAllied(orz, false, 1, true),
            new RaceAllied(vux, true, 1, false), new RaceAllied(Rules.Races.Count, true, 0, false), new RaceAllied(-1, false, 0, false),
        }, talk.Events.OfType<RaceAllied>());
        Assert.Equal("{\"allied\":[3,1,1]}", Json(talk.Events.OfType<RaceAllied>().First()).ToJsonString());
        Assert.Equal(Allied.Bad, story.Alliance("ORZ_SHIP"));
    }

    [Fact]
    public void StartSphereTrackingFromAScriptTracksTheSphereAndSaysWhetherTheRaceLives()
    {
        var story = StoryState.NewGame(Rules, new GameClock(2155, 2, 17));
        story.SetFleet("SHOFIXTI_SHIP", story.Fleet("SHOFIXTI_SHIP") with { ActualStrength = 0 });
        story.SetAlliance("SHOFIXTI_SHIP", Allied.Dead);
        int utwig = RaceOf("UTWIG_SHIP"), umgah = RaceOf("UMGAH_SHIP"), shofixti = RaceOf("SHOFIXTI_SHIP");
        var results = new List<int>();
        var (talk, _) = Probe(s =>
        {
            results.Add(s.Talk.StartSphereTracking(utwig));
            results.Add(s.Talk.StartSphereTracking(umgah));
            results.Add(s.Talk.StartSphereTracking(shofixti));   // no sphere, dead: extinct
            results.Add(s.Talk.StartSphereTracking(0));          // the Arilou: their number, 0, though tracked
            results.Add(s.Talk.StartSphereTracking(Rules.Races.Count));
        }, story: story);
        talk.Start();
        Assert.Equal(new[] { utwig, umgah, 0, 0, 0 }, results);
        Assert.Equal(1, story.Fleet("UTWIG_SHIP").KnownStrength);
        Assert.Equal((story.Fleet("UTWIG_SHIP").LocX, story.Fleet("UTWIG_SHIP").LocY),
            (story.Fleet("UTWIG_SHIP").KnownLocX, story.Fleet("UTWIG_SHIP").KnownLocY));
        Assert.Equal(1, story.Fleet("ARILOU_SHIP").KnownStrength);
        Assert.Equal(new[] { new SphereTracked(utwig, utwig), new SphereTracked(umgah, umgah), new SphereTracked(shofixti, 0),
            new SphereTracked(0, 0), new SphereTracked(Rules.Races.Count, 0) }, talk.Events.OfType<SphereTracked>());
        Assert.Equal($"{{\"sphere\":[{utwig},{utwig}]}}", Json(talk.Events.OfType<SphereTracked>().First()).ToJsonString());
    }

    [Fact]
    public void TheEscortFleetIsReadAndItsShipsOfARaceTakenAway()
    {
        int orz = RaceOf("ORZ_SHIP"), spathi = RaceOf("SPATHI_SHIP"), shofixti = RaceOf("SHOFIXTI_SHIP");
        var ship = new TestShip { CrewCapacity = 50, FuelCapacity = 1000, Escorts = new() { orz, spathi, orz, orz },
            EscortsWorth = 3 * Rules.RaceShips[orz].Cost + Rules.RaceShips[spathi].Cost, EscortRoom = 8 };
        var seen = new List<(int, bool, bool, bool)>();
        var left = new List<int>();
        var (talk, _) = Probe(s =>
        {
            var t = s.Talk;
            seen.Add((t.CountEscortShips(orz), t.HaveEscortShip(spathi), t.HaveEscortShip(shofixti), t.EscortFleetEmpty));
            left.Add(t.RemoveSomeEscortShips(orz, 1));   // the first Orz; none left over: no redraw
            left.Add(t.RemoveSomeEscortShips(orz, 0));   // nothing asked: nothing done, nothing recorded
            t.RemoveEscortShips(orz);
            seen.Add((t.CountEscortShips(orz), t.HaveEscortShip(spathi), false, t.EscortFleetEmpty));
            t.RemoveEscortShips(spathi);
            t.RemoveEscortShips(shofixti);   // none to take: the redraw all the same
            seen.Add((t.CountEscortShips(Rules.Races.Count), t.HaveEscortShip(spathi), false, t.EscortFleetEmpty));
        }, ship);
        talk.Start();
        Assert.Equal(new[] { (3, true, false, false), (0, true, false, false), (0, false, false, true) }, seen);
        Assert.Equal(new[] { 0, 0 }, left);
        Assert.Equal((0, 0, 12), (ship.Escorts.Count, ship.EscortsWorth, ship.EscortRoom));
        Assert.Equal(new[]
        {
            "{\"removed\":[3,1,1]}", "{\"removed\":[3,65535,2]}", "{\"gauges\":[32767,32767,32767]}",
            "{\"removed\":[6,65535,1]}", "{\"gauges\":[32767,32767,32767]}",
            $"{{\"removed\":[{shofixti},65535,0]}}", "{\"gauges\":[32767,32767,32767]}",
        }, talk.Events.Where(e => e is EscortsRemoved or GaugesChanged).Select(e => Json(e).ToJsonString()));
    }

    [Fact]
    public void ShipsGivenJoinTheEscortQueuesTail()
    {
        int orz = RaceOf("ORZ_SHIP"), spathi = RaceOf("SPATHI_SHIP");
        var ship = new TestShip { CrewCapacity = 50, FuelCapacity = 1000, Escorts = new() { spathi }, EscortRoom = 2 };
        var (talk, _) = Probe(s => s.Talk.AddEscortShips(orz, 3), ship);
        talk.Start();
        Assert.Equal(new[] { spathi, orz, orz }, ship.Escorts);
    }

    [Fact]
    public void TheBioDataAboardIsTheScriptsToChangeAndItsChangesAreRecorded()
    {
        var ship = new TestShip { CrewCapacity = 50, FuelCapacity = 1000, BioData = 7 };
        var (talk, _) = Probe(s =>
        {
            s.Talk.Ship.BioData += 1000 / 5;   // umgahc.c's reward: 1000 / BIO_CREDIT_VALUE
            s.Talk.NPCPhrase("HELLO");
            s.Talk.Ship.BioData = 0;           // melnorm.c's
            s.Talk.Ship.BioData = 0;
            s.Talk.NPCPhrase("BYE");
        }, ship);
        talk.Start();
        Assert.Equal(new[] { new BioChanged(207), new BioChanged(0) }, talk.Events.OfType<BioChanged>());
        Assert.Equal("{\"bio\":207}", Json(talk.Events.OfType<BioChanged>().First()).ToJsonString());
        // Each at the next engine call after it, as check_changes sees it.
        var kinds = talk.Events.Where(e => e is BioChanged or LineQueued).Select(e => e.GetType().Name).ToList();
        Assert.Equal(new[] { "BioChanged", "LineQueued", "BioChanged", "LineQueued" }, kinds);
    }

    // The events in words, for the callbacks' tests: lines (with their callbacks), waits, runs, screens.
    private static List<string> Played(Talk talk) => talk.Events.Select(e => e switch
    {
        LineQueued l => $"line {l.Phrase}{(l.Callback is { } c ? " " + c : "")}",
        TalkWait w => $"wait {w.Track}",
        CallbackRun r => $"run {r.Callback}",
        ScreenChanged x => $"screen {(int)x.Ambient[0]}",
        ReplyChosen r => $"reply {r.Index}",
        RepliesOffered => "offer",
        TalkEnded => "end",
        _ => null,
    }).Where(e => e is not null).Select(e => e!).ToList();

    [Fact]
    public void ALinesCallbackRunsAsPlaybackReachesItsTrackNotAsItsQueued()
    {
        var (talk, script) = Probe(s =>
        {
            s.Talk.NPCPhrase("HELLO", s.Zoq);
            s.Talk.NPCPhrase("BYE", s.Pik);
            s.Talk.NPCPhrase("HELLO", s.Zoq);
            s.Talk.AlienTalkSegue(1);   // the first track plays; the rest wait
            s.Talk.NPCPhrase("BYE", s.Pik);
            s.Talk.AlienTalkSegue(3);
        });
        talk.Start();
        int ambient = (int)talk.Description.Ambient[0].Flags;
        int other = ambient ^ (int)CommAnimationFlags.WaitTalking;
        Assert.Equal(new[]
        {
            $"screen {ambient}", "line 1 Zoq", "line 2 Pik", "line 1 Zoq", "wait 1", "run Zoq",
            $"screen {other}", "line 2 Pik", "wait 3", "run Pik", "run Zoq",
            // The play-out as the intro returns: the fourth track.
            $"screen {ambient}", "run Pik", "end",
        }, Played(talk));
        Assert.Equal(new[] { "Zoq", "Pik", "Zoq", "Pik" }, script.Ran);
        Assert.Equal("{\"track\":1,\"clip\":\"h.ogg\",\"text\":\"" + Hash("Hello"u8.ToArray()) + "\",\"callback\":\"Zoq\"}",
            Json(talk.Events.OfType<LineQueued>().First()).ToJsonString());
        Assert.Equal("{\"run\":\"Zoq\"}", Json(talk.Events.OfType<CallbackRun>().First()).ToJsonString());
    }

    [Fact]
    public void ASegueThatPlaysEveryTrackEndsTheTalkingTillTheNextReply()
    {
        // TalkingFinished: the segues after one that played every track queued
        // play nothing, and lines queued meanwhile never play; a reply chosen
        // drops them (StopTrack) and the talking starts afresh.
        var (talk, script) = Probe(s =>
        {
            s.Talk.NPCPhrase("HELLO", s.Zoq);
            s.Talk.AlienTalkSegue(1);   // every track queued: finished
            s.Talk.NPCPhrase("BYE", s.Pik);
            s.Talk.AlienTalkSegue(Talk.WaitAll);
            s.Talk.Response("ask", r =>
            {
                s.Talk.NPCPhrase("BYE", s.Pik);
            });
        });
        talk.Start();
        Assert.Equal(new[] { "Zoq" }, script.Ran);
        talk.Choose(0);
        Assert.Equal(new[] { "Zoq", "Pik" }, script.Ran);
        Assert.Equal(new[] { "line 2 Pik", "run Pik", "end" }, Played(talk).SkipWhile(e => e != "reply 0").Skip(1));
    }

    [Fact]
    public void ASegueWithNothingQueuedEndsTheTalkingToo()
    {
        var (talk, script) = Probe(s =>
        {
            s.Talk.AlienTalkSegue(Talk.WaitAll);
            s.Talk.NPCPhrase("HELLO", s.Zoq);
        });
        talk.Start();
        Assert.Empty(script.Ran);
    }

    [Fact]
    public void ACallbackRunsForEachPageOfItsLine()
    {
        // trackplayer.c tags every page's chunk with the callback (its "TODO:
        // We may have to tag only one page"); a line without a clip appended
        // to the last track tags its pages after the first.
        var (talk, script) = Probe(s =>
        {
            s.Talk.NPCPhrase("PAGED", s.Zoq);          // three pages
            s.Talk.NPCPhrase("paged_append", s.Pik);   // two: the second its own chunk
        }, paged: true);
        talk.Start();
        Assert.Equal(new[] { "Zoq", "Zoq", "Zoq", "Pik" }, script.Ran);
    }

    [Fact]
    public void ALineJoinedAfterASplicedClipHasItsFirstPageUntagged()
    {
        // SpliceMultiTrack (NPCPhrase_splice with a clip) sets no_page_break: the
        // next line with a clip joins the last track, its first chunk untagged.
        var (talk, script) = Probe(s =>
        {
            s.Talk.NPCPhrase("ask", s.Pik);            // no clip, nothing queued yet: dropped
            s.Talk.NPCPhrase("HELLO");
            s.Talk.NPCPhraseSplice("BYE");
            s.Talk.NPCPhrase("HELLO", s.Zoq);          // joined: its one page untagged
            s.Talk.NPCPhrase("PAGED", s.Pik);          // a track of its own again, three pages
            s.Talk.AlienTalkSegue(2);
        }, paged: true);
        talk.Start();
        Assert.Equal(new[] { "Pik", "Pik", "Pik" }, script.Ran);
        Assert.Equal(new[] { "wait 2", "run Pik", "run Pik", "run Pik", "end" },
            Played(talk).SkipWhile(e => !e.StartsWith("wait")).Where(e => !e.StartsWith("screen")));
    }
}
