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
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Godot;

namespace Crateris.Game;

/// <summary>
/// Runs the game as host or guest. The host owns a GameWorld, steps it 24
/// times a second, applies everyone's input and sends a snapshot to all
/// clients every frame. Guests only send input and draw snapshots. The
/// host plays through the same input path, with the same delay.
/// Networking is Godot's MultiplayerAPI over ENet.
/// </summary>
public partial class Main : Node2D
{
    private Options _options = null!;
    private bool _isHost;
    private GameWorld? _world;
    private string _playerId = "";
    private string _name = "";
    private string _savePath = "";
    private readonly Dictionary<long, string> _peerPlayers = new();
    private readonly List<string> _log = new();

    private WorldSnapshot? _snapshot;
    private Protocol.Welcome? _welcome;
    private int _inputDelay;
    private long _frames;
    private int _scriptStep;
    private int _scriptFrame;
    private int _undockFrames = -1;
    private bool _undockShown;
    private string _status = "";
    private double _saveTimer;

    private HyperspaceView _view = null!;
    private Label _hud = null!;

    public override void _Ready()
    {
        try
        {
            _options = Options.Parse(OS.GetCmdlineUserArgs());
        }
        catch (Exception e)
        {
            GD.PrintErr($"Bad command line: {e.Message}");
            GetTree().Quit(2);
            return;
        }

        string dataDir = DataDirectory();
        _view = new HyperspaceView(Starmap.Load(dataDir));
        AddChild(_view);
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        layer.AddChild(_hud);

        _name = _options.Name ?? System.Environment.UserName;
        _playerId = _options.PlayerId ?? LoadOrCreatePlayerId();
        _savePath = _options.SavePath ?? ProjectSettings.GlobalizePath("user://host-save.json");

        if (_options.Join)
            StartClient();
        else
            StartHost(dataDir);
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

    private void StartHost(string dataDir)
    {
        _isHost = true;
        string? load = _options.LoadPath ?? (File.Exists(_savePath) && _options.SavePath == null ? _savePath : null);
        var systems = SystemCatalog.Load(dataDir);
        if (load != null && File.Exists(load))
        {
            _world = HostSave.FromJson(File.ReadAllText(load), systems);
            _playerId = _world.Host.Id; // the save belongs to its host
            _world.Join(_playerId, _name);
            Log($"Loaded {load}");
        }
        else
        {
            var settings = new GameSettings();
            if (_options.SecondsPerDay is { } spd)
                settings = settings with { SecondsPerGameDay = spd };
            _world = GameWorld.NewGame(settings, NewGameData.Load(dataDir, systems.Map), _playerId, _name, systems);
            Log("New game");
        }
        _inputDelay = _world.Settings.InputDelayFrames;

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(_options.Port, _world.Settings.MaxPlayers - 1);
        if (err != Error.Ok)
        {
            _status = $"Could not open port {_options.Port} ({err}). Playing solo.";
            Log(_status);
        }
        else
        {
            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            _status = $"Hosting on port {_options.Port}.";
            Log(_status);
            if (_options.UseUpnp)
                OpenPortWithUpnp(_options.Port);
        }
        _snapshot = _world.Snapshot();
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
        }).ContinueWith(t => CallDeferred(MethodName.SetStatus, t.Result));
    }

    private void SetStatus(string text)
    {
        _status = text;
        Log(text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string playerId, string name, int version)
    {
        if (!_isHost || _world == null)
            return;
        long peer = Multiplayer.GetRemoteSenderId();
        JoinResult result;
        string message;
        if (version != Protocol.Version)
        {
            result = JoinResult.Full;
            message = $"Version mismatch: host {Protocol.Version}, you {version}.";
        }
        else
        {
            result = _world.Join(playerId, name);
            message = Protocol.Describe(result);
        }
        var welcome = new Protocol.Welcome(result, playerId, _world.Settings.InputDelayFrames, message);
        RpcId(peer, MethodName.Welcomed, Protocol.Encode(welcome));
        Log($"{name} ({playerId}): {result}");
        if (Protocol.Accepted(result))
            _peerPlayers[peer] = playerId;
        else
            GetTree().CreateTimer(0.5).Timeout += () => Multiplayer.MultiplayerPeer?.DisconnectPeer((int)peer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void SendInput(long frame, int input)
    {
        if (_world != null && _peerPlayers.TryGetValue(Multiplayer.GetRemoteSenderId(), out var id))
            _world.SubmitInput(id, frame, (ShipInput)(input & 7));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UndockShown()
    {
        if (_world != null && _peerPlayers.TryGetValue(Multiplayer.GetRemoteSenderId(), out var id))
            _world.UndockShown(id);
    }

    private void OnPeerDisconnected(long peer)
    {
        if (_world != null && _peerPlayers.Remove(peer, out var id))
        {
            _world.Leave(id);
            Log($"{id} left");
        }
    }

    private void SaveGame()
    {
        if (_world == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, HostSave.ToJson(_world));
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
        _status = $"Connecting to {_options.Address}:{_options.Port}...";
        Log(_status);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcomed(byte[] data)
    {
        _welcome = Protocol.Decode<Protocol.Welcome>(data);
        _inputDelay = _welcome.InputDelayFrames;
        _status = _welcome.Message;
        Log($"Welcome: {_welcome.Result}");
        if (!Protocol.Accepted(_welcome.Result))
            Fail(_welcome.Message);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void ReceiveSnapshot(byte[] data)
    {
        var snap = Protocol.Decode<WorldSnapshot>(data);
        if (_snapshot == null || snap.Frame > _snapshot.Frame)
            _snapshot = snap;
    }

    private void Fail(string message)
    {
        _status = message;
        Log(message);
        if (_options.QuitAfterFrames > 0 || _options.ReportPath != null)
            Finish(1);
    }

    // ------------------------------------------------------------ frame

    public override void _PhysicsProcess(double delta)
    {
        _frames++;
        var input = ReadInput();

        if (_isHost && _world != null)
        {
            _world.SubmitInput(_playerId, _world.Frame + _inputDelay, input);
            _world.Step();
            _snapshot = _world.Snapshot();
            if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer)
                Rpc(MethodName.ReceiveSnapshot, Protocol.Encode(_snapshot));
        }
        else if (_welcome != null && Protocol.Accepted(_welcome.Result) && _snapshot != null)
        {
            RpcId(1, MethodName.SendInput, _snapshot.Frame + _inputDelay, (int)input);
            HandleUndock();
        }

        _view.Show(_snapshot, _playerId, _undockFrames >= 0 && !_undockShown);
        UpdateHud();

        if (_options.QuitAfterFrames > 0 && _frames >= _options.QuitAfterFrames)
            Finish(0);
    }

    public override void _Process(double delta)
    {
        if (!_isHost)
            return;
        _saveTimer += delta;
        if (_saveTimer >= 60)
        {
            _saveTimer = 0;
            SaveGame();
        }
    }

    // A first-time guest sees a short undocking scene (placeholder: a
    // message for 2 seconds), then tells the host it has been shown.
    private void HandleUndock()
    {
        var me = _snapshot?.Ships.FirstOrDefault(s => s.Id == _playerId);
        if (me == null || !me.UndockPending || _undockShown)
            return;
        if (_undockFrames < 0)
        {
            _undockFrames = 0;
            Log("Undocking");
        }
        if (++_undockFrames >= 2 * GameClock.FramesPerSecond)
        {
            _undockShown = true;
            RpcId(1, MethodName.UndockShown);
        }
    }

    private ShipInput ReadInput()
    {
        if (_options.Script is { } script)
        {
            while (_scriptStep < script.Count && _scriptFrame >= script[_scriptStep].Frames)
            {
                _scriptStep++;
                _scriptFrame = 0;
            }
            if (_scriptStep >= script.Count)
                return ShipInput.None;
            _scriptFrame++;
            return script[_scriptStep].Input;
        }
        var input = ShipInput.None;
        if (Input.IsKeyPressed(Key.Left))
            input |= ShipInput.Left;
        if (Input.IsKeyPressed(Key.Right))
            input |= ShipInput.Right;
        if (Input.IsKeyPressed(Key.Up))
            input |= ShipInput.Thrust;
        return input;
    }

    private void UpdateHud()
    {
        var lines = new List<string> { "Attack on Crateris - stage 1: hyperspace", _status };
        if (_snapshot != null)
        {
            lines.Add($"Date {_snapshot.Year:D4}-{_snapshot.Month:D2}-{_snapshot.Day:D2}");
            var me = _snapshot.Ships.FirstOrDefault(s => s.Id == _playerId);
            if (me != null)
                lines.Add($"You: {me.UniverseX / 10.0:0.0} : {me.UniverseY / 10.0:0.0}   fuel {me.Fuel / 100.0:0.00} / {me.FuelCapacity / 100}");
            lines.Add($"Players ({_snapshot.Ships.Count}):");
            foreach (var s in _snapshot.Ships)
                lines.Add($"  {s.Name}{(s.IsHost ? " (host, flagship)" : " (escort)")}  {s.UniverseX / 10.0:0.0} : {s.UniverseY / 10.0:0.0}");
        }
        lines.Add("Arrows: turn and thrust");
        _hud.Text = string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
    }

    // ---------------------------------------------------- quit / report

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && _isHost)
            SaveGame();
    }

    private void Log(string message)
    {
        _log.Add($"[{_frames}] {message}");
        GD.Print(message);
    }

    private void Finish(int code)
    {
        if (_isHost)
            SaveGame();
        if (_options.ScreenshotPath != null && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(_options.ScreenshotPath);
        if (_options.ReportPath != null)
        {
            var report = new
            {
                mode = _isHost ? "host" : "client",
                player_id = _playerId,
                frames = _frames,
                welcome = _welcome?.Result.ToString(),
                status = _status,
                log = _log,
                snapshot = _snapshot,
            };
            File.WriteAllText(_options.ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Multiplayer.MultiplayerPeer?.Close();
        GetTree().Quit(code);
    }
}
