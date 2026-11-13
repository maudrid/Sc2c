// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Combat;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Xunit;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle screen's own parts (stage 4a item 12) that UQM's recording
/// can't check: a player's own camera when the battle's follows another,
/// the minimap, and the raster's scaled stamps and lines.
/// BattleDrawTests checks the rest against UQM's drawing.
/// </summary>
[Trait("Topic", "drawing")]
public class BattleScreenTests
{
    private const int W = Combat.Battle.LogSpaceWidth, H = Combat.Battle.LogSpaceHeight;

    private static readonly BattleShipStatus Status = new("sis_ship", 10, 20, 5, 10);

    // A battle with "me" and a teammate "you" (followed by the battle's camera) and the enemy; no elements.
    private static BattleSnapshot Battle((short X, short Y) me, (short X, short Y) enemy, bool meFollowed = false,
        BattleCamera? host = null) =>
        new(1, W, H, Status with { Ship = "ilwrath" }, enemy.X, enemy.Y,
            new[] { new BattleFighterSnapshot("you", Status, 100, 100, !meFollowed), new BattleFighterSnapshot("me", Status, me.X, me.Y, meFollowed) },
            Array.Empty<BattleElementSnapshot>(), host ?? new BattleCamera(0, 0, 0, 256, 0, 0, false), 7);

    [Fact]
    public void APlayerTheBattlesCameraFollowsSeesItsCamera()
    {
        var view = new BattleView();
        var host = new BattleCamera(1, 8, -4, 300, 100, 200, false);
        view.Follow(Battle((1000, 1000), (3000, 1000), meFollowed: true, host), "me");
        Assert.Equal(host, view.Camera);
        Assert.False(view.Own);
        Assert.Equal((0, 0), (view.OffsetX, view.OffsetY));
    }

    [Fact]
    public void AnotherPlayerCentresTheirOwnShipAndTheEnemy()
    {
        var view = new BattleView();
        view.Follow(Battle((1000, 1000), (3000, 1400)), "me");   // its first frame: the battle's camera
        Assert.True(view.Own);
        for (int i = 0; i < 3; ++i)
            view.Follow(Battle((1000, 1000), (3000, 1400)), "me");
        // The midpoint of the two, in the camera's frame, is the arena's centre.
        Assert.Equal(W / 2, Combat.Battle.DisplayAlign(1000 + view.OffsetX + 1000));
        Assert.Equal(H / 2, Combat.Battle.DisplayAlign(1000 + view.OffsetY + 200));
        int near = view.Camera.ZoomOut;
        Assert.True(near > 256, $"zoomed out to keep both in view: {near}");
        // Farther apart, further out; still none past UQM's widest.
        for (int i = 0; i < 40; ++i)
            view.Follow(Battle((1000, 1000), (5000, 1400)), "me");
        Assert.True(view.Camera.ZoomOut > near);
        Assert.True(view.Camera.ZoomOut <= 1 << 10);
        // Coming back to the battle's camera places the stars afresh.
        view.Follow(Battle((1000, 1000), (5000, 1400), meFollowed: true), "me");
        Assert.Equal(2, view.Camera.ViewState);
        Assert.Equal((0, 0), (view.OffsetX, view.OffsetY));
    }

    [Fact]
    public void ALoneShipIsFollowedAFewUnitsAFrame()
    {
        var view = new BattleView();
        var b = Battle((0, 0), (3000, 3000)) with { Enemy = Status with { Ship = "ilwrath", Gone = true } };
        view.Follow(b, "me");
        view.Follow(b, "me");
        Assert.Equal(Core.Uqm.Units.OrgJumpX, view.OffsetX);   // CalcView's jump with one ship followed
        Assert.Equal(1, view.Camera.ViewState);
    }

    [Fact]
    public void ElementsAreMovedIntoTheCamerasFrameAcrossTheSeam()
    {
        var view = new BattleView();
        var b = Battle((100, 100), (W - 100, 100));
        view.Follow(b, "me");
        view.Follow(b, "me");
        var line = new BattleElementSnapshot(PrimType.Line, 50, 60, 40, 80, "", 0, 0);
        var moved = view.InFrame(line, W, H);
        Assert.Equal(((50 + view.OffsetX) % W + W) % W, moved.X);
        Assert.Equal(moved.X - 10, moved.X2);
        Assert.Equal(moved.Y + 20, moved.Y2);
        // The two ships either side of the seam, their midpoint on it: the camera's frame has them either side of the centre.
        Assert.Equal(W / 2 + 100, ((100 + view.OffsetX) % W + W) % W);
        Assert.Equal(W / 2 - 100, ((W - 100 + view.OffsetX) % W + W) % W);
    }

    [Fact]
    public void TheMinimapShowsEveryShipAroundThePlayer()
    {
        var canvas = new UqmCanvas(_ => 1, (_, _) => default, new Dictionary<string, int>());
        var screen = new BattleScreen(canvas, (s, id) => new StatusShip(null, null, s.MaxCrew, s.MaxEnergy, false, false, id != null, "n", "c"));
        var drawn = screen.Frame(Battle((1000, 1000), (1000 + W / 4, 1000)), "me", BattleSpace.SizeOf);
        var box = BattleScreen.MinimapRect(W, H);
        var dots = drawn.Where(d => d.Kind == UqmDrawKind.Rect && d.Width == 2).ToList();
        Assert.Equal(3, dots.Count);
        var mine = dots[^1];
        Assert.Equal(box.X + 1 + (box.W - 2) / 2, mine.X);   // the player's own at the centre
        var enemy = dots[1];
        Assert.Equal(mine.X + (box.W - 2) / 4, enemy.X);     // a quarter of the arena to the right
        Assert.True(screen.Fresh);
        Assert.NotEmpty(screen.Status);                       // the panel drawn afresh
        Assert.Equal(UqmDrawKind.Clear, drawn[0].Kind);
    }

    private static ScreenRaster Raster(SpriteImage img) => new((_, _) => img, _ => throw new InvalidOperationException());

    [Fact]
    public void AScaledStampIsTheNearestPixels()
    {
        // 4 by 4, hot spot (2, 2), each pixel its own red.
        var rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; ++i)
            (rgba[i * 4], rgba[i * 4 + 3]) = ((byte)(i * 10 + 10), 255);
        var r = Raster(new SpriteImage(4, 4, 2, 2, rgba));
        r.Paint(new UqmDraw(UqmDrawKind.Stamp, 10, 10, Sheet: "s", Clip: BattleScreen.SpaceClip, Scale: 128));
        // Half size: 2 by 2 at (9, 9); TFB_DrawCanvas_Rescale_Nearest steps 3 source pixels a pixel from 0.375 in.
        byte At(int x, int y) => r.Rgba[(y * ScreenRaster.Width + x) * 4];
        Assert.Equal(10, At(9, 9));
        Assert.Equal(40, At(10, 9));
        Assert.Equal(130, At(9, 10));
        Assert.Equal(160, At(10, 10));
        Assert.Equal(0, At(11, 10));
        Assert.Equal(0, At(8, 9));
    }

    [Fact]
    public void ALineIsDrawnEndToEnd()
    {
        var r = Raster(new SpriteImage(1, 1, 0, 0, new byte[4]));
        r.Paint(new UqmDraw(UqmDrawKind.Line, 2, 3, 8, 5, new Rgb(255, 0, 0), Clip: BattleScreen.SpaceClip));
        var lit = new List<(int, int)>();
        for (int y = 0; y < 10; ++y)
            for (int x = 0; x < 12; ++x)
                if (r.Rgba[(y * ScreenRaster.Width + x) * 4] == 255)
                    lit.Add((x, y));
        // line_prim's Bresenham: x steps every pixel, y twice.
        Assert.Equal(new[] { (2, 3), (3, 3), (4, 4), (5, 4), (6, 4), (7, 5), (8, 5) }, lit);
    }
}
