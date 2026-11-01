// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>Entering and leaving star systems.</summary>
public class SystemTransitionTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly JsonElement Geometry = Golden.Load("ip-geometry");
    private static readonly StarMap Map = StarMap.Load(DataDir);
    private static Star Sol => Map.At(1752, 1450)!;

    private static HyperFlight NewShip()
    {
        var ship = new HyperFlight(ShipDrive.ForHyperspace(thrusters: 2, jets: 2), 6000) { Fuel = 5000 };
        ship.ResetFuelCounters();
        return ship;
    }

    [Fact]
    public void ScreenGeometryMatchesUqm()
    {
        Assert.Equal(IpGeometry.ScreenWidth, Geometry.GetProperty("SIS_SCREEN_WIDTH").GetInt32());
        Assert.Equal(IpGeometry.ScreenHeight, Geometry.GetProperty("SIS_SCREEN_HEIGHT").GetInt32());
        Assert.Equal(IpGeometry.MaxZoomRadius, Geometry.GetProperty("MAX_ZOOM_RADIUS").GetInt32());
        Assert.Equal(IpGeometry.MinZoomRadius, Geometry.GetProperty("MIN_ZOOM_RADIUS").GetInt32());
        Assert.Equal(IpGeometry.DisplayFactor, Geometry.GetProperty("DISPLAY_FACTOR").GetInt32());

        foreach (var e in Geometry.GetProperty("display_to_location").EnumerateArray())
        {
            int[] v = e.Ints();
            Assert.Equal(new IpPoint((short)v[3], (short)v[4]),
                IpGeometry.DisplayToLocation(new IpPoint((short)v[1], (short)v[2]), v[0]));
        }
        foreach (var e in Geometry.GetProperty("location_to_display").EnumerateArray())
        {
            int[] v = e.Ints();
            Assert.Equal(new IpPoint((short)v[3], (short)v[4]),
                IpGeometry.LocationToDisplay(new IpPoint((short)v[1], (short)v[2]), v[0]));
        }
    }

    [Fact]
    public void ArrivalIsWhereUqmPutsTheShip()
    {
        var arrival = SystemTransition.Arrive(Sol);
        var a = Geometry.GetProperty("arrival");
        Assert.Equal(a.GetProperty("origin").Ints(), new int[] { arrival.ScreenPosition.X, arrival.ScreenPosition.Y });
        Assert.Equal(a.GetProperty("location").Ints(), new int[] { arrival.Location.X, arrival.Location.Y });
        Assert.Equal(0, arrival.Facing);
    }

    [Fact]
    public void FlyingIntoAStarEntersIt()
    {
        var ship = NewShip();
        // Start 40 pixels below Sol, facing up (facing 0), and thrust.
        ship.PlaceAtUniverse(Sol.X, Sol.Y);
        ship.LogY += 40 * 32;
        ship.Facing = 0;

        Star? entered = null;
        for (int frame = 0; frame < 200 && entered is null; ++frame)
        {
            ship.Step(ShipInput.Thrust);
            entered = StarContact.Check(ship, Map);
        }
        Assert.Equal(Sol, entered);
    }

    [Fact]
    public void AShipAtRestOnAStarStaysInHyperspace()
    {
        var ship = NewShip();
        ship.PlaceAtUniverse(Sol.X, Sol.Y);
        ship.Step(ShipInput.None);
        Assert.Null(StarContact.Check(ship, Map));
    }

    [Fact]
    public void LeavingPutsTheShipOnTheStarAndItDoesNotFallBackIn()
    {
        var ship = NewShip();
        ship.LogX += 12345;   // anywhere
        SystemTransition.Leave(ship, Sol, facing: 4);

        Assert.Equal(Sol.X, ship.UniverseX);
        Assert.Equal(Sol.Y, ship.UniverseY);
        Assert.Equal(4, ship.Facing);                 // keeps the system heading
        Assert.Equal((0, 0), ship.Velocity.Current());
        Assert.Equal(Sol.Number, ship.StarJustLeft);

        // Fly away (facing 4 is right): no re-entry on the way out.
        int frames = 0;
        while (ship.StarJustLeft is not null && frames < 300)
        {
            ship.Step(ShipInput.Thrust);
            Assert.Null(StarContact.Check(ship, Map));
            ++frames;
        }
        Assert.Null(ship.StarJustLeft);               // cleared the star

        // Turn round and come back: now it can be entered.
        for (int i = 0; i < 100; ++i)
            ship.Step(ShipInput.Left);
        Star? entered = null;
        for (int frame = 0; frame < 400 && entered is null; ++frame)
        {
            ship.Step(ShipInput.Thrust);
            entered = StarContact.Check(ship, Map);
        }
        Assert.Equal(Sol, entered);
    }
}
