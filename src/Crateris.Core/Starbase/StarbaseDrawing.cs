// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Net;
using Crateris.Core.World;

namespace Crateris.Core.Starbase;

/// <summary>
/// A docked player's ship and the prices as the starbase's screens draw
/// them (GLOBAL_SIS and GLOBAL), as they stood after a key: the slots
/// (sis.h's numbers), the crew, the fuel in hundredths, the landers, the
/// resource units, the minerals' mass, the names (null: the flagship's
/// UQM names, which the client reads from its content),
/// CHMMR_BOMB_STATE, the ship's lander shields (LANDER_SHIELDS), the
/// prices (a module's by its number, before
/// <see cref="ModuleCostScale"/>).
/// </summary>
public sealed record StarbaseShip(byte[] Modules, byte[] Drives, byte[] Jets, int Crew, uint Fuel, int Landers,
    uint ResourceUnits, int MineralMass, string? ShipName, string? Captain, int BombState, int LanderShields,
    int FuelCost, int CrewCost, int[] ModuleCosts, int ModuleCostScale)
{
    // Bounds a message from the host is held to (the screens loop over the crew, the minerals, the landers and the fuel).
    private const int MostCrew = 10_000, MostMass = 100_000, MostCost = byte.MaxValue, MostScale = 10_000;

    /// <summary>The most fuel a ship's drawing is sent with, in hundredths (far past what sixteen tanks hold).</summary>
    public const int MostFuel = 1_000_000;

    internal void Write(WireWriter w)
    {
        foreach (var slots in new[] { Modules, Drives, Jets })
            foreach (byte b in slots)
                w.Byte(b);
        w.UInt((ulong)Crew);
        w.UInt(Fuel);
        w.UInt((ulong)Landers);
        w.UInt(ResourceUnits);
        w.UInt((ulong)MineralMass);
        w.String(ShipName);
        w.String(Captain);
        w.UInt((ulong)BombState);
        w.UInt((ulong)LanderShields);
        w.UInt((ulong)FuelCost);
        w.UInt((ulong)CrewCost);
        foreach (int c in ModuleCosts)
            w.UInt((ulong)c);
        w.UInt((ulong)ModuleCostScale);
    }

    internal static StarbaseShip Read(ref WireReader r)
    {
        byte[] Slots(ref WireReader r, int n)
        {
            var s = new byte[n];
            for (int i = 0; i < n; ++i)
                s[i] = r.Byte() is var b && b <= ShipModules.EmptyLander ? b : throw new InvalidDataException("Bad slot.");
            return s;
        }
        static int Upto(ulong v, int most) => v <= (ulong)most ? (int)v : throw new InvalidDataException("Bad starbase ship.");
        var modules = Slots(ref r, ShipModules.ModuleSlotCount);
        var drives = Slots(ref r, ShipModules.DriveSlotCount);
        var jets = Slots(ref r, ShipModules.JetSlotCount);
        int crew = Upto(r.UInt(), MostCrew);
        ulong fuel = r.UInt();
        if (fuel > MostFuel)
            throw new InvalidDataException("Bad starbase ship.");
        int landers = Upto(r.UInt(), ShipHold.MaxLanders);
        ulong ru = r.UInt();
        if (ru > uint.MaxValue)
            throw new InvalidDataException("Bad starbase ship.");
        int mass = Upto(r.UInt(), MostMass);
        string? ship = r.String(), captain = r.String();
        int bomb = Upto(r.UInt(), byte.MaxValue), shields = Upto(r.UInt(), byte.MaxValue);
        int fuelCost = Upto(r.UInt(), MostCost), crewCost = Upto(r.UInt(), MostCost);
        var costs = new int[ShipModules.Modules];
        for (int i = 0; i < costs.Length; ++i)
            costs[i] = Upto(r.UInt(), MostCost);
        int scale = Upto(r.UInt(), MostScale);
        return new StarbaseShip(modules, drives, jets, crew, (uint)fuel, landers, (uint)ru, mass, ship, captain, bomb,
            shields, fuelCost, crewCost, costs, scale);
    }
}

/// <summary>
/// What a docked player's starbase screen draws for one key (or as it
/// opens), at UQM's points (<see cref="ScreenOp"/>), with the ship as that
/// key left it: the screen draws a key's drawing with that key's ship, not
/// a later one's (docs/plans/stage-3.md, item 11). <see cref="Ship"/> is
/// null where the drawing doesn't read the ship (the starbase's menu): the
/// screen keeps the last.
/// </summary>
public sealed record StarbaseDrawing(StarbaseShip? Ship, IReadOnlyList<ScreenOp> Ops)
{
    // A message's bounds: drawings, and operations in one.
    private const int MostDrawings = 1024, MostOps = 64;

    /// <summary>Drawings, in order, as one message (sent reliably: each is drawn over the last).</summary>
    public static byte[] Encode(IReadOnlyList<StarbaseDrawing> drawings)
    {
        var w = new WireWriter();
        w.UInt((ulong)drawings.Count);
        foreach (var d in drawings)
        {
            if (d.Ship is null)
                w.Byte(0);
            else
            {
                w.Byte(1);
                d.Ship.Write(w);
            }
            w.UInt((ulong)d.Ops.Count);
            foreach (var op in d.Ops)
            {
                w.Byte((byte)op.Kind);
                w.Int(op.A);
                w.Int(op.B);
                w.Int(op.C);
            }
        }
        return w.Written.ToArray();
    }

    /// <summary>Drawings from the host. A bad message is an <see cref="InvalidDataException"/>.</summary>
    public static List<StarbaseDrawing> Decode(ReadOnlySpan<byte> data)
    {
        var r = new WireReader(data);
        ulong count = r.UInt();
        if (count > MostDrawings)
            throw new InvalidDataException("Too many starbase drawings.");
        var drawings = new List<StarbaseDrawing>((int)count);
        for (ulong i = 0; i < count; ++i)
        {
            StarbaseShip? ship = r.Byte() switch
            {
                0 => null,
                1 => StarbaseShip.Read(ref r),
                _ => throw new InvalidDataException("Bad starbase drawing."),
            };
            ulong n = r.UInt();
            if (n > MostOps)
                throw new InvalidDataException("Too many drawing operations.");
            var ops = new List<ScreenOp>((int)n);
            for (ulong k = 0; k < n; ++k)
            {
                byte kind = r.Byte();
                long a = r.Int(), b = r.Int(), c = r.Int();
                if (a is < int.MinValue or > int.MaxValue || b is < int.MinValue or > int.MaxValue
                        || c is < int.MinValue or > int.MaxValue)
                    throw new InvalidDataException("Bad drawing operation.");
                var op = new ScreenOp((ScreenOpKind)kind, (int)a, (int)b, (int)c);
                if (!Valid(op))
                    throw new InvalidDataException($"Bad drawing operation {op}.");
                ops.Add(op);
            }
            drawings.Add(new StarbaseDrawing(ship, ops));
        }
        if (!r.AtEnd)
            throw new InvalidDataException("Starbase drawings have bytes after their end.");
        return drawings;
    }

    /// <summary>Whether an operation is one the screens can draw: its kind, and what it indexes by, in range.</summary>
    public static bool Valid(ScreenOp op) => op.Kind switch
    {
        ScreenOpKind.MenuOpened => op.A is >= StarbaseMenu.TalkCommander and <= StarbaseMenu.DepartBase,
        ScreenOpKind.MenuMoved => op.A is >= StarbaseMenu.TalkCommander and <= StarbaseMenu.DepartBase
            && op.B is >= StarbaseMenu.TalkCommander and <= StarbaseMenu.DepartBase,
        ScreenOpKind.OutfitOpened => op.A is >= 0 and <= StarbaseShip.MostFuel,
        ScreenOpKind.MenuStrings => op.A == ScreenOp.PmFuel ? op.B is >= OutfitMenu.Fuel and <= OutfitMenu.Exit
            : op.A == ScreenOp.PmCrew && op.B is >= ShipyardMenu.Crew and <= ShipyardMenu.Exit,
        ScreenOpKind.ModuleStrings => op.A is >= 0 and <= ShipModules.EmptyLander,
        ScreenOpKind.ShipPiece => op.A is >= 0 and <= ShipModules.EmptyLander && op.B >= 0
            && op.B < OutfitMenu.SlotCount(OutfitMenu.RowOf((byte)op.A)),
        ScreenOpKind.CrewPoint => op.A is 0 or 1 && op.B >= 0,
        ScreenOpKind.YardOpened or ScreenOpKind.ClearRadar or ScreenOpKind.SisDisplay or ScreenOpKind.RedistributeFuel
            or ScreenOpKind.DisplayLanders or ScreenOpKind.FlagshipStats or ScreenOpKind.Gauges
            or ScreenOpKind.FlagshipName or ScreenOpKind.CaptainsName => true,
        _ => false,
    };
}
