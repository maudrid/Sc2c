// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The communication screen (docs/plans/stage-3.md, item 11): every race's
/// alien window drawn from the content, and the screen's lines, replies,
/// feedback and summary on the client's clock, with UQM's keys. The text's
/// layout and the animations are checked against UQM in
/// <see cref="CommTextTests"/>, <see cref="AlienAnimationTests"/> and
/// <see cref="ColorFadesTests"/>.
/// </summary>
[Trait("Topic", "drawing")]
public class CommScreenTests
{
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    public static IEnumerable<object[]> Races() => Scripts.All.Select(s => new object[] { s.Name });

    [ContentTheory]
    [MemberData(nameof(Races))]
    public void EveryRacesWindowIsDrawnAndAnimates(string script)
    {
        var d = Scripts[script].Description;
        TestContent.With(package =>
        {
            if (!package.Resources.ContainsKey(d.Frames))
                return 0;   // a race whose pictures UQM never loads
            var sheets = new AnimationSheets(package);
            var window = new AlienWindow(sheets, package, d, 7, 0);
            var main = SpriteSheet.Load(sheets, d.Frames);
            Assert.Equal(window.Height, main.Render(0, window.Maps).Height);
            Assert.True(window.Update(0));   // the first frame: the whole picture
            Assert.Contains(window.Pixels.Where((_, i) => i % 4 == 3), a => a == 255);
            // A minute of talk on and off: every frame and colour map it
            // uses is there (an animation's frame past the end wraps, as
            // SetAbsFrameIndex's).
            for (long t = 21; t < 840 * 60; t += 21)
            {
                if (t % (840 * 10) == 21 * 40)
                    window.Animation.StartTalking();
                else if (t % (840 * 10) == 21 * 200)
                    window.Animation.StopTalking();
                window.Update(t);
            }
            Assert.False(window.Update(840 * 60 - 1));   // not a frame's time yet
            return 0;
        });
    }

    private static CommScreen Starbase(out CommDescription race)
    {
        var d = race = Scripts["starbas"].Description;
        return TestContent.With(package =>
        {
            var sheets = new AnimationSheets(package);
            var activity = SpriteSheet.Load(sheets, "graphics.activity");
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            return new CommScreen(new AlienWindow(sheets, package, d, 1, 0), d, UqmFont.Load(package, d.Font),
                UqmFont.Load(package, "font.player"), UqmFont.Load(package, "font.tiny"),
                activity.Render(CommScreen.SliderButtonFrame, maps).Width);
        });
    }

    private static CommScreen ZoqFotPik(out AlienWindow window)
    {
        var d = Scripts["zoqfot"].Description;
        var screen = TestContent.With(package =>
        {
            var sheets = new AnimationSheets(package);
            var activity = SpriteSheet.Load(sheets, "graphics.activity");
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            return new CommScreen(new AlienWindow(sheets, package, d, 1, 0), d, UqmFont.Load(package, d.Font),
                UqmFont.Load(package, "font.player"), UqmFont.Load(package, "font.tiny"),
                activity.Render(CommScreen.SliderButtonFrame, maps).Width, CommSpeakers.For("zoqfot"));
        });
        window = screen.Window;
        return screen;
    }

    [ContentFact]
    public void TheZoqFotPiksLinesSwitchTheSpeakerAsEachPlays()
    {
        // Stage 3b-2 item 5: each line's callback (the view's speakers) runs as its pages play: Zoq on the
        // left, Pik on the right (zoqfotc.c's ZOQ_BASE_X, PIK_BASE_X, BASE_Y 24), each with his own talking
        // frames and the eye's move to him, Fot's smoke waiting while Pik talks.
        var screen = ZoqFotPik(out var window);
        var anim = window.Animation;
        screen.Show(new[] { "Zoq speaks.", "Pik speaks.", "Zoq again." }, "", new[] { "Bye" }, 1, 0,
            new[] { "SelectAlienZOQ", "SelectAlienPIK", "SelectAlienZOQ" });
        // Each run of lines starts with Zoq (ZFPTalkSegue).
        screen.Update(1);
        Assert.Equal(new CommTextPlace(ZoqFotPikSpeakers.ZoqBaseX, 24, new Rgb(255, 255, 255), new Rgb(0, 0, 0)), screen.TextPlace);
        Assert.Equal((18, 5, 23), anim.Talking);
        Assert.Equal(0, (int)(anim.AmbientFlags[1] & CommAnimationFlags.WaitTalking));
        Assert.Equal(61, ScreenTextX(screen));
        // The second page is Pik's.
        long t = 1;
        while (screen.TextPlace.X != ZoqFotPikSpeakers.PikBaseX && screen.Talking)
            screen.Update(t += 21);
        Assert.True(screen.Talking);
        Assert.Equal(181, ZoqFotPikSpeakers.PikBaseX);
        Assert.Equal((29, 2, 26), anim.Talking);
        Assert.NotEqual(0, (int)(anim.AmbientFlags[1] & CommAnimationFlags.WaitTalking));
        Assert.Equal(181, ScreenTextX(screen));
        // Then Zoq's again.
        while (screen.TextPlace.X != ZoqFotPikSpeakers.ZoqBaseX && screen.Talking)
            screen.Update(t += 21);
        Assert.Equal((18, 5, 23), anim.Talking);
        // Played again (Left): no ZFPTalkSegue (UQM's replay has none), so the first page's SelectAlienZOQ runs with the
        // last speaker as the lines left him (LastAlien), here Pik, whose talking plays out first (TALK_DONE). Replaying
        // from the first page is the port's (differences.md row 77): UQM's, once the lines have ended, plays only the
        // last page, so it wouldn't make this Pik-to-Zoq change.
        screen.Show(new[] { "Zoq speaks.", "Pik ends." }, "", new[] { "Bye" }, 2, t, new[] { "SelectAlienZOQ", "SelectAlienPIK" });
        while (screen.Talking)
            screen.Update(t += 21);
        Assert.Equal(ZoqFotPikSpeakers.PikBaseX, screen.TextPlace.X);
        screen.Page(TalkPage.Replay);
        Assert.Equal((29, 2, 26), anim.Talking);   // no segue: Pik's still, until the first page plays
        screen.Update(t += 1);
        Assert.Equal((18, 5, 23), anim.Talking);
        Assert.Equal(ZoqFotPikSpeakers.ZoqBaseX, screen.TextPlace.X);
        // A race without speakers keeps its description's place.
        Assert.Null(CommSpeakers.For("starbas"));
    }

    // A race's communication screen opened as a talk's view gives it (TalkView.Screen), with its colour table.
    private static CommScreen Opened(string script, TalkScreen? start, out BinaryTable colors)
    {
        var d = Scripts[script].Description;
        BinaryTable table = null!;
        var screen = TestContent.With(package =>
        {
            var sheets = new AnimationSheets(package);
            var activity = SpriteSheet.Load(sheets, "graphics.activity");
            var maps = new ColorMaps();
            maps.Set(BinaryTable.Load(package, "colortable.main")[0]);
            table = BinaryTable.Load(package, d.ColorMap);
            return new CommScreen(new AlienWindow(sheets, package, d, 1, 0), d, UqmFont.Load(package, d.Font),
                UqmFont.Load(package, "font.player"), UqmFont.Load(package, "font.tiny"),
                activity.Render(CommScreen.SliderButtonFrame, maps).Width, null, start);
        });
        colors = table;
        return screen;
    }

    // Whether the window's colours are the colour table's entry (its first map, where the alien's colours are).
    private static bool ColorsAre(CommScreen screen, BinaryTable colors, int entry)
    {
        var e = colors[entry].ToArray();
        var map = screen.Window.Maps[e[0]]!;
        return Enumerable.Range(0, 256).All(i => map[i] == new Rgb(e[2 + i * 3], e[3 + i * 3], e[4 + i * 3]));
    }

    [ContentFact]
    public void ATalksScreenChangesAreMadeAsTheirLinesPlay()
    {
        // Differences.md row 83. The commander before the radioactives: the screen opened in his low-power colours
        // (map 1) with the flicker on; as the transfer's second line starts the flicker goes off and the colours
        // fade to full power over half a second.
        var flags = Scripts["comandr"].Description.Ambient.Select(a => a.Flags).ToList();
        // A view's screen as the host sends it: the colour map, the flicker off or not, and the changes at the lines.
        TalkScreen View(int map, bool flickerOff, params ScreenChange[] changes) =>
            new(map, flags.Select((f, i) => i == 2 && flickerOff ? f | CommAnimationFlags.Disabled : f).ToList(), true, changes);
        bool FlickerOff(CommScreen s) => (s.Window.Animation.AmbientFlags[2] & CommAnimationFlags.Disabled) != 0;
        var screen = Opened("comandr", View(1, false), out var colors);
        var anim = screen.Window.Animation;
        Assert.True(ColorsAre(screen, colors, 1));
        Assert.False(ColorsAre(screen, colors, 0));
        var transfer = View(1, false, new ScreenChange(1, ScreenChangeKind.AmbientOff, 2), new ScreenChange(1, ScreenChangeKind.Fade, 0, 420));
        screen.Show(new[] { "Fuel up.", "Power coming." }, "Transfer them.", new[] { "Who are you?" }, 2, 0, null, transfer);
        long t = 0;
        screen.Update(t);
        Assert.False(FlickerOff(screen));   // the first line: not yet
        while (screen.Draw(-1).OverTexts[0] is var p && Encoding.UTF8.GetString(p) != "Power coming.")
            screen.Update(t += 21);
        Assert.True(FlickerOff(screen));
        long faded = t + 420;
        while (t <= faded + 21)
            screen.Update(t += 21);
        Assert.True(ColorsAre(screen, colors, 0));   // full power, and steady: no flicker
        for (int k = 0; k < 100; ++k)
            screen.Update(t += 21);
        Assert.True(ColorsAre(screen, colors, 0));
        // Played again: the script has run, nothing is made again (the flicker turned back on by hand stays on).
        anim.SetAmbientFlags(2, anim.AmbientFlags[2] & ~CommAnimationFlags.Disabled);
        screen.Page(TalkPage.Replay);
        while (screen.Talking)
            screen.Update(t += 21);
        Assert.False(FlickerOff(screen));
        anim.SetAmbientFlags(2, anim.AmbientFlags[2] | CommAnimationFlags.Disabled);   // as the host has it again
        // A skip makes what's left at once; lines replaced before their changes were made make them then, and the
        // next view's screen, which they lead to, changes nothing more.
        screen.Show(new[] { "One.", "Two.", "Three." }, "Again.", Array.Empty<string>(), 3, t, null,
            View(0, true, new ScreenChange(2, ScreenChangeKind.AmbientOn, 2)));
        screen.Update(t += 1);
        Assert.True(FlickerOff(screen));
        screen.Page(TalkPage.Skip);
        Assert.False(FlickerOff(screen));
        screen.Show(new[] { "Four.", "Five." }, "More.", Array.Empty<string>(), 4, t, null,
            View(0, false, new ScreenChange(1, ScreenChangeKind.AmbientOff, 2), new ScreenChange(2, ScreenChangeKind.Fade, 1, 0)));
        screen.Update(t += 1);
        Assert.False(FlickerOff(screen));
        screen.Show(new[] { "Six." }, "Last.", Array.Empty<string>(), 5, t, null, View(1, true));
        Assert.True(FlickerOff(screen));
        screen.Update(t += 21);
        Assert.True(ColorsAre(screen, colors, 1));

        // A view never shown here (two in one frame, the client showing the last): the next lines start from the
        // screen as it has it, the colours at once, the flicker off.
        screen = Opened("comandr", View(1, false), out _);
        screen.Show(new[] { "Do you have them?" }, "", new[] { "Transfer them." }, 1, 0, null, View(1, false));
        for (t = 0; t < 840; t += 21)
            screen.Update(t);
        Assert.False(ColorsAre(screen, colors, 0));
        screen.Show(new[] { "Who are you?" }, "We are the Vindicator.", new[] { "Bye." }, 3, t, null, View(0, true));
        screen.Update(t += 21);
        Assert.True(FlickerOff(screen));
        Assert.True(ColorsAre(screen, colors, 0));
        for (int k = 0; k < 100; ++k)
            screen.Update(t += 21);
        Assert.True(ColorsAre(screen, colors, 0));
        // The talking too: a view starting with it off stops it.
        screen.Show(new[] { "Quiet." }, "Hush.", new[] { "Bye." }, 4, t, null, View(0, true) with { Talking = false });
        Assert.NotEqual(0, (int)(screen.Window.Animation.TalkFlags & CommAnimationFlags.PauseTalking));

        // The allied commander: his mouth stops as the scientist's report starts, and moves again with the line after.
        var starbase = Opened("starbas", null, out _);
        var talking = starbase.Window.Animation;
        starbase.Show(new[] { "Devices.", "About the beast.", "The scientist's report.", "The rest." }, "", new[] { "Bye" }, 1, 0, null,
            new TalkScreen(0, Scripts["starbas"].Description.Ambient.Select(a => a.Flags).ToList(), true,
                new[] { new ScreenChange(2, ScreenChangeKind.TalkingOff), new ScreenChange(3, ScreenChangeKind.TalkingOn) }));
        t = 0;
        starbase.Update(t);
        for (int k = 0; k < 10; ++k)
            starbase.Update(t += 21);
        Assert.True(talking.Running);
        while (Encoding.UTF8.GetString(starbase.Draw(-1).OverTexts[0]) != "The scientist's report.")
            starbase.Update(t += 21);
        Assert.NotEqual(0, (int)(talking.TalkFlags & CommAnimationFlags.PauseTalking));
        for (int k = 0; k < 20 && starbase.Talking; ++k)
            starbase.Update(t += 21);
        Assert.Equal("The scientist's report.", Encoding.UTF8.GetString(starbase.Draw(-1).OverTexts[0]));
        Assert.False(talking.Running);   // still through the report
        while (Encoding.UTF8.GetString(starbase.Draw(-1).OverTexts[0]) != "The rest.")
            starbase.Update(t += 21);
        starbase.Update(t += 21);
        Assert.True(talking.Running);
        Assert.Equal(0, (int)(talking.TalkFlags & CommAnimationFlags.PauseTalking));
    }

    [ContentFact]
    public void TheTalkingPetsStrobePlaysOnceEachTimeItIsStartedAndAScreenOpenedAfterItDoesntPlayItAgain()
    {
        // Differences.md row 83, protocol 32. The Pet's mind-control strobe is a one-shot ambient animation (a
        // colour animation, off in the description): started by the script as its line ends, it plays once and
        // turns itself off (commanim.c's ProcessCommAnimations), and a second start plays it again. Every view's
        // screen has it off, so new lines neither stop one that's playing nor start it, and a screen opened
        // afresh after one doesn't play it.
        int strobe = Core.Comm.Scripts.TalkingPetScript.StrobeAnimation;
        var flags = Scripts["talkpet"].Description.Ambient.Select(a => a.Flags).ToList();
        Assert.NotEqual(0, (int)(flags[strobe] & CommAnimationFlags.Disabled));
        TalkScreen View(params ScreenChange[] changes) => new(0, flags, true, changes);
        var start = new ScreenChange(1, ScreenChangeKind.AmbientStart, strobe);
        bool On(CommScreen s) => (s.Window.Animation.AmbientFlags[strobe] & CommAnimationFlags.Disabled) == 0;
        // The strobe's length as the description has it: 30 colour maps, 42 ticks each (ONE_SECOND * 3 / 2).
        var d = Scripts["talkpet"].Description.Ambient[strobe];
        Assert.Equal((30, 42, 0), (d.NumFrames, d.BaseFrameRate, d.RandomFrameRate));
        // How long the strobe stays on from now, the screen stepped a frame at a time (0 if it isn't on).
        long t = 0;
        int Plays(CommScreen s, Action<int>? each = null)
        {
            int frames = 0;
            for (int k = 0; k < 400 && On(s); ++k, ++frames)
            {
                s.Update(t += 21);
                each?.Invoke(k);
            }
            Assert.False(On(s));
            return frames;
        }

        var screen = Opened("talkpet", View(), out var colors);
        Assert.False(On(screen));
        screen.Show(new[] { "Don't kill me." }, "I must.", new[] { "One.", "Two.", "Three." }, 2, t, null, View(start));
        screen.Update(t);
        Assert.True(screen.Talking);
        // While its line plays: not yet.
        while (screen.Talking)
        {
            Assert.False(On(screen));
            screen.Update(t += 21);
        }
        // The line over: the strobe on, the colours flashing; then off by itself, a little over two seconds on.
        Assert.True(On(screen));
        int flashed = 0;
        int first = Plays(screen, _ => flashed += ColorsAre(screen, colors, 0) ? 0 : 1);
        Assert.True(flashed > 10, $"{flashed} frames in other colours");
        // 30 maps, each shown 42 ticks and the alarm's one more (commanim.c: Alarm = FrameRate + 1), so three of
        // the screen's 21-tick frames a map, as UQM's at its 40 frames a second.
        Assert.InRange(first, 85, 91);
        for (int k = 0; k < 100; ++k)
            screen.Update(t += 21);
        Assert.False(On(screen));   // once
        // Played again by the replier (Left): the script has run, the strobe isn't started again.
        screen.Page(TalkPage.Replay);
        while (screen.Talking)
            screen.Update(t += 21);
        for (int k = 0; k < 10; ++k)
            screen.Update(t += 21);
        Assert.False(On(screen));

        // The next view's start (the second strobe, the host's flags unchanged): it plays again, as long.
        screen.Show(new[] { "Good human." }, "Yes, flowers.", new[] { "I wish to go now." }, 3, t, null, View(start));
        Assert.False(On(screen));
        while (screen.Talking)
            screen.Update(t += 21);
        Assert.True(On(screen));
        // New lines come while it plays (the reply chosen at once), their screen with it off as every view's: it
        // plays on to its end, and isn't started again by them.
        int second = Plays(screen, k =>
        {
            if (k == 10)
                screen.Show(new[] { "An excellent idea." }, "I wish to go now.", Array.Empty<string>(), 4, t, null, View());
        });
        Assert.Equal(first, second);
        while (screen.Talking)
            screen.Update(t += 21);
        for (int k = 0; k < 100; ++k)
            screen.Update(t += 21);
        Assert.False(On(screen));

        // Skipped (Cancel while the line plays): the start is made at once.
        screen.Show(new[] { "Don't kill me." }, "I must.", new[] { "One." }, 5, t, null, View(start));
        screen.Update(t += 21);
        Assert.False(On(screen));
        screen.Page(TalkPage.Skip);
        Assert.True(On(screen));
        Assert.InRange(Plays(screen), first - 1, first + 1);   // (started within the skip's own frame)

        // A screen opened afresh after a strobe (a player joining or coming back): the view it opens in has the
        // strobe off and no start, and it never plays.
        t = 0;
        var fresh = Opened("talkpet", View(), out _);
        fresh.Show(new[] { "An excellent idea." }, "I wish to go now.", Array.Empty<string>(), 4, t, null, View());
        for (int k = 0; k < 300; ++k)
        {
            fresh.Update(t += 21);
            Assert.False(On(fresh));
        }
        // One opened in the view that starts it plays it with the lines, as every screen showing them does.
        t = 0;
        fresh = Opened("talkpet", View(start), out _);
        fresh.Show(new[] { "Good human." }, "Yes, flowers.", new[] { "I wish to go now." }, 3, t, null, View(start));
        fresh.Update(t);
        Assert.False(On(fresh));
        while (fresh.Talking)
            fresh.Update(t += 21);
        Assert.True(On(fresh));
        Assert.InRange(Plays(fresh), first - 1, first + 1);
    }

    [ContentFact]
    public void ANewViewsScreenChangesNothingTheShownViewsHaveMade()
    {
        // The new lines' screen is matched only where the views shown here left it otherwise: on the normal path,
        // nothing is made again or undone.
        var flags = Scripts["comandr"].Description.Ambient.Select((a, i) => i == 2 ? a.Flags | CommAnimationFlags.Disabled : a.Flags).ToList();   // the flicker off
        // (a) A fade still going as the next view's lines come (its screen at the colours it goes to) goes on.
        var screen = Opened("comandr", new TalkScreen(1, flags, true, Array.Empty<ScreenChange>()), out var colors);
        screen.Show(new[] { "Fading." }, "", new[] { "Next." }, 1, 0, null,
            new TalkScreen(1, flags, true, new[] { new ScreenChange(0, ScreenChangeKind.Fade, 0, 840) }));
        long t;
        for (t = 0; t < 420; t += 21)
            screen.Update(t);
        screen.Show(new[] { "Next lines." }, "Next.", new[] { "Bye." }, 2, t, null, new TalkScreen(0, flags, true, Array.Empty<ScreenChange>()));
        screen.Update(t += 21);
        Assert.False(ColorsAre(screen, colors, 1));
        Assert.False(ColorsAre(screen, colors, 0));   // halfway, still fading
        for (int k = 0; k < 30; ++k)
            screen.Update(t += 21);
        Assert.True(ColorsAre(screen, colors, 0));

        // (b) An ambient animation the client turned off itself (the allied commander's still frame, drawn whole and
        // then disabled) stays off, though the host's flags have it on.
        var d = Scripts["starbas"].Description;
        int still = d.Ambient.ToList().FindIndex(a => (a.Flags & (CommAnimationFlags.Random | CommAnimationFlags.Circular | CommAnimationFlags.YoYo)) == 0);
        Assert.True(still >= 0);
        var hostFlags = d.Ambient.Select(a => a.Flags).ToList();
        var starbase = Opened("starbas", new TalkScreen(0, hostFlags, true, Array.Empty<ScreenChange>()), out _);
        starbase.Show(new[] { "Hello." }, "", new[] { "Bye." }, 1, 0, null, new TalkScreen(0, hostFlags, true, Array.Empty<ScreenChange>()));
        starbase.Update(0);
        Assert.NotEqual(0, (int)(starbase.Window.Animation.AmbientFlags[still] & CommAnimationFlags.Disabled));
        starbase.Show(new[] { "Again." }, "Bye.", new[] { "Bye." }, 2, 21, null, new TalkScreen(0, hostFlags, true, Array.Empty<ScreenChange>()));
        Assert.NotEqual(0, (int)(starbase.Window.Animation.AmbientFlags[still] & CommAnimationFlags.Disabled));   // not turned on
        starbase.Update(21);
        Assert.NotEqual(0, (int)(starbase.Window.Animation.AmbientFlags[still] & CommAnimationFlags.Disabled));

        // (c) A view never shown here turned the talking on again: the next lines' mouth moves.
        starbase = Opened("starbas", new TalkScreen(0, hostFlags, false, Array.Empty<ScreenChange>()), out _);
        var talking = starbase.Window.Animation;
        starbase.Show(new[] { "Quiet." }, "", new[] { "Go on." }, 1, 0, null, new TalkScreen(0, hostFlags, false, Array.Empty<ScreenChange>()));
        for (t = 0; t < 210; t += 21)
            starbase.Update(t);
        Assert.False(talking.Running);
        starbase.Show(new[] { "Talking again.", "And more." }, "Go on.", new[] { "Bye." }, 3, t, null,
            new TalkScreen(0, hostFlags, true, Array.Empty<ScreenChange>()));
        for (int k = 0; k < 10; ++k)
            starbase.Update(t += 21);
        Assert.True(starbase.Talking);
        Assert.True(talking.Running);
    }

    // The subtitle's x as drawn (each line of it centred there, in white traced in black).
    private static int ScreenTextX(CommScreen screen)
    {
        var over = screen.Draw(-1).Over;
        Assert.NotEmpty(over);
        Assert.All(over, d => Assert.Equal((new Rgb(255, 255, 255), new Rgb(0, 0, 0)), (d.Color, d.Trace)));
        return Assert.Single(over.Select(d => d.X).Distinct());
    }

    private const string First = "Greetings, Captain. This line is long enough to wrap and to take a while to say.";
    private const string Second = "Another line.\nIts second page.";

    [ContentFact]
    public void TheLinesPlayPageByPageThenTheRepliesShow()
    {
        var screen = Starbase(out var race);
        screen.Show(new[] { First, Second }, "", new[] { "Hello.", "Goodbye." }, 1, 0);
        Assert.True(screen.Talking);
        var pages = new[] { First, Second }.SelectMany(l => CommText.SplitPages(Encoding.UTF8.GetBytes(l))).ToList();
        Assert.True(pages.Count >= 3);
        long t = 0;
        foreach (var (page, millis) in pages)
        {
            screen.Update(t);
            Assert.True(screen.Talking);
            Assert.True(screen.Window.Animation.Running);
            var drawn = screen.Draw(0);
            Assert.All(drawn.Over, o => Assert.Equal(CommTextKind.Traced, o.Kind));
            // The page's lines, one after another, are the page.
            var text = drawn.Over.SelectMany(o => CommScreen.TextOf(o, drawn.OverTexts)).ToArray();
            Assert.Equal(Encoding.UTF8.GetString(page).Replace("\n", "").Replace(" ", ""),
                Encoding.UTF8.GetString(text).Replace(" ", ""));
            // While the lines play, the replies wait: the feedback window, empty.
            Assert.Single(drawn.Below);
            t += millis * 840 / 1000;
        }
        Assert.True(screen.SliderOffset > 0);
        screen.Update(t);
        Assert.False(screen.Talking);
        Assert.Equal(0, screen.SliderOffset);
        var replies = screen.Draw(1);
        Assert.Empty(replies.Over);
        var lit = replies.Below.Where(b => b.Kind == CommTextKind.Text && b.Source == 1).ToList();
        Assert.All(lit, b => Assert.Equal(CommText.PlayerHighlight, b.Color));
        Assert.Equal("Goodbye.", Encoding.UTF8.GetString(CommScreen.TextOf(lit[0], replies.BelowTexts)));
        // A watcher: none lit.
        Assert.DoesNotContain(screen.Draw(-1).Below, b => b.Color == CommText.PlayerHighlight);
        // The talking stops (through the animation's own steps).
        for (long k = 1; k < 200; ++k)
            screen.Update(t + 21 * k);
        Assert.False(screen.Window.Animation.Running);
        // The same talk again changes nothing.
        screen.Show(new[] { First, Second }, "", new[] { "Hello.", "Goodbye." }, 1, t + 5000);
        Assert.False(screen.Talking);
    }

    [ContentFact]
    public void UqmsKeysSkipReplayAndSummarise()
    {
        var screen = Starbase(out _);
        screen.Show(new[] { First, Second }, "Hello.", new[] { "Hello.", "Goodbye." }, 2, 0);
        screen.Update(10);
        // The reply said shows under the lines.
        var fb = screen.Draw(0);
        Assert.Contains(fb.Below, b => b.Source == -3 && b.Color == CommText.ResponseIntro);
        Assert.Contains(fb.Below, b => b.Source == 0 && b.Color == CommText.Feedback);
        // While they play: the replies' keys do nothing, Cancel ends them.
        Assert.Equal(CommKeyResult.Ignored, screen.Press(MenuKey.Select));
        Assert.Equal(CommKeyResult.Used, screen.Press(MenuKey.Cancel));
        Assert.False(screen.Talking);
        // At the replies: up, down and select are the session's; Left plays the lines again.
        Assert.Equal(CommKeyResult.Replies, screen.Press(MenuKey.Down));
        Assert.Equal(CommKeyResult.Used, screen.Press(MenuKey.Left));
        Assert.True(screen.Talking);
        screen.Press(MenuKey.Cancel);
        // Cancel: the summary, turned a page at a time and closed after the last.
        Assert.Equal(CommKeyResult.Used, screen.Press(MenuKey.Cancel));
        Assert.True(screen.SummaryOpen);
        var summary = screen.Draw(0);
        Assert.Equal(CommText.HistoryBackground, summary.Over[0].Color);
        Assert.Equal(CommKeyResult.Ignored, screen.Press(MenuKey.Up));
        int pages = 0;
        while (screen.SummaryOpen)
        {
            Assert.Equal(CommKeyResult.Used, screen.Press(MenuKey.Select));
            ++pages;
        }
        Assert.Equal(CommText.Summary(TestContent.With(p => UqmFont.Load(p, "font.tiny")),
            new[] { First, Second }.Select(Encoding.UTF8.GetBytes).ToList()).Count, pages);
        // New lines (a new round) play from their start.
        screen.Show(new[] { Second }, "Goodbye.", new[] { "Hello." }, 3, 900);
        Assert.True(screen.Talking);
    }

    [ContentFact]
    public void TheRepliesScrollAsUqmsDo()
    {
        var screen = Starbase(out _);
        var replies = Enumerable.Range(0, 8).Select(i => $"Reply {i}: and a long tail to it, long enough that it takes two lines to say it all.").ToList();
        screen.Show(Array.Empty<string>(), "", replies, 1, 0);
        screen.Update(0);
        Assert.False(screen.Talking);
        var player = TestContent.With(p => UqmFont.Load(p, "font.player"));
        var bytes = replies.Select(Encoding.UTF8.GetBytes).ToList();
        int top = 0;
        for (int cursor = 0; cursor < 8; ++cursor)
        {
            // PlayerResponseInput: a reply ending past the foot comes to the top.
            if (CommText.ReplyEnd(player, bytes, top, cursor) > CommText.Height)
                top = cursor;
            Assert.Equal(top, screen.TopFor(cursor));
        }
        Assert.True(top > 0);
        Assert.Contains(screen.Draw(7).Below, b => b.Kind == CommTextKind.Stamp && b.Count == 6);   // the up arrow
        Assert.Equal(0, screen.TopFor(0));   // round to the first: from the start again
    }

    [ContentFact]
    public void AWatcherFollowsTheRepliersPagingEvenAMomentBehind()
    {
        // The replier's keys say what they did to the lines.
        var replier = Starbase(out _);
        replier.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 0);
        replier.Update(10);
        Assert.Equal(CommKeyResult.Used, replier.Press(MenuKey.Cancel, out var paged));
        Assert.Equal(TalkPage.Skip, paged);
        Assert.Equal(CommKeyResult.Used, replier.Press(MenuKey.Cancel, out paged));
        Assert.Equal(TalkPage.Summary, paged);
        Assert.Equal(CommKeyResult.Used, replier.Press(MenuKey.Right, out paged));
        Assert.Equal(TalkPage.SummaryTurn, paged);
        while (replier.SummaryOpen)
            replier.Page(TalkPage.SummaryTurn);
        Assert.Equal(CommKeyResult.Replies, replier.Press(MenuKey.Down, out paged));   // the replies' key: no paging
        Assert.Null(paged);
        // A watcher still playing the lines when the summary opened: they end, and the summary opens.
        var watcher = Starbase(out _);
        watcher.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 0);
        watcher.Update(10);
        watcher.Page(TalkPage.Summary);
        Assert.False(watcher.Talking);
        Assert.True(watcher.SummaryOpen);
        // Turned past its last page: closed; a turn with none open, or a skip with the lines over, does nothing.
        while (watcher.SummaryOpen)
            watcher.Page(TalkPage.SummaryTurn);
        watcher.Page(TalkPage.SummaryTurn);
        watcher.Page(TalkPage.Skip);
        Assert.False(watcher.Talking || watcher.SummaryOpen);
        // Played again: from the start.
        watcher.Page(TalkPage.Replay);
        Assert.True(watcher.Talking);
        watcher.Page(TalkPage.Skip);
        Assert.False(watcher.Talking);
    }

    [ContentFact]
    public void AScreenFollowsThePagingItHasntYetAndAWatcherBecomingTheReplierCatchesUp()
    {
        var paging = new List<TalkPage>();
        // Opened mid-talk (a player joining): the replier is hearing the lines again, from now.
        var joiner = Starbase(out _);
        joiner.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 50_000);
        paging.AddRange(new[] { TalkPage.Skip, TalkPage.Replay });
        joiner.Follow("a", paging);
        joiner.Update(50_010);
        Assert.True(joiner.Talking);
        // Followed once: the same paging again does nothing more.
        joiner.Follow("a", paging);
        joiner.Update(50_020);
        Assert.True(joiner.Talking);
        paging.Add(TalkPage.Skip);
        joiner.Follow("a", paging);
        Assert.False(joiner.Talking);
        // The replier's own paging is on their screen already: the host's echo of it, a moment behind, does nothing.
        var replier = Starbase(out _);
        replier.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 0);
        replier.Follow("a", Array.Empty<TalkPage>());
        replier.Update(10);
        replier.Press(MenuKey.Left, out _);     // played again
        replier.Press(MenuKey.Cancel, out _);   // skipped
        replier.Follow("a", new[] { TalkPage.Replay });
        Assert.False(replier.Talking);
        replier.Follow("a", new[] { TalkPage.Replay, TalkPage.Skip });
        Assert.False(replier.Talking);
        // The replier's screen opened afresh, even before the echo of their page came: followed when it does.
        var reopened = Starbase(out _);
        reopened.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 0);
        reopened.Follow("a", Array.Empty<TalkPage>());
        reopened.Update(10);
        reopened.Follow("a", new[] { TalkPage.Skip });
        Assert.False(reopened.Talking);
        // A watcher made the replier by a view that also brings the last replier's skip: followed first.
        var heir = Starbase(out _);
        heir.Show(new[] { First, Second }, "", new[] { "Hello." }, 1, 0);
        heir.Follow("a", Array.Empty<TalkPage>());
        heir.Update(10);
        heir.Follow("a", new[] { TalkPage.Skip });
        Assert.False(heir.Talking);
        // New lines: followed from their first page.
        heir.Show(new[] { Second }, "Hello.", new[] { "Bye." }, 2, 100);
        heir.Follow("b", new[] { TalkPage.Skip });
        Assert.False(heir.Talking);
    }
}
