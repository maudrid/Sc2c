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
        var drawn = screen.Frame(Battle((1000, 1000), (1000 + W / 4, 1000)), "me", BattleSpace.SizeOf)!;
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

    [Fact]
    public void EachEnemyShipComingInHasItsStatusDrawnAfresh()
    {
        // A fleet's next ship, or an infinite fleet's brought back with a new captain (stage 4b item 5): InitShipStatus.
        var canvas = new UqmCanvas(_ => 1, (_, _) => default, new Dictionary<string, int>());
        var shown = new List<(string Ship, int Captain)>();
        var screen = new BattleScreen(canvas, (s, id) =>
        {
            if (id is null)
                shown.Add((s.Ship, s.Captain));
            return new StatusShip(null, null, s.MaxCrew, s.MaxEnergy, false, false, id != null, "n", "c");
        });
        var b = Battle((1000, 1000), (1200, 1000));
        int frame = 0;
        void Show(BattleShipStatus enemy) =>
            Assert.NotNull(screen.Frame(b with { Enemy = enemy, Camera = b.Camera with { Frame = ++frame } }, "me", BattleSpace.SizeOf));
        Show(Status with { Ship = "ilwrath", Captain = 5 });
        Show(Status with { Ship = "ilwrath", Captain = 5, Crew = 3 });   // the same ship: updated, not drawn afresh
        Show(Status with { Ship = "ilwrath", Captain = 5, Crew = 5 });   // nor as its crew rises (crew picked up, regrown)
        Assert.Single(shown);
        Show(Status with { Ship = "spathi", Captain = 5 });
        Show(Status with { Ship = "spathi", Captain = 9 });
        // A ship whose status stopped (here gone), back with crew and the same captain: shown afresh (an infinite fleet's
        // real path, its ship never gone, is EnemyFleetTests'); not while gone, even with crew.
        for (int i = 0; i < 3; ++i)
            Show(Status with { Ship = "spathi", Captain = 9, Crew = 0, Gone = true });
        Show(Status with { Ship = "spathi", Captain = 9, Crew = 4, Gone = true });
        Assert.Equal(3, shown.Count);
        Show(Status with { Ship = "spathi", Captain = 9 });
        Assert.Equal(new[] { ("ilwrath", 5), ("spathi", 5), ("spathi", 9), ("spathi", 9) }, shown);

        // A Pkunk reborn with the frames of its death and rebirth missed (its slot not stopped: the life seen next no
        // higher than the last): crew 0 then crew again is a ship back all the same.
        Show(Status with { Ship = "pkunk", Captain = 6, Crew = 8 });
        Show(Status with { Ship = "pkunk", Captain = 6, Crew = 0, LifeSpan = 20 });
        Show(Status with { Ship = "pkunk", Captain = 6, Crew = 8, LifeSpan = 4 });
        Show(Status with { Ship = "pkunk", Captain = 6, Crew = 8, LifeSpan = 3 });
        Assert.Equal(new[] { ("pkunk", 6), ("pkunk", 6) }, shown.Skip(4));
    }

    // The stars a frame draws, in the space window.
    private static List<UqmDraw> Stars(List<UqmDraw> drawn) => drawn.Skip(1).Take(BattleGalaxy.NumStars).ToList();

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void StarsFillTheWindowInABiggerArenaAndOnAFirstFrameSeenStill(double scale)
    {
        // The widest zoom, the camera still (view state 0) on the frame first seen: the stars are placed all the same.
        var arena = ArenaSize.Scaled(scale);
        var zoom = 1 << 10;
        var (ox, oy) = Combat.Battle.SpaceOrg(BattleZoom.Smooth, arena, zoom);
        var b = new BattleSnapshot(1, arena.Width, arena.Height, Status with { Ship = "ilwrath" }, (short)(arena.Width / 2), 100,
            new[] { new BattleFighterSnapshot("me", Status, (short)(arena.Width / 2), (short)(arena.Height - 100), true) },
            Array.Empty<BattleElementSnapshot>(), new BattleCamera(0, 0, 0, zoom, ox, oy, false, 40, 1234, -567), 99);
        var canvas = new UqmCanvas(_ => 1, (_, _) => default, new Dictionary<string, int>());
        var screen = new BattleScreen(canvas, (s, id) => new StatusShip(null, null, s.MaxCrew, s.MaxEnergy, false, false, id != null, "n", "c"));
        var stars = Stars(screen.Frame(b, "me", BattleSpace.SizeOf)!);
        bool In(UqmDraw d, int x0, int x1, int y0, int y1) => d.X >= x0 && d.X < x1 && d.Y >= y0 && d.Y < y1;
        const int w = UqmCanvas.SpaceWidth, h = UqmCanvas.SpaceHeight;
        Assert.Contains(stars, d => In(d, 0, w / 2, 0, h / 2));
        Assert.Contains(stars, d => In(d, w / 2, w, 0, h / 2));
        Assert.Contains(stars, d => In(d, 0, w / 2, h / 2, h));
        Assert.Contains(stars, d => In(d, w / 2, w, h / 2, h));
        // Zoomed out, the smooth zoom draws every star a point (the 3DO's choice: MoveGalaxy), the near ones too.
        Assert.All(stars, d => Assert.Equal(UqmDrawKind.Point, d.Kind));
    }

    [Fact]
    public void AnotherPlayersStarsAfterMissedFramesAreAsIfEachWasSeen()
    {
        // The player's own camera scrolls up and left to centre its ship and the enemy, below and right of the
        // arena's centre: its scroll adds up negative, past what the arena wraps to.
        BattleSnapshot At(int frame) => Battle(((short)(W * 3 / 4), (short)(H * 3 / 4)), ((short)(W * 3 / 4 + 400), (short)(H * 3 / 4 + 300)))
            with { Camera = new BattleCamera(0, 0, 0, 256, 3072, 2880, false, frame) };
        UqmCanvas Canvas() => new(_ => 1, (_, _) => default, new Dictionary<string, int>());
        StatusShip Look(BattleShipStatus s, string? id) => new(null, null, s.MaxCrew, s.MaxEnergy, false, false, id != null, "n", "c");
        var every = new BattleScreen(Canvas(), Look);
        var some = new BattleScreen(Canvas(), Look);
        List<UqmDraw>? last = null;
        for (int f = 1; f <= 8; ++f)
        {
            last = every.Frame(At(f), "me", BattleSpace.SizeOf);
            if (f is 1 or 2 or 8)
                Assert.Equal(last, some.Frame(At(f), "me", BattleSpace.SizeOf));
        }
        Assert.True(every.View.Own);
        Assert.True(every.View.OffsetX > W / 2, "the offset wraps at the arena: the scroll so far is negative");
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

    // ---- stage 4b item 10: the player's slot, the captain's window's marks ----

    // A canvas whose every sheet has 20 frames, each 5 by 5 with its hot spot at (2, 2).
    private static UqmCanvas FakeCanvas() =>
        new(_ => 20, (_, _) => new URect(-2, -2, 5, 5), new Dictionary<string, int> { ["starcon"] = 11, ["tiny"] = 10 });

    private static StatusShip Look(BattleShipStatus s, string? player) =>
        new("icons." + s.Ship, "captain." + s.Ship, s.MaxCrew, s.MaxEnergy, false, false, false, s.Ship, $"captain {s.Captain}");

    // Whether a frame's status calls drew the player's slot afresh (OutlineShipStatus's first rectangle there).
    private static bool PlayersSlotDrawnAfresh(IEnumerable<UqmDraw> calls) =>
        calls.Any(d => d.Kind == UqmDrawKind.Rect && d.X == UqmCanvas.SpaceWidth && d.Y == BattleStatus.PlayerSlot + 1
            && d.Width == BattleStatus.StatusWidth && d.Height == 1);

    [Fact]
    public void ThePlayersSlotIsDrawnAfreshAsANewShipOfTheirsComesIn()
    {
        var canvas = FakeCanvas();
        var screen = new BattleScreen(canvas, Look);
        int frame = 0;
        bool Show(BattleShipStatus mine)
        {
            canvas.Drawn.Clear();
            var b = new BattleSnapshot(1, W, H, Status with { Ship = "ilwrath", Captain = 5 }, 3000, 3000,
                new[] { new BattleFighterSnapshot("me", mine, 1000, 1000, true) }, Array.Empty<BattleElementSnapshot>(),
                new BattleCamera(0, 0, 0, 256, 0, 0, false, ++frame), 7);
            Assert.NotNull(screen.Frame(b, "me", BattleSpace.SizeOf));
            return PlayersSlotDrawnAfresh(canvas.Drawn);
        }
        // Before the host's first pick, their status is gone (no ship of theirs in it): the slot isn't drawn yet.
        Assert.False(Show(new BattleShipStatus(ShipDescriptions.Flagship, 0, 50, 0, 10, Gone: true)));
        Assert.False(Show(new BattleShipStatus(ShipDescriptions.Flagship, 0, 50, 0, 10, Gone: true)));
        var spathi = new BattleShipStatus("spathi", 5, 30, 10, 10, Captain: 7);
        Assert.True(Show(spathi));                                    // the first ship picked
        Assert.False(Show(spathi with { Crew = 4 }));                 // flying on
        Assert.False(Show(spathi with { Crew = 4, Gone = true }));     // retreated: its panel stands while the host chooses
        Assert.False(Show(spathi with { Crew = 4, Gone = true }));
        var arilou = new BattleShipStatus("arilou", 6, 6, 20, 20, Captain: 9);
        Assert.True(Show(arilou));                                    // another ship
        Assert.False(Show(arilou with { Energy = 19 }));
        Assert.False(Show(arilou with { Gone = true }));
        Assert.True(Show(arilou));                                    // the same race and captain after one gone: another ship
        Assert.True(Show(arilou with { Captain = 10 }));              // another captain at once
        Assert.True(Show(new BattleShipStatus("sis_ship", 50, 50, 10, 10)));   // the flagship last
        Assert.False(Show(new BattleShipStatus("sis_ship", 49, 50, 10, 10)));
    }

    [Fact]
    public void OnlyTheMarksNotDrawnYetAreDrawn()
    {
        static SilhouetteMarkSnapshot M(int ordinal, int x) => new("marks", 1, (short)x, 30, false, ordinal);
        var a = M(0, 20);
        var b = M(1, 24);
        var c = M(5, 28);
        Assert.Equal(0, BattleStatus.FirstNew(-1, new[] { a, b }));
        Assert.Equal(1, BattleStatus.FirstNew(0, new[] { a, b }));
        Assert.Equal(2, BattleStatus.FirstNew(1, new[] { a, b }));
        Assert.Equal(1, BattleStatus.FirstNew(1, new[] { b, c }));   // a pruned, c new
        Assert.Equal(1, BattleStatus.FirstNew(3, new[] { a, c }));   // marks between missed and pruned
        Assert.Equal(0, BattleStatus.FirstNew(4, new[] { c }));
        // A mark equal by value to one drawn before is still new by its ordinal.
        Assert.Equal(1, BattleStatus.FirstNew(1, new[] { b, a with { Ordinal = 2 } }));
    }

    [Fact]
    public void AMarkIsStampedAndAnEraseDrawsThePanelAfreshWithinItsRectangle()
    {
        var canvas = FakeCanvas();
        var status = new BattleStatus(canvas);
        var ship = new StatusShip("icons", "captain", 20, 10, false, false, false, "Orz", "captain");
        var state = new StatusState(12, 6, StatusFlags.None, 12, 0);
        status.Init(BattleStatus.EnemySlot, ship, state);
        var stamp = new SilhouetteMarkSnapshot("marines", 4, 30, 33, false, 0);
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Marks = new[] { stamp } });
        var drawn = Assert.Single(canvas.Drawn);
        Assert.Equal((UqmDrawKind.Stamp, "marines", 4, UqmCanvas.SpaceWidth + 30, 33), (drawn.Kind, drawn.Sheet, drawn.Frame, drawn.X, drawn.Y));
        // Nothing new: nothing drawn.
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Marks = new[] { stamp } });
        Assert.Empty(canvas.Drawn);
        // An erase at an odd y: the panel drawn within the frame's rectangle there, its top one row up (shipstat.c's
        // corner.y & ~1, its height one more), the gauges as they stand, the drawing's origin kept.
        var erase = stamp with { Erase = true, Ordinal = 1 };
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Marks = new[] { erase } });
        Assert.NotEmpty(canvas.Drawn);
        var within = new URect(UqmCanvas.SpaceWidth + 30 - 2, 33 - 2 - 1, 5, 5 + 1);
        Assert.All(canvas.Drawn, d => Assert.Equal(within, d.Clip));
        Assert.Contains(canvas.Drawn, d => d.Kind == UqmDrawKind.Rect && d.X == UqmCanvas.SpaceWidth && d.Y == 1 && d.Width == 64);
        // An even y: the rectangle as it is.
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Marks = new[] { erase, erase with { Y = 34, Ordinal = 2 } } });
        Assert.All(canvas.Drawn, d => Assert.Equal(new URect(UqmCanvas.SpaceWidth + 28, 32, 5, 5), d.Clip));
        // The panel's own clip is back afterwards.
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Crew = 11, Marks = new[] { erase, erase with { Y = 34, Ordinal = 2 } } });
        Assert.All(canvas.Drawn, d => Assert.Equal(new URect(UqmCanvas.SpaceWidth, 0, 64, UqmCanvas.StatusHeight), d.Clip));
        // A slot drawn afresh draws every mark sent after the window, in order.
        canvas.Drawn.Clear();
        status.Init(BattleStatus.PlayerSlot, ship, state with { Marks = new[] { stamp, stamp with { Frame = 5, X = 20, Ordinal = 3 } } });
        var marks = canvas.Drawn.Where(d => d.Sheet == "marines").ToList();
        Assert.Equal(new[] { (4, UqmCanvas.SpaceWidth + 30, BattleStatus.PlayerSlot + 33), (5, UqmCanvas.SpaceWidth + 20, BattleStatus.PlayerSlot + 33) },
            marks.Select(d => (d.Frame, d.X, d.Y)));
        Assert.True(canvas.Drawn.IndexOf(marks[0]) > canvas.Drawn.FindLastIndex(d => d.Sheet == "captain"));
    }

    [Fact]
    public void TheSpecialKeyShowsTheFrameTheShipsCodeKeeps()
    {
        var canvas = FakeCanvas();
        var status = new BattleStatus(canvas);
        var ship = new StatusShip("icons", "captain", 6, 4, false, false, false, "Shofixti", "captain");
        var state = new StatusState(6, 4, StatusFlags.None, 6, 0, Special: 12);
        status.Init(BattleStatus.EnemySlot, ship, state);
        // Pressed: the first pass with the frame as it was, the second with the one its postprocess stepped to.
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Flags = StatusFlags.Special, Special = 13 });
        Assert.Equal(new[] { 13, 15 }, canvas.Drawn.Where(d => d.Sheet == "captain").Select(d => d.Frame));
        canvas.Drawn.Clear();
        status.Update(BattleStatus.EnemySlot, state with { Special = 14 });   // released: stepped again
        Assert.Equal(new[] { 14, 14 }, canvas.Drawn.Where(d => d.Sheet == "captain").Select(d => d.Frame));
        // 0 is DrawCaptainsWindow's 12.
        canvas.Drawn.Clear();
        status.Init(BattleStatus.PlayerSlot, ship, state with { Special = 0 });
        status.Update(BattleStatus.PlayerSlot, state with { Special = 0, Flags = StatusFlags.Special });
        Assert.Equal(new[] { 0, 13, 14 }, canvas.Drawn.Where(d => d.Sheet == "captain").Select(d => d.Frame));
    }
}
