// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using System.Text.RegularExpressions;
using Crateris.Core.Comm;
using Crateris.Core.Starbase;
using Crateris.Core.Content;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>Which screen the local player sees.</summary>
public enum PlayerScreen
{
    Hyperspace,
    System,
    Orbit,
    Planetside,

    /// <summary>The communication screen: a talk this player is in, or watches in a story pause (<see cref="PlayerSession.TalkShown"/>).</summary>
    Comm,

    /// <summary>Docked at the allied starbase: its menu, outfitting or the shipyard (<see cref="PlayerSession.Starbase"/>).</summary>
    Starbase,
}

/// <summary>
/// The local player's side of a game, the same for the host and a guest,
/// without Godot: the latest snapshot, what the player's keys (or a test
/// script) send, the orbit screen's menus for the current visit, the
/// undocking scene of a first-time guest, notices, which screen to show,
/// and the status text. The Godot layer feeds it keys, time and snapshots,
/// and draws what it says.
/// </summary>
public sealed class PlayerSession
{
    /// <summary>How long a notice shows, in seconds.</summary>
    public const double NoticeSeconds = 6;

    /// <summary>How many frames the action key is held after Navigate is chosen, so a lost packet can't lose it.</summary>
    public const int LeaveFrames = 4;

    /// <summary>The placeholder undocking scene's length (a message), in world frames.</summary>
    public const int UndockFrames = 2 * GameClock.FramesPerSecond;

    private readonly SystemCatalog _systems;
    private readonly Names? _names;
    private readonly InputScript? _script;
    private readonly Dictionary<string, Activity> _seenActivity = new();
    private int _leaveFrames;
    private (int X, int Y)? _landingSite;
    private readonly Queue<int> _discards = new();
    private bool _landingAsked;   // sent, not yet down or refused: the menu takes no keys
    private LanderPhase? _seenLanderPhase;
    private bool _wasSweeping;
    private int _undockFrames = -1;
    private double _time;
    private List<(string Text, double Until)> _notice = new();

    /// <param name="names">Star and world names for the status (null: by number).</param>
    /// <param name="entryFrameCount">The hyperspace flagship sheet's frame count, for star entries.</param>
    public PlayerSession(SystemCatalog systems, Names? names, string playerId, int entryFrameCount, InputScript? script = null)
    {
        _systems = systems;
        _names = names;
        PlayerId = playerId;
        _script = script;
        Entries = new StarEntryTracker(systems.Map, entryFrameCount);
    }

    private string _playerId = "";

    /// <summary>The local player's id (a host that loads a save takes the save's).</summary>
    public string PlayerId
    {
        get => _playerId;
        set
        {
            _playerId = value;
            Me = Snapshot?.Ships.FirstOrDefault(s => s.Id == value);
        }
    }

    public WorldSnapshot? Snapshot { get; private set; }

    /// <summary>The local player's ship in the snapshot (found once for each snapshot).</summary>
    public ShipSnapshot? Me { get; private set; }

    /// <summary>Whether the local player's entry into a star is playing.</summary>
    public bool Entering => Me != null && Entries.Playing(Me.Id);

    /// <summary>The entries into stars playing in the snapshot.</summary>
    public StarEntryTracker Entries { get; }

    /// <summary>The orbit screen's menus for the current visit to a world, or null when not in orbit.</summary>
    public OrbitMenu? OrbitMenu { get; private set; }

    /// <summary>What the orbit screen last said (an item that comes later, a refusal, leaving).</summary>
    public string OrbitMessage { get; private set; } = "";

    /// <summary>Whether input comes from a script rather than keys.</summary>
    public bool Scripted => _script != null;

    /// <summary>The lasting status line (hosting, connected, an error), set by the Godot layer.</summary>
    public string Status { get; set; } = "";

    /// <summary>The notice being shown (a long one's page), or "" once it has run out.</summary>
    public string Notice => _notice.FirstOrDefault(p => _time < p.Until).Text ?? "";

    /// <summary>Whether a first-time guest's undocking scene is showing.</summary>
    public bool Undocking => _undockFrames >= 0 && !UndockDone;

    public bool UndockDone { get; private set; }

    /// <summary>What happened, for the log (read by the network test).</summary>
    public event Action<string>? Logged;

    private void Log(string message) => Logged?.Invoke(message);

    /// <summary>
    /// Shows a one-off message under the status line for a while (and logs
    /// it). One too long for a page (a lander report) is shown a page at a
    /// time (<see cref="NoticePages"/>), each numbered and up for as long as
    /// it takes to read (and for its share of <paramref name="seconds"/>, if
    /// longer), so the status panel's other lines stay on the screen.
    /// </summary>
    public void Notify(string message, double seconds = NoticeSeconds)
    {
        var pages = NoticePages(message);
        _notice = new();
        if (pages.Count == 1)
            _notice.Add((pages[0], _time + seconds));   // the message, or it squeezed to fit (its blank lines and runs of spaces)
        else
        {
            double until = _time;
            for (int i = 0; i < pages.Count; ++i)
            {
                until += Math.Max(Math.Max(NoticeSeconds, pages[i].Length / ReadingPace), seconds / pages.Count);
                _notice.Add(($"{pages[i]}\n({i + 1}/{pages.Count})", until));
            }
        }
        Log(message);
    }

    /// <summary>
    /// The lines of the status panel a page of a notice is let take, as
    /// <see cref="NoticeLines"/> reckons them (its number one more): with the
    /// panel's other lines for four players, about all the window has room
    /// for. Measured in the game, every page of the content's lander
    /// reports leaves those lines on the screen, a line to spare.
    /// </summary>
    public const int NoticePageLines = 9;

    /// <summary>The characters reckoned to a line of the status panel (about as many capitals as it holds, wrapped between words).</summary>
    public const int NoticeLineChars = 22;

    /// <summary>How fast a long notice's page is taken to be read, in characters a second.</summary>
    public const double ReadingPace = 20;

    /// <summary>
    /// The lines of the status panel a notice's text is reckoned to take,
    /// <see cref="NoticeLineChars"/> to a line: an estimate, not a bound
    /// (long words of wide letters wrap to more).
    /// </summary>
    public static int NoticeLines(string text) =>
        text.Split('\n').Sum(line => Math.Max(1, (line.Length + NoticeLineChars - 1) / NoticeLineChars));

    private static bool FitsAPage(string text) => NoticeLines(text) <= NoticePageLines;

    // How a notice too long for a page is split, in turn while a piece is still too long:
    // between paragraphs (one blank line or more; the next one's indent kept), lines,
    // sentences, words, and a word too long for a page into pieces that fit.
    private static readonly (Func<string, IEnumerable<string>> Split, string Joint)[] NoticeSplits =
    {
        (t => Regex.Split(t, @"\n(?:[ \t]*\n)+").Select(p => p.Trim('\n')).Where(p => p.Trim() != ""), "\n\n"),
        (t => t.Split('\n'), "\n"),
        (t => Regex.Split(t, @"(?<=[.!?]) +"), " "),
        (t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries), " "),
        (t => t.Chunk(NoticeLineChars * NoticePageLines).Select(c => new string(c)), ""),
    };

    /// <summary>
    /// A notice's pages: the whole text if it fits on one; otherwise its
    /// paragraphs, together while they fit, one too long for a page split
    /// between its lines, then its sentences, then its words, and a word
    /// too long for a page cut into pieces; blank space alone makes no page.
    /// </summary>
    public static IReadOnlyList<string> NoticePages(string text)
    {
        if (FitsAPage(text))
            return new[] { text };
        var pages = new List<string>();
        var page = new StringBuilder();
        void Add(string piece, string joint)
        {
            if (piece.Trim() == "")
                return;   // no page of blank space
            if (page.Length > 0 && !FitsAPage($"{page}{joint}{piece}"))
            {
                pages.Add(page.ToString());
                page.Clear();
            }
            if (page.Length > 0)
                page.Append(joint);
            page.Append(piece);
        }
        // Each piece of text, split at the level given if too long; the first piece joined as the text is.
        void Split(string part, int level, string joint)
        {
            foreach (string piece in NoticeSplits[level].Split(part))
            {
                if (FitsAPage(piece) || level == NoticeSplits.Length - 1)
                    Add(piece, joint);
                else
                    Split(piece, level + 1, joint);
                joint = NoticeSplits[level].Joint;
            }
        }
        Split(text.Replace("\r\n", "\n"), 0, "");
        if (pages.Count == 0 && page.Length == 0)
            return new[] { text };   // nothing but blank space
        if (page.Length > 0)
            pages.Add(page.ToString());
        return pages;
    }

    /// <summary>
    /// A snapshot message from the host (<see cref="SnapshotReceiver"/>):
    /// observed once decoded. A delta from a snapshot not kept here is
    /// dropped; the host's next one is from a snapshot this guest has said
    /// it has. Bad data is refused with <see cref="InvalidDataException"/>.
    /// </summary>
    public void ReceiveSnapshot(ReadOnlySpan<byte> data)
    {
        if (_snapshots.Receive(data, out bool full) is { } snapshot)
        {
            if (full)
                SnapshotsFull++;
            else
                SnapshotsDelta++;
            Observe(snapshot);
        }
        else
            SnapshotsWithoutBase++;
    }

    /// <summary>Snapshot messages received in full, as deltas, and as deltas from a snapshot not kept (dropped).</summary>
    public int SnapshotsFull { get; private set; }

    /// <inheritdoc cref="SnapshotsFull"/>
    public int SnapshotsDelta { get; private set; }

    /// <inheritdoc cref="SnapshotsFull"/>
    public int SnapshotsWithoutBase { get; private set; }

    private readonly SnapshotReceiver _snapshots = new();

    /// <summary>
    /// A snapshot arrived (or the host made one). Older ones than the one
    /// held are ignored. Logs each ship's activity when it changes, and
    /// keeps the orbit menu and star entries up to date.
    /// </summary>
    public void Observe(WorldSnapshot snapshot)
    {
        if (Snapshot != null && snapshot.Frame <= Snapshot.Frame)
            return;
        Snapshot = snapshot;
        Me = snapshot.Ships.FirstOrDefault(s => s.Id == PlayerId);
        foreach (var s in snapshot.Ships)
        {
            if (_seenActivity.TryGetValue(s.Id, out var last) && last == s.Activity)
                continue;
            _seenActivity[s.Id] = s.Activity;
            Log($"Saw {s.Id} {s.Activity} at frame {snapshot.Frame}");
        }
        Entries.Observe(snapshot);
        if (RescueOffer is { } wreck && snapshot.Ships.FirstOrDefault(s => s.Id == wreck) is not { Wrecked: true })
            RescueOffer = null;   // revived (by anyone), or gone
        UpdateOrbitMenu();
        // The Devices list follows the story the host sends while it's
        // open (another player's talk or lander can change what the group
        // carries; in UQM only using a device could, which leaves the list).
        OrbitMenu?.RefreshDevices(Devices());
        NoteLander();
        FollowTalk();
    }

    /// <summary>The local player's lander on its surface, as the last snapshot gives it; null if it isn't down.</summary>
    public LanderSnapshot? MyLander =>
        SnapshotCodec.SurfaceSeen(Snapshot, PlayerId)?.Landers.FirstOrDefault(l => l.PlayerId == PlayerId);

    // Logs the lander's landing, its crew all dying, its return with what
    // it brought, and the first sight of each other lander on its surface.
    private void NoteLander()
    {
        foreach (var other in SnapshotCodec.SurfaceSeen(Snapshot, PlayerId)?.Landers ?? Array.Empty<LanderSnapshot>())
        {
            if (other.PlayerId != PlayerId && _seenLanders.Add(other.PlayerId))
                Log($"Saw {other.PlayerId}'s lander");
        }
        var lander = MyLander;
        if (lander is not null)
            _landingAsked = false;   // it's down: the request is answered
        if (lander is { Crew: 0 } && _seenLanderCrew > 0)
            Log("The lander's crew are all dead");
        var phase = lander?.Phase;
        if (phase == _seenLanderPhase)
        {
            _seenLanderCrew = lander?.Crew ?? -1;
            return;
        }
        if (phase == LanderPhase.Ground)
            Log("Landed");
        else if (phase is null && Me?.Hold is { } hold)
            Log((_seenLanderCrew == 0 ? "Lander lost. " : "")
                + $"Back in orbit: crew {hold.Crew}, landers {hold.Landers}, minerals {hold.MineralMass}, bio {hold.BioData}");
        _seenLanderPhase = phase;
        _seenLanderCrew = lander?.Crew ?? -1;
    }

    /// <summary>The host refused the landing asked for (a race: the ship left orbit, or its lander was already down).</summary>
    public void LandingRefused(DispatchRefusal refusal)
    {
        OrbitMessage = RefusalMessage(refusal);
        _landingAsked = false;
        Log($"Landing refused: {refusal}");
    }

    private int _seenLanderCrew = -1;
    private readonly HashSet<string> _seenLanders = new();

    /// <summary>The cargo units dropped in the cargo display since last asked, by category, to send to the host.</summary>
    public IReadOnlyList<int> TakeDiscards()
    {
        var all = _discards.ToArray();
        _discards.Clear();
        return all;
    }

    /// <summary>
    /// The landing site picked in the orbit menu, to send to the host
    /// once (the host sends the lander down, or refuses); null if none is waiting.
    /// </summary>
    public (int X, int Y)? TakeLandingRequest()
    {
        var site = _landingSite;
        _landingSite = null;
        return site;
    }

    /// <summary>
    /// The ship input to send this frame, from the keys held (or the script,
    /// whose menu keys are pressed here). In orbit the keys go to the orbit
    /// menu instead. After Navigate, the action key is held a few frames.
    /// While the player's entry into a star plays, nothing is sent: the host
    /// ignores input read then anyway (by the frame it's stamped for, which
    /// covers a guest that sees the entry late), as UQM does. A script's
    /// steps still run by time.
    /// </summary>
    public ShipInput NextInput(ShipInput keys)
    {
        UpdateOrbitMenu();   // so a key on the first frame in orbit reaches the menu
        ShipInput input;
        if (_script != null)
        {
            input = _script.Next(out var press, out bool give, out bool pause);
            _pausePressed |= pause;
            if (give)
                ToggleTrade();
            if (press is MenuKey key)
                PressMenu(key);
            if (Trade is not null)
                input = ShipInput.None;   // the ship waits while the giving screen is open, as with keys
        }
        else
        {
            // After the giving screen closes, the ship waits for the keys to
            // be let go: the Esc that closed it mustn't take the lander off.
            if (_waitForRelease && keys == ShipInput.None)
                _waitForRelease = false;
            input = Me?.Activity is Activity.Orbit or Activity.Talk or Activity.Starbase || StoryPaused || Trade is not null
                    || _waitForRelease
                ? ShipInput.None : keys;   // on the surface, the lander's keys
        }
        if (Entering)
            return ShipInput.None;
        // A Navigate chosen just before a story pause, or the host's, leaves orbit after it.
        if (StoryPaused || HostPaused)
            return ShipInput.None;
        if (_leaveFrames > 0)
        {
            _leaveFrames--;
            return ShipInput.Action;
        }
        return input;
    }

    /// <summary>
    /// The talk this player is in, as the host last sent it (until the
    /// communication screen, item 11, in words); null if none.
    /// </summary>
    public TalkView? Talk { get; private set; }

    /// <summary>Whether the game is paused for a story talk (the host's last snapshot says so).</summary>
    public bool StoryPaused => Snapshot?.StoryPause is not null;

    private bool _pausePressed;

    /// <summary>Whether the script pressed the pause key since last asked (the host's session acts on it; a guest's is dropped).</summary>
    public bool TakePauseKey()
    {
        bool pressed = _pausePressed;
        _pausePressed = false;
        return pressed;
    }

    /// <summary>Whether the host has paused the game (the host's last snapshot says so): nothing moves till it lifts.</summary>
    public bool HostPaused => Snapshot?.HostPaused == true;

    /// <summary>
    /// Whether the communication screen shows a talk: this player's own
    /// (talking, or the commander's from the starbase's menu), or the
    /// story's that everyone watches while the game waits.
    /// </summary>
    public bool TalkShown =>
        Talk is { Step: not (VisitStep.Menu or VisitStep.Done) } && (Me?.Activity is Activity.Talk or Activity.Starbase || StoryPaused);

    /// <summary>Whether this player answers the talk shown (the starter, or who the replies passed to).</summary>
    public bool Replying => Talk is { } t && t.Replier == PlayerId;

    /// <summary>The reply the cursor is on.</summary>
    public int ReplyCursor { get; private set; }

    private (int Round, int Index)? _reply;
    private int _answered = -1;
    private TalkView? _lastView;

    /// <summary>
    /// The host sent this player's talk (null: none). A visit's closing
    /// lines, once it's over, show as a notice.
    /// </summary>
    public void ShowTalk(TalkView? view)
    {
        if (view is null ? _lastView is null : view.Same(_lastView))
            return;
        // A talk with the commander from the starbase's menu over: its
        // closing lines show as a notice over the menu.
        if (view is { Step: VisitStep.Menu, Lines.Count: > 0 } && _lastView is { Step: not (VisitStep.Menu or VisitStep.Done) })
            Notify(string.Join("\n", view.Lines.Select(OneLine)));
        _lastView = view;
        if (view is null || view.Step == VisitStep.Done)
            _answered = -1;   // the next talk's rounds are new
        if (view?.Step == VisitStep.Done)
        {
            if (view.Lines.Count > 0)
                Notify(string.Join("\n", view.Lines.Select(OneLine)));
            view = null;
        }
        if (view is null || Talk is null || view.Round != Talk.Round)
            ReplyCursor = 0;
        Talk = view;
        FollowTalk();
    }

    private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>A string table's texts by resource (the lander reports), from the content; null without it.</summary>
    public Func<string, IReadOnlyList<string>>? StringTables { get; set; }

    /// <summary>
    /// The lander report the host sent (DoDiscoveryReport). This player's
    /// own lander's goes to the screen, which types it into the scan map's
    /// area as UQM does (<see cref="TakeOwnReport"/>); another player's,
    /// found while this player's lander was on the same world, is a notice
    /// in the status panel saying who found it (stage 3, decision 9: the
    /// others stay and are told, their keys still their own).
    /// </summary>
    public void ShowReport(LanderReport report)
    {
        string text = ReportText(report.Resource);
        Log($"Lander report {report.Resource}" + (report.FoundBy is { } f ? $" (found by {f})" : ""));
        if (report.FoundBy is { } id)
        {
            string who = Snapshot?.Ships.FirstOrDefault(s => s.Id == id)?.Name ?? id;
            Notify($"{who}'s lander found it:\n{text}", ReportSeconds);
        }
        else
            _ownReports.Enqueue(text);
    }

    // The report's text: its table's first entry, which DiscoveryString is when the world loads it.
    private string ReportText(string resource) =>
        StringTables?.Invoke(resource) is { Count: > 0 } texts ? texts[0] : "";

    private readonly Queue<string> _ownReports = new();

    /// <summary>The next of this player's own lander reports to show (their texts, oldest first); null if none waits.</summary>
    public string? TakeOwnReport() => _ownReports.Count > 0 ? _ownReports.Dequeue() : null;

    /// <summary>The screens a lander report is shown on (over the scan map): planet-side and orbit.</summary>
    public static bool ShowsReports(PlayerScreen screen) => screen is PlayerScreen.Planetside or PlayerScreen.Orbit;

    /// <summary>
    /// The player's own report to show now on <paramref name="screen"/>
    /// (null: none), the screen <paramref name="free"/> for one: on a screen
    /// that shows them, the next waiting; in a talk, kept till the player is
    /// back; anywhere else, each waiting one a notice instead
    /// (<see cref="NoticeOwnReports"/>).
    /// </summary>
    public string? OwnReportFor(PlayerScreen screen, bool free)
    {
        if (ShowsReports(screen))
            return free ? TakeOwnReport() : null;
        if (screen != PlayerScreen.Comm)
            NoticeOwnReports();
        return null;
    }

    /// <summary>
    /// The player is somewhere their own reports can't be shown (not at the
    /// planet-side or orbit screen, nor in a talk they'll come back from):
    /// each waiting one is a notice instead, so none shows later at another world.
    /// </summary>
    public void NoticeOwnReports()
    {
        while (_ownReports.TryDequeue(out var text))
            Notify(text, ReportSeconds);
    }

    /// <summary>How long a lander report shown as a notice stays up at least (a longer one a page at a time, each its share or as long as it takes to read, <see cref="Notify"/>).</summary>
    public const double ReportSeconds = 20;

    /// <summary>The reply chosen (its round and place), to send to the host once; null if none is waiting.</summary>
    public (int Round, int Index)? TakeReply()
    {
        var r = _reply;
        _reply = null;
        return r;
    }

    // In a talk the menu keys move the reply cursor (up and down, round)
    // and choose (select), as UQM's PlayerResponseInput.
    private void PressTalk(MenuKey key)
    {
        // One reply a set: the next waits for the host's next set.
        if (Talk is not { Replies.Count: > 0 } talk || _reply is not null || _answered == talk.Round)
            return;
        int n = talk.Replies.Count;
        switch (key)
        {
            case MenuKey.Up:
                ReplyCursor = (ReplyCursor + n - 1) % n;
                break;
            case MenuKey.Down:
                ReplyCursor = (ReplyCursor + 1) % n;
                break;
            case MenuKey.Select:
                _reply = (talk.Round, ReplyCursor);
                _answered = talk.Round;
                Log($"Reply {ReplyCursor}: {talk.Replies[ReplyCursor]}");
                break;
        }
    }

    /// <summary>
    /// The docked player's starbase screen, as the host last sent it (the
    /// host runs it: the keys go there); null when not docked.
    /// </summary>
    public StarbaseView? Starbase { get; private set; }

    private readonly Queue<MenuKey> _starbaseKeys = new();
    private (string? Ship, string? Captain)? _rename;

    /// <summary>The host sent this player's starbase screen (null: not docked). A refusal and a busy commander show as notices.</summary>
    public void ShowStarbase(StarbaseView? view)
    {
        if (view is not null && view.Refusals > (Starbase?.Refusals ?? int.MaxValue))
            Notify(view.Screen == StarbaseScreen.Shipyard ? "The shipyard can't do that." : "That can't be done.");
        if (view is { Busy.Length: > 0 } && view.Busy != Starbase?.Busy)
            Notify($"The commander is talking with {view.Busy}.");
        if (view is null && Starbase is not null)
            _starbaseKeys.Clear();
        Starbase = view;
    }

    /// <summary>The keys pressed on the starbase screen since the last call, to send to the host.</summary>
    public IReadOnlyList<MenuKey> TakeStarbaseKeys()
    {
        var keys = _starbaseKeys.ToList();
        _starbaseKeys.Clear();
        return keys;
    }

    /// <summary>Renames this player's ship or captain (null leaves it), at the starbase: sent to the host once.</summary>
    public void Rename(string? ship, string? captain)
    {
        if (Me?.Activity == Activity.Starbase)
            _rename = (ship, captain);
    }

    /// <summary>The renaming to send, once; null if none.</summary>
    public (string? Ship, string? Captain)? TakeRename()
    {
        var r = _rename;
        _rename = null;
        return r;
    }

    /// <summary>The screen for giving another player something, while it's open (item 9); the ship waits meanwhile.</summary>
    public TradeScreen? Trade { get; private set; }

    private readonly Queue<Gift> _gifts = new();

    private bool _waitForRelease;

    /// <summary>
    /// Opens or closes the giving screen. It doesn't open while the game
    /// waits for a story talk (nothing can be given then, and the replier's
    /// keys are the talk's), in a talk (the communication screen has the
    /// keys, and the minerals' count-down the status area), over the cargo
    /// display or the Devices list, for a wreck (no one is aboard), or with no
    /// one else in the game but wrecks; and it closes when a talk is shown,
    /// or the player's ship becomes a wreck (<see cref="FollowTalk"/>).
    /// </summary>
    public void ToggleTrade()
    {
        if (Trade is not null)
        {
            CloseTrade();
            return;
        }
        if (HostPaused)
            Notify(Gift.Why(TradeRefusal.HostPaused));
        else if (StoryPaused)
            Notify(Gift.Why(TradeRefusal.Paused));
        else if (TalkShown)
            Notify("Not during a talk.");
        else if (OrbitMenu?.CargoOpen == true)
            Notify("Close the cargo display first.");
        else if (OrbitMenu?.DeviceList is not null)
            Notify("Close the Devices list first.");
        else if (Me?.Wrecked == true)
            Notify(Gift.Why(TradeRefusal.Wrecked));
        else if (TradeScreen.Others(Snapshot, PlayerId).Count == 0)
            Notify(Gift.Why(TradeRefusal.NoOne));
        else
        {
            Trade = new TradeScreen();
            Trade.Follow(Snapshot, PlayerId);   // the receiver, from the start
        }
    }

    /// <summary>
    /// Closes the Manifest's Devices list, back at the Manifest, as Esc would
    /// but not by a key (the game unable to draw it; in a story pause a key
    /// would be the talk's).
    /// </summary>
    public void CloseDevices()
    {
        if (OrbitMenu?.DeviceList is null)
            return;
        OrbitMenu.CloseDevices();
        OrbitMessage = "";
    }

    private void CloseTrade()
    {
        Trade = null;
        _waitForRelease = true;
        TradeClosed?.Invoke();
    }

    /// <summary>
    /// The giving screen closed (a key or a script's <c>give</c>, a talk
    /// shown, the story pausing the game, the player's own ship becoming a
    /// wreck, the game unable to draw it), as it
    /// closes: the keys held then aren't the game's till they've
    /// come up, before another key's repeat can reach what's under it.
    /// </summary>
    public event Action? TradeClosed;

    // A talk shown (the player's own, or the story's, which pauses the
    // game) closes the giving screen: its keys are the talk's, and the
    // status area the minerals' count-down's. So does the player's own ship
    // becoming a wreck (no one aboard to give); another's leaves it open.
    private void FollowTalk()
    {
        // A talk shown, or the player's ship a wreck (opened during its battle): nothing can be given now.
        if (Trade is not null && (StoryPaused || TalkShown || Me?.Wrecked == true))
            CloseTrade();
    }

    /// <summary>The gifts to send the host, once each.</summary>
    public IReadOnlyList<Gift> TakeGifts()
    {
        var all = _gifts.ToList();
        _gifts.Clear();
        return all;
    }

    /// <summary>The host refused this player's gift: why, as a notice.</summary>
    public void TradeRefused(TradeRefusal refusal) => Notify(Gift.Why(refusal));

    /// <summary>
    /// The battle this player's ship is in, as the host last sent it (stage
    /// 4a item 11): the elements near its ship, every ship's status and
    /// place; null when in none. Item 12 draws it; until then the status
    /// lines say it.
    /// </summary>
    public BattleSnapshot? Battle => Snapshot?.Battles?.FirstOrDefault(b => b.FighterOf(PlayerId) is not null);

    /// <summary>The battle notices received, oldest first (stage 4a item 11): what item 12's screens show.</summary>
    public List<BattleNotice> BattleNotices { get; } = new();

    /// <summary>The most battle notices kept (the oldest go).</summary>
    public const int MostBattleNotices = 64;

    /// <summary>
    /// The wreck this player may give crew to (its player's id), offered as
    /// they flew into it; null once it's revived (by anyone: the snapshot no
    /// longer shows it a wreck) or another is offered.
    /// </summary>
    public string? RescueOffer { get; private set; }

    /// <summary>Whether the host has said the game is over (the flagship lost).</summary>
    public bool GameOver { get; private set; }

    /// <summary>A battle notice from the host: kept, said in words, and logged.</summary>
    public void ShowBattleNotice(BattleNotice notice)
    {
        BattleNotices.Add(notice);
        if (BattleNotices.Count > MostBattleNotices)
            BattleNotices.RemoveAt(0);
        switch (notice.Kind)
        {
            case BattleNoticeKind.RescueOffer:
                RescueOffer = notice.Other;
                break;
            case BattleNoticeKind.Revived when notice.Other == RescueOffer:
                RescueOffer = null;
                break;
            case BattleNoticeKind.GameOver:
                GameOver = true;
                break;
        }
        // The other player by name, as the snapshot has it.
        string name = Snapshot?.Ships.FirstOrDefault(s => s.Id == notice.Other)?.Name ?? notice.Other;
        string text = (notice with { Other = name }).Describe();
        Notify(text);
        Log($"Battle notice: {notice.Kind} {text}");
    }

    private (string Wreck, int Crew)? _revive;

    /// <summary>Gives a wreck crew (item 12's rescue screen): sent to the host once.</summary>
    public void Revive(string wreck, int crew) => _revive = (wreck, crew);

    /// <summary>The revival to send the host, once; null if none is waiting.</summary>
    public (string Wreck, int Crew)? TakeRevive()
    {
        var r = _revive;
        _revive = null;
        return r;
    }

    /// <summary>The host refused a revival.</summary>
    public void RescueRefused(RescueRefusal refusal) => Notify(RescueWhy(refusal));

    /// <summary>Why a revival was refused, in words.</summary>
    public static string RescueWhy(RescueRefusal refusal) => refusal switch
    {
        RescueRefusal.NoOne => "There's no one to rescue.",
        RescueRefusal.NotAWreck => "That ship isn't a wreck.",
        RescueRefusal.Busy => "Not while busy.",
        RescueRefusal.NotHere => "You aren't at the wreck.",
        RescueRefusal.Nothing => "There's no crew to give.",
        RescueRefusal.NotEnough => "You must keep at least one crew.",
        RescueRefusal.NoRoom => "The wreck hasn't room for that much crew.",
        RescueRefusal.Paused => "The game waits while the story's talk goes on.",
        RescueRefusal.GameOver => "The game is over.",
        RescueRefusal.HostPaused => "The host has paused the game.",
        _ => "",
    };

    /// <summary>A gift given: to its giver, that it went; to its receiver, what came and from whom.</summary>
    public void ShowGift(Gift gift)
    {
        string Name(string id) => Snapshot?.Ships.FirstOrDefault(s => s.Id == id)?.Name ?? id;
        if (gift.To == PlayerId)
            Notify($"{Name(gift.From)} gave you {gift.Describe()}.");
        else if (gift.From == PlayerId)
            Notify($"You gave {Name(gift.To)} {gift.Describe()}.");
        Log($"Gift from {gift.From} to {gift.To}: {gift.Describe()}");
    }

    /// <summary>A key for the orbit screen's menus (in a talk, for its replies; docked, for the starbase's screens; on the giving screen, for it).</summary>
    public void PressMenu(MenuKey key)
    {
        if (Trade is { } trade)
        {
            if (key == MenuKey.Cancel)
                CloseTrade();
            else if (trade.Press(key, Snapshot, PlayerId) is { } gift)
                _gifts.Enqueue(gift);
            return;
        }
        if (Me?.Activity == Activity.Starbase && !StoryPaused)
        {
            _starbaseKeys.Enqueue(key);
            return;
        }
        if (Me?.Activity == Activity.Talk || StoryPaused)
        {
            if (Replying)
                PressTalk(key);
            return;   // a watcher's keys do nothing while the story waits
        }
        if (OrbitMenu == null || Me?.Activity == Activity.Planetside || _landingAsked)
            return;   // the keys drive the lander then (or will soon); the menu waits for its return
        if (HostPaused)
            return;   // the menu waits for the host's pause to lift, as the world does
        OrbitMenuItem item = OrbitMenu.Selected;
        MenuOutcome outcome = OrbitMenu.Press(key, Taken, Kit);
        OrbitMessage = outcome switch
        {
            MenuOutcome.NotYet when OrbitMenu.DeviceList is not null => "Using devices comes in a later stage (3c).",
            MenuOutcome.NotYet => $"{MenuLabel(item)} comes in a later stage.",
            MenuOutcome.Refused when item == OrbitMenuItem.DispatchLander => RefusalMessage(OrbitMenu.LastRefusal),
            MenuOutcome.Refused => "The world is shielded.",
            MenuOutcome.LeaveOrbit => "Leaving orbit...",
            MenuOutcome.PickingSite => $"Pick a landing site. Fuel: {OrbitMenu.LandingFuel / 100.0:0.0}",
            MenuOutcome.Dispatch => "Dispatching the lander...",
            _ => "",
        };
        if (outcome == MenuOutcome.DiscardCargo)
        {
            // DoDiscardCargo: only if there's some of it (a failure sound otherwise).
            int category = OrbitMenu.CargoSelected;
            if (Me?.Hold is { } hold && hold.Minerals[category] > 0)
            {
                _discards.Enqueue(category);
                Log($"Discarding a unit of category {category}");
            }
        }
        if (outcome == MenuOutcome.ScanStarted)
            Log($"Scan started: {item}");
        if (outcome == MenuOutcome.PickingSite)
            Log("Picking a landing site");
        if (outcome == MenuOutcome.Dispatch)
        {
            Log($"Landing site picked: {OrbitMenu.Site!.X}, {OrbitMenu.Site.Y}");
            _landingSite = (OrbitMenu.Site.X, OrbitMenu.Site.Y);
            _landingAsked = true;
        }
        if (outcome == MenuOutcome.LeaveOrbit)
        {
            Log("Leaving orbit");
            _leaveFrames = LeaveFrames;
        }
        // A key can finish the last scan type: log it as when it runs out.
        NoteScanEnd();
    }

    /// <summary>The ship's fuel, landers and crew as the host last said, for Dispatch lander's checks.</summary>
    private (uint Fuel, int Landers, int Crew)? Kit() =>
        Me is { Hold: { } hold } me ? (me.Fuel, hold.Landers, hold.Crew) : null;

    /// <summary>Why the lander can't go, in a message.</summary>
    public static string RefusalMessage(DispatchRefusal refusal) => refusal switch
    {
        DispatchRefusal.Shielded => "The world is shielded.",
        DispatchRefusal.GasGiant => "The lander can't go down to a gas giant.",
        DispatchRefusal.Fuel => "Not enough fuel to land.",
        DispatchRefusal.NoLander => "No lander aboard.",
        DispatchRefusal.NoCrew => "No crew to send down.",
        DispatchRefusal.NotInOrbit => "The ship isn't in orbit.",
        DispatchRefusal.LanderOut => "The lander is already down.",
        DispatchRefusal.StoryPause => "The game waits while the story's talk goes on.",
        DispatchRefusal.HostPaused => "The host has paused the game.",
        DispatchRefusal.OffTheMap => "That site is off the map.",
        _ => "",
    };

    /// <summary>The engine's own label for a menu item, in messages.</summary>
    public static string MenuLabel(OrbitMenuItem item) => item switch
    {
        OrbitMenuItem.Scan => "Scan",
        OrbitMenuItem.Starmap => "Starmap",
        OrbitMenuItem.Manifest => "Manifest",
        OrbitMenuItem.GameMenu => "Game menu",
        OrbitMenuItem.Navigate => "Navigate (leave orbit)",
        OrbitMenuItem.Cargo => "Cargo",
        OrbitMenuItem.Devices => "Devices",
        OrbitMenuItem.Roster => "Roster",
        OrbitMenuItem.ExitManifest => "Exit",
        OrbitMenuItem.MineralScan => "Mineral scan",
        OrbitMenuItem.EnergyScan => "Energy scan",
        OrbitMenuItem.BiologicalScan => "Biological scan",
        OrbitMenuItem.AutoScan => "Auto scan",
        OrbitMenuItem.DispatchLander => "Dispatch lander",
        _ => "Exit scan",
    };

    /// <summary>Local time passes: the orbit screen's scans sweep, the landing site moves by the keys held, notices run out.</summary>
    public void Advance(double seconds, HeldKeys held = HeldKeys.None)
    {
        _time += seconds;
        if (Trade is not null)
            held = HeldKeys.None;   // the arrows are the giving screen's then, not the landing site's
        if (OrbitMenu == null || StoryPaused || HostPaused)
            return;   // the scans wait for the story too, and for the host's pause
        OrbitMenu.Advance(seconds, held);
        NoteScanEnd();
    }

    private void NoteScanEnd()
    {
        if (OrbitMenu == null)
            return;
        if (_wasSweeping && !OrbitMenu.Scan.Sweeping)
        {
            var dots = OrbitMenu.Scan.Dots(Taken).ToList();
            Log($"Scan done: {dots.Count(d => d.Scan == ScanType.Mineral)} mineral, "
                + $"{dots.Count(d => d.Scan == ScanType.Energy)} energy, {dots.Count(d => d.Scan == ScanType.Biological)} life shown");
        }
        _wasSweeping = OrbitMenu.Scan.Sweeping;
    }

    /// <summary>
    /// A first-time guest's undocking scene, one world frame: true once,
    /// when the scene has been shown and the host should be told.
    /// </summary>
    public bool UndockTick()
    {
        var me = Me;
        if (me == null || !me.UndockPending || UndockDone || Entering)
            return false;   // while an entry plays, the scene waits for the system
        if (_undockFrames < 0)
        {
            _undockFrames = 0;
            Log("Undocking");
        }
        if (++_undockFrames < UndockFrames)
            return false;
        UndockDone = true;
        Notify("Undocked. You're flying your escort.");
        return true;
    }

    /// <summary>
    /// The deposits on the surface the local player's lander is on, as its
    /// last frame drew them: the world's mineral nodes not taken, and those
    /// taken in that frame (still drawn in it).
    /// </summary>
    public IReadOnlyList<(int Node, SurfaceNode Info)> Deposits() =>
        OrbitMenu?.Orbit.Nodes is { } nodes && SnapshotCodec.SurfaceSeen(Snapshot, PlayerId) is not null
            ? PlanetWorldState.Remaining(TakenAsDrawn(ScanType.Mineral), nodes.Minerals)
            : Array.Empty<(int, SurfaceNode)>();

    /// <summary>
    /// The taken-node mask as the surface's last frame drew it: deposits
    /// taken in that frame were still drawn, on the surface and on the scan
    /// map (RedrawSurfaceScan comes after the lander touches them, before
    /// they go).
    /// </summary>
    public uint TakenAsDrawn(ScanType scan)
    {
        uint mask = Taken(scan);
        if (scan == ScanType.Mineral && SnapshotCodec.SurfaceSeen(Snapshot, PlayerId) is { } surface)
        {
            foreach (int node in surface.DepositsTaken ?? Array.Empty<int>())
            {
                if (node < PlanetWorldState.MaxNodes)
                    mask &= ~(1u << node);
            }
        }
        return mask;
    }

    /// <summary>The host's taken-node mask for a scan type of the world being orbited.</summary>
    public uint Taken(ScanType scan)
    {
        if (OrbitMenu == null || Snapshot?.OrbitedWorlds is not { } worlds)
            return 0;
        var w = OrbitMenu.World;
        var o = worlds.FirstOrDefault(x => x.Star == w.Star && x.Planet == w.Planet && x.Moon == w.Moon);
        return o == null ? 0 : scan switch
        {
            ScanType.Mineral => o.MineralTaken,
            ScanType.Energy => o.EnergyTaken,
            _ => o.BiologicalTaken,
        };
    }

    /// <summary>
    /// The screen for the local player's activity. While the ship's entry
    /// into a star plays, it stays on hyperspace (docs/differences.md row 24).
    /// </summary>
    public PlayerScreen Screen
    {
        get
        {
            if (TalkShown)
                return PlayerScreen.Comm;
            var me = Me;
            // In a battle, until item 12 draws it: the place it's in, its marker's.
            if (me == null || me.Activity == Activity.Hyperspace || Entering
                || me.Activity == Activity.Battle && me.SystemStar is null)
                return PlayerScreen.Hyperspace;
            if (me.Activity == Activity.Starbase && Starbase is { Screen: not StarbaseScreen.Talk })
                return PlayerScreen.Starbase;
            return me.Activity switch
            {
                Activity.Orbit => PlayerScreen.Orbit,
                Activity.Planetside => PlayerScreen.Planetside,
                _ => PlayerScreen.System,
            };
        }
    }

    /// <summary>The devices the group carries (InventoryDevices), by the story the host last sent; none before it has.</summary>
    public IReadOnlyList<int> Devices() =>
        _systems.StoryRules is { } rules && Snapshot?.Story is { } story
            ? rules.Devices.Inventory(f => story.Get(rules.Table, f))
            : Array.Empty<int>();

    /// <summary>The status panel's lines.</summary>
    public IReadOnlyList<string> StatusLines()
    {
        var lines = new List<string> { "Attack on Crateris - stage 3a", Status, Notice };
        if (HostPaused)
            lines.Add(Me?.IsHost == true ? "PAUSED - P or Pause: go on" : "PAUSED by the host");
        if (Snapshot != null)
        {
            lines.Add($"Date {Snapshot.Year:D4}-{Snapshot.Month:D2}-{Snapshot.Day:D2}");
            if (Me is { } me)
            {
                lines.Add($"You: {Where(me)}");
                lines.Add($"Fuel {me.Fuel / 100.0:0.00} / {me.FuelCapacity / 100}");
                if (me.Hold is { } h)
                    lines.Add($"Crew {h.Crew}, landers {h.Landers}, minerals {h.MineralMass} / {h.StorageCapacity}, bio {h.BioData}, {h.ResourceUnits} RU");
                if (MyLander is { } l)
                    lines.Add($"Lander: {l.Phase.ToString().ToLowerInvariant()}, crew {l.Crew}, minerals {l.MineralMass}, bio {l.BioData}");
            }
            // The ships in space: the players here, and the wrecks, whose players may be away (stage 4a item 7).
            lines.Add($"Ships ({Snapshot.Ships.Count}):");
            foreach (var s in Snapshot.Ships)
                lines.Add($"  {s.Name}{(s.IsHost ? " (host, flagship)" : " (escort)")}{(s.Wrecked ? " (wreck)" : "")}  {Where(s)}");
        }
        if (Undocking)
        {
            var host = Snapshot?.Ships.FirstOrDefault(s => s.IsHost);
            lines.Add($"Undocking from {host?.Name ?? "the"}'s flagship...");
        }
        if (Talk is { } talk && (Me?.Activity == Activity.Talk || StoryPaused))
        {
            if (!Replying)
            {
                string who = Snapshot?.Ships.FirstOrDefault(s => s.Id == talk.Replier)?.Name ?? talk.Replier;
                lines.Add($"The story's talk: {who} replies; the game waits.");
            }
            // The communication screen shows the talk itself.
            if (talk.Step == VisitStep.TimePassing)
                lines.Add("Time passes...");
            if (Replying)
                lines.Add(Replying && talk.Replies.Count > 0
                    ? "Esc: skip the lines, then the summary; Left: hear them again; Up, down: choose a reply; Enter: say it"
                    : "Esc: skip the lines, then the summary; Left: hear them again");
            // Watching a story talk: the replier pages the lines, and this screen follows (decision 7).
        }
        else if (Me?.Activity == Activity.Talk)
        {
            // In a talk whose view hasn't come yet: no keys to show.
        }
        else if (Me?.Activity == Activity.Starbase && Starbase is { } sb)
            lines.AddRange(StarbaseLines(sb));
        else if (Me?.Activity == Activity.Planetside)
            lines.Add("Arrows: turn and thrust; Space: fire; Esc: take off");
        else if (Battle is { } battle)
        {
            // The status panel in words until item 12 draws the battle.
            var mine = battle.FighterOf(PlayerId)!.Status;
            lines.Add($"In battle against the {battle.Enemy.Ship}: crew {mine.Crew}/{mine.MaxCrew}, energy {mine.Energy}/{mine.MaxEnergy}; "
                + $"the enemy's crew {battle.Enemy.Crew}/{battle.Enemy.MaxCrew}");
            lines.Add("Arrows: turn and thrust; Space: fire; Shift: special; Esc: retreat, where allowed");
        }
        else if (Me?.Activity != Activity.Orbit && !Entering && Me?.Wrecked != true)
            lines.Add("Arrows: turn and thrust");
        // The Manifest's Devices list is drawn in the status area (DevicesDisplay), which hides this panel while it's open.
        // The giving screen is drawn in the status area, which hides this panel while it's open.
        if (Trade is null && Me?.Wrecked != true && TradeScreen.Others(Snapshot, PlayerId).Count > 0 && !StoryPaused && !HostPaused && !TalkShown)   // giving waits while a talk goes on
            lines.Add("T: give another player something");
        if (Me?.IsHost == true && !HostPaused)
            lines.Add("P or Pause: pause the game");
        lines.Add("F1: about and credits");
        return lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
    }

    private static readonly string[] StarbaseItems = { "Talk to the commander", "Outfit the ship", "The shipyard", "Leave" };
    private static readonly string[] OutfitItems = { "Fuel", "Modules", "Save game", "Exit" };
    private static readonly string[] YardItems = { "Crew", "Save game", "Exit" };

    /// <summary>A module's name, in words (the status panel's, listing the outfitting menu as its keys' help, row 45).</summary>
    public static string ModuleName(byte piece) => piece switch
    {
        ShipModules.PlanetLander => "planet lander",
        ShipModules.FusionThruster => "fusion thruster",
        ShipModules.TurningJets => "turning jets",
        ShipModules.CrewPod => "crew pod",
        ShipModules.StorageBay => "storage bay",
        ShipModules.FuelTank => "fuel tank",
        ShipModules.HighEfficiencyFuelSystem => "high-efficiency fuel system",
        ShipModules.DynamoUnit => "dynamo unit",
        ShipModules.ShivaFurnace => "shiva furnace",
        ShipModules.GunWeapon => "ion-bolt gun",
        ShipModules.BlasterWeapon => "fusion blaster",
        ShipModules.CannonWeapon => "hellbore cannon",
        ShipModules.TrackingSystem => "tracking system",
        ShipModules.AntimissileDefense => "point defense",
        < ShipModules.EmptySlot => "the bomb",
        _ => "empty",
    };

    private static string Menu(IReadOnlyList<string> items, int at) =>
        string.Join("  ", items.Select((x, i) => i == at ? $"[{x}]" : x));

    // The starbase's screens in words beside their picture: where the cursor is, and the keys.
    private IEnumerable<string> StarbaseLines(StarbaseView sb)
    {
        var me = Me!;
        switch (sb.Screen)
        {
            case StarbaseScreen.Menu:
                yield return "The allied starbase:";
                yield return Menu(StarbaseItems, sb.State);
                yield return "Arrows: choose; Enter: go";
                break;
            case StarbaseScreen.Outfit when !sb.Installing:
                yield return "Outfitting:";
                yield return sb.OutfitState == OutfitMenu.DoFuel
                    ? "Fuel: up, down a unit; PgUp, PgDn a bar; Enter: done"
                    : Menu(OutfitItems, sb.OutfitState);
                if (sb.OutfitState != OutfitMenu.DoFuel)
                    yield return "Arrows: choose; Enter: go; F2, F3: rename the ship, the captain";
                break;
            case StarbaseScreen.Outfit when me.Modules is { } m:
                byte state = sb.OutfitState;
                var row = state switch
                {
                    ShipModules.PlanetLander or ShipModules.EmptyLander => SlotKind.Lander,
                    ShipModules.FusionThruster or ShipModules.EmptyDrive => SlotKind.Drive,
                    ShipModules.TurningJets or ShipModules.EmptyJet => SlotKind.Jet,
                    _ => SlotKind.Module,
                };
                string Slots(SlotKind kind)
                {
                    var pieces = kind switch
                    {
                        SlotKind.Lander => Enumerable.Range(0, ShipHold.MaxLanders)
                            .Select(i => i < (me.Hold?.Landers ?? 0) ? ShipModules.PlanetLander : ShipModules.EmptyLander).ToArray(),
                        SlotKind.Drive => m.Drives!,
                        SlotKind.Jet => m.Jets!,
                        _ => m.Modules!,
                    };
                    return string.Join(" ", pieces.Select((p, i) =>
                        (kind == row && i == sb.OutfitItem ? ">" : "") + (p < ShipModules.EmptySlot ? ModuleName(p).Split(' ')[0] : ".")));
                }
                yield return $"Drives:  {Slots(SlotKind.Drive)}";
                yield return $"Jets:    {Slots(SlotKind.Jet)}";
                yield return $"Modules: {Slots(SlotKind.Module)}";
                yield return $"Landers: {Slots(SlotKind.Lander)}";
                if (state < ShipModules.EmptySlot)
                {
                    int cost = Snapshot?.Story?.ModuleCost[state] ?? 0;
                    yield return $"Buy: {ModuleName(state)}, {cost * ModuleCostScale} RU (left, right: choose; Enter: buy; Esc: back)";
                }
                else
                    yield return "Up, down: the row; left, right: the slot; Enter: buy into an empty one, or sell; Esc: back";
                break;
            case StarbaseScreen.Shipyard:
                yield return "The shipyard:";
                yield return sb.YardMode switch
                {
                    YardMode.Crew =>
                        $"Crew {me.Hold?.Crew} of {CrewRoom(me)}, {Snapshot?.Story?.CrewCost} RU each: up hires, down sends away; Enter: done",
                    YardMode.Ships => ShipyardMenu.OnFlagship(sb.YardCursor)
                        ? "The flagship (arrows: the hangar's slots; Enter: its crew; Esc: back)"
                        : $"Hangar slot {(sb.YardCursor & 0xF) + 1}, empty: fleet ships come with combat (arrows: move; Esc: back)",
                    _ => Menu(YardItems, sb.YardState),
                };
                break;
        }
    }

    /// <summary>MODULE_COST_SCALE (data/modules.json): resource units a unit of a module's cost.</summary>
    public int ModuleCostScale { get; set; } = 50;

    private static int CrewRoom(ShipSnapshot me) =>
        me.Modules is { } m ? ShipModules.Restore(m).CrewCapacity : 0;

    /// <summary>Where a ship is, in words (entering a star while its entry plays, as the screen shows it).</summary>
    public string Where(ShipSnapshot s)
    {
        if (s.Activity == Activity.Hyperspace || s.SystemStar is not int star)
            return $"hyperspace {s.UniverseX / 10.0:0.0} : {s.UniverseY / 10.0:0.0}";
        var at = _systems.Map.Stars[star];
        string cluster = _names?.Cluster(at) ?? $"star {star}";
        if (Entries.Playing(s.Id))
            return $"entering {cluster}";
        return s.Activity switch
        {
            Activity.InnerSystem when s.InnerPlanet is int p => $"the {cluster} system, near {_names?.PlanetName(at, p) ?? $"planet {p}"}",
            Activity.Talk when s.InnerPlanet is int p => $"talking at the starbase by {_names?.PlanetName(at, p) ?? $"planet {p}"}, {cluster}",
            Activity.Starbase when s.InnerPlanet is int p => $"docked at the starbase by {_names?.PlanetName(at, p) ?? $"planet {p}"}, {cluster}",
            Activity.Planetside when s.OrbitPlanet is int p =>
                $"landed on {_names?.PlanetOrMoon(at, p, s.OrbitMoon) ?? $"planet {p}" + (s.OrbitMoon is int m ? $" moon {m}" : "")}, {cluster}",
            Activity.Orbit when s.OrbitPlanet is int p =>
                $"orbiting {_names?.PlanetOrMoon(at, p, s.OrbitMoon) ?? $"planet {p}" + (s.OrbitMoon is int m ? $" moon {m}" : "")}, {cluster}",
            _ => $"the {cluster} system",
        };
    }

    /// <summary>
    /// Keeps the orbit menu for the current visit to a world, through a
    /// landing too (UQM comes back to the scan screen with what was found);
    /// none when not in orbit.
    /// </summary>
    private void UpdateOrbitMenu()
    {
        var me = Me;
        if (me?.Activity is Activity.Orbit or Activity.Planetside && me.SystemStar is int star && me.OrbitPlanet is int planet)
        {
            var world = new WorldId(star, planet, me.OrbitMoon);
            if (OrbitMenu?.World != world)
            {
                // A new visit: the menu starts afresh, nothing found yet.
                OrbitMenu = new OrbitMenu(world, _systems[star].EnterOrbit(planet, me.OrbitMoon)) { Inventory = Devices };
                OrbitMessage = "";
                _wasSweeping = false;
            }
        }
        else
            OrbitMenu = null;
    }
}
