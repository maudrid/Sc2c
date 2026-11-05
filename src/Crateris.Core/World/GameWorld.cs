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
using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Core.World;

public enum JoinResult
{
    /// <summary>First time in this game: a new escort undocks from the host.</summary>
    Joined,
    /// <summary>Back again: the player's ship returns where they left it.</summary>
    Rejoined,
    /// <summary>Already connected.</summary>
    AlreadyPresent,
    /// <summary>The game already has the most players allowed.</summary>
    Full,
}

/// <summary>
/// One ship as clients see it. Hyperspace fields (LogX .. Facing) are where
/// the ship is in hyperspace, or the star it will come out at while in a
/// system; VelocityX/Y is always hyperspace velocity. System fields are set
/// while in a system: the star, the inner system's planet, the ship's place
/// in the system (IpX/IpY), on the screen (Screen*), its facing and velocity
/// there (IpFacing, IpVelocityX/Y, in UQM's interplanetary units), the outer
/// view's zoom, and the world orbited. Thrusting is whether the ship is
/// thrusting (in hyperspace) or thrust last frame (in a system); the views
/// don't draw it, as UQM shows thrust only in battle (ion trails). ViewX and
/// ViewY are the ship's place in its hyperspace view, which lags the ship at
/// speed.
/// Clients generate systems themselves from the star number.
/// </summary>
public sealed record ShipSnapshot(
    string Id, string Name, bool IsHost, ShipKind Kind,
    int LogX, int LogY, int UniverseX, int UniverseY,
    int Facing, int VelocityX, int VelocityY,
    uint Fuel, uint FuelCapacity, bool Thrusting, bool UndockPending,
    Activity Activity = Activity.Hyperspace,
    int? SystemStar = null, int? InnerPlanet = null,
    int IpX = 0, int IpY = 0, int ScreenX = 0, int ScreenY = 0, int IpFacing = 0, int ZoomRadius = 0,
    int? OrbitPlanet = null, int? OrbitMoon = null, int IpVelocityX = 0, int IpVelocityY = 0,
    short ViewX = Crateris.Core.Hyperspace.HyperFlight.CenterX, short ViewY = Crateris.Core.Hyperspace.HyperFlight.CenterY,
    StarEntryState? Entry = null, HoldSnapshot? Hold = null);

/// <summary>A world someone is orbiting, with its taken nodes (the masks of <see cref="PlanetWorldState"/>).</summary>
public sealed record OrbitedWorldSnapshot(int Star, int Planet, int? Moon, uint MineralTaken, uint EnergyTaken, uint BiologicalTaken);

/// <summary>
/// The game at a frame: the date, the ships of the players present, the
/// worlds being orbited (with their taken nodes) and the surfaces with
/// landers on them. A guest is sent only its own lander's surface.
/// </summary>
public sealed record WorldSnapshot(long Frame, int Year, int Month, int Day, IReadOnlyList<ShipSnapshot> Ships,
    IReadOnlyList<OrbitedWorldSnapshot>? OrbitedWorlds = null, IReadOnlyList<SurfaceSnapshot>? Surfaces = null);

/// <summary>
/// The host's authoritative game: world state (clock, planets) and every
/// player's state, present or away. Each player is in one activity
/// (<see cref="World.Activity"/>). The world steps at 24 frames a second,
/// hyperspace's rate; players in systems take 30 system frames a second,
/// one or two per world frame (five every four), so both match UQM's
/// rates frame for frame. Players whose landers are on a world's surface
/// share that world's one surface (<see cref="PlanetsideSurface"/>), which
/// takes 35 surface frames a second, one or two a world frame. Not
/// thread-safe; the server calls it from one thread.
/// </summary>
public sealed class GameWorld
{
    private readonly List<Player> _players = new();
    private readonly Dictionary<string, InputBuffer> _inputs = new();
    private readonly HashSet<string> _actionHeld = new();   // the action key was down last frame
    // Each world with landers on it has one surface, in the order they were made.
    private readonly List<(WorldId World, PlanetsideSurface Surface)> _surfaces = new();
    private readonly Dictionary<Lander, Player> _landerPlayers = new();
    private readonly Dictionary<Lander, LanderInput> _landerInput = new();

    public GameSettings Settings { get; }
    public GameClock Clock { get; }
    public long Frame { get; private set; }

    /// <summary>Planets and moons: taken nodes, shared by every player.</summary>
    public PlanetWorldState Planets { get; }

    /// <summary>The star map and its systems, when the world has them (always in the game; optional in tests).</summary>
    public SystemCatalog? Systems { get; }

    public IReadOnlyList<Player> Players => _players;

    public Player Host => _players.Single(p => p.IsHost);

    /// <summary>The sprites' shapes, which decide contact with stars and worlds (with <see cref="Systems"/>).</summary>
    public ContactShapes? Shapes { get; }

    /// <summary>A world of hyperspace only, without systems (tests).</summary>
    public GameWorld(GameSettings settings, GameClock clock, long frame = 0)
        : this(settings, clock, frame, null, null, loading: false)
    {
    }

    /// <summary>A world with its systems, which needs the sprites' shapes from the content (content stage, decision 4).</summary>
    public GameWorld(GameSettings settings, GameClock clock, long frame, SystemCatalog systems, ContactShapes shapes)
        : this(settings, clock, frame, systems ?? throw new ArgumentNullException(nameof(systems)),
            shapes ?? throw new ArgumentNullException(nameof(shapes)), loading: false)
    {
    }

    /// <summary>For <see cref="HostSave"/>: systems and shapes both, or neither.</summary>
    internal GameWorld(GameSettings settings, GameClock clock, long frame, SystemCatalog? systems, ContactShapes? shapes, bool loading)
    {
        if ((systems is null) != (shapes is null))
            throw new ArgumentException("systems and the sprites' shapes go together");
        ShipHold.Check(settings.EscortLoadout);   // a guest's escort is made from it
        Settings = settings;
        Clock = clock;
        Frame = frame;
        Systems = systems;
        Shapes = shapes;
        Planets = new PlanetWorldState(systems);
    }

    /// <summary>A new game with the host in the flagship, in hyperspace at the start position.</summary>
    public static GameWorld NewGame(GameSettings settings, NewGameData data, string hostId, string hostName) =>
        NewGame(new GameWorld(settings, new GameClock(data.Year, data.Month, data.Day)), data, hostId, hostName);

    /// <summary>A new game with its systems and the shapes that decide contact.</summary>
    public static GameWorld NewGame(GameSettings settings, NewGameData data, string hostId, string hostName,
        SystemCatalog systems, ContactShapes shapes) =>
        NewGame(new GameWorld(settings, new GameClock(data.Year, data.Month, data.Day), 0, systems, shapes), data, hostId, hostName);

    private static GameWorld NewGame(GameWorld world, NewGameData data, string hostId, string hostName)
    {
        var ship = new PlayerShip(ShipKind.Flagship, data.Flagship);
        ship.Flight.PlaceAtUniverse(data.StartUniverseX, data.StartUniverseY);
        ship.Flight.ResetFuelCounters();
        ship.Flight.StarJustLeft = data.StartStar;
        world.AddPlayer(new Player(hostId, hostName, isHost: true, ship) { Present = true });
        return world;
    }

    /// <summary>Restore a player from a save (not present until they rejoin).</summary>
    public void AddPlayer(Player player)
    {
        if (_players.Any(p => p.Id == player.Id))
            throw new ArgumentException($"Player {player.Id} is already in the game.");
        if (player.IsHost && _players.Any(p => p.IsHost))
            throw new ArgumentException("The game already has a host.");
        _players.Add(player);
        _inputs[player.Id] = new InputBuffer();
    }

    public Player? Find(string id) => _players.FirstOrDefault(p => p.Id == id);

    public JoinResult Join(string id, string name)
    {
        var existing = Find(id);
        if (existing is { Present: true })
            return JoinResult.AlreadyPresent;
        if (_players.Count(p => p.Present) >= Settings.MaxPlayers)
            return JoinResult.Full;

        if (existing != null)
        {
            existing.Name = name;
            existing.Present = true;
            _inputs[id].Clear();
            if (existing.Ship.InputHeldUntil >= Frame)
                _inputs[id].DropThrough(existing.Ship.InputHeldUntil);   // back during its entry
            return JoinResult.Rejoined;
        }

        // First join: a new escort at the host's position, shown undocking
        // from the flagship to this player only (docs/differences.md row 14).
        // In a system the escort undocks there; with the host in orbit, in
        // the inner system next to the host's world (stage 2, decision 6).
        var hostShip = Host.Ship;
        var host = hostShip.Flight;
        var ship = new PlayerShip(ShipKind.Escort, Settings.EscortLoadout);
        ship.Flight.LogX = host.LogX;
        ship.Flight.LogY = host.LogY;
        ship.Flight.Facing = host.Facing;
        ship.Flight.StarJustLeft = host.StarJustLeft;   // undocking on a star doesn't drop into it
        ship.Flight.ResetFuelCounters();
        if (hostShip.Ip is { } hostIp)
        {
            ship.SystemStar = hostShip.SystemStar;
            ship.Ip = hostIp.JoinAt(ship.Loadout.Thrusters, ship.Loadout.Jets);
            // While the flagship's entry into the star plays, the escort
            // goes in with it: it waits, following the flagship's entry
            // (only the flagship's is drawn), and both arrive when it ends.
            if (hostShip.Entry is { } e && e.PlayingAt(Frame))
                ship.Entry = e with { Following = Host.Id };
        }
        AddPlayer(new Player(id, name, isHost: false, ship) { Present = true, UndockPending = true });
        if (ship.Entry is not null)
            HoldInput(ship, hostShip.InputHeldUntil);
        return JoinResult.Joined;
    }

    /// <summary>
    /// Leave or disconnect: the ship vanishes and waits, stopped, in the save
    /// until the player rejoins.
    /// </summary>
    public void Leave(string id)
    {
        var player = Find(id);
        if (player == null)
            return;
        Withdraw(player);   // decision 5: its lander takes off at once, with its crew and cargo
        player.Present = false;
        _inputs[id].Clear();
        _actionHeld.Remove(id);
        player.Ship.Flight.Stop();
        player.Ship.Ip?.Stop();   // in orbit it stays in orbit
    }

    /// <summary>The client has shown the undocking scene.</summary>
    public void UndockShown(string id)
    {
        if (Find(id) is { } p)
            p.UndockPending = false;
    }

    public void SubmitInput(string id, long frame, ShipInput input)
    {
        if (Find(id) is { Present: true })
            _inputs[id].Submit(frame, input);
    }

    /// <summary>The surfaces with landers on them, by world, in the order they were made.</summary>
    public IReadOnlyList<(WorldId World, PlanetsideSurface Surface)> Surfaces => _surfaces;

    /// <summary>The surface of a world, if any lander is on it.</summary>
    public PlanetsideSurface? SurfaceOf(WorldId world) =>
        _surfaces.FirstOrDefault(s => s.World == world).Surface;

    /// <summary>Surface frames (35 a second) to run in world frame <paramref name="frame"/> (24 a second).</summary>
    public static int SurfaceFramesIn(long frame) =>
        (int)(frame * PlanetsideSurface.FrameRate / GameClock.FramesPerSecond
              - (frame - 1) * PlanetsideSurface.FrameRate / GameClock.FramesPerSecond);

    /// <summary>System frames (30 a second) to run in world frame <paramref name="frame"/> (24 a second).</summary>
    public static int SystemFramesIn(long frame) =>
        (int)(frame * IpFlight.FrameRate / GameClock.FramesPerSecond
              - (frame - 1) * IpFlight.FrameRate / GameClock.FramesPerSecond);

    /// <summary>Advance the whole game one world frame.</summary>
    public void Step()
    {
        Frame++;
        int systemFrames = SystemFramesIn(Frame);
        foreach (var p in _players)
        {
            if (!p.Present)
                continue;
            // Input stamped while an entry into a star played is dropped
            // by the buffer (HoldInput), however late it arrives.
            ShipInput input = _inputs[p.Id].Take(Frame);
            // The action key acts when pressed, not while held.
            bool actionPressed = (input & ShipInput.Action) != 0 && !_actionHeld.Contains(p.Id);
            if ((input & ShipInput.Action) != 0)
                _actionHeld.Add(p.Id);
            else
                _actionHeld.Remove(p.Id);
            switch (p.Ship.Activity)
            {
                case Activity.Hyperspace:
                    StepHyperspace(p.Ship, input);
                    break;
                case Activity.Orbit:
                    if (actionPressed)
                        LeaveOrbit(p.Ship);
                    break;
                case Activity.Planetside:
                    // The surfaces step after everyone's input is in.
                    _landerInput[p.Ship.Lander!] = ToLanderInput(input);
                    break;
                default:
                    // While the entry into the star plays, the ship can't be
                    // flown and its system hasn't loaded yet, as in UQM.
                    if (p.Ship.Entry is { } entry)
                    {
                        if (entry.PlayingAt(Frame))
                            break;
                        p.Ship.Entry = null;
                    }
                    for (int i = 0; i < systemFrames && p.Ship.Activity is Activity.OuterSystem or Activity.InnerSystem; ++i)
                        StepSystem(p.Ship, input);
                    break;
            }
        }
        StepSurfaces();
        Clock.Tick(Settings.SecondsPerGameDay);
    }

    // The keys on the surface: turn, thrust, fire, take off.
    private static LanderInput ToLanderInput(ShipInput input) =>
        (LanderInput)(int)(input & (ShipInput.Left | ShipInput.Right | ShipInput.Thrust))
        | ((input & ShipInput.Fire) != 0 ? LanderInput.Fire : 0)
        | ((input & ShipInput.TakeOff) != 0 ? LanderInput.TakeOff : 0);

    /// <summary>
    /// Every surface's frames for this world frame, each lander with its
    /// player's input; what's taken is taken from the world for everyone;
    /// a lander that has gone back (or been lost) leaves its ship in orbit,
    /// and a surface with no landers left goes (the next landing there
    /// starts a new one, its creatures where UQM would put them again).
    /// </summary>
    private void StepSurfaces()
    {
        int frames = SurfaceFramesIn(Frame);
        for (int k = 0; k < _surfaces.Count; ++k)
        {
            var (world, surface) = _surfaces[k];
            for (int i = 0; i < frames && surface.Landers.Count > 0; ++i)
            {
                surface.Step(l => _landerInput.GetValueOrDefault(l));
                foreach (var (scan, node) in surface.Taken)
                {
                    if (node < PlanetWorldState.MaxNodes)
                        Planets.SetRetrieved(world, scan, node);
                }
                foreach (var gone in _landerPlayers.Keys.Where(l => l.Phase == LanderPhase.Gone).ToList())
                    LanderGone(gone);
            }
        }
        _surfaces.RemoveAll(s => s.Surface.Landers.Count == 0);
        _landerInput.Clear();
    }

    private void LanderGone(Lander lander)
    {
        if (_landerPlayers.Remove(lander, out var player) && player.Ship.Lander == lander)
            player.Ship.Lander = null;
        _landerInput.Remove(lander);
    }

    // A player's lander off its surface at once, back to the ship.
    private void Withdraw(Player player)
    {
        if (player.Ship.Lander is not { } lander)
            return;
        var surface = _surfaces.First(s => s.Surface.Landers.Contains(lander));
        surface.Surface.Withdraw(lander);
        LanderGone(lander);
        if (surface.Surface.Landers.Count == 0)
            _surfaces.Remove(surface);
    }

    private void StepHyperspace(PlayerShip ship, ShipInput input)
    {
        var before = StarContact.SpritePosition(ship.Flight);
        ship.Flight.Step(input & ~ShipInput.Action);
        if (Systems is null)
            return;
        if (StarContact.Check(ship.Flight, Systems.Map, Shapes!, before) is { } star)
        {
            // The entry plays from where and how the ship touched the star
            // (init_transition); clients draw it from this.
            var f = ship.Flight;
            var entry = new StarEntryState(Frame, f.LogX, f.LogY, f.ViewX, f.ViewY, f.Facing, star.Number,
                StarEntry.Turns(f.Facing, Shapes!.HyperFlagship.Count));
            EnterSystem(ship, star);
            ship.Entry = entry;
            HoldInput(ship, Frame + entry.Steps + Settings.InputDelayFrames);
        }
    }

    /// <summary>
    /// After an entry into a star, input read while it still played on the
    /// player's screen (stamped up to its end plus the input delay) is
    /// dropped, however late it arrives, and the last input isn't repeated.
    /// </summary>
    private void HoldInput(PlayerShip ship, long through)
    {
        ship.InputHeldUntil = through;
        if (_players.FirstOrDefault(p => p.Ship == ship) is { } player)
            _inputs[player.Id].DropThrough(through);
    }

    private void StepSystem(PlayerShip ship, ShipInput input)
    {
        IpFlight ip = ship.Ip!;
        switch (ip.Step(input & ~ShipInput.Action))
        {
            case IpEvent.EnteredOrbit:
                if (!ip.OrbitData!.Entered)
                    ip.ReturnFromOrbit();   // UQM starts a conversation here; none exist yet (stage 3)
                break;
            case IpEvent.LeftSystem:
                SystemTransition.Leave(ship.Flight, Systems!.Map.Stars[ship.SystemStar!.Value], ip.Facing);
                ship.Ip = null;
                ship.SystemStar = null;
                break;
        }
    }

    private void EnterSystem(PlayerShip ship, Star star)
    {
        ship.Entry = null;
        // ExploreSolarSys: the hyperspace position becomes the star's, so the
        // ship comes back out on it.
        ship.Flight.PlaceAtUniverse(star.X, star.Y);
        ship.Flight.Stop();
        ship.SystemStar = star.Number;
        ship.Ip = new IpFlight(Systems![star.Number], SystemTransition.Arrive(star),
            ship.Loadout.Thrusters, ship.Loadout.Jets, Shapes!);
    }

    private static void LeaveOrbit(PlayerShip ship) => ship.Ip!.ReturnFromOrbit();

    /// <summary>
    /// Puts a player straight into orbit around a world, as if they had
    /// flown there (for testing and debugging: --start-orbit). Checks the
    /// world first and changes nothing if it can't be orbited.
    /// </summary>
    public void PutInOrbit(string id, int star, int planet, int? moon = null)
    {
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        if (Systems is null)
            throw new InvalidOperationException("this world has no systems");
        if (!Systems.HasWorld(star, planet, moon))
            throw new ArgumentException($"star {star} has no planet {planet}" + (moon is int m ? $" moon {m}" : ""));
        if (!Systems[star].EnterOrbit(planet, moon).Entered)
            throw new ArgumentException($"star {star} planet {planet} moon {moon}: that world can't be orbited");

        Withdraw(player);
        PlayerShip ship = player.Ship;
        // Put straight there: no entry plays, so no input is held for one.
        ship.Entry = null;
        ship.InputHeldUntil = -1;
        _inputs[id].Clear();
        if (ship.SystemStar != star)
        {
            if (ship.Ip is not null)
                ship.Ip = null;   // out of the other system, straight into this one
            EnterSystem(ship, Systems.Map.Stars[star]);
        }
        if (ship.Ip!.Orbit is not null)
            LeaveOrbit(ship);
        ship.Ip.PlaceInOrbit(planet, moon);
    }

    /// <summary>
    /// Sends a present player's lander down at a landing site
    /// (DispatchLander), in planetside map units: checks the world and the
    /// ship as DoScan does, then takes the fuel, the lander leaves with its
    /// crew and comes down on the world's surface, made now if no other
    /// lander is there, with the nodes not yet taken and the world's hazards.
    /// It starts with the next surface frame. Returns why not, changing
    /// nothing, if it can't go (a request can come after the ship has left
    /// orbit, or while its lander is down).
    /// </summary>
    public DispatchRefusal Dispatch(string id, int siteX, int siteY)
    {
        var player = Find(id) ?? throw new ArgumentException($"no player {id}");
        if (siteX < 0 || siteX >= PlanetsideMap.Width || siteY < 0 || siteY >= PlanetsideMap.Height)
            throw new ArgumentOutOfRangeException(nameof(siteX), $"the landing site ({siteX}, {siteY}) is off the map");
        PlayerShip ship = player.Ship;
        if (!player.Present || ship.Orbit is not { Entered: true } orbit || ship.Entry is not null)
            return DispatchRefusal.NotInOrbit;
        if (ship.Lander is not null || ship.Hold.LanderOut is not null)
            return DispatchRefusal.LanderOut;
        var refusal = Landing.Check(orbit, ship);
        if (refusal != DispatchRefusal.None)
            return refusal;
        if (Systems is null)
            throw new InvalidOperationException("this world has no systems");
        var world = ship.OrbitedWorld!.Value;
        var surface = SurfaceOf(world) ?? NewSurface(world, orbit);
        ship.Flight.Fuel -= Landing.FuelNeeded(orbit.Report!);
        ship.Hold.LanderLeaves();
        var lander = surface.Land(ship.Hold, siteX, siteY);
        ship.Lander = lander;
        _landerPlayers[lander] = player;
        return DispatchRefusal.None;
    }

    // A world's surface as a first lander finds it: its nodes not yet
    // taken (GeneratePlanetSide), its creatures, its hazards; its own
    // TFB_Random, seeded from the frame and the world, so no two landings
    // are alike (UQM's runs on from the program's start).
    private PlanetsideSurface NewSurface(WorldId world, OrbitData orbit)
    {
        var nodes = orbit.Nodes ?? throw new InvalidOperationException($"{world} has no surface to land on");
        var minerals = Planets.Remaining(world, ScanType.Mineral, nodes.Minerals)
            .Select(n => new SurfaceMineral(n.Node, n.Info.X, n.Info.Y, n.Info.Type, n.Info.Density));
        var life = Planets.Remaining(world, ScanType.Biological, nodes.Life)
            .Select(n => new SurfaceCreature(n.Node, n.Info.X, n.Info.Y, n.Info.Type));
        uint seed = unchecked((uint)(Frame * 1_000_003 + world.Star * 7919 + world.Planet * 131 + (world.Moon ?? -1)));
        var surface = new PlanetsideSurface(new TfbRandom(seed), minerals, Systems!.Generator.Types.ElementCategory,
            Shapes, life, Systems.Lifeforms ?? throw new InvalidOperationException("the systems were loaded without their lifeforms"),
            HazardChances.For(orbit.Report!));
        _surfaces.Add((world, surface));
        return surface;
    }

    public WorldSnapshot Snapshot()
    {
        var present = _players.Where(p => p.Present).ToList();
        var ships = present.Select(p =>
        {
            var f = p.Ship.Flight;
            var ip = p.Ship.Ip;
            var (vx, vy) = f.Velocity.Current();
            var (ivx, ivy) = ip?.Velocity.Current() ?? ((short)0, (short)0);
            return new ShipSnapshot(p.Id, p.Name, p.IsHost, p.Ship.Kind, f.LogX, f.LogY, f.UniverseX, f.UniverseY,
                f.Facing, vx, vy, f.Fuel, f.FuelCapacity,
                ip is null ? (f.Status & HyperFlight.StatusThrust) != 0 : ip.Orbit is null && ip.Thrusting,
                p.UndockPending, p.Ship.Activity, p.Ship.SystemStar, ip?.InnerPlanet,
                ip?.Location.X ?? 0, ip?.Location.Y ?? 0, ip?.Screen.X ?? 0, ip?.Screen.Y ?? 0,
                ip?.Facing ?? 0, ip?.ZoomRadius ?? 0, ip?.Orbit?.Planet, ip?.Orbit?.Moon, ivx, ivy, f.ViewX, f.ViewY,
                p.Ship.Entry is { } e && e.PlayingAt(Frame) ? e : null, p.Ship.Hold.Snapshot());
        }).ToList();

        // Worlds being orbited, with what has been taken from them, so an
        // orbiting player's scan shows what's left right now.
        var orbited = present
            .Where(p => p.Ship.Ip?.Orbit is not null)
            .Select(p => new WorldId(p.Ship.SystemStar!.Value, p.Ship.Ip!.Orbit!.Value.Planet, p.Ship.Ip.Orbit.Value.Moon))
            .Distinct()
            .Select(w => new OrbitedWorldSnapshot(w.Star, w.Planet, w.Moon,
                Planets.RetrieveMask(w, ScanType.Mineral), Planets.RetrieveMask(w, ScanType.Energy),
                Planets.RetrieveMask(w, ScanType.Biological)))
            .ToList();

        var surfaces = _surfaces.Select(s => new SurfaceSnapshot(s.World.Star, s.World.Planet, s.World.Moon,
                s.Surface.Landers.Select(l => LanderSnapshot.Of(_landerPlayers[l].Id, l)).ToList(),
                s.Surface.Elements.Where(e => e.Kind != SurfaceElementKind.Mineral).Select(SurfaceElementSnapshot.Of).ToList()))
            .ToList();
        return new WorldSnapshot(Frame, Clock.Year, Clock.Month, Clock.Day, ships, orbited, surfaces);
    }
}
