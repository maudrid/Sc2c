// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (planets/solarsys.c and ipdisp.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;

namespace Crateris.Core.Systems;

/// <summary>What happened in an interplanetary frame.</summary>
public enum IpEvent
{
    None = 0,
    Zoom = 1,
    EnteredInnerSystem = 2,
    LeftInnerSystem = 3,
    EnteredOrbit = 4,
    LeftSystem = 5,
}

/// <summary>
/// Everything needed to put an <see cref="IpFlight"/> back as it was (host
/// save). Screen images, moons and the zoom box are derived from these.
/// </summary>
public sealed record IpFlightState(
    int Facing, int[] Velocity, int LocationX, int LocationY, int ScreenX, int ScreenY,
    int ZoomRadius, int SunLocationX, int SunLocationY, int? InnerPlanet, int WaitIntersect,
    int TurnCounter, int ThrustCounter, int? OrbitPlanet, int? OrbitMoon);

/// <summary>
/// The sizes used to decide when the flagship touches a planet or moon, in
/// display pixels of the 242x227 interplanetary view. UQM tests sprite
/// masks from the content package: the flagship's sprite against a single
/// point at the world's centre for a new contact, and against the world's
/// whole image to tell when a contact has ended. Until the content stage
/// reads those masks, circles stand in for them; PlanetRadius is indexed
/// by UQM's planet image size (0..5).
/// </summary>
public sealed record IpContactShape(int ShipRadius, IReadOnlyList<int> PlanetRadius, int JunkRadius)
{
    public static readonly IpContactShape Provisional = new(3, new[] { 2, 3, 4, 6, 8, 10 }, 4);
}

/// <summary>
/// One ship flying inside a star system (UQM's interplanetary flight), at
/// 30 frames a second: the outer system with its zoom levels, a planet's
/// inner system with its moons, and touching a world to enter orbit.
/// Follows IP_frame in solarsys.c: controls, then CheckShipLocation, then
/// the flagship moves (flag_ship_preprocess).
/// </summary>
public sealed class IpFlight
{
    public const int FrameRate = 30;    // ONE_SECOND / IP_FRAME_RATE
    private const int ThrustIncrement = 8;   // IP_SHIP_THRUST_INCREMENT
    private const int TurnWaitBase = 17;     // IP_SHIP_TURN_WAIT
    private const int TurnDecrement = 2;     // IP_SHIP_TURN_DECREMENT
    private const int ThrustWait = 1;
    private const int InnerEntryDistance = WorldTypes.MinMoonRadius
        + (WorldTypes.MaxMoons - 1) * WorldTypes.MoonDelta + WorldTypes.MoonDelta / 4;
    private const ushort AllCollisionsOff = 0xFFFF;
    private const int CenterX = SystemLayout.CenterX;
    private const int CenterY = SystemLayout.CenterY;

    private readonly IpContactShape _shape;
    private readonly IpPoint[] _planetImage;
    private readonly int[] _planetImageSize;
    private IpPoint[] _moonImage = Array.Empty<IpPoint>();
    private int[] _moonImageSize = Array.Empty<int>();
    private int _innerPlanetImageSize;
    private IpRect _scaleRect;

    public SolarSystem System { get; }

    /// <summary>Facing 0..15, 0 up, clockwise (the ShipStamp frame).</summary>
    public ushort Facing { get; private set; }

    public Velocity Velocity;

    /// <summary>The ship's place in the system (ip_location): outer system units, or inner system units while in an inner system.</summary>
    public IpPoint Location { get; private set; }

    /// <summary>The ship on the screen (ShipStamp.origin).</summary>
    public IpPoint Screen { get; private set; }

    /// <summary>The outer view's zoom (SunDesc[0].radius).</summary>
    public short ZoomRadius { get; private set; }

    /// <summary>In an inner system: where the planet is in the outer system (SunDesc[0].location).</summary>
    public IpPoint SunLocation { get; private set; }

    /// <summary>The planet whose inner system the ship is in, or null in the outer system.</summary>
    public int? InnerPlanet { get; private set; }

    /// <summary>The inner system's moons (empty in the outer system).</summary>
    public IReadOnlyList<WorldDesc> Moons { get; private set; } = Array.Empty<WorldDesc>();

    /// <summary>The world being touched, so it isn't entered again until the ship clears it (WaitIntersect).</summary>
    public ushort WaitIntersect { get; private set; }

    public byte TurnCounter { get; private set; }
    public byte ThrustCounter { get; private set; }
    public byte MaxSpeed { get; }
    public byte TurnWait { get; }

    /// <summary>After <see cref="IpEvent.EnteredOrbit"/>: the world orbited (moon null for the planet itself).</summary>
    public (int Planet, int? Moon)? Orbit
    {
        get => _orbit;
        private set
        {
            _orbit = value;
            _orbitData = null;
        }
    }

    /// <summary>
    /// What entering orbit gives for that world: report, scan seeds and
    /// nodes, or <see cref="OrbitData.Entered"/> false for a world where UQM
    /// starts a conversation instead. Null when not in orbit.
    /// </summary>
    public OrbitData? OrbitData => _orbit is { } o ? (_orbitData ??= System.EnterOrbit(o.Planet, o.Moon)) : null;

    private (int Planet, int? Moon)? _orbit;
    private OrbitData? _orbitData;

    /// <summary>Where planets are on the screen in the outer view.</summary>
    public IReadOnlyList<IpPoint> PlanetScreen => _planetImage;

    /// <summary>Where moons are on the screen in the inner view.</summary>
    public IReadOnlyList<IpPoint> MoonScreen => _moonImage;

    /// <summary>Arrives from hyperspace (InitSolarSys).</summary>
    public IpFlight(SolarSystem system, SystemArrival arrival, int thrusters, int jets, IpContactShape? shape = null)
        : this(system, thrusters, jets, shape)
    {
        Facing = arrival.Facing;
        Screen = arrival.ScreenPosition;
        Location = arrival.Location;

        CheckZoomLevel();
        ValidateOrbits();
        CheckIntersect();   // ResetSolarSys
    }

    private IpFlight(SolarSystem system, int thrusters, int jets, IpContactShape? shape)
    {
        System = system;
        _shape = shape ?? IpContactShape.Provisional;
        _planetImage = new IpPoint[system.Planets.Count];
        _planetImageSize = new int[system.Planets.Count];
        MaxSpeed = (byte)((thrusters + 5) * ThrustIncrement);
        TurnWait = (byte)(TurnWaitBase - TurnDecrement * jets);
    }

    /// <summary>The state to save (see <see cref="Restore"/>).</summary>
    public IpFlightState Save() => new(
        Facing, Velocity.ToArray(), Location.X, Location.Y, Screen.X, Screen.Y, ZoomRadius,
        SunLocation.X, SunLocation.Y, InnerPlanet, WaitIntersect, TurnCounter, ThrustCounter,
        Orbit?.Planet, Orbit?.Moon);

    /// <summary>Puts a flight back as it was saved. Throws InvalidDataException if the state can't be.</summary>
    public static IpFlight Restore(SolarSystem system, IpFlightState s, int thrusters, int jets, IpContactShape? shape = null)
    {
        static void Require(bool ok, string what)
        {
            if (!ok)
                throw new InvalidDataException($"system flight: {what}");
        }

        Require(s.Facing is >= 0 and < 16, "facing");
        Require(IsZoom(s.ZoomRadius), "zoom radius");
        Require(s.InnerPlanet is null || (uint)s.InnerPlanet < (uint)system.Planets.Count, "inner planet");
        Require(s.OrbitPlanet is null || s.OrbitPlanet == s.InnerPlanet, "orbit outside its inner system");
        if (s.OrbitPlanet is int op && s.OrbitMoon is int om)
            Require((uint)om < system.Planets[op].NumPlanets, "orbit moon");
        Require(s.OrbitPlanet is not null || s.OrbitMoon is null, "orbit moon without a planet");

        var f = new IpFlight(system, thrusters, jets, shape)
        {
            Facing = (ushort)s.Facing,
            Velocity = Velocity.FromArray(s.Velocity),
            Location = new IpPoint((short)s.LocationX, (short)s.LocationY),
            Screen = new IpPoint((short)s.ScreenX, (short)s.ScreenY),
            ZoomRadius = (short)s.ZoomRadius,
            SunLocation = new IpPoint((short)s.SunLocationX, (short)s.SunLocationY),
            WaitIntersect = (ushort)s.WaitIntersect,
            TurnCounter = (byte)s.TurnCounter,
            ThrustCounter = (byte)s.ThrustCounter,
        };
        f._scaleRect = ScaleRect(f.ZoomRadius);
        f.ValidateOrbits();
        if (s.InnerPlanet is int p)
        {
            f.InnerPlanet = p;
            f.Moons = system.Moons(p);
            f.ValidateInnerOrbits();
        }
        if (s.OrbitPlanet is int orbitPlanet)
            f.Orbit = (orbitPlanet, s.OrbitMoon);
        return f;
    }

    /// <summary>
    /// A second ship at this one's place, at rest and not in orbit: a guest
    /// undocking from the host in a system. It keeps this ship's contact,
    /// so it doesn't drop into the world the host is touching.
    /// </summary>
    public IpFlight JoinAt(int thrusters, int jets) =>
        Restore(System, Save() with
        {
            Velocity = new Velocity().ToArray(), OrbitPlanet = null, OrbitMoon = null, TurnCounter = 0, ThrustCounter = 0,
        }, thrusters, jets, _shape);

    /// <summary>Comes to a stop where it is (a player leaving the game).</summary>
    public void Stop() => Velocity.Zero();

    /// <summary>
    /// Puts the ship straight into orbit around a planet or one of its moons,
    /// as if it had flown there (for testing and debugging).
    /// </summary>
    public void PlaceInOrbit(int planet, int? moon)
    {
        if ((uint)planet >= (uint)System.Planets.Count)
            throw new ArgumentOutOfRangeException(nameof(planet));
        if (InnerPlanet is not null && InnerPlanet != planet)
            LeaveInnerSystem();
        if (InnerPlanet is null)
        {
            EnterInnerSystem(planet);
            ValidateInnerOrbits();
        }
        if (moon is int m && (uint)m >= (uint)Moons.Count)
            throw new ArgumentOutOfRangeException(nameof(moon));
        Velocity.Zero();
        Screen = moon is int mm ? _moonImage[mm] : new IpPoint(CenterX, CenterY);
        Location = IpGeometry.DisplayToLocation(Screen, IpGeometry.MaxZoomRadius);
        WaitIntersect = MakeWord(planet + 1, moon is int w ? w + 2 : 1);
        Orbit = (planet, moon);
    }

    private static bool IsZoom(int r)
    {
        for (int z = IpGeometry.MaxZoomRadius; z >= IpGeometry.MinZoomRadius; z >>= 1)
        {
            if (r == z)
                return true;
        }
        return false;
    }

    /// <summary>The zoom-in box FindRadius leaves for a zoom radius (it depends only on the radius).</summary>
    private static IpRect ScaleRect(int radius)
    {
        int nextRadius = radius > IpGeometry.MinZoomRadius ? radius >> 1 : 0;
        return SystemLayout.OrbitRect(nextRadius, nextRadius, nextRadius, IpGeometry.DisplayFactor, IpGeometry.DisplayFactor >> 2, radius);
    }

    /// <summary>One frame with this frame's input.</summary>
    public IpEvent Step(ShipInput input)
    {
        if (Orbit is not null)
            throw new InvalidOperationException("in orbit: call ReturnFromOrbit first");

        ProcessShipControls(input);

        IpEvent ev = CheckShipLocation(out bool locChange, out short newRadius);
        if (ev == IpEvent.LeftSystem)
            return ev;

        if (ev == IpEvent.None && locChange)
        {
            if (InnerPlanet is not null)
            {
                ev = IpEvent.EnteredInnerSystem;
                ValidateInnerOrbits();
            }
            else if (ZoomRadius == newRadius)
            {
                ev = IpEvent.LeftInnerSystem;
                ValidateOrbits();
            }
            else
            {
                // ScaleSystem
                ev = IpEvent.Zoom;
                ZoomRadius = newRadius;
                Screen = IpGeometry.LocationToDisplay(Location, ZoomRadius);
                ValidateOrbits();
            }
        }

        MoveFlagship();
        return ev;
    }

    /// <summary>
    /// Back to flight after orbit, as EnterPlanetOrbit does: on the world,
    /// at rest, still in its inner system, with the contact remembered so
    /// the ship doesn't go straight back into orbit.
    /// </summary>
    public void ReturnFromOrbit()
    {
        if (Orbit is not { } orbit)
            throw new InvalidOperationException("not in orbit");
        var (planet, moon) = orbit;

        Screen = moon is int m ? _moonImage[m] : new IpPoint(CenterX, CenterY);
        // saveNonOrbitalLocation and LoadSolarSys: the inner system is set
        // up again around the same planet.
        Location = IpGeometry.DisplayToLocation(Screen, IpGeometry.MaxZoomRadius);
        Moons = System.Moons(planet);
        CheckZoomLevel();
        ValidateOrbits();
        ValidateInnerOrbits();
        CheckIntersect();   // ResetSolarSys
        Orbit = null;
    }

    // ---- controls ----

    private void ProcessShipControls(ShipInput input)
    {
        int dy = (input & ShipInput.Thrust) != 0 ? -1 : 0;
        int dx = 0;
        if ((input & ShipInput.Left) != 0)
            dx -= 1;
        if ((input & ShipInput.Right) != 0)
            dx += 1;

        int index = Facing;
        if (TurnCounter > 0)
            --TurnCounter;
        else if (dx != 0)
        {
            index = UqmMath.NormalizeFacing(dx < 0 ? index - 1 : index + 1);
            Facing = (ushort)index;
            TurnCounter = TurnWait;
        }
        if (ThrustCounter > 0)
            --ThrustCounter;
        else if (dy < 0)
        {
            InertialThrust(UqmMath.FacingToAngle(index));
            ThrustCounter = ThrustWait;
        }
    }

    /// <summary>flagship_inertial_thrust.</summary>
    private void InertialThrust(int currentAngle)
    {
        byte maxSpeed = MaxSpeed;
        var (curX, curY) = Velocity.Current();
        int travelAngle = Velocity.TravelAngle;
        if (travelAngle == currentAngle
            && curX == UqmMath.Cosine(currentAngle, maxSpeed)
            && curY == UqmMath.Sine(currentAngle, maxSpeed))
            return;

        short deltaX = (short)(curX + UqmMath.Cosine(currentAngle, ThrustIncrement));
        short deltaY = (short)(curY + UqmMath.Sine(currentAngle, ThrustIncrement));
        uint desired = (uint)((long)deltaX * deltaX) + (uint)((long)deltaY * deltaY);
        uint max2 = (uint)((ushort)maxSpeed * maxSpeed);
        if (desired <= max2)
            Velocity.SetComponents(deltaX, deltaY);
        else if (travelAngle == currentAngle)
            Velocity.SetComponents(UqmMath.Cosine(currentAngle, maxSpeed), UqmMath.Sine(currentAngle, maxSpeed));
        else
        {
            Velocity v = Velocity;
            v.Delta(
                UqmMath.Cosine(currentAngle, ThrustIncrement >> 1) - UqmMath.Cosine(travelAngle, ThrustIncrement),
                UqmMath.Sine(currentAngle, ThrustIncrement >> 1) - UqmMath.Sine(travelAngle, ThrustIncrement));
            var (vx, vy) = v.Current();
            desired = (uint)((long)vx * vx) + (uint)((long)vy * vy);
            if (desired > max2)
            {
                Velocity.SetComponents(UqmMath.Cosine(currentAngle, maxSpeed), UqmMath.Sine(currentAngle, maxSpeed));
                return;
            }
            Velocity = v;
        }
    }

    /// <summary>flag_ship_preprocess: move by the velocity, slowed in the zoomed-in outer views.</summary>
    private void MoveFlagship()
    {
        var (vdx, vdy) = Velocity.Current();
        int radius = InnerPlanet is null ? ZoomRadius : IpGeometry.MaxZoomRadius;
        if (radius == IpGeometry.MinZoomRadius)
        {
            vdx = (short)(vdx >> 2);
            vdy = (short)(vdy >> 2);
        }
        else if (radius < IpGeometry.MaxZoomRadius)
        {
            vdx = (short)(vdx >> 1);
            vdy = (short)(vdy >> 1);
        }
        Location = new IpPoint((short)(Location.X + vdx), (short)(Location.Y + vdy));
        Screen = IpGeometry.LocationToDisplay(Location, radius);
    }

    // ---- where the ship is ----

    private IpEvent CheckShipLocation(out bool locChange, out short newRadius)
    {
        short radius = ZoomRadius;
        newRadius = radius;
        locChange = false;

        if (Screen.X < 0 || Screen.X >= IpGeometry.ScreenWidth || Screen.Y < 0 || Screen.Y >= IpGeometry.ScreenHeight)
        {
            if (InnerPlanet is null)
            {
                if (radius == IpGeometry.MaxZoomRadius)
                    return IpEvent.LeftSystem;
                newRadius = FindRadius(Location, IpGeometry.MaxZoomRadius << 1);
            }
            else
                LeaveInnerSystem();
            locChange = true;
            return IpEvent.None;
        }

        if (InnerPlanet is null && _scaleRect.Contains(Screen))
        {
            newRadius = FindRadius(Location, radius);
            locChange = true;
            return IpEvent.None;
        }

        int? hit = CheckIntersect();
        if (hit is int world)
        {
            if (InnerPlanet is int p)
            {
                // enterOrbital: world -1 is the planet itself.
                Velocity.Zero();
                Orbit = (p, world < 0 ? null : world);
                return IpEvent.EnteredOrbit;
            }
            EnterInnerSystem(world);
            locChange = true;
        }
        return IpEvent.None;
    }

    private void EnterInnerSystem(int planet)
    {
        int angle = UqmMath.FacingToAngle(Facing) + UqmMath.HalfCircle;
        int x = CenterX + UqmMath.Cosine(angle, InnerEntryDistance);
        int y = CenterY + UqmMath.Sine(angle, InnerEntryDistance);
        if (y < 0)
            y = 1;
        else if (y >= IpGeometry.ScreenHeight)
            y = IpGeometry.ScreenHeight - 1 - 1;
        Screen = new IpPoint((short)x, (short)y);
        Location = IpGeometry.DisplayToLocation(Screen, IpGeometry.MaxZoomRadius);
        SunLocation = IpGeometry.DisplayToLocation(_planetImage[planet], ZoomRadius);   // planetOuterLocation
        Velocity.Zero();
        Moons = System.Moons(planet);
        InnerPlanet = planet;
    }

    private void LeaveInnerSystem()
    {
        int planet = InnerPlanet!.Value;
        InnerPlanet = null;
        Moons = Array.Empty<WorldDesc>();
        _moonImage = Array.Empty<IpPoint>();
        _moonImageSize = Array.Empty<int>();

        ushort outerPlanetWait = MakeWord(planet + 1, 0);
        Location = SunLocation;
        Screen = IpGeometry.LocationToDisplay(Location, ZoomRadius);
        Velocity.Zero();

        WaitIntersect = outerPlanetWait;
        CheckIntersect();
        if (WaitIntersect != outerPlanetWait)
            WaitIntersect = AllCollisionsOff;
    }

    private static ushort MakeWord(int lo, int hi) => (ushort)((byte)lo | ((byte)hi << 8));

    /// <summary>
    /// CheckIntersect. Returns the world newly touched: in the outer system
    /// a planet index; in an inner system -1 for the planet itself or a
    /// moon index. Null if none.
    /// </summary>
    private int? CheckIntersect()
    {
        const int pointRadius = 0;
        int shipR = _shape.ShipRadius;
        ushort newWait = 0;

        bool Touches(IpPoint at, int radius)
        {
            long dx = Screen.X - at.X, dy = Screen.Y - at.Y;
            long r = shipR + radius;
            return dx * dx + dy * dy <= r * r;
        }

        int CollisionRadius(ushort word, int imageRadius) =>
            WaitIntersect != AllCollisionsOff && WaitIntersect != word ? pointRadius : imageRadius;

        if (InnerPlanet is int planet)
        {
            // The planet in the middle of the inner view.
            ushort word = MakeWord(planet + 1, 1);
            if (Touches(new IpPoint(CenterX, CenterY), CollisionRadius(word, ImageRadius(_innerPlanetImageSize))))
            {
                newWait = word;
                if (WaitIntersect != AllCollisionsOff && WaitIntersect != newWait)
                {
                    WaitIntersect = newWait;
                    return -1;
                }
            }

            for (int m = 0; m < Moons.Count; ++m)
            {
                ushort mw = MakeWord(planet + 1, m + 2);
                if (!Touches(_moonImage[m], CollisionRadius(mw, ImageRadius(_moonImageSize[m]))))
                    continue;
                newWait = mw;
                if (WaitIntersect == AllCollisionsOff)
                    break;
                if (WaitIntersect == newWait)
                    continue;
                WaitIntersect = newWait;
                return m;
            }
        }
        else
        {
            // The sun can't be touched.
            for (int p = 0; p < _planetImage.Length; ++p)
            {
                ushort pw = MakeWord(p + 1, 0);
                if (!Touches(_planetImage[p], CollisionRadius(pw, ImageRadius(_planetImageSize[p]))))
                    continue;
                newWait = pw;
                if (WaitIntersect == AllCollisionsOff)
                    break;
                if (WaitIntersect == newWait)
                    continue;
                WaitIntersect = newWait;
                return p;
            }
        }

        if (WaitIntersect != AllCollisionsOff || newWait == 0)
            WaitIntersect = newWait;
        return null;
    }

    // Image sizes: 0..5 for planet images, -1 for the starbase and the Sa-Matra.
    private int ImageRadius(int size) => size < 0 ? _shape.JunkRadius : _shape.PlanetRadius[size];

    // ---- zoom and orbits ----

    private void CheckZoomLevel()
    {
        IpPoint shipLoc = InnerPlanet is null ? Location : SunLocation;
        ZoomRadius = FindRadius(shipLoc, IpGeometry.MaxZoomRadius << 1);
        if (InnerPlanet is null)
            Screen = IpGeometry.LocationToDisplay(shipLoc, ZoomRadius);
    }

    private short FindRadius(IpPoint shipLoc, int fromRadius)
    {
        IpPoint displayLoc;
        do
        {
            fromRadius >>= 1;
            _scaleRect = ScaleRect(fromRadius);
            displayLoc = IpGeometry.LocationToDisplay(shipLoc, fromRadius);
        } while (_scaleRect.Contains(displayLoc));
        return (short)fromRadius;
    }

    private int ImageSize(byte dataIndex, int add) => SystemLayout.ImageSize(System.Generator, dataIndex, add);

    /// <summary>ValidateOrbits: planet images in the outer view at the current zoom.</summary>
    private void ValidateOrbits()
    {
        int add = SystemLayout.OuterSizeAdd(ZoomRadius);
        for (int i = 0; i < _planetImage.Length; ++i)
        {
            WorldDesc p = System.Planets[i];
            _planetImage[i] = SystemLayout.OuterPlanet(p, ZoomRadius);
            _planetImageSize[i] = ImageSize(p.DataIndex, add);
        }
    }

    /// <summary>ValidateInnerOrbits: the planet in the middle and its moons.</summary>
    private void ValidateInnerOrbits()
    {
        int planet = InnerPlanet!.Value;
        _innerPlanetImageSize = ImageSize(System.Planets[planet].DataIndex, SystemLayout.InnerPlanetSizeAdd);
        _moonImage = new IpPoint[Moons.Count];
        _moonImageSize = new int[Moons.Count];
        for (int m = 0; m < Moons.Count; ++m)
        {
            _moonImage[m] = SystemLayout.InnerMoon(Moons[m]);
            _moonImageSize[m] = ImageSize(Moons[m].DataIndex, SystemLayout.MoonSizeAdd);
        }
    }
}
