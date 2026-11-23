// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The talks' screen changes in a talk's view (docs/differences.md row
/// 83): the communication screen as the lines start (its colour map, its
/// ambient animations, the talking) and the script's changes at the lines
/// they're made at, as UQM's script makes them (at once as a reply's lines
/// start, or as the track after an AlienTalkSegue's starts), checked on
/// every recorded path of every ported script and on the talks the row
/// names: the commander before and after the radioactives, the allied
/// commander's scientist's report, the Spathi at home, the Melnorme and
/// Admiral ZEX.
/// </summary>
[Trait("Topic", "talk")]
public class TalkScreenTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(DataDir);
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);

    // ------------------------------------------------------------ the recorded paths

    // The lines a view shows from event `from` (each line with a clip), as TalkView counts them.
    private static List<LineQueued> Lines(Talk talk, int from) =>
        talk.Events.Skip(from).OfType<LineQueued>().Where(l => l.Clip is not null).ToList();

    // A recorded start's talk with these replies chosen, and the event its last reply's lines start at.
    private static (Talk Talk, int From) Replay(ContentPackage package, string script, string start, params int[] replies)
    {
        var s = TalkTests.Recorded(script).Single(x => x.GetProperty("name").GetString() == start);
        var talk = TalkTests.Start(package, script, s);
        int from = 0;
        foreach (int r in replies)
        {
            from = talk.Events.Count + 1;
            talk.Choose(r);
        }
        return (talk, from);
    }

    private static int Phrase(string script, string id) => Scripts[script].PhraseNumber(id);

    private static bool Off(TalkScreen s, int ambient) => (s.Ambient[ambient] & CommAnimationFlags.Disabled) != 0;

    [ContentTheory]
    [MemberData(nameof(TalkTests.PortedScripts), MemberType = typeof(TalkTests))]
    public void EveryRecordedPathsScreenChangesAddUp(string script)
    {
        // On every recorded path (each one not a part of a longer), the view after each reply: its changes at its
        // lines in order, one for each fade and talking switch the script made, and the screen they leave the one
        // the next view starts with.
        TestContent.With(package =>
        {
            foreach (var start in TalkTests.Recorded(script).Sample())
            {
                var paths = start.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("replies").Ints()).ToList();
                foreach (var path in paths.Where(p => !paths.Any(q => q.Length > p.Length && q.Take(p.Length).SequenceEqual(p))))
                {
                    var talk = TalkTests.Start(package, script, start);
                    string where = $"{script} {start.GetProperty("name").GetString()} [{string.Join(", ", path)}]";
                    var views = new List<(TalkScreen Screen, int Lines, int From, int To)>();
                    void Add(int from)
                    {
                        var screen = TalkScreen.Of(talk, from);
                        Assert.True(screen is not null, where);
                        views.Add((screen!, Lines(talk, from).Count, from, talk.Events.Count));
                    }
                    Add(0);
                    foreach (int r in path)
                    {
                        int from = talk.Events.Count + 1;
                        talk.Choose(r);
                        Add(from);
                    }
                    for (int j = 0; j < views.Count; ++j)
                    {
                        var (screen, lines, from, to) = views[j];
                        var range = talk.Events.Take(to).Skip(from).ToList();
                        Assert.All(screen.Changes, c => Assert.InRange(c.Line, 0, lines));
                        Assert.True(screen.Changes.Select(c => c.Line).SequenceEqual(screen.Changes.Select(c => c.Line).Order()), where);
                        Assert.Equal(range.Count(e => e is ColorTransform), screen.Changes.Count(c => c.Kind == ScreenChangeKind.Fade));
                        if (j > 0)
                        {
                            Assert.Equal(range.Count(e => e is TalkingAnimation),
                                screen.Changes.Count(c => c.Kind is ScreenChangeKind.TalkingOn or ScreenChangeKind.TalkingOff));
                            // Each ambient animation turned on or off between the screen's recorded changes.
                            var last = talk.Events.Take(from).OfType<ScreenChanged>().Last().Ambient;
                            int flips = 0;
                            foreach (var s in range.OfType<ScreenChanged>())
                            {
                                flips += s.Ambient.Zip(last).Count(a => ((a.First ^ a.Second) & CommAnimationFlags.Disabled) != 0);
                                last = s.Ambient;
                            }
                            Assert.Equal(flips, screen.Changes.Count(c => c.Kind is ScreenChangeKind.AmbientOn or ScreenChangeKind.AmbientOff));
                        }
                        if (j + 1 == views.Count)
                            continue;
                        // The changes made on this view's start leave the next's.
                        int map = screen.ColorMap;
                        var off = screen.Ambient.Select(a => (a & CommAnimationFlags.Disabled) != 0).ToArray();
                        bool talking = screen.Talking;
                        foreach (var c in screen.Changes)
                            switch (c.Kind)
                            {
                                case ScreenChangeKind.Fade: map = c.Value; break;
                                case ScreenChangeKind.AmbientOn: off[c.Value] = false; break;
                                case ScreenChangeKind.AmbientOff: off[c.Value] = true; break;
                                case ScreenChangeKind.TalkingOn: talking = true; break;
                                case ScreenChangeKind.TalkingOff: talking = false; break;
                            }
                        var next = views[j + 1].Screen;
                        Assert.True(map == next.ColorMap, $"{where}: view {j}'s colours end at {map}, the next starts at {next.ColorMap}");
                        Assert.Equal(off, next.Ambient.Select(a => (a & CommAnimationFlags.Disabled) != 0));
                        Assert.Equal(talking, next.Talking);
                    }
                }
            }
            return 0;
        });
    }

    [ContentFact]
    public void TheCommandersPowerComesBackAsTheRadioactivesAreTransferred()
    {
        TestContent.With(package =>
        {
            // His first visit: the dark low-power colours (map 1) and the flicker (the third ambient animation, a
            // colour animation) from the screen's first drawing, the talking on.
            var (talk, _) = Replay(package, "comandr", "first-radios-lander-fuel");
            var intro = TalkScreen.Of(talk, 0)!;
            Assert.Equal((1, false, true), (intro.ColorMap, Off(intro, 2), intro.Talking));
            Assert.Empty(intro.Changes);
            Assert.Equal(CommAnimationFlags.Random | CommAnimationFlags.PauseTalking, intro.Ambient[2]);

            // "We'll transfer them now": FUEL_UP0 plays, then as FUEL_UP1 starts (AlienTalkSegue (1)) the flicker is
            // turned off and the colours fade to full power (map 0) over half a second.
            (talk, int from) = Replay(package, "comandr", "first-radios-lander-fuel", 0, 0);
            var lines = Lines(talk, from);
            Assert.Equal(new[] { Phrase("comandr", "FUEL_UP0"), Phrase("comandr", "FUEL_UP1") }, lines.Select(l => l.Phrase));
            var transfer = TalkScreen.Of(talk, from)!;
            Assert.Equal((1, false), (transfer.ColorMap, Off(transfer, 2)));
            Assert.Equal(new[]
            {
                new ScreenChange(1, ScreenChangeKind.AmbientOff, 2),
                new ScreenChange(1, ScreenChangeKind.Fade, 0, Talk.OneSecond / 2),
            }, transfer.Changes);
            // After the view's other talk's lines (a visit's), the same changes at its own lines.
            Assert.Equal(new[] { 4, 4 }, TalkScreen.Of(talk, from, 3)!.Changes.Select(c => c.Line));

            // Every later visit: his init turns the flicker off, and the screen is drawn at full power.
            (talk, _) = Replay(package, "comandr", "moon-base-lander-fuel");
            var later = TalkScreen.Of(talk, 0)!;
            Assert.Equal((0, true, true), (later.ColorMap, Off(later, 2), later.Talking));
            Assert.Empty(later.Changes);
            return 0;
        });
    }

    [ContentFact]
    public void TheAlliedCommandersMouthIsStillThroughTheScientistsReport()
    {
        TestContent.With(package =>
        {
            // The devices told of with the VUX beast aboard (starbas.c:1311): the talking stops as the scientist's
            // report (ABOUT_VUXBEAST1) starts and starts again with the line after it.
            var (talk, from) = Replay(package, "starbas", "device-vux_beast_on_ship", 0);
            var lines = Lines(talk, from);
            int report = lines.FindIndex(l => l.Phrase == Phrase("starbas", "ABOUT_VUXBEAST1"));
            Assert.Equal(2, report);
            var screen = TalkScreen.Of(talk, from)!;
            Assert.True(screen.Talking);
            Assert.Equal(new[]
            {
                new ScreenChange(report, ScreenChangeKind.TalkingOff),
                new ScreenChange(report + 1, ScreenChangeKind.TalkingOn),
            }, screen.Changes);
            return 0;
        });
    }

    [ContentFact]
    public void TheSpathiAtHomeTurnFriendly()
    {
        TestContent.With(package =>
        {
            // At the council with the password (spahome.c:948): friendly from the screen's first drawing.
            var (talk, _) = Replay(package, "spahome", "council");
            var council = TalkScreen.Of(talk, 0)!;
            Assert.Equal(1, council.ColorMap);
            Assert.Empty(council.Changes);
            // The password given (spahome.c:760): the colours fade to friendly at once, as YES_GOOD_PASSWORD starts.
            (talk, int from) = Replay(package, "spahome", "melnorme-password", 0);
            var password = TalkScreen.Of(talk, from)!;
            Assert.Equal(0, password.ColorMap);
            Assert.Equal(new[] { new ScreenChange(0, ScreenChangeKind.Fade, 1, Talk.OneSecond / 4) }, password.Changes);
            Assert.Equal(Phrase("spahome", "YES_GOOD_PASSWORD"), Lines(talk, from)[0].Phrase);
            return 0;
        });
    }

    [ContentFact]
    public void TheMelnormesColoursFade()
    {
        TestContent.With(package =>
        {
            // Down to business (melnorm.c:1342): the news, then as BUY_OR_SELL starts the fade to purple.
            var (talk, _) = Replay(package, "melnorm", "hello-6");
            var business = TalkScreen.Of(talk, 0)!;
            Assert.Equal(0, business.ColorMap);
            Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.Fade, 1, Talk.OneSecond / 2) }, business.Changes);
            Assert.Equal(Phrase("melnorm", "BUY_OR_SELL"), Lines(talk, 0)[1].Phrase);
            // Greeting the players angry (melnorm.c:1651): the fade to red, made before the screen is first drawn,
            // runs as it's drawn in their first colours.
            (talk, _) = Replay(package, "melnorm", "hate-0");
            var hate = TalkScreen.Of(talk, 0)!;
            Assert.Equal(0, hate.ColorMap);
            Assert.Equal(new[] { new ScreenChange(0, ScreenChangeKind.Fade, 2, Talk.OneSecond / 2) }, hate.Changes);
            return 0;
        });
    }

    [ContentFact]
    public void ZexsColoursFadeAsTheBeastKillsHim()
    {
        TestContent.With(package =>
        {
            // "OK, take the beast" (vuxc.c): FOOL_AIEE0, then the fade as FOOL_AIEE1 starts.
            var path = new[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            var (talk, from) = Replay(package, "vux", "zex-beast-1-know-0", path);
            Assert.Equal(new[] { Phrase("vux", "FOOL_AIEE0"), Phrase("vux", "FOOL_AIEE1") }, Lines(talk, from).Select(l => l.Phrase));
            var screen = TalkScreen.Of(talk, from)!;
            Assert.Equal(new[] { new ScreenChange(1, ScreenChangeKind.Fade, 1, Talk.OneSecond / 4) }, screen.Changes);
            // A player coming back after it finds him in the colours it faded to.
            Assert.Equal(1, TalkScreen.Of(talk, talk.Events.Count)!.ColorMap);
            return 0;
        });
    }

    // ------------------------------------------------------------ the views

    private static GameWorld WithTalks()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), "host", "Host",
            Systems, TestContent.Shapes);
        world.EncounterRaces = new HashSet<string>();
        world.TalkLibrary = UrquanMeetingTests.Library.Value;
        world.PutInOrbit("host", 82, 2, 1);   // Sol, Earth, Luna
        world.Host.Ship.Ip!.ReturnFromOrbit();
        return world;
    }

    // The host's reply by its phrase, in the visit they're shown (the opening's, or their own from the menu).
    private static void Say(GameWorld world, string script, string phrase)
    {
        var visit = world.VisitOf("host")!;
        var talk = visit.Talk!;
        int i = talk.Replies.Select(r => r.Phrase).ToList().IndexOf(Phrase(script, phrase));
        Assert.True(i >= 0, $"{phrase} isn't offered");
        Assert.True(world.ChooseReply(visit.Replier.Id, i));
    }

    [ContentFact]
    public void AVisitsViewCarriesTheScreenOfItsTalk()
    {
        // The commander, met before, with radioactives aboard: his view's screen is the low-power one, and the
        // transfer's changes come with its lines, through the codec whole.
        var world = WithTalks();
        world.CommanderMet = true;
        var hold = world.Host.Ship.Hold;
        hold.Restore(hold.Save() with { Minerals = new[] { 0, 0, 0, 0, 0, 0, 5, 0 } });
        world.VisitStarbase(world.Host);
        var view = TalkView.Of(world.Visit)!;
        Assert.Equal((1, false), (view.Screen!.ColorMap, Off(view.Screen, 2)));
        Say(world, "comandr", "we_will_transfer_now");
        view = TalkView.Of(world.Visit)!;
        Assert.Equal(2, view.Lines.Count);
        Assert.Equal(new[] { ScreenChangeKind.AmbientOff, ScreenChangeKind.Fade }, view.Screen!.Changes.Select(c => c.Kind));
        Assert.All(view.Screen.Changes, c => Assert.Equal(1, c.Line));
        WithinLines(view);
        var sent = TalkView.Decode(view.Encode())!;
        Assert.True(view.Same(sent));
        Assert.Equal(view.Screen.Changes, sent.Screen!.Changes);

        // A view over two talks (the commander's closing lines, then the Ilwrath's): the screen is the last
        // talk's, whose lines come after his.
        world = WithTalks();
        var story = world.Story!;
        story.Set("RADIOACTIVES_PROVIDED", 1);
        story.Set("MOONBASE_DESTROYED", 1);
        story.Set("MOONBASE_ON_SHIP", 1);
        world.CommanderMet = true;
        world.VisitStarbase(world.Host);
        Say(world, "comandr", "base_was_abandoned");
        var visit = world.Visit!;
        Assert.Equal(VisitStep.Ilwrath, visit.Step);
        view = TalkView.Of(visit)!;
        int his = Lines(visit.Talks[0], visit.Talks[0].Events.ToList().FindLastIndex(e => e is ReplyChosen) + 1).Count;
        Assert.True(his > 0 && view.Lines.Count > his);
        Assert.True(view.Screen!.Same(TalkScreen.Of(visit.Talks[1], 0, his)));
        Assert.Equal(Scripts["ilwrath"].Description.Ambient.Count, view.Screen.Ambient.Count);
        WithinLines(view);
    }

    // A view's changes are at its lines or after them, never past (TalkScreen.Encode's clamp a safety net only).
    private static void WithinLines(TalkView view) =>
        Assert.All(view.Screen?.Changes ?? Array.Empty<ScreenChange>(), c => Assert.InRange(c.Line, 0, view.Lines.Count));

    [ContentFact]
    public void TheAlliedCommandersTalkFromTheMenuCarriesItsChangesAndLeavingShowsTheScreenAsTheyLeftIt()
    {
        // Docked, the VUX beast aboard: the new devices told of, the scientist's report with the mouth still
        // (its two switches at its lines); goodbye; leaving shows the visit's end with no lines (GameWorld's
        // TalkView.Over): the screen with no changes, which encodes.
        var world = WithTalks();
        var story = world.Story!;
        foreach (var f in new[] { "STARBASE_AVAILABLE", "RADIOACTIVES_PROVIDED", "MOONBASE_DESTROYED", "VUX_BEAST_ON_SHIP" })
            story.Set(f, 1);
        story.Set("STARBASE_MONTH", 3);
        story.Set("STARBASE_DAY", 3);
        story.Set32("STARBASE_BULLETS0", 0xFFFFFFFF);
        world.CommanderMet = true;
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        var host = world.Host;
        world.VisitStarbase(host);
        world.StarbaseKey("host", MenuKey.Select, out _);   // Talk
        var visit = host.Ship.Visit!;
        Assert.Equal(VisitStep.MenuTalk, visit.Step);
        Say(world, "starbas", "new_devices");
        var view = TalkView.Of(visit)!;
        Assert.Equal(new[] { ScreenChangeKind.TalkingOff, ScreenChangeKind.TalkingOn }, view.Screen!.Changes.Select(c => c.Kind));
        WithinLines(view);
        Assert.True(view.Same(TalkView.Decode(view.Encode())));
        Say(world, "starbas", "goodbye_commander");
        Assert.Equal(VisitStep.Menu, visit.Step);
        var last = TalkView.Of(visit)!.Over();
        world.StarbaseKey("host", MenuKey.Up, out _);
        world.StarbaseKey("host", MenuKey.Select, out _);   // Leave
        Assert.Null(host.Ship.Visit);
        // The world's end view is the visit's last view made over (TalkView.Over). Goodbye's lines make no
        // screen changes, and no reachable end view has any, so the direct Over() below is what pins the screen.
        var over = world.TalkFor("host")!;
        Assert.True(over.Same(last));
        Assert.Equal((VisitStep.Done, 0), (over.Step, over.Lines.Count));
        Assert.Empty(over.Screen!.Changes);
        Assert.True(over.Screen.Talking);
        Assert.True(over.Same(TalkView.Decode(over.Encode())));
        // What a visit's end makes of a last view with changes at its lines: none left, the screen as they leave it.
        var cut = view.Over();
        Assert.Empty(cut.Screen!.Changes);
        Assert.True(cut.Screen.Same(view.Screen.AfterChanges()));
        Assert.True(cut.Same(TalkView.Decode(cut.Encode())));
        Assert.True((view with { Screen = view.Screen with { Talking = false } }).Over().Screen!.Talking);   // its switches made: on again
    }
}
