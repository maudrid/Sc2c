// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Uqm;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>Places on UQM's hyperspace screen, worked out from process.c and hyper.c.</summary>
public class HyperScreenTests
{
    [Fact]
    public void TheShipIsDrawnWhereUqmDrawsIt()
    {
        // SpaceOrg: 4096 - 8192 * 256 / 4096, and 3840 - 7680 * 256 / 4096.
        Assert.Equal((3584, 3360), (HyperScreen.OriginX, HyperScreen.OriginY));
        Assert.Equal((128, 120), HyperScreen.Ship(5000, 6000, HyperFlight.CenterX, HyperFlight.CenterY, 5000, 6000));
        // A view that lags the ship draws it off the middle.
        Assert.Equal((128 + 10, 120 - 5), HyperScreen.Ship(5000, 6000, HyperFlight.CenterX + 40, HyperFlight.CenterY - 20, 5000, 6000));
        // Another ship 400 logical units right of the viewer.
        Assert.Equal((228, 120), HyperScreen.Ship(5400, 6000, HyperFlight.CenterX, HyperFlight.CenterY, 5000, 6000));
        Assert.Equal((42, 33), (StarContact.ReachX, StarContact.ReachY));
    }

    [Fact]
    public void OnlyShipsNearTheViewerAreDrawn()
    {
        Assert.True(StarContact.InReach(1042, 1967, 1000, 2000));
        Assert.False(StarContact.InReach(1043, 2000, 1000, 2000));
        // Across the map: not drawn, rather than placed by a wrapped distance.
        Assert.False(StarContact.InReach(8800, 2000, 1000, 2000));
        // A ship 130,000 logical units away is placed off the screen, not on it.
        var (x, _) = HyperScreen.Ship(140000, 6000, HyperFlight.CenterX, HyperFlight.CenterY, 10000, 6000);
        Assert.True(x > 30000, $"placed at {x}");
    }

    [Fact]
    public void StarsArePlacedAroundTheShip()
    {
        var star = new Star(0, 1000, 2000, StarSize.Dwarf, StarColor.Yellow, null);
        int lx = Units.UniverseToLogX(1000), ly = Units.UniverseToLogY(2000);
        Assert.Equal((128, 120), HyperScreen.Star(star, lx, ly));
        // Ten universe units right (about 160 logical units, 40 pixels) and up.
        var (x, y) = HyperScreen.Star(star, Units.UniverseToLogX(990), Units.UniverseToLogY(1990));
        Assert.Equal((128 + (Units.UniverseToLogX(1000) - Units.UniverseToLogX(990)) / 4, 120 - (Units.UniverseToLogY(1990) - Units.UniverseToLogY(2000)) / 4), (x, y));
        Assert.True(StarContact.InReach(star, 1042, 1967));
        Assert.False(StarContact.InReach(star, 1043, 2000));
        Assert.False(StarContact.InReach(star, 1000, 2034));
    }
}
