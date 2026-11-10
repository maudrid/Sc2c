// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>
/// The session logic the Godot node used to hold: scripted input, the host's
/// side (guests, input, start options) and the local player's (keys, orbit
/// menus, the screen, the status), run here without Godot.
/// </summary>
[Trait("Topic", "world")]
public class SessionTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly SystemCatalog Systems = SystemCatalog.Load(DataDir);
    private const int SolNumber = 82;

    private static HostSession NewHost(string id = "host") =>
        HostSession.New(new GameSettings(), NewGameData.Load(DataDir, Systems.Map), id, "Host", Systems, TestContent.Shapes);

    private static PlayerSession Player(string id, InputScript? script = null) =>
        new(Systems, null, id, TestContent.Shapes.HyperFlagship.Count, script);

    [Fact]
    public void AScriptPlaysItsStepsAndPressesMenuKeysOnce()
    {
        var script = InputScript.Parse("thrust:2,left+thrust:1,down:2,select:1");
        var got = new List<(ShipInput, MenuKey?)>();
        for (int i = 0; i < 8; ++i)
            got.Add((script.Next(out var key), key));
        Assert.Equal(new (ShipInput, MenuKey?)[]
        {
            (ShipInput.Thrust, null), (ShipInput.Thrust, null), (ShipInput.Left | ShipInput.Thrust, null),
            (ShipInput.None, MenuKey.Down), (ShipInput.None, null), (ShipInput.None, MenuKey.Select),
            (ShipInput.None, null), (ShipInput.None, null),
        }, got);
        Assert.True(script.Done);
        Assert.Throws<ArgumentException>(() => InputScript.Parse("warp:3"));
        Assert.Throws<ArgumentException>(() => InputScript.Parse("thrust"));
        // A step of no frames would be skipped before its key is pressed: refused.
        Assert.Throws<ArgumentException>(() => InputScript.Parse("none:5,select:0"));
        // The giving screen's steps: give opens or closes it, page up and down are menu keys.
        script = InputScript.Parse("give:2,pageup:1,pagedown+give:1");
        var steps = new List<(ShipInput, MenuKey?, bool)>();
        for (int i = 0; i < 5; ++i)
            steps.Add((script.Next(out var key, out bool give), key, give));
        Assert.Equal(new (ShipInput, MenuKey?, bool)[]
        {
            (ShipInput.None, null, true), (ShipInput.None, null, false), (ShipInput.None, MenuKey.PageUp, false),
            (ShipInput.None, MenuKey.PageDown, true), (ShipInput.None, null, false),
        }, steps);
    }

    [ContentFact]
    public void TheHostTakesGuestsByPeerAndRefusesAnotherVersion()
    {
        var host = NewHost();
        var logs = new List<string>();
        host.Logged += logs.Add;
        var refused = host.Hello(7, "old", "Old", Protocol.Version - 1);
        Assert.False(Protocol.Accepted(refused.Result));
        Assert.Contains("Version mismatch", refused.Message);
        Assert.Equal(0, host.Guests);

        var welcome = host.Hello(5, "guest", "Guest", Protocol.Version);
        Assert.True(Protocol.Accepted(welcome.Result));
        Assert.Equal(host.World.Settings.InputDelayFrames, welcome.InputDelayFrames);
        Assert.Equal(1, host.Guests);
        // Input from the peer goes to its player; from an unknown peer, nowhere.
        host.GuestInput(5, host.World.Frame + 1, (int)ShipInput.Thrust, host.World.Frame);
        host.GuestInput(99, host.World.Frame + 1, (int)ShipInput.Thrust, host.World.Frame);
        host.Step(ShipInput.None);
        // A second hello on the same connection changes nothing.
        var again = host.Hello(5, "other", "Other", Protocol.Version);
        Assert.Equal(JoinResult.AlreadyPresent, again.Result);
        Assert.Equal("guest", again.PlayerId);                        // the player already on it
        Assert.Equal(Protocol.WelcomeAction.Ignore, Protocol.OnWelcome(again, alreadyIn: true));   // the guest stays
        Assert.Equal(Protocol.WelcomeAction.Leave, Protocol.OnWelcome(refused, alreadyIn: false));
        Assert.Equal(Protocol.WelcomeAction.Join, Protocol.OnWelcome(welcome, alreadyIn: false));
        Assert.True(host.InGame(5));
        Assert.Null(host.World.Find("other"));
        host.GuestLeft(5);
        Assert.Equal(0, host.Guests);
        Assert.False(host.World.Find("guest")!.Present);
        Assert.Contains(logs, l => l == "guest left");
    }

    [ContentFact]
    public void StartingInHyperspaceNeedsTheHostToBeInHyperspace()
    {
        var host = NewHost();
        Assert.Equal("Starting in hyperspace at 1752:1430", host.StartAt(1752, 1430));
        Assert.Equal((1752, 1430), ((int)host.World.Host.Ship.Flight.UniverseX, (int)host.World.Host.Ship.Flight.UniverseY));
        Assert.Null(host.World.Host.Ship.Flight.StarJustLeft);

        host.StartInOrbit(SolNumber, 2, null);
        Assert.Equal(Activity.Orbit, host.World.Host.Ship.Activity);
        Assert.Throws<InvalidOperationException>(() => host.StartAt(100, 200));   // in orbit: left alone
        Assert.Equal(Activity.Orbit, host.World.Host.Ship.Activity);
        Assert.Throws<ArgumentException>(() => host.StartInOrbit(SolNumber, 40, null));
    }

    [ContentFact]
    public void ALoadedGameIsTheSavesHosts()
    {
        var host = NewHost("original");
        var loaded = HostSession.Load(host.SaveJson(), "Renamed", Systems, TestContent.Shapes);
        Assert.Equal("original", loaded.HostId);
        Assert.True(loaded.World.Host.Present);
        Assert.Equal("Renamed", loaded.World.Host.Name);
    }

    [ContentFact]
    public void InOrbitTheKeysGoToTheMenuAndNavigateHoldsTheActionKey()
    {
        var host = NewHost();
        host.StartInOrbit(SolNumber, 2, 1);   // Luna
        var player = Player("host");
        var logs = new List<string>();
        player.Logged += logs.Add;
        player.Observe(host.World.Snapshot());
        Assert.Equal(PlayerScreen.Orbit, player.Screen);
        Assert.NotNull(player.OrbitMenu);

        Assert.Equal(ShipInput.None, player.NextInput(ShipInput.Thrust));   // the keys go to the menu
        // Navigate is the fifth entry: leaving holds the action key for a few frames.
        for (int i = 0; i < 4; ++i)
            player.PressMenu(MenuKey.Down);
        player.PressMenu(MenuKey.Select);
        Assert.Equal("Leaving orbit...", player.OrbitMessage);
        Assert.Contains("Leaving orbit", logs);
        for (int i = 0; i < PlayerSession.LeaveFrames; ++i)
            Assert.Equal(ShipInput.Action, player.NextInput(ShipInput.None));
        for (int i = 0; i < 4; ++i)
            player.Observe(host.Step(ShipInput.Action));
        Assert.Equal(PlayerScreen.System, player.Screen);
        Assert.Null(player.OrbitMenu);
        Assert.Contains(logs, l => l.StartsWith("Saw host InnerSystem at frame "));
        Assert.Equal(ShipInput.Thrust, player.NextInput(ShipInput.Thrust));
    }

    [ContentFact]
    public void TheStatusSaysWhereEveryoneIs()
    {
        var host = NewHost();
        host.Hello(2, "guest", "Guest", Protocol.Version);
        var player = Player("guest");
        player.Status = "Playing.";
        player.Observe(host.Step(ShipInput.None));
        var lines = player.StatusLines();
        Assert.Equal("Attack on Crateris - stage 3a", lines[0]);
        Assert.Contains("Playing.", lines);
        Assert.Contains("You: hyperspace 175.2 : 145.0", lines);
        Assert.Contains("Crew 10, landers 1, minerals 0 / 500, bio 0, 0 RU", lines);
        Assert.Contains(lines, l => l.StartsWith("  Host (host, flagship)"));
        Assert.Contains("Arrows: turn and thrust", lines);
        player.Notify("Hello", seconds: 1);
        Assert.Contains("Hello", player.StatusLines());
        player.Advance(1.5);
        Assert.DoesNotContain("Hello", player.StatusLines());
    }

    [Fact]
    public void ALongNoticeIsSplitIntoPagesBetweenParagraphsThenLinesSentencesAndWords()
    {
        string Words(int n, string end = ".") => string.Join(" ", Enumerable.Range(0, n).Select(i => $"word{i % 10}")) + end;
        void Fit(IEnumerable<string> pages) =>
            Assert.All(pages, p => Assert.InRange(PlayerSession.NoticeLines(p), 1, PlayerSession.NoticePageLines));
        // Paragraphs share a page while they fit (however many blank lines between them; an indent kept).
        string paragraph = Words(6);   // 41 characters: two lines
        Assert.Equal(new[] { $"Heading:\n  {paragraph}\n\n{paragraph}\n\n{paragraph}", paragraph },
            PlayerSession.NoticePages($"Heading:\n  {paragraph}\n\n\n{paragraph}\n \n\n{paragraph}\n\n{paragraph}\n"));
        // One too long for a page between its lines (a line each: the commander's closing lines).
        var lines = Enumerable.Range(1, 12).Select(i => $"line {i}").ToList();
        Assert.Equal(new[] { string.Join("\n", lines.Take(9)), string.Join("\n", lines.Skip(9)) },
            PlayerSession.NoticePages(string.Join("\n", lines)));
        // A line too long for a page at its sentences' ends.
        string sentence = Words(10);   // 60: three lines
        var bySentence = PlayerSession.NoticePages(string.Join(" ", Enumerable.Repeat(sentence, 5)));
        Fit(bySentence);
        Assert.Equal(new[] { $"{sentence} {sentence} {sentence}", $"{sentence} {sentence}" }, bySentence);   // 182 characters: nine lines
        // A sentence too long for a page between its words, and a word too long for one in pieces.
        string longest = Words(60, "");
        var byWord = PlayerSession.NoticePages(longest);
        Fit(byWord);
        Assert.True(byWord.Count > 1);
        Assert.Equal(longest, string.Join(" ", byWord));
        string word = new('x', 500);
        var byPiece = PlayerSession.NoticePages(word);
        Fit(byPiece);
        Assert.Equal(word, string.Concat(byPiece));
        // Windows' line ends are lines too.
        string big = Words(25);
        Assert.Equal(new[] { big, big }, PlayerSession.NoticePages($"{big}\r\n\r\n{big}"));
        // A notice that fits is its own page, as it is; so is nothing but blank space.
        Assert.Equal(new[] { "  a\n\n\nb " }, PlayerSession.NoticePages("  a\n\n\nb "));
        string blank = new(' ', 500);
        Assert.Equal(new[] { blank }, PlayerSession.NoticePages(blank));
        // Blank space makes no page of its own.
        var afterBlank = PlayerSession.NoticePages($"  \n{new string('z', 250)} end");
        Assert.DoesNotContain(afterBlank, p => p.Trim() == "");
        Assert.Equal($"{new string('z', 250)}end", string.Concat(afterBlank).Replace(" ", ""));
    }

    [ContentFact]
    public void ANoticeThatFitsOnlySqueezedIsShownSqueezed()
    {
        // Too many lines as it is, one page with its blank lines squeezed: that page is shown.
        var player = Player("guest");
        string text = "Heading:\n\n\n\n\n\n\n\nbody one\nbody two\nbody three";
        Assert.True(PlayerSession.NoticeLines(text) > PlayerSession.NoticePageLines);
        player.Notify(text);
        Assert.Equal("Heading:\n\nbody one\nbody two\nbody three", player.Notice);
        Assert.Equal(PlayerSession.NoticePages(text), new[] { player.Notice });
    }

    [ContentFact]
    public void ALongNoticeIsShownAPageAtATimeForAsLongAsItTakesToRead()
    {
        var player = Player("guest");
        string text = string.Join("\n\n", Enumerable.Range(0, 3).Select(i => new string((char)('a' + i), 150)));
        var pages = PlayerSession.NoticePages(text);
        Assert.Equal(3, pages.Count);
        void ShownInTurn(double each)
        {
            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.Equal($"{pages[i]}\n({i + 1}/3)", player.Notice);
                Assert.Contains(player.Notice, player.StatusLines());
                player.Advance(each - 0.5);
                Assert.Equal($"{pages[i]}\n({i + 1}/3)", player.Notice);   // still being read
                player.Advance(0.6);
            }
            Assert.Equal("", player.Notice);
        }
        player.Notify(text, seconds: 1);
        ShownInTurn(150 / PlayerSession.ReadingPace);
        // Asked to show for longer: each page its share.
        player.Notify(text, seconds: 60);
        ShownInTurn(20);
        // A new notice puts the rest of a long one away.
        player.Notify(text);
        player.Notify("Hello");
        Assert.Equal("Hello", player.Notice);
        player.Advance(PlayerSession.NoticeSeconds + 0.1);
        Assert.Equal("", player.Notice);
    }

    [ContentFact]
    public void AFirstTimeGuestUndocksOnceAfterItsScene()
    {
        var host = NewHost();
        host.Hello(3, "guest", "Guest", Protocol.Version);
        var player = Player("guest");
        int told = 0;
        for (int i = 0; i < PlayerSession.UndockFrames + 5; ++i)
        {
            player.Observe(host.Step(ShipInput.None));
            if (player.UndockTick())
            {
                told++;
                host.GuestUndockShown(3);
            }
        }
        Assert.Equal(1, told);
        Assert.True(player.UndockDone);
        Assert.False(player.Undocking);
    }

    [ContentFact]
    public void TheScreenStaysOnHyperspaceWhileTheEntryPlays()
    {
        var host = NewHost();
        host.StartAt(1752, 1430);   // just south of Sol
        var player = Player("host");
        int frames = 0;
        while (host.World.Host.Ship.Activity == Activity.Hyperspace && frames++ < 200)
            player.Observe(host.Step(ShipInput.Thrust));
        Assert.Equal(Activity.OuterSystem, host.World.Host.Ship.Activity);
        var entry = player.Me!.Entry!;
        Assert.Equal(host.World.Frame, entry.ContactFrame);
        int shown = 0;
        while (player.Screen == PlayerScreen.Hyperspace && shown < 100)
        {
            shown++;
            player.Observe(host.Step(ShipInput.Thrust));
        }
        // The contact frame, then one frame for each step of the entry.
        Assert.Equal(entry.Steps + 1, shown);
        Assert.Equal(StarEntry.Turns(entry.Facing, TestContent.Shapes.HyperFlagship.Count), entry.Steps);
        Assert.Equal(PlayerScreen.System, player.Screen);
        Assert.Null(player.Me!.Entry);
    }

    [ContentFact]
    public void WhileYourEntryPlaysNothingIsSentAndTheStatusSaysSo()
    {
        var host = NewHost();
        host.StartAt(1752, 1430);
        var player = Player("host");
        for (int i = 0; i < 200 && host.World.Host.Ship.Activity == Activity.Hyperspace; ++i)
            player.Observe(host.Step(player.NextInput(ShipInput.Thrust)));
        Assert.True(player.Entering);
        Assert.Equal(ShipInput.None, player.NextInput(ShipInput.Thrust | ShipInput.Left));
        var lines = player.StatusLines();
        Assert.Contains(lines, l => l.StartsWith("You: entering star 82"));
        Assert.DoesNotContain("Arrows: turn and thrust", lines);
        // Held keys during the entry don't carry into the system after it.
        while (player.Entering)
            player.Observe(host.Step(player.NextInput(ShipInput.Thrust | ShipInput.Left)));
        var arrival = host.World.Host.Ship.Ip!;
        var (location, facing) = (arrival.Location, arrival.Facing);
        for (int i = 0; i < host.World.Settings.InputDelayFrames; ++i)
            player.Observe(host.Step(player.NextInput(ShipInput.None)));
        Assert.Equal((location, facing), (arrival.Location, arrival.Facing));
    }

    [ContentFact]
    public void AGuestJoiningDuringTheHostsEntryGoesInWithIt()
    {
        var host = NewHost();
        host.StartAt(1752, 1430);
        for (int i = 0; i < 200 && host.World.Host.Ship.Activity == Activity.Hyperspace; ++i)
            host.Step(ShipInput.Thrust);
        var entry = host.World.Host.Ship.Entry!;
        host.Step(ShipInput.None);
        host.Hello(4, "guest", "Guest", Protocol.Version);
        var guest = host.World.Find("guest")!.Ship;
        Assert.Equal(entry with { Following = "host" }, guest.Entry);
        // It rides in with the flagship: entering, but drawn only once, and
        // its undocking scene waits for the system.
        var player = Player("guest");
        player.Observe(host.World.Snapshot());
        Assert.True(player.Entering);
        Assert.Equal(new[] { "host" }, player.Entries.Drawn.Select(e => e.Ship.Id));
        Assert.False(player.UndockTick());
        Assert.False(player.Undocking);
        var at = guest.Ip!.Location;
        host.Step(ShipInput.None);
        Assert.Equal(at, guest.Ip!.Location);   // waiting with the flagship
        while (host.World.Host.Ship.Entry != null)
            host.Step(ShipInput.None);
        Assert.Null(guest.Entry);               // both arrive together
    }
}
