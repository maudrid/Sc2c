// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Content;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>Which screen the local player sees.</summary>
public enum PlayerScreen { Hyperspace, System, Orbit, Planetside }

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
    private string _notice = "";
    private double _noticeUntil;

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

    /// <summary>The notice being shown, or "" once it has run out.</summary>
    public string Notice => _time < _noticeUntil ? _notice : "";

    /// <summary>Whether a first-time guest's undocking scene is showing.</summary>
    public bool Undocking => _undockFrames >= 0 && !UndockDone;

    public bool UndockDone { get; private set; }

    /// <summary>What happened, for the log (read by the network test).</summary>
    public event Action<string>? Logged;

    private void Log(string message) => Logged?.Invoke(message);

    /// <summary>Shows a one-off message under the status line for a while (and logs it).</summary>
    public void Notify(string message, double seconds = NoticeSeconds)
    {
        _notice = message;
        _noticeUntil = _time + seconds;
        Log(message);
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
        UpdateOrbitMenu();
        NoteLander();
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
            input = _script.Next(out var press);
            if (press is MenuKey key)
                PressMenu(key);
        }
        else
            input = Me?.Activity == Activity.Orbit ? ShipInput.None : keys;   // on the surface, the lander's keys
        if (Entering)
            return ShipInput.None;
        if (_leaveFrames > 0)
        {
            _leaveFrames--;
            return ShipInput.Action;
        }
        return input;
    }

    /// <summary>A key for the orbit screen's menus.</summary>
    public void PressMenu(MenuKey key)
    {
        if (OrbitMenu == null || Me?.Activity == Activity.Planetside || _landingAsked)
            return;   // the keys drive the lander then (or will soon); the menu waits for its return
        OrbitMenuItem item = OrbitMenu.Selected;
        MenuOutcome outcome = OrbitMenu.Press(key, Taken, Kit);
        OrbitMessage = outcome switch
        {
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
        if (OrbitMenu == null)
            return;
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
            var me = Me;
            if (me == null || me.Activity == Activity.Hyperspace || Entering)
                return PlayerScreen.Hyperspace;
            return me.Activity switch
            {
                Activity.Orbit => PlayerScreen.Orbit,
                Activity.Planetside => PlayerScreen.Planetside,
                _ => PlayerScreen.System,
            };
        }
    }

    /// <summary>The status panel's lines.</summary>
    public IReadOnlyList<string> StatusLines()
    {
        var lines = new List<string> { "Attack on Crateris - stage 2", Status, Notice };
        if (Snapshot != null)
        {
            lines.Add($"Date {Snapshot.Year:D4}-{Snapshot.Month:D2}-{Snapshot.Day:D2}");
            if (Me is { } me)
            {
                lines.Add($"You: {Where(me)}");
                lines.Add($"Fuel {me.Fuel / 100.0:0.00} / {me.FuelCapacity / 100}");
                if (me.Hold is { } h)
                    lines.Add($"Crew {h.Crew}, landers {h.Landers}, minerals {h.MineralMass} / {h.StorageCapacity}, bio {h.BioData}");
                if (MyLander is { } l)
                    lines.Add($"Lander: {l.Phase.ToString().ToLowerInvariant()}, crew {l.Crew}, minerals {l.MineralMass}, bio {l.BioData}");
            }
            lines.Add($"Players ({Snapshot.Ships.Count}):");
            foreach (var s in Snapshot.Ships)
                lines.Add($"  {s.Name}{(s.IsHost ? " (host, flagship)" : " (escort)")}  {Where(s)}");
        }
        if (Undocking)
        {
            var host = Snapshot?.Ships.FirstOrDefault(s => s.IsHost);
            lines.Add($"Undocking from {host?.Name ?? "the"}'s flagship...");
        }
        if (Me?.Activity == Activity.Planetside)
            lines.Add("Arrows: turn and thrust; Space: fire; Esc: take off");
        else if (Me?.Activity != Activity.Orbit && !Entering)
            lines.Add("Arrows: turn and thrust");
        lines.Add("F1: about and credits");
        return lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
    }

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
                OrbitMenu = new OrbitMenu(world, _systems[star].EnterOrbit(planet, me.OrbitMoon));
                OrbitMessage = "";
                _wasSweeping = false;
            }
        }
        else
            OrbitMenu = null;
    }
}
