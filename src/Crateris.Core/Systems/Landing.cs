// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (planets/scan.c DoScan,
// getLandingFuelNeeded, PickPlanetSide, DoPickPlanetSide, setPlanetCursorLoc,
// flashPlanetLocation and DispatchLander; the start of ScanSystem),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.World;

namespace Crateris.Core.Systems;

/// <summary>Why the lander can't be dispatched (DoScan plays MENU_SOUND_FAILURE for each).</summary>
public enum DispatchRefusal
{
    /// <summary>It can go.</summary>
    None,
    /// <summary>The world is shielded (PLANET_SHIELDED).</summary>
    Shielded,
    /// <summary>The world is a gas giant (GAS_GIANT_ATMOSPHERE).</summary>
    GasGiant,
    /// <summary>Less fuel aboard than the landing costs.</summary>
    Fuel,
    /// <summary>No lander aboard.</summary>
    NoLander,
    /// <summary>No crew aboard.</summary>
    NoCrew,
}

/// <summary>
/// The rules for dispatching the lander from the scan menu (DoScan): which
/// worlds it can go down to, what it costs, and what the ship needs aboard.
/// </summary>
public static class Landing
{
    /// <summary>The most a landing costs: 3 units (3 * FUEL_TANK_SCALE).</summary>
    public const uint MaxFuel = 3 * Hyperspace.Fuel.Scale;

    /// <summary>
    /// The fuel a landing costs, in 1/100 units: twice the world's surface
    /// gravity, at most 3 units (getLandingFuelNeeded).
    /// </summary>
    public static uint FuelNeeded(PlanetInfo report) =>
        Math.Min((uint)report.SurfaceGravity << 1, MaxFuel);

    /// <summary>Whether the world lets a lander down (DoScan's first test): orbited, neither shielded nor a gas giant.</summary>
    public static DispatchRefusal World(OrbitData orbit)
    {
        if (!orbit.Entered)
            throw new ArgumentException("that world isn't orbited", nameof(orbit));
        if (orbit.Nodes is null)
            return DispatchRefusal.Shielded;
        if (orbit.Report!.AtmoDensity == PlanetInfo.GasGiantAtmosphere)
            return DispatchRefusal.GasGiant;
        return DispatchRefusal.None;
    }

    /// <summary>
    /// Every test DoScan makes before the landing site is picked, in its
    /// order: the world, then the fuel, a lander and crew aboard.
    /// </summary>
    public static DispatchRefusal Check(OrbitData orbit, uint fuel, int landers, int crew)
    {
        var world = World(orbit);
        if (world != DispatchRefusal.None)
            return world;
        if (fuel < FuelNeeded(orbit.Report!))
            return DispatchRefusal.Fuel;
        if (landers == 0)
            return DispatchRefusal.NoLander;
        if (crew == 0)
            return DispatchRefusal.NoCrew;
        return DispatchRefusal.None;
    }

    /// <summary>The checks for a ship: its fuel, landers and crew; a lander already down counts as none aboard.</summary>
    public static DispatchRefusal Check(OrbitData orbit, PlayerShip ship) =>
        Check(orbit, ship.Flight.Fuel, ship.Hold.LanderOut is null ? ship.Hold.Landers : 0, ship.Hold.Crew);
}

/// <summary>The direction keys held down (UQM's CurrentInputState.menu).</summary>
[Flags]
public enum HeldKeys { None = 0, Up = 1, Down = 2, Left = 4, Right = 8 }

/// <summary>
/// The landing site being picked on the scan map (PickPlanetSide and
/// DoPickPlanetSide), in planetside map units (four to a scan map pixel,
/// MAG_SHIFT). The picker runs at 40 frames a second: each frame the
/// direction keys held move the site one scan map pixel, wrapping east and
/// west and stopping at the poles. The cursor is drawn at the site's scan
/// map pixel, flashing grey to white and back while it stays still
/// (flashPlanetLocation).
/// </summary>
public sealed class LandingSite
{
    /// <summary>The picker's frames a second (DoPickPlanetSide sleeps until TimeIn + ONE_SECOND / 40).</summary>
    public const int FramesPerSecond = 40;

    /// <summary>A picker frame in UQM's clock ticks (ONE_SECOND / 40).</summary>
    public const int TicksPerFrame = OrbitScan.TicksPerSecond / FramesPerSecond;

    /// <summary>FLASH_FRAME_DELAY: the cursor's colour changes every 1/16 second.</summary>
    public const int FlashTicks = OrbitScan.TicksPerSecond / 16;

    /// <summary>The cursor's brightest colour: 0x1A in each 5-bit channel.</summary>
    public const int FlashMax = 0x1A;

    private int _flashLevel, _flashStep = -2;
    private long _now, _nextFlash;
    private (int X, int Y)? _flashedAt;
    private double _carry;

    /// <summary>The site starts in the middle of the map, as ScanSystem puts it.</summary>
    public LandingSite()
    {
        X = (SurfaceMap.Width >> 1) << PlanetsideMap.MagShift;
        Y = (SurfaceMap.Height >> 1) << PlanetsideMap.MagShift;
    }

    /// <summary>Where the lander will go down, in planetside map units (planetLoc).</summary>
    public int X { get; private set; }

    /// <inheritdoc cref="X"/>
    public int Y { get; private set; }

    /// <summary>The cursor's scan map pixel (setPlanetCursorLoc).</summary>
    public (int X, int Y) Cursor => (X >> PlanetsideMap.MagShift, Y >> PlanetsideMap.MagShift);

    /// <summary>The cursor's level, 0 to <see cref="FlashMax"/> in each 5-bit channel (MAKE_RGB15 (c, c, c)).</summary>
    public int FlashLevel => _flashLevel;

    /// <summary>The cursor's colour now.</summary>
    public Rgb FlashColor => Rgb.From15(_flashLevel, _flashLevel, _flashLevel);

    /// <summary>
    /// Starts picking: the site stays where it was left (planetLoc is kept
    /// for the whole scan menu), and the cursor flashes from black.
    /// UQM keeps the flashing's state from the last pick (statics in
    /// flashPlanetLocation); here each pick starts it afresh, which only
    /// changes the cursor's shade for a moment.
    /// </summary>
    public void BeginPick()
    {
        _flashLevel = 0;
        _flashStep = -2;
        _flashedAt = null;
        _now = 0;
        _nextFlash = 0;
        _carry = 0;
        Flash();
    }

    /// <summary>Time passes with these keys held: one move and one flash test for each picker frame.</summary>
    public void Advance(double seconds, HeldKeys held)
    {
        _carry += seconds * FramesPerSecond;
        while (_carry >= 1)
        {
            _carry -= 1;
            Frame(held);
        }
    }

    /// <summary>One picker frame (DoPickPlanetSide with no select or cancel).</summary>
    public void Frame(HeldKeys held)
    {
        _now += TicksPerFrame;
        int dx = 0, dy = 0;
        if ((held & HeldKeys.Left) != 0)
            dx = -1;
        if ((held & HeldKeys.Right) != 0)
            dx = 1;
        if ((held & HeldKeys.Up) != 0)
            dy = -1;
        if ((held & HeldKeys.Down) != 0)
            dy = 1;
        Move(dx, dy);
        Flash();
    }

    /// <summary>Moves the site by scan map pixels: east and west wrap, a move past a pole leaves the site where it was.</summary>
    public void Move(int dx, int dy)
    {
        const int width = PlanetsideMap.Width, height = PlanetsideMap.Height;
        if (dx != 0)
        {
            X += dx << PlanetsideMap.MagShift;
            if (X < 0)
                X += width;
            else if (X >= width)
                X -= width;
        }
        if (dy != 0)
        {
            int y = Y + (dy << PlanetsideMap.MagShift);
            if (y >= 0 && y < height)
                Y = y;
        }
    }

    // flashPlanetLocation: a move starts the cycle again from black; while
    // still, every FLASH_FRAME_DELAY the level goes 2 up to 0x1A, then down.
    private void Flash()
    {
        var at = Cursor;
        if (_flashedAt == at && _now < _nextFlash)
            return;
        if (_flashedAt != at)
        {
            _flashLevel = 0;
            _flashStep = -2;
            _flashedAt = at;
            _nextFlash = _now + FlashTicks;
            return;
        }
        if (_flashLevel == 0 || _flashLevel == FlashMax)
            _flashStep = -_flashStep;
        _flashLevel += _flashStep;
        if (_now - _nextFlash > FlashTicks)
            _nextFlash = _now + FlashTicks;   // missed the time by too much
        else
            _nextFlash += FlashTicks;
    }
}
