// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;
using Godot;

namespace Crateris.Game;

/// <summary>
/// The roster in flight (stage 4b item 10): the status area as UQM shows
/// it while the roster is open (the flagship, its crew and fuel, the fleet's
/// ships round it: DeltaSISGauges), and the roster's drawing over it
/// (<see cref="RosterDrawing"/>: the lit ship flashing, its crew in the
/// status message, white while its crew is changed). The orbit view shows
/// it where its status area is, above the menu box. Each frame it follows
/// the open roster (<see cref="OrbitMenu.Roster"/>) and the host's ship and
/// fleet as the snapshot has them: a roster opened afresh (a talk changing
/// the fleet's ships reopens it) or another fleet is drawn afresh.
/// </summary>
public sealed class RosterPanel
{
    private readonly Func<(UqmCanvas Canvas, ScreenRaster Raster, StarbaseScreens Screens)?> _newArea;
    private readonly Func<int, YardRace?> _races;
    private RosterMenu? _menu;
    private (int State, bool Modifying) _seen;
    private List<(int Slot, int Race, int Captain)> _ships = new();
    private int[] _crews = Array.Empty<int>();
    private UqmCanvas? _canvas;
    private ScreenRaster? _raster;
    private StarbaseScreens? _screens;
    private RosterDrawing? _drawing;
    private ImageTexture? _texture;
    private bool _changed;

    public RosterPanel(Func<(UqmCanvas, ScreenRaster, StarbaseScreens)?> newArea, Func<int, YardRace?> races)
    {
        _newArea = newArea;
        _races = races;
    }

    /// <summary>The status area painted (the whole screen's raster; the status area is its right 64 columns), or null while closed.</summary>
    public ImageTexture? Texture => _menu is null ? null : _texture;

    /// <summary>Where the highlight flashes (the status message while the crew is changed), in the screen's pixels.</summary>
    public URect? Flash => _menu is null ? null : _drawing?.Flash;

    /// <summary>
    /// A frame: the roster as it stands (<paramref name="menu"/>, null when
    /// closed) over the host's ship <paramref name="host"/>, at
    /// <paramref name="now"/> (UQM's ticks).
    /// </summary>
    public void Update(RosterMenu? menu, ShipSnapshot? host, Func<string, int> raceIndex, long now)
    {
        if (menu is null || menu.Left || host?.Fleet is not { } fleet || host.Modules is null || host.Hold is null)
        {
            _menu = null;
            return;
        }
        var ships = fleet.Ships.Select(s => (s.Slot, raceIndex(s.Race), (int)s.Captain)).ToList();
        var crews = fleet.Ships.Select(s => s.Crew).ToArray();
        var sis = Sis(host, fleet, raceIndex);
        if (!ReferenceEquals(menu, _menu) || !ships.SequenceEqual(_ships) || _drawing is null)
        {
            if (_newArea() is not { } area)
                return;
            (_canvas, _raster, _screens) = area;
            _screens.Ship = sis;
            // The status area as the roster opens over it: the flagship and the fleet's ships (DeltaSISGauges).
            _screens.Apply(new ScreenOp(ScreenOpKind.Gauges, ScreenOp.Undefined, ScreenOp.Undefined));
            _drawing = new RosterDrawing(_canvas, _screens, _races);
            _drawing.Open(menu);
            (_menu, _ships, _crews, _seen) = (menu, ships, crews, (menu.State, menu.ModifyingCrew));
        }
        else
        {
            _screens!.Ship = sis;
            // What the roster's keys drew, from what they changed (the session took the keys).
            if (menu.State != _seen.State)
                _drawing.Key(menu, _seen, MenuKey.Down, MenuOutcome.Moved, 0, now);
            if (menu.ModifyingCrew != _seen.Modifying)
                _drawing.Key(menu, (menu.State, _seen.Modifying), MenuKey.Select, MenuOutcome.Moved, 0, now);
            int lit = menu.Selected;
            if (menu.ModifyingCrew && lit >= 0 && lit < crews.Length && lit < _crews.Length && crews[lit] != _crews[lit])
                _drawing.Key(menu, (menu.State, true), MenuKey.Up, MenuOutcome.Moved, crews[lit] - _crews[lit], now);
            _drawing.Tick(menu, now);
            (_crews, _seen) = (crews, (menu.State, menu.ModifyingCrew));
        }
        if (_canvas!.Drawn.Count > 0)
        {
            _raster!.Paint(_canvas.Drawn);
            _canvas.Drawn.Clear();
            _changed = true;
        }
        if (_changed)
        {
            var image = Image.CreateFromData(ScreenRaster.Width, ScreenRaster.Height, false, Image.Format.Rgba8, _raster!.Rgba);
            if (_texture is null)
                _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            _changed = false;
        }
    }

    // The flagship as the status area draws it, from the host's ship in the snapshot, with the fleet's ships.
    private static SisState Sis(ShipSnapshot host, FleetSnapshot fleet, Func<string, int> raceIndex)
    {
        var modules = ShipModules.Restore(host.Modules!);
        var hold = host.Hold!;
        return new SisState(modules.ModuleSlots.ToArray(), modules.DriveSlots.ToArray(), modules.JetSlots.ToArray(), hold.Crew,
            host.Fuel, hold.Landers, hold.ResourceUnits, hold.MineralMass, host.ShipName ?? host.Name, host.CaptainName ?? "",
            0, (int)(hold.Upgrades & (LanderUpgrades.BioShield | LanderUpgrades.QuakeShield | LanderUpgrades.LightningShield
                | LanderUpgrades.LavaShield)), 0, 0, _ => 0, 50,
            fleet.Ships.Select(s => new HangarShip(s.Slot, raceIndex(s.Race), s.Crew)).ToArray());
    }
}
