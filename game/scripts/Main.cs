// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The Godot node that wires the game up. What the game does is decided
/// without Godot: the host's side by a <see cref="HostSession"/> (the world,
/// guests, input, saves), the local player's by a <see cref="PlayerSession"/>
/// (keys or script, the orbit menus, the screen to show, the status text).
/// This node reads keys, moves messages over Godot's MultiplayerAPI and
/// ENet, reads and writes files, opens the content, and gives the views
/// what to draw. The host steps its world 24 times a second and sends a
/// snapshot to all clients every frame; guests only send input and draw.
/// </summary>
public partial class Main : Node2D
{
    private Options _options = null!;
    private bool _running;   // false once quitting (a bad command line, no content, Finish): the frame methods stop
    private string _playerId = "";
    private string _name = "";
    private string _savePath = "";
    private string _dataDir = "";
    private readonly List<string> _log = new();
    private long _frames;
    private double _saveTimer;

    private HostSession? _host;
    private PlayerSession? _player;
    private Protocol.Welcome? _welcome;
    private int _inputDelay;

    // A guest is connected from an accepted welcome until the connection fails or the host leaves.
    private bool _connected;
    private bool _failing;           // quitting after a failure, in a moment
    private bool _finished;
    private long _firstJoinFrame = -1;
    private string _status = "";

    private HyperspaceView _view = null!;
    private SystemView _systemView = null!;
    private OrbitView _orbitView = null!;
    private PlanetsideView _planetsideView = null!;
    private Label _hud = null!;
    private SystemCatalog _systems = null!;

    // What the game reads from the UQM content package (checked as the
    // official one), and the screens that get it on first run and show its notice.
    private Names _names = null!;
    private ContactShapes _shapes = null!;
    private TalkLibrary? _talks;
    private readonly Dictionary<string, IReadOnlyList<string>> _reportTexts = new();
    private FirstRunScreen? _firstRun;
    private ContentErrorScreen? _contentError;
    private AboutScreen _about = null!;
    private CanvasLayer _screens = null!;

    private static bool Headless => DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        try
        {
            _options = Options.Parse(OS.GetCmdlineUserArgs());
        }
        catch (Exception e)
        {
            GD.PrintErr($"Bad command line: {e.Message}");
            Quit(2);
            return;
        }

        _running = true;
        _dataDir = DataDirectory();
        _systems = SystemCatalog.Load(_dataDir);
        AddChild(new ColorRect { Color = new Color(0.03f, 0.03f, 0.05f), Size = new Vector2(1024, 768) });
        // The system screen sits on the right; the status and menus on the left.
        var systemScreenAt = new Vector2(1024 - SisScreen.Size.X - 12, (768 - SisScreen.Size.Y) / 2);
        _view = new HyperspaceView(_systems.Map) { Position = systemScreenAt };
        AddChild(_view);
        _systemView = new SystemView(_systems) { Position = systemScreenAt, Visible = false };
        AddChild(_systemView);
        _orbitView = new OrbitView(_systems) { Position = systemScreenAt, Visible = false };
        AddChild(_orbitView);
        _planetsideView = new PlanetsideView(_systems) { Position = systemScreenAt, Visible = false };
        AddChild(_planetsideView);
        var layer = new CanvasLayer();
        AddChild(layer);
        // Left of the system screen; long lines wrap.
        _hud = new Label
        {
            Position = new Vector2(12, 8),
            Size = new Vector2(1024 - SisScreen.Size.X - 36, 300),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        layer.AddChild(_hud);

        _screens = new CanvasLayer { Layer = 10 };
        AddChild(_screens);
        _about = new AboutScreen(ProjectSettings.GlobalizePath("user://")) { Visible = _options.ShowAbout };
        _screens.AddChild(_about);

        _name = _options.Name ?? System.Environment.UserName;
        _playerId = _options.PlayerId ?? LoadOrCreatePlayerId();
        _savePath = _options.SavePath ?? ProjectSettings.GlobalizePath("user://host-save.json");

        PrepareContent();
    }

    // ---------------------------------------------------------- content

    /// <summary>
    /// Finds and checks the content package, then starts the game. Hosting
    /// and joining need it (content stage, decision 4): without it a window
    /// shows the first-run screen, and a headless run stops and says how to
    /// give it one.
    /// </summary>
    private void LogAddons(string userFolder)
    {
        foreach (var addon in OfficialPackages.Addons)
            Log($"{addon.Title}: {(OfficialPackages.IsInstalled(userFolder, addon) ? "here (not played yet)" : "not here")}");
    }

    private void PrepareContent()
    {
        string userFolder = ProjectSettings.GlobalizePath("user://");
        string stored = ContentLocation.StoredPackage(userFolder);
        string? problem = null;
        ContentPackage? found = null;
        try
        {
            if (ContentLocation.Find(_options.ContentPath, userFolder) is { } path)
            {
                found = ContentPackage.Open(path);
                Log($"Content: {path}");
                LogAddons(userFolder);
            }
        }
        catch (ContentPackageException e)
        {
            problem = e.Message;
        }
        if (found != null)
        {
            if (UseContent(found))
                StartGame();
            return;
        }

        string why = problem ?? "The UQM 0.8.0 content isn't here yet.";
        if (Headless)
        {
            GD.PrintErr($"{why} Give it with --content PATH or {ContentLocation.EnvironmentVariable}, " +
                $"or start the game once with a window to download it.");
            Quit(3);
            return;
        }
        Log(why);
        _firstRun = new FirstRunScreen(stored, problem, userFolder);
        _firstRun.Installed += path =>
        {
            if (_firstRun is null)
                return;   // already started
            ContentPackage installed;
            try
            {
                // The installer has just checked the file's hash.
                installed = ContentPackage.Open(path, expectedSha256: null);
            }
            catch (ContentPackageException e)
            {
                GD.PrintErr(e.Message);
                _firstRun!.ShowFailure(e.Message);
                return;
            }
            Log($"Content installed: {path}");
            LogAddons(userFolder);
            _firstRun!.QueueFree();
            _firstRun = null;
            if (UseContent(installed))
                StartGame();
        };
        _screens.AddChild(_firstRun);
        foreach (string addon in _options.FirstRunTick)
            _firstRun.Tick(addon);
        if (_options.FirstRunUse is { } file)
            _firstRun.Choose(file);
    }

    /// <summary>
    /// Reads what the game needs from the opened, checked package: the
    /// names and the shapes that decide contact always, and with a window
    /// what the views draw (a headless server or guest draws nothing). The
    /// package is the official one, so a failure here is a fault (in the
    /// engine, or reading the file), not a wrong file: it's shown, with no
    /// offer to download the same file again, and the game doesn't start.
    /// </summary>
    private bool UseContent(ContentPackage content)
    {
        try
        {
            var sheets = new AnimationSheets(content);   // decoded once, for the shapes and the sprites
            _names = Names.Load(content);
            _shapes = ContactShapes.Load(sheets);
            _talks = TalkLibrary.Load(content, ConversationScripts.Load(_dataDir));
            foreach (string report in Crateris.Core.Story.StoryWorlds.Reports)
            {
                var table = StringTable.Load(content, report);
                _reportTexts[report] = Enumerable.Range(0, table.Count).Select(i => table[i]).ToList();
            }
            if (!Headless)
            {
                _view.Sprites = _systemView.Sprites = new SpriteTextures(new SpaceSprites(content, sheets));
                _orbitView.Text = _planetsideView.Text = OrbitScreenText.Load(content);
                _orbitView.Surfaces = _planetsideView.Surfaces = new PlanetSurfaces(content);
                _orbitView.Sprites = _planetsideView.Sprites = new PlanetsideSprites(content, sheets);
            }
        }
        catch (ContentPackageException e)
        {
            string message = $"The UQM content is the official package, but it couldn't be read: {e.Message}";
            GD.PrintErr(message);
            if (Headless)
                Quit(4);
            else
            {
                _contentError = new ContentErrorScreen(message);
                _screens.AddChild(_contentError);
            }
            return false;
        }
        _systemView.Names = _orbitView.Names = _names;
        return true;
    }

    private void StartGame()
    {
        _player = new PlayerSession(_systems, _names, _playerId, _shapes.HyperFlagship.Count, _options.Script)
        {
            StringTables = id => _reportTexts.GetValueOrDefault(id) ?? Array.Empty<string>(),
        };
        _player.Logged += Log;
        if (_options.Join)
            StartClient();
        else
            StartHost();
    }

    private static string DataDirectory()
    {
        string? env = System.Environment.GetEnvironmentVariable("CRATERIS_DATA");
        if (!string.IsNullOrEmpty(env))
            return env;
        return Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"));
    }

    private static string LoadOrCreatePlayerId()
    {
        string path = ProjectSettings.GlobalizePath("user://player-id.txt");
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();
        string id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, id);
        return id;
    }

    // ------------------------------------------------------------- host

    private void StartHost()
    {
        string? load = _options.LoadPath ?? (File.Exists(_savePath) && _options.SavePath == null ? _savePath : null);
        if (load != null && File.Exists(load))
        {
            _host = HostSession.Load(File.ReadAllText(load), _name, _systems, _shapes);
            _playerId = _player!.PlayerId = _host.HostId;   // the save belongs to its host
            Log($"Loaded {load}");
        }
        else
        {
            var settings = new GameSettings();
            if (_options.SecondsPerDay is { } spd)
                settings = settings with { SecondsPerGameDay = spd };
            _host = HostSession.New(settings, NewGameData.Load(_dataDir, _systems.Map), _playerId, _name, _systems, _shapes);
            Log("New game");
        }
        _host.Logged += Log;
        _host.World.TalkLibrary = _talks;
        ApplyStartOptions(_host);
        _inputDelay = _host.World.Settings.InputDelayFrames;

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(_options.Port, _host.World.Settings.MaxPlayers - 1);
        if (err != Error.Ok)
            SetStatus($"Could not open port {_options.Port} ({err}). Playing solo.");
        else
        {
            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerDisconnected += peerId => _host.GuestLeft(peerId);
            SetStatus($"Hosting on port {_options.Port}.");
            if (_options.UseUpnp)
                OpenPortWithUpnp(_options.Port);
        }
        _player!.Observe(_host.World.Snapshot());
    }

    /// <summary>--start-at and --start-orbit (testing): a notice says if they can't be done.</summary>
    private void ApplyStartOptions(HostSession host)
    {
        try
        {
            if (_options.StartAt is { } at)
                Log(host.StartAt(at.X, at.Y));
            if (_options.StartOrbit is { } o)
                Log(host.StartInOrbit(o.Star, o.Planet, o.Moon));
            host.GuestsStartInOrbit = _options.GuestsInOrbit;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // A notice: the hosting status set after this would hide a status line.
            _player!.Notify($"Can't start there: {e.Message}", 20);
        }
    }

    // Ask the router to forward the port (OpenRA-style). Runs in the
    // background; failure just leaves the port closed to the internet.
    private void OpenPortWithUpnp(int port)
    {
        Task.Run(() =>
        {
            var upnp = new Upnp();
            int result = upnp.Discover(2000, 2, "");
            if (result != (int)Upnp.UpnpResult.Success || upnp.GetGateway() is not { } gw || !gw.IsValidGateway())
                return "No UPnP router found; forward the port by hand if friends can't connect.";
            if (upnp.AddPortMapping(port, port, "Attack on Crateris", "UDP") != (int)Upnp.UpnpResult.Success)
                return "The router refused to open the port; forward it by hand.";
            return $"Port {port} opened on the router. Friends join with {upnp.QueryExternalAddress()}:{port}";
        }).ContinueWith(t => CallDeferred(MethodName.SetStatus, $"Hosting on port {port}. {t.Result}"));
    }

    private void SetStatus(string text)
    {
        _status = text;
        Log(text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string playerId, string name, int version)
    {
        if (_host == null)
            return;
        long peer = Multiplayer.GetRemoteSenderId();
        var welcome = _host.Hello(peer, playerId, name, version);
        RpcId(peer, MethodName.Welcomed, Protocol.Encode(welcome));
        // A refused newcomer is let go; a player already on this connection stays.
        if (!Protocol.Accepted(welcome.Result) && !_host.InGame(peer))
            GetTree().CreateTimer(0.5).Timeout += () => Multiplayer.MultiplayerPeer?.DisconnectPeer((int)peer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void SendInput(long frame, int input, long has) => _host?.GuestInput(Multiplayer.GetRemoteSenderId(), frame, input, has);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UndockShown() => _host?.GuestUndockShown(Multiplayer.GetRemoteSenderId());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestLanding(int x, int y)
    {
        if (_host == null)
            return;
        long peer = Multiplayer.GetRemoteSenderId();
        var refusal = _host.GuestDispatch(peer, x, y);
        if (refusal != DispatchRefusal.None && _host.InGame(peer))
            RpcId(peer, MethodName.LandingRefused, (int)refusal);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void DiscardCargo(int category) => _host?.GuestDiscard(Multiplayer.GetRemoteSenderId(), category);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LandingRefused(int refusal) => _player?.LandingRefused((DispatchRefusal)refusal);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ChooseReply(int round, int index) => _host?.GuestChooseReply(Multiplayer.GetRemoteSenderId(), round, index);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowTalk(byte[] talk)
    {
        try
        {
            _player?.ShowTalk(TalkView.Decode(talk));
        }
        catch (InvalidDataException e)
        {
            Log($"Bad talk from the host: {e.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowReport(string resource, string foundBy) =>
        _player?.ShowReport(new Crateris.Core.Planetside.LanderReport(resource, foundBy == "" ? null : foundBy));

    private void SaveGame()
    {
        if (_host == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _host.SaveJson());
    }

    // ----------------------------------------------------------- client

    private void StartClient()
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(_options.Address, _options.Port);
        if (err != Error.Ok)
        {
            Fail($"Could not connect to {_options.Address}:{_options.Port} ({err}).");
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () =>
            RpcId(1, MethodName.Hello, _playerId, _name, Protocol.Version);
        Multiplayer.ConnectionFailed += () => Fail($"Could not reach {_options.Address}:{_options.Port}.");
        Multiplayer.ServerDisconnected += () => Fail("The host closed the game.");
        SetStatus($"Connecting to {_options.Address}:{_options.Port}...");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcomed(byte[] data)
    {
        var welcome = Protocol.Decode<Protocol.Welcome>(data);
        switch (Protocol.OnWelcome(welcome, alreadyIn: _connected))
        {
            case Protocol.WelcomeAction.Ignore:
                Log($"Welcome again: {welcome.Message}");
                return;
            case Protocol.WelcomeAction.Leave:
                Fail(welcome.Message);
                return;
        }
        _welcome = welcome;
        _inputDelay = _welcome.InputDelayFrames;
        _connected = true;
        _status = $"Playing in the game at {_options.Address}:{_options.Port}.";
        _player?.Notify(_welcome.Message);
    }

    private int _badSnapshots;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void ReceiveSnapshot(byte[] data)
    {
        try
        {
            _player?.ReceiveSnapshot(data);
        }
        catch (InvalidDataException e)
        {
            _badSnapshots++;
            GD.PushWarning($"Dropped a bad snapshot: {e.Message}");
        }
    }

    private void Fail(string message)
    {
        _connected = false;   // nothing more is sent, and the undocking scene stops
        SetStatus(message);
        // A second's grace before quitting, so a report shows what the
        // game did after the failure (the network test checks it).
        if ((_options.QuitAfterFrames > 0 || _options.ReportPath != null) && !_failing)
        {
            _failing = true;
            GetTree().CreateTimer(1.0).Timeout += () => Finish(1);
        }
    }

    // ------------------------------------------------------------ frame

    public override void _PhysicsProcess(double delta)
    {
        if (!_running)
            return;
        _frames++;
        if (_player != null)
        {
            // A guest's script starts once it is in the game, so a slow join
            // doesn't use up its first steps (the network test depends on it).
            bool inGame = _host != null || (_connected && _player.Snapshot != null);
            // No ship keys while the about screen is up (its Esc mustn't take off).
            var keys = _about.Visible ? ShipInput.None : ReadKeys(_player.Screen == PlayerScreen.Planetside);
            var input = inGame ? _player.NextInput(keys) : ShipInput.None;
            var landing = inGame ? _player.TakeLandingRequest() : null;
            var discards = inGame ? _player.TakeDiscards() : Array.Empty<int>();
            if (_host != null)
            {
                foreach (int category in discards)
                    _host.World.DiscardCargo(_host.HostId, category);
                if (landing is { } site && _host.Dispatch(_host.HostId, site.X, site.Y) is var refusal
                        && refusal != DispatchRefusal.None)
                    _player.LandingRefused(refusal);
                if (inGame && _player.TakeReply() is var (round, reply))
                    _host.ChooseReply(_host.HostId, round, reply);
                var snapshot = _host.Step(input);
                _player.Observe(snapshot);
                _player.ShowTalk(_host.TalkFor(_host.HostId));
                // Each guest gets its own: a delta from what it has, or in full.
                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer)
                {
                    foreach (long peer in _host.GuestPeers)
                        RpcId(peer, MethodName.ReceiveSnapshot, _host.SnapshotFor(peer));
                    foreach (var (peer, talk) in _host.TalkUpdates())
                        RpcId(peer, MethodName.ShowTalk, talk);
                }
                var (ownReports, guestReports) = _host.TakeReports();
                foreach (var report in ownReports)
                    _player.ShowReport(report);
                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer)
                {
                    foreach (var (peer, report) in guestReports)
                        RpcId(peer, MethodName.ShowReport, report.Resource, report.FoundBy ?? "");
                }
            }
            else if (_connected && _player.Snapshot is { } snap)
            {
                if (landing is { } site)
                    RpcId(1, MethodName.RequestLanding, site.X, site.Y);
                foreach (int category in discards)
                    RpcId(1, MethodName.DiscardCargo, category);
                if (_player.TakeReply() is var (round, reply))
                    RpcId(1, MethodName.ChooseReply, round, reply);
                RpcId(1, MethodName.SendInput, snap.Frame + _inputDelay, (int)input, snap.Frame);
                if (_player.UndockTick())
                    RpcId(1, MethodName.UndockShown);
            }
            ShowViews(_player);
        }
        UpdateHud();

        if (_host != null && _options.QuitAfterJoinFrames > 0 && _firstJoinFrame < 0 && _host.Guests > 0)
            _firstJoinFrame = _frames;
        bool quitAfterJoin = _firstJoinFrame >= 0 && _frames >= _firstJoinFrame + _options.QuitAfterJoinFrames;
        if (!_failing && ((_options.QuitAfterFrames > 0 && _frames >= _options.QuitAfterFrames) || quitAfterJoin))
            Finish(0);
    }

    public override void _Process(double delta)
    {
        if (!_running)
            return;
        _player?.Advance(delta, _player.Scripted || _about.Visible ? HeldKeys.None : HeldArrows());
        _orbitView.Advance(delta);
        if (_player != null)
            _orbitView.Show(_player.OrbitMenu, _player.Taken, _player.Snapshot, _player.PlayerId, _player.OrbitMessage);
        _systemView.Advance(delta);
        if (_host == null)
            return;
        _saveTimer += delta;
        if (_saveTimer >= 60)
        {
            _saveTimer = 0;
            SaveGame();
        }
    }

    /// <summary>The ship keys held (the session ignores them when a script plays, or in orbit).</summary>
    // The keys held. On a planet's surface, as UQM's: Space, Enter or Ctrl
    // fires, Esc takes off (a key of its own, never leaving orbit); elsewhere
    // Space and Enter are the action key.
    private static ShipInput ReadKeys(bool onSurface)
    {
        var input = ShipInput.None;
        if (Input.IsKeyPressed(Key.Left))
            input |= ShipInput.Left;
        if (Input.IsKeyPressed(Key.Right))
            input |= ShipInput.Right;
        if (Input.IsKeyPressed(Key.Up))
            input |= ShipInput.Thrust;
        bool select = Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Space);
        if (onSurface)
        {
            if (select || Input.IsKeyPressed(Key.Ctrl))
                input |= ShipInput.Fire;
            if (Input.IsKeyPressed(Key.Escape))
                input |= ShipInput.TakeOff;
        }
        else if (select)
            input |= ShipInput.Action;
        return input;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_running || e is not InputEventKey { Pressed: true } k)
            return;
        // F1 opens and closes the about screen; Esc closes it too.
        if (k.Keycode == Key.F1 || (_about.Visible && k.Keycode == Key.Escape))
        {
            _about.Visible = k.Keycode == Key.F1 && !_about.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_about.Visible || _player == null || _player.Scripted)
            return;
        MenuKey? key = k.Keycode switch
        {
            Key.Up => MenuKey.Up,
            Key.Down => MenuKey.Down,
            Key.Left => MenuKey.Left,
            Key.Right => MenuKey.Right,
            Key.Enter or Key.KpEnter or Key.Space => MenuKey.Select,
            Key.Escape or Key.Backspace => MenuKey.Cancel,
            _ => null,
        };
        // A held Enter mustn't say one reply after another.
        if (key == MenuKey.Select && k.Echo && _player.Talk is not null)
            return;
        if (key is MenuKey mk)
            _player.PressMenu(mk);
    }

    /// <summary>The arrow keys held, which move the landing site while it's picked.</summary>
    private static HeldKeys HeldArrows() =>
        (Input.IsKeyPressed(Key.Up) ? HeldKeys.Up : 0)
        | (Input.IsKeyPressed(Key.Down) ? HeldKeys.Down : 0)
        | (Input.IsKeyPressed(Key.Left) ? HeldKeys.Left : 0)
        | (Input.IsKeyPressed(Key.Right) ? HeldKeys.Right : 0);

    /// <summary>Shows the screen the session picks, with what it holds.</summary>
    private void ShowViews(PlayerSession player)
    {
        var screen = player.Screen;
        _view.Show(player.Snapshot, player.PlayerId, player.Entries);
        _view.Visible = screen == PlayerScreen.Hyperspace;
        _systemView.Visible = screen == PlayerScreen.System;
        _orbitView.Visible = screen == PlayerScreen.Orbit;
        _planetsideView.Visible = screen == PlayerScreen.Planetside;
        if (_planetsideView.Visible)
            _planetsideView.Show(player);
        if (player.Snapshot != null && _systemView.Visible)
            _systemView.Show(player.Snapshot, player.PlayerId);
    }

    private void UpdateHud()
    {
        if (_player != null)
            _player.Status = _status;
        _hud.Text = _player == null ? _status : string.Join("\n", _player.StatusLines());
        // Those screens stand alone; the cargo display covers the status area, as UQM's does.
        _hud.Visible = _firstRun == null && _contentError == null && _player?.OrbitMenu?.CargoOpen != true;
    }

    // ---------------------------------------------------- quit / report

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            SaveGame();
    }

    private void Log(string message)
    {
        _log.Add($"[{_frames}] {message}");
        GD.Print(message);
    }

    /// <summary>Quits; nothing more runs a frame meanwhile.</summary>
    private void Quit(int code)
    {
        _running = false;
        GetTree().Quit(code);
    }

    private void Finish(int code)
    {
        if (_finished)
            return;
        _finished = true;
        SaveGame();
        if (_options.ScreenshotPath != null && !Headless)
            GetViewport().GetTexture().GetImage().SavePng(_options.ScreenshotPath);
        if (_options.ReportPath != null)
        {
            var report = new
            {
                mode = _host != null ? "host" : "client",
                player_id = _player?.PlayerId ?? _playerId,
                frames = _frames,
                welcome = _welcome?.Result.ToString(),
                status = _status,
                notice = _player?.Notice ?? "",
                log = _log,
                snapshot = _player?.Snapshot,
                snapshots = new
                {
                    full = _player?.SnapshotsFull ?? 0,
                    deltas = _player?.SnapshotsDelta ?? 0,
                    without_base = _player?.SnapshotsWithoutBase ?? 0,
                    bad = _badSnapshots,
                },
            };
            File.WriteAllText(_options.ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Multiplayer.MultiplayerPeer?.Close();
        Quit(code);
    }
}
