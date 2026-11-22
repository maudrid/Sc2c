// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Comm;
using Crateris.Core.Comm.Scripts;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The worlds whose orbit starts a talk, generalised (the stage 3b-2 plan's
/// item 1, part B; docs/differences.md row 27): every 3b-2 world listed by
/// its generator (<see cref="GameWorld.TalkWorlds"/>), each waiting for its
/// script; a race's second world (GLOBAL_FLAGS_AND_DATA bit 6: Procyon's
/// Ilwrath, genchmmr.c); a finite guard, written off and its "none left"
/// flag set when none is left, the ship then in orbit (genorz.c's,
/// genmyc.c's and genutw.c's survivors test); a talk with no ships there
/// (gensly.c's, gensyr.c's); and the Thraddash's body count
/// (UninitEncounter). The kinds whose races aren't ported yet are driven
/// by entries of a test table of their kind with the ported races' scripts.
/// </summary>
[Trait("Topic", "world")]
public class WorldTalkTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly Lazy<TalkLibrary> Library = new(() => TestContent.With(p => TalkLibrary.Load(p, Scripts)));

    private static GameWorld NewGame(params string[] guests)
    {
        var world = SystemGroupTests.NewWorld();
        foreach (var g in guests)
        {
            Assert.Equal(JoinResult.Joined, world.Join(g, g));
            world.UndockShown(g);
        }
        world.TalkLibrary = Library.Value;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        world.BattleKit = WorldBattleTests.Kit();
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.Set("SLYLANDRO_MULTIPLIER", 0);
        world.Clock.FramesIntoDay = -100000;   // no day's roll meanwhile
        return world;
    }

    private static void Frames(GameWorld world, int frames, ShipInput input = ShipInput.None)
    {
        for (int i = 0; i < frames; ++i)
        {
            foreach (var p in world.Players.Where(p => p.Present))
                world.SubmitInput(p.Id, world.Frame + 1, input);
            world.Step();
        }
    }

    private static int Star(GameWorld world, string special) => world.Systems!.Map.Stars.First(s => s.Special == special).Number;

    private static int Reply(Talk talk, string phrase)
    {
        int n = Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase);
        int i = talk.Replies.ToList().FindIndex(r => r.Phrase == n);
        return i >= 0 ? i : throw new Xunit.Sdk.XunitException($"{phrase} isn't offered");
    }

    private static bool Said(Talk talk, string phrase) =>
        talk.Events.OfType<LineQueued>().Any(l => l.Phrase == Scripts[talk.Script.Phrases.Name].PhraseNumber(phrase));

    // The meeting's battle won: each enemy ship destroyed once it's in and
    // fighting, the players' ships kept whole meanwhile.
    private static void WinTheBattle(GameWorld world, Meeting m)
    {
        var wb = m.Battle!;
        Starship? seen = null;
        int since = 0;
        for (int i = 0; i < 4000 && m.Step == MeetingStep.Battle; ++i)
        {
            foreach (var f in wb.Fighters)
                if (f.Starship.RaceDesc is { } rd)
                    rd.CrewLevel = rd.MaxCrew;
            var e = wb.Enemy;
            if (!ReferenceEquals(e, seen))
                (seen, since) = (e, 0);
            if (e.RaceDesc is { CrewLevel: > 0 } && e.Ship is not null && ++since > 30)
                WorldBattleTests.Kill(wb.Battle, e);
            Frames(world, 1);
        }
        Assert.Equal(BattleOutcome.Won, wb.Outcome);
    }

    // The host retreats from its meeting's battle.
    private static void Retreat(GameWorld world, Meeting m)
    {
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
            Frames(world, 1, ShipInput.TakeOff);
        Assert.Equal(MeetingStep.Done, m.Step);
    }

    // The script each listed world's talk runs (init_race by its conversation and GLOBAL_FLAGS_AND_DATA).
    private static readonly Dictionary<(string Special, string Race), string> ListedScripts = new()
    {
        [("ILWRATH_DEFINED", "ILWRATH_SHIP")] = "ilwrath",
        [("SPATHI_DEFINED", "SPATHI_SHIP")] = "spahome",
        [("VUX_DEFINED", "VUX_SHIP")] = "vux",
        [("MAIDENS_DEFINED", "VUX_SHIP")] = "vux",
        [("YEHAT_DEFINED", "YEHAT_SHIP")] = "yehat",
        [("ORZ_DEFINED", "ORZ_SHIP")] = "orz",
        [("TAALO_PROTECTOR_DEFINED", "ORZ_SHIP")] = "orz",
        [("MYCON_DEFINED", "MYCON_SHIP")] = "mycon",
        [("SUN_DEVICE_DEFINED", "MYCON_SHIP")] = "mycon",
        [("PKUNK_DEFINED", "PKUNK_SHIP")] = "pkunk",
        [("DRUUGE_DEFINED", "DRUUGE_SHIP")] = "druuge",
        [("UTWIG_DEFINED", "UTWIG_SHIP")] = "utwig",
        [("BOMB_DEFINED", "UTWIG_SHIP")] = "utwig",
        [("BOMB_DEFINED", "DRUUGE_SHIP")] = "druuge",
        [("SUPOX_DEFINED", "SUPOX_SHIP")] = "supox",
        [("THRADD_DEFINED", "THRADDASH_SHIP")] = "thradd",
        [("AQUA_HELIX_DEFINED", "THRADDASH_SHIP")] = "thradd",
        [("ZOQFOT_DEFINED", "ZOQFOTPIK_SHIP")] = "zoqfot",
        [("TALKING_PET_DEFINED", "UMGAH_SHIP")] = "umgah",
        [("SLYLANDRO_DEFINED", "SLYLANDRO_SHIP")] = "slyhome",
        [("SYREEN_DEFINED", "SYREEN_SHIP")] = "syreen",
        [("CHMMR_DEFINED", "ILWRATH_SHIP")] = "ilwrath",
    };

    [ContentFact]
    public void EveryListedWorldWaitsForItsScript()
    {
        var world = NewGame();
        var story = world.Story!;
        var locks = world.DialogueLocks!;
        story.Set("SUN_DEVICE_ON_SHIP", 1);   // Procyon's Ilwrath's test
        Assert.Equal(ListedScripts.Keys.OrderBy(k => k), GameWorld.TalkWorlds.Select(w => (w.Special, w.Race)).OrderBy(k => k));
        foreach (var w in GameWorld.TalkWorlds)
        {
            int star = Star(world, w.Special);
            Assert.True(world.Systems!.HasWorld(star, w.Planet, w.Moon), $"{w.Special} {w.Planet} {w.Moon}");
            // The talk's conversation is locked by the race whose ships are there.
            string conversation = w.Conversation ?? locks.ConversationOf(w.Race)!;
            Assert.Equal(w.Race, locks.RaceOf(conversation));
            string script = TalkLibrary.ScriptFor(Scripts, conversation, story, w.GlobalFlags);
            Assert.Equal(ListedScripts[(w.Special, w.Race)], script);
            Assert.False(TalkRules.Scripts.Contains(script) && TalkRules.IsStory(script, story), $"{script} is story");
            bool ported = TalkLibrary.Ported.Contains(script);
            Assert.Equal(ported ? script : null, world.WorldScript(w));
            // In a new game every listed world is guarded (the bomb's moon by the Utwig first); one whose
            // script isn't ported is orbited as before, and nothing of the story changes.
            var before = story.Bits.ToArray();
            var started = world.WorldTalkAt(star, w.Planet, w.Moon);
            if (w is { Special: "BOMB_DEFINED", Race: "DRUUGE_SHIP" })
                continue;
            Assert.Equal(ported ? w : null, started);
            if (!ported)
                Assert.Equal(before, story.Bits.ToArray());
        }
        // Only the Ilwrath's are ported: their homeworld and Procyon.
        Assert.Equal(new[] { "CHMMR_DEFINED", "ILWRATH_DEFINED" },
            GameWorld.TalkWorlds.Where(w => world.WorldScript(w) is not null).Select(w => w.Special).OrderBy(s => s));
        // The Druuge at the bomb's moon once the Utwig have their Ultron: still waiting for theirs.
        story.Set("UTWIG_HAVE_ULTRON", 1);
        Assert.Null(world.WorldTalkAt(Star(world, "BOMB_DEFINED"), 5, 1));
    }

    [ContentFact]
    public void ProcyonsIlwrathTalkAtTheChmmrsWorldWithBitSix()
    {
        // genchmmr.c:108: with the Sun Device aboard, the Ilwrath not deceived and the Chmmr not unleashed,
        // orbiting the Chmmr's world meets the Ilwrath's infinite fleet, their talk told bit 6.
        var world = NewGame("g");
        var story = world.Story!;
        int star = Star(world, "CHMMR_DEFINED");
        world.PutInOrbit("h", star, 1);
        Assert.Null(world.MeetingOf("h"));   // no Sun Device: an orbit
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);

        story.Set("SUN_DEVICE_ON_SHIP", 1);
        world.Host.Ship.Ip!.ReturnFromOrbit();
        world.Host.Ship.Ip.EscapeCounter = 0;
        // An Ilwrath group in the system, and the guest there.
        world.AddEncounterNear("h", story.Rules.RaceIndex("ILWRATH_SHIP"), 1);
        world.PutInSystem("g", star);
        world.TakeNotices();
        world.PutInOrbit("h", star, 1);
        var m = world.MeetingOf("h")!;
        // genchmmr.c:112 puts the groups aside with no NotifyOthers first: the
        // Ilwrath there don't stand down, and the guest is told who's meeting
        // whom, not that they hold off.
        Assert.Contains(world.SystemGroups!.Systems[star].Groups,
            g => g.Race == m.Race && (g.Task & IpGroup.IgnoreFlagship) == 0);
        var told = world.TakeNotices().Where(n => n.PlayerId == "g").Select(n => n.Text).ToList();
        Assert.Contains("Host is meeting the Ilwrath.", told);
        Assert.DoesNotContain(told, t => t.Contains("hold off"));
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        Assert.Equal(IpFlight.EscapeFrames, world.Host.Ship.Ip.EscapeCounter);
        Assert.Equal(((int, int?)?)(1, null), m.World);
        Assert.True(m.Infinite);
        Assert.Equal("ilwrath", Assert.Single(m.Ships).Ship);
        Assert.Equal("h", world.DialogueLocks!.Holder("ILWRATH_CONVERSATION"));
        var talk = m.Talk!;
        Assert.IsType<IlwrathScript>(talk.Script);
        Assert.Equal(1 << 6, talk.OwnFields["GLOBAL_FLAGS_AND_DATA"]);
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(Said(talk, "INIT_CHMMR_HELLO"));
        Assert.Equal(1, story.Get("ILWRATH_CHMMR_VISITS"));
        Assert.True(world.ChooseReply("h", Reply(talk, "bye"), m.Round));
        MeetingTests.HearClosing(world, "h");
        Assert.True(m.Battle!.EnemyFleet.Infinite);
        Retreat(world, m);
        Assert.Null(world.Host.Ship.Ip!.Orbit);   // genchmmr.c returns: never an orbit after it

        // Deceived, or the Chmmr unleashed (their own talk, 3c's): no Ilwrath there.
        story.Set("ILWRATH_DECEIVED", 1);
        Assert.Null(world.WorldTalkAt(star, 1, null));
        story.Set("ILWRATH_DECEIVED", 0);
        story.Set("CHMMR_UNLEASHED", 1);
        Assert.Null(world.WorldTalkAt(star, 1, null));
    }

    // A new game's story with these fields set.
    private static StoryState StoryWith(params (string Field, int Value)[] sets)
    {
        var story = StoryState.NewGame(OutfitTests.Rules, new GameClock(2155, 2, 17));
        foreach (var (f, v) in sets)
            story.Set(f, v);
        return story;
    }

    // An entry's starting test: its answer, and whether it asked StartSphereTracking (given its answer).
    private static (bool Guarded, bool Asked) Guard(TalkWorld w, StoryState s, bool lives = true)
    {
        bool asked = false;
        bool g = w.Guarded(s, () => { asked = true; return lives; });
        return (g, asked);
    }

    private static TalkWorld Listed(string special, string race) =>
        GameWorld.TalkWorlds.Single(w => w.Special == special && w.Race == race);

    [Fact]
    public void EachListedWorldIsItsGeneratorsTests()
    {
        var s0 = StoryWith();
        const int Infinite = TalkWorld.InfiniteFleet;
        // What every entry shares unless said below: the race's own
        // conversation, matched always, guarded by StartSphereTracking alone
        // (asked), an infinite fleet, the groups standing down and put aside,
        // the generator returning after the talk (no Stays), no flag.
        void Plain(TalkWorld w, int bit, string? because)
        {
            Assert.Equal(((int, int?))(0, null), (w.Planet, w.Moon));   // matchWorld (solarSys, world, 0, MATCH_PLANET)
            Assert.Equal(bit, w.GlobalFlags);
            Assert.Null(w.Conversation);
            Assert.Null(w.Matches);
            Assert.Equal((true, true), Guard(w, s0));
            Assert.Equal((false, true), Guard(w, s0, lives: false));
            Assert.Equal(Infinite, w.Fleet(s0));
            Assert.True(w.StandDown && w.PutAside, because);
            Assert.Null(w.Stays);
            Assert.Null(w.Unguarded);
            Assert.False(w.UnguardedWhenOrbited);
        }
        // genilw.c:81 StartSphereTracking, 83 NotifyOthers, 84 PutGroupInfo, 88 INFINITE_FLEET, 92 bit 7, 101 return.
        Plain(Listed("ILWRATH_DEFINED", "ILWRATH_SHIP"), 1 << 7, "genilw.c");
        Plain(Listed("VUX_DEFINED", "VUX_SHIP"), 1 << 7, "genvux.c:128-139, 150");
        Plain(Listed("YEHAT_DEFINED", "YEHAT_SHIP"), 1 << 7, "genyeh.c:80-101");
        Plain(Listed("PKUNK_DEFINED", "PKUNK_SHIP"), 1 << 7, "genpku.c:82-102");
        Plain(Listed("DRUUGE_DEFINED", "DRUUGE_SHIP"), 1 << 7, "gendru.c:91-110");
        Plain(Listed("UTWIG_DEFINED", "UTWIG_SHIP"), 1 << 7, "genutw.c:111-139 (UTWIG_DEFINED ||)");
        Plain(Listed("SUPOX_DEFINED", "SUPOX_SHIP"), 1 << 7, "gensup.c:82-102");
        Plain(Listed("THRADD_DEFINED", "THRADDASH_SHIP"), 1 << 7, "genthrad.c:99-133 (THRADD_DEFINED ||)");
        Assert.Equal(22, GameWorld.TalkWorlds.Count);

        // genspa.c:123-124: !SPATHI_SHIELDED_SELVES first, then StartSphereTracking.
        var spa = Listed("SPATHI_DEFINED", "SPATHI_SHIP");
        Assert.Equal(((int, int?))(0, 0), (spa.Planet, spa.Moon));   // :120 matchWorld (0, 0)
        Assert.Equal((false, false), Guard(spa, StoryWith(("SPATHI_SHIELDED_SELVES", 1))));
        Assert.Equal((true, true), Guard(spa, s0));
        Assert.Equal((1 << 7, Infinite, true, true, (Func<StoryState, bool, bool>?)null),
            (spa.GlobalFlags, spa.Fleet(s0), spa.StandDown, spa.PutAside, spa.Stays));   // :126-134, :144

        // genvux.c:124-128: the Maidens' world while !ZEX_IS_DEAD, then StartSphereTracking; bit 6 (:143);
        // after the talk (:157-159) out of orbit while ZEX lives, whatever's left (no survivors test).
        var maidens = Listed("MAIDENS_DEFINED", "VUX_SHIP");
        Assert.Equal(((int, int?))(0, null), (maidens.Planet, maidens.Moon));   // :124 matchWorld (0, MATCH_PLANET)
        Assert.Equal((false, false), Guard(maidens, StoryWith(("ZEX_IS_DEAD", 1))));
        Assert.Equal((true, true), Guard(maidens, s0));
        Assert.Equal((1 << 6, Infinite, true, true), (maidens.GlobalFlags, maidens.Fleet(s0), maidens.StandDown, maidens.PutAside));
        Assert.True(maidens.Stays!(s0, false));
        Assert.False(maidens.Stays(StoryWith(("ZEX_IS_DEAD", 1)), true));
        Assert.Null(maidens.Unguarded);

        // genorz.c:86-90: the homeworld always, the Taalo moon (1, 2) while !TAALO_PROTECTOR; :94-96
        // (ORZ_DEFINED || !TAALO_UNPROTECTED) && StartSphereTracking; :105-107 infinite, bit 7; :111-116
        // 14 ships, bit 6; :127-136 out of orbit while OrzSurvivors (the head link && (ORZ_DEFINED ||
        // !TAALO_UNPROTECTED)); :142 TAALO_UNPROTECTED set past the test, the Orz extinct too.
        var orz = Listed("ORZ_DEFINED", "ORZ_SHIP");
        Assert.Equal(((int, int?))(0, null), (orz.Planet, orz.Moon));   // :87 matchWorld (0, MATCH_PLANET)
        Assert.True(orz.PutAside);   // :99 PutGroupInfo
        Assert.Equal((true, true), Guard(orz, StoryWith(("TAALO_UNPROTECTED", 1))));
        Assert.Equal((1 << 7, Infinite), (orz.GlobalFlags, orz.Fleet(s0)));
        Assert.True(orz.Stays!(StoryWith(("TAALO_UNPROTECTED", 1)), true));
        Assert.False(orz.Stays(s0, false));
        Assert.Equal(("TAALO_UNPROTECTED", true, true), (orz.Unguarded, orz.UnguardedWhenOrbited, orz.StandDown));
        var taalo = Listed("TAALO_PROTECTOR_DEFINED", "ORZ_SHIP");
        Assert.Equal(((int, int?))(1, 2), (taalo.Planet, taalo.Moon));
        Assert.False(taalo.Matches!(StoryWith(("TAALO_PROTECTOR", 1))));
        Assert.True(taalo.Matches(s0));
        Assert.Equal((false, false), Guard(taalo, StoryWith(("TAALO_UNPROTECTED", 1))));
        Assert.Equal((true, true), Guard(taalo, s0));
        Assert.Equal((1 << 6, 14, true, true), (taalo.GlobalFlags, taalo.Fleet(s0), taalo.StandDown, taalo.PutAside));
        Assert.True(taalo.Stays!(s0, true));
        Assert.False(taalo.Stays(s0, false));
        Assert.False(taalo.Stays(StoryWith(("TAALO_UNPROTECTED", 1)), true));
        Assert.Equal(("TAALO_UNPROTECTED", true), (taalo.Unguarded, taalo.UnguardedWhenOrbited));

        // genmyc.c:90-95: StartSphereTracking first, then (MYCON_DEFINED || !SUN_DEVICE_UNGUARDED);
        // :102-113 infinite unless the Sun Device world's Mycon fell for the ambush (5); :120 bit 7, :124
        // bit 6; :134-144 out of orbit while any is left, else SUN_DEVICE_UNGUARDED (not past the test).
        var mycon = Listed("MYCON_DEFINED", "MYCON_SHIP");
        Assert.Equal(((int, int?))(0, null), (mycon.Planet, mycon.Moon));   // :88 matchWorld (0, MATCH_PLANET)
        Assert.Equal((true, true), (mycon.StandDown, mycon.PutAside));   // :97 NotifyOthers, :98 PutGroupInfo
        Assert.Equal((true, true), Guard(mycon, StoryWith(("SUN_DEVICE_UNGUARDED", 1))));
        Assert.Equal(Infinite, mycon.Fleet(StoryWith(("MYCON_FELL_FOR_AMBUSH", 1))));
        Assert.True(mycon.Stays!(s0, true));
        Assert.False(mycon.Stays(s0, false));
        Assert.Equal((1 << 7, "SUN_DEVICE_UNGUARDED", false), (mycon.GlobalFlags, mycon.Unguarded, mycon.UnguardedWhenOrbited));
        var sun = Listed("SUN_DEVICE_DEFINED", "MYCON_SHIP");
        Assert.Equal(((int, int?))(0, null), (sun.Planet, sun.Moon));   // genmyc.c:88 matchWorld (0, MATCH_PLANET)
        Assert.Equal((false, true), Guard(sun, StoryWith(("SUN_DEVICE_UNGUARDED", 1))));   // tracked first
        Assert.Equal((true, true), Guard(sun, s0));
        Assert.Equal(Infinite, sun.Fleet(s0));
        Assert.Equal(5, sun.Fleet(StoryWith(("MYCON_FELL_FOR_AMBUSH", 1))));
        Assert.True(sun.Stays!(s0, true));
        Assert.False(sun.Stays(s0, false));
        Assert.Equal((1 << 6, true, true, "SUN_DEVICE_UNGUARDED", false),
            (sun.GlobalFlags, sun.StandDown, sun.PutAside, sun.Unguarded, sun.UnguardedWhenOrbited));

        // genutw.c:104-108: the bomb's moon (5, 1) while !UTWIG_BOMB; :110-112 the Utwig while
        // !UTWIG_HAVE_ULTRON, then StartSphereTracking (infinite, bit 6 :129, returning :139); else :142-144
        // the Druuge while !BOMB_UNPROTECTED, then StartSphereTracking: no NotifyOthers (:148
        // PutGroupInfo only), 5 ships (:152), bit 6 (:158), out of orbit while any is left, else
        // BOMB_UNPROTECTED (:167-178).
        var utwigBomb = Listed("BOMB_DEFINED", "UTWIG_SHIP");
        var druugeBomb = Listed("BOMB_DEFINED", "DRUUGE_SHIP");
        Assert.True(GameWorld.TalkWorlds.ToList().IndexOf(utwigBomb) < GameWorld.TalkWorlds.ToList().IndexOf(druugeBomb));
        foreach (var w in new[] { utwigBomb, druugeBomb })
        {
            Assert.Equal(((int, int?))(5, 1), (w.Planet, w.Moon));
            Assert.False(w.Matches!(StoryWith(("UTWIG_BOMB", 1))));
            Assert.True(w.Matches(s0));
            Assert.Equal(1 << 6, w.GlobalFlags);
        }
        Assert.Equal((false, false), Guard(utwigBomb, StoryWith(("UTWIG_HAVE_ULTRON", 1))));
        Assert.Equal((true, true), Guard(utwigBomb, s0));
        Assert.Equal((Infinite, true, true, (Func<StoryState, bool, bool>?)null),
            (utwigBomb.Fleet(s0), utwigBomb.StandDown, utwigBomb.PutAside, utwigBomb.Stays));
        Assert.Equal((false, false), Guard(druugeBomb, StoryWith(("BOMB_UNPROTECTED", 1))));
        Assert.Equal((true, true), Guard(druugeBomb, s0));
        Assert.Equal((5, false, true), (druugeBomb.Fleet(s0), druugeBomb.StandDown, druugeBomb.PutAside));
        Assert.True(druugeBomb.Stays!(s0, true));
        Assert.False(druugeBomb.Stays(s0, false));
        Assert.Equal(("BOMB_UNPROTECTED", false), (druugeBomb.Unguarded, druugeBomb.UnguardedWhenOrbited));

        // genthrad.c:99-102: StartSphereTracking first, then the Helix's world while !HELIX_UNPROTECTED
        // and (BYTE)(THRADD_MISSION - 1) >= 3 (the mission 0, or past 3); bit 6 (:119); after the talk
        // (:130-133) the same test, whatever's left.
        var helix = Listed("AQUA_HELIX_DEFINED", "THRADDASH_SHIP");
        Assert.Equal(((int, int?))(0, null), (helix.Planet, helix.Moon));   // :97 matchWorld (0, MATCH_PLANET)
        Assert.Equal((false, true), Guard(helix, StoryWith(("HELIX_UNPROTECTED", 1))));
        Assert.Equal((true, true), Guard(helix, s0));
        foreach (var (mission, guarded) in new[] { (1, false), (2, false), (3, false), (4, true), (7, true) })
        {
            Assert.Equal(guarded, Guard(helix, StoryWith(("THRADD_MISSION", mission))).Guarded);
            Assert.Equal(guarded, helix.Stays!(StoryWith(("THRADD_MISSION", mission)), true));
        }
        Assert.True(helix.Stays!(s0, false));
        Assert.False(helix.Stays(StoryWith(("HELIX_UNPROTECTED", 1)), true));
        Assert.Equal((1 << 6, Infinite, true, true, (string?)null),
            (helix.GlobalFlags, helix.Fleet(s0), helix.StandDown, helix.PutAside, helix.Unguarded));

        // genzoq.c:90 StartSphereTracking, :92 PutGroupInfo with no NotifyOthers, :117-121 infinite, bit
        // 7. ZOQFOT_DISTRESS (:96) brings the Kohr-Ah's ship first, 3c's: until then the entry waits.
        var zoq = Listed("ZOQFOT_DEFINED", "ZOQFOTPIK_SHIP");
        Assert.Equal(((int, int?))(0, null), (zoq.Planet, zoq.Moon));   // :88 matchWorld (0, MATCH_PLANET)
        Assert.Equal((false, true), Guard(zoq, StoryWith(("ZOQFOT_DISTRESS", 1))));
        Assert.Equal((true, true), Guard(zoq, s0));
        Assert.Equal((1 << 7, Infinite, false, true, (Func<StoryState, bool, bool>?)null),
            (zoq.GlobalFlags, zoq.Fleet(s0), zoq.StandDown, zoq.PutAside, zoq.Stays));

        // genpet.c:83-101, the Umgah's half: their talk while they aren't zombies (:97), with
        // StartSphereTracking (:93); NotifyOthers (:88), infinite (:99), bit 7 (:96).
        var umgah = Listed("TALKING_PET_DEFINED", "UMGAH_SHIP");
        Assert.Equal(((int, int?))(0, null), (umgah.Planet, umgah.Moon));   // :83 matchWorld (0, MATCH_PLANET)
        Assert.Equal((false, false), Guard(umgah, StoryWith(("UMGAH_ZOMBIE_BLOBBIES", 1))));
        Assert.Equal((true, true), Guard(umgah, s0));
        Assert.Equal((false, true), Guard(umgah, s0, lives: false));
        Assert.Equal((1 << 7, Infinite, true, true, (Func<StoryState, bool, bool>?)null),
            (umgah.GlobalFlags, umgah.Fleet(s0), umgah.StandDown, umgah.PutAside, umgah.Stays));

        // gensly.c:61-64 (planet 3) and gensyr.c:91-95 (the starbase, moon 0 of planet 0): their own
        // conversations, no test, no ship cloned, no group touched, no bit set, returning at once.
        foreach (var (w, conversation, place) in new[]
                 {
                     (Listed("SLYLANDRO_DEFINED", "SLYLANDRO_SHIP"), "SLYLANDRO_HOME_CONVERSATION", ((int, int?))(3, null)),
                     (Listed("SYREEN_DEFINED", "SYREEN_SHIP"), "SYREEN_CONVERSATION", ((int, int?))(0, 0)),
                 })
        {
            Assert.Equal(place, (w.Planet, w.Moon));
            Assert.Equal(conversation, w.Conversation);
            Assert.Null(w.Matches);
            Assert.Equal((true, false), Guard(w, s0, lives: false));
            Assert.Equal((0, 0, false, false), (w.GlobalFlags, w.Fleet(s0), w.StandDown, w.PutAside));
            Assert.Null(w.Stays);
            Assert.Null(w.Unguarded);
        }

        // genchmmr.c:94-110: planet 1, the Chmmr's own talk first once CHMMR_UNLEASHED (:96, 3c's), else
        // the Ilwrath while SUN_DEVICE_ON_SHIP and !ILWRATH_DECEIVED, then StartSphereTracking; :112
        // PutGroupInfo with no NotifyOthers, :116 infinite, :119 bit 6, :130 returning.
        var procyon = Listed("CHMMR_DEFINED", "ILWRATH_SHIP");
        Assert.Equal(((int, int?))(1, null), (procyon.Planet, procyon.Moon));
        Assert.False(procyon.Matches!(StoryWith(("CHMMR_UNLEASHED", 1))));
        Assert.True(procyon.Matches(s0));
        Assert.Equal((false, false), Guard(procyon, s0));
        Assert.Equal((true, true), Guard(procyon, StoryWith(("SUN_DEVICE_ON_SHIP", 1))));
        Assert.Equal((false, false), Guard(procyon, StoryWith(("SUN_DEVICE_ON_SHIP", 1), ("ILWRATH_DECEIVED", 1))));
        Assert.Equal((1 << 6, Infinite, false, true, (Func<StoryState, bool, bool>?)null),
            (procyon.GlobalFlags, procyon.Fleet(s0), procyon.StandDown, procyon.PutAside, procyon.Stays));
    }

    // A finite guard of two Ilwrath at their homeworld, by genutw.c's Druuge
    // at the bomb: their talk while BOMB_UNPROTECTED isn't set, the world
    // orbited once none of them is left.
    private static TalkWorld Guard(int ships = 2) =>
        new("ILWRATH_DEFINED", 0, null, "ILWRATH_SHIP", 1 << 7, "test")
        {
            Guarded = (s, lives) => s.Get("BOMB_UNPROTECTED") == 0 && lives(),
            Fleet = _ => ships,
            StandDown = false,
            Stays = (_, survivors) => survivors,
            Unguarded = "BOMB_UNPROTECTED",
        };

    [ContentFact]
    public void AFiniteGuardBeatenSetsItsFlagAndTheShipIsInOrbit()
    {
        var world = NewGame();
        world.WorldTalkTable = new[] { Guard() };
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var d = world.SystemGroups!.Systems[star];
        var m = world.MeetingOf("h")!;
        Assert.False(m.Infinite);
        Assert.Equal(new[] { "ilwrath", "ilwrath" }, m.Ships.Select(s => s.Ship));
        Assert.Equal(IpFlight.EscapeFrames, world.Host.Ship.Ip!.EscapeCounter);
        // The race's groups don't stand down (genutw.c's Druuge: no NotifyOthers).
        Assert.Contains(d.Groups, g => g.Race == m.Race && (g.Task & IpGroup.IgnoreFlagship) == 0);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Equal(new[] { "ilwrath", "ilwrath" }, m.Battle!.EnemyFleet.Ships.Select(s => s.Ship));
        Assert.False(m.Battle.EnemyFleet.Infinite);
        WinTheBattle(world, m);
        Frames(world, 1);
        // None left: the flag set and the ship in orbit around the world (genutw.c:173).
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Empty(m.Left!);
        Assert.Equal(1, world.Story!.Get("BOMB_UNPROTECTED"));
        Assert.Equal(((int, int?)?)(0, null), world.Host.Ship.Ip!.Orbit);
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
        // Orbited again: nothing guards it.
        world.Host.Ship.Ip.ReturnFromOrbit();
        world.PutInOrbit("h", star, 0);
        Assert.Null(world.MeetingOf("h"));
        Assert.Equal(Activity.Orbit, world.Host.Ship.Activity);
    }

    [ContentFact]
    public void AFiniteGuardWithSurvivorsComesBackWhole()
    {
        var world = NewGame();
        world.WorldTalkTable = new[] { Guard(3) };
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var wb = m.Battle!;
        for (int i = 0; i < 400 && wb.Enemy.RaceDesc is null; ++i)
            Frames(world, 1);
        Frames(world, 30);
        WorldBattleTests.Kill(wb.Battle, wb.Enemy);
        Frames(world, 5);
        Retreat(world, m);
        // Survivors: no flag, no orbit (the generator returns); the guard isn't
        // kept: the next orbit clones it whole again (ReinitQueue after the talk).
        Assert.Equal(2, m.Left!.Count);
        Assert.Equal(0, world.Story!.Get("BOMB_UNPROTECTED"));
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        world.Host.Ship.Ip.EscapeCounter = 0;
        world.PutInOrbit("h", star, 0);
        Assert.Equal(3, world.MeetingOf("h")!.Ships.Count);
    }

    [ContentFact]
    public void AGuardBeatenAfterThePlayerLeftItsBattleSetsTheFlagWithNoOrbit()
    {
        // The host attacks and retreats; the guest, let in, destroys the
        // guard: none left, the flag set, but the host, flying elsewhere
        // since, isn't put in orbit.
        var world = NewGame("g");
        world.WorldTalkTable = new[] { Guard() };
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        world.PutInSystem("g", star);
        var gip = world.Find("g")!.Ship.Ip!;
        gip.PlaceInOrbit(0, null);
        gip.ReturnFromOrbit();   // the guest by the same world
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.True(world.LetIntoBattle("g"));
        Frames(world, 3);
        var wb = m.Battle!;
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            if (wb.Enemy.RaceDesc is { } enemy)
                enemy.EnergyLevel = 0;
            world.Step();
        }
        Assert.Null(world.Host.Ship.Battle);
        Assert.Equal(MeetingStep.Battle, m.Step);
        WinTheBattle(world, m);
        Frames(world, 1);
        Assert.Equal(1, world.Story!.Get("BOMB_UNPROTECTED"));
        Assert.Null(world.Host.Ship.Ip!.Orbit);
        Assert.Null(world.Find("g")!.Ship.Ip!.Orbit);   // not the meeting's player
    }

    [ContentFact]
    public void AWorldsTalkCanEndItsGuardWithoutABattle()
    {
        // The generator's test after the talk (genvux.c's ZEX_IS_DEAD,
        // genthrad.c's Helix): here one Spathi at the Ilwrath homeworld,
        // whose talk counts SPATHI_VISITS: once it's counted, the world falls
        // through to its orbit, the flag set, with no battle.
        var world = NewGame();
        world.WorldTalkTable = new[]
        {
            new TalkWorld("ILWRATH_DEFINED", 0, null, "SPATHI_SHIP", 0, "test")
            {
                Conversation = "SPATHI_CONVERSATION",
                Fleet = _ => 1,
                Stays = (s, _) => s.Get("SPATHI_VISITS") == 0,
                Unguarded = "BOMB_UNPROTECTED",
            },
        };
        int star = Star(world, "ILWRATH_DEFINED");
        world.PutInOrbit("h", star, 0);
        var m = world.MeetingOf("h")!;
        Assert.IsType<SpathiScript>(m.Talk!.Script);
        Assert.False(m.Talk.OwnFields.ContainsKey("GLOBAL_FLAGS_AND_DATA"));   // none set: its space talk
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", Reply(m.Talk, "part_in_peace"), m.Round));
        MeetingTests.HearClosing(world, "h");
        Assert.Null(m.Battle);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(1, world.Story!.Get("BOMB_UNPROTECTED"));
        Assert.Equal(((int, int?)?)(0, null), world.Host.Ship.Ip!.Orbit);
    }

    [ContentFact]
    public void ATalkWithNoShipsThereHasNoBattle()
    {
        // gensyr.c:91: the Syreen's starbase starts their talk with no ship
        // cloned (no BuildBattle: no escape counter), the system's groups
        // left as they are. Here the Spathi's space talk there, which is
        // hostile: its encounter screen shows (InitEncounter, BATTLE_SEGUE
        // set by the script), and a talk ending hostile has nothing to fight.
        var world = NewGame("g");
        world.WorldTalkTable = new[]
        {
            new TalkWorld("SYREEN_DEFINED", 0, 0, "SPATHI_SHIP", 0, "test")
            {
                Conversation = "SPATHI_CONVERSATION",
                Guarded = (_, _) => true,
                Fleet = _ => 0,
                StandDown = false,
                PutAside = false,
            },
        };
        int star = Star(world, "SYREEN_DEFINED");
        Assert.False(world.Systems![star].EnterOrbit(0, 0).Entered);   // a starbase: never orbited
        world.PutInSystem("g", star);
        world.TakeNotices();
        world.PutInOrbit("h", star, 0, 0);
        var m = world.MeetingOf("h")!;
        Assert.Empty(m.Ships);
        Assert.False(m.Infinite);
        Assert.Equal(((int, int?)?)(0, 0), m.World);
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
        Assert.Null(world.Host.Ship.Ip.Orbit);
        Assert.Equal("h", world.DialogueLocks!.Holder("SPATHI_CONVERSATION"));
        // The guest there is told who's meeting whom, no "hold off" (nothing stands down).
        Assert.Contains(world.TakeNotices(), n => n.PlayerId == "g" && n.Text.Contains("meeting the Spathi"));
        Assert.Equal(MeetingStep.Encounter, m.Step);
        Assert.True(world.EncounterChoice("h", EncounterAction.Hail));
        Assert.True(world.ChooseReply("h", Reply(m.Talk!, "prepare_to_die"), m.Round));
        Assert.True(world.ChooseReply("h", Reply(m.Talk!, "attack_you_now"), m.Round));
        Assert.Equal(Segue.Hostile, m.Talk!.Segue);
        MeetingTests.HearClosing(world, "h");
        // Hostile, but no ships: no battle (InitCommunication's GetHeadLink), the meeting over.
        Assert.Null(m.Battle);
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Null(world.MeetingOf("h"));
        Assert.Null(world.DialogueLocks.Holder("SPATHI_CONVERSATION"));
        Assert.Equal(0, world.Host.Ship.Ip!.EscapeCounter);
        Assert.Equal(Activity.InnerSystem, world.Host.Ship.Activity);
        // An attack at the screen fights nothing either.
        Frames(world, 1);
        world.Host.Ship.Ip.PlaceInOrbit(0, 0);
        world.Host.Ship.Ip.ReturnFromOrbit();
        world.PutInOrbit("h", star, 0, 0);
        m = world.MeetingOf("h")!;
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.Null(m.Battle);
        Assert.Equal(MeetingStep.Done, m.Step);
    }

    [ContentFact]
    public void TheWorldsAtOneWorldAreTriedInOrderAndAnUnportedOneHoldsTheOrbit()
    {
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "ILWRATH_DEFINED");
        var spathi = new TalkWorld("ILWRATH_DEFINED", 0, null, "SPATHI_SHIP", 0, "test")
        {
            Conversation = "SPATHI_CONVERSATION",
            Guarded = (s, lives) => s.Get("BOMB_UNPROTECTED") == 0 && lives(),
        };
        var ilwrath = GameWorld.TalkWorlds[0];
        world.WorldTalkTable = new[] { spathi, ilwrath };
        Assert.Same(spathi, world.WorldTalkAt(star, 0, null));
        story.Set("BOMB_UNPROTECTED", 1);   // the first not guarded: the next
        Assert.Same(ilwrath, world.WorldTalkAt(star, 0, null));
        // A guarded first entry whose script isn't ported: orbited as before, the next not tried.
        var vux = new TalkWorld("ILWRATH_DEFINED", 0, null, "VUX_SHIP", 1 << 7, "test");
        world.WorldTalkTable = new[] { vux, ilwrath };
        Assert.Null(world.WorldTalkAt(star, 0, null));
        Assert.Equal(0, story.Fleet("VUX_SHIP").KnownStrength);   // nothing tracked
        // An entry the world isn't now (Matches): skipped.
        world.WorldTalkTable = new[] { vux with { Matches = _ => false }, ilwrath };
        Assert.Same(ilwrath, world.WorldTalkAt(star, 0, null));
    }

    [ContentFact]
    public void TheOrzWorldsSetTaaloUnprotectedWhenOrbitedWithNoTalk()
    {
        // genorz.c:142: past its test, TAALO_UNPROTECTED is set, the Orz
        // extinct too. Here at the Ilwrath homeworld with the Ilwrath's script;
        // the real Orz entries wait for theirs.
        var world = NewGame();
        var story = world.Story!;
        int star = Star(world, "ILWRATH_DEFINED");
        story.SetFleet("ILWRATH_SHIP", story.Fleet("ILWRATH_SHIP") with { ActualStrength = 0 });
        story.SetAlliance("ILWRATH_SHIP", Allied.Dead);
        world.WorldTalkTable = new[] { GameWorld.TalkWorlds[0] with { Unguarded = "TAALO_UNPROTECTED", UnguardedWhenOrbited = true } };
        Assert.Null(world.WorldTalkAt(star, 0, null));
        Assert.Equal(1, story.Get("TAALO_UNPROTECTED"));
        // Not when another entry there starts its talk instead: no orbit.
        story.Set("TAALO_UNPROTECTED", 0);
        var spathi = new TalkWorld("ILWRATH_DEFINED", 0, null, "SPATHI_SHIP", 0, "test") { Conversation = "SPATHI_CONVERSATION" };
        world.WorldTalkTable = new[] { world.WorldTalkTable[0], spathi };
        Assert.Same(spathi, world.WorldTalkAt(star, 0, null));
        Assert.Equal(0, story.Get("TAALO_UNPROTECTED"));

        var orz = NewGame();
        orz.Story!.SetFleet("ORZ_SHIP", orz.Story.Fleet("ORZ_SHIP") with { ActualStrength = 0 });
        orz.Story.SetAlliance("ORZ_SHIP", Allied.Dead);
        Assert.Null(orz.WorldTalkAt(Star(orz, "ORZ_DEFINED"), 0, null));
        Assert.Equal(0, orz.Story.Get("TAALO_UNPROTECTED"));   // orzc.c not ported: as before
    }

    // ---- the Thraddash's body count (encount.c:760, UninitEncounter)

    [ContentFact]
    public void TheBodyCountIsUninitEncountersArithmetic()
    {
        var story = NewGame().Story!;
        void Count(int killed, string race = "THRADDASH_SHIP") => GameWorld.CountThraddashDead(story, race, killed);
        Count(3);
        Assert.Equal(3, story.Get("THRADDASH_BODY_COUNT"));
        Count(0);   // ships_killed 0: nothing written
        Count(2, "ILWRATH_SHIP");   // EncounterRace not the Thraddash
        Assert.Equal(3, story.Get("THRADDASH_BODY_COUNT"));
        Count(21);
        Assert.Equal(24, story.Get("THRADDASH_BODY_COUNT"));
        Count(5);   // capped at THRADDASH_BODY_THRESHOLD
        Assert.Equal(25, story.Get("THRADDASH_BODY_COUNT"));
        story.Set("THRADDASH_BODY_COUNT", 31);   // past it (5 bits): one more brings it back to 25
        Count(1);
        Assert.Equal(25, story.Get("THRADDASH_BODY_COUNT"));
        story.Set("THRADDASH_BODY_COUNT", 0);
        story.Set("THRADD_MANNER", 1);   // allied, or the alliance broken: not counted
        Count(4);
        Assert.Equal(0, story.Get("THRADDASH_BODY_COUNT"));
    }

    public static IEnumerable<object[]> BodyCounts() =>
        Golden.Load("encounter-screens").GetProperty("scenarios").EnumerateArray()
            .Where(s => s.GetProperty("step").GetString() != "encounter")
            .Select(s => new object[] { s.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(BodyCounts))]
    public void TheBodyCountIsUqmsUninitEncounters(string name)
    {
        // UQM's own UninitEncounter (tests/golden/encounter-screens.json): a
        // battle won removes every ship, one not won those whose crew ran
        // out (an infinite fleet's never: crew -1); the count after it is
        // the port's for the ships EndMeeting finds gone.
        var sc = Golden.Load("encounter-screens").GetProperty("scenarios").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == name);
        var ships = sc.GetProperty("ships").EnumerateArray().Select(s => (Race: s[0].GetString()!, Crew: s[1].GetInt32())).ToList();
        bool won = sc.GetProperty("step").GetString()!.StartsWith("won:", StringComparison.Ordinal);
        bool infinite = ships is [{ Crew: < 0 }];
        int left = won ? 0 : ships.Count(s => s.Crew != 0);
        var story = StoryState.NewGame(OutfitTests.Rules, new GameClock(2155, 2, 17));
        story.Set("THRADDASH_BODY_COUNT", sc.GetProperty("body_count").GetInt32());
        story.Set("THRADD_MANNER", sc.GetProperty("thradd_manner").GetInt32());
        GameWorld.CountThraddashDead(story, ships[0].Race, infinite ? 0 : ships.Count - left);
        Assert.Equal(sc.GetProperty("body_count_after").GetInt32(), story.Get("THRADDASH_BODY_COUNT"));
    }

    // A Thraddash group of so many ships by the host in hyperspace, met (their talk not ported: the screen).
    private static Meeting MeetThraddash(GameWorld world, int ships)
    {
        world.PutInHyperspace("h");
        var f = world.Host.Ship.Flight;
        f.PlaceAtUniverse(5800, 6100);
        f.Stop();
        f.StarJustLeft = null;
        int race = world.Story!.Rules.RaceIndex("THRADDASH_SHIP");
        var e = new HyperEncounter
        {
            Race = race, Roller = "h", OriginX = f.UniverseX, OriginY = f.UniverseY, Radius = 2000,
            LocX = (short)(f.UniverseX + 10), LocY = f.UniverseY,
            LogX = Uqm.Units.UniverseToLogX((short)(f.UniverseX + 10)), LogY = Uqm.Units.UniverseToLogY(f.UniverseY),
        };
        for (int i = 0; i < ships; ++i)
            e.Ships.Add(world.Story.Rules.RaceShips[race].Crew);
        world.Encounters!.List.Add(e);
        for (int i = 0; i < 300 && world.MeetingOf("h") is not { Step: MeetingStep.Encounter }; ++i)
            Frames(world, 1);
        return world.MeetingOf("h")!;
    }

    [ContentFact]
    public void ThraddashDestroyedInABattleWithThemAreCounted()
    {
        var world = NewGame();
        world.Story!.Set("THRADDASH_BODY_COUNT", 20);
        var m = MeetThraddash(world, 3);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var wb = m.Battle!;
        for (int i = 0; i < 400 && wb.Enemy.RaceDesc is null; ++i)
            Frames(world, 1);
        Frames(world, 30);
        WorldBattleTests.Kill(wb.Battle, wb.Enemy);
        Frames(world, 5);
        Retreat(world, m);
        // One destroyed, two left: one counted.
        Assert.Equal(2, m.Left!.Count);
        Assert.Equal(21, world.Story.Get("THRADDASH_BODY_COUNT"));
        Frames(world, 120);

        // A battle won: every ship counted, up to the threshold.
        world.Story.Set("THRADDASH_BODY_COUNT", 23);
        m = MeetThraddash(world, 3);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        WinTheBattle(world, m);
        Assert.Equal(25, world.Story.Get("THRADDASH_BODY_COUNT"));
    }

    [ContentFact]
    public void AGuestsKillsInTheBattleCountToo()
    {
        // Question 9: every player's kills in a battle with the Thraddash
        // count. The host attacks and leaves; the guest, let in, destroys
        // both ships: the meeting's battle counts them.
        var world = NewGame("g");
        var m = MeetThraddash(world, 2);
        var gs = world.Find("g")!.Ship;
        world.PutInHyperspace("g");
        gs.Flight.PlaceAtUniverse(world.Host.Ship.Flight.UniverseX, world.Host.Ship.Flight.UniverseY);
        gs.Flight.Stop();
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        Assert.True(world.LetIntoBattle("g"));
        Frames(world, 3);
        var wb = m.Battle!;
        Assert.Same(wb, gs.Battle);
        for (int i = 0; i < 300 && world.Host.Ship.Battle is not null; ++i)
        {
            world.SubmitInput("h", world.Frame + 1, ShipInput.TakeOff);
            world.SubmitInput("g", world.Frame + 1, ShipInput.None);
            if (wb.Enemy.RaceDesc is { } enemy)
                enemy.EnergyLevel = 0;
            world.Step();
        }
        Assert.Null(world.Host.Ship.Battle);
        Assert.Equal(MeetingStep.Battle, m.Step);
        WinTheBattle(world, m);
        Assert.Equal(2, world.Story!.Get("THRADDASH_BODY_COUNT"));
    }

    [ContentFact]
    public void AnInfiniteFleetsShipIsNeverCounted()
    {
        // UninitEncounter removes only ships whose crew ran out; an infinite
        // fleet's one fragment keeps INFINITE_FLEET (tactrans.c doesn't
        // update it). Thraddash ships at a test world with the Ilwrath's talk.
        var world = NewGame();
        world.WorldTalkTable = new[] { GameWorld.TalkWorlds[0] with { Race = "THRADDASH_SHIP", Conversation = "ILWRATH_CONVERSATION" } };
        world.PutInOrbit("h", Star(world, "ILWRATH_DEFINED"), 0);
        var m = world.MeetingOf("h")!;
        Assert.True(m.Infinite);
        Assert.True(world.EncounterChoice("h", EncounterAction.Attack));
        var wb = m.Battle!;
        for (int i = 0; i < 400 && wb.Enemy.RaceDesc is null; ++i)
            Frames(world, 1);
        Frames(world, 30);
        // The host retreats, its enemy destroyed whenever it's in: the battle
        // ends with the ship dead (crew 0), not yet brought back.
        for (int i = 0; i < 300 && m.Step == MeetingStep.Battle; ++i)
        {
            if (wb.Enemy is { RaceDesc.CrewLevel: > 0, Ship: not null } e)
                WorldBattleTests.Kill(wb.Battle, e);
            Frames(world, 1, ShipInput.TakeOff);
        }
        Assert.Equal(MeetingStep.Done, m.Step);
        Assert.Equal(0, wb.Enemies[0].Starship.CrewLevel);
        Assert.Equal(0, world.Story!.Get("THRADDASH_BODY_COUNT"));
    }
}
