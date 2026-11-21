// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// Where a client draws the groups of alien ships it's sent (stage 3b-1
/// item 11): in hyperspace as ProcessEncounter places an encounter's
/// element (as a star's, from the ship's place), its frame of the
/// hyperspace stars' sheet; in a system as ip_group_preprocess places a
/// group (locationToDisplay at the view's zoom), its melee icon's frame 1;
/// none where UQM draws none (hidden, another view, another place).
/// </summary>
[Trait("Topic", "drawing")]
public class GroupDrawingTests
{
    [Fact]
    public void AnEncounterIsDrawnWhereItsElementIsWithItsFrame()
    {
        int logX = 400_000, logY = 300_000;
        // At the ship's own place: the view's middle, as a star there is drawn.
        var at = HyperScreen.Group(new GroupSnapshot(1, null, 5, 0, logX, logY, Frame: 3, Transition: 2), logX, logY);
        Assert.Equal(HyperScreen.Sprite(logX, logY, logX, logY), (at!.Value.X, at.Value.Y));
        Assert.Equal(3, at.Value.Frame);
        Assert.Equal(HyperScreen.Display(HyperFlight.CenterX, HyperScreen.OriginX), at.Value.X);
        // 400 logical units east: 100 pixels further right (a quarter of a pixel a unit).
        var east = HyperScreen.Group(new GroupSnapshot(1, null, 5, 0, logX + 400, logY, Frame: 3), logX, logY)!.Value;
        Assert.Equal(at.Value.X + 100, east.X);
        Assert.Equal(at.Value.Y, east.Y);
        // Hidden (off every screen, or in its vortex's first frames): not drawn; nor a system's group.
        Assert.Null(HyperScreen.Group(new GroupSnapshot(1, null, 5, 0, logX, logY, Flags: GroupSnapshotFlags.Hidden), logX, logY));
        Assert.Null(HyperScreen.Group(new GroupSnapshot(1, 82, 5, 0, logX, logY), logX, logY));
        // Met, it's drawn still, where it stopped.
        Assert.NotNull(HyperScreen.Group(new GroupSnapshot(1, null, 5, 0, logX, logY, Flags: GroupSnapshotFlags.Met), logX, logY));
    }

    [Fact]
    public void ASystemsGroupIsDrawnAtItsViewsZoomAndFlashesWhileReforming()
    {
        var loc = new IpPoint(1200, -800);
        var g = new GroupSnapshot(7, 82, 5, 0, loc.X, loc.Y);
        // The outer system at the viewer's own zoom: as each zoom puts that place.
        foreach (int zoom in new[] { IpGeometry.MinZoomRadius, IpGeometry.MinZoomRadius * 2, IpGeometry.MaxZoomRadius })
            Assert.Equal(IpGeometry.LocationToDisplay(loc, zoom), SystemSprites.Group(g, 0, zoom));
        Assert.NotEqual(SystemSprites.Group(g, 0, IpGeometry.MinZoomRadius), SystemSprites.Group(g, 0, IpGeometry.MaxZoomRadius));
        // An inner system's group is always at its one zoom, whatever the viewer's outer one was.
        var inner = g with { View = 3 };
        Assert.Equal(IpGeometry.LocationToDisplay(loc, IpGeometry.MaxZoomRadius), SystemSprites.Group(inner, 3, IpGeometry.MinZoomRadius));
        // Another view's group isn't drawn; nor a re-forming one on the frames it flashes off; nor a hyperspace one.
        Assert.Null(SystemSprites.Group(inner, 0, IpGeometry.MinZoomRadius));
        Assert.Null(SystemSprites.Group(g, 2, IpGeometry.MinZoomRadius));
        Assert.Null(SystemSprites.Group(g with { Flags = GroupSnapshotFlags.Hidden }, 0, IpGeometry.MinZoomRadius));
        Assert.Null(SystemSprites.Group(g with { Star = null }, 0, IpGeometry.MinZoomRadius));
        Assert.Equal(1, SystemSprites.GroupIconFrame);
    }
}
