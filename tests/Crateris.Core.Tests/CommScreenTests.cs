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
