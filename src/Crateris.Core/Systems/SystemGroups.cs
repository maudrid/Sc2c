// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0: BuildGroups, GetGroupInfo's
// GROUP_INIT_IP and PutGroupInfo's keeping of a system's groups
// (src/uqm/grpinfo.c), NotifyOthers, ip_group_preprocess,
// ip_group_collision, flag_ship_collision, spawn_ip_group, the flagship's
// juggling of the list and DoMissions (src/uqm/ipdisp.c), the parts of
// process.c's PreProcess, ProcessCollisions and PostProcessQueue a
// system's elements go through, and the generators' groups
// (src/uqm/planets/generate/gendefault.c, gensol.c's init_probe and
// check_probe, gencol.c, genmel.c, genshof.c, genzfpscout.c, genzoq.c,
// genutw.c). Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Hyperspace;
using Crateris.Core.Story;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>
/// The groups kept with one system (docs/plans/stage-3b.md, item 4,
/// decision 7): its random draw (<see cref="RandomGroups"/>, UQM's
/// RANDGRPINFO_FILE, here one for each system), until when it's kept once
/// nobody's in it (<see cref="Expires"/>), the players who were in it and
/// haven't entered another system since (<see cref="Visitors"/>), those in
/// it (<see cref="Present"/>), and while anyone is, the groups flying
/// (<see cref="Groups"/>, ip_group_q; <see cref="Defined"/> names the
/// special system whose own groups they are, BattleGroupRef, or is null
/// for the random draw's), their elements while anyone flies there, and
/// the system's random numbers (UQM's TFB_Random, made as a player comes
/// into the empty system).
/// </summary>
public sealed class SystemDraw
{
    public SystemDraw(int star) => Star = star;

    public int Star { get; }

    /// <summary>The special system whose own groups fly (BattleGroupRef), or null: the random draw's.</summary>
    public string? Defined { get; internal set; }

    /// <summary>The random draw's groups, or null if none is kept.</summary>
    public List<GroupShips>? RandomGroups { get; internal set; }

    /// <summary>The last day the random draw is good for once nobody's in the system (PutGroupInfo: a week on), or null before one's been set.</summary>
    public GameDate? Expires { get; internal set; }

    public HashSet<string> Visitors { get; } = new();
    public HashSet<string> Present { get; } = new();

    /// <summary>The groups in the system (ip_group_q), in its order; empty while nobody's in it.</summary>
    public List<IpGroup> Groups { get; internal set; } = new();

    /// <summary>The groups' elements in the display list's order, or null while nobody flies in the system.</summary>
    public List<IpGroup>? Elements { get; internal set; }

    /// <summary>The flagship's FLIP_WAIT: frames to the next juggle of the elements' order.</summary>
    public int FlipWait { get; internal set; }

    /// <summary>The race of a battle fought in the system (EncounterRace), whose groups turn on the players as the system comes back; -1 for none.</summary>
    public int EncounterRace { get; set; } = -1;

    /// <summary>The system's random numbers (TFB_Random), while anyone's in it.</summary>
    public HyperspaceRandom? Random { get; internal set; }

    // A meeting started in this frame (START_ENCOUNTER, for the groups'
    // collisions with each other).
    internal bool MeetingThisFrame;
}

/// <summary>A player flying in a system, as its groups see them: their id and their flight.</summary>
public sealed class SystemFlyer
{
    public SystemFlyer(string id, IpFlight flight)
    {
        Id = id;
        Flight = flight;
    }

    public string Id { get; }
    public IpFlight Flight { get; }

    /// <summary>The group the flyer met this frame (EncounterGroup), once both sides' collisions have run (START_ENCOUNTER).</summary>
    public IpGroup? Met { get; internal set; }

    internal IpGroup? Meeting;
    internal bool Started;

    /// <summary>The race of a group the flyer touched this frame but may not meet (another player holds its lock), or null.</summary>
    public int? PassedThrough { get; internal set; }
}

/// <summary>
/// System groups (docs/plans/stage-3b.md, item 4): each system's groups,
/// drawn as a player comes into it with nobody there, unless it keeps an
/// unexpired draw (decision 7), the special systems' own groups first
/// (<see cref="Defined"/>, kept for good), flown each frame for the
/// players flying there (detecting and intercepting the nearest player in
/// their view, decision 4; their standing shared, decision 8), and the
/// group a player's ship touches met by that player (decision 5).
/// </summary>
public sealed class SystemGroups
{
    private const int MaxRevolutions = 5;
    private const int StationRadius = 1600, OrbitRadius = 2400;
    private const int TrackWait = 5, OrbitSpeed = 60, FlipWait = 42;
    private const int MaxBattleGroups = 32;
    private const int NameOffset = 5, NumCaptainsNames = 16;

    private readonly StoryRules _rules;
    private readonly int _drone, _urquan, _melnorme, _shofixti, _zoqfot, _spathi, _supox, _utwig, _slylandro;
    private readonly Dictionary<int, string> _homeWorld = new();

    public SystemGroups(StoryRules rules)
    {
        _rules = rules;
        _drone = rules.RaceIndex("URQUAN_DRONE_SHIP");
        _urquan = rules.RaceIndex("URQUAN_SHIP");
        _melnorme = rules.RaceIndex("MELNORME_SHIP");
        _shofixti = rules.RaceIndex("SHOFIXTI_SHIP");
        _zoqfot = rules.RaceIndex("ZOQFOTPIK_SHIP");
        _spathi = rules.RaceIndex("SPATHI_SHIP");
        _supox = rules.RaceIndex("SUPOX_SHIP");
        _utwig = rules.RaceIndex("UTWIG_SHIP");
        _slylandro = rules.RaceIndex("SLYLANDRO_SHIP");
        // BuildGroups' HomeWorld: the races always met at their homeworld.
        foreach (var (race, home) in new[]
                 {
                     ("ORZ_SHIP", "ORZ_DEFINED"), ("PKUNK_SHIP", "PKUNK_DEFINED"), ("SPATHI_SHIP", "SPATHI_DEFINED"),
                     ("SUPOX_SHIP", "SUPOX_DEFINED"), ("THRADDASH_SHIP", "THRADD_DEFINED"),
                     ("UTWIG_SHIP", "UTWIG_DEFINED"), ("VUX_SHIP", "VUX_DEFINED"), ("YEHAT_SHIP", "YEHAT_DEFINED"),
                     ("DRUUGE_SHIP", "DRUUGE_DEFINED"), ("ILWRATH_SHIP", "ILWRATH_DEFINED"),
                     ("MYCON_SHIP", "MYCON_DEFINED"), ("UMGAH_SHIP", "UMGAH_DEFINED"),
                     ("ZOQFOTPIK_SHIP", "ZOQFOT_DEFINED"),
                 })
            _homeWorld[rules.RaceIndex(race)] = home;
    }

    /// <summary>
    /// The story field holding a special system's own groups' offset (its
    /// generator's GLOBAL (BattleGroupRef) = GET_GAME_STATE_32 (field)),
    /// which the talks compare with BattleGroupRef; null for none (a system
    /// of random groups, BattleGroupRef 0).
    /// </summary>
    public static string? OffsetField(string? special) => special switch
    {
        "SOL_DEFINED" => "URQUAN_PROBE_GRPOFFS0",
        "START_COLONY_DEFINED" => "COLONY_GRPOFFS0",
        "SAMATRA_DEFINED" => "SAMATRA_GRPOFFS0",
        "SHOFIXTI_DEFINED" => "SHOFIXTI_GRPOFFS0",
        "ZOQ_SCOUT_DEFINED" => "ZOQFOT_GRPOFFS0",
        { } m when m.StartsWith("MELNORME", StringComparison.Ordinal) && m.EndsWith("_DEFINED", StringComparison.Ordinal)
            => m[..^"_DEFINED".Length] + "_GRPOFFS0",
        _ => null,
    };

    /// <summary>The systems with groups kept, by star.</summary>
    public Dictionary<int, SystemDraw> Systems { get; } = new();

    /// <summary>
    /// The special systems' own groups (the generators' GROUPS_ADD_NEW,
    /// their offsets in the game state: URQUAN_PROBE_GRPOFFS0,
    /// COLONY_GRPOFFS0, ...), by the system's special name, made on the
    /// first entry and kept for good.
    /// </summary>
    public Dictionary<string, List<GroupShips>> Defined { get; } = new();

    /// <summary>
    /// The races whose talk is ported, whose groups fly (decision 2):
    /// every other race's are drawn as UQM draws them, then kept out of
    /// the flight. The world's (GameWorld.EncounterRaces).
    /// </summary>
    public IReadOnlySet<string> Ported { get; set; } = new HashSet<string>();

    // ---- players coming and going ----

    /// <summary>
    /// A player comes into a system (their first frame there): they leave
    /// every other system's visitors (a system with none left and nobody
    /// in it loses its draw), and if nobody else is in this one, its
    /// groups are set going as InitSolarSys's initNpcs does: the special
    /// system's own, else the random draw kept, else a new draw
    /// (BuildGroups), each group given a new task (GROUP_INIT_IP).
    /// <paramref name="flight"/> is the arriving ship's (its zoom places
    /// the groups on station); <paramref name="newRandom"/> makes the
    /// system's random numbers.
    /// </summary>
    public SystemDraw Arrive(SolarSystem system, string player, IpFlight flight, GameDate today, StoryState story,
        Func<HyperspaceRandom> newRandom)
    {
        int star = system.Star.Number;
        foreach (var (s, other) in Systems.ToList())
        {
            if (s == star)
                continue;
            other.Visitors.Remove(player);
            if (other.Visitors.Count == 0 && other.Present.Count == 0)
                Systems.Remove(s);
        }
        if (!Systems.TryGetValue(star, out var d))
            Systems[star] = d = new SystemDraw(star);
        bool empty = d.Present.Count == 0;
        d.Present.Add(player);
        d.Visitors.Add(player);
        if (!empty)
            return d;
        d.Random = newRandom();
        d.Elements = null;
        d.EncounterRace = -1;
        InitNpcs(d, system, flight.ZoomRadius, today, story);
        // Decision 2: the groups of races whose talk isn't ported are drawn
        // and kept, but don't fly.
        d.Groups.RemoveAll(g => !Ported.Contains(_rules.Races[g.Race]));
        return d;
    }

    /// <summary>
    /// A player leaves a system (into hyperspace, or away from the game
    /// with their ship gone from it). The last one out keeps the draw
    /// for a week (uninitNpcs: PutGroupInfo GROUP_SAVE_IP): the groups that
    /// flew out of the system are gone from it, and the rest are kept as
    /// the draw has them, to draw new tasks on the next entry.
    /// </summary>
    public void Depart(int star, string player, GameDate today)
    {
        if (!Systems.TryGetValue(star, out var d) || !d.Present.Remove(player) || d.Present.Count > 0)
            return;
        Flush(d);
        d.Expires = today.Plus(7);
        d.RandomGroups ??= new();
        d.Groups = new();
        d.Elements = null;
        d.Random = null;
        d.EncounterRace = -1;
    }

    // FlushGroupInfo's weeding: the groups that left the system are gone
    // from it, a special system's own as dead.
    private void Flush(SystemDraw d)
    {
        foreach (var g in d.Groups.Where(g => !g.InSystem).ToList())
        {
            if (d.Defined is { } special)
            {
                if (Defined.TryGetValue(special, out var own) && own.FirstOrDefault(s => s.Id == g.GroupId) is { } dead)
                    dead.Kill();
            }
            else
                d.RandomGroups?.RemoveAll(s => s.Id == g.GroupId);
            d.Groups.Remove(g);
        }
    }

    // ---- initNpcs ----

    private void InitNpcs(SystemDraw d, SolarSystem system, int zoom, GameDate today, StoryState story)
    {
        d.Defined = null;   // InitSolarSys: BattleGroupRef = 0
        d.Groups = new();
        string? special = system.Star.Special;
        switch (special)
        {
            case "SOL_DEFINED":
                // GenerateSol_initNpcs, init_probe: the drone orbits Earth
                // until it has delivered its message.
                d.Defined = special;
                Own(d, special, _drone);
                if (story.Get("PROBE_MESSAGE_DELIVERED") == 0 && ReadDefined(d, system, zoom) && d.Groups.Count > 0)
                    PutInOrbit(d.Groups[0], 2 + 1);
                else
                    DefaultInit(d, system, zoom, today, story);
                break;
            case "START_COLONY_DEFINED":
                // GenerateColony_initNpcs: the Ur-Quan guard orbits the colony.
                d.Defined = special;
                Own(d, special, _urquan);
                DefaultInit(d, system, zoom, today, story);
                if (d.Defined is not null && d.Groups.Count > 0)
                    PutInOrbit(d.Groups[0], 0 + 1);
                break;
            case "MELNORME0_DEFINED" or "MELNORME1_DEFINED" or "MELNORME2_DEFINED" or "MELNORME3_DEFINED"
                or "MELNORME4_DEFINED" or "MELNORME5_DEFINED" or "MELNORME6_DEFINED" or "MELNORME7_DEFINED"
                or "MELNORME8_DEFINED":
                d.Defined = special;
                Own(d, special, _melnorme);
                DefaultInit(d, system, zoom, today, story);
                break;
            case "SHOFIXTI_DEFINED":
                // GenerateShofixti_initNpcs: Tanaka (or his brother), while
                // neither is recruited or lost. Their group's captain and
                // its end on leaving (SHOFIXTI_KIA) are 3b-2's.
                if (story.Get("SHOFIXTI_RECRUITED") == 0
                    && (story.Get("SHOFIXTI_KIA") == 0
                        || (story.Get("SHOFIXTI_BRO_KIA") == 0 && story.Get("MAIDENS_ON_SHIP") != 0)))
                {
                    d.Defined = special;
                    if (!Defined.ContainsKey(special) || !ReadDefined(d, system, zoom))
                    {
                        // One ship of one crew, its captain Tanaka, or Katana if Tanaka died.
                        NameCaptain(d, _shofixti, new List<int>());
                        Defined[special] = new()
                        {
                            new GroupShips(1, _shofixti, new[] { 1 },
                                new[] { NameOffset + NumCaptainsNames + (story.Get("SHOFIXTI_KIA") & 1) }),
                        };
                    }
                }
                DefaultInit(d, system, zoom, today, story);
                break;
            case "ZOQ_SCOUT_DEFINED":
                if (story.Get("MET_ZOQFOT") == 0)
                {
                    d.Defined = special;
                    Own(d, special, _zoqfot);
                }
                DefaultInit(d, system, zoom, today, story);
                break;
            case "ZOQFOT_DEFINED":
                if (story.Get("ZOQFOT_DISTRESS") != 1)
                    DefaultInit(d, system, zoom, today, story);
                break;
            case "BOMB_DEFINED":
                // GenerateUtwig_initNpcs: none at the bomb's system until it's taken.
                if (story.Get("UTWIG_BOMB") != 0)
                    DefaultInit(d, system, zoom, today, story);
                break;
            case "SAMATRA_DEFINED":
                // The Sa-Matra's guards are the endgame's.
                break;
            default:
                DefaultInit(d, system, zoom, today, story);
                break;
        }
    }

    // A special system's own group of one ship, made on the first entry
    // (CloneShipFragment, then PutGroupInfo (GROUPS_ADD_NEW, 1)).
    private void Own(SystemDraw d, string special, int race)
    {
        if (!Defined.ContainsKey(special))
            Defined[special] = new() { new GroupShips(1, race, new[] { _rules.RaceShips[race].Crew }, new[] { NameCaptain(d, race, new List<int>()) }) };
    }

    // CloneShipFragment's NameCaptain: a captain's name not yet among the
    // group's ships (PickCaptainName, past NAME_OFFSET's 5); the drone's
    // (SAMATRA_SHIP's number) none.
    private int NameCaptain(SystemDraw d, int race, List<int> taken)
    {
        if (race == _drone)
            return 0;
        int name;
        do
            name = (int)(((ushort)R(d, "NameCaptain") & (NumCaptainsNames - 1)) + NameOffset);
        while (taken.Contains(name));
        return name;
    }

    private static void PutInOrbit(IpGroup g, byte planet)
    {
        g.Task = IpGroup.InOrbit;
        g.SysLoc = planet;
        g.DestLoc = planet;
        g.LocX = 0;
        g.LocY = 0;
        g.GroupCounter = 0;
    }

    // GenerateDefault_initNpcs.
    private void DefaultInit(SystemDraw d, SolarSystem system, int zoom, GameDate today, StoryState story)
    {
        bool read = d.Defined is not null ? ReadDefined(d, system, zoom) : ReadRandom(d, system, zoom, today);
        if (!read)
        {
            d.Defined = null;
            BuildGroups(d, system, zoom, today, story);
        }
    }

    // GetGroupInfo (BattleGroupRef, GROUP_INIT_IP) for a special system's
    // own groups: the living ones get new tasks, and the random draw goes
    // (InitGroupInfo (FALSE)).
    private bool ReadDefined(SystemDraw d, SolarSystem system, int zoom)
    {
        d.Groups = new();
        foreach (var s in Defined[d.Defined!].Where(s => s.Ships.Count > 0))
            d.Groups.Add(NewTask(d, s, system, zoom));
        d.RandomGroups = null;
        return d.Groups.Count > 0;
    }

    // GetGroupInfo (GROUPS_RANDOM, GROUP_INIT_IP): the random draw, if it's
    // kept and still good, its living groups given new tasks; else it goes.
    private bool ReadRandom(SystemDraw d, SolarSystem system, int zoom, GameDate today)
    {
        d.Groups = new();
        if (d.RandomGroups is null || d.Expires is not { } expires || expires.CompareTo(today) < 0)
        {
            d.RandomGroups = null;
            return false;
        }
        foreach (var s in d.RandomGroups.Where(s => s.Ships.Count > 0))
            d.Groups.Add(NewTask(d, s, system, zoom));
        return true;
    }

    // GROUP_INIT_IP's task for one group.
    private IpGroup NewTask(SystemDraw d, GroupShips s, SolarSystem system, int zoom)
    {
        var g = new IpGroup { GroupId = s.Id, Race = s.Race, InSystem = true };
        uint r = R(d, "GetGroupInfo");
        byte task = (byte)((byte)r % IpGroup.OnStation);
        if (task == IpGroup.Flee)
            task = IpGroup.OnStation;
        g.OrbitPos = (byte)UqmMath.NormalizeFacing((byte)(r >> 16));
        int planets = system.Planets.Count;
        byte groupLoc = (byte)planets;
        if (groupLoc == 1 && task == IpGroup.Explore)
            task = IpGroup.InOrbit;
        else
            groupLoc = (byte)((byte)(r >> 8) % Math.Max(1, (int)groupLoc) + 1);
        g.DestLoc = groupLoc;
        r = R(d, "GetGroupInfo");
        g.LocX = (short)((ushort)r % 10000 - 5000);
        g.LocY = (short)((ushort)(r >> 16) % 10000 - 5000);
        g.GroupCounter = 0;
        if (task == IpGroup.Explore)
            g.GroupCounter = (ushort)(((ushort)R(d, "GetGroupInfo") % MaxRevolutions) << UqmMath.FacingShift);
        else if (task == IpGroup.OnStation)
        {
            var org = PlanetOuterLocation(system, groupLoc - 1, zoom);
            int angle = UqmMath.FacingToAngle(g.OrbitPos + 1);
            g.LocX = (short)(org.X + UqmMath.Cosine(angle, StationRadius));
            g.LocY = (short)(org.Y + UqmMath.Sine(angle, StationRadius));
            groupLoc = 0;
        }
        g.Task = task;
        g.SysLoc = groupLoc;
        return g;
    }

    /// <summary>planetOuterLocation: a planet's place in the outer system as the view at <paramref name="zoom"/> has it.</summary>
    public static IpPoint PlanetOuterLocation(SolarSystem system, int planet, int zoom) =>
        IpGeometry.DisplayToLocation(SystemLayout.OuterPlanet(system.Planets[planet], zoom), zoom);

    // BuildGroups: at most one race's groups, by the system's place in the
    // races' spheres (always the race at its homeworld).
    private void BuildGroups(SystemDraw d, SolarSystem system, int zoom, GameDate today, StoryState story)
    {
        var star = system.Star;
        var fleets = story.Fleets;
        int bestIndex = 0, bestPercent = 0;
        int mission = story.Get("UTWIG_SUPOX_MISSION");
        bool missionAway = mission > 1 && mission < 5;
        for (int index = 0; index < fleets.Count; ++index)
        {
            var fleet = fleets[index];
            int i = (byte)_rules.Fleets[index].InterplanetaryPercent;
            if (index == _slylandro)
                i = (byte)(i * story.Get("SLYLANDRO_MULTIPLIER"));
            ushort radius = (ushort)fleet.ActualStrength;
            if (radius == 0 || i == 0)
                continue;
            if (_homeWorld.TryGetValue(index, out var home) && !(missionAway && (index == _utwig || index == _supox))
                && star.Special == home)
            {
                bestIndex = index;
                bestPercent = home is "SPATHI_DEFINED" or "SUPOX_DEFINED" ? 2 : 70;
                break;
            }
            radius = radius == _rules.InfiniteRadius
                ? (ushort)((Units.MaxXUniverse + 1) << 1)
                : (ushort)((radius * _rules.SphereRadiusIncrement) >> 1);
            short dx = (short)Math.Abs((short)(star.X - fleet.LocX));
            short dy = (short)Math.Abs((short)(star.Y - fleet.LocY));
            uint d2 = (uint)(dx * dx) + (uint)(dy * dy);
            if ((ushort)dx < radius && (ushort)dy < radius && d2 < (uint)radius * radius)
            {
                if (fleet.ActualStrength != _rules.InfiniteRadius)
                    i = (ushort)(70 - (ushort)((long)UqmMath.SquareRoot(d2) * 60 / radius));
                uint r = R(d, "BuildGroups");
                if ((ushort)r % 100 < i && (bestPercent == 0 || (ushort)(r >> 16) % (i + bestPercent) < i))
                {
                    if (fleet.ActualStrength == _rules.InfiniteRadius)
                        i = 4;
                    bestPercent = i;
                    bestIndex = index;
                }
            }
        }

        d.RandomGroups = new();
        d.Expires = today.Plus(7);
        if (bestPercent != 0)
        {
            int numGroups = (ushort)R(d, "BuildGroups") % (bestPercent >> 1) + 1;
            if (numGroups > MaxBattleGroups)
                numGroups = MaxBattleGroups;
            else if (numGroups < 5 && _homeWorld.TryGetValue(bestIndex, out var home)
                     && !(missionAway && (bestIndex == _utwig || bestIndex == _supox)) && star.Special == home)
                numGroups = 5;
            var rules = _rules.Fleets[bestIndex];
            int crew = _rules.RaceShips[bestIndex].Crew;
            int which = 0;
            do
            {
                var ships = new List<int>();
                var captains = new List<int>();
                for (int k = rules.MakeupMax; k > 0; --k)
                {
                    if (k <= rules.MakeupMin || (ushort)R(d, "BuildGroups") % 100 < 50)
                    {
                        captains.Add(NameCaptain(d, bestIndex, captains));
                        ships.Add(crew);
                    }
                }
                d.RandomGroups.Add(new GroupShips(++which, bestIndex, ships, captains));
            } while (--numGroups > 0);
        }
        ReadRandom(d, system, zoom, today);
    }

    // ---- the frame ----

    private uint R(SystemDraw d, string place) => (d.Random ??= new HyperspaceRandom(0x53595354u ^ (uint)d.Star)).Next(place);

    /// <summary>
    /// One frame of a system's groups (RedrawQueue's elements after the
    /// flagships have moved), for the players flying there this frame, in
    /// the game's order (each <see cref="SystemFlyer.Flight"/> stepped
    /// already): their elements made if nobody flew there last frame
    /// (DoMissions), each group flown (ip_group_preprocess), the touches
    /// found (ProcessCollisions over the masks, flag_ship_collision,
    /// ip_group_collision), then the elements' frame ended
    /// (PostProcessQueue). With nobody flying the groups keep no elements
    /// and stay where they are. The flyers who met a group come back with
    /// <see cref="SystemFlyer.Met"/>.
    /// </summary>
    public void Frame(SystemDraw d, SolarSystem system, IReadOnlyList<SystemFlyer> flyers, ContactShapes shapes)
    {
        if (flyers.Count == 0)
        {
            d.Elements = null;
            return;
        }
        var ctx = new FrameContext(this, d, system, flyers);
        d.MeetingThisFrame = false;
        if (d.Elements is null)
        {
            // DoMissions: the groups in the system get elements, in ip_group_q's order.
            d.Elements = new();
            d.FlipWait = FlipWait;
            foreach (var g in d.Groups.Where(g => g.InSystem))
            {
                int radius = ctx.Radius(g.SysLoc, ctx.Reference(g));
                var at = IpGeometry.LocationToDisplay(new IpPoint(g.LocX, g.LocY), radius);
                g.Element = new IpGroupElement
                {
                    Flags = ElementFlags.Changing | ElementFlags.FiniteLife | ElementFlags.IgnoreVelocity,
                    Cur = at, Next = at, Point = at, Radius = radius,
                    Control = new IntersectControl(shapes.IpPoint, at.X, at.Y),
                };
                d.Elements.Add(g);
            }
        }
        if (flyers.Any(f => f.Flight.ContactAppearing) && d.EncounterRace >= 0)
        {
            // DoMissions: after a battle, the race's reinforcements.
            NotifyOthers(d, d.EncounterRace, IpGroup.InterceptPlayer);
            d.EncounterRace = -1;
        }
        if (flyers.Any(f => !f.Flight.ContactAppearing) && --d.FlipWait == 0)
        {
            // flag_ship_preprocess: the element after the flagship goes to the end of the list.
            if (d.Elements.Count >= 2)
            {
                var first = d.Elements[0];
                d.Elements.RemoveAt(0);
                d.Elements.Add(first);
            }
            d.FlipWait = FlipWait;
        }

        // The display list: the flagships (moved already), then the groups.
        var list = new List<Elem>();
        foreach (var f in flyers)
        {
            f.Met = null;
            f.Meeting = null;
            f.Started = false;
            f.PassedThrough = null;
            var fl = f.Flight;
            var flags = ElementFlags.PreProcess | ElementFlags.IgnoreVelocity;
            if (fl.ContactAppearing)
                flags |= ElementFlags.Appearing;
            if (!fl.ContactSolid)
                flags |= ElementFlags.NonSolid;
            list.Add(new Elem(null, f)
            {
                Flags = flags,
                Control = new IntersectControl(shapes.IpFlagship[fl.ContactFacing], fl.ContactFrom.X, fl.ContactFrom.Y)
                {
                    EndX = fl.ContactTo.X, EndY = fl.ContactTo.Y,
                },
                Next = fl.ContactTo, LifeSpan = 1,
            });
        }
        foreach (var g in d.Elements)
        {
            g.MetBy = null;
            list.Add(new Elem(g, null));
        }
        ctx.List = list;

        // PreProcessQueue.
        for (int i = 0; i < list.Count; ++i)
        {
            var e = list[i];
            if ((e.Flags & ElementFlags.PreProcess) == 0)
                ctx.PreProcess(e);
            if (Colliding(e) && (e.Flags & ElementFlags.Collision) == 0)
                ctx.ProcessCollisions(i + 1, e, Intersection.MaxTimeValue);
        }

        ctx.TouchMeetings();

        // PostProcessQueue.
        foreach (var e in list)
        {
            if (e.Group is not { } g)
                continue;
            var el = g.Element!;
            el.Flags &= ~ElementFlags.Collision;
            if ((el.Flags & ElementFlags.Disappearing) != 0)
            {
                d.Elements.Remove(g);
                g.Element = null;
                continue;
            }
            el.Cur = el.Next;
            el.Control.OriginX = el.Cur.X;
            el.Control.OriginY = el.Cur.Y;
            el.Control.EndX = el.Next.X;
            el.Control.EndY = el.Next.Y;
            el.Flags = (el.Flags & ~(ElementFlags.PreProcess | ElementFlags.Changing | ElementFlags.Appearing))
                       | ElementFlags.PostProcess;
        }
        foreach (var f in flyers)
        {
            if (f.Started)
                f.Met = f.Meeting;
        }
    }

    /// <summary>
    /// A player's meeting in a system starts (UninitSolarSys' GetGroupInfo
    /// (BattleGroupRef, EncounterGroup)): the groups that left the system
    /// are gone from it (the group list flushed), and the group met is read
    /// for the encounter (its ships made again, CloneShipFragment naming each
    /// captain before the group file's name is read over it). The group
    /// stays where it met the player until the meeting is over (decision 5:
    /// <see cref="IpGroup.InMeeting"/>). Returns its ships as the draw keeps
    /// them, or null if the draw has none for it.
    /// </summary>
    public GroupShips? StartMeeting(SystemDraw d, IpGroup met, string player)
    {
        Flush(d);
        met.InMeeting = player;
        if (ShipsOf(d, met) is not { } ships)
            return null;
        var taken = new List<int>();
        foreach (int captain in ships.Captains)
        {
            NameCaptain(d, met.Race, taken);
            taken.Add(captain);   // ReadShipFragment: the file's name over the one drawn
        }
        return ships;
    }

    /// <summary>
    /// A player's meeting in a system is over, as UQM's game comes back from
    /// it (InitSolarSys' reinitNpcs, the group list read: GetGroupInfo
    /// GROUP_LIST): the group met keeps the ships left
    /// (<paramref name="left"/>, each its crew and captain, in its order:
    /// npc_built_ship_q after UninitEncounter; null for all of them, as a
    /// talk without a battle leaves them) and is gone from the system with
    /// none left (dead in the draw); the groups that left the system are
    /// gone from it; at Sol a drone just met flies off (check_probe). The
    /// group met, if it stays, re-forms where it is, and the race's groups
    /// stand down (<see cref="IpGroup.IgnoreFlagship"/>), as touching it
    /// left them. After a battle (<paramref name="battleRace"/>, the
    /// encounter's race, UninitEncounter's EncounterRace), the race's groups
    /// turn on the players as one comes back into flight there (DoMissions).
    /// </summary>
    public void EndMeeting(SystemDraw d, IpGroup met, IReadOnlyList<(int Crew, int Captain)>? left, int battleRace = -1)
    {
        met.InMeeting = null;
        if (left is not null && ShipsOf(d, met) is { } ships)
        {
            if (left.Count == 0)
            {
                ships.Kill();
                d.Groups.Remove(met);
                d.Elements?.Remove(met);
                met.Element = null;
            }
            else
                ships.SetShips(left.Select(s => s.Crew), left.Select(s => s.Captain));
        }
        Flush(d);
        if (battleRace >= 0)
            d.EncounterRace = battleRace;
        if (d.Defined == "SOL_DEFINED" && d.Groups.Count > 0 && d.Groups[0] is var head
            && head.Race == _drone && (head.Task & IpGroup.ReformGroup) != 0)
        {
            head.Task = IpGroup.Flee | IpGroup.IgnoreFlagship;
            head.DestLoc = 0;
        }
    }

    /// <summary>
    /// A meeting that ended at once as a talk without a battle (the
    /// meeting's start and end together: <see cref="StartMeeting"/>, then
    /// <see cref="EndMeeting"/> keeping every ship).
    /// </summary>
    public void AfterMeeting(SystemDraw d, IpGroup met)
    {
        StartMeeting(d, met, "");
        EndMeeting(d, met, null);
    }

    /// <summary>
    /// Whether a player may meet a group of a race now (the world's: not
    /// while another player holds the race's dialogue lock, decision 5);
    /// null: always. A group a player may not meet passes through them
    /// (<see cref="SystemFlyer.PassedThrough"/>).
    /// </summary>
    public Func<string, int, bool>? CanMeet { get; set; }

    /// <summary>
    /// Whether two races' groups share a dialogue lock (the world's: the
    /// drone's with the Ur-Quan); null: each race its own.
    /// </summary>
    public Func<int, int, bool>? OneLock { get; set; }

    // A touch the flyer may meet: none while another player holds the
    // race's lock (CanMeet), nor while another flyer has met a group under
    // the same lock earlier in this frame (its meeting takes the lock as it
    // begins, after the frame: decision 5's rule, the later touch passing
    // through as the lock taken would make it).
    internal bool MayMeet(SystemFlyer flyer, IpGroup g, IReadOnlyList<SystemFlyer> flyers)
    {
        if (CanMeet is null
            || CanMeet(flyer.Id, g.Race)
               && !flyers.Any(o => o != flyer && o.Meeting is { } other
                                   && (OneLock?.Invoke(other.Race, g.Race) ?? other.Race == g.Race)))
            return true;
        flyer.PassedThrough = g.Race;
        return false;
    }

    /// <summary>A flying group's ships as the system's draw keeps them (its special system's own, or the random draw's).</summary>
    public GroupShips? ShipsOf(SystemDraw d, IpGroup g) =>
        (d.Defined is { } special ? Defined.GetValueOrDefault(special) : d.RandomGroups)?.FirstOrDefault(s => s.Id == g.GroupId);

    /// <summary>NotifyOthers: every group of the race in the system turns on the players, or stands down.</summary>
    public void NotifyOthers(SystemDraw d, int race, byte target)
    {
        foreach (var g in d.Groups)
        {
            if (g.Race != race)
                continue;
            if (target == IpGroup.InterceptPlayer)
            {
                g.Task &= unchecked((byte)~IpGroup.IgnoreFlagship);
                g.OrbitPos = g.DestLoc;
                g.DestLoc = IpGroup.InterceptPlayer;
            }
            else if (target == IpGroup.AllClear)
            {
                g.Task |= IpGroup.IgnoreFlagship;
                if (g.DestLoc == IpGroup.InterceptPlayer)
                {
                    g.DestLoc = g.OrbitPos;
                    g.OrbitPos = (byte)UqmMath.NormalizeFacing((int)R(d, "NotifyOthers"));
                }
                if ((g.Task & IpGroup.ReformGroup) == 0)
                {
                    if ((g.Task & ~IpGroup.IgnoreFlagship) != IpGroup.Explore)
                        g.GroupCounter = 0;
                    else
                        g.GroupCounter = (ushort)(((ushort)R(d, "NotifyOthers") % MaxRevolutions) << UqmMath.FacingShift);
                }
            }
            else
            {
                g.Task |= IpGroup.IgnoreFlagship;
                g.DestLoc = target;
            }
        }
    }

    private static bool Colliding(Elem e) => (e.Flags & (ElementFlags.NonSolid | ElementFlags.Disappearing)) == 0;

    // An element of the frame's display list: a group's (its element's
    // state lives on the group) or a flagship's (made each frame).
    private sealed class Elem
    {
        public Elem(IpGroup? group, SystemFlyer? flyer)
        {
            Group = group;
            Flyer = flyer;
        }

        public readonly IpGroup? Group;
        public readonly SystemFlyer? Flyer;
        private ElementFlags _flags;
        private IntersectControl? _control;
        private IpPoint _next;
        private int _life;

        public ElementFlags Flags
        {
            get => Group?.Element!.Flags ?? _flags;
            set
            {
                if (Group is not null)
                    Group.Element!.Flags = value;
                else
                    _flags = value;
            }
        }

        public IntersectControl Control
        {
            get => Group?.Element!.Control ?? _control!;
            set
            {
                if (Group is not null)
                    Group.Element!.Control = value;
                else
                    _control = value;
            }
        }

        public IpPoint Next
        {
            get => Group?.Element!.Next ?? _next;
            set
            {
                if (Group is not null)
                    Group.Element!.Next = value;
                else
                    _next = value;
            }
        }

        public int LifeSpan
        {
            get => Group?.Element!.LifeSpan ?? _life;
            set
            {
                if (Group is not null)
                    Group.Element!.LifeSpan = value;
                else
                    _life = value;
            }
        }
    }

    // One frame's work for one system.
    private sealed class FrameContext
    {
        private readonly SystemGroups _sg;
        private readonly SystemDraw _d;
        private readonly SolarSystem _system;
        private readonly IReadOnlyList<SystemFlyer> _flyers;
        public List<Elem> List = new();

        public FrameContext(SystemGroups sg, SystemDraw d, SolarSystem system, IReadOnlyList<SystemFlyer> flyers)
        {
            _sg = sg;
            _d = d;
            _system = system;
            _flyers = flyers;
        }

        // The player a group goes by (decision 4): the nearest flying in its
        // view, else the nearest in the system, measured in the outer
        // system (a player in an inner system at its planet); the first in
        // the game's order of those as near.
        public SystemFlyer Reference(IpGroup g)
        {
            SystemFlyer? best = null;
            long bestD = long.MaxValue;
            foreach (var f in _flyers.Where(f => f.Flight.View == g.SysLoc))
            {
                long dx = f.Flight.Location.X - g.LocX, dy = f.Flight.Location.Y - g.LocY, dd = dx * dx + dy * dy;
                if (dd < bestD)
                {
                    best = f;
                    bestD = dd;
                }
            }
            if (best is not null)
                return best;
            var at = Outer(g.SysLoc, g.LocX, g.LocY);
            foreach (var f in _flyers)
            {
                var p = Outer(f.Flight.View, f.Flight.Location.X, f.Flight.Location.Y);
                long dx = p.X - at.X, dy = p.Y - at.Y, dd = dx * dx + dy * dy;
                if (dd < bestD)
                {
                    best = f;
                    bestD = dd;
                }
            }
            return best!;
        }

        // A place in the outer system: a view's own, or its planet's (planetOuterLocation at the widest zoom).
        private (long X, long Y) Outer(byte view, short x, short y)
        {
            if (view == 0)
                return (x, y);
            var p = PlanetOuterLocation(_system, view - 1, IpGeometry.MaxZoomRadius);
            return (p.X, p.Y);
        }

        // zoomRadiusForLocation: the outer view's is the reference player's zoom.
        public int Radius(byte loc, SystemFlyer by) => loc == 0 ? by.Flight.ZoomRadius : IpGeometry.MaxZoomRadius;

        private bool AnyIn(byte view) => _flyers.Any(f => f.Flight.View == view);

        // PreProcess for a group's element: it dies each frame, and its
        // death_func flies it (ip_group_preprocess).
        public void PreProcess(Elem e)
        {
            var el = e.Group!.Element!;
            if (e.Group.InMeeting is not null)
            {
                // In a meeting: it stays where it met the player, untouchable,
                // until the meeting is over (decision 5).
                el.LifeSpan = 1;
                el.Next = el.Cur;
                el.Flags = (el.Flags & ~(ElementFlags.PostProcess | ElementFlags.Collision | ElementFlags.Disappearing))
                           | ElementFlags.PreProcess | ElementFlags.NonSolid;
                return;
            }
            if (el.LifeSpan == 0)
            {
                el.Flags |= ElementFlags.Disappearing;
                Fly(e.Group);
            }
            var f = el.Flags;
            if ((f & ElementFlags.Disappearing) == 0)
            {
                if (Colliding(e))
                {
                    el.Control.EndX = el.Next.X;
                    el.Control.EndY = el.Next.Y;
                }
                if ((f & ElementFlags.FiniteLife) != 0)
                    --el.LifeSpan;
            }
            el.Flags = (f & ~(ElementFlags.PostProcess | ElementFlags.Collision)) | ElementFlags.PreProcess;
        }

        private static void Adjust(int zoom, ref short dx, ref short dy)
        {
            if (zoom == IpGeometry.MinZoomRadius)
            {
                dx >>= 2;
                dy >>= 2;
            }
            else if (zoom < IpGeometry.MaxZoomRadius)
            {
                dx >>= 1;
                dy >>= 1;
            }
        }

        private IpPoint PlanetOuter(int planet, SystemFlyer by) => PlanetOuterLocation(_system, planet, by.Flight.ZoomRadius);

        // ip_group_preprocess.
        private void Fly(IpGroup g)
        {
            var el = g.Element!;
            el.Flags &= ~(ElementFlags.Disappearing | ElementFlags.NonSolid);
            ++el.LifeSpan;
            byte groupLoc = g.SysLoc;
            el.Point = new IpPoint(g.LocX, g.LocY);
            var by = Reference(g);
            int radius = Radius(groupLoc, by);
            el.Cur = IpGeometry.LocationToDisplay(el.Point, radius);
            el.Control.OriginX = el.Cur.X;
            el.Control.OriginY = el.Cur.Y;
            byte flagshipLoc = by.Flight.View;
            byte task = g.Task;

            if ((task & IpGroup.ReformGroup) != 0 && --g.GroupCounter == 0)
            {
                task &= unchecked((byte)~IpGroup.ReformGroup);
                g.Task = task;
                if ((task & ~IpGroup.IgnoreFlagship) != IpGroup.Explore)
                    g.GroupCounter = 0;
                else
                    g.GroupCounter = (ushort)(((ushort)_sg.R(_d, "ip_group_preprocess") % MaxRevolutions) << UqmMath.FacingShift);
            }

            if ((task & ~(IpGroup.IgnoreFlagship | IpGroup.ReformGroup)) == IpGroup.Flee && (task & IpGroup.IgnoreFlagship) != 0)
                el.Flags |= ElementFlags.NonSolid;

            byte targetLoc = g.DestLoc;
            if ((task & (IpGroup.IgnoreFlagship | IpGroup.ReformGroup)) == 0)
            {
                if (targetLoc == IpGroup.InterceptPlayer && task != IpGroup.Flee)
                {
                    targetLoc = flagshipLoc;
                    if (el.ThrustWait > TrackWait)
                    {
                        el.ThrustWait = 0;
                        el.Velocity.Zero();
                    }
                }
                else if (groupLoc == flagshipLoc)
                {
                    long detect = 1200;
                    if (groupLoc != 0)
                    {
                        detect *= IpGeometry.MaxZoomRadius / IpGeometry.MinZoomRadius;
                        if (g.Race == _sg._drone)
                            detect <<= 1;
                    }
                    short vx = (short)(by.Flight.Location.X - g.LocX), vy = (short)(by.Flight.Location.Y - g.LocY);
                    if ((long)vx * vx + (long)vy * vy < detect * detect)
                    {
                        el.ThrustWait = 0;
                        el.Velocity.Zero();
                        _sg.NotifyOthers(_d, g.Race, IpGroup.InterceptPlayer);
                        task = g.Task;
                        targetLoc = g.DestLoc;
                        if (targetLoc == IpGroup.InterceptPlayer)
                            targetLoc = flagshipLoc;
                    }
                }
            }

            var (vdx, vdy) = el.Velocity.Current();

            task &= unchecked((byte)~IpGroup.IgnoreFlagship);
            if (task <= IpGroup.OnStation)
            {
                bool transition = false;
                IpPoint dest;
                short deltaX, deltaY;
                ushort angle;
                if (task == IpGroup.Flee)
                    dest = new IpPoint((short)(g.LocX << 1), (short)(g.LocY << 1));
                else if (((task != IpGroup.OnStation || g.DestLoc == IpGroup.InterceptPlayer) && groupLoc == targetLoc)
                         || (task == IpGroup.OnStation && g.DestLoc != IpGroup.InterceptPlayer && groupLoc == 0))
                {
                    if (g.DestLoc == IpGroup.InterceptPlayer)
                        dest = by.Flight.Location;
                    else
                    {
                        int orbitDist;
                        IpPoint org;
                        if (task != IpGroup.OnStation)
                        {
                            orbitDist = OrbitRadius;
                            org = new IpPoint(0, 0);
                        }
                        else
                        {
                            orbitDist = StationRadius;
                            org = PlanetOuter(targetLoc - 1, by);
                        }
                        int a = UqmMath.FacingToAngle(g.OrbitPos + 1);
                        dest = new IpPoint((short)(org.X + UqmMath.Cosine(a, orbitDist)), (short)(org.Y + UqmMath.Sine(a, orbitDist)));
                        if (g.LocX == dest.X && g.LocY == dest.Y)
                        {
                            g.OrbitPos = (byte)UqmMath.NormalizeFacing(UqmMath.AngleToFacing(a));
                            a += UqmMath.FacingToAngle(1);
                            dest = new IpPoint((short)(org.X + UqmMath.Cosine(a, orbitDist)), (short)(org.Y + UqmMath.Sine(a, orbitDist)));
                            el.ThrustWait = 255;
                            byte next;
                            if (g.GroupCounter != 0)
                                --g.GroupCounter;
                            else if (task == IpGroup.Explore
                                     && (next = (byte)((ushort)_sg.R(_d, "ip_group_preprocess") % _system.Planets.Count + 1)) != targetLoc)
                            {
                                el.ThrustWait = 0;
                                targetLoc = next;
                                g.DestLoc = next;
                            }
                        }
                    }
                }
                else if (groupLoc == 0)
                    dest = g.DestLoc == IpGroup.InterceptPlayer ? by.Flight.SunLocation : PlanetOuter(targetLoc - 1, by);
                else
                {
                    if (task == IpGroup.OnStation)
                        targetLoc = 0;
                    dest = new IpPoint((short)(g.LocX << 1), (short)(g.LocY << 1));
                }

                deltaX = (short)(dest.X - g.LocX);
                deltaY = (short)(dest.Y - g.LocY);
                angle = UqmMath.ArcTan(deltaX, deltaY);

                if (el.ThrustWait != 0 && el.ThrustWait != 255)
                    --el.ThrustWait;
                else if ((vdx == 0 && vdy == 0) || angle != el.Velocity.TravelAngle)
                {
                    int speed;
                    if (el.ThrustWait != 0 && g.DestLoc != IpGroup.InterceptPlayer)
                    {
                        speed = OrbitSpeed;
                        if (task == IpGroup.OnStation)
                            speed >>= 1;
                    }
                    else
                    {
                        speed = _sg._rules.Fleets[g.Race].IpSpeed;
                        el.ThrustWait = TrackWait;
                    }
                    vdx = UqmMath.Cosine(angle, speed);
                    vdy = UqmMath.Sine(angle, speed);
                    el.Velocity.SetComponents(vdx, vdy);
                }

                short dx = vdx, dy = vdy;
                bool PartialRevolution()
                {
                    if ((long)((ushort)(dx * dx) + (ushort)(dy * dy)) >= (long)deltaX * deltaX + (long)deltaY * deltaY)
                    {
                        g.LocX = dest.X;
                        g.LocY = dest.Y;
                        vdx = 0;
                        vdy = 0;
                        el.Velocity.Zero();
                    }
                    return true;
                }
                bool CheckGetAway()
                {
                    var p = IpGeometry.LocationToDisplay(new IpPoint(g.LocX, g.LocY), radius);
                    return p.X < 0 || p.X >= IpGeometry.ScreenWidth || p.Y < 0 || p.Y >= IpGeometry.ScreenHeight;
                }

                bool toTransition = false;
                if (groupLoc == targetLoc)
                {
                    if (targetLoc == 0)
                    {
                        if (task == IpGroup.Flee)
                        {
                            transition = CheckGetAway();
                            toTransition = true;
                        }
                    }
                    else if (targetLoc == g.DestLoc)
                        PartialRevolution();
                }
                else
                {
                    if (groupLoc == 0)
                    {
                        Adjust(radius, ref dx, ref dy);
                        if (task == IpGroup.OnStation && g.DestLoc != 0)
                            PartialRevolution();
                        else
                        {
                            transition = (long)((ushort)(dx * dx) + (ushort)(dy * dy)) >= (long)deltaX * deltaX + (long)deltaY * deltaY;
                            toTransition = true;
                        }
                    }
                    else
                    {
                        transition = CheckGetAway();
                        toTransition = true;
                    }
                }

                if (toTransition && transition)
                {
                    // No collisions during the transition.
                    el.Flags |= ElementFlags.NonSolid;
                    vdx = 0;
                    vdy = 0;
                    el.Velocity.Zero();
                    if (groupLoc != 0)
                    {
                        var p = PlanetOuter(groupLoc - 1, by);
                        g.LocX = p.X;
                        g.LocY = p.Y;
                        groupLoc = 0;
                        g.SysLoc = 0;
                    }
                    else if (targetLoc == 0)
                    {
                        // Out of the system.
                        el.LifeSpan = 0;
                        el.Flags |= ElementFlags.Disappearing | ElementFlags.NonSolid;
                        g.InSystem = false;
                        return;
                    }
                    else
                    {
                        if (targetLoc == g.DestLoc)
                        {
                            g.OrbitPos = (byte)UqmMath.NormalizeFacing(UqmMath.AngleToFacing(angle + UqmMath.HalfCircle));
                            g.GroupCounter = (ushort)(((ushort)_sg.R(_d, "ip_group_preprocess") % MaxRevolutions) << UqmMath.FacingShift);
                        }
                        // Into the inner system on a circle 9/16 of the screen out.
                        var entry = new IpPoint(
                            (short)((IpGeometry.ScreenWidth >> 1) - UqmMath.Cosine(angle, IpGeometry.ScreenWidth * 9 / 16)),
                            (short)((IpGeometry.ScreenHeight >> 1) - UqmMath.Sine(angle, IpGeometry.ScreenHeight * 9 / 16)));
                        var loc = IpGeometry.DisplayToLocation(entry, IpGeometry.MaxZoomRadius);
                        g.LocX = loc.X;
                        g.LocY = loc.Y;
                        groupLoc = targetLoc;
                        g.SysLoc = targetLoc;
                    }
                }
            }

            radius = Radius(groupLoc, by);
            Adjust(radius, ref vdx, ref vdy);
            g.LocX += vdx;
            g.LocY += vdy;
            el.Next = IpGeometry.LocationToDisplay(new IpPoint(g.LocX, g.LocY), radius);
            el.Radius = radius;

            // Drawn only in a view someone flies in, and flashing as it re-forms.
            if (!AnyIn(groupLoc) || ((task & IpGroup.ReformGroup) != 0 && (g.GroupCounter & 1) != 0))
            {
                el.Drawn = false;
                el.Flags |= ElementFlags.NonSolid;
            }
            else
            {
                el.Drawn = true;
                if ((task & IpGroup.ReformGroup) != 0)
                    el.Flags |= ElementFlags.NonSolid;
            }
            el.Flags |= ElementFlags.Changing;
        }

        // ProcessCollisions: an element against those after it in the list
        // (from index), each preprocessed first if it isn't yet.
        public ElementFlags ProcessCollisions(int from, Elem e, ushort minTime)
        {
            for (int i = from; i < List.Count; ++i)
            {
                var test = List[i];
                if ((test.Flags & ElementFlags.PreProcess) == 0)
                    PreProcess(test);
                if (test == e)
                    continue;
                // CollisionPossible: flagships pass through each other.
                if (!Colliding(test) || ((test.Flags & e.Flags) & ElementFlags.Collision) != 0
                    || (test.Group is null && e.Group is null) || !OnOneScreen(e, test))
                    continue;
                var sf = e.Flags;
                var tf = test.Flags;
                var ec = OnScreenOf(e, test);
                var tc = OnScreenOf(test, e);
                ushort time;
                if (((sf | tf) & ElementFlags.FiniteLife) != 0
                    && (((sf & ElementFlags.Appearing) != 0 && e.LifeSpan > 1)
                        || ((tf & ElementFlags.Appearing) != 0 && test.LifeSpan > 1)))
                    time = 0;
                else
                    time = Intersection.DrawablesIntersect(ec, tc, minTime);
                if (time == 0)
                    continue;
                // Where they touched: only a ship's is used (a group never
                // newly gains COLLISION against a ship), on its own screen.
                var savePt = new IpPoint(ec.EndX, ec.EndY);
                var testSavePt = new IpPoint(tc.EndX, tc.EndY);
                e.Control.EndX = e.Next.X;
                e.Control.EndY = e.Next.Y;
                test.Control.EndX = test.Next.X;
                test.Control.EndY = test.Next.Y;
                if (time == 1
                    || (((sf & ElementFlags.Collision) != 0 || ProcessCollisions(i + 1, e, (ushort)(time - 1)) == 0)
                        && ((tf & ElementFlags.Collision) != 0
                            || ProcessCollisions((test.Flags & ElementFlags.Appearing) == 0 ? List.IndexOf(e) + 1 : 0,
                                test, (ushort)(time - 1)) == 0)))
                {
                    sf = e.Flags;
                    tf = test.Flags;
                    Collide(e, test);
                    Collide(test, e);
                    if ((test.Flags & ElementFlags.Collision) != 0 && (tf & ElementFlags.Collision) == 0)
                    {
                        test.Control.OriginX = testSavePt.X;
                        test.Control.OriginY = testSavePt.Y;
                        test.Next = testSavePt;
                        test.Control.EndX = test.Next.X;
                        test.Control.EndY = test.Next.Y;
                    }
                    if ((e.Flags & ElementFlags.Collision) != 0)
                    {
                        if ((sf & ElementFlags.Collision) == 0)
                        {
                            e.Control.OriginX = savePt.X;
                            e.Control.OriginY = savePt.Y;
                            e.Next = savePt;
                            e.Control.EndX = e.Next.X;
                            e.Control.EndY = e.Next.Y;
                        }
                        return ElementFlags.Collision;
                    }
                    if (!Colliding(e))
                    {
                        e.Flags |= ElementFlags.Collision;
                        return ElementFlags.Collision;
                    }
                }
            }
            return e.Flags & ElementFlags.Collision;
        }

        // Each player's own screen (UQM has one, the flagship's): a ship
        // touches only the groups in its own view, and two groups bump only
        // in one view at one zoom (in the outer view, the zooms of the
        // players they go by; its screen is the sun's at that zoom).
        private static bool OnOneScreen(Elem a, Elem b)
        {
            if (a.Group is { } ga && b.Group is { } gb)
                return ga.SysLoc == gb.SysLoc && ga.Element!.Radius == gb.Element!.Radius;
            var (g, f) = a.Group is { } group ? (group, b.Flyer!) : (b.Group!, a.Flyer!);
            return g.SysLoc == f.Flight.View;
        }

        private static int FlyerRadius(SystemFlyer f) =>
            f.Flight.InnerPlanet is null ? f.Flight.ZoomRadius : IpGeometry.MaxZoomRadius;

        // An element's control on the pair's screen: a group's tested
        // against a ship at another zoom is its own made again at the
        // ship's zoom, from where the group is in the system.
        private static IntersectControl OnScreenOf(Elem x, Elem other)
        {
            if (x.Group is not { } g || other.Flyer is not { } f || FlyerRadius(f) == g.Element!.Radius)
                return x.Control;
            int to = FlyerRadius(f);
            var c = x.Control.Copy();
            var el = g.Element;
            var here = new IpPoint(g.LocX, g.LocY);
            var origin = Rescale(el, new IpPoint(c.OriginX, c.OriginY), el.Point, here, to);
            var end = Rescale(el, new IpPoint(c.EndX, c.EndY), here, el.Point, to);
            c.OriginX = origin.X;
            c.OriginY = origin.Y;
            c.EndX = end.X;
            c.EndY = end.Y;
            return c;
        }

        // A group's point on its screen at another zoom: the place in the
        // system it shows (for the start, where the group was at the
        // frame's start; for the end, where it is now; else the other, else
        // the point's own place).
        private static IpPoint Rescale(IpGroupElement el, IpPoint p, IpPoint first, IpPoint second, int to)
        {
            var place = p == IpGeometry.LocationToDisplay(first, el.Radius) ? first
                : p == IpGeometry.LocationToDisplay(second, el.Radius) ? second
                : IpGeometry.DisplayToLocation(p, el.Radius);
            return IpGeometry.LocationToDisplay(place, to);
        }

        // A group in a meeting is untouchable (PreProcess), but a ship that
        // would touch it (solid, meeting nothing this frame) passes through
        // it, to be told who's talking (decision 5), as in hyperspace (the
        // meeting's own player, flying while others fight its battle, is
        // told nothing: the lock is let go by then).
        public void TouchMeetings()
        {
            foreach (var f in List)
            {
                if (f.Flyer is not { Started: false, PassedThrough: null } flyer || !flyer.Flight.ContactSolid)
                    continue;
                foreach (var e in List)
                {
                    if (e.Group is { InMeeting: not null } g && OnOneScreen(f, e)
                        && Intersection.DrawablesIntersect(OnScreenOf(f, e), OnScreenOf(e, f), Intersection.MaxTimeValue) != 0)
                    {
                        flyer.PassedThrough = g.Race;
                        break;
                    }
                }
            }
        }

        private void Collide(Elem e0, Elem e1)
        {
            if (e0.Group is null)
                FlagshipCollision(e0, e1);
            else
                GroupCollision(e0, e1);
        }

        // flag_ship_collision: a flyer touching a group; the meeting starts
        // once both sides' collisions have run (START_ENCOUNTER, the flyer's).
        private void FlagshipCollision(Elem f, Elem other)
        {
            var flyer = f.Flyer!;
            if (flyer.Started || (other.Group!.MetBy is { } by && by != flyer))
                return;
            if (!_sg.MayMeet(flyer, other.Group, _flyers))
                return;
            if ((other.Flags & ElementFlags.Collision) == 0)
                f.Flags |= ElementFlags.Collision | ElementFlags.NonSolid;
            else
            {
                other.Flags &= ~ElementFlags.Collision;
                Start(flyer);
            }
        }

        private void Start(SystemFlyer flyer)
        {
            flyer.Started = true;
            _d.MeetingThisFrame = true;
        }

        // ip_group_collision.
        private void GroupCollision(Elem e0, Elem e1)
        {
            var g = e0.Group!;
            var el = g.Element!;
            if (e1.Group is { } other)
            {
                // Another group: the groups don't merge into one icon.
                if (_d.MeetingThisFrame)
                    return;
                var oel = other.Element!;
                if ((el.Flags & ElementFlags.Collision) != 0 || (oel.Cur == oel.Next))
                    el.Flags &= ~ElementFlags.Collision;
                else
                {
                    oel.Flags |= ElementFlags.Collision;
                    g.LocX = el.Point.X;
                    g.LocY = el.Point.Y;
                    el.Next = el.Cur;
                    el.Control.EndX = el.Next.X;
                    el.Control.EndY = el.Next.Y;
                }
                return;
            }
            var flyer = e1.Flyer!;
            if (flyer.Started || (g.MetBy is { } by && by != flyer))
                return;
            if (!_sg.MayMeet(flyer, g, _flyers))
                return;
            // A flyer: the meeting (EncounterGroup). The group re-forms, and
            // its race's groups stand down for the time being.
            g.MetBy = flyer;
            flyer.Meeting = g;
            g.Task |= IpGroup.ReformGroup;
            g.GroupCounter = 100;
            _sg.NotifyOthers(_d, g.Race, IpGroup.AllClear);
            if ((e1.Flags & ElementFlags.Collision) == 0)
                el.Flags |= ElementFlags.Collision | ElementFlags.NonSolid;
            else
            {
                e1.Flags &= ~ElementFlags.Collision;
                Start(flyer);
            }
        }
    }
}
