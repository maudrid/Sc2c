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
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

using Crateris.Core.Comm;

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
    StarEntryState? Entry = null, HoldSnapshot? Hold = null,
    string? ShipName = null, string? CaptainName = null, ModulesState? Modules = null);

/// <summary>A world someone is orbiting, with its taken nodes (the masks of <see cref="PlanetWorldState"/>).</summary>
public sealed record OrbitedWorldSnapshot(int Star, int Planet, int? Moon, uint MineralTaken, uint EnergyTaken, uint BiologicalTaken);

/// <summary>
/// The game at a frame: the date, the ships of the players present, the
/// worlds being orbited (with their taken nodes) and the surfaces with
/// landers on them, and the story state (made again only when it changes);
/// while the game is paused for a story talk, who replies in it
/// (<see cref="StoryPause"/>). A guest is sent only its own lander's surface.
/// </summary>
public sealed record WorldSnapshot(long Frame, int Year, int Month, int Day, IReadOnlyList<ShipSnapshot> Ships,
    IReadOnlyList<OrbitedWorldSnapshot>? OrbitedWorlds = null, IReadOnlyList<SurfaceSnapshot>? Surfaces = null,
    Story.StorySnapshot? Story = null, string? StoryPause = null);

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
    private StorySnapshot? _storySnapshot;
    private long _storyVersion;
    private StoryState? _storyOf;

    public GameSettings Settings { get; }
    public GameClock Clock { get; }
    public long Frame { get; private set; }

    /// <summary>Planets and moons: taken nodes, shared by every player.</summary>
    public PlanetWorldState Planets { get; }

    /// <summary>
    /// The story state the group shares (docs/plans/stage-3.md, item 1):
    /// made from the systems' <see cref="SystemCatalog.StoryRules"/>, so a
    /// world without them (some tests) has none.
    /// </summary>
    public StoryState? Story { get; internal set; }

    /// <summary>
    /// The talks' content (the content package's lines for the ported
    /// scripts): without it, touching a world that starts a talk leaves the
    /// ship in flight as if the talk had ended at once (docs/differences.md
    /// row 27), as in tests without the content.
    /// </summary>
    public Comm.TalkLibrary? TalkLibrary { get; set; }

    /// <summary>
    /// Whether the group has met the starbase's commander
    /// (STARBASE_VISITED before the alliance: comandr.c reads it as "met
    /// before", and nothing clears it until the alliance). The story's,
    /// so the group's. At the allied starbase the field is each player's
    /// own (decision 11).
    /// </summary>
    public bool CommanderMet { get; internal set; }

    /// <summary>
    /// The dialogue locks (stage 3a, item 7): who is in an ordinary talk
    /// with which race. The allied starbase's menu takes the commander's
    /// (item 8), and ships in space will take theirs (3b); a talk ending
    /// lets go of its lock, and a player leaving of all theirs. Null when not set
    /// (the game sets it from data/races.json; some tests don't).
    /// </summary>
    public Comm.DialogueLocks? DialogueLocks { get; set; }

    /// <summary>
    /// The visit to Earth's starbase in its opening, if any (before the
    /// starbase's menu): a story visit, one at a time. Docked players'
    /// visits are their ships' own (<see cref="PlayerShip.Visit"/>).
    /// </summary>
    public StarbaseVisit? Visit { get; private set; }

    /// <summary>
    /// Whether the game is paused for a story talk (stage 3, decisions 6
    /// and 7): the clock, every ship, the landers on their surfaces and
    /// the orbit menus stop, and everyone is shown the talk, which only
    /// its replier answers.
    /// </summary>
    public bool StoryPaused => Visit is { Story: true };

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
        if (!loading && systems?.StoryRules is { } rules)
            Story = StoryState.NewGame(rules, clock);
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
        var ship = new PlayerShip(ShipKind.Escort, Settings.EscortLoadout) { Name = ShipNames.Fit(Settings.EscortShipName) };
        ship.Flight.LogX = host.LogX;
        ship.Flight.LogY = host.LogY;
        ship.Flight.Facing = host.Facing;
        ship.Flight.StarJustLeft = host.StarJustLeft;   // undocking on a star doesn't drop into it
        ship.Flight.ResetFuelCounters();
        if (hostShip.Ip is { } hostIp)
        {
            ship.SystemStar = hostShip.SystemStar;
            ship.Ip = hostIp.JoinAt(ship.Modules.Thrusters, ship.Modules.Jets);
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
        DialogueLocks?.ReleaseAll(id);
        // The replier of a story talk leaving hands the replies on to the
        // next player in join order, the host first, so the story isn't
        // left half told (decision 7); with no one to take them, or for a
        // visit that isn't story, it's undone.
        if (Visit is { } visit && visit.Replier == player)
        {
            var next = visit.Story ? _players.FirstOrDefault(p => p.Present && p != player) : null;
            if (next is not null)
                visit.Replier = next;
            else
            {
                visit.Undo();
                AfterVisit(visit);
            }
        }
        // Docked: the visit ends as leaving the starbase does, its talk
        // with it; the ship waits in flight by the starbase.
        if (player.Ship.Visit is { Opening: false } docked)
        {
            docked.Abandon();
            AfterVisit(docked);
        }
        _ended.Remove(id);
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
        if (_wasPaused && !StoryPaused)
        {
            // Input stamped for the pause's frames that arrives late is
            // dropped too, not taken now.
            foreach (var p in _players)
                _inputs[p.Id].DropThrough(Frame - 1);
        }
        _wasPaused = StoryPaused;
        if (StoryPaused)
        {
            // Everything waits for the story (decision 7): input read now
            // is dropped, and only the visit goes on (time passing's fade).
            foreach (var p in _players.Where(p => p.Present))
            {
                _inputs[p.Id].Take(Frame);
                _actionHeld.Remove(p.Id);
            }
            Visit!.Frame();
            AfterVisit(Visit);
            return;
        }
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
                case Activity.Talk:
                    // Replies come as their own messages (ChooseReply).
                    if (p.Ship.Visit == Visit)
                    {
                        Visit!.Frame();
                        AfterVisit(Visit!);
                    }
                    break;
                case Activity.Starbase:
                    // The starbase's menu is the player's own screen: what
                    // they choose comes as its own messages.
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
                {
                    // UQM starts a talk here instead of an orbit. The ship
                    // stays by the world, in flight, while it goes on.
                    var touched = ip.Orbit;
                    ip.ReturnFromOrbit();
                    if (IsEarthsStarbase(ship.SystemStar!.Value, touched))
                        VisitStarbase(_players.First(p => p.Ship == ship));
                }
                break;
            case IpEvent.LeftSystem:
                SystemTransition.Leave(ship.Flight, Systems!.Map.Stars[ship.SystemStar!.Value], ip.Facing);
                ship.Ip = null;
                ship.SystemStar = null;
                break;
        }
    }

    // Earth's first moon is the starbase (GenerateSol_generateMoons).
    private bool IsEarthsStarbase(int star, (int Planet, int? Moon)? world) =>
        Systems!.Map.Stars[star].Special == "SOL_DEFINED" && world == (2, 0);

    /// <summary>
    /// A player touches Earth's starbase (VisitStarBase): before the
    /// alliance, the commander's talk and what follows it; with the Lunar
    /// base aboard, the starbase's opening; and once allied, the starbase's
    /// menu (docked). One opening goes on at a time: while another does, or
    /// without the talks' content, the ship stays in flight (docs/differences.md
    /// row 27); and it waits while another player talks to the commander
    /// from the menu (row 17).
    /// </summary>
    internal void VisitStarbase(Player player)
    {
        if (TalkLibrary is null || Story is null || player.Ship.Visit is not null)
            return;
        bool opening = Story.Get("STARBASE_AVAILABLE") == 0 || Story.Get("MOONBASE_ON_SHIP") != 0
            || Story.Get("CHMMR_BOMB_STATE") == 2;
        if (opening)
        {
            if (Visit is not null)
                return;
            if (DialogueLocks?.Holder("COMMANDER_CONVERSATION") is { } holder && holder != player.Id)
                return;
        }
        var visit = new StarbaseVisit(this, player, NextRound());
        if (opening)
            _ended.Clear();
        else
            _ended.Remove(player.Id);
        if (visit.Done)
        {
            _ended[player.Id] = (visit, null);
            return;
        }
        player.Ship.Visit = visit;
        if (visit.Opening)
            Visit = visit;
    }

    // Rounds count on through every visit (StarbaseVisit.FirstRound).
    private int NextRound() =>
        Math.Max(_rounds, _players.Select(p => p.Ship.Visit?.Round ?? 0).DefaultIfEmpty(0).Max());

    /// <summary>
    /// The player in a talk chooses a reply from the set offered in
    /// <paramref name="round"/> (<see cref="StarbaseVisit.Round"/>; null for
    /// whatever is offered now): the replier of the story talk, or a docked
    /// player in their talk with the commander. False, changing nothing, if
    /// they're in none, it's no reply offered, or it was for a set already
    /// answered.
    /// </summary>
    public bool ChooseReply(string id, int index, int? round = null)
    {
        if (Find(id) is not { Present: true } player)
            return false;
        var visit = Visit is { } story && story.Replier == player ? story
            : !StoryPaused && player.Ship.Visit is { Step: VisitStep.MenuTalk } own ? own
            : null;
        if (visit is null || round is int r && r != visit.Round)
            return false;
        bool chosen = visit.Choose(index);
        AfterVisit(visit);
        return chosen;
    }

    /// <summary>
    /// The replier of the story talk paged its lines on their screen
    /// (skipped them, played them again, the summary): kept with the talk
    /// as it's shown, for everyone watching to follow (decision 7). False,
    /// changing nothing, from anyone else, in an ordinary talk (no one
    /// watches it), for lines other than those shown now
    /// (<paramref name="lines"/>, their <see cref="TalkView.LinesHash"/>:
    /// a reply can replace them in the same round), or past
    /// <see cref="TalkView.MostPaging"/>.
    /// </summary>
    public bool PageTalk(string id, int round, int lines, TalkPage page)
    {
        if (Visit is not { Story: true } visit || visit.Replier.Id != id || round != visit.Round
                || !Enum.IsDefined(page) || TalkView.Of(visit) is not { } view || view.LinesHash != lines)
            return false;
        if (visit.Paging?.Key != view.LinesKey)
            visit.Paging = new TalkPaging(view.LinesKey, new List<TalkPage>());
        if (visit.Paging.Pages.Count >= TalkView.MostPaging)
            return false;
        visit.Paging.Pages.Add(page);
        return true;
    }

    /// <summary>What a docked player's request to talk to the commander came to.</summary>
    public enum CommanderTalk
    {
        /// <summary>The talk started.</summary>
        Started,

        /// <summary>Another player is talking to him (decision 8): <see cref="TalkToCommander"/> says who.</summary>
        Busy,

        /// <summary>Not docked at the starbase's menu (or the game is paused for a story).</summary>
        NotAtMenu,
    }

    /// <summary>
    /// The starbase menu's Talk: a docked player talks to the allied
    /// commander, an ordinary talk, theirs alone, under the dialogue lock
    /// (item 7): while another player talks to him, they're told who
    /// (<paramref name="holder"/>, decision 8).
    /// </summary>
    public CommanderTalk TalkToCommander(string id, out string? holder)
    {
        holder = null;
        if (StoryPaused || Find(id) is not { Present: true, Ship.Visit: { AtMenu: true } visit })
            return CommanderTalk.NotAtMenu;
        if (DialogueLocks is { } locks && !locks.TryTake("COMMANDER_CONVERSATION", id, out holder))
            return CommanderTalk.Busy;
        visit.TalkToCommander();
        AfterVisit(visit);
        return CommanderTalk.Started;
    }

    /// <summary>The starbase menu's Leave: the ship is back in flight by the starbase. False if not docked at the menu.</summary>
    public bool Depart(string id)
    {
        if (StoryPaused || Find(id) is not { Present: true, Ship.Visit: { AtMenu: true } visit } || !visit.Depart())
            return false;
        AfterVisit(visit);
        return true;
    }

    /// <summary>
    /// Outfitting or the shipyard's crew, for a player docked at the
    /// starbase's menu, by UQM's rules (<see cref="Starbase.Outfitting"/>):
    /// false, changing nothing, if refused or not docked.
    /// </summary>
    public bool Outfit(string id, Starbase.OutfitAction action)
    {
        if (StoryPaused || Story is null || Find(id) is not { Present: true, Ship.Visit.AtMenu: true } player)
            return false;
        if (!new Starbase.Outfitting(new OutfitShip(player.Ship), Story).Apply(action))
            return false;
        player.Ship.Refit();
        return true;
    }

    /// <summary>
    /// A docked player's key on their starbase screen (DoStarBase, DoOutfit,
    /// DoShipyard), run here as their client sends it. <paramref name="save"/>
    /// is set when the host chose Save/Load (a guest is refused it: only
    /// the host saves). What the key did; None if not docked or paused.
    /// </summary>
    public Starbase.OutfitOutcome StarbaseKey(string id, Crateris.Core.Systems.MenuKey key, out bool save)
    {
        save = false;
        if (StoryPaused || Story is null || Find(id) is not { Present: true, Ship.Visit: { AtMenu: true, Menu: { } m } visit } player)
            return Starbase.OutfitOutcome.None;
        var outcome = Starbase.OutfitOutcome.None;
        m.Noticed(StarbaseShipOf(player));   // what others changed, drawn before the key's own drawing
        switch (m.Screen)
        {
            case Starbase.StarbaseScreen.Menu:
                m.Busy = null;
                byte was = m.State;
                if (key is Crateris.Core.Systems.MenuKey.Left or Crateris.Core.Systems.MenuKey.Up)
                    m.State = (byte)(m.State == Starbase.StarbaseMenu.TalkCommander ? Starbase.StarbaseMenu.DepartBase : m.State - 1);
                else if (key is Crateris.Core.Systems.MenuKey.Right or Crateris.Core.Systems.MenuKey.Down)
                    m.State = (byte)(m.State == Starbase.StarbaseMenu.DepartBase ? Starbase.StarbaseMenu.TalkCommander : m.State + 1);
                if (m.State != was)
                    m.Drew(null, new() { new(Starbase.ScreenOpKind.MenuMoved, was, m.State) });   // DrawBaseStateStrings
                else if (key == Crateris.Core.Systems.MenuKey.Select)
                {
                    switch (m.State)
                    {
                        case Starbase.StarbaseMenu.TalkCommander:
                            if (TalkToCommander(id, out var holder) == CommanderTalk.Busy)
                                m.Busy = holder is null ? "" : Find(holder)?.Name ?? holder;
                            else if (visit.Step == VisitStep.MenuTalk)
                                m.Screen = Starbase.StarbaseScreen.Talk;
                            break;
                        case Starbase.StarbaseMenu.OutfitStarship:
                            m.Outfit = new Starbase.OutfitMenu(new Starbase.Outfitting(new OutfitShip(player.Ship), Story),
                                a => Outfit(id, a));
                            m.Screen = Starbase.StarbaseScreen.Outfit;
                            m.Outfit.Open();
                            m.Drew(StarbaseShipOf(player), m.Outfit.TakeOps());
                            break;
                        case Starbase.StarbaseMenu.Shipyard:
                            m.Yard = new Starbase.ShipyardMenu(new Starbase.Outfitting(new OutfitShip(player.Ship), Story),
                                a => Outfit(id, a));
                            m.Drew(StarbaseShipOf(player), m.Yard.TakeOps());
                            m.Screen = Starbase.StarbaseScreen.Shipyard;
                            break;
                        default:
                            Depart(id);
                            return Starbase.OutfitOutcome.Left;
                    }
                }
                return outcome;
            case Starbase.StarbaseScreen.Outfit:
                // GameOptions: the host saves; a guest has no save of their own.
                outcome = m.Outfit!.Key(key, canSave: player.IsHost);
                m.Drew(StarbaseShipOf(player), m.Outfit.TakeOps());   // with the ship as this key left it
                break;
            case Starbase.StarbaseScreen.Shipyard:
                outcome = m.Yard!.Key(key, canSave: player.IsHost);
                m.Drew(StarbaseShipOf(player), m.Yard.TakeOps());
                break;
            default:
                return outcome;   // the talk's replies come as their own messages
        }
        save = outcome == Starbase.OutfitOutcome.SaveLoad;
        if (outcome == Starbase.OutfitOutcome.Refused)
            m.Refusals++;
        else if (outcome == Starbase.OutfitOutcome.Left)
        {
            (m.Outfit, m.Yard, m.Screen) = (null, null, Starbase.StarbaseScreen.Menu);
            m.Opened();   // DoStarBase, not Initialized: the menu drawn afresh
        }
        return outcome;
    }

    /// <summary>
    /// What a docked player's starbase screen has drawn since the last
    /// take, in order, each with the ship as its key left it, to send to
    /// their client (empty: nothing, or not docked).
    /// </summary>
    public List<Starbase.StarbaseDrawing> TakeStarbaseDrawing(string id)
    {
        if (Find(id) is not { Ship.Visit.Menu: { } m } player || Story is null)
            return new();
        // What others changed since: only the story (the prices, the bomb) and the hold (the units, the
        // minerals) change what Noticed draws, so the ship is looked at again only when one has.
        var hold = player.Ship.Hold;
        var stamp = (Story.Version, hold.ResourceUnits, hold.MineralMass);
        if (m.NoticedAt != stamp)
        {
            m.Noticed(StarbaseShipOf(player));
            m.NoticedAt = stamp;   // after: drawing what changed clears it
        }
        return m.TakeDrawing(() => StarbaseShipOf(player));
    }

    /// <summary>A docked player's client has no picture of its starbase screen: the next drawing taken draws it afresh.</summary>
    public void RedrawStarbase(string id)
    {
        if (Find(id) is { Ship.Visit.Menu: { } m })
            m.RedrawNext();
    }

    // A player's ship and the prices as the starbase's screens draw them.
    private Starbase.StarbaseShip StarbaseShipOf(Player player)
    {
        var ship = player.Ship;
        var story = Story!;
        bool flagship = ship.Kind == ShipKind.Flagship;
        return new Starbase.StarbaseShip(ship.Modules.ModuleSlots.ToArray(), ship.Modules.DriveSlots.ToArray(),
            ship.Modules.JetSlots.ToArray(), ship.Hold.Crew, ship.Flight.Fuel, ship.Hold.Landers, ship.Hold.ResourceUnits,
            ship.Hold.MineralMass,
            // The flagship's UQM names are the client's to read (null); a guest's captain is the name they joined with.
            ship.Name ?? (flagship ? null : ""), player.CaptainName ?? (flagship ? null : ShipNames.Fit(player.Name)),
            story.Get("CHMMR_BOMB_STATE"),
            (int)(ship.Hold.Upgrades & (LanderUpgrades.BioShield | LanderUpgrades.QuakeShield
                | LanderUpgrades.LightningShield | LanderUpgrades.LavaShield)),
            story.FuelCost, story.CrewCost,
            ShipModules.Names.Select(n => story.Rules.Modules.Contains(n) ? story.ModuleCost(n) : 0).ToArray(),
            story.Rules.ModuleCostScale);
    }

    /// <summary>A docked player's starbase screen, as their client is shown it; null if not docked.</summary>
    public Starbase.StarbaseView? StarbaseFor(string id) =>
        Find(id)?.Ship.Visit is { Step: VisitStep.Menu or VisitStep.MenuTalk, Menu: { } m } ? m.View() : null;

    // What becomes of a visit after it changed: an opening that has docked
    // is no longer the story visit (its closing lines go on showing to
    // those who watched it), a talk with the commander that's over lets go
    // of his lock, and a visit that's over leaves its ship.
    private void AfterVisit(StarbaseVisit v)
    {
        _rounds = Math.Max(_rounds, v.Round);
        if (v.Step != VisitStep.MenuTalk)
            DialogueLocks?.Release("COMMANDER_CONVERSATION", v.Starter.Id);
        if (v == Visit && !v.Opening)
        {
            Visit = null;
            // Those who watched the story see its closing lines, as they
            // were: not the starter's own talks after it.
            if (!v.Undone && v.WasStory && TalkView.Of(v) is { } closing)
                foreach (var p in _players.Where(p => p.Present && p != v.Starter))
                    _ended[p.Id] = (v, closing with { Step = VisitStep.Done });
        }
        // An opening whose starter went away (the replies passed on) ends
        // as it docks: they come back in flight by the starbase.
        if (v.AtMenu && !v.Starter.Present)
            v.Abandon();
        if (v.Done)
        {
            v.Starter.Ship.Visit = null;
            // Once docked, the starter was shown its talks' closing lines on
            // the menu: leaving shows none again.
            if (!v.Undone)
                _ended[v.Starter.Id] = (v, v.Menu is null ? null
                    : TalkView.Of(v) is { } last ? last with { Step = VisitStep.Done, Lines = Array.Empty<string>() } : null);
        }
    }

    /// <summary>
    /// The story to save: as it is, with the bulletins given in a docked
    /// player's talk with the commander kept as given (ByeBye would; a
    /// loaded game has no talk to finish).
    /// </summary>
    internal StoryState.Saved StoryForSave()
    {
        var story = Story!;
        var open = _players.Select(p => p.Ship.Visit)
            .Where(v => v is { Step: VisitStep.MenuTalk, Talk: { Ended: false, Script: Comm.Scripts.StarbaseScript } })
            .Select(v => ((Comm.Scripts.StarbaseScript)v!.Talk!.Script).GivenBulletins)
            .Aggregate(0u, (a, b) => a | b);
        if (open == 0)
            return story.Save();
        var copy = StoryState.Restore(story.Rules, story.Save());
        copy.Set32("STARBASE_BULLETS0", copy.Get32("STARBASE_BULLETS0") | open);
        return copy.Save();
    }

    private readonly Dictionary<string, (StarbaseVisit Visit, TalkView? Frozen)> _ended = new();
    private int _rounds;
    private bool _wasPaused;

    /// <summary>
    /// The visit a player is shown: a story visit, to everyone; their own;
    /// or their last one, over, whose closing lines they're still shown
    /// (a story visit's to everyone who saw it). Null if none.
    /// </summary>
    public StarbaseVisit? VisitOf(string id) =>
        Visit is { Story: true } story ? story
        : Find(id)?.Ship.Visit is { } v && v.Starter.Id == id ? v
        : _ended.TryGetValue(id, out var e) ? e.Visit : null;

    /// <summary>The talk a player is shown (<see cref="VisitOf"/>): the closing lines of one they watched as they were when it ended.</summary>
    public TalkView? TalkFor(string id) =>
        Visit is { Story: true } || Find(id)?.Ship.Visit is not null || !_ended.TryGetValue(id, out var e)
            ? TalkView.Of(VisitOf(id))
            : e.Frozen ?? TalkView.Of(e.Visit);

    private void EnterSystem(PlayerShip ship, Star star)
    {
        ship.Entry = null;
        // ExploreSolarSys: the hyperspace position becomes the star's, so the
        // ship comes back out on it.
        ship.Flight.PlaceAtUniverse(star.X, star.Y);
        ship.Flight.Stop();
        ship.SystemStar = star.Number;
        ship.Ip = new IpFlight(Systems![star.Number], SystemTransition.Arrive(star),
            ship.Modules.Thrusters, ship.Modules.Jets, Shapes!);
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
        if (StoryPaused)
            return DispatchRefusal.StoryPause;
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
        string? special = Systems!.Map.Stars[world.Star].Special;
        var energy = StoryWorlds.EnergySheet(special, world, Story) is { } sheet
            ? Planets.Remaining(world, ScanType.Energy, nodes.Energy).Select(n => new SurfaceEnergy(n.Node, n.Info.X, n.Info.Y, sheet))
            : null;
        uint seed = unchecked((uint)(Frame * 1_000_003 + world.Star * 7919 + world.Planet * 131 + (world.Moon ?? -1)));
        PlanetsideSurface? surface = null;
        surface = new PlanetsideSurface(new TfbRandom(seed), minerals, Systems!.Generator.Types.ElementCategory,
            Shapes, life, Systems.Lifeforms ?? throw new InvalidOperationException("the systems were loaded without their lifeforms"),
            HazardChances.For(orbit.Report!), energy,
            (lander, node) => PickupEnergy(special, world, surface!, lander, node));
        _surfaces.Add((world, surface));
        return surface;
    }

    // A lander touched an energy node: the world's story decides. A
    // discovery's report goes to its player, and word of it to the other
    // landers there, which stay (decision 9).
    private bool PickupEnergy(string? special, WorldId world, PlanetsideSurface surface, Lander lander, int node)
    {
        if (Story is null || !StoryWorlds.PickupEnergy(special, world, Story, lander, node))
            return false;
        if (lander.Report is { } report && _landerPlayers.TryGetValue(lander, out var finder))
        {
            _reports.Add((finder.Id, report));
            foreach (var other in surface.Landers.Where(l => l != lander))
            {
                other.Told(report with { FoundBy = finder.Id });
                if (_landerPlayers.TryGetValue(other, out var told))
                    _reports.Add((told.Id, other.Report!));
            }
        }
        return true;
    }

    private readonly List<(string PlayerId, LanderReport Report)> _reports = new();

    /// <summary>
    /// A player gives another resource units, anywhere, and minerals and
    /// bio data within a solar system, the minerals up to the receiver's
    /// room (stage 3a, item 9). Both must be in the game; not while the
    /// game waits for a story talk. Given, the gift goes on the list the
    /// receivers are told of (<see cref="TakeGifts"/>).
    /// </summary>
    public TradeRefusal Give(Gift gift)
    {
        // The opening's starter's hold can be taken back (StarbaseVisit.Undo,
        // and a save is of it as before): no gifts to or from them meanwhile.
        if (StoryPaused || (Visit is { Opening: true } opening && (opening.Starter.Id == gift.From || opening.Starter.Id == gift.To)))
            return TradeRefusal.Paused;
        if (gift.From == gift.To || Find(gift.From) is not { Present: true } giver || Find(gift.To) is not { Present: true } taker)
            return TradeRefusal.NoOne;
        if (gift.Minerals.Count != ShipHold.Categories || gift.Minerals.Any(m => m < 0) || gift.BioData < 0
                || (gift.ResourceUnits == 0 && !gift.Cargo))
            return TradeRefusal.Nothing;
        var from = giver.Ship.Hold;
        var to = taker.Ship.Hold;
        if (from.ResourceUnits < gift.ResourceUnits || from.BioData < gift.BioData
                || gift.Minerals.Where((m, i) => from.Minerals[i] < m).Any())
            return TradeRefusal.NotEnough;
        if (gift.Cargo && (giver.Ship.SystemStar is not int star || taker.Ship.SystemStar != star))
            return TradeRefusal.NotHere;
        if (gift.Minerals.Sum() > to.RoomForCargo)
            return TradeRefusal.NoRoom;
        if ((ulong)to.ResourceUnits + gift.ResourceUnits > uint.MaxValue)
            return TradeRefusal.TooMuch;
        from.ResourceUnits -= gift.ResourceUnits;
        to.ResourceUnits += gift.ResourceUnits;
        from.MoveCargo(gift.Minerals, gift.BioData, -1);
        to.MoveCargo(gift.Minerals, gift.BioData, +1);
        _gifts.Add(gift);
        return TradeRefusal.None;
    }

    private readonly List<Gift> _gifts = new();

    /// <summary>The gifts given since the last call, to tell their receivers.</summary>
    public List<Gift> TakeGifts()
    {
        var all = _gifts.ToList();
        _gifts.Clear();
        return all;
    }

    /// <summary>
    /// The lander reports to show since the last call, each to its player
    /// (DoDiscoveryReport): the finder's, and the word other landers there
    /// got (decision 9).
    /// </summary>
    public List<(string PlayerId, LanderReport Report)> TakeReports()
    {
        var all = _reports.ToList();
        _reports.Clear();
        return all;
    }

    /// <summary>
    /// A present player drops a unit of an element category overboard (the
    /// Manifest's cargo display); false, changing nothing, if there's none
    /// or no such category.
    /// </summary>
    public bool DiscardCargo(string id, int category) =>
        !StoryPaused && Find(id) is { Present: true, Ship.Visit: null } player && player.Ship.Hold.Discard(category);

    // A player's ship as outfitting sees it.
    private sealed class OutfitShip : Starbase.IOutfitShip
    {
        private readonly PlayerShip _ship;

        public OutfitShip(PlayerShip ship) => _ship = ship;

        public ShipKind Kind => _ship.Kind;

        public ShipModules Modules => _ship.Modules;

        public int Crew
        {
            get => _ship.Hold.Crew;
            set => _ship.Hold.SetCrew(value);
        }

        public uint Fuel
        {
            get => _ship.Flight.Fuel;
            set => _ship.Flight.Fuel = value;
        }

        public int Landers
        {
            get => _ship.Hold.Landers;
            set => _ship.Hold.SetLanders(value);
        }

        public uint ResourceUnits
        {
            get => _ship.Hold.ResourceUnits;
            set => _ship.Hold.ResourceUnits = value;
        }

        public int MineralMass => _ship.Hold.MineralMass;
    }

    /// <summary>
    /// Renames a player's ship or captain, or both (UQM's NameCaptainOrShip,
    /// from the starbase's outfitting): each cut to UQM's 15 bytes; null
    /// leaves it as it is. False if there's no such player.
    /// </summary>
    public bool Rename(string id, string? shipName, string? captainName)
    {
        if (Find(id) is not { } player)
            return false;
        if (shipName is not null)
            player.Ship.Name = ShipNames.Fit(shipName);
        if (captainName is not null)
            player.CaptainName = ShipNames.Fit(captainName);
        if (player.Ship.Visit is { Menu: { Outfit: { } outfit } menu })
        {
            menu.Noticed(StarbaseShipOf(player));
            outfit.Renamed(shipName is not null, captainName is not null);
            menu.Drew(StarbaseShipOf(player), outfit.TakeOps());
        }
        return true;
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
                p.Ship.Entry is { } e && e.PlayingAt(Frame) ? e : null, p.Ship.Hold.Snapshot(),
                p.Ship.Name, p.CaptainName, p.Ship.Modules.Save());
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
                s.Surface.Elements.Where(e => e.Kind != SurfaceElementKind.Mineral).Select(SurfaceElementSnapshot.Of).ToList(),
                s.Surface.Taken.Where(t => t.Scan == ScanType.Mineral).Select(t => t.Node).Distinct().ToList()))
            .ToList();
        // Made again when the story changed, or is another story state altogether.
        if (Story is { } story && (_storySnapshot is null || _storyVersion != story.Version || !ReferenceEquals(_storyOf, story)))
        {
            _storySnapshot = StorySnapshot.Of(story);
            _storyVersion = story.Version;
            _storyOf = story;
        }
        return new WorldSnapshot(Frame, Clock.Year, Clock.Month, Clock.Day, ships, orbited, surfaces,
            Story is null ? null : _storySnapshot, StoryPaused ? Visit!.Replier.Id : null);
    }
}
