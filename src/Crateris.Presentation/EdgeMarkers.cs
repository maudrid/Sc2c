// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>What an edge marker points at (stage 3c item 5; the stage 3c plan's question 12).</summary>
public enum MarkerKind
{
    /// <summary>Another player's ship.</summary>
    Player,

    /// <summary>A wreck (a player's ship destroyed in a battle that's over).</summary>
    Wreck,

    /// <summary>A leaver's container.</summary>
    Container,
}

/// <summary>
/// A target in the viewer's view: what it is, its words (its name, the
/// world or star the way to it goes by, and its distance in hyperspace's
/// coordinates where it's in another place), and where it is in the
/// view's display (the space window's pixels, as <see cref="HyperScreen"/>
/// and <see cref="IpGeometry"/> give them: far outside it for one far
/// away, and for one reached another way, a point in the way's direction).
/// <see cref="Drawn"/>: the view already draws it there.
/// </summary>
public sealed record MarkerTarget(MarkerKind Kind, string Label, double X, double Y, bool Drawn);

/// <summary>
/// A marker as drawn: its words, where (on the screen's edge, or at the
/// target on the screen), and the way it points (a unit vector; 0, 0 for a
/// label on the screen, which points at nothing).
/// </summary>
public sealed record EdgeMarker(MarkerKind Kind, string Label, double X, double Y, double DirX, double DirY)
{
    /// <summary>Whether it's an arrow on the screen's edge (the target off the screen).</summary>
    public bool AtEdge => DirX != 0 || DirY != 0;
}

/// <summary>
/// The markers to other players, wrecks and containers (stage 3c item 5;
/// the owner's answer to the stage 3c plan's question 12): in the
/// hyperspace and system views, each one not drawn on the screen gets a
/// labelled arrow at the screen's edge pointing to it, or, on the screen
/// but not drawn there (a ship in a talk or docked, one in a system whose
/// star is on the screen), its label where it is. Every player is a
/// target whatever they're doing, at their ship's place, as the status
/// panel lists them. A target in another space or view is pointed at by
/// the way to it: from hyperspace, its system's star; from a system's
/// outer view, a target in a planet's inner view by its planet, and from
/// a planet's inner view, any target outside it by the direction it lies
/// in from the planet; from a system, a target outside it by the direction
/// it lies in in hyperspace, the way out. QuasiSpace's are item 9's.
/// </summary>
public static class EdgeMarkers
{
    // A point in a direction from the screen's middle, far beyond its edge.
    private const double Far = 1 << 20;

    private static readonly double HyperScale = (double)(1 << HyperScreen.ZoomShift) / (HyperScreen.Reduction << Units.OneShift);

    /// <summary>
    /// The targets for a viewer in hyperspace at (<paramref name="logX"/>,
    /// <paramref name="logY"/>), logical units (the screen's place).
    /// </summary>
    /// <param name="cluster">A star's name in words.</param>
    public static List<MarkerTarget> InHyperspace(WorldSnapshot snapshot, string me, int logX, int logY, StarMap map,
        Func<Star, string> cluster)
    {
        var list = new List<MarkerTarget>();
        int ux = Units.LogXToUniverse(logX), uy = Units.LogYToUniverse(logY);
        foreach (var s in snapshot.Ships)
        {
            if (s.Id == me)
                continue;
            var (kind, name) = Named(s);
            if (s.SystemStar is int star)
            {
                var st = map.Stars[star];
                var (x, y) = TowardStar(st, logX, logY);
                list.Add(new(kind, $"{name}: {cluster(st)} {Distance(ux, uy, st.X, st.Y)}", x, y, false));
            }
            else
            {
                var (x, y) = Toward(s.LogX + s.ViewX - HyperFlight.CenterX, s.LogY + s.ViewY - HyperFlight.CenterY, logX, logY);
                bool drawn = DrawnInHyperspace(s) && StarContact.InReach(s.UniverseX, s.UniverseY, ux, uy);
                list.Add(new(kind, $"{name} {Distance(ux, uy, s.UniverseX, s.UniverseY)}", x, y, drawn));
            }
        }
        foreach (var c in snapshot.Containers ?? Array.Empty<ContainerSnapshot>())
        {
            string name = ContainerName(c);
            if (c.Star is int star)
            {
                var st = map.Stars[star];
                var (x, y) = TowardStar(st, logX, logY);
                list.Add(new(MarkerKind.Container, $"{name}: {cluster(st)} {Distance(ux, uy, st.X, st.Y)}", x, y, false));
            }
            else
            {
                var (x, y) = Toward(c.X, c.Y, logX, logY);
                short cx = Units.LogXToUniverse(c.X), cy = Units.LogYToUniverse(c.Y);
                list.Add(new(MarkerKind.Container, $"{name} {Distance(ux, uy, cx, cy)}", x, y, StarContact.InReach(cx, cy, ux, uy)));
            }
        }
        return list;
    }

    /// <summary>
    /// The targets for a viewer in <paramref name="system"/>, in its outer
    /// view at <paramref name="zoomRadius"/> (<paramref name="innerPlanet"/>
    /// null) or a planet's inner view.
    /// </summary>
    /// <param name="planetName">A planet's name in words (by its number in the system).</param>
    public static List<MarkerTarget> InSystem(WorldSnapshot snapshot, string me, SolarSystem system, int? innerPlanet,
        int zoomRadius, StarMap map, Func<Star, string> cluster, Func<int, string> planetName)
    {
        var list = new List<MarkerTarget>();
        int radius = innerPlanet is null ? zoomRadius : IpGeometry.MaxZoomRadius;
        int star = system.Star.Number;
        foreach (var s in snapshot.Ships)
        {
            if (s.Id == me)
                continue;
            var (kind, name) = Named(s);
            if (s.SystemStar == star)
                list.Add(Here(kind, name, s.InnerPlanet, s.IpX, s.IpY,
                    drawnInView: s.Entry is null && s.Activity is Activity.OuterSystem or Activity.InnerSystem or Activity.Battle,
                    drawnAtWorld: innerPlanet is null && s.Activity is Activity.Orbit or Activity.Planetside));
            else
                list.Add(Out(kind, name, s.SystemStar, s.UniverseX, s.UniverseY));
        }
        foreach (var c in snapshot.Containers ?? Array.Empty<ContainerSnapshot>())
        {
            string name = ContainerName(c);
            if (c.Star == star)
                list.Add(Here(MarkerKind.Container, name, c.InnerPlanet, c.X, c.Y, drawnInView: true, drawnAtWorld: false));
            else if (c.Star is int other)
                list.Add(Out(MarkerKind.Container, name, other, 0, 0));
            else
                list.Add(Out(MarkerKind.Container, name, null, Units.LogXToUniverse(c.X), Units.LogYToUniverse(c.Y)));
        }
        return list;

        // A target in this system: in this view where it is (drawn by the view if it's flying there, or a
        // container); from the outer view, in a planet's inner view by its planet; from an inner view, any
        // other place in the system by the way it lies from this planet.
        MarkerTarget Here(MarkerKind kind, string name, int? planet, int ipX, int ipY, bool drawnInView, bool drawnAtWorld)
        {
            if (planet == innerPlanet)
            {
                var at = IpGeometry.LocationToDisplay(new IpPoint((short)ipX, (short)ipY), radius);
                return new(kind, name, at.X, at.Y, drawnInView);
            }
            if (innerPlanet is null)
            {
                var p = SystemLayout.OuterPlanet(system.Planets[planet!.Value], zoomRadius);
                return new(kind, $"{name}: {planetName(planet.Value)}", p.X, p.Y, drawnAtWorld);
            }
            var from = system.Planets[innerPlanet.Value];
            var (tx, ty) = planet is int q ? (system.Planets[q].X, system.Planets[q].Y) : ((short)ipX, (short)ipY);
            var (dx, dy) = Way(tx - from.X, ty - from.Y);
            return new(kind, planet is int named ? $"{name}: {planetName(named)}" : name, dx, dy, false);
        }

        // A target outside this system: by the way it lies in hyperspace, the way out.
        MarkerTarget Out(MarkerKind kind, string name, int? at, int tux, int tuy)
        {
            if (at is int other)
                (tux, tuy) = (map.Stars[other].X, map.Stars[other].Y);
            var (dx, dy) = Way(Units.UniverseToLogX((short)tux) - (long)Units.UniverseToLogX(system.Star.X),
                Units.UniverseToLogY((short)tuy) - (long)Units.UniverseToLogY(system.Star.Y));
            string where = at is int o ? $": {cluster(map.Stars[o])}" : ": hyperspace";
            return new(kind, $"{name}{where} {Distance(system.Star.X, system.Star.Y, tux, tuy)}", dx, dy, false);
        }
    }

    /// <summary>
    /// A target's marker on a screen of <paramref name="width"/> by
    /// <paramref name="height"/> (the display's pixels), its arrows kept
    /// <paramref name="margin"/> inside the edge: none for one the view
    /// draws on the screen; a label where it is for one on the screen the
    /// view doesn't draw; else an arrow on the edge, where the line from the
    /// screen's middle to it leaves the screen, pointing that way.
    /// </summary>
    public static EdgeMarker? Place(MarkerTarget t, double width, double height, double margin)
    {
        bool onScreen = t.X >= 0 && t.X < width && t.Y >= 0 && t.Y < height;
        if (onScreen)
            return t.Drawn ? null : new EdgeMarker(t.Kind, t.Label, t.X, t.Y, 0, 0);
        double cx = width / 2, cy = height / 2, dx = t.X - cx, dy = t.Y - cy;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0)
            return null;
        double ux = dx / len, uy = dy / len;
        double hx = cx - margin, hy = cy - margin;
        double s = Math.Min(ux == 0 ? double.MaxValue : hx / Math.Abs(ux), uy == 0 ? double.MaxValue : hy / Math.Abs(uy));
        return new EdgeMarker(t.Kind, t.Label, cx + ux * s, cy + uy * s, ux, uy);
    }

    private static (MarkerKind, string) Named(ShipSnapshot s) =>
        s.Wrecked ? (MarkerKind.Wreck, $"{s.Name} (wreck)") : (MarkerKind.Player, s.Name);

    /// <summary>A container's words.</summary>
    public static string ContainerName(ContainerSnapshot c) => $"{c.OwnerName}'s container";

    // Ships the hyperspace view draws: flying there, or in a battle there.
    private static bool DrawnInHyperspace(ShipSnapshot s) =>
        s.Activity == Activity.Hyperspace || s.Activity == Activity.Battle && s.SystemStar is null;

    // A sprite's place on the hyperspace screen, as HyperScreen.Sprite gives it, but in doubles (far ones don't overflow).
    private static (double X, double Y) Toward(long x, long y, int viewerLogX, int viewerLogY) =>
        ((x - viewerLogX + HyperFlight.CenterX - HyperScreen.OriginX) * HyperScale,
         (y - viewerLogY + HyperFlight.CenterY - HyperScreen.OriginY) * HyperScale);

    // A star's place on the hyperspace screen, as HyperScreen.Star gives it (the view's middle its), but in doubles and unwrapped.
    private static (double X, double Y) TowardStar(Star star, int viewerLogX, int viewerLogY) =>
        ((Units.UniverseToLogX(star.X) + (Units.LogSpaceWidth >> 1) - (long)viewerLogX - HyperScreen.OriginX) * HyperScale,
         (Units.UniverseToLogY(star.Y) + (Units.LogSpaceHeight >> 1) - (long)viewerLogY - HyperScreen.OriginY) * HyperScale);

    // A point far from the screen's middle in a direction (the system views' display: y down, as the locations').
    private static (double X, double Y) Way(double dx, double dy)
    {
        double len = Math.Sqrt(dx * dx + dy * dy);
        double cx = IpGeometry.ScreenWidth / 2.0, cy = IpGeometry.ScreenHeight / 2.0;
        return len == 0 ? (cx + Far, cy) : (cx + dx / len * Far, cy + dy / len * Far);
    }

    // A distance in hyperspace's coordinates (universe units / 10, as the status panel's places), between two universe places.
    private static string Distance(int ax, int ay, int bx, int by)
    {
        double dx = bx - ax, dy = by - ay;
        return (Math.Sqrt(dx * dx + dy * dy) / 10).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
