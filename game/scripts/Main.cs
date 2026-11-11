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
using Crateris.Core.Combat;
using Crateris.Core.Comm;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Starbase;
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
    private bool _keepDefaultSave;
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
    private ReportView _reportView = null!;
    private CommView _commView = null!;
    private SellingView _sellingView = null!;
    private GivingView _givingView = null!;
    private DevicesView _devicesView = null!;
    private StarbaseScreenView _starbaseView = null!;
    private Label _hud = null!;
    private BoxNoticeView _boxNotice = null!;   // the notice, below the cargo display or the Devices list (the panel hidden)
    private SystemCatalog _systems = null!;

    // What the game reads from the UQM content package (checked as the
    // official one), and the screens that get it on first run and show its notice.
    private Names _names = null!;
    private ContactShapes _shapes = null!;
    private ShipDescriptions _ships = null!;
    private BattleKit _battleKit = null!;
    private TalkLibrary? _talks;
    private readonly Dictionary<string, IReadOnlyList<string>> _reportTexts = new();
    private FirstRunScreen? _firstRun;
    private ContentErrorScreen? _contentError;
    private AboutScreen _about = null!;
    // Keys that went down into the about screen or the lander report: the game's keys wait for each to come up.
    private readonly TakenKeys _taken = new();
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
        _ships = ShipDescriptions.Load(_dataDir);
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
        // The lander report, typed into the scan map's area of the planet-side and orbit views.
        _reportView = new ReportView
        {
            Position = systemScreenAt + new Vector2(0, (IpGeometry.ScreenHeight - SurfaceMap.Height) * SisScreen.Scale),
            Visible = false,
        };
        _reportView.Failed += text => _player?.Notify(text, PlayerSession.ReportSeconds);
        AddChild(_reportView);
        _commView = new CommView { Position = systemScreenAt, Visible = false };
        _commView.Paged += (round, lines, page) =>
        {
            if (_host != null)
                _host.PageTalk(_host.HostId, round, lines, page);
            else if (_connected)
                RpcId(1, MethodName.PageTalk, round, lines, (int)page);
        };
        AddChild(_commView);
        // The minerals' count-down, in the status area left of the communication screen,
        // where the orbit screen's cargo display goes (SisScreen.StatusOrigin).
        var statusOrigin = new Vector2(12, systemScreenAt.Y + SisScreen.MenuTop)
            - new Vector2(OrbitScreenText.MenuAreaX - 1, OrbitScreenText.MenuAreaY) * SisScreen.Scale;
        _sellingView = new SellingView
        {
            Position = statusOrigin + new Vector2(MineralSelling.Area.X - UqmCanvas.SpaceWidth, MineralSelling.Area.Y) * SisScreen.Scale,
            Visible = false,
        };
        AddChild(_sellingView);
        // UQM's whole screen, at the right as the system screen is.
        _starbaseView = new StarbaseScreenView
        {
            Position = new Vector2(1024 - StarbaseScreenView.Size.X - 12, (768 - StarbaseScreenView.Size.Y) / 2),
            Visible = false,
        };
        _starbaseView.Lost += () =>
        {
            if (_host != null)
                _host.World.RedrawStarbase(_host.HostId);
            else if (_connected)
                RpcId(1, MethodName.StarbaseRedraw);
        };
        AddChild(_starbaseView);
        // The giving screen, in the status area where the cargo display goes, over every view.
        _givingView = new GivingView
        {
            Position = statusOrigin + new Vector2(GivingScreen.Area.X - UqmCanvas.SpaceWidth, GivingScreen.Area.Y) * SisScreen.Scale,
            Width = systemScreenAt.X - 24,
            Visible = false,
        };
        AddChild(_givingView);
        // The Manifest's Devices list, in the status area where the cargo display goes, over the orbit view.
        _devicesView = new DevicesView
        {
            Position = statusOrigin + new Vector2(DevicesDisplay.Area.X - UqmCanvas.SpaceWidth, DevicesDisplay.Area.Y) * SisScreen.Scale,
            Visible = false,
        };
        AddChild(_devicesView);
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
        // While the Manifest's cargo display or Devices list covers the status area (its panel hidden), the
        // notice (a gift, another player's report) is shown below the box, as the giving screen shows it: eight
        // lines fit above the orbit menu, which hold every page of the content's lander reports.
        _boxNotice = new BoxNoticeView
        {
            Position = _devicesView.Position + new Vector2(0, DevicesDisplay.Area.H * SisScreen.Scale),
            Width = systemScreenAt.X - 24,
            Visible = false,
        };
        layer.AddChild(_boxNotice);

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
            // The battle's shapes, for the ported ships and every planet, headless too (stage 4a item 6).
            var planetTypes = _systems.Generator.Types;
            var battleShapes = BattleShapes.Load(sheets, BattleKit.Ported(_ships), planetTypes);
            _battleKit = BattleKit.Load(_dataDir, _ships,
                new ContentBattleGraphics(new BattleSheets(sheets, battleShapes), planetTypes));
            var scripts = ConversationScripts.Load(_dataDir);
            _talks = TalkLibrary.Load(content, scripts);
            var reports = Crateris.Core.Story.StoryWorlds.Reports.ToList();
            if (_options.ShowReport is { } shown && !reports.Contains(shown))
                reports.Add(shown);
            foreach (string report in reports)
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
                _commView.UseContent(content, sheets, scripts, _names);
                _starbaseView.UseContent(content, sheets);
                _reportView.UseContent(content, sheets);
                _sellingView.UseContent(content, sheets);
                _givingView.UseContent(content, sheets);
                _devicesView.UseContent(content, sheets);
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
            ModuleCostScale = _systems.StoryRules?.ModuleCostScale ?? 50,
        };
        _player.Logged += Log;
        _player.TradeClosed += TradeClosed;
        if (_options.ShowReport is { } report)
            _player.ShowReport(new Crateris.Core.Planetside.LanderReport(report));
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
        // A testing start (--start-battle and the like) is a new game, and leaves the default save alone.
        _keepDefaultSave = _options.StartsNewGame && _options.SavePath == null;
        string? load = _options.LoadPath ?? (File.Exists(_savePath) && _options.SavePath == null && !_options.StartsNewGame ? _savePath : null);
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
        _host.SaveRequested += () =>
        {
            _player?.Notify(SaveGame() ? "Game saved." : "This game isn't saved.");
        };
        _host.World.TalkLibrary = _talks;
        _host.World.BattleKit = _battleKit;
        _host.World.DialogueLocks = DialogueLocks.Load(_dataDir);
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

    /// <summary>--start-at, --start-orbit and the other --start options (testing): a notice says if they can't be done.</summary>
    private void ApplyStartOptions(HostSession host)
    {
        try
        {
            if (_options.StartAt is { } at)
                Log(host.StartAt(at.X, at.Y));
            if (_options.StartOrbit is { } o)
                Log(host.StartInOrbit(o.Star, o.Planet, o.Moon));
            if (_options.StartVisit)
                Log(host.StartVisit());
            if (_options.StartMinerals is { } minerals)
                Log(host.StartWithMinerals(minerals));
            if (_options.StartUnits is { } units)
                Log(host.StartWithUnits(units));
            if (_options.StartDevices is { } devices)
                Log(host.StartWithDevices(devices));
            if (_options.StartDocked)
                Log(host.StartDocked());
            if (_options.StartBattle is { } race)
                Log(host.StartBattle(race));
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PageTalk(int round, int lines, int page) => _host?.GuestPageTalk(Multiplayer.GetRemoteSenderId(), round, lines, page);

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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void GiveGift(byte[] gift)
    {
        if (_host == null)
            return;
        long peer = Multiplayer.GetRemoteSenderId();
        var refusal = _host.GuestGive(peer, gift);
        if (refusal != TradeRefusal.None && _host.InGame(peer))
            RpcId(peer, MethodName.GiftRefused, (int)refusal);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void GiftRefused(int refusal) => _player?.TradeRefused((TradeRefusal)refusal);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowGift(byte[] gift)
    {
        try
        {
            _player?.ShowGift(Gift.Decode(gift));
        }
        catch (InvalidDataException e)
        {
            Log($"Bad gift from the host: {e.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StarbaseKey(int key) => _host?.GuestStarbaseKey(Multiplayer.GetRemoteSenderId(), key);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RenameShip(string ship, string captain) => _host?.GuestRename(Multiplayer.GetRemoteSenderId(), ship, captain);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StarbaseRedraw() => _host?.GuestStarbaseRedraw(Multiplayer.GetRemoteSenderId());

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void DrawStarbase(byte[] drawing)
    {
        try
        {
            _starbaseView.Receive(StarbaseDrawing.Decode(drawing));
        }
        catch (InvalidDataException e)
        {
            Log($"Bad starbase drawing from the host: {e.Message}");
            _starbaseView.Lose();   // drawn afresh, asked for as the screen next shows
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowStarbase(byte[] view)
    {
        try
        {
            _player?.ShowStarbase(StarbaseView.Decode(view));
        }
        catch (InvalidDataException e)
        {
            Log($"Bad starbase screen from the host: {e.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowReport(string resource, string foundBy) =>
        _player?.ShowReport(new Crateris.Core.Planetside.LanderReport(resource, foundBy == "" ? null : foundBy));

    // The host's save; none once the game is over (the save before it is kept,
    // decision 12), nor over the default save from a testing start.
    private bool SaveGame()
    {
        if (_keepDefaultSave || _host?.SaveJson() is not { } json)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, json);
        return true;
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
            // Nor while the lander report has them (UQM waits for its keys).
            var keys = _about.Visible || ReportHasKeys || SellingHasKeys ? ShipInput.None
                : ReadKeys(_player.Screen == PlayerScreen.Planetside, _player.Me?.Activity == Activity.Battle);
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
                if (inGame)
                {
                    foreach (var gift in _player.TakeGifts())
                        if (_host.Give(gift) is var why && why != TradeRefusal.None)
                            _player.TradeRefused(why);
                    foreach (var key in _player.TakeStarbaseKeys())
                        _host.StarbaseKey(_host.HostId, key);
                    if (_player.TakeRename() is var (ship, captain))
                        _host.Rename(_host.HostId, ship, captain);
                }
                var snapshot = _host.Step(input);
                _player.Observe(snapshot);
                _player.ShowTalk(_host.TalkFor(_host.HostId));
                _starbaseView.Receive(_host.HostStarbaseDrawing());
                _player.ShowStarbase(_host.StarbaseFor(_host.HostId));
                // Each guest gets its own: a delta from what it has, or in full.
                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer)
                {
                    foreach (long peer in _host.GuestPeers)
                        RpcId(peer, MethodName.ReceiveSnapshot, _host.SnapshotFor(peer));
                    foreach (var (peer, talk) in _host.TalkUpdates())
                        RpcId(peer, MethodName.ShowTalk, talk);
                    // The drawing before the screen it's for (both reliable, in order).
                    foreach (var (peer, drawing) in _host.StarbaseDrawings())
                        RpcId(peer, MethodName.DrawStarbase, drawing);
                    foreach (var (peer, view) in _host.StarbaseUpdates())
                        RpcId(peer, MethodName.ShowStarbase, view);
                }
                var (ownGifts, guestGifts) = _host.TakeGifts();
                foreach (var gift in ownGifts)
                    _player.ShowGift(gift);
                if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer)
                {
                    foreach (var (peer, gift) in guestGifts)
                        RpcId(peer, MethodName.ShowGift, gift.Encode());
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
                foreach (var key in _player.TakeStarbaseKeys())
                    RpcId(1, MethodName.StarbaseKey, (int)key);
                foreach (var gift in _player.TakeGifts())
                    RpcId(1, MethodName.GiveGift, gift.Encode());
                if (_player.TakeRename() is var (ship, captain))
                    RpcId(1, MethodName.RenameShip, ship ?? "", captain ?? "");
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
        _player?.Advance(delta, _player.Scripted || _about.Visible || ReportHasKeys || SellingHasKeys ? HeldKeys.None : HeldArrows());
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
    // fires, Esc takes off (a key of its own, never leaving orbit); in a
    // battle the same, Esc retreating where allowed, and Shift the special
    // (the guests' Special key is item 11's); elsewhere Space and Enter are
    // the action key.
    private ShipInput ReadKeys(bool onSurface, bool inBattle)
    {
        var input = ShipInput.None;
        if (Held(Key.Left))
            input |= ShipInput.Left;
        if (Held(Key.Right))
            input |= ShipInput.Right;
        if (Held(Key.Up))
            input |= ShipInput.Thrust;
        bool select = Held(Key.Enter) || Held(Key.Space);
        if (onSurface || inBattle)
        {
            if (select || Held(Key.Ctrl))
                input |= ShipInput.Fire;
            if (Held(Key.Escape))
                input |= ShipInput.TakeOff;
            if (inBattle && Held(Key.Shift))
                input |= ShipInput.Special;
        }
        else if (select)
            input |= ShipInput.Action;
        return input;
    }

    // A key held for the game: not one taken by another screen (TakenKeys) and held since.
    private bool Held(Key key) => Input.IsKeyPressed(key) && _taken.Free((long)key);

    /// <summary>Whether the lander report is up and has the player's keys (shown, and not over: <see cref="ReportView.Active"/>).</summary>
    private bool ReportHasKeys => _reportView.Visible && _reportView.Active;

    /// <summary>Whether the minerals' count-down is up and has the player's keys (until it's put away).</summary>
    private bool SellingHasKeys => _sellingView.Visible && _sellingView.Active;

    // The keys down (physical, with their key), for a count-down starting while one is held: UQM's input sees it held.
    private readonly Dictionary<Key, Key> _down = new();

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Echo: false } k)
        {
            // Under the about screen, every key going down is taken, its buttons' keys too (the GUI has
            // them first); and a key coming up is let go here, before a button can keep it.
            if (k.Pressed && _about.Visible)
                _taken.Take((long)k.Keycode);
            if (!k.Pressed)
                _taken.Up((long)k.Keycode);
            if (k.Pressed)
                _down[k.PhysicalKeycode] = k.Keycode;
            else
                _down.Remove(k.PhysicalKeycode);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_running)
            return;
        // The lander report takes every key while it's up, going down and
        // coming up (UQM waits for any key), but the about screen's.
        // A key coming up goes to a running report wherever it went down, so it's never left
        // held (the report would wait for it for ever); a key held down repeating goes nowhere.
        if (e is InputEventKey { Pressed: false } up)
        {
            _taken.Up((long)up.Keycode);
            if (_reportView.Active)
                _reportView.KeyUp(up.PhysicalKeycode);   // the key itself, whatever the layout
            if (_sellingView.Active)
                _sellingView.KeyUp(up.PhysicalKeycode);
        }
        // The minerals' count-down takes every key but F1 while it's up, as the report does (any such key sells the rest at once).
        if (e is InputEventKey sk && SellingHasKeys && !_about.Visible && sk.Keycode != Key.F1 && _player is { Scripted: false })
        {
            if (sk.Pressed && !sk.Echo)
            {
                _sellingView.KeyDown(sk.PhysicalKeycode);
                _taken.Take((long)sk.Keycode);
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey rk && ReportHasKeys && !_about.Visible && rk.Keycode != Key.F1
                && _player is { Scripted: false })
        {
            if (rk.Pressed && !rk.Echo)
            {
                _reportView.KeyDown(rk.PhysicalKeycode);
                _taken.Take((long)rk.Keycode);
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is not InputEventKey { Pressed: true } k)
            return;
        // F1 opens and closes the about screen; Esc closes it too.
        if ((k.Keycode == Key.F1 || (_about.Visible && k.Keycode == Key.Escape)) && !k.Echo)
        {
            _about.Visible = k.Keycode == Key.F1 && !_about.Visible;
            _taken.Take((long)k.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_about.Visible || _player == null || _player.Scripted || _naming is not null)
            return;
        // Held since it went down into the about screen or the report (its Esc mustn't leave orbit).
        if (!_taken.Pass((long)k.Keycode, k.Echo))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // T opens and closes the giving screen (item 9).
        if (k.Keycode == Key.T && !k.Echo && (_host != null || _connected))
        {
            _player.ToggleTrade();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Docked, outfitting: F2 and F3 rename the ship and the captain (not over the giving screen, which has the keys).
        if (k.Keycode is Key.F2 or Key.F3 && _player.Starbase is { Screen: StarbaseScreen.Outfit } && _player.Trade is null)
        {
            StartNaming(k.Keycode == Key.F2);
            GetViewport().SetInputAsHandled();
            return;
        }
        MenuKey? key = k.Keycode switch
        {
            Key.Up => MenuKey.Up,
            Key.Down => MenuKey.Down,
            Key.Left => MenuKey.Left,
            Key.Right => MenuKey.Right,
            Key.Enter or Key.KpEnter or Key.Space => MenuKey.Select,
            Key.Escape or Key.Backspace => MenuKey.Cancel,
            Key.Pageup => MenuKey.PageUp,
            Key.Pagedown => MenuKey.PageDown,
            _ => null,
        };
        // A held Enter mustn't say one reply after another.
        if (key == MenuKey.Select && k.Echo && _player.Talk is not null)
            return;
        // The communication screen takes UQM's keys first (skipping the
        // lines, playing them again, the summary); the replies' are the session's.
        if (key is MenuKey ck && _player.Screen == PlayerScreen.Comm && _commView.Press(ck) != CommKeyResult.Replies)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key is MenuKey mk)
            _player.PressMenu(mk);
    }

    private LineEdit? _naming;

    // UQM's naming (NameCaptainOrShip): a line to type the name in, Enter
    // to keep it, Esc to leave it as it was.
    private void StartNaming(bool ship)
    {
        var me = _player.Me;
        _naming = new LineEdit
        {
            Position = new Vector2(12, 320),
            Size = new Vector2(320, 32),
            MaxLength = ShipNames.MaxBytes,
            PlaceholderText = ship ? "The ship's name" : "The captain's name",
            Text = ship ? me?.ShipName ?? "" : me?.CaptainName ?? "",
        };
        _naming.TextSubmitted += text =>
        {
            if (text.Trim() != "")
                _player.Rename(ship ? text.Trim() : null, ship ? null : text.Trim());
            EndNaming();
        };
        _naming.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape } esc)
            {
                _naming?.AcceptEvent();   // not the menu's Esc too, nor, repeating, the line's own (it stops editing)
                if (esc.Echo)
                    return;
                if (_about.Visible)
                {
                    _about.Visible = false;   // the about screen's, over the name line
                    _taken.Take((long)Key.Escape);
                }
                else
                    EndNaming();
            }
        };
        _screens.AddChild(_naming);
        _naming.GrabFocus();
    }

    // The Enter or Esc that put the name line away, held, isn't the menu's (TakenKeys).
    private void EndNaming()
    {
        foreach (var key in new[] { Key.Enter, Key.KpEnter, Key.Escape })
            if (Input.IsKeyPressed(key))
                _taken.Take((long)key);
        _naming?.QueueFree();
        _naming = null;
    }

    /// <summary>The arrow keys held, which move the landing site while it's picked.</summary>
    private HeldKeys HeldArrows() =>
        (Held(Key.Up) ? HeldKeys.Up : 0)
        | (Held(Key.Down) ? HeldKeys.Down : 0)
        | (Held(Key.Left) ? HeldKeys.Left : 0)
        | (Held(Key.Right) ? HeldKeys.Right : 0);

    /// <summary>Shows the screen the session picks, with what it holds.</summary>
    private void ShowViews(PlayerSession player)
    {
        var screen = player.Screen;
        _view.Show(player.Snapshot, player.PlayerId, player.Entries);
        _view.Visible = screen == PlayerScreen.Hyperspace;
        _systemView.Visible = screen == PlayerScreen.System;
        _orbitView.Visible = screen == PlayerScreen.Orbit;
        _planetsideView.Visible = screen == PlayerScreen.Planetside;
        // The player's own lander report, over the scan map's area of the planet-side and orbit views.
        _reportView.Visible = PlayerSession.ShowsReports(screen);
        if (Headless)
            player.TakeOwnReport();   // nothing to show it on (the session has logged it)
        else if (player.OwnReportFor(screen, _reportView.Ready) is { } reportText)
            _reportView.Show(reportText);
        _commView.Visible = screen == PlayerScreen.Comm;
        if (_commView.Visible)
        {
            // The player's own reply sold their minerals: counted down before the commander's lines (SellMinerals).
            long linesFrom = 0;
            if (player.Talk is { } talk)
            {
                _sellingView.Follow(talk.LinesKey);
                if (talk.Sale is { } sale && talk.Replier == player.PlayerId && !Headless)
                {
                    // A key held as it starts is the count-down's, as a key pressed then would be (TakeHeld),
                    // and the game's again only once it's come up; taken as it's to start (if it can't be drawn, till they come up all the same).
                    if (_sellingView.CanCount(talk.LinesKey))
                    {
                        var seen = _taken.TakeHeld(_down.Select(d => ((long)d.Key, (long)d.Value)),
                            _about.Visible || player.Scripted, (long)Key.F1);
                        _sellingView.Count(sale, talk.LinesKey, seen.Select(k => (Key)k).ToList());
                    }
                }
                linesFrom = _sellingView.LinesFrom(talk.LinesKey);
            }
            _commView.Show(player, linesFrom);
        }
        else
        {
            _commView.Close();
            _sellingView.Stop();
        }
        _sellingView.Visible = _commView.Visible && _sellingView.Active;
        _starbaseView.Visible = screen == PlayerScreen.Starbase;
        _starbaseView.Show(player.Starbase);
        if (_planetsideView.Visible)
            _planetsideView.Show(player);
        if (player.Snapshot != null && _systemView.Visible)
            _systemView.Show(player.Snapshot, player.PlayerId);
        // The giving screen, while it's open, over whichever screen it was opened on: below it seven lines fit
        // above the lander's gauges, more elsewhere (the orbit menu, which hasn't the keys meanwhile, isn't
        // drawn). One that can't be drawn is closed.
        bool gauges = screen == PlayerScreen.Planetside || (screen == PlayerScreen.Orbit && player.OrbitMenu is { Picking: true });
        _givingView.MostLines = gauges ? 7 : 12;
        _givingView.Visible = !Headless && _givingView.Show(player);
        _orbitView.MenuCovered = _givingView.Visible;
        // The Manifest's Devices list, while it's open in orbit. One that can't be drawn is closed.
        _devicesView.Visible = !Headless && screen == PlayerScreen.Orbit && _devicesView.Show(player);
        if (_devicesView.Broken && player.OrbitMenu?.DeviceList is not null)
        {
            player.CloseDevices();   // not a key: in a story pause the keys are the talk's
            player.Notify("The Devices list couldn't be drawn.");
        }
        if (_givingView.Broken && player.Trade is not null)
        {
            player.ToggleTrade();
            player.Notify("The giving screen couldn't be drawn.");
        }
    }

    // The keys held as the giving screen closes (the Esc that closed it, an arrow as a talk closed it) are taken
    // there and then, the game's again only once they've come up: their repeats mustn't leave outfitting, the
    // Manifest, or move a reply (a guest's talk comes between frames, before the next key's handled).
    private void TradeClosed() =>
        _taken.TakeHeld(_down.Select(d => ((long)d.Key, (long)d.Value)), shut: true, except: (long)Key.F1);

    private void UpdateHud()
    {
        if (_player != null)
            _player.Status = _status;
        _hud.Text = _player == null ? _status : string.Join("\n", _player.StatusLines());
        // Those screens stand alone; the cargo display covers the status area, as UQM's does (the
        // Manifest's, and the minerals' count-down's), and the giving screen, laid out after it, and the
        // Manifest's Devices list.
        // (the cargo display only on the orbit screen: a story talk leaves it open, under the communication screen)
        bool box = (_player?.Screen == PlayerScreen.Orbit && _player.OrbitMenu?.CargoOpen == true) || _devicesView.Visible;
        _hud.Visible = _firstRun == null && _contentError == null && !box && !_sellingView.Visible && !_givingView.Visible;
        _boxNotice.Text = _player?.Notice ?? "";
        _boxNotice.Visible = _firstRun == null && _contentError == null && box && !_sellingView.Visible && !_givingView.Visible
            && _boxNotice.Text != "";
    }

    // ---------------------------------------------------- quit / report

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            SaveGame();
        else if (what == NotificationApplicationFocusOut)
        {
            _reportView?.LetGo();   // the keys held come up elsewhere
            _sellingView?.LetGo();
            _down.Clear();
            _taken.Clear();
        }
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
                talk = _player?.Talk,
                story_pause = _player?.Snapshot?.StoryPause,
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
