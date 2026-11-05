// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0, src/uqm/planets/lander.c
// (ScrollPlanetSide, BuildObjectList, DamageColorCycle, InitLander,
// AnimateLanderWarmup, DeltaLanderCrew, FillLanderHold, PlanetSide's
// ColorCycle), Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc.
// 1992-2002.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Planetside;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>The sheets the surface is drawn from, numbered as the lander recordings number them.</summary>
public enum PlanetsideSheet
{
    /// <summary>graphics.lander (LanderFrame[0]): the lander's facings, its shots, its explosion, its gauges.</summary>
    Lander = 0,
    Quake = 1,
    Lightning = 2,
    Lava = 3,
    /// <summary>graphics.lander's shield (LanderFrame[4]): one frame for each of the lander's.</summary>
    Shield = 4,
    /// <summary>The lander leaving the ship (LanderFrame[5]).</summary>
    Launch = 5,
    /// <summary>The lander coming back (LanderFrame[6]).</summary>
    Return = 6,
    MiscData = 8,
    Canister = 9,
    /// <summary>graphics.life.0; kind N is this plus N.</summary>
    Life = 10,
}

/// <summary>What one drawing call does.</summary>
public enum SurfaceDrawKind
{
    /// <summary>The planetside map, its top left at (X, Y) (DrawStamp of TopoZoomFrame).</summary>
    Map,
    /// <summary>A frame, its hot spot at (X, Y) (DrawStamp).</summary>
    Stamp,
    /// <summary>A frame's shape filled with a colour (DrawFilledStamp, STAMPFILL_PRIM).</summary>
    Filled,
    /// <summary>A rectangle filled with a colour, (X, Y) its corner (DrawFilledRectangle).</summary>
    Rectangle,
    /// <summary>A line of the tiny font, centred on (X, Y) its baseline (font_DrawText).</summary>
    Text,
}

/// <summary>
/// One drawing call on the surface view or the lander's gauges, in UQM's
/// pixels of that view, as UQM makes it.
/// </summary>
public readonly record struct SurfaceDraw(SurfaceDrawKind Kind, PlanetsideSheet Sheet, int Frame, int X, int Y,
    Rgb Color = default, int Width = 0, int Height = 0, string Text = "");

/// <summary>A mineral deposit still on the surface: its node number, place (planetside map units) and frame of MiscDataFrame.</summary>
public readonly record struct SurfaceDeposit(int Node, int X, int Y, int Frame);

/// <summary>
/// The surface view as UQM's ScrollPlanetSide draws it, around the local
/// player's lander: the planetside map, everything on the surface in the
/// display list's order, the other landers (none in UQM), the lander with
/// its shadow while it's above the ground and its flash after a hit, and
/// the mineral text after a pickup. All from a snapshot's surface.
/// </summary>
public static class PlanetsideScreen
{
    public const int Width = PlanetsideSurface.ViewWidth, Height = PlanetsideSurface.ViewHeight;

    /// <summary>DamageColorCycle's damage_tab: white, then from deep red to pale yellow.</summary>
    public static IReadOnlyList<Rgb> DamageColors => PlanetsideSurface.DamageColors;

    /// <summary>PlanetSide's ColorCycle for the mineral text: from red up to white and back, a colour every two frames.</summary>
    public static readonly IReadOnlyList<Rgb> TextColors = MakeTextColors();

    private static readonly Rgb White = new(0xFF, 0xFF, 0xFF), Black = new(0, 0, 0);

    private static Rgb[] MakeTextColors()
    {
        var c = new Rgb[Lander.TextFrames >> 1];
        Rgb[] ends = { Rgb.From15(0x1F, 0x03, 0x00), Rgb.From15(0x1F, 0x0A, 0x00), Rgb.From15(0x1F, 0x11, 0x00), Rgb.From15(0x1F, 0x1F, 0x00) };
        for (int i = 0; i < c.Length; ++i)
            c[i] = i < 4 ? ends[i] : i >= c.Length - 4 ? ends[c.Length - 1 - i] : Rgb.From15(0x1F, 0x1F, 0x1F);
        return c;
    }

    /// <summary>
    /// The deposits left on a world's surface, in the queue's order (the
    /// last node first, as GeneratePlanetSide puts them), each drawn with
    /// the scan map's dot for its size, one bigger.
    /// </summary>
    public static IReadOnlyList<SurfaceDeposit> Deposits(IReadOnlyList<(int Node, SurfaceNode Info)> left, Func<int, int> elementCategory) =>
        left.OrderByDescending(n => n.Node).Select(n => new SurfaceDeposit(n.Node, n.Info.X << PlanetsideMap.MagShift,
            n.Info.Y << PlanetsideMap.MagShift,
            Core.Content.ContactShapes.FirstMineralFrame + elementCategory(n.Info.Type) * Core.Content.ContactShapes.MineralFramesPerCategory
            + (n.Info.Density & 0xFF) + 2)).ToList();

    /// <summary>
    /// The frame's drawing around <paramref name="me"/>'s lander on
    /// <paramref name="surface"/>, with the deposits left on it
    /// (<see cref="Deposits"/>, as they were before this frame: one taken
    /// this frame is still drawn, as UQM draws before it touches), and the
    /// elements' names for the mineral text (UQM's element strings).
    /// </summary>
    public static List<SurfaceDraw> Draw(SurfaceSnapshot surface, string me, IReadOnlyList<SurfaceDeposit> deposits,
        Func<int, string>? elementName = null)
    {
        var mine = surface.Landers.FirstOrDefault(l => l.PlayerId == me)
            ?? throw new ArgumentException($"{me} has no lander on this surface", nameof(me));
        var draws = new List<SurfaceDraw>();

        // The planetside map, three times so it wraps east and west.
        int mx = -mine.X + (Width >> 1), my = -mine.Y + (Height >> 1);
        draws.Add(new(SurfaceDrawKind.Map, 0, 0, mx, my));
        draws.Add(new(SurfaceDrawKind.Map, 0, 0, mx + PlanetsideMap.Width, my));
        draws.Add(new(SurfaceDrawKind.Map, 0, 0, mx - PlanetsideMap.Width, my));

        // The display list: each element goes in front of the last, but
        // lightning and the explosion go at the end (ADD_AT_END), in turn.
        // The deposits sit in the queue after the creatures and before the
        // hazards (bolts and explosions go at its head, hazards at its end).
        var queue = new List<(SurfaceElementSnapshot? Element, SurfaceDeposit Deposit)>();
        int hazards = surface.Elements.Count;
        for (int i = 0; i < surface.Elements.Count; ++i)
        {
            if (surface.Elements[i].Kind is SurfaceElementKind.Quake or SurfaceElementKind.Lightning or SurfaceElementKind.Lava)
            {
                hazards = i;
                break;
            }
        }
        for (int i = 0; i < hazards; ++i)
            queue.Add((surface.Elements[i], default));
        foreach (var d in deposits)
            queue.Add((null, d));
        for (int i = hazards; i < surface.Elements.Count; ++i)
            queue.Add((surface.Elements[i], default));
        static bool AtEnd(SurfaceElementSnapshot? e) => e?.Kind is SurfaceElementKind.Lightning or SurfaceElementKind.Explosion;
        for (int i = queue.Count - 1; i >= 0; --i)
        {
            if (!AtEnd(queue[i].Element))
                draws.Add(ElementDraw(queue[i], mine));
        }
        foreach (var q in queue)
        {
            if (AtEnd(q.Element))
                draws.Add(ElementDraw(q, mine));
        }

        // Other players' landers (UQM has one), then the player's own.
        foreach (var other in surface.Landers)
        {
            if (other.PlayerId != me)
            {
                var (ox, oy) = ViewPlace(other.X, other.Y, mine);
                LanderDraws(draws, other, ox, oy);
            }
        }
        LanderDraws(draws, mine, Width >> 1, Height >> 1);

        // The mineral text, drawn as its frames count down, moving with the ground.
        if (mine.Text is { } t)
        {
            var color = TextColors[t.Frames >> 1];
            string name = elementName?.Invoke(t.Element) ?? "";
            int space = name.IndexOf(' ');
            string first = space < 0 ? name : name[..space], second = space < 0 ? "" : name[(space + 1)..];
            draws.Add(new(SurfaceDrawKind.Text, 0, 0, t.X, t.Y, color, Text: t.Amount.ToString()));
            draws.Add(new(SurfaceDrawKind.Text, 0, 0, t.X, t.Y + 7, color, Text: first));
            draws.Add(new(SurfaceDrawKind.Text, 0, 0, t.X, t.Y + 14, color, Text: second));
        }
        return draws;
    }

    // BuildObjectList's stamp origin: the nearer way round east or west.
    private static (int X, int Y) ViewPlace(int x, int y, LanderSnapshot mine)
    {
        int vx = x - mine.X + (Width >> 1);
        const int far = PlanetsideMap.Width - Width * 3 / 2;
        if (vx >= far)
            vx -= PlanetsideMap.Width;
        else if (vx <= -far)
            vx += PlanetsideMap.Width;
        return (vx, y - mine.Y + (Height >> 1));
    }

    private static SurfaceDraw ElementDraw((SurfaceElementSnapshot? Element, SurfaceDeposit Deposit) q, LanderSnapshot mine)
    {
        if (q.Element is not { } e)
        {
            var (dx, dy) = ViewPlace(q.Deposit.X, q.Deposit.Y, mine);
            return new(SurfaceDrawKind.Stamp, PlanetsideSheet.MiscData, q.Deposit.Frame, dx, dy);
        }
        var (x, y) = ViewPlace(e.X, e.Y, mine);
        var sheet = e.Sheet switch
        {
            SurfaceSheet.Lander => PlanetsideSheet.Lander,
            SurfaceSheet.MiscData => PlanetsideSheet.MiscData,
            SurfaceSheet.Canister => PlanetsideSheet.Canister,
            SurfaceSheet.Quake => PlanetsideSheet.Quake,
            SurfaceSheet.Lightning => PlanetsideSheet.Lightning,
            SurfaceSheet.Lava => PlanetsideSheet.Lava,
            _ => PlanetsideSheet.Life + e.LifeKind,
        };
        // As DrawBatch drew it: filled (a flashing creature, a quake,
        // lightning) or plainly.
        Rgb? fill = e.Fill;
        return fill is { } c
            ? new(SurfaceDrawKind.Filled, sheet, e.Frame, x, y, c)
            : new(SurfaceDrawKind.Stamp, sheet, e.Frame, x, y);
    }

    // ScrollPlanetSide's lander: drawn while it has crew, while its flash
    // lasts and early in its explosion; a black shadow while it's above the
    // ground; after a hit, filled with the damage colour, or with its
    // shield's white over it if a shield held.
    private static void LanderDraws(List<SurfaceDraw> draws, LanderSnapshot l, int x, int y)
    {
        if (!l.Drawn)
            return;
        int offset = -l.Height;
        int ly = y + offset;
        if (offset != 0)
            draws.Add(new(SurfaceDrawKind.Filled, PlanetsideSheet.Lander, l.Facing, ly + x - y, ly, Black));
        switch (l.Flash)
        {
            case LanderFlash.Shield:
                if (l.DamageIndex > 0)
                    draws.Add(new(SurfaceDrawKind.Filled, PlanetsideSheet.Shield, l.Facing, x, ly, White));
                draws.Add(new(SurfaceDrawKind.Stamp, PlanetsideSheet.Lander, l.Facing, x, ly));
                break;
            case LanderFlash.Damage:
                draws.Add(new(SurfaceDrawKind.Filled, PlanetsideSheet.Lander, l.Facing, x, ly, DamageColors[l.DamageIndex]));
                break;
            default:
                draws.Add(new(SurfaceDrawKind.Stamp, PlanetsideSheet.Lander, l.Facing, x, ly));
                break;
        }
    }
}

/// <summary>
/// The lander's gauges in the radar box (RADAR_WIDTH x RADAR_HEIGHT), as
/// UQM leaves them: InitLander's lander and shields and the storage the
/// bays can't take, the warm-up's power, gun, engine and cargo pod, a cell
/// for each crew member it took (filled while they live, DeltaLanderCrew),
/// and a bar a unit of minerals and of bio data (FillLanderHold).
/// </summary>
public static class LanderGauges
{
    public const int Width = 56, Height = 53;
    private const int Facings = 16;
    private const int CrewColumns = 6;

    /// <summary>The gauges' drawing, in UQM's order, for a lander and its ship's hold (as it was when the lander left).</summary>
    public static List<SurfaceDraw> Draw(LanderSnapshot lander, HoldSnapshot hold)
    {
        var d = new List<SurfaceDraw>();
        void Stamp(int frame, int x = 0, int y = 0) => d.Add(new(SurfaceDrawKind.Stamp, PlanetsideSheet.Lander, frame, x, y));
        var up = hold.Upgrades;
        bool cargo = (up & LanderUpgrades.Cargo) != 0;
        d.Add(new(SurfaceDrawKind.Rectangle, 0, 0, 0, 0, new Rgb(0, 0, 0), Width, Height));
        Stamp(Facings << 1);   // the lander, powered down
        int shift = cargo ? 1 : 0;
        int free = hold.StorageCapacity - hold.MineralMass;
        if (free < (ShipHold.MaxScrounged << shift))
            d.Add(new(SurfaceDrawKind.Rectangle, 0, 0, 1, 0, new Rgb(0, 0, 0), 4, ShipHold.MaxScrounged - (free >> shift) + 1));
        for (int s = 0; s < 4; ++s)
        {
            // Shields: earthquake, biological, lightning, lava, in that order.
            var disaster = s switch { 0 => LanderDisaster.Earthquake, 1 => LanderDisaster.Biological, 2 => LanderDisaster.Lightning, _ => LanderDisaster.Lavaspot };
            if (((int)up & (1 << (int)disaster)) != 0)
                Stamp(37 + s);
        }
        // The warm-up: power, crew coming aboard, the gun, the engine, the cargo pod.
        Stamp((Facings << 1) + 1);
        for (int i = 0; i < lander.CrewTaken; ++i)
            Stamp(i < lander.Crew ? 55 : 56, 11 + 6 * (i % CrewColumns), 35 - 6 * (i / CrewColumns));
        Stamp((up & LanderUpgrades.Shot) != 0 ? 58 : (Facings << 1) + 2);
        if ((up & LanderUpgrades.Speed) != 0)
            Stamp(57);
        else
        {
            Stamp((Facings << 1) + 3);
            Stamp((Facings << 1) + 4);
        }
        if (cargo)
            Stamp(59);
        // The hold: a bar a unit (half a unit of minerals with the cargo pod).
        for (int c = 0, n = cargo ? lander.MineralMass >> 1 : lander.MineralMass; c < n; ++c)
            Stamp(43 + (c & 1), 0, -c);
        for (int c = 0; c < lander.BioData; ++c)
            Stamp(41 + (c & 1), 0, -c);
        return d;
    }
}
