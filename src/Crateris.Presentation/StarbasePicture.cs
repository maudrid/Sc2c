// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoStarBase and
// rotateStarbase, shipyard.c's DoShipyard and animatePowerLines), Copyright
// Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Starbase;

namespace Crateris.Presentation;

/// <summary>
/// A docked player's starbase screen on their client (docs/plans/stage-3.md,
/// item 11): the host's drawings (<see cref="StarbaseDrawing"/>, each with
/// the ship as its key left it) drawn in order over what was drawn before,
/// and the clocks that run on the client: the starbase turning on the menu
/// (<see cref="StarbaseTurning"/>) and the shipyard's power lines
/// (<see cref="PowerLines"/>), each started where UQM's are, as its screen
/// is drawn afresh. Which screen the clocks run for follows the drawing
/// (the screen last drawn afresh), not the screen the host last sent, so a
/// clock never draws over another screen's drawing. What is drawn comes
/// back as UQM's drawing calls, to paint (<see cref="ScreenRaster"/>,
/// with <see cref="ColorMap"/>).
/// </summary>
public sealed class StarbasePicture
{
    private enum On { Nothing, Menu, Outfit, Yard }

    private readonly UqmCanvas _canvas;
    private readonly StarbaseTurning _turning;
    private readonly PowerLines _lines;
    private readonly Func<StarbaseShip, SisState> _sis;
    private On _on;

    /// <param name="canvas">The canvas the screens draw on (its calls are taken as they're drawn).</param>
    /// <param name="gameString">The game's strings (GAME_STRING).</param>
    /// <param name="starbaseFrames">How many frames graphics.starbase has.</param>
    /// <param name="hangarMaps">How many maps colortable.hangar has.</param>
    /// <param name="sis">The ship as the screens read it, from the host's.</param>
    public StarbasePicture(UqmCanvas canvas, Func<int, string> gameString, int starbaseFrames, int hangarMaps,
        Func<StarbaseShip, SisState> sis)
    {
        _canvas = canvas;
        Screens = new StarbaseScreens(canvas, gameString);
        _turning = new StarbaseTurning(starbaseFrames);
        _lines = new PowerLines(hangarMaps);
        _sis = sis;
    }

    public StarbaseScreens Screens { get; }

    /// <summary>The colour map the shipyard's frames are painted in (colortable.hangar's; -1 none yet).</summary>
    public int ColorMap => Screens.ColorMap;

    /// <summary>Whether a screen has been drawn afresh (until then there's nothing to show).</summary>
    public bool Drawn => _on != On.Nothing;

    /// <summary>The host's drawings, in order, drawn at <paramref name="now"/> (UQM's ticks); the calls drawn.</summary>
    public List<UqmDraw> Draw(IEnumerable<StarbaseDrawing> drawings, long now)
    {
        foreach (var d in drawings)
        {
            if (d.Ship is { } ship)
                Screens.Ship = _sis(ship);
            foreach (var op in d.Ops)
            {
                switch (op.Kind)
                {
                    case ScreenOpKind.MenuOpened:
                        _on = On.Menu;
                        _turning.Open(now);   // the menu's drawing stamps frame 0
                        break;
                    case ScreenOpKind.OutfitOpened:
                        _on = On.Outfit;
                        break;
                    case ScreenOpKind.YardOpened:
                        _on = On.Yard;
                        _lines.Open(now);   // the shipyard's drawing shows the first map
                        break;
                }
                Screens.Apply(op);
            }
        }
        return Take();
    }

    /// <summary>
    /// The client's clock at <paramref name="now"/>: on the menu, the
    /// starbase's next frame when it's due; in the shipyard, the power
    /// lines' next map. The calls drawn (none if nothing was due).
    /// </summary>
    public List<UqmDraw> Tick(long now)
    {
        if (_on == On.Menu && _turning.Pass(now))
            Screens.Turn(_turning.Frame);
        else if (_on == On.Yard && _lines.Frame(now))
            Screens.PowerLines(_lines.Map);
        return Take();
    }

    private List<UqmDraw> Take()
    {
        var drawn = new List<UqmDraw>(_canvas.Drawn);
        _canvas.Drawn.Clear();
        return drawn;
    }
}
